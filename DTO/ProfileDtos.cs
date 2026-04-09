namespace QuizAPI.DTO
{
    public class UserProfileDto
    {
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int TotalAttempts { get; set; }
        public double AverageScorePercent { get; set; }
        public int BestScorePercent { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalPages { get; set; }
        public string Sort { get; set; } = "submitted_desc";
        public List<UserQuizAttemptDto> Attempts { get; set; } = new();
    }

    public class UpdateProfileRequestDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class ChangePasswordRequestDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UserQuizAttemptDto
    {
        public Guid AttemptId { get; set; }
        public Guid? QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public string QuizCategory { get; set; } = "Uncategorized";
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public double ScorePercent { get; set; }
        public DateTime SubmittedUtc { get; set; }
    }
}
