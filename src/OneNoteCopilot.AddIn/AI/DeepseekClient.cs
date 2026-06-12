using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OneNoteCopilot.AI.Models;

namespace OneNoteCopilot.AI
{
    public class DeepseekClient : IDisposable
    {
        private const string ChatCompletionsPath = "/v1/chat/completions";

        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly string _apiKey;
        private readonly string _baseUrl;

        public DeepseekClient(string apiKey, string baseUrl = "https://api.deepseek.com")
        {
            if (string.IsNullOrWhiteSpace(apiKey))
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
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, _baseUrl + ChatCompletionsPath);

            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return message;
        }
    }
}
