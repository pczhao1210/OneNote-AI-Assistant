using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    public class ResultDialog : Form
    {
        // Win32 �?used to suspend/resume painting of the RichTextBox while we
        // rebuild its content during streaming. Without this every token causes
        // a full repaint + scrollbar flicker.
        private const int WM_SETREDRAW = 0x000B;
        private const int WM_USER = 0x400;
        private const int EM_HIDESELECTION = WM_USER + 63;
        private const int EM_GETEVENTMASK = WM_USER + 59;
        private const int EM_SETEVENTMASK = WM_USER + 69;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly RichTextBox _rtbResult;
        private readonly Panel _buttonPanel;
        private readonly Button _btnInsert;
        private readonly Button _btnCopy;
        private readonly Button _btnRegenerate;
        private readonly Button _btnFollowUp;
        private readonly Button _btnClose;

        // Streaming flicker control: coalesce token-level updates so we redraw
        // at most ~25 fps instead of once per network chunk.
        private readonly Timer _renderTimer;
        private bool _dirty;
        private bool _streamComplete;

        public string FullText { get; private set; } = string.Empty;

        public bool InsertRequested { get; private set; }

        /// <summary>
        /// Optional callback invoked when the user clicks "重新生成".
        /// Commands set this to a delegate that re-issues the LLM request with
        /// the same parameters. If null, the button stays hidden.
        ///
        /// The handler is responsible for calling <see cref="ResetForRegenerate"/>
        /// before streaming new tokens.
        /// </summary>
        public Action OnRegenerate { get; set; }

        /// <summary>
        /// Optional callback invoked when the user clicks "继续提问".
        /// Used by the QA command to support multi-turn dialogue. The handler
        /// should prompt for a follow-up question and stream the answer via
        /// <see cref="AppendText"/> �?the dialog will NOT clear existing text,
        /// so the conversation accumulates. If null, the button stays hidden.
        /// </summary>
        public Action OnFollowUp { get; set; }

        public ResultDialog(string title)
        {
            Text = string.IsNullOrWhiteSpace(title) ? "AI 结果" : title;
            ClientSize = new Size(600, 500);
            MinimumSize = new Size(520, 400);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            TopMost = true;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Shown += delegate
            {
                ForceForeground.Apply(this);
                UpdateRegenerateButton();
            };
            Deactivate += delegate
            {
                if (TopMost)
                {
                    TopMost = false;
                }
            };

            _rtbResult = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                HideSelection = true,
                DetectUrls = true,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point)
            };

            _buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(12, 10, 12, 10)
            };

            _btnInsert = new Button
            {
                Text = "插入到页�?,
                Size = new Size(110, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            _btnInsert.Click += OnInsertClick;

            _btnCopy = new Button
            {
                Text = "复制",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            _btnCopy.Click += OnCopyClick;

            _btnRegenerate = new Button
            {
                Text = "重新生成",
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Visible = false,
                Enabled = false
            };
            _btnRegenerate.Click += OnRegenerateClick;

            _btnFollowUp = new Button
            {
                Text = "继续提问",
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Visible = false,
                Enabled = false
            };
            _btnFollowUp.Click += OnFollowUpClick;

            _btnClose = new Button
            {
                Text = "关闭",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                DialogResult = DialogResult.Cancel
            };
            _btnClose.Click += delegate { Close(); };

            _buttonPanel.Controls.Add(_btnInsert);
            _buttonPanel.Controls.Add(_btnCopy);
            _buttonPanel.Controls.Add(_btnRegenerate);
            _buttonPanel.Controls.Add(_btnFollowUp);
            _buttonPanel.Controls.Add(_btnClose);

            Controls.Add(_rtbResult);
            Controls.Add(_buttonPanel);

            CancelButton = _btnClose;

            Resize += OnDialogResize;
            OnDialogResize(this, EventArgs.Empty);

            _renderTimer = new Timer { Interval = 80 };
            _renderTimer.Tick += OnRenderTick;
            _renderTimer.Start();

            FormClosed += delegate
            {
                _renderTimer.Stop();
                _renderTimer.Dispose();
            };
        }

        public void AppendText(string token)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendText), token);
                return;
            }

            FullText += token ?? string.Empty;
            _dirty = true;
        }

        public void SetResult(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetResult), text);
                return;
            }

            FullText = text ?? string.Empty;
            _streamComplete = true;
            _dirty = true;
            FlushRender();
            UpdateRegenerateButton();
        }

        /// <summary>
        /// Marks the stream as complete without changing the text. Commands
        /// call this when the LLM finishes streaming so the regenerate button
        /// becomes available.
        /// </summary>
        public void MarkStreamComplete()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(MarkStreamComplete));
                return;
            }
            _streamComplete = true;
            UpdateRegenerateButton();
        }

        /// <summary>
        /// Resets the dialog so a new stream of tokens can be appended. Called
        /// by the regenerate handler before re-issuing the LLM request.
        /// </summary>
        public void ResetForRegenerate()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ResetForRegenerate));
                return;
            }
            FullText = string.Empty;
            _streamComplete = false;
            _dirty = true;
            FlushRender();
            UpdateRegenerateButton();
        }

        private void UpdateRegenerateButton()
        {
            bool showRegen = OnRegenerate != null;
            _btnRegenerate.Visible = showRegen;
            _btnRegenerate.Enabled = showRegen && _streamComplete;

            bool showFollow = OnFollowUp != null;
            _btnFollowUp.Visible = showFollow;
            _btnFollowUp.Enabled = showFollow && _streamComplete;
        }

        private void OnRenderTick(object sender, EventArgs e)
        {
            if (!_dirty)
            {
                return;
            }

            FlushRender();
        }

        private void FlushRender()
        {
            _dirty = false;
            RenderMarkdown(FullText);
        }

        private void RenderMarkdown(string markdown)
        {
            markdown = markdown ?? string.Empty;

            IntPtr handle = _rtbResult.Handle;

            // Save scroll position so streaming updates don't yank the user
            // around if they've scrolled up to read earlier content.
            bool wasAtBottom = IsScrollNearBottom();

            // Suspend redraw + event notifications while we rebuild the contents.
            IntPtr eventMask = SendMessage(handle, EM_GETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
            SendMessage(handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            SendMessage(handle, EM_SETEVENTMASK, IntPtr.Zero, IntPtr.Zero);

            try
            {
                _rtbResult.Clear();

                string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
                string[] lines = normalized.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    AppendFormattedLine(lines[i]);
                    if (i < lines.Length - 1)
                    {
                        _rtbResult.AppendText(Environment.NewLine);
                    }
                }
            }
            finally
            {
                // Restore event mask + redraw, then ask Windows for ONE repaint.
                SendMessage(handle, EM_SETEVENTMASK, IntPtr.Zero, eventMask);
                SendMessage(handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);

                if (wasAtBottom || _streamComplete)
                {
                    _rtbResult.SelectionStart = _rtbResult.TextLength;
                    _rtbResult.SelectionLength = 0;
                    _rtbResult.ScrollToCaret();
                }

                _rtbResult.Invalidate();
            }
        }

        private bool IsScrollNearBottom()
        {
            // Heuristic: if the last visible char index is within ~120 chars
            // of the end, treat the user as "tailing" the stream.
            try
            {
                int firstVisible = _rtbResult.GetCharIndexFromPosition(new Point(2, _rtbResult.ClientSize.Height - 4));
                return (_rtbResult.TextLength - firstVisible) < (_rtbResult.ClientSize.Height); // crude but safe
            }
            catch
            {
                return true;
            }
        }

        private void AppendFormattedLine(string line)
        {
            string safeLine = line ?? string.Empty;
            string trimmed = safeLine.TrimStart();

            if (trimmed.Length == 0)
            {
                return;
            }

            Match headingMatch = Regex.Match(trimmed, "^(#{1,3})\\s+(.*)$");
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Value.Length;
                float size = level == 1 ? 16F : level == 2 ? 14F : 12F;
                AppendInlineFormattedText(headingMatch.Groups[2].Value, new Font("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Point), Color.FromArgb(32, 43, 63));
                return;
            }

            Match numberedMatch = Regex.Match(trimmed, "^(\\d+)\\.\\s+(.*)$");
            if (numberedMatch.Success)
            {
                AppendBulletPrefix(numberedMatch.Groups[1].Value + ". ", false);
                AppendInlineFormattedText(numberedMatch.Groups[2].Value, new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point), Color.Black);
                return;
            }

            Match bulletMatch = Regex.Match(trimmed, "^[-•]\\s+(.*)$");
            if (bulletMatch.Success)
            {
                AppendBulletPrefix("�?", true);
                AppendInlineFormattedText(bulletMatch.Groups[1].Value, new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point), Color.Black);
                return;
            }

            AppendInlineFormattedText(safeLine, new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point), Color.Black);
        }

        private void AppendBulletPrefix(string prefix, bool highlight)
        {
            Font font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            Color color = highlight ? Color.FromArgb(0, 120, 215) : Color.FromArgb(80, 80, 80);

            _rtbResult.SelectionStart = _rtbResult.TextLength;
            _rtbResult.SelectionLength = 0;
            _rtbResult.SelectionFont = font;
            _rtbResult.SelectionColor = color;
            _rtbResult.AppendText(prefix);
        }

        private void AppendInlineFormattedText(string text, Font baseFont, Color baseColor)
        {
            string input = text ?? string.Empty;

            if (input.Length == 0)
            {
                return;
            }

            Regex regex = new Regex("(\\*\\*.+?\\*\\*|\\*.+?\\*)");
            MatchCollection matches = regex.Matches(input);
            int currentIndex = 0;

            foreach (Match match in matches)
            {
                if (match.Index > currentIndex)
                {
                    AppendSegment(input.Substring(currentIndex, match.Index - currentIndex), baseFont, baseColor);
                }

                string value = match.Value;
                if (value.StartsWith("**", StringComparison.Ordinal) && value.EndsWith("**", StringComparison.Ordinal) && value.Length >= 4)
                {
                    AppendSegment(value.Substring(2, value.Length - 4), new Font(baseFont, baseFont.Style | FontStyle.Bold), baseColor);
                }
                else if (value.StartsWith("*", StringComparison.Ordinal) && value.EndsWith("*", StringComparison.Ordinal) && value.Length >= 2)
                {
                    AppendSegment(value.Substring(1, value.Length - 2), new Font(baseFont, baseFont.Style | FontStyle.Italic), baseColor);
                }
                else
                {
                    AppendSegment(value, baseFont, baseColor);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < input.Length)
            {
                AppendSegment(input.Substring(currentIndex), baseFont, baseColor);
            }
        }

        private void AppendSegment(string text, Font font, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _rtbResult.SelectionStart = _rtbResult.TextLength;
            _rtbResult.SelectionLength = 0;
            _rtbResult.SelectionFont = font;
            _rtbResult.SelectionColor = color;
            _rtbResult.AppendText(text);
        }

        private void OnInsertClick(object sender, EventArgs e)
        {
            InsertRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnRegenerateClick(object sender, EventArgs e)
        {
            Action handler = OnRegenerate;
            if (handler == null) return;

            // Disable until the new stream completes; the handler is expected
            // to call ResetForRegenerate(), then stream tokens, then either
            // SetResult() or MarkStreamComplete() �?both re-enable the button.
            _btnRegenerate.Enabled = false;
            _btnFollowUp.Enabled = false;
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Msg.Show("重新生成失败�? + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnRegenerate.Enabled = _streamComplete;
                _btnFollowUp.Enabled = _streamComplete && OnFollowUp != null;
            }
        }

        private void OnFollowUpClick(object sender, EventArgs e)
        {
            Action handler = OnFollowUp;
            if (handler == null) return;

            _btnRegenerate.Enabled = false;
            _btnFollowUp.Enabled = false;
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Msg.Show("追问失败�? + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnRegenerate.Enabled = _streamComplete && OnRegenerate != null;
                _btnFollowUp.Enabled = _streamComplete;
            }
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FullText))
            {
                return;
            }

            try
            {
                Clipboard.SetText(FullText);
            }
            catch (Exception ex)
            {
                Msg.Show("复制失败�? + ex.Message, "OneNote Copilot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnDialogResize(object sender, EventArgs e)
        {
            int top = 10;
            _btnClose.Location = new Point(_buttonPanel.ClientSize.Width - _btnClose.Width, top);
            _btnCopy.Location = new Point(_btnClose.Left - 10 - _btnCopy.Width, top);
            _btnInsert.Location = new Point(_btnCopy.Left - 10 - _btnInsert.Width, top);
            _btnRegenerate.Location = new Point(0, top);
            _btnFollowUp.Location = new Point(_btnRegenerate.Right + 10, top);
        }
    }
}
