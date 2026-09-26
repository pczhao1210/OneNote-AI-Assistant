using System;
using System.Threading;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    /// <summary>
    /// Owns a dedicated STA thread that runs a WinForms message loop
    /// (Application.Run). All add-in dialogs (ProgressOverlay, ResultDialog,
    /// PromptDialog, SettingsDialog, MessageBox) MUST be created and shown
    /// on this thread.
    ///
    /// Reason: the OneNote add-in is hosted in dllhost.exe (COM Surrogate).
    /// Ribbon callbacks arrive on a thread that has no WinForms message
    /// pump, so a non-modal Form.Show() simply hangs ("not responding").
    /// </summary>
    public static class UiThread
    {
        private static Thread _thread;
        private static Form _pumpForm;
        private static SynchronizationContext _syncContext;
        private static readonly object _gate = new object();
        private static ManualResetEventSlim _ready;

        public static void EnsureStarted()
        {
            lock (_gate)
            {
                if (_thread != null && _thread.IsAlive)
                {
                    return;
                }

                _ready = new ManualResetEventSlim(false);
                _thread = new Thread(ThreadProc)
                {
                    IsBackground = true,
                    Name = "OneNoteAI.UiThread"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait();
            }
        }

        private static void ThreadProc()
        {
            DpiSupport.InitializeThread();

            // Install a WinForms sync context so BeginInvoke/Invoke work.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _pumpForm = new MessageAnchor
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000),
                Size = new System.Drawing.Size(1, 1),
                Visible = false
            };

            // Create the anchor handle before publishing the synchronization context.
            var handle = _pumpForm.Handle;
            GC.KeepAlive(handle);
            _syncContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_syncContext);
            _pumpForm.BeginInvoke(new Action(() => _ready.Set()));
            // Passing a Form to Run makes it visible, even if Visible was false.
            Application.Run();
        }

        private sealed class MessageAnchor : Form
        {
            protected override bool ShowWithoutActivation => true;
            protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle = (parameters.ExStyle | 0x80) & ~0x40000; // TOOLWINDOW, not APPWINDOW
                    return parameters;
                }
            }
        }

        /// <summary>Run an action on the UI thread (fire-and-forget).</summary>
        public static void Post(Action action)
        {
            EnsureStarted();
            _syncContext.Post(delegate { action(); }, null);
        }

        /// <summary>Run a function on the UI thread synchronously and return its result.</summary>
        public static T Send<T>(Func<T> func)
        {
            EnsureStarted();
            T result = default(T);
            Exception captured = null;
            _syncContext.Send(delegate
            {
                try { result = func(); }
                catch (Exception ex) { captured = ex; }
            }, null);
            if (captured != null) throw captured;
            return result;
        }

        public static void Send(Action action)
        {
            EnsureStarted();
            Exception captured = null;
            _syncContext.Send(delegate
            {
                try { action(); }
                catch (Exception ex) { captured = ex; }
            }, null);
            if (captured != null) throw captured;
        }

        /// <summary>Anchor form to use as MessageBox / dialog owner when no other owner is available.</summary>
        public static IWin32Window Anchor
        {
            get
            {
                EnsureStarted();
                return _pumpForm;
            }
        }
    }
}
