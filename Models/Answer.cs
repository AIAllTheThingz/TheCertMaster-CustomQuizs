namespace QuizAPI.Models
{
    public class Answer
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid QuestionId { get; set; }

        public string Text { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
        public int OrderIndex { get; set; }

        public Question? Question { get; set; }
    }
}
