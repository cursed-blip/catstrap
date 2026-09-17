using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Web;

using Bloxstrap.Models.Entities;

namespace Bloxstrap.Integrations.AssetProxy
{
    public class AssetProxyServer : IDisposable
    {
        private const string LOG_IDENT = "AssetProxyServer";

        private static readonly HashSet<string> MitmHosts = new(AssetProxyManager.InterceptHosts, StringComparer.OrdinalIgnoreCase);

        private readonly X509Certificate2 _caCertificate;
        private readonly ConcurrentDictionary<string, X509Certificate2> _serverCertificates = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly string _cacheDirectory;

        private readonly ConcurrentDictionary<string, DateTime> _recentFailures = new();

        private long _upstreamErrors;

        public long UpstreamErrors => Interlocked.Read(ref _upstreamErrors);

        private TcpListener? _listener;
        private Task? _acceptTask;

        private TcpListener? _originListener;
        private Task? _originAcceptTask;
        private X509Certificate2? _originCertificate;

        public int Port { get; private set; } = 0;

        public bool IsRunning => _listener is not null && _acceptTask is not null && !_acceptTask.IsCompleted;

        public bool IsOriginRunning => _originListener is not null && _originAcceptTask is not null && !_originAcceptTask.IsCompleted;

        public long RequestCount { get; private set; }
        public long CachedAssets { get; private set; }

        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        public DateTime LastClientRequestUtc { get; private set; } = DateTime.UtcNow;

        public DateTime LastControlRequestUtc { get; private set; } = DateTime.UtcNow;

        public event Action? StopRequested;

        public event Action? ReloadRequested;

        public Func<bool>? IsClientRunning { get; set; }

        private const string ControlPrefix = "/__catstrap/";

        public AssetProxyServer(X509Certificate2 caCertificate, string cacheDirectory)
        {
            _caCertificate = caCertificate;
            _cacheDirectory = cacheDirectory;
        }

        public void Start(int preferredPort = 58443, bool strict = false)
        {
            int lastPort = strict ? preferredPort : preferredPort + 100;

            for (int port = preferredPort; port <= lastPort; port++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    _listener = listener;
                    Port = port;
                    break;
                }
                catch (SocketException)
                {
                }
            }

            if (_listener is null)
                throw new InvalidOperationException(strict
                    ? $"Port {preferredPort} is already in use"
                    : "Could not find a free port for the asset proxy");

            App.Logger.WriteLine(LOG_IDENT, $"Listening on 127.0.0.1:{Port}");

            _acceptTask = Task.Run(AcceptLoopAsync);
        }

        public void StartOrigin(int port = AssetProxyManager.OriginPort)
        {
            _originCertificate = CreateMultiHostCertificate(_caCertificate, AssetProxyManager.InterceptHosts);

            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            _originListener = listener;
            InterceptionPort = port;
            _originAcceptTask = Task.Run(OriginAcceptLoopAsync);

            App.Logger.WriteLine(LOG_IDENT, $"Intercepting on 127.0.0.1:{port} for {string.Join(", ", AssetProxyManager.InterceptHosts)}");
        }

        public int InterceptionPort { get; private set; }

        public static SslProtocols LocalTlsProtocols { get; private set; } = SslProtocols.Tls12;

