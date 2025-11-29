using api.Models;
using MyApp.Namespace.Services;

namespace MyApp.Namespace.Services
{
    public class ThreatDeduplicationService
    {
        private readonly DatabaseService _db;

        public ThreatDeduplicationService(DatabaseService db)
        {
            _db = db;
        }

        public async Task<DeduplicationResult> ProcessThreatsAsync(List<Threat> threats)
        {
            var result = new DeduplicationResult
            {
                NewThreats = new List<Threat>(),
                UpdatedThreats = new List<Threat>(),
                SkippedThreats = new List<Threat>()
            };

            foreach (var threat in threats)
            {
                if (string.IsNullOrEmpty(threat.external_reference))
                {
                    result.SkippedThreats.Add(threat);
                    continue;
                }

                var existing = await FindExistingThreatAsync(threat.external_reference);

                if (existing == null)
                {
                    result.NewThreats.Add(threat);
                }
                else
                {
                    var existingDate = existing.TryGetValue("created_at", out var created) && created != null && created != DBNull.Value
                        ? DateTime.Parse(created.ToString()!)
                        : DateTime.MinValue;

                    var daysSinceCreation = (DateTime.UtcNow - existingDate).TotalDays;

                    if (daysSinceCreation < 30)
                    {
                        result.SkippedThreats.Add(threat);
                    }
                    else
                    {
                        threat.id = existing.TryGetValue("id", out var id) ? Convert.ToInt32(id) : null;
                        result.UpdatedThreats.Add(threat);
                    }
                }
            }

            return result;
        }

        private async Task<Dictionary<string, object>?> FindExistingThreatAsync(string externalReference)
        {
            try
            {
                var sql = "SELECT * FROM threats WHERE external_reference = @p0 LIMIT 1";
                var results = await _db.QueryAsync(sql, externalReference);
                return results.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public async Task<int> InsertThreatAsync(Threat threat)
        {
            try
            {
                var sql = @"INSERT INTO threats (title, description, category, source, date_observed, impact_level, external_reference, status, user_id, created_at, updated_at)
                           VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)";

                var now = DateTime.UtcNow;
                // Convert DateTime to Date only (schema uses DATE type)
                var dateObserved = threat.date_observed.Date;
                return await _db.ExecuteAsync(sql,
                    threat.title,
                    threat.description,
                    threat.category,
                    threat.source,
                    dateObserved,
                    threat.impact_level,
                    threat.external_reference ?? (object)DBNull.Value,
                    threat.status,
                    threat.user_id ?? 1,
                    now,
                    now
                );
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to insert threat: {ex.Message}", ex);
            }
        }

        public async Task<int> UpdateThreatAsync(Threat threat)
        {
            if (threat.id == null)
            {
                throw new ArgumentException("Threat ID is required for update");
            }

            try
            {
                var sql = @"UPDATE threats 
                           SET title = @p0, description = @p1, category = @p2, source = @p3, 
                               date_observed = @p4, impact_level = @p5, external_reference = @p6, 
                               status = @p7, updated_at = @p8
                           WHERE id = @p9";

                // Convert DateTime to Date only (schema uses DATE type)
                var dateObserved = threat.date_observed.Date;
                return await _db.ExecuteAsync(sql,
                    threat.title,
                    threat.description,
                    threat.category,
                    threat.source,
                    dateObserved,
                    threat.impact_level,
                    threat.external_reference ?? (object)DBNull.Value,
                    threat.status,
                    DateTime.UtcNow,
                    threat.id
                );
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to update threat: {ex.Message}", ex);
            }
        }

        public async Task<int> BulkInsertThreatsAsync(List<Threat> threats)
        {
            var inserted = 0;
            foreach (var threat in threats)
            {
                try
                {
                    await InsertThreatAsync(threat);
                    inserted++;
                }
                catch
                {
                    continue;
                }
            }
            return inserted;
        }

        public async Task<int> BulkUpdateThreatsAsync(List<Threat> threats)
        {
            var updated = 0;
            foreach (var threat in threats)
            {
                try
                {
                    await UpdateThreatAsync(threat);
                    updated++;
                }
                catch
                {
                    continue;
                }
            }
            return updated;
        }
    }

    public class DeduplicationResult
    {
        public List<Threat> NewThreats { get; set; } = new();
        public List<Threat> UpdatedThreats { get; set; } = new();
        public List<Threat> SkippedThreats { get; set; } = new();
    }
}

