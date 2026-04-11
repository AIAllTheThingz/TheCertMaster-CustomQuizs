using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QuizAPI.Services;
using System.IO;

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

            try
            {
                var msg = await _import.SaveUploadAsync(model.File);
                return Ok(msg);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(BuildImportErrorMessage(ex.Message, ImportAction.UploadFile));
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("upload-package")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadPackage([FromForm] FileUploadModel model)
        {
            if (model?.File == null || model.File.Length == 0)
                return BadRequest("A non-empty file is required.");

            try
            {
                var result = await _import.SaveUploadPackageAsync(model.File);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(BuildImportErrorMessage(ex.Message, ImportAction.UploadPackage));
            }
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
            catch (FileNotFoundException ex)
            {
                return NotFound(BuildImportErrorMessage(ex.Message, ImportAction.ProcessCsv));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(BuildImportErrorMessage(ex.Message, ImportAction.ProcessCsv));
            }
            catch (Exception ex)
            {
                return Problem(BuildImportErrorMessage(ex.Message, ImportAction.ProcessCsv));
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

        private static string BuildImportErrorMessage(string? detail, ImportAction action)
        {
            var safeDetail = string.IsNullOrWhiteSpace(detail) ? "The import request could not be completed." : detail.Trim();
            var nextStep = action switch
            {
                ImportAction.UploadFile => "Upload a non-empty .csv or .txt file, then click Import from the file list.",
                ImportAction.UploadPackage => "Upload one .zip package that contains exactly one quiz .csv file and any related image files.",
                _ => "Review the file, correct the issue, re-upload it if needed, then try Import again."
            };

            if (safeDetail.Contains("No file received", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = action == ImportAction.UploadPackage
                    ? "Select one .zip package from your workstation and upload it again."
                    : "Select one .csv or .txt quiz file from your workstation and upload it again.";
            }
            else if (safeDetail.Contains("Package upload must be a .zip file", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Upload a .zip package instead of an individual CSV. The package should contain exactly one quiz CSV and any related image files.";
            }
            else if (safeDetail.Contains("exactly one .csv file", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Rebuild the ZIP so it contains one quiz CSV file only. Keep any image files alongside that CSV, then upload the ZIP again.";
            }
            else if (safeDetail.Contains("CSV ended while inside a quoted field", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Open the CSV and fix the broken quoted value. Check for a missing closing quote or a pasted line break inside a field, then re-upload the corrected file.";
            }
            else if (safeDetail.Contains("CSV appears empty", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Confirm the CSV still has the header row and at least one quiz-question-answer row, then upload and import it again.";
            }
            else if (safeDetail.Contains("CSV missing required columns", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Update the CSV header to include QuizTitle, QuestionText, AnswerText, and IsCorrect. QuestionImgKey and Category are optional.";
            }
            else if (safeDetail.Contains("Invalid file name", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Refresh the file list and retry using a listed file. Avoid renaming files in the request URL manually.";
            }
            else if (safeDetail.Contains("File not found", StringComparison.OrdinalIgnoreCase))
            {
                nextStep = "Refresh the file list, confirm the extracted CSV is still present, then click Import again. If it is missing, upload the file or package again first.";
            }

            return $"{safeDetail} Next step: {nextStep}";
        }

        private enum ImportAction
        {
            UploadFile,
            UploadPackage,
            ProcessCsv
        }

    }

    // define the upload model right here so Swagger can describe it
    public class FileUploadModel
    {
        [FromForm(Name = "File")]
        public IFormFile File { get; set; } = default!;
    }
}
