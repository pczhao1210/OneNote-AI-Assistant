using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using OneNoteAI.AI;
using OneNoteAI.Knowledge;
using OneNoteAI.Mcp;
using OneNoteAI.Settings;
using static OneNoteAI.UI.KnowledgeUi;

namespace OneNoteAI.UI
{
    internal sealed class KnowledgeSettingsDialog : DpiAwareForm
    {
        private readonly KnowledgeOptions _draft;
        private readonly TextBox _endpoint = new TextBox();
        private readonly TextBox _key = new TextBox { UseSystemPasswordChar = true };
        private readonly ComboBox _model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly NumericUpDown _dimensions = new NumericUpDown { Minimum = 0, Maximum = 16384 };
        private readonly NumericUpDown _context = new NumericUpDown { Minimum = 0, Maximum = 4000000, Increment = 1024 };
        private readonly NumericUpDown _retrievedChunks = new NumericUpDown { Minimum = 1, Maximum = KnowledgeOptions.MaxRetrievedChunksLimit };
        private readonly CheckBox _tools = new CheckBox { AutoSize = true };
        private readonly CheckBox _automatic = new CheckBox { AutoSize = true };
        private readonly CheckBox _diagrams = new CheckBox { AutoSize = true };
        private readonly KnowledgeScopeTree _scope = new KnowledgeScopeTree();
        private readonly ListBox _servers = new ListBox { Dock = DockStyle.Fill };
        private CancellationTokenSource _test;

