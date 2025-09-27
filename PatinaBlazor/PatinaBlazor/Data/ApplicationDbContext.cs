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

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("datetime('now')");
            });

            builder.Entity<HitCounter>(entity =>
            {
                entity.HasIndex(e => e.PagePath).IsUnique();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
                entity.Property(e => e.LastHit).HasDefaultValueSql("datetime('now')");
            });

            builder.Entity<Collectable>(entity =>
            {
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("datetime('now')");
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
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("datetime('now')");
                entity.HasOne(e => e.Collectable)
                      .WithMany(e => e.Images)
                      .HasForeignKey(e => e.CollectableId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
