using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.Mcp;
using OneNoteAI.Settings;
using static OneNoteAI.UI.KnowledgeUi;

namespace OneNoteAI.UI
{
    internal sealed class McpToolsDialog : Form
    {
        private readonly McpServerOptions _server;
        private readonly McpManager _manager;
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly DataGridView _tools = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly TextBox _arguments = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = "{}" };
        private readonly ListBox _resources = new ListBox { Dock = DockStyle.Fill };
        private readonly ListBox _prompts = new ListBox { Dock = DockStyle.Fill };
        private readonly TextBox _resourceUri = new TextBox { Dock = DockStyle.Top };
        private readonly TextBox _promptArguments = new TextBox { Dock = DockStyle.Bottom, Height = 90, Multiline = true, Text = "{}" };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8), AutoEllipsis = true };
        private readonly Button _stop = Theme.CreateDangerButton(L("停止", "Stop"));
        private CancellationTokenSource _operation;
        private bool _closing;
        private bool _loading;
        public Dictionary<string, McpToolPolicy> Policies => _server.Tools;

        public McpToolsDialog(McpServerOptions server)
        {
            _server = JsonConvert.DeserializeObject<McpServerOptions>(JsonConvert.SerializeObject(server));
            _manager = new McpManager(() => new[] { _server });
            Text = server.Name + " / MCP";
            Size = new Size(900, 730);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyTo(this);
            var toolTab = new TabPage(L("工具与审批", "Tools and approval"));
            _tools.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = L("启用", "Enabled"), FillWeight = 14 });
            _tools.Columns.Add(new DataGridViewComboBoxColumn { Name = "approval", HeaderText = L("审批", "Approval"), FillWeight = 28,
                DataSource = new[] { "Auto approve", "Require approval" } });
            _tools.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = L("工具", "Tool"), ReadOnly = true, FillWeight = 38 });
            _tools.Columns.Add(new DataGridViewTextBoxColumn { Name = "description", HeaderText = L("说明", "Description"), ReadOnly = true, FillWeight = 80 });
            _tools.CurrentCellDirtyStateChanged += (s, e) => { if (_tools.IsCurrentCellDirty) _tools.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _tools.CellValueChanged += (s, e) =>
            {
                if (_loading || e.RowIndex < 0) return;
                DataGridViewRow row = _tools.Rows[e.RowIndex];
                if (row.Tag is RemoteTool tool) _server.Tools[tool.OriginalName] = new McpToolPolicy
                { Enabled = (bool)row.Cells["enabled"].Value, RequireApproval = (string)row.Cells["approval"].Value == "Require approval" };
            };
            var input = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8) };
            var toolButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 170, FlowDirection = FlowDirection.TopDown };
            Button invoke = Theme.CreatePrimaryButton(L("手动调用", "Call tool"));
            Button schema = Theme.CreateSecondaryButton(L("查看参数 Schema", "View input schema"));
            invoke.Width = schema.Width = 160;
            toolButtons.Controls.AddRange(new Control[] { invoke, schema });
            input.Controls.Add(_arguments);
            input.Controls.Add(toolButtons);
            input.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = L("JSON 参数；Auto approve 也允许写入/删除。", "JSON arguments; Auto approve also permits writes/deletes.") });
            toolTab.Controls.Add(_tools);
            toolTab.Controls.Add(input);
            schema.Click += (s, e) =>
            {
                if (_tools.CurrentRow?.Tag is RemoteTool tool) TextViewer(this, tool.OriginalName, tool.Definition.Function.Parameters.ToString(Formatting.Indented));
            };
            invoke.Click += async (s, e) =>
            {
                if (!(_tools.CurrentRow?.Tag is RemoteTool tool)) return;
                string arguments = _arguments.Text;
                await RunAsync(async token =>
                {
                    ToolExecution result = await Task.Run(() => _manager.ExecuteAsync(tool, "manual:" + Guid.NewGuid().ToString("N"), arguments,
                        (request, ct) => ApproveAsync(this, request, ct), text => UiThread.Post(() => { if (!IsDisposed) _status.Text = text; }), token));
                    _status.Text = result.Status;
                    if (!_closing) TextViewer(this, L("MCP 结果 / 操作回执", "MCP result / operation receipt"),
                        L("取消不代表回滚；结果未写入向量索引。", "Cancellation is not rollback; this result is not indexed.") + "\r\n\r\n" +
                        JsonConvert.SerializeObject(result, Formatting.Indented));
                });
            };
            var resourceTab = new TabPage(L("资源", "Resources"));
            Button read = Theme.CreatePrimaryButton(L("读取远程资源 URI", "Read remote resource URI"));
            read.Dock = DockStyle.Bottom;
            resourceTab.Controls.Add(_resources);
            resourceTab.Controls.Add(_resourceUri);
            resourceTab.Controls.Add(read);
            _resources.SelectedIndexChanged += (s, e) => { if (_resources.SelectedItem is DirectoryItem item) _resourceUri.Text = item.Key; };
            read.Click += async (s, e) =>
            {
                string uri = _resourceUri.Text.Trim();
                await RunAsync(async token =>
                {
                    JObject result = await _manager.ReadResourceAsync(_server.Id, uri, token);
                    if (!_closing) TextViewer(this, uri, result.ToString(Formatting.Indented));
                });
            };
            var promptTab = new TabPage(L("提示词", "Prompts"));
            Button get = Theme.CreatePrimaryButton(L("获取提示词", "Get prompt"));
            get.Dock = DockStyle.Bottom;
            promptTab.Controls.Add(_prompts);
            promptTab.Controls.Add(_promptArguments);
            promptTab.Controls.Add(get);
            get.Click += async (s, e) =>
            {
                if (!(_prompts.SelectedItem is DirectoryItem item)) return;
                string arguments = _promptArguments.Text;
                await RunAsync(async token =>
                {
                    JObject result = await _manager.GetPromptAsync(_server.Id, item.Key, arguments, token);
                    if (!_closing) TextViewer(this, L("远程提示词（不会自动执行）", "Remote prompt (not automatically executed)"), result.ToString(Formatting.Indented));
                });
            };
            _tabs.TabPages.AddRange(new[] { toolTab, resourceTab, promptTab });
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
            Button save = Theme.CreatePrimaryButton(L("保存工具策略", "Save tool policies"));
            save.Width = 150;
            save.DialogResult = DialogResult.OK;
            Button refresh = Theme.CreateSecondaryButton(L("刷新目录", "Refresh directory"));
            refresh.Width = 145;
            _stop.Enabled = false;
            _stop.Click += (s, e) => _operation?.Cancel();
            refresh.Click += async (s, e) => await LoadDirectoryAsync();
            footer.Controls.AddRange(new Control[] { save, _stop, refresh });
            Controls.Add(_tabs);
            Controls.Add(_status);
            Controls.Add(footer);
            Shown += async (s, e) => await LoadDirectoryAsync();
            FormClosing += (s, e) => { if (_operation != null) { e.Cancel = true; _closing = true; _operation.Cancel(); } };
            FormClosed += async (s, e) =>
            {
                try { await _manager.DisposeAsync(); }
                catch (Exception ex) { Logging.Logger.Warn("MCP disconnect failed: " + ex.GetType().Name); }
            };
        }

        private Task LoadDirectoryAsync() => RunAsync(async token =>
        {
            JObject directory = await _manager.DirectoryAsync(_server.Id, token);
            _loading = true;
            try
            {
                _tools.Rows.Clear();
                foreach (JObject definition in directory["tools"])
                {
                    RemoteTool tool = McpManager.Describe(_server, definition);
                    McpToolPolicy policy = _server.Policy(tool.OriginalName);
                    int row = _tools.Rows.Add(policy.Enabled, policy.RequireApproval ? "Require approval" : "Auto approve", tool.OriginalName, (string)definition["description"]);
                    _tools.Rows[row].Tag = tool;
                }
                _resources.Items.Clear();
                foreach (JObject item in directory["resources"]) _resources.Items.Add(new DirectoryItem((string)item["uri"], (string)item["name"]));
                _prompts.Items.Clear();
                foreach (JObject item in directory["prompts"]) _prompts.Items.Add(new DirectoryItem((string)item["name"],
                    (string)item["name"] + " " + item["arguments"]?.ToString(Formatting.None)));
                _status.Text = L("新工具默认 Auto approve；刷新保留已有策略。", "New tools default to Auto approve; refresh preserves existing policies.");
            }
            finally { _loading = false; }
        });

        private async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (_operation != null) return;
            _operation = new CancellationTokenSource();
            _operation.CancelAfter(TimeSpan.FromMinutes(5));
            _tabs.Enabled = false;
            _stop.Enabled = true;
            _status.Text = L("正在连接 / 执行…", "Connecting / running...");
            try { await action(_operation.Token); }
            catch (OperationCanceledException) { _status.Text = L("已停止。已发送的操作不保证撤销。", "Stopped. Sent operations may still have taken effect."); }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                if (!_closing) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                _tabs.Enabled = true;
                _stop.Enabled = false;
                if (_closing) Close();
            }
        }

        private sealed class DirectoryItem
        {
            public string Key { get; }
            private readonly string _name;
            public DirectoryItem(string key, string name) { Key = key; _name = name; }
            public override string ToString() => _name + "  " + Key;
        }
    }
}
