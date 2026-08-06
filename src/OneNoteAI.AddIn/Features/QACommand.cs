using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    public static class QACommand
    {
        /// <summary>
        /// System prompt for cross-page Q&A with citation support.
        /// </summary>
        private static string CrossPageQASystem
        {
            get { return PromptTemplates.CrossPageQASystem; }
        }

        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OneNoteProvider provider;
            PageContent currentPage;
            string sectionName;
            List<(string PageId, string Title)> sectionPages;

            try
            {
                provider = new OneNoteProvider();
                currentPage = provider.GetCurrentPage();
                sectionName = provider.GetCurrentSectionName();
                sectionPages = provider.GetSectionPages();
            }
            catch (Exception ex)
            {
                Logger.Error("获取页面/分区信息失败", ex);
                Msg.Show("获取页面/分区信息失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Let user choose scope: current page or entire section
            ScopeKind scope;
            using (ScopeDialog scopeDialog = new ScopeDialog("智能问答 - 选择范围", sectionName, sectionPages.Count))
            {
                if (scopeDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }
                scope = scopeDialog.SelectedScope;
            }

            if (scope == ScopeKind.CurrentPage)
            {
                await RunSinglePageQA(provider, currentPage);
            }
            else
            {
                await RunSectionQA(provider, currentPage, sectionName, sectionPages);
            }
        }

        // ── Single page Q&A (original behavior) ─────────────────────────────

        private static async Task RunSinglePageQA(OneNoteProvider provider, PageContent page)
        {
            string noteContent = page.GetPlainText();

            if (string.IsNullOrWhiteSpace(noteContent))
            {
                Msg.Show("当前页面没有内容可供问答。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string question;
            using (PromptDialog promptDialog = new PromptDialog(
                "笔记问答",
                "请输入您的问题（AI 将基于当前页面内容回答）：",
                "在此输入您的问题..."))
            {
                if (promptDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }
                question = promptDialog.UserInput;
            }

            if (string.IsNullOrWhiteSpace(question))
            {
                return;
            }

            string pageId = page.PageId;
            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();
            string systemPrompt = PromptTemplates.QASystem;

            List<ChatMessage> history = new List<ChatMessage>
            {
                ChatMessage.System(systemPrompt),
                ChatMessage.User(PromptTemplates.BuildQAPrompt(noteContent, question))
            };

            int roundStartLength = 0;
            ResultDialog resultDialog = null;

            Func<Task> runOnce = async delegate
            {
                ProgressOverlay localProgress = ProgressOverlay.Show(null);
                roundStartLength = resultDialog.FullText.Length;
                try
                {
                    int tokens = TokenEstimator.Estimate(string.Join("\n", history.ConvertAll(m => m.Content)));
                    string model = settings.AutoSelectModel
                        ? DeepseekClient.SelectModel("qa", tokens)
                        : settings.DefaultModel;

                    using (DeepseekClient client = new DeepseekClient(apiKey, settings.ApiBaseUrl))
                    {
                        ChatRequest request = new ChatRequest
                        {
                            Model = model,
                            Temperature = settings.Temperature,
                            MaxTokens = settings.MaxTokens,
                            Messages = new List<ChatMessage>(history)
                        };
                        string finalText = string.Empty;

                        await client.StreamAsync(
                            request,
                            delegate(string token) { localProgress.ReportTokens(token == null ? 0 : token.Length); resultDialog.AppendText(token); },
                            delegate(string completed) { finalText = completed ?? string.Empty; },
                            localProgress.Token);

                        string assistantReply = !string.IsNullOrWhiteSpace(finalText)
                            ? finalText
                            : (resultDialog.FullText.Length > roundStartLength
                                ? resultDialog.FullText.Substring(roundStartLength)
                                : string.Empty);
                        if (!string.IsNullOrWhiteSpace(assistantReply))
                        {
                            history.Add(ChatMessage.Assistant(assistantReply));
                        }

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
                    Logger.Error("问答失败", ex);
                    Msg.Show("问答失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog("问答（多轮）");
                AttachInsertHandler(resultDialog, pageId, "AI 问答", "插入问答结果失败：");

                resultDialog.OnRegenerate = delegate
                {
                    if (history.Count > 0 && history[history.Count - 1].Role == "assistant")
                    {
                        history.RemoveAt(history.Count - 1);
                    }
                    string keep = resultDialog.FullText.Length > roundStartLength
                        ? resultDialog.FullText.Substring(0, roundStartLength)
                        : string.Empty;
                    resultDialog.ResetForRegenerate();
                    if (!string.IsNullOrEmpty(keep)) resultDialog.AppendText(keep);
                    _ = runOnce();
                };

                resultDialog.OnFollowUp = delegate
                {
                    string followUp;
                    using (PromptDialog dlg = new PromptDialog(
                        "继续提问",
                        "请输入追问（AI 将基于之前的对话回答）：",
                        "在此输入您的追问..."))
                    {
                        if (dlg.ShowDialog(UiThread.Anchor) != DialogResult.OK) return;
                        followUp = dlg.UserInput;
                    }
                    if (string.IsNullOrWhiteSpace(followUp)) return;

                    history.Add(ChatMessage.User(followUp));
                    resultDialog.AppendText("\n\n---\n\n**问：** " + followUp.Trim() + "\n\n**答：** ");
                    _ = runOnce();
                };

                resultDialog.Show(UiThread.Anchor);
                await runOnce();
            }
            catch (Exception ex)
            {
                Logger.Error("问答启动失败", ex);
                Msg.Show("问答失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Section-wide Q&A with citation ───────────────────────────────────

        private static async Task RunSectionQA(
            OneNoteProvider provider,
            PageContent currentPage,
            string sectionName,
            List<(string PageId, string Title)> sectionPages)
        {
            if (sectionPages == null || sectionPages.Count == 0)
            {
                Msg.Show("当前分区没有可处理的页面。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Ask the question first
            string question;
            using (PromptDialog promptDialog = new PromptDialog(
                "跨页问答",
                string.Format("AI 将搜索分区「{0}」中的 {1} 个页面来回答您的问题。\n回答将标注信息来源页面。",
                    string.IsNullOrWhiteSpace(sectionName) ? "当前分区" : sectionName,
                    sectionPages.Count),
                "在此输入您的问题..."))
            {
                if (promptDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }
                question = promptDialog.UserInput;
            }

            if (string.IsNullOrWhiteSpace(question))
            {
                return;
            }

            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();
            ProgressOverlay progress = null;
            ResultDialog resultDialog = null;

            try
            {
                progress = ProgressOverlay.Show(null);

                // ── Determine query kind: metadata (titles/count) vs content ──
                bool metadataOnly = IsMetadataQuery(question);

                // ── Gather page content or just titles (token-budget aware) ──
                progress.UpdateStatus(metadataOnly ? "正在收集页面标题..." : "正在读取分区页面...");
                StringBuilder contextBuilder = new StringBuilder();
                int loadedPages = 0;

                // Token budget: reserve tokens for system prompt, question, and response.
                // Dynamically sized from the active model's context window so
                // larger models (GPT-4.1 128k, Claude 200k, Gemini 1M) can
                // read many more pages than DeepSeek's 64k.
                const int SystemOverhead = 1500; // system prompt + question + formatting
                int contextWindow = settings.GetContextWindow();
                int remainingBudget = contextWindow - SystemOverhead - settings.MaxTokens;
                if (remainingBudget < 8000) remainingBudget = 8000; // sanity floor for tiny windows

                // First pass: read all pages and estimate tokens
                List<(string Title, string Text, int Tokens)> pageData = new List<(string, string, int)>();
                for (int i = 0; i < sectionPages.Count; i++)
                {
                    progress.Token.ThrowIfCancellationRequested();
                    var entry = sectionPages[i];
                    progress.UpdateStatus(string.Format(metadataOnly ? "收集标题 {0}/{1}：{2}" : "读取页面 {0}/{1}：{2}",
                        i + 1, sectionPages.Count, Truncate(entry.Title, 20)));

                    string pageText;
                    if (metadataOnly)
                    {
                        // Metadata queries (page count / title list) only need
                        // the titles we already fetched in GetSectionPages();
                        // skip reading every page's body content entirely.
                        pageText = entry.Title;
                    }
                    else
                    {
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
                    }

                    int pageTokens = TokenEstimator.Estimate(pageText);
                    pageData.Add((entry.Title, pageText, pageTokens));
                }

                if (pageData.Count == 0)
                {
                    if (progress != null && !progress.IsDisposed) progress.Close();
                    Msg.Show("分区内所有页面均为空。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Second pass: allocate budget proportionally
                int totalTokens = 0;
                for (int i = 0; i < pageData.Count; i++) totalTokens += pageData[i].Tokens;

                progress.UpdateStatus("正在构建上下文...");
                for (int i = 0; i < pageData.Count; i++)
                {
                    if (remainingBudget <= 200) break; // stop if budget exhausted

                    var pd = pageData[i];
                    int allowedChars;
                    if (totalTokens <= remainingBudget)
                    {
                        // All pages fit, no truncation needed
                        allowedChars = pd.Text.Length;
                    }
                    else
                    {
                        // Proportional allocation: each page gets budget share
                        double share = (double)pd.Tokens / totalTokens;
                        int allowedTokens = Math.Max(200, (int)(remainingBudget * share));
                        allowedChars = Math.Min(pd.Text.Length, allowedTokens * 2); // ~2 chars/token for Chinese
                    }

                    string truncatedText = TruncateContent(pd.Text, allowedChars);
                    int usedTokens = TokenEstimator.Estimate(truncatedText);
                    remainingBudget -= usedTokens;

                    contextBuilder.AppendFormat(metadataOnly
                        ? "{0}. {1}"
                        : "\n\n【页面：{0}】\n{1}", pd.Title, truncatedText);
                    loadedPages++;
                }

                if (loadedPages == 0)
                {
                    if (progress != null && !progress.IsDisposed) progress.Close();
                    Msg.Show("分区内所有页面均为空。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string allContent = contextBuilder.ToString();

                // ── Build conversation with citation-aware system prompt ──
                List<ChatMessage> history = new List<ChatMessage>
                {
                    ChatMessage.System(CrossPageQASystem),
                    ChatMessage.User(string.Format(
                        metadataOnly
                            ? "以下是分区「{0}」中 {1} 个页面的标题列表：\n{2}\n\n---\n\n我的问题是：{3}\n\n请基于上面的标题列表如实回答。若问题要求列出所有标题，请全部列出，不要省略。"
                            : "以下是分区「{0}」中 {1} 个页面的笔记内容：\n{2}\n\n---\n\n我的问题是：{3}",
                        sectionName, loadedPages, allContent, question))
                };

                int roundStartLength = 0;

                // Close progress before showing result dialog
                if (progress != null && !progress.IsDisposed) progress.Close();
                progress = null;

                resultDialog = new ResultDialog(string.Format("跨页问答 - {0}（{1}页）",
                    string.IsNullOrWhiteSpace(sectionName) ? "当前分区" : sectionName, loadedPages));
                AttachInsertHandler(resultDialog, currentPage.PageId, "AI 跨页问答", "插入问答结果失败：");

                Func<Task> runOnce = async delegate
                {
                    ProgressOverlay localProgress = ProgressOverlay.Show(null);
                    roundStartLength = resultDialog.FullText.Length;
                    try
                    {
                        int tokens = TokenEstimator.Estimate(string.Join("\n", history.ConvertAll(m => m.Content)));
                        string model = settings.AutoSelectModel
                            ? DeepseekClient.SelectModel("qa", tokens)
                            : settings.DefaultModel;

                        localProgress.UpdateStatus("AI 正在分析并回答...");

                        using (DeepseekClient client = new DeepseekClient(apiKey, settings.ApiBaseUrl))
                        {
                            ChatRequest request = new ChatRequest
                            {
                                Model = model,
                                Temperature = settings.Temperature,
                                MaxTokens = settings.MaxTokens,
                                Messages = new List<ChatMessage>(history)
                            };
                            string finalText = string.Empty;

                            await client.StreamAsync(
                                request,
                                delegate(string token) { localProgress.ReportTokens(token == null ? 0 : token.Length); resultDialog.AppendText(token); },
                                delegate(string completed) { finalText = completed ?? string.Empty; },
                                localProgress.Token);

                            string assistantReply = !string.IsNullOrWhiteSpace(finalText)
                                ? finalText
                                : (resultDialog.FullText.Length > roundStartLength
                                    ? resultDialog.FullText.Substring(roundStartLength)
                                    : string.Empty);
                            if (!string.IsNullOrWhiteSpace(assistantReply))
                            {
                                history.Add(ChatMessage.Assistant(assistantReply));
                            }

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
                        Logger.Error("跨页问答失败", ex);
                        Msg.Show("跨页问答失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                    }
                    finally
                    {
                        if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                    }
                };

                resultDialog.OnRegenerate = delegate
                {
                    if (history.Count > 0 && history[history.Count - 1].Role == "assistant")
                    {
                        history.RemoveAt(history.Count - 1);
                    }
                    string keep = resultDialog.FullText.Length > roundStartLength
                        ? resultDialog.FullText.Substring(0, roundStartLength)
                        : string.Empty;
                    resultDialog.ResetForRegenerate();
                    if (!string.IsNullOrEmpty(keep)) resultDialog.AppendText(keep);
                    _ = runOnce();
                };

                resultDialog.OnFollowUp = delegate
                {
                    string followUp;
                    using (PromptDialog dlg = new PromptDialog(
                        "继续提问",
                        "请输入追问（AI 将基于分区内容和之前的对话回答）：",
                        "在此输入您的追问..."))
                    {
                        if (dlg.ShowDialog(UiThread.Anchor) != DialogResult.OK) return;
                        followUp = dlg.UserInput;
                    }
                    if (string.IsNullOrWhiteSpace(followUp)) return;

                    history.Add(ChatMessage.User(followUp));
                    resultDialog.AppendText("\n\n---\n\n**问：** " + followUp.Trim() + "\n\n**答：** ");
                    _ = runOnce();
                };

                resultDialog.Show(UiThread.Anchor);
                await runOnce();
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
                Logger.Error("跨页问答启动失败", ex);
                Msg.Show("跨页问答失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (progress != null && !progress.IsDisposed) progress.Close();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Detects metadata-only questions that only need page titles/counts
        /// (e.g. "这个分区有多少页？", "列出所有页面标题"). For these we skip
        /// reading every page's body content, which is dramatically faster and
        /// lets ALL titles fit in the context window regardless of model size.
        /// </summary>
        private static bool IsMetadataQuery(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return false;

            string q = question.Trim();
            string[] chineseKeywords =
            {
                "多少页", "几个页面", "多少个页面", "页面标题", "所有标题", "标题列表",
                "列出", "列举", "有哪些页面", "有哪些页", "全部页面", "所有页面",
                "统计", "标题", "索引", "目录", "清单"
            };
            foreach (string kw in chineseKeywords)
            {
                if (q.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
            }

            string ql = q.ToLowerInvariant();
            string[] englishKeywords =
            {
                "how many pages", "list of pages", "list all pages", "page titles",
                "all titles", "list of titles", "titles of", "page count",
                "index of", "table of contents", "overview"
            };
            foreach (string kw in englishKeywords)
            {
                if (ql.IndexOf(kw, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Truncates content to approximately maxChars characters, cutting at
        /// paragraph boundaries to keep content coherent.
        /// </summary>
        private static string TruncateContent(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            {
                return text ?? string.Empty;
            }

            // Try to cut at a paragraph boundary
            int cutPoint = text.LastIndexOf('\n', maxChars);
            if (cutPoint < maxChars / 2)
            {
                cutPoint = maxChars;
            }

            return text.Substring(0, cutPoint) + "\n...(内容已截断)";
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
                    Logger.Error("插入问答结果到页面失败", ex);
                    Msg.Show(errorPrefix + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
