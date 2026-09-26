using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OneNoteAI.Knowledge;
using OneNoteAI.Mcp;

namespace OneNoteAI.UI
{
    internal static class KnowledgeUi
    {
        public static string L(string chinese, string english) => Strings.IsChinese ? chinese : english;

        public static string ReadSecret(string encrypted, IWin32Window owner)
        {
            try { return Settings.EncryptionHelper.Decrypt(encrypted); }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException || ex is FormatException)
            {
                Logging.Logger.Warn("Credential could not be decrypted: " + ex.GetType().Name);
                MessageBox.Show(owner, L("此凭据无法在当前 Windows 用户下解密，请重新输入。其他设置会保留。",
                    "This credential cannot be decrypted for the current Windows user. Re-enter it; other settings are preserved."),
                    "OneNote AI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return "";
            }
        }

        public static Label Field(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 12, 8) };
            table.Controls.Add(caption, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 5, 3, 5);
            table.Controls.Add(control, 1, row);
            return caption;
        }

        public static TableLayoutPanel Fields() => new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12),
            ColumnStyles = { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100) }
        };

        public static void TextViewer(IWin32Window owner, string title, string text)
        {
            using (var dialog = new Form { Text = title, Size = new Size(800, 620), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false })
            {
                Theme.ApplyTo(dialog);
                dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                    WordWrap = true, Font = Theme.FontContent, Text = text, BackColor = Theme.BgCard });
                dialog.ShowDialog(owner);
            }
        }

        public static Task<bool> ApproveAsync(IWin32Window owner, ToolApproval request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            UiThread.Post(() =>
            {
                if (token.IsCancellationRequested) { completion.TrySetCanceled(); return; }
                using (var dialog = new Form { Text = L("批准 MCP 调用", "Approve MCP call"), Size = new Size(730, 580),
                    StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
                {
                    Theme.ApplyTo(dialog);
                    var header = new Label { Dock = DockStyle.Top, Height = 88, Padding = new Padding(12), Text =
                        request.Server + " / " + request.Tool + "\nCall: " + request.CallId + "\nSchema: " + request.SchemaHash };
                    var args = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                        WordWrap = false, Font = new Font("Consolas", 10), Text = request.Arguments };
                    var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
                    Button yes = Theme.CreatePrimaryButton(L("批准本次调用", "Approve this call"));
                    Button no = Theme.CreateSecondaryButton(L("拒绝", "Decline"));
                    yes.Width = 140;
                    yes.DialogResult = DialogResult.OK;
                    no.DialogResult = DialogResult.Cancel;
                    footer.Controls.AddRange(new Control[] { no, yes });
                    dialog.Controls.Add(args);
                    dialog.Controls.Add(header);
                    dialog.Controls.Add(footer);
                    dialog.CancelButton = no;
                    dialog.ActiveControl = no;
                    using (token.Register(() => UiThread.Post(() => { if (!dialog.IsDisposed) dialog.Close(); })))
                        completion.TrySetResult(dialog.ShowDialog(owner) == DialogResult.OK && !token.IsCancellationRequested);
                }
            });
            return completion.Task;
        }
    }

    internal sealed class KnowledgeScopeTree : TreeView
    {
        private bool _updating;
        public KnowledgeScopeTree()
        {
            CheckBoxes = true;
            Dock = DockStyle.Fill;
            HideSelection = false;
            ShowNodeToolTips = true;
            BorderStyle = BorderStyle.FixedSingle;
            BeforeCheck += (sender, e) => { if (!_updating && !e.Node.Checked && ((NoteNode)e.Node.Tag).Unavailable) e.Cancel = true; };
            AfterCheck += (sender, e) =>
            {
                if (_updating) return;
                _updating = true;
                try
                {
                    foreach (TreeNode child in Flatten(e.Node.Nodes)) child.Checked = e.Node.Checked && !((NoteNode)child.Tag).Unavailable;
                    if (!e.Node.Checked)
                        for (TreeNode parent = e.Node.Parent; parent != null; parent = parent.Parent) parent.Checked = false;
                }
                finally { _updating = false; }
                ScopeChanged?.Invoke(this, EventArgs.Empty);
            };
        }
        public event EventHandler ScopeChanged;
        public List<string> CheckedRoots => Flatten(Nodes).Where(n => n.Checked && !Ancestors(n).Any(p => p.Checked)).Select(n => ((NoteNode)n.Tag).Id).ToList();

        public void Populate(NoteNode root, IEnumerable<string> selected, HashSet<string> allowed = null)
        {
            var ids = new HashSet<string>(selected, StringComparer.Ordinal);
            _updating = true;
            BeginUpdate();
            try
            {
                Nodes.Clear();
                foreach (NoteNode child in root.Children)
                {
                    TreeNode node = Build(child, ids, allowed, false);
                    if (node != null) Nodes.Add(node);
                }
                foreach (TreeNode node in Nodes) node.Expand();
                foreach (TreeNode node in Flatten(Nodes).Where(n => ids.Contains(((NoteNode)n.Tag).Id)))
                    foreach (TreeNode parent in Ancestors(node)) parent.Expand();
            }
            finally { EndUpdate(); _updating = false; }
        }

        private static TreeNode Build(NoteNode source, HashSet<string> selected, HashSet<string> allowed, bool parentChecked)
        {
            if (source.Kind == "Page") return null;
            bool check = parentChecked || selected.Contains(source.Id);
            var node = new TreeNode(source.Name + (source.Unavailable ? KnowledgeUi.L("（不可访问）", " (unavailable)") : ""))
            { Tag = source, ToolTipText = source.Path + "\n" + source.Id, Checked = check && !source.Unavailable, ForeColor = source.Unavailable ? Theme.TextMuted : Theme.TextPrimary };
            foreach (NoteNode child in source.Children)
            {
                TreeNode nested = Build(child, selected, allowed, check);
                if (nested != null) node.Nodes.Add(nested);
            }
            if (allowed != null && (source.Kind == "Section" ? !allowed.Contains(source.Id) : node.Nodes.Count == 0)) return null;
            return node;
        }

        private static IEnumerable<TreeNode> Ancestors(TreeNode node)
        {
            for (TreeNode parent = node.Parent; parent != null; parent = parent.Parent) yield return parent;
        }
        private static IEnumerable<TreeNode> Flatten(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;
                foreach (TreeNode child in Flatten(node.Nodes)) yield return child;
            }
        }
    }
}
