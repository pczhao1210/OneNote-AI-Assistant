using System;
using System.Windows.Forms;
using OneNoteAI.Logging;
using OneNoteAI.UI;

namespace OneNoteAI.Features
{
    public static class QACommand
    {
        private static KnowledgeDialog _window;

        public static void Execute()
        {
            try
            {
                if (_window == null || _window.IsDisposed) _window = new KnowledgeDialog();
                if (!_window.Visible) _window.Show(UiThread.Anchor);
                _window.Activate();
            }
            catch (Exception ex)
            {
                Logger.Error("Q&A assistant could not open: " + ex.GetType().Name);
                Msg.Show(ex.Message, "OneNote AI Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void Close() => UiThread.Post(() => { if (_window != null && !_window.IsDisposed) _window.Close(); });
    }
}
