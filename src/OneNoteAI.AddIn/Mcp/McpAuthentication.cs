using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using ModelContextProtocol.Authentication;
using Newtonsoft.Json;
using OneNoteAI.Knowledge;
using OneNoteAI.Settings;

namespace OneNoteAI.Mcp
{
    internal sealed class HttpsOnlyHandler : DelegatingHandler
    {
        public HttpsOnlyHandler(HttpMessageHandler inner) : base(inner) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            EmbeddingOptions.RemoteEndpoint(request.RequestUri.AbsoluteUri);
            return base.SendAsync(request, cancellationToken);
        }
    }

    public sealed class McpTokenCache : ITokenCache
    {
        private readonly string _path;
        private readonly string _identity;
        private readonly long _revision;
        private readonly object _gate;
        private static readonly ConcurrentDictionary<string, long> Revisions = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, object> Gates = new ConcurrentDictionary<string, object>();
        public static long Revision(string identity) => Revisions.TryGetValue(identity, out long value) ? value : 0;
        public McpTokenCache(string identity, string directory = null)
        {
            _identity = identity;
            _gate = Gates.GetOrAdd(identity, _ => new object());
            _revision = Revision(identity);
            string root = directory ?? LocalState.DirectoryPath;
            LocalState.EnsureDirectory(root);
            _path = Path.Combine(root, "oauth-" + LocalState.Hash(identity) + ".json");
        }

        public ValueTask<TokenContainer> GetTokensAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
                return new ValueTask<TokenContainer>(_revision == Revision(_identity) && File.Exists(_path)
                    ? JsonConvert.DeserializeObject<TokenContainer>(EncryptionHelper.Decrypt(File.ReadAllText(_path))) : null);
        }

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_revision != Revision(_identity)) throw new InvalidOperationException("Signed out during authorization. Reconnect to sign in again.");
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, EncryptionHelper.Encrypt(JsonConvert.SerializeObject(tokens)));
                LocalState.Replace(temporary, _path);
            }
            return default;
        }

        public void Clear()
        {
            lock (_gate)
            {
                Revisions.AddOrUpdate(_identity, 1, (_, revision) => revision + 1);
                if (File.Exists(_path)) File.Delete(_path);
            }
        }
    }

    internal static class McpOAuth
    {
        public static ClientOAuthOptions Options(McpServerOptions server) => new ClientOAuthOptions
        {
            RedirectUri = new Uri("http://127.0.0.1:" + server.CallbackPort + "/callback/"),
            ClientId = string.IsNullOrWhiteSpace(server.ClientId) ? null : server.ClientId,
            ClientSecret = string.IsNullOrEmpty(server.ClientSecret) ? null : EncryptionHelper.Decrypt(server.ClientSecret),
            ClientMetadataDocumentUri = string.IsNullOrWhiteSpace(server.ClientMetadataUrl) ? null : EmbeddingOptions.RemoteEndpoint(server.ClientMetadataUrl),
            Scopes = (server.Scopes ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries),
            TokenCache = new McpTokenCache(server.Identity),
            AuthServerSelector = servers =>
            {
                foreach (Uri uri in servers) if (uri.Scheme == Uri.UriSchemeHttps) return uri;
                throw new InvalidOperationException("No HTTPS OAuth authorization server was advertised.");
            },
            AuthorizationCallbackHandler = AuthorizeAsync
        };

        private static async Task<AuthorizationResult> AuthorizeAsync(AuthorizationCallbackContext context, CancellationToken token)
        {
            EmbeddingOptions.RemoteEndpoint(context.AuthorizationUri.AbsoluteUri);
            Uri redirect = context.RedirectUri;
            if (redirect.Scheme != "http" || redirect.Host != "127.0.0.1" || redirect.AbsolutePath != "/callback/")
                throw new InvalidOperationException("Invalid OAuth loopback callback.");
            string expectedState = HttpUtility.ParseQueryString(context.AuthorizationUri.Query)["state"];
            if (string.IsNullOrEmpty(expectedState)) throw new InvalidOperationException("OAuth state is missing.");
            var listener = new TcpListener(IPAddress.Loopback, redirect.Port);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(3));
                listener.Start(1);
                using (deadline.Token.Register(listener.Stop))
                try
                {
                    Process.Start(new ProcessStartInfo(context.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
                    while (true)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        using (TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                        using (deadline.Token.Register(client.Close))
                        using (NetworkStream stream = client.GetStream())
                        {
                            string[] parts = (await ReadCallbackRequestAsync(stream, deadline.Token).ConfigureAwait(false)).Split(' ');
                            if (parts.Length != 3 || parts[0] != "GET" || !Uri.TryCreate(redirect, parts[1], out Uri callback) ||
                                callback.Authority != redirect.Authority || callback.AbsolutePath != redirect.AbsolutePath)
                            {
                                await ReplyAsync(stream, 400, "Invalid OAuth callback.", deadline.Token).ConfigureAwait(false);
                                continue;
                            }
                            var values = HttpUtility.ParseQueryString(callback.Query);
                            if (!string.Equals(values["state"], expectedState, StringComparison.Ordinal))
                            {
                                await ReplyAsync(stream, 400, "OAuth state did not match.", deadline.Token).ConfigureAwait(false);
                                continue;
                            }
                            if (!string.IsNullOrEmpty(values["error"]))
                            {
                                await ReplyAsync(stream, 400, "Authorization was declined.", deadline.Token).ConfigureAwait(false);
                                throw new InvalidOperationException("OAuth authorization was declined.");
                            }
                            await ReplyAsync(stream, 200, "Authorization received. You can close this window and return to OneNote.", deadline.Token).ConfigureAwait(false);
                            return new AuthorizationResult { Code = values["code"], State = values["state"], Iss = values["iss"] };
                        }
                    }
                }
                catch (Exception ex) when (deadline.IsCancellationRequested && (ex is SocketException || ex is IOException || ex is ObjectDisposedException))
                {
                    throw new OperationCanceledException("OAuth authorization canceled or timed out.", ex, deadline.Token);
                }
                finally { listener.Stop(); }
            }
        }

        internal static async Task<string> ReadCallbackRequestAsync(Stream stream, CancellationToken token)
        {
            var buffer = new byte[8192];
            int length = 0;
            while (length < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer, length, buffer.Length - length, token).ConfigureAwait(false);
                if (count == 0) throw new InvalidDataException("Incomplete OAuth callback request.");
                length += count;
                string headers = Encoding.ASCII.GetString(buffer, 0, length);
                if (headers.Contains("\r\n\r\n")) return headers.Substring(0, headers.IndexOf("\r\n", StringComparison.Ordinal));
            }
            throw new InvalidDataException("OAuth callback headers are too large.");
        }

        private static Task ReplyAsync(NetworkStream stream, int status, string text, CancellationToken token)
        {
            byte[] content = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + (status == 200 ? " OK" : " Bad Request") +
                "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " +
                Encoding.UTF8.GetByteCount(text) + "\r\n\r\n" + text);
            return stream.WriteAsync(content, 0, content.Length, token);
        }
    }
}
