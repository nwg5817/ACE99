using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ACE.Database.Models.PKKills
{
    public partial class PKKillsDbContext : DbContext
    {
        public PKKillsDbContext()
        {
        }

        public PKKillsDbContext(DbContextOptions<PKKillsDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Kill> Kills { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var config = Common.ConfigManager.Config.MySql.PKKills;

                optionsBuilder.UseMySql($"server={config.Host};port={config.Port};user={config.Username};password={config.Password};database={config.Database};TreatTinyAsBoolean=False", builder =>
                {
                    builder.EnableRetryOnFailure(10);
                });
            }

#if EFPKKILLSDEBUG
            optionsBuilder.EnableSensitiveDataLogging(true);
#endif
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Kill>(entity =>
            {
                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.ToTable("kills");


                entity.Property(e => e.VictimId)
                    .HasColumnName("victim_id");

                entity.Property(e => e.KillerId)
                    .HasColumnName("killer_id");

                entity.Property(e => e.VictimMonarchId)
                    .HasColumnName("victim_monarch_id");

                entity.Property(e => e.KillerMonarchId)
                    .HasColumnName("killer_monarch_id");

                entity.Property(e => e.KillDateTime)
                    .HasColumnName("kill_datetime");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
