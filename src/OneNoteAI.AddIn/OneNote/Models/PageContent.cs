using System;
using System.Collections.Generic;
using System.Linq;

namespace OneNoteAI.OneNote.Models
{
    public class PageContent
    {
        public string PageId { get; set; }

        public string Title { get; set; }

        public DateTime DateCreated { get; set; }

        public DateTime DateModified { get; set; }

        public List<OutlineContent> Outlines { get; set; } = new List<OutlineContent>();

        public string GetPlainText()
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                Outlines
                    .SelectMany(outline => outline.TextBlocks)
                    .Select(block => block.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }
    }

    public class OutlineContent
    {
        public string OutlineId { get; set; }

        public List<TextBlock> TextBlocks { get; set; } = new List<TextBlock>();
    }

    public class TextBlock
    {
        public string ElementId { get; set; }

        public string Text { get; set; }

        public string RawHtml { get; set; }

        public int IndentLevel { get; set; }
    }
}
