using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.AI;
using OneNoteAI.AI.Models;
using OneNoteAI.Conversation;
using OneNoteAI.Knowledge;
using OneNoteAI.Mcp;
using OneNoteAI.OneNote;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Tests
{
    internal static class Program
    {
        private static string _root;
        private static int _passed;

        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            try { return RunAsync(args).GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            _root = Path.Combine(Path.GetTempPath(), "OneNoteAI.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            RedirectState();
            var tests = new List<(string Name, Func<Task> Run)>
            {
                ("provider snapshots and isolated settings", SettingsAsync),
                ("model parameter auto compatibility and overrides", ModelParametersAsync),
                ("independent OneNote hierarchy and content revisions", PageRevisionsAsync),
                ("table extraction and line breaks", ParserAsync),
                ("chunk bounds and source IDs", ChunksAsync),
                ("whole-request context budgets", BudgetsAsync),
                ("SSE cancellation and explicit errors", StreamsAsync),
                ("OpenAI native tool deltas", OpenAiToolsAsync),
                ("Claude native tool messages", ClaudeToolsAsync),
                ("embedding presets, ordering and validation", EmbeddingsAsync),
                ("scope expansion and duplicate names", ScopeAsync),
                ("SQLite incremental indexing and model isolation", IndexingAsync),
                ("durable embedding batch recovery", ResumeAsync),
                ("index consent revocation", ConsentAsync),
                ("independent hybrid recall and live access checks", RetrievalAsync),
                ("HNSW persistence, corruption recovery and scope", AnnAsync),
                ("cosine distance hardware and scalar kernels", DistanceAsync),
                ("high-dimensional embedding ANN and reload", async () =>
                {
                    await AnnAsync(1536, 5000, false);
                    await AnnAsync(3072, 5000, false);
                }),
                ("MCP JSON/SSE protocol and directory", McpProtocolAsync),
                ("MCP auto/approval/disabled/schema policies", McpPoliciesAsync),
                ("MCP unknown outcomes are not replayed", McpUnknownAsync),
                ("OAuth encrypted token cache", TokenCacheAsync),
                ("OAuth discovery, PKCE, refresh and state validation", OAuthProtocolAsync),
                ("fresh multi-turn evidence and scope reset", ConversationAsync),
                ("native model-MCP-model loop", ToolLoopAsync),
                ("WinForms layout and follow-up cancellation", UiAsync)
            };
            try
            {
                int failed = 0;
                foreach (var test in tests.Where(t => args.Length == 0 || args.Any(a => t.Name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        await test.Run().ConfigureAwait(false);
                        _passed++;
                        Console.WriteLine("PASS " + test.Name + " (" + clock.ElapsedMilliseconds + " ms)");
                    }
                    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
                }
                Console.WriteLine(_passed + " scenarios passed; process=" + (Environment.Is64BitProcess ? "x64" : "x86"));
                return failed == 0 ? 0 : 1;
            }
            finally
            {
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
        }

        private static void RedirectState()
        {
            SetStatic(typeof(SettingsManager), "SettingsDir", _root);
            SetStatic(typeof(SettingsManager), "SettingsFile", Path.Combine(_root, "settings.json"));
            SetStatic(typeof(SettingsManager), "_current", Settings());
            SetStatic(typeof(OneNoteAI.Logging.Logger), "LogDirectory", _root);
            SetStatic(typeof(OneNoteAI.Logging.Logger), "LogFilePath", Path.Combine(_root, "test.log"));
            SetStatic(typeof(OneNoteAI.Logging.Logger), "BackupLogFilePath", Path.Combine(_root, "test.log.bak"));
        }

        private static void SetStatic(Type type, string name, object value) =>
            type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);

        private static AppSettings Settings() => new AppSettings
        {
            Provider = AiProvider.OpenAI, ApiBaseUrl = "https://chat.invalid/v1", ApiKey = EncryptionHelper.Encrypt("synthetic-chat-key"),
            DefaultModel = "synthetic-model", Language = "en", MaxTokens = 1024,
            Knowledge = new KnowledgeOptions { Embedding = Embedding(), AllowedRootIds = new List<string> { "s1" } }
        };

        private static EmbeddingOptions Embedding() => new EmbeddingOptions { Endpoint = "https://embedding.invalid/v1",
            ApiKey = EncryptionHelper.Encrypt("synthetic-embedding-key") };
        private static string Folder() => Path.Combine(_root, Guid.NewGuid().ToString("N"));
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message = null)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception((message ?? "Values differ") + ": expected=" + expected + ", actual=" + actual); }
        private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try { await action().ConfigureAwait(false); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        private static Task Sync(Action action) { action(); return Task.CompletedTask; }

        private static Task SettingsAsync() => Sync(() =>
        {
            var settings = new AppSettings();
            SettingsManager.SwitchProvider(AiProvider.Ollama, settings);
            Equal("http://localhost:11434/v1", settings.ApiBaseUrl);
            SettingsManager.SwitchProvider(AiProvider.OpenAI, settings);
            Equal("https://api.openai.com/v1", settings.ApiBaseUrl);
            settings.ApiBaseUrl = "https://custom.invalid/v1";
            SettingsManager.SwitchProvider(AiProvider.DeepSeek, settings);
            Equal("https://api.deepseek.com", settings.ApiBaseUrl);
            SettingsManager.SwitchProvider(AiProvider.OpenAI, settings);
            Equal("https://custom.invalid/v1", settings.ApiBaseUrl);
            SetStatic(typeof(SettingsManager), "_current", Settings());
            UiThread.Send(() =>
            {
                using (var dialog = new SettingsDialog())
                {
                    var handle = dialog.Handle;
                    typeof(SettingsDialog).GetMethod("OnDialogLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { null, EventArgs.Empty });
                    var combo = (ComboBox)typeof(SettingsDialog).GetField("_cmbProvider", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                    var limit = (ComboBox)typeof(SettingsDialog).GetField("_cmbTokenLimit", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                    var temperature = (ComboBox)typeof(SettingsDialog).GetField("_cmbTemperatureMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                    limit.SelectedIndex = (int)TokenLimitParameter.MaxCompletionTokens;
                    temperature.SelectedIndex = (int)TemperatureParameter.Omit;
                    combo.SelectedIndex = (int)AiProvider.Ollama;
                    Equal(AiProvider.OpenAI, SettingsManager.Current.Provider, "Draft mutated active settings");
                    Equal(TokenLimitParameter.Auto, SettingsManager.Current.GetChatApiOptions().TokenLimit);
                    Equal(0, limit.SelectedIndex, "New provider inherited another provider's request parameters");
                    combo.SelectedIndex = (int)AiProvider.OpenAI;
                    Equal((int)TokenLimitParameter.MaxCompletionTokens, limit.SelectedIndex);
                    Equal((int)TemperatureParameter.Omit, temperature.SelectedIndex);
                    combo.SelectedIndex = (int)AiProvider.Ollama;
                    typeof(SettingsDialog).GetMethod("OnOkClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { null, EventArgs.Empty });
                }
            });
            Equal("en", SettingsManager.Current.Language);
            Equal(AiProvider.Ollama, SettingsManager.Current.Provider);
            Equal(TokenLimitParameter.MaxCompletionTokens, SettingsManager.Current.ProviderSettings[AiProvider.OpenAI].ChatApi.TokenLimit);
            Equal(TemperatureParameter.Omit, SettingsManager.Current.ProviderSettings[AiProvider.OpenAI].ChatApi.Temperature);
            Check(!File.ReadAllText(Path.Combine(_root, "settings.json")).Contains("synthetic-chat-key"), "Plain key persisted");
            SetStatic(typeof(SettingsManager), "_current", Settings());
        });

        private static async Task ModelParametersAsync()
        {
            var automatic = new ChatApiOptions();
            var cases = new[]
            {
                (AiProvider.DeepSeek, "deepseek-chat", automatic, "max_tokens", true),
                (AiProvider.OpenAI, "gpt-4.1", automatic, "max_completion_tokens", true),
                (AiProvider.OpenAI, "gpt-5.2", automatic, "max_completion_tokens", false),
                (AiProvider.OpenRouter, "openai/o3-mini", automatic, "max_completion_tokens", false),
                (AiProvider.Custom, "my-new-model", new ChatApiOptions { TokenLimit = TokenLimitParameter.MaxCompletionTokens,
                    Temperature = TemperatureParameter.Omit }, "max_completion_tokens", false),
                (AiProvider.Custom, "gpt-5-compatible-alias", new ChatApiOptions { TokenLimit = TokenLimitParameter.MaxTokens,
                    Temperature = TemperatureParameter.Send }, "max_tokens", true),
                (AiProvider.Claude, "claude-model", new ChatApiOptions { TokenLimit = TokenLimitParameter.MaxCompletionTokens,
                    Temperature = TemperatureParameter.Omit }, "max_tokens", false)
            };
            foreach (var test in cases)
            {
                int requests = 0;
                var options = new ChatApiOptions { TokenLimit = test.Item3.TokenLimit, Temperature = test.Item3.Temperature };
                using (var http = new HttpClient(new FakeHttp(async (message, token) =>
                {
                    requests++;
                    JObject body = JObject.Parse(await message.Content.ReadAsStringAsync());
                    Equal(2048, (int)body[test.Item4], "Wrong output limit");
                    Equal(test.Item5, body["temperature"] != null, "Wrong temperature policy");
                    Equal<JToken>(null, body[test.Item4 == "max_tokens" ? "max_completion_tokens" : "max_tokens"]);
                    Equal(test.Item2, (string)body["model"]);
                    Check(body["messages"] is JArray, "Message payload was lost");
                    Check(body["tools"] is JArray && body["tools"].Count() == 1, "Tool schema was lost");
                    Check(body["messages"].ToString().Contains("call-1"), "Tool history was lost");
                    if (test.Item1 == AiProvider.Claude)
                        return FakeHttp.Json(JObject.Parse("{\"content\":[{\"type\":\"text\",\"text\":\"OK\"}],\"stop_reason\":\"end_turn\"}"));
                    return (bool)body["stream"] ? FakeHttp.Sse(Delta(new JObject { ["content"] = "OK" }), Delta(new JObject(), "stop")) :
                        FakeHttp.Json(JObject.Parse("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}"));
                })))
                using (var client = new DeepseekClient("synthetic-key", "https://chat.invalid/v1", test.Item1, http, options))
                {
                    options.TokenLimit = test.Item4 == "max_tokens" ? TokenLimitParameter.MaxCompletionTokens : TokenLimitParameter.MaxTokens;
                    options.Temperature = test.Item5 ? TemperatureParameter.Omit : TemperatureParameter.Send;
                    var request = new ChatRequest { Model = test.Item2, MaxTokens = 2048, Temperature = .4,
                        Tools = new List<ChatTool> { new ChatTool { Function = new ToolDefinition { Name = "lookup", Parameters = JObject.Parse("{\"type\":\"object\"}") } } },
                        Messages = new List<ChatMessage> { ChatMessage.User("Hi"),
                            new ChatMessage { Role = "assistant", ToolCalls = new List<ToolCall> {
                                new ToolCall { Id = "call-1", Function = new ToolArguments { Name = "lookup", Arguments = "{}" } } } },
                            new ChatMessage { Role = "tool", ToolCallId = "call-1", Content = "Found" } } };
                    Equal("OK", (await client.SendAsync(request)).Choices[0].Message.Content);
                    if (test.Item1 != AiProvider.Claude) Equal("OK", (await client.StreamChoiceAsync(request, null)).Message.Content);
                    Equal(2048, request.MaxTokens);
                    Equal(.4, request.Temperature);
                    Equal(test.Item1 == AiProvider.Claude ? 1 : 2, requests, "Parameter handling unexpectedly retried");
                }
            }
        }

        private static async Task PageRevisionsAsync()
        {
            var notes = new FakeNotes { ReportedVersion = NoteNode.NormalizeVersion("2022-05-16T02:34:50.000Z") };
            notes.Pages["p1"].DateModified = new DateTime(2026, 9, 26, 3, 42, 37, DateTimeKind.Utc);
            var options = Settings().Knowledge;
            var fake = new FakeEmbeddings();
            using (var store = new IndexStore(Folder()))
            {
                var retrieval = new RetrievalService(notes, store, new VectorIndex(store));
                NoteChunk original = retrieval.CurrentPage("p1", "deployment", CancellationToken.None).Chunks.Single();
                Equal(notes.ReportedVersion, original.Version);
                var indexing = new IndexingService(notes, store);
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(1, store.Coverage(new[] { "s1" }, options.Embedding.Generation).Indexed);
                long revision = store.Shards(new[] { "s1" }, options.Embedding.Generation).Single().Revision;
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(1, fake.Calls, "Stable content with differing clocks was re-embedded");
                Equal(revision, store.Shards(new[] { "s1" }, options.Embedding.Generation).Single().Revision,
                    "Unchanged content unnecessarily invalidated the ANN cache");
                notes.Pages["p1"].Outlines[0].TextBlocks[0].Text = "A new knowledge passage.";
                Check(!retrieval.ValidateSource(original, requireCurrentVersion: false).IsCurrent, "Stale citation would navigate to an old block");
                await ThrowsAsync<InvalidOperationException>(() => Sync(() => retrieval.ValidateSource(original)));
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(2, fake.Calls, "A content edit with an unchanged hierarchy clock was not indexed");
                store.Failed("p1", "Page became inaccessible on refresh");
                Equal(1, store.Coverage(new[] { "s1" }, options.Embedding.Generation).Pending);
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(0, store.Coverage(new[] { "s1" }, options.Embedding.Generation).Pending);
                Equal(2, fake.Calls, "Recovery unnecessarily re-embedded stable content");
                notes.NoSearchHits = true;
                RetrievalResult result = await retrieval.SearchAsync("knowledge", new[] { "s1" }, options, CancellationToken.None, fake.Client(options.Embedding));
                Check(result.Chunks.Any(c => c.Text.Contains("new knowledge")), "Updated semantic passage was not returned");
                notes.Reads.Clear();
                notes.OnRead = () => { if (notes.Reads.Count == 2) notes.Pages["p1"].Outlines[0].TextBlocks[0].Text += " Changed during read."; };
                await ThrowsAsync<InvalidOperationException>(() => Sync(() => retrieval.CurrentPage("p1", "knowledge", CancellationToken.None)));
                notes.OnRead = null;
                notes.Reads.Clear();
                notes.OnRead = () => { if (notes.Reads.Count == 2) notes.Pages["p1"].Outlines[0].TextBlocks[0].ElementId += "-replaced"; };
                await ThrowsAsync<InvalidOperationException>(() => Sync(() => NoteReader.ReadStable(notes, "p1", CancellationToken.None)));
                notes.OnRead = null;
                notes.Reads.Clear();
                notes.Sections["p1"] = "s2";
                await ThrowsAsync<InvalidOperationException>(() => Sync(() =>
                    NoteReader.ReadStable(notes, "p1", CancellationToken.None, new HashSet<string> { "s1" })));
                Equal(0, notes.Reads.Count, "An out-of-scope page was read before the scope guard");
            }
        }

        private static Task ParserAsync() => Sync(() =>
        {
            string xml = "<one:Page xmlns:one=\"" + FakeNotes.Namespace + "\" ID=\"p\" name=\"Table\"><one:Outline><one:OEChildren>" +
                "<one:OE objectID=\"table\"><one:Table><one:Row><one:Cell><one:OEChildren><one:OE objectID=\"c1\"><one:T><![CDATA[Revenue<br>2026]]></one:T></one:OE></one:OEChildren></one:Cell>" +
                "<one:Cell><one:OEChildren><one:OE objectID=\"c2\"><one:T>12345</one:T></one:OE></one:OEChildren></one:Cell></one:Row></one:Table>" +
                "<one:OEChildren><one:OE objectID=\"after\"><one:T>After the table</one:T></one:OE></one:OEChildren></one:OE></one:OEChildren></one:Outline></one:Page>";
            var page = PageParser.Parse(xml);
            Check(page.GetPlainText().Contains("Revenue\n2026") && page.GetPlainText().Contains("12345"), "Table text or breaks omitted");
            Equal(3, page.Outlines[0].TextBlocks.Count);
            Equal("c1", page.Outlines[0].TextBlocks.First(b => b.TableColumn == 1).ElementId);
            Check(page.GetPlainText().IndexOf("12345", StringComparison.Ordinal) < page.GetPlainText().IndexOf("After the table", StringComparison.Ordinal), "Table order lost");
        });

        private static Task ChunksAsync() => Sync(() =>
        {
            var notes = new FakeNotes();
            notes.Pages["p1"].Outlines[0].TextBlocks[0].Text = string.Concat(Enumerable.Repeat("知识检索与学习系统。", 900));
            List<NoteChunk> chunks = NoteChunker.Split(notes.Pages["p1"], notes.PageState("p1"));
            Check(chunks.Count > 5, "Long source was not split");
            Check(chunks.All(c => c.BlockId == "block-p1" && c.PageId == "p1"), "Source target lost");
            Check(chunks.All(c => TokenEstimator.Estimate(c.Text) <= 725 && Encoding.UTF8.GetByteCount(c.EmbeddingText) <= 8191), "Chunk input bound exceeded");
            Equal(chunks.Count, chunks.Select(c => c.Id).Distinct().Count());
            notes.Pages["p1"].Outlines[0].TextBlocks[0].Text = "start" + new string(' ', 20000) + "end";
            chunks = NoteChunker.Split(notes.Pages["p1"], notes.PageState("p1"));
            Check(chunks.All(c => Encoding.UTF8.GetByteCount(c.EmbeddingText) <= 8191), "Whitespace escaped embedding input budget");
        });

        private static Task BudgetsAsync() => Sync(() =>
        {
            var request = new ChatRequest { Model = "moonshot-v1-8k", MaxTokens = 4096, Messages = new List<ChatMessage> { ChatMessage.User(new string('中', 6000)) } };
            Check(!ContextBudget.Fits(request, 8192), "Original over-budget request accepted");
            request.Messages[0].Content = "A short question.";
            Check(ContextBudget.Fits(request, 8192), "Short request rejected");
            int baseline = ContextBudget.Measure(request);
            request.Tools = new List<ChatTool> { new ChatTool { Function = new ToolDefinition { Name = "tool", Description = new string('x', 5000), Parameters = JObject.Parse("{\"type\":\"object\"}") } } };
            Check(ContextBudget.Measure(request) > baseline + 5000 && !ContextBudget.Fits(request, 8192), "Schemas were omitted from budget");
            Check(ContextBudget.Measure(request) >= Encoding.UTF8.GetByteCount(DeepseekClient.ClaudePayload(request).ToString(Formatting.None)), "Claude payload not budgeted");
        });

        private static async Task StreamsAsync()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Task read = DeepseekStreamReader.ReadStreamAsync(new StalledStream(), _ => { }, cancellation.Token);
                cancellation.Cancel();
                Equal(read, await Task.WhenAny(read, Task.Delay(1000)).ConfigureAwait(false), "Cancellation is blocked on network read");
                await ThrowsAsync<OperationCanceledException>(() => read);
            }
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("data:{\"error\":{\"message\":\"synthetic failure\"}}\n\n")))
                await ThrowsAsync<InvalidOperationException>(() => DeepseekStreamReader.ReadStreamAsync(stream, _ => { }, CancellationToken.None));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("data:not-json\n\n")))
                await ThrowsAsync<JsonException>(() => DeepseekStreamReader.ReadStreamAsync(stream, _ => { }, CancellationToken.None));
        }

        private static JObject Delta(JObject delta, string finish = null) => new JObject
        { ["choices"] = new JArray(new JObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }) };

        private static DeepseekClient Chat(Func<JObject, HttpResponseMessage> response, AiProvider provider = AiProvider.OpenAI) =>
            new DeepseekClient("synthetic-key", "https://chat.invalid/v1", provider, new HttpClient(new FakeHttp(async (request, token) =>
                response(JObject.Parse(await request.Content.ReadAsStringAsync())))));

        private static async Task OpenAiToolsAsync()
        {
            using (var chat = Chat(_ => FakeHttp.Sse(
                Delta(JObject.Parse("{\"reasoning_content\":\"provider reasoning\",\"tool_calls\":[{\"index\":0,\"id\":\"a\",\"function\":{\"name\":\"change_record\",\"arguments\":\"{\\\"id\\\":\"}},{\"index\":1,\"id\":\"b\",\"function\":{\"name\":\"lookup\",\"arguments\":\"{\\\"query\\\":\"}}]}")),
                Delta(JObject.Parse("{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"x\\\"}\"}},{\"index\":1,\"function\":{\"arguments\":\"\\\"notes\\\"}\"}}]}")),
                Delta(new JObject(), "tool_calls"))))
            {
                ChatChoice reply = await chat.StreamChoiceAsync(new ChatRequest { Model = "test" }, _ => { });
                Equal(2, reply.Message.ToolCalls.Count);
                Equal("x", (string)JObject.Parse(reply.Message.ToolCalls[0].Function.Arguments)["id"]);
                Equal("b", reply.Message.ToolCalls[1].Id);
                Equal("provider reasoning", reply.Message.ReasoningContent);
            }
            using (var chat = Chat(_ => FakeHttp.Sse(Delta(new JObject { ["content"] = "partial" }))))
                await ThrowsAsync<InvalidDataException>(() => chat.StreamChoiceAsync(new ChatRequest(), _ => { }));
            using (var chat = Chat(_ => FakeHttp.Sse(Delta(new JObject(), "length"))))
                await ThrowsAsync<InvalidDataException>(() => chat.StreamChoiceAsync(new ChatRequest(), _ => { }));
        }

        private static async Task ClaudeToolsAsync()
        {
            using (var chat = Chat(_ => FakeHttp.Sse(
                JObject.Parse("{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"c1\",\"name\":\"change_record\",\"input\":{}}}"),
                JObject.Parse("{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"id\\\":\\\"x\\\"}\"}}"),
                JObject.Parse("{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"}}"),
                JObject.Parse("{\"type\":\"message_stop\"}")), AiProvider.Claude))
            {
                ChatChoice reply = await chat.StreamChoiceAsync(new ChatRequest { Model = "test" }, _ => { });
                Equal("x", (string)JObject.Parse(reply.Message.ToolCalls[0].Function.Arguments)["id"]);
                var request = new ChatRequest { Messages = new List<ChatMessage> { ChatMessage.System("system"), reply.Message,
                    new ChatMessage { Role = "tool", ToolCallId = "c1", Content = "{\"ok\":true}" } } };
                JObject body = DeepseekClient.ClaudePayload(request);
                Equal("tool_use", (string)body["messages"][0]["content"][0]["type"]);
                Equal("tool_result", (string)body["messages"][1]["content"][0]["type"]);
                Equal("c1", (string)body["messages"][1]["content"][0]["tool_use_id"]);
            }
        }

        private static async Task EmbeddingsAsync()
        {
            var options = Embedding();
            var fake = new FakeEmbeddings();
            float[][] small = await fake.Client(options).EmbedAsync(new[] { "first", "second" }, CancellationToken.None);
            Equal(1536, small[0].Length);
            Check(small[0][0] < small[1][0], "Response indices were not reordered");
            string smallGeneration = options.Generation;
            options.Model = "text-embedding-3-large";
            Equal(3072, (await fake.Client(options).EmbedAsync(new[] { "large" }, CancellationToken.None))[0].Length);
            options.Dimensions = 1536;
            Check(options.Generation != smallGeneration, "Different models sharing dimensions mixed");
            options = Embedding();
            await ThrowsAsync<InvalidDataException>(() => new FakeEmbeddings { DuplicateIndices = true }.Client(options).EmbedAsync(new[] { "a", "b" }, CancellationToken.None));
            await ThrowsAsync<InvalidDataException>(() => new FakeEmbeddings { Zero = true }.Client(options).EmbedAsync(new[] { "a" }, CancellationToken.None));
            await ThrowsAsync<InvalidDataException>(() => new FakeEmbeddings { WrongDimensions = true }.Client(options).EmbedAsync(new[] { "a" }, CancellationToken.None));
            await ThrowsAsync<InvalidDataException>(() => Sync(() => EmbeddingClient.Normalize(new[] { float.NaN, 1f }, 2)));
        }

        private static Task ScopeAsync() => Sync(() =>
        {
            NoteNode root = new FakeNotes().Hierarchy();
            var expanded = NoteNode.Sections(root, new[] { "book", "group", "s1" });
            Equal(2, expanded.Count);
            Equal(1, NoteNode.Sections(root, new[] { "s1" }).Count);
            NoteNode locked = NoteNode.Parse("<one:Notebooks xmlns:one=\"" + FakeNotes.Namespace + "\"><one:Notebook ID=\"book\"><one:SectionGroup ID=\"g\"><one:Section ID=\"s\" isLocked=\"true\"><one:Page ID=\"p\"/></one:Section></one:SectionGroup></one:Notebook></one:Notebooks>");
            Equal(0, NoteNode.Sections(locked, new[] { "book" }).Count);
            Check(locked.DescendantsAndSelf().Single(n => n.Id == "p").Unavailable, "Lock did not propagate");
        });

        private static async Task IndexingAsync()
        {
            var notes = new FakeNotes();
            var options = Settings().Knowledge;
            var fake = new FakeEmbeddings();
            using (var store = new IndexStore(Folder()))
            {
                var indexing = new IndexingService(notes, store);
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(1, fake.Calls);
                Check(!notes.Reads.Contains("p2") && fake.Texts.All(t => !t.Contains("private content")), "Out-of-scope document uploaded");
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(1, fake.Calls, "Unchanged document re-embedded");
                notes.Pages["p1"].DateModified = notes.Pages["p1"].DateModified.AddMinutes(1);
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(1, fake.Calls, "Timestamp-only edit re-embedded");
                notes.Pages["p1"].Outlines[0].TextBlocks[0].Text += " Changed.";
                notes.Pages["p1"].DateModified = notes.Pages["p1"].DateModified.AddMinutes(1);
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(2, fake.Calls);
                string old = options.Embedding.Generation;
                options.Embedding.Model = "text-embedding-3-large";
                options.Embedding.Dimensions = 1536;
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(3, fake.Calls);
                Equal(0, store.Shards(new[] { "s1" }, old).Count);
                notes.Hidden.Add("p1");
                indexing.Refresh(options);
                Equal(0, store.Shards(new[] { "s1" }, options.Embedding.Generation).Count, "Inaccessible cached note remained searchable");
                notes.Hidden.Clear();
                await indexing.UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(3, fake.Calls, "Temporary unreadability discarded durable vectors");
                notes.Sections["p1"] = "s2";
                options.AllowedRootIds.Add("s2");
                indexing.Refresh(options);
                Equal(0, store.Shards(new[] { "s1" }, options.Embedding.Generation).Count, "Moved page remained in old shard");
                Check(store.Shards(new[] { "s2" }, options.Embedding.Generation).Count > 0, "Same-version move lost semantic recall");
                options.AllowedRootIds.Clear();
                indexing.Refresh(options);
                Equal(0, store.ReusableVectors("p1", options.Embedding.Generation, 1536).Count, "Revoked scope retained vectors");
            }
        }

        private static async Task ResumeAsync()
        {
            var notes = new FakeNotes();
            var options = Settings().Knowledge;
            var fake = new FakeEmbeddings();
            string folder = Folder();
            using (var cancel = new CancellationTokenSource())
            using (var store = new IndexStore(folder))
            {
                fake.AfterResponse = cancel.Cancel;
                await ThrowsAsync<OperationCanceledException>(() => new IndexingService(notes, store).UpdateAsync(options, null, cancel.Token, fake.Client(options.Embedding)));
                Equal(1, store.Pending().Count);
            }
            fake.AfterResponse = null;
            using (var store = new IndexStore(folder))
            {
                await new IndexingService(notes, store).UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(1, fake.Calls, "A completed embedding batch was lost across restart");
                Equal(1, store.Coverage(new[] { "s1" }, options.Embedding.Generation).Indexed);
            }
        }

        private static async Task ConsentAsync()
        {
            var notes = new FakeNotes();
            var options = Settings().Knowledge;
            var current = options.Clone();
            var fake = new FakeEmbeddings { AfterResponse = () => current.AllowedRootIds.Clear() };
            using (var store = new IndexStore(Folder()))
            {
                await ThrowsAsync<InvalidOperationException>(() => new IndexingService(notes, store, () => current)
                    .UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding)));
                Equal(0, store.Coverage(new[] { "s1" }, options.Embedding.Generation).Indexed, "Revoked indexing committed");
            }
            notes = new FakeNotes();
            notes.OnRead = () => notes.Sections["p1"] = "s2";
            fake = new FakeEmbeddings();
            using (var store = new IndexStore(Folder()))
            {
                await new IndexingService(notes, store).UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                Equal(0, fake.Calls, "A page moved out of scope during reading was uploaded");
            }
            notes = new FakeNotes();
            notes.Pages["p1"].Outlines[0].TextBlocks[0].Text = string.Concat(Enumerable.Repeat("knowledge ", 50000));
            Check(NoteChunker.Split(notes.Pages["p1"], notes.PageState("p1")).Count > 32, "Batch fixture is too small");
            fake = new FakeEmbeddings { AfterResponse = () => notes.Sections["p1"] = "s2" };
            using (var store = new IndexStore(Folder()))
            {
                await ThrowsAsync<InvalidOperationException>(() => new IndexingService(notes, store)
                    .UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding)));
                Equal(1, fake.Calls, "Additional batches were sent after a page moved outside consent");
            }
        }

        private static async Task RetrievalAsync()
        {
            var notes = new FakeNotes();
            var options = Settings().Knowledge;
            var fake = new FakeEmbeddings();
            using (var store = new IndexStore(Folder()))
            {
                await new IndexingService(notes, store).UpdateAsync(options, null, CancellationToken.None, fake.Client(options.Embedding));
                var retrieval = new RetrievalService(notes, store, new VectorIndex(store));
                notes.NoSearchHits = true;
                RetrievalResult result = await retrieval.SearchAsync("risk mitigation", new[] { "s1" }, options, CancellationToken.None, fake.Client(options.Embedding));
                Check(result.Chunks.Any(c => c.PageId == "p1"), "Semantic recall depended on native hits");
                Check(result.Chunks.All(c => c.PageId != "p2") && notes.SearchedScopes.All(s => s == "s1"), "Scope leak");
                notes.SearchedScopes.Clear();
                result = await retrieval.SearchAsync("?!", new[] { "s1" }, options, CancellationToken.None, fake.Client(options.Embedding));
                Equal(0, notes.SearchedScopes.Count, "Empty native query searched the whole scope");
                Check(result.Warnings.Any(w => w.Contains("searchable terms")), "Empty keyword query was silently ignored");
                NoteChunk original = retrieval.CurrentPage("p1", "risk", CancellationToken.None).Chunks[0];
                Equal("p1", retrieval.ValidateSource(original).State.Id);
                notes.ReportedVersion = NoteNode.NormalizeVersion("2022-05-16T02:34:50.000Z");
                Check(retrieval.CurrentPage("p1", "risk", CancellationToken.None).Chunks.Count > 0,
                    "Different hierarchy/content clocks caused a false edit error");
                notes.ReportedVersion = null;
                notes.Sections["p1"] = "s2";
                await ThrowsAsync<InvalidOperationException>(() => Sync(() => retrieval.ValidateSource(original, requireCurrentVersion: false)));
                notes.Sections["p1"] = "s1";
                notes.Pages["p1"].DateModified = notes.Pages["p1"].DateModified.AddMinutes(1);
                await ThrowsAsync<InvalidOperationException>(() => Sync(() => retrieval.ValidateSource(original)));
                Equal("p1", retrieval.ValidateSource(original, requireCurrentVersion: false).State.Id);
                notes.Pages["p1"].DateModified = notes.Pages["p1"].DateModified.AddMinutes(-1);
                notes.NoSearchHits = false;
                options.Embedding.ApiKey = "";
                result = await retrieval.SearchAsync("列出项目风险", new[] { "s1" }, options, CancellationToken.None);
                Check(result.Chunks.Any(c => c.Text.Contains("12345")), "Metadata-like question discarded the body");
                Check(result.Warnings.Any(w => w.Contains("Semantic")), "Single-path degradation not visible");
                options.Embedding = Embedding();
                notes.SearchFails = true;
                result = await retrieval.SearchAsync("risk", new[] { "s1" }, options, CancellationToken.None, fake.Client(options.Embedding));
                Check(result.Chunks.Count > 0 && result.Warnings.Any(w => w.Contains("OneNote Search")), "Native failure broke semantic recall");
                notes.SearchFails = false;
                fake.AfterResponse = () => notes.Hidden.Add("p1");
                result = await retrieval.SearchAsync("risk", new[] { "s1" }, options, CancellationToken.None, fake.Client(options.Embedding));
                Equal(0, result.Chunks.Count, "A note locked during retrieval leaked through cached candidates");
            }
        }

        private static Task DistanceAsync() => Sync(() =>
        {
            var random = new Random(27);
            float[] first = EmbeddingClient.Normalize(Enumerable.Range(0, 1536).Select(_ => (float)(random.NextDouble() - .5)).ToArray(), 1536);
            float[] second = EmbeddingClient.Normalize(Enumerable.Range(0, 1536).Select(_ => (float)(random.NextDouble() - .5)).ToArray(), 1536);
            double expected = 1 - first.Select((value, index) => (double)value * second[index]).Sum();
            Check(Math.Abs(VectorIndex.Distance(first, second) - expected) < 0.00001, "Cosine distance precision changed");
            Check(Math.Abs(VectorIndex.Distance(first, first)) < 0.00001, "Self distance is not zero");
            if (!System.Numerics.Vector.IsHardwareAccelerated)
                Equal(HNSW.Net.CosineDistance.ForUnits(first, second), VectorIndex.Distance(first, second), "Software SIMD was selected on an unaccelerated runtime");
        });

        private static Task AnnAsync() => AnnAsync(16, 5000, true);

        private static Task AnnAsync(int dimensions, int count, bool corrupt) => Sync(() =>
        {
            var notes = new FakeNotes();
            var options = Settings().Knowledge;
            options.Embedding.Model = dimensions == 3072 ? "text-embedding-3-large" : "text-embedding-3-small";
            options.Embedding.Dimensions = dimensions;
            string generation = options.Embedding.Generation;
            using (var store = new IndexStore(Folder()))
            {
                store.Synchronize(notes.Hierarchy(), new HashSet<string> { "s1" }, generation);
                var random = new Random(42);
                var chunks = new List<NoteChunk>();
                for (int i = 0; i < count; i++)
                {
                    float[] vector = EmbeddingClient.Normalize(Enumerable.Range(0, dimensions).Select(_ => (float)(random.NextDouble() - .5)).ToArray(), dimensions);
                    chunks.Add(new NoteChunk { Id = LocalState.Hash("chunk-" + i), PageId = "p1", SectionId = "s1", Title = "Synthetic",
                        Path = "Book / s1", Text = "Synthetic chunk " + i, BlockId = "b" + i, Hash = LocalState.Hash("text-" + i), Vector = vector });
                }
                store.CommitPage(notes.PageState("p1"), notes.PageState("p1").Version, chunks, generation);
                var vectors = new VectorIndex(store);
                Check(vectors.Search(chunks[777].Vector, new[] { "s1" }, generation, 20, CancellationToken.None).Any(c => c.Id == chunks[777].Id), "ANN missed exact stored vector");
                Check(vectors.CachedBytes <= (Environment.Is64BitProcess ? 192L : 48L) * 1024 * 1024, "ANN cache exceeded its accounted memory budget");
                string[] files = Directory.GetFiles(store.DirectoryPath, "*.hnsw");
                Check(files.Length > 0, "ANN snapshot was not persisted");
                Equal(0, vectors.Search(chunks[777].Vector, new[] { "s2" }, generation, 20, CancellationToken.None).Count);
                if (corrupt) File.WriteAllText(files[0], "corrupt");
                var warnings = new List<string>();
                vectors = new VectorIndex(store);
                Check(vectors.Search(chunks[777].Vector, new[] { "s1" }, generation, 20, CancellationToken.None, warnings.Add).Any(c => c.Id == chunks[777].Id), "Persisted ANN did not retrieve its source");
                if (corrupt) Check(warnings.Count > 0, "ANN recovery was silent");
                else Equal(0, warnings.Count, "A valid snapshot failed to reload");
            }
        });

        private static McpServerOptions Server(string id = "server") => new McpServerOptions
        { Id = id, Name = id, Endpoint = "https://mcp.invalid/mcp", Authentication = McpAuthentication.Bearer,
            Secret = EncryptionHelper.Encrypt("synthetic-secret"), TimeoutSeconds = 5 };
        private static Task<bool> NoApproval(ToolApproval request, CancellationToken token) => throw new Exception("Auto approve unexpectedly asked for approval.");

        private static async Task McpProtocolAsync()
        {
            foreach (bool sse in new[] { false, true })
            {
                McpServerOptions server = Server();
                var fake = new FakeMcpHttp { UseSse = sse };
                await using (var manager = new McpManager(() => new[] { server }, _ => fake))
                {
                    List<RemoteTool> tools = await manager.DiscoverAsync(new[] { server.Id }, null, CancellationToken.None);
                    Equal(2, tools.Count);
                    Check(fake.Lists >= 2, "Tool pagination missing");
                    ToolExecution result = await manager.ExecuteAsync(tools.Single(t => t.OriginalName == "change_record"), "auto-" + sse,
                        "{\"id\":\"record\"}", NoApproval, null, CancellationToken.None);
                    Equal("returned", result.Status);
                    Equal(1, fake.Calls);
                    Equal("record", (string)fake.LastArguments["id"]);
                    Check(result.Result["structuredContent"]["data"] is JArray, "Structured data discarded");
                    Check(!result.Result.ToString().Contains("synthetic-secret"), "Credential exposed in result");
                    Check(result.Result.ToString().Contains("non-text data omitted"), "Unsupported image was silently discarded");
                    JObject directory = await manager.DirectoryAsync(server.Id, CancellationToken.None);
                    Equal(1, directory["resources"].Count());
                    Equal(1, directory["prompts"].Count());
                    JObject resource = await manager.ReadResourceAsync(server.Id, "file:///remote-only/data.txt", CancellationToken.None);
                    Check(resource.ToString().Contains("Remote, not local disk."), "Remote URI not resolved through MCP");
                    Check((await manager.GetPromptAsync(server.Id, "learn", "{}", CancellationToken.None)).ToString().Contains("Explain"), "Prompt access failed");
                    Check(fake.Methods.Contains("notifications/initialized"), "Initialization notification missing");
                    Check(fake.Headers.Any(h => h.Contains("Mcp-Session-Id")), "Session header missing");
                    Check(fake.Authorizations.All(h => h == "Bearer synthetic-secret"), "MCP access token was not sent in the expected Authorization header");
                    Check(fake.Headers.Any(h => h.Contains("MCP-Protocol-Version") || h.Contains("Mcp-Protocol-Version")), "Protocol header missing");
                    fake.ToolError = true;
                    ToolExecution error = await manager.ExecuteAsync(tools.Single(t => t.OriginalName == "change_record"), "error-" + sse,
                        "{\"id\":\"record\"}", NoApproval, null, CancellationToken.None);
                    Equal("tool_error", error.Status, "Server tool error was presented as success");
                }
            }
            await ThrowsAsync<InvalidOperationException>(() => Sync(() => new McpServerOptions { Endpoint = "http://mcp.invalid" }.Validate()));
        }

        private static async Task McpPoliciesAsync()
        {
            McpServerOptions server = Server();
            var fake = new FakeMcpHttp();
            await using (var manager = new McpManager(() => new[] { server }, _ => fake))
            {
                RemoteTool tool = (await manager.DiscoverAsync(new[] { server.Id }, null, CancellationToken.None)).First(t => t.OriginalName == "change_record");
                ToolExecution invalid = await manager.ExecuteAsync(tool, "invalid", "{\"id\":42}", NoApproval, null, CancellationToken.None);
                Equal("rejected", invalid.Status);
                Equal(0, fake.Calls);
                server.Tools[tool.OriginalName] = new McpToolPolicy { RequireApproval = true };
                ToolExecution declined = await manager.ExecuteAsync(tool, "declined", "{\"id\":\"x\"}", (r, c) => Task.FromResult(false), null, CancellationToken.None);
                Equal("rejected", declined.Status);
                ToolExecution mutated = await manager.ExecuteAsync(tool, "mutated", "{\"id\":\"x\"}", (r, c) =>
                { r.Arguments = "{\"id\":\"other\"}"; return Task.FromResult(true); }, null, CancellationToken.None);
                Equal("rejected", mutated.Status);
                ToolExecution disabled = await manager.ExecuteAsync(tool, "disabled", "{\"id\":\"x\"}", (r, c) =>
                { server.Tools[tool.OriginalName].Enabled = false; return Task.FromResult(true); }, null, CancellationToken.None);
                Equal("rejected", disabled.Status);
                server.Tools[tool.OriginalName].Enabled = true;
                ToolExecution changed = await manager.ExecuteAsync(tool, "changed", "{\"id\":\"x\"}", (r, c) =>
                { fake.ChangedSchema = true; return Task.FromResult(true); }, null, CancellationToken.None);
                Equal("rejected", changed.Status);
                Equal(0, fake.Calls);
                fake.ChangedSchema = false;
                int approvals = 0;
                ToolExecution approved = await manager.ExecuteAsync(tool, "approved", "{\"id\":\"x\"}", (r, c) =>
                { approvals++; return Task.FromResult(true); }, null, CancellationToken.None);
                Equal("returned", approved.Status);
                Equal(1, approvals);
                await manager.CloseAsync(server.Id);
                await manager.DiscoverAsync(new[] { server.Id }, null, CancellationToken.None);
                Check(server.Tools[tool.OriginalName].RequireApproval, "Reconnect reset explicit policy");
                tool = (await manager.DiscoverAsync(new[] { server.Id }, null, CancellationToken.None)).First(t => t.OriginalName == "change_record");
                ToolExecution signedOut = await manager.ExecuteAsync(tool, "signed-out", "{\"id\":\"x\"}", (r, c) =>
                { new McpTokenCache(server.Identity, Folder()).Clear(); return Task.FromResult(true); }, null, CancellationToken.None);
                Equal("rejected", signedOut.Status, "Logout did not invalidate pending approval");
            }
            var first = Server("first");
            var second = Server("second");
            await using (var manager = new McpManager(() => new[] { first, second }, _ => new FakeMcpHttp()))
            {
                var tools = await manager.DiscoverAsync(new[] { first.Id, second.Id }, null, CancellationToken.None);
                Equal(4, tools.Select(t => t.Alias).Distinct().Count(), "Cross-server tool name collision");
            }
        }

        private static async Task McpUnknownAsync()
        {
            McpServerOptions server = Server();
            var fake = new FakeMcpHttp { FailAfterDispatch = true };
            await using (var manager = new McpManager(() => new[] { server }, _ => fake))
            {
                RemoteTool tool = (await manager.DiscoverAsync(new[] { server.Id }, null, CancellationToken.None)).First(t => t.OriginalName == "change_record");
                ToolExecution result = await manager.ExecuteAsync(tool, "uncertain", "{\"id\":\"x\"}", NoApproval, null, CancellationToken.None);
                Check(result.OutcomeUnknown, "Lost write response was presented as a known failure/success");
                Equal(1, fake.Calls, "Write was replayed after lost response");
                await ThrowsAsync<InvalidOperationException>(() => manager.ExecuteAsync(tool, "uncertain", "{\"id\":\"x\"}", NoApproval, null, CancellationToken.None));
                Equal(1, fake.Calls);
            }
        }

        private static async Task TokenCacheAsync()
        {
            string directory = Folder();
            var cache = new McpTokenCache("server-identity", directory);
            await cache.StoreTokensAsync(new TokenContainer { TokenType = "Bearer", AccessToken = "private-access-token",
                RefreshToken = "private-refresh-token", ObtainedAt = DateTimeOffset.UtcNow, ClientId = "registered-client" }, CancellationToken.None);
            Check(!File.ReadAllText(Directory.GetFiles(directory).Single()).Contains("private"), "OAuth token not encrypted");
            Equal("private-refresh-token", (await new McpTokenCache("server-identity", directory).GetTokensAsync(CancellationToken.None)).RefreshToken);
            Equal<TokenContainer>(null, await new McpTokenCache("different-server", directory).GetTokensAsync(CancellationToken.None));
            cache.Clear();
            Equal<TokenContainer>(null, await cache.GetTokensAsync(CancellationToken.None));
            await ThrowsAsync<InvalidOperationException>(() => cache.StoreTokensAsync(new TokenContainer
                { TokenType = "Bearer", AccessToken = "stale-access-token", RefreshToken = "stale-refresh-token",
                    ObtainedAt = DateTimeOffset.UtcNow, ClientId = "registered-client" }, CancellationToken.None).AsTask());
            using (var request = new MemoryStream(Encoding.ASCII.GetBytes("GET /callback/?code=test HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")))
                Equal("GET /callback/?code=test HTTP/1.1", await McpOAuth.ReadCallbackRequestAsync(request, CancellationToken.None));
            using (var oversized = new MemoryStream(new byte[8193]))
                await ThrowsAsync<InvalidDataException>(() => McpOAuth.ReadCallbackRequestAsync(oversized, CancellationToken.None));
        }

        private static async Task OAuthProtocolAsync()
        {
            var cache = new McpTokenCache("oauth-protocol", Folder());
            var grants = new List<System.Collections.Specialized.NameValueCollection>();
            string challenge = null;
            int browserCalls = 0;
            bool wrongState = false;
            string acceptedToken = "access-1";
            using (var backend = new HttpMessageInvoker(new FakeMcpHttp()))
            using (var http = new HttpClient(new HttpsOnlyHandler(new FakeHttp(async (request, token) =>
            {
                string path = request.RequestUri.AbsolutePath;
                if (path == "/mcp")
                {
                    if (request.Headers.Authorization?.Parameter == acceptedToken)
                        return await backend.SendAsync(request, token);
                    var unauthorized = new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
                        { RequestMessage = request, Content = new StringContent("") };
                    unauthorized.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer resource_metadata=\"https://mcp.invalid/.well-known/oauth-protected-resource\"");
                    return unauthorized;
                }
                if (path == "/.well-known/oauth-protected-resource")
                    return FakeHttp.Json(JObject.Parse("{\"resource\":\"https://mcp.invalid/mcp\",\"authorization_servers\":[\"https://auth.invalid\"],\"scopes_supported\":[\"read\"]}"));
                if (path == "/.well-known/oauth-authorization-server")
                    return FakeHttp.Json(JObject.Parse("{\"issuer\":\"https://auth.invalid\",\"authorization_endpoint\":\"https://auth.invalid/authorize\",\"token_endpoint\":\"https://auth.invalid/token\",\"response_types_supported\":[\"code\"],\"grant_types_supported\":[\"authorization_code\",\"refresh_token\"],\"code_challenge_methods_supported\":[\"S256\"],\"token_endpoint_auth_methods_supported\":[\"none\"],\"authorization_response_iss_parameter_supported\":true}"));
                if (path == "/token")
                {
                    grants.Add(System.Web.HttpUtility.ParseQueryString(await request.Content.ReadAsStringAsync()));
                    return FakeHttp.Json(new JObject { ["access_token"] = "access-" + grants.Count, ["refresh_token"] = "refresh-" + grants.Count,
                        ["token_type"] = "Bearer", ["expires_in"] = 3600, ["scope"] = "read" });
                }
                throw new InvalidOperationException("Unexpected OAuth request: " + request.RequestUri);
            }))))
            {
                var options = new ClientOAuthOptions
                {
                    ClientId = "synthetic-client", RedirectUri = new Uri("http://127.0.0.1:38741/callback/"),
                    Scopes = new[] { "read" }, TokenCache = cache,
                    AuthorizationCallbackHandler = (context, token) =>
                    {
                        browserCalls++;
                        var query = System.Web.HttpUtility.ParseQueryString(context.AuthorizationUri.Query);
                        Equal("https://mcp.invalid/mcp", query["resource"]);
                        Equal("S256", query["code_challenge_method"]);
                        Equal("http://127.0.0.1:38741/callback/", query["redirect_uri"]);
                        challenge = query["code_challenge"];
                        return Task.FromResult(new AuthorizationResult { Code = "synthetic-code",
                            State = wrongState ? "wrong-state" : query["state"], Iss = "https://auth.invalid" });
                    }
                };
                async Task Connect()
                {
                    using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    await using (var transport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Endpoint = new Uri("https://mcp.invalid/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
                        OAuth = options, MaxReconnectionAttempts = 0
                    }, http))
                    await using (var client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token))
                        Equal(2, (await client.ListToolsAsync(cancellationToken: deadline.Token)).Count);
                }
                await Connect();
                Equal("access-1", (await cache.GetTokensAsync(CancellationToken.None)).AccessToken);
                Equal("authorization_code", grants[0]["grant_type"]);
                Equal("synthetic-code", grants[0]["code"]);
                Equal("https://mcp.invalid/mcp", grants[0]["resource"]);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    Equal(challenge, Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(grants[0]["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
                TokenContainer cached = await cache.GetTokensAsync(CancellationToken.None);
                cached.ObtainedAt = DateTimeOffset.UtcNow.AddHours(-2);
                await cache.StoreTokensAsync(cached, CancellationToken.None);
                acceptedToken = "access-2";
                await Connect();
                Equal("access-2", (await cache.GetTokensAsync(CancellationToken.None)).AccessToken);
                Equal(1, browserCalls, "Refresh unnecessarily reopened authorization");
                Equal("refresh_token", grants[1]["grant_type"]);
                Equal("refresh-1", grants[1]["refresh_token"]);
                Equal("refresh-2", (await cache.GetTokensAsync(CancellationToken.None)).RefreshToken);
                cache.Clear();
                options.TokenCache = new McpTokenCache("oauth-protocol", Folder());
                wrongState = true;
                Exception failure = null;
                try { await Connect(); }
                catch (Exception ex) { failure = ex; }
                Check(failure != null && failure.ToString().IndexOf("state", StringComparison.OrdinalIgnoreCase) >= 0, "Mismatched OAuth state was accepted");
                Equal(2, grants.Count, "Invalid authorization response reached the token endpoint");
            }
        }

        private static async Task ConversationAsync()
        {
            var notes = new FakeNotes { ReportedVersion = NoteNode.NormalizeVersion("2022-05-16T02:34:50.000Z") };
            var settings = Settings();
            var captured = new List<JObject>();
            using (var store = new IndexStore(Folder()))
            await using (var mcp = new McpManager(() => Array.Empty<McpServerOptions>()))
            using (var chat = Chat(body =>
            {
                captured.Add(body);
                string references = string.Join("\n", body["messages"].Select(m => (string)m["content"]));
                var match = System.Text.RegularExpressions.Regex.Match(references, "\"id\":\"(S\\d+)\"");
                return FakeHttp.Sse(Delta(new JObject { ["content"] = "Current answer [" + match.Groups[1].Value + "]" }), Delta(new JObject(), "stop"));
            }))
            {
                var conversation = new KnowledgeConversation(new RetrievalService(notes, store, new VectorIndex(store)), mcp);
                KnowledgeAnswer first = await conversation.AnswerAsync("First question", "p1", Array.Empty<string>(), Array.Empty<string>(), settings,
                    null, null, null, NoApproval, CancellationToken.None, chat);
                Check(first.Sources.All(s => s.Note.PageId == "p1"), "First source binding wrong");
                notes.Pages["p1"].Outlines[0].TextBlocks[0].Text = "Updated source version.";
                notes.Pages["p1"].DateModified = notes.Pages["p1"].DateModified.AddMinutes(1);
                await conversation.AnswerAsync("Follow-up", "p1", Array.Empty<string>(), Array.Empty<string>(), settings, null, null, null, NoApproval, CancellationToken.None, chat);
                Check(!captured[1].ToString().Contains("rollback plan") && !captured[1].ToString().Contains("Current answer"), "Stale evidence/history reused");
                await conversation.AnswerAsync("New scope", "p2", Array.Empty<string>(), Array.Empty<string>(), settings, null, null, null, NoApproval, CancellationToken.None, chat);
                Check(!captured[2].ToString().Contains("First question") && !captured[2].ToString().Contains("Updated source version"), "Scope switch retained source context");
            }
        }

        private static async Task ToolLoopAsync()
        {
            McpServerOptions server = Server();
            var fake = new FakeMcpHttp();
            var settings = Settings();
            settings.Knowledge.Servers.Add(server);
            int requests = 0;
            using (var store = new IndexStore(Folder()))
            await using (var manager = new McpManager(() => new[] { server }, _ => fake))
            using (var chat = Chat(body =>
            {
                requests++;
                if (requests == 1)
                {
                    string name = (string)body["tools"].First(t => ((string)t["function"]["description"]).Contains("change_record"))["function"]["name"];
                    return FakeHttp.Sse(Delta(new JObject { ["tool_calls"] = new JArray(new JObject { ["index"] = 0, ["id"] = "call1",
                        ["function"] = new JObject { ["name"] = name, ["arguments"] = "{\"id\":\"requested-record\"}" } }) }), Delta(new JObject(), "tool_calls"));
                }
                JToken message = body["messages"].Last();
                Equal("tool", (string)message["role"]);
                Equal("call1", (string)message["tool_call_id"]);
                Check(((string)message["content"]).Contains("\"id\":\"M1\""), "External source was not bound");
                return FakeHttp.Sse(Delta(new JObject { ["content"] = "Operation returned [M1]." }), Delta(new JObject(), "stop"));
            }))
            {
                var conversation = new KnowledgeConversation(new RetrievalService(new FakeNotes(), store, new VectorIndex(store)), manager);
                KnowledgeAnswer answer = await conversation.AnswerAsync("Update the requested record.", "", Array.Empty<string>(), new[] { server.Id }, settings,
                    null, null, null, NoApproval, CancellationToken.None, chat);
                Equal(2, requests);
                Equal(1, fake.Calls);
                Equal("M1", answer.Sources.Single().Id);
                Equal("returned", answer.Sources.Single().Execution.Status);
            }
        }

        private static Task UiAsync() => Sync(() =>
        {
            SetStatic(typeof(SettingsManager), "_current", Settings());
            UiThread.Send(() =>
            {
                using (var result = new ResultDialog("Synthetic"))
                {
                    result.OnFollowUp = () => { };
                    result.OnRegenerate = () => { };
                    result.SetResult("Completed");
                    typeof(ResultDialog).GetMethod("OnFollowUpClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(result, new object[] { null, EventArgs.Empty });
                    Check(((Button)typeof(ResultDialog).GetField("_btnFollowUp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(result)).Enabled, "Cancelled follow-up disabled buttons");
                }
                string images = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews");
                Directory.CreateDirectory(images);
                using (var settings = new KnowledgeSettingsDialog(new FakeNotes().Hierarchy()))
                {
                    Preview(settings, Path.Combine(images, "knowledge-settings.png"));
                }
                using (var settings = new SettingsDialog())
                    Preview(settings, Path.Combine(images, "model-settings.png"));
                using (var server = new McpServerDialog(Server()))
                {
                    var fields = server.Controls.OfType<TableLayoutPanel>().Single();
                    var authentication = fields.Controls.OfType<ComboBox>().Single();
                    Equal(McpAuthentication.Bearer, (McpAuthentication)authentication.SelectedItem);
                    var secret = fields.Controls.OfType<TextBox>().Single(t => t.UseSystemPasswordChar && t.Enabled);
                    Check(!fields.Controls.OfType<NumericUpDown>().Single(n => n.Minimum == 1024).Enabled, "Bearer enabled OAuth callback settings");
                    authentication.SelectedItem = McpAuthentication.None;
                    Check(!secret.Enabled, "None authentication left the token enabled");
                    authentication.SelectedItem = McpAuthentication.OAuth;
                    Check(!secret.Enabled, "OAuth requested a manual access token");
                    Check(fields.Controls.OfType<NumericUpDown>().Single(n => n.Minimum == 1024).Enabled, "OAuth callback settings disabled");
                    authentication.SelectedItem = McpAuthentication.Bearer;
                    Preview(server, Path.Combine(images, "mcp-bearer-settings.png"));
                }
                using (var store = new IndexStore(Folder()))
                using (var main = new KnowledgeDialog(new FakeNotes(), store, () => "p1"))
                {
                    Preview(main, Path.Combine(images, "knowledge-assistant.png"));
                }
            });
        });

        private static void Preview(Form form, string path)
        {
            using (var host = new PreviewHost { Size = form.Size, Text = form.Text })
            {
                form.TopLevel = false;
                form.FormBorderStyle = FormBorderStyle.None;
                form.Dock = DockStyle.Fill;
                host.Controls.Add(form);
                host.Show();
                form.Show();
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 300) { Application.DoEvents(); Thread.Sleep(5); }
                Check(form.Controls.Cast<Control>().Any(c => c.Visible && c.Width > 0 && c.Height > 0), "Preview did not create visible controls");
                using (var bitmap = new Bitmap(host.Width, host.Height))
                { host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
                form.Close();
                host.Close();
            }
        }

        private sealed class PreviewHost : Form
        {
            protected override bool ShowWithoutActivation => true;
            public PreviewHost() { ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-32000, -32000); }
        }
    }
}
