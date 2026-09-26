using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OneNoteAI.Knowledge;

namespace OneNoteAI.Settings
{
    public sealed class KnowledgeOptions
    {
        public const int DefaultMaxRetrievedChunks = 16;
        public const int MaxRetrievedChunksLimit = 100;

        public EmbeddingOptions Embedding { get; set; } = new EmbeddingOptions();
        public List<string> AllowedRootIds { get; set; } = new List<string>();
        public List<McpServerOptions> Servers { get; set; } = new List<McpServerOptions>();
        public int ContextWindow { get; set; }
        public int MaxRetrievedChunks { get; set; } = DefaultMaxRetrievedChunks;
        public bool ModelSupportsTools { get; set; } = true;
        public bool AutomaticIndexing { get; set; }
        public bool EnableDiagramPreview { get; set; }

        public KnowledgeOptions Clone() => JsonConvert.DeserializeObject<KnowledgeOptions>(JsonConvert.SerializeObject(this));

        public void Validate()
        {
            Embedding.Validate(false);
            if (AllowedRootIds == null || Servers == null) throw new InvalidOperationException("Invalid knowledge settings.");
            if (ContextWindow != 0 && ContextWindow < 2048) throw new InvalidOperationException("Context window must be at least 2048.");
            ValidateMaxRetrievedChunks(MaxRetrievedChunks);
            if (Servers.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Servers.Count)
                throw new InvalidOperationException("Duplicate MCP server IDs.");
            foreach (McpServerOptions server in Servers) server.Validate();
        }

        internal static void ValidateMaxRetrievedChunks(int count)
        {
            if (count < 1 || count > MaxRetrievedChunksLimit)
                throw new InvalidOperationException("Retrieved passage count must be between 1 and " + MaxRetrievedChunksLimit + ".");
        }
    }

    public sealed class EmbeddingOptions
    {
        public string Endpoint { get; set; } = "https://api.openai.com/v1";
        public string Model { get; set; } = "text-embedding-3-small";
        public string ApiKey { get; set; }
        public int? Dimensions { get; set; }

        [JsonIgnore]
        public int VectorSize => Dimensions ?? (Model == "text-embedding-3-small" ? 1536 :
            Model == "text-embedding-3-large" ? 3072 : throw new InvalidOperationException("Set dimensions for a custom embedding model."));

        [JsonIgnore]
        public string Generation => LocalState.Hash(Endpoint.TrimEnd('/') + "\n" + Model + "\n" + VectorSize + "\nchunk-v1");

        public void Validate(bool requireKey)
        {
            RemoteEndpoint(Endpoint);
            if (string.IsNullOrWhiteSpace(Model) || VectorSize < 1 || VectorSize > 16384)
                throw new InvalidOperationException("Invalid embedding model or dimensions.");
            int maximum = Model == "text-embedding-3-small" ? 1536 : Model == "text-embedding-3-large" ? 3072 : 16384;
            if (VectorSize > maximum) throw new InvalidOperationException("Dimensions exceed the model's default size.");
            if (requireKey && string.IsNullOrWhiteSpace(EncryptionHelper.Decrypt(ApiKey)))
                throw new InvalidOperationException("Configure the independent Embedding API key first.");
        }

        internal static Uri RemoteEndpoint(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("A remote HTTPS endpoint without embedded credentials is required.");
            return uri;
        }
    }

    public enum McpAuthentication { None, Bearer, ApiKeyHeader, OAuth }

    public sealed class McpToolPolicy
    {
        public bool Enabled { get; set; } = true;
        public bool RequireApproval { get; set; }
    }

    public sealed class McpServerOptions
    {
        public override string ToString() => Name;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "MCP";
        public string Endpoint { get; set; }
        public bool Enabled { get; set; } = true;
        public McpAuthentication Authentication { get; set; }
        public string Secret { get; set; }
        public string HeaderName { get; set; } = "X-API-Key";
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string ClientMetadataUrl { get; set; }
        public string Scopes { get; set; }
        public int CallbackPort { get; set; } = 38741;
        public int TimeoutSeconds { get; set; } = 120;
        public Dictionary<string, McpToolPolicy> Tools { get; set; } = new Dictionary<string, McpToolPolicy>(StringComparer.Ordinal);

        public McpToolPolicy Policy(string name) =>
            Tools.TryGetValue(name, out McpToolPolicy policy) ? policy : new McpToolPolicy();

        [JsonIgnore]
        public string Identity => LocalState.Hash(Id + "\n" + Endpoint + "\n" + Authentication + "\n" + EncryptionHelper.Decrypt(Secret) +
            "\n" + HeaderName + "\n" + ClientId + "\n" + EncryptionHelper.Decrypt(ClientSecret) + "\n" + ClientMetadataUrl + "\n" + Scopes + "\n" + CallbackPort);

        public void Validate()
        {
            EmbeddingOptions.RemoteEndpoint(Endpoint);
            if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Tools == null)
                throw new InvalidOperationException("MCP server ID, name and tool policies are required.");
            if (TimeoutSeconds < 5 || TimeoutSeconds > 1800 || CallbackPort < 1024 || CallbackPort > 65535)
                throw new InvalidOperationException("Invalid MCP timeout or OAuth callback port.");
            if (!string.IsNullOrWhiteSpace(ClientMetadataUrl)) EmbeddingOptions.RemoteEndpoint(ClientMetadataUrl);
            if (Authentication == McpAuthentication.ApiKeyHeader &&
                (!System.Text.RegularExpressions.Regex.IsMatch(HeaderName ?? "", "^[A-Za-z0-9-]+$") ||
                 new[] { "host", "content-length", "connection", "accept", "content-type" }.Contains(HeaderName.ToLowerInvariant())))
                throw new InvalidOperationException("Invalid API key header name.");
        }
    }
}
