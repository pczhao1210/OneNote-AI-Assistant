using System.Windows.Forms;

namespace OneNoteAI.UI
{
    internal sealed class ChatInputBox : TextBox
    {
        internal bool IsComposing { get; private set; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x010D) IsComposing = true; // WM_IME_STARTCOMPOSITION
            if (message.Msg == 0x010E) IsComposing = false; // WM_IME_ENDCOMPOSITION
            base.WndProc(ref message);
        }
    }
}
