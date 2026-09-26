using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    internal sealed class ChatSendButton : Button
    {
        private bool _busy, _hover, _pressed;
        public bool Busy { get => _busy; set { _busy = value; Invalidate(); } }

        public ChatSendButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(40, 40);
            BackColor = Theme.BgCard;
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = Math.Min(ClientSize.Width, ClientSize.Height);
            float inset = size * 0.08f;
            var circle = new RectangleF((Width - size) / 2f + inset, (Height - size) / 2f + inset, size - inset * 2, size - inset * 2);
            Color fill = !Enabled ? Theme.BgCardBorder : _pressed ? Color.FromArgb(95, 38, 92) : _hover ? Color.FromArgb(109, 45, 105) : Theme.Purple;
            Color icon = Enabled ? Color.White : Theme.TextSecondary;
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, circle);
            float cx = Width / 2f, cy = Height / 2f;
            if (Busy)
            {
                using (var brush = new SolidBrush(icon))
                    g.FillRectangle(brush, cx - size * 0.12f, cy - size * 0.12f, size * 0.24f, size * 0.24f);
            }
            else
            {
                using (var pen = new Pen(icon, size * 0.055f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLine(pen, cx, cy + size * 0.18f, cx, cy - size * 0.18f);
                    g.DrawLines(pen, new[] { new PointF(cx - size * 0.14f, cy - size * 0.03f),
                        new PointF(cx, cy - size * 0.18f), new PointF(cx + size * 0.14f, cy - size * 0.03f) });
                }
            }
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Purple, Math.Max(1, size * 0.025f)) { DashStyle = DashStyle.Dot })
                    g.DrawEllipse(pen, (Width - size) / 2f + 1, (Height - size) / 2f + 1, size - 2, size - 2);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) _pressed = true; base.OnKeyDown(e); Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; base.OnKeyUp(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { _pressed = false; base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { _pressed = false; base.OnEnabledChanged(e); Invalidate(); }
    }
}
