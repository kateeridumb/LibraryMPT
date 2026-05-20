using LibraryMPT.Helpers;
using Xunit;

namespace LibraryMPT.Tests;

public class BookFileFormatHelperTests
{
    [Theory]
    [InlineData(@"C:\books\doc.pdf", "pdf")]
    [InlineData("/storage/file.epub", "epub")]
    [InlineData("novel.fb2", "fb2")]
    [InlineData("readme.txt", "text")]
    [InlineData("bundle.fb2.zip", "fb2")]
    [InlineData(@"X:\path\misc.bin", "unknown")]
    public void GetReaderFileType_MapsExtensionAndFb2Zip(string path, string expected)
    {
        Assert.Equal(expected, BookFileFormatHelper.GetReaderFileType(path));
    }

    [Fact]
    public void GetReaderFileType_ReturnsUnknownForNullOrWhitespace()
    {
        Assert.Equal("unknown", BookFileFormatHelper.GetReaderFileType(null));
        Assert.Equal("unknown", BookFileFormatHelper.GetReaderFileType("   "));
    }

    [Theory]
    [InlineData(@"book.pdf", "application/pdf")]
    [InlineData("x.epub", "application/epub+zip")]
    [InlineData("f.fb2", "application/x-fictionbook+xml")]
    [InlineData("notes.txt", "text/plain; charset=utf-8")]
    [InlineData("archive.fb2.zip", "application/zip")]
    [InlineData("unknown.xyz", "application/octet-stream")]
    public void GetDownloadContentType_MapsKnownExtensions(string path, string expected)
    {
        Assert.Equal(expected, BookFileFormatHelper.GetDownloadContentType(path));
    }

    [Fact]
    public void BuildDownloadFileName_ReplacesInvalidFileNameCharacters()
    {
        var name = BookFileFormatHelper.BuildDownloadFileName("A:B<C>|?", @"C:\files\a.pdf");
        Assert.Equal("A_B_C___.pdf", name);
    }

    [Fact]
    public void BuildDownloadFileName_PreservesFb2ZipSuffix()
    {
        var name = BookFileFormatHelper.BuildDownloadFileName("Книга", @"D:\x\title.fb2.zip");
        Assert.EndsWith(".fb2.zip", name, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Книга.", name, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDownloadFileName_FallsBackWhenTitleBlank()
    {
        var name = BookFileFormatHelper.BuildDownloadFileName("   ", "/data/file.docx");
        Assert.Equal("book.docx", name);
    }
}
