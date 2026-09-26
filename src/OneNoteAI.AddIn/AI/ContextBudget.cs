using System;
using System.Text;
using Newtonsoft.Json;
using OneNoteAI.AI.Models;

namespace OneNoteAI.AI
{
    public static class ContextBudget
    {
        // A UTF-8 byte bound deliberately overestimates byte-tokenized models,
        // including whitespace, tool schemas, arguments and protocol overhead.
        public static int Measure(ChatRequest request) =>
            Math.Max(Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(request)),
                Encoding.UTF8.GetByteCount(DeepseekClient.ClaudePayload(request).ToString(Formatting.None))) + 512;

        public static bool Fits(ChatRequest request, int contextWindow) =>
            (long)Measure(request) + (request.MaxTokens ?? 4096) <= contextWindow;

        public static void Validate(ChatRequest request, int contextWindow)
        {
            if (!Fits(request, contextWindow))
                throw new InvalidOperationException("The request exceeds the context budget. Narrow the scope, reduce tools or shorten the question.");
        }
    }
}
