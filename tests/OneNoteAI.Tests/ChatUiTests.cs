using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using OneNoteAI.AI;
using OneNoteAI.Conversation;
using OneNoteAI.Knowledge;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Tests
{
    internal static class ChatUiTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static void SeedPreview(KnowledgeDialog dialog)
        {
            var notes = new FakeNotes();
            dialog.GetType().GetField("_hierarchy", Private).SetValue(dialog, notes.Hierarchy());
            Invoke(dialog, "Populate");
            object turn = Invoke(dialog, "AddTurn", "APS 是什么？有哪些开源求解器可以用？", "选定范围：1 个分区 / 11 个可访问页面");
            turn.GetType().GetField("Text").SetValue(turn,
                "APS 是**高级计划与排程**系统，用于在设备、人员和交期等约束下安排生产。[S1]\n\n" +
                "### 可以考虑的开源求解器\n\n| 求解器 | 适用场景 |\n| --- | --- |\n| **OR-Tools** | 组合优化、任务排程和路径规划 |\n| **SCIP** | 混合整数规划与复杂约束 |\n\n" +
                "先明确约束与优化目标，再选择求解器。文档中的例子可以作为建模起点。\n\n```text\n目标：减少延期\n约束：设备容量、工序顺序、人员班次\n```");
            turn.GetType().GetField("Finished").SetValue(turn, true);
            var sources = new List<EvidenceSource> { new EvidenceRegistry().Add(NoteChunker.Split(notes.Pages["p1"], notes.PageState("p1"))[0]) };
            turn.GetType().GetField("Sources").SetValue(turn, sources);
            Invoke(dialog, "ShowSources", sources);
            dialog.GetType().GetField("_queuePaused", Private).SetValue(dialog, true);
            var input = Field<ChatInputBox>(dialog, "_question");
            input.Text = "结合笔记比较 OR-Tools 和 SCIP 的适用场景";
            ((Task)Invoke(dialog, "SubmitAsync", true)).GetAwaiter().GetResult();
            input.Text = "哪些约束需要优先建模？";
            Invoke(dialog, "RenderConversation");
        }

        internal static Task Run(string root) => UiThread.Send<Task>(async () =>
        {
            var current = typeof(SettingsManager).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = current.GetValue(null);
            try
            {
                IntPtr anchor = UiThread.Anchor.Handle;
                Check(!IsWindowVisible(anchor), "Message-loop anchor is visible.");
                long style = GetWindowLong(anchor, -20);
                Check((style & 0x80) != 0 && (style & 0x40000) == 0, "Message-loop anchor is eligible for the shell switcher.");
                ((Form)Control.FromHandle(anchor)).Show();
                Check(!IsWindowVisible(anchor), "Showing an owner made the message-loop anchor visible.");

                var settings = new AppSettings { Language = "en", Provider = AiProvider.OpenAI, DefaultModel = "test-model",
                    ApiKey = EncryptionHelper.Encrypt("synthetic"), ApiBaseUrl = "https://chat.invalid/v1", MaxTokens = 512 };
                settings.Knowledge.AllowedRootIds.Add("s1");
                settings.Knowledge.EnableDiagramPreview = true;
                current.SetValue(null, settings);
                var firstStarted = Signal();
                var releaseFirst = Signal();
                var cancelStarted = Signal();
                var settingsStarted = Signal();
                var closeStarted = Signal();
                var snapshotStarted = Signal();
                var releaseSnapshot = Signal();
                var requests = new List<string>();
                var notes = new FakeNotes();
                notes.Add("p3", "s1", "Different active page", "Different active page content for snapshot testing.");
                string activePage = "p1";
                using (var http = new HttpClient(new FakeHttp(async (request, token) =>
                {
                    JObject body = JObject.Parse(await request.Content.ReadAsStringAsync());
                    string question = (string)body["messages"].Last()["content"];
                    requests.Add(question);
                    if (question == "First") { firstStarted.SetResult(true); await releaseFirst.Task; }
                    if (question == "Snapshot") { snapshotStarted.SetResult(true); await releaseSnapshot.Task; }
                    if (question == "Page snapshot")
                        Check(body.ToString().Contains("rollback plan") && !body.ToString().Contains("Different active page content"), "Queued current-page identity changed at dispatch.");
                    if (question == "Cancel")
                    {
                        cancelStarted.SetResult(true);
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream(System.Text.Encoding.UTF8.GetBytes(
                            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Partial answer [S1]\"}}]}\n\n"))) };
                    }
                    if (question == "Settings" || question == "Close")
                    {
                        (question == "Settings" ? settingsStarted : closeStarted).SetResult(true);
                        await Task.Delay(Timeout.Infinite, token);
                    }
                    if (question == "Fail")
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("Synthetic chat failure") };
                    return FakeHttp.Sse(new JObject { ["choices"] = new JArray(new JObject { ["index"] = 0,
                        ["delta"] = new JObject { ["content"] = "## Answer\n**" + question + "** [S1]\n\n| Input | Output |\n| --- | --- |\n| Notes | Answer |\n\n" +
                            "```text\n{literal}\\path\n```\n\n```mermaid\nflowchart LR\nA-->B\n```" } }) },
                        new JObject { ["choices"] = new JArray(new JObject { ["index"] = 0, ["delta"] = new JObject(), ["finish_reason"] = "stop" }) });
                })))
                using (var chat = new DeepseekClient("synthetic", "https://chat.invalid/v1", AiProvider.OpenAI, http))
                using (var store = new IndexStore(Path.Combine(root, "chat-ui")))
                using (var dialog = new KnowledgeDialog(notes, store, () => activePage, chat))
                {
                    IntPtr window = dialog.Handle;
                    dialog.GetType().GetField("_hierarchy", Private).SetValue(dialog, notes.Hierarchy());
                    Invoke(dialog, "Populate");
                    var input = Field<ChatInputBox>(dialog, "_question");
                    Task Submit(string question, bool queue = false)
                    {
                        input.Text = question;
                        return (Task)Invoke(dialog, "SubmitAsync", queue);
                    }
                    KeyEventArgs Key(Keys key)
                    {
                        var e = new KeyEventArgs(key);
                        typeof(Control).GetMethod("OnKeyDown", Private).Invoke(input, new object[] { e });
                        return e;
                    }
                    Task first = Submit("First");
                    await Wait(firstStarted.Task);
                    input.Text = "Draft";
                    Check(Key(Keys.Enter).SuppressKeyPress && input.Text == "Draft" && requests.Count == 1, "Busy Enter lost or sent the draft.");
                    Check(!Key(Keys.Enter | Keys.Shift).SuppressKeyPress, "Shift+Enter was intercepted.");
                    object[] message = { Message.Create(input.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero) };
                    Invoke(input, "WndProc", message);
                    Check(!Key(Keys.Enter).SuppressKeyPress && input.Text == "Draft", "IME confirmation submitted a message.");
                    message[0] = Message.Create(input.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero);
                    Invoke(input, "WndProc", message);
                    input.Text = "Second";
                    Check(Key(Keys.Enter | Keys.Control).SuppressKeyPress && input.Text == "", "Ctrl+Enter did not queue and clear input.");
                    await Submit("Third", true);
                    Check(Field<IList>(dialog, "_queue").Count == 2, "Pending messages are not visible in the queue.");
                    input.Text = "Unsent draft";
                    releaseFirst.SetResult(true);
                    await Wait(first);
                    Check(requests.SequenceEqual(new[] { "First", "Second", "Third" }), "Queue did not execute serially in FIFO order.");
                    Check(input.Text == "Unsent draft", "Queue completion erased a newer draft.");
                    Check(Field<IList>(dialog, "_queue").Count == 0, "Completed queue was not drained.");
                    Check(notes.SearchedScopes.All(s => s == "s1"), "Queued requests escaped selected scope.");
                    var data = (DataObject)Invoke(dialog, "CopyData");
                    string plain = (string)data.GetData(DataFormats.UnicodeText);
                    Check(plain.Contains("**Third**") && plain.Contains("## Sources") && !plain.Contains("**First**"), "Copy did not select the latest answer and sources.");
                    Check(data.GetDataPresent(DataFormats.Rtf), "Copy lacks formatted text.");
                    using (var rich = new RichTextBox { Rtf = (string)data.GetData(DataFormats.Rtf) })
                    {
                        Check(rich.Text.Contains("{literal}\\path") && !rich.Text.Contains("**Third**"), "Markdown/code or RTF escaping is incorrect.");
                        rich.Select(rich.Text.IndexOf("Third", StringComparison.Ordinal), 5);
                        Check(rich.SelectionFont.Bold, "Markdown bold is not formatted.");
                    }
                    var answer = Field<RichTextBox>(dialog, "_answer");
                    int selected = MarkdownTests.Find(answer, "Third");
                    answer.Select(selected, 5);
                    Check((string)((DataObject)Invoke(dialog, "CopyData")).GetData(DataFormats.UnicodeText) == "Third", "Selected-text copy copied the whole conversation.");
                    Invoke(dialog, "RenderConversation");
                    Check(answer.SelectedText == "Third", "Redrawing destroyed the reading selection.");
                    answer.Select(RichTextView.Length(answer), 0);
                    Field<ComboBox>(dialog, "_turnPicker").SelectedIndex = 0;
                    Check(Field<ListBox>(dialog, "_sources").Items.Cast<EvidenceSource>().Any(), "Earlier-turn sources disappeared.");

                    Task cancel = Submit("Cancel");
                    await Wait(cancelStarted.Task);
                    await Wait(PartialAnswer());
                    Check(((string)((DataObject)Invoke(dialog, "CopyData")).GetData(DataFormats.UnicodeText)).Contains("not yet complete"),
                        "Streaming copy was not marked incomplete.");
                    await Submit("After cancel", true);
                    Invoke(dialog, "CancelOperation");
                    await Wait(cancel);
                    Check(Field<bool>(dialog, "_queuePaused") && !requests.Contains("After cancel"), "Cancellation automatically continued the queue.");
                    string partial = (string)((DataObject)Invoke(dialog, "CopyData")).GetData(DataFormats.UnicodeText);
                    Check(partial.Contains("Partial answer") && partial.Contains("Cancelled"), "Cancellation discarded partial output or its status.");
                    await Submit("Extra", true);
                    Check(!requests.Contains("Extra") && Field<IList>(dialog, "_queue").Count == 2, "Adding to a paused queue resumed it.");
                    Field<ListBox>(dialog, "_queued").SelectedIndex = 1;
                    Click(Field<Button>(dialog, "_removeQueued"));
                    Check(Field<IList>(dialog, "_queue").Count == 1, "Remove did not remove the selected pending message.");
                    dialog.GetType().GetField("_queuePaused", Private).SetValue(dialog, false);
                    await Wait((Task)Invoke(dialog, "DrainQueueAsync"));
                    Check(requests.Last() == "After cancel" && !requests.Contains("Extra"), "Resume did not honor pending message removal.");

                    await Wait(Submit("Fail"));
                    Check(Field<bool>(dialog, "_queuePaused"), "Failed response did not pause the queue.");
                    await Submit("Do not auto retry", true);
                    Check(!requests.Contains("Do not auto retry"), "An error caused a queued remote request.");
                    Invoke(dialog, "ResetConversation");
                    Check(Field<IList>(dialog, "_queue").Count == 0, "New conversation retained pending messages.");

                    Task changing = Submit("Settings");
                    await Wait(settingsStarted.Task);
                    await Submit("Old identity", true);
                    Invoke(dialog, "SettingsChanged", null, EventArgs.Empty);
                    await Wait(changing);
                    Check(Field<IList>(dialog, "_queue").Count == 0 && !requests.Contains("Old identity"), "Settings change retained queued work.");

                    Field<ComboBox>(dialog, "_mode").SelectedIndex = 0;
                    Task snapshot = Submit("Snapshot");
                    await Wait(snapshotStarted.Task);
                    await Submit("Page snapshot", true);
                    activePage = "p3";
                    releaseSnapshot.SetResult(true);
                    await Wait(snapshot);
                    input.Text = "Keyboard";
                    Check(Key(Keys.Enter).SuppressKeyPress, "Idle Enter was not handled by the composer.");
                    await Wait(FinishTurn());
                    Check(requests.Last() == "Keyboard" && input.Text.Length == 0, "Idle Enter did not send and clear input.");

                    // A new explicit send after a cleared conversation must not remain paused.
                    Task closing = Submit("Close");
                    await Wait(closeStarted.Task);
                    await Submit("Must not run after close", true);
                    dialog.Close();
                    await Wait(closing);
                    Check(!requests.Contains("Must not run after close"), "Closing the window sent pending work.");

                    async Task FinishTurn()
                    {
                        while (Field<bool>(dialog, "_draining")) await Task.Delay(10);
                    }
                    async Task PartialAnswer()
                    {
                        while (!((string)Field<object>(dialog, "_activeTurn").GetType().GetField("Text").GetValue(Field<object>(dialog, "_activeTurn")))
                            .Contains("Partial answer")) await Task.Delay(10);
                    }
                }
                Check(!IsWindowVisible(anchor), "Closing chat exposed the hidden owner.");
                UiThread.Post(() => { });
            }
            finally { current.SetValue(null, previous); }
        });

        private static void Click(Button button) => typeof(Button).GetMethod("OnClick", Private).Invoke(button, new object[] { EventArgs.Empty });
        private static async Task Wait(Task task, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (await Task.WhenAny(task, Task.Delay(10000)) != task) throw new TimeoutException("Chat UI operation did not complete at test line " + line + ".");
            await task;
        }
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    }
}
