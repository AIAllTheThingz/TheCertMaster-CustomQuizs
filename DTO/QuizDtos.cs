namespace QuizAPI.DTO
{
    public class RandomizedQuizDto
    {
        public Guid QuizId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Uncategorized";
        public double PassThresholdPercent { get; set; } = 70;
        public List<QuestionDto> Questions { get; set; } = new();
    }

    public class QuizSelectionGenerateRequestDto
    {
        public List<Guid> QuizIds { get; set; } = new();
        public int? QuestionCount { get; set; }
        public bool AllQuestions { get; set; }
        public string? Title { get; set; }
        public string? Difficulty { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public class QuestionDto
    {
        public Guid QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool AllowMultiple { get; set; }
        public string Difficulty { get; set; } = "Unspecified";
        public List<string> Tags { get; set; } = new();
        public List<AnswerDto> Answers { get; set; } = new();
        public List<ImageDto> Images { get; set; } = new();
    }

    public class AnswerDto
    {
        public Guid AnswerId { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class ImageDto
    {
        public Guid ImageId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class SaveQuizProgressDto
    {
        public string SessionKey { get; set; } = string.Empty;
        public Guid? QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuizCategory { get; set; } = "Uncategorized";
        public string LaunchMode { get; set; } = "single";
        public RandomizedQuizDto? Quiz { get; set; }
        public Dictionary<string, List<Guid>> Selections { get; set; } = new();
        public int CurrentIndex { get; set; }
        public int? TimerRemainingSeconds { get; set; }
    }

    public class QuizProgressDto
    {
        public string SessionKey { get; set; } = string.Empty;
        public Guid? QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuizCategory { get; set; } = "Uncategorized";
        public string LaunchMode { get; set; } = "single";
        public RandomizedQuizDto? Quiz { get; set; }
        public Dictionary<string, List<Guid>> Selections { get; set; } = new();
        public int CurrentIndex { get; set; }
        public int? TimerRemainingSeconds { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}
