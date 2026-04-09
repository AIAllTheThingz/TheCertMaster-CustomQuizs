namespace QuizAPI.DTO
{
    public class UpdateUserRequestDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
    }

    public class QuizUsageReportItemDto
    {
        public Guid? QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuizCategory { get; set; } = "Uncategorized";
        public int AttemptCount { get; set; }
        public double AverageScorePercent { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
    }

    public class RecentAttemptDto
    {
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string QuizTitle { get; set; } = string.Empty;
        public string QuizCategory { get; set; } = "Uncategorized";
        public double ScorePercent { get; set; }
        public int CorrectCount { get; set; }
        public int TotalQuestions { get; set; }
        public DateTime SubmittedUtc { get; set; }
    }

    public class PreEmploymentSubmissionDto
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string QuizTitle { get; set; } = string.Empty;
        public string SourceQuizTitles { get; set; } = string.Empty;
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public double ScorePercent { get; set; }
        public double PassingScorePercent { get; set; }
        public bool Passed { get; set; }
        public DateTime SubmittedUtc { get; set; }
    }

    public class QuestionEditorAnswerDto
    {
        public Guid Id { get; set; }
        public string AnswerText { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class QuestionEditorItemDto
    {
        public Guid QuestionId { get; set; }
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string QuestionImgKey { get; set; } = string.Empty;
        public List<QuestionEditorAnswerDto> Answers { get; set; } = new();
    }

    public class QuestionEditorPageDto
    {
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public List<QuestionEditorItemDto> Items { get; set; } = new();
    }

    public class QuestionEditorAnswerUpdateDto
    {
        public Guid Id { get; set; }
        public string AnswerText { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class QuestionEditorUpdateRequestDto
    {
        public string QuestionText { get; set; } = string.Empty;
        public string QuestionImgKey { get; set; } = string.Empty;
        public List<QuestionEditorAnswerUpdateDto> Answers { get; set; } = new();
    }

    public class QuizCreatorSourceQuizDto
    {
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
    }

    public class QuizCreatorSourceQuestionDto
    {
        public Guid QuestionId { get; set; }
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public List<QuestionEditorAnswerDto> Answers { get; set; } = new();
    }

    public class QuizCreatorSourceQuestionPageDto
    {
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public List<QuizCreatorSourceQuestionDto> Items { get; set; } = new();
    }

    public class QuizCreatorSelectedQuestionDto
    {
        public Guid QuestionId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
    }

    public class QuizCreatorCreateRequestDto
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Custom Created";
        public double PassThresholdPercent { get; set; } = 70;
        public List<Guid> QuestionIds { get; set; } = new();
    }

    public class QuizCreatorCreateResponseDto
    {
        public Guid QuizId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
    }
}
