// Build the add-in first, then compile this standalone .NET Framework test runner:
// csc /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:src\OneNoteAI.AddIn\bin\Release\OneNoteAI.AddIn.dll /out:src\OneNoteAI.AddIn\bin\Release\DpiSmokeTests.exe tools\DpiSmokeTests.cs
// Run DpiSmokeTests.exe on Windows 10 1703+. No OneNote instance or API key is required.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OneNoteAI.Settings;
using OneNoteAI.UI;

internal static class DpiSmokeTests
{
    private static int _assertions;

    [STAThread]
    private static int Main()
    {
        try
        {
            // Model an unaware COM host even if the launching shell is DPI-aware.
            SetThreadDpiAwarenessContext(new IntPtr(-1));
            IntPtr hostContext = GetThreadDpiAwarenessContext();
            Strings.SetLanguage("en");
            typeof(SettingsManager).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, new AppSettings());

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
            });

            Console.WriteLine("PASS: " + _assertions + " DPI assertions across all six dialogs.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckRibbonImages()
    {
        Assembly assembly = typeof(ResultDialog).Assembly;
        int count = 0;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("OneNoteAI.Ribbon.Icons.", StringComparison.Ordinal)) continue;
            using (var stream = assembly.GetManifestResourceStream(name))
            using (Image image = Image.FromStream(stream))
            {
                Assert(image.Width == 128 && image.Height == 128, "High-resolution ribbon image: " + name);
                count++;
            }
        }
        Assert(count == 10, "All ten high-resolution ribbon images are embedded.");
    }

    private static void CheckDialog(DpiAwareForm form)
    {
        using (form)
        {
            Size logicalClient = form.ClientSize;
            Size logicalMinimum = form.MinimumSize;
            var logicalFonts = new Dictionary<Control, float>();
            CaptureFonts(form, logicalFonts);

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
        Assert(Math.Abs(form.ClientSize.Width - client.Width * scale) <= 1 &&
            Math.Abs(form.ClientSize.Height - client.Height * scale) <= 1,
            form.GetType().Name + " client size at " + dpi + ": " + form.ClientSize);
        Assert(form.MinimumSize == new Size((int)Math.Round(minimum.Width * scale), (int)Math.Round(minimum.Height * scale)),
            form.GetType().Name + " minimum size at " + dpi);
        foreach (var entry in fonts)
        {
            float pixels;
            using (Graphics graphics = entry.Key.CreateGraphics())
            {
                // RichEdit exposes its native font in points, rounded to twips.
                pixels = entry.Key.Font.Unit == GraphicsUnit.Pixel ? entry.Key.Font.Size :
                    entry.Key.Font.SizeInPoints * graphics.DpiY / 72F;
            }
            Assert(Math.Abs(pixels - entry.Value * dpi / 72F) < 0.15F,
                form.GetType().Name + " " + entry.Key.GetType().Name + " font at " + dpi +
                ": actual " + entry.Key.Font.Size + " " + entry.Key.Font.Unit + ", logical points " + entry.Value);
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
        if (tree != null) Assert(tree.ItemHeight == (int)Math.Round(26 * scale), "Help tree row height.");
        if (form is ResultDialog)
        {
            var close = (Button)form.CancelButton;
            Assert(close.Top == (int)Math.Round(13 * scale), "Result action top spacing.");
            Assert(close.Parent.ClientSize.Width - close.Right == (int)Math.Round(20 * scale),
                "Result action right spacing.");
        }
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
        bounds.Right -= bounds.Left;
        bounds.Bottom -= bounds.Top;
        bounds.Left = 0;
        bounds.Top = 0;
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
