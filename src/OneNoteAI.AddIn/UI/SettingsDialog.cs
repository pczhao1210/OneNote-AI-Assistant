using System;
using System.Drawing;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using OneNoteAI.AI;
using OneNoteAI.Settings;

namespace OneNoteAI.UI
{
    public class SettingsDialog : DpiAwareForm
    {
        private readonly AppSettings _draft = SettingsManager.Snapshot();
        // API + Model tab
        private readonly TextBox _txtApiKey;
        private readonly ComboBox _cmbProvider;
        private readonly Button _btnToggleApiKey;
        private readonly TextBox _txtApiBaseUrl;
        private readonly Button _btnTestConnection;
        private readonly CheckBox _chkAutoSelectModel;
        private readonly ComboBox _cmbDefaultModel;
        private readonly NumericUpDown _numTemperature;
        private readonly NumericUpDown _numMaxTokens;
        private readonly ComboBox _cmbTokenLimit;
        private readonly ComboBox _cmbTemperatureMode;

        // Prompt tab
        private readonly TextBox _txtPromptSummarize;
        private readonly TextBox _txtPromptGenerate;
        private readonly TextBox _txtPromptRewrite;
        private readonly TextBox _txtPromptQA;
        private readonly TextBox _txtPromptExtractTodos;

        // Footer
        private readonly Button _btnOk;
        private readonly Button _btnCancel;

        private bool _showApiKey;

        private sealed class ProviderListItem
        {
            public AiProvider Provider { get; private set; }
            private readonly string _name;
            public ProviderListItem(AiProvider provider, string name) { Provider = provider; _name = name; }
            public override string ToString() { return _name; }
        }

