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
        var query = "SELECT id, email, password_hash, full_name, facility_name, facility_type, role, status, created_at FROM users WHERE email = @email";
        
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
                CreatedAt = reader.GetDateTimeByName("created_at")
            };
        }
        return null;
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, password_hash, full_name, facility_name, facility_type, role, status, created_at FROM users WHERE id = @id";
        
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
                CreatedAt = reader.GetDateTimeByName("created_at")
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
}

