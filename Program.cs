using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text;
using System.Security.Claims;
using QuizAPI.Data;
using QuizAPI.Models;
using QuizAPI.Services;
using QuizAPI.Middleware;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:Key"];
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(jwtIssuer))
    throw new InvalidOperationException("Jwt:Issuer is required.");

if (string.IsNullOrWhiteSpace(jwtAudience))
    throw new InvalidOperationException("Jwt:Audience is required.");

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key is required and must be at least 32 characters long.");

if (string.IsNullOrWhiteSpace(defaultConnection))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

var jwtSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

// Persist DataProtection keys so cookies/auth survive IIS recycles/reboots
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var keysPath = Path.Combine(appDataPath, "keys");
var appDataUploadsPath = Path.Combine(appDataPath, "uploads");
var webRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var webRootUploadsPath = Path.Combine(webRootPath, "uploads");
var logsPath = Path.Combine(builder.Environment.ContentRootPath, "logs");

Directory.CreateDirectory(appDataPath);
Directory.CreateDirectory(keysPath);
Directory.CreateDirectory(appDataUploadsPath);
Directory.CreateDirectory(webRootPath);
Directory.CreateDirectory(webRootUploadsPath);
Directory.CreateDirectory(logsPath);

var dataProtection = builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("QuizAPI");

if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

// Controllers & JSON
builder.Services.AddControllers()
    .AddJsonOptions(o => { o.JsonSerializerOptions.PropertyNamingPolicy = null; });

// Swagger
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Quiz API",
        Version = "v1",
        Description = "Quiz API with JWT Authentication"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token in the format: Bearer {your token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

const string CorsPolicyName = "ConfiguredCors";
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    return false;

                return uri.IsLoopback;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
            return;
        }

        throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one origin outside Development.");
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authWindowMinutes = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowMinutes") ?? 5, 1, 60);
    var authRegisterPermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:Auth:RegisterPermitLimit") ?? 10, 1, 5000);
    var authLoginPermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:Auth:LoginPermitLimit") ?? 25, 1, 5000);
    var authLoopbackPermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:Auth:LoopbackPermitLimit") ?? 200, 1, 10000);

    options.AddPolicy("AuthRegisterPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"register:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = httpContext.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp)
                    ? authLoopbackPermitLimit
                    : authRegisterPermitLimit,
                Window = TimeSpan.FromMinutes(authWindowMinutes),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthLoginPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"login:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = httpContext.Connection.RemoteIpAddress is { } remoteIp && IPAddress.IsLoopback(remoteIp)
                    ? authLoopbackPermitLimit
                    : authLoginPermitLimit,
                Window = TimeSpan.FromMinutes(authWindowMinutes),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            }));
});

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddEntityFrameworkStores<QuizDbContext>()
    .AddDefaultTokenProviders();

// Disable cookie auth for API calls; use JWT only
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = PathString.Empty;
    options.AccessDeniedPath = PathString.Empty;
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = jwtSigningKey,
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.Name
    };
});

// Call to appsettings.json for SQL Express connection string
builder.Services.AddDbContext<QuizDbContext>(options =>
    options.UseSqlServer(defaultConnection));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyHealthCheck>("sql", tags: new[] { "ready" });

// App services
builder.Services.AddScoped<QuizQueryService>();
builder.Services.AddScoped<QuizImportService>();
builder.Services.AddScoped<IPreEmploymentConfigStore, FilePreEmploymentConfigStore>();
builder.Services.Configure<ActiveDirectoryOptions>(builder.Configuration.GetSection("ActiveDirectory"));
if (OperatingSystem.IsWindows())
{
    builder.Services.AddScoped<IActiveDirectoryAuthService, ActiveDirectoryAuthService>();
}
else
{
    builder.Services.AddScoped<IActiveDirectoryAuthService, NoOpActiveDirectoryAuthService>();
}

