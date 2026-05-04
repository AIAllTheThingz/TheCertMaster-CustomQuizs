namespace QuizAPI.DTO
{
    public class QuizAttemptSubmitDto
    {
        public List<QuestionAttemptDto> Answers { get; set; } = new();
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? QuizTitle { get; set; }
        public string? QuizCategory { get; set; }
        public string? SessionKey { get; set; }
        public string? AccessCode { get; set; }
    }

    public class QuestionAttemptDto
    {
        public Guid QuestionId { get; set; }
        public List<Guid> SelectedAnswerIds { get; set; } = new();
    }

    public class QuizAttemptResultDto
    {
        public Guid QuizId { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public double ScorePercent { get; set; }
        public double PassThresholdPercent { get; set; }
        public bool Passed { get; set; }
        public List<QuestionResultDto> Questions { get; set; } = new();
    }

    public class QuestionResultDto
    {
        public Guid QuestionId { get; set; }
        public bool IsCorrect { get; set; }
        public List<Guid> SelectedAnswerIds { get; set; } = new();
        public List<Guid> CorrectAnswerIds { get; set; } = new();
    }
}
