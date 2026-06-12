using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Office.Interop.OneNote;
using OneNoteCopilot.OneNote.Interop;
using OneNoteCopilot.OneNote.Models;

namespace OneNoteCopilot.OneNote
{
    public class OneNoteProvider
    {
        private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        private readonly IOneNoteApplication _app;

        public OneNoteProvider()
        {
            object raw = AddIn.Connect.Instance?.OneNoteApp
                ?? throw new InvalidOperationException("OneNote application not available.");
            _app = raw as IOneNoteApplication;
            if (_app == null)
            {
                throw new InvalidOperationException(
                    "Failed to cast OneNote Application to IOneNoteApplication (type=" + raw.GetType().FullName + ").");
            }
        }

        internal IOneNoteApplication App => _app;

        public PageContent GetCurrentPage()
        {
            Windows windows = _app.GetWindows();
            if (windows == null)
            {
                throw new InvalidOperationException("OneNote.Windows is not available.");
            }
            Window currentWindow = windows.CurrentWindow;
            if (currentWindow == null)
            {
                throw new InvalidOperationException("Current window is not available.");
            }
            string pageId = currentWindow.CurrentPageId;
            if (string.IsNullOrWhiteSpace(pageId))
            {
                throw new InvalidOperationException("Current page is not available.");
            }

            return GetPage(pageId);
        }

        public PageContent GetPage(string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId))
            {
                throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));
            }

            return PageParser.Parse(GetPageXml(pageId));
        }

        public List<(string PageId, string Title)> GetSectionPages(string sectionId = null)
        {
            string effectiveSectionId = sectionId;
            if (string.IsNullOrWhiteSpace(effectiveSectionId))
            {
                Window currentWindow = _app.GetWindows()?.CurrentWindow;
                effectiveSectionId = currentWindow?.CurrentSectionId;
            }

            if (string.IsNullOrWhiteSpace(effectiveSectionId))
            {
                return new List<(string PageId, string Title)>();
            }

            string xml;
            _app.GetHierarchy(effectiveSectionId, HierarchyScope.hsPages, out xml, XMLSchema.xs2013);

            if (string.IsNullOrWhiteSpace(xml))
            {
                return new List<(string PageId, string Title)>();
            }

            XDocument document = XDocument.Parse(xml);

            return document
                .Descendants(OneNs + "Page")
                .Select(page => (
                    PageId: (string)page.Attribute("ID") ?? string.Empty,
                    Title: (string)page.Attribute("name") ?? string.Empty))
                .Where(page => !string.IsNullOrWhiteSpace(page.PageId))
                .ToList();
        }

        public string GetPageXml(string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId))
            {
                throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));
            }

            string xml;
            _app.GetPageContent(pageId, out xml, PageInfo.piBasic, XMLSchema.xs2013);
            return xml;
        }

        /// <summary>
        /// Returns the display name of the section currently active in the
        /// foreground OneNote window. Used by Scope dialogs to show the user
        /// what "current section" actually refers to.
        /// </summary>
        public string GetCurrentSectionName()
        {
            Window currentWindow = _app.GetWindows()?.CurrentWindow;
            string sectionId = currentWindow?.CurrentSectionId;
            if (string.IsNullOrWhiteSpace(sectionId))
            {
                return string.Empty;
            }

            string xml;
            _app.GetHierarchy(sectionId, HierarchyScope.hsSelf, out xml, XMLSchema.xs2013);
            if (string.IsNullOrWhiteSpace(xml))
            {
                return string.Empty;
            }

            XDocument doc = XDocument.Parse(xml);
            return (string)doc.Root?.Attribute("name") ?? string.Empty;
        }

        /// <summary>
        /// Returns the ID of the section currently active in the foreground
        /// OneNote window, or empty string if none.
        /// </summary>
        public string GetCurrentSectionId()
        {
            Window currentWindow = _app.GetWindows()?.CurrentWindow;
            return currentWindow?.CurrentSectionId ?? string.Empty;
        }

        /// <summary>
        /// Returns the plain-text content the user currently has selected on
        /// the active page, or empty string if nothing is selected.
        ///
        /// OneNote marks selection via the <c>selected</c> attribute on
        /// hierarchy nodes (<c>all</c> = fully selected, <c>partial</c> =
        /// partially selected). We collect text from every <c>one:T</c> under
        /// any <c>selected="all"</c> ancestor (OE / OEChildren / Outline) and
        /// strip HTML tags to produce plain text suitable for an LLM prompt.
        /// </summary>
        public string GetCurrentSelectionText()
        {
            Window currentWindow = _app.GetWindows()?.CurrentWindow;
            string pageId = currentWindow?.CurrentPageId;
            if (string.IsNullOrWhiteSpace(pageId))
            {
                return string.Empty;
            }

            string xml;
            _app.GetPageContent(pageId, out xml, PageInfo.piSelection, XMLSchema.xs2013);
            if (string.IsNullOrWhiteSpace(xml))
            {
                return string.Empty;
            }

            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return string.Empty; }

            // Find all <one:T> elements that live inside an ancestor marked
            // selected="all". This captures both whole-OE selections and
            // multi-OE block selections.
            List<string> texts = new List<string>();
            foreach (XElement t in doc.Descendants(OneNs + "T"))
            {
                bool selected = false;
                for (XElement cur = t; cur != null; cur = cur.Parent)
                {
                    string sel = (string)cur.Attribute("selected");
                    if (string.Equals(sel, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        selected = true;
                        break;
                    }
                }
                if (!selected) continue;

                string plain = PageParser.StripHtml(t.Value);
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    texts.Add(plain.Trim());
                }
            }

            return string.Join("\n", texts).Trim();
        }
    }
}
