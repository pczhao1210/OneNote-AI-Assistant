using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OneNoteAI.AI
{
    public static class ContentChunker
    {
        /// Default max tokens for input content (leaving room for system prompt + response)
        public const int DefaultMaxInputTokens = 6000;

        private static readonly Regex ParagraphSplitRegex = new Regex(@"(?:\r?\n){2,}", RegexOptions.Compiled);
        private static readonly Regex SentenceSplitRegex = new Regex(@"(?<=[。！？])|(?<=[.!?])(?=\s|$)", RegexOptions.Compiled);

        /// Split text into chunks, each fitting within maxTokens.
        /// Splits on paragraph boundaries (double newlines) first, then sentence boundaries.
        public static List<string> ChunkText(string text, int maxTokens = DefaultMaxInputTokens)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string> { text ?? string.Empty };
            }

            int estimatedTokens = TokenEstimator.Estimate(text);
            if (estimatedTokens <= maxTokens)
            {
                return new List<string> { text };
            }

            string normalizedText = NormalizeLineEndings(text).Trim();
            string[] paragraphs = ParagraphSplitRegex.Split(normalizedText);

            List<string> chunks = new List<string>();
            StringBuilder currentChunk = new StringBuilder();

            for (int i = 0; i < paragraphs.Length; i++)
            {
                string paragraph = (paragraphs[i] ?? string.Empty).Trim();
                if (paragraph.Length == 0)
                {
                    continue;
                }

                if (TokenEstimator.Estimate(paragraph) > maxTokens)
                {
                    FlushChunk(chunks, currentChunk);

                    List<string> sentenceParts = SplitBySentences(paragraph);
                    AppendSegments(chunks, sentenceParts, maxTokens, Environment.NewLine);
                    continue;
                }

                TryAppendSegment(currentChunk, paragraph, maxTokens, "\n\n", chunks);
            }

            FlushChunk(chunks, currentChunk);

            return chunks.Count > 0 ? chunks : new List<string> { text };
        }

        /// For section-level summary: takes multiple page texts and groups them into batches
        public static List<List<string>> BatchPages(List<string> pageTexts, int maxTokensPerBatch = DefaultMaxInputTokens)
        {
            List<List<string>> batches = new List<List<string>>();
            if (pageTexts == null || pageTexts.Count == 0)
            {
                return batches;
            }

            List<string> currentBatch = new List<string>();
            int currentBatchTokens = 0;

            for (int i = 0; i < pageTexts.Count; i++)
            {
                List<string> pageChunks = ChunkText(pageTexts[i], maxTokensPerBatch);

                for (int j = 0; j < pageChunks.Count; j++)
                {
                    string chunk = pageChunks[j] ?? string.Empty;
                    int chunkTokens = TokenEstimator.Estimate(chunk);

                    if (currentBatch.Count > 0 && currentBatchTokens + chunkTokens > maxTokensPerBatch)
                    {
                        batches.Add(currentBatch);
                        currentBatch = new List<string>();
                        currentBatchTokens = 0;
                    }

                    currentBatch.Add(chunk);
                    currentBatchTokens += chunkTokens;
                }
            }

            if (currentBatch.Count > 0)
            {
                batches.Add(currentBatch);
            }

            return batches;
        }

        /// Split a single long paragraph into sentence-level pieces
        private static List<string> SplitBySentences(string text)
        {
            List<string> sentences = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return sentences;
            }

            string normalizedText = NormalizeLineEndings(text).Trim();
            string[] parts = SentenceSplitRegex.Split(normalizedText);

            for (int i = 0; i < parts.Length; i++)
            {
                string part = (parts[i] ?? string.Empty).Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (TokenEstimator.Estimate(part) <= DefaultMaxInputTokens)
                {
                    sentences.Add(part);
                }
                else
                {
                    sentences.AddRange(SplitOversizedText(part, DefaultMaxInputTokens));
                }
            }

            return sentences;
        }

        private static void AppendSegments(List<string> chunks, List<string> segments, int maxTokens, string separator)
        {
            StringBuilder currentChunk = new StringBuilder();

            for (int i = 0; i < segments.Count; i++)
            {
                string segment = (segments[i] ?? string.Empty).Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                if (TokenEstimator.Estimate(segment) > maxTokens)
                {
                    FlushChunk(chunks, currentChunk);

                    List<string> smallerPieces = SplitOversizedText(segment, maxTokens);
                    for (int j = 0; j < smallerPieces.Count; j++)
                    {
                        chunks.Add(smallerPieces[j]);
                    }

                    continue;
                }

                TryAppendSegment(currentChunk, segment, maxTokens, separator, chunks);
            }

            FlushChunk(chunks, currentChunk);
        }

        private static void TryAppendSegment(StringBuilder currentChunk, string segment, int maxTokens, string separator, List<string> chunks)
        {
            if (currentChunk.Length == 0)
            {
                currentChunk.Append(segment);
                return;
            }

            string candidate = currentChunk.ToString() + separator + segment;
            if (TokenEstimator.Estimate(candidate) <= maxTokens)
            {
                currentChunk.Append(separator);
                currentChunk.Append(segment);
                return;
            }

            FlushChunk(chunks, currentChunk);
            currentChunk.Append(segment);
        }

        private static void FlushChunk(List<string> chunks, StringBuilder currentChunk)
        {
            if (currentChunk.Length == 0)
            {
                return;
            }

            chunks.Add(currentChunk.ToString().Trim());
            currentChunk.Clear();
        }

        private static List<string> SplitOversizedText(string text, int maxTokens)
        {
            List<string> pieces = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return pieces;
            }

            string normalizedText = text.Trim();
            int start = 0;

            while (start < normalizedText.Length)
            {
                int length = FindLargestFittingLength(normalizedText, start, maxTokens);
                string piece = normalizedText.Substring(start, length).Trim();

                if (piece.Length == 0)
                {
                    break;
                }

                pieces.Add(piece);
                start += length;
            }

            return pieces;
        }

        private static int FindLargestFittingLength(string text, int startIndex, int maxTokens)
        {
            int remainingLength = text.Length - startIndex;
            int low = 1;
            int high = remainingLength;
            int best = 1;

            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                string candidate = text.Substring(startIndex, mid);
                int tokens = TokenEstimator.Estimate(candidate);

                if (tokens <= maxTokens)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best;
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
