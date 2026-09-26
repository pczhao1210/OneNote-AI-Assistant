using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.AI.Models;
using OneNoteAI.Settings;

namespace OneNoteAI.AI
{
    public class DeepseekClient : IDisposable
    {
        private static readonly HttpClient SharedClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly AiProvider _provider;
        private readonly ChatApiOptions _options;

        public DeepseekClient(AppSettings settings) : this(EncryptionHelper.Decrypt(settings.ApiKey),
            settings.GetEffectiveBaseUrl(), settings.Provider, requestOptions: settings.GetChatApiOptions()) { }

        public DeepseekClient(string apiKey, string baseUrl = "https://api.deepseek.com",
            AiProvider? provider = null, HttpClient httpClient = null, ChatApiOptions requestOptions = null)
        {
            _provider = provider ?? SettingsManager.Current.Provider;
            if (string.IsNullOrWhiteSpace(apiKey) && _provider != AiProvider.Ollama)
                throw new ArgumentNullException(nameof(apiKey));
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');
            _http = httpClient ?? SharedClient;
            _options = new ChatApiOptions { TokenLimit = requestOptions?.TokenLimit ?? TokenLimitParameter.Auto,
                Temperature = requestOptions?.Temperature ?? TemperatureParameter.Auto };
        }

        public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            request.Stream = false;
            using (HttpRequestMessage message = CreateRequest(request))
            using (HttpResponseMessage response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                CheckResponse(response, body);
                if (_provider != AiProvider.Claude)
                {
                    ChatResponse result = JsonConvert.DeserializeObject<ChatResponse>(body);
                    if (result?.Choices?.FirstOrDefault()?.Message == null) throw new InvalidDataException("AI response has no message.");
                    return result;
                }
                JObject parsed = JObject.Parse(body);
                var text = new StringBuilder();
                var calls = new List<ToolCall>();
                foreach (JObject block in parsed["content"] ?? new JArray())
                {
                    if ((string)block["type"] == "text") text.Append((string)block["text"]);
                    if ((string)block["type"] == "tool_use")
                        calls.Add(new ToolCall { Id = (string)block["id"], Function = new ToolArguments
                        { Name = (string)block["name"], Arguments = block["input"]?.ToString(Formatting.None) ?? "{}" } });
                }
                return new ChatResponse { Model = request.Model, Choices = new List<ChatChoice>
                {
                    new ChatChoice { FinishReason = (string)parsed["stop_reason"], Message = new ChatMessage
                    { Role = "assistant", Content = text.ToString(), ToolCalls = calls.Count == 0 ? null : calls } }
                } };
            }
        }

        public async Task StreamAsync(ChatRequest request, Action<string> onToken, Action<string> onComplete = null,
            CancellationToken cancellationToken = default)
        {
            ChatChoice result = await StreamChoiceAsync(request, onToken, cancellationToken).ConfigureAwait(false);
            if (result.Message.ToolCalls?.Count > 0)
                throw new InvalidOperationException("This action does not support tool calls. Use the Q&A assistant.");
            onComplete?.Invoke(result.Message.Content);
        }

