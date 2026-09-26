using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OneNoteAI.Knowledge;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Tests;

internal static class DpiTests
{
    private static int _assertions;

    internal static void Run(string root)
    {
        IntPtr originalContext = GetThreadDpiAwarenessContext();
        var currentSettings = typeof(SettingsManager).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
        object originalSettings = currentSettings.GetValue(null);
        try
        {
            _assertions = 0;
            // Model an unaware COM host even if the launching shell is DPI-aware.
            SetThreadDpiAwarenessContext(new IntPtr(-1));
            IntPtr hostContext = GetThreadDpiAwarenessContext();
            currentSettings.SetValue(null, new AppSettings());

            UiThread.EnsureStarted();
            Assert(AreDpiAwarenessContextsEqual(hostContext, GetThreadDpiAwarenessContext()),
                "Starting UI must not change the calling thread's awareness.");

            UiThread.Send(delegate
            {
                CheckRibbonImages();
                Assert(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new IntPtr(-4)),
                    "UI thread must be PerMonitorV2 on Windows 10 1703+.");
                Assert(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(UiThread.Anchor.Handle), new IntPtr(-4)),
                    "Message-loop anchor must be created after DPI initialization.");
                CheckNativeMonitorMoves();

                foreach (string language in new[] { "en", "zh" })
                {
                    Strings.SetLanguage(language);
                    CheckDialog(new PromptDialog("DPI prompt", "Instruction", "Placeholder"));
                    CheckDialog(new ScopeDialog("DPI scope", "Section", 3));
                    CheckDialog(new SettingsDialog());
                    CheckDialog(new ProgressOverlay());
                    CheckDialog(new HelpDialog());
                    var result = new ResultDialog("DPI result");
                    result.OnRegenerate = delegate { };
                    result.OnFollowUp = delegate { };
                    result.SetResult("# Heading\nNormal **bold** and *italic*\n- Bullet");
                    CheckDialog(result);

                    CheckDialog(new KnowledgeSettingsDialog(new FakeNotes().Hierarchy()));
                    var server = new McpServerOptions { Name = "Synthetic", Endpoint = "https://example.invalid/mcp" };
                    CheckDialog(new McpServerDialog(server));
                    CheckDialog(new McpToolsDialog(server));
                    using (var store = new IndexStore(Path.Combine(root, "dpi-" + language)))
                    {
                        var chat = new KnowledgeDialog(new FakeNotes(), store, () => "p1");
                        ChatUiTests.SeedPreview(chat);
                        CheckDialog(chat);
                    }
                }
            });

