using System;
using System.Drawing;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using OneNoteCopilot.AI;
using OneNoteCopilot.Settings;

namespace OneNoteCopilot.UI
{
    public class SettingsDialog : Form
    {
        // API + Model tab
        private readonly TextBox _txtApiKey;
        private readonly Button _btnToggleApiKey;
        private readonly TextBox _txtApiBaseUrl;
        private readonly Button _btnTestConnection;
        private readonly CheckBox _chkAutoSelectModel;
        private readonly ComboBox _cmbDefaultModel;
        private readonly NumericUpDown _numTemperature;
        private readonly NumericUpDown _numMaxTokens;

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

        public SettingsDialog()
        {
            Text = "OneNote Copilot 设置";
            ClientSize = new Size(560, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            TabControl tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(536, 450)
            };

            // ── Tab 1: API + Model ─────────────────────────────────────
            TabPage tabApi = new TabPage("API 与模型");

            GroupBox grpApi = new GroupBox
            {
                Text = "API 设置",
                Location = new Point(10, 10),
                Size = new Size(510, 145)
            };

            Label lblApiKey = new Label { Text = "API 密钥:", AutoSize = true, Location = new Point(16, 32) };

            _txtApiKey = new TextBox
            {
                Location = new Point(90, 28),
                Size = new Size(320, 23),
                UseSystemPasswordChar = true
            };

            _btnToggleApiKey = new Button
            {
                Text = "显示",
                Location = new Point(418, 27),
                Size = new Size(70, 26)
            };
            _btnToggleApiKey.Click += OnToggleApiKeyClick;

            Label lblApiBaseUrl = new Label { Text = "API 地址:", AutoSize = true, Location = new Point(16, 69) };

            _txtApiBaseUrl = new TextBox
            {
                Location = new Point(90, 65),
                Size = new Size(398, 23),
                Text = "https://api.deepseek.com"
            };

            _btnTestConnection = new Button
            {
                Text = "测试连接",
                Location = new Point(388, 103),
                Size = new Size(100, 28)
            };
            _btnTestConnection.Click += async (sender, args) => await TestConnectionAsync();

            grpApi.Controls.Add(lblApiKey);
            grpApi.Controls.Add(_txtApiKey);
            grpApi.Controls.Add(_btnToggleApiKey);
            grpApi.Controls.Add(lblApiBaseUrl);
            grpApi.Controls.Add(_txtApiBaseUrl);
            grpApi.Controls.Add(_btnTestConnection);

            GroupBox grpModel = new GroupBox
            {
                Text = "模型设置",
                Location = new Point(10, 165),
                Size = new Size(510, 155)
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
                Size = new Size(180, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
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
                Text = "可为每个功能自定义 system prompt。留空则使用内置默认值。修改后立即生效（无需重启）。"
            };

            TabControl promptTabs = new TabControl
            {
                Location = new Point(10, 44),
                Size = new Size(510, 366)
            };

            _txtPromptSummarize = BuildPromptEditor(promptTabs, "摘要", () => PromptTemplates.SummarizeSystemDefault);
            _txtPromptGenerate = BuildPromptEditor(promptTabs, "生成", () => PromptTemplates.GenerateSystemDefault);
            _txtPromptRewrite = BuildPromptEditor(promptTabs, "改写", () => PromptTemplates.RewriteSystemDefault);
            _txtPromptQA = BuildPromptEditor(promptTabs, "问答", () => PromptTemplates.QASystemDefault);
            _txtPromptExtractTodos = BuildPromptEditor(promptTabs, "提取待办", () => PromptTemplates.ExtractTodosSystemDefault);

            tabPrompts.Controls.Add(lblPromptHint);
            tabPrompts.Controls.Add(promptTabs);

            tabs.TabPages.Add(tabApi);
            tabs.TabPages.Add(tabPrompts);

            // ── Footer buttons ──
            _btnOk = new Button
            {
                Text = "确定",
                Size = new Size(90, 30),
                Location = new Point(362, 475),
                DialogResult = DialogResult.None
            };
            _btnOk.Click += OnOkClick;

            _btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(90, 30),
                Location = new Point(458, 475),
                DialogResult = DialogResult.Cancel
            };

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
            AppSettings settings = SettingsManager.Current;

            _txtApiKey.Text = SettingsManager.GetApiKey();
            _txtApiBaseUrl.Text = settings.ApiBaseUrl ?? "https://api.deepseek.com";
            _chkAutoSelectModel.Checked = settings.AutoSelectModel;

            if (!string.IsNullOrWhiteSpace(settings.DefaultModel) && _cmbDefaultModel.Items.Contains(settings.DefaultModel))
            {
                _cmbDefaultModel.SelectedItem = settings.DefaultModel;
            }
            else
            {
                _cmbDefaultModel.SelectedItem = "deepseek-chat";
            }

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
            string defaultModel = _cmbDefaultModel.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                MessageBox.Show("请输入 API 地址。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiBaseUrl.Focus();
                return;
            }

            if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri _))
            {
                MessageBox.Show("API 地址格式无效。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiBaseUrl.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(defaultModel))
            {
                MessageBox.Show("请选择默认模型。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _cmbDefaultModel.Focus();
                return;
            }

            AppSettings settings = SettingsManager.Current;
            settings.ApiBaseUrl = apiBaseUrl;
            settings.DefaultModel = defaultModel;
            settings.AutoSelectModel = _chkAutoSelectModel.Checked;
            settings.Temperature = Convert.ToDouble(_numTemperature.Value);
            settings.MaxTokens = Decimal.ToInt32(_numMaxTokens.Value);
            settings.Language = "zh-CN";

            if (settings.PromptOverrides == null) settings.PromptOverrides = new PromptOverrides();
            // Only persist as an override if it differs from the built-in default
            // (so resetting goes back to following any future default changes).
            settings.PromptOverrides.Summarize = NormalizeOverride(_txtPromptSummarize.Text, PromptTemplates.SummarizeSystemDefault);
            settings.PromptOverrides.Generate = NormalizeOverride(_txtPromptGenerate.Text, PromptTemplates.GenerateSystemDefault);
            settings.PromptOverrides.Rewrite = NormalizeOverride(_txtPromptRewrite.Text, PromptTemplates.RewriteSystemDefault);
            settings.PromptOverrides.QA = NormalizeOverride(_txtPromptQA.Text, PromptTemplates.QASystemDefault);
            settings.PromptOverrides.ExtractTodos = NormalizeOverride(_txtPromptExtractTodos.Text, PromptTemplates.ExtractTodosSystemDefault);

            SettingsManager.Save();
            SettingsManager.SetApiKey(apiKey);

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
            string model = _cmbDefaultModel.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("请先输入 API Key。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtApiKey.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(apiBaseUrl) || !Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri baseUri))
            {
                MessageBox.Show("请输入有效的 API 地址。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = baseUri;
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    object payload = new
                    {
                        model = model,
                        messages = new[]
                        {
                            new { role = "user", content = "Hi" }
                        },
                        max_tokens = 5,
                        temperature = 0.0
                    };

                    string json = JsonConvert.SerializeObject(payload);
                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        HttpResponseMessage response = await client.PostAsync("/v1/chat/completions", content).ConfigureAwait(true);
                        string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

                        if (response.IsSuccessStatusCode)
                        {
                            MessageBox.Show("连接测试成功。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(
                                "连接测试失败：" + response.StatusCode + Environment.NewLine + responseText,
                                "OneNote Copilot",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("连接测试失败：" + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnTestConnection.Enabled = true;
            }
        }
    }
}
