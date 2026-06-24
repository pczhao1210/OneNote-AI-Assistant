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
    /// Auto-tag command — uses AI to analyze the current page and generate
    /// relevant tags, categories, and a brief classification summary.
    /// Results can be inserted at the top of the page as metadata.
    /// </summary>
    public static class TagCommand
    {
        private const string TagSystemPrompt =
            "你是一个专业的笔记分类助手。请分析用户提供的笔记内容，生成以下信息：\n" +
            "1. **标签**：3-8个关键词标签，用 # 号开头，空格分隔\n" +
            "2. **分类**：归入一个最合适的类别（如：工作、学习、项目、会议、技术、生活、创意、研究等）\n" +
            "3. **主题摘要**：一句话概括笔记主题（不超过30字）\n\n" +
            "输出格式：\n" +
            "标签：#标签1 #标签2 #标签3 ...\n" +
            "分类：XXX\n" +
            "主题：XXX\n\n" +
            "要求：标签要具体有意义，能反映内容核心；分类要准确；主题要精炼。";

        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OneNoteProvider provider;
            PageContent page;
            string noteContent;

            try
            {
                provider = new OneNoteProvider();
                page = provider.GetCurrentPage();
                noteContent = page.GetPlainText();
            }
            catch (Exception ex)
            {
                Logger.Error("获取当前页面失败", ex);
                Msg.Show("获取当前页面失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(noteContent))
            {
                Msg.Show("当前页面没有内容可供分析。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pageId = page.PageId;
            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();

            // Truncate content if too long (tags don't need full content)
            string contentForAnalysis = noteContent.Length > 4000
                ? noteContent.Substring(0, 4000) + "\n...(内容已截断)"
                : noteContent;

            string userPrompt = string.Format("请分析以下笔记内容并生成标签和分类：\n\n{0}", contentForAnalysis);

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
                        localProgress.UpdateStatus("AI 正在分析笔记内容...");
                        ChatRequest request = new ChatRequest
                        {
                            Model = model,
                            Temperature = 0.5f,
                            MaxTokens = 500,
                            Messages = new List<ChatMessage>
                            {
                                ChatMessage.System(TagSystemPrompt),
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
                    Logger.Error("自动标签生成失败", ex);
                    Msg.Show("自动标签生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog("自动标签与分类");
                resultDialog.FormClosed += delegate
                {
                    if (!resultDialog.InsertRequested || string.IsNullOrWhiteSpace(resultDialog.FullText))
                    {
                        return;
                    }
                    try
                    {
                        PageWriter writer = new PageWriter();
                        writer.AppendOutline(pageId, resultDialog.FullText, "AI 标签");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("插入标签结果失败", ex);
                        Msg.Show("插入标签结果失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                Logger.Error("自动标签启动失败", ex);
                Msg.Show("自动标签生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
