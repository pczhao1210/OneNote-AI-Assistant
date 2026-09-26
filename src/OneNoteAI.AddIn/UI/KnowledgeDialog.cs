using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OneNoteAI.AI;
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
        private readonly DeepseekClient _chat;
        private readonly Image _brandImage;
        private readonly KnowledgeScopeTree _scope = new KnowledgeScopeTree();
        private readonly ComboBox _mode = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckedListBox _servers = new CheckedListBox { Dock = DockStyle.Bottom, Height = 120, CheckOnClick = true };
        private readonly CheckBox _external = new CheckBox { Dock = DockStyle.Bottom, Height = 30, Text = L("允许按需使用 MCP", "MCP only when needed") };
        private readonly RichTextBox _answer = new MarkdownBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false,
            BackColor = Theme.BgCard, BorderStyle = BorderStyle.None, Font = Theme.FontContent, ScrollBars = RichTextBoxScrollBars.Vertical };
        private readonly ListBox _sources = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, FormattingEnabled = true };
        private readonly ListBox _activity = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        private readonly ChatInputBox _question = new ChatInputBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = Theme.FontContent };
        private readonly Label _scopeSummary = new Label { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4), AutoEllipsis = true };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8), AutoEllipsis = true };
        private readonly ChatSendButton _send = new ChatSendButton();
        private readonly List<string> _diagrams = new List<string>();
        private readonly Button _copy = Theme.CreateGhostButton(L("复制回答", "Copy answer"));
        private readonly Button _fresh = Theme.CreateGhostButton(L("新对话", "New chat"));
        private readonly Button _showSources = Theme.CreateGhostButton(L("来源", "Sources"));
        private readonly Button _showActivity = Theme.CreateGhostButton(L("活动", "Activity"));
        private readonly Button _more = Theme.CreateGhostButton("...");
        private readonly ToolStripMenuItem _find = new ToolStripMenuItem(L("搜索片段（不调用模型）", "Find passages (no model)"));
        private readonly ToolStripMenuItem _update = new ToolStripMenuItem(L("更新索引", "Update index"));
        private readonly ToolStripMenuItem _settings = new ToolStripMenuItem(L("索引与 MCP 设置", "Index / MCP settings"));
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolTip _tips = new ToolTip();
        private readonly SplitContainer _split = new SplitContainer { Size = new Size(1000, 660), Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1, SplitterDistance = 240, Panel1MinSize = 200 };
        private readonly TabControl _details = new TabControl { Dock = DockStyle.Bottom, Height = 170, Visible = false };
        private readonly ComboBox _turnPicker = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Panel _queuePanel = new Panel { Dock = DockStyle.Bottom, Height = 100, Visible = false, Padding = new Padding(8) };
        private readonly Label _queueTitle = new Label { Dock = DockStyle.Top, Height = 24 };
        private readonly ListBox _queued = new ListBox { Dock = DockStyle.Fill };
        private readonly Button _resumeQueue = Theme.CreateGhostButton(L("继续队列", "Resume"));
        private readonly Button _removeQueued = Theme.CreateGhostButton(L("移除", "Remove"));
        private readonly List<PendingQuestion> _queue = new List<PendingQuestion>();
        private readonly List<ChatTurn> _turns = new List<ChatTurn>();
        private readonly System.Windows.Forms.Timer _render = new System.Windows.Forms.Timer { Interval = 80 };
        private readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
        private CancellationTokenSource _operation;
        private bool _loading;
        private bool _closing;
        private bool _editingSettings;
        private bool _pendingReset;
        private bool _hasAllowedScope;
        private int _viewEpoch;
        private NoteNode _hierarchy;
        private bool _draining;
        private bool _queuePaused;
        private bool _answerBusy;
        private bool _dirty;
        private ChatTurn _activeTurn;

        private sealed class PendingQuestion
        {
            public string Text, Page, Scope;
            public List<string> Roots, Servers;
            public AppSettings Settings;
            public override string ToString() => Text.Replace("\r", " ").Replace("\n", " ");
        }

        private sealed class ChatTurn
        {
            public string Question, Scope, Text = "", Notice = "";
            public bool Finished;
            public List<EvidenceSource> Sources = new List<EvidenceSource>();
            public override string ToString() => Question.Replace("\r", " ").Replace("\n", " ");
        }

        public KnowledgeDialog(INoteSource source = null, IndexStore store = null, Func<string> currentPageId = null, DeepseekClient chat = null)
        {
            _source = source ?? new OneNoteSource();
            _store = store ?? new IndexStore();
            _currentPageId = currentPageId ?? (() => new OneNoteProvider().GetCurrentPageId());
            _chat = chat;
            Text = "OneNote " + Strings.BtnQA;
            Size = new Size(1100, 790);
            MinimumSize = new Size(920, 660);
            StartPosition = FormStartPosition.CenterScreen;
            Theme.ApplyTo(this);
            ShowInTaskbar = true;
            using (var icon = typeof(KnowledgeDialog).Assembly.GetManifestResourceStream("OneNoteAI.Ribbon.Icons.QA.ico"))
                Icon = new Icon(icon ?? throw new InvalidOperationException("The Q&A window icon resource is missing."));
            using (var stream = typeof(KnowledgeDialog).Assembly.GetManifestResourceStream("OneNoteAI.Ribbon.Icons.QA.png"))
            using (var image = Image.FromStream(stream ?? throw new InvalidOperationException("The Q&A image resource is missing.")))
                _brandImage = new Bitmap(image);
            _vectors = new VectorIndex(_store);
            _indexing = new IndexingService(_source, _store, () => SettingsManager.Current.Knowledge);
            _retrieval = new RetrievalService(_source, _store, _vectors);
            _mcp = new McpManager(() => (SettingsManager.Current.Knowledge ?? new KnowledgeOptions()).Servers);
            _conversation = new KnowledgeConversation(_retrieval, _mcp);
            _split.Panel1.Padding = new Padding(12);
            _split.Panel2.Padding = new Padding(16, 8, 16, 8);
            _split.Panel2.BackColor = Theme.BgCard;
            _mode.Items.AddRange(new object[] { L("当前页面（不建索引）", "Current page (no indexing)"), L("选定笔记范围", "Selected note scope"), L("不读取笔记", "Do not read notes") });
            _hasAllowedScope = SettingsManager.Current.Knowledge?.AllowedRootIds?.Count > 0;
            _mode.SelectedIndex = _hasAllowedScope ? 1 : 0;
            _scope.BorderStyle = BorderStyle.None;
            _scope.BackColor = Theme.BgPage;
            _split.Panel1.Controls.Add(_scope);
            _split.Panel1.Controls.Add(_mode);
            _split.Panel1.Controls.Add(_external);
            _split.Panel1.Controls.Add(_servers);
            _split.Panel1.Controls.Add(new Label { Dock = DockStyle.Top, Height = 32, Text = L("笔记范围", "Note scope"), Font = Theme.FontHeading });
            var sourceTab = new TabPage(L("来源（双击打开）", "Sources (double-click to open)"));
            sourceTab.Controls.Add(_sources);
            sourceTab.Controls.Add(_turnPicker);
            var activityTab = new TabPage(L("索引 / 工具活动", "Index / tool activity"));
            activityTab.Controls.Add(_activity);
            _details.TabPages.AddRange(new[] { sourceTab, activityTab });
            _split.Panel2.Controls.Add(_answer);
            _split.Panel2.Controls.Add(_details);
            _split.Panel2.Controls.Add(_scopeSummary);
            var compose = new Panel { Dock = DockStyle.Bottom, Height = 136, Padding = new Padding(0, 10, 0, 0) };
            var input = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.BgCard };
            input.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, input.ClientRectangle,
                _question.Focused ? Theme.Purple : Theme.BgCardBorder, ButtonBorderStyle.Solid);
            _question.GotFocus += (s, e) => input.Invalidate();
            _question.LostFocus += (s, e) => input.Invalidate();
            var send = new Panel { Dock = DockStyle.Right, Width = 48, Padding = new Padding(8, 0, 0, 0) };
            _send.Dock = DockStyle.Bottom;
            _send.Height = 40;
            send.Controls.Add(_send);
            input.Controls.Add(_question);
            input.Controls.Add(send);
            compose.Controls.Add(input);
            compose.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(0, 5, 0, 0), ForeColor = Theme.TextSecondary,
                Text = L("Enter 发送   Shift+Enter 换行   Ctrl+Enter 排队", "Enter send   Shift+Enter new line   Ctrl+Enter queue") });
            _split.Panel2.Controls.Add(_queuePanel);
            _split.Panel2.Controls.Add(compose);
            var queueActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 104, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _resumeQueue.Size = _removeQueued.Size = new Size(96, 28);
            _resumeQueue.Margin = _removeQueued.Margin = Padding.Empty;
            queueActions.Controls.AddRange(new Control[] { _resumeQueue, _removeQueued });
            _queuePanel.Controls.Add(_queued);
            _queuePanel.Controls.Add(queueActions);
            _queuePanel.Controls.Add(_queueTitle);
            var header = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(12, 8, 12, 8) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 430, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            _more.Width = 36;
            _copy.Width = 106;
            _fresh.Width = 88;
            _showSources.Width = 94;
            _showActivity.Width = 72;
            foreach (Button button in new[] { _more, _copy, _fresh, _showActivity, _showSources }) { button.Height = 32; button.Margin = new Padding(2, 0, 2, 0); actions.Controls.Add(button); }
            actions.Width = 422;
            var toggleScope = Theme.CreateGhostButton(L("笔记范围", "Note scope"));
            toggleScope.Dock = DockStyle.Left;
            toggleScope.Width = 100;
            toggleScope.Click += (s, e) => _split.Panel1Collapsed = !_split.Panel1Collapsed;
            header.Controls.Add(new Label { Dock = DockStyle.Fill, Text = Strings.BtnQA, UseMnemonic = false, Font = Theme.FontTitle, TextAlign = ContentAlignment.MiddleLeft });
            header.Controls.Add(new PictureBox { Dock = DockStyle.Left, Width = 36, Padding = new Padding(0, 3, 8, 3), Image = _brandImage, SizeMode = PictureBoxSizeMode.Zoom });
            header.Controls.Add(toggleScope);
            header.Controls.Add(actions);
            var clear = new ToolStripMenuItem(L("清除本地索引", "Clear local index"));
            _menu.Items.AddRange(new ToolStripItem[] { _settings, _update, _find, new ToolStripSeparator(), clear });
            _more.AccessibleName = L("更多操作", "More actions");
            _more.Click += (s, e) => _menu.Show(_more, new Point(0, _more.Height));
            _showSources.Click += (s, e) => ToggleDetails(0);
            _showActivity.Click += (s, e) => ToggleDetails(1);
            _tips.SetToolTip(_copy, L("复制选中文字，或最新回答及来源；不会写入笔记", "Copy selected text, or the latest answer and sources; no note is changed"));
            Controls.Add(_split);
            Controls.Add(_status);
            Controls.Add(header);
            _send.Click += async (s, e) => { if (_operation != null) CancelOperation(); else await SubmitAsync(false); };
            _find.Click += async (s, e) => await FindAsync();
            _update.Click += async (s, e) => await UpdateAsync();
            _settings.Click += async (s, e) => await ConfigureAsync();
            _copy.Click += (s, e) => CopyAnswer();
            _fresh.Click += (s, e) => ResetConversation();
            _resumeQueue.Click += async (s, e) => { _queuePaused = false; await DrainQueueAsync(); };
            _removeQueued.Click += (s, e) => { if (_queued.SelectedIndex >= 0) { _queue.RemoveAt(_queued.SelectedIndex); UpdateQueue(); } };
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
            _external.CheckedChanged += (s, e) =>
            {
                _servers.Enabled = _external.Checked;
                _servers.Visible = _external.Checked && _servers.Items.Count > 0;
                ResetConversation();
            };
            _servers.ItemCheck += (s, e) => { if (!_loading) ResetConversation(); };
            _sources.DoubleClick += async (s, e) => await OpenSourceAsync();
            _question.KeyDown += ComposerKeyDown;
            _question.AccessibleName = L("输入消息", "Message");
            _answer.AccessibleName = L("对话", "Conversation");
            _answer.LinkClicked += (s, e) => DiagramPreviewDialog.Open(this, e.LinkText, _diagrams);
            _queued.AccessibleName = L("待发送消息", "Queued messages");
            _turnPicker.AccessibleName = L("来源对应的对话轮次", "Source conversation turn");
            _question.TextChanged += (s, e) => UpdateSendButton();
            _answer.SelectionChanged += (s, e) => _copy.Enabled = _answer.SelectionLength > 0 || !string.IsNullOrWhiteSpace(_turns.LastOrDefault()?.Text);
            _turnPicker.SelectedIndexChanged += (s, e) => ShowSources((_turnPicker.SelectedItem as ChatTurn)?.Sources ?? new List<EvidenceSource>());
            _sources.Format += (s, e) => { if (e.ListItem is EvidenceSource item) e.Value = "[" + item.Id + "] " + (item.Note?.Title ?? item.ToString()); };
            _render.Tick += (s, e) => { if (_dirty) RenderConversation(); };
            _render.Start();
            _answer.SizeChanged += (s, e) =>
            {
                if (IsScalingForDpi) return;
                _answer.RightMargin = Math.Max(1, Math.Min(_answer.ClientSize.Width - ScaleLogical(16), ScaleLogical(800)));
                _dirty = true;
            };
            Shown += async (s, e) =>
            {
                await RunAsync(async token => { _hierarchy = await Task.Run(() => _source.Hierarchy(), token); Populate(); });
                if (!IsDisposed && !_closing) _poll.Start();
            };
            _poll.Tick += async (s, e) =>
            {
                if (_operation == null && SettingsManager.Current.Knowledge.AutomaticIndexing) await UpdateAsync();
            };
            FormClosing += (s, e) =>
            {
                _poll.Stop();
                _closing = true;
                _queue.Clear();
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
            UpdateScopeSummary();
            RenderConversation();
        }

        private void ToggleDetails(int index)
        {
            bool hide = _details.Visible && _details.SelectedIndex == index;
            _details.SelectedIndex = index;
            _details.Visible = !hide;
        }

        private void Populate()
        {
            KnowledgeOptions options = SettingsManager.Current.Knowledge ?? new KnowledgeOptions();
            _loading = true;
            try
            {
                bool hasAllowedScope = options.AllowedRootIds.Count > 0;
                if (!_hasAllowedScope && hasAllowedScope) _mode.SelectedIndex = 1;
                _hasAllowedScope = hasAllowedScope;
                List<string> selected = _scope.CheckedRoots;
                _scope.Populate(_hierarchy, selected.Count == 0 ? options.AllowedRootIds : selected, NoteNode.Sections(_hierarchy, options.AllowedRootIds));
                var checkedServers = _servers.CheckedItems.Cast<McpServerOptions>().Select(s => s.Id).ToHashSet();
                _servers.Items.Clear();
                foreach (McpServerOptions server in options.Servers.Where(s => s.Enabled)) _servers.Items.Add(server, checkedServers.Count == 0 || checkedServers.Contains(server.Id));
                _servers.Visible = _external.Checked && _servers.Items.Count > 0;
                _status.Text = L("文档优先回答，MCP 默认关闭、启用后按需使用。当前页无需索引；跨页语义检索需授权并更新索引。",
                    "Documents first; MCP is off by default and used only when needed. Current page needs no index; cross-page semantic search needs consent and indexing.");
                UpdateScopeSummary();
            }
            finally { _loading = false; }
        }

        private string CurrentPage() => _mode.SelectedIndex == 0 ? _currentPageId() : _mode.SelectedIndex == 2 ? "" : null;

        private string ScopeDescription()
        {
            if (_mode.SelectedIndex == 0) return L("当前页面（仅此页，不检索其他页面）", "Current page only (no other pages)");
            if (_mode.SelectedIndex == 2) return L("不读取笔记", "Do not read notes");
            if (_hierarchy == null) return L("选定笔记范围（正在读取目录）", "Selected note scope (loading hierarchy)");
            HashSet<string> sections = NoteNode.Sections(_hierarchy, _scope.CheckedRoots);
            sections.IntersectWith(NoteNode.Sections(_hierarchy, SettingsManager.Current.Knowledge.AllowedRootIds));
            if (sections.Count == 0) return L("选定笔记范围（尚未选择可访问的授权分区）", "Selected note scope (no accessible authorized section selected)");
            int pages = _hierarchy.DescendantsAndSelf().Count(n => n.Kind == "Page" && !n.Unavailable && sections.Contains(n.SectionId));
            return L("选定范围：", "Selected scope: ") + sections.Count + L(" 个分区 / ", " section(s) / ") +
                pages + L(" 个可访问页面", " accessible page(s)");
        }

        private void UpdateScopeSummary() => _scopeSummary.Text = ScopeDescription();

        private async void ComposerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || e.Shift || e.Alt || _question.IsComposing) return;
            e.SuppressKeyPress = true;
            await SubmitAsync(e.Control);
        }

        private async Task SubmitAsync(bool enqueue)
        {
            if (_closing || string.IsNullOrWhiteSpace(_question.Text)) return;
            if (_operation != null && (!_answerBusy || !enqueue))
            {
                Report(_answerBusy
                    ? L("正在回答。草稿已保留；按 Ctrl+Enter 加入队列。", "Answering. Draft kept; press Ctrl+Enter to queue it.")
                    : L("请等待当前操作结束，草稿已保留。", "Wait for the current operation; your draft is kept."));
                return;
            }
            if (!enqueue && _queue.Count > 0)
            {
                Report(L("请先继续或移除队列中的消息；Ctrl+Enter 可追加排队。", "Resume or remove queued messages first; Ctrl+Enter adds to the queue."));
                return;
            }
            if (!enqueue) _queuePaused = false;
            try
            {
                _queue.Add(new PendingQuestion { Text = _question.Text.Trim(), Page = CurrentPage(), Scope = ScopeDescription(),
                    Settings = SettingsManager.Snapshot(), Roots = _scope.CheckedRoots,
                    Servers = _external.Checked ? _servers.CheckedItems.Cast<McpServerOptions>().Select(s => s.Id).ToList() : new List<string>() });
            }
            catch (Exception ex)
            {
                Report(ex.Message);
                Logging.Logger.Warn("Cannot prepare Q&A message: " + ex.GetType().Name);
                return;
            }
            _question.Clear();
            UpdateQueue();
            await DrainQueueAsync();
        }

        private async Task DrainQueueAsync()
        {
            if (_draining || _operation != null || _queuePaused || _closing) return;
            _draining = true;
            _answerBusy = true;
            try
            {
                while (_queue.Count > 0 && !_queuePaused && !_closing)
                {
                    PendingQuestion pending = _queue[0];
                    _queue.RemoveAt(0);
                    UpdateQueue();
                    if (!await AskAsync(pending)) _queuePaused = true;
                }
            }
            finally
            {
                _answerBusy = false;
                _draining = false;
                _activeTurn = null;
                if (!IsDisposed)
                {
                    UpdateQueue();
                    SetBusy(false);
                    if (_closing) Close();
                }
            }
        }

        private ChatTurn AddTurn(string question, string scope)
        {
            var turn = new ChatTurn { Question = question, Scope = scope };
            _turns.Add(turn);
            _turnPicker.Items.Add(turn);
            _turnPicker.SelectedIndex = _turnPicker.Items.Count - 1;
            _dirty = true;
            return turn;
        }

        private async Task<bool> AskAsync(PendingQuestion pending)
        {
            ChatTurn turn = AddTurn(pending.Text, pending.Scope);
            _activeTurn = turn;
            int epoch = _viewEpoch;
            bool IsCurrent() => !IsDisposed && epoch == _viewEpoch && !turn.Finished;
            IProgress<string> text = new Progress<string>(part => { if (IsCurrent()) { turn.Text += part; _dirty = true; } });
            IProgress<string> activity = new Progress<string>(item => { if (IsCurrent()) Report(item); });
            IProgress<IReadOnlyList<EvidenceSource>> sources = new Progress<IReadOnlyList<EvidenceSource>>(items =>
            {
                if (!IsCurrent()) return;
                turn.Sources = items.ToList();
                if (_turnPicker.SelectedItem == turn) ShowSources(turn.Sources);
            });
            bool success = await RunAsync(async token =>
            {
                KnowledgeAnswer answer = await Task.Run(() => _conversation.AnswerAsync(pending.Text, pending.Page, pending.Roots, pending.Servers, pending.Settings,
                    part => text.Report(part), item => activity.Report(item), items => sources.Report(items),
                    (request, ct) => ApproveAsync(this, request, ct), token, _chat), token);
                turn.Text = answer.Text;
                turn.Sources = answer.Sources;
                turn.Notice = string.Join("\n", answer.Warnings.Distinct());
                if (_turnPicker.SelectedItem == turn) ShowSources(turn.Sources);
                _status.Text = L("回答完成。可以继续提问或复制回答。", "Answer complete. Ask a follow-up or copy the answer.");
            });
            turn.Finished = true;
            if (!IsDisposed) RenderConversation();
            return success;
        }

        private void CancelOperation()
        {
            _queuePaused = true;
            Report(L("正在取消；队列暂停，已发送的远程操作不保证回滚。", "Cancelling; queue paused. Sent remote operations are not rolled back."));
            _operation?.Cancel();
            UpdateSendButton();
            UpdateQueue();
        }

        private void UpdateQueue()
        {
            _queued.Items.Clear();
            foreach (PendingQuestion pending in _queue) _queued.Items.Add(pending);
            if (_queued.Items.Count > 0) _queued.SelectedIndex = 0;
            _queuePanel.Visible = _queue.Count > 0;
            _queueTitle.Text = (_queuePaused ? L("队列已暂停：", "Queue paused: ") : L("等待发送：", "Queued: ")) + _queue.Count;
            _resumeQueue.Enabled = _queue.Count > 0 && _operation == null && !_draining;
            _removeQueued.Enabled = _queue.Count > 0;
        }

        private async Task FindAsync()
        {
            if (_operation != null || string.IsNullOrWhiteSpace(_question.Text)) return;
            string query = _question.Text.Trim();
            List<string> roots = _scope.CheckedRoots;
            KnowledgeOptions options = SettingsManager.Current.Knowledge.Clone();
            ChatTurn turn = AddTurn(query, ScopeDescription());
            await RunAsync(async token =>
            {
                string page = CurrentPage();
                RetrievalResult result = await Task.Run(() => page == null ? _retrieval.SearchAsync(query, roots, options, token) :
                    Task.FromResult(_retrieval.CurrentPage(page, query, token, options.MaxRetrievedChunks)), token);
                List<EvidenceSource> sources = result.Chunks.Select(_conversation.Evidence.Add).ToList();
                turn.Sources = sources;
                ShowSources(sources);
                turn.Text = string.Join("\n\n", sources.Select(s => s + "\n" + s.Text));
                turn.Notice = L("仅检索，未调用聊天模型。", "Search only; no chat model call.") + "\n" + string.Join("\n", result.Warnings);
                _status.Text = L("仅检索，未调用聊天模型。片段：", "Search only, no chat model call. Passages: ") + sources.Count + "   " +
                    L("语义索引页：", "Indexed pages: ") + result.Coverage.Indexed + "/" + result.Coverage.Total;
            });
            turn.Finished = true;
            RenderConversation();
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

        private DataObject CopyData()
        {
            string markdown;
            if (_answer.SelectionLength > 0)
            {
                var selection = new DataObject();
                selection.SetData(DataFormats.UnicodeText, _answer.SelectedText);
                selection.SetData(DataFormats.Rtf, _answer.SelectedRtf);
                return selection;
            }
            ChatTurn turn = _turns.LastOrDefault();
            if (turn == null || string.IsNullOrWhiteSpace(turn.Text)) return null;
            markdown = turn.Text;
            if (!string.IsNullOrEmpty(turn.Notice)) markdown += "\n\n" + L("提示：", "Notice: ") + turn.Notice;
            if (!turn.Finished) markdown += "\n\n" + L("（回答尚未完成）", "(Answer not yet complete)");
            if (turn.Sources.Count > 0)
                markdown += "\n\n" + L("## 来源", "## Sources") + "\n" + string.Join("\n", turn.Sources.Select(s => s.ToString()));
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, markdown);
            data.SetData(DataFormats.Rtf, MarkdownRtf.Render(markdown, Theme.FontContent));
            return data;
        }

        private void CopyAnswer()
        {
            DataObject data = CopyData();
            if (data == null) return;
            try
            {
                Clipboard.SetDataObject(data, true, 5, 100);
                _status.Text = L("已复制，可自行粘贴到 OneNote 或其他应用。", "Copied. Paste into OneNote or another app.");
            }
            catch (ExternalException ex)
            {
                Report(L("复制失败，请稍后重试：", "Copy failed; try again: ") + ex.Message);
                Logging.Logger.Warn("Q&A clipboard failed: " + ex.GetType().Name);
            }
        }

        private void RenderConversation()
        {
            _dirty = false;
            var sections = new List<string>();
            if (_turns.Count == 0)
                sections.Add(L("## 从笔记开始对话\n\n提出问题、比较资料，或继续追问。\n回答优先依据选定范围的文档，来源随时可查看。",
                    "## Start with your notes\n\nAsk a question, compare information, or follow up.\nAnswers start with documents in your selected scope. Sources stay available."));
            foreach (ChatTurn turn in _turns)
            {
                sections.Add("### " + L("你", "You"));
                sections.Add(turn.Question);
                sections.Add("> " + turn.Scope);
                sections.Add("### " + Strings.BtnQA);
                sections.Add(string.IsNullOrEmpty(turn.Text) && !turn.Finished ? L("正在查阅与思考...", "Reading and thinking...") : turn.Text);
                if (!string.IsNullOrEmpty(turn.Notice)) sections.Add("> " + turn.Notice);
                if (turn.Sources.Count > 0)
                    sections.Add("> " + turn.Sources.Count + L(" 个来源，可在“来源”中查看", " sources available in Sources"));
                sections.Add("---");
            }
            int start = _answer.SelectionStart, length = _answer.SelectionLength;
            Point scroll = new Point();
            var info = new ScrollInfo { Size = Marshal.SizeOf<ScrollInfo>(), Mask = 7 };
            GetScrollInfo(_answer.Handle, 1, ref info);
            bool follow = length == 0 && info.Position + info.Page >= info.Maximum;
            SendMessage(_answer.Handle, 0x04DD, IntPtr.Zero, ref scroll);
            SendMessage(_answer.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try
            {
                _diagrams.Clear();
                _answer.Rtf = MarkdownRtf.RenderSections(sections, _answer.Font, MarkdownRtf.AvailableWidth(_answer),
                    SettingsManager.Current.Knowledge.EnableDiagramPreview ? _diagrams : null);
                int textLength = RichTextView.Length(_answer);
                if (follow) { _answer.Select(textLength, 0); _answer.ScrollToCaret(); }
                else
                {
                    _answer.Select(Math.Min(start, textLength), Math.Min(length, Math.Max(0, textLength - start)));
                    SendMessage(_answer.Handle, 0x04DE, IntPtr.Zero, ref scroll);
                }
            }
            finally { SendMessage(_answer.Handle, 0x000B, new IntPtr(1), IntPtr.Zero); _answer.Invalidate(); }
            _copy.Enabled = _answer.SelectionLength > 0 || !string.IsNullOrWhiteSpace(_turns.LastOrDefault()?.Text);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollInfo { public int Size, Mask, Minimum, Maximum, Page, Position, Track; }
        [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref Point point);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        protected override void OnDpiScaleChanged(EventArgs e)
        {
            base.OnDpiScaleChanged(e);
            RenderConversation();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SettingsManager.SettingsChanged -= SettingsChanged;
                _render.Dispose();
                _poll.Dispose();
                _menu.Dispose();
                _tips.Dispose();
                _brandImage?.Dispose();
                Icon?.Dispose();
            }
            base.Dispose(disposing);
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
            _showSources.Text = L("来源 ", "Sources ") + sources.Count;
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
            UpdateScopeSummary();
            _viewEpoch++;
            _conversation.Clear();
            if (_queue.Count > 0) Report(L("范围或对话已改变，已清空待发送队列。", "Scope or conversation changed; queued messages were cleared."));
            _queue.Clear();
            _queuePaused = false;
            _turns.Clear();
            _turnPicker.Items.Clear();
            _sources.Items.Clear();
            _showSources.Text = L("来源", "Sources");
            _copy.Enabled = false;
            UpdateQueue();
            RenderConversation();
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || _editingSettings) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => SettingsChanged(sender, e))); return; }
            if (_operation != null)
            {
                if (_queue.Count > 0) Report(L("设置已改变，已清空待发送队列。", "Settings changed; queued messages were cleared."));
                _queue.Clear();
                _queuePaused = true;
                UpdateQueue();
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
            _settings.Enabled = _update.Enabled = _fresh.Enabled = !busy;
            _mode.Enabled = !busy;
            _scope.Enabled = !busy && _mode.SelectedIndex == 1;
            _external.Enabled = !busy;
            _servers.Enabled = !busy && _external.Checked;
            _servers.Visible = _external.Checked && _servers.Items.Count > 0;
            _sources.Enabled = !busy;
            _find.Enabled = !busy && _mode.SelectedIndex != 2;
            foreach (ToolStripItem item in _menu.Items) if (item != _find) item.Enabled = !busy;
            UpdateSendButton();
            UpdateQueue();
        }

        private void UpdateSendButton()
        {
            bool busy = _operation != null;
            _send.Busy = busy;
            _send.AccessibleName = busy ? L("取消当前操作", "Cancel current operation") : L("发送消息", "Send message");
            _tips.SetToolTip(_send, _send.AccessibleName);
            _send.Enabled = !_closing && (busy ? !_operation.IsCancellationRequested : !string.IsNullOrWhiteSpace(_question.Text));
        }

        private async Task<bool> RunAsync(Func<CancellationToken, Task> action)
        {
            if (_operation != null) return false;
            _operation = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                await action(_operation.Token);
                _operation.Token.ThrowIfCancellationRequested();
                return !_pendingReset;
            }
            catch (OperationCanceledException)
            {
                string notice = L("已取消。队列暂停；已发送的远程操作不保证撤销。", "Cancelled. Queue paused; sent remote operations may still have taken effect.");
                if (_activeTurn != null) _activeTurn.Notice = notice;
                Report(notice);
                return false;
            }
            catch (Exception ex)
            {
                Report(ex.Message);
                if (_activeTurn != null) _activeTurn.Notice = L("回答失败，队列已暂停：", "Answer failed; queue paused: ") + ex.Message;
                Logging.Logger.Warn("Knowledge operation failed: " + ex.GetType().Name);
                if (!_closing && !_answerBusy) MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
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
                if (_closing && !_draining) Close();
            }
        }
    }
}
