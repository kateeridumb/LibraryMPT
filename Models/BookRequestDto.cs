using System;

namespace LibraryMPT.Models
{
    public sealed class BookRequestDto
    {
        public int BookRequestID { get; set; }
        public int UserID { get; set; }
        public int BookID { get; set; }
        public string BookTitle { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; }

        public DateTime? DecidedAt { get; set; }
        public int? DecisionByUserID { get; set; }

        public string? RequestedByUsername { get; set; }
        public string? RequestedByEmail { get; set; }
    }
}

