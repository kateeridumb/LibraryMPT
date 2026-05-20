using LibraryMPT.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace LibraryMPT.Tests;

public class MobileBookPhysicalPathResolverTests
{
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
            ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
            WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
        }

        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Production";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
    }

    [Fact]
    public void ResolveBookFullPath_ReturnsNullForBlankInput()
    {
        using var tmp = new TempContentRoot();
        var env = new FakeWebHostEnvironment(tmp.Root);

        Assert.Null(MobileBookPhysicalPathResolver.ResolveBookFullPath(env, null));
        Assert.Null(MobileBookPhysicalPathResolver.ResolveBookFullPath(env, "  "));
    }

    [Fact]
    public void ResolveBookFullPath_FindsFileUnderWwwrootForRelativeAndHttpPaths()
    {
        using var tmp = new TempContentRoot();
        var env = new FakeWebHostEnvironment(tmp.Root);
        var relDir = Path.Combine(env.WebRootPath, "books");
        Directory.CreateDirectory(relDir);
        var full = Path.Combine(relDir, "one.pdf");
        File.WriteAllText(full, "x");

        var fromRelative = MobileBookPhysicalPathResolver.ResolveBookFullPath(env, @"books/one.pdf");
        var fromHttp = MobileBookPhysicalPathResolver.ResolveBookFullPath(env, "https://host/books/one.pdf");

        Assert.Equal(Path.GetFullPath(full), Path.GetFullPath(fromRelative!));
        Assert.Equal(Path.GetFullPath(full), Path.GetFullPath(fromHttp!));
    }

    [Fact]
    public void ResolveBookFullPath_WhenFileMissing_ReturnsExpectedCandidatePath()
    {
        using var tmp = new TempContentRoot();
        var env = new FakeWebHostEnvironment(tmp.Root);
        var expected = Path.Combine(env.WebRootPath, "missing.pdf");

        var resolved = MobileBookPhysicalPathResolver.ResolveBookFullPath(env, "missing.pdf");

        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(resolved!));
        Assert.False(File.Exists(resolved));
    }

    [Fact]
    public void ResolvePlaceholderBookCoverPath_FindsSiblingRepoWwwrootWhenMissingUnderApi()
    {
        using var tmp = new TempContentRoot();
        var repoRoot = tmp.Root;
        var apiRoot = Path.Combine(repoRoot, "LibraryMPT.Api");
        Directory.CreateDirectory(apiRoot);
        var imgDir = Path.Combine(repoRoot, "wwwroot", "images");
        Directory.CreateDirectory(imgDir);
        var placeholder = Path.Combine(imgDir, "placeholder-book.png");
        File.WriteAllText(placeholder, "png");

        var env = new FakeWebHostEnvironment(apiRoot);

        var resolved = MobileBookPhysicalPathResolver.ResolvePlaceholderBookCoverPath(env);

        Assert.Equal(Path.GetFullPath(placeholder), Path.GetFullPath(resolved!));
    }

    private sealed class TempContentRoot : IDisposable
    {
        public string Root { get; }

        public TempContentRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "LibraryMPT-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // ignore cleanup errors on CI
            }
        }
    }
}
