using System;
using System.Drawing;
using System.Windows.Forms;
using OneNoteCopilot.OneNote;

namespace OneNoteCopilot.UI
{
    /// <summary>
    /// Lets the user choose whether an AI command runs on the current page
    /// alone or on every page in the current section. Shown by features that
    /// support multi-page processing (Summarize, ExtractTodos).
    /// </summary>
    public class ScopeDialog : Form
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
            ClientSize = new Size(420, 200);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Label lblHeader = new Label
            {
                AutoSize = false,
                Location = new Point(18, 16),
                Size = new Size(380, 22),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "请选择处理范围："
            };

            _rbPage = new RadioButton
            {
                AutoSize = false,
                Location = new Point(24, 50),
                Size = new Size(374, 24),
                Text = "仅当前页面",
                Checked = defaultScope == ScopeKind.CurrentPage
            };

            string sectionLabel = string.IsNullOrWhiteSpace(sectionName)
                ? string.Format("当前分区（共 {0} 页）", sectionPageCount)
                : string.Format("当前分区 \"{0}\"（共 {1} 页）", sectionName, sectionPageCount);

            _rbSection = new RadioButton
            {
                AutoSize = false,
                Location = new Point(24, 82),
                Size = new Size(374, 24),
                Text = sectionLabel,
                Checked = defaultScope == ScopeKind.CurrentSection,
                Enabled = sectionPageCount > 0
            };

            Label lblHint = new Label
            {
                AutoSize = false,
                Location = new Point(24, 110),
                Size = new Size(374, 36),
                ForeColor = Color.FromArgb(110, 118, 130),
                Text = "提示：分区范围会逐页处理后汇总，耗时与页数成正比。"
            };

            _btnOk = new Button
            {
                Text = "确定",
                Size = new Size(90, 30),
                Location = new Point(216, 156),
                DialogResult = DialogResult.OK
            };

            _btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(90, 30),
                Location = new Point(312, 156),
                DialogResult = DialogResult.Cancel
            };

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
