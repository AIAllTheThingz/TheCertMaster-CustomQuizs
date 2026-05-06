using System.Text.Json;
using QuizAPI.DTO;

namespace QuizAPI.Services
{
    public interface IPreEmploymentConfigStore
    {
        Task<PreEmploymentConfigDto> GetAsync();
        Task<PreEmploymentConfigDto> SaveAsync(PreEmploymentConfigDto config);
    }

    public sealed class FilePreEmploymentConfigStore : IPreEmploymentConfigStore
    {
        private const int DefaultQuestionCount = 20;
        private const int MaxQuestionLimit = 100;
        private const int MaxAccessCodeLength = 128;

        private readonly string _filePath;
        private readonly object _lock = new();

        public FilePreEmploymentConfigStore(IWebHostEnvironment env)
        {
            var appData = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "preemployment_config.json");
        }

        public Task<PreEmploymentConfigDto> GetAsync()
        {
            lock (_lock)
            {
                var config = LoadUnsafe();
                return Task.FromResult(config);
            }
        }

        public Task<PreEmploymentConfigDto> SaveAsync(PreEmploymentConfigDto config)
        {
            ArgumentNullException.ThrowIfNull(config);

            lock (_lock)
            {
                var sanitized = Sanitize(config);
                var json = JsonSerializer.Serialize(sanitized, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_filePath, json);
                return Task.FromResult(sanitized);
            }
        }

        private PreEmploymentConfigDto LoadUnsafe()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var saved = JsonSerializer.Deserialize<PreEmploymentConfigDto>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (saved != null)
                    {
                        return Sanitize(saved);
                    }
                }
            }
            catch
            {
            }

            return Sanitize(new PreEmploymentConfigDto());
        }

        private static PreEmploymentConfigDto Sanitize(PreEmploymentConfigDto source)
        {
            var maxQuestionCount = source.MaxQuestionCount <= 0 ? MaxQuestionLimit : source.MaxQuestionCount;
            if (maxQuestionCount > MaxQuestionLimit)
            {
                maxQuestionCount = MaxQuestionLimit;
            }

            var questionCount = source.QuestionCount <= 0 ? DefaultQuestionCount : source.QuestionCount;
            if (questionCount > maxQuestionCount)
            {
                questionCount = maxQuestionCount;
            }

            var accessCode = string.IsNullOrWhiteSpace(source.AccessCode)
                ? null
                : source.AccessCode.Trim();
            if (accessCode?.Length > MaxAccessCodeLength)
            {
                accessCode = accessCode[..MaxAccessCodeLength];
            }

            return new PreEmploymentConfigDto
            {
                Title = string.IsNullOrWhiteSpace(source.Title) ? "Pre-Employment Quiz" : source.Title.Trim(),
                QuizId = source.QuizId,
                QuizTitle = string.IsNullOrWhiteSpace(source.QuizTitle) ? null : source.QuizTitle.Trim(),
                QuizIds = (source.QuizIds ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList(),
                QuizTitles = (source.QuizTitles ?? new List<string>())
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .Select(title => title.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                QuestionIds = (source.QuestionIds ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList(),
                SelectedQuestions = (source.SelectedQuestions ?? new List<QuizCreatorSelectedQuestionDto>())
                    .Where(item => item.QuestionId != Guid.Empty)
                    .GroupBy(item => item.QuestionId)
                    .Select(group => group.First())
                    .Select(item => new QuizCreatorSelectedQuestionDto
                    {
                        QuestionId = item.QuestionId,
                        QuizTitle = string.IsNullOrWhiteSpace(item.QuizTitle) ? string.Empty : item.QuizTitle.Trim(),
                        QuestionText = string.IsNullOrWhiteSpace(item.QuestionText) ? string.Empty : item.QuestionText.Trim()
                    })
                    .ToList(),
                QuestionCount = questionCount,
                MaxQuestionCount = maxQuestionCount,
                TimeLimitMinutes = Math.Max(0, source.TimeLimitMinutes),
                PassingScorePercent = source.PassingScorePercent < 0 ? 0 : source.PassingScorePercent,
                RandomizeAnswers = source.RandomizeAnswers,
                ShowCorrectAnswersAtEnd = source.ShowCorrectAnswersAtEnd,
                AccessCodeRequired = !string.IsNullOrWhiteSpace(accessCode),
                AccessCode = accessCode
            };
        }
    }
}
