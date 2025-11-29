using MyApp.Namespace.Services;
using MySqlConnector;
using api.Models;
using api.DTOs;
using api.Extensions;

namespace MyApp.Namespace.Services
{
    public class AuditLogService
    {
        private readonly DatabaseService _db;

        public AuditLogService(DatabaseService db)
        {
            _db = db;
        }

        public async Task LogAsync(string actionType, string actionDetails, int userId = 1)
        {
            try
            {
                var sql = @"INSERT INTO audit_logs (action_type, action_details, user_id, timestamp)
                           VALUES (@p0, @p1, @p2, @p3)";

                await _db.ExecuteAsync(sql, actionType, actionDetails, userId, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create audit log: {ex.Message}", ex);
            }
        }

        public async Task<List<AuditLog>> GetAuditLogsAsync(AuditLogFilter filter)
        {
            var logs = new List<AuditLog>();
            var conditions = new List<string>();
            var parameters = new List<object>();

            if (filter.ThreatId.HasValue)
            {
                conditions.Add("threat_id = @p" + parameters.Count);
                parameters.Add(filter.ThreatId.Value);
            }

            if (filter.UserId.HasValue)
            {
                conditions.Add("user_id = @p" + parameters.Count);
                parameters.Add(filter.UserId.Value);
            }

            if (!string.IsNullOrEmpty(filter.ActionType))
            {
                conditions.Add("action_type = @p" + parameters.Count);
                parameters.Add(filter.ActionType);
            }

            if (filter.StartDate.HasValue)
            {
                conditions.Add("timestamp >= @p" + parameters.Count);
                parameters.Add(filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                conditions.Add("timestamp <= @p" + parameters.Count);
                parameters.Add(filter.EndDate.Value);
            }

            var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            // Join users to get user name/email for display in UI and exports
            var sql = $@"
                SELECT al.id, al.threat_id, al.user_id, al.action_type, al.action_details, al.timestamp,
                       u.full_name, u.email
                FROM audit_logs al
                LEFT JOIN users u ON al.user_id = u.id
                {whereClause}
                ORDER BY al.timestamp DESC 
                LIMIT @p{parameters.Count} OFFSET @p{parameters.Count + 1}";

            parameters.Add(filter.Limit);
            parameters.Add(filter.Offset);

            using var connection = await _db.GetConnectionAsync();
            using var command = new MySqlCommand(sql, connection);
            
            for (int i = 0; i < parameters.Count; i++)
            {
                command.Parameters.AddWithValue($"@p{i}", parameters[i]);
            }

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var userId = reader.GetInt32ByName("user_id");
                string? fullName = null;
                string? email = null;
                try
                {
                    if (!reader.IsDBNullByName("full_name"))
                    {
                        fullName = reader.GetStringByName("full_name");
                    }
                }
                catch { /* column may not exist in some environments */ }

                try
                {
                    if (!reader.IsDBNullByName("email"))
                    {
                        email = reader.GetStringByName("email");
                    }
                }
                catch { /* column may not exist in some environments */ }

                var userDisplayName = fullName;
                if (string.IsNullOrWhiteSpace(userDisplayName))
                {
                    userDisplayName = email;
                }
                if (string.IsNullOrWhiteSpace(userDisplayName))
                {
                    userDisplayName = userId == 1 ? "System" : $"User {userId}";
                }

                logs.Add(new AuditLog
                {
                    Id = reader.GetInt32ByName("id"),
                    ThreatId = reader.IsDBNullByName("threat_id") ? null : reader.GetInt32ByName("threat_id"),
                    UserId = userId,
                    UserName = userDisplayName,
                    ActionType = reader.GetStringByName("action_type"),
                    Details = reader.IsDBNullByName("action_details") ? null : reader.GetStringByName("action_details"),
                    Timestamp = reader.GetDateTimeByName("timestamp")
                });
            }

            return logs;
        }

        public async Task<int> GetAuditLogCountAsync(AuditLogFilter filter)
        {
            var conditions = new List<string>();
            var parameters = new List<object>();

            if (filter.ThreatId.HasValue)
            {
                conditions.Add("threat_id = @p" + parameters.Count);
                parameters.Add(filter.ThreatId.Value);
            }

            if (filter.UserId.HasValue)
            {
                conditions.Add("user_id = @p" + parameters.Count);
                parameters.Add(filter.UserId.Value);
            }

            if (!string.IsNullOrEmpty(filter.ActionType))
            {
                conditions.Add("action_type = @p" + parameters.Count);
                parameters.Add(filter.ActionType);
            }

            if (filter.StartDate.HasValue)
            {
                conditions.Add("timestamp >= @p" + parameters.Count);
                parameters.Add(filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                conditions.Add("timestamp <= @p" + parameters.Count);
                parameters.Add(filter.EndDate.Value);
            }

            var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            var sql = $"SELECT COUNT(*) FROM audit_logs {whereClause}";

            using var connection = await _db.GetConnectionAsync();
            using var command = new MySqlCommand(sql, connection);
            
            for (int i = 0; i < parameters.Count; i++)
            {
                command.Parameters.AddWithValue($"@p{i}", parameters[i]);
            }

            var result = await command.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        }
    }
}
