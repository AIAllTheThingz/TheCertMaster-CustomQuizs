using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QuizAPI.Services;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/import")]
    public class ImportController : ControllerBase
    {
        private readonly QuizImportService _import;

        public ImportController(QuizImportService import) => _import = import;

        [Authorize(Roles = "Admin")]
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public async Task<IActionResult> Upload([FromForm] FileUploadModel model)
        {
            if (model?.File == null || model.File.Length == 0)
                return BadRequest("A non-empty file is required.");

            var msg = await _import.SaveUploadAsync(model.File);
            return Ok(msg);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("upload-package")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadPackage([FromForm] FileUploadModel model)
        {
            if (model?.File == null || model.File.Length == 0)
                return BadRequest("A non-empty file is required.");

            var result = await _import.SaveUploadPackageAsync(model.File);
            return Ok(result);
        }

        //[Authorize(Roles ="Admin")]
        [Authorize(Roles = "Admin")]
        [HttpPost("process/{fileName}")]
        public async Task<IActionResult> Process(string fileName)
        {
            var normalizedFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedFileName) || !string.Equals(fileName, normalizedFileName, StringComparison.Ordinal))
                return BadRequest("Invalid file name.");

            try
            {
                var result = await _import.ProcessCsvAsync(normalizedFileName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
        }





        [Authorize(Roles = "Admin")]
[HttpGet("history")]
public IActionResult History([FromQuery] int take = 4)
{
    if (take < 1) take = 1;
    if (take > 200) take = 200;
    return Ok(_import.ReadImportHistory(take));
}

    }

    // define the upload model right here so Swagger can describe it
    public class FileUploadModel
    {
        [FromForm(Name = "File")]
        public IFormFile File { get; set; } = default!;
    }
}
