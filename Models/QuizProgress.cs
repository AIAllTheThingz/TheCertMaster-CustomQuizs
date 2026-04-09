using System.ComponentModel.DataAnnotations;

namespace QuizAPI.Models
{
    public class QuizProgress
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string UserId { get; set; } = string.Empty;

        public AppUser? User { get; set; }

        [MaxLength(100)]
        public string SessionKey { get; set; } = string.Empty;

        public Guid? QuizId { get; set; }

        [MaxLength(256)]
        public string QuizTitle { get; set; } = string.Empty;

        [MaxLength(128)]
        public string QuizCategory { get; set; } = "Uncategorized";

        [MaxLength(32)]
        public string LaunchMode { get; set; } = "single";

        public string QuizDataJson { get; set; } = "{}";

        public string SelectionsJson { get; set; } = "{}";

        public int CurrentIndex { get; set; }

        public int? TimerRemainingSeconds { get; set; }

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
