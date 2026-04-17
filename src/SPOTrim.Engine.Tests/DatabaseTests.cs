using Xunit;
using SPOTrim.Engine.Database;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Tests;

public class DatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDb _db;

    public DatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"spotrim_test_{Guid.NewGuid():N}.db");
        _db = new SqliteDb(_dbPath);
        _db.Initialize();
    }

    [Fact]
    public void Initialize_CreatesDatabase()
    {
        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void ConfigRepository_SetAndGet()
    {
        var repo = new ConfigRepository(_db);
        repo.Set("testKey", "testValue");
        Assert.Equal("testValue", repo.Get("testKey"));
    }

    [Fact]
    public void ConfigRepository_GetAll_ReturnsAll()
    {
        var repo = new ConfigRepository(_db);
        repo.Set("key1", "val1");
        repo.Set("key2", "val2");
        var all = repo.GetAll();
        Assert.True(all.ContainsKey("key1"));
        Assert.True(all.ContainsKey("key2"));
    }

    [Fact]
    public void ConfigRepository_Set_UpdatesExisting()
    {
        var repo = new ConfigRepository(_db);
        repo.Set("key", "v1");
        repo.Set("key", "v2");
        Assert.Equal("v2", repo.Get("key"));
    }

    [Fact]
    public void ScanRepository_CreateAndGet()
    {
        var repo = new ScanRepository(_db);
        var scan = new ScanInfo
        {
            TenantId = "test-tenant",
            TenantDomain = "test.onmicrosoft.com",
            ScanType = "Discovery",
            StartedAt = DateTime.UtcNow.ToString("O"),
            StartedBy = "test@user.com",
            ConfigSnapshot = "{}",
            ModuleVersion = "1.0.0"
        };

        var id = repo.Create(scan);
        Assert.True(id > 0);

        var retrieved = repo.GetById(id);
        Assert.NotNull(retrieved);
        Assert.Equal("test-tenant", retrieved.TenantId);
        Assert.Equal("Discovery", retrieved.ScanType);
    }

    [Fact]
    public void ScanRepository_UpdateStatus()
    {
        var repo = new ScanRepository(_db);
        var id = repo.Create(new ScanInfo
        {
            TenantId = "t1",
            TenantDomain = "t1.onmicrosoft.com",
            ScanType = "Discovery",
            StartedAt = DateTime.UtcNow.ToString("O"),
            StartedBy = "user",
            ConfigSnapshot = "{}",
            ModuleVersion = "1.0.0"
        });

        repo.UpdateStatus(id, ScanStatus.Completed, totalSites: 42);

        var scan = repo.GetById(id);
        Assert.NotNull(scan);
        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(42, scan.TotalSites);
    }

    [Fact]
    public void SiteRepository_InsertBatchAndRetrieve()
    {
        var scanRepo = new ScanRepository(_db);
        var scanId = scanRepo.Create(new ScanInfo
        {
            TenantId = "t1",
            TenantDomain = "t1.onmicrosoft.com",
            ScanType = "Discovery",
            StartedAt = DateTime.UtcNow.ToString("O"),
            StartedBy = "user",
            ConfigSnapshot = "{}",
            ModuleVersion = "1.0.0"
        });

        var siteRepo = new SiteRepository(_db);
        var sites = new List<SiteInfo>
        {
            new() { ScanId = scanId, SiteId = "s1", SiteUrl = "https://test.sharepoint.com/sites/one", SiteTitle = "Site One", SiteType = "TeamSite", Owner = "owner1", LastActivityDate = "" },
            new() { ScanId = scanId, SiteId = "s2", SiteUrl = "https://test.sharepoint.com/sites/two", SiteTitle = "Site Two", SiteType = "OneDrive", Owner = "owner2", LastActivityDate = "" }
        };

        siteRepo.InsertBatch(sites);

        var retrieved = siteRepo.GetByScan(scanId);
        Assert.Equal(2, retrieved.Count);
        Assert.Equal(2, siteRepo.CountByScan(scanId));
    }

    [Fact]
    public void AuditRepository_LogAndRetrieve()
    {
        var repo = new AuditRepository(_db);
        repo.Log("TestAction", "testuser@test.com", "Test details");

        var entries = repo.GetRecent(10);
        Assert.NotEmpty(entries);
        Assert.Equal("TestAction", entries[0].Action);
        Assert.Equal("testuser@test.com", entries[0].UserName);
    }

    [Fact]
    public void DatabaseInfo_ReturnsTableCounts()
    {
        var info = _db.GetDatabaseInfo();
        Assert.NotNull(info);
        Assert.Equal(_dbPath, info.Path);
        Assert.True(info.TableCounts.ContainsKey("scans"));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }
}
