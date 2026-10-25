using CoachingApp.Data;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Services;

public record FeeSummary(decimal TotalNet, decimal Paid, decimal Balance, decimal OverdueNow, int OverdueCount, decimal Waived);
public record DueRow(int StudentId, int RollNo, string Name, string Mobile, string Batch, string Category, decimal Due, int Months, DateTime Oldest);
public record DashboardStats(int Active, int Pending, decimal TodayCollection, decimal MonthCollection, decimal TotalOverdue, int Defaulters, int Batches);

public class FeeService(IDbContextFactory<AppDb> f, PushService push, ChatNotifier notifier)
{
    // Admission ke time poora fee schedule banta hai: 1 admission fee + har month ki installment
    public static void Generate(Student s, Batch b)
    {
        if (s.Installments.Count > 0) return;
        if (b.AdmissionFee > 0)
            s.Installments.Add(new FeeInstallment
            {
                Type = FeeType.Admission, MonthNo = 0, Title = "Admission Fee", DueDate = s.AdmissionDate,
                Amount = b.AdmissionFee, Discount = Math.Min(s.AdmissionDiscount, b.AdmissionFee)
            });
        for (int i = 0; i < b.DurationMonths; i++)
        {
            var due = s.AdmissionDate.AddMonths(i);
            s.Installments.Add(new FeeInstallment
            {
                Type = FeeType.Monthly, MonthNo = i + 1, Title = $"Month {i + 1} ({due:MMM yyyy})", DueDate = due,
                Amount = b.MonthlyFee, Discount = Math.Min(s.MonthlyDiscount, b.MonthlyFee)
            });
        }
    }

    // Student ko chat me message + FCM push (app band ho tab bhi system notification aata hai)
    async Task NoticeAsync(int studentId, string title, string text)
    {
        try
        {
            await using var db = await f.CreateDbContextAsync();
            var name = await db.Students.Where(s => s.Id == studentId).Select(s => s.Name).FirstOrDefaultAsync();
            var m = new ChatMessage { StudentId = studentId, FromAdmin = true, Text = text, SentAt = DateTime.Now };
            db.ChatMessages.Add(m);
            await db.SaveChangesAsync();
            notifier.Publish(new ChatEvent(studentId, m, name));
            _ = push.ToStudentsAsync(new[] { (studentId, text) }, title, new Dictionary<string, string> { { "type", "chat" } });
        }
        catch { /* notification fail hone se payment fail nahi honi chahiye */ }
    }

    // Payment: sabse purani due se adjust hota hai
    public async Task<Payment> ReceiveAsync(int studentId, decimal amount, string? mode, string? reference, string? remarks, string receivedBy)
    {
        if (amount <= 0) throw new InvalidOperationException("Amount 0 se zyada hona chahiye");
        await using var db = await f.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var inst = await db.FeeInstallments
            .Where(i => i.StudentId == studentId && i.Amount - i.Discount - i.Waived - i.PaidAmount > 0)
            .OrderBy(i => i.DueDate).ThenBy(i => i.MonthNo).ToListAsync();
        var balance = inst.Sum(i => i.Balance);
        if (amount > balance) throw new InvalidOperationException($"Amount total balance (₹{balance:N0}) se zyada hai");
        var p = new Payment
        {
            StudentId = studentId, Amount = amount, Mode = string.IsNullOrWhiteSpace(mode) ? "Cash" : mode,
            Reference = reference, Remarks = remarks, ReceivedBy = receivedBy, PaidOn = DateTime.Now,
            ReceiptNo = "TMP" + Guid.NewGuid().ToString("N")[..12]
        };
        var left = amount;
        foreach (var i in inst)
        {
            if (left <= 0) break;
            var take = Math.Min(left, i.Balance);
            i.PaidAmount += take; left -= take;
            p.Allocations.Add(new PaymentAllocation { Installment = i, Amount = take });
        }
        db.Payments.Add(p);
        await db.SaveChangesAsync();
        p.ReceiptNo = $"RC{p.Id:D6}";
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var name = await db.Students.Where(s => s.Id == studentId).Select(s => s.Name).FirstAsync();
        var remaining = balance - amount;
        var text = $"🙏 Dhanyawad {name}! Aapki ₹{amount:N0} fees prapt hui (Receipt {p.ReceiptNo}). " +
                   (remaining > 0 ? $"Baaki balance: ₹{remaining:N0}." : "Aapki poori fees clear ho gayi hai 🎉");
        await NoticeAsync(studentId, "Fees received ✔", text);
        return p;
    }

