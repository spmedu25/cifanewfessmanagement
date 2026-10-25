using System.Security.Claims;
using CoachingApp.Data;
using CoachingApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Api;

public record AdminLogin(string Username, string Password);
public record StudentLogin(string Login, string Password);
public record PayRequest(decimal Amount, string? Mode, string? Reference, string? Remarks);
public record CategoryDto(int Id, string Name, string? Description, bool IsActive);
public record BatchDto(int Id, int CategoryId, string Name, string Course, int DurationMonths, decimal AdmissionFee, decimal MonthlyFee, int Capacity, DateTime StartDate, bool IsActive);
public record ChangePassword(string OldPassword, string NewPassword);
public record StatusDto(string Status);
public record SettleReq(string? Scope, decimal PayNow, string? Mode, string? Reference, string? Remarks, bool CloseAccount);

public static class Endpoints
{
    // ---------- Web admin cookie login/logout ----------
    public static void MapAccount(this WebApplication app)
    {
        app.MapPost("/account/login", async (HttpContext ctx, IDbContextFactory<AppDb> f) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var u = form["username"].ToString().Trim(); var p = form["password"].ToString();
            await using var db = await f.CreateDbContextAsync();
            var admin = await db.Admins.FirstOrDefaultAsync(a => a.Username == u);
            if (admin == null || !Security.Verify(p, admin.PasswordHash)) return Results.Redirect("/login?error=1");
            var id = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Name, admin.FullName),
                new Claim(ClaimTypes.Role, "Admin") }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
            return Results.Redirect("/");
        }).DisableAntiforgery();
        app.MapGet("/account/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }

    // ---------- Printable pages: ID card + receipt ----------
    static bool CanSee(ClaimsPrincipal u, int studentId) => u.IsInRole("Admin") || u.FindFirstValue("sid") == studentId.ToString();

    public static void MapPrint(this WebApplication app)
    {
        app.MapGet("/idcard/student/{id:int}", async (int id, HttpContext c, IdCardService svc, string? side, bool? embed) =>
        {
            if (!CanSee(c.User, id)) return Results.Forbid();
            return Results.Content(await svc.RenderAsync(new[] { id }, false, side ?? "both", embed == true), "text/html; charset=utf-8");
        }).RequireAuthorization();
        app.MapGet("/idcard/batch/{batchId:int}", async (int batchId, IdCardService svc, string? side) =>
            Results.Content(await svc.RenderBatchAsync(batchId, side ?? "front"), "text/html; charset=utf-8"))
            .RequireAuthorization(p => p.RequireRole("Admin"));
        app.MapGet("/receipt/{id:int}", async (int id, HttpContext c, IdCardService svc, IDbContextFactory<AppDb> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var sid = await db.Payments.Where(p => p.Id == id).Select(p => (int?)p.StudentId).FirstOrDefaultAsync();
            if (sid == null) return Results.NotFound();
            if (!CanSee(c.User, sid.Value)) return Results.Forbid();
            return Results.Content((await svc.ReceiptAsync(id))!, "text/html; charset=utf-8");
        }).RequireAuthorization();
    }

    // ---------- JSON API for Flutter ----------
    static object StudentJson(Student s) => new
    {
        s.Id, s.RollNo, s.Name, s.FatherName, s.MotherName, s.Address, s.Mobile, s.AltMobile, s.Email, s.DateOfBirth, s.Gender,
        s.AadhaarNo, s.PhotoPath, s.AdmissionDate, s.BatchId, Batch = s.Batch?.Name, Course = s.Batch?.Course,
        Category = s.Batch?.Category?.Name, Duration = s.Batch?.DurationMonths, s.MonthlyDiscount, s.AdmissionDiscount,
        Status = s.Status.ToString(), s.Remarks
    };
    static IEnumerable<object> FeeJson(IEnumerable<FeeInstallment> l) => l.OrderBy(i => i.DueDate).ThenBy(i => i.MonthNo).Select(i => (object)new
    {
        i.Id, i.Title, Type = i.Type.ToString(), i.DueDate, i.Amount, i.Discount, i.Waived, Net = i.Net, i.PaidAmount, Balance = i.Balance,
        IsOverdue = i.Balance > 0 && i.DueDate.Date <= DateTime.Today
    });
    static IEnumerable<object> PayJson(IEnumerable<Payment> l) => l.OrderByDescending(p => p.PaidOn).Select(p => (object)new
    { p.Id, p.ReceiptNo, p.Amount, p.PaidOn, p.Mode, p.Reference, p.Remarks, p.ReceivedBy, p.IsCancelled });
    static IResult Err(string m) => Results.BadRequest(new { error = m });
    static int Sid(ClaimsPrincipal u) => int.Parse(u.FindFirstValue("sid")!);

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ===== Public =====
        api.MapPost("/auth/admin-login", async (AdminLogin r, IDbContextFactory<AppDb> f, TokenService t) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var a = await db.Admins.FirstOrDefaultAsync(x => x.Username == r.Username.Trim());
            if (a == null || !Security.Verify(r.Password, a.PasswordHash)) return Results.Json(new { error = "Username ya password galat hai" }, statusCode: 401);
            return Results.Ok(new { token = t.ForAdmin(a), role = "Admin", name = a.FullName });
        });
        api.MapPost("/auth/student-login", async (StudentLogin r, IDbContextFactory<AppDb> f, TokenService t) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var login = r.Login.Trim();
            var list = await db.Students.Where(x => x.Mobile == login || x.RollNo.ToString() == login).ToListAsync();
            var s = list.FirstOrDefault(x => Security.Verify(r.Password, x.PasswordHash));
            if (s == null) return Results.Json(new { error = "Roll No / Mobile ya password galat hai" }, statusCode: 401);
            return Results.Ok(new { token = t.ForStudent(s), role = "Student", name = s.Name });
        });
        api.MapGet("/public/catalog", async (IDbContextFactory<AppDb> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var cats = await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new
            {
                c.Id, c.Name,
                Batches = c.Batches.Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new
                { b.Id, b.Name, b.Course, b.DurationMonths, b.AdmissionFee, b.MonthlyFee }).ToList()
            }).ToListAsync();
            return Results.Ok(cats);
        });
        api.MapPost("/public/register", async (StudentInput r, StudentService ss, TokenService t) =>
        {
            try
            {
                var s = await ss.CreateAsync(r, false);
                return Results.Ok(new { token = t.ForStudent(s), role = "Student", name = s.Name, rollNo = s.RollNo,
                    message = "Registration ho gaya. Admin approve karega to fees schedule ban jayega." });
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });

        // ===== Student =====
        var st = api.MapGroup("/student").RequireAuthorization(p => p.RequireRole("Student"));
        st.MapGet("/me", async (ClaimsPrincipal u, StudentService ss, FeeService fs) =>
        {
            var s = await ss.GetAsync(Sid(u));
            if (s == null) return Results.NotFound();
            return Results.Ok(new { student = StudentJson(s), summary = await fs.SummaryAsync(s.Id) });
        });
        st.MapGet("/fees", async (ClaimsPrincipal u, IDbContextFactory<AppDb> f) =>
        {
            var sid = Sid(u);
            await using var db = await f.CreateDbContextAsync();
            return Results.Ok(FeeJson(await db.FeeInstallments.Where(i => i.StudentId == sid).ToListAsync()));
        });
        st.MapGet("/payments", async (ClaimsPrincipal u, IDbContextFactory<AppDb> f) =>
        {
            var sid = Sid(u);
            await using var db = await f.CreateDbContextAsync();
            return Results.Ok(PayJson(await db.Payments.Where(p => p.StudentId == sid).ToListAsync()));
        });
        st.MapPost("/photo", async (ClaimsPrincipal u, IFormFile file, FileStore fsx, StudentService ss) =>
        {
            if (file.Length > 4_000_000) return Err("Photo 4MB se choti rakhein");
            try
            {
                await using var stream = file.OpenReadStream();
                var path = await fsx.SaveAsync(stream, file.FileName, "photos");
                await ss.SetPhotoAsync(Sid(u), path);
                return Results.Ok(new { photoPath = path });
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
        }).DisableAntiforgery();
        st.MapPost("/change-password", async (ClaimsPrincipal u, ChangePassword r, IDbContextFactory<AppDb> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var s = await db.Students.FindAsync(Sid(u));
            if (s == null || !Security.Verify(r.OldPassword, s.PasswordHash)) return Err("Purana password galat hai");
            if (r.NewPassword.Length < 4) return Err("Naya password kam se kam 4 character ka ho");
            s.PasswordHash = Security.Hash(r.NewPassword); await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        // ===== Admin =====
        var ad = api.MapGroup("/admin").RequireAuthorization(p => p.RequireRole("Admin"));
        ad.MapGet("/dashboard", async (FeeService fs) => Results.Ok(await fs.DashboardAsync()));

        ad.MapGet("/categories", async (IDbContextFactory<AppDb> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            return Results.Ok(await db.Categories.OrderBy(c => c.Name).Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive)).ToListAsync());
        });
        ad.MapPost("/categories", async (CategoryDto d, IDbContextFactory<AppDb> f) =>
        {
            if (string.IsNullOrWhiteSpace(d.Name)) return Err("Name required");
            await using var db = await f.CreateDbContextAsync();
            var c = d.Id == 0 ? new Category() : await db.Categories.FindAsync(d.Id);
            if (c == null) return Results.NotFound();
            c.Name = d.Name.Trim(); c.Description = d.Description; c.IsActive = d.IsActive;
            if (d.Id == 0) db.Categories.Add(c);
            await db.SaveChangesAsync(); return Results.Ok(new { c.Id });
        });
        ad.MapGet("/batches", async (int? categoryId, IDbContextFactory<AppDb> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            var q = db.Batches.AsQueryable();
            if (categoryId > 0) q = q.Where(b => b.CategoryId == categoryId);
            return Results.Ok(await q.OrderBy(b => b.Name).Select(b => new
            {
                b.Id, b.CategoryId, Category = b.Category.Name, b.Name, b.Course, b.DurationMonths, b.AdmissionFee, b.MonthlyFee,
                b.Capacity, b.StartDate, b.IsActive, Students = b.Students.Count(s => s.Status == StudentStatus.Active || s.Status == StudentStatus.Pending)
            }).ToListAsync());
        });
        ad.MapPost("/batches", async (BatchDto d, IDbContextFactory<AppDb> f) =>
        {
            if (string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Course)) return Err("Batch name aur course required");
            await using var db = await f.CreateDbContextAsync();
            var b = d.Id == 0 ? new Batch() : await db.Batches.FindAsync(d.Id);
            if (b == null) return Results.NotFound();
            b.CategoryId = d.CategoryId; b.Name = d.Name.Trim(); b.Course = d.Course.Trim(); b.DurationMonths = d.DurationMonths;
            b.AdmissionFee = d.AdmissionFee; b.MonthlyFee = d.MonthlyFee; b.Capacity = d.Capacity; b.StartDate = d.StartDate; b.IsActive = d.IsActive;
            if (d.Id == 0) db.Batches.Add(b);
            await db.SaveChangesAsync(); return Results.Ok(new { b.Id });
        });

        ad.MapGet("/students", async (string? q, int? categoryId, int? batchId, string? status, int? page, StudentService ss) =>
        {
            StudentStatus? stt = Enum.TryParse<StudentStatus>(status, true, out var x) ? x : null;
            var (rows, total) = await ss.SearchAsync(q, categoryId, batchId, stt, page ?? 1, 30);
            return Results.Ok(new { total, items = rows.Select(r => new { student = StudentJson(r.S), overdue = r.Overdue }) });
        });
        ad.MapGet("/students/{id:int}", async (int id, StudentService ss, FeeService fs, IDbContextFactory<AppDb> f) =>
        {
            var s = await ss.GetAsync(id);
            if (s == null) return Results.NotFound();
            await using var db = await f.CreateDbContextAsync();
            var fees = await db.FeeInstallments.Where(i => i.StudentId == id).ToListAsync();
            var pays = await db.Payments.Where(p => p.StudentId == id).ToListAsync();
            var sets = (await fs.SettlementsAsync(id)).Select(x => new { x.Id, x.SettledOn, Scope = x.Scope.ToString(), x.BalanceBefore, x.PaidAmount, x.WaivedAmount, x.Remarks, x.SettledBy, x.IsCancelled, x.ClosedAccount });
            return Results.Ok(new { student = StudentJson(s), summary = await fs.SummaryAsync(id), fees = FeeJson(fees), payments = PayJson(pays), settlements = sets });
        });
        ad.MapPost("/students", async (StudentInput r, StudentService ss) =>
        {
            try { var s = await ss.CreateAsync(r, true); return Results.Ok(new { s.Id, s.RollNo }); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapPut("/students/{id:int}", async (int id, StudentInput r, StudentService ss) =>
        {
            try { await ss.UpdateAsync(id, r); return Results.Ok(new { ok = true }); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapPost("/students/{id:int}/approve", async (int id, StudentService ss) => { await ss.ApproveAsync(id); return Results.Ok(new { ok = true }); });
        ad.MapPost("/students/{id:int}/status", async (int id, StatusDto d, StudentService ss) =>
        {
            if (!Enum.TryParse<StudentStatus>(d.Status, true, out var s)) return Err("Invalid status");
            await ss.SetStatusAsync(id, s); return Results.Ok(new { ok = true });
        });
        ad.MapPost("/students/{id:int}/photo", async (int id, IFormFile file, FileStore fsx, StudentService ss) =>
        {
            if (file.Length > 4_000_000) return Err("Photo 4MB se choti rakhein");
            try
            {
                await using var stream = file.OpenReadStream();
                var path = await fsx.SaveAsync(stream, file.FileName, "photos");
                await ss.SetPhotoAsync(id, path); return Results.Ok(new { photoPath = path });
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
        }).DisableAntiforgery();
        ad.MapPost("/students/{id:int}/payments", async (int id, PayRequest r, ClaimsPrincipal u, FeeService fs) =>
        {
            try { var p = await fs.ReceiveAsync(id, r.Amount, r.Mode, r.Reference, r.Remarks, u.Identity?.Name ?? "Admin"); return Results.Ok(new { p.Id, p.ReceiptNo }); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapGet("/students/{id:int}/settle-preview", async (int id, string? scope, FeeService fs) =>
            Results.Ok(new { balance = await fs.BalanceAsync(id, scope == "All" ? SettleScope.All : SettleScope.Overdue) }));
        ad.MapPost("/students/{id:int}/settle", async (int id, SettleReq r, ClaimsPrincipal u, FeeService fs) =>
        {
            try
            {
                var s = await fs.SettleAsync(id, r.Scope == "All" ? SettleScope.All : SettleScope.Overdue, r.PayNow, r.Mode, r.Reference, r.Remarks, r.CloseAccount, u.Identity?.Name ?? "Admin");
                return Results.Ok(new { s.Id, s.PaymentId, s.WaivedAmount });
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapPost("/settlements/{id:int}/cancel", async (int id, FeeService fs) =>
        {
            try { await fs.CancelSettlementAsync(id); return Results.Ok(new { ok = true }); }
            catch (InvalidOperationException e) { return Err(e.Message); }
        });
        ad.MapPost("/payments/{id:int}/cancel", async (int id, FeeService fs) => { await fs.CancelPaymentAsync(id); return Results.Ok(new { ok = true }); });
        ad.MapGet("/dues", async (int? categoryId, int? batchId, FeeService fs) => Results.Ok(await fs.DuesAsync(categoryId, batchId, DateTime.Today)));
        ad.MapGet("/payments/recent", async (IDbContextFactory<AppDb> f) =>
        {
            await using var db = await f.CreateDbContextAsync();
            return Results.Ok(await db.Payments.Where(p => !p.IsCancelled).OrderByDescending(p => p.PaidOn).Take(30)
                .Select(p => new { p.Id, p.ReceiptNo, p.Amount, p.PaidOn, p.Mode, Student = p.Student.Name, p.Student.RollNo }).ToListAsync());
        });
    }
}
