using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data;

public class RegisterContext : DbContext
{
    public DbSet<Group> Groups { get; set; }
    public DbSet<Position> Positions { get; set; }

    public RegisterContext(DbContextOptions<RegisterContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all IEntityTypeConfiguration implementations from the current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegisterContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}