namespace PatinaBlazor.Data
{
    // Implemented by any entity that has pictures attached to it (Collectable, StorageProperty,
    // and future entities). ImageSubfolder centralizes the on-disk/URL subfolder name that was
    // previously repeated as magic strings ("collectables", "storageproperties") at every call site.
    public interface ISupportImageAttachments
    {
        string ImageSubfolder { get; }
        ICollection<ImageAttachment> Images { get; }
    }

    public static class ImageAttachmentExtensions
    {
        public static ImageAttachment? GetMainImage(this ISupportImageAttachments owner) =>
            owner.Images
                .OrderByDescending(i => i.IsMainImage)
                .ThenBy(i => i.DisplayOrder)
                .FirstOrDefault();
    }
}
