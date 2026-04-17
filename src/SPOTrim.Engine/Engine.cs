using System.Diagnostics;
using System.Text.Json;
using SPOTrim.Engine.Auth;
using SPOTrim.Engine.Database;
using SPOTrim.Engine.Export;
using SPOTrim.Engine.Graph;
using SPOTrim.Engine.Http;
using SPOTrim.Engine.Models;
using SPOTrim.Engine.Scanning;

namespace SPOTrim.Engine;

/// <summary>
/// Main entry point / facade for the SPOTrim engine.
/// Wires together all subsystems: database, auth, HTTP server, scanning, export.
/// Called from the PowerShell module wrapper.
/// </summary>
public sealed class Engine : IDisposable
{
    public static string ModuleVersion { get; } =
        typeof(Engine).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly SqliteDb _db;
    private readonly ConfigRepository _configRepo;
    private readonly ScanRepository _scanRepo;
    private readonly SiteRepository _siteRepo;
    private readonly AuditRepository _auditRepo;
    private readonly TokenCache _tokenCache;
    private readonly DelegatedAuth _auth;
    private readonly ExcelExporter _excelExporter;
    private readonly CsvExporter _csvExporter;

    private GraphClient? _graphClient;
    private SharePointRestClient? _spClient;
    private ScanOrchestrator? _orchestrator;
    private WebServer? _webServer;

    private AppConfig _config;
    private readonly Task _sessionRestoreTask;

    public Engine(string databasePath)
    {
        _db = new SqliteDb(databasePath);
        _db.Initialize();

        _configRepo = new ConfigRepository(_db);
        _scanRepo = new ScanRepository(_db);
        _siteRepo = new SiteRepository(_db);
        _auditRepo = new AuditRepository(_db);

        var persistDir = Path.GetDirectoryName(databasePath) ?? ".";
        _tokenCache = new TokenCache(persistDir);

        _auth = new DelegatedAuth(_tokenCache);
        _excelExporter = new ExcelExporter();
        _csvExporter = new CsvExporter();

        _config = LoadConfig();

        _sessionRestoreTask = Task.Run(async () =>
        {
            try
            {
                if (await _auth.TryRestoreSessionAsync())
                    InitializeClients();
            }
            catch { /* best effort */ }
        });
    }

    public async Task EnsureSessionRestoredAsync(int timeoutMs = 5000)
    {
        await Task.WhenAny(_sessionRestoreTask, Task.Delay(timeoutMs));
    }

    // ── Status ──────────────────────────────────────────────────

    public StatusResponse GetStatus() => new()
    {
        Connected = _auth.IsConnected,
        TenantId = _auth.TenantId,
        TenantDomain = _auth.TenantDomain,
        UserPrincipalName = _auth.UserPrincipalName,
        ModuleVersion = ModuleVersion,
        Scanning = _orchestrator?.IsScanning ?? false,
        ActiveScanId = _orchestrator?.IsScanning == true ? _orchestrator.ActiveScanId : null,
        RefreshTokenExpiry = _auth.RefreshTokenExpiry?.ToString("O")
    };

