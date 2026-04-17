function Disconnect-SPOTrim {
    <#
    .SYNOPSIS
        Disconnects from Microsoft 365 and clears all cached tokens.
    .EXAMPLE
        Disconnect-SPOTrim
    #>
    [CmdletBinding()]
    param()

    $engine = Get-SPOTrimEngine
    $engine.Disconnect()
    Write-Host "Disconnected from Microsoft 365" -ForegroundColor Yellow
}
