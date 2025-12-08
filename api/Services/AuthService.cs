using api.Models;
using MyApp.Namespace.Services;
using api.Extensions;
using MySqlConnector;
using BCrypt.Net;

namespace api.Services;

public class AuthService
{
    private readonly DatabaseService _dbService;
    private readonly IConfiguration _configuration;

    public AuthService(DatabaseService dbService, IConfiguration configuration)
    {
        _dbService = dbService;
        _configuration = configuration;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, password_hash, full_name, facility_name, facility_type, role, status, created_at, totp_secret, is_two_factor_enabled, two_factor_enabled_at FROM users WHERE email = @email";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@email", email);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                PasswordHash = reader.GetStringByName("password_hash"),
                FullName = reader.GetStringByName("full_name"),
                FacilityName = reader.GetStringByName("facility_name"),
                FacilityType = Enum.Parse<FacilityType>(reader.GetStringByName("facility_type"), true),
                Role = Enum.Parse<UserRole>(reader.GetStringByName("role"), true),
                Status = Enum.Parse<UserStatus>(reader.GetStringByName("status"), true),
                CreatedAt = reader.GetDateTimeByName("created_at"),
                TotpSecret = reader.IsDBNull(reader.GetOrdinal("totp_secret")) ? null : reader.GetString(reader.GetOrdinal("totp_secret")),
                IsTwoFactorEnabled = !reader.IsDBNull(reader.GetOrdinal("is_two_factor_enabled")) && reader.GetBoolean(reader.GetOrdinal("is_two_factor_enabled")),
                TwoFactorEnabledAt = reader.IsDBNull(reader.GetOrdinal("two_factor_enabled_at")) ? null : reader.GetDateTime(reader.GetOrdinal("two_factor_enabled_at"))
            };
        }
        return null;
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, password_hash, full_name, facility_name, facility_type, role, status, created_at, totp_secret, is_two_factor_enabled, two_factor_enabled_at FROM users WHERE id = @id";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", id);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                PasswordHash = reader.GetStringByName("password_hash"),
                FullName = reader.GetStringByName("full_name"),
                FacilityName = reader.GetStringByName("facility_name"),
                FacilityType = Enum.Parse<FacilityType>(reader.GetStringByName("facility_type"), true),
                Role = Enum.Parse<UserRole>(reader.GetStringByName("role"), true),
                Status = Enum.Parse<UserStatus>(reader.GetStringByName("status"), true),
                CreatedAt = reader.GetDateTimeByName("created_at"),
                TotpSecret = reader.IsDBNull(reader.GetOrdinal("totp_secret")) ? null : reader.GetString(reader.GetOrdinal("totp_secret")),
                IsTwoFactorEnabled = !reader.IsDBNull(reader.GetOrdinal("is_two_factor_enabled")) && reader.GetBoolean(reader.GetOrdinal("is_two_factor_enabled")),
                TwoFactorEnabledAt = reader.IsDBNull(reader.GetOrdinal("two_factor_enabled_at")) ? null : reader.GetDateTime(reader.GetOrdinal("two_factor_enabled_at"))
            };
        }
        return null;
    }

    public bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt());
    }

    public async Task<User> RegisterUserAsync(string email, string password, string fullName, string facilityName, FacilityType facilityType, UserRole roleRequest = UserRole.User)
    {
        if (await GetUserByEmailAsync(email) != null)
        {
            throw new InvalidOperationException("Email already registered");
        }

        var passwordHash = HashPassword(password);
        var status = roleRequest == UserRole.Admin ? UserStatus.Pending : UserStatus.Pending;

        using var connection = await _dbService.GetConnectionAsync();
        var query = @"INSERT INTO users (email, password_hash, full_name, facility_name, facility_type, role, status, created_at) 
                      VALUES (@email, @password_hash, @full_name, @facility_name, @facility_type, @role, @status, NOW())";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@password_hash", passwordHash);
        command.Parameters.AddWithValue("@full_name", fullName);
        command.Parameters.AddWithValue("@facility_name", facilityName);
        command.Parameters.AddWithValue("@facility_type", facilityType.ToString());
        command.Parameters.AddWithValue("@role", roleRequest.ToString());
        command.Parameters.AddWithValue("@status", status.ToString());

        await command.ExecuteNonQueryAsync();
        var userId = (int)command.LastInsertedId;

        return await GetUserByIdAsync(userId) ?? throw new Exception("Failed to retrieve created user");
    }

    public bool ValidatePasswordStrength(string password)
    {
        // At least 8 characters, 1 uppercase, 1 lowercase, 1 number
        if (password.Length < 8) return false;
        if (!password.Any(char.IsUpper)) return false;
        if (!password.Any(char.IsLower)) return false;
        if (!password.Any(char.IsDigit)) return false;
        return true;
    }

    public async Task EnableTwoFactorAsync(int userId, string totpSecret)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = @"UPDATE users SET totp_secret = @totp_secret, is_two_factor_enabled = 1, two_factor_enabled_at = NOW() WHERE id = @id";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@totp_secret", totpSecret);
        command.Parameters.AddWithValue("@id", userId);
        
        await command.ExecuteNonQueryAsync();
    }

    public async Task<User> RegisterUserWithDocumentsAsync(
        string email, 
        string password, 
        string fullName, 
        string facilityName, 
        FacilityType facilityType,
        string? workIdPath,
        string? professionalLicensePath,
        string? supervisorLetterPath,
        double? aiConfidenceScore,
        string? aiAnalysisResult,
        bool requiresManualReview,
        UserStatus status)
    {
        if (await GetUserByEmailAsync(email) != null)
        {
            throw new InvalidOperationException("Email already registered");
        }

        var passwordHash = HashPassword(password);

        using var connection = await _dbService.GetConnectionAsync();
        var query = @"INSERT INTO users (
            email, password_hash, full_name, facility_name, facility_type, role, status, created_at,
            work_id_path, professional_license_path, supervisor_letter_path,
            ai_confidence_score, ai_analysis_result, requires_manual_review, approved_at
        ) VALUES (
            @email, @password_hash, @full_name, @facility_name, @facility_type, @role, @status, NOW(),
            @work_id_path, @professional_license_path, @supervisor_letter_path,
            @ai_confidence_score, @ai_analysis_result, @requires_manual_review, @approved_at
        )";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@password_hash", passwordHash);
        command.Parameters.AddWithValue("@full_name", fullName);
        command.Parameters.AddWithValue("@facility_name", facilityName);
        command.Parameters.AddWithValue("@facility_type", facilityType.ToString());
        command.Parameters.AddWithValue("@role", UserRole.User.ToString());
        command.Parameters.AddWithValue("@status", status.ToString());
        command.Parameters.AddWithValue("@work_id_path", (object?)workIdPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@professional_license_path", (object?)professionalLicensePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@supervisor_letter_path", (object?)supervisorLetterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@ai_confidence_score", (object?)aiConfidenceScore ?? DBNull.Value);
        command.Parameters.AddWithValue("@ai_analysis_result", (object?)aiAnalysisResult ?? DBNull.Value);
        command.Parameters.AddWithValue("@requires_manual_review", requiresManualReview);
        command.Parameters.AddWithValue("@approved_at", status == UserStatus.Active ? DateTime.UtcNow : DBNull.Value);

        await command.ExecuteNonQueryAsync();
        var userId = (int)command.LastInsertedId;

        return await GetUserByIdAsync(userId) ?? throw new Exception("Failed to retrieve created user");
    }

    public async Task<List<User>> GetPendingUsersAsync()
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = @"SELECT id, email, full_name, facility_name, facility_type, status, created_at, 
                      ai_confidence_score, requires_manual_review 
                      FROM users 
                      WHERE status = 'Pending' AND requires_manual_review = 1 
                      ORDER BY created_at DESC";
        
        using var command = new MySqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        var users = new List<User>();
        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name"),
                FacilityName = reader.GetStringByName("facility_name"),
                FacilityType = Enum.Parse<FacilityType>(reader.GetStringByName("facility_type"), true),
                Status = Enum.Parse<UserStatus>(reader.GetStringByName("status"), true),
                CreatedAt = reader.GetDateTimeByName("created_at"),
                AiConfidenceScore = reader.IsDBNull(reader.GetOrdinal("ai_confidence_score")) ? null : reader.GetDouble(reader.GetOrdinal("ai_confidence_score")),
                RequiresManualReview = reader.GetBoolean(reader.GetOrdinal("requires_manual_review"))
            });
        }
        return users;
    }

    public async Task<User?> GetUserWithDocumentsAsync(int id)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = @"SELECT id, email, password_hash, full_name, facility_name, facility_type, role, status, created_at,
                      work_id_path, professional_license_path, supervisor_letter_path,
                      ai_confidence_score, ai_analysis_result, requires_manual_review,
                      rejection_reason, reviewed_by_admin_id, reviewed_at, approved_at
                      FROM users WHERE id = @id";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", id);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                PasswordHash = reader.GetStringByName("password_hash"),
                FullName = reader.GetStringByName("full_name"),
                FacilityName = reader.GetStringByName("facility_name"),
                FacilityType = Enum.Parse<FacilityType>(reader.GetStringByName("facility_type"), true),
                Role = Enum.Parse<UserRole>(reader.GetStringByName("role"), true),
                Status = Enum.Parse<UserStatus>(reader.GetStringByName("status"), true),
                CreatedAt = reader.GetDateTimeByName("created_at"),
                WorkIdPath = reader.IsDBNull(reader.GetOrdinal("work_id_path")) ? null : reader.GetString(reader.GetOrdinal("work_id_path")),
                ProfessionalLicensePath = reader.IsDBNull(reader.GetOrdinal("professional_license_path")) ? null : reader.GetString(reader.GetOrdinal("professional_license_path")),
                SupervisorLetterPath = reader.IsDBNull(reader.GetOrdinal("supervisor_letter_path")) ? null : reader.GetString(reader.GetOrdinal("supervisor_letter_path")),
                AiConfidenceScore = reader.IsDBNull(reader.GetOrdinal("ai_confidence_score")) ? null : reader.GetDouble(reader.GetOrdinal("ai_confidence_score")),
                AiAnalysisResult = reader.IsDBNull(reader.GetOrdinal("ai_analysis_result")) ? null : reader.GetString(reader.GetOrdinal("ai_analysis_result")),
                RequiresManualReview = !reader.IsDBNull(reader.GetOrdinal("requires_manual_review")) && reader.GetBoolean(reader.GetOrdinal("requires_manual_review")),
                RejectionReason = reader.IsDBNull(reader.GetOrdinal("rejection_reason")) ? null : reader.GetString(reader.GetOrdinal("rejection_reason")),
                ReviewedByAdminId = reader.IsDBNull(reader.GetOrdinal("reviewed_by_admin_id")) ? null : reader.GetInt32(reader.GetOrdinal("reviewed_by_admin_id")),
                ReviewedAt = reader.IsDBNull(reader.GetOrdinal("reviewed_at")) ? null : reader.GetDateTime(reader.GetOrdinal("reviewed_at")),
                ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at"))
            };
        }
        return null;
    }

    public async Task ApproveUserAsync(int userId, int adminId)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = @"UPDATE users SET status = 'Active', approved_at = NOW(), reviewed_by_admin_id = @admin_id, reviewed_at = NOW() WHERE id = @id";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", userId);
        command.Parameters.AddWithValue("@admin_id", adminId);
        
        await command.ExecuteNonQueryAsync();
    }

    public async Task RejectUserAsync(int userId, int adminId, string reason)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = @"UPDATE users SET status = 'Rejected', rejection_reason = @reason, reviewed_by_admin_id = @admin_id, reviewed_at = NOW() WHERE id = @id";
        
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", userId);
        command.Parameters.AddWithValue("@admin_id", adminId);
        command.Parameters.AddWithValue("@reason", reason);
        
        await command.ExecuteNonQueryAsync();
    }
}

