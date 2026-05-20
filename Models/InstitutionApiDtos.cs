namespace LibraryMPT.Models;

public sealed class InstitutionIndexResponse
{
    public bool HasFaculty { get; set; }
    public string? ErrorMessage { get; set; }
    public Faculty? Faculty { get; set; }
    public int? FacultyId { get; set; }
    public Subscription? ActiveSubscription { get; set; }
    public int TotalStudents { get; set; }
    public int TotalDownloads { get; set; }
    public int TotalReads { get; set; }
}

public sealed class InstitutionSubscriptionsResponse
{
    public bool HasFaculty { get; set; }
    public string? ErrorMessage { get; set; }
    public Faculty? Faculty { get; set; }
    public List<Subscription> Templates { get; set; } = new();
    public List<Subscription> MySubscriptions { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
}

public sealed class InstitutionStudentStatsResponse
{
    public bool HasFaculty { get; set; }
    public string? ErrorMessage { get; set; }
    public Faculty? Faculty { get; set; }
    public string? Search { get; set; }
    public string SortBy { get; set; } = "downloads";
    public string SortDir { get; set; } = "desc";
    public List<StudentStatsDto> Students { get; set; } = new();
}

public sealed class InstitutionBookStatsResponse
{
    public bool HasFaculty { get; set; }
    public string? ErrorMessage { get; set; }
    public Faculty? Faculty { get; set; }
    public string? Search { get; set; }
    public string SortBy { get; set; } = "downloads";
    public string SortDir { get; set; } = "desc";
    public List<BookStatisticsDto> BookStats { get; set; } = new();
}

public sealed class SelectSubscriptionRequest
{
    public int UserId { get; set; }
    public int SubscriptionId { get; set; }
    public int CategoryId { get; set; }
}

public sealed class InstitutionContractResponse
{
    public int? SubscriptionId { get; set; }
    public string? FacultyName { get; set; }
    public string? RepresentativeFullName { get; set; }
    public string? RepresentativeEmail { get; set; }
    public string? SubscriptionName { get; set; }
    public int StudentsCount { get; set; }
    public List<Category> Categories { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

