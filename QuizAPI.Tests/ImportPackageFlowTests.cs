using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuizAPI.Data;
using QuizAPI.Models;
using QuizAPI.Services;
using Xunit;

namespace QuizAPI.Tests;

public sealed class ImportPackageFlowTests : IClassFixture<QuizApiApplicationFactory>
{
    private readonly QuizApiApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ImportPackageFlowTests(QuizApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Endpoint_Returns_Ok_Without_Authentication()
    {
        await _factory.InitializeAsync();

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Local_Login_Remains_Primary_When_Active_Directory_Is_Enabled()
    {
        await _factory.InitializeAsync();

        var fakeAd = new FakeActiveDirectoryAuthService();
        using var app = CreateFactoryWithActiveDirectory(fakeAd);
        using var client = app.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "local.only@example.com",
            FirstName = "Local",
            LastName = "Only",
            Password = "Employee123!"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "local.only@example.com",
            Password = "Employee123!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(0, fakeAd.CallCount);
    }

    [Fact]
    public async Task Active_Directory_Login_Auto_Provisions_Local_User_And_Maps_Roles()
    {
        await _factory.InitializeAsync();

        var fakeAd = new FakeActiveDirectoryAuthService
        {
            Result = new ActiveDirectoryAuthResult
            {
                Email = "employee.ad@example.com",
                UserName = "employee.ad@example.com",
                FirstName = "Taylor",
                LastName = "Domain",
                Groups = new List<string> { "TCM_Admins", "TCM_Users" },
                Roles = new List<string> { "Admin", "User" }
            }
        };

        using var app = CreateFactoryWithActiveDirectory(fakeAd);
        using var client = app.CreateClient();

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "employee.ad@example.com",
            Password = "DomainPassword123!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(1, fakeAd.CallCount);

        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var provisionedUser = await userManager.FindByEmailAsync("employee.ad@example.com");

        Assert.NotNull(provisionedUser);
        Assert.Equal("Taylor", provisionedUser!.FirstName);
        Assert.Equal("Domain", provisionedUser.LastName);

        var roles = await userManager.GetRolesAsync(provisionedUser);
        Assert.Contains("Admin", roles);
        Assert.Contains("User", roles);
    }

    [Fact]
    public async Task SamplePackage_Imports_And_Returns_Image_Urls_EndToEnd()
    {
        await _factory.InitializeAsync();

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var packagePath = BuildSamplePackage();
        try
        {
            using var form = new MultipartFormDataContent();
            await using var packageStream = File.OpenRead(packagePath);
            using var packageContent = new StreamContent(packageStream);
            packageContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(packageContent, "File", Path.GetFileName(packagePath));

            using var uploadResponse = await client.PostAsync("/api/import/upload-package", form);
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);

            var upload = await uploadResponse.Content.ReadFromJsonAsync<UploadPackageResponse>(_jsonOptions);
            Assert.NotNull(upload);
            Assert.False(string.IsNullOrWhiteSpace(upload!.CsvFileName));
            Assert.True(upload.ImagesSaved >= 1);

            using var processResponse = await client.PostAsync($"/api/import/process/{Uri.EscapeDataString(upload.CsvFileName)}", content: null);
            Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);

            var quizzes = await client.GetFromJsonAsync<List<QuizListItem>>("/api/quiz?category=Safety", _jsonOptions);
            Assert.NotNull(quizzes);

            var sampleQuiz = quizzes!.Single(q => q.Title == "Sample Safety Quiz");

            var quiz = await client.GetFromJsonAsync<RandomizedQuizResponse>($"/api/quiz/{sampleQuiz.Id}/random", _jsonOptions);
            Assert.NotNull(quiz);
            Assert.True(quiz!.Questions.Count >= 2);

            var imageQuestion = quiz.Questions.FirstOrDefault(q => q.Images.Count > 0);
            Assert.NotNull(imageQuestion);

            var image = Assert.Single(imageQuestion!.Images);
            Assert.False(string.IsNullOrWhiteSpace(image.Url));
            Assert.Contains("/uploads/images/", image.Url, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("/forklift-safety.svg", image.Url, StringComparison.OrdinalIgnoreCase);

            using var imageResponse = await client.GetAsync(image.Url);
            Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
            Assert.Equal("image/svg+xml", imageResponse.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public async Task UploadPackage_Rejects_Zip_With_Multiple_Csv_Files()
    {
        await _factory.InitializeAsync();

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var packagePath = BuildMalformedPackageWithMultipleCsvFiles();
        try
        {
            using var form = new MultipartFormDataContent();
            await using var packageStream = File.OpenRead(packagePath);
            using var packageContent = new StreamContent(packageStream);
            packageContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(packageContent, "File", Path.GetFileName(packagePath));

            using var uploadResponse = await client.PostAsync("/api/import/upload-package", form);
            Assert.Equal(HttpStatusCode.BadRequest, uploadResponse.StatusCode);

            var body = await uploadResponse.Content.ReadAsStringAsync();
            Assert.Contains("exactly one .csv file", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Next step", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public async Task PreEmployment_Config_Can_Be_Saved_And_Used_For_MultiQuiz_Generation()
    {
        await _factory.InitializeAsync();

        Guid quizId1;
        Guid quizId2;
        const string quizTitle1 = "Warehouse Readiness";
        const string quizTitle2 = "Safety Essentials";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var quiz1 = new Quiz
            {
                Title = quizTitle1,
                Category = "Operations",
                Questions = Enumerable.Range(1, 5).Select(i => new Question
                {
                    Text = $"Warehouse Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            var quiz2 = new Quiz
            {
                Title = quizTitle2,
                Category = "Safety",
                Questions = Enumerable.Range(1, 4).Select(i => new Question
                {
                    Text = $"Safety Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.AddRange(quiz1, quiz2);
            await db.SaveChangesAsync();
            quizId1 = quiz1.Id;
            quizId2 = quiz2.Id;
        }

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var saveRequest = new
        {
            Title = "Hiring Assessment",
            QuizIds = new[] { quizId1, quizId2 },
            QuestionCount = 3,
            TimeLimitMinutes = 15,
            PassingScorePercent = 80,
            RandomizeAnswers = true,
            ShowCorrectAnswersAtEnd = false
        };

        using var saveResponse = await client.PostAsJsonAsync("/api/preemployment/config", saveRequest);
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var saved = await saveResponse.Content.ReadFromJsonAsync<PreEmploymentConfigResponse>(_jsonOptions);
        Assert.NotNull(saved);
        Assert.Null(saved!.QuizId);
        Assert.Contains(quizId1, saved.QuizIds);
        Assert.Contains(quizId2, saved.QuizIds);
        Assert.Contains(quizTitle1, saved.QuizTitles);
        Assert.Contains(quizTitle2, saved.QuizTitles);
        Assert.Equal(3, saved.QuestionCount);

        client.DefaultRequestHeaders.Authorization = null;

        var loaded = await client.GetFromJsonAsync<PreEmploymentConfigResponse>("/api/preemployment/config", _jsonOptions);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.QuizId);
        Assert.Contains(quizId1, loaded.QuizIds);
        Assert.Contains(quizId2, loaded.QuizIds);
        Assert.Contains(quizTitle1, loaded.QuizTitles);
        Assert.Contains(quizTitle2, loaded.QuizTitles);
        Assert.Equal(3, loaded.QuestionCount);

        var generateRequest = new
        {
            QuizIds = loaded.QuizIds,
            QuestionCount = loaded.QuestionCount,
            Title = loaded.Title
        };

        using var generateResponse = await client.PostAsJsonAsync("/api/preemployment/generate", generateRequest);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var generated = await generateResponse.Content.ReadFromJsonAsync<PreEmploymentQuizResponse>(_jsonOptions);
        Assert.NotNull(generated);
        Assert.Equal("Hiring Assessment", generated!.Title);
        Assert.Null(generated.QuizId);
        Assert.Contains(quizId1, generated.QuizIds);
        Assert.Contains(quizId2, generated.QuizIds);
        Assert.Contains(quizTitle1, generated.SourceQuizTitles);
        Assert.Contains(quizTitle2, generated.SourceQuizTitles);
        Assert.Equal(6, generated.QuestionCount);
        Assert.Equal(6, generated.Questions.Count);
        Assert.Equal(3, generated.Questions.Count(q => q.Text.StartsWith("Warehouse Question ", StringComparison.Ordinal)));
        Assert.Equal(3, generated.Questions.Count(q => q.Text.StartsWith("Safety Question ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PreEmployment_Submit_Sends_Notification_Email_When_Smtp_Is_Configured()
    {
        await _factory.InitializeAsync();

        var emailService = _factory.Services.GetRequiredService<TestEmailService>();
        emailService.SentEmails.Clear();

        Guid quizId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var quiz = new Quiz
            {
                Title = "Notification Quiz",
                Category = "Safety",
                Questions = Enumerable.Range(1, 3).Select(i => new Question
                {
                    Text = $"Notification Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        }

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var smtpSave = new
        {
            Host = "smtp.example.com",
            Port = 25,
            UseStartTls = false,
            UseSsl = false,
            Username = "",
            Password = "",
            KeepExistingPasswordWhenBlank = true,
            FromEmail = "noreply@example.com",
            FromName = "QuizAPI Tests",
            NotificationEmail = "hr@example.com"
        };

        using var smtpResponse = await client.PostAsJsonAsync("/api/admin/smtp", smtpSave);
        Assert.Equal(HttpStatusCode.OK, smtpResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;

        var generateRequest = new
        {
            QuizIds = new[] { quizId },
            QuestionCount = 3,
            Title = "Notification Assessment"
        };

        using var generateResponse = await client.PostAsJsonAsync("/api/preemployment/generate", generateRequest);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var generated = await generateResponse.Content.ReadFromJsonAsync<PreEmploymentQuizResponse>(_jsonOptions);
        Assert.NotNull(generated);
        Assert.Equal(3, generated!.Questions.Count);

        var submitRequest = new
        {
            FirstName = "Casey",
            LastName = "Jordan",
            Answers = generated.Questions.Select(q => new
            {
                QuestionId = q.QuestionId,
                SelectedAnswerIds = q.Answers.Take(1).Select(a => a.AnswerId).ToArray()
            }).ToArray()
        };

        using var submitResponse = await client.PostAsJsonAsync("/api/preemployment/submit", submitRequest);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var sent = Assert.Single(emailService.SentEmails);
        Assert.Equal("hr@example.com", sent.ToEmail);
        Assert.Equal("Pre-Employment Quiz Submitted", sent.Subject);
        Assert.Contains("A pre-employment quiz has been submitted.", sent.BodyText, StringComparison.Ordinal);
        Assert.Contains("Casey Jordan", sent.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_Can_Load_And_Update_Questions_In_Question_Editor()
    {
        await _factory.InitializeAsync();

        Guid quizId;
        Guid questionId;
        Guid answerId1;
        Guid answerId2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var quiz = new Quiz
            {
                Title = "Question Editor Quiz",
                Category = "Admin",
                Questions = new List<Question>
                {
                    new()
                    {
                        Text = "Original question text",
                        OrderIndex = 1,
                        AllowMultiple = false,
                        Answers = new List<Answer>
                        {
                            new() { Text = "Original answer 1", IsCorrect = true, OrderIndex = 0 },
                            new() { Text = "Original answer 2", IsCorrect = false, OrderIndex = 1 }
                        },
                        Images = new List<Image>
                        {
                            new() { FileName = "original-image.png", ContentType = "image/png", Url = "/uploads/images/pkg/original-image.png" }
                        }
                    }
                }
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();

            quizId = quiz.Id;
            questionId = quiz.Questions[0].Id;
            answerId1 = quiz.Questions[0].Answers[0].Id;
            answerId2 = quiz.Questions[0].Answers[1].Id;
        }

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var page = await client.GetFromJsonAsync<QuestionEditorPageResponse>($"/api/admin/question-editor?quizId={quizId}&page=1&pageSize=10", _jsonOptions);
        Assert.NotNull(page);
        var editorItem = Assert.Single(page!.Items);
        Assert.Equal("Original question text", editorItem.QuestionText);
        Assert.Equal("original-image.png", editorItem.QuestionImgKey);

        using var updateResponse = await client.PutAsJsonAsync($"/api/admin/question-editor/{questionId}", new
        {
            QuestionText = "Updated question text",
            QuestionImgKey = "updated-image.svg",
            Answers = new[]
            {
                new { Id = answerId1, AnswerText = "Updated answer 1", IsCorrect = false },
                new { Id = answerId2, AnswerText = "Updated answer 2", IsCorrect = true }
            }
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            var question = await db.Questions
                .Include(q => q.Answers)
                .Include(q => q.Images)
                .SingleAsync(q => q.Id == questionId);

            Assert.Equal("Updated question text", question.Text);
            Assert.Equal("Updated answer 1", question.Answers.Single(a => a.Id == answerId1).Text);
            Assert.False(question.Answers.Single(a => a.Id == answerId1).IsCorrect);
            Assert.Equal("Updated answer 2", question.Answers.Single(a => a.Id == answerId2).Text);
            Assert.True(question.Answers.Single(a => a.Id == answerId2).IsCorrect);
            var image = Assert.Single(question.Images);
            Assert.Equal("updated-image.svg", image.FileName);
            Assert.Equal("/uploads/images/pkg/updated-image.svg", image.Url);
        }
    }

    [Fact]
    public async Task Admin_Can_Create_Custom_Quiz_From_Basic_Source_Questions()
    {
        await _factory.InitializeAsync();

        Guid basicQuizId1;
        Guid basicQuizId2;
        Guid nonBasicQuizId;
        Guid selectedQuestionId1;
        Guid selectedQuestionId2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var basicQuiz1 = new Quiz
            {
                Title = "Basic Security",
                Category = "Security",
                Questions = new List<Question>
                {
                    new()
                    {
                        Text = "Security question one",
                        OrderIndex = 1,
                        Answers = new List<Answer>
                        {
                            new() { Text = "Security answer A", IsCorrect = true, OrderIndex = 0 },
                            new() { Text = "Security answer B", IsCorrect = false, OrderIndex = 1 }
                        }
                    }
                }
            };

            var basicQuiz2 = new Quiz
            {
                Title = "Basic Networking",
                Category = "Networking",
                Questions = new List<Question>
                {
                    new()
                    {
                        Text = "Networking question one",
                        OrderIndex = 1,
                        Answers = new List<Answer>
                        {
                            new() { Text = "Networking answer A", IsCorrect = false, OrderIndex = 0 },
                            new() { Text = "Networking answer B", IsCorrect = true, OrderIndex = 1 }
                        }
                    }
                }
            };

            var nonBasicQuiz = new Quiz
            {
                Title = "Advanced Systems",
                Category = "Systems",
                Questions = new List<Question>
                {
                    new()
                    {
                        Text = "Advanced systems question",
                        OrderIndex = 1,
                        Answers = new List<Answer>
                        {
                            new() { Text = "Advanced answer A", IsCorrect = true, OrderIndex = 0 }
                        }
                    }
                }
            };

            db.Quizzes.AddRange(basicQuiz1, basicQuiz2, nonBasicQuiz);
            await db.SaveChangesAsync();

            basicQuizId1 = basicQuiz1.Id;
            basicQuizId2 = basicQuiz2.Id;
            nonBasicQuizId = nonBasicQuiz.Id;
            selectedQuestionId1 = basicQuiz1.Questions[0].Id;
            selectedQuestionId2 = basicQuiz2.Questions[0].Id;
        }

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var sourceQuizzes = await client.GetFromJsonAsync<List<QuizCreatorSourceQuizResponse>>("/api/admin/quiz-creator/source-quizzes", _jsonOptions);
        Assert.NotNull(sourceQuizzes);
        Assert.Contains(sourceQuizzes!, q => q.QuizId == basicQuizId1);
        Assert.Contains(sourceQuizzes!, q => q.QuizId == basicQuizId2);
        Assert.DoesNotContain(sourceQuizzes!, q => q.QuizId == nonBasicQuizId);

        var sourceQuestions = await client.GetFromJsonAsync<QuizCreatorSourceQuestionPageResponse>($"/api/admin/quiz-creator/source-questions?quizId={basicQuizId1}&page=1&pageSize=10", _jsonOptions);
        Assert.NotNull(sourceQuestions);
        Assert.Contains(sourceQuestions!.Items, q => q.QuestionId == selectedQuestionId1 && q.QuestionText == "Security question one");

        using var createResponse = await client.PostAsJsonAsync("/api/admin/quiz-creator/create", new
        {
            Title = "Custom Basic Review",
            Category = "Custom Created",
            PassThresholdPercent = 75,
            QuestionIds = new[] { selectedQuestionId1, selectedQuestionId2 }
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<QuizCreatorCreateResponse>(_jsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Custom Basic Review", created!.Title);
        Assert.Equal(2, created.QuestionCount);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            var quiz = await db.Quizzes
                .Include(q => q.Questions)
                    .ThenInclude(qn => qn.Answers)
                .SingleAsync(q => q.Id == created.QuizId);

            Assert.Equal("Custom Basic Review", quiz.Title);
            Assert.Equal("Custom Created", quiz.Category);
            Assert.Equal(75, quiz.PassThresholdPercent);
            Assert.Equal(2, quiz.Questions.Count);
            Assert.Contains(quiz.Questions, q => q.Text == "Security question one");
            Assert.Contains(quiz.Questions, q => q.Text == "Networking question one");
            Assert.Contains(quiz.Questions.SelectMany(q => q.Answers), a => a.Text == "Security answer A" && a.IsCorrect);
            Assert.Contains(quiz.Questions.SelectMany(q => q.Answers), a => a.Text == "Networking answer B" && a.IsCorrect);
        }
    }

    [Fact]
    public async Task QuizSelection_Generates_And_Submits_A_Mixed_Quiz()
    {
        await _factory.InitializeAsync();

        Guid quizId1;
        Guid quizId2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var quiz1 = new Quiz
            {
                Title = "General Knowledge",
                Category = "General",
                Questions = Enumerable.Range(1, 12).Select(i => new Question
                {
                    Text = $"General Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            var quiz2 = new Quiz
            {
                Title = "Workplace Safety",
                Category = "Safety",
                Questions = Enumerable.Range(1, 13).Select(i => new Question
                {
                    Text = $"Safety Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.AddRange(quiz1, quiz2);
            await db.SaveChangesAsync();
            quizId1 = quiz1.Id;
            quizId2 = quiz2.Id;
        }

        using var client = _factory.CreateClient();

        var list = await client.GetFromJsonAsync<List<QuizListItem>>("/api/quiz", _jsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list!, q => q.Id == quizId1 && q.QuestionCount == 12);
        Assert.Contains(list!, q => q.Id == quizId2 && q.QuestionCount == 13);

        var generateRequest = new
        {
            QuizIds = new[] { quizId1, quizId2 },
            QuestionCount = 10,
            AllQuestions = false,
            Title = "Mixed Launch Quiz"
        };

        using var generateResponse = await client.PostAsJsonAsync("/api/quiz/generate-selection", generateRequest);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var generated = await generateResponse.Content.ReadFromJsonAsync<PreEmploymentQuizResponse>(_jsonOptions);
        Assert.NotNull(generated);
        Assert.Equal("Mixed Launch Quiz", generated!.Title);
        Assert.Equal(20, generated.Questions.Count);
        Assert.Equal(10, generated.Questions.Count(q => q.Text.StartsWith("General Question ", StringComparison.Ordinal)));
        Assert.Equal(10, generated.Questions.Count(q => q.Text.StartsWith("Safety Question ", StringComparison.Ordinal)));

        var submitRequest = new
        {
            Answers = generated.Questions.Select(q => new
            {
                QuestionId = q.QuestionId,
                SelectedAnswerIds = q.Answers.Take(1).Select(a => a.AnswerId).ToArray()
            }).ToArray()
        };

        using var submitResponse = await client.PostAsJsonAsync("/api/quiz/submit-selection", submitRequest);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var result = await submitResponse.Content.ReadFromJsonAsync<QuizAttemptResultResponse>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(20, result!.TotalQuestions);
        Assert.Equal(20, result.Questions.Count);
    }

    [Fact]
    public async Task Authenticated_User_Quiz_Attempts_Appear_On_Profile()
    {
        await _factory.InitializeAsync();

        Guid quizId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);
            db.QuizAttempts.RemoveRange(db.QuizAttempts);

            var quiz = new Quiz
            {
                Title = "Employee Review Quiz",
                Category = "Training",
                Questions = Enumerable.Range(1, 3).Select(i => new Question
                {
                    Text = $"Employee Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        }

        using var client = _factory.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "employee@example.com",
            FirstName = "Avery",
            LastName = "Stone",
            Password = "Employee123!"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var token = await LoginAsync(client, "employee@example.com", "Employee123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var randomizedQuiz = await client.GetFromJsonAsync<RandomizedQuizResponse>($"/api/quiz/{quizId}/random", _jsonOptions);
        Assert.NotNull(randomizedQuiz);

        var submitRequest = new
        {
            Answers = randomizedQuiz!.Questions.Select(q => new
            {
                QuestionId = q.QuestionId,
                SelectedAnswerIds = q.Answers.Take(1).Select(a => a.AnswerId).ToArray()
            }).ToArray()
        };

        using var submitResponse = await client.PostAsJsonAsync($"/api/quiz/{quizId}/submit", submitRequest);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/profile", _jsonOptions);
        Assert.NotNull(profile);
        Assert.Equal("employee@example.com", profile!.Email);
        Assert.Equal(1, profile.TotalAttempts);
        Assert.Single(profile.Attempts);
        Assert.Equal("Employee Review Quiz", profile.Attempts[0].QuizTitle);
    }

    [Fact]
    public async Task Authenticated_User_Mixed_Quiz_Attempt_Appears_On_Profile()
    {
        await _factory.InitializeAsync();

        Guid quizId1;
        Guid quizId2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);
            db.QuizAttempts.RemoveRange(db.QuizAttempts);

            var quiz1 = new Quiz
            {
                Title = "Mixed Source A",
                Category = "General",
                Questions = Enumerable.Range(1, 12).Select(i => new Question
                {
                    Text = $"Mixed A Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct A{i}", IsCorrect = true },
                        new() { Text = $"Wrong A{i}", IsCorrect = false }
                    }
                }).ToList()
            };

            var quiz2 = new Quiz
            {
                Title = "Mixed Source B",
                Category = "Safety",
                Questions = Enumerable.Range(1, 12).Select(i => new Question
                {
                    Text = $"Mixed B Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct B{i}", IsCorrect = true },
                        new() { Text = $"Wrong B{i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.AddRange(quiz1, quiz2);
            await db.SaveChangesAsync();
            quizId1 = quiz1.Id;
            quizId2 = quiz2.Id;
        }

        using var client = _factory.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "mixed.employee@example.com",
            FirstName = "Jamie",
            LastName = "Parker",
            Password = "Employee123!"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var token = await LoginAsync(client, "mixed.employee@example.com", "Employee123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var generateResponse = await client.PostAsJsonAsync("/api/quiz/generate-selection", new
        {
            QuizIds = new[] { quizId1, quizId2 },
            QuestionCount = 10,
            AllQuestions = false,
            Title = "Configured Quiz"
        });
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var generated = await generateResponse.Content.ReadFromJsonAsync<PreEmploymentQuizResponse>(_jsonOptions);
        Assert.NotNull(generated);

        using var submitResponse = await client.PostAsJsonAsync("/api/quiz/submit-selection", new
        {
            QuizTitle = "Configured Quiz",
            QuizCategory = "Mixed",
            Answers = generated!.Questions.Select(q => new
            {
                QuestionId = q.QuestionId,
                SelectedAnswerIds = q.Answers.Take(1).Select(a => a.AnswerId).ToArray()
            }).ToArray()
        });
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/profile", _jsonOptions);
        Assert.NotNull(profile);
        Assert.Equal(1, profile!.TotalAttempts);
        Assert.Single(profile.Attempts);
        Assert.Equal("Configured Quiz", profile.Attempts[0].QuizTitle);
    }

    [Fact]
    public async Task Profile_Can_Be_Updated_And_Password_Changed()
    {
        await _factory.InitializeAsync();

        using var client = _factory.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "profile.user@example.com",
            FirstName = "Morgan",
            LastName = "Reed",
            Password = "Employee123!"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var token = await LoginAsync(client, "profile.user@example.com", "Employee123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var updateResponse = await client.PutAsJsonAsync("/api/profile", new
        {
            FirstName = "Taylor",
            LastName = "Brooks"
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var profile = await client.GetFromJsonAsync<UserProfileResponse>("/api/profile?page=1&pageSize=5&sort=title_asc", _jsonOptions);
        Assert.NotNull(profile);
        Assert.Equal("Taylor", profile!.FirstName);
        Assert.Equal("Brooks", profile.LastName);
        Assert.Equal("title_asc", profile.Sort);

        using var passwordChangeResponse = await client.PostAsJsonAsync("/api/profile/change-password", new
        {
            CurrentPassword = "Employee123!",
            NewPassword = "Employee456!"
        });
        Assert.Equal(HttpStatusCode.OK, passwordChangeResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var newToken = await LoginAsync(client, "profile.user@example.com", "Employee456!");
        Assert.False(string.IsNullOrWhiteSpace(newToken));
    }

    [Fact]
    public async Task SignedIn_User_Can_Save_Progress_And_Filter_Mixed_Quiz_By_Difficulty_And_Tag()
    {
        await _factory.InitializeAsync();

        Guid quizId1;
        Guid quizId2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);
            db.QuizProgressEntries.RemoveRange(db.QuizProgressEntries);

            var quiz1 = new Quiz
            {
                Title = "Filter Quiz A",
                Category = "Safety",
                Questions = Enumerable.Range(1, 15).Select(i => new Question
                {
                    Text = $"Hard Safety A{i}",
                    Difficulty = "Hard",
                    Tags = "Safety,Operations",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct A{i}", IsCorrect = true },
                        new() { Text = $"Wrong A{i}", IsCorrect = false }
                    }
                }).Concat(Enumerable.Range(1, 5).Select(i => new Question
                {
                    Text = $"Easy A{i}",
                    Difficulty = "Easy",
                    Tags = "General",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct EasyA{i}", IsCorrect = true },
                        new() { Text = $"Wrong EasyA{i}", IsCorrect = false }
                    }
                })).ToList()
            };

            var quiz2 = new Quiz
            {
                Title = "Filter Quiz B",
                Category = "Compliance",
                Questions = Enumerable.Range(1, 10).Select(i => new Question
                {
                    Text = $"Hard Safety B{i}",
                    Difficulty = "Hard",
                    Tags = "Safety,Compliance",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct B{i}", IsCorrect = true },
                        new() { Text = $"Wrong B{i}", IsCorrect = false }
                    }
                }).Concat(Enumerable.Range(1, 5).Select(i => new Question
                {
                    Text = $"Medium B{i}",
                    Difficulty = "Medium",
                    Tags = "Compliance",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct MediumB{i}", IsCorrect = true },
                        new() { Text = $"Wrong MediumB{i}", IsCorrect = false }
                    }
                })).ToList()
            };

            db.Quizzes.AddRange(quiz1, quiz2);
            await db.SaveChangesAsync();
            quizId1 = quiz1.Id;
            quizId2 = quiz2.Id;
        }

        using var client = _factory.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "progress.user@example.com",
            FirstName = "Logan",
            LastName = "Hayes",
            Password = "Employee123!"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var token = await LoginAsync(client, "progress.user@example.com", "Employee123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var generateResponse = await client.PostAsJsonAsync("/api/quiz/generate-selection", new
        {
            QuizIds = new[] { quizId1, quizId2 },
            QuestionCount = 10,
            AllQuestions = false,
            Title = "Filtered Quiz",
            Difficulty = "Hard",
            Tags = new[] { "Safety" }
        });
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var generated = await generateResponse.Content.ReadFromJsonAsync<GeneratedQuizResponse>(_jsonOptions);
        Assert.NotNull(generated);
        Assert.Equal(20, generated!.Questions.Count);
        Assert.All(generated.Questions, q =>
        {
            Assert.Equal("Hard", q.Difficulty);
            Assert.Contains("Safety", q.Tags);
        });

        using var saveProgressResponse = await client.PostAsJsonAsync("/api/quiz/progress", new
        {
            SessionKey = "session-progress-test",
            QuizId = (Guid?)null,
            QuizTitle = "Filtered Quiz",
            QuizCategory = "Mixed",
            LaunchMode = "selection",
            Quiz = generated,
            Selections = new Dictionary<string, Guid[]>
            {
                [generated.Questions[0].QuestionId.ToString()] = new[] { generated.Questions[0].Answers[0].AnswerId }
            },
            CurrentIndex = 4,
            TimerRemainingSeconds = 540
        });
        Assert.Equal(HttpStatusCode.OK, saveProgressResponse.StatusCode);

        var progress = await client.GetFromJsonAsync<QuizProgressResponse>("/api/quiz/progress/current", _jsonOptions);
        Assert.NotNull(progress);
        Assert.Equal("session-progress-test", progress!.SessionKey);
        Assert.Equal("Filtered Quiz", progress.QuizTitle);
        Assert.Equal("selection", progress.LaunchMode);
        Assert.Equal(4, progress.CurrentIndex);
        Assert.Equal(540, progress.TimerRemainingSeconds);
        Assert.Equal(20, progress.Quiz!.Questions.Count);
    }

    [Fact]
    public async Task Authenticated_Submit_Returns_Pass_Threshold_And_Correct_Answers_For_Review()
    {
        await _factory.InitializeAsync();

        Guid quizId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var quiz = new Quiz
            {
                Title = "Threshold Review Quiz",
                Category = "Training",
                PassThresholdPercent = 80,
                Questions = Enumerable.Range(1, 2).Select(i => new Question
                {
                    Text = $"Threshold Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        }

        using var client = _factory.CreateClient();

        using var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "review.user@example.com",
            FirstName = "Riley",
            LastName = "Ford",
            Password = "Employee123!"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var token = await LoginAsync(client, "review.user@example.com", "Employee123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var randomizedQuiz = await client.GetFromJsonAsync<GeneratedQuizResponse>($"/api/quiz/{quizId}/random", _jsonOptions);
        Assert.NotNull(randomizedQuiz);
        Dictionary<Guid, Guid> correctByQuestion;
        Dictionary<Guid, Guid> wrongByQuestion;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            var questions = await db.Questions
                .AsNoTracking()
                .Include(q => q.Answers)
                .Where(q => q.QuizId == quizId)
                .ToListAsync();

            correctByQuestion = questions.ToDictionary(
                q => q.Id,
                q => q.Answers.Single(a => a.IsCorrect).Id);

            wrongByQuestion = questions.ToDictionary(
                q => q.Id,
                q => q.Answers.Single(a => !a.IsCorrect).Id);
        }

        using var submitResponse = await client.PostAsJsonAsync($"/api/quiz/{quizId}/submit", new
        {
            SessionKey = "threshold-review",
            Answers = new[]
            {
                new
                {
                    QuestionId = randomizedQuiz!.Questions[0].QuestionId,
                    SelectedAnswerIds = new[] { correctByQuestion[randomizedQuiz.Questions[0].QuestionId] }
                },
                new
                {
                    QuestionId = randomizedQuiz.Questions[1].QuestionId,
                    SelectedAnswerIds = new[] { wrongByQuestion[randomizedQuiz.Questions[1].QuestionId] }
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var result = await submitResponse.Content.ReadFromJsonAsync<QuizSubmitReviewResponse>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(80, result!.PassThresholdPercent);
        Assert.False(result.Passed);
        Assert.Equal(2, result.Questions.Count);
        Assert.All(result.Questions, q => Assert.NotEmpty(q.CorrectAnswerIds));
    }

    [Fact]
    public async Task Archived_Quiz_Is_Hidden_From_Public_List_And_Can_Be_Restored()
    {
        await _factory.InitializeAsync();

        Guid quizId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);

            var quiz = new Quiz
            {
                Title = "Archive Test Quiz",
                Category = "Compliance",
                Questions = new List<Question>
                {
                    new()
                    {
                        Text = "Archive me",
                        AllowMultiple = false,
                        Answers = new List<Answer>
                        {
                            new() { Text = "Yes", IsCorrect = true },
                            new() { Text = "No", IsCorrect = false }
                        }
                    }
                }
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        }

        using var client = _factory.CreateClient();
        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var visibleBeforeArchive = await client.GetFromJsonAsync<List<QuizListItem>>("/api/quiz", _jsonOptions);
        Assert.Contains(visibleBeforeArchive!, q => q.Id == quizId);

        using var archiveResponse = await client.PostAsync($"/api/quiz/{quizId}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var visibleAfterArchive = await client.GetFromJsonAsync<List<QuizListItem>>("/api/quiz", _jsonOptions);
        Assert.DoesNotContain(visibleAfterArchive!, q => q.Id == quizId);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var allQuizzes = await client.GetFromJsonAsync<List<QuizListItem>>("/api/quiz?includeArchived=true", _jsonOptions);
        Assert.Contains(allQuizzes!, q => q.Id == quizId && q.IsArchived);

        using var restoreResponse = await client.PostAsync($"/api/quiz/{quizId}/restore", content: null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var visibleAfterRestore = await client.GetFromJsonAsync<List<QuizListItem>>("/api/quiz", _jsonOptions);
        Assert.Contains(visibleAfterRestore!, q => q.Id == quizId && !q.IsArchived);
    }

    [Fact]
    public async Task PreEmployment_Submissions_Are_Stored_And_Visible_To_Admin()
    {
        await _factory.InitializeAsync();

        Guid quizId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Images.RemoveRange(db.Images);
            db.Answers.RemoveRange(db.Answers);
            db.Questions.RemoveRange(db.Questions);
            db.Quizzes.RemoveRange(db.Quizzes);
            db.PreEmploymentSubmissions.RemoveRange(db.PreEmploymentSubmissions);

            var quiz = new Quiz
            {
                Title = "Candidate Review Quiz",
                Category = "Screening",
                Questions = Enumerable.Range(1, 2).Select(i => new Question
                {
                    Text = $"Candidate Question {i}",
                    AllowMultiple = false,
                    Answers = new List<Answer>
                    {
                        new() { Text = $"Correct {i}", IsCorrect = true },
                        new() { Text = $"Wrong {i}", IsCorrect = false }
                    }
                }).ToList()
            };

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        }

        using var client = _factory.CreateClient();

        using var generateResponse = await client.PostAsJsonAsync("/api/preemployment/generate", new
        {
            QuizIds = new[] { quizId },
            QuestionCount = 2,
            Title = "Candidate Intake"
        });
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);

        var generated = await generateResponse.Content.ReadFromJsonAsync<PreEmploymentQuizResponse>(_jsonOptions);
        Assert.NotNull(generated);

        using var submitResponse = await client.PostAsJsonAsync("/api/preemployment/submit", new
        {
            FirstName = "Jordan",
            LastName = "Miles",
            Answers = generated!.Questions.Select(q => new
            {
                QuestionId = q.QuestionId,
                SelectedAnswerIds = q.Answers.Take(1).Select(a => a.AnswerId).ToArray()
            }).ToArray()
        });
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var token = await LoginAsAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var submissions = await client.GetFromJsonAsync<List<PreEmploymentSubmissionResponse>>("/api/preemployment/submissions?take=10", _jsonOptions);
        Assert.NotNull(submissions);
        Assert.Contains(submissions!, s => s.FirstName == "Jordan" && s.LastName == "Miles" && s.QuizTitle == "Pre-Employment Quiz");
    }

    private async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        return await LoginAsync(client, _factory.SeedAdminEmail, _factory.SeedAdminPassword);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactoryWithActiveDirectory(FakeActiveDirectoryAuthService fakeAdService)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ActiveDirectory:Enabled"] = "true",
                    ["ActiveDirectory:DefaultRole"] = "User",
                    ["ActiveDirectory:AdminGroups:0"] = "TCM_Admins",
                    ["ActiveDirectory:UserGroups:0"] = "TCM_Users"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IActiveDirectoryAuthService>();
                services.AddSingleton<IActiveDirectoryAuthService>(fakeAdService);
            });
        });
    }

    private async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login!.Token));
        return login.Token;
    }

    private static string BuildSamplePackage()
    {
        var sampleDir = Path.Combine(AppContext.BaseDirectory, "Samples", "ImportPackage");
        var csvPath = Path.Combine(sampleDir, "quiz.csv");
        var imagePath = Path.Combine(sampleDir, "forklift-safety.svg");

        Assert.True(File.Exists(csvPath), $"Sample CSV was not copied to test output: {csvPath}");
        Assert.True(File.Exists(imagePath), $"Sample image was not copied to test output: {imagePath}");

        var zipPath = Path.Combine(Path.GetTempPath(), $"sample-import-package-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(csvPath, "quiz.csv");
        archive.CreateEntryFromFile(imagePath, "forklift-safety.svg");
        return zipPath;
    }

    private static string BuildMalformedPackageWithMultipleCsvFiles()
    {
        var sampleDir = Path.Combine(AppContext.BaseDirectory, "Samples", "ImportPackage");
        var csvPath = Path.Combine(sampleDir, "quiz.csv");
        var imagePath = Path.Combine(sampleDir, "forklift-safety.svg");

        Assert.True(File.Exists(csvPath), $"Sample CSV was not copied to test output: {csvPath}");
        Assert.True(File.Exists(imagePath), $"Sample image was not copied to test output: {imagePath}");

        var zipPath = Path.Combine(Path.GetTempPath(), $"malformed-import-package-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(csvPath, "quiz.csv");
        archive.CreateEntryFromFile(csvPath, "duplicate.csv");
        archive.CreateEntryFromFile(imagePath, "forklift-safety.svg");
        return zipPath;
    }

    private sealed class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
    }

    private sealed class UploadPackageResponse
    {
        public string CsvFileName { get; set; } = string.Empty;
        public int ImagesSaved { get; set; }
    }

    private sealed class QuizListItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        public bool IsArchived { get; set; }
    }

    private sealed class RandomizedQuizResponse
    {
        public List<QuestionResponse> Questions { get; set; } = new();
    }

    private sealed class GeneratedQuizResponse
    {
        public Guid? QuizId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double PassThresholdPercent { get; set; }
        public List<QuestionResponse> Questions { get; set; } = new();
    }

    private sealed class PreEmploymentConfigResponse
    {
        public string Title { get; set; } = string.Empty;
        public Guid? QuizId { get; set; }
        public List<Guid> QuizIds { get; set; } = new();
        public string QuizTitle { get; set; } = string.Empty;
        public List<string> QuizTitles { get; set; } = new();
        public int QuestionCount { get; set; }
    }

    private sealed class PreEmploymentQuizResponse
    {
        public string Title { get; set; } = string.Empty;
        public Guid? QuizId { get; set; }
        public List<Guid> QuizIds { get; set; } = new();
        public string SourceQuizTitle { get; set; } = string.Empty;
        public List<string> SourceQuizTitles { get; set; } = new();
        public int QuestionCount { get; set; }
        public List<QuestionResponse> Questions { get; set; } = new();
    }

    private sealed class QuestionResponse
    {
        public Guid QuestionId { get; set; }
        public List<AnswerResponse> Answers { get; set; } = new();
        public string Text { get; set; } = string.Empty;
        public List<ImageResponse> Images { get; set; } = new();
        public string Difficulty { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
    }

    private sealed class AnswerResponse
    {
        public Guid AnswerId { get; set; }
    }

    private sealed class QuizAttemptResultResponse
    {
        public int TotalQuestions { get; set; }
        public List<QuestionResultResponse> Questions { get; set; } = new();
    }

    private sealed class QuizSubmitReviewResponse
    {
        public double PassThresholdPercent { get; set; }
        public bool Passed { get; set; }
        public List<QuestionReviewResponse> Questions { get; set; } = new();
    }

    private sealed class QuestionReviewResponse
    {
        public List<Guid> CorrectAnswerIds { get; set; } = new();
    }

    private sealed class QuizProgressResponse
    {
        public string SessionKey { get; set; } = string.Empty;
        public string QuizTitle { get; set; } = string.Empty;
        public string LaunchMode { get; set; } = string.Empty;
        public int CurrentIndex { get; set; }
        public int? TimerRemainingSeconds { get; set; }
        public GeneratedQuizResponse? Quiz { get; set; }
    }

    private sealed class UserProfileResponse
    {
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int TotalAttempts { get; set; }
        public string Sort { get; set; } = string.Empty;
        public List<UserAttemptResponse> Attempts { get; set; } = new();
    }

    private sealed class PreEmploymentSubmissionResponse
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string QuizTitle { get; set; } = string.Empty;
    }

    private sealed class UserAttemptResponse
    {
        public string QuizTitle { get; set; } = string.Empty;
    }

    private sealed class QuestionResultResponse
    {
        public Guid QuestionId { get; set; }
    }

    private sealed class ImageResponse
    {
        public string Url { get; set; } = string.Empty;
    }

    private sealed class QuestionEditorPageResponse
    {
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public List<QuestionEditorItemResponse> Items { get; set; } = new();
    }

    private sealed class QuestionEditorItemResponse
    {
        public Guid QuestionId { get; set; }
        public Guid QuizId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string QuestionImgKey { get; set; } = string.Empty;
        public List<QuestionEditorAnswerResponse> Answers { get; set; } = new();
    }

    private sealed class QuestionEditorAnswerResponse
    {
        public Guid Id { get; set; }
        public string AnswerText { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    private sealed class QuizCreatorSourceQuizResponse
    {
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
    }

    private sealed class QuizCreatorSourceQuestionPageResponse
    {
        public Guid QuizId { get; set; }
        public string QuizTitle { get; set; } = string.Empty;
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public List<QuizCreatorSourceQuestionResponse> Items { get; set; } = new();
    }

    private sealed class QuizCreatorSourceQuestionResponse
    {
        public Guid QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
    }

    private sealed class QuizCreatorCreateResponse
    {
        public Guid QuizId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
    }

    private sealed class FakeActiveDirectoryAuthService : IActiveDirectoryAuthService
    {
        public int CallCount { get; private set; }
        public ActiveDirectoryAuthResult? Result { get; set; }

        public Task<ActiveDirectoryAuthResult?> AuthenticateAsync(string login, string password, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}
