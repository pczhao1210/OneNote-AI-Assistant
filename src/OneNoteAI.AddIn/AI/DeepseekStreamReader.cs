using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OneNoteAI.AI
{
    public static class DeepseekStreamReader
    {
        public static Task ReadStreamAsync(Stream stream, Action<string> onToken, CancellationToken cancellationToken)
        {
            return ReadEventsAsync(stream, data =>
            {
                string text = (string)data["choices"]?[0]?["delta"]?["content"];
                if (!string.IsNullOrEmpty(text)) onToken(text);
            }, cancellationToken);
        }

        internal static async Task ReadEventsAsync(Stream stream, Action<JObject> onEvent, CancellationToken token)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetCanceled()))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var data = new StringBuilder();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    Task<string> read = reader.ReadLineAsync();
                    if (await Task.WhenAny(read, cancelled.Task).ConfigureAwait(false) != read)
                    {
                        // Observe a read that faults after disposal without delaying cancellation.
                        _ = read.ContinueWith(t => { var error = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        token.ThrowIfCancellationRequested();
                    }
                    string line = await read.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (line == null || line.Length == 0)
                    {
                        if (data.Length > 0)
                        {
                            string payload = data.ToString().TrimEnd('\n');
                            data.Clear();
                            if (payload == "[DONE]") return;
                            JObject parsed = JObject.Parse(payload);
                            if (parsed["error"] != null)
                                throw new InvalidOperationException("AI stream error: " + (string)parsed["error"]?["message"]);
                            onEvent(parsed);
                        }
                        if (line == null) return;
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        data.Append(line.Substring(5).TrimStart(' ')).Append('\n');
                        if (data.Length > 2 * 1024 * 1024)
                            throw new InvalidDataException("AI stream event exceeds the 2 MiB limit.");
                    }
                }
            }
        }
    }
}