        private static SslServerAuthenticationOptions CreateLocalTlsOptions(X509Certificate2 certificate) => new()
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = LocalTlsProtocols,
            ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11 }
        };

        public async Task<(bool Ok, string? Detail)> SelfTestAsync(string host, CancellationToken token = default)
        {
            if (_originCertificate is null || !IsOriginRunning)
                return (false, "the interception listener is not running");

            string? capped = await TryHandshakeAsync(host, LocalTlsProtocols, token);

            if (capped is null)
                return (true, null);

            string? relaxed = await TryHandshakeAsync(host, SslProtocols.Tls12 | SslProtocols.Tls13, token);

            if (relaxed is null)
            {
                LocalTlsProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                App.Logger.WriteLine(LOG_IDENT, $"The TLS 1.2 handshake failed ({capped}); allowing 1.3 as well");
                return (true, $"relaxed the TLS ceiling after: {capped}");
            }

            return (false, capped);
        }

        private async Task<string?> TryHandshakeAsync(string host, SslProtocols protocols, CancellationToken token)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, InterceptionPort, token);

                using var ssl = new SslStream(client.GetStream(), false, (_, _, _, _) => true);

                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = protocols,
                    ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11 }
                }, token);

                if (ssl.NegotiatedApplicationProtocol != SslApplicationProtocol.Http11)
                    return "the client and the proxy did not agree on http/1.1";

                if (ssl.RemoteCertificate is not null && _caCertificate is not null)
                {
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.CustomTrustStore.Add(_caCertificate);

                    using var presented = new X509Certificate2(ssl.RemoteCertificate);

                    if (!chain.Build(presented))
                    {
                        string status = string.Join("; ", chain.ChainStatus.Select(entry => entry.StatusInformation.Trim()));
                        return $"the certificate we present does not chain to our own CA ({status})";
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return ex.GetBaseException().Message;
            }
        }

        public void Stop()
        {
            try
            {
                _cts.Cancel();
                _listener?.Stop();
                _originListener?.Stop();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Failed to stop cleanly: {ex.Message}");
            }

            _listener = null;
            _acceptTask = null;
            _originListener = null;
            _originAcceptTask = null;
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }

        #region Static certificate helpers

        public static X509Certificate2 CreateMultiHostCertificate(X509Certificate2 caCertificate, IEnumerable<string> hosts)
        {
            var names = hosts.ToList();

            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest($"CN={names[0]}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

            var sanBuilder = new SubjectAlternativeNameBuilder();

            foreach (string name in names)
                sanBuilder.AddDnsName(name);

            request.CertificateExtensions.Add(sanBuilder.Build());

            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);

            X509Certificate2 certificate = request
                .Create(caCertificate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), serial)
                .CopyWithPrivateKey(rsa);

            return WithPersistedKey(certificate);
        }

        public static X509Certificate2 CreateCaCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Catstrap Local CA, O=Catstrap", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        }

        #endregion

        #region Connection handling

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener!.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Accept error: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    stream.ReadTimeout = 60000;
                    stream.WriteTimeout = 60000;

                    string requestLine = await ReadLineAsync(stream);

                    if (String.IsNullOrEmpty(requestLine))
                        return;

                    if (requestLine.StartsWith($"GET {ControlPrefix}", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleControlRequestAsync(stream, requestLine);
                        return;
                    }

                    LastClientRequestUtc = DateTime.UtcNow;

                    if (requestLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
                        await HandleConnectAsync(client, stream, requestLine);
                    else
                        await HandleDirectHttpAsync(client, stream, requestLine);
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Connection error: {ex.GetBaseException().Message}");
            }
        }

        private async Task OriginAcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _originListener!.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    NoteFailure($"Accept error on the interception port: {ex.Message}", countAsError: false);
                    continue;
                }

                _ = Task.Run(() => HandleOriginClientAsync(client));
            }
        }

        private async Task HandleOriginClientAsync(TcpClient client)
        {
            string? host = null;

            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    stream.ReadTimeout = 60000;
                    stream.WriteTimeout = 60000;

                    var sslStream = new SslStream(stream, false);

                    await sslStream.AuthenticateAsServerAsync(CreateLocalTlsOptions(_originCertificate!));

                    string defaultHost = AssetProxyManager.InterceptHosts[0];

                    for (int i = 0; i < 256 && !_cts.IsCancellationRequested; i++)
                    {
                        string requestLine = await ReadLineAsync(sslStream);

                        if (String.IsNullOrEmpty(requestLine))
                            return;

                        var headers = await ReadHeadersAsync(sslStream);
                        var body = await ReadBodyAsync(sslStream, headers);

                        host = headers.TryGetValue("Host", out string? hostHeader) && !String.IsNullOrWhiteSpace(hostHeader)
                            ? hostHeader.Split(':')[0].Trim()
                            : defaultHost;

                        LastClientRequestUtc = DateTime.UtcNow;

                        bool keepAlive = ShouldKeepAlive(requestLine, headers);
                        bool closeConnection = await HandleMitmRequestAsync(sslStream, requestLine, headers, body, host, keepAlive);

                        if (!keepAlive || closeConnection)
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                string message = ex.GetBaseException().Message;

                bool cancelled = message.Contains("aborted", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("closed by the remote", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase);

                NoteFailure($"Connection error for {host ?? "an intercepted host"}: {message}", countAsError: !cancelled);
            }
        }

        private async Task HandleConnectAsync(TcpClient client, NetworkStream stream, string requestLine)
        {
            var parts = requestLine.Split(' ');
            if (parts.Length < 2)
                return;

            string authority = parts[1];
            int colon = authority.LastIndexOf(':');
            string host = colon > 0 ? authority[..colon] : authority;
            int port = colon > 0 && int.TryParse(authority[(colon + 1)..], out int parsedPort) ? parsedPort : 443;

            await ReadHeadersAsync(stream);

            bool isMitmHost = IsMitmHost(host);

            if (!isMitmHost)
            {
                await WriteAsync(stream, "HTTP/1.1 200 Connection established\r\n\r\n");

                using var upstream = new TcpClient();
                await upstream.ConnectAsync(host, port);
                await RelayAsync(stream, upstream.GetStream());
                return;
            }

            await WriteAsync(stream, "HTTP/1.1 200 Connection established\r\n\r\n");

            var serverCertificate = GetOrCreateServerCertificate(host);

            using var sslStream = new SslStream(stream, false);
            await sslStream.AuthenticateAsServerAsync(CreateLocalTlsOptions(serverCertificate));

            for (int i = 0; i < 64 && !_cts.IsCancellationRequested; i++)
            {
                string tlsRequestLine = await ReadLineAsync(sslStream);

                if (String.IsNullOrEmpty(tlsRequestLine))
                    return;

                var headers = await ReadHeadersAsync(sslStream);
                var body = await ReadBodyAsync(sslStream, headers);

                bool keepAlive = ShouldKeepAlive(tlsRequestLine, headers);
                bool closeConnection = await HandleMitmRequestAsync(sslStream, tlsRequestLine, headers, body, host, keepAlive);

                if (!keepAlive || closeConnection)
                    return;
            }
        }

        private async Task HandleControlRequestAsync(NetworkStream stream, string requestLine)
        {
            LastControlRequestUtc = DateTime.UtcNow;

            string[] parts = requestLine.Split(' ');
            string path = parts.Length > 1 ? parts[1] : "";

            await ReadHeadersAsync(stream);

            if (path.StartsWith($"{ControlPrefix}stop", StringComparison.OrdinalIgnoreCase))
            {
                App.Logger.WriteLine(LOG_IDENT, "Stop asked for by another Catstrap process");
                await SendJsonAsync(stream, parts.Length > 2 ? parts[2] : "HTTP/1.1", "{\"stopping\":true}");
                StopRequested?.Invoke();
                return;
            }

            if (path.StartsWith($"{ControlPrefix}reload", StringComparison.OrdinalIgnoreCase))
            {
                App.Logger.WriteLine(LOG_IDENT, "Reloading asset rules on request");
                ReloadRequested?.Invoke();
                await SendJsonAsync(stream, parts.Length > 2 ? parts[2] : "HTTP/1.1", $"{{\"rules\":{AssetProxyManager.RuleCount}}}");
                return;
            }

            var status = new AssetProxyStatus
            {
                Port = Port,
                Requests = RequestCount,
                Cached = CachedAssets,
                Rules = AssetProxyManager.RuleCount,
                Errors = UpstreamErrors,
                ClientIdleSeconds = (long)(DateTime.UtcNow - LastClientRequestUtc).TotalSeconds,
                RobloxRunning = IsClientRunning?.Invoke() ?? false,
                Started = StartedAtUtc.ToString("O")
            };

            await SendJsonAsync(stream, parts.Length > 2 ? parts[2] : "HTTP/1.1", JsonSerializer.Serialize(status));
        }

        private static Task SendJsonAsync(Stream stream, string version, string json)
            => SendSimpleResponseAsync(stream, version, 200, "OK", "application/json", Encoding.UTF8.GetBytes(json));

        private async Task HandleDirectHttpAsync(TcpClient client, NetworkStream stream, string requestLine)
        {
            var headers = await ReadHeadersAsync(stream);
            var body = await ReadBodyAsync(stream, headers);

            var parts = requestLine.Split(' ');
            if (parts.Length < 3)
                return;

            string target = parts[1];
            string host;

            if (Uri.TryCreate(target, UriKind.Absolute, out Uri? absUri))
                host = absUri.Host;
            else
                return;

            string version = parts[2];
            bool keepAlive = ShouldKeepAlive(requestLine, headers);

            bool closeConnection = await HandleMitmRequestAsync(stream, requestLine, headers, body, host, keepAlive);

            if (!keepAlive || closeConnection)
                return;

            for (int i = 0; i < 64 && !_cts.IsCancellationRequested; i++)
            {
                string nextLine = await ReadLineAsync(stream);
                if (String.IsNullOrEmpty(nextLine))
                    return;

                var nextHeaders = await ReadHeadersAsync(stream);
                var nextBody = await ReadBodyAsync(stream, nextHeaders);
                bool nextKeepAlive = ShouldKeepAlive(nextLine, nextHeaders);

                bool closeNext = await HandleMitmRequestAsync(stream, nextLine, nextHeaders, nextBody, host, nextKeepAlive);

                if (!nextKeepAlive || closeNext)
                    return;
            }
        }

        #endregion

        #region MITM request handling

        private async Task<bool> HandleMitmRequestAsync(Stream clientStream, string requestLine, Dictionary<string, string> headers, byte[] body, string host, bool clientKeepAlive)
        {
            RequestCount++;

            var parts = requestLine.Split(' ');
            if (parts.Length < 3)
                return true;

            string method = parts[0].ToUpperInvariant();
            string target = parts[1];
            string version = parts[2];

            if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
                uri = new Uri($"https://{host}{target}");

            string assetPath = uri.AbsolutePath;
            var query = HttpUtility.ParseQueryString(uri.Query);

            long assetId = 0;
            bool isSingleAssetRequest = IsAssetPath(assetPath) && long.TryParse(query["id"], out assetId);
            bool isBatchRequest = IsBatchPath(assetPath);

            if (isSingleAssetRequest)
            {
                var rule = FindMatchingRule(assetId);

                if (rule is not null)
                {
                    switch (rule.Action)
                    {
                        case AssetProxyAction.Remove:
                            App.Logger.WriteLine(LOG_IDENT, $"Removed asset {assetId} (rule '{rule.Name}')");
                            await SendSimpleResponseAsync(clientStream, version, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("Asset removed by Catstrap"));
                            return true;

                        case AssetProxyAction.Redirect:
                            if (rule.RedirectTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || rule.RedirectTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                App.Logger.WriteLine(LOG_IDENT, $"Redirected asset {assetId} to {rule.RedirectTarget} (rule '{rule.Name}')");
                                await SendSimpleResponseAsync(clientStream, version, 302, "Found", "text/plain", Array.Empty<byte>(), $"Location: {rule.RedirectTarget}");
                                return true;
                            }
                            else if (File.Exists(rule.RedirectTarget))
                            {
                                App.Logger.WriteLine(LOG_IDENT, $"Serving asset {assetId} from local file {rule.RedirectTarget} (rule '{rule.Name}')");
                                byte[] fileBytes = await File.ReadAllBytesAsync(rule.RedirectTarget);
                                await SendSimpleResponseAsync(clientStream, version, 200, "OK", GetMimeType(rule.RedirectTarget), fileBytes);
                                return true;
                            }
                            break;

                        case AssetProxyAction.Replace:
                            if (rule.ToAssetId > 0)
                            {
                                App.Logger.WriteLine(LOG_IDENT, $"Replaced asset {assetId} with {rule.ToAssetId} (rule '{rule.Name}')");
                                query.Set("id", rule.ToAssetId.ToString());
                                target = $"{assetPath}?{query}";
                            }
                            break;
                    }
                }
            }
            else if (isBatchRequest && method == "POST" && body.Length > 0)
            {
                (body, headers) = RewriteBatchRequest(body, headers);
            }

            bool useTls = uri.Scheme == "https";
            bool cacheable = (isSingleAssetRequest || isBatchRequest) && useTls;

            return await ForwardRequestAsync(clientStream, host, method, target, headers, body, version, isSingleAssetRequest ? assetId : null, uri, useTls, cacheable, clientKeepAlive);
        }

        private sealed class UpstreamConnection : IDisposable
        {
            public Stream Stream { get; }

            public string? Address { get; }

            public UpstreamConnection(Stream stream, string? address)
            {
                Stream = stream;
                Address = address;
            }

            public void Dispose() => Stream.Dispose();
        }

        private static async Task<UpstreamConnection> OpenUpstreamAsync(string host, int port, bool useTls, CancellationToken token)
        {
            if (!AssetProxyManager.IsInterceptedHost(host))
            {
                var direct = new TcpClient();
                await direct.ConnectAsync(host, port, token);

                return new UpstreamConnection(await WrapUpstreamAsync(direct.GetStream(), host, useTls), null);
            }

            var addresses = UpstreamEndpoints.Get(host);

            if (addresses.Count == 0)
                throw new IOException($"nowhere to reach {host}: the name points at us and no address is known");

            var failures = new List<string>();

            foreach (string address in addresses)
            {
                var client = new TcpClient();

                try
                {
                    await client.ConnectAsync(IPAddress.Parse(address), port, token);

                    Stream stream = await WrapUpstreamAsync(client.GetStream(), host, useTls);

                    UpstreamEndpoints.ReportSuccess(host, address);

                    return new UpstreamConnection(stream, address);
                }
                catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException or FormatException)
                {
                    client.Dispose();
                    failures.Add($"{address} ({ex.GetBaseException().Message})");
                    UpstreamEndpoints.ReportFailure(host, address);
                }
            }

            throw new IOException($"no address for {host} produced a usable connection: {string.Join(", ", failures)}");
        }

        private static async Task<Stream> WrapUpstreamAsync(Stream stream, string host, bool useTls)
        {
            if (!useTls)
                return stream;

            var ssl = new SslStream(stream, false);

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11 }
            });

            return ssl;
        }

        private async Task<bool> ForwardRequestAsync(Stream clientStream, string host, string method, string target, Dictionary<string, string> headers, byte[] body, string version, long? assetId, Uri uri, bool useTls, bool cacheable, bool clientKeepAlive)
        {
            bool responseStarted = false;
            string? upstreamAddress = null;

            try
            {
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                using var upstream = await OpenUpstreamAsync(host, useTls ? 443 : 80, useTls, connectTimeout.Token);
                upstreamAddress = upstream.Address;

                Stream upstreamStream = upstream.Stream;

                var requestBuilder = new StringBuilder();
                requestBuilder.Append(method).Append(' ').Append(target).Append(' ').Append(version).Append("\r\n");

                foreach (var (key, value) in headers)
                {
                    if (IsHopByHopHeader(key)
                        || key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("Expect", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        continue;

                    requestBuilder.Append(key).Append(": ").Append(value).Append("\r\n");
                }

                requestBuilder.Append("Host: ").Append(host).Append("\r\n");
                requestBuilder.Append("Connection: close\r\n");

                if (body.Length > 0)
                    requestBuilder.Append("Content-Length: ").Append(body.Length).Append("\r\n");

                requestBuilder.Append("\r\n");

                await upstreamStream.WriteAsync(Encoding.UTF8.GetBytes(requestBuilder.ToString()));
                if (body.Length > 0)
                    await upstreamStream.WriteAsync(body);

                string responseHead = await ReadResponseHeadAsync(upstreamStream);

                if (String.IsNullOrEmpty(responseHead))
                {
                    await SendSimpleResponseAsync(clientStream, version, 502, "Bad Gateway", "text/plain", Encoding.UTF8.GetBytes("Catstrap asset proxy: upstream sent nothing"));
                    return true;
                }

                string statusLine = responseHead.Split("\r\n")[0];
                var responseHeaders = ParseHeaders(responseHead);
                int statusCode = ParseStatusCode(statusLine);

                int contentLength = responseHeaders.TryGetValue("Content-Length", out string? lengthText) && int.TryParse(lengthText, out int parsedLength)
                    ? parsedLength
                    : -1;

                bool isChunked = responseHeaders.TryGetValue("Transfer-Encoding", out string? transferEncoding)
                    && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);

                bool hasNoBody = method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || statusCode == 204 || statusCode == 304;

                bool eofDelimited = !hasNoBody && !isChunked && contentLength < 0;
                bool closeClient = eofDelimited || !clientKeepAlive;

                var headBuilder = new StringBuilder();
                headBuilder.Append(statusLine).Append("\r\n");

                foreach (var (key, value) in responseHeaders)
                {
                    if (IsHopByHopHeader(key))
                        continue;

                    headBuilder.Append(key).Append(": ").Append(value).Append("\r\n");
                }

                headBuilder.Append(closeClient ? "Connection: close\r\n" : "Connection: keep-alive\r\n");
                headBuilder.Append("\r\n");

                await WriteAsync(clientStream, headBuilder.ToString());
                responseStarted = true;

                string? cachePath = null;
                FileStream? cacheStream = null;

                if (App.Settings.Prop.CacheOriginalAssets
                    && cacheable
                    && statusCode == 200
                    && contentLength > 0
                    && !responseHeaders.ContainsKey("Content-Encoding"))
                {
                    cachePath = GetCachePath(uri, assetId, responseHead);

                    try
                    {
                        Directory.CreateDirectory(_cacheDirectory);

                        cachePath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
                        cacheStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    }
                    catch (Exception ex)
                    {
                        NoteFailure($"Failed to open cache file: {ex.Message}", countAsError: false);
                        cacheStream = null;
                        cachePath = null;
                    }
                }

                if (hasNoBody)
                {
                }
                else if (isChunked)
                {
                    await RelayChunkedBodyAsync(upstreamStream, clientStream);
                }
                else if (contentLength >= 0)
                {
                    await CopyExactlyAsync(upstreamStream, clientStream, contentLength, cacheStream);
                }
                else
                {
                    await CopyUntilEndAsync(upstreamStream, clientStream, cacheStream);
                }

                if (cacheStream is not null)
                {
                    string tempPath = cachePath!;
                    cacheStream.Dispose();

                    try
                    {
                        string finalPath = tempPath[..tempPath.LastIndexOf('.')];
                        finalPath = finalPath[..finalPath.LastIndexOf('.')];

                        File.Move(tempPath, finalPath, true);
                        CachedAssets++;
                    }
                    catch (Exception ex)
                    {
                        NoteFailure($"Failed to finalize cache file: {ex.Message}", countAsError: false);
                    }
                }

                return closeClient;
            }
            catch (Exception ex)
            {
                string message = ex.GetBaseException().Message;

                bool cancelled = message.Contains("aborted", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("closed by the remote", StringComparison.OrdinalIgnoreCase);

                NoteFailure($"Upstream error for {host}{target}: {message}", countAsError: !cancelled);

                if (AssetProxyManager.IsInterceptedHost(host))
                    UpstreamEndpoints.ReportFailure(host, upstreamAddress);

                if (!responseStarted)
                {
                    try
                    {
                        await SendSimpleResponseAsync(clientStream, version, 502, "Bad Gateway", "text/plain", Encoding.UTF8.GetBytes("Catstrap asset proxy: upstream request failed"));
                    }
                    catch
                    {
                    }
                }

                return true;
            }
        }

        #endregion

        #region Rule matching

        private static AssetProxyRule? FindMatchingRule(long assetId)
        {
            return AssetProxyManager.RuleIndex.TryGetValue(assetId, out AssetProxyRule? rule) ? rule : null;
        }

        private (byte[] Body, Dictionary<string, string> Headers) RewriteBatchRequest(byte[] body, Dictionary<string, string> headers)
        {
            try
            {
                bool gzipped = headers.TryGetValue("Content-Encoding", out string? encoding) && encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase);

                using var document = JsonDocument.Parse(gzipped ? Decompress(body) : body);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("requestIds", out JsonElement requestIds)
                    || requestIds.ValueKind != JsonValueKind.Array)
                    return (body, headers);

                bool changed = false;
                using var output = new MemoryStream();

                using (var writer = new Utf8JsonWriter(output))
                {
                    writer.WriteStartObject();

                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Name != "requestIds")
                        {
                            property.WriteTo(writer);
                            continue;
                        }

                        writer.WritePropertyName("requestIds");
                        writer.WriteStartArray();

                        foreach (var item in requestIds.EnumerateArray())
                        {
                            long assetId = 0;

                            bool hasId = item.ValueKind == JsonValueKind.Object
                                && item.TryGetProperty("id", out JsonElement idElement)
                                && idElement.TryGetInt64(out assetId);

                            var rule = hasId ? FindMatchingRule(assetId) : null;

                            if (rule is null)
                            {
                                item.WriteTo(writer);
                                continue;
                            }

                            if (rule.Action == AssetProxyAction.Remove)
                            {
                                App.Logger.WriteLine(LOG_IDENT, $"Removed asset {assetId} from batch request (rule '{rule.Name}')");
                                changed = true;
                                continue;
                            }

                            if (rule.Action == AssetProxyAction.Replace && rule.ToAssetId > 0)
                            {
                                App.Logger.WriteLine(LOG_IDENT, $"Replaced asset {assetId} with {rule.ToAssetId} in batch request (rule '{rule.Name}')");

                                writer.WriteStartObject();

                                foreach (var itemProperty in item.EnumerateObject())
                                {
                                    if (itemProperty.Name == "id")
                                        writer.WriteNumber("id", rule.ToAssetId);
                                    else
                                    {
                                        writer.WritePropertyName(itemProperty.Name);
                                        itemProperty.Value.WriteTo(writer);
                                    }
                                }

                                writer.WriteEndObject();
                                changed = true;
                                continue;
                            }

                            item.WriteTo(writer);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                if (!changed)
                    return (body, headers);

                byte[] rewritten = gzipped ? Compress(output.ToArray()) : output.ToArray();

                var updatedHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Length"] = rewritten.Length.ToString(CultureInfo.InvariantCulture)
                };

                return (rewritten, updatedHeaders);
            }
            catch (Exception ex)
            {
                NoteFailure($"Could not rewrite the batch request, forwarding it unchanged: {ex.Message}", countAsError: false);
                return (body, headers);
            }
        }

        private static byte[] Decompress(byte[] body)
        {
            using var input = new MemoryStream(body);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            gzip.CopyTo(output);

            return output.ToArray();
        }

        private static byte[] Compress(byte[] body)
        {
            using var output = new MemoryStream();

            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
                gzip.Write(body, 0, body.Length);

            return output.ToArray();
        }

        #endregion

        #region Response helpers

        private static async Task SendSimpleResponseAsync(Stream stream, string version, int statusCode, string reason, string contentType, byte[] body, params string[] extraHeaders)
        {
            var builder = new StringBuilder();
            builder.Append(version).Append(' ').Append(statusCode).Append(' ').Append(reason).Append("\r\n");
            builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
            builder.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            builder.Append("Connection: close\r\n");
            builder.Append("Cache-Control: no-store\r\n");

            foreach (string header in extraHeaders)
                builder.Append(header).Append("\r\n");

            builder.Append("\r\n");

            await stream.WriteAsync(Encoding.UTF8.GetBytes(builder.ToString()));
            if (body.Length > 0)
                await stream.WriteAsync(body);
        }

        private static string GetMimeType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".mp3" => "audio/mpeg",
                ".ogg" or ".oga" => "audio/ogg",
                ".wav" => "audio/wav",
                ".mp4" => "video/mp4",
                ".rbxm" => "model/rbxm",
                ".rbxmx" => "model/rbxmx",
                ".ttf" => "font/ttf",
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
        }

        #endregion

        #region Caching

        private string? GetCachePath(Uri uri, long? assetId, string responseHead)
        {
            string id = assetId?.ToString() ?? HashUrl(uri.ToString());
            string extension = GetExtensionFromContentType(responseHead) ?? GetExtensionFromUrl(uri.AbsolutePath);

            return Path.Combine(_cacheDirectory, $"{id}{extension}");
        }

        private static string? GetExtensionFromContentType(string responseHead)
        {
            foreach (string line in responseHead.Split('\n'))
            {
                if (!line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                    continue;

                string contentType = line[(line.IndexOf(':') + 1)..].Trim().Split(';')[0].ToLowerInvariant();

                return contentType switch
                {
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    "image/gif" => ".gif",
                    "image/webp" => ".webp",
                    "audio/mpeg" => ".mp3",
                    "audio/ogg" => ".ogg",
                    "audio/wav" => ".wav",
                    "video/mp4" => ".mp4",
                    "model/rbxm" => ".rbxm",
                    "model/rbxmx" => ".rbxmx",
                    "application/json" => ".json",
                    _ => null!
                };
            }

            return null;
        }

        private static string GetExtensionFromUrl(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension.Length > 0 && extension.Length <= 8)
                return extension;

            return ".bin";
        }

        private static string HashUrl(string url)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        }

        #endregion

        #region Host matching

        private static bool IsMitmHost(string host)
        {
            return MitmHosts.Contains(host);
        }

        private static bool IsHopByHopHeader(string name)
        {
            return name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("TE", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAssetPath(string path)
        {
            return path.Contains("/asset", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBatchPath(string path)
        {
            return path.Contains("/asset/batch", StringComparison.OrdinalIgnoreCase) || path.Contains("/assets/batch", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Low-level stream helpers

        private static Task<string> ReadLineAsync(Stream stream)
        {
            var buffer = new List<byte>(128);

            while (buffer.Count < 8192)
            {
                int read = stream.ReadByte();

                if (read == -1)
                    break;

                if (read == '\n')
                    break;

                if (read != '\r')
                    buffer.Add((byte)read);
            }

            return Task.FromResult(Encoding.UTF8.GetString(buffer.ToArray()));
        }

        private static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                string line = await ReadLineAsync(stream);

                if (String.IsNullOrEmpty(line))
                    break;

                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            return headers;
        }

        private static async Task<byte[]> ReadBodyAsync(Stream stream, Dictionary<string, string> headers)
        {
            if (headers.TryGetValue("Transfer-Encoding", out string? encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                return await ReadChunkedBodyAsync(stream);

            if (!headers.TryGetValue("Content-Length", out string? lengthValue) || !int.TryParse(lengthValue, out int length))
                return Array.Empty<byte>();

            if (length <= 0 || length > 32 * 1024 * 1024)
                return Array.Empty<byte>();

            var buffer = new byte[length];
            int totalRead = 0;

            while (totalRead < length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));

                if (read == 0)
                    break;

                totalRead += read;
            }

            if (totalRead != length)
                return Array.Empty<byte>();

            return buffer;
        }

        private static async Task<byte[]> ReadChunkedBodyAsync(Stream stream)
        {
            using var output = new MemoryStream();
            var buffer = new byte[65536];

            while (true)
            {
                string sizeLine = await ReadLineAsync(stream);

                if (sizeLine.Length == 0)
                    break;

                string sizeText = sizeLine.Split(';')[0].Trim();

                if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int size) || size < 0)
                    break;

                if (size == 0)
                {
                    while (true)
                    {
                        string trailer = await ReadLineAsync(stream);

                        if (trailer.Length == 0)
                            break;
                    }

                    break;
                }

                int remaining = size;

                while (remaining > 0)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));

                    if (read == 0)
                        return output.ToArray();

                    output.Write(buffer, 0, read);
                    remaining -= read;
                }

                await ReadLineAsync(stream);

                if (output.Length > 32 * 1024 * 1024)
                    break;
            }

            return output.ToArray();
        }

        private static async Task<string> ReadResponseHeadAsync(Stream stream)
        {
            var builder = new StringBuilder();

            while (true)
            {
                string line = await ReadLineAsync(stream);

                if (String.IsNullOrEmpty(line))
                {
                    if (builder.Length > 0)
                        return builder.ToString().TrimEnd('\r', '\n');

                    return "";
                }

                builder.Append(line).Append("\r\n");
            }
        }

        private static bool ShouldKeepAlive(string requestLine, Dictionary<string, string> headers)
        {
            headers.TryGetValue("Connection", out string? connection);

            connection ??= string.Empty;

            if (connection.Contains("close", StringComparison.OrdinalIgnoreCase))
                return false;

            if (requestLine.Contains("HTTP/1.0", StringComparison.OrdinalIgnoreCase)
                && !connection.Contains("keep-alive", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static async Task WriteAsync(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes);
        }

        private static async Task RelayAsync(NetworkStream clientStream, NetworkStream upstreamStream)
        {
            var clientToUpstream = RelayOneWayAsync(clientStream, upstreamStream);
            var upstreamToClient = RelayOneWayAsync(upstreamStream, clientStream);

            await Task.WhenAny(clientToUpstream, upstreamToClient);
        }

        private void NoteFailure(string message, bool countAsError = true)
        {
            if (countAsError)
                Interlocked.Increment(ref _upstreamErrors);

            DateTime now = DateTime.UtcNow;

            if (_recentFailures.TryGetValue(message, out DateTime last) && now - last < TimeSpan.FromMinutes(1))
                return;

            _recentFailures[message] = now;

            App.Logger.WriteLine(LOG_IDENT, message);
        }

        private static Dictionary<string, string> ParseHeaders(string head)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = head.Split("\r\n");

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');

                if (colon <= 0)
                    continue;

                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
            }

            return headers;
        }

        private static int ParseStatusCode(string statusLine)
        {
            string[] parts = statusLine.Split(' ');

            return parts.Length > 1 && int.TryParse(parts[1], out int status) ? status : 0;
        }

        private static async Task CopyExactlyAsync(Stream source, Stream destination, int count, FileStream? cache)
        {
            var buffer = new byte[Math.Min(65536, Math.Max(1, count))];
            int remaining = count;

            while (remaining > 0)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));

                if (read == 0)
                    break;

                remaining -= read;

                await destination.WriteAsync(buffer.AsMemory(0, read));

                if (cache is not null)
                    await cache.WriteAsync(buffer.AsMemory(0, read));
            }
        }

        private static async Task CopyUntilEndAsync(Stream source, Stream destination, FileStream? cache)
        {
            var buffer = new byte[65536];

            while (true)
            {
                int read = await source.ReadAsync(buffer);

                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read));

                if (cache is not null)
                    await cache.WriteAsync(buffer.AsMemory(0, read));
            }
        }

        private static async Task RelayChunkedBodyAsync(Stream source, Stream destination)
        {
            while (true)
            {
                string sizeLine = await ReadLineAsync(source);

                await WriteAsync(destination, sizeLine + "\r\n");

                string sizeText = sizeLine.Split(';')[0].Trim();

                if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int size) || size < 0)
                    return;

                if (size == 0)
                    break;

                await CopyExactlyAsync(source, destination, size + 2, null);
            }

            while (true)
            {
                string trailer = await ReadLineAsync(source);

                await WriteAsync(destination, trailer + "\r\n");

                if (trailer.Length == 0)
                    return;
            }
        }

        private static async Task RelayOneWayAsync(Stream source, Stream destination)
        {
            try
            {
                var buffer = new byte[16384];

                while (true)
                {
                    int bytesRead = await source.ReadAsync(buffer);

                    if (bytesRead == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
                }
            }
            catch (Exception)
            {
            }
        }

        private X509Certificate2 GetOrCreateServerCertificate(string host)
        {
            return _serverCertificates.GetOrAdd(host, h => CreateServerCertificate(_caCertificate, h));
        }

        private static X509Certificate2 CreateServerCertificate(X509Certificate2 caCertificate, string host)
        {
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(host);
            request.CertificateExtensions.Add(sanBuilder.Build());

            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);

            X509Certificate2 certificate = request.Create(caCertificate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), serial)
                .CopyWithPrivateKey(rsa);

            return WithPersistedKey(certificate);
        }

        private static X509Certificate2 WithPersistedKey(X509Certificate2 certificate)
        {
            try
            {
                const X509KeyStorageFlags flags =
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet;

                var persisted = new X509Certificate2(certificate.Export(X509ContentType.Pfx), (string?)null, flags);

                certificate.Dispose();

                return persisted;
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine(LOG_IDENT, $"Could not persist the server certificate key: {ex.GetBaseException().Message}");

                return certificate;
            }
        }

        #endregion
    }
}