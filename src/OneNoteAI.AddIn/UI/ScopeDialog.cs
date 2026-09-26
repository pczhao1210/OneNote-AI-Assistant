using System;
using System.Drawing;
using System.Windows.Forms;
using OneNoteAI.OneNote;

namespace OneNoteAI.UI
{
    /// <summary>
    /// Lets the user choose whether an AI command runs on the current page
    /// alone or on every page in the current section. Shown by features that
    /// support multi-page processing (Summarize, ExtractTodos).
    /// </summary>
    public class ScopeDialog : DpiAwareForm
    {
        private readonly RadioButton _rbPage;
        private readonly RadioButton _rbSection;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;

        public ScopeKind SelectedScope
        {
            get { return _rbSection.Checked ? ScopeKind.CurrentSection : ScopeKind.CurrentPage; }
        }

        public ScopeDialog(string title, string sectionName, int sectionPageCount, ScopeKind defaultScope = ScopeKind.CurrentPage)
        {
            Text = string.IsNullOrWhiteSpace(title) ? "选择范围" : title;
            ClientSize = new Size(440, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            Theme.ApplyTo(this);

            // ── Header ──
            Panel header = Theme.CreateHeader(string.IsNullOrWhiteSpace(title) ? "选择范围" : title, 42);
            Controls.Add(header);

            Label lblHeader = new Label
            {
                AutoSize = false,
                Location = new Point(24, 56),
                Size = new Size(392, 24),
                Font = Theme.FontHeading,
                ForeColor = Theme.TextPrimary,
                Text = "请选择处理范围："
            };

            _rbPage = new RadioButton
            {
                AutoSize = false,
                Location = new Point(30, 90),
                Size = new Size(380, 26),
                Text = "仅当前页面",
                Font = Theme.FontContent,
                ForeColor = Theme.TextPrimary,
                Checked = defaultScope == ScopeKind.CurrentPage
            };

            string sectionLabel = string.IsNullOrWhiteSpace(sectionName)
                ? string.Format("当前分区（共 {0} 页）", sectionPageCount)
                : string.Format("当前分区「{0}」（共 {1} 页）", sectionName, sectionPageCount);

            _rbSection = new RadioButton
            {
                AutoSize = false,
                Location = new Point(30, 122),
                Size = new Size(380, 26),
                Text = sectionLabel,
                Font = Theme.FontContent,
                ForeColor = Theme.TextPrimary,
                Checked = defaultScope == ScopeKind.CurrentSection,
                Enabled = sectionPageCount > 0
            };

            Label lblHint = new Label
            {
                AutoSize = false,
                Location = new Point(30, 156),
                Size = new Size(380, 36),
                ForeColor = Theme.TextMuted,
                Text = "提示：分区范围会逐页处理后汇总，耗时与页数成正比。"
            };

            _btnOk = Theme.CreatePrimaryButton("确定");
            _btnOk.Location = new Point(236, 200);
            _btnOk.Size = new Size(90, 34);
            _btnOk.DialogResult = DialogResult.OK;

            _btnCancel = Theme.CreateSecondaryButton("取消");
            _btnCancel.Location = new Point(334, 200);
            _btnCancel.Size = new Size(90, 34);
            _btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(lblHeader);
            Controls.Add(_rbPage);
            Controls.Add(_rbSection);
            Controls.Add(lblHint);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Shown += delegate { ForceForeground.Apply(this); };
        }
    }
}
