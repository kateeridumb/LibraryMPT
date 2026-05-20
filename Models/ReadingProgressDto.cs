namespace LibraryMPT.Models;

public sealed class BookCategoryRow
{
    public int BookID { get; set; }
    public int CategoryID { get; set; }
}

public sealed class ReadingProgressDto
{
    public int BookID { get; set; }
    public int? LastPage { get; set; }
    public string? LastPosition { get; set; }
    public int? Percent { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SaveProgressRequest
{
    public int BookId { get; set; }
    public int? Page { get; set; }
    public string? Position { get; set; }
    public int? Percent { get; set; }
}

public sealed class BookWithProgressDto
{
    public Book Book { get; set; } = null!;
    public int? LastPage { get; set; }
    public string? LastPosition { get; set; }
    public int? Percent { get; set; }
    public DateTime UpdatedAt { get; set; }
}
