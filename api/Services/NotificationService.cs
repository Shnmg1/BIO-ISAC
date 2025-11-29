using api.Models;
using MyApp.Namespace.Services;
using api.Extensions;
using MySqlConnector;

namespace api.Services;

public class NotificationService
{
    private readonly DatabaseService _dbService;
    private readonly EmailService _emailService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(DatabaseService dbService, EmailService emailService, ILogger<NotificationService> logger)
    {
        _dbService = dbService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task SendThreatNotificationAsync(int threatId, ThreatTier tier)
    {
        try
        {
            var threat = await GetThreatAsync(threatId);
            if (threat == null) return;

            var classification = await GetClassificationAsync(threatId);

            switch (tier)
            {
                case ThreatTier.High:
                    await SendTier1NotificationAsync(threat, classification);
                    break;
                case ThreatTier.Medium:
                    await SendTier2NotificationAsync(threat, classification);
                    break;
                case ThreatTier.Low:
                    await SendTier3NotificationAsync(threat, classification);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending threat notification");
        }
    }

    private async Task SendTier1NotificationAsync(Threat threat, Classification? classification)
    {
        var admins = await GetAdminsAsync();
        foreach (var admin in admins)
        {
            await CreateAndSendNotificationAsync(
                threat.id,
                ThreatTier.High,
                admin.Id,
                null,
                false,
                $"CRITICAL ALERT: {threat.title}",
                BuildNotificationBody(threat, classification, ThreatTier.High),
                DeliveryMethod.Both
            );
        }

        var facilityTypes = await GetRelevantFacilityTypesAsync(threat);
        foreach (var facilityType in facilityTypes)
        {
            await CreateNotificationForFacilityTypeAsync(
                threat.id,
                ThreatTier.High,
                facilityType,
                $"CRITICAL ALERT: {threat.title}",
                BuildNotificationBody(threat, classification, ThreatTier.High),
                DeliveryMethod.Both
            );
        }
    }

    private async Task SendTier2NotificationAsync(Threat threat, Classification? classification)
    {
        var facilityTypes = await GetRelevantFacilityTypesAsync(threat);
        foreach (var facilityType in facilityTypes)
        {
            await CreateNotificationForFacilityTypeAsync(
                threat.id,
                ThreatTier.Medium,
                facilityType,
                $"Security Alert: {threat.title}",
                BuildNotificationBody(threat, classification, ThreatTier.Medium),
                DeliveryMethod.Email
            );
        }
    }

    private async Task SendTier3NotificationAsync(Threat threat, Classification? classification)
    {
        var users = await GetAllActiveUsersAsync();
        foreach (var user in users)
        {
            await CreateAndSendNotificationAsync(
                threat.id,
                ThreatTier.Low,
                user.Id,
                null,
                false,
                $"Information Alert: {threat.title}",
                BuildNotificationBody(threat, classification, ThreatTier.Low),
                DeliveryMethod.InApp
            );
        }
    }

    public async Task SendMassAlertAsync(MassAlertRequest request)
    {
        try
        {
            List<User> recipients;

            if (request.SendToAll)
            {
                recipients = await GetAllActiveUsersAsync();
            }
            else if (request.FacilityTypes != null && request.FacilityTypes.Count > 0)
            {
                recipients = await GetUsersByFacilityTypesAsync(request.FacilityTypes);
            }
            else if (request.UserIds != null && request.UserIds.Count > 0)
            {
                recipients = await GetUsersByIdsAsync(request.UserIds);
            }
            else
            {
                throw new InvalidOperationException("No recipients specified");
            }

            foreach (var user in recipients)
            {
                await CreateAndSendNotificationAsync(
                    request.ThreatId,
                    request.Tier,
                    user.Id,
                    null,
                    false,
                    request.Subject,
                    request.Body,
                    request.DeliveryMethod
                );
            }

            _logger.LogInformation($"Mass alert sent to {recipients.Count} recipients");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending mass alert");
            throw;
        }
    }

    private async Task CreateAndSendNotificationAsync(
        int? threatId,
        ThreatTier tier,
        int? sentTo,
        FacilityType? sentToFacilityType,
        bool sentToAll,
        string subject,
        string body,
        DeliveryMethod deliveryMethod)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = @"INSERT INTO notifications (threat_id, tier, sent_to, sent_to_facility_type, sent_to_all, 
                         subject, body, delivery_method, delivery_status, created_at) 
                      VALUES (@threat_id, @tier, @sent_to, @sent_to_facility_type, @sent_to_all, 
                         @subject, @body, @delivery_method, 'Pending', NOW())";

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@threat_id", threatId.HasValue ? (object)threatId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@tier", tier.ToString());
        command.Parameters.AddWithValue("@sent_to", sentTo.HasValue ? (object)sentTo.Value : DBNull.Value);
        command.Parameters.AddWithValue("@sent_to_facility_type", sentToFacilityType.HasValue ? sentToFacilityType.Value.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("@sent_to_all", sentToAll);
        command.Parameters.AddWithValue("@subject", subject);
        command.Parameters.AddWithValue("@body", body);
        command.Parameters.AddWithValue("@delivery_method", deliveryMethod.ToString());

        await command.ExecuteNonQueryAsync();
        var notificationId = (int)command.LastInsertedId;

        if (deliveryMethod == DeliveryMethod.Email || deliveryMethod == DeliveryMethod.Both)
        {
            if (sentTo.HasValue)
            {
                var user = await GetUserAsync(sentTo.Value);
                if (user != null)
                {
                    try
                    {
                        await _emailService.SendEmailAsync(user.Email, subject, body);
                        await UpdateNotificationStatusAsync(notificationId, DeliveryStatus.Sent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to send email to {user.Email}");
                        await UpdateNotificationStatusAsync(notificationId, DeliveryStatus.Failed);
                    }
                }
            }
        }
        else
        {
            await UpdateNotificationStatusAsync(notificationId, DeliveryStatus.Sent);
        }
    }

    private async Task CreateNotificationForFacilityTypeAsync(
        int? threatId,
        ThreatTier tier,
        FacilityType facilityType,
        string subject,
        string body,
        DeliveryMethod deliveryMethod)
    {
        var users = await GetUsersByFacilityTypeAsync(facilityType);
        foreach (var user in users)
        {
            await CreateAndSendNotificationAsync(
                threatId,
                tier,
                user.Id,
                facilityType,
                false,
                subject,
                body,
                deliveryMethod
            );
        }
    }

    private async Task UpdateNotificationStatusAsync(int notificationId, DeliveryStatus status)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "UPDATE notifications SET delivery_status = @status, sent_at = NOW() WHERE id = @id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@status", status.ToString());
        command.Parameters.AddWithValue("@id", notificationId);
        await command.ExecuteNonQueryAsync();
    }

    private string BuildNotificationBody(Threat threat, Classification? classification, ThreatTier tier)
    {
        var body = $@"
<h2>Threat Alert: {threat.title}</h2>
<p><strong>Category:</strong> {threat.category}</p>
<p><strong>Description:</strong> {threat.description}</p>
<p><strong>Date Observed:</strong> {threat.date_observed:yyyy-MM-dd HH:mm}</p>
<p><strong>Impact Level:</strong> {threat.impact_level}</p>";

        if (classification != null)
        {
            body += $@"
<p><strong>AI Classification:</strong> {classification.AiTier} (Confidence: {classification.AiConfidence}%)</p>
<p><strong>Reasoning:</strong> {classification.AiReasoning}</p>";
            if (!string.IsNullOrEmpty(classification.AiActions))
            {
                body += $@"<p><strong>Recommended Actions:</strong> {classification.AiActions}</p>";
            }
        }

        body += $@"
<p>Please review this threat and take appropriate action.</p>
<p>BioISAC Shield Team</p>";

        return body;
    }

    private async Task<Threat?> GetThreatAsync(int threatId)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, user_id, title, description, category, source, date_observed, impact_level, status FROM threats WHERE id = @id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", threatId);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Threat
            {
                id = reader.GetInt32ByName("id"),
                user_id = reader.GetInt32ByName("user_id"),
                title = reader.GetStringByName("title"),
                description = reader.GetStringByName("description"),
                category = reader.GetStringByName("category"),
                source = reader.GetStringByName("source"),
                date_observed = reader.GetDateTimeByName("date_observed"),
                impact_level = reader.GetStringByName("impact_level"),
                status = reader.GetStringByName("status")
            };
        }
        return null;
    }

    private async Task<Classification?> GetClassificationAsync(int threatId)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT ai_tier, ai_confidence, ai_reasoning, ai_actions FROM classifications WHERE threat_id = @threat_id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@threat_id", threatId);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Classification
            {
                AiTier = reader.IsDBNullByName("ai_tier") ? null : Enum.Parse<ThreatTier>(reader.GetStringByName("ai_tier"), true),
                AiConfidence = reader.IsDBNullByName("ai_confidence") ? (decimal?)null : reader.GetDecimalByName("ai_confidence"),
                AiReasoning = reader.IsDBNullByName("ai_reasoning") ? null : reader.GetStringByName("ai_reasoning"),
                AiActions = reader.IsDBNullByName("ai_actions") ? null : reader.GetStringByName("ai_actions")
            };
        }
        return null;
    }

    private async Task<List<User>> GetAdminsAsync()
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, full_name FROM users WHERE role = 'Admin' AND status = 'Active'";
        using var command = new MySqlCommand(query, connection);
        
        var admins = new List<User>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            admins.Add(new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name")
            });
        }
        return admins;
    }

    private Task<List<FacilityType>> GetRelevantFacilityTypesAsync(Threat threat)
    {
        return Task.FromResult(new List<FacilityType> { FacilityType.Hospital, FacilityType.Lab, FacilityType.Biomanufacturing, FacilityType.Agriculture });
    }

    private async Task<List<User>> GetAllActiveUsersAsync()
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, full_name FROM users WHERE status = 'Active'";
        using var command = new MySqlCommand(query, connection);
        
        var users = new List<User>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name")
            });
        }
        return users;
    }

    private async Task<List<User>> GetUsersByFacilityTypeAsync(FacilityType facilityType)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, full_name FROM users WHERE facility_type = @facility_type AND status = 'Active'";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@facility_type", facilityType.ToString());
        
        var users = new List<User>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name")
            });
        }
        return users;
    }

    private async Task<List<User>> GetUsersByFacilityTypesAsync(List<FacilityType> facilityTypes)
    {
        var allUsers = new List<User>();
        foreach (var facilityType in facilityTypes)
        {
            var users = await GetUsersByFacilityTypeAsync(facilityType);
            allUsers.AddRange(users);
        }
        return allUsers.DistinctBy(u => u.Id).ToList();
    }

    private async Task<List<User>> GetUsersByIdsAsync(List<int> userIds)
    {
        if (userIds.Count == 0) return new List<User>();
        
        using var connection = await _dbService.GetConnectionAsync();
        var placeholders = string.Join(",", userIds.Select((_, i) => $"@id{i}"));
        var query = $"SELECT id, email, full_name FROM users WHERE id IN ({placeholders}) AND status = 'Active'";
        using var command = new MySqlCommand(query, connection);
        
        for (int i = 0; i < userIds.Count; i++)
        {
            command.Parameters.AddWithValue($"@id{i}", userIds[i]);
        }
        
        var users = new List<User>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name")
            });
        }
        return users;
    }

    private async Task<User?> GetUserAsync(int userId)
    {
        using var connection = await _dbService.GetConnectionAsync();
        var query = "SELECT id, email, full_name FROM users WHERE id = @id";
        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", userId);
        
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = reader.GetInt32ByName("id"),
                Email = reader.GetStringByName("email"),
                FullName = reader.GetStringByName("full_name")
            };
        }
        return null;
    }
}

public class MassAlertRequest
{
    public int? ThreatId { get; set; }
    public ThreatTier Tier { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DeliveryMethod DeliveryMethod { get; set; }
    public bool SendToAll { get; set; }
    public List<FacilityType>? FacilityTypes { get; set; }
    public List<int>? UserIds { get; set; }
}

