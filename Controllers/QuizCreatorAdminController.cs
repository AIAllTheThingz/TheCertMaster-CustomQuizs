using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin/quiz-creator")]
    [Authorize(Roles = "Admin")]
    public class QuizCreatorAdminController : ControllerBase
    {
        private readonly QuizDbContext _db;
        private const string BasicQuizPattern = "%Basic%";

        public QuizCreatorAdminController(QuizDbContext db)
        {
            _db = db;
        }

        [HttpGet("source-quizzes")]
        public async Task<IActionResult> GetSourceQuizzes()
        {
            var quizzes = await _db.Quizzes
                .AsNoTracking()
                .Where(q => !q.IsArchived)
                .Where(q => EF.Functions.Like(q.Title, BasicQuizPattern))
                .OrderBy(q => q.Title)
                .Select(q => new QuizCreatorSourceQuizDto
                {
                    QuizId = q.Id,
                    QuizTitle = q.Title,
                    QuestionCount = q.Questions.Count
                })
                .ToListAsync();

            return Ok(quizzes);
        }

        [HttpGet("source-questions")]
        public async Task<IActionResult> GetSourceQuestions([FromQuery] Guid quizId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null)
        {
            if (quizId == Guid.Empty)
                return BadRequest("QuizId is required.");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var normalizedSearch = (search ?? string.Empty).Trim();

            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId && !q.IsArchived)
                .Where(q => EF.Functions.Like(q.Title, BasicQuizPattern))
                .Select(q => new { q.Id, q.Title })
                .FirstOrDefaultAsync();

            if (quiz is null)
                return NotFound("Source quiz not found.");

            var questionQuery = _db.Questions
                .AsNoTracking()
                .Where(q => q.QuizId == quizId);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                questionQuery = questionQuery.Where(q =>
                    q.Text.Contains(normalizedSearch) ||
                    q.Answers.Any(a => a.Text.Contains(normalizedSearch)));
            }

            var totalItems = await questionQuery.CountAsync();

            var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            var items = await questionQuery
                .Include(q => q.Answers)
                .OrderBy(q => q.OrderIndex)
                .ThenBy(q => q.Text)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(q => new QuizCreatorSourceQuestionDto
                {
                    QuestionId = q.Id,
                    QuizId = q.QuizId,
                    QuizTitle = quiz.Title,
                    QuestionText = q.Text,
                    Answers = q.Answers
                        .OrderBy(a => a.OrderIndex)
                        .ThenBy(a => a.Text)
                        .Select(a => new QuestionEditorAnswerDto
                        {
                            Id = a.Id,
                            AnswerText = a.Text,
                            IsCorrect = a.IsCorrect
                        })
                        .ToList()
                })
                .ToListAsync();

            return Ok(new QuizCreatorSourceQuestionPageDto
            {
                QuizId = quiz.Id,
                QuizTitle = quiz.Title,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages,
                SearchTerm = normalizedSearch,
                Items = items
            });
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateQuiz([FromBody] QuizCreatorCreateRequestDto request)
        {
            if (request is null)
                return BadRequest("Missing request body.");

            var title = (request.Title ?? string.Empty).Trim();
            var category = string.IsNullOrWhiteSpace(request.Category) ? "Custom Created" : request.Category.Trim();
            var questionIds = (request.QuestionIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (string.IsNullOrWhiteSpace(title))
                return BadRequest("Title is required.");

            if (questionIds.Count == 0)
                return BadRequest("Select at least one question.");

            var sourceQuestions = await _db.Questions
                .AsNoTracking()
                .Where(q => questionIds.Contains(q.Id))
                .Include(q => q.Answers)
                .Include(q => q.Images)
                .OrderBy(q => q.OrderIndex)
                .ThenBy(q => q.Text)
                .ToListAsync();

            if (sourceQuestions.Count != questionIds.Count)
                return BadRequest("One or more selected questions could not be found.");

            var sourceById = sourceQuestions.ToDictionary(q => q.Id);

            var quiz = new Quiz
            {
                Title = title,
                Category = category,
                PassThresholdPercent = request.PassThresholdPercent <= 0 ? 70 : request.PassThresholdPercent,
                Questions = questionIds.Select((id, index) =>
                {
                    var source = sourceById[id];
                    return new Question
                    {
                        Text = source.Text,
                        OrderIndex = index,
                        AllowMultiple = source.AllowMultiple,
                        Difficulty = source.Difficulty,
                        Tags = source.Tags,
                        Answers = source.Answers
                            .OrderBy(a => a.OrderIndex)
                            .ThenBy(a => a.Text)
                            .Select((a, answerIndex) => new Answer
                            {
                                Text = a.Text,
                                IsCorrect = a.IsCorrect,
                                OrderIndex = answerIndex
                            })
                            .ToList(),
                        Images = source.Images
                            .OrderBy(i => i.FileName)
                            .Select(i => new Image
                            {
                                FileName = i.FileName,
                                ContentType = i.ContentType,
                                Url = i.Url
                            })
                            .ToList()
                    };
                }).ToList()
            };

            _db.Quizzes.Add(quiz);
            await _db.SaveChangesAsync();

            return Ok(new QuizCreatorCreateResponseDto
            {
                QuizId = quiz.Id,
                Title = quiz.Title,
                Category = string.IsNullOrWhiteSpace(quiz.Category) ? "Uncategorized" : quiz.Category,
                QuestionCount = quiz.Questions.Count
            });
        }
    }
}
