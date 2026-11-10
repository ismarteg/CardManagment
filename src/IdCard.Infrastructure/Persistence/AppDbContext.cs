using IdCard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdCard.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<CardProject> Projects => Set<CardProject>();
    public DbSet<InternalRecord> InternalRecords => Set<InternalRecord>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<PrintAgentDevice> Agents => Set<PrintAgentDevice>();
    public DbSet<AgentPairingCode> PairingCodes => Set<AgentPairingCode>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(256);
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(200);
        });

        modelBuilder.Entity<CardProject>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.DefaultPrinter).WithMany().HasForeignKey(x => x.DefaultPrinterId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InternalRecord>(e =>
        {
            e.HasIndex(x => new { x.ProjectId, x.ExternalKey });
            e.HasOne(x => x.Project).WithMany(p => p.InternalRecords).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Printer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Agent).WithMany(a => a.Printers).HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PrintAgentDevice>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.MachineName).HasMaxLength(200);
        });

        modelBuilder.Entity<AgentPairingCode>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
        });

        modelBuilder.Entity<PrintJob>(e =>
        {
            e.HasOne(x => x.Project).WithMany(p => p.PrintJobs).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Printer).WithMany().HasForeignKey(x => x.PrinterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.Action);
            e.Property(x => x.Action).HasMaxLength(200);
        });
    }
}
