using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneNoteCopilot.UI
{
    /// <summary>
    /// Helper used by every Copilot dialog/overlay to defeat Windows' foreground
    /// lock when our Add-in (hosted in dllhost.exe COM Surrogate, a background
    /// process) tries to display a window on top of ONENOTE.EXE.
    ///
    /// Without this dance the window is created with TopMost=true but still
    /// renders behind the OneNote main window because Windows refuses to grant
    /// foreground rights to a process that is not the current foreground app.
    /// </summary>
    internal static class ForceForeground
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        /// <summary>
        /// Call from a Form's <c>Shown</c> handler. Attaches our (background)
        /// thread input to the current foreground window's thread for one
        /// instant — Windows then allows SetForegroundWindow to succeed.
        /// </summary>
        public static void Apply(Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            try
            {
                IntPtr hWnd = form.Handle;
                ShowWindow(hWnd, SW_SHOW);
                BringWindowToTop(hWnd);

                IntPtr fgWnd = GetForegroundWindow();
                if (fgWnd == IntPtr.Zero || fgWnd == hWnd)
                {
                    SetForegroundWindow(hWnd);
                    form.Activate();
                    return;
                }

                uint fgThread = GetWindowThreadProcessId(fgWnd, out _);
                uint ourThread = GetCurrentThreadId();

                if (fgThread == ourThread)
                {
                    SetForegroundWindow(hWnd);
                    form.Activate();
                    return;
                }

                if (AttachThreadInput(ourThread, fgThread, true))
                {
                    try
                    {
                        BringWindowToTop(hWnd);
                        SetForegroundWindow(hWnd);
                        form.Activate();
                    }
                    finally
                    {
                        AttachThreadInput(ourThread, fgThread, false);
                    }
                }
                else
                {
                    // Fallback: rapid TopMost toggle nudges z-order in many
                    // cases even when AttachThreadInput is denied.
                    form.TopMost = false;
                    form.TopMost = true;
                    form.BringToFront();
                    form.Activate();
                }
            }
            catch
            {
                // Best-effort only; never let z-order tricks break the dialog.
            }
        }
    }
}