// SMTP settings + email service (supports authenticated and unauthenticated relay)
builder.Services.AddScoped<ISmtpSettingsStore, FileSmtpSettingsStore>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();
var swaggerEnabled = builder.Configuration.GetValue<bool?>("Swagger:Enabled") ?? app.Environment.IsDevelopment();
var httpsRedirectionEnabled = builder.Configuration.GetValue<bool?>("HttpsRedirection:Enabled")
    ?? !app.Environment.IsDevelopment();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Quiz API v1");
        c.RoutePrefix = "swagger";
    });
}

if (httpsRedirectionEnabled)
{
    app.UseHttpsRedirection();
}
app.UseCors(CorsPolicyName);
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var autoMigrateOnStartup = app.Configuration.GetValue<bool>("Database:AutoMigrateOnStartup");
    if (app.Environment.IsDevelopment() && autoMigrateOnStartup)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    if (!await roleManager.RoleExistsAsync("User"))
        await roleManager.CreateAsync(new IdentityRole("User"));

    var bootstrapAdminEmail = app.Configuration["BootstrapAdmin:Email"]?.Trim();
    var bootstrapAdminPassword = app.Configuration["BootstrapAdmin:Password"];
    var bootstrapAdminFirstName = app.Configuration["BootstrapAdmin:FirstName"]?.Trim();
    var bootstrapAdminLastName = app.Configuration["BootstrapAdmin:LastName"]?.Trim();

    if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail))
    {
        if (string.IsNullOrWhiteSpace(bootstrapAdminPassword))
        {
            throw new InvalidOperationException("BootstrapAdmin:Password is required when BootstrapAdmin:Email is configured.");
        }

        var bootstrapAdmin = await userManager.FindByEmailAsync(bootstrapAdminEmail);
        if (bootstrapAdmin == null)
        {
            bootstrapAdmin = new AppUser
            {
                UserName = bootstrapAdminEmail,
                Email = bootstrapAdminEmail,
                FirstName = string.IsNullOrWhiteSpace(bootstrapAdminFirstName) ? "Admin" : bootstrapAdminFirstName,
                LastName = string.IsNullOrWhiteSpace(bootstrapAdminLastName) ? "User" : bootstrapAdminLastName,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(bootstrapAdmin, bootstrapAdminPassword);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to create bootstrap admin user: " +
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            var needsUpdate = false;

            if (!string.IsNullOrWhiteSpace(bootstrapAdminFirstName) && bootstrapAdmin.FirstName != bootstrapAdminFirstName)
            {
                bootstrapAdmin.FirstName = bootstrapAdminFirstName;
                needsUpdate = true;
            }

            if (!string.IsNullOrWhiteSpace(bootstrapAdminLastName) && bootstrapAdmin.LastName != bootstrapAdminLastName)
            {
                bootstrapAdmin.LastName = bootstrapAdminLastName;
                needsUpdate = true;
            }

            if (!bootstrapAdmin.EmailConfirmed)
            {
                bootstrapAdmin.EmailConfirmed = true;
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                var updateResult = await userManager.UpdateAsync(bootstrapAdmin);
                if (!updateResult.Succeeded)
                {
                    throw new InvalidOperationException("Failed to update bootstrap admin user: " +
                        string.Join("; ", updateResult.Errors.Select(e => e.Description)));
                }
            }
        }

        if (!await userManager.IsInRoleAsync(bootstrapAdmin, "Admin"))
        {
            var addRoleResult = await userManager.AddToRoleAsync(bootstrapAdmin, "Admin");
            if (!addRoleResult.Succeeded)
            {
                throw new InvalidOperationException("Failed to assign Admin role to bootstrap admin: " +
                    string.Join("; ", addRoleResult.Errors.Select(e => e.Description)));
            }
        }

        app.Logger.LogInformation("Bootstrap admin ready for {Email}.", bootstrapAdminEmail);
    }
}

app.Run();

public partial class Program { }
