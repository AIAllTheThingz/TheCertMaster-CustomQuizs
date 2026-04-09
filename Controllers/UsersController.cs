using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QuizAPI.DTO;
using QuizAPI.Models;
using System.ComponentModel.DataAnnotations;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize(Roles = "Admin")]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = _userManager.Users.ToList();

            var result = new List<object>(users.Count);
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                result.Add(new
                {
                    id = user.Id,
                    email = user.Email ?? user.UserName ?? string.Empty,
                    userName = user.UserName ?? user.Email ?? string.Empty,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    role = roles.FirstOrDefault() ?? "User"
                });
            }

            return Ok(result);
        }

        [HttpPost("{email}/reset-password")]
        public async Task<IActionResult> ResetPassword(string email, [FromBody] ResetPasswordRequest req)
        {
            var normalizedEmail = email?.Trim() ?? string.Empty;
            if (!new EmailAddressAttribute().IsValid(normalizedEmail))
                return BadRequest("A valid email is required.");
            if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest("NewPassword is required.");

            var user = await _userManager.FindByEmailAsync(normalizedEmail);
            if (user == null)
                return NotFound("User not found.");

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, req.NewPassword);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok($"Password updated for {normalizedEmail}.");
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            var email = req.Email?.Trim() ?? string.Empty;
            var role = req.Role?.Trim() ?? "User";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Email and password are required.");

            if (!new EmailAddressAttribute().IsValid(email))
                return BadRequest("A valid email is required.");

            if (!await _roleManager.RoleExistsAsync(role))
                return BadRequest($"Role '{role}' does not exist.");

            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
                return BadRequest("User already exists.");

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                FirstName = req.FirstName?.Trim() ?? string.Empty,
                LastName = req.LastName?.Trim() ?? string.Empty
            };

            var result = await _userManager.CreateAsync(user, req.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            await _userManager.AddToRoleAsync(user, role);
            return Ok($"User {email} created with role {role}");
        }

        [HttpPut("{email}/role")]
        public async Task<IActionResult> UpdateRole(string email, [FromBody] UpdateRoleRequest req)
        {
            var normalizedEmail = email?.Trim() ?? string.Empty;
            var role = req.Role?.Trim() ?? string.Empty;
            if (!new EmailAddressAttribute().IsValid(normalizedEmail))
                return BadRequest("A valid email is required.");
            if (string.IsNullOrWhiteSpace(role))
                return BadRequest("Role is required.");

            var user = await _userManager.FindByEmailAsync(normalizedEmail);
            if (user == null) return NotFound("User not found.");

            if (!await _roleManager.RoleExistsAsync(role))
                return BadRequest($"Role '{role}' does not exist.");

            var oldRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, oldRoles);
            await _userManager.AddToRoleAsync(user, role);

            return Ok($"User {normalizedEmail} role updated to {role}");
        }

        [HttpPut("{email}")]
        public async Task<IActionResult> UpdateUser(string email, [FromBody] UpdateUserRequestDto req)
        {
            var normalizedEmail = email?.Trim() ?? string.Empty;
            if (!new EmailAddressAttribute().IsValid(normalizedEmail))
                return BadRequest("A valid email is required.");

            var user = await _userManager.FindByEmailAsync(normalizedEmail);
            if (user == null) return NotFound("User not found.");

            if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
                return BadRequest("FirstName and LastName are required.");

            if (!await _roleManager.RoleExistsAsync(req.Role))
                return BadRequest($"Role '{req.Role}' does not exist.");

            user.FirstName = req.FirstName.Trim();
            user.LastName = req.LastName.Trim();

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return BadRequest(updateResult.Errors);

            var oldRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, oldRoles);
            await _userManager.AddToRoleAsync(user, req.Role);

            return Ok(new
            {
                message = $"User {email} updated.",
                email = normalizedEmail,
                firstName = user.FirstName,
                lastName = user.LastName,
                role = req.Role
            });
        }

        [HttpDelete("{email}")]
        public async Task<IActionResult> DeleteUser(string email)
        {
            var normalizedEmail = email?.Trim() ?? string.Empty;
            if (!new EmailAddressAttribute().IsValid(normalizedEmail))
                return BadRequest("A valid email is required.");

            var user = await _userManager.FindByEmailAsync(normalizedEmail);
            if (user == null) return NotFound("User not found.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok($"User {normalizedEmail} deleted.");
        }
    }

    public class CreateUserRequest
    {
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = "User";
    }

    public class ResetPasswordRequest
    {
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