            Console.WriteLine(_assertions + " DPI assertions across original, knowledge and MCP dialogs.");
        }
        finally
        {
            currentSettings.SetValue(null, originalSettings);
            Strings.SetLanguage(null);
            SetThreadDpiAwarenessContext(originalContext);
        }
    }

    private static void CheckRibbonImages()
    {
        Assembly assembly = typeof(ResultDialog).Assembly;
        int count = 0;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("OneNoteAI.Ribbon.Icons.", StringComparison.Ordinal) || !name.EndsWith(".png", StringComparison.Ordinal)) continue;
            using (var stream = assembly.GetManifestResourceStream(name))
            using (Image image = Image.FromStream(stream))
            {
                Assert(image.Width == 128 && image.Height == 128, "High-resolution ribbon image: " + name);
                count++;
            }
        }
        Assert(count == 10, "All ten high-resolution ribbon images are embedded.");
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128 })
            using (var stream = assembly.GetManifestResourceStream("OneNoteAI.Ribbon.Icons.QA.ico"))
            using (var icon = new Icon(stream, size, size))
            using (var bitmap = icon.ToBitmap())
            {
                Assert(bitmap.Size == new Size(size, size), "Q&A window icon conversion at " + size + " pixels.");
                Assert(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(size / 2, size / 3).A > 0,
                    "Q&A icon transparency/artwork at " + size + " pixels.");
            }
    }

    private static void CheckNativeMonitorMoves()
    {
        using (var form = new PromptDialog("Native DPI", "Monitor transitions"))
        {
            var fonts = new Dictionary<Control, float>();
            CaptureFonts(form, fonts);
            IntPtr handle = form.Handle;
            typeof(DpiAwareForm).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { EventArgs.Empty });
            CheckNativeFonts(form, fonts);
            foreach (Screen screen in Screen.AllScreens)
            {
                form.Location = screen.WorkingArea.Location + new Size(100, 100);
                CheckNativeFonts(form, fonts);
            }
        }
    }

    private static void CheckNativeFonts(Form form, Dictionary<Control, float> fonts)
    {
        int dpi = (int)GetDpiForWindow(form.Handle);
        foreach (var entry in fonts) CheckFont(form, entry.Key, entry.Value, dpi);
        Console.WriteLine("Native monitor/centering DPI " + dpi + ": fonts matched the window.");
    }

    private static void CheckDialog(DpiAwareForm form)
    {
        using (form)
        {
            // Keep synthetic transitions on one monitor, without native centering moves.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = Screen.PrimaryScreen.WorkingArea.Location + new Size(100, 100);
            DataGridView grid = Find<DataGridView>(form);
            if (grid != null) grid.Rows.Add(true, "Auto approve", "synthetic.tool", "Tool description");
            Size logicalClient = form.ClientSize;
            Size logicalMinimum = form.MinimumSize;
            var logicalFonts = new Dictionary<Control, float>();
            CaptureFonts(form, logicalFonts);
            SplitContainer split = Find<SplitContainer>(form);
            int splitterDistance = split?.SplitterDistance ?? 0;
            int splitterWidth = split?.SplitterWidth ?? 0;
            int panelMinimum = split?.Panel1MinSize ?? 0;

            IntPtr handle = form.Handle;
            Assert(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(handle), new IntPtr(-4)),
                form.GetType().Name + " window awareness.");
            int initialDpi = (int)GetDpiForWindow(handle);
            typeof(DpiAwareForm).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { EventArgs.Empty });
            CheckMetrics(form, logicalClient, logicalMinimum, logicalFonts, initialDpi);

            RichTextBox content = Find<RichTextBox>(form);
            if (content != null && content.TextLength >= 8) content.Select(3, 5);
            foreach (int dpi in new[] { 96, 120, 144, 192, 288, 96, 192, 96 })
            {
                ChangeDpi(form, dpi, logicalClient);
                CheckMetrics(form, logicalClient, logicalMinimum, logicalFonts, dpi);
                if (content != null && content.TextLength >= 8)
                {
                    Assert(content.SelectionStart == 3 && content.SelectionLength == 5,
                        form.GetType().Name + " text selection survived DPI change.");
                }
                if (split != null)
                {
                    Assert(Math.Abs(split.SplitterDistance - splitterDistance * dpi / 96F) <= 2,
                        "Knowledge scope splitter distance at " + dpi + ": " + split.SplitterDistance);
                    Assert(Math.Abs(split.Panel1MinSize - panelMinimum * dpi / 96F) <= 1,
                        "Knowledge scope minimum width at " + dpi + ": " + split.Panel1MinSize);
                    Assert(Math.Abs(split.SplitterWidth - splitterWidth * dpi / 96F) <= 1,
                        "Knowledge splitter grip width at " + dpi + ": " + split.SplitterWidth);
                }
            }
            if (split != null)
            {
                split.SplitterDistance = 350;
                ChangeDpi(form, 192, logicalClient);
                Assert(split.SplitterDistance == 700, "User-resized scope width scales proportionally.");
                ChangeDpi(form, 96, logicalClient);
                Assert(split.SplitterDistance == 350, "User-resized scope width survives a round trip.");
            }

            // Keep user state and rich-text styles while moving between monitors.
            if (form is PromptDialog)
            {
                Assert(((PromptDialog)form).UserInput == string.Empty, "Placeholder semantics.");
            }
            if (form is ResultDialog)
            {
                var result = (ResultDialog)form;
                Assert(result.FullText.Contains("**bold**"), "Result content survived DPI changes.");
                RichTextBox text = Find<RichTextBox>(form);
                int bold = text.Text.IndexOf("bold", StringComparison.Ordinal);
                text.Select(bold, 4);
                Assert(text.SelectionFont.Bold, "Rich-text bold styling survived DPI changes.");
                form.ClientSize = new Size(1000, 700);
                ChangeDpi(form, 192, new Size(1000, 700));
                Assert(form.ClientSize == new Size(2000, 1400), "User-resized window scales proportionally.");
            }
            Console.WriteLine(form.GetType().Name + ": startup DPI " + initialDpi + ", 100-300% transitions passed.");
        }
    }

    private static void CaptureFonts(Control control, Dictionary<Control, float> fonts)
    {
        fonts.Add(control, control.Font.SizeInPoints);
        foreach (Control child in control.Controls) CaptureFonts(child, fonts);
    }

    private static void CheckMetrics(DpiAwareForm form, Size client, Size minimum,
        Dictionary<Control, float> fonts, int dpi)
    {
        RealizeTabs(form);
        float scale = dpi / 96F;
        // Windows caps top-level window bounds at the monitor's maximum tracking size.
        Size maximum = SystemInformation.MaxWindowTrackSize - (form.Size - form.ClientSize);
        float expectedWidth = Math.Min(client.Width * scale, maximum.Width);
        float expectedHeight = Math.Min(client.Height * scale, maximum.Height);
        Assert(Math.Abs(form.ClientSize.Width - expectedWidth) <= 1 &&
            Math.Abs(form.ClientSize.Height - expectedHeight) <= 1,
            form.GetType().Name + " client size at " + dpi + ": " + form.ClientSize +
            ", logical " + client + ", maximum " + maximum);
        Assert(form.MinimumSize == new Size((int)Math.Round(minimum.Width * scale), (int)Math.Round(minimum.Height * scale)),
            form.GetType().Name + " minimum size at " + dpi);
        foreach (var entry in fonts)
        {
            CheckFont(form, entry.Key, entry.Value, dpi);
            if (entry.Key is Button)
            {
                var button = (Button)entry.Key;
                Assert(button.Left >= 0 && button.Top >= 0 &&
                    button.Right <= button.Parent.ClientSize.Width + 1 &&
                    button.Bottom <= button.Parent.ClientSize.Height + 1,
                    form.GetType().Name + " button bounds at " + dpi + ": " + button.Text + " " + button.Bounds +
                    " parent " + button.Parent.ClientSize);
            }
        }

        TreeView tree = Find<TreeView>(form);
        if (form is KnowledgeDialog)
        {
            var transcript = Find<RichTextBox>(form);
            Assert(transcript.GetPositionFromCharIndex(transcript.TextLength - 1).Y >= 0 &&
                transcript.GetPositionFromCharIndex(0).Y < transcript.ClientSize.Height,
                "Chat transcript scrolled past all content after reflow at " + dpi + ".");
        }
        if (form is HelpDialog) Assert(tree.ItemHeight == (int)Math.Round(26 * scale), "Help tree row height.");
        if (form is ResultDialog)
        {
            var close = (Button)form.CancelButton;
            Assert(close.Top == (int)Math.Round(13 * scale), "Result action top spacing.");
            Assert(close.Parent.ClientSize.Width - close.Right == (int)Math.Round(20 * scale),
                "Result action right spacing.");
        }
        DataGridView grid = Find<DataGridView>(form);
        if (grid != null)
        {
            using (Graphics graphics = grid.CreateGraphics())
            {
                Assert(grid.Rows[0].Height >= Math.Ceiling(grid.Font.GetHeight(graphics)) + 4,
                    "MCP tool row is clipped at " + dpi + ": " + grid.Rows[0].Height);
                Assert(grid.ColumnHeadersHeight >= Math.Ceiling(grid.Font.GetHeight(graphics)) + 4,
                    "MCP tool header is clipped at " + dpi + ": " + grid.ColumnHeadersHeight);
            }
        }
    }

    private static void CheckFont(Form form, Control control, float logicalPoints, int dpi)
    {
        float pixels;
        using (Graphics graphics = control.CreateGraphics())
        {
            // RichEdit exposes its native font in points, rounded to twips.
            pixels = control.Font.Unit == GraphicsUnit.Pixel ? control.Font.Size :
                control.Font.SizeInPoints * graphics.DpiY / 72F;
        }
        Assert(Math.Abs(pixels - logicalPoints * dpi / 72F) < 0.15F,
            form.GetType().Name + " " + control.GetType().Name + " font at " + dpi +
            ": actual " + control.Font.Size + " " + control.Font.Unit + ", logical points " + logicalPoints);
    }

    private static void RealizeTabs(Control parent)
    {
        // WinForms sizes inactive TabPages lazily, when the user selects them.
        var tabs = parent as TabControl;
        if (tabs != null)
        {
            IntPtr handle = tabs.Handle;
            int selected = tabs.SelectedIndex;
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                RealizeTabs(page);
            }
            tabs.SelectedIndex = selected;
            return;
        }
        foreach (Control child in parent.Controls) RealizeTabs(child);
    }

    private static T Find<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T) return (T)child;
            T found = Find<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static void ChangeDpi(Form form, int dpi, Size logicalClient)
    {
        // WM_DPICHANGED's rectangle is in physical pixels and includes the frame.
        var bounds = new NativeRectangle { Right = (int)Math.Round(logicalClient.Width * dpi / 96.0),
            Bottom = (int)Math.Round(logicalClient.Height * dpi / 96.0) };
        int style = GetWindowLong(form.Handle, -16);
        int extendedStyle = GetWindowLong(form.Handle, -20);
        Assert(AdjustWindowRectExForDpi(ref bounds, style, false, extendedStyle, (uint)GetDpiForWindow(form.Handle)),
            "Calculate native frame.");
        Point origin = Screen.PrimaryScreen.WorkingArea.Location;
        bounds.Right = origin.X + bounds.Right - bounds.Left;
        bounds.Bottom = origin.Y + bounds.Bottom - bounds.Top;
        bounds.Left = origin.X;
        bounds.Top = origin.Y;
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeRectangle)));
        try
        {
            Marshal.StructureToPtr(bounds, memory, false);
            SendMessage(form.Handle, 0x02E0, new IntPtr(dpi | (dpi << 16)), memory);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void Assert(bool value, string message)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref NativeRectangle rect, int style, bool menu, int exStyle, uint dpi);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
