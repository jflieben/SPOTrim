<#
.SYNOPSIS
    Builds the SPOTrim module — compiles .NET engine, copies DLLs + GUI to module/lib/.
.DESCRIPTION
    Runs dotnet publish on the Engine project and assembles the final module folder.
    If module/lib/ DLLs are locked (e.g. module is loaded in another PS session), the script
    will report the locking process and exit with guidance.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..' 'module' 'lib')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$engineProject = Join-Path $repoRoot 'src' 'SPOTrim.Engine' 'SPOTrim.Engine.csproj'
$publishDir = Join-Path $repoRoot 'src' 'SPOTrim.Engine' 'bin' $Configuration 'net8.0' 'publish'

# Sync version from psd1 (single source of truth) → csproj
$psd1Path = Join-Path $repoRoot 'module' 'SPOTrim.psd1'
$manifest = Import-PowerShellDataFile $psd1Path
$version = $manifest.ModuleVersion
$csprojContent = Get-Content $engineProject -Raw
if ($csprojContent -match '<Version>[^<]+</Version>') {
    $csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>$version</Version>"
    Set-Content $engineProject -Value $csprojContent -NoNewline
    Write-Host "Synced .csproj version to $version (from psd1)" -ForegroundColor Green
}

# Pre-check: detect locked DLLs before spending time on a build
$lockTarget = Join-Path $OutputPath 'SPOTrim.Engine.dll'
if (Test-Path $lockTarget) {
    try {
        [IO.File]::Open($lockTarget, 'Open', 'ReadWrite', 'None').Close()
    }
    catch {
        Write-Host "`nERROR: module/lib/ DLLs are locked by another process." -ForegroundColor Red
        Write-Host "This usually means SPOTrim is imported in another PowerShell session.`n" -ForegroundColor Yellow
        Write-Host "Fix: close that PS session (or run Remove-Module SPOTrim), then retry." -ForegroundColor Yellow
        throw "Cannot overwrite locked DLLs in $OutputPath. Close the PowerShell session that has the module loaded."
    }
}

Write-Host "Building SPOTrim.Engine ($Configuration)..." -ForegroundColor Cyan
dotnet publish $engineProject -c $Configuration -o $publishDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# Ensure output directory exists
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Copy all DLLs (engine + dependencies)
Write-Host "Copying DLLs to $OutputPath..." -ForegroundColor Cyan
Get-ChildItem -Path $publishDir -Filter '*.dll' | ForEach-Object {
    Copy-Item $_.FullName -Destination $OutputPath -Force
}

# Also copy native SQLite binary (runtimes/{rid}/native/)
$nativeDir = Join-Path $publishDir 'runtimes'
if (Test-Path $nativeDir) {
    $runtimesDest = Join-Path $OutputPath 'runtimes'
    if (Test-Path $runtimesDest) { Remove-Item $runtimesDest -Recurse -Force }
    Copy-Item $nativeDir -Destination $runtimesDest -Recurse -Force
    Write-Host "Copied native runtimes." -ForegroundColor Green
}

$dllCount = (Get-ChildItem -Path $OutputPath -Filter '*.dll').Count
Write-Host "Build complete — $dllCount DLLs in $OutputPath" -ForegroundColor Green
