using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PatinaBlazor.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<HitCounter> HitCounters { get; set; }
        public DbSet<Collectable> Collectables { get; set; }
        public DbSet<CollectableCollection> CollectableCollections { get; set; }
        public DbSet<CollectableCollectionItem> CollectableCollectionItems { get; set; }
        public DbSet<IrcEvent> IrcEvents { get; set; }
        public DbSet<StorageProperty> StorageProperties { get; set; }
        public DbSet<StorageUnit> StorageUnits { get; set; }
        public DbSet<StorageRental> StorageRentals { get; set; }
        public DbSet<ImageAttachment> ImageAttachments { get; set; }

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
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.DisplayName).HasMaxLength(50);
                entity.HasIndex(e => e.DisplayName).IsUnique().HasFilter("[DisplayName] IS NOT NULL");
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
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.LastHit).HasDefaultValueSql("GETUTCDATE()");
            });

            builder.Entity<Collectable>(entity =>
            {
                // Configure foreign key to match Identity user ID length
                entity.Property(e => e.UserId).HasMaxLength(128);
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CollectableCollection>(entity =>
            {
                entity.Property(e => e.UserId).HasMaxLength(128);
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.ModifiedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.CollectableItems)
                      .WithOne(e => e.Collection)
                      .HasForeignKey(e => e.CollectableCollectionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CollectableCollectionItem>(entity =>
            {
                entity.Property(e => e.AddedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.Collection)
                      .WithMany(e => e.CollectableItems)
                      .HasForeignKey(e => e.CollectableCollectionId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Collectable)
                      .WithMany()
                      .HasForeignKey(e => e.CollectableId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<IrcEvent>(entity =>
            {
                entity.Property(e => e.Action).HasConversion<string>();
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => new { e.Network, e.Timestamp });
            });

            builder.Entity<StorageProperty>(entity =>
            {
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.ModifiedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.CreatedByUserId).HasMaxLength(128);
                entity.Property(e => e.ModifiedByUserId).HasMaxLength(128);

                // Audit FKs use Restrict (not SetNull/Cascade): SQL Server rejects two
                // SetNull/Cascade paths from the same table to the same parent table
                // (error 1785) even when the columns are independent, and this table
                // already has two audit FKs pointing at AspNetUsers.
                entity.HasOne(e => e.CreatedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ModifiedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.ModifiedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(e => e.Units)
                      .WithOne(e => e.Property)
                      .HasForeignKey(e => e.StoragePropertyId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<StorageUnit>(entity =>
            {
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.ModifiedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.CreatedByUserId).HasMaxLength(128);
                entity.Property(e => e.ModifiedByUserId).HasMaxLength(128);

                entity.HasOne(e => e.CreatedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ModifiedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.ModifiedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Property)
                      .WithMany(e => e.Units)
                      .HasForeignKey(e => e.StoragePropertyId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Rentals)
                      .WithOne(e => e.Unit)
                      .HasForeignKey(e => e.StorageUnitId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<StorageRental>(entity =>
            {
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.ModifiedDate).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.CustomerUserId).HasMaxLength(128);
                entity.Property(e => e.CreatedByUserId).HasMaxLength(128);
                entity.Property(e => e.ModifiedByUserId).HasMaxLength(128);

                // Preserve rental/revenue history even if the customer account is later
                // deleted, mirroring CollectableCollectionItem -> Collectable's Restrict.
                entity.HasOne(e => e.Customer)
                      .WithMany()
                      .HasForeignKey(e => e.CustomerUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.CreatedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.ModifiedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.ModifiedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Unit)
                      .WithMany(e => e.Rentals)
                      .HasForeignKey(e => e.StorageUnitId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // The single uniform image table for every ISupportImageAttachments entity.
            // A new owner type adds its own HasOne(...).WithMany(...) pair here alongside a
            // new nullable FK column on ImageAttachment - never a new one-off image table.
            builder.Entity<ImageAttachment>(entity =>
            {
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(e => e.Collectable)
                      .WithMany(e => e.Images)
                      .HasForeignKey(e => e.CollectableId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.StorageProperty)
                      .WithMany(e => e.Images)
                      .HasForeignKey(e => e.StoragePropertyId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_ImageAttachment_ExactlyOneOwner",
                    "([CollectableId] IS NOT NULL AND [StoragePropertyId] IS NULL) OR ([CollectableId] IS NULL AND [StoragePropertyId] IS NOT NULL)"));
            });
        }
    }
}
