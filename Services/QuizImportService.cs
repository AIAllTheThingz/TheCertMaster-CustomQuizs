using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.Models;
using System.Globalization;
using System.Text.Json;
using System.Text;
using System.IO.Compression;

namespace QuizAPI.Services
{
    public class QuizImportService
    {
        private const long MaxCsvUploadBytes = 25L * 1024 * 1024;
        private const long MaxPackageUploadBytes = 50L * 1024 * 1024;
        private const long MaxPackageUncompressedBytes = 150L * 1024 * 1024;
        private const long MaxImageEntryBytes = 10L * 1024 * 1024;
        private const int MaxPackageEntries = 250;
        private const int MaxPackageImages = 200;

        private readonly IWebHostEnvironment _env;
        private readonly QuizDbContext _db;

        public QuizImportService(IWebHostEnvironment env, QuizDbContext db)
        {
            _env = env;
            _db = db;
        }

        private sealed class ImportRunRecord
        {
            public DateTime ImportedUtc { get; set; }
            public string FileName { get; set; } = "";
            public int Rows { get; set; }
            public int Quizzes { get; set; }
            public int Questions { get; set; }
            public int Answers { get; set; }
            public string Status { get; set; } = "";
            public string Message { get; set; } = "";
        }

        private sealed class ImportRow
        {
            public string QuizTitle { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public double? PassThresholdPercent { get; set; }
            public string QuestionImgKey { get; set; } = string.Empty;
            public string QuestionText { get; set; } = string.Empty;
            public string Difficulty { get; set; } = string.Empty;
            public string Tags { get; set; } = string.Empty;
            public string AnswerText { get; set; } = string.Empty;
            public bool IsCorrect { get; set; }
        }

        private string GetImportHistoryPath()
        {
            var root = Path.Combine(_env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(root);
            return Path.Combine(root, "import_history.jsonl");
        }

        private void AppendImportHistory(ImportRunRecord run)
        {
            var line = JsonSerializer.Serialize(run);
            File.AppendAllText(GetImportHistoryPath(), line + Environment.NewLine, Encoding.UTF8);
        }

        public IEnumerable<object> ReadImportHistory(int take = 50)
        {
            var path = GetImportHistoryPath();
            if (!File.Exists(path))
                return Array.Empty<object>();

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var parsed = new List<ImportRunRecord>();
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l))
                    continue;

                try
                {
                    var run = JsonSerializer.Deserialize<ImportRunRecord>(l);
                    if (run != null)
                        parsed.Add(run);
                }
                catch
                {
                }
            }

            return parsed
                .OrderByDescending(r => r.ImportedUtc)
                .Take(take)
                .Select(r => new
                {
                    r.ImportedUtc,
                    r.FileName,
                    r.Status,
                    r.Message,
                    r.Rows,
                    r.Quizzes,
                    r.Questions,
                    r.Answers
                })
                .ToList();
        }

        public async Task<string> SaveUploadAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("No file received.");

            if (file.Length > MaxCsvUploadBytes)
                throw new InvalidOperationException($"CSV upload may not exceed {FormatBytes(MaxCsvUploadBytes)}.");

            var uploadsRoot = GetPrivateUploadsRoot();
            Directory.CreateDirectory(uploadsRoot);

            var safeName = Path.GetFileName(file.FileName);
            var outPath = Path.Combine(uploadsRoot, $"{Guid.NewGuid()}_{safeName}");

            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                await file.CopyToAsync(fs);

