@{
    RootModule           = 'SPOTrim.psm1'
    ModuleVersion        = '1.0.0'
    CompatiblePSEditions = @('Core')
    GUID                 = 'a3c5e7f9-1b2d-4f6a-8c0e-2d4f6a8b0c1e'
    Author               = 'Jos Lieben (jos@lieben.nu)'
    CompanyName          = 'Lieben Consultancy'
    Copyright            = 'https://www.lieben.nu/liebensraum/commercial-use/'
    HelpInfoURI          = 'https://lieben.nu/liebensraum/spotrim/'
    Description          = @'
SPOTrim - SharePoint Online Storage Trimming & Version Management

Scans and manages versioning settings across all SharePoint Online and OneDrive sites.
Identifies storage waste from excessive file versions and provides cleanup capabilities.

INSTALLATION:
    Install-PSResource -Name SPOTrim -Repository PSGallery

USAGE:
    Import-Module SPOTrim   # Opens GUI automatically in your browser

Free for non-commercial use. See https://www.lieben.nu/liebensraum/commercial-use/
'@
    PowerShellVersion    = '7.4'

    RequiredAssemblies   = @(
        'lib\SPOTrim.Engine.dll',
        'lib\Microsoft.Data.Sqlite.dll',
        'lib\SQLitePCLRaw.core.dll',
        'lib\SQLitePCLRaw.provider.e_sqlite3.dll',
        'lib\SQLitePCLRaw.batteries_v2.dll',
        'lib\ClosedXML.dll',
        'lib\DocumentFormat.OpenXml.dll',
        'lib\SixLabors.Fonts.dll'
    )

    FunctionsToExport    = @(
        'Connect-SPOTrim',
        'Disconnect-SPOTrim',
        'Start-SPOTrimScan',
        'Stop-SPOTrimScan',
        'Start-SPOTrimGUI',
        'Stop-SPOTrimGUI',
        'Get-SPOTrimConfig',
        'Set-SPOTrimConfig'
    )

    CmdletsToExport      = @()
    VariablesToExport     = @()
    AliasesToExport       = @()

    PrivateData          = @{
        PSData = @{
            Tags         = @('SharePoint', 'OneDrive', 'SPO', 'Storage', 'Versioning', 'Cleanup', 'Trim')
            LicenseUri   = 'https://www.lieben.nu/liebensraum/commercial-use/'
            ProjectUri   = 'https://lieben.nu/liebensraum/spotrim'
            ReleaseNotes = 'Initial release'
        }
    }
}
