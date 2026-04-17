using Microsoft.Data.Sqlite;

namespace SPOTrim.Engine.Database;

public sealed class AuditRepository
{
    private readonly SqliteDb _db;

    public AuditRepository(SqliteDb db) => _db = db;

    public void Log(string action, string userName, string details, long? scanId = null)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO audit_log (action, user_name, details, scan_id)
                            VALUES (@action, @userName, @details, @scanId)";
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@userName", userName);
        cmd.Parameters.AddWithValue("@details", details);
        cmd.Parameters.AddWithValue("@scanId", scanId.HasValue ? scanId.Value : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<AuditEntry> GetRecent(int limit = 100, string? action = null)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        var where = action != null ? "WHERE action = @action" : "";
        cmd.CommandText = $"SELECT * FROM audit_log {where} ORDER BY timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        if (action != null)
            cmd.Parameters.AddWithValue("@action", action);

        var results = new List<AuditEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new AuditEntry
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                Action = reader.GetString(reader.GetOrdinal("action")),
                UserName = reader.GetString(reader.GetOrdinal("user_name")),
                Details = reader.GetString(reader.GetOrdinal("details")),
                ScanId = reader.IsDBNull(reader.GetOrdinal("scan_id")) ? null : reader.GetInt64(reader.GetOrdinal("scan_id")),
                Timestamp = reader.GetString(reader.GetOrdinal("timestamp"))
            });
        }
        return results;
    }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public string Action { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Details { get; set; } = "";
    public long? ScanId { get; set; }
    public string Timestamp { get; set; } = "";
}
