using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace PatinaBlazor.Tests
{
    // Minimal IWebHostEnvironment for tests that need real file I/O (ImageService writes to
    // WebRootPath) without a real ASP.NET Core host - shared by any test needing this, not
    // just DatabaseFixture, since some (e.g. ImageOrientationTests) don't need a database at all.
    public sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string webRootPath)
        {
            WebRootPath = webRootPath;
            WebRootFileProvider = new PhysicalFileProvider(webRootPath);
            ContentRootPath = webRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(webRootPath);
        }

        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ApplicationName { get; set; } = "PatinaBlazor.Tests";
        public IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
