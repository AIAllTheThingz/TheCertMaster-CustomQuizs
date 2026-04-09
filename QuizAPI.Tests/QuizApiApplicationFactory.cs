using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuizAPI.Data;
using QuizAPI.Models;
using QuizAPI.Services;
using System.Reflection;
using System.Data.Common;
using Xunit;

namespace QuizAPI.Tests;

public sealed class QuizApiApplicationFactory : WebApplicationFactory<Program>
{
    private const string AdminEmail = "admin@example.com";
    private const string AdminPassword = "Admin123!Pass";
    private const string JwtIssuer = "QuizApiTests";
    private const string JwtAudience = "QuizApiTestsAudience";
    private const string JwtKey = "0123456789ABCDEF0123456789ABCDEF";
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "QuizApiTests", Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private bool _initialized;

    public string SeedAdminEmail => AdminEmail;
    public string SeedAdminPassword => AdminPassword;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "App_Data"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "wwwroot", "uploads"));

        builder.UseEnvironment("Development");
        builder.UseContentRoot(_tempRoot);
        builder.UseWebRoot(Path.Combine(_tempRoot, "wwwroot"));

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "QuizApiTests",
                ["Jwt:Audience"] = "QuizApiTestsAudience",
                ["Jwt:Key"] = "0123456789ABCDEF0123456789ABCDEF",
                ["Database:AutoMigrateOnStartup"] = "false",
                ["RateLimiting:Auth:PermitLimit"] = "1000",
                ["RateLimiting:Auth:WindowMinutes"] = "1",
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=UnusedForTests;Trusted_Connection=True;",
                ["Cors:AllowedOrigins:0"] = "http://localhost"
            });
        });

        builder.ConfigureServices(services =>
        {
            var sqlServerDescriptors = services
                .Where(descriptor =>
                    IsSqlServerAssembly(descriptor.ServiceType.Assembly) ||
                    IsSqlServerAssembly(descriptor.ImplementationType?.Assembly) ||
                    IsSqlServerAssembly(descriptor.ImplementationInstance?.GetType().Assembly))
                .ToList();

            foreach (var descriptor in sqlServerDescriptors)
            {
                services.Remove(descriptor);
            }

            var dbContextOptionDescriptors = services
                .Where(descriptor =>
                    descriptor.ServiceType.IsGenericType &&
                    string.Equals(descriptor.ServiceType.GetGenericTypeDefinition().FullName,
                        "Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration`1",
                        StringComparison.Ordinal))
                .ToList();

            foreach (var descriptor in dbContextOptionDescriptors)
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<QuizDbContext>();
            services.RemoveAll<DbContextOptions<QuizDbContext>>();
            services.RemoveAll<DbConnection>();
            services.RemoveAll<IEmailService>();
            services.RemoveAll<TestEmailService>();

            if (_connection.State != System.Data.ConnectionState.Open)
            {
                _connection.Open();
            }

            services.AddSingleton<DbConnection>(_connection);

            services.AddDbContext<QuizDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });
            services.AddSingleton<TestEmailService>();
            services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<TestEmailService>());

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
            db.Database.EnsureCreated();
        });
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        Environment.SetEnvironmentVariable("Database__AutoMigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "1000");
        Environment.SetEnvironmentVariable("RateLimiting__Auth__WindowMinutes", "1");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=(localdb)\\mssqllocaldb;Database=UnusedForTests;Trusted_Connection=True;");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost");

        await _connection.OpenAsync();
        _ = CreateClient();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        if (!await roleManager.RoleExistsAsync("User"))
            await roleManager.CreateAsync(new IdentityRole("User"));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await userManager.FindByEmailAsync(AdminEmail);
        if (admin == null)
        {
            admin = new AppUser
            {
                UserName = AdminEmail,
                Email = AdminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(admin, AdminPassword);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to create seeded admin user: " +
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
        }

        var roles = await userManager.GetRolesAsync(admin);
        if (!roles.Contains("Admin"))
        {
            var addRoleResult = await userManager.AddToRoleAsync(admin, "Admin");
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to assign Admin role: " +
                    string.Join("; ", addRoleResult.Errors.Select(e => e.Description)));
            }
        }

        _initialized = true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__Key", null);
        Environment.SetEnvironmentVariable("Database__AutoMigrateOnStartup", null);
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", null);
        Environment.SetEnvironmentVariable("RateLimiting__Auth__WindowMinutes", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", null);
        _connection.Dispose();

        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static bool IsSqlServerAssembly(Assembly? assembly)
    {
        var name = assembly?.GetName().Name;
        return !string.IsNullOrWhiteSpace(name) &&
               name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class TestEmailService : IEmailService
{
    public List<SentEmail> SentEmails { get; } = new();

    public Task SendAsync(string toEmail, string subject, string bodyText)
    {
        SentEmails.Add(new SentEmail
        {
            ToEmail = toEmail,
            Subject = subject,
            BodyText = bodyText
        });

        return Task.CompletedTask;
    }
}

public sealed class SentEmail
{
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
}
