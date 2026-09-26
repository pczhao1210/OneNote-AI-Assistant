using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using HNSW.Net;

namespace OneNoteAI.Knowledge
{
    public sealed class VectorIndex
    {
        private sealed class CacheEntry
        {
            public string Generation;
            public long Revision;
            public long Used;
            public long Bytes;
            public List<NoteChunk> Chunks;
            public SmallWorld<float[], float> Graph;
        }

        private readonly IndexStore _store;
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();
        private readonly long _memoryLimit = (Environment.Is64BitProcess ? 192L : 48L) * 1024 * 1024;
        private long _clock;
        internal long CachedBytes => _cache.Values.Sum(e => e.Bytes);
        public VectorIndex(IndexStore store) { _store = store; }

        public List<NoteChunk> Search(float[] query, IEnumerable<string> sections, string generation,
            int count, CancellationToken token, Action<string> warning = null)
        {
            var best = new List<(NoteChunk Chunk, float Distance)>();
            int shardLimit = (int)Math.Max(256, Math.Min(Environment.Is64BitProcess ? 8192 : 2048, _memoryLimit / 2 / (query.Length * 4L + 20 * 1024)));
            foreach (IndexShard shard in _store.Shards(sections, generation, shardLimit))
            {
                token.ThrowIfCancellationRequested();
                CacheEntry entry = Load(shard, generation, query.Length, token, warning);
                if (entry.Graph == null)
                {
                    foreach (NoteChunk chunk in entry.Chunks)
                    {
                        token.ThrowIfCancellationRequested();
                        best.Add((chunk, Distance(query, chunk.Vector)));
                    }
                }
                else
                {
                    foreach (var hit in entry.Graph.KNNSearch(query, Math.Min(count, entry.Chunks.Count), cancellationToken: token))
                        best.Add((entry.Chunks[hit.Id], hit.Distance));
                }
                best = best.OrderBy(p => p.Distance).ThenBy(p => p.Chunk.Id, StringComparer.Ordinal).Take(count).ToList();
            }
            return best.Select(p => p.Chunk).ToList();
        }

        private CacheEntry Load(IndexShard shard, string generation, int dimensions, CancellationToken token, Action<string> warning)
        {
            if (_cache.TryGetValue(shard.Key, out CacheEntry entry) && entry.Generation == generation && entry.Revision == shard.Revision)
            {
                entry.Used = ++_clock;
                return entry;
            }
            _cache.Remove(shard.Key);
            long reservation = (long)shard.Count * (dimensions * 4 + 20 * 1024);
            MakeRoom(reservation);
            List<NoteChunk> chunks = _store.LoadShard(shard, generation, dimensions);
            entry = new CacheEntry { Generation = generation, Revision = shard.Revision, Used = ++_clock,
                Chunks = chunks, Bytes = chunks.Sum(c => (long)dimensions * 4 +
                    ((long)c.Text.Length + (c.Title?.Length ?? 0) + (c.Path?.Length ?? 0)) * 2 + 2048) };
            if (chunks.Count >= 256)
            {
                List<float[]> vectors = chunks.Select(c => c.Vector).ToList();
                string identity = LocalState.Hash(generation + "\n" + string.Join("\n", chunks.Select(c => c.Id + c.Hash)));
                string snapshot = Path.Combine(_store.DirectoryPath, shard.Key + ".hnsw");
                if (File.Exists(snapshot))
                {
                    try
                    {
                        using (FileStream stream = File.OpenRead(snapshot))
                        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                        {
                            if (reader.ReadString() == identity)
                            {
                                int length = reader.ReadInt32();
                                if (length < 0 || length > 64 * 1024 * 1024 || stream.Length - stream.Position != length + 32)
                                    throw new InvalidDataException("Invalid ANN snapshot length.");
                                byte[] expected = reader.ReadBytes(32);
                                byte[] graphBytes = reader.ReadBytes(length);
                                using (var sha = SHA256.Create())
                                    if (!sha.ComputeHash(graphBytes).SequenceEqual(expected)) throw new InvalidDataException("ANN snapshot checksum mismatch.");
                                using (var graphStream = new MemoryStream(graphBytes, false))
                                {
                                    var restored = SmallWorld<float[], float>.DeserializeGraph(vectors, Distance, DefaultRandomGenerator.DisableThreading, graphStream, false);
                                    if (restored.ItemsNotInGraph.Length != 0) throw new InvalidDataException("Incomplete graph snapshot.");
                                    entry.Graph = restored.Graph;
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is MessagePack.MessagePackSerializationException)
                    {
                        warning?.Invoke("ANN cache is unreadable; rebuilding from SQLite vectors.");
                        Logging.Logger.Warn("ANN snapshot rebuild: " + ex.GetType().Name);
                    }
                }
                if (entry.Graph == null)
                {
                    var graph = new SmallWorld<float[], float>(Distance, DefaultRandomGenerator.DisableThreading,
                        new SmallWorld<float[], float>.Parameters { M = 16, LevelLambda = 1 / Math.Log(16),
                            ConstructionPruning = 120, EnableDistanceCacheForConstruction = false, InitialDistanceCacheSize = 0 }, false);
                    for (int start = 0; start < vectors.Count; start += 64)
                    {
                        token.ThrowIfCancellationRequested();
                        graph.AddItems(vectors.Skip(start).Take(64).ToList());
                    }
                    string temporary = snapshot + ".tmp";
                    try
                    {
                        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                        {
                            using (var graphStream = new MemoryStream())
                            using (var sha = SHA256.Create())
                            {
                                graph.SerializeGraph(graphStream);
                                byte[] bytes = graphStream.ToArray();
                                writer.Write(identity);
                                writer.Write(bytes.Length);
                                writer.Write(sha.ComputeHash(bytes));
                                writer.Write(bytes);
                                writer.Flush();
                            }
                            stream.Flush(true);
                        }
                        token.ThrowIfCancellationRequested();
                        LocalState.Replace(temporary, snapshot);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    entry.Graph = graph;
                }
            }
            MakeRoom(entry.Bytes);
            if (entry.Bytes <= _memoryLimit) _cache[shard.Key] = entry;
            return entry;
        }

        private void MakeRoom(long bytes)
        {
            while (_cache.Count > 0 && CachedBytes + bytes > _memoryLimit)
                _cache.Remove(_cache.OrderBy(p => p.Value.Used).First().Key);
        }

        public static float Distance(float[] first, float[] second)
        {
            if (first.Length != second.Length) throw new InvalidDataException("Mixed embedding dimensions.");
            return System.Numerics.Vector.IsHardwareAccelerated
                ? CosineDistance.SIMDForUnits(first, second)
                : CosineDistance.ForUnits(first, second);
        }

        public void ClearCache() => _cache.Clear();
    }
}
