using Microsoft.Data.Sqlite;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Database;

public sealed class SiteRepository
{
    private readonly SqliteDb _db;

    public SiteRepository(SqliteDb db) => _db = db;

    public long Create(SiteInfo site)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO sites (scan_id, site_id, site_url, site_title, site_type, owner, storage_used_bytes, storage_quota_bytes, last_activity_date)
                            VALUES (@scanId, @siteId, @siteUrl, @siteTitle, @siteType, @owner, @storageUsed, @storageQuota, @lastActivity);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@scanId", site.ScanId);
        cmd.Parameters.AddWithValue("@siteId", site.SiteId);
        cmd.Parameters.AddWithValue("@siteUrl", site.SiteUrl);
        cmd.Parameters.AddWithValue("@siteTitle", site.SiteTitle);
        cmd.Parameters.AddWithValue("@siteType", site.SiteType);
        cmd.Parameters.AddWithValue("@owner", site.Owner);
        cmd.Parameters.AddWithValue("@storageUsed", site.StorageUsedBytes);
        cmd.Parameters.AddWithValue("@storageQuota", site.StorageQuotaBytes);
        cmd.Parameters.AddWithValue("@lastActivity", site.LastActivityDate);
        return (long)cmd.ExecuteScalar()!;
    }

    public void InsertBatch(List<SiteInfo> sites)
    {
        if (sites.Count == 0) return;

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO sites (scan_id, site_id, site_url, site_title, site_type, owner, storage_used_bytes, storage_quota_bytes, last_activity_date)
                            VALUES (@scanId, @siteId, @siteUrl, @siteTitle, @siteType, @owner, @storageUsed, @storageQuota, @lastActivity)";
        var pScanId = cmd.Parameters.Add("@scanId", SqliteType.Integer);
        var pSiteId = cmd.Parameters.Add("@siteId", SqliteType.Text);
        var pSiteUrl = cmd.Parameters.Add("@siteUrl", SqliteType.Text);
        var pSiteTitle = cmd.Parameters.Add("@siteTitle", SqliteType.Text);
        var pSiteType = cmd.Parameters.Add("@siteType", SqliteType.Text);
        var pOwner = cmd.Parameters.Add("@owner", SqliteType.Text);
        var pStorageUsed = cmd.Parameters.Add("@storageUsed", SqliteType.Integer);
        var pStorageQuota = cmd.Parameters.Add("@storageQuota", SqliteType.Integer);
        var pLastActivity = cmd.Parameters.Add("@lastActivity", SqliteType.Text);

        foreach (var site in sites)
        {
            pScanId.Value = site.ScanId;
            pSiteId.Value = site.SiteId;
            pSiteUrl.Value = site.SiteUrl;
            pSiteTitle.Value = site.SiteTitle;
            pSiteType.Value = site.SiteType;
            pOwner.Value = site.Owner;
            pStorageUsed.Value = site.StorageUsedBytes;
            pStorageQuota.Value = site.StorageQuotaBytes;
            pLastActivity.Value = site.LastActivityDate;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<SiteInfo> GetByScan(long scanId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM sites WHERE scan_id = @scanId ORDER BY site_title";
        cmd.Parameters.AddWithValue("@scanId", scanId);

        var results = new List<SiteInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadSite(reader));
        return results;
    }

    public int CountByScan(long scanId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sites WHERE scan_id = @scanId";
        cmd.Parameters.AddWithValue("@scanId", scanId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static SiteInfo ReadSite(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        ScanId = r.GetInt64(r.GetOrdinal("scan_id")),
        SiteId = r.GetString(r.GetOrdinal("site_id")),
        SiteUrl = r.GetString(r.GetOrdinal("site_url")),
        SiteTitle = r.GetString(r.GetOrdinal("site_title")),
        SiteType = r.GetString(r.GetOrdinal("site_type")),
        Owner = r.GetString(r.GetOrdinal("owner")),
        StorageUsedBytes = r.GetInt64(r.GetOrdinal("storage_used_bytes")),
        StorageQuotaBytes = r.GetInt64(r.GetOrdinal("storage_quota_bytes")),
        LastActivityDate = r.GetString(r.GetOrdinal("last_activity_date"))
    };
}
