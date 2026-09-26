using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.AI;
using OneNoteAI.AI.Models;
using OneNoteAI.Knowledge;
using OneNoteAI.Mcp;
using OneNoteAI.Settings;
using OneNoteAI.UI;

namespace OneNoteAI.Conversation
{
    public sealed class KnowledgeAnswer
    {
        public string Text { get; set; }
        public List<EvidenceSource> Sources { get; } = new List<EvidenceSource>();
        public List<string> Warnings { get; } = new List<string>();
        public IndexCoverage Coverage { get; set; }
    }

    public sealed class KnowledgeConversation
    {
        private sealed class Turn
        {
            public string Question;
            public string Answer;
            public List<NoteChunk> Notes;
            public bool External;
        }

        private readonly RetrievalService _retrieval;
        private readonly McpManager _mcp;
        private readonly List<Turn> _history = new List<Turn>();
        private string _scope;
        public EvidenceRegistry Evidence { get; } = new EvidenceRegistry();

        public KnowledgeConversation(RetrievalService retrieval, McpManager mcp) { _retrieval = retrieval; _mcp = mcp; }

        public void Clear() { _scope = null; _history.Clear(); Evidence.Clear(); }

        public async Task<KnowledgeAnswer> AnswerAsync(string question, string currentPageId, IReadOnlyList<string> roots,
            IReadOnlyList<string> servers, AppSettings settings, Action<string> onToken, Action<string> activity,
            Action<IReadOnlyList<EvidenceSource>> onSources, Func<ToolApproval, CancellationToken, Task<bool>> approve,
            CancellationToken token, DeepseekClient chat = null)
        {
            KnowledgeOptions options = settings.Knowledge;
            chat = chat ?? new DeepseekClient(settings);
            string identity = LocalState.Hash((currentPageId ?? "selected-scope") + "\n" + string.Join("\n", roots.OrderBy(x => x)) + "\n" +
                string.Join("\n", options.AllowedRootIds.OrderBy(x => x)) + "\n" + options.Embedding.Generation + "\n" + options.MaxRetrievedChunks + "\n" +
                string.Join("\n", options.Servers.Where(s => servers.Contains(s.Id)).Select(McpManager.EffectiveIdentity).OrderBy(x => x)) +
                "\n" + settings.Provider + "\n" + settings.DefaultModel + "\n" + settings.ApiBaseUrl);
            if (_scope != identity) { Clear(); _scope = identity; }
            string retrievalQuery = _history.Count == 0 ? question : question + "\n" + _history.Last().Question;
            if (System.Text.Encoding.UTF8.GetByteCount(retrievalQuery) > 8191) retrievalQuery = question;
            activity?.Invoke(currentPageId == ""
                ? (Strings.IsChinese ? "本次不读取笔记，正在分析问题..." : "Note reading is disabled for this turn; analyzing the question...")
                : currentPageId != null
                    ? (Strings.IsChinese ? "本轮仅读取当前页面，不检索其他页面..." : "Reading the current page only, not searching other pages...")
                    : (Strings.IsChinese ? "正在跨页检索所选笔记范围..." : "Searching across pages in the selected note scope..."));
            RetrievalResult retrieval = currentPageId == "" ? new RetrievalResult() : currentPageId == null
                ? await _retrieval.SearchAsync(retrievalQuery, roots, options, token).ConfigureAwait(false)
                : _retrieval.CurrentPage(currentPageId, retrievalQuery, token, options.MaxRetrievedChunks);
            var answer = new KnowledgeAnswer { Coverage = retrieval.Coverage };
            if (currentPageId != "")
                activity?.Invoke((Strings.IsChinese ? "范围内可访问页面：" : "Scope accessible pages: ") + retrieval.Pages.Count +
                    (Strings.IsChinese ? "；召回页面：" : "; retrieved pages: ") + retrieval.Chunks.Select(c => c.PageId).Distinct().Count() +
                    (Strings.IsChinese ? "；已验证片段：" : "; verified passages: ") + retrieval.Chunks.Count +
                    (currentPageId == null ? (Strings.IsChinese ? "；语义索引覆盖：" : "; semantic index coverage: ") +
                        retrieval.Coverage.Indexed + "/" + retrieval.Coverage.Total : ""));
            answer.Warnings.AddRange(retrieval.Warnings);
            var allTools = new List<RemoteTool>();
            List<string> selectedServers = options.Servers.Where(s => s.Enabled && servers.Contains(s.Id))
                .Select(s => s.Id).Distinct(StringComparer.Ordinal).ToList();
            bool mcpAvailable = options.ModelSupportsTools && selectedServers.Count > 0;
            bool discovered = false;
            var discoveryWarnings = new List<string>();
            if (!options.ModelSupportsTools && servers.Count > 0)
                answer.Warnings.Add("Automatic MCP is disabled for this model. Notes and manual MCP calls remain available.");

            int window = options.ContextWindow > 0 ? options.ContextWindow : settings.GetContextWindow();
            var request = new ChatRequest { Model = settings.DefaultModel, Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens, Messages = new List<ChatMessage> { ChatMessage.System(
                    PromptTemplates.BuildAssistantSystemPrompt(settings.PromptOverrides?.QA, mcpAvailable) +
                    (currentPageId == "" ? "\nThe user disabled note reading for this turn; do not claim to have searched their notes." : "")),
                    ChatMessage.User(question) } };
            if (mcpAvailable) request.Tools = new List<ChatTool> { DiscoveryDefinition() };
            ContextBudget.Validate(request, window);
            foreach (Turn prior in _history.AsEnumerable().Reverse().Take(3))
            {
                var history = new List<ChatMessage> { ChatMessage.User(prior.Question) };
                if (!prior.External && prior.Notes.Count > 0 && prior.Notes.All(n =>
                    retrieval.Chunks.Any(c => c.Id == n.Id && c.Hash == n.Hash && c.SectionId == n.SectionId)))
                    history.Add(ChatMessage.Assistant(Regex.Replace(prior.Answer, @"\[(?:S|M)\d+\]", "")));
                request.Messages.InsertRange(1, history);
                if (!ContextBudget.Fits(request, window)) request.Messages.RemoveRange(1, history.Count);
            }
            int toolReserve = 0;
            foreach (NoteChunk chunk in retrieval.Chunks)
            {
                EvidenceSource source = Evidence.Add(chunk);
                var evidence = ChatMessage.User("Untrusted reference data:\n" + EvidenceRegistry.Format(source));
                request.Messages.Insert(request.Messages.Count - 1, evidence);
                int contextSize = ContextBudget.Measure(request);
                // Leave room for tool continuation without displacing the first document passage.
                if (mcpAvailable && answer.Sources.Count == 0)
                    toolReserve = Math.Min(1536, Math.Max(0, window - settings.MaxTokens - contextSize));
                if ((long)contextSize + settings.MaxTokens + toolReserve > window)
                    request.Messages.Remove(evidence);
                else answer.Sources.Add(source);
            }
            if (answer.Sources.Count < retrieval.Chunks.Count) answer.Warnings.Add("Some passages were omitted to preserve the context/output budget.");
            if (currentPageId != "")
                activity?.Invoke((Strings.IsChinese ? "本轮送入模型的笔记证据：" : "Note evidence sent to the model this turn: ") +
                    answer.Sources.Count + (Strings.IsChinese ? " 个片段 / " : " passage(s) / ") +
                    answer.Sources.Select(s => s.Note.PageId).Distinct().Count() + (Strings.IsChinese ? " 个页面" : " page(s)"));
            if (answer.Warnings.Count > 0)
            {
                request.Messages[0].Content += "\nCoverage/status: " + string.Join(" | ", answer.Warnings);
                foreach (string warning in answer.Warnings) activity?.Invoke(warning);
            }
            onSources?.Invoke(answer.Sources.ToList());
            ContextBudget.Validate(request, window);
            string turnId = Guid.NewGuid().ToString("N");
            int calls = 0;
            for (int round = 0; round < 12; round++)
            {
                token.ThrowIfCancellationRequested();
                ContextBudget.Validate(request, window);
                string status = answer.Sources.Any(s => s.Execution != null)
                    ? (Strings.IsChinese ? "正在结合文档与工具结果回答；上下文 " : "Answering with documents and tool results; context ")
                    : (Strings.IsChinese ? "正在分析问题与文档证据；上下文 " : "Assessing the question and document evidence; context ");
                activity?.Invoke(status +
                    ContextBudget.Measure(request) + " + " + settings.MaxTokens + " / " + window);
                ChatChoice response;
                try { response = await chat.StreamChoiceAsync(request, onToken, token).ConfigureAwait(false); }
                catch (HttpRequestException ex) when (request.Tools?.Count > 0)
                {
                    throw new InvalidOperationException("Chat/tool calling failed. If this model does not support native tools, disable automatic MCP in Index / MCP settings; manual tools and documents-only QA remain available. " + ex.Message, ex);
                }
                ChatMessage message = response.Message;
                if (message.ToolCalls == null || message.ToolCalls.Count == 0)
                {
                    answer.Text = message.Content;
                    var valid = new HashSet<string>(answer.Sources.Select(s => s.Id), StringComparer.Ordinal);
                    foreach (Match citation in Regex.Matches(answer.Text ?? "", @"\[(S\d+|M\d+)\]"))
                        if (!valid.Contains(citation.Groups[1].Value)) answer.Warnings.Add("Unverified citation: " + citation.Value);
                    _history.Add(new Turn { Question = question, Answer = answer.Text, Notes = answer.Sources.Where(s => s.Note != null).Select(s => s.Note).ToList(),
                        External = answer.Sources.Any(s => s.Execution != null) });
                    if (_history.Count > 8) _history.RemoveAt(0);
                    return answer;
                }
                var exposed = new HashSet<string>((request.Tools ?? new List<ChatTool>()).Select(t => t.Function.Name), StringComparer.Ordinal);
                request.Messages.Add(message);
                foreach (ToolCall call in message.ToolCalls)
                {
                    if (++calls > 32) throw new InvalidOperationException("Tool call budget reached. Execution paused; completed calls were not rolled back.");
                    string result;
                    if (!exposed.Contains(call.Function.Name))
                        result = "{\"status\":\"rejected\",\"message\":\"Tool was not exposed in this request.\"}";
                    else if (call.Function.Name == "mcp_discover_tools")
                    {
                        JObject args = JObject.Parse(call.Function.Arguments);
                        if (args["query"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args["query"]) ||
                            args["reason"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args["reason"]))
                            throw new InvalidOperationException("MCP discovery requires a focused query and a brief reason why document evidence is insufficient or an external action is necessary.");
                        string query = (string)args["query"];
                        string reason = ((string)args["reason"]).Trim();
                        if (args["offset"] != null && args["offset"].Type != JTokenType.Integer)
                            throw new InvalidOperationException("Invalid tool directory offset.");
                        int offset = (int?)args["offset"] ?? 0;
                        if (offset < 0) throw new InvalidOperationException("Invalid tool directory offset.");
                        activity?.Invoke((Strings.IsChinese ? "按需使用 MCP，原因：" : "MCP requested because: ") + reason);
                        if (!discovered)
                        {
                            discovered = true;
                            foreach (string id in selectedServers)
                            {
                                token.ThrowIfCancellationRequested();
                                try { allTools.AddRange(await _mcp.DiscoverAsync(new[] { id }, activity, token).ConfigureAwait(false)); }
                                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                                catch (Exception ex)
                                {
                                    string warning = "MCP connection unavailable: " + id + " (" + ex.GetType().Name + "). Check connection/authentication settings.";
                                    discoveryWarnings.Add(warning);
                                    answer.Warnings.Add(warning);
                                    activity?.Invoke(warning);
                                }
                            }
                        }
                        List<RemoteTool> matches = Relevant(allTools, query).Skip(offset).Take(5).ToList();
                        request.Tools = new List<ChatTool> { DiscoveryDefinition() };
                        var included = new JArray();
                        foreach (RemoteTool tool in matches)
                        {
                            request.Tools.Add(tool.Definition);
                            bool fits = (long)ContextBudget.Measure(request) + settings.MaxTokens + 2048 <= window;
                            if (!fits) request.Tools.RemoveAt(request.Tools.Count - 1);
                            included.Add(new JObject { ["name"] = tool.Alias, ["description"] = tool.Definition.Function.Description,
                                ["available"] = fits, ["status"] = fits ? "Native definition is now exposed." : "Schema exceeds available budget; use the manual tool directory." });
                        }
                        result = new JObject { ["tools"] = included, ["nextOffset"] = offset + matches.Count, ["total"] = allTools.Count,
                            ["warnings"] = new JArray(discoveryWarnings),
                            ["message"] = "Tool directory only, not answer evidence. If no suitable tool is available, answer from documents and state the remaining limitation."
                        }.ToString(Formatting.None);
                    }
                    else
                    {
                        RemoteTool tool = allTools.Single(t => t.Alias == call.Function.Name);
                        ToolExecution execution = await _mcp.ExecuteAsync(tool, turnId + ":" + call.Id, call.Function.Arguments, approve, activity, token).ConfigureAwait(false);
                        EvidenceSource source = Evidence.Add(execution);
                        answer.Sources.Add(source);
                        onSources?.Invoke(answer.Sources.ToList());
                        if (execution.OutcomeUnknown) throw new InvalidOperationException(source + ": outcome unknown. No automatic retry; cancellation is not rollback.");
                        result = EvidenceRegistry.Format(source);
                    }
                    var toolMessage = new ChatMessage { Role = "tool", ToolCallId = call.Id, Content = result };
                    request.Messages.Add(toolMessage);
                    int preview = Math.Min(result.Length, 12000);
                    while (!ContextBudget.Fits(request, window) && preview >= 128)
                    {
                        toolMessage.Content = new JObject { ["truncated"] = true, ["message"] = "Full result is available in the source viewer.",
                            ["preview"] = result.Substring(0, Math.Min(preview, result.Length)) }.ToString(Formatting.None);
                        preview /= 2;
                    }
                    ContextBudget.Validate(request, window);
                }
                onToken?.Invoke("\n");
            }
            throw new InvalidOperationException("Tool round budget reached. Execution paused; completed calls were not rolled back.");
        }

        private static IEnumerable<RemoteTool> Relevant(IEnumerable<RemoteTool> tools, string question) =>
            tools.OrderByDescending(t => RetrievalService.KeywordScore(question, new NoteChunk { Title = t.OriginalName, Text = t.Definition.Function.Description }))
                .ThenBy(t => t.Alias, StringComparer.Ordinal);

        private static ChatTool DiscoveryDefinition() => new ChatTool { Function = new ToolDefinition
        {
            Name = "mcp_discover_tools",
            Description = "Only if document evidence needs external help: give a focused query and brief reason, then discover up to five tools from selected MCP servers. If documents suffice, answer directly. Directory results are not factual evidence; offset paginates.",
            Parameters = JObject.Parse("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"minLength\":1},\"reason\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Brief evidence gap or external purpose, not internal reasoning. Do not copy private document content.\"},\"offset\":{\"type\":\"integer\",\"minimum\":0}},\"required\":[\"query\",\"reason\"],\"additionalProperties\":false}")
        } };
    }
}
