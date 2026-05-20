using LibraryMPT.Api.Helpers;
using Xunit;

namespace LibraryMPT.Tests;

public class EmailDomainRulesTests
{
    private static readonly string[] SampleWhitelist = ["gmail.com", "mail.ru"];

    [Theory]
    [InlineData("  GMAIL.COM  ", "gmail.com")]
    [InlineData("@Yandex.Ru", "yandex.ru")]
    [InlineData(" Mail.RU\t", "mail.ru")]
    public void NormalizeDomain_TrimsAtSymbolAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, EmailDomainRules.NormalizeDomain(input));
    }

    [Fact]
    public void IsAllowedEmail_AllowsAddressWhenDomainMatchesWhitelist_CaseInsensitive()
    {
        Assert.True(EmailDomainRules.IsAllowedEmail("User@GMAIL.COM", SampleWhitelist));
        Assert.True(EmailDomainRules.IsAllowedEmail("a@mail.ru", SampleWhitelist));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@onlydomain.com")]
    [InlineData("user@")]
    [InlineData("user@@bad.com")]
    public void IsAllowedEmail_RejectsMalformedOrMissingDomain(string email)
    {
        Assert.False(EmailDomainRules.IsAllowedEmail(email, SampleWhitelist));
    }

    [Fact]
    public void IsAllowedEmail_RejectsWhenDomainNotInWhitelist()
    {
        Assert.False(EmailDomainRules.IsAllowedEmail("user@vk.com", SampleWhitelist));
    }

    [Fact]
    public void IsAllowedEmail_RejectsNullOrEmptyWhitelist()
    {
        Assert.False(EmailDomainRules.IsAllowedEmail("u@gmail.com", Array.Empty<string>()));
        Assert.False(EmailDomainRules.IsAllowedEmail("u@gmail.com", []));
    }

    [Fact]
    public void IsAllowedEmail_RejectsNullEmail()
    {
        Assert.False(EmailDomainRules.IsAllowedEmail(null, SampleWhitelist));
    }

    [Fact]
    public void IsAllowedEmail_UsesLastAtSignForDomainPart()
    {
        Assert.True(EmailDomainRules.IsAllowedEmail("a@b@gmail.com", SampleWhitelist));
        Assert.False(EmailDomainRules.IsAllowedEmail("a@b@company.org", SampleWhitelist));
    }
}
