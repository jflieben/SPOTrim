function Set-SPOTrimConfig {
    <#
    .SYNOPSIS
        Updates SPOTrim configuration settings.
    .PARAMETER Config
        An AppConfig object with updated settings.
    .EXAMPLE
        $config = Get-SPOTrimConfig
        $config.DefaultVersionLimit = 50
        Set-SPOTrimConfig -Config $config
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [SPOTrim.Engine.Models.AppConfig]$Config
    )

    $engine = Get-SPOTrimEngine
    $engine.UpdateConfig($Config)
    Write-Host "Configuration updated" -ForegroundColor Green
}
