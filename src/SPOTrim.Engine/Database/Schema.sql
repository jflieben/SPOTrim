-- SPOTrim SQLite Schema
-- Applied automatically on first run via SqliteDb.Initialize()

CREATE TABLE IF NOT EXISTS config (
    key         TEXT PRIMARY KEY NOT NULL,
    value       TEXT NOT NULL,
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Scan jobs: each scan operation covers one or more sites
CREATE TABLE IF NOT EXISTS scans (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    tenant_id           TEXT NOT NULL DEFAULT '',
    tenant_domain       TEXT NOT NULL DEFAULT '',
    status              TEXT NOT NULL DEFAULT 'Pending',     -- Pending, Running, Completed, Failed, Cancelled
    scan_type           TEXT NOT NULL DEFAULT 'Discovery',   -- Discovery, VersionAnalysis, Cleanup, Full
    started_at          TEXT NOT NULL DEFAULT '',
    completed_at        TEXT NOT NULL DEFAULT '',
    started_by          TEXT NOT NULL DEFAULT '',
    total_sites         INTEGER NOT NULL DEFAULT 0,
    total_libraries     INTEGER NOT NULL DEFAULT 0,
    config_snapshot     TEXT NOT NULL DEFAULT '{}',
    error_message       TEXT NOT NULL DEFAULT '',
    module_version      TEXT NOT NULL DEFAULT '',
    notes               TEXT NOT NULL DEFAULT ''
);

-- Sites discovered in the tenant
CREATE TABLE IF NOT EXISTS sites (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    scan_id             INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
    site_id             TEXT NOT NULL DEFAULT '',
    site_url            TEXT NOT NULL DEFAULT '',
    site_title          TEXT NOT NULL DEFAULT '',
    site_type           TEXT NOT NULL DEFAULT '',             -- TeamSite, CommunicationSite, OneDrive, Other
    owner               TEXT NOT NULL DEFAULT '',
    storage_used_bytes  INTEGER NOT NULL DEFAULT 0,
    storage_quota_bytes INTEGER NOT NULL DEFAULT 0,
    last_activity_date  TEXT NOT NULL DEFAULT '',
    discovered_at       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_sites_scan_id ON sites(scan_id);
CREATE INDEX IF NOT EXISTS idx_sites_url ON sites(scan_id, site_url);

-- Document libraries within sites
CREATE TABLE IF NOT EXISTS libraries (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    scan_id             INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
    site_id             INTEGER NOT NULL REFERENCES sites(id) ON DELETE CASCADE,
    library_id          TEXT NOT NULL DEFAULT '',
    library_title       TEXT NOT NULL DEFAULT '',
    library_url         TEXT NOT NULL DEFAULT '',
    item_count          INTEGER NOT NULL DEFAULT 0,
    version_count       INTEGER NOT NULL DEFAULT 0,
    versioning_enabled  INTEGER NOT NULL DEFAULT 1,          -- 0=disabled, 1=enabled
    major_version_limit INTEGER NOT NULL DEFAULT 500,
    minor_version_limit INTEGER NOT NULL DEFAULT 0,
    storage_used_bytes  INTEGER NOT NULL DEFAULT 0,
    version_storage_bytes INTEGER NOT NULL DEFAULT 0,
    discovered_at       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_libraries_scan_id ON libraries(scan_id);
CREATE INDEX IF NOT EXISTS idx_libraries_site_id ON libraries(site_id);

-- Individual file version details (populated during VersionAnalysis)
CREATE TABLE IF NOT EXISTS file_versions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    scan_id             INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
    library_id          INTEGER NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
    file_name           TEXT NOT NULL DEFAULT '',
    file_path           TEXT NOT NULL DEFAULT '',
    file_size_bytes     INTEGER NOT NULL DEFAULT 0,
    version_count       INTEGER NOT NULL DEFAULT 0,
    versions_size_bytes INTEGER NOT NULL DEFAULT 0,
    current_version     TEXT NOT NULL DEFAULT '',
    oldest_version_date TEXT NOT NULL DEFAULT '',
    newest_version_date TEXT NOT NULL DEFAULT '',
    discovered_at       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_file_versions_scan_id ON file_versions(scan_id);
CREATE INDEX IF NOT EXISTS idx_file_versions_library_id ON file_versions(library_id);

-- Cleanup actions: tracks what was trimmed/deleted
CREATE TABLE IF NOT EXISTS cleanup_actions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    scan_id             INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
    library_id          INTEGER NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
    action_type         TEXT NOT NULL DEFAULT '',             -- VersionTrim, VersionDelete, FileDelete, VersioningConfig
    target_path         TEXT NOT NULL DEFAULT '',
    target_name         TEXT NOT NULL DEFAULT '',
    detail              TEXT NOT NULL DEFAULT '',             -- JSON: e.g. {"versionsRemoved": 42, "bytesFreed": 12345}
    status              TEXT NOT NULL DEFAULT 'Pending',      -- Pending, InProgress, Completed, Failed, Skipped
    error_message       TEXT NOT NULL DEFAULT '',
    executed_at         TEXT NOT NULL DEFAULT '',
    bytes_freed         INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_cleanup_scan_id ON cleanup_actions(scan_id);
CREATE INDEX IF NOT EXISTS idx_cleanup_status ON cleanup_actions(scan_id, status);

-- Progress tracking per scan category
CREATE TABLE IF NOT EXISTS scan_progress (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    scan_id             INTEGER NOT NULL REFERENCES scans(id) ON DELETE CASCADE,
    category            TEXT NOT NULL DEFAULT '',             -- Discovery, VersionAnalysis, Cleanup
    total_targets       INTEGER NOT NULL DEFAULT 0,
    completed_targets   INTEGER NOT NULL DEFAULT 0,
    failed_targets      INTEGER NOT NULL DEFAULT 0,
    items_found         INTEGER NOT NULL DEFAULT 0,
    current_target      TEXT NOT NULL DEFAULT '',
    status              TEXT NOT NULL DEFAULT 'Pending',
    started_at          TEXT NOT NULL DEFAULT '',
    UNIQUE(scan_id, category)
);

-- Audit log for all operations
CREATE TABLE IF NOT EXISTS audit_log (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    action              TEXT NOT NULL DEFAULT '',
    user_name           TEXT NOT NULL DEFAULT '',
    details             TEXT NOT NULL DEFAULT '',
    scan_id             INTEGER,
    timestamp           TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp);
CREATE INDEX IF NOT EXISTS idx_audit_action ON audit_log(action);

-- Logs for scan operations
CREATE TABLE IF NOT EXISTS logs (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    scan_id             INTEGER,
    level               INTEGER NOT NULL DEFAULT 3,          -- 0=Critical, 1=Error, 2=Warning, 3=Info, 4=Verbose, 5=Debug
    message             TEXT NOT NULL DEFAULT '',
    category            TEXT NOT NULL DEFAULT '',
    timestamp           TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_logs_scan_id ON logs(scan_id);
CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON logs(timestamp);
