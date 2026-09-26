using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.AI;
using OneNoteAI.Knowledge;
using OneNoteAI.OneNote;
using OneNoteAI.OneNote.Models;
using OneNoteAI.Settings;

namespace OneNoteAI.Tests
{
    internal sealed class FakeNotes : INoteSource
    {
        public const string Namespace = "http://schemas.microsoft.com/office/onenote/2013/onenote";
        public readonly Dictionary<string, PageContent> Pages = new Dictionary<string, PageContent>();
        public readonly Dictionary<string, string> Sections = new Dictionary<string, string>();
        public readonly HashSet<string> Hidden = new HashSet<string>();
        public readonly List<string> Reads = new List<string>();
        public readonly List<string> SearchedScopes = new List<string>();
        public bool SearchFails;
        public bool NoSearchHits;
        public Action OnRead;
        public string ReportedVersion;

        public FakeNotes()
        {
            Add("p1", "s1", "Engineering", "The deployment requires a rollback plan. Revenue is 12345.");
            Add("p2", "s2", "Private", "Unrelated private content that must never cross scopes.");
        }

        public void Add(string id, string section, string title, string content)
        {
            var page = new PageContent { PageId = id, Title = title, DateModified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
            page.Outlines.Add(new OutlineContent { OutlineId = "outline", TextBlocks = new List<TextBlock>
            { new TextBlock { ElementId = "block-" + id, Text = content } } });
            Pages[id] = page;
            Sections[id] = section;
        }

        public NoteNode Hierarchy()
        {
            var root = new NoteNode { Id = "", Kind = "Notebooks", Name = "Notebooks" };
            var book = new NoteNode { Id = "book", Kind = "Notebook", Name = "Book" };
            var group = new NoteNode { Id = "group", ParentId = "book", Kind = "SectionGroup", Name = "Folder" };
            root.Children.Add(book);
            book.Children.Add(group);
            foreach (string sectionId in new[] { "s1", "s2" })
            {
                var section = new NoteNode { Id = sectionId, Kind = "Section", ParentId = "group", SectionId = sectionId, Name = "Same name" };
                group.Children.Add(section);
                foreach (var pair in Pages.Where(p => Sections[p.Key] == sectionId && !Hidden.Contains(p.Key)))
                    section.Children.Add(PageState(pair.Key));
            }
            return root;
        }

        public NoteNode PageState(string id)
        {
            PageContent page = Pages[id];
            return new NoteNode { Id = id, Name = page.Title, Kind = "Page", ParentId = Sections[id], SectionId = Sections[id], Path = "Book / " + Sections[id] + " / " + page.Title,
                Version = ReportedVersion ?? page.DateModified.Ticks.ToString(), Unavailable = Hidden.Contains(id) };
        }

        public PageContent ReadPage(string id)
        {
            if (Hidden.Contains(id)) throw new System.Runtime.InteropServices.COMException("Locked synthetic page.");
            Reads.Add(id);
            OnRead?.Invoke();
            return JsonConvert.DeserializeObject<PageContent>(JsonConvert.SerializeObject(Pages[id]));
        }

        public IReadOnlyList<string> Search(string scopeId, string query)
        {
            if (SearchFails) throw new System.Runtime.InteropServices.COMException("Synthetic search service unavailable.");
            SearchedScopes.Add(scopeId);
            return NoSearchHits ? new List<string>() : Pages.Keys.Where(p => Sections[p] == scopeId && !Hidden.Contains(p)).ToList();
        }

        public void Navigate(string pageId, string blockId) { }
    }

    internal sealed class FakeHttp : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;
        public FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) { _response = response; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _response(request, cancellationToken);
        public static HttpResponseMessage Json(JToken data) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(data.ToString(Formatting.None), Encoding.UTF8, "application/json") };
        public static HttpResponseMessage Sse(params JObject[] events) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(string.Join("", events.Select(e => "data:" + e.ToString(Formatting.None) + "\n\n")) + "data: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
    }

    internal sealed class FakeEmbeddings
    {
        public int Calls;
        public int Inputs;
        public readonly List<string> Texts = new List<string>();
        public Action AfterResponse;
        public bool DuplicateIndices;
        public bool Zero;
        public bool WrongDimensions;
        public EmbeddingClient Client(EmbeddingOptions options) => new EmbeddingClient(options, new HttpClient(new FakeHttp(async (request, token) =>
        {
            JObject body = JObject.Parse(await request.Content.ReadAsStringAsync());
            Calls++;
            int size = (int?)body["dimensions"] ?? ((string)body["model"] == "text-embedding-3-large" ? 3072 : 1536);
            var data = new JArray();
            for (int i = 0; i < body["input"].Count(); i++)
            {
                Inputs++;
                Texts.Add((string)body["input"][i]);
                var vector = new float[WrongDimensions ? size + 1 : size];
                if (!Zero) { vector[0] = i + 1; if (vector.Length > 1) vector[1] = 1; }
                data.Insert(0, new JObject { ["index"] = DuplicateIndices ? 0 : i, ["embedding"] = new JArray(vector) });
            }
            AfterResponse?.Invoke();
            return FakeHttp.Json(new JObject { ["model"] = body["model"], ["data"] = data });
        })));
    }

