// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// SQLite
using Microsoft.Data.Sqlite;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.AppSettings;
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class SqliteProjectService : IDisposable
    {
        private SqliteConnection _connection;
        private bool _disposed;
        private readonly object _dbLock = new object();

        private const int CurrentSchemaVersion = 5;
        private static readonly string[] ReservedColumnNames = { "_row_id", "_source_id", "_is_new" };

        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

        public string FilePath { get; private set; }
        public bool IsOpen => _connection != null && !_disposed;

        // ─── Factory ───────────────────────────────────────────────────────────

        public static SqliteProjectService CreateInMemory(string name = "Test Project")
        {
            var svc = new SqliteProjectService();
            svc.FilePath = ":memory:";
            svc._connection = OpenConnection(":memory:");
            svc.CreateSchema();
            svc.SetProjectValue("version", CurrentSchemaVersion.ToString());
            svc.SetProjectValue("name", name);
            svc.SetProjectValue("created_on", DateTime.UtcNow.ToString("O"));
            svc.SetProjectValue("updated_on", DateTime.UtcNow.ToString("O"));
            return svc;
        }

        // ─── Lifecycle ─────────────────────────────────────────────────────────

        public void CreateProject(string filePath, string name)
        {
            if (File.Exists(filePath))
                throw new InvalidOperationException($"File already exists: {filePath}");

            FilePath = filePath;
            _connection = OpenConnection(filePath);
            CreateSchema();
            SetProjectValue("version", CurrentSchemaVersion.ToString());
            SetProjectValue("name", name);
            SetProjectValue("created_on", DateTime.UtcNow.ToString("O"));
            SetProjectValue("updated_on", DateTime.UtcNow.ToString("O"));
        }

        public void OpenProject(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Project file not found.", filePath);

            FilePath = filePath;
            _connection = OpenConnection(filePath);

            var versionStr = GetProjectValue("version");
            if (!int.TryParse(versionStr, out var version))
                throw new InvalidOperationException("Project file is missing a version number.");

            if (version < CurrentSchemaVersion)
            {
                var backupPath = filePath + ".bak";
                File.Copy(filePath, backupPath, overwrite: true);
                try
                {
                    RunMigrations(version);
                }
                catch
                {
                    CloseConnection();
                    if (File.Exists(filePath)) File.Delete(filePath);
                    if (File.Exists(backupPath)) File.Move(backupPath, filePath);
                    throw;
                }
            }
        }

        public void CloseProject()
        {
            CloseConnection();
            FilePath = null;
        }

        private static SqliteConnection OpenConnection(string dataSource)
        {
            SQLitePCL.Batteries_V2.Init();

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dataSource,
                Mode = dataSource == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate
            };
            var conn = new SqliteConnection(builder.ToString());
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();

            return conn;
        }

        private void CloseConnection()
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
            // Force pool flush so callers can delete the file immediately after closing.
            SqliteConnection.ClearAllPools();
        }

        // ─── Schema ────────────────────────────────────────────────────────────

        private void CreateSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS _project (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS _environments (
    id            TEXT NOT NULL,
    unique_name   TEXT NOT NULL,
    friendly_name TEXT NOT NULL,
    tag           TEXT,
    url           TEXT,
    role          TEXT NOT NULL,
    PRIMARY KEY (id, role)
);

CREATE TABLE IF NOT EXISTS _table_configs (
    logical_name      TEXT PRIMARY KEY,
    display_name      TEXT,
    primary_id_attr   TEXT,
    primary_name_attr TEXT,
    config_json       TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS _snapshots (
    id                    TEXT PRIMARY KEY,
    name                  TEXT NOT NULL UNIQUE,
    table_suffix          TEXT NOT NULL UNIQUE,
    table_logical_name    TEXT NOT NULL,
    source_env_id         TEXT NOT NULL,
    created_on            TEXT NOT NULL,
    updated_on            TEXT NOT NULL,
    row_count             INTEGER DEFAULT 0,
    source                TEXT NOT NULL,
    source_file_path      TEXT,
    pull_filter           TEXT,
    primary_id_attr       TEXT,
    load_match_key_mode   TEXT,
    load_match_key_fields TEXT,
    column_config_json    TEXT NOT NULL,
    sort_order            INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS _optionset_values (
    table_logical_name     TEXT NOT NULL,
    attribute_logical_name TEXT NOT NULL,
    value                  INTEGER NOT NULL,
    label                  TEXT,
    PRIMARY KEY (table_logical_name, attribute_logical_name, value)
);

CREATE TABLE IF NOT EXISTS _plans (
    id            TEXT PRIMARY KEY,
    name          TEXT NOT NULL,
    description   TEXT,
    created_on    TEXT NOT NULL,
    updated_on    TEXT NOT NULL,
    defaults_json TEXT
);

CREATE TABLE IF NOT EXISTS _plan_steps (
    id                  TEXT PRIMARY KEY,
    plan_id             TEXT NOT NULL REFERENCES _plans(id) ON DELETE CASCADE,
    sort_order          INTEGER NOT NULL,
    name                TEXT,
    enabled             INTEGER DEFAULT 1,
    operation           TEXT NOT NULL,
    table_logical_name  TEXT,
    source_env_id       TEXT,
    target_env_id       TEXT,
    snapshot_name       TEXT,
    file_type           TEXT,
    file_path           TEXT,
    snapshot_json       TEXT,
    failure_policy_json TEXT,
    validation_json     TEXT
);

CREATE TABLE IF NOT EXISTS _id_mappings (
    table_logical_name TEXT NOT NULL,
    source_env_id      TEXT NOT NULL,
    source_id          TEXT NOT NULL,
    target_env_id      TEXT NOT NULL,
    target_id          TEXT NOT NULL,
    mapped_on          TEXT NOT NULL,
    PRIMARY KEY (table_logical_name, source_env_id, source_id, target_env_id)
);

CREATE TABLE IF NOT EXISTS _run_logs (
    id           TEXT PRIMARY KEY,
    plan_id      TEXT,
    plan_name    TEXT,
    started_on   TEXT NOT NULL,
    completed_on TEXT,
    status       TEXT NOT NULL,
    log_json     TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS _rowcraft_edit_sessions (
    id                       TEXT PRIMARY KEY,
    snapshot_name            TEXT NOT NULL,
    table_logical_name       TEXT NOT NULL,
    base_snapshot_updated_on TEXT,
    status                   TEXT NOT NULL,
    created_on               TEXT NOT NULL,
    updated_on               TEXT NOT NULL,
    applied_on               TEXT,
    discarded_on             TEXT
);

CREATE INDEX IF NOT EXISTS idx_rowcraft_sessions_snapshot_status
    ON _rowcraft_edit_sessions(snapshot_name, status);

CREATE INDEX IF NOT EXISTS idx_rowcraft_sessions_updated
    ON _rowcraft_edit_sessions(updated_on);

CREATE TABLE IF NOT EXISTS _rowcraft_pending_changes (
    id                   TEXT PRIMARY KEY,
    session_id           TEXT NOT NULL,
    snapshot_name        TEXT NOT NULL,
    table_logical_name   TEXT NOT NULL,
    operation            TEXT NOT NULL,
    row_id               INTEGER,
    client_row_id        TEXT,
    changed_columns_json TEXT,
    before_json          TEXT,
    after_json           TEXT,
    staged_on            TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_rowcraft_pending_session_operation
    ON _rowcraft_pending_changes(session_id, operation);

CREATE INDEX IF NOT EXISTS idx_rowcraft_pending_snapshot_row
    ON _rowcraft_pending_changes(snapshot_name, row_id);

CREATE INDEX IF NOT EXISTS idx_rowcraft_pending_staged
    ON _rowcraft_pending_changes(staged_on);

CREATE TABLE IF NOT EXISTS _rowcraft_edit_log (
    id                   TEXT PRIMARY KEY,
    session_id           TEXT NOT NULL,
    snapshot_name        TEXT NOT NULL,
    table_logical_name   TEXT NOT NULL,
    row_id               INTEGER,
    client_row_id        TEXT,
    operation            TEXT NOT NULL,
    changed_columns_json TEXT NOT NULL,
    before_json          TEXT,
    after_json           TEXT,
    status               TEXT NOT NULL,
    staged_on            TEXT NOT NULL,
    applied_on           TEXT
);

CREATE INDEX IF NOT EXISTS idx_rowcraft_edit_log_snapshot_row
    ON _rowcraft_edit_log(snapshot_name, row_id);

CREATE INDEX IF NOT EXISTS idx_rowcraft_edit_log_staged
    ON _rowcraft_edit_log(staged_on);
";
            cmd.ExecuteNonQuery();
        }

        private void RunMigrations(int fromVersion)
        {
            if (fromVersion < 2)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE _snapshots ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();

                // Backfill sort_order by rowid order (preserves original insertion order)
                cmd.CommandText = @"
WITH ranked AS (
    SELECT id, ROW_NUMBER() OVER (ORDER BY rowid) AS rn FROM _snapshots
)
UPDATE _snapshots SET sort_order = (SELECT rn FROM ranked WHERE ranked.id = _snapshots.id);";
                cmd.ExecuteNonQuery();
            }

            if (fromVersion < 3)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE _snapshots ADD COLUMN source_file_path TEXT;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "ALTER TABLE _snapshots ADD COLUMN pull_filter TEXT;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "ALTER TABLE _snapshots ADD COLUMN primary_id_attr TEXT;";
                cmd.ExecuteNonQuery();
            }

            if (fromVersion < 4)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE _environments ADD COLUMN tag TEXT;";
                cmd.ExecuteNonQuery();
            }

            if (fromVersion < 5)
            {
                CreateRowcraftSchema();
            }

            SetProjectValue("version", CurrentSchemaVersion.ToString());
        }

        private void CreateRowcraftSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS _rowcraft_edit_sessions (
    id                       TEXT PRIMARY KEY,
    snapshot_name            TEXT NOT NULL,
    table_logical_name       TEXT NOT NULL,
    base_snapshot_updated_on TEXT,
    status                   TEXT NOT NULL,
    created_on               TEXT NOT NULL,
    updated_on               TEXT NOT NULL,
    applied_on               TEXT,
    discarded_on             TEXT
);
CREATE INDEX IF NOT EXISTS idx_rowcraft_sessions_snapshot_status
    ON _rowcraft_edit_sessions(snapshot_name, status);
CREATE INDEX IF NOT EXISTS idx_rowcraft_sessions_updated
    ON _rowcraft_edit_sessions(updated_on);
CREATE TABLE IF NOT EXISTS _rowcraft_pending_changes (
    id                   TEXT PRIMARY KEY,
    session_id           TEXT NOT NULL,
    snapshot_name        TEXT NOT NULL,
    table_logical_name   TEXT NOT NULL,
    operation            TEXT NOT NULL,
    row_id               INTEGER,
    client_row_id        TEXT,
    changed_columns_json TEXT,
    before_json          TEXT,
    after_json           TEXT,
    staged_on            TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_rowcraft_pending_session_operation
    ON _rowcraft_pending_changes(session_id, operation);
CREATE INDEX IF NOT EXISTS idx_rowcraft_pending_snapshot_row
    ON _rowcraft_pending_changes(snapshot_name, row_id);
CREATE INDEX IF NOT EXISTS idx_rowcraft_pending_staged
    ON _rowcraft_pending_changes(staged_on);
CREATE TABLE IF NOT EXISTS _rowcraft_edit_log (
    id                   TEXT PRIMARY KEY,
    session_id           TEXT NOT NULL,
    snapshot_name        TEXT NOT NULL,
    table_logical_name   TEXT NOT NULL,
    row_id               INTEGER,
    client_row_id        TEXT,
    operation            TEXT NOT NULL,
    changed_columns_json TEXT NOT NULL,
    before_json          TEXT,
    after_json           TEXT,
    status               TEXT NOT NULL,
    staged_on            TEXT NOT NULL,
    applied_on           TEXT
);
CREATE INDEX IF NOT EXISTS idx_rowcraft_edit_log_snapshot_row
    ON _rowcraft_edit_log(snapshot_name, row_id);
CREATE INDEX IF NOT EXISTS idx_rowcraft_edit_log_staged
    ON _rowcraft_edit_log(staged_on);
";
            cmd.ExecuteNonQuery();
        }

        // ─── Project metadata ──────────────────────────────────────────────────

        public string GetProjectValue(string key)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM _project WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", key);
                return cmd.ExecuteScalar() as string;
            }
        }

        public void SetProjectValue(string key, string value)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO _project(key, value) VALUES(@key, @value);";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }
        }

        public string ProjectName => GetProjectValue("name");

        // ─── Environments ──────────────────────────────────────────────────────

        public void SaveEnvironment(DmtProjectEnvironment env)
        {
            lock (_dbLock)
            {
                if (env.Role == "source")
                {
                    var existing = GetSourceEnvironment();
                    var changingSource = existing == null
                        || !string.Equals(existing.Id, env.Id, StringComparison.OrdinalIgnoreCase);

                    // Source identity is locked once data exists, but labels can still be edited.
                    if (HasAnySnapshot() && changingSource)
                        throw new InvalidOperationException(
                            "Cannot change the source environment after snapshots exist. Create a new project.");

                    // Delete prior source row (only one source allowed)
                    using var del = _connection.CreateCommand();
                    del.CommandText = "DELETE FROM _environments WHERE role = 'source';";
                    del.ExecuteNonQuery();
                }

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO _environments(id, unique_name, friendly_name, tag, url, role)
VALUES(@id, @unique_name, @friendly_name, @tag, @url, @role);";
                cmd.Parameters.AddWithValue("@id", env.Id);
                cmd.Parameters.AddWithValue("@unique_name", env.UniqueName);
                cmd.Parameters.AddWithValue("@friendly_name", env.FriendlyName);
                cmd.Parameters.AddWithValue("@tag", (object)env.Tag ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@url", (object)env.Url ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@role", env.Role);
                cmd.ExecuteNonQuery();
            }
        }

        public DmtProjectEnvironment GetSourceEnvironment()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id, unique_name, friendly_name, tag, url, role FROM _environments WHERE role = 'source' LIMIT 1;";
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadEnvironment(reader) : null;
            }
        }

        public List<DmtProjectEnvironment> GetEnvironments(string role = null)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                if (role != null)
                {
                    cmd.CommandText = "SELECT id, unique_name, friendly_name, tag, url, role FROM _environments WHERE role = @role;";
                    cmd.Parameters.AddWithValue("@role", role);
                }
                else
                {
                    cmd.CommandText = "SELECT id, unique_name, friendly_name, tag, url, role FROM _environments;";
                }

                var result = new List<DmtProjectEnvironment>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add(ReadEnvironment(reader));
                return result;
            }
        }

        private static DmtProjectEnvironment ReadEnvironment(SqliteDataReader r) => new DmtProjectEnvironment
        {
            Id            = r.GetString(0),
            UniqueName    = r.GetString(1),
            FriendlyName  = r.GetString(2),
            Tag           = r.IsDBNull(3) ? null : r.GetString(3),
            Url           = r.IsDBNull(4) ? null : r.GetString(4),
            Role          = r.GetString(5)
        };

        // ─── Table configs ─────────────────────────────────────────────────────

        public void SaveTableConfig(string logicalName, string displayName,
            string primaryIdAttr, string primaryNameAttr, DataTableConfig config)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO _table_configs(logical_name, display_name, primary_id_attr, primary_name_attr, config_json)
