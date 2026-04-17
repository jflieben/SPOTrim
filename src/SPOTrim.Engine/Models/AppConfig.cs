namespace SPOTrim.Engine.Models;

public sealed class AppConfig
{
    public int GuiPort { get; set; } = 8080;
    public int MaxThreads { get; set; } = 5;
    public string OutputFormat { get; set; } = "XLSX";          // XLSX | CSV
    public string LogLevel { get; set; } = "Minimal";          // Full | Normal | Minimal | None
    public int DefaultVersionLimit { get; set; } = 100;        // Default major version limit to set
    public int MinorVersionLimit { get; set; } = 0;            // Default minor version limit
    public int CleanupBatchSize { get; set; } = 500;           // Items per cleanup batch
    public int MaxJobRetries { get; set; } = 3;
    public bool IncludeOneDrive { get; set; } = true;          // Include OneDrive sites in scans
    public bool DryRun { get; set; } = true;                   // Dry-run mode by default (no actual deletions)
}
