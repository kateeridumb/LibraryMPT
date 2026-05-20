using Microsoft.AspNetCore.Mvc;

namespace LibraryMPT.Helpers;

/// <summary>Преобразует значение imagepath из БД в корректный URL для атрибута src (в т.ч. ~/… → путь от корня приложения).</summary>
public static class BookCoverWebUrl
{
    /// <summary>Снимает пробелы и типичные обрамляющие кавычки из значения в БД.</summary>
    public static string? NormalizeStoredImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var t = imagePath.Trim().Trim('"', '\'', '\u201c', '\u201d', '«', '»').Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    public static string? ForImgSrc(IUrlHelper url, string? imagePath)
    {
        var normalized = NormalizeStoredImagePath(imagePath);
        if (normalized is null)
            return null;

        var t = normalized;
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return t;

        var root = Path.GetPathRoot(t) ?? string.Empty;
        if ((root.Length >= 2 && char.IsLetter(t[0]) && t[1] == ':') ||
            t.StartsWith("\\\\", StringComparison.Ordinal))
            return null;

        if (t.StartsWith('/'))
            return t;

        if (t.StartsWith('~'))
            return url.Content(t);

        return url.Content("~/" + t.TrimStart('/', '\\').Replace('\\', '/'));
    }

    /// <summary>
    /// Всегда URL прокси <c>Client/BookCover</c>: внешние CDN качаются на сервере API, браузер не ходит на postimg напрямую.
    /// </summary>
    public static string ForAuthenticatedClientCover(IUrlHelper url, int bookId, string? _)
    {
        if (bookId <= 0)
            return url.Content("~/images/placeholder-book.png") ?? "/images/placeholder-book.png";

        return url.Action("BookCover", "Client", new { id = bookId })
               ?? url.Content("~/images/placeholder-book.png")
               ?? "/images/placeholder-book.png";
    }
}
