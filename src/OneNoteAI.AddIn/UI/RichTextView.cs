using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    internal sealed class MarkdownBox : RichTextBox
    {
        private static readonly Lazy<IntPtr> RichEditLibrary = new Lazy<IntPtr>(() =>
        {
            IntPtr library = LoadLibraryEx(Path.Combine(Environment.SystemDirectory, "Msftedit.dll"), IntPtr.Zero, 0x00000800);
            if (library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load the Windows rich-text renderer.");
            return library;
        });

        protected override CreateParams CreateParams
        {
            get
            {
                // COM hosts can default to RichEdit20W regardless of the add-in's target framework.
                // Keep the system module loaded for the lifetime of its native window class.
                _ = RichEditLibrary.Value;
                CreateParams parameters = base.CreateParams;
                parameters.ClassName = "RICHEDIT50W";
                return parameters;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Framework RichTextBox drops friendly-link events past Text.Length, which excludes field instructions.
            if (m.Msg == 0x204E && m.LParam != IntPtr.Zero && Marshal.ReadInt32(m.LParam, IntPtr.Size * 2) == 0x070B)
            {
                int headerSize = IntPtr.Size == 8 ? 24 : 12;
                if (Marshal.ReadInt32(m.LParam, headerSize) == 0x0201)
                {
                    int rangeOffset = headerSize + 4 + IntPtr.Size * 2;
                    int start = Marshal.ReadInt32(m.LParam, rangeOffset);
                    int end = Marshal.ReadInt32(m.LParam, rangeOffset + 4);
                    if (start >= 0 && end > start && end <= RichTextView.Length(this))
                    {
                        IntPtr buffer = Marshal.AllocHGlobal(checked((end - start + 1) * 2));
                        try
                        {
                            var range = new TextRange { Start = start, End = end, Text = buffer };
                            int copied = SendMessage(Handle, 0x044B, IntPtr.Zero, ref range).ToInt32();
                            string target = Marshal.PtrToStringUni(buffer, copied);
                            if (DiagramPreviewDialog.LinkIndex(target) >= 0)
                            {
                                OnLinkClicked(new LinkClickedEventArgs(target));
                                m.Result = new IntPtr(1);
                                return;
                            }
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                    }
                }
            }
            base.WndProc(ref m);
        }

        [StructLayout(LayoutKind.Sequential)] private struct TextRange { public int Start, End; public IntPtr Text; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, ref TextRange range);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    }

    internal static class RichTextView
    {
        // RichEdit selections include hidden table/field characters that RichTextBox.Text omits.
        internal static int Length(RichTextBox view)
        {
            var options = new TextLength { Flags = 10, CodePage = 1200 };
            return SendMessage(view.Handle, 0x045F, ref options, IntPtr.Zero).ToInt32();
        }

        internal static Point Position(RichTextBox view, int index)
        {
            Point point = Point.Empty;
            SendMessage(view.Handle, 0x0426, ref point, new IntPtr(index));
            return point;
        }

        [StructLayout(LayoutKind.Sequential)] private struct TextLength { public int Flags, CodePage; }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, ref TextLength options, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, ref Point point, IntPtr parameter);
    }
}
