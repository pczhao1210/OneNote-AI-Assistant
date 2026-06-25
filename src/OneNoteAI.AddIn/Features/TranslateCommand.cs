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
    /// <summary>
    /// Translate command — translates selected text or the full page content.
    /// Supports auto-detection of source language and user-selected target language.
    /// </summary>
    public static class TranslateCommand
    {
        private static readonly string[] TargetLanguages = new[]
        {
            "英文 (English)",
            "中文 (Chinese)",
            "日文 (Japanese)",
            "韩文 (Korean)",
            "法文 (French)",
            "德文 (German)",
            "西班牙文 (Spanish)",
            "俄文 (Russian)"
        };

        private static readonly string[] LanguageCodes = new[]
        {
            "English",
            "Chinese",
            "Japanese",
            "Korean",
            "French",
            "German",
            "Spanish",
            "Russian"
        };

        private static string TranslateSystemPrompt
        {
            get { return PromptTemplates.TranslateSystemPrompt; }
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
            string sourceText;
            bool isSelection = false;

            try
            {
                provider = new OneNoteProvider();
                page = provider.GetCurrentPage();

                // Try to get selected text first
                string selectionText = provider.GetCurrentSelectionText();
                if (!string.IsNullOrWhiteSpace(selectionText))
                {
                    sourceText = selectionText;
                    isSelection = true;
                }
                else
                {
                    sourceText = page.GetPlainText();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("获取页面内容失败", ex);
                Msg.Show("获取页面内容失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                Msg.Show("当前页面没有内容可供翻译。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Ask user for target language
            string targetLanguage;
            using (PromptDialog langDialog = new PromptDialog(
                "翻译 - 选择目标语言",
                isSelection
                    ? "将翻译选中的文本。请输入目标语言（如：英文、中文、日文、韩文、法文、德文、西班牙文、俄文）："
                    : "将翻译当前页面内容。请输入目标语言（如：英文、中文、日文、韩文、法文、德文、西班牙文、俄文）：",
                "英文"))
            {
                if (langDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }
                targetLanguage = langDialog.UserInput;
            }

            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                Msg.Show("请指定目标语言。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pageId = page.PageId;
            AppSettings settings = SettingsManager.Current;
            string apiKey = SettingsManager.GetApiKey();

            string userPrompt = string.Format(
                "请将以下文本翻译为{0}：\n\n{1}",
                targetLanguage.Trim(),
                sourceText);

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
                        localProgress.UpdateStatus("AI 正在翻译");
                        ChatRequest request = new ChatRequest
                        {
                            Model = model,
                            Temperature = 0.3f, // Lower temperature for translation accuracy
                            MaxTokens = settings.MaxTokens,
                            Messages = new List<ChatMessage>
                            {
                                ChatMessage.System(TranslateSystemPrompt),
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
                    Logger.Error("翻译失败", ex);
                    Msg.Show("翻译失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog(string.Format("翻译结果（→ {0}）", targetLanguage));
                resultDialog.FormClosed += delegate
                {
                    if (!resultDialog.InsertRequested || string.IsNullOrWhiteSpace(resultDialog.FullText))
                    {
                        return;
                    }
                    try
                    {
                        PageWriter writer = new PageWriter();
                        writer.AppendOutline(pageId, resultDialog.FullText, "AI 翻译");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("插入翻译结果失败", ex);
                        Msg.Show("插入翻译结果失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                Logger.Error("翻译启动失败", ex);
                Msg.Show("翻译失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
