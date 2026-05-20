using Microsoft.AspNetCore.Hosting;

namespace LibraryMPT.Helpers;

/// <summary>Локальный поиск файла обложки и заглушки (тот же смысл путей, что в API).</summary>
public static class BookCoverPhysicalFilePaths
{
    private const string PlaceholderRelativePath = "images/placeholder-book.png";

    public static string? ResolvePlaceholder(IWebHostEnvironment environment)
    {
        var main = Path.Combine(environment.ContentRootPath, "wwwroot", PlaceholderRelativePath);
        if (File.Exists(main))
            return main;

        var parent = Directory.GetParent(environment.ContentRootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
            return null;

        var apiSibling = Path.Combine(parent, "LibraryMPT.Api", "wwwroot", PlaceholderRelativePath);
        if (File.Exists(apiSibling))
            return apiSibling;

        return null;
    }

    public static string? ResolveBookImage(IWebHostEnvironment environment, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var trimmed = filePath.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        var root = Path.GetPathRoot(trimmed) ?? string.Empty;
        var isWindowsDrive = root.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';
        var isUnc = trimmed.StartsWith("\\\\", StringComparison.Ordinal);

        if (isWindowsDrive || isUnc)
            return File.Exists(trimmed) ? trimmed : null;

        var relativePath = trimmed.Replace("\\", "/", StringComparison.Ordinal)
            .TrimStart('~')
            .TrimStart('/', '\\');

        if (relativePath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath.Substring("wwwroot/".Length);

        var mainWww = Path.Combine(environment.ContentRootPath, "wwwroot", relativePath);
        if (File.Exists(mainWww))
            return mainWww;

        var parent = Directory.GetParent(environment.ContentRootPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var legacy = Path.Combine(parent, "wwwroot", relativePath);
            if (File.Exists(legacy))
                return legacy;

            var apiSibling = Path.Combine(parent, "LibraryMPT.Api", "wwwroot", relativePath);
            if (File.Exists(apiSibling))
                return apiSibling;
        }

        return null;
    }

    public static string GetImageContentType(string fullPath)
    {
        var ext = Path.GetExtension(fullPath)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
