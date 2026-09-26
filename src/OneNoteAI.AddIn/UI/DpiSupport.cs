using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OneNoteAI.Logging;

namespace OneNoteAI.UI
{
    internal static class DpiSupport
    {
        internal const int LogicalDpi = 96;

        [ThreadStatic]
        private static bool _enableNonClientScaling;

        // A COM DLL's config/manifest cannot opt its host into WinForms high DPI.
        // Change only our dedicated UI thread, never OneNote or dllhost's process.
        internal static void InitializeThread()
        {
            try
            {
                if (SetThreadDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero)
                {
                    Logger.Info("UI DPI awareness: PerMonitorV2");
                    return;
                }

                int error = Marshal.GetLastWin32Error();
                Logger.Warn("PerMonitorV2 unavailable (Win32 " + error + "); trying PerMonitor.");
                if (SetThreadDpiAwarenessContext(new IntPtr(-3)) != IntPtr.Zero)
                {
                    _enableNonClientScaling = true;
                    Logger.Info("UI DPI awareness: PerMonitor");
                    return;
                }

                Logger.Warn("Cannot set UI thread DPI awareness (Win32 " +
                    Marshal.GetLastWin32Error() + "); retaining host awareness.");
            }
            catch (EntryPointNotFoundException)
            {
                Logger.Warn("Thread DPI awareness requires Windows 10 version 1607 or later; retaining host awareness.");
            }
        }

        internal static void EnableNonClientScaling(IntPtr window)
        {
            if (_enableNonClientScaling && !EnableNonClientDpiScaling(window))
            {
                Logger.Warn("Cannot enable non-client DPI scaling (Win32 " +
                    Marshal.GetLastWin32Error() + ").");
            }
        }

        internal static int GetWindowDpi(Control control)
        {
            try
            {
                uint dpi = GetDpiForWindow(control.Handle);
                if (dpi > 0)
                {
                    return checked((int)dpi);
                }

                Logger.Warn("GetDpiForWindow returned zero; using the window graphics DPI.");
            }
            catch (EntryPointNotFoundException)
            {
                Logger.Warn("GetDpiForWindow is unavailable; using the window graphics DPI.");
            }

            using (Graphics graphics = control.CreateGraphics())
            {
                return (int)Math.Round(graphics.DpiX);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableNonClientDpiScaling(IntPtr window);
    }
}
