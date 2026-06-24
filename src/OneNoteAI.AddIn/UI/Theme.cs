using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    /// <summary>
    /// Shared OneNote-style theme colors and helpers used by all dialogs.
    /// Palette inspired by OneNote's purple section tabs.
    /// </summary>
    public static class Theme
    {
        // ── Primary palette ──
        public static readonly Color Purple        = Color.FromArgb(128, 57, 123);
        public static readonly Color PurpleLight   = Color.FromArgb(149, 97, 166);
        public static readonly Color PurplePale    = Color.FromArgb(240, 232, 244);
        public static readonly Color PurpleHover   = Color.FromArgb(200, 190, 210);

        // ── Backgrounds ──
        public static readonly Color BgPage        = Color.FromArgb(248, 249, 252);
        public static readonly Color BgCard        = Color.White;
        public static readonly Color BgCardBorder  = Color.FromArgb(224, 224, 230);

        // ── Text ──
        public static readonly Color TextPrimary   = Color.FromArgb(42, 42, 50);
        public static readonly Color TextSecondary  = Color.FromArgb(100, 100, 115);
        public static readonly Color TextMuted     = Color.FromArgb(140, 140, 155);

        // ── Accents ──
        public static readonly Color AccentRed     = Color.FromArgb(210, 60, 75);
        public static readonly Color AccentRedHover = Color.FromArgb(185, 40, 55);

        // ── Fonts ──
        public static readonly Font FontBase       = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontContent    = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FontHeading    = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FontTitle      = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);

        /// <summary>Applies standard OneNote theme to a Form.</summary>
        public static void ApplyTo(Form form)
        {
            form.BackColor = BgPage;
            form.Font = FontBase;
        }

        /// <summary>Creates a gradient header panel (OneNote purple).</summary>
        public static Panel CreateHeader(string title, int height = 48)
        {
            Panel panel = new Panel { Dock = DockStyle.Top, Height = height };
            panel.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    panel.ClientRectangle, Purple, PurpleLight, LinearGradientMode.Horizontal))
                {
                    pe.Graphics.FillRectangle(brush, panel.ClientRectangle);
                }
            };

            Label lbl = new Label
            {
                Text = "  ✦  " + (title ?? ""),
                ForeColor = Color.White,
                Font = FontTitle,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            panel.Controls.Add(lbl);
            return panel;
        }

        /// <summary>Creates the left accent stripe.</summary>
        public static Panel CreateAccentStripe()
        {
            return new Panel { Dock = DockStyle.Left, Width = 5, BackColor = Purple };
        }

        /// <summary>Creates a themed primary button (purple bg, white text).</summary>
        public static Button CreatePrimaryButton(string text)
        {
            return CreateButton(text, Color.White, Purple, PurpleLight);
        }

        /// <summary>Creates a themed secondary button (light bg).</summary>
        public static Button CreateSecondaryButton(string text)
        {
            return CreateButton(text, TextPrimary, PurplePale, PurpleHover);
        }

        /// <summary>Creates a themed ghost/link button (no bg).</summary>
        public static Button CreateGhostButton(string text)
        {
            Button btn = CreateButton(text, TextSecondary, Color.Transparent, PurplePale);
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        /// <summary>Creates a cancel/danger button (red).</summary>
        public static Button CreateDangerButton(string text)
        {
            return CreateButton(text, Color.White, AccentRed, AccentRedHover);
        }

        private static Button CreateButton(string text, Color fore, Color back, Color hover)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(110, 36),
                FlatStyle = FlatStyle.Flat,
                ForeColor = fore,
                BackColor = back,
                Font = FontBase,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = back == Color.Transparent
                ? BgCardBorder
                : Color.FromArgb(
                    System.Math.Max(0, back.R - 15),
                    System.Math.Max(0, back.G - 15),
                    System.Math.Max(0, back.B - 15));
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = hover;
            return btn;
        }
    }
}
