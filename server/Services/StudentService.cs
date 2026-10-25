using System.ComponentModel.DataAnnotations;
using CoachingApp.Data;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Services;

public record StudentRow(Student S, decimal Overdue);

public class StudentService(IDbContextFactory<AppDb> f, PushService push)
{
    static void Validate(StudentInput i)
    {
        var res = new List<ValidationResult>();
        if (!Validator.TryValidateObject(i, new ValidationContext(i), res, true))
            throw new InvalidOperationException(string.Join("; ", res.Select(r => r.ErrorMessage)));
        if (!string.IsNullOrWhiteSpace(i.Password) && i.Password.Length < 4)
            throw new InvalidOperationException("Password kam se kam 4 character ka ho");
    }

    // admitNow = true -> admin ne admission liya (Active + fee schedule). false -> student ne khud register kiya (Pending)
    public async Task<Student> CreateAsync(StudentInput inp, bool admitNow)
    {
        Validate(inp);
        await using var db = await f.CreateDbContextAsync();
        var batch = await db.Batches.FindAsync(inp.BatchId) ?? throw new InvalidOperationException("Batch nahi mila");
        if (!batch.IsActive) throw new InvalidOperationException("Ye batch band hai");
        var filled = await db.Students.CountAsync(s => s.BatchId == batch.Id && (s.Status == StudentStatus.Active || s.Status == StudentStatus.Pending));
        if (filled >= batch.Capacity) throw new InvalidOperationException("Batch full ho chuka hai");
        var set = await db.Settings.FirstAsync();
        var s = new Student { RollNo = set.NextRollNo, CreatedAt = DateTime.Now };
        set.NextRollNo++;
        inp.ApplyTo(s);
        if (!admitNow) { s.AdmissionDate = DateTime.Today; s.MonthlyDiscount = 0; s.AdmissionDiscount = 0; }
        s.PasswordHash = Security.Hash(string.IsNullOrWhiteSpace(inp.Password) ? inp.Mobile : inp.Password);
        s.Status = admitNow ? StudentStatus.Active : StudentStatus.Pending;
        if (admitNow) FeeService.Generate(s, batch);
        db.Students.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task UpdateAsync(int id, StudentInput inp)
    {
        Validate(inp);
        await using var db = await f.CreateDbContextAsync();
        var s = await db.Students.Include(x => x.Installments).FirstAsync(x => x.Id == id);
        var batch = await db.Batches.FindAsync(inp.BatchId) ?? throw new InvalidOperationException("Batch nahi mila");
        inp.ApplyTo(s);
        if (!string.IsNullOrWhiteSpace(inp.Password)) s.PasswordHash = Security.Hash(inp.Password);
        if (s.Status == StudentStatus.Active)
        {
            // jo installments pe abhi payment nahi hui, unko nayi batch fee / discount se update karo
            foreach (var i in s.Installments.Where(i => i.PaidAmount == 0 && i.Waived == 0).ToList())
            {
                if (i.Type == FeeType.Admission) { i.Amount = batch.AdmissionFee; i.Discount = Math.Min(s.AdmissionDiscount, batch.AdmissionFee); }
                else if (i.MonthNo > batch.DurationMonths) db.FeeInstallments.Remove(i);
                else { i.Amount = batch.MonthlyFee; i.Discount = Math.Min(s.MonthlyDiscount, batch.MonthlyFee); }
            }
            var have = s.Installments.Where(i => i.Type == FeeType.Monthly).Select(i => i.MonthNo).ToHashSet();
            for (int m = 1; m <= batch.DurationMonths; m++)
                if (!have.Contains(m))
                {
                    var due = s.AdmissionDate.AddMonths(m - 1);
                    s.Installments.Add(new FeeInstallment { Type = FeeType.Monthly, MonthNo = m, Title = $"Month {m} ({due:MMM yyyy})",
                        DueDate = due, Amount = batch.MonthlyFee, Discount = Math.Min(s.MonthlyDiscount, batch.MonthlyFee) });
                }
        }
        await db.SaveChangesAsync();
    }

    public async Task ApproveAsync(int id)
    {
        await using var db = await f.CreateDbContextAsync();
        var s = await db.Students.Include(x => x.Batch).Include(x => x.Installments).FirstAsync(x => x.Id == id);
        if (s.Status != StudentStatus.Pending) return;
        s.Status = StudentStatus.Active;
        s.AdmissionDate = DateTime.Today;
        FeeService.Generate(s, s.Batch);
        await db.SaveChangesAsync();
        _ = push.ToStudentsAsync(new[] { (id, "Aapka admission approve ho gaya hai. Fees schedule aur ID card app me dekhein.") }, "Admission approved",
            new Dictionary<string, string> { { "type", "admission" } });
    }

    public async Task SetStatusAsync(int id, StudentStatus status)
    {
        await using var db = await f.CreateDbContextAsync();
        var s = await db.Students.FindAsync(id) ?? throw new InvalidOperationException("Student nahi mila");
        s.Status = status; await db.SaveChangesAsync();
    }

    public async Task SetPhotoAsync(int id, string path)
    {
        await using var db = await f.CreateDbContextAsync();
        var s = await db.Students.FindAsync(id) ?? throw new InvalidOperationException("Student nahi mila");
        s.PhotoPath = path; await db.SaveChangesAsync();
    }

    public async Task<Student?> GetAsync(int id)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.Students.Include(s => s.Batch).ThenInclude(b => b.Category).FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<(List<StudentRow> Rows, int Total)> SearchAsync(string? q, int? categoryId, int? batchId, StudentStatus? status, int page = 1, int size = 25)
    {
        await using var db = await f.CreateDbContextAsync();
        var query = db.Students.Include(s => s.Batch).ThenInclude(b => b.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(s => s.Name.Contains(q) || s.FatherName.Contains(q) || s.Mobile.Contains(q) || s.RollNo.ToString() == q);
        }
        if (batchId > 0) query = query.Where(s => s.BatchId == batchId);
        else if (categoryId > 0) query = query.Where(s => s.Batch.CategoryId == categoryId);
        if (status != null) query = query.Where(s => s.Status == status);
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(s => s.Id).Skip((page - 1) * size).Take(size).ToListAsync();
        var ids = list.Select(s => s.Id).ToList(); var today = DateTime.Today;
        var od = await db.FeeInstallments.Where(i => ids.Contains(i.StudentId) && i.DueDate <= today && i.Amount - i.Discount - i.Waived - i.PaidAmount > 0)
            .GroupBy(i => i.StudentId).Select(g => new { g.Key, Due = g.Sum(x => x.Amount - x.Discount - x.Waived - x.PaidAmount) }).ToListAsync();
        var map = od.ToDictionary(x => x.Key, x => x.Due);
        return (list.Select(s => new StudentRow(s, map.GetValueOrDefault(s.Id))).ToList(), total);
    }
}
