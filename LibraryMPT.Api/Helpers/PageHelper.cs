using System.Text.RegularExpressions;

namespace LibraryMPT.Api.Helpers;

public static class PageHelper
{
    public static int? ParsePageNumber(string? rawPage)
    {
        if (string.IsNullOrWhiteSpace(rawPage))
            return null;

        if (int.TryParse(rawPage, out var direct))
            return direct;

        var match = Regex.Match(rawPage, @"\d+");
        return match.Success && int.TryParse(match.Value, out var parsed) ? parsed : null;
    }
}
