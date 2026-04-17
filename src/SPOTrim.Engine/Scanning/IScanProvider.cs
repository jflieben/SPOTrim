using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Scanning;

/// <summary>
/// Interface for scan providers. Each implementation handles one scan phase.
/// </summary>
public interface IScanProvider
{
    /// <summary>Display name for the scan phase (Discovery, VersionAnalysis, Cleanup).</summary>
    string Category { get; }

    /// <summary>
    /// Execute the scan phase, yielding results as they are discovered.
    /// The orchestrator handles batching inserts and progress tracking.
    /// </summary>
    IAsyncEnumerable<SiteInfo> ScanAsync(ScanContext context, CancellationToken ct);
}

/// <summary>
/// Shared context passed to all scan providers during a scan.
/// </summary>
public sealed class ScanContext
{
    public required long ScanId { get; init; }
    public required string TenantDomain { get; init; }
    public required string UserPrincipalName { get; init; }
    public required AppConfig Config { get; init; }
    public required Action<string, int> ReportProgress { get; init; }
    public required Action<int> SetTotalTargets { get; init; }
    public required Action CompleteTarget { get; init; }
    public required Action FailTarget { get; init; }
}
