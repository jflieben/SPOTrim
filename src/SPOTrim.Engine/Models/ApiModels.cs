namespace SPOTrim.Engine.Models;

public sealed class StatusResponse
{
    public bool Connected { get; set; }
    public string? TenantId { get; set; }
    public string? TenantDomain { get; set; }
    public string? UserPrincipalName { get; set; }
    public string ModuleVersion { get; set; } = "";
    public bool Scanning { get; set; }
    public long? ActiveScanId { get; set; }
    public string? RefreshTokenExpiry { get; set; }
}

public sealed record ScanProgress
{
    public long ScanId { get; init; }
    public string Category { get; init; } = "";
    public int TotalTargets { get; init; }
    public int CompletedTargets { get; init; }
    public int FailedTargets { get; init; }
    public int ItemsFound { get; init; }
    public string CurrentTarget { get; init; } = "";
    public string Status { get; init; } = "Pending";
    public string StartedAt { get; init; } = "";
}

public sealed class AggregatedProgress
{
    public bool Scanning { get; set; }
    public long ScanId { get; set; }
    public int OverallPercent { get; set; }
    public string CurrentCategory { get; set; } = "";
    public string CurrentTarget { get; set; } = "";
    public Dictionary<string, ScanProgress> Categories { get; set; } = new();
    public List<string> RecentLogs { get; set; } = new();
    public string Status { get; set; } = "Pending";
}

/// <summary>Generic API response wrapper.</summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    public static ApiResponse Ok() => new() { Success = true };
    public static ApiResponse Fail(string error) => new() { Success = false, Error = error };
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public new static ApiResponse<T> Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Dashboard summary statistics.</summary>
public sealed class DashboardStats
{
    public int TotalSites { get; set; }
    public int TotalLibraries { get; set; }
    public long TotalStorageBytes { get; set; }
    public long TotalVersionStorageBytes { get; set; }
    public long PotentialSavingsBytes { get; set; }
    public int LibrariesOverLimit { get; set; }
    public int ScanCount { get; set; }
    public int CleanupActionsCompleted { get; set; }
    public long BytesFreed { get; set; }
}
