using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    public class DpiAwareForm : Form
    {
        private const int WmDpiChanged = 0x02E0;
        private const int WmNcCreate = 0x0081;
        private readonly Dictionary<Control, Font> _logicalFonts = new Dictionary<Control, Font>();
        private List<Font> _scaledFonts = new List<Font>();
        private Size _logicalMinimumSize;
        private Size _logicalMaximumSize;
        private int _dpi = DpiSupport.LogicalDpi;
        private bool _dpiInitialized;

        protected bool IsScalingForDpi { get; private set; }

        protected DpiAwareForm()
        {
            // Own scaling explicitly: Framework 4.8's automatic DPI handling
            // depends on the host EXE's configuration, which an add-in cannot set.
            AutoScaleMode = AutoScaleMode.None;
        }

        protected int ScaleLogical(int value)
        {
            return (int)Math.Round(value * (double)_dpi / DpiSupport.LogicalDpi);
        }

        protected Font CreateDpiFont(Font logicalFont)
        {
            // Pixel fonts avoid using the system monitor's DPI for GDI font handles.
            return new Font(logicalFont.FontFamily, logicalFont.SizeInPoints * _dpi / 72F,
                logicalFont.Style, GraphicsUnit.Pixel, logicalFont.GdiCharSet,
                logicalFont.GdiVerticalFont);
        }

        protected override void OnLoad(EventArgs e)
        {
            if (!_dpiInitialized)
            {
                CaptureFonts(this);
                _logicalMinimumSize = MinimumSize;
                _logicalMaximumSize = MaximumSize;
                _dpiInitialized = true;
                ApplyDpi(DpiSupport.GetWindowDpi(this));
                if (StartPosition == FormStartPosition.CenterScreen)
                {
                    CenterToScreen();
                }
                else if (StartPosition == FormStartPosition.CenterParent)
                {
                    CenterToParent();
                }
            }

            base.OnLoad(e);
        }

        private void CaptureFonts(Control control)
        {
            _logicalFonts.Add(control, control.Font);
            foreach (Control child in control.Controls)
            {
                CaptureFonts(child);
            }
        }

        private void ApplyDpi(int dpi)
        {
            if (dpi <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dpi));
            }

            float factor = dpi / (float)_dpi;
            var textViews = new List<RichTextViewState>();
            foreach (Control control in _logicalFonts.Keys)
            {
                if (control is RichTextBox textBox && textBox.IsHandleCreated)
                {
                    textViews.Add(new RichTextViewState(textBox));
                }
            }

            _dpi = dpi;
            IsScalingForDpi = true;
            SuspendLayout();
            var previousFonts = _scaledFonts;
            _scaledFonts = new List<Font>();
            try
            {
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                if (factor != 1F)
                {
                    Scale(new SizeF(factor, factor));
                }

                foreach (var entry in _logicalFonts)
                {
                    Font font = CreateDpiFont(entry.Value);
                    _scaledFonts.Add(font);
                    entry.Key.Font = font;
                }

                MinimumSize = new Size(ScaleLogical(_logicalMinimumSize.Width), ScaleLogical(_logicalMinimumSize.Height));
                MaximumSize = new Size(ScaleLogical(_logicalMaximumSize.Width), ScaleLogical(_logicalMaximumSize.Height));
            }
            finally
            {
                ResumeLayout(true);
                IsScalingForDpi = false;
                foreach (Font font in previousFonts)
                {
                    font.Dispose();
                }
            }

            OnDpiScaleChanged(EventArgs.Empty);
            foreach (RichTextViewState view in textViews)
            {
                view.Restore(factor);
            }
            Invalidate(true);
        }

        protected virtual void OnDpiScaleChanged(EventArgs e)
        {
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcCreate)
            {
                DpiSupport.EnableNonClientScaling(m.HWnd);
            }
            else if (m.Msg == WmDpiChanged)
            {
                if (_dpiInitialized)
                {
                    NativeRectangle bounds = Marshal.PtrToStructure<NativeRectangle>(m.LParam);
                    ApplyDpi((int)(m.WParam.ToInt64() & 0xffff));
                    Bounds = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                }

                // Do not let a DPI-enabled host also run WinForms' scaling pass.
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font font in _scaledFonts)
                {
                    font.Dispose();
                }
                _scaledFonts.Clear();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private sealed class RichTextViewState
        {
            private readonly RichTextBox _control;
            private readonly int _selectionStart;
            private readonly int _selectionLength;
            private Point _scrollPosition;

            internal RichTextViewState(RichTextBox control)
            {
                _control = control;
                _selectionStart = control.SelectionStart;
                _selectionLength = control.SelectionLength;
                SendMessage(control.Handle, 0x04DD /* EM_GETSCROLLPOS */, IntPtr.Zero, ref _scrollPosition);
            }

            internal void Restore(float factor)
            {
                _control.Select(_selectionStart, _selectionLength);
                var position = new Point((int)Math.Round(_scrollPosition.X * factor),
                    (int)Math.Round(_scrollPosition.Y * factor));
                SendMessage(_control.Handle, 0x04DE /* EM_SETSCROLLPOS */, IntPtr.Zero, ref position);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref Point lParam);
    }
}
