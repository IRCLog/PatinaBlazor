using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatinaBlazor.Data;
using PatinaBlazor.Services;

namespace PatinaBlazor.Interceptors
{
    // Guarantees any entity implementing ISupportImageAttachments has its physical image
    // files removed whenever it is deleted through ApplicationDbContext.SaveChanges -
    // regardless of which service or page triggers the delete - so a new call site can
    // never reintroduce the orphaned-file bug ArticleService.DeleteAsync and
    // StorageService.DeletePropertyAsync originally had (they never touched ImageService
    // at all). Entity-specific Delete methods just Remove() the entity as normal;
    // EntityLogicUnitInterceptor discovers and dispatches to this unit automatically.
    //
    // Migrated from the original, single-purpose ImageCleanupSaveChangesInterceptor -
    // same explicit-load-if-not-already-loaded logic, now expressed as one
    // EntityLogicUnit<T> instead of a hardcoded interceptor.
    public class ImageCleanupLogicUnit : EntityLogicUnit<ISupportImageAttachments>
    {
        private readonly IImageService _imageService;
        private List<ImageAttachment> _imagesToDelete = new();

        public ImageCleanupLogicUnit(
            ILogger logger,
            DbContext currentContext,
            string entityTypeName,
            IDbContextFactory<ApplicationDbContext> contextFactory,
            object?[] keyValues,
            IImageService imageService)
            : base(logger, currentContext, entityTypeName, contextFactory, keyValues)
        {
            _imageService = imageService;
        }

        public override async Task OnSaving(ISupportImageAttachments entity, EntityChangeType changeType)
        {
            if (changeType != EntityChangeType.Deleted)
            {
                return;
            }

            var images = CurrentContext.Entry(entity).Collection(nameof(ISupportImageAttachments.Images));
            if (!images.IsLoaded)
            {
                await images.LoadAsync();
            }

            _imagesToDelete = entity.Images.ToList();
        }

        public override async Task OnSaved(ISupportImageAttachments entity, EntityChangeType changeType)
        {
            foreach (var image in _imagesToDelete)
            {
                if (!await _imageService.DeleteImageAsync(image))
                {
                    LogError($"Failed to delete image file '{image.FileName}'.");
                }
            }
        }
    }
}
