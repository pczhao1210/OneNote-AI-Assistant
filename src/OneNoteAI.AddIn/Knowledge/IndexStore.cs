using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using OneNoteAI.AI;

namespace OneNoteAI.Knowledge
{
    public sealed class IndexCoverage
    {
        public int Total { get; set; }
        public int Indexed { get; set; }
        public int Failed { get; set; }
        public int Pending => Total - Indexed;
    }

    public sealed class IndexShard
    {
        public string SectionId { get; set; }
        public string Bucket { get; set; }
        public long Revision { get; set; }
        public int Count { get; set; }
        public string Key => LocalState.Hash(SectionId) + "-" + Bucket;
    }

    public sealed class IndexStore : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly object _gate = new object();
        public string DirectoryPath { get; }

        public IndexStore(string directory = null)
        {
            DirectoryPath = directory ?? LocalState.DirectoryPath;
            LocalState.EnsureDirectory(DirectoryPath);
            _connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
            { DataSource = Path.Combine(DirectoryPath, "index.sqlite"), ForeignKeys = true, DefaultTimeout = 10 }.ToString());
            _connection.Open();
            Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
            Execute(@"CREATE TABLE IF NOT EXISTS Meta(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS Sections(Id TEXT PRIMARY KEY, Revision INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Pages(
                    Id TEXT PRIMARY KEY, SectionId TEXT NOT NULL, Title TEXT NOT NULL, Path TEXT NOT NULL,
                    Version TEXT NOT NULL, IndexedVersion TEXT NOT NULL DEFAULT '', Generation TEXT NOT NULL DEFAULT '',
                    Available INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Chunks(
                    Id TEXT PRIMARY KEY, PageId TEXT NOT NULL REFERENCES Pages(Id) ON DELETE CASCADE,
                    SectionId TEXT NOT NULL, Bucket TEXT NOT NULL, BlockId TEXT NOT NULL, Text TEXT NOT NULL,
                    Hash TEXT NOT NULL, Generation TEXT NOT NULL, Vector BLOB NOT NULL);
                CREATE INDEX IF NOT EXISTS ChunkScope ON Chunks(Generation,SectionId,Bucket,Id);
                CREATE INDEX IF NOT EXISTS ChunkPage ON Chunks(PageId);
                CREATE TABLE IF NOT EXISTS StagedVectors(
                    PageId TEXT NOT NULL REFERENCES Pages(Id) ON DELETE CASCADE,
                    Hash TEXT NOT NULL, Generation TEXT NOT NULL, Vector BLOB NOT NULL,
                    PRIMARY KEY(PageId,Hash,Generation));
                CREATE TABLE IF NOT EXISTS Queue(
                    PageId TEXT PRIMARY KEY REFERENCES Pages(Id) ON DELETE CASCADE,
                    Attempts INTEGER NOT NULL DEFAULT 0, Error TEXT NOT NULL DEFAULT '');");
            using (SQLiteCommand command = Command("SELECT Value FROM Meta WHERE Key='schema'"))
            {
                object version = command.ExecuteScalar();
                if (version != null && (string)version != "1") throw new InvalidDataException("Unsupported knowledge index version. Back up and clear the index.");
            }
            Execute("INSERT OR IGNORE INTO Meta VALUES('schema','1')");
        }

