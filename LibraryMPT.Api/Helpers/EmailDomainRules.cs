namespace LibraryMPT.Api.Helpers;

/// <summary>Правила проверки домена email (2FA и др.).</summary>
public static class EmailDomainRules
{
    public static readonly string[] DefaultAllowedDomains =
    {
        "gmail.com",
        "yandex.ru",
        "ya.ru",
        "mail.ru",
        "bk.ru",
        "inbox.ru",
        "list.ru",
        "rambler.ru"
    };

    public static string NormalizeDomain(string value)
        => value.Trim().TrimStart('@').ToLowerInvariant();

    public static bool IsAllowedEmail(string? email, IReadOnlyList<string> allowedDomains)
    {
        if (string.IsNullOrWhiteSpace(email) || allowedDomains.Count == 0)
            return false;

        var atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return false;

        var domain = NormalizeDomain(email[(atIndex + 1)..]);
        return allowedDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
    }
}
