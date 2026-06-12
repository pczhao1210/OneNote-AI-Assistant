using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    /// <summary>
    /// Wraps the OneNote main window HWND so WinForms dialogs can be parented to it,
    /// preventing them from being hidden behind OneNote.
    /// </summary>
    internal sealed class WindowWrapper : IWin32Window
    {
        public WindowWrapper(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public static IWin32Window OneNoteMainWindow
        {
            get
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName("ONENOTE");
                    foreach (Process p in procs)
                    {
                        IntPtr h = p.MainWindowHandle;
                        if (h != IntPtr.Zero)
                        {
                            return new WindowWrapper(h);
                        }
                    }
                }
                catch
                {
                    // ignored â€?fall back to null owner
                }
                return null;
            }
        }
    }
}
