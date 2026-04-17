function Stop-SPOTrimScan {
    <#
    .SYNOPSIS
        Cancels a running scan.
    .EXAMPLE
        Stop-SPOTrimScan
    #>
    [CmdletBinding()]
    param()

    $engine = Get-SPOTrimEngine
    $engine.CancelScan()
    Write-Host "Scan cancellation requested" -ForegroundColor Yellow
}
