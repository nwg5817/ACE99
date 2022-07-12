using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ACE.Database.Models.SessionLog
{
    public partial class SessionLogDbContext : DbContext
    {
        public SessionLogDbContext()
        {
        }

        public SessionLogDbContext(DbContextOptions<SessionLogDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<AccountSessionLog> AccountSessions { get; set; }

        public virtual DbSet<CharacterLoginLog> CharacterLogins { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var config = Common.ConfigManager.Config.MySql.SessionLog;

                optionsBuilder.UseMySql($"server={config.Host};port={config.Port};user={config.Username};password={config.Password};database={config.Database};TreatTinyAsBoolean=False", builder =>
                {
                    builder.EnableRetryOnFailure(10);
                });
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AccountSessionLog>(entity =>
            {
                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.Property(e => e.Id).HasColumnName("sessionLogId");

                entity.ToTable("account_session_log");

                entity.Property(e => e.AccountId)
                    .HasColumnName("accountId");

                entity.Property(e => e.AccountName)
                    .HasColumnName("accountName");

                entity.Property(e => e.SessionIP)
                    .HasColumnName("sessionIP");

                entity.Property(e => e.LoginDateTime)
                    .HasColumnName("loginDateTime");
            });

            modelBuilder.Entity<CharacterLoginLog>(entity =>
            {
                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.Property(e => e.Id).HasColumnName("characterLoginLogId");

                entity.ToTable("character_login_log");

                entity.Property(e => e.AccountId)
                    .HasColumnName("accountId");

                entity.Property(e => e.AccountName)
                    .HasColumnName("accountName");

                entity.Property(e => e.SessionIP)
                    .HasColumnName("sessionIP");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId");

                entity.Property(e => e.CharacterName)
                    .HasColumnName("characterName");

                entity.Property(e => e.LoginDateTime)
                    .HasColumnName("loginDateTime");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
