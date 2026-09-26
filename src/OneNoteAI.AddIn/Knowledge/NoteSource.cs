using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Office.Interop.OneNote;
using OneNoteAI.OneNote;
using OneNoteAI.OneNote.Models;
using OneNoteAI.UI;

namespace OneNoteAI.Knowledge
{
    public sealed class NoteNode
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string SectionId { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Path { get; set; }
        public string Version { get; set; }
        public bool Unavailable { get; set; }
        public List<NoteNode> Children { get; } = new List<NoteNode>();

        public IEnumerable<NoteNode> DescendantsAndSelf()
        {
            yield return this;
            foreach (NoteNode child in Children)
                foreach (NoteNode item in child.DescendantsAndSelf()) yield return item;
        }

        public override string ToString() => Name;

        public static NoteNode Parse(string xml)
        {
            XElement root = XDocument.Parse(xml).Root ?? throw new InvalidOperationException("Empty OneNote hierarchy.");
            return ParseElement(root, null, "", "", false);
        }

        private static NoteNode ParseElement(XElement element, string parent, string section, string path, bool blocked)
        {
            string kind = element.Name.LocalName;
            string id = (string)element.Attribute("ID") ?? "";
            string name = (string)element.Attribute("name") ?? kind;
            var node = new NoteNode { Id = id, ParentId = parent, Kind = kind, Name = name,
                SectionId = kind == "Section" ? id : section, Path = string.IsNullOrEmpty(path) ? name : path + " / " + name,
                Version = NormalizeVersion((string)element.Attribute("lastModifiedTime")),
                Unavailable = blocked || new[] { "isLocked", "locked", "isRecycleBin", "isInRecycleBin", "isDeleted" }
                    .Any(a => string.Equals((string)element.Attribute(a), "true", StringComparison.OrdinalIgnoreCase)) };
            foreach (XElement child in element.Elements().Where(e => new[] { "Notebook", "SectionGroup", "Section", "Page" }.Contains(e.Name.LocalName)))
                node.Children.Add(ParseElement(child, id, node.SectionId, kind == "Notebooks" ? "" : node.Path, node.Unavailable));
            return node;
        }

        public static string NormalizeVersion(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset date)
                ? date.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) : "";

        public static HashSet<string> Sections(NoteNode root, IEnumerable<string> selected, bool includeUnavailable = false)
        {
            var ids = new HashSet<string>(selected ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return new HashSet<string>(root.DescendantsAndSelf().Where(n => ids.Contains(n.Id))
                .SelectMany(n => n.DescendantsAndSelf()).Where(n => n.Kind == "Section" && (includeUnavailable || !n.Unavailable))
                .Select(n => n.Id), StringComparer.Ordinal);
        }
    }

    public interface INoteSource
    {
        NoteNode Hierarchy();
        NoteNode PageState(string pageId);
        PageContent ReadPage(string pageId);
        IReadOnlyList<string> Search(string scopeId, string query);
        void Navigate(string pageId, string blockId);
    }

    public static class NoteReader
    {
        public static (PageContent Page, NoteNode State) ReadStable(INoteSource source, string pageId, CancellationToken token,
            ISet<string> allowedSections = null)
        {
            token.ThrowIfCancellationRequested();
            NoteNode before = source.PageState(pageId);
            if (before.Unavailable || (allowedSections != null && !allowedSections.Contains(before.SectionId)))
                throw new InvalidOperationException("The page is unavailable or outside the selected scope.");
            PageContent page = source.ReadPage(pageId);
            token.ThrowIfCancellationRequested();
            PageContent verified = source.ReadPage(pageId);
            NoteNode after = source.PageState(pageId);
            token.ThrowIfCancellationRequested();
            // Hierarchy and content timestamps are different OneNote revision clocks.
            if (after.Unavailable || (allowedSections != null && !allowedSections.Contains(after.SectionId)) ||
                before.SectionId != after.SectionId || before.Version != after.Version ||
                page.PageId != pageId || verified.PageId != pageId ||
                !SameContent(page, verified))
                throw new InvalidOperationException("The page changed while reading. Try again.");
            return (verified, after);
        }

        public static bool SameContent(PageContent first, PageContent second) =>
            first.PageId == second.PageId && first.Title == second.Title &&
            first.DateModified.ToUniversalTime() == second.DateModified.ToUniversalTime() &&
            first.Outlines.SelectMany(o => o.TextBlocks).Select(b => (b.ElementId, b.Text, b.TableColumn, b.EndsTableRow))
                .SequenceEqual(second.Outlines.SelectMany(o => o.TextBlocks).Select(b => (b.ElementId, b.Text, b.TableColumn, b.EndsTableRow)));
    }

    internal sealed class OneNoteSource : INoteSource
    {
        private readonly OneNoteProvider _provider;
        public OneNoteSource() { _provider = UiThread.Send(() => new OneNoteProvider()); }
        public NoteNode Hierarchy() => NoteNode.Parse(UiThread.Send(() =>
        {
            _provider.App.GetHierarchy("", HierarchyScope.hsPages, out string xml, XMLSchema.xs2013);
            return xml;
        }));
        public NoteNode PageState(string pageId) => UiThread.Send(() =>
        {
            _provider.App.GetHierarchy(pageId, HierarchyScope.hsSelf, out string xml, XMLSchema.xs2013);
            NoteNode node = NoteNode.Parse(xml);
            _provider.App.GetHierarchyParent(pageId, out string parentId);
            var visited = new HashSet<string>(StringComparer.Ordinal) { pageId };
            var path = new List<string> { node.Name };
            while (!string.IsNullOrEmpty(parentId))
            {
                if (!visited.Add(parentId)) throw new InvalidOperationException("OneNote hierarchy contains a cycle.");
                _provider.App.GetHierarchy(parentId, HierarchyScope.hsSelf, out string parentXml, XMLSchema.xs2013);
                NoteNode parent = NoteNode.Parse(parentXml);
                node.Unavailable |= parent.Unavailable;
                path.Add(parent.Name);
                if (parent.Kind == "Section") node.SectionId = parent.Id;
                if (parent.Kind == "Notebook") break;
                _provider.App.GetHierarchyParent(parentId, out parentId);
            }
            path.Reverse();
            node.Path = string.Join(" / ", path);
            return node;
        });
        public PageContent ReadPage(string pageId) => PageParser.Parse(UiThread.Send(() => _provider.GetPageXml(pageId)));
        public IReadOnlyList<string> Search(string scopeId, string query) => UiThread.Send(() =>
        {
            _provider.App.FindPages(scopeId, query, out string xml, true, false, XMLSchema.xs2013);
            return (IReadOnlyList<string>)NoteNode.Parse(xml).DescendantsAndSelf().Where(n => n.Kind == "Page" && !n.Unavailable).Select(n => n.Id).ToList();
        });
        public void Navigate(string pageId, string blockId) =>
            UiThread.Send(() => _provider.App.NavigateTo(pageId, blockId ?? "", false));
    }
}
