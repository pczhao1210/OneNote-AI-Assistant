using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using Microsoft.Office.Interop.OneNote;
using OneNoteAI.OneNote.Interop;

namespace OneNoteAI.OneNote
{
    public class PageWriter
    {
        private static readonly XNamespace OneNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        private readonly IOneNoteApplication _app;

        public PageWriter()
        {
            object raw = AddIn.Connect.Instance?.OneNoteApp
                ?? throw new InvalidOperationException("OneNote application not available.");
            _app = raw as IOneNoteApplication
                ?? throw new InvalidOperationException("Failed to cast OneNote Application to IOneNoteApplication.");
        }

        public void AppendOutline(string pageId, string content, string heading = null)
        {
            ValidatePageId(pageId);

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("Content cannot be null or empty.", nameof(content));
            }

            XDocument pageDoc = GetPageDocument(pageId);
            XElement pageElement = GetPageElement(pageDoc);
            double nextY = CalculateNextOutlineY(pageDoc);
            XElement outlineElement = CreateOutlineElement(nextY, heading, new List<string> { content }, true, true);

            pageElement.Add(outlineElement);
            UpdatePage(pageDoc);
        }

        public void AppendParagraphs(string pageId, IEnumerable<string> paragraphs, string heading = null)
        {
            ValidatePageId(pageId);

            List<string> paragraphList = NormalizeParagraphs(paragraphs, nameof(paragraphs));
            if (paragraphList.Count == 0)
            {
                throw new ArgumentException("Paragraphs cannot be empty.", nameof(paragraphs));
            }

            XDocument pageDoc = GetPageDocument(pageId);
            XElement pageElement = GetPageElement(pageDoc);
            double nextY = CalculateNextOutlineY(pageDoc);
            XElement outlineElement = CreateOutlineElement(nextY, heading, paragraphList, false, true);

            pageElement.Add(outlineElement);
            UpdatePage(pageDoc);
        }

