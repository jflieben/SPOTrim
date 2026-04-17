using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace SPOTrim.Engine.Database;

/// <summary>
/// Manages SQLite connection lifecycle and schema migration.
/// Thread-safe: uses a single connection string; SQLite handles WAL-mode concurrency.
/// </summary>
public sealed class SqliteDb : IDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private bool _initialized;
    private static bool _nativeInitialized;

    public string DatabasePath => _databasePath;

    public SqliteDb(string databasePath)
    {
        if (!_nativeInitialized)
        {
            _nativeInitialized = true;
            var providerAssembly = typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly;
            NativeLibrary.SetDllImportResolver(providerAssembly, ResolveNativeLibrary);
            SQLitePCL.Batteries_V2.Init();
        }

        _databasePath = databasePath;

        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public void Initialize()
    {
        if (_initialized) return;

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "SPOTrim.Engine.Database.Schema.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        ApplyMigrations(conn);
        _initialized = true;
    }

    private static void ApplyMigrations(SqliteConnection conn)
    {
        // Future migrations go here, guarded by AddColumnIfMissing / CREATE TABLE IF NOT EXISTS
    }

    internal static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
    {
        using var infoCmd = conn.CreateCommand();
        infoCmd.CommandText = $"PRAGMA table_info({table})";
        bool exists = false;
        using (var reader = infoCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                { exists = true; break; }
            }
        }
        if (!exists)
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            alterCmd.ExecuteNonQuery();
        }
    }

    public DatabaseInfo GetDatabaseInfo()
    {
        var info = new DatabaseInfo { Path = _databasePath };

        if (File.Exists(_databasePath))
            info.SizeBytes = new FileInfo(_databasePath).Length;

        var walPath = _databasePath + "-wal";
        if (File.Exists(walPath))
            info.SizeBytes += new FileInfo(walPath).Length;

        using var conn = CreateConnection();
        foreach (var table in new[] { "scans", "sites", "libraries", "file_versions", "cleanup_actions", "scan_progress", "audit_log", "logs", "config" })
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                var count = Convert.ToInt64(cmd.ExecuteScalar());
                info.TableCounts[table] = count;
            }
            catch { /* table may not exist */ }
        }

        return info;
    }

    public void ResetDatabase()
    {
        using (var conn = CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM cleanup_actions;
                DELETE FROM file_versions;
                DELETE FROM libraries;
                DELETE FROM sites;
                DELETE FROM scan_progress;
                DELETE FROM logs;
                DELETE FROM audit_log;
                DELETE FROM scans;
            ";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using (var conn = CreateConnection())
        {
            using var vacCmd = conn.CreateCommand();
            vacCmd.CommandText = "VACUUM;";
            vacCmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        try
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { /* best effort */ }

        SqliteConnection.ClearAllPools();
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "e_sqlite3")
            return IntPtr.Zero;

        var libDir = Path.GetDirectoryName(typeof(SqliteDb).Assembly.Location) ?? ".";

        var os = OperatingSystem.IsWindows() ? "win"
               : OperatingSystem.IsLinux() ? "linux"
               : OperatingSystem.IsMacOS() ? "osx"
               : null;

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => null
        };

        if (os != null && arch != null)
        {
            var runtimePath = Path.Combine(libDir, "runtimes", $"{os}-{arch}", "native", libraryName);
            if (NativeLibrary.TryLoad(runtimePath, out var handle))
                return handle;
        }

        if (NativeLibrary.TryLoad(Path.Combine(libDir, libraryName), out var fallback))
            return fallback;

        return IntPtr.Zero;
    }
}

public sealed class DatabaseInfo
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public Dictionary<string, long> TableCounts { get; set; } = new();
}
