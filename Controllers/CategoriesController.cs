using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly QuizDbContext _db;

        public CategoriesController(QuizDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var raw = await _db.Quizzes
                .AsNoTracking()
                .Where(q => !q.IsArchived)
                .Select(q => q.Category)
                .ToListAsync();

            var categories = raw
                .Select(NormalizeCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            if (!categories.Contains("Uncategorized", StringComparer.OrdinalIgnoreCase))
                categories.Add("Uncategorized");

            return Ok(categories);
        }

        private static string NormalizeCategory(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Uncategorized";

            var s = Regex.Replace(input.Trim(), "\\s+", " ");
            var ti = CultureInfo.InvariantCulture.TextInfo;
            s = ti.ToTitleCase(s.ToLowerInvariant());
            return s;
        }
    }
}
