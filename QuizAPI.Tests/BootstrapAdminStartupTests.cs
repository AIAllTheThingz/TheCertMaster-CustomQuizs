using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuizAPI.Data;
using QuizAPI.Models;
using Xunit;

namespace QuizAPI.Tests;

[CollectionDefinition("BootstrapAdminStartup", DisableParallelization = true)]
public sealed class BootstrapAdminStartupCollectionDefinition
{
}

[Collection("BootstrapAdminStartup")]
public sealed class BootstrapAdminStartupTests
{
    [Fact]
    public async Task Startup_Creates_Configured_Bootstrap_Admin_When_Missing()
    {
        const string bootstrapAdminEmail = "bootstrap@example.com";
        const string bootstrapAdminPassword = "ConfiguredBootstrap123!";

        using var factory = new BootstrapAdminTestApplicationFactory(
            bootstrapAdminEmail,
            bootstrapAdminPassword,
            "Bootstrap",
            "Owner");
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var bootstrapAdmin = await userManager.FindByEmailAsync(bootstrapAdminEmail);

        Assert.NotNull(bootstrapAdmin);
        Assert.True(bootstrapAdmin!.EmailConfirmed);
        Assert.Equal("Bootstrap", bootstrapAdmin.FirstName);
        Assert.Equal("Owner", bootstrapAdmin.LastName);
        Assert.True(await userManager.CheckPasswordAsync(bootstrapAdmin, bootstrapAdminPassword));
        Assert.True(await userManager.IsInRoleAsync(bootstrapAdmin, "Admin"));

        using var loginResponse = await LoginAsync(client, bootstrapAdminEmail, bootstrapAdminPassword);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Startup_Rotates_Existing_Seeded_Admin_To_Configured_Bootstrap_Password()
    {
        const string bootstrapAdminEmail = "admin@quizapi.local";
        const string configuredBootstrapPassword = "RotatedBootstrap123!";
        const string legacySeededPassword = "LegacySeededPassword123!";

        using var factory = new BootstrapAdminTestApplicationFactory(
            bootstrapAdminEmail,
            configuredBootstrapPassword,
            "Server",
            "Admin",
            legacySeededPassword);
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var bootstrapAdmin = await userManager.FindByEmailAsync(bootstrapAdminEmail);

        Assert.NotNull(bootstrapAdmin);
        Assert.Equal("Server", bootstrapAdmin!.FirstName);
        Assert.Equal("Admin", bootstrapAdmin.LastName);
        Assert.True(await userManager.CheckPasswordAsync(bootstrapAdmin, configuredBootstrapPassword));
        Assert.False(await userManager.CheckPasswordAsync(bootstrapAdmin, legacySeededPassword));

        using var configuredLoginResponse = await LoginAsync(client, bootstrapAdminEmail, configuredBootstrapPassword);
        Assert.Equal(HttpStatusCode.OK, configuredLoginResponse.StatusCode);

        using var legacyLoginResponse = await LoginAsync(client, bootstrapAdminEmail, legacySeededPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, legacyLoginResponse.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        return client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
    }

    private sealed class BootstrapAdminTestApplicationFactory : WebApplicationFactory<Program>
    {
        private const string JwtIssuer = "QuizApiTests";
        private const string JwtAudience = "QuizApiTestsAudience";
        private const string JwtKey = "0123456789ABCDEF0123456789ABCDEF";
        private const string PackagedSeedAdminEmail = "admin@quizapi.local";

        private readonly string _bootstrapAdminEmail;
        private readonly string _bootstrapAdminPassword;
        private readonly string _bootstrapAdminFirstName;
        private readonly string _bootstrapAdminLastName;
        private readonly string? _seedAdminPasswordBeforeStartup;
        private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "QuizApiTests", Guid.NewGuid().ToString("N"));
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public BootstrapAdminTestApplicationFactory(
            string bootstrapAdminEmail,
            string bootstrapAdminPassword,
            string bootstrapAdminFirstName,
            string bootstrapAdminLastName,
            string? seedAdminPasswordBeforeStartup = null)
        {
            _bootstrapAdminEmail = bootstrapAdminEmail;
            _bootstrapAdminPassword = bootstrapAdminPassword;
            _bootstrapAdminFirstName = bootstrapAdminFirstName;
            _bootstrapAdminLastName = bootstrapAdminLastName;
            _seedAdminPasswordBeforeStartup = seedAdminPasswordBeforeStartup;

            Environment.SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
            Environment.SetEnvironmentVariable("Jwt__Audience", JwtAudience);
            Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
            Environment.SetEnvironmentVariable("Database__AutoMigrateOnStartup", "false");
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Server=(localdb)\\mssqllocaldb;Database=UnusedForTests;Trusted_Connection=True;");
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost");
            Environment.SetEnvironmentVariable("BootstrapAdmin__Email", _bootstrapAdminEmail);
            Environment.SetEnvironmentVariable("BootstrapAdmin__Password", _bootstrapAdminPassword);
            Environment.SetEnvironmentVariable("BootstrapAdmin__FirstName", _bootstrapAdminFirstName);
            Environment.SetEnvironmentVariable("BootstrapAdmin__LastName", _bootstrapAdminLastName);
        }

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
                    ["Jwt:Issuer"] = JwtIssuer,
                    ["Jwt:Audience"] = JwtAudience,
                    ["Jwt:Key"] = JwtKey,
                    ["Database:AutoMigrateOnStartup"] = "false",
                    ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=UnusedForTests;Trusted_Connection=True;",
                    ["Cors:AllowedOrigins:0"] = "http://localhost",
                    ["BootstrapAdmin:Email"] = _bootstrapAdminEmail,
                    ["BootstrapAdmin:Password"] = _bootstrapAdminPassword,
                    ["BootstrapAdmin:FirstName"] = _bootstrapAdminFirstName,
                    ["BootstrapAdmin:LastName"] = _bootstrapAdminLastName
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
                        string.Equals(
                            descriptor.ServiceType.GetGenericTypeDefinition().FullName,
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

                if (_connection.State != System.Data.ConnectionState.Open)
                {
                    _connection.Open();
                }

                services.AddSingleton<DbConnection>(_connection);
                services.AddDbContext<QuizDbContext>(options => options.UseSqlite(_connection));

                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
                db.Database.EnsureCreated();

                if (!string.IsNullOrWhiteSpace(_seedAdminPasswordBeforeStartup))
                {
                    SeedExistingPackagedAdmin(scope.ServiceProvider, _seedAdminPasswordBeforeStartup);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            Environment.SetEnvironmentVariable("Jwt__Issuer", null);
            Environment.SetEnvironmentVariable("Jwt__Audience", null);
            Environment.SetEnvironmentVariable("Jwt__Key", null);
            Environment.SetEnvironmentVariable("Database__AutoMigrateOnStartup", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", null);
            Environment.SetEnvironmentVariable("BootstrapAdmin__Email", null);
            Environment.SetEnvironmentVariable("BootstrapAdmin__Password", null);
            Environment.SetEnvironmentVariable("BootstrapAdmin__FirstName", null);
            Environment.SetEnvironmentVariable("BootstrapAdmin__LastName", null);
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

        private static void SeedExistingPackagedAdmin(IServiceProvider services, string password)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            EnsureRoleExists(roleManager, "Admin");
            EnsureRoleExists(roleManager, "User");

            var userManager = services.GetRequiredService<UserManager<AppUser>>();
            var bootstrapAdmin = userManager.FindByEmailAsync(PackagedSeedAdminEmail).GetAwaiter().GetResult();
            if (bootstrapAdmin == null)
            {
                bootstrapAdmin = new AppUser
                {
                    UserName = PackagedSeedAdminEmail,
                    Email = PackagedSeedAdminEmail,
                    FirstName = "Legacy",
                    LastName = "Admin",
                    EmailConfirmed = true
                };

                var createResult = userManager.CreateAsync(bootstrapAdmin, password).GetAwaiter().GetResult();
                EnsureSuccess(createResult, "Failed to seed packaged admin user for test.");
            }

            if (!userManager.IsInRoleAsync(bootstrapAdmin, "Admin").GetAwaiter().GetResult())
            {
                var addRoleResult = userManager.AddToRoleAsync(bootstrapAdmin, "Admin").GetAwaiter().GetResult();
                EnsureSuccess(addRoleResult, "Failed to assign Admin role to seeded packaged admin for test.");
            }
        }

        private static void EnsureRoleExists(RoleManager<IdentityRole> roleManager, string roleName)
        {
            if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
            {
                var createResult = roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                EnsureSuccess(createResult, $"Failed to create role '{roleName}' for test.");
            }
        }

        private static void EnsureSuccess(IdentityResult result, string errorPrefix)
        {
            if (result.Succeeded)
            {
                return;
            }

            throw new InvalidOperationException(errorPrefix + " " +
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        private static bool IsSqlServerAssembly(Assembly? assembly)
        {
            var name = assembly?.GetName().Name;
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        }
    }
}
