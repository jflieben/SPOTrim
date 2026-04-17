function Stop-SPOTrimGUI {
    <#
    .SYNOPSIS
        Stops the SPOTrim web GUI server.
    .EXAMPLE
        Stop-SPOTrimGUI
    #>
    [CmdletBinding()]
    param()

    $engine = Get-SPOTrimEngine
    $task = $engine.StopServerAsync()
    $task.GetAwaiter().GetResult()
    Write-Host "SPOTrim GUI stopped" -ForegroundColor Yellow
}
