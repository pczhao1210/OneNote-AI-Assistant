using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// Summarize command — supports both single page (current page) and
    /// section-wide (all pages in the current section) scopes. When the
    /// section scope is selected we run a map-reduce: each page is
    /// summarized individually, then the per-page summaries are fed back
    /// to the model to produce the final overview.
    /// </summary>
    public static class SummarizeCommand
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
            using (ScopeDialog scopeDialog = new ScopeDialog("智能摘要 - 选择范围", sectionName, sectionPages.Count))
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

        // ── single page ──────────────────────────────────────────────────

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
                Msg.Show("当前页面内容为空，无法生成摘要。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();
            int tokens = TokenEstimator.Estimate(content);
            string model = settings.AutoSelectModel
                ? DeepseekClient.SelectModel("summarize", tokens)
                : settings.DefaultModel;

            ResultDialog resultDialog = null;
            // Cache the (possibly map-reduced) user prompt across regenerations
            // so we don't repeat the expensive chunk-summarize map step.
            string cachedUserPrompt = null;

            Func<Task> runOnce = async delegate
            {
                ProgressOverlay localProgress = ProgressOverlay.Show(null);
                try
                {
                    using (DeepseekClient client = new DeepseekClient(settings))
                    {
                        if (cachedUserPrompt == null)
                        {
                            cachedUserPrompt = tokens > ContentChunker.DefaultMaxInputTokens
                                ? await BuildChunkedSummarizePromptAsync(client, settings, content, localProgress, localProgress.Token)
                                : PromptTemplates.BuildSummarizePrompt(content);
                        }

                        localProgress.UpdateStatus("AI 正在生成摘要");
                        ChatRequest request = CreateRequest(settings, model, PromptTemplates.SummarizeSystem, cachedUserPrompt);
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
                    Logger.Error("摘要生成失败", ex);
                    Msg.Show("摘要生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog("摘要结果");
                AttachInsertHandler(resultDialog, pageId, "AI 摘要", "插入摘要失败：");
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
                Logger.Error("摘要启动失败", ex);
                Msg.Show("摘要生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── section (multi-page map-reduce) ───────────────────────────────

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
                resultDialog = new ResultDialog(string.Format("分区摘要 - {0}", string.IsNullOrWhiteSpace(sectionName) ? "当前分区" : sectionName));
                AttachInsertHandler(resultDialog, currentPageId, "AI 分区摘要", "插入分区摘要失败：");
                resultDialog.Show(UiThread.Anchor);

                string apiKey = SettingsManager.GetApiKey();
                using (DeepseekClient client = new DeepseekClient(settings))
                {
                    // ── MAP: per-page summaries ──
                    List<string> partialSummaries = new List<string>();
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
                            ? DeepseekClient.SelectModel("summarize", pageTokens)
                            : settings.DefaultModel;

                        // Long pages also need their own chunking before the map step.
                        string mapInput = pageTokens > ContentChunker.DefaultMaxInputTokens
                            ? await BuildChunkedSummarizePromptAsync(client, settings, pageText, progress, progress.Token)
                            : PromptTemplates.BuildSummarizePrompt(pageText);

                        ChatRequest mapRequest = CreateRequest(settings, mapModel, PromptTemplates.SummarizeSystem, mapInput);
                        ChatResponse mapResponse = await client.SendAsync(mapRequest, progress.Token);
                        string mapSummary = ExtractResponseText(mapResponse);

                        if (!string.IsNullOrWhiteSpace(mapSummary))
                        {
                            partialSummaries.Add(string.Format("### {0}\n{1}", entry.Title, mapSummary.Trim()));
                        }
                    }

                    if (partialSummaries.Count == 0)
                    {
                        resultDialog.SetResult("分区内所有页面均为空，未生成摘要。");
                        return;
                    }

                    // ── REDUCE: combine per-page summaries into a single overview ──
                    progress.UpdateStatus("正在汇总整个分区摘要");
                    string combined = string.Join("\n\n", partialSummaries);
                    string reducePrompt = PromptTemplates.BuildSummarizePrompt(combined, true);
                    int reduceTokens = TokenEstimator.Estimate(combined);
                    string reduceModel = settings.AutoSelectModel
                        ? DeepseekClient.SelectModel("summarize", reduceTokens)
                        : settings.DefaultModel;

                    ChatRequest reduceRequest = CreateRequest(settings, reduceModel, PromptTemplates.SummarizeSystem, reducePrompt);
                    await StreamToDialogAsync(client, reduceRequest, resultDialog, progress, progress.Token);
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
                Logger.Error("分区摘要生成失败", ex);
                Msg.Show("分区摘要生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (progress != null && !progress.IsDisposed)
                {
                    progress.Close();
                }
            }
        }

        // ── helpers ──────────────────────────────────────────────────────

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

        private static async Task<string> BuildChunkedSummarizePromptAsync(
            DeepseekClient client,
            AppSettings settings,
            string content,
            ProgressOverlay progress,
            CancellationToken cancellationToken)
        {
            List<string> chunks = ContentChunker.ChunkText(content, ContentChunker.DefaultMaxInputTokens);
            if (chunks.Count <= 1)
            {
                return PromptTemplates.BuildSummarizePrompt(content);
            }

            List<string> partialSummaries = new List<string>();

            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (progress != null && !progress.IsDisposed)
                {
                    progress.UpdateStatus(string.Format("正在处理长页面 ({0}/{1})", i + 1, chunks.Count));
                }

                string chunk = chunks[i];
                int chunkTokens = TokenEstimator.Estimate(chunk);
                string chunkModel = settings.AutoSelectModel
                    ? DeepseekClient.SelectModel("summarize", chunkTokens)
                    : settings.DefaultModel;

                ChatRequest chunkRequest = CreateRequest(
                    settings,
                    chunkModel,
                    PromptTemplates.SummarizeSystem,
                    PromptTemplates.BuildSummarizePrompt(chunk));

                ChatResponse chunkResponse = await client.SendAsync(chunkRequest, cancellationToken);
                string summary = ExtractResponseText(chunkResponse);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    partialSummaries.Add(string.Format("第 {0} 部分摘要：\n{1}", i + 1, summary.Trim()));
                }
            }

            if (partialSummaries.Count == 0)
            {
                return PromptTemplates.BuildSummarizePrompt(content);
            }

            return PromptTemplates.BuildSummarizePrompt(string.Join("\n\n", partialSummaries), true);
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
                    Logger.Error("插入摘要到页面失败", ex);
                    Msg.Show(errorPrefix + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
