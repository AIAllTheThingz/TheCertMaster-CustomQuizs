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

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            IConfiguration config,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
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
            if (user == null)
            {
                _logger.LogWarning("Login failed for unknown email {Email}.", email);
                return Unauthorized("Invalid email or password");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var shouldLockoutOnFailure = !roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
            var valid = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: shouldLockoutOnFailure);
            if (!valid.Succeeded)
            {
                _logger.LogWarning("Login failed for {Email}.", email);
                return Unauthorized("Invalid email or password");
            }

            var (token, expiresUtc) = GenerateJwtToken(user, roles);
            _logger.LogInformation("Login succeeded for {Email}.", email);

            return Ok(new { token, expiresUtc });
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
