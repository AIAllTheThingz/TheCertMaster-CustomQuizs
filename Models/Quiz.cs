using System.ComponentModel.DataAnnotations;

namespace QuizAPI.Models
{
    public class Quiz
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(256)]
        public string Title { get; set; } = "Untitled Quiz";

        [MaxLength(128)]
        public string? Category { get; set; }

        public bool IsArchived { get; set; }

        public double PassThresholdPercent { get; set; } = 70;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public List<Question> Questions { get; set; } = new();
    }
}
