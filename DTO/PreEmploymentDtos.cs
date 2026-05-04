namespace QuizAPI.DTO
{
    public class PreEmploymentGenerateRequestDto
    {
        public List<string> Categories { get; set; } = new();
        public Guid? QuizId { get; set; }
        public List<Guid> QuizIds { get; set; } = new();
        public int? QuestionCount { get; set; }
        public string? Title { get; set; }
        public string? AccessCode { get; set; }
    }

    public class PreEmploymentQuizDto
    {
        public string Title { get; set; } = "Pre-Employment Quiz";
        public int QuestionCount { get; set; }
        public List<string> Categories { get; set; } = new();
        public Guid? QuizId { get; set; }
        public List<Guid> QuizIds { get; set; } = new();
        public string? SourceQuizTitle { get; set; }
        public List<string> SourceQuizTitles { get; set; } = new();
        public List<QuestionDto> Questions { get; set; } = new();
    }

    public class PreEmploymentConfigDto
    {
        public string Title { get; set; } = "Pre-Employment Quiz";
        public Guid? QuizId { get; set; }
        public string? QuizTitle { get; set; }
        public List<Guid> QuizIds { get; set; } = new();
        public List<string> QuizTitles { get; set; } = new();
        public int QuestionCount { get; set; } = 20;
        public int MaxQuestionCount { get; set; } = 100;
        public int TimeLimitMinutes { get; set; }
        public double PassingScorePercent { get; set; } = 70;
        public bool RandomizeAnswers { get; set; } = true;
        public bool ShowCorrectAnswersAtEnd { get; set; }
        public bool AccessCodeRequired { get; set; }
        public string? AccessCode { get; set; }
    }
}
