using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MediaPlayerCore
{
    public class StreamProxy : IDisposable
    {
        private static readonly Lazy<StreamProxy> _instance = new Lazy<StreamProxy>(() => new StreamProxy());
        public static StreamProxy Instance => _instance.Value;

        private HttpListener _listener;
        private int _port;
        private CancellationTokenSource _cts;
        private ConcurrentDictionary<string, ProxySession> _sessions = new ConcurrentDictionary<string, ProxySession>();

        public class ProxySession
        {
            public string OriginalM3u8Url { get; set; }
            public string? PreFetchedM3u8Content { get; set; }
            public bool IsLive { get; set; }
            public Dictionary<string, string>? Headers { get; set; }
            public HttpClient Client { get; set; }
        }

        private const int FetchMaxAttempts = 3;
        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

        private StreamProxy()
        {
            _port = 40000 + new Random().Next(1000);
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _cts = new CancellationTokenSource();
        }

        public void Start()
        {
            if (_listener.IsListening) return;
            try
            {
                _listener.Start();
                Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamProxy] Failed to start listener: {ex.Message}");
            }
        }

        public string RegisterStream(string m3u8Url, Dictionary<string, string>? headers = null, string? preFetchedM3u8Content = null, bool isLive = false)
        {
            Start();
            string sessionId = Guid.NewGuid().ToString("N");
            
            var handler = new HttpClientHandler();
            handler.UseCookies = false; // We are manually injecting the Cookie header
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            }

            var client = new HttpClient(handler);
            bool hasUserAgent = false;
            bool hasAccept = false;
            if (headers != null)
            {
                foreach (var kvp in headers)
                {
                    if (kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) hasUserAgent = true;
                    if (kvp.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) hasAccept = true;
                    try { client.DefaultRequestHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value); } catch { }
                }
            }
            if (!hasUserAgent)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
            if (!hasAccept)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            }
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

            _sessions[sessionId] = new ProxySession
            {
                OriginalM3u8Url = m3u8Url,
                PreFetchedM3u8Content = isLive ? null : preFetchedM3u8Content,
                IsLive = isLive,
                Headers = headers,
                Client = client
            };

            return $"http://127.0.0.1:{_port}/stream.m3u8?sid={sessionId}";
        }
        public string RegisterDirectMediaSession(string mediaUrl, Dictionary<string, string>? headers = null)
        {
            if (string.IsNullOrEmpty(mediaUrl)) return string.Empty;
            Start();
            string sessionId = Guid.NewGuid().ToString("N");
            
            var handler = new HttpClientHandler { UseCookies = true, AutomaticDecompression = DecompressionMethods.All };
            var client = new HttpClient(handler);
            bool hasUserAgent = false;
            bool hasAccept = false;

            if (headers != null)
            {
                foreach (var kvp in headers)
                {
                    if (kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) hasUserAgent = true;
                    if (kvp.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) hasAccept = true;
                    try { client.DefaultRequestHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value); } catch { }
                }
            }
            if (!hasUserAgent) client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            if (!hasAccept) client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");

            _sessions[sessionId] = new ProxySession { OriginalM3u8Url = mediaUrl, Headers = headers, Client = client };

            string targetBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(mediaUrl));
            return $"http://127.0.0.1:{_port}/proxy_media?sid={sessionId}&target={Uri.EscapeDataString(targetBase64)}";
        }

        /// <summary>
        /// Attempts to recover the original upstream URL from a proxy session ID.
        /// Returns null if the session is not found or has expired.
        /// </summary>
        public string GetOriginalUrl(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var session))
            {
                return session.OriginalM3u8Url;
            }
            return null;
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch { }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                var req = context.Request;
                var res = context.Response;

                string path = req.Url.LocalPath;
                string sid = req.QueryString["sid"];

                if (string.IsNullOrEmpty(sid) || !_sessions.TryGetValue(sid, out var session))
                {
                    res.StatusCode = 404;
                    res.Close();
                    return;
                }

                if (path == "/stream.m3u8")
                {
                    // Fetch original m3u8
                    string m3u8Url = req.QueryString["target"] != null 
                        ? Encoding.UTF8.GetString(Convert.FromBase64String(req.QueryString["target"]))
                        : session.OriginalM3u8Url;

                    string text = "";
                    if (req.QueryString["target"] == null && !session.IsLive && !string.IsNullOrEmpty(session.PreFetchedM3u8Content))
                    {
                        text = session.PreFetchedM3u8Content;
                    }
                    else
                    {
                        text = await TryFetchM3u8TextAsync(session.Client, m3u8Url);
                        if (text == null)
                        {
                            res.StatusCode = 502;
                            res.Close();
                            return;
                        }
                    }

                    // Rewrite URLs
                    Uri baseUri = new Uri(m3u8Url);
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var sb = new StringBuilder();

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("#"))
                        {
                            sb.AppendLine(RewriteHlsAttributeUris(line, baseUri, sid));
                        }
                        else
                        {
                            // This is a URL
                            Uri absoluteUrl;
                            if (!Uri.TryCreate(baseUri, line, out absoluteUrl))
                            {
                                sb.AppendLine(line);
                                continue;
                            }

                            sb.AppendLine(BuildProxiedHlsUrl(absoluteUrl, sid));
                        }
                    }

                    byte[] outBytes = Encoding.UTF8.GetBytes(sb.ToString());
                    res.ContentType = "application/vnd.apple.mpegurl";
                    res.ContentLength64 = outBytes.Length;
                    await res.OutputStream.WriteAsync(outBytes, 0, outBytes.Length);
                }
                else if (path == "/stream.ts")
                {
                    string targetUrl = Encoding.UTF8.GetString(Convert.FromBase64String(req.QueryString["target"]));
                    if (!await TryFetchToStreamAsync(session.Client, targetUrl, res, "video/MP2T"))
                    {
                        if (res.OutputStream.CanWrite && !res.SendChunked && res.ContentLength64 == 0)
                            res.StatusCode = 502;
                    }
                }
                else if (path == "/proxy_media")
                {
                    string targetUrl = Encoding.UTF8.GetString(Convert.FromBase64String(req.QueryString["target"]));
                    var requestMessage = new HttpRequestMessage(HttpMethod.Get, targetUrl);

                    long requestedOffset = 0;
                    string rangeHeader = req.Headers["Range"];
                    if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
                    {
                        string rangeVal = rangeHeader.Substring("bytes=".Length).Split('-')[0];
                        if (long.TryParse(rangeVal, out long offset))
                        {
                            requestedOffset = offset;
                            requestMessage.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
                        }
                    }

                    using var response = await session.Client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                    
                    res.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

                    using var stream = await response.Content.ReadAsStreamAsync();

                    if (response.StatusCode == HttpStatusCode.OK && requestedOffset > 0)
                    {
                        // SERVER IGNORED RANGE REQUEST. WE MUST MANUALLY DISCARD BYTES.
                        long bytesToDiscard = requestedOffset;
                        byte[] discardBuffer = new byte[81920]; // 80KB buffer
                        while (bytesToDiscard > 0)
                        {
                            int toRead = (int)Math.Min(bytesToDiscard, discardBuffer.Length);
                            int read = await stream.ReadAsync(discardBuffer, 0, toRead);
                            if (read == 0) break; // EOF
                            bytesToDiscard -= read;
                        }

                        res.StatusCode = 206; // Trick VLC into thinking the server honored the Range request
                        long totalLength = response.Content.Headers.ContentLength ?? 0;
                        if (totalLength > 0)
                        {
                            res.ContentLength64 = totalLength - requestedOffset;
                            res.Headers["Content-Range"] = $"bytes {requestedOffset}-{totalLength - 1}/{totalLength}";
                        }
                        else
                        {
                            res.SendChunked = true;
                            res.Headers["Content-Range"] = $"bytes {requestedOffset}-/*";
                        }
                    }
                    else
                    {
                        res.StatusCode = (int)response.StatusCode;
                        if (response.Content.Headers.ContentLength.HasValue)
                        {
                            res.ContentLength64 = response.Content.Headers.ContentLength.Value;
                        }
                        else
                        {
                            res.SendChunked = true;
                        }

                        if (response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            res.Headers["Content-Range"] = response.Content.Headers.ContentRange?.ToString();
                        }
                    }

                    await stream.CopyToAsync(res.OutputStream);
                }
                else
                {
                    res.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamProxy] Error handling request: {ex.Message}");
                try { context.Response.StatusCode = 500; } catch { }
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private string RewriteHlsAttributeUris(string line, Uri baseUri, string sid)
        {
            const string uriPrefix = "URI=\"";
            int searchStart = 0;

            while (true)
            {
                int uriStart = line.IndexOf(uriPrefix, searchStart, StringComparison.OrdinalIgnoreCase);
                if (uriStart < 0) return line;

                int valueStart = uriStart + uriPrefix.Length;
                int valueEnd = line.IndexOf('"', valueStart);
                if (valueEnd < 0) return line;

                string uriValue = line.Substring(valueStart, valueEnd - valueStart);
                if (Uri.TryCreate(baseUri, uriValue, out var absoluteUri))
                {
                    string proxiedUri = BuildProxiedHlsUrl(absoluteUri, sid);
                    line = line.Substring(0, valueStart) + proxiedUri + line.Substring(valueEnd);
                    searchStart = valueStart + proxiedUri.Length;
                }
                else
                {
                    searchStart = valueEnd + 1;
                }
            }
        }

        private static async Task<string?> TryFetchM3u8TextAsync(HttpClient client, string url)
        {
            for (int attempt = 0; attempt < FetchMaxAttempts; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(FetchTimeout);
                    using var response = await client.GetAsync(url, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        if (code < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
                            return null;

                        if (attempt < FetchMaxAttempts - 1)
                        {
                            await Task.Delay(500 * (attempt + 1));
                            continue;
                        }
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StreamProxy] m3u8 fetch attempt {attempt + 1} failed: {ex.Message}");
                    if (attempt < FetchMaxAttempts - 1)
                    {
                        await Task.Delay(500 * (attempt + 1));
                        continue;
                    }
                }
            }
            return null;
        }

        private static async Task<bool> TryFetchToStreamAsync(HttpClient client, string url, HttpListenerResponse res, string contentTypeFallback)
        {
            for (int attempt = 0; attempt < FetchMaxAttempts; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(FetchTimeout);
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        if (code < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
                        {
                            res.StatusCode = (int)response.StatusCode;
                            return false;
                        }

                        if (attempt < FetchMaxAttempts - 1)
                        {
                            await Task.Delay(500 * (attempt + 1));
                            continue;
                        }
                        return false;
                    }

                    res.StatusCode = (int)response.StatusCode;
                    res.ContentType = response.Content.Headers.ContentType?.ToString() ?? contentTypeFallback;
                    if (response.Content.Headers.ContentLength.HasValue)
                        res.ContentLength64 = response.Content.Headers.ContentLength.Value;

                    await response.Content.CopyToAsync(res.OutputStream);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StreamProxy] segment fetch attempt {attempt + 1} failed: {ex.Message}");
                    if (attempt < FetchMaxAttempts - 1)
                    {
                        await Task.Delay(500 * (attempt + 1));
                        continue;
                    }
                }
            }
            return false;
        }

        private string BuildProxiedHlsUrl(Uri absoluteUrl, string sid)
        {
            string targetBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(absoluteUrl.ToString()));
            string escapedTarget = Uri.EscapeDataString(targetBase64);

            if (absoluteUrl.ToString().Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                return $"http://127.0.0.1:{_port}/stream.m3u8?sid={sid}&target={escapedTarget}";
            }

            return $"http://127.0.0.1:{_port}/stream.ts?sid={sid}&target={escapedTarget}";
        }

        public void ClearSessions()
        {
            foreach (var session in _sessions.Values)
            {
                try { session.Client?.Dispose(); } catch { }
            }
            _sessions.Clear();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            ClearSessions();
        }
    }
}