        public void ReplaceOutlineContent(string pageId, string outlineId, string newContent)
        {
            ValidatePageId(pageId);

            if (string.IsNullOrWhiteSpace(outlineId))
            {
                throw new ArgumentException("Outline ID cannot be null or empty.", nameof(outlineId));
            }

            if (string.IsNullOrWhiteSpace(newContent))
            {
                throw new ArgumentException("New content cannot be null or empty.", nameof(newContent));
            }

            XDocument pageDoc = GetPageDocument(pageId);
            XElement outlineElement = pageDoc
                .Descendants(OneNs + "Outline")
                .FirstOrDefault(outline => string.Equals((string)outline.Attribute("objectID"), outlineId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals((string)outline.Attribute("ID"), outlineId, StringComparison.OrdinalIgnoreCase));

            if (outlineElement == null)
            {
                throw new InvalidOperationException("The specified outline was not found on the page.");
            }

            List<string> paragraphList = SplitIntoParagraphs(newContent);
            XElement childrenElement = outlineElement.Element(OneNs + "OEChildren");
            if (childrenElement == null)
            {
                childrenElement = new XElement(OneNs + "OEChildren");
                outlineElement.Add(childrenElement);
            }
            else
            {
                childrenElement.RemoveNodes();
            }

            foreach (XElement paragraphElement in CreateContentElements(paragraphList, false))
            {
                childrenElement.Add(paragraphElement);
            }

            UpdatePage(pageDoc);
        }

        private XElement CreateSeparatorElement()
        {
            return new XElement(OneNs + "OE",
                new XElement(OneNs + "T",
                    new XCData("──────── AI 生成内容 ────────")));
        }

        private double CalculateNextOutlineY(XDocument pageDoc)
        {
            if (pageDoc == null)
            {
                throw new ArgumentNullException(nameof(pageDoc));
            }

            double maxBottom = 0d;
            bool hasExistingOutline = false;

            foreach (XElement outlineElement in pageDoc.Descendants(OneNs + "Outline"))
            {
                XElement positionElement = outlineElement.Element(OneNs + "Position");
                if (positionElement == null)
                {
                    continue;
                }

                double y = ParseDouble((string)positionElement.Attribute("y"), double.NaN);
                if (double.IsNaN(y))
                {
                    continue;
                }

                XElement sizeElement = outlineElement.Element(OneNs + "Size");
                double height = ParseDouble((string)sizeElement?.Attribute("height"), double.NaN);
                if (double.IsNaN(height) || height <= 0d)
                {
                    height = EstimateOutlineHeight(outlineElement);
                }

                double bottom = y + height;
                if (!hasExistingOutline || bottom > maxBottom)
                {
                    maxBottom = bottom;
                    hasExistingOutline = true;
                }
            }

            return hasExistingOutline ? maxBottom + 40d : 200d;
        }

        private XElement CreateOutlineElement(double y, string heading, IList<string> contentBlocks, bool singleBlock, bool includeSeparator)
        {
            XElement childrenElement = new XElement(OneNs + "OEChildren");

            if (includeSeparator)
            {
                childrenElement.Add(CreateSeparatorElement());
            }

            if (!string.IsNullOrWhiteSpace(heading))
            {
                childrenElement.Add(CreateTextOeElement("<b>" + WebUtility.HtmlEncode(heading.Trim()) + "</b>"));
            }

            foreach (XElement contentElement in CreateContentElements(contentBlocks, singleBlock))
            {
                childrenElement.Add(contentElement);
            }

            // NOTE: <one:Size> requires BOTH width and height attributes per
            // the OneNote 2013 schema. Omit it entirely and let OneNote
            // auto-size the outline based on its content.
            return new XElement(OneNs + "Outline",
                new XElement(OneNs + "Position",
                    new XAttribute("x", FormatDouble(36d)),
                    new XAttribute("y", FormatDouble(y))),
                childrenElement);
        }

        private IEnumerable<XElement> CreateContentElements(IList<string> contentBlocks, bool singleBlock)
        {
            if (contentBlocks == null || contentBlocks.Count == 0)
            {
                yield break;
            }

            if (singleBlock)
            {
                yield return CreateTextOeElement(ToHtmlContent(contentBlocks[0]));
                yield break;
            }

            foreach (string contentBlock in contentBlocks)
            {
                yield return CreateTextOeElement(ToHtmlContent(contentBlock));
            }
        }

        private XElement CreateTextOeElement(string htmlContent)
        {
            return new XElement(OneNs + "OE",
                new XElement(OneNs + "T",
                    new XCData(SanitizeCData(htmlContent))));
        }

        private XDocument GetPageDocument(string pageId)
        {
            string xml;
            _app.GetPageContent(pageId, out xml, PageInfo.piAll, XMLSchema.xs2013);

            if (string.IsNullOrWhiteSpace(xml))
            {
                throw new InvalidOperationException("OneNote returned empty page XML.");
            }

            return XDocument.Parse(xml);
        }

        private XElement GetPageElement(XDocument pageDoc)
        {
            XElement pageElement = pageDoc == null ? null : pageDoc.Root;
            if (pageElement == null)
            {
                throw new InvalidOperationException("Page XML does not contain a root element.");
            }

            return pageElement;
        }

        private void UpdatePage(XDocument pageDoc)
        {
            _app.UpdatePageContent(
                pageDoc.ToString(SaveOptions.DisableFormatting),
                DateTime.MinValue,
                XMLSchema.xs2013,
                false);
        }

        private List<string> NormalizeParagraphs(IEnumerable<string> paragraphs, string parameterName)
        {
            if (paragraphs == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return paragraphs
                .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
                .Select(paragraph => paragraph.Trim())
                .ToList();
        }

        private List<string> SplitIntoParagraphs(string content)
        {
            string normalized = NormalizeLineEndings(content);

            List<string> paragraphs = normalized
                .Split(new[] { "\n\n" }, StringSplitOptions.None)
                .Select(paragraph => paragraph.Trim())
                .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
                .ToList();

            if (paragraphs.Count == 0)
            {
                paragraphs.Add(normalized.Trim());
            }

            return paragraphs;
        }

        private double EstimateOutlineHeight(XElement outlineElement)
        {
            int lineCount = outlineElement.Descendants(OneNs + "OE").Count();
            if (lineCount <= 0)
            {
                return 60d;
            }

            return Math.Max(60d, (lineCount * 24d) + 20d);
        }

        private double ParseDouble(string value, double defaultValue)
        {
            double parsed;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string ToHtmlContent(string content)
        {
            string normalized = NormalizeLineEndings(content).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (ContainsSimpleHtml(normalized))
            {
                return normalized.Replace("\n", "<br>");
            }

            return WebUtility.HtmlEncode(normalized).Replace("\n", "<br>");
        }

        private bool ContainsSimpleHtml(string value)
        {
            return value.IndexOf("<br", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<b>", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("</b>", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<strong", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<i>", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("</i>", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<u>", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("</u>", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<p", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<div", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<span", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("<li", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string NormalizeLineEndings(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }

        private string SanitizeCData(string value)
        {
            return (value ?? string.Empty).Replace("]]>", "]]&gt;");
        }

        private void ValidatePageId(string pageId)
        {
            if (string.IsNullOrWhiteSpace(pageId))
            {
                throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));
            }
        }
    }
}
