# SPOTrim — Copilot Instructions

PowerShell module with a compiled .NET 8 engine that helps configure and clean up Microsoft 365 SharePoint type sites (including OneDrive for Business). Connects using OAuth2 PKCE delegated auth with a standard multi-tenant app by Lieben Consultancy, or bring-your-own.

The tool enumerates all sites in the tenant, analyzes versioning settings per library, and configures them per site or centrally. Provides a browser-based dashboard with key metrics. Supports cleanup of old file versions per configurable parameters, and can delete entire files matching criteria (age, size, labels, orphaned). Takes API limits into account with batched processing, can resume if interrupted, and keeps permanent audit trail surfaced in the GUI.

## Tech Stack
- **Engine**: .NET 8 class library (C#), compiled to DLLs loaded by PowerShell
- **Database**: SQLite via Microsoft.Data.Sqlite (WAL mode, embedded schema)
- **Web GUI**: C# HttpListener server + Vanilla JS SPA (no frameworks, no build step)
- **Excel Export**: ClosedXML (replaces ImportExcel PowerShell module)
- **Auth**: OAuth2 PKCE delegated flow (browser popup → loopback callback)
- **APIs**: Microsoft Graph REST, SharePoint REST
- **Testing**: xUnit (C#) + Pester (PowerShell)
- **CI/CD**: GitHub Actions (build → test → artifact)
- **Publishing**: PSGallery

## Project Structure
```
SPOTrim/
  SPOTrim.sln                      # Solution file
  src/
    SPOTrim.Engine/                 # .NET 8 class library
      Models/                      # AppConfig, StatusResponse, ScanInfo, SiteModels, ApiModels
      Database/                    # SqliteDb, Schema.sql, ConfigRepository, ScanRepository, SiteRepository, AuditRepository
      Http/                        # WebServer, ApiRoutes, StaticFiles
      Auth/                        # DelegatedAuth (PKCE), TokenCache
      Graph/                       # GraphClient, SharePointRestClient
      Scanning/                    # IScanProvider, ScanOrchestrator, SiteDiscoveryScanner
      Export/                      # ExcelExporter, CsvExporter
      Engine.cs                    # Main facade wiring all subsystems
    SPOTrim.Engine.Tests/          # xUnit tests
  module/                          # PowerShell module (published to PSGallery)
    SPOTrim.psd1                   # Module manifest
    SPOTrim.psm1                   # Entry point (loads DLLs, auto-starts GUI)
    public/                        # 8 exported cmdlet wrappers
    gui/static/                    # SPA frontend (index.html, app.js, style.css)
    lib/                           # Compiled DLLs (created by Build-Module.ps1)
  build/                           # Build + publish scripts
  tests/                           # Pester module tests
```

## Development Workflow
1. Make code changes in `src/SPOTrim.Engine/`
2. Build: `dotnet build SPOTrim.sln`
3. Test C#: `dotnet test SPOTrim.sln`
4. Build module: `pwsh ./build/Build-Module.ps1` (or `-Configuration Debug` for dev)
5. Test PowerShell: `pwsh -c "Invoke-Pester ./tests/SPOTrim.Tests.ps1"`
6. GUI changes: Refresh browser (no build step needed)
7. After lifecycle/cleanup changes: Test full load → remove → re-import cycle in a single pwsh session

### Local Module Testing
The module requires compiled DLLs in `module/lib/` before it can be imported. Without them, you'll get "Could not load file or assembly" errors.

**Quick local test cycle:**
```powershell
dotnet publish src/SPOTrim.Engine -c Debug -o module/lib
pwsh -NoProfile -c "Import-Module ./module/SPOTrim.psd1 -Force; Get-SPOTrimConfig"
```

**IMPORTANT: DLL locking**
- .NET DLLs loaded by PowerShell are locked for the lifetime of that PS process
- You CANNOT overwrite `module/lib/` DLLs while the module is loaded in any PS session
- `Remove-Module` frees PS references but does NOT release the .NET assembly lock
- You must **close the PowerShell process** that loaded the module before rebuilding
- The build script (`Build-Module.ps1`) pre-checks for locked DLLs and gives guidance

## Key Architecture Decisions
- **No external PS module dependencies**: PnP.PowerShell, Pode, ImportExcel are all replaced by compiled C# equivalents
- **SQLite over JSON files**: Enables pagination, search, comparison without loading everything into memory
- **HttpListener over Pode**: Lightweight, no PowerShell runspace isolation issues, proper async/await
- **IAsyncEnumerable streaming**: Scan results stream into SQLite in batches of 500 — no memory bloat
- **OAuth2 PKCE**: Browser-based auth with loopback redirect — no client secrets needed

## API Routes
- `GET /api/status` — Connection status, tenantId, tenantDomain, module version, scan state
- `POST /api/connect` — Start OAuth2 PKCE browser auth
- `POST /api/disconnect` — Clear tokens, sign out
- `GET /api/config` / `PUT /api/config` — Read/update configuration
- `POST /api/scan/start` — Start scan (body: `{ scanType: "Discovery"|"VersionAnalysis"|"Cleanup"|"Full" }`)
- `GET /api/scan/progress` — Current scan progress with logs
- `POST /api/scan/cancel` — Cancel running scan
- `GET /api/scans?tenantId=X` — List scans
- `GET /api/scans/:id/sites` — Sites discovered in a scan
- `GET /api/scans/:id/export?format=xlsx|csv` — Download XLSX/CSV export
- `GET /api/dashboard` — Dashboard summary statistics
- `GET /api/audit?limit=N` — Audit log entries
- `GET /api/database` — Database size and table counts

## Database Schema
9 tables: `config`, `scans`, `sites`, `libraries`, `file_versions`, `cleanup_actions`, `scan_progress`, `audit_log`, `logs`
- Sites indexed on scan_id and site_url
- Libraries indexed on scan_id and site_id
- File versions indexed on scan_id and library_id
- Cleanup actions indexed on scan_id and status
- WAL mode enabled for concurrent read/write during scans
- Schema auto-applied from embedded resource on first run

## Scan Types & Phases
- **Discovery**: Enumerate all SharePoint + OneDrive sites in the tenant
- **VersionAnalysis**: Discovery + analyze versioning settings per library
- **Cleanup**: Discovery + VersionAnalysis + trim versions (respects dry-run mode)
- **Full**: Same as Cleanup

Phases run sequentially; each phase has its own IScanProvider. The ScanOrchestrator manages lifecycle, progress tracking, and batched SQLite inserts.

## SQLite Native Library Loading
SQLitePCLRaw's native `e_sqlite3.dll` is NOT automatically discovered when .NET assemblies are loaded inside PowerShell (unlike a standard .NET host). Two things are required in `SqliteDb.cs`:

1. **`NativeLibrary.SetDllImportResolver`** on the `SQLitePCLRaw.provider.e_sqlite3` assembly to probe `runtimes/{rid}/native/` relative to the DLL location
2. **`SQLitePCL.Batteries_V2.Init()`** called after the resolver is registered

Both are guarded by a static `_nativeInitialized` flag. The resolver must be registered **before** `Batteries_V2.Init()`. The `runtimes/` directory must be copied alongside the managed DLLs in `module/lib/`.

## Resource Lifecycle & Cleanup
Proper cleanup is critical because the module runs an HTTP server and holds SQLite connections:

**Cleanup chain on module unload:**
```
Remove-Module / PowerShell.Exiting
  → Cleanup-Engine (PSM1)
    → Engine.StopServerAsync()
      → WebServer.StopAsync() — cancels listen loop, stops HttpListener, awaits task
    → Engine.Dispose()
      → ScanOrchestrator.Shutdown() — cancels scan, disposes CTS, waits for task
      → WebServer.Dispose() — closes listener, disposes CTS
      → SqliteDb.Dispose() — WAL checkpoint + ClearAllPools() (releases file handles)
```

**Key rules:**
- `SqliteDb.Dispose()` must call `SqliteConnection.ClearAllPools()` — otherwise .db/.wal/.shm handles stay locked until GC
- PSM1 registers both `OnRemove` and `PowerShell.Exiting` events for cleanup
- `Remove-Module` does NOT release .NET assembly file locks — only closing the PS process does

## Engine.cs Facade
- Constructor: `Engine(string databasePath)` — single parameter
- `StartServer(int port, string staticFilesPath, bool openBrowser)` — starts HttpListener
- `UpdateConfig(AppConfig)` overload — for PowerShell cmdlets passing full config objects
- `UpdateConfig(Dictionary<string, JsonElement>)` — for API route JSON partial updates
- `StartScanAsync` returns `Task<long>` (wraps `Task.FromResult`) — scan runs via `Task.Run` in orchestrator

## GUI Frontend JSON Property Names
C# `JsonNamingPolicy.CamelCase` produces these names. JS must match exactly:
- `connected` (not `isConnected`), `scanning` (not `scanRunning`)
- `activeScanId` (not `currentScanId`), `overallPercent` (not `percentComplete`)

## Graph API Patterns
- **Pagination**: `IAsyncEnumerable` with `@odata.nextLink` following
- **Throttling**: 429 → Retry-After header, exponential backoff (5^attempt seconds)
- **Service unavailable**: 503/504 → exponential backoff (2^attempt seconds)
- **Concurrency**: SemaphoreSlim-based throttle per client instance
- **SharePoint REST**: Separate client with SharePoint-specific access token (not Graph token)

## Common Issues & Fixes

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Unable to load DLL 'e_sqlite3'` | Native resolver not registered | Ensure `SetDllImportResolver` before `Batteries_V2.Init()` |
| `Could not load assembly 'SPOTrim.Engine'` | module/lib/ empty | Run `Build-Module.ps1` or `dotnet publish ... -o module/lib` |
| Build fails "file used by another process" | Module loaded elsewhere | Close that PS session, then rebuild |
| `yield return` in try/catch (CS1626) | C# limitation | Collect into List, yield outside try/catch |
| GUI shows no data | JS property name mismatch | Check `JsonNamingPolicy.CamelCase` output |

## Validation
- After C# changes: `dotnet build && dotnet test`
- After PS changes: `Invoke-Pester ./tests/SPOTrim.Tests.ps1`
- Before publishing: Run full build pipeline via `./build/Build-Module.ps1`
- GUI changes: Refresh browser (no build step needed)
- After lifecycle/cleanup changes: Test full load → remove → re-import cycle in a single pwsh session
