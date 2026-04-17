namespace SPOTrim.Engine.Models;

public sealed class SiteInfo
{
    public long Id { get; set; }
    public long ScanId { get; set; }
    public string SiteId { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string SiteTitle { get; set; } = "";
    public string SiteType { get; set; } = "";              // TeamSite, CommunicationSite, OneDrive, Other
    public string Owner { get; set; } = "";
    public long StorageUsedBytes { get; set; }
    public long StorageQuotaBytes { get; set; }
    public string LastActivityDate { get; set; } = "";
}

public sealed class LibraryInfo
{
    public long Id { get; set; }
    public long ScanId { get; set; }
    public long SiteDbId { get; set; }
    public string LibraryId { get; set; } = "";
    public string LibraryTitle { get; set; } = "";
    public string LibraryUrl { get; set; } = "";
    public int ItemCount { get; set; }
    public int VersionCount { get; set; }
    public bool VersioningEnabled { get; set; } = true;
    public int MajorVersionLimit { get; set; } = 500;
    public int MinorVersionLimit { get; set; }
    public long StorageUsedBytes { get; set; }
    public long VersionStorageBytes { get; set; }
}

public sealed class FileVersionInfo
{
    public long Id { get; set; }
    public long ScanId { get; set; }
    public long LibraryDbId { get; set; }
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int VersionCount { get; set; }
    public long VersionsSizeBytes { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string OldestVersionDate { get; set; } = "";
    public string NewestVersionDate { get; set; } = "";
}

public sealed class CleanupAction
{
    public long Id { get; set; }
    public long ScanId { get; set; }
    public long LibraryDbId { get; set; }
    public string ActionType { get; set; } = "";            // VersionTrim, VersionDelete, FileDelete, VersioningConfig
    public string TargetPath { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string Detail { get; set; } = "";                // JSON detail
    public string Status { get; set; } = "Pending";         // Pending, InProgress, Completed, Failed, Skipped
    public string ErrorMessage { get; set; } = "";
    public string ExecutedAt { get; set; } = "";
    public long BytesFreed { get; set; }
}
