using System;
using System.Collections.Generic;
using System.Threading;
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
    public static class GenerateCommand
    {
        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string instruction;
            using (PromptDialog promptDialog = new PromptDialog(
                "生成内容",
                "请输入生成指令（例如：写一篇关于项目管理的总结）",
                "在此输入您的指令..."))
            {
                if (promptDialog.ShowDialog(UiThread.Anchor) != DialogResult.OK)
                {
                    return;
                }

                instruction = promptDialog.UserInput;
            }

            if (string.IsNullOrWhiteSpace(instruction))
            {
                Msg.Show("请输入生成指令。", "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OneNoteProvider provider;
            PageContent page;
            string existingContent;

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
            ResultDialog resultDialog = null;

            string apiKey = SettingsManager.GetApiKey();
            string systemPrompt = PromptTemplates.GenerateSystem;
            string userPrompt = PromptTemplates.BuildGeneratePrompt(instruction, existingContent);
            int tokens = TokenEstimator.Estimate(userPrompt);
            string model = settings.AutoSelectModel
                ? DeepseekClient.SelectModel("generate", tokens)
                : settings.DefaultModel;

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
                    Logger.Error("生成内容失败", ex);
                    Msg.Show("生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (resultDialog != null && !resultDialog.IsDisposed) resultDialog.MarkStreamComplete();
                }
                finally
                {
                    if (localProgress != null && !localProgress.IsDisposed) localProgress.Close();
                }
            };

            try
            {
                resultDialog = new ResultDialog("生成结果");
                AttachInsertHandler(resultDialog, pageId, "AI 生成", "插入生成内容失败：");
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
                Logger.Error("生成启动失败", ex);
                Msg.Show("生成失败：" + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    Logger.Error("插入生成内容到页面失败", ex);
                    Msg.Show(errorPrefix + ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
