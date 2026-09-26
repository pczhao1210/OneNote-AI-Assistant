using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Tests
{
    internal static class MarkdownTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        internal const string Example = "# Markdown\n\n**Bold** and __strong__, *italic*, ***nested***, ~~removed~~, `a|b`.\n\n" +
            "| Solver | Type | Notes |\n| :--- | :---: | ---: |\n| **OR-Tools** | CP-SAT | `a\\|b` |\n| SCIP | MIP | First<br>Second |\n\n" +
            "3. First item\n   - Nested item\n     - Deep item\n4. Second item\n\n- [x] Done\n- [ ] Pending\n\n" +
            "> Quoted **evidence** [S1]\n\n[Documentation](https://example.invalid/docs)\n\n" +
            "~~~~csharp\nvar path = @\"C:\\notes\\{draft}\";\n// **literal**, ``` nested fence\n~~~~\n\n" +
            "```mermaid\nflowchart LR\n A[Notes] --> B{Enough evidence?}\n B -->|Yes| C[Answer]\n```\n";

        internal static Task Run() => UiThread.Send<Task>(async () =>
        {
            await Task.Yield();
            using (var form = new Form { ClientSize = new Size(800, 700), ShowInTaskbar = false })
            using (var rich = new MarkdownBox { Dock = DockStyle.Fill, Font = Theme.FontContent, DetectUrls = false, ReadOnly = true })
            {
                form.Controls.Add(rich);
                var diagrams = new List<string>();
                rich.Rtf = MarkdownRtf.Render(Example, rich.Font, 10000, diagrams);
                Check(!rich.Text.Contains("| :---") && !rich.Text.Contains("**Bold**"), "Table/emphasis markup leaked into rich text.");
                Check(rich.Rtf.Contains(@"\trowd") && rich.Rtf.Contains(@"\cell"), "The table was flattened rather than rendered as native cells.");
                Select("Bold"); Check(rich.SelectionFont.Bold, "Strong emphasis did not render.");
                Select("strong"); Check(rich.SelectionFont.Bold, "Underscore strong emphasis did not render.");
                Select("nested"); Check(rich.SelectionFont.Bold && rich.SelectionFont.Italic, "Nested emphasis did not render.");
                Select("removed"); Check(rich.SelectionFont.Strikeout, "Strikethrough did not render.");
                Select("Solver"); Check(rich.SelectionFont.Bold, "The table header lost its emphasis.");
                Select("OR-Tools"); Check(rich.SelectionFont.Bold, "Inline formatting inside a table cell was lost.");
                Select("var path"); Check(rich.SelectionFont.Name == "Consolas", "Fenced code did not use a monospace font.");
                Check(rich.Text.Contains("C:\\notes\\{draft}") && rich.Text.Contains("**literal**, ``` nested fence"), "Code or RTF escapes changed the original text.");
                Check(rich.Text.Contains("3. First item") && rich.Text.Contains("4. Second item") &&
                    rich.Text.Contains("[x] Done") && rich.Text.Contains("https://example.invalid/docs"), "Lists, task boxes or links lost content.");
                Check(!rich.Text.Contains("a\\|b"), "An escaped table pipe leaked into an inline code cell.");
                Check(diagrams.Count == 1 && diagrams[0].Contains("flowchart LR"), "A complete Mermaid fence was not exposed for preview.");
                Select("View diagram 1");
                Check(rich.SelectedRtf.Contains("onenoteai-diagram:0"), "The diagram action is not a real hyperlink.");
                form.Show();
                Select("View diagram 1");
                rich.ScrollToCaret();
                await Task.Delay(50);
                string clicked = null;
                rich.LinkClicked += (s, e) => clicked = e.LinkText;
                Point linkPoint = RichTextView.Position(rich, rich.SelectionStart);
                Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews"));
                using (var image = new Bitmap(rich.Width, rich.Height))
                {
                    rich.DrawToBitmap(image, rich.ClientRectangle);
                    image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews", "markdown-link.png"));
                }
                IntPtr location = new IntPtr((linkPoint.X + 5) | ((linkPoint.Y + 5) << 16));
                Point cursor = Cursor.Position;
                try
                {
                    Cursor.Position = rich.PointToScreen(new Point(linkPoint.X + 5, linkPoint.Y + 5));
                    SendMessage(rich.Handle, 0x0200, IntPtr.Zero, location);
                    SendMessage(rich.Handle, 0x0201, new IntPtr(1), location);
                    SendMessage(rich.Handle, 0x0202, IntPtr.Zero, location);
                }
                finally { Cursor.Position = cursor; }
                Check(DiagramPreviewDialog.LinkIndex(clicked) == 0, "Clicking the native diagram link did not deliver its target: " + clicked +
                    ", position=" + linkPoint + ", selected=" + rich.SelectedText + ", events=" + SendMessage(rich.Handle, 0x043B, IntPtr.Zero, IntPtr.Zero));
                Check(DiagramPreviewDialog.LinkIndex("View diagram 1#onenoteai-diagram:0") == 0, "RichEdit link routing failed.");
                Check(DiagramPreviewDialog.LinkIndex("https://example.invalid") == -1, "An external link was treated as a diagram.");
                diagrams.Clear();
                rich.Rtf = MarkdownRtf.Render("```mermaid\nflowchart LR\nA-->B", rich.Font, 9000, diagrams);
                Check(diagrams.Count == 0 && rich.Text.Contains("A-->B"), "Incomplete streaming Mermaid became an active preview.");
                rich.Rtf = MarkdownRtf.RenderSections(new[] { "```text\nunfinished", "## Next turn\n**visible**" }, rich.Font);
                Select("visible"); Check(rich.SelectionFont.Bold, "An unfinished code block consumed the next conversation turn.");
                rich.Rtf = MarkdownRtf.Render(Example, rich.Font);
                Check(!rich.Text.Contains("View diagram"), "Diagram previews are not opt-in.");
                Find(rich, "Solver");
                Point header = RichTextView.Position(rich, rich.SelectionStart);
                Find(rich, "Type");
                Point secondColumn = RichTextView.Position(rich, rich.SelectionStart);
                Check(secondColumn.X > header.X && Math.Abs(secondColumn.Y - header.Y) <= 1, "Table columns are not aligned in native RichEdit.");
                Find(rich, "SCIP");
                Check(RichTextView.Position(rich, rich.SelectionStart).Y > header.Y, "Table rows overlap.");
                for (int length = 1; length < Example.Length; length++)
                    rich.Rtf = MarkdownRtf.Render(Example.Substring(0, length), rich.Font);
                rich.Rtf = MarkdownRtf.Render("中文 😀 \\{literal\\}\n\n<script>alert(1)</script>", rich.Font);
                Check(rich.Text.Contains("中文 😀 {literal}") && rich.Text.Contains("<script>"), "Unicode, escapes or inert HTML were lost.");
                CheckRoundButton();

                void Select(string text)
                {
                    int at = Find(rich, text);
                    Check(at >= 0, "Rendered text is missing: " + text);
                    rich.Select(at, text.Length);
                }
            }
            Check(!new KnowledgeOptions().EnableDiagramPreview, "Diagram preview must default off.");
            Check(JsonConvert.DeserializeObject<KnowledgeOptions>("{}").EnableDiagramPreview == false, "Old settings enabled diagrams.");
            Check(new KnowledgeOptions { EnableDiagramPreview = true }.Clone().EnableDiagramPreview, "Settings snapshots lost the diagram flag.");
            using (var settings = new KnowledgeSettingsDialog(new FakeNotes().Hierarchy(), () => throw new InvalidOperationException("Synthetic missing WebView2")))
            {
                var toggle = (CheckBox)settings.GetType().GetField("_diagrams", Private).GetValue(settings);
                toggle.Checked = true;
                Check(!toggle.Checked, "Missing WebView2 did not prevent enabling diagram preview.");
            }
        });

        // RichTextBox.Find uses managed string offsets, which exclude hidden table/link characters.
        internal static int Find(RichTextBox rich, string text, bool select = true)
        {
            var find = new FindText { Search = new CharRange { Start = 0, End = -1 }, Text = text };
            int at = SendMessage(rich.Handle, 0x047C, new IntPtr(5), ref find).ToInt32();
            if (at >= 0 && select) rich.Select(at, text.Length);
            return at;
        }

        internal static Task HostRendering() => UiThread.Send<Task>(async () =>
        {
            string language = (string)typeof(Strings).GetField("_forcedLang", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Strings.SetLanguage("zh");
            try
            {
                using (var baseline = new RichTextBox())
                    Console.WriteLine("Host default RichEdit: " + NativeClass(baseline));
                using (var form = new Form { ClientSize = new Size(800, 720), ShowInTaskbar = false })
                using (var rich = new MarkdownBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BackColor = Color.White })
                {
                    form.Controls.Add(rich);
                    form.Show();
                    string description = "部件通过胶水连接在测试板上，需要确认位置、方向、间距以及表面状态。" +
                        "这是用于检查单元格自动换行的合成文本，并非真实笔记。" +
                        "Mixed English words and 中文内容应始终保持在说明列中，直到末尾标记。";
                    string markdown = "## 主要工艺\n\n| 序号 | 工艺 | 说明 |\n| --- | --- | --- |\n" +
                        "| 5 | 放置部件 | " + description + " |\n" +
                        "| 6 | 检查部件 | 第一行<br/>第二行 |\n" +
                        "| 7 | 下一工序 | 后续说明 |\n\n" +
                        "```mermaid\nflowchart TD\n A[读取笔记] --> B[生成回答]\n B --> C[完成<br/>检查通过]\n```";
                    var diagrams = new List<string>();
                    foreach (int width in new[] { 440, 700, 1000 })
                    foreach (float size in new[] { 10f, 15f, 20f })
                    using (var font = new Font("Microsoft YaHei UI", size))
                    {
                        form.ClientSize = new Size(width, 720);
                        rich.Font = font;
                        rich.RightMargin = rich.ClientSize.Width - 16;
                        diagrams.Clear();
                        rich.Rtf = MarkdownRtf.Render(markdown, rich.Font, MarkdownRtf.AvailableWidth(rich), diagrams);
                        rich.Select(0, 0);
                        rich.ScrollToCaret();
                        await Task.Yield();
                        Check(rich.Text.Contains("查看流程图 1") && !rich.Text.Contains("onenoteai-diagram:"),
                            "Chinese diagram label is garbled or exposes the internal target. Native control: " + NativeClass(rich));
                        Check(NativeClass(rich).IndexOf("RICHEDIT50W", StringComparison.OrdinalIgnoreCase) >= 0,
                            "Markdown still depends on the COM host's legacy RichEdit version.");
                        Point first = At("部件通过胶水");
                        Point last = At("末尾标记");
                        Point next = At("检查部件");
                        Check(last.Y > first.Y, "The long-field fixture did not actually wrap.");
                        Check(last.X >= first.X - 2 && next.Y > last.Y,
                            "Wrapped description escaped its cell or overlapped the next row: " + first + ", " + last + ", " + next);
                        Point line1 = At("第一行"), line2 = At("第二行"), after = At("下一工序");
                        Check(Math.Abs(line1.X - line2.X) <= 2 && line2.Y > line1.Y && after.Y > line2.Y,
                            "An explicit cell line break escaped the table at width=" + width + ", font=" + size +
                            ": " + line1 + ", " + line2 + ", " + after);
                        if (width == 700 && size == 10f)
                        {
                            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews"));
                            using (var image = new Bitmap(rich.Width, rich.Height))
                            {
                                rich.DrawToBitmap(image, rich.ClientRectangle);
                                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews", "host-table-wrapping.png"));
                            }
                        }
                    }
                    Find(rich, "查看流程图 1");
                    rich.ScrollToCaret();
                    await Task.Delay(50);
                    string clicked = null;
                    rich.LinkClicked += (s, e) => clicked = e.LinkText;
                    Point point = RichTextView.Position(rich, rich.SelectionStart);
                    IntPtr location = new IntPtr((point.X + 5) | ((point.Y + 5) << 16));
                    SendMessage(rich.Handle, 0x0201, new IntPtr(1), location);
                    SendMessage(rich.Handle, 0x0202, IntPtr.Zero, location);
                    Check(DiagramPreviewDialog.LinkIndex(clicked) == 0 && diagrams.Count == 1,
                        "Chinese diagram preview cannot be opened in the COM-host control mode.");

                    Point At(string text)
                    {
                        int at = Find(rich, text, select: false);
                        Check(at >= 0, "Wrapped cell text is missing: " + text);
                        return RichTextView.Position(rich, at);
                    }
                }
            }
            finally { Strings.SetLanguage(language); }
        });

        private static string NativeClass(Control control)
        {
            var name = new StringBuilder(256);
            GetClassName(control.Handle, name, name.Capacity);
            return name.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, StringBuilder name, int capacity);

        [StructLayout(LayoutKind.Sequential)] private struct CharRange { public int Start, End; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindText { public CharRange Search; [MarshalAs(UnmanagedType.LPWStr)] public string Text; public CharRange Found; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, ref FindText text);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);

        private static void CheckRoundButton()
        {
            using (var button = new ChatSendButton())
            {
                foreach (int size in new[] { 40, 50, 60, 80, 120 })
                foreach (bool enabled in new[] { true, false })
                foreach (bool busy in new[] { true, false })
                {
                    button.Size = new Size(size, size);
                    button.Enabled = enabled;
                    button.Busy = busy;
                    using (var bitmap = new Bitmap(size, size))
                    {
                        button.DrawToBitmap(bitmap, new Rectangle(0, 0, size, size));
                        Check(bitmap.GetPixel(0, 0).ToArgb() == Theme.BgCard.ToArgb(), "The action button is still a filled rectangle.");
                        Color center = bitmap.GetPixel(size / 2, size / 2);
                        Check(center.ToArgb() == (enabled ? Color.White : Theme.TextSecondary).ToArgb(), "Send/cancel glyph has the wrong contrast.");
                        Check(bitmap.GetPixel(size / 2, size / 5).ToArgb() == (enabled ? Theme.Purple : Theme.BgCardBorder).ToArgb(),
                            "The circular action background is incorrect.");
                    }
                }
            }
        }

        internal static Task Diagrams(string root) => UiThread.Send<Task>(async () =>
        {
            bool missingDetected = false;
            try { DiagramPreviewDialog.RuntimeVersion(root); }
            catch (InvalidOperationException) { missingDetected = true; }
            Check(missingDetected, "A missing WebView2 installation was reported as available.");
            Console.WriteLine("WebView2 Runtime: " + DiagramPreviewDialog.RuntimeVersion());
            using (var dialog = new DiagramPreviewDialog("flowchart TD\nA[准备部件] --> B[处理<br/>125°C/1h/N2]\nB --> C[完成<br/>覆盖率>75%<br/>检查通过]"))
            {
                dialog.Show();
                Check(await Await(dialog.Rendered), "Local Mermaid failed to render.");
                var browser = (WebView2)dialog.GetType().GetField("_browser", Private).GetValue(dialog);
                string nodes = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('#diagram svg .node').length");
                Check(nodes == "3", "The preview does not contain the expected three flowchart nodes: " + nodes);
                string labels = JsonConvert.DeserializeObject<string>(await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#diagram svg').textContent"));
                Check(labels.Contains("125°C/1h/N2") && labels.Contains("覆盖率") && !labels.Contains("<br/>"),
                    "Chinese diagram labels, units or line breaks did not render.");
                // ExecuteScriptAsync does not await a Promise; wait on an explicit DOM marker instead.
                await browser.CoreWebView2.ExecuteScriptAsync("fetch('https://example.invalid/private').then(()=>document.body.dataset.blocked='no',()=>document.body.dataset.blocked='yes')");
                await Task.Delay(150);
                Check(await browser.CoreWebView2.ExecuteScriptAsync("document.body.dataset.blocked") == "\"yes\"", "Diagram content can access the network.");
                string images = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews");
                Directory.CreateDirectory(images);
                using (var output = File.Create(Path.Combine(images, "qa-diagram.png")))
                    await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, output);
                dialog.Close();
            }
            using (var dialog = new DiagramPreviewDialog("this is not valid mermaid"))
            {
                dialog.Show();
                Check(!await Await(dialog.Rendered), "Invalid Mermaid was reported as successful.");
                var browser = (WebView2)dialog.GetType().GetField("_browser", Private).GetValue(dialog);
                Check((await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('pre').textContent")).Contains("not valid"), "Invalid diagram source disappeared.");
                dialog.Close();
            }
        });

        private static async Task<bool> Await(Task<bool> task)
        {
            if (await Task.WhenAny(task, Task.Delay(30000)) != task) throw new TimeoutException("Diagram preview did not complete.");
            return await task;
        }
    }
}
