using LibraryMPT.Api.Helpers;
using Xunit;

namespace LibraryMPT.Tests;

public class PageHelperTests
{
    [Fact]
    public void ParsePageNumber_WithValidNumber_ReturnsNumber()
    {
        Assert.Equal(5, PageHelper.ParsePageNumber("5"));
        Assert.Equal(42, PageHelper.ParsePageNumber("42"));
    }

    [Fact]
    public void ParsePageNumber_WithTextContainingDigits_ReturnsFirstNumber()
    {
        Assert.Equal(10, PageHelper.ParsePageNumber("page 10"));
        Assert.Equal(3, PageHelper.ParsePageNumber("p.3"));
    }

    [Fact]
    public void ParsePageNumber_WithNullOrEmpty_ReturnsNull()
    {
        Assert.Null(PageHelper.ParsePageNumber(null));
        Assert.Null(PageHelper.ParsePageNumber(""));
        Assert.Null(PageHelper.ParsePageNumber("   "));
    }

    [Fact]
    public void ParsePageNumber_WithNoDigits_ReturnsNull()
    {
        Assert.Null(PageHelper.ParsePageNumber("abc"));
        Assert.Null(PageHelper.ParsePageNumber("-"));
    }

    [Fact]
    public void ParsePageNumber_WithZero_ReturnsZero()
    {
        Assert.Equal(0, PageHelper.ParsePageNumber("0"));
    }

    [Fact]
    public void ParsePageNumber_WithDigitsAtStart_ReturnsLeadingNumber()
    {
        Assert.Equal(12, PageHelper.ParsePageNumber("12 из 34"));
        Assert.Equal(7, PageHelper.ParsePageNumber("7abc"));
    }
}