VALUES(@ln, @dn, @pid, @pn, @cfg);";
                cmd.Parameters.AddWithValue("@ln", logicalName);
                cmd.Parameters.AddWithValue("@dn", (object)displayName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pid", (object)primaryIdAttr ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pn", (object)primaryNameAttr ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cfg", JsonConvert.SerializeObject(config, _json));
                cmd.ExecuteNonQuery();
                TouchProject();
            }
        }

        public (DataTableConfig config, string displayName, string primaryIdAttr, string primaryNameAttr)
            GetTableConfig(string logicalName)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT display_name, primary_id_attr, primary_name_attr, config_json FROM _table_configs WHERE logical_name = @ln;";
                cmd.Parameters.AddWithValue("@ln", logicalName);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return (null, null, null, null);

                var displayName   = reader.IsDBNull(0) ? null : reader.GetString(0);
                var primaryIdAttr = reader.IsDBNull(1) ? null : reader.GetString(1);
                var primaryNmAttr = reader.IsDBNull(2) ? null : reader.GetString(2);
                var config        = JsonConvert.DeserializeObject<DataTableConfig>(reader.GetString(3), _json);
                return (config, displayName, primaryIdAttr, primaryNmAttr);
            }
        }

        public (DataTableConfig config, string displayName, string primaryIdAttr, string primaryNameAttr)
            EnsureTableConfigForSnapshot(DmtSnapshot snapshot)
        {
            lock (_dbLock)
            {
                if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TableLogicalName))
                    return (null, null, null, null);

                var existing = GetTableConfig(snapshot.TableLogicalName);
                if (existing.config != null)
                    return existing;

                var columns = snapshot.ColumnConfig ?? new List<DataTableColumnConfig>();
                var primaryIdAttr = !string.IsNullOrWhiteSpace(snapshot.PrimaryIdAttribute)
                    ? snapshot.PrimaryIdAttribute
                    : InferPrimaryIdAttribute(snapshot.TableLogicalName, columns);
                var primaryNameAttr = InferPrimaryNameAttribute(columns);
                var config = new DataTableConfig
                {
                    SelectedAttributes = columns
                        .Where(c => !string.IsNullOrWhiteSpace(c.LogicalName)
                            && !ReservedColumnNames.Contains(c.LogicalName, StringComparer.OrdinalIgnoreCase))
                        .Select(c => c.LogicalName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    AllColumns = columns
                        .Where(c => !string.IsNullOrWhiteSpace(c.LogicalName))
                        .Select(CloneColumnConfig)
                        .ToList(),
                    LoadMatchKeyMode = snapshot.LoadMatchKeyMode ?? "Guid",
                    LoadMatchKeyFields = snapshot.LoadMatchKeyFields != null
                        ? new List<string>(snapshot.LoadMatchKeyFields)
                        : new List<string>()
                };

                SaveTableConfig(snapshot.TableLogicalName, snapshot.TableLogicalName, primaryIdAttr, primaryNameAttr, config);
                return (config, snapshot.TableLogicalName, primaryIdAttr, primaryNameAttr);
            }
        }

        private static string InferPrimaryIdAttribute(string tableLogicalName, List<DataTableColumnConfig> columns)
        {
            var conventional = string.IsNullOrWhiteSpace(tableLogicalName) ? null : tableLogicalName + "id";
            var conventionalMatch = columns.FirstOrDefault(c =>
                string.Equals(c.LogicalName, conventional, StringComparison.OrdinalIgnoreCase));
            if (conventionalMatch != null) return conventionalMatch.LogicalName;

            return columns.FirstOrDefault(c =>
                    string.Equals(c.Type, "Uniqueidentifier", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(c.LogicalName)
                    && c.LogicalName.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                ?.LogicalName;
        }

        private static string InferPrimaryNameAttribute(List<DataTableColumnConfig> columns)
        {
            return columns.FirstOrDefault(c =>
                    string.Equals(c.LogicalName, "name", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Type, "String", StringComparison.OrdinalIgnoreCase))
                ?.LogicalName
                ?? columns.FirstOrDefault(c =>
                    string.Equals(c.Type, "String", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(c.LogicalName))
                ?.LogicalName;
        }

        private static DataTableColumnConfig CloneColumnConfig(DataTableColumnConfig col)
        {
            return new DataTableColumnConfig
            {
                LogicalName = col.LogicalName,
                DisplayName = col.DisplayName,
                Type = col.Type,
                SqliteType = col.SqliteType,
                RelatedTable = col.RelatedTable,
                Resolution = col.Resolution,
                AlternateKeyFields = col.AlternateKeyFields != null ? new List<string>(col.AlternateKeyFields) : null,
                IsMultiSelect = col.IsMultiSelect
            };
        }

        public List<string> GetTableConfigLogicalNames()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT logical_name FROM _table_configs;";
                using var reader = cmd.ExecuteReader();
                var result = new List<string>();
                while (reader.Read()) result.Add(reader.GetString(0));
                return result;
            }
        }

        private void TouchProject()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO _project(key, value) VALUES('updated_on', @value);";
            cmd.Parameters.AddWithValue("@value", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        // ─── Snapshots ─────────────────────────────────────────────────────────

        public void SaveSnapshot(DmtSnapshot snapshot)
        {
            lock (_dbLock)
            {
                SaveSnapshot(snapshot, null);
            }
        }

        private void SaveSnapshot(DmtSnapshot snapshot, SqliteTransaction tx)
        {
            if (snapshot.SortOrder <= 0)
            {
                using var maxCmd = _connection.CreateCommand();
                if (tx != null) maxCmd.Transaction = tx;
                maxCmd.CommandText = "SELECT COALESCE(MAX(sort_order), 0) + 1 FROM _snapshots;";
                snapshot.SortOrder = (int)(long)maxCmd.ExecuteScalar();
            }

            using var cmd = _connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT OR REPLACE INTO _snapshots(id, name, table_suffix, table_logical_name, source_env_id,
    created_on, updated_on, row_count, source, source_file_path, pull_filter, primary_id_attr,
    load_match_key_mode, load_match_key_fields, column_config_json, sort_order)
VALUES(@id, @name, @suffix, @tln, @seid, @co, @uo, @rc, @src, @sfp, @pf, @pid, @lmkm, @lmkf, @ccj, @so);";
            cmd.Parameters.AddWithValue("@id",   snapshot.Id);
            cmd.Parameters.AddWithValue("@name", snapshot.Name);
            cmd.Parameters.AddWithValue("@suffix", snapshot.TableSuffix);
            cmd.Parameters.AddWithValue("@tln",  snapshot.TableLogicalName);
            cmd.Parameters.AddWithValue("@seid", snapshot.SourceEnvId);
            cmd.Parameters.AddWithValue("@co",   snapshot.CreatedOn.ToString("O"));
            cmd.Parameters.AddWithValue("@uo",   snapshot.UpdatedOn.ToString("O"));
            cmd.Parameters.AddWithValue("@rc",   snapshot.RowCount);
            cmd.Parameters.AddWithValue("@src",  snapshot.Source);
            cmd.Parameters.AddWithValue("@sfp",  (object)snapshot.SourceFilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pf",   (object)snapshot.PullFilter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pid",  (object)snapshot.PrimaryIdAttribute ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lmkm", (object)snapshot.LoadMatchKeyMode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lmkf",
                snapshot.LoadMatchKeyFields?.Count > 0
                    ? JsonConvert.SerializeObject(snapshot.LoadMatchKeyFields, _json)
                    : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ccj", JsonConvert.SerializeObject(snapshot.ColumnConfig, _json));
            cmd.Parameters.AddWithValue("@so",   snapshot.SortOrder);
            cmd.ExecuteNonQuery();
        }

        public void ReplaceSnapshotData(DmtSnapshot snapshot, List<DataTableColumnConfig> columns, IEnumerable<Dictionary<string, object>> rows)
        {
            lock (_dbLock)
            {
                using var tx = _connection.BeginTransaction();
                try
                {
                    SaveSnapshot(snapshot, tx);
                    CreateSnapshotTable(snapshot.TableSuffix, columns, tx);
                    WriteSnapshotRecords(snapshot.TableSuffix, rows, columns, tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public DmtSnapshot GetSnapshot(string name)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id,name,table_suffix,table_logical_name,source_env_id,created_on,updated_on,row_count,source,source_file_path,pull_filter,primary_id_attr,load_match_key_mode,load_match_key_fields,column_config_json,sort_order FROM _snapshots WHERE name=@name;";
                cmd.Parameters.AddWithValue("@name", name);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadSnapshot(reader) : null;
            }
        }

        public List<DmtSnapshot> GetSnapshots()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id,name,table_suffix,table_logical_name,source_env_id,created_on,updated_on,row_count,source,source_file_path,pull_filter,primary_id_attr,load_match_key_mode,load_match_key_fields,column_config_json,sort_order FROM _snapshots ORDER BY sort_order;";
                var result = new List<DmtSnapshot>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) result.Add(ReadSnapshot(reader));
                return result;
            }
        }

        public bool HasSnapshot(string name)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM _snapshots WHERE name=@name LIMIT 1;";
                cmd.Parameters.AddWithValue("@name", name);
                return cmd.ExecuteScalar() != null;
            }
        }

        public bool HasAnySnapshot()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM _snapshots LIMIT 1;";
                return cmd.ExecuteScalar() != null;
            }
        }

        public ISet<string> GetSnapshotSourceIds(string tableSuffix)
        {
            lock (_dbLock)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT _source_id FROM [data_{tableSuffix}] WHERE _source_id IS NOT NULL;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    if (!reader.IsDBNull(0)) result.Add(reader.GetString(0));
                return result;
            }
        }

        public ISet<string> GetAllSnapshotSourceIdsForTable(string tableLogicalName)
        {
            lock (_dbLock)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var snapshots = GetSnapshots()
                    .Where(s => string.Equals(s.TableLogicalName, tableLogicalName, StringComparison.OrdinalIgnoreCase));
                foreach (var snap in snapshots)
                    foreach (var id in GetSnapshotSourceIds(snap.TableSuffix))
                        result.Add(id);
                return result;
            }
        }

        public void DeleteSnapshot(string name)
        {
            lock (_dbLock)
            {
                var snapshot = GetSnapshot(name)
                    ?? throw new InvalidOperationException($"Snapshot '{name}' does not exist.");

                using var tx = _connection.BeginTransaction();
                try
                {
                    DropDataTableIfExists(snapshot.TableSuffix, tx);

                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM _snapshots WHERE name=@name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.ExecuteNonQuery();

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private static DmtSnapshot ReadSnapshot(SqliteDataReader r)
        {
            var fieldsJson = r.IsDBNull(13) ? null : r.GetString(13);
            return new DmtSnapshot
            {
                Id                 = r.GetString(0),
                Name               = r.GetString(1),
                TableSuffix        = r.GetString(2),
                TableLogicalName   = r.GetString(3),
                SourceEnvId        = r.GetString(4),
                CreatedOn          = DateTime.Parse(r.GetString(5)),
                UpdatedOn          = DateTime.Parse(r.GetString(6)),
                RowCount           = r.IsDBNull(7) ? 0 : (int)r.GetInt64(7),
                Source             = r.GetString(8),
                SourceFilePath     = r.IsDBNull(9) ? null : r.GetString(9),
                PullFilter         = r.IsDBNull(10) ? null : r.GetString(10),
                PrimaryIdAttribute = r.IsDBNull(11) ? null : r.GetString(11),
                LoadMatchKeyMode   = r.IsDBNull(12) ? null : r.GetString(12),
                LoadMatchKeyFields = fieldsJson != null
                    ? JsonConvert.DeserializeObject<List<string>>(fieldsJson)
                    : new List<string>(),
                ColumnConfig       = JsonConvert.DeserializeObject<List<DataTableColumnConfig>>(r.GetString(14)),
                SortOrder          = r.IsDBNull(15) ? 0 : (int)r.GetInt64(15)
            };
        }

        // ─── Snapshot name sanitization ────────────────────────────────────────

        public static string SanitizeSnapshotName(string name)
        {
            var trimmed = (name ?? "").Trim().ToLowerInvariant();
            var underscored = trimmed.Replace(' ', '_');
            var clean = Regex.Replace(underscored, @"[^a-z0-9_]", "");
            clean = clean.Trim('_');
            return string.IsNullOrEmpty(clean) ? "snapshot" : clean;
        }

        // Returns a unique table_suffix for a new snapshot.
        // If the sanitized base is already used, appends _2, _3, etc.
        public string ResolveTableSuffix(string snapshotName)
        {
            lock (_dbLock)
            {
                var sanitized = SanitizeSnapshotName(snapshotName);
                if (!TableSuffixExists(sanitized)) return sanitized;

                for (var i = 2; i < 1000; i++)
                {
                    var candidate = $"{sanitized}_{i}";
                    if (!TableSuffixExists(candidate)) return candidate;
                }
                throw new InvalidOperationException($"Cannot find a unique table suffix for '{snapshotName}'.");
            }
        }

        private bool TableSuffixExists(string suffix)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM _snapshots WHERE table_suffix=@s LIMIT 1;";
            cmd.Parameters.AddWithValue("@s", suffix);
            return cmd.ExecuteScalar() != null;
        }

        // ─── Snapshot data tables ──────────────────────────────────────────────

        public void CreateSnapshotTable(string tableSuffix, List<DataTableColumnConfig> columns)
        {
            lock (_dbLock)
            {
                CreateSnapshotTable(tableSuffix, columns, null);
            }
        }

        private void CreateSnapshotTable(string tableSuffix, List<DataTableColumnConfig> columns, SqliteTransaction tx)
        {
            var reserved = columns.Select(c => c.LogicalName)
                .Intersect(ReservedColumnNames, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (reserved.Count > 0)
                throw new InvalidOperationException(
                    $"Column name(s) collide with reserved names: {string.Join(", ", reserved)}");

            DropDataTableIfExists(tableSuffix, tx);

            var sb = new StringBuilder();
            sb.Append($"CREATE TABLE [data_{tableSuffix}] (");
            sb.Append("_row_id INTEGER PRIMARY KEY AUTOINCREMENT, ");
            sb.Append("_source_id TEXT, ");
            sb.Append("_is_new INTEGER DEFAULT 0");

            foreach (var col in columns)
            {
                var sqliteType = col.SqliteType ?? "TEXT";
                sb.Append($", [{col.LogicalName}] {sqliteType}");
            }
            sb.Append(");");

            using var cmd = _connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = sb.ToString();
            cmd.ExecuteNonQuery();
        }

        public void WriteSnapshotRecords(string tableSuffix,
            IEnumerable<Dictionary<string, object>> rows,
            List<DataTableColumnConfig> columns)
        {
            lock (_dbLock)
            {
                using var tx = _connection.BeginTransaction();
                try
                {
                    WriteSnapshotRecords(tableSuffix, rows, columns, tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private void WriteSnapshotRecords(string tableSuffix,
            IEnumerable<Dictionary<string, object>> rows,
            List<DataTableColumnConfig> columns,
            SqliteTransaction tx)
        {
            var colNames = columns.Select(c => c.LogicalName).ToList();
            var insertCols = string.Join(", ", new[] { "_source_id", "_is_new" }
                .Concat(colNames.Select(n => $"[{n}]")));
            var insertParams = string.Join(", ", new[] { "@_source_id", "@_is_new" }
                .Concat(colNames.Select(n => $"@{n}")));

            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO [data_{tableSuffix}]({insertCols}) VALUES({insertParams});";

            // Pre-create parameters
            cmd.Parameters.Add("@_source_id", SqliteType.Text);
            cmd.Parameters.Add("@_is_new", SqliteType.Integer);
            foreach (var col in columns)
            {
                var paramType = col.SqliteType == "INTEGER" ? SqliteType.Integer
                    : col.SqliteType == "REAL" ? SqliteType.Real
                    : SqliteType.Text;
                cmd.Parameters.Add($"@{col.LogicalName}", paramType);
            }

            foreach (var row in rows)
            {
                row.TryGetValue("_source_id", out var srcId);
                row.TryGetValue("_is_new", out var isNew);

                cmd.Parameters["@_source_id"].Value = srcId != null ? (object)srcId.ToString() : DBNull.Value;
                cmd.Parameters["@_is_new"].Value = (isNew is true || isNew is 1L || isNew is 1) ? 1L : 0L;

                foreach (var col in columns)
                {
                    row.TryGetValue(col.LogicalName, out var val);
                    cmd.Parameters[$"@{col.LogicalName}"].Value = BindValue(val, col.SqliteType);
                }

                cmd.ExecuteNonQuery();
            }

            // Update snapshot row_count
            using var countCmd = _connection.CreateCommand();
            countCmd.Transaction = tx;
            countCmd.CommandText = $"SELECT COUNT(*) FROM [data_{tableSuffix}];";
            var count = (long)countCmd.ExecuteScalar();

            using var updateCmd = _connection.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE _snapshots SET row_count=@rc, updated_on=@uo WHERE table_suffix=@s;";
            updateCmd.Parameters.AddWithValue("@rc", count);
            updateCmd.Parameters.AddWithValue("@uo", DateTime.UtcNow.ToString("O"));
            updateCmd.Parameters.AddWithValue("@s", tableSuffix);
            updateCmd.ExecuteNonQuery();
        }

        public IEnumerable<Dictionary<string, object>> ReadSnapshotRecords(
            string tableSuffix,
            List<DataTableColumnConfig> columns,
            int offset = 0,
            int limit = -1)  // -1 = no limit (SQLite LIMIT -1 = all rows)
        {
            lock (_dbLock)
            {
                var colSelect = string.Join(", ", columns.Select(c => $"[{c.LogicalName}]"));
                var sql = $"SELECT _row_id, _source_id, _is_new{(colSelect.Length > 0 ? ", " + colSelect : "")} " +
                          $"FROM [data_{tableSuffix}] LIMIT @limit OFFSET @offset;";

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@limit", limit < 0 ? -1 : limit);
                cmd.Parameters.AddWithValue("@offset", offset);

                var result = new List<Dictionary<string, object>>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>
                    {
                        ["_row_id"]    = reader.GetInt64(0),
                        ["_source_id"] = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ["_is_new"]    = reader.GetInt64(2) == 1L
                    };

                    for (var i = 0; i < columns.Count; i++)
                    {
                        var col = columns[i];
                        var idx = i + 3;
                        if (reader.IsDBNull(idx))
                        {
                            row[col.LogicalName] = null;
                        }
                        else if (col.SqliteType == "INTEGER")
                        {
                            row[col.LogicalName] = reader.GetInt64(idx);
                        }
                        else if (col.SqliteType == "REAL")
                        {
                            row[col.LogicalName] = reader.GetDouble(idx);
                        }
                        else
                        {
                            row[col.LogicalName] = reader.GetString(idx);
                        }
                    }
                    result.Add(row);
                }
                return result;
            }
        }

        public Dictionary<string, object> ReadSnapshotRecordBySourceId(string tableSuffix, string sourceId)
        {
            lock (_dbLock)
            {
                using var check = _connection.CreateCommand();
                check.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='data_{tableSuffix}';";
                if (check.ExecuteScalar() == null) return null;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT * FROM [data_{tableSuffix}] WHERE _source_id=@id LIMIT 1;";
                cmd.Parameters.AddWithValue("@id", sourceId);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;

                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                return row;
            }
        }

        public int CountSnapshotRows(string tableSuffix)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM [data_{tableSuffix}];";
                return (int)(long)cmd.ExecuteScalar();
            }
        }

        public RowcraftEditSession StartRowcraftEditSession(string snapshotName)
        {
            lock (_dbLock)
            {
                var snapshot = GetSnapshot(snapshotName)
                    ?? throw new InvalidOperationException($"Snapshot '{snapshotName}' does not exist.");

                var existing = GetPendingRowcraftEditSession(snapshotName);
                if (existing != null) return existing;

                var now = DateTime.UtcNow;
                var session = new RowcraftEditSession
                {
                    Id = Guid.NewGuid().ToString("D"),
                    SnapshotName = snapshot.Name,
                    TableLogicalName = snapshot.TableLogicalName,
                    BaseSnapshotUpdatedOn = snapshot.UpdatedOn,
                    Status = "Pending",
                    CreatedOn = now,
                    UpdatedOn = now
                };

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO _rowcraft_edit_sessions(id,snapshot_name,table_logical_name,base_snapshot_updated_on,status,created_on,updated_on)
VALUES(@id,@sn,@t,@base,@st,@co,@uo);";
                cmd.Parameters.AddWithValue("@id", session.Id);
                cmd.Parameters.AddWithValue("@sn", session.SnapshotName);
                cmd.Parameters.AddWithValue("@t", session.TableLogicalName);
                cmd.Parameters.AddWithValue("@base", session.BaseSnapshotUpdatedOn.HasValue ? (object)session.BaseSnapshotUpdatedOn.Value.ToString("O") : DBNull.Value);
                cmd.Parameters.AddWithValue("@st", session.Status);
                cmd.Parameters.AddWithValue("@co", session.CreatedOn.ToString("O"));
                cmd.Parameters.AddWithValue("@uo", session.UpdatedOn.ToString("O"));
                cmd.ExecuteNonQuery();
                return session;
            }
        }

        public RowcraftEditSession GetRowcraftEditSession(string sessionId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id,snapshot_name,table_logical_name,base_snapshot_updated_on,status,created_on,updated_on,applied_on,discarded_on FROM _rowcraft_edit_sessions WHERE id=@id;";
                cmd.Parameters.AddWithValue("@id", sessionId);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRowcraftEditSession(reader) : null;
            }
        }

        public RowcraftEditSession GetPendingRowcraftEditSession(string snapshotName)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
SELECT id,snapshot_name,table_logical_name,base_snapshot_updated_on,status,created_on,updated_on,applied_on,discarded_on
FROM _rowcraft_edit_sessions
WHERE snapshot_name=@sn AND status='Pending'
ORDER BY updated_on DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@sn", snapshotName);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRowcraftEditSession(reader) : null;
            }
        }

        public List<RowcraftChangeSummary> GetPendingRowcraftChangeSummaries()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
SELECT s.id,s.snapshot_name,s.status,
       SUM(CASE WHEN c.operation='Create' THEN 1 ELSE 0 END) AS creates,
       SUM(CASE WHEN c.operation='Update' THEN 1 ELSE 0 END) AS updates,
       SUM(CASE WHEN c.operation='Delete' THEN 1 ELSE 0 END) AS deletes
FROM _rowcraft_edit_sessions s
LEFT JOIN _rowcraft_pending_changes c ON c.session_id=s.id
WHERE s.status='Pending'
GROUP BY s.id,s.snapshot_name,s.status
ORDER BY s.updated_on DESC;";
                var result = new List<RowcraftChangeSummary>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add(ReadRowcraftChangeSummary(reader));
                return result;
            }
        }

        public RowcraftChangeSummary GetRowcraftChangeSummary(string sessionId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
SELECT s.id,s.snapshot_name,s.status,
       SUM(CASE WHEN c.operation='Create' THEN 1 ELSE 0 END) AS creates,
       SUM(CASE WHEN c.operation='Update' THEN 1 ELSE 0 END) AS updates,
       SUM(CASE WHEN c.operation='Delete' THEN 1 ELSE 0 END) AS deletes
FROM _rowcraft_edit_sessions s
LEFT JOIN _rowcraft_pending_changes c ON c.session_id=s.id
WHERE s.id=@id
GROUP BY s.id,s.snapshot_name,s.status;";
                cmd.Parameters.AddWithValue("@id", sessionId);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadRowcraftChangeSummary(reader) : null;
            }
        }

        public RowcraftPendingChange StageRowcraftCreate(string sessionId, Dictionary<string, object> values, string clientRowId = null)
        {
            lock (_dbLock)
            {
                var session = RequirePendingRowcraftSession(sessionId);
                var snapshot = GetSnapshot(session.SnapshotName)
                    ?? throw new InvalidOperationException($"Snapshot '{session.SnapshotName}' does not exist.");
                var after = ValidateRowcraftValues(snapshot, values);
                var change = new RowcraftPendingChange
                {
                    SessionId = session.Id,
                    SnapshotName = session.SnapshotName,
                    TableLogicalName = session.TableLogicalName,
                    Operation = "Create",
                    ClientRowId = string.IsNullOrWhiteSpace(clientRowId) ? Guid.NewGuid().ToString("D") : clientRowId,
                    ChangedColumns = new Dictionary<string, object>(after, StringComparer.OrdinalIgnoreCase),
                    After = after
                };
                SaveRowcraftPendingChange(change);
                TouchRowcraftSession(session.Id);
                return change;
            }
        }

        public RowcraftPendingChange StageRowcraftUpdate(string sessionId, long rowId, Dictionary<string, object> values)
        {
            lock (_dbLock)
            {
                var session = RequirePendingRowcraftSession(sessionId);
                var snapshot = GetSnapshot(session.SnapshotName)
                    ?? throw new InvalidOperationException($"Snapshot '{session.SnapshotName}' does not exist.");
                var before = ReadSnapshotRecordByRowId(snapshot.TableSuffix, rowId)
                    ?? throw new InvalidOperationException($"Snapshot row '{rowId}' does not exist.");
                var changed = ValidateRowcraftValues(snapshot, values);
                var after = new Dictionary<string, object>(before, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in changed) after[pair.Key] = pair.Value;

                var change = new RowcraftPendingChange
                {
                    SessionId = session.Id,
                    SnapshotName = session.SnapshotName,
                    TableLogicalName = session.TableLogicalName,
                    Operation = "Update",
                    RowId = rowId,
                    ChangedColumns = changed,
                    Before = before,
                    After = after
                };
                SaveRowcraftPendingChange(change);
                TouchRowcraftSession(session.Id);
                return change;
            }
        }

        public RowcraftPendingChange StageRowcraftDelete(string sessionId, long rowId)
        {
            lock (_dbLock)
            {
                var session = RequirePendingRowcraftSession(sessionId);
                var snapshot = GetSnapshot(session.SnapshotName)
                    ?? throw new InvalidOperationException($"Snapshot '{session.SnapshotName}' does not exist.");
                var before = ReadSnapshotRecordByRowId(snapshot.TableSuffix, rowId)
                    ?? throw new InvalidOperationException($"Snapshot row '{rowId}' does not exist.");
                var change = new RowcraftPendingChange
                {
                    SessionId = session.Id,
                    SnapshotName = session.SnapshotName,
                    TableLogicalName = session.TableLogicalName,
                    Operation = "Delete",
                    RowId = rowId,
                    Before = before,
                    ChangedColumns = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                };
                SaveRowcraftPendingChange(change);
                TouchRowcraftSession(session.Id);
                return change;
            }
        }

        public void DiscardRowcraftEditSession(string sessionId)
        {
            lock (_dbLock)
            {
                var session = RequirePendingRowcraftSession(sessionId);
                var now = DateTime.UtcNow;
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "UPDATE _rowcraft_edit_sessions SET status='Discarded', updated_on=@uo, discarded_on=@do WHERE id=@id;";
                cmd.Parameters.AddWithValue("@uo", now.ToString("O"));
                cmd.Parameters.AddWithValue("@do", now.ToString("O"));
                cmd.Parameters.AddWithValue("@id", session.Id);
                cmd.ExecuteNonQuery();
            }
        }

        public RowcraftApplyResult ApplyRowcraftEditSession(string sessionId)
        {
            lock (_dbLock)
            {
                var session = RequirePendingRowcraftSession(sessionId);
                var snapshot = GetSnapshot(session.SnapshotName)
                    ?? throw new InvalidOperationException($"Snapshot '{session.SnapshotName}' does not exist.");
                var changes = GetRowcraftPendingChanges(session.Id);

                using var tx = _connection.BeginTransaction();
                try
                {
                    var result = new RowcraftApplyResult { SessionId = session.Id, SnapshotName = session.SnapshotName };
                    foreach (var change in changes)
                    {
                        if (string.Equals(change.Operation, "Create", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyRowcraftCreate(snapshot, change, tx);
                            result.Created++;
                        }
                        else if (string.Equals(change.Operation, "Update", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyRowcraftUpdate(snapshot, change, tx);
                            result.Updated++;
                        }
                        else if (string.Equals(change.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyRowcraftDelete(snapshot, change, tx);
                            result.Deleted++;
                        }
                    }

                    result.RowCount = UpdateSnapshotRowCount(snapshot.TableSuffix, tx);
                    var now = DateTime.UtcNow;
                    using (var sess = _connection.CreateCommand())
                    {
                        sess.Transaction = tx;
                        sess.CommandText = "UPDATE _rowcraft_edit_sessions SET status='Applied', updated_on=@uo, applied_on=@ao WHERE id=@id;";
                        sess.Parameters.AddWithValue("@uo", now.ToString("O"));
                        sess.Parameters.AddWithValue("@ao", now.ToString("O"));
                        sess.Parameters.AddWithValue("@id", session.Id);
                        sess.ExecuteNonQuery();
                    }
                    using (var log = _connection.CreateCommand())
                    {
                        log.Transaction = tx;
                        log.CommandText = "UPDATE _rowcraft_edit_log SET status='Applied', applied_on=@ao WHERE session_id=@sid AND status='Staged';";
                        log.Parameters.AddWithValue("@ao", now.ToString("O"));
                        log.Parameters.AddWithValue("@sid", session.Id);
                        log.ExecuteNonQuery();
                    }

                    tx.Commit();
                    return result;
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private Dictionary<string, object> ReadSnapshotRecordByRowId(string tableSuffix, long rowId)
        {
            using var check = _connection.CreateCommand();
            check.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='data_{tableSuffix}';";
            if (check.ExecuteScalar() == null) return null;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM [data_{tableSuffix}] WHERE _row_id=@id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", rowId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            return row;
        }

        private RowcraftEditSession RequirePendingRowcraftSession(string sessionId)
        {
            var session = GetRowcraftEditSession(sessionId)
                ?? throw new InvalidOperationException($"Rowcraft edit session '{sessionId}' does not exist.");
            if (!string.Equals(session.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Rowcraft edit session '{sessionId}' is {session.Status}.");
            return session;
        }

        private Dictionary<string, object> ValidateRowcraftValues(DmtSnapshot snapshot, Dictionary<string, object> values)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var columns = (snapshot.ColumnConfig ?? new List<DataTableColumnConfig>())
                .ToDictionary(c => c.LogicalName, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values ?? new Dictionary<string, object>())
            {
                if (ReservedColumnNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Column '{pair.Key}' is managed by DMT and cannot be edited from Rowcraft.");
                if (!columns.TryGetValue(pair.Key, out var col))
                    throw new InvalidOperationException($"Column '{pair.Key}' is not part of snapshot '{snapshot.Name}'.");
                result[col.LogicalName] = CoerceRowcraftValue(pair.Value, col.SqliteType);
            }
            return result;
        }

        private static object CoerceRowcraftValue(object value, string sqliteType)
        {
            if (value == null) return null;
            if (value is Newtonsoft.Json.Linq.JValue jv) value = jv.Value;
            if (value == null) return null;
            if (sqliteType == "INTEGER")
            {
                if (value is bool b) return b ? 1L : 0L;
                if (value is long l) return l;
                if (value is int i) return (long)i;
                if (long.TryParse(value.ToString(), out var parsed)) return parsed;
                throw new InvalidOperationException($"Value '{value}' is not a valid integer.");
            }
            if (sqliteType == "REAL")
            {
                if (value is double d) return d;
                if (value is float f) return (double)f;
                if (value is decimal m) return (double)m;
                if (double.TryParse(value.ToString(), out var parsed)) return parsed;
                throw new InvalidOperationException($"Value '{value}' is not a valid number.");
            }
            return value.ToString();
        }

        private void SaveRowcraftPendingChange(RowcraftPendingChange change)
        {
            change.Id = string.IsNullOrWhiteSpace(change.Id) ? Guid.NewGuid().ToString("D") : change.Id;
            change.StagedOn = DateTime.UtcNow;
            var changedJson = JsonConvert.SerializeObject(change.ChangedColumns ?? new Dictionary<string, object>(), _json);
            var beforeJson = change.Before != null ? JsonConvert.SerializeObject(change.Before, _json) : null;
            var afterJson = change.After != null ? JsonConvert.SerializeObject(change.After, _json) : null;

            using var tx = _connection.BeginTransaction();
            try
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO _rowcraft_pending_changes(id,session_id,snapshot_name,table_logical_name,operation,row_id,client_row_id,changed_columns_json,before_json,after_json,staged_on)
VALUES(@id,@sid,@sn,@t,@op,@rid,@crid,@cc,@bj,@aj,@so);";
                    BindRowcraftChangeParameters(cmd, change, changedJson, beforeJson, afterJson);
                    cmd.ExecuteNonQuery();
                }
                using (var log = _connection.CreateCommand())
                {
                    log.Transaction = tx;
                    log.CommandText = @"
INSERT INTO _rowcraft_edit_log(id,session_id,snapshot_name,table_logical_name,row_id,client_row_id,operation,changed_columns_json,before_json,after_json,status,staged_on)
VALUES(@id,@sid,@sn,@t,@rid,@crid,@op,@cc,@bj,@aj,'Staged',@so);";
                    BindRowcraftChangeParameters(log, change, changedJson, beforeJson, afterJson);
                    log.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static void BindRowcraftChangeParameters(SqliteCommand cmd, RowcraftPendingChange change, string changedJson, string beforeJson, string afterJson)
        {
            cmd.Parameters.AddWithValue("@id", change.Id);
            cmd.Parameters.AddWithValue("@sid", change.SessionId);
            cmd.Parameters.AddWithValue("@sn", change.SnapshotName);
            cmd.Parameters.AddWithValue("@t", change.TableLogicalName);
            cmd.Parameters.AddWithValue("@op", change.Operation);
            cmd.Parameters.AddWithValue("@rid", change.RowId.HasValue ? (object)change.RowId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@crid", (object)change.ClientRowId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cc", changedJson);
            cmd.Parameters.AddWithValue("@bj", (object)beforeJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@aj", (object)afterJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@so", change.StagedOn.ToString("O"));
        }

        private void TouchRowcraftSession(string sessionId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE _rowcraft_edit_sessions SET updated_on=@uo WHERE id=@id;";
            cmd.Parameters.AddWithValue("@uo", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@id", sessionId);
            cmd.ExecuteNonQuery();
        }

        private List<RowcraftPendingChange> GetRowcraftPendingChanges(string sessionId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT id,session_id,snapshot_name,table_logical_name,operation,row_id,client_row_id,changed_columns_json,before_json,after_json,staged_on
FROM _rowcraft_pending_changes WHERE session_id=@sid ORDER BY staged_on,id;";
            cmd.Parameters.AddWithValue("@sid", sessionId);
            var result = new List<RowcraftPendingChange>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) result.Add(ReadRowcraftPendingChange(reader));
            return result;
        }

        private void ApplyRowcraftCreate(DmtSnapshot snapshot, RowcraftPendingChange change, SqliteTransaction tx)
        {
            var columns = snapshot.ColumnConfig ?? new List<DataTableColumnConfig>();
            var colNames = columns.Select(c => c.LogicalName).ToList();
            var insertCols = string.Join(", ", new[] { "_source_id", "_is_new" }.Concat(colNames.Select(n => $"[{n}]")));
            var insertParams = string.Join(", ", new[] { "@_source_id", "@_is_new" }.Concat(colNames.Select(n => $"@{n}")));
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO [data_{snapshot.TableSuffix}]({insertCols}) VALUES({insertParams});";
            cmd.Parameters.AddWithValue("@_source_id", "rowcraft:" + (change.ClientRowId ?? Guid.NewGuid().ToString("D")));
            cmd.Parameters.AddWithValue("@_is_new", 1L);
            foreach (var col in columns)
            {
                object val = null;
                change.After?.TryGetValue(col.LogicalName, out val);
                cmd.Parameters.AddWithValue($"@{col.LogicalName}", BindValue(val, col.SqliteType));
            }
            cmd.ExecuteNonQuery();
        }

        private void ApplyRowcraftUpdate(DmtSnapshot snapshot, RowcraftPendingChange change, SqliteTransaction tx)
        {
            if (!change.RowId.HasValue) throw new InvalidOperationException("Update change is missing row ID.");
            var columns = (snapshot.ColumnConfig ?? new List<DataTableColumnConfig>())
                .ToDictionary(c => c.LogicalName, StringComparer.OrdinalIgnoreCase);
            var changed = change.ChangedColumns ?? new Dictionary<string, object>();
            if (!changed.Any()) return;
            var sets = string.Join(", ", changed.Keys.Select(k => $"[{columns[k].LogicalName}]=@{columns[k].LogicalName}"));
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE [data_{snapshot.TableSuffix}] SET {sets} WHERE _row_id=@rowid;";
            foreach (var pair in changed)
            {
                var col = columns[pair.Key];
                cmd.Parameters.AddWithValue($"@{col.LogicalName}", BindValue(pair.Value, col.SqliteType));
            }
            cmd.Parameters.AddWithValue("@rowid", change.RowId.Value);
            if (cmd.ExecuteNonQuery() == 0)
                throw new InvalidOperationException($"Snapshot row '{change.RowId.Value}' no longer exists.");
        }

        private void ApplyRowcraftDelete(DmtSnapshot snapshot, RowcraftPendingChange change, SqliteTransaction tx)
        {
            if (!change.RowId.HasValue) throw new InvalidOperationException("Delete change is missing row ID.");
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM [data_{snapshot.TableSuffix}] WHERE _row_id=@rowid;";
            cmd.Parameters.AddWithValue("@rowid", change.RowId.Value);
            cmd.ExecuteNonQuery();
        }

        private int UpdateSnapshotRowCount(string tableSuffix, SqliteTransaction tx)
        {
            using var countCmd = _connection.CreateCommand();
            countCmd.Transaction = tx;
            countCmd.CommandText = $"SELECT COUNT(*) FROM [data_{tableSuffix}];";
            var count = (long)countCmd.ExecuteScalar();

            using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE _snapshots SET row_count=@rc, updated_on=@uo WHERE table_suffix=@s;";
            update.Parameters.AddWithValue("@rc", count);
            update.Parameters.AddWithValue("@uo", DateTime.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("@s", tableSuffix);
            update.ExecuteNonQuery();
            return (int)count;
        }

        private static RowcraftEditSession ReadRowcraftEditSession(SqliteDataReader r)
        {
            string baseUpdated = r.IsDBNull(3) ? null : r.GetString(3);
            string applied = r.IsDBNull(7) ? null : r.GetString(7);
            string discarded = r.IsDBNull(8) ? null : r.GetString(8);
            return new RowcraftEditSession
            {
                Id = r.GetString(0),
                SnapshotName = r.GetString(1),
                TableLogicalName = r.GetString(2),
                BaseSnapshotUpdatedOn = baseUpdated != null ? DateTime.Parse(baseUpdated) : (DateTime?)null,
                Status = r.GetString(4),
                CreatedOn = DateTime.Parse(r.GetString(5)),
                UpdatedOn = DateTime.Parse(r.GetString(6)),
                AppliedOn = applied != null ? DateTime.Parse(applied) : (DateTime?)null,
                DiscardedOn = discarded != null ? DateTime.Parse(discarded) : (DateTime?)null
            };
        }

        private static RowcraftChangeSummary ReadRowcraftChangeSummary(SqliteDataReader r)
        {
            return new RowcraftChangeSummary
            {
                SessionId = r.GetString(0),
                SnapshotName = r.GetString(1),
                Status = r.GetString(2),
                Creates = r.IsDBNull(3) ? 0 : (int)r.GetInt64(3),
                Updates = r.IsDBNull(4) ? 0 : (int)r.GetInt64(4),
                Deletes = r.IsDBNull(5) ? 0 : (int)r.GetInt64(5)
            };
        }

        private static RowcraftPendingChange ReadRowcraftPendingChange(SqliteDataReader r)
        {
            return new RowcraftPendingChange
            {
                Id = r.GetString(0),
                SessionId = r.GetString(1),
                SnapshotName = r.GetString(2),
                TableLogicalName = r.GetString(3),
                Operation = r.GetString(4),
                RowId = r.IsDBNull(5) ? (long?)null : r.GetInt64(5),
                ClientRowId = r.IsDBNull(6) ? null : r.GetString(6),
                ChangedColumns = r.IsDBNull(7)
                    ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    : JsonConvert.DeserializeObject<Dictionary<string, object>>(r.GetString(7), _json),
                Before = r.IsDBNull(8) ? null : JsonConvert.DeserializeObject<Dictionary<string, object>>(r.GetString(8), _json),
                After = r.IsDBNull(9) ? null : JsonConvert.DeserializeObject<Dictionary<string, object>>(r.GetString(9), _json),
                StagedOn = DateTime.Parse(r.GetString(10))
            };
        }

        private void DropDataTableIfExists(string tableSuffix, SqliteTransaction tx = null)
        {
            using var cmd = _connection.CreateCommand();
            if (tx != null) cmd.Transaction = tx;
            cmd.CommandText = $"DROP TABLE IF EXISTS [data_{tableSuffix}];";
            cmd.ExecuteNonQuery();
        }

        private static object BindValue(object value, string sqliteType)
        {
            if (value is Newtonsoft.Json.Linq.JValue jv) value = jv.Value;
            if (value == null) return DBNull.Value;
            if (sqliteType == "INTEGER")
            {
                if (value is bool b) return b ? 1L : 0L;
                return Convert.ToInt64(value);
            }
            if (sqliteType == "REAL") return Convert.ToDouble(value);
            return value.ToString();
        }

        // ─── OptionSet values ──────────────────────────────────────────────────

        public void SaveOptionSetValues(string tableLogicalName, string attrLogicalName,
            IEnumerable<OptionConfig> options)
        {
            lock (_dbLock)
            {
                using var tx = _connection.BeginTransaction();
                try
                {
                    using var del = _connection.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM _optionset_values WHERE table_logical_name=@t AND attribute_logical_name=@a;";
                    del.Parameters.AddWithValue("@t", tableLogicalName);
                    del.Parameters.AddWithValue("@a", attrLogicalName);
                    del.ExecuteNonQuery();

                    using var ins = _connection.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO _optionset_values(table_logical_name,attribute_logical_name,value,label) VALUES(@t,@a,@v,@l);";
                    ins.Parameters.Add("@t", SqliteType.Text).Value = tableLogicalName;
                    ins.Parameters.Add("@a", SqliteType.Text).Value = attrLogicalName;
                    ins.Parameters.Add("@v", SqliteType.Integer);
                    ins.Parameters.Add("@l", SqliteType.Text);

                    foreach (var opt in options ?? Enumerable.Empty<OptionConfig>())
                    {
                        ins.Parameters["@v"].Value = (long)opt.Value;
                        ins.Parameters["@l"].Value = (object)opt.Label ?? DBNull.Value;
                        ins.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public Dictionary<int, string> GetOptionSetValues(string tableLogicalName, string attrLogicalName)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT value, label FROM _optionset_values WHERE table_logical_name=@t AND attribute_logical_name=@a;";
                cmd.Parameters.AddWithValue("@t", tableLogicalName);
                cmd.Parameters.AddWithValue("@a", attrLogicalName);

                var result = new Dictionary<int, string>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    result[(int)reader.GetInt64(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
                return result;
            }
        }

        public int? ResolveOptionSetLabel(string tableLogicalName, string attrLogicalName, string label)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM _optionset_values WHERE table_logical_name=@t AND attribute_logical_name=@a AND label=@l LIMIT 1;";
                cmd.Parameters.AddWithValue("@t", tableLogicalName);
                cmd.Parameters.AddWithValue("@a", attrLogicalName);
                cmd.Parameters.AddWithValue("@l", label);
                var scalar = cmd.ExecuteScalar();
                return scalar == null ? (int?)null : (int)(long)scalar;
            }
        }

        // ─── Plans ─────────────────────────────────────────────────────────────

        public void SavePlan(DmtPlan plan)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO _plans(id,name,description,created_on,updated_on,defaults_json)
VALUES(@id,@name,@desc,@co,@uo,@dj);";
                cmd.Parameters.AddWithValue("@id",   plan.Id);
                cmd.Parameters.AddWithValue("@name", plan.Name);
                cmd.Parameters.AddWithValue("@desc", (object)plan.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@co",   plan.CreatedOn.ToString("O"));
                cmd.Parameters.AddWithValue("@uo",   plan.UpdatedOn.ToString("O"));
                cmd.Parameters.AddWithValue("@dj",   plan.Defaults != null
                    ? JsonConvert.SerializeObject(plan.Defaults, _json)
                    : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public void ReplacePlan(DmtPlan plan, IEnumerable<DmtPlanStep> steps)
        {
            lock (_dbLock)
            {
                using var tx = _connection.BeginTransaction();
                try
                {
                    SavePlan(plan, tx);

                    using (var delete = _connection.CreateCommand())
                    {
                        delete.Transaction = tx;
                        delete.CommandText = "DELETE FROM _plan_steps WHERE plan_id=@pid;";
                        delete.Parameters.AddWithValue("@pid", plan.Id);
                        delete.ExecuteNonQuery();
                    }

                    foreach (var step in steps ?? Enumerable.Empty<DmtPlanStep>())
                        SavePlanStep(step, tx);

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private void SavePlan(DmtPlan plan, SqliteTransaction tx)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT OR REPLACE INTO _plans(id,name,description,created_on,updated_on,defaults_json)
VALUES(@id,@name,@desc,@co,@uo,@dj);";
            cmd.Parameters.AddWithValue("@id",   plan.Id);
            cmd.Parameters.AddWithValue("@name", plan.Name);
            cmd.Parameters.AddWithValue("@desc", (object)plan.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@co",   plan.CreatedOn.ToString("O"));
            cmd.Parameters.AddWithValue("@uo",   plan.UpdatedOn.ToString("O"));
            cmd.Parameters.AddWithValue("@dj",   plan.Defaults != null
                ? JsonConvert.SerializeObject(plan.Defaults, _json)
                : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public List<DmtPlan> GetPlans()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id,name,description,created_on,updated_on,defaults_json FROM _plans ORDER BY name;";
                var result = new List<DmtPlan>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var defaultsJson = reader.IsDBNull(5) ? null : reader.GetString(5);
                    result.Add(new DmtPlan
                    {
                        Id          = reader.GetString(0),
                        Name        = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatedOn   = DateTime.Parse(reader.GetString(3)),
                        UpdatedOn   = DateTime.Parse(reader.GetString(4)),
                        Defaults    = defaultsJson != null
                            ? JsonConvert.DeserializeObject<ExecutionPlanDefaults>(defaultsJson, _json)
                            : new ExecutionPlanDefaults()
                    });
                }
                return result;
            }
        }

        public void DeletePlan(string id)
        {
            lock (_dbLock)
            {
                // Steps deleted via ON DELETE CASCADE
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM _plans WHERE id=@id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        // ─── Plan steps ────────────────────────────────────────────────────────

        public void SavePlanStep(DmtPlanStep step)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO _plan_steps(id,plan_id,sort_order,name,enabled,operation,
    table_logical_name,source_env_id,target_env_id,snapshot_name,file_type,file_path,
    snapshot_json,failure_policy_json,validation_json)
VALUES(@id,@pid,@so,@name,@en,@op,@tln,@seid,@teid,@sn,@ft,@fp,@sj,@fpj,@vj);";
                cmd.Parameters.AddWithValue("@id",   step.Id);
                cmd.Parameters.AddWithValue("@pid",  step.PlanId);
                cmd.Parameters.AddWithValue("@so",   step.SortOrder);
                cmd.Parameters.AddWithValue("@name", (object)step.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@en",   step.Enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@op",   step.Operation);
                cmd.Parameters.AddWithValue("@tln",  (object)step.TableLogicalName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@seid", (object)step.SourceEnvId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@teid", (object)step.TargetEnvId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sn",   (object)step.SnapshotName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ft",   (object)step.FileType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fp",   (object)step.FilePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sj",   step.Snapshot != null
                    ? JsonConvert.SerializeObject(step.Snapshot, _json) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@fpj",  step.FailurePolicy != null
                    ? JsonConvert.SerializeObject(step.FailurePolicy, _json) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@vj",   step.Validation != null
                    ? JsonConvert.SerializeObject(step.Validation, _json) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private void SavePlanStep(DmtPlanStep step, SqliteTransaction tx)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT OR REPLACE INTO _plan_steps(id,plan_id,sort_order,name,enabled,operation,
    table_logical_name,source_env_id,target_env_id,snapshot_name,file_type,file_path,
    snapshot_json,failure_policy_json,validation_json)
VALUES(@id,@pid,@so,@name,@en,@op,@tln,@seid,@teid,@sn,@ft,@fp,@sj,@fpj,@vj);";
            cmd.Parameters.AddWithValue("@id",   step.Id);
            cmd.Parameters.AddWithValue("@pid",  step.PlanId);
            cmd.Parameters.AddWithValue("@so",   step.SortOrder);
            cmd.Parameters.AddWithValue("@name", (object)step.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@en",   step.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@op",   step.Operation);
            cmd.Parameters.AddWithValue("@tln",  (object)step.TableLogicalName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@seid", (object)step.SourceEnvId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@teid", (object)step.TargetEnvId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sn",   (object)step.SnapshotName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ft",   (object)step.FileType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fp",   (object)step.FilePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sj",   step.Snapshot != null
                ? JsonConvert.SerializeObject(step.Snapshot, _json) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@fpj",  step.FailurePolicy != null
                ? JsonConvert.SerializeObject(step.FailurePolicy, _json) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@vj",   step.Validation != null
                ? JsonConvert.SerializeObject(step.Validation, _json) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public List<DmtPlanStep> GetPlanSteps(string planId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id,plan_id,sort_order,name,enabled,operation,table_logical_name,source_env_id,target_env_id,snapshot_name,file_type,file_path,snapshot_json,failure_policy_json,validation_json FROM _plan_steps WHERE plan_id=@pid ORDER BY sort_order;";
                cmd.Parameters.AddWithValue("@pid", planId);

                var result = new List<DmtPlanStep>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var snapshotJson      = reader.IsDBNull(12) ? null : reader.GetString(12);
                    var failurePolicyJson = reader.IsDBNull(13) ? null : reader.GetString(13);
                    var validationJson    = reader.IsDBNull(14) ? null : reader.GetString(14);
                    result.Add(new DmtPlanStep
                    {
                        Id               = reader.GetString(0),
                        PlanId           = reader.GetString(1),
                        SortOrder        = (int)reader.GetInt64(2),
                        Name             = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Enabled          = reader.GetInt64(4) == 1L,
                        Operation        = reader.GetString(5),
                        TableLogicalName = reader.IsDBNull(6) ? null : reader.GetString(6),
                        SourceEnvId      = reader.IsDBNull(7) ? null : reader.GetString(7),
                        TargetEnvId      = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SnapshotName     = reader.IsDBNull(9) ? null : reader.GetString(9),
                        FileType         = reader.IsDBNull(10) ? null : reader.GetString(10),
                        FilePath         = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Snapshot         = snapshotJson != null
                            ? JsonConvert.DeserializeObject<DmtPlanStepSnapshot>(snapshotJson, _json)
                            : new DmtPlanStepSnapshot(),
                        FailurePolicy    = failurePolicyJson != null
                            ? JsonConvert.DeserializeObject<ExecutionPlanFailurePolicy>(failurePolicyJson, _json)
                            : new ExecutionPlanFailurePolicy(),
                        Validation       = validationJson != null
                            ? JsonConvert.DeserializeObject<ExecutionPlanValidation>(validationJson, _json)
                            : new ExecutionPlanValidation()
                    });
                }
                return result;
            }
        }

        public void DeletePlanStep(string id)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM _plan_steps WHERE id=@id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public int MarkPlanPreviewsStaleForSnapshot(string snapshotName)
        {
            lock (_dbLock)
            {
                var changed = 0;
                foreach (var plan in GetPlans())
                {
                    foreach (var step in GetPlanSteps(plan.Id))
                    {
                        if (!string.Equals(step.SnapshotName, snapshotName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (step.Validation?.Preview == null || step.Validation.Preview.IsStale)
                            continue;

                        step.Validation.Preview.IsStale = true;
                        SavePlanStep(step);
                        changed++;
                    }
                }
                return changed;
            }
        }

        // ─── ID mappings ───────────────────────────────────────────────────────

        public void SaveIdMapping(string table, string sourceEnvId, string sourceId,
            string targetEnvId, string targetId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO _id_mappings(table_logical_name,source_env_id,source_id,target_env_id,target_id,mapped_on)
VALUES(@t,@se,@si,@te,@ti,@mo);";
                cmd.Parameters.AddWithValue("@t",  table);
                cmd.Parameters.AddWithValue("@se", sourceEnvId);
                cmd.Parameters.AddWithValue("@si", sourceId);
                cmd.Parameters.AddWithValue("@te", targetEnvId);
                cmd.Parameters.AddWithValue("@ti", targetId);
                cmd.Parameters.AddWithValue("@mo", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }

        public void RemoveIdMapping(string table, string sourceEnvId, string sourceId, string targetEnvId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM _id_mappings WHERE table_logical_name=@t AND source_env_id=@se AND source_id=@si AND target_env_id=@te;";
                cmd.Parameters.AddWithValue("@t",  table);
                cmd.Parameters.AddWithValue("@se", sourceEnvId);
                cmd.Parameters.AddWithValue("@si", sourceId);
                cmd.Parameters.AddWithValue("@te", targetEnvId);
                cmd.ExecuteNonQuery();
            }
        }

        public string ResolveTargetId(string table, string sourceEnvId, string sourceId, string targetEnvId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT target_id FROM _id_mappings WHERE table_logical_name=@t AND source_env_id=@se AND source_id=@si AND target_env_id=@te LIMIT 1;";
                cmd.Parameters.AddWithValue("@t",  table);
                cmd.Parameters.AddWithValue("@se", sourceEnvId);
                cmd.Parameters.AddWithValue("@si", sourceId);
                cmd.Parameters.AddWithValue("@te", targetEnvId);
                return cmd.ExecuteScalar() as string;
            }
        }

        public Dictionary<string, string> GetAllIdMappings(string table, string sourceEnvId, string targetEnvId)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT source_id, target_id FROM _id_mappings WHERE table_logical_name=@t AND source_env_id=@se AND target_env_id=@te;";
                cmd.Parameters.AddWithValue("@t",  table);
                cmd.Parameters.AddWithValue("@se", sourceEnvId);
                cmd.Parameters.AddWithValue("@te", targetEnvId);

                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
                return result;
            }
        }

        // ─── Run logs ──────────────────────────────────────────────────────────

        public void SaveRunLog(DmtRunLog log)
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
INSERT OR REPLACE INTO _run_logs(id,plan_id,plan_name,started_on,completed_on,status,log_json)
VALUES(@id,@pid,@pn,@so,@co,@st,@lj);";
                cmd.Parameters.AddWithValue("@id",  log.Id);
                cmd.Parameters.AddWithValue("@pid", (object)log.PlanId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pn",  (object)log.PlanName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@so",  log.StartedOn.ToString("O"));
                cmd.Parameters.AddWithValue("@co",  log.CompletedOn.HasValue
                    ? (object)log.CompletedOn.Value.ToString("O") : DBNull.Value);
                cmd.Parameters.AddWithValue("@st",  log.Status);
                cmd.Parameters.AddWithValue("@lj",  log.Log != null
                    ? JsonConvert.SerializeObject(log.Log, _json) : "{}");
                cmd.ExecuteNonQuery();
            }
        }

        public List<DmtRunLog> GetRunLogs()
        {
            lock (_dbLock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT id,plan_id,plan_name,started_on,completed_on,status,log_json FROM _run_logs ORDER BY started_on DESC;";
                var result = new List<DmtRunLog>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var completedStr = reader.IsDBNull(4) ? (string)null : reader.GetString(4);
                    result.Add(new DmtRunLog
                    {
                        Id          = reader.GetString(0),
                        PlanId      = reader.IsDBNull(1) ? null : reader.GetString(1),
                        PlanName    = reader.IsDBNull(2) ? null : reader.GetString(2),
                        StartedOn   = DateTime.Parse(reader.GetString(3)),
                        CompletedOn = completedStr != null ? DateTime.Parse(completedStr) : (DateTime?)null,
                        Status      = reader.GetString(5),
                        Log         = JsonConvert.DeserializeObject<ExecutionPlanRunLog>(reader.GetString(6), _json)
                    });
                }
                return result;
            }
        }

        // ─── SQLite type mapping ───────────────────────────────────────────────

        // Maps a Dataverse AttributeTypeCode name to a SQLite column type.
        public static string GetSqliteType(string attributeTypeCode)
        {
            switch (attributeTypeCode)
            {
                case "Integer":
                case "BigInt":
                case "Boolean":
                case "OptionSet":
                case "Picklist":
                case "State":
                case "Status":
                    return "INTEGER";
                case "Double":
                case "Decimal":
                case "Money":
                    return "REAL";
                default:
                    return "TEXT";
            }
        }

        // Returns true for attribute types that should be excluded from AllColumns.
        public static bool IsExcludedAttributeType(string attributeTypeCode)
        {
            switch (attributeTypeCode)
            {
                case "Virtual":
                case "ManagedProperty":
                case "CalcRollup":
                case "Rollup":
                case "Image":
                case "File":
                    return true;
                default:
                    return false;
            }
        }

        // ─── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CloseConnection();
        }
    }
}
