using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OneNoteAI.OneNote.Models;

namespace OneNoteAI.OneNote
{
    public static class PageParser
    {
        private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        public static PageContent Parse(string pageXml)
        {
            if (string.IsNullOrWhiteSpace(pageXml))
            {
                throw new ArgumentException("Page XML cannot be null or empty.", nameof(pageXml));
            }

            XDocument document = XDocument.Parse(pageXml);
            XElement pageElement = document.Root;

            if (pageElement == null)
            {
                throw new InvalidOperationException("Page XML does not contain a root element.");
            }

            PageContent page = new PageContent
            {
                PageId = (string)pageElement.Attribute("ID") ?? string.Empty,
                Title = GetPageTitle(pageElement),
                DateCreated = ParseDate((string)pageElement.Attribute("dateTime")),
                DateModified = ParseDate((string)pageElement.Attribute("lastModifiedTime"))
            };

            // Parse TagDef elements (tag definitions at page level)
            foreach (XElement tagDefElement in pageElement.Elements(OneNs + "TagDef"))
            {
                int index;
                int type;
                int symbol;
                int.TryParse((string)tagDefElement.Attribute("index") ?? "0", out index);
                int.TryParse((string)tagDefElement.Attribute("type") ?? "0", out type);
                int.TryParse((string)tagDefElement.Attribute("symbol") ?? "0", out symbol);

                page.TagDefs.Add(new TagDef
                {
                    Index = index,
                    Name = (string)tagDefElement.Attribute("name") ?? string.Empty,
                    Type = type,
                    Symbol = symbol
                });
            }

            foreach (XElement outlineElement in pageElement.Elements(OneNs + "Outline"))
            {
                OutlineContent outline = new OutlineContent
                {
                    OutlineId = (string)outlineElement.Attribute("objectID")
                        ?? (string)outlineElement.Attribute("ID")
                        ?? string.Empty
                };

                XElement childrenElement = outlineElement.Element(OneNs + "OEChildren");
                if (childrenElement != null)
                {
                    foreach (XElement oeElement in childrenElement.Elements(OneNs + "OE"))
                    {
                        ParseOeElement(oeElement, outline, 0, page.TagDefs);
                    }
                }

                page.Outlines.Add(outline);
            }

            return page;
        }

        private static void ParseOeElement(XElement oeElement, OutlineContent outline, int indentLevel, System.Collections.Generic.List<TagDef> tagDefs)
        {
            if (oeElement == null)
            {
                return;
            }

            // Parse Tag on this OE element
            TagInfo tagInfo = null;
            XElement tagElement = oeElement.Element(OneNs + "Tag");
            if (tagElement != null)
            {
                int tagIndex;
                int.TryParse((string)tagElement.Attribute("index") ?? "0", out tagIndex);
                bool completed = string.Equals(
                    (string)tagElement.Attribute("completed"), "true",
                    StringComparison.OrdinalIgnoreCase);

                // Look up tag name from TagDef
                string tagName = string.Empty;
                if (tagDefs != null)
                {
                    TagDef def = tagDefs.Find(d => d.Index == tagIndex);
                    if (def != null)
                    {
                        tagName = def.Name;
                    }
                }

                tagInfo = new TagInfo
                {
                    Index = tagIndex,
                    Completed = completed,
                    TagName = tagName
                };
            }

            XElement textElement = oeElement.Element(OneNs + "T");
            if (textElement != null)
            {
                string rawHtml = textElement.Value ?? string.Empty;
                string plainText = StripHtml(rawHtml);

                if (!string.IsNullOrWhiteSpace(rawHtml) || !string.IsNullOrWhiteSpace(plainText))
                {
                    outline.TextBlocks.Add(new TextBlock
                    {
                        ElementId = (string)oeElement.Attribute("objectID")
                            ?? (string)oeElement.Attribute("ID")
                            ?? string.Empty,
                        RawHtml = rawHtml,
                        Text = plainText,
                        IndentLevel = indentLevel,
                        Tag = tagInfo
                    });
                }
            }

            XElement childrenElement = oeElement.Element(OneNs + "OEChildren");
            if (childrenElement == null)
            {
                return;
            }

            foreach (XElement childOe in childrenElement.Elements(OneNs + "OE"))
            {
                ParseOeElement(childOe, outline, indentLevel + 1, tagDefs);
            }
        }

        private static string GetPageTitle(XElement pageElement)
        {
            string title = (string)pageElement.Attribute("name");
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            XElement titleElement = pageElement.Element(OneNs + "Title");
            if (titleElement == null)
            {
                return string.Empty;
            }

            XElement textElement = titleElement.Descendants(OneNs + "T").FirstOrDefault();
            return textElement == null ? string.Empty : StripHtml(textElement.Value);
        }

        private static DateTime ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DateTime.MinValue;
            }

            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        internal static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            string withoutTags = Regex.Replace(html, "<[^>]+>", string.Empty);
            return WebUtility.HtmlDecode(withoutTags).Trim();
        }
    }
}
