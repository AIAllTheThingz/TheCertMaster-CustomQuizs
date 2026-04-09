using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;

namespace QuizAPI.Services
{
    public class QuizQueryService
    {
        private readonly QuizDbContext _db;
        private readonly Random _rng = new();

        public QuizQueryService(QuizDbContext db) => _db = db;

        public async Task<RandomizedQuizDto?> GetRandomizedAsync(Guid quizId)
        {
            var quiz = await _db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Images)
                .FirstOrDefaultAsync(q => q.Id == quizId && !q.IsArchived);

            if (quiz is null) return null;

            var qShuffled = quiz.Questions.OrderBy(_ => _rng.Next()).ToList();

            return new RandomizedQuizDto
            {
                QuizId = quiz.Id,
                Title = quiz.Title,
                Category = string.IsNullOrWhiteSpace(quiz.Category) ? "Uncategorized" : quiz.Category,
                PassThresholdPercent = quiz.PassThresholdPercent <= 0 ? 70 : quiz.PassThresholdPercent,
                Questions = qShuffled.Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    AllowMultiple = q.AllowMultiple,
                    Difficulty = q.Difficulty,
                    Tags = ParseTags(q.Tags),
                    Answers = q.Answers
                        .OrderBy(_ => _rng.Next())
                        .Select(a => new AnswerDto { AnswerId = a.Id, Text = a.Text })
                        .ToList(),
                    Images = q.Images
                        .Select(i => new ImageDto
                        {
                            ImageId = i.Id,
                            FileName = i.FileName,
                            ContentType = i.ContentType,
                            Url = i.Url
                        })
                        .ToList()
                }).ToList()
            };
        }

        public async Task<RandomizedQuizDto?> GetRandomizedSelectionAsync(List<Guid> quizIds, int? questionCount, bool allQuestions, string? title, string? difficulty, List<string>? tags)
        {
            var selectedIds = (quizIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (selectedIds.Count == 0)
            {
                return null;
            }

            var quizzes = await _db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Images)
                .Where(q => selectedIds.Contains(q.Id) && !q.IsArchived)
                .ToListAsync();

            if (quizzes.Count != selectedIds.Count)
            {
                return null;
            }

            var normalizedDifficulty = NormalizeDifficulty(difficulty);
            var requestedTags = (tags ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var filteredQuestionsByQuiz = quizzes.ToDictionary(
                quiz => quiz.Id,
                quiz => quiz.Questions
                    .Where(q => string.IsNullOrWhiteSpace(normalizedDifficulty) || string.Equals(q.Difficulty, normalizedDifficulty, StringComparison.OrdinalIgnoreCase))
                    .Where(q => requestedTags.Count == 0 || ParseTags(q.Tags).Any(tag => requestedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                    .OrderBy(_ => _rng.Next())
                    .ToList());

            if (filteredQuestionsByQuiz.Values.All(list => list.Count == 0))
            {
                return null;
            }

            List<Question> picked;
            if (allQuestions)
            {
                picked = filteredQuestionsByQuiz.Values
                    .SelectMany(list => list)
                    .OrderBy(_ => _rng.Next())
                    .ToList();
            }
            else
            {
                var perQuizCount = questionCount ?? 20;
                if (perQuizCount <= 0)
                {
                    return null;
                }

                if (filteredQuestionsByQuiz.Values.Any(list => list.Count < perQuizCount))
                {
                    return null;
                }

                picked = filteredQuestionsByQuiz.Values
                    .SelectMany(list => list.Take(perQuizCount))
                    .OrderBy(_ => _rng.Next())
                    .ToList();
            }

            return new RandomizedQuizDto
            {
                QuizId = Guid.Empty,
                Title = string.IsNullOrWhiteSpace(title) ? "Configured Quiz" : title.Trim(),
                Category = "Mixed",
                PassThresholdPercent = Math.Round(quizzes
                    .Select(q => q.PassThresholdPercent <= 0 ? 70 : q.PassThresholdPercent)
                    .DefaultIfEmpty(70)
                    .Average(), 2),
                Questions = picked.Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    AllowMultiple = q.AllowMultiple,
                    Difficulty = q.Difficulty,
                    Tags = ParseTags(q.Tags),
                    Answers = q.Answers
                        .OrderBy(_ => _rng.Next())
                        .Select(a => new AnswerDto { AnswerId = a.Id, Text = a.Text })
                        .ToList(),
                    Images = q.Images
                        .Select(i => new ImageDto
                        {
                            ImageId = i.Id,
                            FileName = i.FileName,
                            ContentType = i.ContentType,
                            Url = i.Url
                        })
                        .ToList()
                }).ToList()
            };
        }

        private static List<string> ParseTags(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? NormalizeDifficulty(string? difficulty)
        {
            var value = (difficulty ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase)
                ? null
                : value;
        }
    }
}