    internal sealed class FakeMcpHttp : HttpMessageHandler
    {
        public int Calls;
        public int Lists;
        public bool UseSse;
        public bool FailAfterDispatch;
        public bool ToolError;
        public bool ChangedSchema;
        public JObject LastArguments;
        public readonly List<string> Methods = new List<string>();
        public readonly List<string> Headers = new List<string>();
        public readonly List<string> Authorizations = new List<string>();

        public JObject WriteTool => new JObject { ["name"] = "change_record", ["description"] = "Update or delete a record.",
            ["inputSchema"] = JObject.Parse(ChangedSchema
                ? "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}},\"required\":[\"id\"]}"
                : "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"id\"],\"additionalProperties\":false}"),
            ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = true } };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) { Content = new StringContent("") };
            if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
            JObject message = JObject.Parse(await request.Content.ReadAsStringAsync());
            string method = (string)message["method"];
            Methods.Add(method);
            Headers.Add(string.Join(",", request.Headers.Select(h => h.Key)));
            Authorizations.Add(request.Headers.Authorization?.ToString());
            if (message["id"] == null) return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("") };
            JObject result;
            switch (method)
            {
                case "server/discover":
                    return FakeHttp.Json(new JObject { ["jsonrpc"] = "2.0", ["id"] = message["id"],
                        ["error"] = new JObject { ["code"] = -32601, ["message"] = "This synthetic server uses the initialize handshake." } });
                case "initialize":
                    result = new JObject { ["protocolVersion"] = message["params"]["protocolVersion"],
                        ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = true }, ["resources"] = new JObject(), ["prompts"] = new JObject() },
                        ["serverInfo"] = new JObject { ["name"] = "synthetic-test-server", ["version"] = "1.0" } };
                    break;
                case "tools/list":
                    Lists++;
                    result = (string)message["params"]?["cursor"] == "page2"
                        ? new JObject { ["tools"] = new JArray(new JObject { ["name"] = "lookup", ["description"] = "Find documents.",
                            ["inputSchema"] = JObject.Parse("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}}}") }) }
                        : new JObject { ["tools"] = new JArray(WriteTool), ["nextCursor"] = "page2" };
                    break;
                case "tools/call":
                    Calls++;
                    LastArguments = (JObject)message["params"]["arguments"];
                    if (FailAfterDispatch) throw new HttpRequestException("Synthetic reply lost after dispatch.");
                    result = new JObject { ["isError"] = ToolError,
                        ["structuredContent"] = new JObject { ["data"] = new JArray(new JObject { ["id"] = 1, ["title"] = "Keep structured data" }),
                            ["api_key"] = "synthetic-secret" },
                        ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = "https://example.invalid/source synthetic-secret" },
                            new JObject { ["type"] = "image", ["mimeType"] = "image/png", ["data"] = "Ymlu" },
                            new JObject { ["type"] = "resource_link", ["name"] = "Remote file", ["uri"] = "file:///remote-only/data.txt" }) };
                    break;
                case "resources/list":
                    result = new JObject { ["resources"] = new JArray(new JObject { ["name"] = "Remote document", ["uri"] = "file:///remote-only/data.txt", ["mimeType"] = "text/plain" }) };
                    break;
                case "resources/read":
                    result = new JObject { ["contents"] = new JArray(new JObject { ["uri"] = message["params"]["uri"], ["mimeType"] = "text/plain", ["text"] = "Remote, not local disk." }) };
                    break;
                case "prompts/list":
                    result = new JObject { ["prompts"] = new JArray(new JObject { ["name"] = "learn", ["description"] = "Learning prompt" }) };
                    break;
                case "prompts/get":
                    result = new JObject { ["messages"] = new JArray(new JObject { ["role"] = "user", ["content"] = new JObject { ["type"] = "text", ["text"] = "Explain the note." } }) };
                    break;
                default: throw new InvalidOperationException("Unexpected MCP method: " + method);
            }
            var rpc = new JObject { ["jsonrpc"] = "2.0", ["id"] = message["id"], ["result"] = result };
            var response = UseSse && method != "initialize" ? new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("event: message\ndata: " + rpc.ToString(Formatting.None) + "\n\n", Encoding.UTF8, "text/event-stream") }
                : FakeHttp.Json(rpc);
            if (method == "initialize") response.Headers.Add("Mcp-Session-Id", "synthetic-session");
            return response;
        }
    }

    internal sealed class StalledStream : Stream
    {
        private readonly TaskCompletionSource<int> _read = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly byte[] _prefix;
        private int _offset;
        public StalledStream(byte[] prefix = null) { _prefix = prefix ?? Array.Empty<byte>(); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            int length = Math.Min(count, _prefix.Length - _offset);
            if (length == 0) return _read.Task;
            Array.Copy(_prefix, _offset, buffer, offset, length);
            _offset += length;
            return Task.FromResult(length);
        }
        protected override void Dispose(bool disposing) { _read.TrySetException(new ObjectDisposedException(nameof(StalledStream))); base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
