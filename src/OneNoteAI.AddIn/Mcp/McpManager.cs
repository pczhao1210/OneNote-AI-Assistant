using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.AI.Models;
using OneNoteAI.Knowledge;
using OneNoteAI.Logging;
using OneNoteAI.Settings;

namespace OneNoteAI.Mcp
{
    public sealed class RemoteTool
    {
        public string ServerId { get; set; }
        public string ServerName { get; set; }
        public string Identity { get; set; }
        public string OriginalName { get; set; }
        public ChatTool Definition { get; set; }
        public string SchemaHash => LocalState.Hash(Definition.Function.Parameters.ToString(Formatting.None));
        public string Alias => Definition.Function.Name;
        public override string ToString() => ServerName + " / " + OriginalName;
    }

    public sealed class ToolApproval
    {
        public string Server { get; set; }
        public string Tool { get; set; }
        public string CallId { get; set; }
        public string SchemaHash { get; set; }
        public string Arguments { get; set; }
    }

    public sealed class ToolExecution
    {
        public string ServerId { get; set; }
        public string ServerName { get; set; }
        public string ServerIdentity { get; set; }
        public string ToolName { get; set; }
        public string CallId { get; set; }
        public string Status { get; set; }
        public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
        public JObject Result { get; set; }
        public bool OutcomeUnknown => Status == "unknown";
    }

    public sealed class McpManager : IAsyncDisposable
    {
        private sealed class Connection
        {
            public string Identity;
            public McpClient Client;
            public HttpClientTransport Transport;
            public bool Invalid;
        }
        private readonly Func<IReadOnlyList<McpServerOptions>> _settings;
        private readonly Func<McpServerOptions, HttpMessageHandler> _httpHandler;
        private readonly Dictionary<string, Connection> _connections = new Dictionary<string, Connection>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _dispatched = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _knownTools = new Dictionary<string, string>(StringComparer.Ordinal);

        public McpManager(Func<IReadOnlyList<McpServerOptions>> settings,
            Func<McpServerOptions, HttpMessageHandler> httpHandler = null)
        { _settings = settings; _httpHandler = httpHandler; }

        private McpServerOptions Current(string id)
        {
            McpServerOptions server = _settings().FirstOrDefault(s => s.Id == id && s.Enabled);
            if (server == null) throw new InvalidOperationException("MCP connection is disabled or removed.");
            server.Validate();
            return server;
        }

        public static string EffectiveIdentity(McpServerOptions server) =>
            LocalState.Hash(server.Identity + "\n" + McpTokenCache.Revision(server.Identity));

        private async Task<Connection> ConnectAsync(string id, CancellationToken token)
        {
            McpServerOptions server = Current(id);
            if (_connections.TryGetValue(id, out Connection existing))
            {
                if (existing.Identity == EffectiveIdentity(server) && !existing.Invalid && !existing.Client.Completion.IsCompleted) return existing;
                await CloseAsync(id).ConfigureAwait(false);
            }
            var connection = new Connection { Identity = EffectiveIdentity(server) };
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string secret = EncryptionHelper.Decrypt(server.Secret);
                if (server.Authentication == McpAuthentication.Bearer) headers["Authorization"] = "Bearer " + secret;
                if (server.Authentication == McpAuthentication.ApiKeyHeader) headers[server.HeaderName] = secret;
                var options = new HttpClientTransportOptions { Name = server.Name, Endpoint = EmbeddingOptions.RemoteEndpoint(server.Endpoint),
                    TransportMode = HttpTransportMode.StreamableHttp, AdditionalHeaders = headers,
                    ConnectionTimeout = TimeSpan.FromSeconds(30), MaxReconnectionAttempts = 0,
                    OAuth = server.Authentication == McpAuthentication.OAuth ? McpOAuth.Options(server) : null };
                var http = new HttpClient(new HttpsOnlyHandler(_httpHandler?.Invoke(server) ??
                    new HttpClientHandler { AllowAutoRedirect = false, MaxConnectionsPerServer = 8 }))
                { Timeout = Timeout.InfiniteTimeSpan };
                connection.Transport = new HttpClientTransport(options, http, ownsHttpClient: true);
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromMinutes(3));
                        connection.Client = await McpClient.CreateAsync(connection.Transport, new McpClientOptions
                        { ClientInfo = new Implementation { Name = "OneNoteAI", Version = "1.0.0" } }, cancellationToken: timeout.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    await connection.Transport.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            _connections[id] = connection;
            return connection;
        }

