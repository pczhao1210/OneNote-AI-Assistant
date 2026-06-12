using System.Collections.Generic;
using Newtonsoft.Json;

namespace OneNoteAI.AI.Models
{
    public class ChatRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("messages")]
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        [JsonProperty("stream")]
        public bool Stream { get; set; } = true;

        [JsonProperty("temperature")]
        public double Temperature { get; set; } = 0.7;

        [JsonProperty("max_tokens")]
        public int? MaxTokens { get; set; }
    }

    public class ChatMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        public static ChatMessage System(string content)
        {
            return new ChatMessage { Role = "system", Content = content };
        }

        public static ChatMessage User(string content)
        {
            return new ChatMessage { Role = "user", Content = content };
        }

        public static ChatMessage Assistant(string content)
        {
            return new ChatMessage { Role = "assistant", Content = content };
        }
    }

    public class ChatResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("choices")]
        public List<ChatChoice> Choices { get; set; }

        [JsonProperty("usage")]
        public UsageInfo Usage { get; set; }
    }

    public class ChatChoice
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("message")]
        public ChatMessage Message { get; set; }

        [JsonProperty("delta")]
        public ChatMessage Delta { get; set; }

        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; }
    }

    public class UsageInfo
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }

    public class StreamChunk
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("choices")]
        public List<ChatChoice> Choices { get; set; }
    }
}
