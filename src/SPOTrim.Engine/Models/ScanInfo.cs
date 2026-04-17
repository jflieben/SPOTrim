namespace SPOTrim.Engine.Models;

public enum ScanStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class ScanInfo
{
    public long Id { get; set; }
    public string TenantId { get; set; } = "";
    public string TenantDomain { get; set; } = "";
    public ScanStatus Status { get; set; } = ScanStatus.Pending;
    public string ScanType { get; set; } = "Discovery";       // Discovery, VersionAnalysis, Cleanup, Full
    public string StartedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
    public string StartedBy { get; set; } = "";
    public int TotalSites { get; set; }
    public int TotalLibraries { get; set; }
    public string ConfigSnapshot { get; set; } = "{}";
    public string ErrorMessage { get; set; } = "";
    public string ModuleVersion { get; set; } = "";
    public string Notes { get; set; } = "";
}
