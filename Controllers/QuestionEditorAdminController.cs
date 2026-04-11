using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin/question-editor")]
    [Authorize(Roles = "Admin")]
    public class QuestionEditorAdminController : ControllerBase
    {
        private readonly QuizDbContext _db;

        public QuestionEditorAdminController(QuizDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetQuestions([FromQuery] Guid quizId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null)
        {
            if (quizId == Guid.Empty)
                return BadRequest("QuizId is required.");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var normalizedSearch = (search ?? string.Empty).Trim();

            var quiz = await _db.Quizzes
                .AsNoTracking()
                .Where(q => q.Id == quizId)
                .Select(q => new { q.Id, q.Title })
                .FirstOrDefaultAsync();

            if (quiz is null)
                return NotFound("Quiz not found.");

            var questionQuery = _db.Questions
                .AsNoTracking()
                .Where(q => q.QuizId == quizId);

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                questionQuery = questionQuery.Where(q =>
                    q.Text.Contains(normalizedSearch) ||
                    q.Answers.Any(a => a.Text.Contains(normalizedSearch)) ||
                    q.Images.Any(i => i.FileName.Contains(normalizedSearch)));
            }

            var totalItems = await questionQuery.CountAsync();

            var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
            if (totalPages > 0 && page > totalPages)
            {
                page = totalPages;
            }

            var questions = await questionQuery
                .Include(q => q.Answers.OrderBy(a => a.OrderIndex).ThenBy(a => a.Text))
                .Include(q => q.Images)
                .OrderBy(q => q.OrderIndex)
                .ThenBy(q => q.Text)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(q => new QuestionEditorItemDto
                {
                    QuestionId = q.Id,
                    QuizId = q.QuizId,
                    QuizTitle = quiz.Title,
                    QuestionText = q.Text,
                    QuestionImgKey = q.Images
                        .OrderBy(i => i.FileName)
                        .Select(i => i.FileName)
                        .FirstOrDefault() ?? string.Empty,
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

            return Ok(new QuestionEditorPageDto
            {
                QuizId = quiz.Id,
                QuizTitle = quiz.Title,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages,
                SearchTerm = normalizedSearch,
                Items = questions
            });
        }

        [HttpPut("{questionId:guid}")]
        public async Task<IActionResult> UpdateQuestion(Guid questionId, [FromBody] QuestionEditorUpdateRequestDto request)
        {
            if (request is null)
                return BadRequest("Missing request body.");

            var question = await _db.Questions
                .Include(q => q.Answers)
                .Include(q => q.Images)
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question is null)
                return NotFound("Question not found.");

            var normalizedQuestionText = (request.QuestionText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedQuestionText))
                return BadRequest("QuestionText is required.");

            if (request.Answers is null || request.Answers.Count == 0)
                return BadRequest("At least one answer is required.");

            var duplicateAnswerIds = request.Answers
                .GroupBy(a => a.Id)
                .Any(g => g.Key == Guid.Empty || g.Count() > 1);

            if (duplicateAnswerIds)
                return BadRequest("Answer IDs must be unique and non-empty.");

            var existingAnswers = question.Answers.ToDictionary(a => a.Id);
            if (request.Answers.Any(a => !existingAnswers.ContainsKey(a.Id)))
                return BadRequest("One or more answers do not belong to this question.");

            question.Text = normalizedQuestionText;

            for (var i = 0; i < request.Answers.Count; i++)
            {
                var incoming = request.Answers[i];
                var normalizedAnswerText = (incoming.AnswerText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedAnswerText))
                    return BadRequest("AnswerText is required for every answer.");

                var answer = existingAnswers[incoming.Id];
                answer.Text = normalizedAnswerText;
                answer.IsCorrect = incoming.IsCorrect;
                answer.OrderIndex = i;
            }

            var normalizedImageKey = Path.GetFileName((request.QuestionImgKey ?? string.Empty).Trim());
            if (!string.Equals(normalizedImageKey, (request.QuestionImgKey ?? string.Empty).Trim(), StringComparison.Ordinal))
                return BadRequest("QuestionImgKey must be a file name only.");

            SyncQuestionImage(question, normalizedImageKey);

            await _db.SaveChangesAsync();

            var firstImageKey = question.Images
                .OrderBy(i => i.FileName)
                .Select(i => i.FileName)
                .FirstOrDefault() ?? string.Empty;

            return Ok(new QuestionEditorItemDto
            {
                QuestionId = question.Id,
                QuizId = question.QuizId,
                QuizTitle = string.Empty,
                QuestionText = question.Text,
                QuestionImgKey = firstImageKey,
                Answers = question.Answers
                    .OrderBy(a => a.OrderIndex)
                    .ThenBy(a => a.Text)
                    .Select(a => new QuestionEditorAnswerDto
                    {
                        Id = a.Id,
                        AnswerText = a.Text,
                        IsCorrect = a.IsCorrect
                    })
                    .ToList()
            });
        }

        private static void SyncQuestionImage(Question question, string normalizedImageKey)
        {
            var existingImage = question.Images
                .OrderBy(i => i.FileName)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(normalizedImageKey))
            {
                if (existingImage is not null)
                {
                    question.Images.Remove(existingImage);
                }

                return;
            }

            if (existingImage is null)
            {
                question.Images.Add(new Image
                {
                    QuestionId = question.Id,
                    FileName = normalizedImageKey,
                    ContentType = GetContentTypeFromExtension(Path.GetExtension(normalizedImageKey)),
                    Url = "/uploads/images/" + normalizedImageKey
                });
                return;
            }

            existingImage.FileName = normalizedImageKey;
            existingImage.ContentType = GetContentTypeFromExtension(Path.GetExtension(normalizedImageKey));
            existingImage.Url = ReplaceUrlFileName(existingImage.Url, normalizedImageKey);
        }

        private static string ReplaceUrlFileName(string? currentUrl, string fileName)
        {
            if (string.IsNullOrWhiteSpace(currentUrl))
                return "/uploads/images/" + fileName;

            var lastSlash = currentUrl.LastIndexOf('/');
            return lastSlash >= 0
                ? currentUrl[..(lastSlash + 1)] + fileName
                : "/uploads/images/" + fileName;
        }

        private static string GetContentTypeFromExtension(string? extension)
        {
            return (extension ?? string.Empty).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }
    }
}
