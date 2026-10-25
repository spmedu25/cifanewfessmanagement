using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace CoachingApp.Data;

// "dotnet ef" ke liye - app start kiye bina DB context banata hai.
// Connection string: appsettings.json / appsettings.Production.json ya env var ConnectionStrings__Default
public class AppDbFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args)
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: true)
            .AddEnvironmentVariables().Build();
        var opts = new DbContextOptionsBuilder<AppDb>().UseSqlServer(cfg.GetConnectionString("Default")).Options;
        return new AppDb(opts);
    }
}
