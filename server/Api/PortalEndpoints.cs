using System.Security.Claims;
using CoachingApp.Data;
using CoachingApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Api;

public static class PortalEndpoints
{
    public static void MapPortal(this WebApplication app)
    {
        // Route ko /api/portal/login kar diya taaki Blazor page ke route se clash na ho
        app.MapPost("/api/portal/login", async (HttpContext ctx, IDbContextFactory<AppDb> f) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var login = form["login"].ToString().Trim(); var pw = form["password"].ToString();
            await using var db = await f.CreateDbContextAsync();
            var list = await db.Students.Where(x => x.Mobile == login || x.RollNo.ToString() == login).ToListAsync();
            var s = list.FirstOrDefault(x => Security.Verify(pw, x.PasswordHash));
            if (s == null) return Results.Redirect("/portal/login?error=1");
            var id = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, s.Id.ToString()), new Claim("sid", s.Id.ToString()),
                new Claim(ClaimTypes.Name, s.Name), new Claim(ClaimTypes.Role, "Student") }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
            return Results.Redirect("/portal");
        }).DisableAntiforgery();

        app.MapGet("/portal/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/portal/login");
        });
    }
}