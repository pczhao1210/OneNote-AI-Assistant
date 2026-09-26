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

        /// <summary>Tag definitions at page level (index → TagDef mapping).</summary>
        public List<TagDef> TagDefs { get; set; } = new List<TagDef>();

        public string GetPlainText()
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                Outlines
                    .SelectMany(outline => outline.TextBlocks)
                    .Select(block => block.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        /// <summary>
        /// Returns all text blocks that have a To-Do tag (completed or not).
        /// </summary>
        public List<TextBlock> GetTaggedTodos()
        {
            return Outlines
                .SelectMany(o => o.TextBlocks)
                .Where(b => b.Tag != null && b.Tag.IsToDoTag)
                .ToList();
        }

        /// <summary>
        /// Returns plain text of blocks that do NOT have any tag (for AI analysis).
        /// </summary>
        public string GetUntaggedText()
        {
            return string.Join(
                Environment.NewLine,
                Outlines
                    .SelectMany(o => o.TextBlocks)
                    .Where(b => b.Tag == null && !string.IsNullOrWhiteSpace(b.Text))
                    .Select(b => b.Text));
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

        public int TableColumn { get; set; }
        public bool EndsTableRow { get; set; }

        /// <summary>Tag applied to this text block (null if none).</summary>
        public TagInfo Tag { get; set; }
    }

    /// <summary>
    /// Represents a tag definition at the page level.
    /// OneNote defines tags like: TagDef index="0" name="To Do" type="0" symbol="3"
    /// </summary>
    public class TagDef
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Type { get; set; }
        public int Symbol { get; set; }
    }

    /// <summary>
    /// Represents a tag applied to a specific OE element.
    /// </summary>
    public class TagInfo
    {
        public int Index { get; set; }
        public bool Completed { get; set; }
        public string TagName { get; set; }

        /// <summary>
        /// True if this tag represents any kind of actionable/checkbox item.
        /// In OneNote, any tag with a completed attribute is a checkbox-type tag.
        /// We treat ALL tags as relevant for todo extraction — the green checkmark,
        /// the checkbox, priority tags, follow-up tags, etc.
        /// </summary>
        public bool IsToDoTag
        {
            get
            {
                // All tags are relevant. OneNote uses Tag element with completed
                // attribute for any checkbox-like marker (To Do, priority, follow-up, etc.)
                return true;
            }
        }
    }
}
