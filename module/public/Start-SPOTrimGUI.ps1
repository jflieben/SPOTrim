function Start-SPOTrimGUI {
    <#
    .SYNOPSIS
        Starts the SPOTrim web GUI.
    .PARAMETER Port
        Port to listen on. Defaults to configured port (8080).
    .PARAMETER NoBrowser
        Don't open the browser automatically.
    .EXAMPLE
        Start-SPOTrimGUI
    .EXAMPLE
        Start-SPOTrimGUI -Port 9090 -NoBrowser
    #>
    [CmdletBinding()]
    param(
        [int]$Port = 0,
        [switch]$NoBrowser
    )

    $engine = Get-SPOTrimEngine
    if ($Port -eq 0) { $Port = ($engine.GetConfig()).GuiPort }
    $engine.StartServer($Port, $script:GuiRoot, (-not $NoBrowser))
    Write-Host "SPOTrim GUI started at http://localhost:$Port" -ForegroundColor Cyan
}