        public void Synchronize(NoteNode hierarchy, HashSet<string> allowedSections, string generation)
        {
            lock (_gate)
            {
                var nodes = hierarchy.DescendantsAndSelf().ToList();
                var knownSections = new HashSet<string>(nodes.Where(n => n.Kind == "Section").Select(n => n.Id), StringComparer.Ordinal);
                var allPages = nodes.Where(n => n.Kind == "Page").ToDictionary(n => n.Id);
                var pages = allPages.Values.Where(n => !n.Unavailable && allowedSections.Contains(n.SectionId)).ToDictionary(n => n.Id);
                var old = new Dictionary<string, (string Section, string Version, string Title, string Path, bool Available)>(StringComparer.Ordinal);
                using (SQLiteCommand command = Command("SELECT Id,SectionId,Version,Title,Path,Available FROM Pages"))
                using (SQLiteDataReader reader = command.ExecuteReader())
                    while (reader.Read()) old.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
                var changed = new HashSet<string>(StringComparer.Ordinal);
                using (SQLiteTransaction transaction = _connection.BeginTransaction())
                {
                    foreach (var item in old)
                    {
                        bool outside = allPages.TryGetValue(item.Key, out NoteNode live)
                            ? !allowedSections.Contains(live.SectionId)
                            : knownSections.Contains(item.Value.Section) && !allowedSections.Contains(item.Value.Section);
                        if (outside)
                        {
                            Execute("DELETE FROM Pages WHERE Id=@id", ("@id", item.Key));
                            changed.Add(item.Value.Section);
                        }
                        else if (!pages.ContainsKey(item.Key) && item.Value.Available)
                        {
                            // Missing from a successful scan means inaccessible, not proven deleted.
                            Execute("UPDATE Pages SET Available=0 WHERE Id=@id", ("@id", item.Key));
                            changed.Add(item.Value.Section);
                        }
                    }
                    foreach (NoteNode page in pages.Values)
                    {
                        bool exists = old.TryGetValue(page.Id, out var previous);
                        if (exists && previous.Available && previous.Section == page.SectionId && previous.Version == page.Version &&
                            previous.Title == page.Name && previous.Path == page.Path) continue;
                        Execute(@"INSERT INTO Pages(Id,SectionId,Title,Path,Version,Available) VALUES(@id,@section,@title,@path,@version,1)
                            ON CONFLICT(Id) DO UPDATE SET SectionId=@section,Title=@title,Path=@path,Version=@version,Available=1",
                            ("@id", page.Id), ("@section", page.SectionId), ("@title", page.Name), ("@path", page.Path), ("@version", page.Version));
                        if (exists && previous.Section != page.SectionId)
                            Execute("UPDATE Chunks SET SectionId=@section WHERE PageId=@id", ("@section", page.SectionId), ("@id", page.Id));
                        if (exists && previous.Title != page.Name)
                            Execute("UPDATE Pages SET IndexedVersion='' WHERE Id=@id", ("@id", page.Id));
                        changed.Add(page.SectionId);
                        if (exists) changed.Add(previous.Section);
                    }
                    Execute(@"INSERT OR IGNORE INTO Queue(PageId)
                        SELECT Id FROM Pages WHERE Available=1 AND (Version<>IndexedVersion OR Generation<>@generation)",
                        ("@generation", generation));
                    foreach (string section in changed) Bump(section);
                    transaction.Commit();
                }
            }
        }

