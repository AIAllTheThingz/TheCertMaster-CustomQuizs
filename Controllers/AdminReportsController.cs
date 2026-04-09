using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;
using QuizAPI.Services;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin/reports")]
    [Authorize(Roles = "Admin")]
    public class AdminReportsController : ControllerBase
    {
        private readonly QuizDbContext _db;
        private readonly UserManager<AppUser> _userManager;
        private readonly QuizImportService _importService;

        public AdminReportsController(QuizDbContext db, UserManager<AppUser> userManager, QuizImportService importService)
        {
            _db = db;
            _userManager = userManager;
            _importService = importService;
        }

        [HttpGet("quiz-usage")]
        public async Task<IActionResult> GetQuizUsage()
        {
            var attempts = await _db.QuizAttempts.AsNoTracking().ToListAsync();

            var usage = attempts
                .GroupBy(a => new { a.QuizId, a.QuizTitle, a.QuizCategory })
                .Select(g => new QuizUsageReportItemDto
                {
                    QuizId = g.Key.QuizId,
                    QuizTitle = g.Key.QuizTitle,
                    QuizCategory = g.Key.QuizCategory,
                    AttemptCount = g.Count(),
                    AverageScorePercent = Math.Round(g.Average(x => x.ScorePercent), 2),
                    LastAttemptUtc = g.Max(x => x.SubmittedUtc)
                })
                .OrderByDescending(x => x.AttemptCount)
                .ThenByDescending(x => x.LastAttemptUtc)
                .ToList();

            var recentAttemptRows = await _db.QuizAttempts
                .AsNoTracking()
                .OrderByDescending(a => a.SubmittedUtc)
                .Take(25)
                .Join(_userManager.Users.AsNoTracking(),
                    attempt => attempt.UserId,
                    user => user.Id,
                    (attempt, user) => new
                    {
                        user.FirstName,
                        user.LastName,
                        UserEmail = user.Email ?? user.UserName ?? string.Empty,
                        QuizTitle = attempt.QuizTitle,
                        QuizCategory = attempt.QuizCategory,
                        ScorePercent = attempt.ScorePercent,
                        CorrectCount = attempt.CorrectCount,
                        TotalQuestions = attempt.TotalQuestions,
                        SubmittedUtc = attempt.SubmittedUtc
                    })
                .ToListAsync();

            var recentAttempts = recentAttemptRows
                .Select(row => new RecentAttemptDto
                {
                    UserName = string.Join(" ", new[] { row.FirstName, row.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
                    UserEmail = row.UserEmail,
                    QuizTitle = row.QuizTitle,
                    QuizCategory = row.QuizCategory,
                    ScorePercent = row.ScorePercent,
                    CorrectCount = row.CorrectCount,
                    TotalQuestions = row.TotalQuestions,
                    SubmittedUtc = row.SubmittedUtc
                })
                .ToList();

            return Ok(new
            {
                totalAttempts = attempts.Count,
                uniqueUsers = attempts.Select(a => a.UserId).Distinct().Count(),
                mostTakenQuizzes = usage,
                recentAttempts
            });
        }

        [HttpGet("user-activity.csv")]
        public async Task<IActionResult> ExportUserActivityCsv()
        {
            var rows = await _db.QuizAttempts
                .AsNoTracking()
                .OrderByDescending(a => a.SubmittedUtc)
                .Join(_userManager.Users.AsNoTracking(),
                    attempt => attempt.UserId,
                    user => user.Id,
                    (attempt, user) => new
                    {
                        user.FirstName,
                        user.LastName,
                        Email = user.Email ?? user.UserName ?? string.Empty,
                        attempt.QuizTitle,
                        attempt.QuizCategory,
                        attempt.ScorePercent,
                        attempt.CorrectCount,
                        attempt.TotalQuestions,
                        attempt.SubmittedUtc
                    })
                .ToListAsync();

            var csv = new StringBuilder();
            csv.AppendLine("FirstName,LastName,Email,QuizTitle,QuizCategory,ScorePercent,CorrectCount,TotalQuestions,SubmittedUtc");
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(",",
                    Csv(row.FirstName),
                    Csv(row.LastName),
                    Csv(row.Email),
                    Csv(row.QuizTitle),
                    Csv(row.QuizCategory),
                    row.ScorePercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.CorrectCount,
                    row.TotalQuestions,
                    row.SubmittedUtc.ToString("O")));
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "user-activity.csv");
        }

        [HttpGet("import-history.csv")]
        public IActionResult ExportImportHistoryCsv()
        {
            var rows = _importService.ReadImportHistory(200).ToList();
            var csv = new StringBuilder();
            csv.AppendLine("ImportedUtc,FileName,Status,Message,Rows,Quizzes,Questions,Answers");
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(",",
                    Csv(GetProp(row, "ImportedUtc")),
                    Csv(GetProp(row, "FileName")),
                    Csv(GetProp(row, "Status")),
                    Csv(GetProp(row, "Message")),
                    Csv(GetProp(row, "Rows")),
                    Csv(GetProp(row, "Quizzes")),
                    Csv(GetProp(row, "Questions")),
                    Csv(GetProp(row, "Answers"))));
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "import-history.csv");
        }

        private static string Csv(string? value)
        {
            var s = value ?? string.Empty;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string GetProp(object row, string name)
        {
            var value = row.GetType().GetProperty(name)?.GetValue(row);
            return value switch
            {
                DateTime dt => dt.ToString("O"),
                _ => value?.ToString() ?? string.Empty
            };
        }
    }
}
