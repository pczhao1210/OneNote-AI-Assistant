using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OneNoteAI.AI.Models;
using OneNoteAI.Settings;

namespace OneNoteAI.AI
{
    public class DeepseekClient : IDisposable
    {

        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly string _apiKey;
        private readonly string _baseUrl;

        public DeepseekClient(string apiKey, string baseUrl = "https://api.deepseek.com")
        {
            if (string.IsNullOrWhiteSpace(apiKey) && SettingsManager.Current.Provider != AiProvider.Ollama)
            {
                throw new ArgumentNullException(nameof(apiKey));
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentNullException(nameof(baseUrl));
            }

            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            request.Stream = false;
            if (IsClaude) return await SendClaudeAsync(request, cancellationToken).ConfigureAwait(false);

            using (HttpRequestMessage message = CreateRequestMessage(request))
            {
                using (HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(string.Format(
                            "Deepseek API request failed with status code {0}: {1}",
                            (int)response.StatusCode,
                            responseContent));
                    }

                    ChatResponse chatResponse = JsonConvert.DeserializeObject<ChatResponse>(responseContent);
                    if (chatResponse == null)
                    {
                        throw new InvalidOperationException("Deepseek API returned an empty response.");
                    }

                    return chatResponse;
                }
            }
        }

        public async Task StreamAsync(
            ChatRequest request,
            Action<string> onToken,
            Action<string> onComplete = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (onToken == null)
            {
                throw new ArgumentNullException(nameof(onToken));
            }

            request.Stream = true;
            if (IsClaude)
            {
                await StreamClaudeAsync(request, onToken, onComplete, cancellationToken).ConfigureAwait(false);
                return;
            }

            StringBuilder fullText = new StringBuilder();

            using (HttpRequestMessage message = CreateRequestMessage(request))
            {
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                using (HttpResponseMessage response = await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException(string.Format(
                            "Deepseek streaming request failed with status code {0}: {1}",
                            (int)response.StatusCode,
                            errorContent));
                    }

                    using (System.IO.Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        await DeepseekStreamReader.ReadStreamAsync(
                            stream,
                            delegate(string token)
                            {
                                fullText.Append(token);
                                onToken(token);
                            },
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (onComplete != null)
            {
                onComplete(fullText.ToString());
            }
        }

        public static string SelectModel(string taskType, int estimatedTokens)
        {
            AppSettings settings = SettingsManager.Current;
            if (settings.Provider != AiProvider.DeepSeek)
            {
                return settings.DefaultModel;
            }
            string normalizedTaskType = (taskType ?? string.Empty).Trim().ToLowerInvariant();

            switch (normalizedTaskType)
            {
                case "generate":
                case "qa":
                    return estimatedTokens > 2000 ? "deepseek-reasoner" : "deepseek-chat";
                default:
                    return "deepseek-chat";
            }
        }

        public void Dispose()
        {
        }

        private HttpRequestMessage CreateRequestMessage(ChatRequest request)
        {
            string json = JsonConvert.SerializeObject(request);
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl());

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return message;
        }

        private string BuildChatCompletionsUrl()
        {
            AiProvider provider = SettingsManager.Current.Provider;
            if (provider == AiProvider.Gemini || provider == AiProvider.Zhipu || _baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return _baseUrl + "/chat/completions";
            }
            return _baseUrl + "/v1/chat/completions";
        }

        private bool IsClaude { get { return SettingsManager.Current.Provider == AiProvider.Claude; } }

        private async Task<ChatResponse> SendClaudeAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage message = CreateClaudeRequest(request))
            using (HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("Claude API request failed: " + (int)response.StatusCode + ": " + body);
                dynamic parsed = JsonConvert.DeserializeObject(body);
                string text = parsed?.content?[0]?.text;
                return new ChatResponse { Model = request.Model, Choices = new System.Collections.Generic.List<ChatChoice> { new ChatChoice { Message = ChatMessage.Assistant(text ?? string.Empty) } } };
            }
        }

        private async Task StreamClaudeAsync(ChatRequest request, Action<string> onToken, Action<string> onComplete, CancellationToken cancellationToken)
        {
            StringBuilder fullText = new StringBuilder();
            using (HttpRequestMessage message = CreateClaudeRequest(request))
            {
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using (HttpResponseMessage response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException("Claude streaming request failed: " + (int)response.StatusCode + ": " + body);
                    }
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                            try
                            {
                                dynamic evt = JsonConvert.DeserializeObject(line.Substring(6));
                                string type = evt?.type;
                                string text = type == "content_block_delta" ? (string)evt?.delta?.text : null;
                                if (!string.IsNullOrEmpty(text)) { fullText.Append(text); onToken(text); }
                            }
                            catch (JsonException) { }
                        }
                    }
                }
            }
            if (onComplete != null) onComplete(fullText.ToString());
        }

        private HttpRequestMessage CreateClaudeRequest(ChatRequest request)
        {
            string system = null;
            var messages = new System.Collections.Generic.List<object>();
            foreach (ChatMessage item in request.Messages)
            {
                if (item.Role == "system") system = item.Content;
                else messages.Add(new { role = item.Role, content = item.Content });
            }
            object payload = new { model = request.Model, system = system, messages = messages, max_tokens = request.MaxTokens ?? 4096, temperature = request.Temperature, stream = request.Stream };
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/messages");
            message.Headers.Add("x-api-key", _apiKey);
            message.Headers.Add("anthropic-version", "2023-06-01");
            message.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            return message;
        }
    }
}
