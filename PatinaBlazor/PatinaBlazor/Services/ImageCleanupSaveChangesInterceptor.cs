using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Guarantees any entity implementing ISupportImageAttachments has its physical image
    // files removed whenever it is deleted through ApplicationDbContext.SaveChanges -
    // regardless of which service or page triggers the delete - so a new call site can
    // never reintroduce the orphaned-file bug ArticleService.DeleteAsync and
    // StorageService.DeletePropertyAsync originally had (they never touched ImageService
    // at all). This is the single, uniform place that behavior lives; entity-specific
    // Delete methods just Remove() the entity as normal and this handles the rest.
    //
    // "What to delete" is captured in SavingChangesAsync, before the row (and its
    // cascade-deleted ImageAttachment rows) are actually removed - the real file I/O
    // happens in SavedChangesAsync, after the delete has committed. State is keyed per
    // DbContext instance via ConditionalWeakTable rather than an instance field, since
    // this interceptor is registered once (singleton) and shared across every
    // short-lived DbContext the app's IDbContextFactory creates - an instance field would
    // let concurrent SaveChanges calls from different contexts corrupt each other's
    // pending-delete list.
    public class ImageCleanupSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly IImageService _imageService;
        private static readonly ConditionalWeakTable<DbContext, List<ImageAttachment>> PendingDeletes = new();

        public ImageCleanupSaveChangesInterceptor(IImageService imageService)
        {
            _imageService = imageService;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context)
            {
                var imagesToDelete = new List<ImageAttachment>();

                foreach (var entry in context.ChangeTracker.Entries())
                {
                    if (entry.State != EntityState.Deleted || entry.Entity is not ISupportImageAttachments owner)
                    {
                        continue;
                    }

                    var images = entry.Collection(nameof(ISupportImageAttachments.Images));
                    if (!images.IsLoaded)
                    {
                        await images.LoadAsync(cancellationToken);
                    }

                    imagesToDelete.AddRange(owner.Images);
                }

                if (imagesToDelete.Count > 0)
                {
                    PendingDeletes.AddOrUpdate(context, imagesToDelete);
                }
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && PendingDeletes.TryGetValue(context, out var images))
            {
                PendingDeletes.Remove(context);
                foreach (var image in images)
                {
                    await _imageService.DeleteImageAsync(image);
                }
            }

            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context)
            {
                PendingDeletes.Remove(context);
            }

            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }
    }
}
