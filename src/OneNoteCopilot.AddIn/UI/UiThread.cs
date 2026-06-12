using System;
using System.Threading;
using System.Windows.Forms;

namespace OneNoteCopilot.UI
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
                    Name = "OneNoteCopilot.UiThread"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait();
            }
        }

        private static void ThreadProc()
        {
            // Install a WinForms sync context so BeginInvoke/Invoke work.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Create an invisible message-loop anchor form. Application.Run
            // pumps its messages and keeps the thread alive.
            _pumpForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000),
                Size = new System.Drawing.Size(1, 1),
                Opacity = 0,
                Visible = false
            };

            _pumpForm.Load += delegate
            {
                _pumpForm.Visible = false;
                _syncContext = SynchronizationContext.Current
                    ?? new WindowsFormsSynchronizationContext();
                _ready.Set();
            };

            // HandleCreated triggers Load synchronously enough — but force it.
            var handle = _pumpForm.Handle;
            GC.KeepAlive(handle);
            if (_syncContext == null)
            {
                _syncContext = new WindowsFormsSynchronizationContext();
                _ready.Set();
            }

            Application.Run(_pumpForm);
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
