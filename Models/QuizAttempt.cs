using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace QuizAPI.Models
{
    public class QuizAttempt
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string UserId { get; set; } = string.Empty;

        public AppUser? User { get; set; }

        public Guid? QuizId { get; set; }

        [MaxLength(256)]
        public string QuizTitle { get; set; } = string.Empty;

        [MaxLength(128)]
        public string QuizCategory { get; set; } = "Uncategorized";

        public int TotalQuestions { get; set; }

        public int CorrectCount { get; set; }

        public double ScorePercent { get; set; }

        public DateTime SubmittedUtc { get; set; } = DateTime.UtcNow;
    }
}
