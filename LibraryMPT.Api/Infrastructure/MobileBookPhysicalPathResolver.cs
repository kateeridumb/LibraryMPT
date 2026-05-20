using Microsoft.AspNetCore.Hosting;

namespace LibraryMPT.Api.Infrastructure;

internal static class MobileBookPhysicalPathResolver
{
    private const string PlaceholderRelativePath = "images/placeholder-book.png";

    /// <summary>Путь к заглушке обложки (wwwroot/images/placeholder-book.png) — рядом с API или в основном MVC-проекте.</summary>
    internal static string? ResolvePlaceholderBookCoverPath(IWebHostEnvironment environment)
    {
        var apiCandidate = Path.Combine(environment.ContentRootPath, "wwwroot", PlaceholderRelativePath);
        if (File.Exists(apiCandidate))
            return apiCandidate;

        var parent = Directory.GetParent(environment.ContentRootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
            return null;

        var mainWwwroot = Path.Combine(parent, "wwwroot", PlaceholderRelativePath);
        if (File.Exists(mainWwwroot))
            return mainWwwroot;

        var apiSibling = Path.Combine(parent, "LibraryMPT.Api", "wwwroot", PlaceholderRelativePath);
        if (File.Exists(apiSibling))
            return apiSibling;

        return null;
    }

    public static string? ResolveBookFullPath(IWebHostEnvironment environment, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var trimmed = filePath.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                trimmed = Uri.UnescapeDataString(uri.AbsolutePath);
                trimmed = trimmed.TrimStart('/');
            }
        }

        var root = Path.GetPathRoot(trimmed) ?? string.Empty;
        var isWindowsDrive = root.Length >= 2 && char.IsLetter(root[0]) && root[1] == ':';
        var isUnc = root.StartsWith(@"\\", StringComparison.Ordinal);

        if (isWindowsDrive || isUnc)
            return System.IO.File.Exists(trimmed) ? trimmed : null;

        var relativePath = trimmed.Replace("\\", "/", StringComparison.Ordinal)
            .TrimStart('~')
            .TrimStart('/', '\\');

        if (relativePath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath.Substring("wwwroot/".Length);

        var apiRootCandidate = Path.Combine(environment.ContentRootPath, "wwwroot", relativePath);
        if (System.IO.File.Exists(apiRootCandidate))
            return apiRootCandidate;

        var parent = Directory.GetParent(environment.ContentRootPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var legacyRootCandidate = Path.Combine(parent, "wwwroot", relativePath);
            if (System.IO.File.Exists(legacyRootCandidate))
                return legacyRootCandidate;

            var apiSibling = Path.Combine(parent, "LibraryMPT.Api", "wwwroot", relativePath);
            if (System.IO.File.Exists(apiSibling))
                return apiSibling;
        }

        return apiRootCandidate;
    }
}
