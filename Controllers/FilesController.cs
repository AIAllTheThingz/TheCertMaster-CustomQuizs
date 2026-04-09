using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/files")]
    [Authorize(Roles = "Admin")]
    public class FilesController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public FilesController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpGet]
        public IActionResult GetFiles()
        {
            var uploadsRoot = GetPrivateUploadsRoot();
            if (!Directory.Exists(uploadsRoot))
                return Ok(new List<object>());

            var files = Directory.GetFiles(uploadsRoot)
                .Select(path => new
                {
                    FileName = Path.GetFileName(path),
                    SizeKb = new FileInfo(path).Length / 1024,
                    UploadedUtc = System.IO.File.GetCreationTimeUtc(path),
                    Url = string.Empty
                })
                .OrderByDescending(f => f.UploadedUtc)
                .ToList();

            return Ok(files);
        }

        [HttpDelete("{fileName}")]
        public IActionResult DeleteFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequest("File name is required.");

            var uploadsRoot = GetPrivateUploadsRoot();
            var filePath = GetSafeChildPath(uploadsRoot, fileName);
            if (filePath == null)
                return BadRequest("Invalid file name.");

            if (!System.IO.File.Exists(filePath))
                return NotFound($"File not found: {fileName}");

            System.IO.File.Delete(filePath);
            return Ok($"Deleted {fileName}");
        }

        private string GetPrivateUploadsRoot()
        {
            return Path.Combine(_env.ContentRootPath, "App_Data", "uploads");
        }

        private static string? GetSafeChildPath(string root, string fileName)
        {
            var normalizedName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, normalizedName, StringComparison.Ordinal))
                return null;

            var rootPath = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(rootPath, normalizedName));

            return candidate.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
    }
}
