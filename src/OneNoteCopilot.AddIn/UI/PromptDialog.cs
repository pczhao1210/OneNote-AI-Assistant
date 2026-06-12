using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneNoteCopilot.UI
{
    public class PromptDialog : Form
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
            ClientSize = new Size(500, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _lblInstruction = new Label
            {
                AutoSize = false,
                Location = new Point(18, 18),
                Size = new Size(464, 40),
                Text = string.IsNullOrWhiteSpace(instructionText) ? "请输入内容：" : instructionText
            };

            _txtInput = new TextBox
            {
                Location = new Point(18, 66),
                Size = new Size(464, 158),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = true,
                WordWrap = true
            };

            _btnOk = new Button
            {
                Text = "确定",
                Size = new Size(90, 30),
                Location = new Point(296, 246),
                DialogResult = DialogResult.OK
            };

            _btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(90, 30),
                Location = new Point(392, 246),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(_lblInstruction);
            Controls.Add(_txtInput);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            ApplyPlaceholder();

            _txtInput.Enter += OnInputEnter;
            _txtInput.Leave += OnInputLeave;
            Shown += OnDialogShown;
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
