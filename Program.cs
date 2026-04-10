using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Services.AddHealthChecks();

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
    var authPermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 10, 1, 5000);
    var authWindowMinutes = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowMinutes") ?? 5, 1, 60);
    options.AddPolicy("AuthPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
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

// App services
builder.Services.AddScoped<QuizQueryService>();
builder.Services.AddScoped<QuizImportService>();
builder.Services.AddScoped<IPreEmploymentConfigStore, FilePreEmploymentConfigStore>();

// SMTP settings + email service (supports authenticated and unauthenticated relay)
builder.Services.AddScoped<ISmtpSettingsStore, FileSmtpSettingsStore>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();
var swaggerEnabled = builder.Configuration.GetValue<bool?>("Swagger:Enabled") ?? app.Environment.IsDevelopment();

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

app.UseHttpsRedirection();
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

using (var scope = app.Services.CreateScope())
{
    var autoMigrateOnStartup = app.Configuration.GetValue<bool>("Database:AutoMigrateOnStartup");
    if (app.Environment.IsDevelopment() && autoMigrateOnStartup)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<QuizDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    if (!await roleManager.RoleExistsAsync("User"))
        await roleManager.CreateAsync(new IdentityRole("User"));
}

app.Run();

public partial class Program { }