            return $"Saved import file: {Path.GetFileName(outPath)}";
        }

        public async Task<object> SaveUploadPackageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new InvalidOperationException("No file received.");

            var safeName = Path.GetFileName(file.FileName);
            if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Package upload must be a .zip file.");

            if (file.Length > MaxPackageUploadBytes)
                throw new InvalidOperationException($"Package upload may not exceed {FormatBytes(MaxPackageUploadBytes)}.");

            var privateUploadsRoot = GetPrivateUploadsRoot();
            Directory.CreateDirectory(privateUploadsRoot);

            var packageId = Guid.NewGuid().ToString("N");

            var zipPath = Path.Combine(privateUploadsRoot, $"{packageId}_{safeName}");
            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                await file.CopyToAsync(fs);

            var publicUploadsRoot = GetPublicUploadsRoot();
            var imagesDir = Path.Combine(publicUploadsRoot, "images", packageId);
            Directory.CreateDirectory(imagesDir);

            string? csvEntryName = null;
            string? csvSavedFileName = null;
            int imagesSaved = 0;

            using (var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                var fileEntries = archive.Entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .ToList();

                if (fileEntries.Count > MaxPackageEntries)
                    throw new InvalidOperationException($"Package may not contain more than {MaxPackageEntries} files.");

                var totalUncompressedBytes = 0L;
                foreach (var entry in fileEntries)
                {
                    if (entry.Length < 0)
                        throw new InvalidOperationException($"Package entry \"{entry.FullName}\" has an invalid size.");

                    if (entry.Length > MaxPackageUncompressedBytes
                        || totalUncompressedBytes > MaxPackageUncompressedBytes - entry.Length)
                        throw new InvalidOperationException($"Package uncompressed content may not exceed {FormatBytes(MaxPackageUncompressedBytes)}.");

                    totalUncompressedBytes += entry.Length;

                    var ext = Path.GetExtension(entry.Name);
                    if (IsRejectedImageExtension(ext))
                        throw new InvalidOperationException("SVG images are not accepted in quiz packages. Use PNG, JPG, GIF, or WebP.");
                }

                var csvEntries = fileEntries
                    .Where(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (csvEntries.Count != 1)
                    throw new InvalidOperationException("Package must contain exactly one .csv file.");

                var csvEntry = csvEntries[0];
                if (csvEntry.Length > MaxCsvUploadBytes)
                    throw new InvalidOperationException($"Package CSV may not exceed {FormatBytes(MaxCsvUploadBytes)}.");

                csvEntryName = csvEntry.Name;
                csvSavedFileName = $"{packageId}_{Path.GetFileName(csvEntry.Name)}";
                var csvOutPath = Path.Combine(privateUploadsRoot, csvSavedFileName);

                using (var inStream = csvEntry.Open())
                using (var outStream = new FileStream(csvOutPath, FileMode.Create, FileAccess.Write))
                    await inStream.CopyToAsync(outStream);

                foreach (var entry in fileEntries)
                {
                    var ext = Path.GetExtension(entry.Name);
                    if (string.IsNullOrWhiteSpace(ext))
                        continue;

                    if (!IsSupportedImageExtension(ext))
                        continue;

                    var fileNameOnly = Path.GetFileName(entry.Name);
                    if (string.IsNullOrWhiteSpace(fileNameOnly))
                        continue;

                    if (entry.Length > MaxImageEntryBytes)
                        throw new InvalidOperationException($"Image \"{fileNameOnly}\" may not exceed {FormatBytes(MaxImageEntryBytes)}.");

                    if (imagesSaved >= MaxPackageImages)
                        throw new InvalidOperationException($"Package may not contain more than {MaxPackageImages} images.");

                    var imageBytes = await ReadEntryBytesAsync(entry, MaxImageEntryBytes);
                    ValidateImageBytes(fileNameOnly, ext, imageBytes);

                    var outFileName = EnsureUniqueFileName(imagesDir, fileNameOnly);
                    var outPath = Path.Combine(imagesDir, outFileName);

                    await File.WriteAllBytesAsync(outPath, imageBytes);

                    imagesSaved++;
                }
            }

            return new
            {
                PackageId = packageId,
                ZipFileName = Path.GetFileName(zipPath),
                CsvFileName = csvSavedFileName ?? "",
                ImageBaseUrl = $"/uploads/images/{packageId}/",
                ImagesSaved = imagesSaved,
                CsvEntry = csvEntryName ?? ""
            };
        }

        private static bool IsSupportedImageExtension(string ext)
        {
            return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRejectedImageExtension(string? ext)
        {
            return string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry, long maxBytes)
        {
            await using var inStream = entry.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;

            while (true)
            {
                var read = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0)
                    break;

                total += read;
                if (total > maxBytes)
                    throw new InvalidOperationException($"Package entry \"{entry.Name}\" exceeded the allowed size while reading.");

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }

        private static void ValidateImageBytes(string fileName, string ext, byte[] bytes)
        {
            var valid = ext.ToLowerInvariant() switch
            {
                ".png" => bytes.Length >= 8
                    && bytes[0] == 0x89
                    && bytes[1] == 0x50
                    && bytes[2] == 0x4E
                    && bytes[3] == 0x47
                    && bytes[4] == 0x0D
                    && bytes[5] == 0x0A
                    && bytes[6] == 0x1A
                    && bytes[7] == 0x0A,
                ".jpg" or ".jpeg" => bytes.Length >= 3
                    && bytes[0] == 0xFF
                    && bytes[1] == 0xD8
                    && bytes[2] == 0xFF,
                ".gif" => bytes.Length >= 6
                    && bytes[0] == 0x47
                    && bytes[1] == 0x49
                    && bytes[2] == 0x46
                    && bytes[3] == 0x38
                    && (bytes[4] == 0x37 || bytes[4] == 0x39)
                    && bytes[5] == 0x61,
                ".webp" => bytes.Length >= 12
                    && bytes[0] == 0x52
                    && bytes[1] == 0x49
                    && bytes[2] == 0x46
                    && bytes[3] == 0x46
                    && bytes[8] == 0x57
                    && bytes[9] == 0x45
                    && bytes[10] == 0x42
                    && bytes[11] == 0x50,
                _ => false
            };

            if (!valid)
                throw new InvalidOperationException($"Image \"{fileName}\" does not match its file type.");
        }

        private static string FormatBytes(long bytes)
        {
            return bytes >= 1024 * 1024
                ? $"{bytes / (1024 * 1024)} MB"
                : $"{bytes} bytes";
        }

        private static string EnsureUniqueFileName(string directory, string desiredFileName)
        {
            var safe = Path.GetFileName(desiredFileName);
            var baseName = Path.GetFileNameWithoutExtension(safe);
            var ext = Path.GetExtension(safe);

            var candidate = safe;
            int i = 1;
            while (File.Exists(Path.Combine(directory, candidate)))
            {
                candidate = $"{baseName}_{i}{ext}";
                i++;
            }

            return candidate;
        }

        private static IEnumerable<string[]> ReadCsvRecords(string filePath)
        {
            using var sr = new StreamReader(filePath);
            var record = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            while (true)
            {
                int chInt = sr.Read();
                if (chInt == -1)
                {
                    if (inQuotes)
                        throw new InvalidOperationException("CSV ended while inside a quoted field.");

                    if (field.Length > 0 || record.Count > 0)
                    {
                        record.Add(field.ToString());
                        yield return record.ToArray();
                    }
                    yield break;
                }

                char ch = (char)chInt;

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        int peek = sr.Peek();
                        if (peek == '"')
                        {
                            sr.Read();
                            field.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (ch == ',')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    continue;
                }

                if (ch == '\r')
                {
                    if (sr.Peek() == '\n')
                        sr.Read();

                    record.Add(field.ToString());
                    field.Clear();
                    yield return record.ToArray();
                    record.Clear();
                    continue;
                }

                if (ch == '\n')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    yield return record.ToArray();
                    record.Clear();
                    continue;
                }

                field.Append(ch);
            }
        }

        private static bool ParseBoolLoose(string value)
        {
            var v = (value ?? string.Empty).Trim();
            return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("t", StringComparison.OrdinalIgnoreCase)
                || v.Equals("1", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

        private static double? ParseDoubleLoose(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        public async Task<string> ProcessCsvAsync(string fileName)
        {
            var run = new ImportRunRecord
            {
                ImportedUtc = DateTime.UtcNow,
                FileName = fileName
            };

            try
            {
                var uploadsRoot = GetPrivateUploadsRoot();
                var filePath = GetSafeChildPath(uploadsRoot, fileName);

                if (filePath == null)
                    throw new InvalidOperationException("Invalid file name.");

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {fileName}");

                var records = ReadCsvRecords(filePath).ToList();
                if (records.Count <= 1)
                    throw new InvalidOperationException("CSV appears empty or has no data rows.");

                var packageId = TryGetPackageIdFromFileName(fileName);
                var imageLookup = BuildImageLookup(packageId);

                var header = records[0].Select(h => (h ?? string.Empty).Trim()).ToArray();
                int quizIdx = Array.IndexOf(header, "QuizTitle");
                int categoryIdx = Array.IndexOf(header, "Category");
                int passThresholdIdx = Array.IndexOf(header, "PassThresholdPercent");
                int questionImgKeyIdx = Array.IndexOf(header, "QuestionImgKey");
                int questionIdx = Array.IndexOf(header, "QuestionText");
                int difficultyIdx = Array.IndexOf(header, "Difficulty");
                int tagsIdx = Array.IndexOf(header, "Tags");
                int answerIdx = Array.IndexOf(header, "AnswerText");
                int correctIdx = Array.IndexOf(header, "IsCorrect");

                if (quizIdx < 0 || questionIdx < 0 || answerIdx < 0 || correctIdx < 0)
                    throw new InvalidOperationException("CSV missing required columns (QuizTitle, QuestionText, AnswerText, IsCorrect). Optional columns include Category and QuestionImgKey.");

                var rows = records.Skip(1)
                    .Where(c => c.Length > Math.Max(Math.Max(quizIdx, questionIdx), Math.Max(answerIdx, correctIdx)))
                    .Select(c => new ImportRow
                    {
                        QuizTitle = (c[quizIdx] ?? string.Empty).Trim(),
                        Category = categoryIdx >= 0 && categoryIdx < c.Length ? (c[categoryIdx] ?? string.Empty).Trim() : "",
                        PassThresholdPercent = passThresholdIdx >= 0 && passThresholdIdx < c.Length ? ParseDoubleLoose(c[passThresholdIdx]) : null,
                        QuestionImgKey = questionImgKeyIdx >= 0 && questionImgKeyIdx < c.Length ? (c[questionImgKeyIdx] ?? string.Empty).Trim() : "",
                        QuestionText = (c[questionIdx] ?? string.Empty).Trim(),
                        Difficulty = difficultyIdx >= 0 && difficultyIdx < c.Length ? NormalizeDifficulty(c[difficultyIdx]) : "Unspecified",
                        Tags = tagsIdx >= 0 && tagsIdx < c.Length ? NormalizeTags(c[tagsIdx]) : "",
                        AnswerText = (c[answerIdx] ?? string.Empty).Trim(),
                        IsCorrect = ParseBoolLoose(correctIdx >= 0 && correctIdx < c.Length ? c[correctIdx] : "")
                    })
                    .ToList();

                var grouped = rows.GroupBy(r => new { Title = r.QuizTitle, Category = NormalizeCategory(r.Category) });

                int quizCount = 0, questionCount = 0, answerCount = 0;

                await using var transaction = await _db.Database.BeginTransactionAsync();

                foreach (var quizGroup in grouped)
                {
                    var quizTitle = quizGroup.Key.Title;
                    var quizCategory = quizGroup.Key.Category;

                    var existing = await _db.Quizzes
                        .Include(q => q.Questions)
                            .ThenInclude(q => q.Answers)
                        .Include(q => q.Questions)
                            .ThenInclude(q => q.Images)
                        .FirstOrDefaultAsync(q => q.Title == quizTitle && (q.Category ?? "Uncategorized") == quizCategory);

                    if (existing != null)
                    {
                        _db.Quizzes.Remove(existing);
                    }

                    var quizPassThreshold = quizGroup
                        .Select(r => r.PassThresholdPercent)
                        .FirstOrDefault(v => v.HasValue)
                        ?? 70;

                    var quiz = new Models.Quiz
                    {
                        Title = quizTitle,
                        Category = quizCategory == "Uncategorized" ? null : quizCategory,
                        PassThresholdPercent = Math.Clamp(quizPassThreshold, 0, 100)
                    };
                    _db.Quizzes.Add(quiz);
                    quizCount++;

                    var questions = quizGroup.GroupBy(r => r.QuestionText);

                    int orderIndex = 0;
                    foreach (var qGroup in questions)
                    {
                        var question = new Models.Question
                        {
                            Quiz = quiz,
                            Text = qGroup.Key,
                            OrderIndex = orderIndex++,
                            AllowMultiple = qGroup.Count(r => r.IsCorrect) > 1,
                            Difficulty = qGroup.Select(r => r.Difficulty).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "Unspecified",
                            Tags = qGroup.Select(r => r.Tags).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty
                        };
                        _db.Questions.Add(question);
                        questionCount++;

                        foreach (var image in CreateImagesForQuestion(qGroup, question, imageLookup))
                        {
                            _db.Images.Add(image);
                        }

                        int ansOrder = 0;
                        foreach (var row in qGroup)
                        {
                            var answer = new Models.Answer
                            {
                                Question = question,
                                Text = row.AnswerText,
                                IsCorrect = row.IsCorrect,
                                OrderIndex = ansOrder++
                            };
                            _db.Answers.Add(answer);
                            answerCount++;
                        }
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                run.Rows = rows.Count;
                run.Quizzes = quizCount;
                run.Questions = questionCount;
                run.Answers = answerCount;
                run.Status = "Success";
                run.Message = $"Imported {quizCount} quiz(es), {questionCount} question(s), {answerCount} answer(s) from {fileName}";

                AppendImportHistory(run);

                return run.Message;
            }
            catch (Exception ex)
            {
                run.Status = "Failed";
                run.Message = ex.Message;
                try { AppendImportHistory(run); } catch { }
                throw;
            }
        }

        private static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "Uncategorized";

            var collapsed = System.Text.RegularExpressions.Regex.Replace(category.Trim(), @"\s+", " ");
            var lower = collapsed.ToLowerInvariant();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }

        private static string NormalizeDifficulty(string? difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty))
                return "Unspecified";

            var collapsed = System.Text.RegularExpressions.Regex.Replace(difficulty.Trim(), @"\s+", " ");
            var lower = collapsed.ToLowerInvariant();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
        }

        private static string NormalizeTags(string? tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return string.Empty;

            return string.Join(", ",
                tags.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private IReadOnlyDictionary<string, List<ImportedImage>> BuildImageLookup(string? packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return new Dictionary<string, List<ImportedImage>>(StringComparer.OrdinalIgnoreCase);

            var imageDirectory = Path.Combine(GetPublicUploadsRoot(), "images", packageId);
            if (!Directory.Exists(imageDirectory))
                return new Dictionary<string, List<ImportedImage>>(StringComparer.OrdinalIgnoreCase);

            var lookup = new Dictionary<string, List<ImportedImage>>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.GetFiles(imageDirectory))
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                var image = new ImportedImage
                {
                    FileName = fileName,
                    ContentType = GetContentTypeFromExtension(Path.GetExtension(fileName)),
                    Url = $"/uploads/images/{packageId}/{fileName}"
                };

                AddLookupEntry(lookup, fileName, image);
                AddLookupEntry(lookup, Path.GetFileNameWithoutExtension(fileName), image);
            }

            return lookup;
        }

        private static void AddLookupEntry(Dictionary<string, List<ImportedImage>> lookup, string? key, ImportedImage image)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (!lookup.TryGetValue(key, out var items))
            {
                items = new List<ImportedImage>();
                lookup[key] = items;
            }

            items.Add(image);
        }

        private static IEnumerable<Image> CreateImagesForQuestion(
            IGrouping<string, ImportRow> questionRows,
            Question question,
            IReadOnlyDictionary<string, List<ImportedImage>> imageLookup)
        {
            if (imageLookup.Count == 0)
                yield break;

            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in questionRows)
            {
                foreach (var key in SplitImageKeys((string?)row.QuestionImgKey))
                {
                    if (!imageLookup.TryGetValue(key, out var matches))
                        continue;

                    foreach (var match in matches)
                    {
                        if (!seenUrls.Add(match.Url))
                            continue;

                        yield return new Image
                        {
                            QuestionId = question.Id,
                            FileName = match.FileName,
                            ContentType = match.ContentType,
                            Url = match.Url
                        };
                    }
                }
            }
        }

        private static IEnumerable<string> SplitImageKeys(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            foreach (var part in raw.Split(new[] { '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    yield return part;
            }
        }

        private static string? TryGetPackageIdFromFileName(string fileName)
        {
            var normalized = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var underscoreIndex = normalized.IndexOf('_');
            if (underscoreIndex <= 0)
                return null;

            var candidate = normalized[..underscoreIndex];
            return candidate.Length == 32 && candidate.All(Uri.IsHexDigit) ? candidate : null;
        }

        private static string GetContentTypeFromExtension(string? ext)
        {
            return (ext ?? string.Empty).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }

        private sealed class ImportedImage
        {
            public string FileName { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }

        private string GetPrivateUploadsRoot()
        {
            return Path.Combine(_env.ContentRootPath, "App_Data", "uploads");
        }

        private string GetPublicUploadsRoot()
        {
            return Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads");
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
