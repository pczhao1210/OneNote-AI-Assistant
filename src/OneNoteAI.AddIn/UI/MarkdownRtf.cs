using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace OneNoteAI.UI
{
    internal static class MarkdownRtf
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables().UseTaskLists().UseEmphasisExtras().Build();

        public static string Render(string markdown, Font font, int width = 9000, IList<string> diagrams = null) =>
            RenderSections(new[] { markdown }, font, width, diagrams);

        public static int AvailableWidth(RichTextBox view)
        {
            using (Graphics graphics = view.CreateGraphics())
                return (int)(Math.Max(80, Math.Min(view.RightMargin > 0 ? view.RightMargin : int.MaxValue, view.ClientSize.Width - 16)) * 1440 / graphics.DpiX);
        }

        // Parse each message independently: an unfinished code fence must not consume the next turn.
        public static string RenderSections(IEnumerable<string> sections, Font font, int width = 9000, IList<string> diagrams = null)
        {
            var writer = new Writer(font, width, diagrams);
            foreach (string section in sections)
                writer.Blocks(Markdown.Parse(section ?? "", Pipeline), 0);
            return writer.Finish();
        }

        private sealed class Writer
        {
            private readonly StringBuilder _rtf = new StringBuilder();
            private readonly int _size, _width;
            private readonly IList<string> _diagrams;

            public Writer(Font font, int width, IList<string> diagrams)
            {
                _size = (int)Math.Round(font.SizeInPoints * 2);
                _width = Math.Max(1200, width);
                _diagrams = diagrams;
                _rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 ");
                Escape(_rtf, font.FontFamily.Name);
                _rtf.Append(@";}{\f1 Consolas;}}{\colortbl ;\red42\green42\blue50;\red128\green57\blue123;\red100\green100\blue115;\red243\green244\blue247;\red224\green224\blue230;\red240\green232\blue244;}\viewkind4\uc1 ");
            }

            public string Finish() => _rtf.Append('}').ToString();

            private void Paragraph(int indent, bool inTable = false)
            {
                _rtf.Append(@"\pard\plain\f0\cf1\fs").Append(_size)
                    .Append(@"\li").Append(indent).Append(@"\sa100 ");
                if (inTable) _rtf.Append(@"\intbl ");
            }

            public void Blocks(ContainerBlock blocks, int indent, bool inTable = false)
            {
                foreach (Block block in blocks) Block(block, indent, inTable);
            }

            private void Block(Block block, int indent, bool inTable, string marker = null)
            {
                if (block is Table table) { WriteTable(table, indent); return; }
                if (block is ListBlock list)
                {
                    int number = int.TryParse(list.OrderedStart, out int start) ? start : 1;
                    foreach (ListItemBlock item in list)
                    {
                        bool first = true;
                        foreach (Block child in item)
                        {
                            Block(child, indent + 320, inTable, first ? (list.IsOrdered ? (number++).ToString(CultureInfo.InvariantCulture) + "." : "\u2022") : null);
                            first = false;
                        }
                    }
                    return;
                }
                if (block is QuoteBlock quote)
                {
                    Blocks(quote, indent + 320, inTable);
                    return;
                }
                if (block is ContainerBlock container) { Blocks(container, indent, inTable); return; }
                Paragraph(indent, inTable);
                if (marker != null)
                {
                    _rtf.Append(@"\fi-260 ");
                    Escape(_rtf, marker);
                    _rtf.Append(' ');
                }
                if (block is HeadingBlock heading)
                {
                    _rtf.Append(@"\sb140\b\cf2\fs").Append(_size + (heading.Level < 3 ? 6 : 2)).Append(' ');
                    Inline(heading.Inline);
                }
                else if (block is CodeBlock code)
                {
                    var fenced = code as FencedCodeBlock;
                    if (!string.IsNullOrWhiteSpace(fenced?.Info))
                    {
                        _rtf.Append(@"{\cf3\fs").Append(Math.Max(14, _size - 2)).Append(' ');
                        Escape(_rtf, fenced.Info);
                        _rtf.Append(@"}\line ");
                    }
                    _rtf.Append(@"\f1\highlight4 ");
                    Escape(_rtf, code.Lines.ToString());
                    _rtf.Append(@"\highlight0 ");
                    if (_diagrams != null && string.Equals(fenced?.Info, "mermaid", StringComparison.OrdinalIgnoreCase)
                        && fenced.ClosingFencedCharCount > 0)
                    {
                        int index = _diagrams.Count;
                        _diagrams.Add(code.Lines.ToString());
                        _rtf.Append(@"\line\f0 {\field{\*\fldinst HYPERLINK ""onenoteai-diagram:")
                            .Append(index).Append(@"""}{\fldrslt{\cf2\ul ");
                        Escape(_rtf, KnowledgeUi.L("查看流程图 ", "View diagram ") + (index + 1));
                        _rtf.Append("}}}");
                    }
                }
                else if (block is ThematicBreakBlock)
                    _rtf.Append(@"\brdrb\brdrs\brdrw5\brdrcf5 ");
                else if (block is HtmlBlock html) Escape(_rtf, html.Lines.ToString());
                else if (block is LeafBlock leaf) Inline(leaf.Inline);
                _rtf.Append(@"\par ");
            }

            private void WriteTable(Table table, int indent)
            {
                int columns = table.ColumnDefinitions.Count;
                if (columns == 0) return;
                int cellWidth = Math.Max(240, (_width - indent - 120) / columns);
                foreach (TableRow row in table)
                {
                    _rtf.Append(@"\trowd\trgaph80\trleft").Append(indent).Append(' ');
                    for (int column = 0; column < columns; column++)
                    {
                        _rtf.Append(@"\clvertalt\clbrdrt\brdrs\brdrw5\brdrcf5\clbrdrl\brdrs\brdrw5\brdrcf5\clbrdrb\brdrs\brdrw5\brdrcf5\clbrdrr\brdrs\brdrw5\brdrcf5");
                        if (row.IsHeader) _rtf.Append(@"\clcbpat6");
                        _rtf.Append(@"\cellx").Append(indent + (column + 1) * cellWidth).Append(' ');
                    }
                    for (int column = 0; column < columns; column++)
                    {
                        Paragraph(0, true);
                        TableColumnAlign? alignment = table.ColumnDefinitions[column].Alignment;
                        if (alignment == TableColumnAlign.Center) _rtf.Append(@"\qc ");
                        if (alignment == TableColumnAlign.Right) _rtf.Append(@"\qr ");
                        if (row.IsHeader) _rtf.Append(@"\b ");
                        if (column < row.Count)
                            foreach (Block child in (TableCell)row[column])
                            {
                                if (child is LeafBlock leaf) Inline(leaf.Inline, true);
                            }
                        _rtf.Append(@"\cell ");
                    }
                    _rtf.Append(@"\row ");
                }
                _rtf.Append(@"\pard\plain\fs").Append(_size).Append(@"\sa80\par ");
            }

            private void Inline(ContainerInline parent, bool inTable = false)
            {
                if (parent == null) return;
                foreach (Inline item in parent)
                {
                    if (item is LiteralInline literal) Escape(_rtf, literal.Content.ToString());
                    else if (item is CodeInline code)
                    {
                        _rtf.Append(@"{\f1\highlight4 ");
                        Escape(_rtf, inTable ? code.Content.Replace("\\|", "|") : code.Content);
                        _rtf.Append('}');
                    }
                    else if (item is EmphasisInline emphasis)
                    {
                        _rtf.Append(emphasis.DelimiterChar == '~' ? @"{\strike " :
                            emphasis.DelimiterCount == 2 ? @"{\b " : @"{\i ");
                        Inline(emphasis, inTable);
                        _rtf.Append('}');
                    }
                    else if (item is LinkInline link)
                    {
                        _rtf.Append(@"{\cf2\ul ");
                        Inline(link, inTable);
                        _rtf.Append('}');
                        if (!string.IsNullOrEmpty(link.Url))
                        {
                            _rtf.Append(" (");
                            Escape(_rtf, link.Url);
                            _rtf.Append(')');
                        }
                    }
                    else if (item is AutolinkInline autolink) Escape(_rtf, autolink.Url);
                    else if (item is LineBreakInline) _rtf.Append(@"\line ");
                    else if (item is HtmlInline html)
                    {
                        if (html.Tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
                            html.Tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) ||
                            html.Tag.Equals("<br />", StringComparison.OrdinalIgnoreCase)) _rtf.Append(@"\line ");
                        else Escape(_rtf, html.Tag);
                    }
                    else if (item is TaskList task) Escape(_rtf, task.Checked ? "[x]" : "[ ]");
                    else if (item is ContainerInline child) Inline(child, inTable);
                }
            }
        }

        private static void Escape(StringBuilder rtf, string text)
        {
            foreach (char c in text)
            {
                if (c == '\\' || c == '{' || c == '}') rtf.Append('\\').Append(c);
                else if (c == '\t') rtf.Append(@"\tab ");
                else if (c == '\n') rtf.Append(@"\line ");
                else if (c == '\r') continue;
                else if (c > 127) rtf.Append(@"\u").Append(unchecked((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
                else if (!char.IsControl(c)) rtf.Append(c);
            }
        }
    }
}