    // ── Authentication ──────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _auth.AuthenticateAsync(ct);
        InitializeClients();
    }

    public void Disconnect()
    {
        _auth.SignOut();
        _graphClient = null;
        _spClient = null;
    }

    private void InitializeClients()
    {
        _graphClient = new GraphClient(_auth, _config.MaxThreads);
        _spClient = new SharePointRestClient(_auth, _config.MaxThreads);

        _orchestrator = new ScanOrchestrator(_db, _scanRepo, _siteRepo, _auditRepo);
        _orchestrator.RegisterProvider(new SiteDiscoveryScanner(_graphClient, _auth));
    }

    // ── Configuration ───────────────────────────────────────────

    public AppConfig GetConfig() => _config;

    public void UpdateConfig(Dictionary<string, JsonElement> updates)
    {
        foreach (var (key, value) in updates)
        {
            var strValue = value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.GetRawText();
            _configRepo.Set(key, strValue);
        }
        _config = LoadConfig();
    }

    public void UpdateConfig(AppConfig config)
    {
        _configRepo.Set("guiPort", config.GuiPort.ToString());
        _configRepo.Set("maxThreads", config.MaxThreads.ToString());
        _configRepo.Set("outputFormat", config.OutputFormat);
        _configRepo.Set("logLevel", config.LogLevel);
        _configRepo.Set("defaultVersionLimit", config.DefaultVersionLimit.ToString());
        _configRepo.Set("minorVersionLimit", config.MinorVersionLimit.ToString());
        _configRepo.Set("cleanupBatchSize", config.CleanupBatchSize.ToString());
        _configRepo.Set("maxJobRetries", config.MaxJobRetries.ToString());
        _configRepo.Set("includeOneDrive", config.IncludeOneDrive.ToString());
        _configRepo.Set("dryRun", config.DryRun.ToString());
        _config = LoadConfig();
    }

    private AppConfig LoadConfig()
    {
        var all = _configRepo.GetAll();
        return new AppConfig
        {
            GuiPort = GetInt(all, "guiPort", 8080),
            MaxThreads = GetInt(all, "maxThreads", 5),
            OutputFormat = GetStr(all, "outputFormat", "XLSX"),
            LogLevel = GetStr(all, "logLevel", "Minimal"),
            DefaultVersionLimit = GetInt(all, "defaultVersionLimit", 100),
            MinorVersionLimit = GetInt(all, "minorVersionLimit", 0),
            CleanupBatchSize = GetInt(all, "cleanupBatchSize", 500),
            MaxJobRetries = GetInt(all, "maxJobRetries", 3),
            IncludeOneDrive = GetBool(all, "includeOneDrive", true),
            DryRun = GetBool(all, "dryRun", true)
        };
    }

    private static int GetInt(Dictionary<string, string> d, string key, int def)
        => d.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : def;
    private static string GetStr(Dictionary<string, string> d, string key, string def)
        => d.TryGetValue(key, out var v) ? v : def;
    private static bool GetBool(Dictionary<string, string> d, string key, bool def)
        => d.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : def;

    // ── Scanning ────────────────────────────────────────────────

    public Task<long> StartScanAsync(string scanType = "Discovery", CancellationToken ct = default)
    {
        if (!_auth.IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        if (_orchestrator == null)
            InitializeClients();

        var scan = new ScanInfo
        {
            TenantId = _auth.TenantId ?? "",
            TenantDomain = _auth.TenantDomain ?? "",
            Status = ScanStatus.Pending,
            ScanType = scanType,
            StartedAt = DateTime.UtcNow.ToString("O"),
            StartedBy = _auth.UserPrincipalName ?? "",
            ConfigSnapshot = JsonSerializer.Serialize(_config),
            ModuleVersion = ModuleVersion
        };

        var scanId = _scanRepo.Create(scan);

        var context = new ScanContext
        {
            ScanId = scanId,
            TenantDomain = _auth.TenantDomain ?? "",
            UserPrincipalName = _auth.UserPrincipalName ?? "",
            Config = _config,
            ReportProgress = (_, _) => { },
            SetTotalTargets = _ => { },
            CompleteTarget = () => { },
            FailTarget = () => { }
        };

        // Map scan type to phases
        var phases = scanType switch
        {
            "Discovery" => new List<string> { "Discovery" },
            "VersionAnalysis" => new List<string> { "Discovery", "VersionAnalysis" },
            "Cleanup" => new List<string> { "Discovery", "VersionAnalysis", "Cleanup" },
            "Full" => new List<string> { "Discovery", "VersionAnalysis", "Cleanup" },
            _ => new List<string> { "Discovery" }
        };

        _orchestrator!.StartScan(context, phases);
        _auditRepo.Log("ScanStarted", _auth.UserPrincipalName ?? "", $"Scan started: {scanType}", scanId);
        return Task.FromResult(scanId);
    }

    public void CancelScan() => _orchestrator?.CancelScan();

    public AggregatedProgress? GetScanProgress() => _orchestrator?.GetProgress();

    // ── Results ─────────────────────────────────────────────────

    public List<ScanInfo> GetScans(string? tenantId = null) => _scanRepo.GetAll(tenantId);

    public List<SiteInfo> GetSites(long scanId) => _siteRepo.GetByScan(scanId);

    // ── Export ───────────────────────────────────────────────────

    public (byte[] bytes, string fileName, string contentType) ExportScan(long scanId, string format)
    {
        var scan = _scanRepo.GetById(scanId)
            ?? throw new KeyNotFoundException($"Scan {scanId} not found");

        var sites = _siteRepo.GetByScan(scanId);

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = _csvExporter.ExportSites(sites);
            _auditRepo.Log("Export", _auth.UserPrincipalName ?? "", $"CSV export: scan {scanId}");
            return (bytes, $"SPOTrim_Sites_{scanId}.csv", "text/csv");
        }
        else
        {
            var bytes = _excelExporter.ExportSites(sites);
            _auditRepo.Log("Export", _auth.UserPrincipalName ?? "", $"XLSX export: scan {scanId}");
            return (bytes, $"SPOTrim_Sites_{scanId}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
    }

    // ── Dashboard ───────────────────────────────────────────────

    public DashboardStats GetDashboardStats()
    {
        // Placeholder — will be enriched as more scanners are added
        var scans = _scanRepo.GetAll();
        return new DashboardStats
        {
            ScanCount = scans.Count,
            TotalSites = scans.LastOrDefault()?.TotalSites ?? 0,
            TotalLibraries = scans.LastOrDefault()?.TotalLibraries ?? 0
        };
    }

    // ── Audit Log ───────────────────────────────────────────────

    public void AuditLog(string action, string details, long? scanId = null)
        => _auditRepo.Log(action, _auth.UserPrincipalName ?? "", details, scanId);

    public List<AuditEntry> GetAuditLog(int limit = 100)
        => _auditRepo.GetRecent(limit);

    // ── Database Management ─────────────────────────────────────

    public DatabaseInfo GetDatabaseInfo() => _db.GetDatabaseInfo();

    public void ResetDatabase()
    {
        if (_orchestrator?.IsScanning == true)
            throw new InvalidOperationException("Cannot reset database while a scan is running.");
        _db.ResetDatabase();
        AuditLog("database_reset", "Database cleared");
    }

    // ── HTTP Server ─────────────────────────────────────────────

    public void StartServer(int port, string staticFilesPath, bool openBrowser = true)
    {
        _webServer?.Dispose();

        var server = new WebServer(port, staticFilesPath);
        ApiRoutes.Register(server, this);
        server.Start();
        _webServer = server;

        if (openBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true });
            }
            catch { /* best effort */ }
        }
    }

    public async Task StopServerAsync()
    {
        if (_webServer != null)
            await _webServer.StopAsync();
    }

    public void Dispose()
    {
        _orchestrator?.Shutdown();
        _webServer?.Dispose();
        _db.Dispose();
    }
}
