using System.Security.Claims;
using LibraryMPT.Api.Extensions;
using Xunit;

namespace LibraryMPT.Tests;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetUserId_WithValidId_ReturnsId()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "42") };
        var identity = new ClaimsIdentity(claims);
        var user = new ClaimsPrincipal(identity);

        var result = user.GetUserId();

        Assert.Equal(42, result);
    }

    [Fact]
    public void GetUserId_WithInvalidValue_ReturnsZero()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "not-a-number") };
        var identity = new ClaimsIdentity(claims);
        var user = new ClaimsPrincipal(identity);

        var result = user.GetUserId();

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetUserId_WithNoClaims_ReturnsZero()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        var result = user.GetUserId();

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetUserId_WithZeroClaim_ReturnsZero()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "0") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        var result = user.GetUserId();

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetUserId_WithMultipleIdentities_ReturnsFirstMatchingClaim()
    {
        var first = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "100") }, "schemeA");
        var second = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "200") }, "schemeB");
        var user = new ClaimsPrincipal();
        user.AddIdentity(first);
        user.AddIdentity(second);

        var result = user.GetUserId();

        Assert.Equal(100, result);
    }
}
