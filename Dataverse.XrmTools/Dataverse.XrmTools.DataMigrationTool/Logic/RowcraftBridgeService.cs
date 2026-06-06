// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Newtonsoft
using Newtonsoft.Json;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Models;

namespace Dataverse.XrmTools.DataMigrationTool.Logic
{
    public class RowcraftBridgeService : IDisposable
    {
        private readonly Func<SqliteProjectService> _projectProvider;
        private readonly JsonSerializerSettings _json = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include };
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private string _pendingToken;
        private DateTime _pendingTokenExpiresOn;
        private string _sessionId;
        private DateTime _sessionExpiresOn;
        private bool _disposed;

        public RowcraftBridgeService(Func<SqliteProjectService> projectProvider)
        {
            _projectProvider = projectProvider ?? throw new ArgumentNullException(nameof(projectProvider));
        }

        public bool IsRunning => _listener != null && _listener.IsListening;
        public int Port { get; private set; }
        public string BaseUrl => IsRunning ? $"http://127.0.0.1:{Port}/rowcraft/" : null;

        public string StartForRowcraft()
        {
            if (_projectProvider() == null)
                throw new InvalidOperationException("Open a DMT project before starting the Rowcraft bridge.");

            // Always restart so stale in-flight requests from a previous Rowcraft tab
            // are dropped before the new session begins.
            Stop();
            StartListener();

            _pendingToken = GenerateSecret(32);
            _pendingTokenExpiresOn = DateTime.UtcNow.AddMinutes(2);
            return _pendingToken;
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _cts = null;
            _pendingToken = null;
            _sessionId = null;
        }

