using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using OneNoteCopilot.AI;
using OneNoteCopilot.AI.Models;
using OneNoteCopilot.Logging;
using OneNoteCopilot.OneNote;
using OneNoteCopilot.OneNote.Models;
using OneNoteCopilot.Settings;
using OneNoteCopilot.UI;

namespace OneNoteCopilot.Features
{
    public static class QACommand
    {
        public static async void Execute()
        {
            if (!SettingsManager.HasApiKey())
            {
                Msg.Show("请先在设置中配置 DeepSeek API Key。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                Msg.Show("获取当前页面失败：" + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(noteContent))
            {
                Msg.Show("当前页面没有内容可供问答。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string question;
            using (PromptDialog promptDialog = new PromptDialog(
                "笔记问答",
                "请输入您的问题（AI 将基于当前页面内容回答）",
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
                Msg.Show("请输入您的问题。", "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pageId = page.PageId;
            AppSettings settings = SettingsManager.Current;
            ResultDialog resultDialog = null;

            string apiKey = SettingsManager.GetApiKey();
            string systemPrompt = PromptTemplates.QASystem;

            // Conversation history. The system prompt + initial page-grounded
            // user prompt are the first two messages; each follow-up appends a
            // new user message and we capture the assistant reply at the end
            // of each stream so the next turn sees the prior context.
            List<ChatMessage> history = new List<ChatMessage>
            {
                ChatMessage.System(systemPrompt),
                ChatMessage.User(PromptTemplates.BuildQAPrompt(noteContent, question))
            };

            // Track where each round's tokens start in FullText so we can
            // extract the assistant reply when the stream completes.
            int roundStartLength = 0;

            Func<System.Threading.Tasks.Task> runOnce = async delegate
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

                        // Persist assistant reply into history for next turn.
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
                    Msg.Show("问答失败：" + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    // Re-run the last user turn: drop the trailing assistant
                    // reply (if any) and reset the dialog text to the state
                    // before the last answer started streaming.
                    if (history.Count > 0 && history[history.Count - 1].Role == "assistant")
                    {
                        history.RemoveAt(history.Count - 1);
                    }
                    // Truncate displayed text back to the prior round boundary.
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
                        "请输入追问（AI 将基于之前的对话回答）",
                        "在此输入您的追问..."))
                    {
                        if (dlg.ShowDialog(UiThread.Anchor) != DialogResult.OK) return;
                        followUp = dlg.UserInput;
                    }
                    if (string.IsNullOrWhiteSpace(followUp)) return;

                    history.Add(ChatMessage.User(followUp));
                    // Append a visual separator before streaming the new answer.
                    resultDialog.AppendText("\n\n---\n\n**问：** " + followUp.Trim() + "\n\n**答：** ");
                    _ = runOnce();
                };

                resultDialog.Show(UiThread.Anchor);
                await runOnce();
            }
            catch (Exception ex)
            {
                Logger.Error("问答启动失败", ex);
                Msg.Show("问答失败：" + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    Logger.Error("插入问答结果到页面失败", ex);
                    Msg.Show(errorPrefix + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
        }
    }
}
