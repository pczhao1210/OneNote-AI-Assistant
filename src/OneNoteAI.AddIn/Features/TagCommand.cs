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
        private static string TagSystemPrompt
        {
            get { return PromptTemplates.TagSystemPrompt; }
        }

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
