using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PatinaBlazor.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<HitCounter> HitCounters { get; set; }
        public DbSet<Collectable> Collectables { get; set; }
        public DbSet<CollectableImage> CollectableImages { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure Identity key lengths for SQL Server compatibility
            builder.Entity<IdentityRole>(entity =>
            {
                entity.Property(e => e.Id).HasMaxLength(128);
                entity.Property(e => e.Name).HasMaxLength(256);
                entity.Property(e => e.NormalizedName).HasMaxLength(256);
            });

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(e => e.Id).HasMaxLength(128);
                entity.Property(e => e.UserName).HasMaxLength(256);
                entity.Property(e => e.NormalizedUserName).HasMaxLength(256);
                entity.Property(e => e.Email).HasMaxLength(256);
                entity.Property(e => e.NormalizedEmail).HasMaxLength(256);

                // Database-specific default values
                if (Database.IsSqlServer())
                {
                    entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(e => e.CreatedDate).HasDefaultValueSql("datetime('now')");
                }
            });

            builder.Entity<IdentityUserRole<string>>(entity =>
            {
                entity.Property(e => e.UserId).HasMaxLength(128);
                entity.Property(e => e.RoleId).HasMaxLength(128);
            });

            builder.Entity<IdentityUserClaim<string>>(entity =>
            {
                entity.Property(e => e.UserId).HasMaxLength(128);
            });

            builder.Entity<IdentityUserLogin<string>>(entity =>
            {
                entity.Property(e => e.UserId).HasMaxLength(128);
                entity.Property(e => e.LoginProvider).HasMaxLength(128);
                entity.Property(e => e.ProviderKey).HasMaxLength(128);
            });

            builder.Entity<IdentityRoleClaim<string>>(entity =>
            {
                entity.Property(e => e.RoleId).HasMaxLength(128);
            });

            builder.Entity<IdentityUserToken<string>>(entity =>
            {
                entity.Property(e => e.UserId).HasMaxLength(128);
                entity.Property(e => e.LoginProvider).HasMaxLength(128);
                entity.Property(e => e.Name).HasMaxLength(128);
            });

            builder.Entity<HitCounter>(entity =>
            {
                entity.HasIndex(e => e.PagePath).IsUnique();

                // Database-specific default values
                if (Database.IsSqlServer())
                {
                    entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                    entity.Property(e => e.LastHit).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
                    entity.Property(e => e.LastHit).HasDefaultValueSql("datetime('now')");
                }
            });

            builder.Entity<Collectable>(entity =>
            {
                // Configure foreign key to match Identity user ID length
                entity.Property(e => e.UserId).HasMaxLength(128);

                // Database-specific default values
                if (Database.IsSqlServer())
                {
                    entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(e => e.CreatedDate).HasDefaultValueSql("datetime('now')");
                }

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(e => e.Images)
                      .WithOne(e => e.Collectable)
                      .HasForeignKey(e => e.CollectableId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CollectableImage>(entity =>
            {
                // Database-specific default values
                if (Database.IsSqlServer())
                {
                    entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                }
                else
                {
                    entity.Property(e => e.CreatedDate).HasDefaultValueSql("datetime('now')");
                }

                entity.HasOne(e => e.Collectable)
                      .WithMany(e => e.Images)
                      .HasForeignKey(e => e.CollectableId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