        public async Task<List<RemoteTool>> DiscoverAsync(IEnumerable<string> serverIds, Action<string> activity, CancellationToken token)
        {
            var result = new List<RemoteTool>();
            foreach (string id in serverIds.Distinct(StringComparer.Ordinal))
            {
                Connection connection = await ConnectAsync(id, token).ConfigureAwait(false);
                McpServerOptions server = Current(id);
                if (connection.Client.ServerCapabilities.Tools == null) continue;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(server.TimeoutSeconds));
                    var tools = await connection.Client.ListToolsAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
                    foreach (McpClientTool tool in tools)
                    {
                        var remote = Describe(server, tool);
                        string key = id + "\n" + tool.Name;
                        if (!_knownTools.TryGetValue(key, out string hash))
                            activity?.Invoke(remote + (server.Policy(tool.Name).RequireApproval ? ": Require approval" : ": Auto approve (default includes writes)"));
                        else if (hash != remote.SchemaHash) activity?.Invoke(remote + ": schema changed");
                        _knownTools[key] = remote.SchemaHash;
                        if (server.Policy(tool.Name).Enabled) result.Add(remote);
                    }
                }
            }
            if (result.Select(t => t.Alias).Distinct(StringComparer.Ordinal).Count() != result.Count)
                throw new InvalidOperationException("Duplicate MCP tool identity.");
            return result;
        }

        private static RemoteTool Describe(McpServerOptions server, McpClientTool tool) => new RemoteTool
        {
            ServerId = server.Id, ServerName = server.Name, Identity = EffectiveIdentity(server), OriginalName = tool.Name,
            Definition = new ChatTool { Function = new ToolDefinition { Name = "mcp_" + LocalState.Hash(server.Id + "\n" + tool.Name).Substring(0, 40),
                Description = server.Name + " / " + tool.Name + ": " + tool.Description,
                Parameters = JObject.Parse(tool.JsonSchema.GetRawText()) } }
        };

        public static RemoteTool Describe(McpServerOptions server, JObject tool) => new RemoteTool
        {
            ServerId = server.Id, ServerName = server.Name, Identity = EffectiveIdentity(server), OriginalName = (string)tool["name"],
            Definition = new ChatTool { Function = new ToolDefinition { Name = "mcp_" + LocalState.Hash(server.Id + "\n" + (string)tool["name"]).Substring(0, 40),
                Description = server.Name + " / " + (string)tool["name"] + ": " + (string)tool["description"],
                Parameters = (JObject)tool["inputSchema"]?.DeepClone() ?? throw new InvalidOperationException("Tool has no input schema.") } }
        };

        public async Task<ToolExecution> ExecuteAsync(RemoteTool tool, string callId, string arguments,
            Func<ToolApproval, CancellationToken, Task<bool>> approve, Action<string> activity, CancellationToken token)
        {
            tool = JsonConvert.DeserializeObject<RemoteTool>(JsonConvert.SerializeObject(tool));
            var execution = new ToolExecution { ServerId = tool.ServerId, ServerName = tool.ServerName, ServerIdentity = tool.Identity, ToolName = tool.OriginalName, CallId = callId };
            JObject parsed = JObject.Parse(arguments);
            string canonical = parsed.ToString(Formatting.None);
            string binding = LocalState.Hash(callId + "\n" + tool.Identity + "\n" + tool.SchemaHash + "\n" + canonical);
            if (_dispatched.ContainsKey(callId)) throw new InvalidOperationException("This tool call ID was already dispatched; it will not be replayed.");
            JsonSchema schema = JsonSchema.FromText(tool.Definition.Function.Parameters.ToString(Formatting.None));
            if (!schema.Evaluate(JsonNode.Parse(canonical), new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid)
                return Reject(execution, "Arguments do not match the tool's JSON Schema.");
            Connection connection = await ConnectAsync(tool.ServerId, token).ConfigureAwait(false);
            McpServerOptions server = Current(tool.ServerId);
            if (EffectiveIdentity(server) != tool.Identity) return Reject(execution, "Connection identity changed; rediscover tools.");
            McpToolPolicy policy = server.Policy(tool.OriginalName);
            if (!policy.Enabled) return Reject(execution, "Tool is disabled.");
            bool approved = false;
            if (policy.RequireApproval)
            {
                activity?.Invoke(tool + ": awaiting approval");
                string displayed = parsed.ToString(Formatting.Indented);
                var approval = new ToolApproval { Server = tool.ServerName, Tool = tool.OriginalName, CallId = callId,
                    SchemaHash = tool.SchemaHash, Arguments = displayed };
                approved = await approve(approval, token).ConfigureAwait(false);
                if (approval.Arguments != displayed || approval.SchemaHash != tool.SchemaHash || approval.CallId != callId ||
                    approval.Server != tool.ServerName || approval.Tool != tool.OriginalName)
                    return Reject(execution, "Approval parameters changed; this call was not sent.");
                if (!approved) return Reject(execution, "User declined this call.");
            }
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(server.TimeoutSeconds));
                var currentTools = await connection.Client.ListToolsAsync(cancellationToken: deadline.Token).ConfigureAwait(false);
                McpClientTool currentTool = currentTools.FirstOrDefault(t => t.Name == tool.OriginalName);
                if (currentTool == null || Describe(server, currentTool).SchemaHash != tool.SchemaHash)
                    return Reject(execution, "Tool or schema changed after planning; rediscover before calling.");
                server = Current(tool.ServerId);
                policy = server.Policy(tool.OriginalName);
                if (EffectiveIdentity(server) != tool.Identity || !policy.Enabled) return Reject(execution, "Connection or policy changed before dispatch.");
                if (policy.RequireApproval && !approved) return Reject(execution, "Approval policy changed; this call was not sent.");
                token.ThrowIfCancellationRequested();
                var values = parsed.Properties().ToDictionary(p => p.Name,
                    p => (object)System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(p.Value.ToString(Formatting.None)), StringComparer.Ordinal);
                _dispatched[callId] = binding;
                string log = "MCP server=" + server.Id + " tool=" + SafeName(tool.OriginalName) + " call=" + SafeName(callId) +
                    " approval=" + (policy.RequireApproval ? "approved" : "auto");
                Logger.Info(log + " status=sent");
                activity?.Invoke(tool + ": running (" + (policy.RequireApproval ? "approved" : "auto") + ")");
                var watch = Stopwatch.StartNew();
                try
                {
                    CallToolResult response = await connection.Client.CallToolAsync(tool.OriginalName, values,
                        progress: new Progress<ProgressNotificationValue>(value =>
                            activity?.Invoke(tool + ": " + value.Progress + (value.Total.HasValue ? " / " + value.Total.Value : ""))),
                        cancellationToken: deadline.Token).ConfigureAwait(false);
                    execution.Status = response.IsError == true ? "tool_error" : "returned";
                    execution.Result = await NormalizeAsync(response, server, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // A dispatched request can have changed remote state even when no reply arrives.
                    execution.Status = "unknown";
                    execution.Result = new JObject { ["message"] = "Outcome unknown. Cancellation/timeout is not rollback. Check the remote system before retrying.",
                        ["errorType"] = ex.GetType().Name };
                    connection.Invalid = true;
                }
                finally
                {
                    Logger.Info(log + " status=" + execution.Status + " elapsedMs=" + watch.ElapsedMilliseconds);
                    activity?.Invoke(tool + ": " + execution.Status + " (" + watch.ElapsedMilliseconds + " ms)");
                }
            }
            return execution;
        }

        private static ToolExecution Reject(ToolExecution execution, string reason)
        {
            execution.Status = "rejected";
            execution.Result = new JObject { ["message"] = reason };
            return execution;
        }

        public async Task<JObject> DirectoryAsync(string serverId, CancellationToken token)
        {
            Connection connection = await ConnectAsync(serverId, token).ConfigureAwait(false);
            var result = new JObject { ["tools"] = new JArray(), ["resources"] = new JArray(), ["prompts"] = new JArray() };
            if (connection.Client.ServerCapabilities.Tools != null)
            {
                string cursor = null;
                do
                {
                    var page = await connection.Client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, token).ConfigureAwait(false);
                    foreach (Tool tool in page.Tools) ((JArray)result["tools"]).Add(JObject.Parse(System.Text.Json.JsonSerializer.Serialize(tool)));
                    cursor = page.NextCursor;
                } while (cursor != null);
            }
            if (connection.Client.ServerCapabilities.Resources != null)
            {
                string cursor = null;
                do
                {
                    var page = await connection.Client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = cursor }, token).ConfigureAwait(false);
                    foreach (Resource resource in page.Resources) ((JArray)result["resources"]).Add(JObject.Parse(System.Text.Json.JsonSerializer.Serialize(resource)));
                    cursor = page.NextCursor;
                } while (cursor != null);
            }
            if (connection.Client.ServerCapabilities.Prompts != null)
            {
                string cursor = null;
                do
                {
                    var page = await connection.Client.ListPromptsAsync(new ListPromptsRequestParams { Cursor = cursor }, token).ConfigureAwait(false);
                    foreach (Prompt prompt in page.Prompts) ((JArray)result["prompts"]).Add(JObject.Parse(System.Text.Json.JsonSerializer.Serialize(prompt)));
                    cursor = page.NextCursor;
                } while (cursor != null);
            }
            return result;
        }

        public async Task<JObject> ReadResourceAsync(string serverId, string uri, CancellationToken token)
        {
            Connection connection = await ConnectAsync(serverId, token).ConfigureAwait(false);
            if (connection.Client.ServerCapabilities.Resources == null) throw new InvalidOperationException("Server does not advertise resources.");
            return await NormalizeAsync(await connection.Client.ReadResourceAsync(uri, cancellationToken: token).ConfigureAwait(false),
                Current(serverId), token).ConfigureAwait(false);
        }

        public async Task<JObject> GetPromptAsync(string serverId, string name, string arguments, CancellationToken token)
        {
            Connection connection = await ConnectAsync(serverId, token).ConfigureAwait(false);
            if (connection.Client.ServerCapabilities.Prompts == null) throw new InvalidOperationException("Server does not advertise prompts.");
            var values = JObject.Parse(arguments).Properties().ToDictionary(p => p.Name, p => (object)(string)p.Value);
            return await NormalizeAsync(await connection.Client.GetPromptAsync(name, values, cancellationToken: token).ConfigureAwait(false),
                Current(serverId), token).ConfigureAwait(false);
        }

        private static async Task<JObject> NormalizeAsync<T>(T response, McpServerOptions server, CancellationToken token)
        {
            JObject json = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(response));
            var secrets = new List<string> { EncryptionHelper.Decrypt(server.Secret), EncryptionHelper.Decrypt(server.ClientSecret) };
            if (server.Authentication == McpAuthentication.OAuth)
            {
                var tokens = await new McpTokenCache(server.Identity).GetTokensAsync(token).ConfigureAwait(false);
                if (tokens != null) secrets.AddRange(new[] { tokens.AccessToken, tokens.RefreshToken, tokens.ClientSecret });
            }
            foreach (JProperty property in json.Descendants().OfType<JProperty>().ToList())
            {
                if (new[] { "authorization", "api_key", "access_token", "refresh_token", "client_secret", "password" }.Contains(property.Name.ToLowerInvariant()))
                    property.Value = "[redacted]";
                else if ((property.Name == "data" && new[] { "image", "audio" }.Contains((string)property.Parent["type"])) ||
                    (property.Name == "blob" && property.Parent["uri"] != null && property.Parent["mimeType"] != null))
                    property.Value = "[non-text data omitted: this chat model receives text only]";
                else if (property.Value.Type == JTokenType.String)
                {
                    string value = (string)property.Value;
                    foreach (string secret in secrets.Where(s => !string.IsNullOrEmpty(s))) value = value.Replace(secret, "[redacted]");
                    property.Value = value;
                }
            }
            return json;
        }

        private static string SafeName(string value) => (value ?? "").Replace("\r", "").Replace("\n", "");

        public async Task CloseAsync(string serverId)
        {
            if (!_connections.TryGetValue(serverId, out Connection connection)) return;
            _connections.Remove(serverId);
            try { await connection.Client.DisposeAsync().ConfigureAwait(false); }
            finally { if (connection.Transport != null) await connection.Transport.DisposeAsync().ConfigureAwait(false); }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (string id in _connections.Keys.ToArray()) await CloseAsync(id).ConfigureAwait(false);
        }
    }
}
