function Get-SPOTrimConfig {
    <#
    .SYNOPSIS
        Gets the current SPOTrim configuration.
    .EXAMPLE
        Get-SPOTrimConfig
    #>
    [CmdletBinding()]
    param()

    $engine = Get-SPOTrimEngine
    return $engine.GetConfig()
}
