using CoachingApp.Data;
using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Services;

public static class Seed
{
    public static async Task RunAsync(IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();   // pending migrations apne aap apply
        if (!await db.Admins.AnyAsync())
            db.Admins.Add(new AdminUser { Username = "admin", FullName = "Administrator", PasswordHash = Security.Hash("admin123") });
        if (!await db.Settings.AnyAsync()) db.Settings.Add(new InstituteSettings());
        await db.SaveChangesAsync();
    }
}
