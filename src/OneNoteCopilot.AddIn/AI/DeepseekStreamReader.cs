using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OneNoteCopilot.AI.Models;

namespace OneNoteCopilot.AI
{
    public class DeepseekStreamReader
    {
        public static async Task ReadStreamAsync(Stream stream, Action<string> onToken, CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (onToken == null)
            {
                throw new ArgumentNullException(nameof(onToken));
            }

            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string data = line.Substring(6);
                    if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                    {
                        break;
                    }

                    try
                    {
                        StreamChunk chunk = JsonConvert.DeserializeObject<StreamChunk>(data);
                        string content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                        if (!string.IsNullOrEmpty(content))
                        {
                            onToken(content);
                        }
                    }
                    catch (JsonException)
                    {
                        Debug.WriteLine(string.Format("DeepseekStreamReader: Failed to parse chunk: {0}", data));
                    }
                }
            }
        }
    }
}
