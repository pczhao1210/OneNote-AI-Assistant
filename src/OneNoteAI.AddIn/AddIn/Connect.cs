using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Extensibility;
using Microsoft.Office.Core;
using OneNoteAI.Features;
using OneNoteAI.Logging;
using OneNoteAI.UI;

namespace OneNoteAI.AddIn
{
    [ComVisible(true)]
    [Guid("B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8")]
    [ProgId("OneNoteAI.Connect")]
    public class Connect : IDTExtensibility2, IRibbonExtensibility
    {
        private const string EmptyRibbonXml = "<customUI xmlns=\"http://schemas.microsoft.com/office/2006/01/customui\"></customUI>";
        private const string RibbonResourceName = "OneNoteAI.Ribbon.CustomUI.xml";

        private static Connect _instance;
        private static string _addInDirectory;

        private object _oneNoteApp;
        private object _addInInstance;

        static Connect()
        {
            // Direct, dependency-free trace: prove the type was loaded by the CLR
            // before anything else (Logger, AssemblyResolve, etc) runs. If this
            // file does not appear, OneNote never asked the CLR to load us.
            try
            {
                string traceDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OneNoteAI", "logs");
                Directory.CreateDirectory(traceDir);
                File.AppendAllText(Path.Combine(traceDir, "boot-trace.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " static-ctor pid=" + Process.GetCurrentProcess().Id +
                    " process=" + Process.GetCurrentProcess().ProcessName + Environment.NewLine);
            }
            catch
            {
                // Never let tracing break activation.
            }

            // COM add-ins are loaded in the host process (OneNote). The CLR's AppDomain
            // base directory points to OneNote's folder, so it cannot find our private
            // dependencies (e.g. Newtonsoft.Json.dll). Register an AssemblyResolve
            // handler early (static ctor runs before any instance member is touched) to
            // probe our own install directory.
            _addInDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        public Connect()
        {
            _instance = this;
            try
            {
                Logger.Initialize();
                Logger.Info("Connect ctor invoked");
            }
            catch
            {
                // Ignore: never let logging break activation.
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var assemblyName = new AssemblyName(args.Name);
                string candidatePath = Path.Combine(_addInDirectory, assemblyName.Name + ".dll");
                if (File.Exists(candidatePath))
                {
                    return Assembly.LoadFrom(candidatePath);
                }
            }
            catch
            {
                // Swallow �?let the CLR try other resolution paths.
            }
            return null;
        }

        public static Connect Instance => _instance;

        public dynamic OneNoteApp => _oneNoteApp;

        public void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom)
        {
            try
            {
                _instance = this;
                // Store the raw COM object; OneNoteProvider/PageWriter will cast to IApplicationCOM.
                // This is the only interface the unified OneNote actually QI's successfully.
                _oneNoteApp = application;
                _addInInstance = addInInst;
                Logger.Initialize();
                Logger.Info($"OneNoteAI connected ({connectMode})");

                // Force enable TLS 1.2 / 1.3 for HttpClient. .NET Framework 4.8
                // defaults to SystemDefault which on some hosts (esp. when
                // hosted in dllhost.exe COM Surrogate) excludes TLS 1.2,
                // causing "Could not create SSL/TLS secure channel" against
                // api.deepseek.com.
                try
                {
                    ServicePointManager.SecurityProtocol |=
                        SecurityProtocolType.Tls12 | (SecurityProtocolType)12288 /* Tls13 */;
                    Logger.Info($"TLS protocols enabled: {ServicePointManager.SecurityProtocol}");
                }
                catch (Exception tlsEx)
                {
                    Logger.Warn("Failed to enable TLS 1.2/1.3: " + tlsEx.Message);
                }

                UiThread.EnsureStarted();
                Logger.Info("UI thread started");
            }
            catch (Exception ex)
            {
                Logger.Error("OnConnection failed", ex);
                throw;
            }
        }

        public void OnStartupComplete(ref Array custom)
        {
            try
            {
                Logger.Info("Startup complete");
            }
            catch (Exception ex)
            {
                Logger.Error("OnStartupComplete failed", ex);
                throw;
            }
        }

        public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            try
            {
                if (_oneNoteApp != null)
                {
                    Marshal.ReleaseComObject(_oneNoteApp);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OnDisconnection failed", ex);
                throw;
            }
            finally
            {
                _oneNoteApp = null;
                _addInInstance = null;

                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public void OnAddInsUpdate(ref Array custom)
        {
        }

        public void OnBeginShutdown(ref Array custom)
        {
        }

        public string GetCustomUI(string ribbonId)
        {
            try
            {
                Logger.Initialize();
                Logger.Info($"GetCustomUI requested for ribbonId='{ribbonId ?? "<null>"}'");
                Assembly assembly = Assembly.GetExecutingAssembly();

                using (Stream stream = assembly.GetManifestResourceStream(RibbonResourceName))
                {
                    if (stream == null)
                    {
                        Debug.WriteLine($"OneNoteAI: Ribbon resource not found - {RibbonResourceName}");
                        Logger.Warn($"Ribbon resource not found: {RibbonResourceName}");
                        return EmptyRibbonXml;
                    }

                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string xml = reader.ReadToEnd();
                        Logger.Info($"Ribbon XML loaded successfully (length={xml.Length})");
                        return xml;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetCustomUI failed", ex);
                return EmptyRibbonXml;
            }
        }

        // ---------------------------------------------------------------
        // Ribbon callbacks
        // ---------------------------------------------------------------

        public void OnRibbonLoad(IRibbonUI ribbonUI)
        {
            RibbonState.RibbonUI = ribbonUI;
            Logger.Info("Ribbon loaded");
        }

        /// <summary>
        /// Ribbon loadImage callback. customUI root declares loadImage="LoadImage"
        /// and each button uses image="Foo.png"; Office invokes this once per
        /// image string and we resolve it to an embedded PNG resource.
        ///
        /// loadImage is preferred over per-control getImage in OneNote: the
        /// unified OneNote (Microsoft 365 C2R) IDispatch implementation is
        /// flaky around per-control IPictureDisp returns, but loadImage is
        /// invoked through a different dispatch path that works reliably.
        /// </summary>
        public stdole.IPictureDisp LoadImage(string imageName)
        {
            try
            {
                if (string.IsNullOrEmpty(imageName)) return null;

                string baseName = imageName;
                int dot = baseName.LastIndexOf('.');
                if (dot > 0) baseName = baseName.Substring(0, dot);

                string resource = "OneNoteAI.Ribbon.Icons." + baseName + ".png";
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        Logger.Warn("Ribbon icon resource missing: " + resource);
                        return null;
                    }
                    using (System.Drawing.Image img = System.Drawing.Image.FromStream(stream))
                    {
                        return PictureConverter.ImageToPictureDisp(img);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("LoadImage failed for '" + (imageName ?? "<null>") + "'", ex);
                return null;
            }
        }

        /// <summary>
        /// Bridges System.Drawing.Image to stdole.IPictureDisp using the
        /// hosted-control trick �?AxHost.GetIPictureDispFromPicture is
        /// protected, so we subclass to expose it.
        /// </summary>
        private sealed class PictureConverter : System.Windows.Forms.AxHost
        {
            private PictureConverter() : base("{63109182-966B-4e3c-A8B2-8BC4A88D221C}") { }

            public static stdole.IPictureDisp ImageToPictureDisp(System.Drawing.Image image)
            {
                return (stdole.IPictureDisp)GetIPictureDispFromPicture(image);
            }
        }

        public void OnSummarize(IRibbonControl control)
        {
            UiThread.Post(SummarizeCommand.Execute);
        }

        public void OnGenerate(IRibbonControl control)
        {
            UiThread.Post(GenerateCommand.Execute);
        }

        public void OnRewrite(IRibbonControl control)
        {
            UiThread.Post(RewriteCommand.Execute);
        }

        public void OnQA(IRibbonControl control)
        {
            UiThread.Post(QACommand.Execute);
        }

        public void OnExtractTodos(IRibbonControl control)
        {
            UiThread.Post(ExtractTodosCommand.Execute);
        }

        public void OnTranslate(IRibbonControl control)
        {
            UiThread.Post(TranslateCommand.Execute);
        }

        public void OnSettings(IRibbonControl control)
        {
            UiThread.Post(delegate
            {
                using (var dlg = new SettingsDialog())
                {
                    dlg.StartPosition = FormStartPosition.CenterScreen;
                    dlg.TopMost = true;
                    dlg.ShowDialog(UiThread.Anchor);
                }
            });
        }
    }
}
