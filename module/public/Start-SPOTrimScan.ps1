function Start-SPOTrimScan {
    <#
    .SYNOPSIS
        Starts a scan of SharePoint Online sites, libraries, and versioning settings.
    .PARAMETER ScanType
        Type of scan to perform: Discovery, VersionAnalysis, Cleanup, Full.
    .EXAMPLE
        Start-SPOTrimScan
    .EXAMPLE
        Start-SPOTrimScan -ScanType VersionAnalysis
    #>
    [CmdletBinding()]
    param(
        [ValidateSet('Discovery', 'VersionAnalysis', 'Cleanup', 'Full')]
        [string]$ScanType = 'Discovery'
    )

    $engine = Get-SPOTrimEngine
    $task = $engine.StartScanAsync($ScanType)
    $scanId = $task.GetAwaiter().GetResult()
    Write-Host "Scan started (ID: $scanId, Type: $ScanType). Use the GUI to monitor progress." -ForegroundColor Cyan
    return $scanId
}
