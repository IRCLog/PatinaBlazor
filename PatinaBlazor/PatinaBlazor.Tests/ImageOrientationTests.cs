using Microsoft.Extensions.Logging.Abstractions;
using PatinaBlazor.Services;
using SkiaSharp;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Reproduces the EXIF-orientation verification that was previously done once via a
    // throwaway console harness comparing pixel-for-pixel against Pillow's trusted
    // ImageOps.exif_transpose (see CLAUDE.md's 2026-09-05 EXIF-orientation-bug entry - this
    // is the exact bug that shipped to production once, rotating real users' photos, before
    // being fixed) - then deleted. Permanent coverage this time.
    //
    // Pure file I/O + SkiaSharp - ImageService.SaveImageFromDiskAsync never touches the
    // database, so this doesn't join the "Database" collection (no reason to pay for a SQL
    // Server container here).
    //
    // TestAssets/exif/exif-orientation-{N}.jpg were generated once via Pillow (raw 64x48
    // pixel data with a distinct color in each quadrant - Red/Green/Blue/Yellow for
    // TopLeft/TopRight/BottomLeft/BottomRight - plus a real EXIF Orientation tag set to N,
    // for the 6 orientation values real cameras/phones actually produce: 1,2,3,4,6,8; 5 and
    // 7 don't occur in practice). Expected below is the ground truth Pillow's own
    // ImageOps.exif_transpose produced for each file at generation time - computed once,
    // not re-derived at test time, so this test has no Python/Pillow dependency itself.
    public sealed class ImageOrientationTests : IDisposable
    {
        private readonly string _webRootPath = Path.Combine(Path.GetTempPath(), "PatinaBlazorExifTests_" + Guid.NewGuid());
        private readonly IImageService _imageService;

        private static readonly Dictionary<string, (byte R, byte G, byte B)> ReferenceColors = new()
        {
            ["Red"] = (220, 20, 20),
            ["Green"] = (20, 200, 20),
            ["Blue"] = (20, 20, 220),
            ["Yellow"] = (230, 230, 20),
        };

        private static readonly Dictionary<int, (int Width, int Height, string TopLeft, string TopRight, string BottomLeft, string BottomRight)> Expected = new()
        {
            [1] = (64, 48, "Red", "Green", "Blue", "Yellow"),
            [2] = (64, 48, "Green", "Red", "Yellow", "Blue"),
            [3] = (64, 48, "Yellow", "Blue", "Green", "Red"),
            [4] = (64, 48, "Blue", "Yellow", "Red", "Green"),
            [6] = (48, 64, "Blue", "Red", "Yellow", "Green"),
            [8] = (48, 64, "Green", "Yellow", "Red", "Blue"),
        };

        public ImageOrientationTests()
        {
            Directory.CreateDirectory(_webRootPath);
            _imageService = new ImageService(new TestWebHostEnvironment(_webRootPath), NullLogger<ImageService>.Instance);
        }

        public void Dispose()
        {
            if (Directory.Exists(_webRootPath))
            {
                Directory.Delete(_webRootPath, recursive: true);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(6)]
        [InlineData(8)]
        public async Task SaveImageFromDiskAsync_BakesExifOrientationIntoPixelDataCorrectly(int orientation)
        {
            var sourcePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "exif", $"exif-orientation-{orientation}.jpg");
            Assert.True(File.Exists(sourcePath), $"Missing test fixture: {sourcePath}");

            var upload = await _imageService.SaveImageFromDiskAsync(sourcePath, "exiftest");

            var savedPath = Path.Combine(_webRootPath, upload.RelativePath.TrimStart('/'));
            using var bitmap = SKBitmap.Decode(savedPath);
            Assert.NotNull(bitmap);

            var expected = Expected[orientation];

            // Confirms dimension-swapping orientations (6, 8 - a 90-degree rotation) actually
            // swapped width/height, not just that pixels moved around within the original frame.
            Assert.Equal(expected.Width, bitmap.Width);
            Assert.Equal(expected.Height, bitmap.Height);

            Assert.Equal(expected.TopLeft, ClassifyQuadrant(bitmap, quadrantX: 0, quadrantY: 0));
            Assert.Equal(expected.TopRight, ClassifyQuadrant(bitmap, quadrantX: 1, quadrantY: 0));
            Assert.Equal(expected.BottomLeft, ClassifyQuadrant(bitmap, quadrantX: 0, quadrantY: 1));
            Assert.Equal(expected.BottomRight, ClassifyQuadrant(bitmap, quadrantX: 1, quadrantY: 1));
        }

        // Samples a point well inside the given quadrant (not at its boundary, where JPEG's
        // lossy 8x8 block compression can blend neighboring colors) and classifies it by
        // nearest reference color - robust to the small color drift JPEG re-encoding
        // introduces, unlike an exact RGB equality check.
        private static string ClassifyQuadrant(SKBitmap bitmap, int quadrantX, int quadrantY)
        {
            var x = quadrantX == 0 ? bitmap.Width / 4 : 3 * bitmap.Width / 4;
            var y = quadrantY == 0 ? bitmap.Height / 4 : 3 * bitmap.Height / 4;
            var pixel = bitmap.GetPixel(x, y);

            return ReferenceColors
                .OrderBy(kv => Math.Pow(pixel.Red - kv.Value.R, 2) + Math.Pow(pixel.Green - kv.Value.G, 2) + Math.Pow(pixel.Blue - kv.Value.B, 2))
                .First().Key;
        }
    }
}
