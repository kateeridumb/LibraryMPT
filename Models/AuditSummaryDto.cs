namespace LibraryMPT.Models
{
    public class AuditSummaryDto
    {
        public string? TableName { get; set; }
        public string? ActionType { get; set; }
        public int EventsCount { get; set; }
    }
}


