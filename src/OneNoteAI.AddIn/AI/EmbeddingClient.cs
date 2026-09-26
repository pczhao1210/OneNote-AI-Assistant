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
using OneNoteAI.Settings;

namespace OneNoteAI.AI
{
    public sealed class EmbeddingClient
    {
        private static readonly HttpClient Shared = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private readonly EmbeddingOptions _options;
        private readonly HttpClient _http;

        public EmbeddingClient(EmbeddingOptions options, HttpClient http = null)
        {
            _options = JsonConvert.DeserializeObject<EmbeddingOptions>(JsonConvert.SerializeObject(options));
            _http = http ?? Shared;
        }

        public async Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken token)
        {
            _options.Validate(true);
            if (texts.Count == 0) return Array.Empty<float[]>();
            if (texts.Count > 32 || texts.Any(t => string.IsNullOrWhiteSpace(t) || Encoding.UTF8.GetByteCount(t) > 8191))
                throw new ArgumentException("Embedding batches allow 1-32 nonempty inputs, each within the conservative 8191-byte budget.");
            var payload = new JObject { ["model"] = _options.Model, ["input"] = new JArray(texts), ["encoding_format"] = "float" };
            if (_options.Dimensions.HasValue) payload["dimensions"] = _options.Dimensions.Value;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using (var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint.TrimEnd('/') + "/embeddings"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", EncryptionHelper.Decrypt(_options.ApiKey));
                    request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await _http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (attempt < 3 && (status == 429 || status >= 500))
                        {
                            TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1 << attempt);
                            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, Math.Min(60, delay.TotalSeconds))), token).ConfigureAwait(false);
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException("Embedding HTTP " + status + ". Check the endpoint, model, key and quota.");
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var result = new float[texts.Count][];
                        foreach (JToken item in JObject.Parse(body)["data"] ?? throw new InvalidDataException("Missing embedding data."))
                        {
                            int index = (int?)item["index"] ?? -1;
                            if (index < 0 || index >= result.Length || result[index] != null)
                                throw new InvalidDataException("Invalid or duplicate embedding response index.");
                            result[index] = Normalize(item["embedding"]?.ToObject<float[]>(), _options.VectorSize);
                        }
                        if (result.Any(v => v == null)) throw new InvalidDataException("Incomplete embedding response.");
                        return result;
                    }
                }
            }
        }

        public static float[] Normalize(float[] vector, int dimensions)
        {
            if (vector == null || vector.Length != dimensions || vector.Any(v => float.IsNaN(v) || float.IsInfinity(v)))
                throw new InvalidDataException("Embedding vector dimension or value is invalid.");
            double norm = Math.Sqrt(vector.Sum(v => (double)v * v));
            if (norm == 0) throw new InvalidDataException("Embedding vector is zero.");
            return vector.Select(v => (float)(v / norm)).ToArray();
        }
    }
}
