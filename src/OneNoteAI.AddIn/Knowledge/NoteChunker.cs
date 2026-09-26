using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OneNoteAI.AI;
using OneNoteAI.OneNote.Models;

namespace OneNoteAI.Knowledge
{
    public sealed class NoteChunk
    {
        public string Id { get; set; }
        public string PageId { get; set; }
        public string SectionId { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string BlockId { get; set; }
        public string Text { get; set; }
        public string Hash { get; set; }
        public string Version { get; set; }
        public float[] Vector { get; set; }
        public string EmbeddingText => EmbeddingHeader(Title) + Text;
        internal static string EmbeddingHeader(string title)
        {
            title = title ?? "";
            int length = Math.Min(256, title.Length);
            if (length > 0 && char.IsHighSurrogate(title[length - 1])) length--;
            return title.Substring(0, length) + "\n";
        }
    }

    public static class NoteChunker
    {
        public static List<NoteChunk> Split(PageContent page, NoteNode metadata)
        {
            var pieces = new List<(string Text, string BlockId)>();
            int maxBytes = 8191 - Encoding.UTF8.GetByteCount(NoteChunk.EmbeddingHeader(page.Title));
            foreach (TextBlock block in page.Outlines.SelectMany(o => o.TextBlocks))
            {
                string text = (block.TableColumn > 0 ? "Column " + block.TableColumn + ": " : "") + block.Text + (block.EndsTableRow ? "\n" : "");
                int offset = 0;
                while (offset < text.Length)
                {
                    int length = PrefixLength(text, offset, 600, maxBytes);
                    string part = text.Substring(offset, length);
                    if (!string.IsNullOrWhiteSpace(part)) pieces.Add((part, block.ElementId));
                    if (offset + length == text.Length) break;
                    int overlap = SuffixLength(part, 70);
                    offset += length - overlap;
                }
            }
            var result = new List<NoteChunk>();
            var group = new List<(string Text, string BlockId)>();
            foreach (var piece in pieces)
            {
                string combined = string.Join("\n", group.Select(p => p.Text)) + "\n" + piece.Text;
                if (group.Count > 0 && (TokenEstimator.Estimate(combined) > 700 || Encoding.UTF8.GetByteCount(combined) > maxBytes))
                {
                    Add(result, group, page, metadata);
                    var last = group.Last();
                    group.Clear();
                    int overlap = SuffixLength(last.Text, 70);
                    while (overlap > 0 && Encoding.UTF8.GetByteCount(last.Text.Substring(last.Text.Length - overlap) + "\n" + piece.Text) > maxBytes) overlap--;
                    if (overlap > 0) group.Add((last.Text.Substring(last.Text.Length - overlap), last.BlockId));
                }
                group.Add(piece);
            }
            if (group.Count > 0) Add(result, group, page, metadata);
            return result;
        }

        private static void Add(List<NoteChunk> output, List<(string Text, string BlockId)> pieces, PageContent page, NoteNode metadata)
        {
            string text = string.Join("\n", pieces.Select(p => p.Text));
            output.Add(new NoteChunk { Id = LocalState.Hash(page.PageId + "\n" + output.Count + "\n" + text),
                PageId = page.PageId, SectionId = metadata.SectionId, Title = page.Title, Path = metadata.Path,
                BlockId = pieces[0].BlockId, Text = text, Hash = LocalState.Hash(NoteChunk.EmbeddingHeader(page.Title) + text),
                Version = metadata.Version });
        }

        private static int PrefixLength(string text, int offset, int budget, int maxBytes)
        {
            int low = 1, high = Math.Min(text.Length - offset, maxBytes);
            while (low < high)
            {
                int mid = low + (high - low + 1) / 2;
                string part = text.Substring(offset, mid);
                if (TokenEstimator.Estimate(part) <= budget && Encoding.UTF8.GetByteCount(part) <= maxBytes) low = mid;
                else high = mid - 1;
            }
            if (offset + low < text.Length && char.IsHighSurrogate(text[offset + low - 1])) low--;
            return Math.Max(1, low);
        }

        private static int SuffixLength(string text, int budget)
        {
            int size = Math.Min(text.Length / 2, budget * 4);
            while (size > 0 && TokenEstimator.Estimate(text.Substring(text.Length - size)) > budget) size--;
            if (size > 0 && char.IsLowSurrogate(text[text.Length - size])) size--;
            return size;
        }
    }
}
