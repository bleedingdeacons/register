using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data;

public class RegisterContext : DbContext
{
    public DbSet<Group> Groups { get; set; }
    public DbSet<Position> Positions { get; set; }

    public RegisterContext(DbContextOptions<RegisterContext> options) : base(options) {

    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder 
            .EnableSensitiveDataLogging(); // Add this line
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Group entity configuration
        modelBuilder.Entity<Group>(entity =>
        {
            entity.ToTable("Groups");
            entity.HasKey(e => e.ID);
            entity.Property(e => e.Day).HasMaxLength(50);
            entity.Property(e => e.Time).HasMaxLength(6);
            entity.Property(e => e.EndTime).HasMaxLength(6);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.GsrName).HasMaxLength(255);
            entity.Property(e => e.GsrEmailPersonal).HasMaxLength(255);
            entity.Property(e => e.GsrPhone).HasMaxLength(60);
            entity.Property(e => e.GroupGenericEmail).HasMaxLength(255);
            entity.Property(e => e.Location).HasMaxLength(100);
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.Contact1Name).HasMaxLength(255);
            entity.Property(e => e.Contact1Email).HasMaxLength(255);
            entity.Property(e => e.Contact1Phone).HasMaxLength(20);
            entity.Property(e => e.Contact2Name).HasMaxLength(255);
            entity.Property(e => e.Contact2Email).HasMaxLength(255);
            entity.Property(e => e.Contact2Phone).HasMaxLength(20);
            entity.Property(e => e.Contact3Name).HasMaxLength(255);
            entity.Property(e => e.Contact3Email).HasMaxLength(255);
            entity.Property(e => e.Contact3Phone).HasMaxLength(20);
            entity.Property(e => e.Types).HasMaxLength(255);
            entity.Property(e => e.UsingGeneric).HasColumnType("bit");
            entity.Property(e => e.Updated).HasColumnType("DateTime");
            entity.Property(e => e.Attended).HasColumnType("bit");
        });

        // Position entity configuration
        modelBuilder.Entity<Position>(entity =>
        {
            entity.ToTable("Positions");
            entity.HasKey(e => e.ID);

            entity.Property(e => e.PositionName).HasMaxLength(100);
            entity.Property(e => e.PositionLongName).HasMaxLength(255);
            entity.Property(e => e.PositionGenericEmail).HasMaxLength(255);
            entity.Property(e => e.MemberAnonymousName).HasMaxLength(255);
            entity.Property(e => e.MemberPersonalEmail).HasMaxLength(255);
            entity.Property(e => e.MemberMobile).HasMaxLength(20);
            entity.Property(e => e.PositionDuration).HasMaxLength(50);            
            entity.Property(e => e.Updated).HasColumnType("DateTime");
            entity.Property(e => e.Attended).HasColumnType("bit");
        });

        base.OnModelCreating(modelBuilder);
    }
}