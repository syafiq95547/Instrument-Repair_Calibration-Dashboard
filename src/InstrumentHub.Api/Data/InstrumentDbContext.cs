using InstrumentHub.Shared;
using Microsoft.EntityFrameworkCore;
namespace InstrumentHub.Api.Data;
public sealed class InstrumentDbContext(DbContextOptions<InstrumentDbContext> options) : DbContext(options)
{
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<RepairLog> RepairLogs => Set<RepairLog>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Instrument>().HasIndex(x => x.AssetId).IsUnique();
        model.Entity<Instrument>().Property(x => x.AssetId).HasMaxLength(64);
        model.Entity<Certificate>().HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RepairLog>().HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
