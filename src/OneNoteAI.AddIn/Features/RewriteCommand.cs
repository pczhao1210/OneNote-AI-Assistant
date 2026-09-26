using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using OneNoteAI.AI;
using OneNoteAI.AI.Models;
using OneNoteAI.OneNote;
using OneNoteAI.OneNote.Models;
using OneNoteAI.Logging;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Features
{
    public static class RewriteCommand
    {
        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OneNoteProvider provider;
            PageContent page;
            string content;
            bool isSelection;

            try
            {
                provider = new OneNoteProvider();
                page = provider.GetCurrentPage();

                // Prefer current selection —改写常常只针对一段文字。
                // 如果用户没选中任何东西就回退到整页。
                string selectionText = string.Empty;
                try { selectionText = provider.GetCurrentSelectionText(); }
                catch (Exception selEx) { Logger.Warn("读取选区失败，回退到整页：" + selEx.Message); }

                if (!string.IsNullOrWhiteSpace(selectionText))
                {
                    content = selectionText;
                    isSelection = true;
                }
                else
                {
                    content = page.GetPlainText();
                    isSelection = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("获取当前页面失败", ex);
                Msg.Show("获取当前页面失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                Msg.Show("当前页面内容为空，无法改写。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string rewriteInstruction;
            string promptTitle = isSelection ? "改写要求（已选中文本）" : "改写要求（整页）";
            using (PromptDialog promptDialog = new PromptDialog(promptTitle, "请输入改写要求（可选）", "例如：更正式、更简洁、更适合会议纪要"))
            {
                if (promptDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }

                rewriteInstruction = promptDialog.UserInput;
            }

            string pageId = page.PageId;
            AppSettings settings = SettingsManager.Current;
            ProgressOverlay progress = null;
            ResultDialog resultDialog = null;

            string apiKey = SettingsManager.GetApiKey();
            string systemPrompt = PromptTemplates.RewriteSystem;
            string userPrompt = PromptTemplates.BuildRewritePrompt(content, rewriteInstruction);
            int tokens = TokenEstimator.Estimate(content);
            string model = settings.AutoSelectModel
                ? DeepseekClient.SelectModel("rewrite", tokens)
                : settings.DefaultModel;

            // Single streaming round, reusable for both initial run and "重新生成".
            // Captures resultDialog/apiKey/settings/prompts via closure.
            Func<System.Threading.Tasks.Task> runOnce = async delegate
            {
                ProgressOverlay localProgress = ProgressOverlay.Show(null);
                try
                {
                    using (DeepseekClient client = new DeepseekClient(settings))
                    {
                        ChatRequest request = CreateRequest(settings, model, systemPrompt, userPrompt);
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
                    Logger.Error("改写失败", ex);
                    Msg.Show("改写失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                string dialogTitle = isSelection ? "改写结果（针对选区）" : "改写结果（整页）";
                resultDialog = new ResultDialog(dialogTitle);
                AttachInsertHandler(resultDialog, pageId, "AI 改写", "插入改写结果失败：");
                resultDialog.OnRegenerate = delegate
                {
                    resultDialog.ResetForRegenerate();
                    // Fire-and-forget; runOnce manages its own progress + errors.
                    _ = runOnce();
                };
                resultDialog.Show(UiThread.Anchor);

                await runOnce();
            }
            catch (Exception ex)
            {
                Logger.Error("改写启动失败", ex);
                Msg.Show("改写失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (progress != null && !progress.IsDisposed)
                {
                    progress.Close();
                }
            }
        }

        private static ChatRequest CreateRequest(AppSettings settings, string model, string systemPrompt, string userPrompt)
        {
            return new ChatRequest
            {
                Model = model,
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens,
                Messages = new List<ChatMessage>
                {
                    ChatMessage.System(systemPrompt),
                    ChatMessage.User(userPrompt)
                }
            };
        }

        private static void AttachInsertHandler(ResultDialog resultDialog, string pageId, string heading, string errorPrefix)
        {
            resultDialog.FormClosed += delegate
            {
                if (!resultDialog.InsertRequested || string.IsNullOrWhiteSpace(resultDialog.FullText))
                {
                    return;
                }

                try
                {
                    PageWriter writer = new PageWriter();
                    writer.AppendOutline(pageId, resultDialog.FullText, heading);
                }
                catch (Exception ex)
                {
                    Logger.Error("插入改写结果到页面失败", ex);
                    Msg.Show(errorPrefix + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
