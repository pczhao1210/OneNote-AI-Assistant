using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OneNoteAI.AI;
using OneNoteAI.OneNote.Models;
using OneNoteAI.Settings;

namespace OneNoteAI.Knowledge
{
    public sealed class IndexingService
    {
        private readonly INoteSource _source;
        private readonly IndexStore _store;
        private readonly Func<KnowledgeOptions> _currentOptions;
        private readonly SemaphoreSlim _operation = new SemaphoreSlim(1, 1);
        public IndexingService(INoteSource source, IndexStore store, Func<KnowledgeOptions> currentOptions = null)
        { _source = source; _store = store; _currentOptions = currentOptions; }

        public NoteNode Refresh(KnowledgeOptions options)
        {
            NoteNode hierarchy = _source.Hierarchy();
            _store.Synchronize(hierarchy, NoteNode.Sections(hierarchy, options.AllowedRootIds, true), options.Embedding.Generation);
            return hierarchy;
        }

        public async Task UpdateAsync(KnowledgeOptions options, IProgress<string> progress, CancellationToken token,
            EmbeddingClient embeddings = null)
        {
            await _operation.WaitAsync(token).ConfigureAwait(false);
            try
            {
                options = options.Clone();
                void CheckConsent()
                {
                    if (_currentOptions == null) return;
                    KnowledgeOptions current = _currentOptions();
                    if (current.Embedding.Generation != options.Embedding.Generation || current.Embedding.ApiKey != options.Embedding.ApiKey ||
                        !new HashSet<string>(current.AllowedRootIds, StringComparer.Ordinal).SetEquals(options.AllowedRootIds))
                        throw new InvalidOperationException("Index consent or Embedding settings changed. Indexing paused; refresh to resume.");
                }
                CheckConsent();
                options.Embedding.Validate(true);
                NoteNode hierarchy = Refresh(options);
                HashSet<string> allowed = NoteNode.Sections(hierarchy, options.AllowedRootIds);
                if (allowed.Count == 0) throw new InvalidOperationException("Select an allowed indexing scope first.");
                embeddings = embeddings ?? new EmbeddingClient(options.Embedding);
                string generation = options.Embedding.Generation;
                List<NoteNode> queue = _store.Pending(includeIndexed: true);
                int done = 0;
                foreach (NoteNode item in queue)
                {
                    token.ThrowIfCancellationRequested();
                    CheckConsent();
                    progress?.Report("Index " + (++done) + "/" + queue.Count + ": " + item.Name);
                    PageContent page;
                    NoteNode before;
                    try
                    {
                        var snapshot = NoteReader.ReadStable(_source, item.Id, token, allowed);
                        before = snapshot.State;
                        if (before.Unavailable || !allowed.Contains(before.SectionId) || before.SectionId != item.SectionId)
                            throw new InvalidOperationException("Page is locked or has moved outside the indexing scope.");
                        page = snapshot.Page;
                    }
                    catch (Exception ex) when (ex is COMException || ex is InvalidOperationException)
                    {
                        _store.Failed(item.Id, ex.Message);
                        progress?.Report("Skipped " + item.Name + ": " + ex.Message);
                        continue;
                    }
                    try
                    {
                        List<NoteChunk> chunks = NoteChunker.Split(page, before);
                        var reusable = _store.ReusableVectors(item.Id, generation, options.Embedding.VectorSize);
                        foreach (NoteChunk chunk in chunks)
                            if (reusable.TryGetValue(chunk.Hash, out float[] vector)) chunk.Vector = vector;
                        List<NoteChunk> missing = chunks.Where(c => c.Vector == null).ToList();
                        for (int start = 0; start < missing.Count; start += 32)
                        {
                            CheckConsent();
                            token.ThrowIfCancellationRequested();
                            NoteNode batchState = _source.PageState(item.Id);
                            if (batchState.Unavailable || batchState.SectionId != before.SectionId || batchState.Version != before.Version)
                                throw new InvalidOperationException("Page access or version changed; remaining embedding batches were not sent.");
                            List<NoteChunk> batch = missing.Skip(start).Take(32).ToList();
                            float[][] vectors = await embeddings.EmbedAsync(batch.Select(c => c.EmbeddingText).ToList(), token).ConfigureAwait(false);
                            for (int i = 0; i < batch.Count; i++) batch[i].Vector = vectors[i];
                            CheckConsent();
                            _store.StageVectors(item.Id, batch, generation);
                        }
                        token.ThrowIfCancellationRequested();
                        CheckConsent();
                        var final = NoteReader.ReadStable(_source, item.Id, token, allowed);
                        NoteNode after = final.State;
                        if (after.SectionId != before.SectionId || after.Version != before.Version ||
                            !NoteReader.SameContent(page, final.Page))
                        {
                            _store.Failed(item.Id, "Page changed during indexing; queued for the next refresh.");
                            progress?.Report("Changed during indexing: " + item.Name);
                            continue;
                        }
                        token.ThrowIfCancellationRequested();
                        CheckConsent();
                        _store.CommitPage(before, before.Version, chunks, generation);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _store.Failed(item.Id, ex.Message);
                        throw new InvalidOperationException("Index paused at " + item.Name + ". The queue is saved. " + ex.Message, ex);
                    }
                }
                progress?.Report("Index refresh finished. Failed pages remain queued.");
            }
            finally { _operation.Release(); }
        }
    }
}
