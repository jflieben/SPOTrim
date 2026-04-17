using System.Collections.Concurrent;
using SPOTrim.Engine.Database;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Scanning;

/// <summary>
/// Manages scan lifecycle: dispatches to IScanProvider implementations,
/// collects results, streams to SQLite, tracks progress.
/// </summary>
public sealed class ScanOrchestrator
{
    private readonly SqliteDb _db;
    private readonly ScanRepository _scanRepo;
    private readonly SiteRepository _siteRepo;
    private readonly AuditRepository _auditRepo;
    private readonly List<IScanProvider> _providers = new();

    private CancellationTokenSource? _cts;
    private Task? _scanTask;
    private readonly ConcurrentDictionary<string, ScanProgress> _progress = new();
    private readonly List<string> _logBuffer = new();
    private readonly object _logLock = new();
    private long _activeScanId;

    private const int InsertBatchSize = 500;
    private const int MaxLogBufferSize = 500;

    public bool IsScanning => _scanTask is { IsCompleted: false };
    public long ActiveScanId => _activeScanId;

    public ScanOrchestrator(SqliteDb db, ScanRepository scanRepo, SiteRepository siteRepo, AuditRepository auditRepo)
    {
        _db = db;
        _scanRepo = scanRepo;
        _siteRepo = siteRepo;
        _auditRepo = auditRepo;
    }

    public void RegisterProvider(IScanProvider provider)
    {
        _providers.Add(provider);
    }

    public long StartScan(ScanContext context, List<string> scanPhases)
    {
        if (IsScanning)
            throw new InvalidOperationException("A scan is already in progress.");

        _cts = new CancellationTokenSource();
        _progress.Clear();
        ClearLogBuffer();
        _activeScanId = context.ScanId;

        foreach (var phase in scanPhases)
        {
            _progress[phase] = new ScanProgress
            {
                ScanId = context.ScanId,
                Category = phase,
                Status = "Pending",
                StartedAt = DateTime.UtcNow.ToString("O")
            };
        }

        _scanTask = Task.Run(async () =>
        {
            try
            {
                _scanRepo.UpdateStatus(context.ScanId, ScanStatus.Running);
                AddLog("Scan started", 3);
                await ExecuteScanAsync(context, scanPhases, _cts.Token);

                var totalSites = _siteRepo.CountByScan(context.ScanId);
                _scanRepo.UpdateStatus(context.ScanId, ScanStatus.Completed, totalSites: totalSites);
                AddLog($"Scan completed — {totalSites} sites found", 3);
            }
            catch (OperationCanceledException)
            {
                _scanRepo.UpdateStatus(context.ScanId, ScanStatus.Cancelled);
                AddLog("Scan cancelled by user", 2);
            }
            catch (Exception ex)
            {
                _scanRepo.UpdateStatus(context.ScanId, ScanStatus.Failed, error: ex.Message);
                AddLog($"Scan failed: {ex.Message}", 1);
            }
        });

        return context.ScanId;
    }

    public void CancelScan()
    {
        _cts?.Cancel();
    }

    public AggregatedProgress? GetProgress()
    {
        if (!IsScanning && _progress.IsEmpty) return null;

        var categories = _progress.ToDictionary(p => p.Key, p => p.Value);
        var totalTargets = categories.Values.Sum(p => p.TotalTargets);
        var completedTargets = categories.Values.Sum(p => p.CompletedTargets);
        var overallPercent = totalTargets > 0 ? (int)(completedTargets * 100.0 / totalTargets) : 0;

        var currentCategory = categories.Values
            .FirstOrDefault(p => p.Status == "Running")?.Category ?? "";

        return new AggregatedProgress
        {
            Scanning = IsScanning,
            ScanId = _activeScanId,
            OverallPercent = overallPercent,
            CurrentCategory = currentCategory,
            CurrentTarget = categories.Values.FirstOrDefault(p => p.Status == "Running")?.CurrentTarget ?? "",
            Categories = categories,
            RecentLogs = GetRecentLogs(),
            Status = IsScanning ? "Running" : (_progress.Values.All(p => p.Status == "Completed") ? "Completed" : "Failed")
        };
    }

    public void Shutdown()
    {
        _cts?.Cancel();
        try { _scanTask?.Wait(TimeSpan.FromSeconds(10)); } catch { /* best effort */ }
        _cts?.Dispose();
    }

    private async Task ExecuteScanAsync(ScanContext context, List<string> scanPhases, CancellationToken ct)
    {
        foreach (var phase in scanPhases)
        {
            ct.ThrowIfCancellationRequested();

            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Category, phase, StringComparison.OrdinalIgnoreCase));

            if (provider == null)
            {
                AddLog($"No provider for phase '{phase}', skipping", 2);
                continue;
            }

            _progress[phase] = _progress[phase] with { Status = "Running" };
            AddLog($"Starting {phase}...", 3);

            var phaseContext = new ScanContext
            {
                ScanId = context.ScanId,
                TenantDomain = context.TenantDomain,
                UserPrincipalName = context.UserPrincipalName,
                Config = context.Config,
                ReportProgress = (msg, level) =>
                {
                    AddLog(msg, level);
                },
                SetTotalTargets = count =>
                {
                    if (_progress.TryGetValue(phase, out var p))
                        _progress[phase] = p with { TotalTargets = count };
                },
                CompleteTarget = () =>
                {
                    if (_progress.TryGetValue(phase, out var p))
                        _progress[phase] = p with { CompletedTargets = p.CompletedTargets + 1 };
                },
                FailTarget = () =>
                {
                    if (_progress.TryGetValue(phase, out var p))
                        _progress[phase] = p with { FailedTargets = p.FailedTargets + 1 };
                }
            };

            var batch = new List<SiteInfo>();
            await foreach (var site in provider.ScanAsync(phaseContext, ct))
            {
                batch.Add(site);
                if (batch.Count >= InsertBatchSize)
                {
                    _siteRepo.InsertBatch(batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                _siteRepo.InsertBatch(batch);

            _progress[phase] = _progress[phase] with { Status = "Completed" };
            AddLog($"{phase} completed", 3);
        }
    }

    private void AddLog(string message, int level)
    {
        lock (_logLock)
        {
            _logBuffer.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
            if (_logBuffer.Count > MaxLogBufferSize)
                _logBuffer.RemoveAt(0);
        }
    }

    private List<string> GetRecentLogs()
    {
        lock (_logLock)
        {
            return _logBuffer.TakeLast(50).ToList();
        }
    }

    private void ClearLogBuffer()
    {
        lock (_logLock) { _logBuffer.Clear(); }
    }
}
