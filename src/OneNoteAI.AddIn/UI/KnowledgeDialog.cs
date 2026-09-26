using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.Conversation;
using OneNoteAI.Knowledge;
using OneNoteAI.Mcp;
using OneNoteAI.OneNote;
using OneNoteAI.Settings;
using static OneNoteAI.UI.KnowledgeUi;

namespace OneNoteAI.UI
{
    internal sealed class KnowledgeDialog : DpiAwareForm
    {
        private readonly INoteSource _source;
        private readonly IndexStore _store;
        private readonly Func<string> _currentPageId;
        private readonly VectorIndex _vectors;
        private readonly IndexingService _indexing;
        private readonly RetrievalService _retrieval;
        private readonly McpManager _mcp;
        private readonly KnowledgeConversation _conversation;
        private readonly KnowledgeScopeTree _scope = new KnowledgeScopeTree();
        private readonly ComboBox _mode = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckedListBox _servers = new CheckedListBox { Dock = DockStyle.Bottom, Height = 120, CheckOnClick = true };
        private readonly CheckBox _external = new CheckBox { Dock = DockStyle.Bottom, Height = 30, Text = L("启用所选 MCP 连接", "Use selected MCP servers") };
        private readonly RichTextBox _answer = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false,
            BackColor = Theme.BgCard, BorderStyle = BorderStyle.None, Font = Theme.FontContent };
        private readonly ListBox _sources = new ListBox { Dock = DockStyle.Bottom, Height = 150, HorizontalScrollbar = true };
        private readonly ListBox _activity = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        private readonly TextBox _question = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = Theme.FontContent };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8), AutoEllipsis = true };
        private readonly Button _ask = Theme.CreatePrimaryButton(L("提问", "Ask"));
        private readonly Button _find = Theme.CreateSecondaryButton(L("找资料", "Find passages"));
        private readonly Button _stop = Theme.CreateDangerButton(L("停止", "Stop"));
        private readonly Button _update = Theme.CreateSecondaryButton(L("更新索引", "Update index"));
        private readonly Button _settings = Theme.CreateSecondaryButton(L("索引与 MCP 设置", "Index / MCP settings"));
        private readonly Button _save = Theme.CreateSecondaryButton(L("保存到当前页", "Save to current page"));
        private readonly FlowLayoutPanel _toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6) };
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
        private CancellationTokenSource _operation;
        private bool _loading;
        private bool _closing;
        private bool _editingSettings;
        private bool _pendingReset;
        private int _viewEpoch;
        private NoteNode _hierarchy;
        private KnowledgeAnswer _lastAnswer;

        public KnowledgeDialog(INoteSource source = null, IndexStore store = null, Func<string> currentPageId = null)
        {
            _source = source ?? new OneNoteSource();
            _store = store ?? new IndexStore();
            _currentPageId = currentPageId ?? (() => new OneNoteProvider().GetCurrentPageId());
            Text = L("OneNote 知识助手", "OneNote knowledge assistant");
            Size = new Size(1100, 790);
            MinimumSize = new Size(920, 660);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.ApplyTo(this);
            _vectors = new VectorIndex(_store);
            _indexing = new IndexingService(_source, _store, () => SettingsManager.Current.Knowledge);
            _retrieval = new RetrievalService(_source, _store, _vectors);
            _mcp = new McpManager(() => (SettingsManager.Current.Knowledge ?? new KnowledgeOptions()).Servers);
            _conversation = new KnowledgeConversation(_retrieval, _mcp);
            var split = new SplitContainer { Size = new Size(1000, 660), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 260, Panel1MinSize = 220 };
            split.Panel1.Padding = new Padding(8);
            split.Panel2.Padding = new Padding(8);
            _mode.Items.AddRange(new object[] { L("当前页面（不建索引）", "Current page (no indexing)"), L("选定笔记范围", "Selected note scope"), L("不读取笔记", "Do not read notes") });
            _mode.SelectedIndex = 0;
            split.Panel1.Controls.Add(_scope);
            split.Panel1.Controls.Add(_mode);
            split.Panel1.Controls.Add(_external);
            split.Panel1.Controls.Add(_servers);
            split.Panel1.Controls.Add(new Label { Dock = DockStyle.Top, Height = 32, Text = L("笔记范围", "Note scope"), Font = Theme.FontHeading });
            var answerTab = new TabPage(L("回答与来源", "Answer and sources"));
            answerTab.Controls.Add(_answer);
            answerTab.Controls.Add(_sources);
            var activityTab = new TabPage(L("索引 / 工具活动", "Index / tool activity"));
            activityTab.Controls.Add(_activity);
            _tabs.TabPages.AddRange(new[] { answerTab, activityTab });
            split.Panel2.Controls.Add(_tabs);
            var compose = new Panel { Dock = DockStyle.Bottom, Height = 152, Padding = new Padding(8) };
            var send = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 130, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _ask.Width = _find.Width = _stop.Width = 120;
            _ask.Height = _find.Height = _stop.Height = 29;
            _stop.Enabled = false;
            send.Controls.AddRange(new Control[] { _ask, _find, _stop });
            compose.Controls.Add(_question);
            compose.Controls.Add(send);
            compose.Controls.Add(new Label { Dock = DockStyle.Top, Height = 23, Text = L("问题（Ctrl+Enter 提问；追问会重新检索）", "Question (Ctrl+Enter to ask; follow-ups retrieve fresh sources)") });
            split.Panel2.Controls.Add(compose);
            Button fresh = Theme.CreateSecondaryButton(L("新对话", "New conversation"));
            Button clear = Theme.CreateSecondaryButton(L("清除本地索引", "Clear local index"));
            _settings.Width = 165;
            _save.Width = 155;
            clear.Width = 145;
            _toolbar.Controls.AddRange(new Control[] { _settings, _update, clear, fresh, _save });
            Controls.Add(split);
            Controls.Add(_status);
            Controls.Add(_toolbar);
            _ask.Click += async (s, e) => await AskAsync();
            _find.Click += async (s, e) => await FindAsync();
            _stop.Click += (s, e) => { _status.Text = L("正在停止；远程操作不保证回滚。", "Stopping; remote operations are not rolled back."); _operation?.Cancel(); };
            _update.Click += async (s, e) => await UpdateAsync();
            _settings.Click += async (s, e) => await ConfigureAsync();
            _save.Click += (s, e) => SaveAnswer();
            fresh.Click += (s, e) => ResetConversation();
            clear.Click += async (s, e) =>
            {
                if (MessageBox.Show(this, L("仅删除本机索引，不修改 OneNote。重建将再次调用 Embedding，是否继续？",
                    "Delete only the local index, not OneNote? Rebuilding will call Embeddings again."), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                await RunAsync(async token =>
                {
                    await Task.Run(() => { _vectors.ClearCache(); _store.Clear(); }, token);
                    ResetConversation();
                    _status.Text = L("本地索引已清除。", "Local index cleared.");
                });
            };
            _mode.SelectedIndexChanged += (s, e) => { _scope.Enabled = _mode.SelectedIndex == 1; _find.Enabled = _mode.SelectedIndex != 2; ResetConversation(); };
            _scope.ScopeChanged += (s, e) => ResetConversation();
            _external.CheckedChanged += (s, e) => { _servers.Enabled = _external.Checked; ResetConversation(); };
            _servers.ItemCheck += (s, e) => { if (!_loading) ResetConversation(); };
            _sources.DoubleClick += async (s, e) => await OpenSourceAsync();
            _question.KeyDown += async (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await AskAsync(); } };
            Shown += async (s, e) =>
            {
                await RunAsync(async token => { _hierarchy = await Task.Run(() => _source.Hierarchy(), token); Populate(); });
                _poll.Start();
            };
            _poll.Tick += async (s, e) =>
            {
                if (_operation == null && SettingsManager.Current.Knowledge.AutomaticIndexing) await UpdateAsync();
            };
            FormClosing += (s, e) =>
            {
                _poll.Stop();
                if (_operation != null) { e.Cancel = true; _closing = true; _operation.Cancel(); }
            };
            FormClosed += async (s, e) =>
            {
                SettingsManager.SettingsChanged -= SettingsChanged;
                _poll.Dispose();
                _store.Dispose();
                try { await _mcp.DisposeAsync(); }
                catch (Exception ex) { Logging.Logger.Warn("Knowledge MCP disconnect failed: " + ex.GetType().Name); }
            };
            SettingsManager.SettingsChanged += SettingsChanged;
            SetBusy(false);
        }

        private void Populate()
        {
            KnowledgeOptions options = SettingsManager.Current.Knowledge ?? new KnowledgeOptions();
            _loading = true;
            try
            {
                List<string> selected = _scope.CheckedRoots;
                _scope.Populate(_hierarchy, selected.Count == 0 ? options.AllowedRootIds : selected, NoteNode.Sections(_hierarchy, options.AllowedRootIds));
                var checkedServers = _servers.CheckedItems.Cast<McpServerOptions>().Select(s => s.Id).ToHashSet();
                _servers.Items.Clear();
                foreach (McpServerOptions server in options.Servers.Where(s => s.Enabled)) _servers.Items.Add(server, checkedServers.Count == 0 || checkedServers.Contains(server.Id));
                _status.Text = L("当前页问答无需 Embedding；跨页语义检索请先授权范围并更新索引。MCP 默认不启用。",
                    "Current-page QA needs no Embeddings. For cross-page semantic search, authorize a scope and update the index. MCP is off by default.");
            }
            finally { _loading = false; }
        }

        private string CurrentPage() => _mode.SelectedIndex == 0 ? _currentPageId() : _mode.SelectedIndex == 2 ? "" : null;

        private async Task AskAsync()
        {
            if (_operation != null || string.IsNullOrWhiteSpace(_question.Text)) return;
            string question = _question.Text.Trim();
            AppSettings settings = SettingsManager.Snapshot();
            List<string> roots = _scope.CheckedRoots;
            List<string> servers = _external.Checked ? _servers.CheckedItems.Cast<McpServerOptions>().Select(s => s.Id).ToList() : new List<string>();
            _lastAnswer = null;
            _sources.Items.Clear();
            _answer.AppendText((_answer.TextLength == 0 ? "" : "\n\n") + L("问：", "Q: ") + question + "\n\n");
            _tabs.SelectedIndex = 0;
            int epoch = _viewEpoch;
            IProgress<string> text = new Progress<string>(part => { if (!IsDisposed && epoch == _viewEpoch) { _answer.AppendText(part); _answer.SelectionStart = _answer.TextLength; _answer.ScrollToCaret(); } });
            IProgress<string> activity = new Progress<string>(Report);
            IProgress<IReadOnlyList<EvidenceSource>> sources = new Progress<IReadOnlyList<EvidenceSource>>(items => { if (epoch == _viewEpoch) ShowSources(items); });
            await RunAsync(async token =>
            {
                string page = CurrentPage();
                _lastAnswer = await Task.Run(() => _conversation.AnswerAsync(question, page, roots, servers, settings,
                    part => text.Report(part), item => activity.Report(item), items => sources.Report(items),
                    (request, ct) => ApproveAsync(this, request, ct), token), token);
                ShowSources(_lastAnswer.Sources);
                foreach (string warning in _lastAnswer.Warnings.Distinct()) _answer.AppendText("\n\n" + L("提示：", "Notice: ") + warning);
                _status.Text = L("完成。双击来源可查看原文或定位笔记。", "Done. Double-click a source to inspect it or open the note.");
            });
        }

        private async Task FindAsync()
        {
            if (_operation != null || string.IsNullOrWhiteSpace(_question.Text)) return;
            string query = _question.Text.Trim();
            List<string> roots = _scope.CheckedRoots;
            KnowledgeOptions options = SettingsManager.Current.Knowledge.Clone();
            _lastAnswer = null;
            await RunAsync(async token =>
            {
                string page = CurrentPage();
                RetrievalResult result = await Task.Run(() => page == null ? _retrieval.SearchAsync(query, roots, options, token) :
                    Task.FromResult(_retrieval.CurrentPage(page, query, token)), token);
                List<EvidenceSource> sources = result.Chunks.Select(_conversation.Evidence.Add).ToList();
                ShowSources(sources);
                _answer.Text = string.Join("\n\n", sources.Select(s => s + "\n" + s.Text));
                foreach (string warning in result.Warnings) _answer.AppendText("\n\n" + warning);
                _tabs.SelectedIndex = 0;
                _status.Text = L("找到片段：", "Passages: ") + sources.Count + "   " + L("语义索引页：", "Indexed pages: ") + result.Coverage.Indexed + "/" + result.Coverage.Total;
            });
        }

        private Task UpdateAsync()
        {
            KnowledgeOptions options = SettingsManager.Current.Knowledge.Clone();
            IProgress<string> progress = new Progress<string>(Report);
            return RunAsync(async token =>
            {
                await Task.Run(() => _indexing.UpdateAsync(options, progress, token), token);
                _hierarchy = await Task.Run(() => _source.Hierarchy(), token);
                Populate();
                IndexCoverage coverage = _store.Coverage(NoteNode.Sections(_hierarchy, options.AllowedRootIds), options.Embedding.Generation);
                _status.Text = L("已索引 / 可访问页：", "Indexed / accessible pages: ") + coverage.Indexed + "/" + coverage.Total +
                    L("；失败待重试：", "; failed, queued: ") + coverage.Failed;
            });
        }

        private Task ConfigureAsync() => RunAsync(async token =>
        {
            NoteNode tree = await Task.Run(() => _source.Hierarchy(), token);
            _editingSettings = true;
            try
            {
                using (var dialog = new KnowledgeSettingsDialog(tree))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    await _mcp.DisposeAsync();
                    _vectors.ClearCache();
                    _hierarchy = tree;
                    ResetConversation();
                    Populate();
                    await Task.Run(() => _indexing.Refresh(SettingsManager.Current.Knowledge.Clone()), token);
                }
            }
            finally { _editingSettings = false; }
        });

        private async Task OpenSourceAsync()
        {
            if (_operation != null || !(_sources.SelectedItem is EvidenceSource source)) return;
            await RunAsync(async token =>
            {
                if (source.Note != null)
                {
                    var validation = await Task.Run(() => _retrieval.ValidateSource(source.Note, requireCurrentVersion: false), token);
                    _source.Navigate(source.Note.PageId, validation.IsCurrent ? source.Note.BlockId : "");
                    if (!validation.IsCurrent) _status.Text = L("页面已修改，已打开当前版本；旧引用需要重新检索。", "Page changed; opened the current version. Retrieve again to refresh citations.");
                }
                else
                {
                    ValidateExternalSource(source.Execution);
                    using (var dialog = new DpiAwareForm { Text = source.ToString(), Size = new Size(840, 660), StartPosition = FormStartPosition.CenterParent })
                    {
                        Theme.ApplyTo(dialog);
                        var content = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = true, Font = Theme.FontContent,
                            Text = EvidenceRegistry.Format(source), BackColor = Theme.BgCard };
                        content.LinkClicked += (s, e) =>
                        {
                            if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out Uri uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                        };
                        dialog.Controls.Add(content);
                        dialog.ShowDialog(this);
                    }
                }
            });
        }

        private void SaveAnswer()
        {
            if (_lastAnswer == null) return;
            try
            {
                foreach (EvidenceSource source in _lastAnswer.Sources)
                {
                    if (source.Note != null) _retrieval.ValidateSource(source.Note);
                    else ValidateExternalSource(source.Execution);
                }
                var provider = new OneNoteProvider();
                var text = new StringBuilder(_lastAnswer.Text);
                var links = new List<(string Label, string Url)>();
                text.AppendLine().AppendLine().AppendLine(L("## 来源", "## Sources"));
                foreach (EvidenceSource source in _lastAnswer.Sources)
                {
                    if (source.Note != null)
                    {
                        provider.App.GetHyperlinkToObject(source.Note.PageId, source.Note.BlockId ?? "", out string link);
                        links.Add(("[" + source.Id + "] " + source.Note.Title, link));
                    }

                    else
                    {
                        text.AppendLine(source + " / " + source.Execution.Time.ToString("O") + " / call " + source.Execution.CallId);
                        foreach (string uri in source.Execution.Result.Descendants().OfType<JProperty>()
                            .Where(p => (p.Name == "url" || p.Name == "uri") && p.Value.Type == JTokenType.String).Select(p => (string)p.Value).Distinct())
                            text.AppendLine(uri);
                    }
                }
                new PageWriter().AppendOutline(provider.GetCurrentPageId(), text.ToString(), L("AI 知识问答", "AI knowledge answer"), links);
                _status.Text = L("已保存到当前页面；下次索引更新会正常处理此页。", "Saved to the current page; the next index update will process it normally.");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static void ValidateExternalSource(ToolExecution execution)
        {
            McpServerOptions server = SettingsManager.Current.Knowledge.Servers.FirstOrDefault(s => s.Id == execution.ServerId && s.Enabled);
            if (server == null || McpManager.EffectiveIdentity(server) != execution.ServerIdentity)
                throw new InvalidOperationException(L("连接身份已改变，请重新获取来源。", "Connection identity changed; retrieve the source again."));
        }

        private void ShowSources(IReadOnlyList<EvidenceSource> sources)
        {
            if (IsDisposed) return;
            _sources.BeginUpdate();
            _sources.Items.Clear();
            foreach (EvidenceSource source in sources) _sources.Items.Add(source);
            _sources.EndUpdate();
        }

        private void Report(string message)
        {
            if (IsDisposed) return;
            _status.Text = message;
            _activity.Items.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message);
            if (_activity.Items.Count > 500) _activity.Items.RemoveAt(0);
            _activity.TopIndex = Math.Max(0, _activity.Items.Count - 1);
        }

        private void ResetConversation()
        {
            if (_loading) return;
            _viewEpoch++;
            _conversation.Clear();
            _answer.Clear();
            _sources.Items.Clear();
            _lastAnswer = null;
            _save.Enabled = false;
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || _editingSettings) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => SettingsChanged(sender, e))); return; }
            if (_operation != null)
            {
                _pendingReset = true;
                _viewEpoch++;
                _operation.Cancel();
            }
            else
            {
                ResetConversation();
                _vectors.ClearCache();
                if (_hierarchy != null) Populate();
            }
        }

        private void SetBusy(bool busy)
        {
            _toolbar.Enabled = !busy;
            _mode.Enabled = !busy;
            _scope.Enabled = !busy && _mode.SelectedIndex == 1;
            _external.Enabled = !busy;
            _servers.Enabled = !busy && _external.Checked;
            _sources.Enabled = !busy;
            _ask.Enabled = !busy;
            _find.Enabled = !busy && _mode.SelectedIndex != 2;
            _stop.Enabled = busy;
            _save.Enabled = !busy && _lastAnswer != null;
        }

        private async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (_operation != null) return;
            _operation = new CancellationTokenSource();
            SetBusy(true);
            try { await action(_operation.Token); }
            catch (OperationCanceledException) { Report(L("已停止。已发送的远程操作不保证撤销。", "Stopped. Sent remote operations may still have taken effect.")); }
            catch (Exception ex)
            {
                Report(ex.Message);
                Logging.Logger.Warn("Knowledge operation failed: " + ex.GetType().Name);
                if (!_closing) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _operation.Dispose();
                _operation = null;
                if (_pendingReset)
                {
                    _pendingReset = false;
                    ResetConversation();
                    _vectors.ClearCache();
                    if (_hierarchy != null) Populate();
                }
                SetBusy(false);
                if (_closing) Close();
            }
        }
    }
}
