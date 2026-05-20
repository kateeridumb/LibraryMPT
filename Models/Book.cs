namespace LibraryMPT.Models
{
    public class Book
    {
        public int BookID { get; set; }
        public string Title { get; set; }
        public string? ImagePath { get; set; }
        public string? Description { get; set; }
        public int? PublishYear { get; set; }
        public int AuthorID { get; set; }
        public int? PublisherID { get; set; }

        public string? FilePath { get; set; }
        public bool RequiresSubscription { get; set; }
        public List<int> CategoryIds { get; set; } = new();
        public List<Category>? Categories { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Category? Category => Categories?.FirstOrDefault();

        public Author? Author { get; set; }
        public Publisher? Publisher { get; set; }
    }
}