        public KnowledgeSettingsDialog(NoteNode hierarchy, Func<string> runtimeVersion = null)
        {
            _draft = (SettingsManager.Current.Knowledge ?? new KnowledgeOptions()).Clone();
            Text = L("知识索引与 MCP 设置", "Knowledge index and MCP settings");
            Size = new Size(820, 760);
            MinimumSize = new Size(720, 620);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyTo(this);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var embedding = new TabPage(L("Embedding 与索引授权", "Embedding and index consent"));
            var fields = Fields();
            Field(fields, "Embedding endpoint", _endpoint);
            Field(fields, L("独立 API Key", "Separate API key"), _key);
            _model.Items.AddRange(new object[] { "text-embedding-3-small", "text-embedding-3-large" });
            Field(fields, L("Embedding 模型", "Embedding model"), _model);
            Field(fields, L("维度（0 = 模型默认）", "Dimensions (0 = model default)"), _dimensions);
            Field(fields, L("聊天上下文窗口（0 = 预设）", "Chat context window (0 = preset)"), _context);
            Field(fields, L("检索片段上限（1-100）", "Retrieved passage limit (1-100)"), _retrievedChunks);
            _tools.Text = L("模型支持工具调用，文档不足或必要时使用", "Model supports tool calling; use only when needed");
            Field(fields, L("按需 MCP", "On-demand MCP"), _tools);
            _automatic.Text = L("窗口打开时，每 5 分钟增量更新", "Incremental refresh every 5 minutes while the window is open");
            Field(fields, L("自动索引", "Automatic indexing"), _automatic);
            var notice = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), MaximumSize = new Size(760, 0),
                Text = L("勾选范围即授权：构建索引时，其正文会发送至以上 Embedding 服务。本地保存笔记与向量副本，无需额外数据库服务。父级授权包含以后新增的子级；更换模型、地址或维度需重建。自定义聊天模型请填写实际上下文窗口。检索默认最多 16 个片段；调大可能增加耗时，实际送入模型的数量仍受上下文预算限制。",
                    "Selected notes are sent to the Embedding service when indexing and cached locally. Parent consent includes future descendants. Changing endpoint/model/dimensions requires reindexing. Set the actual context window for custom models. Retrieval defaults to 16 passages; higher limits may be slower, and the model context budget can reduce the final count.") };
            var test = Theme.CreateSecondaryButton(L("测试 Embedding", "Test Embedding"));
            test.Dock = DockStyle.Bottom;
            test.Height = 36;
            test.Click += async (s, e) =>
            {
                if (_test != null) return;
                _test = new CancellationTokenSource();
                test.Enabled = false;
                try
                {
                    EmbeddingOptions options = EmbeddingDraft();
                    await new EmbeddingClient(options).EmbedAsync(new[] { "OneNote AI embedding connection test" }, _test.Token);
                    MessageBox.Show(this, L("连接成功，维度：", "Connected. Dimensions: ") + options.VectorSize, Text);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { _test.Dispose(); _test = null; if (!IsDisposed) test.Enabled = true; }
            };
            embedding.Controls.Add(_scope);
            embedding.Controls.Add(test);
            embedding.Controls.Add(notice);
            embedding.Controls.Add(fields);
            var mcp = new TabPage("Remote HTTPS MCP");
            var mcpNotice = new Label { Dock = DockStyle.Top, Height = 68, Padding = new Padding(12),
                Text = L("只支持远程 HTTPS Streamable HTTP。新工具默认 Auto approve，包括写入和删除；可逐工具停用或设为 Require approval。聊天、Embedding 与 MCP 的密钥分别保存。",
                    "Remote HTTPS Streamable HTTP only. New tools default to Auto approve, including writes/deletes. Disable tools or require approval individually. Chat, Embedding and MCP credentials are separate.") };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8) };
            Button add = Theme.CreateSecondaryButton(L("添加连接", "Add server"));
            Button edit = Theme.CreateSecondaryButton(L("编辑连接", "Edit server"));
            Button remove = Theme.CreateSecondaryButton(L("移除连接", "Remove"));
            Button catalog = Theme.CreateSecondaryButton(L("工具与资源", "Tools / resources"));
            catalog.Width = 150;
            actions.Controls.AddRange(new Control[] { add, edit, remove, catalog });
            add.Click += (s, e) => EditServer(null);
            edit.Click += (s, e) => { if (_servers.SelectedItem is McpServerOptions server) EditServer(server); };
            remove.Click += (s, e) => { if (_servers.SelectedItem is McpServerOptions server) { _draft.Servers.Remove(server); RefreshServers(); } };
            catalog.Click += (s, e) =>
            {
                if (!(_servers.SelectedItem is McpServerOptions server)) return;
                using (var dialog = new McpToolsDialog(server))
                    if (dialog.ShowDialog(this) == DialogResult.OK) server.Tools = dialog.Policies;
            };
            mcp.Controls.Add(_servers);
            mcp.Controls.Add(actions);
            mcp.Controls.Add(mcpNotice);
            tabs.TabPages.Add(embedding);
            tabs.TabPages.Add(mcp);
            var display = new TabPage(L("回答显示", "Answer display"));
            var displayFields = Fields();
            _diagrams.Text = L("启用 Mermaid 流程图预览（需要 WebView2）", "Enable Mermaid diagram previews (requires WebView2)");
            Field(displayFields, L("流程图", "Diagrams"), _diagrams);
            var runtime = new Label { AutoSize = true, MaximumSize = new Size(480, 0) };
            Field(displayFields, "WebView2 Runtime", runtime);
            var download = new LinkLabel { AutoSize = true, Text = L("Microsoft WebView2 下载页面", "Microsoft WebView2 download page") };
            download.LinkClicked += (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            Field(displayFields, "", download);
            bool CheckRuntime()
            {
                try { runtime.Text = runtimeVersion == null ? DiagramPreviewDialog.RuntimeVersion() : runtimeVersion(); runtime.ForeColor = Theme.TextSecondary; return true; }
                catch (InvalidOperationException ex) { runtime.Text = ex.Message; runtime.ForeColor = Theme.AccentRed; return false; }
            }
            _diagrams.CheckedChanged += (s, e) => { if (_diagrams.Checked && !CheckRuntime()) _diagrams.Checked = false; };
            _diagrams.Checked = _draft.EnableDiagramPreview;
            if (!_diagrams.Checked && string.IsNullOrEmpty(runtime.Text)) runtime.Text = L("启用时检测；默认关闭", "Checked when enabled; off by default");
            display.Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(16),
                Text = L("正文使用原生富文本显示表格、标题、列表和代码块，无需浏览器。\n\n开启后，完整的 Mermaid 代码块下会显示“查看流程图”，点击在独立窗口中本地渲染。资源随安装包提供，不向外部渲染服务发送内容。语法错误时保留源码并提示；不会自动下载或安装 WebView2。",
                    "Tables, headings, lists and code use native rich text, with no browser required.\n\nWhen enabled, complete Mermaid blocks offer View diagram in a separate local preview. Assets ship with the add-in; content is not sent to an external renderer. Invalid syntax keeps the source and shows an error. WebView2 is never downloaded or installed automatically.") });
            display.Controls.Add(displayFields);
            tabs.TabPages.Add(display);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            Button save = Theme.CreatePrimaryButton(L("保存设置", "Save settings"));
            Button cancel = Theme.CreateSecondaryButton(L("取消", "Cancel"));
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.AddRange(new Control[] { cancel, save });
            save.Click += (s, e) =>
            {
                try
                {
                    _draft.Embedding = EmbeddingDraft();
                    _draft.AllowedRootIds = _scope.CheckedRoots;
                    _draft.ContextWindow = (int)_context.Value;
                    _draft.MaxRetrievedChunks = (int)_retrievedChunks.Value;
                    _draft.ModelSupportsTools = _tools.Checked;
                    _draft.AutomaticIndexing = _automatic.Checked;
                    if (_diagrams.Checked && !CheckRuntime()) return;
                    _draft.EnableDiagramPreview = _diagrams.Checked;
                    _draft.Validate();
                    AppSettings latest = SettingsManager.Snapshot();
                    latest.Knowledge = _draft;
                    SettingsManager.Save(latest);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            Controls.Add(tabs);
            Controls.Add(footer);
            CancelButton = cancel;
            _endpoint.Text = _draft.Embedding.Endpoint;
            _key.Text = ReadSecret(_draft.Embedding.ApiKey, this);
            _model.Text = _draft.Embedding.Model;
            _dimensions.Value = _draft.Embedding.Dimensions ?? 0;
            _context.Value = _draft.ContextWindow;
            _retrievedChunks.Value = _draft.MaxRetrievedChunks;
            _tools.Checked = _draft.ModelSupportsTools;
            _automatic.Checked = _draft.AutomaticIndexing;
            _scope.Populate(hierarchy, _draft.AllowedRootIds);
            RefreshServers();
            FormClosing += (s, e) => { if (_test != null) { e.Cancel = true; _test.Cancel(); } };
        }

        private EmbeddingOptions EmbeddingDraft()
        {
            var options = new EmbeddingOptions { Endpoint = _endpoint.Text.Trim(), Model = _model.Text.Trim(),
                ApiKey = EncryptionHelper.Encrypt(_key.Text.Trim()), Dimensions = _dimensions.Value == 0 ? (int?)null : (int)_dimensions.Value };
            options.Validate(false);
            return options;
        }

        private void RefreshServers()
        {
            _servers.DataSource = null;
            _servers.DataSource = _draft.Servers.ToList();
        }

        private void EditServer(McpServerOptions server)
        {
            using (var dialog = new McpServerDialog(server ?? new McpServerOptions()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (server != null) _draft.Servers.Remove(server);
                _draft.Servers.Add(dialog.Server);
                RefreshServers();
            }
        }
    }

    internal sealed class McpServerDialog : DpiAwareForm
    {
        public McpServerOptions Server { get; }
        public McpServerDialog(McpServerOptions server)
        {
            Server = JsonConvert.DeserializeObject<McpServerOptions>(JsonConvert.SerializeObject(server));
            Text = L("MCP 连接", "MCP connection");
            Size = new Size(730, 730);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyTo(this);
            var fields = Fields();
            var name = new TextBox { Text = Server.Name };
            var endpoint = new TextBox { Text = Server.Endpoint };
            var enabled = new CheckBox { Checked = Server.Enabled, Text = L("启用", "Enabled"), AutoSize = true };
            var authentication = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            authentication.Items.AddRange(Enum.GetValues(typeof(McpAuthentication)).Cast<object>().ToArray());
            authentication.SelectedItem = Server.Authentication;
            var secret = new TextBox { Text = ReadSecret(Server.Secret, this), UseSystemPasswordChar = true };
            var header = new TextBox { Text = Server.HeaderName };
            var client = new TextBox { Text = Server.ClientId };
            var clientSecret = new TextBox { Text = ReadSecret(Server.ClientSecret, this), UseSystemPasswordChar = true };
            var metadata = new TextBox { Text = Server.ClientMetadataUrl };
            var scopes = new TextBox { Text = Server.Scopes };
            var port = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = Server.CallbackPort };
            var timeout = new NumericUpDown { Minimum = 5, Maximum = 1800, Value = Server.TimeoutSeconds };
            Field(fields, L("名称", "Name"), name);
            Field(fields, "HTTPS endpoint", endpoint);
            Field(fields, L("状态", "Status"), enabled);
            Field(fields, L("认证", "Authentication"), authentication);
            Label secretLabel = Field(fields, L("访问令牌 / API Key", "Access token / API key"), secret);
            Field(fields, "API key header", header);
            Field(fields, "OAuth client ID", client);
            Field(fields, L("OAuth client secret（可选）", "OAuth client secret (optional)"), clientSecret);
            Field(fields, L("客户端元数据 URL（可选）", "Client metadata URL (optional)"), metadata);
            Field(fields, "OAuth scopes", scopes);
            Field(fields, L("OAuth 回调端口", "OAuth callback port"), port);
            Field(fields, L("请求超时（秒）", "Request timeout (seconds)"), timeout);
            var note = new Label { Dock = DockStyle.Fill, Padding = new Padding(16) };
            void UpdateAuthentication()
            {
                var auth = (McpAuthentication)authentication.SelectedItem;
                bool oauth = auth == McpAuthentication.OAuth;
                secret.Enabled = auth == McpAuthentication.Bearer || auth == McpAuthentication.ApiKeyHeader;
                header.Enabled = auth == McpAuthentication.ApiKeyHeader;
                client.Enabled = clientSecret.Enabled = metadata.Enabled = scopes.Enabled = port.Enabled = oauth;
                secretLabel.Text = auth == McpAuthentication.Bearer ? "Bearer token" : "API key";
                note.Text = auth == McpAuthentication.Bearer
                    ? L("填写 MCP 服务签发的访问令牌原文，不要添加 Bearer 前缀，也不要填聊天或 Embedding 的 Key。请求会自动添加 Authorization: Bearer <token>。",
                        "Paste the access token issued by this MCP service, without the Bearer prefix. Do not use your chat or Embedding key. Authorization: Bearer <token> is added automatically.")
                    : oauth
                    ? L("OAuth 使用系统浏览器、PKCE 和临时 127.0.0.1 回调，无需手工填写 Bearer token。留空 client ID 要求服务端支持动态注册。",
                        "OAuth uses your browser, PKCE and a temporary 127.0.0.1 callback; no manual Bearer token is needed. An empty client ID requires dynamic registration support.")
                    : L("API-key 认证填写 MCP 服务的 Key 和指定请求头。无认证模式不发送凭据。仅启用可信服务。",
                        "API-key authentication uses this MCP service's key and specified header. None sends no credentials. Enable only trusted services.");
            }
            authentication.SelectedIndexChanged += (s, e) => UpdateAuthentication();
            UpdateAuthentication();
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
            Button save = Theme.CreatePrimaryButton(L("确定", "OK"));
            Button cancel = Theme.CreateSecondaryButton(L("取消", "Cancel"));
            Button logout = Theme.CreateSecondaryButton(L("退出 OAuth", "Sign out OAuth"));
            logout.Width = 140;
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.AddRange(new Control[] { cancel, save, logout });
            logout.Click += (s, e) => { new McpTokenCache(server.Identity).Clear(); MessageBox.Show(this, L("本机 OAuth 凭据已清除。", "Local OAuth credentials cleared."), Text); };
            save.Click += (s, e) =>
            {
                try
                {
                    Server.Name = name.Text.Trim();
                    Server.Endpoint = endpoint.Text.Trim();
                    Server.Enabled = enabled.Checked;
                    Server.Authentication = (McpAuthentication)authentication.SelectedItem;
                    Server.Secret = EncryptionHelper.Encrypt(secret.Text.Trim());
                    Server.HeaderName = header.Text.Trim();
                    Server.ClientId = client.Text.Trim();
                    Server.ClientSecret = EncryptionHelper.Encrypt(clientSecret.Text.Trim());
                    Server.ClientMetadataUrl = metadata.Text.Trim();
                    Server.Scopes = scopes.Text.Trim();
                    Server.CallbackPort = (int)port.Value;
                    Server.TimeoutSeconds = (int)timeout.Value;
                    Server.Validate();
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            Controls.Add(note);
            Controls.Add(fields);
            Controls.Add(footer);
            CancelButton = cancel;
        }
    }
}
