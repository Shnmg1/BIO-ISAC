using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using api.Models;
using api.Services;
using MyApp.Namespace.Services;
using MySqlConnector;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly DatabaseService _dbService;
    private readonly ILogger<AuthController> _logger;
    private readonly ITotpService _totpService;
    private readonly IMemoryCache _cache;
    private readonly IFileStorageService _fileStorage;
    private readonly IDocumentVerificationService _documentVerification;

    public AuthController(
        AuthService authService, 
        DatabaseService dbService, 
        ILogger<AuthController> logger, 
        ITotpService totpService, 
        IMemoryCache cache,
        IFileStorageService fileStorage,
        IDocumentVerificationService documentVerification)
    {
        _authService = authService;
        _dbService = dbService;
        _logger = logger;
        _totpService = totpService;
        _cache = cache;
        _fileStorage = fileStorage;
        _documentVerification = documentVerification;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Password is required" });
            }

            if (!IsValidEmail(request.Email))
            {
                return BadRequest(new { message = "Invalid email format" });
            }

            var user = await _authService.GetUserByEmailAsync(request.Email);
            if (user == null)
            {
                await Task.Delay(100);
                return Unauthorized(new { message = "Invalid credentials" });
            }

            // Check account status BEFORE password verification for better UX
            if (user.Status == UserStatus.Pending)
            {
                return Unauthorized(new { 
                    message = "Your account is pending approval. You'll receive an email once approved.",
                    status = "pending"
                });
            }

            if (user.Status == UserStatus.Rejected)
            {
                return Unauthorized(new { 
                    message = "Your account registration was not approved. Please contact support.",
                    status = "rejected"
                });
            }

            if (user.Status == UserStatus.Disabled)
            {
                return Unauthorized(new { 
                    message = "Your account has been disabled. Please contact administrator.",
                    status = "disabled"
                });
            }

            _logger.LogInformation($"Attempting login for email: {request.Email}, User ID: {user.Id}, Status: {user.Status}");
            _logger.LogInformation($"Password hash length: {user.PasswordHash?.Length ?? 0}, Hash starts with: {user.PasswordHash?.Substring(0, Math.Min(10, user.PasswordHash?.Length ?? 0))}");
            
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                _logger.LogWarning($"User {user.Id} has no password hash");
                return Unauthorized(new { message = "Invalid credentials" });
            }
            
            if (!_authService.VerifyPassword(request.Password, user.PasswordHash))
            {
                _logger.LogWarning($"Password verification failed for email: {request.Email}");
                await LogAuditAsync(user.Id, "Login_Failed", $"Failed login attempt for email: {request.Email}");
                return Unauthorized(new { message = "Invalid credentials" });
            }
            
            _logger.LogInformation($"Password verification successful for email: {request.Email}");

            // Password is correct - now check 2FA status
            if (!user.IsTwoFactorEnabled)
            {
                // First time login or 2FA not set up yet - need to set up 2FA
                return Ok(new 
                { 
                    requiresTwoFactorSetup = true,
                    userId = user.Id,
                    message = "Please set up two-factor authentication"
                });
            }
            else
            {
                // 2FA already enabled - prompt for TOTP code
                return Ok(new 
                { 
                    requiresTwoFactorCode = true,
                    userId = user.Id,
                    message = "Please enter your authentication code"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { message = "An error occurred during login" });
        }
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    [HttpPost("setup-2fa")]
    public async Task<IActionResult> SetupTwoFactor([FromBody] SetupTwoFactorRequest request)
    {
        try
        {
            var user = await _authService.GetUserByIdAsync(request.UserId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            // Generate new TOTP secret
            var secret = _totpService.GenerateSecret();
            
            // Generate QR code
            var qrCodeUri = _totpService.GenerateQrCodeUri(user.Email, secret);
            var qrCodeImage = _totpService.GenerateQrCodeImage(qrCodeUri);
            var qrCodeBase64 = Convert.ToBase64String(qrCodeImage);

            // Store secret temporarily in cache (expires in 10 minutes)
            _cache.Set($"TempTotpSecret_{user.Id}", secret, TimeSpan.FromMinutes(10));

            return Ok(new 
            {
                qrCode = $"data:image/png;base64,{qrCodeBase64}",
                message = "Scan this QR code with Google Authenticator"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up 2FA");
            return StatusCode(500, new { message = "An error occurred during 2FA setup" });
        }
    }

    [HttpPost("verify-2fa-setup")]
    public async Task<IActionResult> VerifyTwoFactorSetup([FromBody] VerifyTwoFactorRequest request)
    {
        try
        {
            var user = await _authService.GetUserByIdAsync(request.UserId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            // Retrieve temporary secret from cache
            if (!_cache.TryGetValue($"TempTotpSecret_{user.Id}", out string? secret) || string.IsNullOrEmpty(secret))
                return BadRequest(new { message = "Session expired. Please restart setup." });

            // Validate the code
            if (!_totpService.ValidateCode(secret, request.Code))
                return BadRequest(new { message = "Invalid code. Please try again." });

            // Code is valid - save secret and enable 2FA
            await _authService.EnableTwoFactorAsync(user.Id, secret);

            // Clear temporary secret
            _cache.Remove($"TempTotpSecret_{user.Id}");

            await LogAuditAsync(user.Id, "2FA_Enabled", "Two-factor authentication enabled");

            // Fetch updated user
            user = await _authService.GetUserByIdAsync(user.Id);

            return Ok(new 
            {
                success = true,
                message = "Two-factor authentication enabled successfully!",
                user = new
                {
                    id = user!.Id,
                    email = user.Email,
                    fullName = user.FullName,
                    facilityName = user.FacilityName,
                    facilityType = user.FacilityType.ToString(),
                    role = user.Role.ToString(),
                    status = user.Status.ToString()
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying 2FA setup");
            return StatusCode(500, new { message = "An error occurred during 2FA verification" });
        }
    }

    [HttpPost("verify-2fa-login")]
    public async Task<IActionResult> VerifyTwoFactorLogin([FromBody] VerifyTwoFactorRequest request)
    {
        try
        {
            var user = await _authService.GetUserByIdAsync(request.UserId);
            if (user == null || !user.IsTwoFactorEnabled || string.IsNullOrEmpty(user.TotpSecret))
                return Unauthorized(new { message = "Invalid request" });

            // Validate code against stored secret
            if (!_totpService.ValidateCode(user.TotpSecret, request.Code))
                return BadRequest(new { message = "Invalid authentication code" });

            await LogAuditAsync(user.Id, "Login_Success", "User logged in with 2FA");

            return Ok(new 
            {
                success = true,
                message = "Login successful",
                user = new
                {
                    id = user.Id,
                    email = user.Email,
                    fullName = user.FullName,
                    facilityName = user.FacilityName,
                    facilityType = user.FacilityType.ToString(),
                    role = user.Role.ToString(),
                    status = user.Status.ToString()
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying 2FA login");
            return StatusCode(500, new { message = "An error occurred during 2FA verification" });
        }
    }

    [HttpPost("register")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Register([FromForm] RegisterWithDocumentsRequest request)
    {
        try
        {
            // Validation
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { message = "Email is required" });

            if (string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "Password is required" });

            if (string.IsNullOrWhiteSpace(request.FullName))
                return BadRequest(new { message = "Full name is required" });

            if (string.IsNullOrWhiteSpace(request.FacilityName))
                return BadRequest(new { message = "Facility name is required" });

            if (!IsValidEmail(request.Email))
                return BadRequest(new { message = "Invalid email format" });

            if (!_authService.ValidatePasswordStrength(request.Password))
                return BadRequest(new { message = "Password must be at least 8 characters with uppercase, lowercase, and a number" });

            if (request.Password != request.ConfirmPassword)
                return BadRequest(new { message = "Passwords do not match" });

            // Validate required documents
            if (request.WorkId == null)
                return BadRequest(new { message = "Work ID document is required" });

            if (request.SupervisorLetter == null)
                return BadRequest(new { message = "Supervisor Letter is required" });

            if (!_fileStorage.ValidateDocument(request.WorkId))
                return BadRequest(new { message = "Invalid Work ID document. Must be PDF, JPG, or PNG under 10MB." });

            if (!_fileStorage.ValidateDocument(request.SupervisorLetter))
                return BadRequest(new { message = "Invalid Supervisor Letter. Must be PDF, JPG, or PNG under 10MB." });

            if (request.ProfessionalLicense != null && !_fileStorage.ValidateDocument(request.ProfessionalLicense))
                return BadRequest(new { message = "Invalid Professional License. Must be PDF, JPG, or PNG under 10MB." });

            // Save documents
            var workIdPath = await _fileStorage.SaveDocumentAsync(request.WorkId, "workids");
            var letterPath = await _fileStorage.SaveDocumentAsync(request.SupervisorLetter, "letters");
            string? licensePath = null;
            
            if (request.ProfessionalLicense != null)
            {
                licensePath = await _fileStorage.SaveDocumentAsync(request.ProfessionalLicense, "licenses");
            }

            // Try AI analysis if configured, otherwise skip
            Models.DocumentAnalysisResult? analysisResult = null;
            try
            {
                _logger.LogInformation($"Attempting AI analysis for {request.Email}");
                analysisResult = await _documentVerification.AnalyzeDocumentsAsync(
                    request.WorkId,
                    request.ProfessionalLicense,
                    request.SupervisorLetter
                );
                _logger.LogInformation($"AI analysis complete for {request.Email}. Confidence: {analysisResult.ConfidenceScore}%");
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, $"AI analysis failed for {request.Email}, registration will require manual review");
            }

            // ALL registrations require admin approval (manual review)
            // This ensures admins review every new user until AI auto-approval is enabled
            var status = UserStatus.Pending;
            var requiresManualReview = true;

            // Serialize AI analysis for storage (if available)
            string? aiAnalysisJson = null;
            double? aiConfidenceScore = null;
            
            if (analysisResult != null)
            {
                aiConfidenceScore = analysisResult.ConfidenceScore;
                aiAnalysisJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    analysisResult.Reasoning,
                    analysisResult.ExtractedData,
                    analysisResult.Warnings,
                    analysisResult.IsValid
                });
            }

            // Create user account
            var user = await _authService.RegisterUserWithDocumentsAsync(
                request.Email,
                request.Password,
                request.FullName,
                request.FacilityName,
                request.FacilityType,
                workIdPath,
                licensePath,
                letterPath,
                aiConfidenceScore,
                aiAnalysisJson,
                requiresManualReview,
                status
            );

            await LogAuditAsync(user.Id, "User_Registered", $"New user registered: {request.Email}, AI Score: {aiConfidenceScore?.ToString() ?? "N/A"}%, Status: {status}");

            // Return appropriate response - all registrations now go to admin for review
            return Ok(new
            {
                message = "Registration received! Your documents are under review by our team. You'll receive an email once your account is approved.",
                status = status.ToString(),
                confidenceScore = aiConfidenceScore,
                userId = user.Id
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, new { message = "An error occurred during registration" });
        }
    }

    [HttpPost("logout")]
    public Task<IActionResult> Logout()
    {
        try
        {
            return Task.FromResult<IActionResult>(Ok(new { message = "Logged out successfully" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return Task.FromResult<IActionResult>(StatusCode(500, new { message = "An error occurred during logout" }));
        }
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            var user = await _authService.GetUserByEmailAsync(request.Email);
            if (user == null)
            {
                return Ok(new { message = "If the email exists, a password reset link has been sent" });
            }

            await LogAuditAsync(user.Id, "Password_Reset_Requested", "Password reset requested");

            return Ok(new { message = "If the email exists, a password reset link has been sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during forgot password");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [HttpPost("reset-password")]
    public Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Password reset not yet implemented" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password reset");
            return Task.FromResult<IActionResult>(StatusCode(500, new { message = "An error occurred" }));
        }
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        try
        {
            if (Request.Headers.TryGetValue("X-User-Id", out var userIdHeader) && 
                int.TryParse(userIdHeader, out var userId))
            {
                var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized(new { message = "User not found" });
            }

            return Ok(new
            {
                user = new
                {
                    id = user.Id,
                    email = user.Email,
                    fullName = user.FullName,
                    facilityName = user.FacilityName,
                    facilityType = user.FacilityType.ToString(),
                    role = user.Role.ToString(),
                    status = user.Status.ToString()
                }
            });
            }

            return Unauthorized(new { message = "Not authenticated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current user");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    private async Task LogAuditAsync(int userId, string actionType, string details)
    {
        try
        {
            using var connection = await _dbService.GetConnectionAsync();
            var query = "INSERT INTO audit_logs (user_id, action_type, action_details, timestamp) VALUES (@user_id, @action_type, @action_details, NOW())";
            using var command = new MySqlCommand(query, connection);
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

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public FacilityType FacilityType { get; set; }
    public UserRole RoleRequest { get; set; } = UserRole.User;
}

public class RegisterWithDocumentsRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public FacilityType FacilityType { get; set; }
    public IFormFile? WorkId { get; set; }
    public IFormFile? ProfessionalLicense { get; set; }
    public IFormFile? SupervisorLetter { get; set; }
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class SetupTwoFactorRequest
{
    public int UserId { get; set; }
}

public class VerifyTwoFactorRequest
{
    public int UserId { get; set; }
    public string Code { get; set; } = string.Empty;
}

