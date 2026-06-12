using System.Windows.Forms;

namespace OneNoteAI.UI
{
    /// <summary>
    /// MessageBox.Show wrapper that always supplies a Win32 owner (the UI
    /// thread anchor form) AND forces the box to appear above other windows
    /// (notably the OneNote main window which would otherwise hide it).
    /// </summary>
    public static class Msg
    {
        // MB_SETFOREGROUND = 0x10000; brings the message box to the foreground.
        // MB_TOPMOST       = 0x40000; makes it WS_EX_TOPMOST so it sits above
        //                              the OneNote main window.
        private const MessageBoxOptions ForegroundTopmost =
            (MessageBoxOptions)0x40000 | (MessageBoxOptions)0x10000;

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return MessageBox.Show(UiThread.Anchor, text, caption, buttons, icon,
                MessageBoxDefaultButton.Button1, ForegroundTopmost);
        }
    }
}

