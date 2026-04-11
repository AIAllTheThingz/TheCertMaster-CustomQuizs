using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QuizAPI.Models;
using QuizAPI.Services;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private const string DefaultAdProvisionedFirstName = "Directory";
        private const string DefaultAdProvisionedLastName = "User";
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IActiveDirectoryAuthService _activeDirectoryAuthService;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SignInManager<AppUser> signInManager,
            IActiveDirectoryAuthService activeDirectoryAuthService,
            IConfiguration config,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _signInManager = signInManager;
            _activeDirectoryAuthService = activeDirectoryAuthService;
            _config = config;
            _logger = logger;
        }

        public sealed class RegisterRequest
        {
            [EmailAddress]
            public string Email { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;
        }

        public class LoginRequest
        {
            [EmailAddress]
            public string Email { get; set; } = string.Empty;
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;
        }

        [EnableRateLimiting("AuthRegisterPolicy")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var firstName = request?.FirstName?.Trim() ?? string.Empty;
            var lastName = request?.LastName?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                return BadRequest("First name, last name, email, and password are required.");

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Registration failed for {Email}.", email);
                return BadRequest(result.Errors);
            }

            await _userManager.AddToRoleAsync(user, "User");
            _logger.LogInformation("Registered new user {Email}.", email);
            return Ok($"User {email} created with role User");
        }

        [EnableRateLimiting("AuthLoginPolicy")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var email = request?.Email?.Trim() ?? string.Empty;
            var password = request?.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return BadRequest("Email and password are required.");

            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var shouldLockoutOnFailure = !roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
                var valid = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: shouldLockoutOnFailure);
                if (valid.Succeeded)
                {
                    var (token, expiresUtc) = GenerateJwtToken(user, roles);
                    _logger.LogInformation("Login succeeded for {Email} using local Identity.", email);
                    return Ok(new { token, expiresUtc });
                }
            }

            var activeDirectoryUser = await SignInWithActiveDirectoryAsync(email, password);
            if (activeDirectoryUser is null)
            {
                _logger.LogWarning("Login failed for {Email}.", email);
                return Unauthorized("Invalid email or password");
            }

            var (provisionedUser, adRoles) = activeDirectoryUser.Value;
            var (adToken, adExpiresUtc) = GenerateJwtToken(provisionedUser, adRoles);
            _logger.LogInformation("Login succeeded for {Email} using Active Directory.", email);

            return Ok(new { token = adToken, expiresUtc = adExpiresUtc });
        }

        private async Task<(AppUser User, IList<string> Roles)?> SignInWithActiveDirectoryAsync(string email, string password)
        {
            var adResult = await _activeDirectoryAuthService.AuthenticateAsync(email, password, HttpContext.RequestAborted);
            if (adResult is null)
            {
                return null;
            }

            var normalizedEmail = string.IsNullOrWhiteSpace(adResult.Email) ? email : adResult.Email.Trim();
            var user = await _userManager.FindByEmailAsync(normalizedEmail);
            var needsCreate = user is null;

            if (user is null)
            {
                user = new AppUser
                {
                    UserName = normalizedEmail,
                    Email = normalizedEmail,
                    FirstName = string.IsNullOrWhiteSpace(adResult.FirstName) ? DefaultAdProvisionedFirstName : adResult.FirstName.Trim(),
                    LastName = string.IsNullOrWhiteSpace(adResult.LastName) ? DefaultAdProvisionedLastName : adResult.LastName.Trim(),
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    _logger.LogWarning("Active Directory login succeeded for {Email}, but local provisioning failed: {Errors}",
                        normalizedEmail,
                        string.Join("; ", createResult.Errors.Select(e => e.Description)));
                    return null;
                }
            }
            else
            {
                var needsUpdate = false;
                if (!string.Equals(user.UserName, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                {
                    user.UserName = normalizedEmail;
                    needsUpdate = true;
                }

                if (!string.IsNullOrWhiteSpace(adResult.FirstName) && !string.Equals(user.FirstName, adResult.FirstName, StringComparison.Ordinal))
                {
                    user.FirstName = adResult.FirstName.Trim();
                    needsUpdate = true;
                }

                if (!string.IsNullOrWhiteSpace(adResult.LastName) && !string.Equals(user.LastName, adResult.LastName, StringComparison.Ordinal))
                {
                    user.LastName = adResult.LastName.Trim();
                    needsUpdate = true;
                }

                if (!user.EmailConfirmed)
                {
                    user.EmailConfirmed = true;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    var updateResult = await _userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        _logger.LogWarning("Failed to update local profile during Active Directory login for {Email}: {Errors}",
                            normalizedEmail,
                            string.Join("; ", updateResult.Errors.Select(e => e.Description)));
                        return null;
                    }
                }
            }

            var mappedRoles = adResult.Roles.Count > 0
                ? adResult.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { "User" };

            foreach (var role in mappedRoles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    var createRoleResult = await _roleManager.CreateAsync(new IdentityRole(role));
                    if (!createRoleResult.Succeeded)
                    {
                        _logger.LogWarning("Failed to create application role {Role} during Active Directory login for {Email}: {Errors}",
                            role,
                            normalizedEmail,
                            string.Join("; ", createRoleResult.Errors.Select(e => e.Description)));
                        return null;
                    }
                }

                if (!await _userManager.IsInRoleAsync(user, role))
                {
                    var addRoleResult = await _userManager.AddToRoleAsync(user, role);
                    if (!addRoleResult.Succeeded)
                    {
                        _logger.LogWarning("Failed to assign application role {Role} during Active Directory login for {Email}: {Errors}",
                            role,
                            normalizedEmail,
                            string.Join("; ", addRoleResult.Errors.Select(e => e.Description)));
                        return null;
                    }
                }
            }

            _logger.LogInformation("Active Directory login {Action} local account for {Email} with roles: {Roles}",
                needsCreate ? "provisioned" : "updated",
                normalizedEmail,
                string.Join(", ", mappedRoles));

            return (user, mappedRoles);
        }

        private (string Token, DateTime ExpiresUtc) GenerateJwtToken(AppUser user, IList<string> roles)
        {
            var accessTokenMinutes = _config.GetValue<int?>("Jwt:AccessTokenMinutes") ?? 60;
            accessTokenMinutes = Math.Clamp(accessTokenMinutes, 15, 240);
            var expiresUtc = DateTime.UtcNow.AddMinutes(accessTokenMinutes);

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            foreach (var role in roles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            var signingKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
            );

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                expires: expiresUtc,
                claims: authClaims,
                signingCredentials: new SigningCredentials(
                    signingKey, SecurityAlgorithms.HmacSha256
                )
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresUtc);
        }
    }
}
