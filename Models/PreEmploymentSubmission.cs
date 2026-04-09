using System.ComponentModel.DataAnnotations;

namespace QuizAPI.Models
{
    public class PreEmploymentSubmission
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(256)]
        public string QuizTitle { get; set; } = "Pre-Employment Quiz";

        [MaxLength(1024)]
        public string SourceQuizTitles { get; set; } = string.Empty;

        public int TotalQuestions { get; set; }

        public int CorrectCount { get; set; }

        public double ScorePercent { get; set; }

        public double PassingScorePercent { get; set; }

        public bool Passed { get; set; }

        public DateTime SubmittedUtc { get; set; } = DateTime.UtcNow;
    }
}
