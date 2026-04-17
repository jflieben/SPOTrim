using Microsoft.Data.Sqlite;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Database;

public sealed class ScanRepository
{
    private readonly SqliteDb _db;

    public ScanRepository(SqliteDb db) => _db = db;

    public long Create(ScanInfo scan)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO scans (tenant_id, tenant_domain, status, scan_type, started_at, started_by, config_snapshot, module_version)
                            VALUES (@tenantId, @tenantDomain, @status, @scanType, @startedAt, @startedBy, @configSnapshot, @moduleVersion);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@tenantId", scan.TenantId);
        cmd.Parameters.AddWithValue("@tenantDomain", scan.TenantDomain);
        cmd.Parameters.AddWithValue("@status", scan.Status.ToString());
        cmd.Parameters.AddWithValue("@scanType", scan.ScanType);
        cmd.Parameters.AddWithValue("@startedAt", scan.StartedAt);
        cmd.Parameters.AddWithValue("@startedBy", scan.StartedBy);
        cmd.Parameters.AddWithValue("@configSnapshot", scan.ConfigSnapshot);
        cmd.Parameters.AddWithValue("@moduleVersion", scan.ModuleVersion);
        return (long)cmd.ExecuteScalar()!;
    }

    public List<ScanInfo> GetAll(string? tenantId = null)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = tenantId != null
            ? "SELECT * FROM scans WHERE tenant_id = @tid ORDER BY id DESC"
            : "SELECT * FROM scans ORDER BY id DESC";
        if (tenantId != null)
            cmd.Parameters.AddWithValue("@tid", tenantId);

        var results = new List<ScanInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadScan(reader));
        return results;
    }

    public ScanInfo? GetById(long id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM scans WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadScan(reader) : null;
    }

    public void UpdateStatus(long id, ScanStatus status, int? totalSites = null, int? totalLibraries = null, string? error = null)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        var sets = new List<string> { "status = @status" };
        cmd.Parameters.AddWithValue("@status", status.ToString());

        if (status is ScanStatus.Completed or ScanStatus.Failed or ScanStatus.Cancelled)
        {
            sets.Add("completed_at = @completedAt");
            cmd.Parameters.AddWithValue("@completedAt", DateTime.UtcNow.ToString("O"));
        }
        if (totalSites.HasValue)
        {
            sets.Add("total_sites = @totalSites");
            cmd.Parameters.AddWithValue("@totalSites", totalSites.Value);
        }
        if (totalLibraries.HasValue)
        {
            sets.Add("total_libraries = @totalLibraries");
            cmd.Parameters.AddWithValue("@totalLibraries", totalLibraries.Value);
        }
        if (error != null)
        {
            sets.Add("error_message = @error");
            cmd.Parameters.AddWithValue("@error", error);
        }

        cmd.CommandText = $"UPDATE scans SET {string.Join(", ", sets)} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static ScanInfo ReadScan(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        TenantId = r.GetString(r.GetOrdinal("tenant_id")),
        TenantDomain = r.GetString(r.GetOrdinal("tenant_domain")),
        Status = Enum.TryParse<ScanStatus>(r.GetString(r.GetOrdinal("status")), out var s) ? s : ScanStatus.Pending,
        ScanType = r.GetString(r.GetOrdinal("scan_type")),
        StartedAt = r.GetString(r.GetOrdinal("started_at")),
        CompletedAt = r.GetString(r.GetOrdinal("completed_at")),
        StartedBy = r.GetString(r.GetOrdinal("started_by")),
        TotalSites = r.GetInt32(r.GetOrdinal("total_sites")),
        TotalLibraries = r.GetInt32(r.GetOrdinal("total_libraries")),
        ConfigSnapshot = r.GetString(r.GetOrdinal("config_snapshot")),
        ErrorMessage = r.GetString(r.GetOrdinal("error_message")),
        ModuleVersion = r.GetString(r.GetOrdinal("module_version")),
        Notes = r.GetString(r.GetOrdinal("notes"))
    };
}
