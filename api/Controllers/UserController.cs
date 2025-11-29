using Microsoft.AspNetCore.Mvc;
using api.Models;
using api.Services;
using MyApp.Namespace.Services;
using api.Extensions;
using api.DTOs;
using MySqlConnector;
using BCrypt.Net;

namespace api.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly DatabaseService _dbService;
    private readonly AuthService _authService;
    private readonly ILogger<UserController> _logger;

    public UserController(DatabaseService dbService, AuthService authService, ILogger<UserController> logger)
    {
        _dbService = dbService;
        _authService = authService;
        _logger = logger;
    }

    private int GetCurrentUserId()
    {
        if (Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) && 
            int.TryParse(userIdHeader, out var userId))
        {
            return userId;
        }
        return 1;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(new
            {
                id = user.Id,
                email = user.Email,
                fullName = user.FullName,
                facilityName = user.FacilityName,
                facilityType = user.FacilityType.ToString(),
                role = user.Role.ToString(),
                status = user.Status.ToString(),
                createdAt = user.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user profile");
            return StatusCode(500, new { message = "An error occurred while fetching profile" });
        }
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return BadRequest(new { message = "Full name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.FacilityName))
            {
                return BadRequest(new { message = "Facility name is required" });
            }

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"UPDATE users 
                         SET full_name = @full_name, facility_name = @facility_name, facility_type = @facility_type
                         WHERE id = @id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@full_name", request.FullName.Trim());
            command.Parameters.AddWithValue("@facility_name", request.FacilityName.Trim());
            command.Parameters.AddWithValue("@facility_type", request.FacilityType.ToString());
            command.Parameters.AddWithValue("@id", userId);

            await command.ExecuteNonQueryAsync();

            await LogAuditAsync(userId, null, "Profile_Updated", "User updated profile information");

            return Ok(new { message = "Profile updated successfully" });
        }
        catch (MySqlException dbEx)
        {
            _logger.LogError(dbEx, "Database error updating profile");
            return StatusCode(500, new { message = "Database error occurred" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            return StatusCode(500, new { message = "An error occurred while updating profile" });
        }
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return BadRequest(new { message = "Current password is required" });
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "New password is required" });
            }

            if (request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest(new { message = "New passwords do not match" });
            }

            if (!_authService.ValidatePasswordStrength(request.NewPassword))
            {
                return BadRequest(new { message = "New password must be at least 8 characters with uppercase, lowercase, and a number" });
            }

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            if (!_authService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return Unauthorized(new { message = "Current password is incorrect" });
            }

            var newPasswordHash = _authService.HashPassword(request.NewPassword);
            using var connection = await _dbService.GetConnectionAsync();
            var query = "UPDATE users SET password_hash = @password_hash WHERE id = @id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@password_hash", newPasswordHash);
            command.Parameters.AddWithValue("@id", userId);

            await command.ExecuteNonQueryAsync();

            await LogAuditAsync(userId, null, "Password_Changed", "User changed password");

            return Ok(new { message = "Password changed successfully" });
        }
        catch (MySqlException dbEx)
        {
            _logger.LogError(dbEx, "Database error changing password");
            return StatusCode(500, new { message = "Database error occurred" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, new { message = "An error occurred while changing password" });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllUsers()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
            {
                return Unauthorized(new { message = "Authentication required" });
            }

            var currentUser = await _authService.GetUserByIdAsync(userId);
            bool isAdmin = currentUser != null && currentUser.Role.ToString().Equals("Admin", StringComparison.OrdinalIgnoreCase);
            
            if (!isAdmin)
            {
                return Forbid("Only admins can view all users");
            }

            using var connection = await _dbService.GetConnectionAsync();
            var query = @"SELECT id, email, full_name, facility_name, facility_type, role, status 
                         FROM users 
                         WHERE status = 'Active'
                         ORDER BY full_name ASC";

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            
            var users = new List<object>();
            while (await reader.ReadAsync())
            {
                users.Add(new
                {
                    id = reader.GetInt32ByName("id"),
                    email = reader.GetStringByName("email"),
                    fullName = reader.GetStringByName("full_name"),
                    facilityName = reader.IsDBNullByName("facility_name") ? null : reader.GetStringByName("facility_name"),
                    facilityType = reader.IsDBNullByName("facility_type") ? null : reader.GetStringByName("facility_type"),
                    role = reader.GetStringByName("role"),
                    status = reader.GetStringByName("status")
                });
            }

            return Ok(new { users });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all users");
            return StatusCode(500, new { message = "An error occurred while fetching users" });
        }
    }

    private async Task LogAuditAsync(int userId, int? threatId, string actionType, string details)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = "INSERT INTO audit_logs (threat_id, user_id, action_type, action_details, timestamp) VALUES (@threat_id, @user_id, @action_type, @action_details, NOW())";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@threat_id", threatId.HasValue ? (object)threatId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@user_id", userId);
            command.Parameters.AddWithValue("@action_type", actionType);
            command.Parameters.AddWithValue("@action_details", details);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit entry");
        }
    }
}