        private void StartListener()
        {
            Port = FindFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/rowcraft/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext context = null;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleContext(context), token);
                }
                catch
                {
                    if (!token.IsCancellationRequested)
                        SafeError(context, 500, "Bridge request failed.");
                }
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            try
            {
                AddCorsHeaders(context.Response);
                if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address))
                {
                    WriteJson(context, 403, new { error = "Forbidden" });
                    return;
                }

                var path = context.Request.Url.AbsolutePath.Trim('/');
                if (path.StartsWith("rowcraft/", StringComparison.OrdinalIgnoreCase))
                    path = path.Substring(9);
                var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.UnescapeDataString)
                    .ToArray();

                if (parts.Length == 2 && parts[0] == "api" && parts[1] == "v1")
                {
                    WriteJson(context, 200, new { ok = true, mode = "rowcraft-connector" });
                    return;
                }

                if (parts.SequenceEqual(new[] { "api", "v1", "health" }))
                {
                    WriteJson(context, 200, new { ok = true, mode = "rowcraft-connector", contextOpen = _projectProvider() != null });
                    return;
                }

                if (parts.SequenceEqual(new[] { "api", "v1", "session", "exchange" })
                    && string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    ExchangeSession(context);
                    return;
                }

                if (parts.SequenceEqual(new[] { "api", "v1", "session" })
                    && string.Equals(context.Request.HttpMethod, "DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(context))
                    {
                        WriteJson(context, 401, new { error = "Unauthorised" });
                        return;
                    }
                    _sessionId = null;
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                if (!IsAuthorized(context))
                {
                    WriteJson(context, 401, new { error = "Unauthorised" });
                    return;
                }

                RouteAuthorized(context, parts);
            }
            catch (Exception ex)
            {
                WriteJson(context, 500, new { error = ex.Message });
            }
        }

        private void RouteAuthorized(HttpListenerContext context, string[] parts)
        {
            var project = _projectProvider() ?? throw new InvalidOperationException("No DMT project is open.");

            if (parts.SequenceEqual(new[] { "api", "v1", "context" }))
            {
                WriteJson(context, 200, new
                {
                    name = project.ProjectName,
                    fileName = string.IsNullOrWhiteSpace(project.FilePath) ? null : Path.GetFileName(project.FilePath),
                    schemaVersion = project.GetProjectValue("version"),
                    capabilities = BuildCapabilities()
                });
                return;
            }

            if (parts.SequenceEqual(new[] { "api", "v1", "datasets" }))
            {
                var datasets = project.GetSnapshots().Select(s => new
                {
                    name = s.Name,
                    label = s.Name,
                    tableLogicalName = s.TableLogicalName,
                    source = s.Source,
                    rowCount = s.RowCount,
                    updatedOn = s.UpdatedOn,
                    sortOrder = s.SortOrder,
                    columnCount = s.ColumnConfig?.Count ?? 0
                });
                WriteJson(context, 200, datasets);
                return;
            }

            if (parts.Length >= 5 && parts[0] == "api" && parts[1] == "v1" && parts[2] == "datasets")
            {
                RouteDataset(context, project, parts);
                return;
            }

            WriteJson(context, 404, new { error = "Not found" });
        }

        private void RouteDataset(HttpListenerContext context, SqliteProjectService project, string[] parts)
        {
            var datasetName = parts[3];
            var snapshot = project.GetSnapshot(datasetName)
                ?? throw new InvalidOperationException($"Dataset '{datasetName}' does not exist.");

            if (parts.Length == 5 && parts[4] == "columns" && context.Request.HttpMethod == "GET")
            {
                WriteJson(context, 200, ToConnectorColumns(snapshot.ColumnConfig));
                return;
            }

            if (parts.Length == 5 && parts[4] == "records" && context.Request.HttpMethod == "GET")
            {
                var offset = ParseInt(context.Request.QueryString["offset"], 0);
                var limit = Math.Min(ParseInt(context.Request.QueryString["limit"], 100), 500);
                var rows = project.ReadSnapshotRecords(snapshot.TableSuffix, snapshot.ColumnConfig, offset, limit);
                WriteJson(context, 200, new { offset, limit, rows, total = snapshot.RowCount });
                return;
            }

            if (parts.Length == 5 && parts[4] == "edit-session" && context.Request.HttpMethod == "POST")
            {
                var session = project.StartRowcraftEditSession(snapshot.Name);
                var summary = project.GetRowcraftChangeSummary(session.Id);
                WriteJson(context, 200, new { session = ToConnectorSession(session), summary = ToConnectorSummary(summary) });
                return;
            }

            if (parts.Length >= 6 && parts[4] == "edit-session")
            {
                RouteEditSession(context, project, parts);
                return;
            }

            WriteJson(context, 404, new { error = "Not found" });
        }

        private void RouteEditSession(HttpListenerContext context, SqliteProjectService project, string[] parts)
        {
            var sessionId = parts[5];
            if (parts.Length == 7 && parts[6] == "changes" && context.Request.HttpMethod == "GET")
            {
                WriteJson(context, 200, ToConnectorSummary(project.GetRowcraftChangeSummary(sessionId)));
                return;
            }

            if (parts.Length == 7 && parts[6] == "discard" && context.Request.HttpMethod == "POST")
            {
                project.DiscardRowcraftEditSession(sessionId);
                WriteJson(context, 200, new { discarded = true });
                return;
            }

            if (parts.Length == 7 && parts[6] == "records" && context.Request.HttpMethod == "POST")
            {
                var body = ReadBody<Dictionary<string, object>>(context);
                var clientRowId = ExtractClientRowId(body);
                var change = project.StageRowcraftCreate(sessionId, body, clientRowId);
                WriteJson(context, 200, ToConnectorChange(change));
                return;
            }

            if (parts.Length == 8 && parts[6] == "records" && context.Request.HttpMethod == "PATCH")
            {
                var rowId = long.Parse(parts[7]);
                var body = ReadBody<Dictionary<string, object>>(context);
                var change = project.StageRowcraftUpdate(sessionId, rowId, body);
                WriteJson(context, 200, ToConnectorChange(change));
                return;
            }

            if (parts.Length == 8 && parts[6] == "records" && context.Request.HttpMethod == "DELETE")
            {
                var rowId = long.Parse(parts[7]);
                var change = project.StageRowcraftDelete(sessionId, rowId);
                WriteJson(context, 200, ToConnectorChange(change));
                return;
            }

            WriteJson(context, 404, new { error = "Not found" });
        }

        private void ExchangeSession(HttpListenerContext context)
        {
            var body = ReadBody<Dictionary<string, string>>(context);
            body.TryGetValue("token", out var token);
            if (string.IsNullOrWhiteSpace(_pendingToken)
                || DateTime.UtcNow > _pendingTokenExpiresOn
                || !string.Equals(token, _pendingToken, StringComparison.Ordinal))
            {
                WriteJson(context, 401, new { error = "Invalid or expired token" });
                return;
            }

            _sessionId = GenerateSecret(32);
            _sessionExpiresOn = DateTime.UtcNow.AddMinutes(30);
            _pendingToken = null;
            WriteJson(context, 200, new
            {
                sessionId = _sessionId,
                mode = "rowcraft-connector",
                apiVersion = "1",
                capabilities = BuildCapabilities()
            });
        }

        private object BuildCapabilities()
        {
            return new
            {
                readDatasets = true,
                queryRows = false,
                stageCreates = true,
                stageUpdates = true,
                stageDeletes = true,
                applyStagedChanges = false,
                executeSqlReadOnly = false
            };
        }

        private static List<object> ToConnectorColumns(List<DataTableColumnConfig> columns)
        {
            return (columns ?? new List<DataTableColumnConfig>())
                .Select(c => new
                {
                    name = c.LogicalName,
                    label = string.IsNullOrWhiteSpace(c.DisplayName) ? c.LogicalName : c.DisplayName,
                    type = c.Type,
                    sqliteType = c.SqliteType,
                    relatedDataset = c.RelatedTable,
                    isMultiSelect = c.IsMultiSelect
                })
                .Cast<object>()
                .ToList();
        }

        private static object ToConnectorSession(RowcraftEditSession session)
        {
            if (session == null) return null;
            return new
            {
                id = session.Id,
                datasetName = session.SnapshotName,
                status = session.Status?.ToLowerInvariant()
            };
        }

        private static object ToConnectorSummary(RowcraftChangeSummary summary)
        {
            if (summary == null) return null;
            return new
            {
                sessionId = summary.SessionId,
                datasetName = summary.SnapshotName,
                status = summary.Status?.ToLowerInvariant(),
                creates = summary.Creates,
                updates = summary.Updates,
                deletes = summary.Deletes,
                total = summary.Total
            };
        }

        private static object ToConnectorChange(RowcraftPendingChange change)
        {
            if (change == null) return null;
            return new
            {
                id = change.Id,
                sessionId = change.SessionId,
                datasetName = change.SnapshotName,
                operation = change.Operation?.ToLowerInvariant(),
                rowId = change.RowId,
                clientRowId = change.ClientRowId,
                after = change.After,
                changedColumns = change.ChangedColumns?.Keys.ToList() ?? new List<string>(),
                stagedOn = change.StagedOn
            };
        }

        private static string ExtractClientRowId(Dictionary<string, object> body)
        {
            if (body == null) return null;
            if (!body.TryGetValue("clientRowId", out var value)) return null;
            body.Remove("clientRowId");
            if (value is Newtonsoft.Json.Linq.JValue jv) value = jv.Value;
            return value?.ToString();
        }

        private bool IsAuthorized(HttpListenerContext context)
        {
            if (string.IsNullOrWhiteSpace(_sessionId) || DateTime.UtcNow > _sessionExpiresOn) return false;
            var header = context.Request.Headers["Authorization"] ?? string.Empty;
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            var token = header.Substring(prefix.Length).Trim();
            var ok = string.Equals(token, _sessionId, StringComparison.Ordinal);
            if (ok) _sessionExpiresOn = DateTime.UtcNow.AddMinutes(30);
            return ok;
        }

        private T ReadBody<T>(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var json = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json)) return Activator.CreateInstance<T>();
            return JsonConvert.DeserializeObject<T>(json, _json);
        }

        private void WriteJson(HttpListenerContext context, int status, object value)
        {
            var json = JsonConvert.SerializeObject(value, _json);
            var bytes = Encoding.UTF8.GetBytes(json);
            AddCorsHeaders(context.Response);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        private static void SafeError(HttpListenerContext context, int status, string message)
        {
            if (context == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = message }));
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch { }
        }

        private static void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PATCH, DELETE, OPTIONS";
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;
        }

        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string GenerateSecret(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(data);
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