        public List<NoteNode> Pending(bool includeIndexed = false)
        {
            lock (_gate)
            using (SQLiteCommand command = Command(@"SELECT p.Id,p.SectionId,p.Title,p.Path,p.Version FROM Pages p
                LEFT JOIN Queue q ON p.Id=q.PageId WHERE p.Available=1 AND (@all=1 OR q.PageId IS NOT NULL) ORDER BY q.PageId IS NULL,q.Attempts,p.Id",
                ("@all", includeIndexed ? 1 : 0)))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                var result = new List<NoteNode>();
                while (reader.Read()) result.Add(new NoteNode { Id = reader.GetString(0), SectionId = reader.GetString(1),
                    Name = reader.GetString(2), Path = reader.GetString(3), Version = reader.GetString(4), Kind = "Page" });
                return result;
            }
        }

        public Dictionary<string, float[]> ReusableVectors(string pageId, string generation, int dimensions)
        {
            lock (_gate)
            using (SQLiteCommand command = Command(@"SELECT Hash,Vector FROM Chunks WHERE PageId=@page AND Generation=@generation
                UNION ALL SELECT Hash,Vector FROM StagedVectors WHERE PageId=@page AND Generation=@generation",
                ("@page", pageId), ("@generation", generation)))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
                while (reader.Read()) result[reader.GetString(0)] = Decode((byte[])reader[1], dimensions);
                return result;
            }
        }

        public void StageVectors(string pageId, List<NoteChunk> chunks, string generation)
        {
            lock (_gate)
            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                foreach (NoteChunk chunk in chunks)
                {
                    byte[] vector = new byte[chunk.Vector.Length * sizeof(float)];
                    Buffer.BlockCopy(chunk.Vector, 0, vector, 0, vector.Length);
                    Execute("INSERT OR REPLACE INTO StagedVectors VALUES(@page,@hash,@generation,@vector)",
                        ("@page", pageId), ("@hash", chunk.Hash), ("@generation", generation), ("@vector", vector));
                }
                transaction.Commit();
            }
        }

        public void CommitPage(NoteNode page, string version, List<NoteChunk> chunks, string generation)
        {
            lock (_gate)
            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                if (MatchesIndexedPage(page, version, chunks, generation))
                {
                    Execute("DELETE FROM Queue WHERE PageId=@page", ("@page", page.Id));
                    Execute("DELETE FROM StagedVectors WHERE PageId=@page", ("@page", page.Id));
                    transaction.Commit();
                    return;
                }
                Execute("DELETE FROM Chunks WHERE PageId=@page", ("@page", page.Id));
                foreach (NoteChunk chunk in chunks)
                {
                    byte[] vector = new byte[chunk.Vector.Length * sizeof(float)];
                    Buffer.BlockCopy(chunk.Vector, 0, vector, 0, vector.Length);
                    Execute(@"INSERT INTO Chunks(Id,PageId,SectionId,Bucket,BlockId,Text,Hash,Generation,Vector)
                        VALUES(@id,@page,@section,@bucket,@block,@text,@hash,@generation,@vector)",
                        ("@id", chunk.Id), ("@page", page.Id), ("@section", page.SectionId), ("@bucket", chunk.Id.Substring(0, 1)),
                        ("@block", chunk.BlockId ?? ""), ("@text", chunk.Text), ("@hash", chunk.Hash), ("@generation", generation), ("@vector", vector));
                }
                Execute("UPDATE Pages SET Version=@version,IndexedVersion=@version,Generation=@generation WHERE Id=@page",
                    ("@version", version), ("@generation", generation), ("@page", page.Id));
                Execute("DELETE FROM Queue WHERE PageId=@page", ("@page", page.Id));
                Execute("DELETE FROM StagedVectors WHERE PageId=@page", ("@page", page.Id));
                Bump(page.SectionId);
                transaction.Commit();
            }
        }

        public void Failed(string pageId, string error)
        {
            lock (_gate)
                Execute(@"INSERT INTO Queue(PageId,Attempts,Error) VALUES(@page,1,@error)
                    ON CONFLICT(PageId) DO UPDATE SET Attempts=Attempts+1,Error=@error",
                    ("@page", pageId), ("@error", error));
        }

        private bool MatchesIndexedPage(NoteNode page, string version, List<NoteChunk> chunks, string generation)
        {
            using (SQLiteCommand command = Command(@"SELECT COUNT(*) FROM Pages WHERE Id=@id
                AND IndexedVersion=@version AND Generation=@generation AND Title=@title AND SectionId=@section",
                ("@id", page.Id), ("@version", version), ("@generation", generation), ("@title", page.Name), ("@section", page.SectionId)))
                if (Convert.ToInt32(command.ExecuteScalar()) == 0) return false;
            var existing = new HashSet<(string Id, string Hash, string Block)>();
            using (SQLiteCommand command = Command("SELECT Id,Hash,BlockId FROM Chunks WHERE PageId=@id", ("@id", page.Id)))
            using (SQLiteDataReader reader = command.ExecuteReader())
                while (reader.Read()) existing.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            return existing.Count == chunks.Count && chunks.All(c => existing.Contains((c.Id, c.Hash, c.BlockId ?? "")));
        }

        public IndexCoverage Coverage(IEnumerable<string> sections, string generation)
        {
            lock (_gate)
            {
                var result = new IndexCoverage();
                foreach (string section in sections.Distinct(StringComparer.Ordinal))
                using (SQLiteCommand command = Command(@"SELECT COUNT(*),
                    COALESCE(SUM(CASE WHEN p.Version=p.IndexedVersion AND p.Generation=@generation AND q.PageId IS NULL THEN 1 ELSE 0 END),0),
                    COALESCE(SUM(CASE WHEN q.Attempts>0 THEN 1 ELSE 0 END),0)
                    FROM Pages p LEFT JOIN Queue q ON q.PageId=p.Id WHERE p.SectionId=@section AND p.Available=1",
                    ("@section", section), ("@generation", generation)))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    reader.Read();
                    result.Total += reader.GetInt32(0);
                    result.Indexed += reader.GetInt32(1);
                    result.Failed += reader.GetInt32(2);
                }
                return result;
            }
        }

        public List<IndexShard> Shards(IEnumerable<string> sections, string generation, int? maximumItems = null)
        {
            lock (_gate)
            {
                var result = new List<IndexShard>();
                foreach (string section in sections.Distinct(StringComparer.Ordinal))
                using (SQLiteCommand command = Command(@"SELECT c.Bucket,COUNT(*),s.Revision FROM Chunks c
                    JOIN Pages p ON c.PageId=p.Id JOIN Sections s ON s.Id=c.SectionId
                    WHERE c.SectionId=@section AND c.Generation=@generation AND p.Available=1 AND p.Version=p.IndexedVersion
                    GROUP BY c.Bucket,s.Revision", ("@section", section), ("@generation", generation)))
                using (SQLiteDataReader reader = command.ExecuteReader())
                    while (reader.Read()) result.Add(new IndexShard { SectionId = section, Bucket = reader.GetString(0),
                        Count = reader.GetInt32(1), Revision = reader.GetInt64(2) });
                int limit = maximumItems ?? (Environment.Is64BitProcess ? 8192 : 2048);
                if (limit < 1) throw new ArgumentOutOfRangeException(nameof(maximumItems));
                for (int i = 0; i < result.Count; i++)
                {
                    IndexShard shard = result[i];
                    if (shard.Count <= limit) continue;
                    result.RemoveAt(i--);
                    using (SQLiteCommand command = Command(@"SELECT SUBSTR(c.Id,1,@length),COUNT(*) FROM Chunks c JOIN Pages p ON p.Id=c.PageId
                        WHERE c.SectionId=@section AND c.Generation=@generation AND c.Id LIKE @prefix
                        AND p.Available=1 AND p.Version=p.IndexedVersion GROUP BY SUBSTR(c.Id,1,@length)",
                        ("@length", shard.Bucket.Length + 1), ("@section", shard.SectionId), ("@generation", generation), ("@prefix", shard.Bucket + "%")))
                    using (SQLiteDataReader reader = command.ExecuteReader())
                        while (reader.Read()) result.Add(new IndexShard { SectionId = shard.SectionId, Bucket = reader.GetString(0),
                            Count = reader.GetInt32(1), Revision = shard.Revision });
                }
                return result;
            }
        }

        public List<NoteChunk> LoadShard(IndexShard shard, string generation, int dimensions)
        {
            lock (_gate)
            using (SQLiteCommand command = Command(@"SELECT c.Id,c.PageId,c.BlockId,c.Text,c.Hash,c.Vector,p.Title,p.Path,p.IndexedVersion
                FROM Chunks c JOIN Pages p ON c.PageId=p.Id
                WHERE c.SectionId=@section AND c.Id LIKE @bucket AND c.Generation=@generation AND p.Available=1 AND p.Version=p.IndexedVersion
                ORDER BY c.Id", ("@section", shard.SectionId), ("@bucket", shard.Bucket + "%"), ("@generation", generation)))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                var result = new List<NoteChunk>();
                while (reader.Read()) result.Add(new NoteChunk { Id = reader.GetString(0), PageId = reader.GetString(1),
                    BlockId = reader.GetString(2), Text = reader.GetString(3), Hash = reader.GetString(4), Vector = Decode((byte[])reader[5], dimensions),
                    Title = reader.GetString(6), Path = reader.GetString(7), Version = reader.GetString(8), SectionId = shard.SectionId });
                return result;
            }
        }

        private void Bump(string section) => Execute("INSERT INTO Sections(Id,Revision) VALUES(@section,1) ON CONFLICT(Id) DO UPDATE SET Revision=Revision+1", ("@section", section));

        private static float[] Decode(byte[] bytes, int dimensions)
        {
            if (bytes.Length != dimensions * sizeof(float)) throw new InvalidDataException("Corrupt local vector. Clear and rebuild the index.");
            var vector = new float[dimensions];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return EmbeddingClient.Normalize(vector, dimensions);
        }

        private SQLiteCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            var command = new SQLiteCommand(sql, _connection);
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            return command;
        }

        private void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using (SQLiteCommand command = Command(sql, parameters)) command.ExecuteNonQuery();
        }

        public void Clear()
        {
            lock (_gate)
            {
                Execute("DELETE FROM Pages; DELETE FROM Sections;");
                Execute("PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
                foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.hnsw")) File.Delete(path);
            }
        }

        public void Dispose() { lock (_gate) _connection.Dispose(); }
    }
}