        public SettingsDialog()
        {
            Text = "OneNote AI Assistant " + (Strings.IsChinese ? "设置" : "Settings");
            ClientSize = new Size(580, 570);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Theme.ApplyTo(this);

            // ── Header ──
            Panel header = Theme.CreateHeader(Strings.IsChinese ? "设置" : "Settings", 44);
            Controls.Add(header);

            TabControl tabs = new TabControl
            {
                Location = new Point(16, 56),
                Size = new Size(548, 450)
            };

            // ── Tab 1: API + Model ─────────────────────────────────────
            TabPage tabApi = new TabPage("API 与模型");

            GroupBox grpApi = new GroupBox
            {
                Text = "API 设置",
                Location = new Point(10, 10),
                Size = new Size(510, 180)
            };

            Label lblProvider = new Label { Text = "Provider:", AutoSize = true, Location = new Point(16, 32) };
            _cmbProvider = new ComboBox { Location = new Point(90, 28), Size = new Size(200, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (AiProvider provider in Enum.GetValues(typeof(AiProvider)))
                _cmbProvider.Items.Add(new ProviderListItem(provider, GetProviderDisplayName(provider)));
            _cmbProvider.SelectedIndexChanged += OnProviderChanged;

            Label lblApiKey = new Label { Text = "API 密钥:", AutoSize = true, Location = new Point(16, 32) };

            _txtApiKey = new TextBox
            {
                Location = new Point(90, 65),
                Size = new Size(320, 23),
                UseSystemPasswordChar = true
            };
            lblApiKey.Location = new Point(16, 69);

            _btnToggleApiKey = Theme.CreateSecondaryButton(Strings.IsChinese ? "显示" : "Show");
            _btnToggleApiKey.Location = new Point(418, 64);
            _btnToggleApiKey.Size = new Size(70, 26);
            _btnToggleApiKey.Click += OnToggleApiKeyClick;

            Label lblApiBaseUrl = new Label { Text = "API 地址:", AutoSize = true, Location = new Point(16, 69) };

            _txtApiBaseUrl = new TextBox
            {
                Location = new Point(90, 102),
                Size = new Size(398, 23),
                Text = "https://api.deepseek.com"
            };
            lblApiBaseUrl.Location = new Point(16, 106);

            _btnTestConnection = Theme.CreateSecondaryButton(Strings.IsChinese ? "测试连接" : "Test");
            _btnTestConnection.Location = new Point(388, 140);
            _btnTestConnection.Size = new Size(100, 28);
            _btnTestConnection.Click += async (sender, args) => await TestConnectionAsync();

            grpApi.Controls.Add(lblProvider);
            grpApi.Controls.Add(_cmbProvider);
            grpApi.Controls.Add(lblApiKey);
            grpApi.Controls.Add(_txtApiKey);
            grpApi.Controls.Add(_btnToggleApiKey);
            grpApi.Controls.Add(lblApiBaseUrl);
            grpApi.Controls.Add(_txtApiBaseUrl);
            grpApi.Controls.Add(_btnTestConnection);
            grpApi.Controls.Add(new Label { Location = new Point(16, 139), Size = new Size(362, 32),
                Text = KnowledgeUi.L("模型名可直接输入；兼容设置按服务商保存。", "Model ID is editable; settings are per provider.") });

            GroupBox grpModel = new GroupBox
            {
                Text = "模型设置",
                Location = new Point(10, 200),
                Size = new Size(510, 210)
            };

            _chkAutoSelectModel = new CheckBox
            {
                Text = "自动选择模型",
                AutoSize = true,
                Location = new Point(19, 31),
                Checked = true
            };

            Label lblDefaultModel = new Label { Text = "默认模型:", AutoSize = true, Location = new Point(16, 66) };

            _cmbDefaultModel = new ComboBox
            {
                Location = new Point(90, 62),
                Size = new Size(398, 23),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            _cmbDefaultModel.Items.Add("deepseek-chat");
            _cmbDefaultModel.Items.Add("deepseek-reasoner");
            _cmbDefaultModel.SelectedIndex = 0;

            Label lblTemperature = new Label { Text = "温度:", AutoSize = true, Location = new Point(16, 101) };

            _numTemperature = new NumericUpDown
            {
                Location = new Point(110, 97),
                Size = new Size(80, 23),
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.0M,
                Maximum = 2.0M,
                Value = 0.7M
            };

            Label lblMaxTokens = new Label { Text = "最大 Token 数:", AutoSize = true, Location = new Point(220, 101) };

            _numMaxTokens = new NumericUpDown
            {
                Location = new Point(314, 97),
                Size = new Size(94, 23),
                Increment = 512,
                Minimum = 512,
                Maximum = 8192,
                Value = 4096
            };

            grpModel.Controls.Add(_chkAutoSelectModel);
            grpModel.Controls.Add(lblDefaultModel);
            grpModel.Controls.Add(_cmbDefaultModel);
            grpModel.Controls.Add(lblTemperature);
            grpModel.Controls.Add(_numTemperature);
            grpModel.Controls.Add(lblMaxTokens);
            grpModel.Controls.Add(_numMaxTokens);
            _cmbTokenLimit = new ComboBox { Location = new Point(160, 132), Size = new Size(328, 23),
                DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTokenLimit.Items.AddRange(new object[] { KnowledgeUi.L("自动（推荐）", "Auto (recommended)"), "max_tokens", "max_completion_tokens" });
            _cmbTemperatureMode = new ComboBox { Location = new Point(160, 167), Size = new Size(328, 23),
                DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTemperatureMode.Items.AddRange(new object[] { KnowledgeUi.L("自动（推荐）", "Auto (recommended)"),
                KnowledgeUi.L("发送设置的温度", "Send configured temperature"), KnowledgeUi.L("不发送（模型默认）", "Omit (model default)") });
            grpModel.Controls.Add(new Label { Text = KnowledgeUi.L("输出长度参数:", "Output limit field:"), AutoSize = true, Location = new Point(16, 136) });
            grpModel.Controls.Add(_cmbTokenLimit);
            grpModel.Controls.Add(new Label { Text = KnowledgeUi.L("温度参数:", "Temperature field:"), AutoSize = true, Location = new Point(16, 171) });
            grpModel.Controls.Add(_cmbTemperatureMode);

            tabApi.Controls.Add(grpApi);
            tabApi.Controls.Add(grpModel);

            // ── Tab 2: Prompt templates ────────────────────────────────
            TabPage tabPrompts = new TabPage("Prompt 模板");

            Label lblPromptHint = new Label
            {
                AutoSize = false,
                Location = new Point(10, 8),
                Size = new Size(510, 32),
                ForeColor = Color.FromArgb(90, 100, 115),
                Text = "留空使用默认 Prompt，保存后生效。问答自定义样式不替代文档优先、来源与按需 MCP 规则。"
            };

            TabControl promptTabs = new TabControl
            {
                Location = new Point(10, 44),
                Size = new Size(510, 366)
            };

            _txtPromptSummarize = BuildPromptEditor(promptTabs, "摘要", () => PromptTemplates.SummarizeSystemDefault);
            _txtPromptGenerate = BuildPromptEditor(promptTabs, "生成", () => PromptTemplates.GenerateSystemDefault);
            _txtPromptRewrite = BuildPromptEditor(promptTabs, "改写", () => PromptTemplates.RewriteSystemDefault);
            _txtPromptQA = BuildPromptEditor(promptTabs, Strings.BtnQA, () => PromptTemplates.QASystemDefault);
            _txtPromptExtractTodos = BuildPromptEditor(promptTabs, "提取待办", () => PromptTemplates.ExtractTodosSystemDefault);

            tabPrompts.Controls.Add(lblPromptHint);
            tabPrompts.Controls.Add(promptTabs);

            tabs.TabPages.Add(tabApi);
            tabs.TabPages.Add(tabPrompts);
            var tabKnowledge = new TabPage(KnowledgeUi.L("知识与 MCP", "Knowledge and MCP"));
            var knowledgeInfo = new Label { Dock = DockStyle.Top, Height = 90, Padding = new Padding(16),
                Text = KnowledgeUi.L("配置云端 Embedding、本地索引授权范围，以及远程 HTTPS MCP 连接和逐工具审批。聊天 API Key 不会自动复用到这些服务。",
                    "Configure cloud Embeddings, local indexing consent, remote HTTPS MCP connections and per-tool approval. Your chat API key is not reused for these services.") };
            Button knowledgeSettings = Theme.CreatePrimaryButton(KnowledgeUi.L("打开知识设置", "Open knowledge settings"));
            knowledgeSettings.SetBounds(16, 106, 230, 38);
            knowledgeSettings.Click += async (s, e) =>
            {
                knowledgeSettings.Enabled = false;
                try
                {
                    var source = new Knowledge.OneNoteSource();
                    Knowledge.NoteNode hierarchy = await Task.Run(() => source.Hierarchy());
                    using (var dialog = new KnowledgeSettingsDialog(hierarchy)) dialog.ShowDialog(this);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { if (!IsDisposed) knowledgeSettings.Enabled = true; }
            };
            tabKnowledge.Controls.Add(knowledgeSettings);
            tabKnowledge.Controls.Add(knowledgeInfo);
            tabs.TabPages.Add(tabKnowledge);

            // ── Footer buttons ──
            _btnOk = Theme.CreatePrimaryButton(Strings.OK);
            _btnOk.Size = new Size(96, 34);
            _btnOk.Location = new Point(376, 518);
            _btnOk.DialogResult = DialogResult.None;
            _btnOk.Click += OnOkClick;

            _btnCancel = Theme.CreateSecondaryButton(Strings.Cancel);
            _btnCancel.Size = new Size(96, 34);
            _btnCancel.Location = new Point(480, 518);
            _btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(tabs);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Load += OnDialogLoad;
        }

        /// <summary>
        /// Build one prompt-editor sub-tab: a multiline textbox + a "恢复默认" button.
        /// </summary>
        private static TextBox BuildPromptEditor(TabControl parent, string title, Func<string> defaultProvider)
        {
            TabPage page = new TabPage(title);

            TextBox txt = new TextBox
            {
                Location = new Point(8, 8),
                Size = new Size(486, 270),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                WordWrap = true,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
            };

            Label lblDefault = new Label
            {
                AutoSize = false,
                Location = new Point(8, 286),
                Size = new Size(486, 18),
                ForeColor = Color.FromArgb(120, 128, 140),
                Text = "默认值：" + Truncate(defaultProvider(), 110)
            };

            Button btnReset = new Button
            {
                Text = "恢复默认",
                Size = new Size(110, 28),
                Location = new Point(384, 308)
            };
            btnReset.Click += delegate { txt.Text = defaultProvider(); };

            page.Controls.Add(txt);
            page.Controls.Add(lblDefault);
            page.Controls.Add(btnReset);
            parent.TabPages.Add(page);
            return txt;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? string.Empty;
            return s.Substring(0, max) + "...";
        }

        private void OnDialogLoad(object sender, EventArgs e)
        {
            AppSettings settings = _draft;
            SelectProvider(settings.Provider);

            _txtApiKey.Text = KnowledgeUi.ReadSecret(settings.ApiKey, this);
            _txtApiBaseUrl.Text = settings.ApiBaseUrl ?? "https://api.deepseek.com";
            _chkAutoSelectModel.Checked = settings.AutoSelectModel;

            AddRecommendedModels(settings.Provider);
            _cmbDefaultModel.Text = string.IsNullOrWhiteSpace(settings.DefaultModel) ? settings.GetDefaultModelForProvider() : settings.DefaultModel;

            decimal temperature = Convert.ToDecimal(settings.Temperature);
            if (temperature < _numTemperature.Minimum || temperature > _numTemperature.Maximum)
            {
                temperature = 0.7M;
            }
            _numTemperature.Value = temperature;

            decimal maxTokens = settings.MaxTokens;
            if (maxTokens < _numMaxTokens.Minimum || maxTokens > _numMaxTokens.Maximum)
            {
                maxTokens = 4096;
            }
            _numMaxTokens.Value = maxTokens;
            LoadCompatibility();

            // Show current effective prompt (override if set, else default) so
            // the user sees what's actually being used; saving will only persist
            // an override if the text differs from the default.
            PromptOverrides po = settings.PromptOverrides ?? new PromptOverrides();
            _txtPromptSummarize.Text = string.IsNullOrWhiteSpace(po.Summarize) ? PromptTemplates.SummarizeSystemDefault : po.Summarize;
            _txtPromptGenerate.Text = string.IsNullOrWhiteSpace(po.Generate) ? PromptTemplates.GenerateSystemDefault : po.Generate;
            _txtPromptRewrite.Text = string.IsNullOrWhiteSpace(po.Rewrite) ? PromptTemplates.RewriteSystemDefault : po.Rewrite;
            _txtPromptQA.Text = string.IsNullOrWhiteSpace(po.QA) ? PromptTemplates.QASystemDefault : po.QA;
            _txtPromptExtractTodos.Text = string.IsNullOrWhiteSpace(po.ExtractTodos) ? PromptTemplates.ExtractTodosSystemDefault : po.ExtractTodos;
        }

        private void OnToggleApiKeyClick(object sender, EventArgs e)
        {
            _showApiKey = !_showApiKey;
            _txtApiKey.UseSystemPasswordChar = !_showApiKey;
            _btnToggleApiKey.Text = _showApiKey ? "隐藏" : "显示";
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            string apiBaseUrl = (_txtApiBaseUrl.Text ?? string.Empty).Trim();
            string apiKey = (_txtApiKey.Text ?? string.Empty).Trim();
            string defaultModel = (_cmbDefaultModel.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                MessageBox.Show("请输入 API 地址。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiBaseUrl.Focus();
                return;
            }

            if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri _))
            {
                MessageBox.Show("API 地址格式无效。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiBaseUrl.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(defaultModel))
            {
                MessageBox.Show("请选择默认模型。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _cmbDefaultModel.Focus();
                return;
            }

            AppSettings settings = _draft;
            settings.ApiBaseUrl = apiBaseUrl;
            settings.DefaultModel = defaultModel;
            settings.AutoSelectModel = _chkAutoSelectModel.Checked;
            settings.Temperature = Convert.ToDouble(_numTemperature.Value);
            settings.MaxTokens = Decimal.ToInt32(_numMaxTokens.Value);
            SettingsManager.GetActiveProviderSettings(settings).ChatApi = ReadCompatibility();

            if (settings.PromptOverrides == null) settings.PromptOverrides = new PromptOverrides();
            // Only persist as an override if it differs from the built-in default
            // (so resetting goes back to following any future default changes).
            settings.PromptOverrides.Summarize = NormalizeOverride(_txtPromptSummarize.Text, PromptTemplates.SummarizeSystemDefault);
            settings.PromptOverrides.Generate = NormalizeOverride(_txtPromptGenerate.Text, PromptTemplates.GenerateSystemDefault);
            settings.PromptOverrides.Rewrite = NormalizeOverride(_txtPromptRewrite.Text, PromptTemplates.RewriteSystemDefault);
            settings.PromptOverrides.QA = NormalizeOverride(_txtPromptQA.Text, PromptTemplates.QASystemDefault);
            settings.PromptOverrides.ExtractTodos = NormalizeOverride(_txtPromptExtractTodos.Text, PromptTemplates.ExtractTodosSystemDefault);

            settings.ApiKey = EncryptionHelper.Encrypt(apiKey);
            settings.Knowledge = SettingsManager.Current.Knowledge;
            SettingsManager.Save(settings);

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string NormalizeOverride(string text, string defaultText)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string trimmed = text.Trim();
            return trimmed == (defaultText ?? string.Empty).Trim() ? null : trimmed;
        }

        private async Task TestConnectionAsync()
        {
            string apiKey = (_txtApiKey.Text ?? string.Empty).Trim();
            string apiBaseUrl = (_txtApiBaseUrl.Text ?? string.Empty).Trim();
            string model = (_cmbDefaultModel.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(apiKey) && _draft.Provider != AiProvider.Ollama)
            {
                MessageBox.Show("请先输入 API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiKey.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(apiBaseUrl) || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri baseUri))
            {
                MessageBox.Show("请输入有效的 API 地址。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiBaseUrl.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                model = "deepseek-chat";
            }

            _btnTestConnection.Enabled = false;
            try
            {
                using (DeepseekClient client = new DeepseekClient(apiKey, apiBaseUrl, _draft.Provider, requestOptions: ReadCompatibility()))
                {
                    await client.SendAsync(new OneNoteAI.AI.Models.ChatRequest
                    {
                        Model = model,
                        MaxTokens = 512,
                        Temperature = 0.0,
                        Messages = new System.Collections.Generic.List<OneNoteAI.AI.Models.ChatMessage>
                        {
                            OneNoteAI.AI.Models.ChatMessage.User("Hi")
                        }
                    });
                }
                MessageBox.Show("Connection test succeeded.", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connection test failed: " + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnTestConnection.Enabled = true;
            }
        }

        private void OnProviderChanged(object sender, EventArgs e)
        {
            ProviderListItem selected = _cmbProvider.SelectedItem as ProviderListItem;
            if (!IsHandleCreated || selected == null) return;
            AiProvider provider = selected.Provider;
            if (provider == _draft.Provider) return;
            PersistActiveProviderDraft();
            SettingsManager.SwitchProvider(provider, _draft);
            AppSettings settings = _draft;
            _txtApiKey.Text = KnowledgeUi.ReadSecret(settings.ApiKey, this);
            _txtApiBaseUrl.Text = settings.ApiBaseUrl;
            AddRecommendedModels(provider);
            _cmbDefaultModel.Text = settings.DefaultModel;
            LoadCompatibility();
        }

        private void AddRecommendedModels(AiProvider provider)
        {
            _cmbDefaultModel.Items.Clear();
            switch (provider)
            {
                case AiProvider.DeepSeek: _cmbDefaultModel.Items.AddRange(new object[] { "deepseek-chat", "deepseek-reasoner" }); break;
                case AiProvider.OpenAI: _cmbDefaultModel.Items.AddRange(new object[] { "gpt-4.1-mini", "gpt-4.1", "gpt-4o-mini" }); break;
                case AiProvider.Qwen: _cmbDefaultModel.Items.AddRange(new object[] { "qwen-plus", "qwen-turbo", "qwen-max" }); break;
                case AiProvider.Zhipu: _cmbDefaultModel.Items.AddRange(new object[] { "glm-4.5-air", "glm-4.5" }); break;
                case AiProvider.Moonshot: _cmbDefaultModel.Items.Add("moonshot-v1-8k"); break;
                case AiProvider.MiniMax: _cmbDefaultModel.Items.Add("MiniMax-Text-01"); break;
                case AiProvider.Gemini: _cmbDefaultModel.Items.AddRange(new object[] { "gemini-2.5-flash", "gemini-2.5-pro" }); break;
                case AiProvider.Claude: _cmbDefaultModel.Items.AddRange(new object[] { "claude-sonnet-4-20250514", "claude-opus-4-20250514" }); break;
                case AiProvider.Ollama: _cmbDefaultModel.Items.AddRange(new object[] { "qwen2.5:7b", "llama3.1:8b" }); break;
                case AiProvider.OpenRouter: _cmbDefaultModel.Items.Add("openai/gpt-4.1-mini"); break;
            }
        }

        private void PersistActiveProviderDraft()
        {
            AppSettings settings = _draft;
            string plainKey = (_txtApiKey.Text ?? string.Empty).Trim();
            settings.ApiKey = EncryptionHelper.Encrypt(plainKey);
            settings.ApiBaseUrl = (_txtApiBaseUrl.Text ?? string.Empty).Trim();
            settings.DefaultModel = (_cmbDefaultModel.Text ?? string.Empty).Trim();
            ProviderSettings profile = SettingsManager.GetActiveProviderSettings(settings);
            profile.ApiKey = settings.ApiKey;
            profile.ApiBaseUrl = settings.ApiBaseUrl;
            profile.DefaultModel = settings.DefaultModel;
            profile.ChatApi = ReadCompatibility();
        }

        private ChatApiOptions ReadCompatibility() => new ChatApiOptions
        {
            TokenLimit = (TokenLimitParameter)Math.Max(0, _cmbTokenLimit.SelectedIndex),
            Temperature = (TemperatureParameter)Math.Max(0, _cmbTemperatureMode.SelectedIndex)
        };

        private void LoadCompatibility()
        {
            ChatApiOptions options = _draft.GetChatApiOptions();
            _cmbTokenLimit.SelectedIndex = (int)options.TokenLimit;
            _cmbTemperatureMode.SelectedIndex = (int)options.Temperature;
            _cmbTokenLimit.Enabled = _draft.Provider != AiProvider.Claude;
        }

        private void SelectProvider(AiProvider provider)
        {
            foreach (object item in _cmbProvider.Items)
            {
                ProviderListItem candidate = item as ProviderListItem;
                if (candidate != null && candidate.Provider == provider)
                {
                    _cmbProvider.SelectedItem = candidate;
                    return;
                }
            }
        }

        private static string GetProviderDisplayName(AiProvider provider)
        {
            if (!Strings.IsChinese) return provider.ToString();
            switch (provider)
            {
                case AiProvider.DeepSeek: return "DeepSeek（深度求索）";
                case AiProvider.OpenAI: return "OpenAI";
                case AiProvider.Qwen: return "通义千问（Qwen）";
                case AiProvider.Zhipu: return "智谱 GLM";
                case AiProvider.Moonshot: return "Kimi（月之暗面）";
                case AiProvider.MiniMax: return "MiniMax";
                case AiProvider.Gemini: return "Google Gemini";
                case AiProvider.Claude: return "Anthropic Claude";
                case AiProvider.Ollama: return "Ollama（本地模型）";
                case AiProvider.OpenRouter: return "OpenRouter";
                case AiProvider.Custom: return "自定义兼容接口";
                default: return provider.ToString();
            }
        }
    }
}
