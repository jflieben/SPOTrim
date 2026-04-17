using Xunit;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Tests;

public class ModelTests
{
    [Fact]
    public void AppConfig_HasSaneDefaults()
    {
        var config = new AppConfig();
        Assert.Equal(8080, config.GuiPort);
        Assert.Equal(5, config.MaxThreads);
        Assert.Equal("XLSX", config.OutputFormat);
        Assert.Equal(100, config.DefaultVersionLimit);
        Assert.Equal(0, config.MinorVersionLimit);
        Assert.Equal(500, config.CleanupBatchSize);
        Assert.Equal(3, config.MaxJobRetries);
        Assert.True(config.IncludeOneDrive);
        Assert.True(config.DryRun);
    }

    [Fact]
    public void StatusResponse_DefaultsToDisconnected()
    {
        var status = new StatusResponse();
        Assert.False(status.Connected);
        Assert.False(status.Scanning);
        Assert.Null(status.TenantId);
        Assert.Null(status.TenantDomain);
        Assert.Null(status.ActiveScanId);
    }

    [Fact]
    public void ScanInfo_DefaultStatus_IsPending()
    {
        var scan = new ScanInfo();
        Assert.Equal(ScanStatus.Pending, scan.Status);
        Assert.Equal("Discovery", scan.ScanType);
    }

    [Fact]
    public void SiteInfo_Defaults()
    {
        var site = new SiteInfo();
        Assert.Equal(0, site.StorageUsedBytes);
        Assert.Equal(0, site.StorageQuotaBytes);
        Assert.Equal("", site.SiteUrl);
    }

    [Fact]
    public void LibraryInfo_VersioningEnabledByDefault()
    {
        var lib = new LibraryInfo();
        Assert.True(lib.VersioningEnabled);
        Assert.Equal(500, lib.MajorVersionLimit);
    }

    [Fact]
    public void DashboardStats_Defaults()
    {
        var stats = new DashboardStats();
        Assert.Equal(0, stats.TotalSites);
        Assert.Equal(0, stats.TotalLibraries);
        Assert.Equal(0, stats.BytesFreed);
    }

    [Fact]
    public void ApiResponse_Ok_HasSuccessTrue()
    {
        var response = ApiResponse.Ok();
        Assert.True(response.Success);
        Assert.Null(response.Error);
    }

    [Fact]
    public void ApiResponse_Fail_HasSuccessFalse()
    {
        var response = ApiResponse.Fail("something broke");
        Assert.False(response.Success);
        Assert.Equal("something broke", response.Error);
    }

    [Fact]
    public void ApiResponse_Generic_Ok()
    {
        var response = ApiResponse<string>.Ok("hello");
        Assert.True(response.Success);
        Assert.Equal("hello", response.Data);
    }

    [Fact]
    public void CleanupAction_DefaultStatusIsPending()
    {
        var action = new CleanupAction();
        Assert.Equal("Pending", action.Status);
        Assert.Equal(0, action.BytesFreed);
    }
}
