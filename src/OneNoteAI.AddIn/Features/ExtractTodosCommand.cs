using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>
    /// Extract-todos command — supports current page or full current section.
    /// For section scope we extract todos from each page individually and
    /// then concatenate them under per-page subheadings (no reduce step,
    /// since merging discrete todo lists by-page is more useful than
    /// summarizing them into one).
    /// </summary>
    public static class ExtractTodosCommand
    {
        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OneNoteProvider provider;
            string currentPageId;
            string sectionName;
            List<(string PageId, string Title)> sectionPages;

            try
            {
                provider = new OneNoteProvider();
                PageContent currentPage = provider.GetCurrentPage();
                currentPageId = currentPage.PageId;
                sectionName = provider.GetCurrentSectionName();
                sectionPages = provider.GetSectionPages();
            }
            catch (Exception ex)
            {
                Logger.Error("获取页面/分区信息失败", ex);
                Msg.Show("获取页面/分区信息失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ScopeKind scope;
            using (ScopeDialog scopeDialog = new ScopeDialog("提取待办事项 - 选择范围", sectionName, sectionPages.Count))
            {
                if (scopeDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }
                scope = scopeDialog.SelectedScope;
            }

            if (scope == ScopeKind.CurrentPage)
            {
                await RunSinglePageAsync(provider, currentPageId);
            }
            else
            {
                await RunSectionAsync(provider, currentPageId, sectionName, sectionPages);
            }
        }

        // ── single page ──

        private static async Task RunSinglePageAsync(OneNoteProvider provider, string pageId)
        {
            string content;
            try
            {
                content = provider.GetPage(pageId).GetPlainText();
            }
            catch (Exception ex)
            {
                Logger.Error("获取当前页面失败", ex);
                Msg.Show("获取当前页面失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                Msg.Show("当前页面内容为空，无法提取待办事项。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();
            int tokens = TokenEstimator.Estimate(content);
            string model = settings.AutoSelectModel
                ? DeepseekClient.SelectModel("extract-todos", tokens)
                : settings.DefaultModel;

            ResultDialog resultDialog = null;

            Func<Task> runOnce = async delegate
            {
                ProgressOverlay localProgress = ProgressOverlay.Show(null);
                try
                {
                    using (DeepseekClient client = new DeepseekClient(apiKey, settings.ApiBaseUrl))
                    {
                        localProgress.UpdateStatus("AI 正在提取待办");
                        ChatRequest request = CreateRequest(
                            settings, model,
                            PromptTemplates.ExtractTodosSystem,
                            PromptTemplates.BuildExtractTodosPrompt(content));

                        await StreamToDialogAsync(client, request, resultDialog, localProgress, localProgress.Token);
                        if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
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
                    Logger.Error("提取待办事项失败", ex);
                    Msg.Show("提取待办事项失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog("待办提取结果");
                AttachInsertHandler(resultDialog, pageId, "AI 提取待办", "插入待办事项失败：");
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
                Logger.Error("提取待办启动失败", ex);
                Msg.Show("提取待办事项失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── section: extract per page, concatenate under page subheadings ──

        private static async Task RunSectionAsync(
            OneNoteProvider provider,
            string currentPageId,
            string sectionName,
            List<(string PageId, string Title)> sectionPages)
        {
            if (sectionPages == null || sectionPages.Count == 0)
            {
                Msg.Show("当前分区没有可处理的页面。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSettings settings = SettingsManager.Current;
            ProgressOverlay progress = null;
            ResultDialog resultDialog = null;

            try
            {
                progress = ProgressOverlay.Show(null);
                string title = string.Format("分区待办 - {0}", string.IsNullOrWhiteSpace(sectionName) ? "当前分区" : sectionName);
                resultDialog = new ResultDialog(title);
                AttachInsertHandler(resultDialog, currentPageId, "AI 分区待办", "插入待办事项失败：");
                resultDialog.Show(UiThread.Anchor);

                string apiKey = SettingsManager.GetApiKey();
                using (DeepseekClient client = new DeepseekClient(apiKey, settings.ApiBaseUrl))
                {
                    bool anyEmitted = false;
                    for (int i = 0; i < sectionPages.Count; i++)
                    {
                        progress.Token.ThrowIfCancellationRequested();

                        var entry = sectionPages[i];
                        progress.UpdateStatus(string.Format("正在处理第 {0}/{1} 页：{2}", i + 1, sectionPages.Count, Truncate(entry.Title, 24)));

                        string pageText;
                        try
                        {
                            pageText = provider.GetPage(entry.PageId).GetPlainText();
                        }
                        catch (Exception readEx)
                        {
                            Logger.Error("读取页面失败: " + entry.Title, readEx);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(pageText))
                        {
                            continue;
                        }

                        int pageTokens = TokenEstimator.Estimate(pageText);
                        string mapModel = settings.AutoSelectModel
                            ? DeepseekClient.SelectModel("extract-todos", pageTokens)
                            : settings.DefaultModel;

                        // For oversized pages, chunk and extract per chunk; the
                        // model returns a list, we then merge.
                        string pageTodos;
                        if (pageTokens > ContentChunker.DefaultMaxInputTokens)
                        {
                            pageTodos = await ExtractFromChunksAsync(client, settings, pageText, progress, progress.Token);
                        }
                        else
                        {
                            ChatRequest req = CreateRequest(
                                settings, mapModel,
                                PromptTemplates.ExtractTodosSystem,
                                PromptTemplates.BuildExtractTodosPrompt(pageText));
                            ChatResponse resp = await client.SendAsync(req, progress.Token);
                            pageTodos = ExtractResponseText(resp);
                        }

                        if (string.IsNullOrWhiteSpace(pageTodos))
                        {
                            continue;
                        }

                        // Stream each per-page block into the result dialog so the
                        // user sees progress incrementally.
                        if (anyEmitted)
                        {
                            resultDialog.AppendText("\n\n");
                        }
                        resultDialog.AppendText(string.Format("### {0}\n{1}", entry.Title, pageTodos.Trim()));
                        anyEmitted = true;
                    }

                    if (!anyEmitted)
                    {
                        resultDialog.SetResult("分区内未发现待办事项。");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (resultDialog != null && !resultDialog.IsDisposed && string.IsNullOrWhiteSpace(resultDialog.FullText))
                {
                    resultDialog.SetResult("操作已取消。");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("分区待办提取失败", ex);
                Msg.Show("分区待办提取失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (progress != null && !progress.IsDisposed)
                {
                    progress.Close();
                }
            }
        }

        // ── helpers ──

        private static async Task<string> ExtractFromChunksAsync(
            DeepseekClient client, AppSettings settings, string pageText, ProgressOverlay progress, CancellationToken cancellationToken)
        {
            List<string> chunks = ContentChunker.ChunkText(pageText, ContentChunker.DefaultMaxInputTokens);
            List<string> collected = new List<string>();
            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (progress != null && !progress.IsDisposed)
                {
                    progress.UpdateStatus(string.Format("正在处理长页面 ({0}/{1})", i + 1, chunks.Count));
                }

                int chunkTokens = TokenEstimator.Estimate(chunks[i]);
                string chunkModel = settings.AutoSelectModel
                    ? DeepseekClient.SelectModel("extract-todos", chunkTokens)
                    : settings.DefaultModel;

                ChatRequest req = CreateRequest(
                    settings, chunkModel,
                    PromptTemplates.ExtractTodosSystem,
                    PromptTemplates.BuildExtractTodosPrompt(chunks[i]));
                ChatResponse resp = await client.SendAsync(req, cancellationToken);
                string text = ExtractResponseText(resp);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    collected.Add(text.Trim());
                }
            }
            return string.Join("\n", collected);
        }

        private static async Task StreamToDialogAsync(DeepseekClient client, ChatRequest request, ResultDialog resultDialog, ProgressOverlay overlay, CancellationToken cancellationToken)
        {
            string finalText = string.Empty;
            await client.StreamAsync(
                request,
                delegate(string token) { if (overlay != null) overlay.ReportTokens(token == null ? 0 : token.Length); resultDialog.AppendText(token); },
                delegate(string completed) { finalText = completed ?? string.Empty; },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(resultDialog.FullText) && !string.IsNullOrWhiteSpace(finalText))
            {
                resultDialog.SetResult(finalText);
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

        private static string ExtractResponseText(ChatResponse response)
        {
            if (response == null || response.Choices == null)
            {
                return string.Empty;
            }

            ChatChoice choice = response.Choices.FirstOrDefault();
            if (choice == null)
            {
                return string.Empty;
            }

            if (choice.Message != null && !string.IsNullOrWhiteSpace(choice.Message.Content))
            {
                return choice.Message.Content;
            }

            return choice.Delta == null ? string.Empty : choice.Delta.Content ?? string.Empty;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? string.Empty;
            }
            return text.Substring(0, max) + "...";
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
                    Logger.Error("插入待办事项到页面失败", ex);
                    Msg.Show(errorPrefix + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
