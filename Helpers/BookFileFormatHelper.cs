namespace LibraryMPT.Helpers;
public static class BookFileFormatHelper
{
    public static string GetReaderFileType(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "unknown";

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "pdf",
            ".epub" => "epub",
            ".fb2" => "fb2",
            ".txt" => "text",
            ".zip" when fileName.EndsWith(".fb2.zip", StringComparison.Ordinal) => "fb2",
            _ => "unknown"
        };
    }

    public static string GetDownloadContentType(string fullPath)
    {
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var fileName = Path.GetFileName(fullPath).ToLowerInvariant();
        if (fileName.EndsWith(".fb2.zip", StringComparison.Ordinal))
            return "application/zip";

        return extension switch
        {
            ".pdf" => "application/pdf",
            ".epub" => "application/epub+zip",
            ".fb2" => "application/x-fictionbook+xml",
            ".txt" => "text/plain; charset=utf-8",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }
    public static string BuildDownloadFileName(string bookTitle, string fullPath)
    {
        var safe = SanitizeFileNameSegment(string.IsNullOrWhiteSpace(bookTitle) ? "book" : bookTitle);
        if (string.IsNullOrEmpty(safe))
            safe = "book";

        var original = Path.GetFileName(fullPath);
        if (original.EndsWith(".fb2.zip", StringComparison.OrdinalIgnoreCase))
            return $"{safe}.fb2.zip";

        var ext = Path.GetExtension(original);
        return string.IsNullOrEmpty(ext) ? safe : safe + ext;
    }

    private static string SanitizeFileNameSegment(string title)
    {
        var trimmed = title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        trimmed = trimmed.Trim().TrimEnd('.');
        return trimmed;
    }
}
