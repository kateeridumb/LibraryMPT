namespace LibraryMPT.Models;

/// <summary>Строка результата для SqlQuery на панели администратора.</summary>
public sealed class AdminDashboardStatsSqlRow
{
    public int TotalUsers { get; set; }
    public int AdminCount { get; set; }
    public int LibrarianCount { get; set; }
    public int ReaderCount { get; set; }
}
