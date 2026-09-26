using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OneNoteAI.AI;
using OneNoteAI.OneNote.Models;
using OneNoteAI.Settings;

namespace OneNoteAI.Knowledge
{
    public sealed class RetrievalResult
    {
        public List<NoteChunk> Chunks { get; set; } = new List<NoteChunk>();
        public List<string> Warnings { get; } = new List<string>();
        public IndexCoverage Coverage { get; set; } = new IndexCoverage();
        public Dictionary<string, NoteNode> Pages { get; set; } = new Dictionary<string, NoteNode>();
    }

    public sealed class RetrievalService
    {
        private readonly INoteSource _source;
        private readonly IndexStore _store;
        private readonly VectorIndex _vectors;
        public RetrievalService(INoteSource source, IndexStore store, VectorIndex vectors)
        { _source = source; _store = store; _vectors = vectors; }

        public async Task<RetrievalResult> SearchAsync(string question, IEnumerable<string> selectedRoots,
            KnowledgeOptions options, CancellationToken token, EmbeddingClient embeddings = null)
        {
            NoteNode hierarchy = _source.Hierarchy();
            HashSet<string> allowed = NoteNode.Sections(hierarchy, options.AllowedRootIds);
            HashSet<string> sections = NoteNode.Sections(hierarchy, selectedRoots);
            sections.IntersectWith(allowed);
            if (sections.Count == 0) throw new InvalidOperationException("Choose a query scope inside the allowed indexing scope.");
            string generation = options.Embedding.Generation;
            _store.Synchronize(hierarchy, NoteNode.Sections(hierarchy, options.AllowedRootIds, true), generation);
            var result = new RetrievalResult { Pages = hierarchy.DescendantsAndSelf()
                .Where(p => p.Kind == "Page" && !p.Unavailable && sections.Contains(p.SectionId)).ToDictionary(p => p.Id),
                Coverage = _store.Coverage(sections, generation) };
            void Warn(string text) { lock (result.Warnings) result.Warnings.Add(text); }
            async Task<List<NoteChunk>> Semantic()
            {
                try
                {
                    if (result.Coverage.Indexed == 0)
                    {
                        Warn("Semantic search has no indexed pages in this scope; using OneNote Search until the index is built.");
                        return new List<NoteChunk>();
                    }
                    var client = embeddings ?? new EmbeddingClient(options.Embedding);
                    float[][] query = await client.EmbedAsync(new[] { question }, token).ConfigureAwait(false);
                    return _vectors.Search(query[0], sections, generation, 40, token, Warn);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Warn("Semantic search unavailable: " + ex.Message);
                    return new List<NoteChunk>();
                }
            }
            List<NoteChunk> Native()
            {
                var matches = new List<NoteChunk>();
                string nativeQuery = NativeQuery(question);
                if (string.IsNullOrEmpty(nativeQuery))
                {
                    Warn("OneNote Search has no searchable terms; enter more specific keywords.");
                    return matches;
                }
                foreach (string section in sections)
                {
                    token.ThrowIfCancellationRequested();
                    IReadOnlyList<string> pages;
                    try { pages = _source.Search(section, nativeQuery); }
                    catch (COMException ex) { Warn("OneNote Search unavailable for a section: " + ex.Message); continue; }
                    foreach (string pageId in pages.Distinct(StringComparer.Ordinal))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!result.Pages.TryGetValue(pageId, out NoteNode metadata)) continue;
                        try
                        {
                            var snapshot = NoteReader.ReadStable(_source, pageId, token, sections);
                            NoteNode state = snapshot.State;
                            if (state.Unavailable || !sections.Contains(state.SectionId)) continue;
                            matches.AddRange(NoteChunker.Split(snapshot.Page, state));
                            matches = matches.OrderByDescending(c => KeywordScore(question, c)).ThenBy(c => c.Id, StringComparer.Ordinal).Take(40).ToList();
                        }
                        catch (Exception ex) when (ex is COMException || ex is InvalidOperationException)
                        { Warn("A OneNote Search result is inaccessible or changed: " + ex.Message); }
                    }
                }
                return matches;
            }
            Task<List<NoteChunk>> semantic = Semantic();
            Task<List<NoteChunk>> native = Task.Run(Native, token);
            await Task.WhenAll(semantic, native).ConfigureAwait(false);
            List<NoteChunk> fused = Fuse(await semantic.ConfigureAwait(false), await native.ConfigureAwait(false));
            var verified = new Dictionary<string, Dictionary<string, NoteChunk>>(StringComparer.Ordinal);
            foreach (NoteChunk candidate in fused)
            {
                token.ThrowIfCancellationRequested();
                if (!result.Pages.TryGetValue(candidate.PageId, out NoteNode metadata)) continue;
                if (!verified.TryGetValue(candidate.PageId, out var current))
                {
                    current = new Dictionary<string, NoteChunk>(StringComparer.Ordinal);
                    verified[candidate.PageId] = current;
                    try
                    {
                        var snapshot = NoteReader.ReadStable(_source, candidate.PageId, token, sections);
                        NoteNode state = snapshot.State;
                        if (state.Unavailable || !sections.Contains(state.SectionId)) continue;
                        result.Pages[candidate.PageId] = state;
                        foreach (NoteChunk chunk in NoteChunker.Split(snapshot.Page, state)) current[chunk.Id] = chunk;
                    }
                    catch (Exception ex) when (ex is COMException || ex is InvalidOperationException)
                    { Warn("A retrieved source is no longer accessible or changed: " + ex.Message); }
                }
                if (current.TryGetValue(candidate.Id, out NoteChunk fresh) && fresh.Hash == candidate.Hash)
                    result.Chunks.Add(fresh);
                if (result.Chunks.Count == 16) break;
            }
            if (result.Coverage.Pending > 0) Warn("Semantic coverage is incomplete; " + result.Coverage.Pending + " page(s) are not indexed at the current version/model.");
            if (result.Chunks.Count == 0) Warn("No verified passages found. This is not evidence that the whole notebook has no answer.");
            return result;
        }

        public RetrievalResult CurrentPage(string pageId, string question, CancellationToken token)
        {
            var snapshot = NoteReader.ReadStable(_source, pageId, token);
            var chunks = NoteChunker.Split(snapshot.Page, snapshot.State);
            var result = new RetrievalResult { Pages = new Dictionary<string, NoteNode> { [pageId] = snapshot.State },
                Chunks = chunks.OrderByDescending(c => KeywordScore(question, c)).Take(16).ToList() };
            if (chunks.Count > 16) result.Warnings.Add("Only selected passages of this long page are included, not a full-page review.");
            return result;
        }

        public (NoteNode State, bool IsCurrent) ValidateSource(NoteChunk chunk, bool requireCurrentVersion = true)
        {
            var snapshot = NoteReader.ReadStable(_source, chunk.PageId, CancellationToken.None,
                new HashSet<string>(StringComparer.Ordinal) { chunk.SectionId });
            bool current = snapshot.State.Version == chunk.Version &&
                NoteChunker.Split(snapshot.Page, snapshot.State).Any(c => c.Id == chunk.Id && c.Hash == chunk.Hash && c.BlockId == chunk.BlockId);
            if (requireCurrentVersion && !current)
                throw new InvalidOperationException("A source changed, moved or became inaccessible. Retrieve again before using its citation.");
            return (snapshot.State, current);
        }

        public static List<NoteChunk> Fuse(params List<NoteChunk>[] rankings)
        {
            var scores = new Dictionary<string, (NoteChunk Chunk, double Score)>(StringComparer.Ordinal);
            foreach (List<NoteChunk> ranking in rankings)
                for (int i = 0; i < ranking.Count; i++)
                {
                    NoteChunk chunk = ranking[i];
                    scores.TryGetValue(chunk.Id, out var prior);
                    scores[chunk.Id] = (chunk, prior.Score + 1.0 / (60 + i + 1));
                }
            var used = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int perPage = Math.Max(2, 16 / Math.Max(1, scores.Values.Select(p => p.Chunk.PageId).Distinct().Count()));
            var result = new List<NoteChunk>();
            foreach (var item in scores.Values.OrderByDescending(p => p.Score).ThenBy(p => p.Chunk.Id, StringComparer.Ordinal))
            {
                counts.TryGetValue(item.Chunk.PageId, out int count);
                if (count >= perPage || !used.Add(LocalState.Hash(item.Chunk.Text))) continue;
                result.Add(item.Chunk);
                counts[item.Chunk.PageId] = count + 1;
            }
            return result;
        }

        public static string NativeQuery(string question) =>
            string.Join(" OR ", Terms(question).Take(24).Select(t => "\"" + t + "\""));

        private static IEnumerable<string> Terms(string question)
        {
            var terms = new List<string>();
            foreach (Match match in Regex.Matches(question, @"[A-Za-z0-9_]+|[\u4e00-\u9fff]+"))
            {
                string text = match.Value.ToLowerInvariant();
                if (text[0] >= '\u4e00' && text.Length > 2)
                    for (int i = 0; i < text.Length - 1; i++) terms.Add(text.Substring(i, 2));
                else terms.Add(text);
            }
            return terms.Where(t => !new[] { "the", "is", "a", "an", "of", "in", "to", "and", "what", "how", "which", "请问", "什么", "哪些", "如何" }.Contains(t))
                .Distinct(StringComparer.Ordinal);
        }

        public static double KeywordScore(string question, NoteChunk chunk)
        {
            double score = 0;
            foreach (string term in Terms(question))
            {
                if ((chunk.Title ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 3;
                if (chunk.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
            }
            if (chunk.Text.IndexOf(question, StringComparison.OrdinalIgnoreCase) >= 0) score += 5;
            return score;
        }
    }
}
