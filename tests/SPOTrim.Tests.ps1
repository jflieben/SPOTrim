#Requires -Modules Pester

Describe 'SPOTrim Module' {

    BeforeAll {
        $modulePath = Join-Path $PSScriptRoot '..' 'module'
        $manifestPath = Join-Path $modulePath 'SPOTrim.psd1'
    }

    Context 'Module Manifest' {
        It 'Has a valid manifest' {
            { Test-ModuleManifest -Path $manifestPath } | Should -Not -Throw
        }

        It 'Has the correct module name' {
            $manifest = Test-ModuleManifest -Path $manifestPath
            $manifest.Name | Should -Be 'SPOTrim'
        }

        It 'Exports expected functions' {
            $manifest = Test-ModuleManifest -Path $manifestPath
            $expected = @(
                'Connect-SPOTrim',
                'Disconnect-SPOTrim',
                'Start-SPOTrimScan',
                'Stop-SPOTrimScan',
                'Start-SPOTrimGUI',
                'Stop-SPOTrimGUI',
                'Get-SPOTrimConfig',
                'Set-SPOTrimConfig'
            )
            foreach ($fn in $expected) {
                $manifest.ExportedFunctions.Keys | Should -Contain $fn
            }
        }

        It 'Requires PowerShell 7.4+' {
            $manifest = Test-ModuleManifest -Path $manifestPath
            $manifest.PowerShellVersion | Should -Be '7.4'
        }

        It 'Has no external module dependencies' {
            $manifest = Test-ModuleManifest -Path $manifestPath
            $manifest.RequiredModules | Should -BeNullOrEmpty
        }
    }

    Context 'Public Functions' {
        It 'All public .ps1 files have valid PowerShell syntax' {
            $publicPath = Join-Path $modulePath 'public'
            Get-ChildItem -Path $publicPath -Filter '*.ps1' -Recurse | ForEach-Object {
                $errors = $null
                [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors)
                $errors | Should -BeNullOrEmpty -Because "$($_.Name) should have valid syntax"
            }
        }

        It 'Module psm1 has valid syntax' {
            $psm1Path = Join-Path $modulePath 'SPOTrim.psm1'
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($psm1Path, [ref]$null, [ref]$errors)
            $errors | Should -BeNullOrEmpty
        }
    }

    Context 'GUI Files' {
        It 'index.html exists' {
            Join-Path $modulePath 'gui' 'static' 'index.html' | Should -Exist
        }

        It 'app.js exists' {
            Join-Path $modulePath 'gui' 'static' 'app.js' | Should -Exist
        }

        It 'style.css exists' {
            Join-Path $modulePath 'gui' 'static' 'style.css' | Should -Exist
        }
    }

    Context 'Build Output' {
        It 'Solution file exists at repo root' {
            Join-Path $PSScriptRoot '..' 'SPOTrim.sln' | Should -Exist
        }

        It 'Engine csproj exists' {
            Join-Path $PSScriptRoot '..' 'src' 'SPOTrim.Engine' 'SPOTrim.Engine.csproj' | Should -Exist
        }

        It 'Schema.sql is present' {
            Join-Path $PSScriptRoot '..' 'src' 'SPOTrim.Engine' 'Database' 'Schema.sql' | Should -Exist
        }
    }
}
