using Microsoft.EntityFrameworkCore;
namespace CoachingApp.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<FeeInstallment> FeeInstallments => Set<FeeInstallment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> Allocations => Set<PaymentAllocation>();
    public DbSet<AdminUser> Admins => Set<AdminUser>();
    public DbSet<InstituteSettings> Settings => Set<InstituteSettings>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementAllocation> SettlementAllocations => Set<SettlementAllocation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b) => b.Properties<decimal>().HavePrecision(18, 2);

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<InstituteSettings>().Property(s => s.Id).ValueGeneratedNever();   // Id hamesha 1 (identity nahi)
        m.Entity<Student>().HasIndex(s => s.RollNo).IsUnique();
        m.Entity<Student>().HasIndex(s => s.Mobile);
        m.Entity<Payment>().HasIndex(p => p.ReceiptNo).IsUnique();
        m.Entity<Settlement>().HasOne(x => x.Student).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<SettlementAllocation>().HasOne(a => a.Installment).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<SettlementAllocation>().HasOne(a => a.Settlement).WithMany(x => x.Allocations).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ChatMessage>().HasIndex(c => new { c.StudentId, c.Id });
        m.Entity<ChatMessage>().HasOne(c => c.Student).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<DeviceToken>().HasIndex(d => d.Token).IsUnique();
        m.Entity<DeviceToken>().HasOne(d => d.Student).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<AdminUser>().HasIndex(a => a.Username).IsUnique();
        m.Entity<Batch>().HasOne(b => b.Category).WithMany(c => c.Batches).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Student>().HasOne(s => s.Batch).WithMany(b => b.Students).OnDelete(DeleteBehavior.Restrict);
        m.Entity<FeeInstallment>().HasOne(i => i.Student).WithMany(s => s.Installments).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Payment>().HasOne(p => p.Student).WithMany(s => s.Payments).OnDelete(DeleteBehavior.Restrict);
        m.Entity<PaymentAllocation>().HasOne(a => a.Installment).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<PaymentAllocation>().HasOne(a => a.Payment).WithMany(p => p.Allocations).OnDelete(DeleteBehavior.Cascade);
    }
}
