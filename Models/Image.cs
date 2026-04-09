namespace QuizAPI.Models
{
    public class Image
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid QuestionId { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;

        // Store a link (saved under wwwroot/uploads) — simple & web-friendly
        public string Url { get; set; } = string.Empty;
    }
}