    public async Task CancelPaymentAsync(int paymentId)
    {
        await using var db = await f.CreateDbContextAsync();
        var p = await db.Payments.Include(x => x.Allocations).ThenInclude(a => a.Installment)
            .FirstOrDefaultAsync(x => x.Id == paymentId) ?? throw new InvalidOperationException("Payment nahi mila");
        if (p.IsCancelled) return;
        foreach (var a in p.Allocations) a.Installment.PaidAmount -= a.Amount;
        p.IsCancelled = true;
        await db.SaveChangesAsync();
    }

    // ================= SETTLEMENT / ROUND-OFF =================
    public async Task<decimal> BalanceAsync(int studentId, SettleScope scope)
    {
        await using var db = await f.CreateDbContextAsync();
        var today = DateTime.Today;
        var q = db.FeeInstallments.Where(i => i.StudentId == studentId && i.Amount - i.Discount - i.Waived - i.PaidAmount > 0);
        if (scope == SettleScope.Overdue) q = q.Where(i => i.DueDate <= today);
        return await q.SumAsync(i => (decimal?)(i.Amount - i.Discount - i.Waived - i.PaidAmount)) ?? 0;
    }

    // payNow = student abhi kitna dega; (scope ka baaki - payNow) maaf. closeAccount = student ko "Completed" mark karo
    public async Task<Settlement> SettleAsync(int studentId, SettleScope scope, decimal payNow, string? mode, string? reference,
        string? remarks, bool closeAccount, string by)
    {
        if (payNow < 0) throw new InvalidOperationException("Amount negative nahi ho sakta");
        await using var db = await f.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await db.Students.FindAsync(studentId) ?? throw new InvalidOperationException("Student nahi mila");
        var today = DateTime.Today;
        var q = db.FeeInstallments.Where(i => i.StudentId == studentId && i.Amount - i.Discount - i.Waived - i.PaidAmount > 0);
        if (scope == SettleScope.Overdue) q = q.Where(i => i.DueDate <= today);
        var inst = await q.OrderBy(i => i.DueDate).ThenBy(i => i.MonthNo).ToListAsync();
        var before = inst.Sum(i => i.Balance);
        if (before <= 0) throw new InvalidOperationException("Is scope me koi baaki amount nahi hai");
        if (payNow > before) throw new InvalidOperationException($"Amount baaki (₹{before:N0}) se zyada nahi ho sakta");

        var set = new Settlement
        {
            StudentId = studentId, Scope = scope, BalanceBefore = before, PaidAmount = payNow, WaivedAmount = before - payNow,
            Remarks = remarks, SettledBy = by, SettledOn = DateTime.Now
        };

        if (payNow > 0)   // pehle jo amount mili uski normal receipt
        {
            var pay = new Payment
            {
                StudentId = studentId, Amount = payNow, Mode = string.IsNullOrWhiteSpace(mode) ? "Cash" : mode, Reference = reference,
                Remarks = "Settlement payment", ReceivedBy = by, PaidOn = DateTime.Now, ReceiptNo = "TMP" + Guid.NewGuid().ToString("N")[..12]
            };
            var left = payNow;
            foreach (var i in inst)
            {
                if (left <= 0) break;
                var take = Math.Min(left, i.Balance);
                i.PaidAmount += take; left -= take;
                pay.Allocations.Add(new PaymentAllocation { Installment = i, Amount = take });
            }
            db.Payments.Add(pay);
            await db.SaveChangesAsync();
            pay.ReceiptNo = $"RC{pay.Id:D6}";
            set.PaymentId = pay.Id;
        }

        foreach (var i in inst)   // jo baaki bacha wo maaf (waive)
        {
            var b = i.Balance;
            if (b > 0) { i.Waived += b; set.Allocations.Add(new SettlementAllocation { Installment = i, Amount = b }); }
        }
        db.Settlements.Add(set);
        await db.SaveChangesAsync();

        if (closeAccount)
        {
            var remaining = await db.FeeInstallments.Where(i => i.StudentId == studentId)
                .SumAsync(i => (decimal?)(i.Amount - i.Discount - i.Waived - i.PaidAmount)) ?? 0;
            if (remaining > 0) throw new InvalidOperationException("Account close karne ke liye 'Poora baaki (future months bhi)' chunein");
            s.Status = StudentStatus.Completed; set.ClosedAccount = true;
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();

        var text = $"🤝 {s.Name}, aapka hisab settle ho gaya. Baaki ₹{before:N0} me se aapne ₹{payNow:N0} diye aur ₹{set.WaivedAmount:N0} maaf kiye gaye." +
                   (closeAccount ? " Aapka account close ho gaya hai." : "") + " Dhanyawad! 🙏";
        await NoticeAsync(studentId, "Account settled ✔", text);
        return set;
    }

    public async Task CancelSettlementAsync(int id)
    {
        await using var db = await f.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await db.Settlements.Include(x => x.Allocations).ThenInclude(a => a.Installment)
            .FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Settlement nahi mila");
        if (s.IsCancelled) return;
        foreach (var a in s.Allocations) a.Installment.Waived -= a.Amount;
        if (s.PaymentId != null)
        {
            var p = await db.Payments.Include(x => x.Allocations).ThenInclude(a => a.Installment).FirstOrDefaultAsync(x => x.Id == s.PaymentId);
            if (p != null && !p.IsCancelled) { foreach (var a in p.Allocations) a.Installment.PaidAmount -= a.Amount; p.IsCancelled = true; }
        }
        s.IsCancelled = true; s.CancelledOn = DateTime.Now;
        if (s.ClosedAccount)
        {
            var st = await db.Students.FindAsync(s.StudentId);
            if (st != null && st.Status == StudentStatus.Completed) st.Status = StudentStatus.Active;
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<List<Settlement>> SettlementsAsync(int? studentId, int take = 200)
    {
        await using var db = await f.CreateDbContextAsync();
        var q = db.Settlements.Include(x => x.Student).AsQueryable();
        if (studentId != null) q = q.Where(x => x.StudentId == studentId);
        return await q.OrderByDescending(x => x.SettledOn).Take(take).ToListAsync();
    }

    public async Task<FeeSummary> SummaryAsync(int studentId)
    {
        await using var db = await f.CreateDbContextAsync();
        var list = await db.FeeInstallments.Where(i => i.StudentId == studentId)
            .Select(i => new { i.DueDate, Net = i.Amount - i.Discount - i.Waived, i.PaidAmount, i.Waived }).ToListAsync();
        var today = DateTime.Today;
        return new FeeSummary(list.Sum(x => x.Net), list.Sum(x => x.PaidAmount), list.Sum(x => x.Net - x.PaidAmount),
            list.Where(x => x.DueDate <= today).Sum(x => x.Net - x.PaidAmount),
            list.Count(x => x.DueDate <= today && x.Net - x.PaidAmount > 0), list.Sum(x => x.Waived));
    }

    public async Task<List<DueRow>> DuesAsync(int? categoryId, int? batchId, DateTime asOf)
    {
        await using var db = await f.CreateDbContextAsync();
        var q = db.FeeInstallments.Where(i => i.DueDate <= asOf && i.Amount - i.Discount - i.Waived - i.PaidAmount > 0
                                              && i.Student.Status == StudentStatus.Active);
        if (batchId > 0) q = q.Where(i => i.Student.BatchId == batchId);
        else if (categoryId > 0) q = q.Where(i => i.Student.Batch.CategoryId == categoryId);
        var g = await q.GroupBy(i => i.StudentId).Select(x => new
        {
            StudentId = x.Key, Due = x.Sum(i => i.Amount - i.Discount - i.Waived - i.PaidAmount), Months = x.Count(), Oldest = x.Min(i => i.DueDate)
        }).ToListAsync();
        var ids = g.Select(x => x.StudentId).ToList();
        var studs = await db.Students.Include(s => s.Batch).ThenInclude(b => b.Category)
            .Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        return g.Select(x => { var s = studs[x.StudentId];
                return new DueRow(s.Id, s.RollNo, s.Name, s.Mobile, s.Batch.Name, s.Batch.Category.Name, x.Due, x.Months, x.Oldest); })
            .OrderByDescending(r => r.Due).ToList();
    }

    public async Task<DashboardStats> DashboardAsync()
    {
        await using var db = await f.CreateDbContextAsync();
        var today = DateTime.Today; var monthStart = new DateTime(today.Year, today.Month, 1);
        var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active);
        var pending = await db.Students.CountAsync(s => s.Status == StudentStatus.Pending);
        var todayC = await db.Payments.Where(p => !p.IsCancelled && p.PaidOn >= today).SumAsync(p => (decimal?)p.Amount) ?? 0;
        var monthC = await db.Payments.Where(p => !p.IsCancelled && p.PaidOn >= monthStart).SumAsync(p => (decimal?)p.Amount) ?? 0;
        var od = db.FeeInstallments.Where(i => i.DueDate <= today && i.Amount - i.Discount - i.Waived - i.PaidAmount > 0 && i.Student.Status == StudentStatus.Active);
        var odSum = await od.SumAsync(i => (decimal?)(i.Amount - i.Discount - i.Waived - i.PaidAmount)) ?? 0;
        var def = await od.Select(i => i.StudentId).Distinct().CountAsync();
        var batches = await db.Batches.CountAsync(b => b.IsActive);
        return new DashboardStats(active, pending, todayC, monthC, odSum, def, batches);
    }
}
