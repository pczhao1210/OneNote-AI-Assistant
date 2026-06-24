using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OneNoteAI.AI;
using OneNoteAI.AI.Models;
using OneNoteAI.Logging;
using OneNoteAI.OneNote;
using OneNoteAI.OneNote.Models;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Features
{
    /// <summary>
    /// Template-based generation command — lets users pick from predefined
    /// templates (meeting notes, book notes, weekly report, etc.) and
    /// generates structured content based on a brief user input.
    /// </summary>
    public static class TemplateCommand
    {
        // ── Template definitions ──

        private static readonly TemplateInfo[] Templates = new[]
        {
            new TemplateInfo(
                "会议纪要",
                "请根据以下会议信息，生成一份结构化的会议纪要。格式包括：\n" +
                "## 会议基本信息\n（日期、参会人、主题）\n" +
                "## 讨论要点\n（按议题编号列出）\n" +
                "## 决议事项\n（明确责任人和截止日期）\n" +
                "## 待办跟进\n（☐ 格式列出行动项）\n\n" +
                "用户提供的信息：\n{0}",
                "输入会议主题、参会人、讨论内容要点..."),

            new TemplateInfo(
                "读书笔记",
                "请根据以下信息，生成一份结构化的读书笔记。格式包括：\n" +
                "## 书籍信息\n（书名、作者、类型）\n" +
                "## 核心观点\n（3-5个关键观点）\n" +
                "## 精彩摘录\n（值得记录的句子或段落）\n" +
                "## 个人感悟\n（阅读收获和思考）\n" +
                "## 行动计划\n（如何应用到实际中）\n\n" +
                "用户提供的信息：\n{0}",
                "输入书名、作者、关键观点或阅读感受..."),

            new TemplateInfo(
                "周报",
                "请根据以下信息，生成一份工作周报。格式包括：\n" +
                "## 本周完成\n（按项目/任务列出完成的工作）\n" +
                "## 进行中\n（当前正在推进的事项及进度）\n" +
                "## 遇到的问题\n（阻碍和挑战）\n" +
                "## 下周计划\n（下周重点工作安排）\n" +
                "## 需要协助\n（需要上级或同事支持的事项）\n\n" +
                "用户提供的信息：\n{0}",
                "输入本周主要工作、进展、问题..."),

            new TemplateInfo(
                "学习笔记",
                "请根据以下学习内容，生成一份结构化的学习笔记。格式包括：\n" +
                "## 学习主题\n## 核心概念\n（关键定义和概念解释）\n" +
                "## 知识框架\n（逻辑关系和层次结构）\n" +
                "## 重点难点\n（需要反复理解的部分）\n" +
                "## 练习与应用\n（实践方向）\n" +
                "## 关联知识\n（与已有知识的联系）\n\n" +
                "用户提供的信息：\n{0}",
                "输入学习主题、知识点、难点..."),

            new TemplateInfo(
                "项目计划",
                "请根据以下信息，生成一份项目计划文档。格式包括：\n" +
                "## 项目概述\n（目标、背景、范围）\n" +
                "## 里程碑\n（关键节点和时间线）\n" +
                "## 任务分解\n（WBS，按阶段列出子任务）\n" +
                "## 资源需求\n（人力、技术、工具）\n" +
                "## 风险评估\n（潜在风险和应对策略）\n" +
                "## 验收标准\n（如何判定完成）\n\n" +
                "用户提供的信息：\n{0}",
                "输入项目名称、目标、时间线、团队..."),

            new TemplateInfo(
                "头脑风暴",
                "请根据以下主题进行头脑风暴，生成创意和想法。格式包括：\n" +
                "## 主题\n## 核心创意\n（5-10个主要想法）\n" +
                "## 延伸思考\n（每个创意的变体和扩展）\n" +
                "## 可行性分析\n（标注高/中/低可行性）\n" +
                "## 推荐方案\n（最值得深入的 2-3 个方向）\n\n" +
                "用户提供的信息：\n{0}",
                "输入要头脑风暴的主题或问题...")
        };

        private const string TemplateSystemPrompt =
            "你是一个专业的内容生成助手。请严格按照指定的模板格式生成内容。" +
            "内容要结构清晰、专业准确、有实用价值。使用中文输出。";

        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Step 1: Let user choose a template
            int templateIndex = ChooseTemplate();
            if (templateIndex < 0)
            {
                return;
            }

            TemplateInfo selected = Templates[templateIndex];

            // Step 2: Get user input for the template
            string userInput;
            using (PromptDialog inputDialog = new PromptDialog(
                string.Format("模板生成 - {0}", selected.Name),
                string.Format("请提供「{0}」所需的信息：", selected.Name),
                selected.Placeholder))
            {
                if (inputDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }
                userInput = inputDialog.UserInput;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                Msg.Show("请输入内容信息。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Step 3: Get current page context (optional)
            OneNoteProvider provider;
            PageContent page;
            string existingContent = null;

            try
            {
                provider = new OneNoteProvider();
                page = provider.GetCurrentPage();
                existingContent = page.GetPlainText();
            }
            catch (Exception ex)
            {
                Logger.Error("获取当前页面失败", ex);
                Msg.Show("获取当前页面失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string pageId = page.PageId;
            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();

            // Build prompt from template
            string userPrompt = string.Format(selected.PromptTemplate, userInput);

            // If the page already has content, include as reference
            if (!string.IsNullOrWhiteSpace(existingContent) && existingContent.Length > 50)
            {
                string context = existingContent.Length > 2000
                    ? existingContent.Substring(0, 2000) + "\n...(截断)"
                    : existingContent;
                userPrompt += string.Format("\n\n---\n参考当前页面已有内容：\n{0}", context);
            }

            int tokens = TokenEstimator.Estimate(userPrompt);
            string model = settings.AutoSelectModel
                ? DeepseekClient.SelectModel("generate", tokens)
                : settings.DefaultModel;

            ResultDialog resultDialog = null;
            Func<Task> runOnce = async delegate
            {
                ProgressOverlay localProgress = ProgressOverlay.Show(null);
                try
                {
                    using (DeepseekClient client = new DeepseekClient(apiKey, settings.ApiBaseUrl))
                    {
                        localProgress.UpdateStatus(string.Format("AI 正在生成「{0}」...", selected.Name));
                        ChatRequest request = new ChatRequest
                        {
                            Model = model,
                            Temperature = settings.Temperature,
                            MaxTokens = settings.MaxTokens,
                            Messages = new List<ChatMessage>
                            {
                                ChatMessage.System(TemplateSystemPrompt),
                                ChatMessage.User(userPrompt)
                            }
                        };

                        string finalText = string.Empty;
                        await client.StreamAsync(
                            request,
                            delegate(string token) { localProgress.ReportTokens(token == null ? 0 : token.Length); resultDialog.AppendText(token); },
                            delegate(string completed) { finalText = completed ?? string.Empty; },
                            localProgress.Token);

                        if (string.IsNullOrWhiteSpace(resultDialog.FullText) && !string.IsNullOrWhiteSpace(finalText))
                        {
                            resultDialog.SetResult(finalText);
                        }
                        else
                        {
                            resultDialog.MarkStreamComplete();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (resultDialog != null && !resultDialog.IsDisposed && string.IsNullOrWhiteSpace(resultDialog.FullText))
                    {
                        resultDialog.SetResult("操作已取消。");
                    }
                    else if (resultDialog != null && !resultDialog.IsDisposed)
                    {
                        resultDialog.MarkStreamComplete();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("模板生成失败", ex);
                    Msg.Show("模板生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog(string.Format("模板生成 - {0}", selected.Name));
                resultDialog.FormClosed += delegate
                {
                    if (!resultDialog.InsertRequested || string.IsNullOrWhiteSpace(resultDialog.FullText))
                    {
                        return;
                    }
                    try
                    {
                        PageWriter writer = new PageWriter();
                        writer.AppendOutline(pageId, resultDialog.FullText, selected.Name);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("插入模板内容失败", ex);
                        Msg.Show("插入模板内容失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                resultDialog.OnRegenerate = delegate
                {
                    resultDialog.ResetForRegenerate();
                    _ = runOnce();
                };

                resultDialog.Show(UiThread.Anchor);
                await runOnce();
            }
            catch (Exception ex)
            {
                Logger.Error("模板生成启动失败", ex);
                Msg.Show("模板生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Show a simple list dialog for template selection.
        /// Returns -1 if user cancels.
        /// </summary>
        private static int ChooseTemplate()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "选择模板";
                dlg.Size = new System.Drawing.Size(360, 380);
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.TopMost = true;

                Label label = new Label
                {
                    Text = "请选择要使用的笔记模板：",
                    Location = new System.Drawing.Point(12, 12),
                    AutoSize = true
                };
                dlg.Controls.Add(label);

                ListBox listBox = new ListBox
                {
                    Location = new System.Drawing.Point(12, 36),
                    Size = new System.Drawing.Size(320, 240),
                    Font = new System.Drawing.Font("Microsoft YaHei UI", 10f)
                };
                for (int i = 0; i < Templates.Length; i++)
                {
                    listBox.Items.Add(Templates[i].Name);
                }
                listBox.SelectedIndex = 0;
                dlg.Controls.Add(listBox);

                Button btnOk = new Button
                {
                    Text = "确定",
                    DialogResult = DialogResult.OK,
                    Location = new System.Drawing.Point(150, 290),
                    Size = new System.Drawing.Size(80, 32)
                };
                dlg.Controls.Add(btnOk);
                dlg.AcceptButton = btnOk;

                Button btnCancel = new Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.Cancel,
                    Location = new System.Drawing.Point(240, 290),
                    Size = new System.Drawing.Size(80, 32)
                };
                dlg.Controls.Add(btnCancel);
                dlg.CancelButton = btnCancel;

                // Double-click also confirms
                listBox.DoubleClick += delegate { dlg.DialogResult = DialogResult.OK; dlg.Close(); };

                if (dlg.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return -1;
                }

                return listBox.SelectedIndex;
            }
        }

        // ── Template data class ──

        private sealed class TemplateInfo
        {
            public readonly string Name;
            public readonly string PromptTemplate;
            public readonly string Placeholder;

            public TemplateInfo(string name, string promptTemplate, string placeholder)
            {
                Name = name;
                PromptTemplate = promptTemplate;
                Placeholder = placeholder;
            }
        }
    }
}
