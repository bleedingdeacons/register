using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Data
{
    public class MailDbContext : DbContext
    {
        public DbSet<QueuedEmail> QueuedEmails { get; set; }

        public MailDbContext(DbContextOptions<MailDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Per-entity mappings live in dedicated IEntityTypeConfiguration
            // classes under Data/Configurations. ApplyConfigurationsFromAssembly
            // discovers and applies every implementation in this assembly,
            // so adding a new entity is a one-file change (drop in a new
            // *Configuration.cs) rather than touching this method.
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(MailDbContext).Assembly);
        }
    }
}

