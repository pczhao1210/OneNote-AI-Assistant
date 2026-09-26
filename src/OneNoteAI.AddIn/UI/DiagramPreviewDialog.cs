using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using OneNoteAI.Settings;
using static OneNoteAI.UI.KnowledgeUi;

namespace OneNoteAI.UI
{
    internal sealed class DiagramPreviewDialog : DpiAwareForm
    {
        private const string Origin = "https://onenoteai-diagram.invalid/";
        private readonly WebView2 _browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(12) };
        private readonly string _source;
        private readonly TaskCompletionSource<bool> _rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly Lazy<byte[]> Mermaid = new Lazy<byte[]>(() => Resource("mermaid.min.js"));
        internal Task<bool> Rendered => _rendered.Task;

        internal static string RuntimeVersion(string browserFolder = null)
        {
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString(browserFolder);
                if (!string.IsNullOrEmpty(version)) return version;
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException || ex is DllNotFoundException ||
                ex is BadImageFormatException || ex is COMException)
            {
                Logging.Logger.Warn("Diagram runtime detection failed: " + ex.GetType().Name);
            }
            throw new InvalidOperationException(L("未检测到可用的 WebView2 Runtime。请先安装 Microsoft Edge WebView2 Runtime，再开启流程图预览；普通问答不受影响。",
                "WebView2 Runtime is not available. Install Microsoft Edge WebView2 Runtime before enabling diagram previews. Normal Q&A is unaffected."));
        }

        internal static int LinkIndex(string link)
        {
            // RichEdit versions report either the target or "display text#target".
            const string prefix = "onenoteai-diagram:";
            int start = (link ?? "").LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return -1;
            start += prefix.Length;
            int end = start;
            while (end < link.Length && link[end] >= '0' && link[end] <= '9') end++;
            return int.TryParse(link.Substring(start, end - start), out int index) ? index : -1;
        }

        internal static void Open(IWin32Window owner, string link, IList<string> diagrams)
        {
            int index = LinkIndex(link);
            if (index < 0 || index >= diagrams.Count || !SettingsManager.Current.Knowledge.EnableDiagramPreview) return;
            try
            {
                RuntimeVersion();
                using (var dialog = new DiagramPreviewDialog(diagrams[index])) dialog.ShowDialog(owner);
            }
            catch (Exception ex)
            {
                Logging.Logger.Warn("Diagram preview failed: " + ex.GetType().Name);
                MessageBox.Show(owner, ex.Message, L("流程图预览失败", "Diagram preview failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal DiagramPreviewDialog(string source)
        {
            _source = source;
            Text = L("流程图预览", "Diagram preview");
            Size = new Size(900, 680);
            MinimumSize = new Size(520, 380);
            StartPosition = FormStartPosition.CenterParent;
            Theme.ApplyTo(this);
            Controls.Add(_browser);
            Controls.Add(_status);
            _status.Text = L("正在本地渲染流程图...", "Rendering the diagram locally...");
            Shown += async (s, e) => await InitializeAsync();
            FormClosed += (s, e) => _rendered.TrySetCanceled();
        }

        private async Task InitializeAsync()
        {
            try
            {
                RuntimeVersion();
                var environment = await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneNoteAI", "DiagramPreview"));
                if (IsDisposed) return;
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                await _browser.EnsureCoreWebView2Async(environment, options);
                if (IsDisposed) return;
                CoreWebView2 core = _browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.NewWindowRequested += (s, e) => e.Handled = true;
                core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
                core.DownloadStarting += (s, e) => e.Cancel = true;
                core.NavigationStarting += (s, e) => e.Cancel = e.Uri != Origin + "index.html";
                core.FrameNavigationStarting += (s, e) => e.Cancel = true;
                core.ProcessFailed += (s, e) => Failed(L("流程图渲染进程已退出，请关闭并重新打开预览。", "The diagram renderer exited. Close and reopen the preview."));
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (s, e) =>
                {
                    byte[] content = null;
                    string type = "text/plain";
                    if (e.Request.Uri == Origin + "index.html") { content = Resource("diagram.html"); type = "text/html; charset=utf-8"; }
                    else if (e.Request.Uri == Origin + "diagram.js") { content = Resource("diagram.js"); type = "text/javascript; charset=utf-8"; }
                    else if (e.Request.Uri == Origin + "mermaid.min.js") { content = Mermaid.Value; type = "text/javascript; charset=utf-8"; }
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(content ?? Array.Empty<byte>()),
                        content == null ? 403 : 200, content == null ? "Forbidden" : "OK", "Content-Type: " + type + "\r\nCache-Control: no-store");
                };
                core.WebMessageReceived += (s, e) =>
                {
                    if (e.Source != Origin + "index.html") return;
                    string message = e.TryGetWebMessageAsString();
                    if (message == "rendered")
                    {
                        _status.Text = L("本地预览 · Ctrl+滚轮缩放；原始代码保留在回答中。", "Local preview. Ctrl+wheel to zoom; the answer retains the original code.");
                        _rendered.TrySetResult(true);
                    }
                    else if (message == "error")
                        Failed(L("无法渲染此 Mermaid 代码，请检查语法。原始代码仍保留在回答中。", "This Mermaid code could not be rendered. Check its syntax; the answer still contains the original code."));
                };
                core.NavigationCompleted += async (s, e) =>
                {
                    if (!e.IsSuccess) { Failed(L("流程图预览页面加载失败。", "The diagram preview page failed to load.")); return; }
                    try
                    {
                        string json = JsonConvert.SerializeObject(_source, new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml });
                        await core.ExecuteScriptAsync("renderDiagram(" + json + ")");
                    }
                    catch (Exception ex)
                    {
                        if (IsDisposed) return;
                        Logging.Logger.Warn("Diagram script failed: " + ex.GetType().Name);
                        Failed(L("流程图渲染失败，请关闭并重新打开预览。", "Diagram rendering failed. Close and reopen the preview."));
                    }
                };
                core.Navigate(Origin + "index.html");
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                Logging.Logger.Warn("Diagram initialization failed: " + ex.GetType().Name);
                Failed(L("无法启动流程图预览：", "Could not start the diagram preview: ") + ex.Message);
            }
        }

        private void Failed(string message)
        {
            _status.Text = message;
            _status.ForeColor = Theme.AccentRed;
            Logging.Logger.Warn("Diagram preview unavailable or invalid Mermaid input.");
            _rendered.TrySetResult(false);
        }

        private static byte[] Resource(string name)
        {
            using (Stream stream = typeof(DiagramPreviewDialog).Assembly.GetManifestResourceStream("OneNoteAI.UI.Assets." + name))
            using (var memory = new MemoryStream())
            {
                if (stream == null) throw new InvalidOperationException("Missing diagram asset: " + name);
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }
}
