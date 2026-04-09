using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PreEmploymentController : ControllerBase
    {
        private readonly QuizDbContext _db;
        private readonly IPreEmploymentConfigStore _configStore;
        private readonly ISmtpSettingsStore _smtpStore;
        private readonly IEmailService _emailService;
        private readonly ILogger<PreEmploymentController> _logger;
        private readonly Random _rng = new();

        // Safety limits (can be loosened later)
        private const int DefaultQuestionCount = 20;
        private const int MaxQuestionCount = 100;

        public PreEmploymentController(
            QuizDbContext db,
            IPreEmploymentConfigStore configStore,
            ISmtpSettingsStore smtpStore,
            IEmailService emailService,
            ILogger<PreEmploymentController> logger)
        {
            _db = db;
            _configStore = configStore;
            _smtpStore = smtpStore;
            _emailService = emailService;
            _logger = logger;
        }

        [HttpGet("config")]
        [AllowAnonymous]
        public async Task<IActionResult> GetConfig()
        {
            var config = await _configStore.GetAsync();

            var selectedIds = GetRequestedQuizIds(config.QuizId, config.QuizIds);
            if (selectedIds.Count > 0)
            {
                var quizzes = await _db.Quizzes
                    .AsNoTracking()
                    .Where(q => selectedIds.Contains(q.Id) && !q.IsArchived)
                    .Select(q => new { q.Id, q.Title, QuestionCount = q.Questions.Count })
                    .ToListAsync();

                if (quizzes.Count > 0)
                {
                    config.QuizIds = quizzes.Select(q => q.Id).ToList();
                    config.QuizTitles = quizzes.Select(q => q.Title).ToList();
                    config.QuizId = config.QuizIds.Count == 1 ? config.QuizIds[0] : null;
                    config.QuizTitle = config.QuizTitles.Count == 1 ? config.QuizTitles[0] : null;

                    var maxPerQuizQuestionCount = quizzes.Min(q => q.QuestionCount);
                    if (maxPerQuizQuestionCount > 0 && config.QuestionCount > maxPerQuizQuestionCount)
                    {
                        config.QuestionCount = maxPerQuizQuestionCount;
                    }
                    config.MaxQuestionCount = Math.Min(MaxQuestionCount, Math.Max(1, maxPerQuizQuestionCount));
                }
                else
                {
                    config.QuizId = null;
                    config.QuizTitle = null;
                    config.QuizIds = new List<Guid>();
                    config.QuizTitles = new List<string>();
                }
            }

            return Ok(config);
        }

        [HttpPost("config")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SaveConfig([FromBody] PreEmploymentConfigDto config)
        {
            if (config == null)
                return BadRequest("Missing request body.");

            var selectedIds = GetRequestedQuizIds(config.QuizId, config.QuizIds);
            if (selectedIds.Count == 0)
                return BadRequest("At least one source quiz must be selected.");

            var quizzes = await _db.Quizzes
                .AsNoTracking()
                .Include(q => q.Questions)
                .Where(q => selectedIds.Contains(q.Id) && !q.IsArchived)
                .ToListAsync();

            if (quizzes.Count != selectedIds.Count)
                return BadRequest("One or more selected quizzes were not found.");

            var availableQuestionCount = quizzes.Sum(q => q.Questions.Count);
            if (availableQuestionCount == 0)
                return BadRequest("The selected quizzes do not contain any questions.");

            var maxPerQuizQuestionCount = quizzes.Min(q => q.Questions.Count);

            if (config.QuestionCount <= 0)
                return BadRequest("QuestionCount must be greater than 0.");

            if (config.QuestionCount > maxPerQuizQuestionCount)
            {
                var totalAvailable = maxPerQuizQuestionCount * quizzes.Count;
                return BadRequest($"QuestionCount may not exceed the smallest selected quiz ({maxPerQuizQuestionCount} per quiz, {totalAvailable} total across {quizzes.Count} quiz(es)).");
            }

            config.QuizIds = quizzes.Select(q => q.Id).ToList();
            config.QuizTitles = quizzes.Select(q => q.Title).ToList();
            config.QuizId = config.QuizIds.Count == 1 ? config.QuizIds[0] : null;
            config.QuizTitle = config.QuizTitles.Count == 1 ? config.QuizTitles[0] : null;
            if (string.IsNullOrWhiteSpace(config.Title))
            {
                config.Title = "Pre-Employment Quiz";
            }

            config.MaxQuestionCount = Math.Min(MaxQuestionCount, maxPerQuizQuestionCount);

            var saved = await _configStore.SaveAsync(config);
            saved.QuizTitles = quizzes.Select(q => q.Title).ToList();
            saved.QuizIds = quizzes.Select(q => q.Id).ToList();
            saved.QuizId = saved.QuizIds.Count == 1 ? saved.QuizIds[0] : null;
            saved.QuizTitle = saved.QuizTitles.Count == 1 ? saved.QuizTitles[0] : null;
            return Ok(saved);
        }

        // Generates a "virtual quiz" from a configured quiz or a selection of categories.
        // This endpoint is intentionally anonymous-friendly for pre-employment testing.
        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] PreEmploymentGenerateRequestDto req)
        {
            if (req == null)
                return BadRequest("Missing request body.");

            var count = req.QuestionCount ?? DefaultQuestionCount;
            if (count <= 0)
                return BadRequest("QuestionCount must be greater than 0.");

            if (count > MaxQuestionCount)
                return BadRequest($"QuestionCount may not exceed {MaxQuestionCount}.");

            var categories = new List<string>();
            var sourceQuizTitles = new List<string>();
            var selectedQuizTitleMap = new Dictionary<Guid, string>();
            var selectedQuizIds = GetRequestedQuizIds(req.QuizId, req.QuizIds);
            IQueryable<Models.Question> poolQuery = _db.Questions
                .AsNoTracking()
                .Include(q => q.Answers)
                .Include(q => q.Images);

            if (selectedQuizIds.Count > 0)
            {
                var sourceQuizzes = await _db.Quizzes
                    .AsNoTracking()
                    .Where(q => selectedQuizIds.Contains(q.Id))
                    .Where(q => !q.IsArchived)
                    .Select(q => new { q.Id, q.Title, q.Category })
                    .ToListAsync();

                if (sourceQuizzes.Count != selectedQuizIds.Count)
                    return BadRequest("One or more selected quizzes were not found.");

                sourceQuizTitles = sourceQuizzes.Select(q => q.Title).ToList();
                selectedQuizTitleMap = sourceQuizzes.ToDictionary(q => q.Id, q => q.Title);
                categories = sourceQuizzes
                    .Select(q => NormalizeCategory(q.Category))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                poolQuery = poolQuery.Where(q => selectedQuizIds.Contains(q.QuizId));
            }
            else
            {
                categories = (req.Categories ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(NormalizeCategory)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (categories.Count == 0)
                    return BadRequest("At least one category is required.");

                // Pull questions from any quiz whose category matches the selection.
                // IMPORTANT: we cannot call NormalizeCategory() inside an EF query because it can't be translated to SQL.
                // So we resolve matching Quiz IDs first (client-side normalization), then filter Questions by QuizId (SQL-translatable).
                var quizIndex = await _db.Quizzes
                    .AsNoTracking()
                    .Select(q => new { q.Id, q.Category, q.IsArchived })
                    .ToListAsync();

                var allowedQuizIds = quizIndex
                    .Where(q => categories.Contains(NormalizeCategory(q.Category)))
                    .Where(q => !q.IsArchived)
                    .Select(q => q.Id)
                    .ToList();

                if (allowedQuizIds.Count == 0)
                    return BadRequest("No quizzes found for the selected categories.");

                poolQuery = poolQuery.Where(q => allowedQuizIds.Contains(q.QuizId));
            }

            var pool = await poolQuery.ToListAsync();

            List<Models.Question> picked;

            if (selectedQuizIds.Count > 0)
            {
                var groupedQuestions = pool
                    .GroupBy(q => q.QuizId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(_ => _rng.Next()).ToList());

                var insufficientQuiz = selectedQuizIds
                    .Select(id => new
                    {
                        Id = id,
                        Title = selectedQuizTitleMap.TryGetValue(id, out var sourceTitle) ? sourceTitle : "Selected quiz",
                        Available = groupedQuestions.TryGetValue(id, out var quizQuestions) ? quizQuestions.Count : 0
                    })
                    .FirstOrDefault(x => x.Available < count);

                if (insufficientQuiz is not null)
                {
                    return BadRequest($"Not enough questions available. Requested {count} from quiz \"{insufficientQuiz.Title}\", found {insufficientQuiz.Available}.");
                }

                picked = selectedQuizIds
                    .SelectMany(id => groupedQuestions[id].Take(count))
                    .OrderBy(_ => _rng.Next())
                    .ToList();
            }
            else
            {
                if (pool.Count < count)
                {
                    var scope = sourceQuizTitles.Count == 0
                        ? "the selected categories"
                        : sourceQuizTitles.Count == 1
                            ? $"quiz \"{sourceQuizTitles[0]}\""
                            : "the selected quiz pool";
                    return BadRequest($"Not enough questions available. Requested {count}, found {pool.Count} in {scope}.");
                }

                picked = pool
                    .OrderBy(_ => _rng.Next())
                    .Take(count)
                    .ToList();
            }

            var title = string.IsNullOrWhiteSpace(req.Title)
                ? "Pre-Employment Quiz"
                : req.Title.Trim();

            var dto = new PreEmploymentQuizDto
            {
                Title = title,
                QuestionCount = picked.Count,
                Categories = categories,
                QuizId = selectedQuizIds.Count == 1 ? selectedQuizIds[0] : null,
                QuizIds = selectedQuizIds,
                SourceQuizTitle = sourceQuizTitles.Count == 1 ? sourceQuizTitles[0] : null,
                SourceQuizTitles = sourceQuizTitles,
                Questions = picked.Select(q => new QuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    AllowMultiple = q.AllowMultiple,
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

            return Ok(dto);
        }

        // Grades a submission against the DB. Correct answers never leave the server.
        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] QuizAttemptSubmitDto attempt)
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
                .Where(q => questionIds.Contains(q.Id))
                .ToListAsync();

            if (questions.Count != questionIds.Count)
                return BadRequest("One or more QuestionId values were not found.");

            var result = new QuizAttemptResultDto
            {
                QuizId = Guid.Empty,
                TotalQuestions = questions.Count
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
                    isCorrect = selectedIds.Count == 1 &&
                                correctIds.Count == 1 &&
                                selectedIds[0] == correctIds[0];
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
                    SelectedAnswerIds = selectedIds
                });
            }

            result.CorrectCount = correct;
            result.ScorePercent = result.TotalQuestions == 0
                ? 0
                : Math.Round((double)result.CorrectCount / result.TotalQuestions * 100.0, 2);

            await SaveSubmissionAsync(attempt, result);
            await TrySendCompletionEmailAsync(attempt, result);

            return Ok(result);
        }

        [HttpGet("submissions")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSubmissions([FromQuery] int take = 50)
        {
            take = Math.Clamp(take, 1, 200);

            var submissions = await _db.PreEmploymentSubmissions
                .AsNoTracking()
                .OrderByDescending(s => s.SubmittedUtc)
                .Take(take)
                .Select(s => new PreEmploymentSubmissionDto
                {
                    Id = s.Id,
                    FirstName = s.FirstName,
                    LastName = s.LastName,
                    QuizTitle = s.QuizTitle,
                    SourceQuizTitles = s.SourceQuizTitles,
                    TotalQuestions = s.TotalQuestions,
                    CorrectCount = s.CorrectCount,
                    ScorePercent = s.ScorePercent,
                    PassingScorePercent = s.PassingScorePercent,
                    Passed = s.Passed,
                    SubmittedUtc = s.SubmittedUtc
                })
                .ToListAsync();

            return Ok(submissions);
        }

        private static string NormalizeCategory(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Uncategorized";

            var s = Regex.Replace(input.Trim(), "\\s+", " ");
            var ti = CultureInfo.InvariantCulture.TextInfo;
            return ti.ToTitleCase(s.ToLowerInvariant());
        }

        private static List<Guid> GetRequestedQuizIds(Guid? quizId, List<Guid>? quizIds)
        {
            var ids = (quizIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (quizId.HasValue && quizId.Value != Guid.Empty && !ids.Contains(quizId.Value))
            {
                ids.Add(quizId.Value);
            }

            return ids;
        }

        private async Task TrySendCompletionEmailAsync(QuizAttemptSubmitDto attempt, QuizAttemptResultDto result)
        {
            try
            {
                var config = await _configStore.GetAsync();
                var smtp = await _smtpStore.GetAsync();
                var recipient = string.IsNullOrWhiteSpace(smtp.NotificationEmail)
                    ? smtp.FromEmail
                    : smtp.NotificationEmail.Trim();

                if (string.IsNullOrWhiteSpace(recipient))
                {
                    return;
                }

                var fullName = string.Join(" ", new[]
                {
                    attempt.FirstName?.Trim(),
                    attempt.LastName?.Trim()
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

                var body =
                    "A pre-employment quiz has been submitted." + Environment.NewLine +
                    Environment.NewLine +
                    $"Candidate: {(string.IsNullOrWhiteSpace(fullName) ? "Not provided" : fullName)}" + Environment.NewLine +
                    $"Configured quiz: {(string.IsNullOrWhiteSpace(config.Title) ? "Pre-Employment Quiz" : config.Title.Trim())}" + Environment.NewLine +
                    $"Source exams: {string.Join(", ", (config.QuizTitles ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))}" + Environment.NewLine +
                    $"Submitted UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC" + Environment.NewLine +
                    $"Questions answered: {result.TotalQuestions}" + Environment.NewLine +
                    $"Correct answers: {result.CorrectCount}" + Environment.NewLine +
                    $"Score percent: {result.ScorePercent}" + Environment.NewLine +
                    $"Passing threshold: {config.PassingScorePercent}" + Environment.NewLine +
                    $"Outcome: {(result.ScorePercent >= config.PassingScorePercent ? "Passed" : "Did not meet threshold")}" + Environment.NewLine;

                await _emailService.SendAsync(recipient, "Pre-Employment Quiz Submitted", body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send pre-employment completion email.");
            }
        }

        private async Task SaveSubmissionAsync(QuizAttemptSubmitDto attempt, QuizAttemptResultDto result)
        {
            var config = await _configStore.GetAsync();
            var sourceTitles = (config.QuizTitles ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

            _db.PreEmploymentSubmissions.Add(new Models.PreEmploymentSubmission
            {
                FirstName = attempt.FirstName?.Trim() ?? string.Empty,
                LastName = attempt.LastName?.Trim() ?? string.Empty,
                QuizTitle = string.IsNullOrWhiteSpace(config.Title) ? "Pre-Employment Quiz" : config.Title.Trim(),
                SourceQuizTitles = string.Join(", ", sourceTitles),
                TotalQuestions = result.TotalQuestions,
                CorrectCount = result.CorrectCount,
                ScorePercent = result.ScorePercent,
                PassingScorePercent = config.PassingScorePercent,
                Passed = result.ScorePercent >= config.PassingScorePercent,
                SubmittedUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
    }
}