        public async Task<ChatChoice> StreamChoiceAsync(ChatRequest request, Action<string> onToken,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.Stream = true;
            var text = new StringBuilder();
            var reasoning = new StringBuilder();
            var calls = new SortedDictionary<int, ToolCall>();
            string finish = null;
            bool messageStopped = false;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                using (HttpRequestMessage message = CreateRequest(request))
                {
                    message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    using (HttpResponseMessage response = await _http.SendAsync(message,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            CheckResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            await DeepseekStreamReader.ReadEventsAsync(stream, data =>
                            {
                                string token;
                                if (_provider == AiProvider.Claude)
                                {
                                    string type = (string)data["type"];
                                    int index = (int?)data["index"] ?? 0;
                                    token = type == "content_block_delta" ? (string)data["delta"]?["text"] : null;
                                    if (type == "content_block_start" && (string)data["content_block"]?["type"] == "tool_use")
                                        calls[index] = new ToolCall { Id = (string)data["content_block"]["id"],
                                            Function = new ToolArguments { Name = (string)data["content_block"]["name"], Arguments = "" } };
                                    if (type == "content_block_delta" && (string)data["delta"]?["type"] == "input_json_delta")
                                    {
                                        if (!calls.TryGetValue(index, out ToolCall call)) throw new InvalidDataException("Tool delta has no start.");
                                        call.Function.Arguments += (string)data["delta"]["partial_json"];
                                    }
                                    if (type == "message_delta") finish = (string)data["delta"]?["stop_reason"] ?? finish;
                                    if (type == "message_stop") messageStopped = true;
                                }
                                else
                                {
                                    JToken choice = data["choices"]?.FirstOrDefault();
                                    token = (string)choice?["delta"]?["content"] ?? (string)choice?["delta"]?["refusal"];
                                    reasoning.Append((string)choice?["delta"]?["reasoning_content"]);
                                    finish = (string)choice?["finish_reason"] ?? finish;
                                    foreach (JToken delta in choice?["delta"]?["tool_calls"] ?? new JArray())
                                    {
                                        int index = (int?)delta["index"] ?? throw new InvalidDataException("Missing tool index.");
                                        if (!calls.TryGetValue(index, out ToolCall call)) calls[index] = call = new ToolCall();
                                        call.Id = (string)delta["id"] ?? call.Id;
                                        call.Function.Name += (string)delta["function"]?["name"];
                                        call.Function.Arguments += (string)delta["function"]?["arguments"];
                                    }
                                }
                                if (!string.IsNullOrEmpty(token)) { text.Append(token); onToken?.Invoke(token); }
                                if (text.Length + reasoning.Length + calls.Values.Sum(c => c.Function.Arguments?.Length ?? 0) > 2 * 1024 * 1024)
                                    throw new InvalidDataException("AI response exceeds the 2 MiB limit.");
                            }, timeout.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (finish == null || (_provider == AiProvider.Claude && !messageStopped))
                throw new InvalidDataException("The AI stream ended before completion. No tools were executed.");
            if (finish == "length" || finish == "max_tokens")
                throw new InvalidDataException("The AI response was truncated. Increase the output budget; no tools were executed.");
            if (finish == "content_filter" || (text.Length == 0 && calls.Count == 0))
                throw new InvalidDataException("The model returned no usable answer or blocked the request.");
            foreach (ToolCall call in calls.Values)
            {
                if (string.IsNullOrEmpty(call.Id) || string.IsNullOrEmpty(call.Function.Name))
                    throw new InvalidDataException("Incomplete AI tool call.");
                if (string.IsNullOrEmpty(call.Function.Arguments)) call.Function.Arguments = "{}";
                JObject.Parse(call.Function.Arguments);
            }
            if (calls.Values.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != calls.Count)
                throw new InvalidDataException("Duplicate AI tool call IDs.");
            return new ChatChoice { FinishReason = finish, Message = new ChatMessage { Role = "assistant",
                Content = text.ToString(), ReasoningContent = reasoning.Length == 0 ? null : reasoning.ToString(),
                ToolCalls = calls.Count == 0 ? null : calls.Values.ToList() } };
        }

        private HttpRequestMessage CreateRequest(ChatRequest request)
        {
            bool claude = _provider == AiProvider.Claude;
            string url = claude ? _baseUrl + (_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "" : "/v1") + "/messages"
                : _baseUrl + (_provider == AiProvider.Gemini || _provider == AiProvider.Zhipu ||
                    _baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "" : "/v1") + "/chat/completions";
            var message = new HttpRequestMessage(HttpMethod.Post, url);
            if (claude)
            {
                message.Headers.Add("x-api-key", _apiKey);
                message.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (!string.IsNullOrWhiteSpace(_apiKey))
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            JObject payload = claude ? ClaudePayload(request) : JObject.FromObject(request);
            ApplyParameters(payload, request.Model);
            message.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            return message;
        }

        private void ApplyParameters(JObject payload, string model)
        {
            bool reasoning = System.Text.RegularExpressions.Regex.IsMatch(model ?? "",
                @"^(?:openai/)?(?:o[1-9](?:-|$)|gpt-[5-9](?:[.-]|$))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool completionTokens = _options.TokenLimit == TokenLimitParameter.MaxCompletionTokens ||
                (_options.TokenLimit == TokenLimitParameter.Auto && (_provider == AiProvider.OpenAI || reasoning));
            if (_provider != AiProvider.Claude && completionTokens)
            {
                payload["max_completion_tokens"] = payload["max_tokens"];
                payload.Remove("max_tokens");
            }
            if (_options.Temperature == TemperatureParameter.Omit ||
                (_options.Temperature == TemperatureParameter.Auto && _provider != AiProvider.Claude && reasoning))
                payload.Remove("temperature");
        }

        internal static JObject ClaudePayload(ChatRequest request)
        {
            var messages = new JArray();
            foreach (ChatMessage item in request.Messages.Where(m => m.Role != "system"))
            {
                var blocks = new JArray();
                string role = item.Role == "tool" ? "user" : item.Role;
                if (item.Role == "tool")
                    blocks.Add(new JObject { ["type"] = "tool_result", ["tool_use_id"] = item.ToolCallId, ["content"] = item.Content });
                else
                {
                    if (!string.IsNullOrEmpty(item.Content)) blocks.Add(new JObject { ["type"] = "text", ["text"] = item.Content });
                    foreach (ToolCall call in item.ToolCalls ?? new List<ToolCall>())
                        blocks.Add(new JObject { ["type"] = "tool_use", ["id"] = call.Id,
                            ["name"] = call.Function.Name, ["input"] = JObject.Parse(call.Function.Arguments) });
                }
                if (messages.Last is JObject previous && (string)previous["role"] == role)
                    foreach (JToken block in blocks) ((JArray)previous["content"]).Add(block);
                else messages.Add(new JObject { ["role"] = role, ["content"] = blocks });
            }
            var result = new JObject { ["model"] = request.Model, ["system"] = string.Join("\n", request.Messages.Where(m => m.Role == "system").Select(m => m.Content)),
                ["messages"] = messages, ["max_tokens"] = request.MaxTokens ?? 4096, ["temperature"] = request.Temperature, ["stream"] = request.Stream };
            if (request.Tools?.Count > 0) result["tools"] = new JArray(request.Tools.Select(t => new JObject
            { ["name"] = t.Function.Name, ["description"] = t.Function.Description, ["input_schema"] = t.Function.Parameters }));
            return result;
        }

        private static void CheckResponse(HttpResponseMessage response, string body)
        {
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("AI HTTP " + (int)response.StatusCode + ": " + body.Substring(0, Math.Min(body.Length, 2000)));
        }

        public static string SelectModel(string taskType, int estimatedTokens)
        {
            AppSettings settings = SettingsManager.Current;
            if (settings.Provider != AiProvider.DeepSeek) return settings.DefaultModel;
            string normalized = (taskType ?? "").Trim().ToLowerInvariant();
            return (normalized == "generate" || normalized == "qa") && estimatedTokens > 2000 ? "deepseek-reasoner" : "deepseek-chat";
        }

        public void Dispose() { }
    }
}
