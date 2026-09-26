using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    public class PromptDialog : DpiAwareForm
    {
        private readonly Label _lblInstruction;
        private readonly TextBox _txtInput;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;
        private readonly string _placeholder;

        public string UserInput
        {
            get
            {
                if (IsPlaceholderActive())
                {
                    return string.Empty;
                }

                return (_txtInput.Text ?? string.Empty).Trim();
            }
        }

        public PromptDialog(string title, string instructionText, string placeholder = null)
        {
            _placeholder = placeholder;

            Text = string.IsNullOrWhiteSpace(title) ? "输入" : title;
            ClientSize = new Size(540, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            Theme.ApplyTo(this);

            // ── Header ──
            Panel header = Theme.CreateHeader(string.IsNullOrWhiteSpace(title) ? "输入" : title, 42);
            Controls.Add(header);

            // ── Instruction label ──
            _lblInstruction = new Label
            {
                AutoSize = false,
                Location = new Point(24, 56),
                Size = new Size(492, 40),
                Text = string.IsNullOrWhiteSpace(instructionText) ? "请输入内容：" : instructionText,
                ForeColor = Theme.TextPrimary,
                Font = Theme.FontContent
            };

            // ── Input box with card-like border ──
            Panel inputCard = new Panel
            {
                Location = new Point(24, 100),
                Size = new Size(492, 158),
                BackColor = Theme.BgCardBorder
            };

            _txtInput = new TextBox
            {
                Location = new Point(1, 1),
                Size = new Size(490, 156),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = true,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = Theme.FontContent
            };
            inputCard.Controls.Add(_txtInput);

            // ── Buttons ──
            _btnOk = Theme.CreatePrimaryButton("确定");
            _btnOk.Location = new Point(312, 278);
            _btnOk.Size = new Size(96, 36);
            _btnOk.DialogResult = DialogResult.OK;

            _btnCancel = Theme.CreateSecondaryButton("取消");
            _btnCancel.Location = new Point(416, 278);
            _btnCancel.Size = new Size(96, 36);
            _btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(_lblInstruction);
            Controls.Add(inputCard);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            ApplyPlaceholder();

            _txtInput.Enter += OnInputEnter;
            _txtInput.Leave += OnInputLeave;
            _txtInput.KeyDown += OnInputKeyDown;
            Shown += OnDialogShown;
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (!IsPlaceholderActive() && !string.IsNullOrWhiteSpace(_txtInput.Text))
                {
                    _btnOk.PerformClick();
                }
            }
        }

        private void OnDialogShown(object sender, EventArgs e)
        {
            ForceForeground.Apply(this);
            _txtInput.Focus();
            if (!IsPlaceholderActive())
            {
                _txtInput.SelectionStart = _txtInput.TextLength;
            }
        }

        private void OnInputEnter(object sender, EventArgs e)
        {
            if (!IsPlaceholderActive())
            {
                return;
            }

            _txtInput.Text = string.Empty;
            _txtInput.ForeColor = SystemColors.WindowText;
        }

        private void OnInputLeave(object sender, EventArgs e)
        {
            ApplyPlaceholder();
        }

        private void ApplyPlaceholder()
        {
            if (string.IsNullOrWhiteSpace(_placeholder))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_txtInput.Text) && !IsPlaceholderActive())
            {
                return;
            }

            _txtInput.Text = _placeholder;
            _txtInput.ForeColor = SystemColors.GrayText;
        }

        private bool IsPlaceholderActive()
        {
            return !string.IsNullOrWhiteSpace(_placeholder)
                && string.Equals(_txtInput.Text, _placeholder, StringComparison.Ordinal)
                && _txtInput.ForeColor == SystemColors.GrayText;
        }
    }
}
