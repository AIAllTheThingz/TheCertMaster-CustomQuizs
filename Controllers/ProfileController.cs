using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.DTO;
using QuizAPI.Models;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly QuizDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public ProfileController(QuizDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sort = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var sortKey = NormalizeSort(sort);

            var attemptsQuery = _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == user.Id);

            var allAttempts = await attemptsQuery.ToListAsync();
            var totalAttempts = allAttempts.Count;
            var totalPages = totalAttempts == 0 ? 1 : (int)Math.Ceiling(totalAttempts / (double)pageSize);
            page = Math.Min(page, totalPages);

            var sortedAttempts = ApplySort(allAttempts.AsQueryable(), sortKey);
            var attempts = sortedAttempts
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var dto = new UserProfileDto
            {
                Email = user.Email ?? "",
                UserName = user.UserName ?? user.Email ?? "",
                FirstName = user.FirstName,
                LastName = user.LastName,
                TotalAttempts = totalAttempts,
                AverageScorePercent = totalAttempts == 0 ? 0 : Math.Round(allAttempts.Average(a => a.ScorePercent), 2),
                BestScorePercent = totalAttempts == 0 ? 0 : (int)Math.Round(allAttempts.Max(a => a.ScorePercent)),
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                Sort = sortKey,
                Attempts = attempts.Select(a => new UserQuizAttemptDto
                {
                    AttemptId = a.Id,
                    QuizId = a.QuizId,
                    QuizTitle = a.QuizTitle,
                    QuizCategory = a.QuizCategory,
                    TotalQuestions = a.TotalQuestions,
                    CorrectCount = a.CorrectCount,
                    ScorePercent = a.ScorePercent,
                    SubmittedUtc = a.SubmittedUtc
                }).ToList()
            };

            return Ok(dto);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
                return BadRequest("First name and last name are required.");

            user.FirstName = request.FirstName.Trim();
            user.LastName = request.LastName.Trim();

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(new { message = "Profile updated." });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest("CurrentPassword and NewPassword are required.");

            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(new { message = "Password updated." });
        }

        private static string NormalizeSort(string? sort)
        {
            var normalized = (sort ?? "submitted_desc").Trim().ToLowerInvariant();
            return normalized switch
            {
                "submitted_asc" => normalized,
                "score_desc" => normalized,
                "score_asc" => normalized,
                "title_asc" => normalized,
                "title_desc" => normalized,
                _ => "submitted_desc"
            };
        }

        private static IQueryable<QuizAttempt> ApplySort(IQueryable<QuizAttempt> query, string sort)
        {
            return sort switch
            {
                "submitted_asc" => query.OrderBy(a => a.SubmittedUtc),
                "score_desc" => query.OrderByDescending(a => a.ScorePercent).ThenByDescending(a => a.SubmittedUtc),
                "score_asc" => query.OrderBy(a => a.ScorePercent).ThenByDescending(a => a.SubmittedUtc),
                "title_asc" => query.OrderBy(a => a.QuizTitle).ThenByDescending(a => a.SubmittedUtc),
                "title_desc" => query.OrderByDescending(a => a.QuizTitle).ThenByDescending(a => a.SubmittedUtc),
                _ => query.OrderByDescending(a => a.SubmittedUtc)
            };
        }
    }
}
