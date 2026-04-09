using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;
using QuizAPI.Services;
using System.Security.Claims;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuizController : ControllerBase
    {
        private readonly QuizQueryService _query;
        private readonly QuizDbContext _db;

        public QuizController(QuizQueryService query, QuizDbContext db)
        {
            _query = query;
            _db = db;
        }

        // Lightweight quiz listing for browser pickers / admin UIs
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] bool includeArchived = false)
        {
            var query = _db.Quizzes
                .AsNoTracking();

            if (!(includeArchived && User.IsInRole("Admin")))
            {
                query = query.Where(q => !q.IsArchived);
            }

            if (!string.IsNullOrWhiteSpace(category) &&
                !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedCategory = NormalizeCategory(category);

                query = query.Where(q =>
                    (q.Category ?? "Uncategorized") == normalizedCategory);
            }

            var quizzes = await query
                .OrderByDescending(q => q.CreatedUtc)
                .Select(q => new
                {
                    q.Id,
                    q.Title,
                    Category = string.IsNullOrWhiteSpace(q.Category) ? "Uncategorized" : q.Category,
                    q.IsArchived,
                    q.PassThresholdPercent,
                    QuestionCount = q.Questions.Count,
                    q.CreatedUtc
                })
                .ToListAsync();

            return Ok(quizzes);
        }

        // Returns randomized questions & answers on each call
        [HttpGet("{quizId:guid}/random")]
        public async Task<IActionResult> GetRandomized(Guid quizId)
        {
            var dto = await _query.GetRandomizedAsync(quizId);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpPost("generate-selection")]
        public async Task<IActionResult> GenerateSelection([FromBody] QuizSelectionGenerateRequestDto req)
        {
            if (req is null)
                return BadRequest("Missing request body.");

            var quizIds = (req.QuizIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (quizIds.Count == 0)
                return BadRequest("At least one quiz must be selected.");

            if (!req.AllQuestions)
            {
                if (!req.QuestionCount.HasValue || req.QuestionCount.Value <= 0)
                    return BadRequest("QuestionCount must be greater than 0 when AllQuestions is false.");
            }

            var dto = await _query.GetRandomizedSelectionAsync(quizIds, req.QuestionCount, req.AllQuestions, req.Title, req.Difficulty, req.Tags);
            return dto is null
                ? BadRequest("Could not generate a quiz from the selected quizzes and question settings.")
                : Ok(dto);
        }

        // Submits an attempt and returns a score. Correct answers never leave the server.
        [HttpPost("{quizId:guid}/submit")]
        public async Task<IActionResult> Submit(Guid quizId, [FromBody] QuizAttemptSubmitDto attempt)
        {
            if (attempt is null)
                return BadRequest("Missing request body.");

            var quiz = await _db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == quizId && !q.IsArchived);

            if (quiz is null)
                return NotFound();

            // Index incoming answers by QuestionId for quick lookup
            var incoming = (attempt.Answers ?? new List<QuestionAttemptDto>())
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new QuizAttemptResultDto
            {
                QuizId = quizId,
                TotalQuestions = quiz.Questions.Count,
                PassThresholdPercent = quiz.PassThresholdPercent <= 0 ? 70 : quiz.PassThresholdPercent
            };

            var correct = 0;

            foreach (var q in quiz.Questions)
            {
                incoming.TryGetValue(q.Id, out var a);
                var selected = (a?.SelectedAnswerIds ?? new List<Guid>())
                    .Distinct()
                    .ToList();

                // Determine the correct set from DB
                var correctIds = q.Answers
                    .Where(x => x.IsCorrect)
                    .Select(x => x.Id)
                    .OrderBy(x => x)
                    .ToList();

                var selectedIds = selected
                    .OrderBy(x => x)
                    .ToList();

                bool isCorrect;

                if (!q.AllowMultiple)
                {
                    // single-answer question: exactly one selected, and it must be correct
                    isCorrect = selectedIds.Count == 1 && correctIds.Count == 1 && selectedIds[0] == correctIds[0];
                }
                else
                {
                    // multi-answer question: exact set match (order independent)
                    isCorrect = selectedIds.SequenceEqual(correctIds);
                }

                if (isCorrect)
                    correct++;

                result.Questions.Add(new QuestionResultDto
                {
                    QuestionId = q.Id,
                    IsCorrect = isCorrect,
                    SelectedAnswerIds = selectedIds,
                    CorrectAnswerIds = (User.Identity?.IsAuthenticated ?? false) ? correctIds : new List<Guid>()
                });
            }

            result.CorrectCount = correct;
            result.ScorePercent = result.TotalQuestions == 0
                ? 0
                : Math.Round((double)result.CorrectCount / result.TotalQuestions * 100.0, 2);
            result.Passed = result.ScorePercent >= result.PassThresholdPercent;

            await SaveTrackedAttemptIfAuthenticatedAsync(quizId, quiz.Title, quiz.Category, result);
            await ClearTrackedProgressIfAuthenticatedAsync(attempt.SessionKey);

            return Ok(result);
        }

        [HttpPost("submit-selection")]
        public async Task<IActionResult> SubmitSelection([FromBody] QuizAttemptSubmitDto attempt)
        {
            if (attempt is null)
                return BadRequest("Missing request body.");

            var incoming = (attempt.Answers ?? new List<QuestionAttemptDto>())
                .GroupBy(a => a.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

            if (incoming.Count == 0)
                return BadRequest("No answers submitted.");

            var questionIds = incoming.Keys.ToList();

            var questions = await _db.Questions
                .AsNoTracking()
                .Include(q => q.Answers)
                .Include(q => q.Quiz)
                .Where(q => questionIds.Contains(q.Id))
                .ToListAsync();

            if (questions.Count != questionIds.Count)
                return BadRequest("One or more QuestionId values were not found.");

            var result = new QuizAttemptResultDto
            {
                QuizId = Guid.Empty,
                TotalQuestions = questions.Count,
                PassThresholdPercent = questions
                    .Where(q => q.Quiz != null)
                    .Select(q => q.Quiz!)
                    .GroupBy(q => q.Id)
                    .Select(g => g.First().PassThresholdPercent <= 0 ? 70 : g.First().PassThresholdPercent)
                    .DefaultIfEmpty(70)
                    .Average()
            };

            var correct = 0;

            foreach (var q in questions)
            {
                incoming.TryGetValue(q.Id, out var a);
                var selected = (a?.SelectedAnswerIds ?? new List<Guid>())
                    .Distinct()
                    .ToList();

                var correctIds = q.Answers
                    .Where(x => x.IsCorrect)
                    .Select(x => x.Id)
                    .OrderBy(x => x)
                    .ToList();

                var selectedIds = selected
                    .OrderBy(x => x)
                    .ToList();

                bool isCorrect;

                if (!q.AllowMultiple)
                {
                    isCorrect = selectedIds.Count == 1 && correctIds.Count == 1 && selectedIds[0] == correctIds[0];
                }
                else
                {
                    isCorrect = selectedIds.SequenceEqual(correctIds);
                }

                if (isCorrect)
                    correct++;

                result.Questions.Add(new QuestionResultDto
                {
                    QuestionId = q.Id,
                    IsCorrect = isCorrect,
                    SelectedAnswerIds = selectedIds,
                    CorrectAnswerIds = (User.Identity?.IsAuthenticated ?? false) ? correctIds : new List<Guid>()
                });
            }

            result.CorrectCount = correct;
            result.ScorePercent = result.TotalQuestions == 0
                ? 0
                : Math.Round((double)result.CorrectCount / result.TotalQuestions * 100.0, 2);
            result.PassThresholdPercent = Math.Round(result.PassThresholdPercent, 2);
            result.Passed = result.ScorePercent >= result.PassThresholdPercent;

            var trackedTitle = string.IsNullOrWhiteSpace(attempt.QuizTitle)
                ? "Configured Quiz"
                : attempt.QuizTitle.Trim();
            var trackedCategory = string.IsNullOrWhiteSpace(attempt.QuizCategory)
                ? "Mixed"
                : attempt.QuizCategory.Trim();

            await SaveTrackedAttemptIfAuthenticatedAsync(Guid.Empty, trackedTitle, trackedCategory, result);
            await ClearTrackedProgressIfAuthenticatedAsync(attempt.SessionKey);

            return Ok(result);
        }

        [Authorize]
        [HttpGet("progress/current")]
        public async Task<IActionResult> GetCurrentProgress()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var progress = await _db.QuizProgressEntries
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.UpdatedUtc)
                .FirstOrDefaultAsync();

            if (progress == null)
                return NotFound();

            return Ok(new QuizProgressDto
            {
                SessionKey = progress.SessionKey,
                QuizId = progress.QuizId,
                QuizTitle = progress.QuizTitle,
                QuizCategory = progress.QuizCategory,
                LaunchMode = progress.LaunchMode,
                Quiz = string.IsNullOrWhiteSpace(progress.QuizDataJson)
                    ? null
                    : System.Text.Json.JsonSerializer.Deserialize<RandomizedQuizDto>(progress.QuizDataJson),
                Selections = string.IsNullOrWhiteSpace(progress.SelectionsJson)
                    ? new Dictionary<string, List<Guid>>()
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Guid>>>(progress.SelectionsJson) ?? new Dictionary<string, List<Guid>>(),
                CurrentIndex = progress.CurrentIndex,
                TimerRemainingSeconds = progress.TimerRemainingSeconds,
                UpdatedUtc = progress.UpdatedUtc
            });
        }

        [Authorize]
        [HttpPost("progress")]
        public async Task<IActionResult> SaveProgress([FromBody] SaveQuizProgressDto progress)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            if (progress == null || string.IsNullOrWhiteSpace(progress.SessionKey) || progress.Quiz == null)
                return BadRequest("SessionKey and Quiz are required.");

            var existing = await _db.QuizProgressEntries
                .FirstOrDefaultAsync(p => p.UserId == userId && p.SessionKey == progress.SessionKey);

            if (existing == null)
            {
                existing = new QuizProgress
                {
                    UserId = userId,
                    SessionKey = progress.SessionKey
                };
                _db.QuizProgressEntries.Add(existing);
            }

            existing.QuizId = progress.QuizId;
            existing.QuizTitle = string.IsNullOrWhiteSpace(progress.QuizTitle) ? progress.Quiz.Title : progress.QuizTitle;
            existing.QuizCategory = string.IsNullOrWhiteSpace(progress.QuizCategory) ? progress.Quiz.Category : progress.QuizCategory;
            existing.LaunchMode = string.IsNullOrWhiteSpace(progress.LaunchMode) ? "single" : progress.LaunchMode;
            existing.QuizDataJson = System.Text.Json.JsonSerializer.Serialize(progress.Quiz);
            existing.SelectionsJson = System.Text.Json.JsonSerializer.Serialize(progress.Selections ?? new Dictionary<string, List<Guid>>());
            existing.CurrentIndex = progress.CurrentIndex;
            existing.TimerRemainingSeconds = progress.TimerRemainingSeconds;
            existing.UpdatedUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Progress saved." });
        }

        [Authorize]
        [HttpDelete("progress/current")]
        public async Task<IActionResult> ClearProgress([FromQuery] string? sessionKey = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var query = _db.QuizProgressEntries.Where(p => p.UserId == userId);
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                query = query.Where(p => p.SessionKey == sessionKey);
            }

            var rows = await query.ToListAsync();
            if (rows.Count == 0)
                return NotFound();

            _db.QuizProgressEntries.RemoveRange(rows);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Progress cleared." });
        }


        [Authorize(Roles = "Admin")]
        [HttpDelete("{quizId:guid}")]
        public async Task<IActionResult> Delete(Guid quizId)
        {
            var quiz = await _db.Quizzes
             .Include(q => q.Questions)
                .FirstOrDefaultAsync(q => q.Id == quizId);

            if (quiz == null)
             return NotFound($"Quiz not found: {quizId}");

            _db.Quizzes.Remove(quiz);
            await _db.SaveChangesAsync();

            return Ok($"Deleted quiz {quizId}");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{quizId:guid}/archive")]
        public async Task<IActionResult> Archive(Guid quizId)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId);
            if (quiz == null)
                return NotFound($"Quiz not found: {quizId}");

            quiz.IsArchived = true;
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Archived quiz {quiz.Title}.", quizId, isArchived = true });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{quizId:guid}/restore")]
        public async Task<IActionResult> Restore(Guid quizId)
        {
            var quiz = await _db.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId);
            if (quiz == null)
                return NotFound($"Quiz not found: {quizId}");

            quiz.IsArchived = false;
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Restored quiz {quiz.Title}.", quizId, isArchived = false });
        }

        private static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "Uncategorized";

            var collapsed = System.Text.RegularExpressions.Regex.Replace(category.Trim(), @"\s+", " ");
            var lower = collapsed.ToLowerInvariant();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }

        private async Task SaveTrackedAttemptIfAuthenticatedAsync(Guid quizId, string quizTitle, string? quizCategory, QuizAttemptResultDto result)
        {
            if (!(User.Identity?.IsAuthenticated ?? false))
                return;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return;

            _db.QuizAttempts.Add(new QuizAttempt
            {
                UserId = userId,
                QuizId = quizId,
                QuizTitle = string.IsNullOrWhiteSpace(quizTitle) ? "Quiz" : quizTitle,
                QuizCategory = string.IsNullOrWhiteSpace(quizCategory) ? "Uncategorized" : quizCategory,
                TotalQuestions = result.TotalQuestions,
                CorrectCount = result.CorrectCount,
                ScorePercent = result.ScorePercent,
                SubmittedUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        private async Task ClearTrackedProgressIfAuthenticatedAsync(string? sessionKey)
        {
            if (!(User.Identity?.IsAuthenticated ?? false))
                return;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return;

            var query = _db.QuizProgressEntries.Where(p => p.UserId == userId);
            if (!string.IsNullOrWhiteSpace(sessionKey))
            {
                query = query.Where(p => p.SessionKey == sessionKey);
            }

            var rows = await query.ToListAsync();
            if (rows.Count == 0)
                return;

            _db.QuizProgressEntries.RemoveRange(rows);
            await _db.SaveChangesAsync();
        }
    }
}
