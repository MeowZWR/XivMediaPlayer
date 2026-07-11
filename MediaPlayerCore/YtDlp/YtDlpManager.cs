using Newtonsoft.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MediaPlayerCore.YtDlp
{
    /// <summary>
    /// Metadata returned by yt-dlp --dump-json for a given URL.
    /// </summary>
    public class YtDlpMetadata
    {
        [JsonProperty("title")]
        public string? Title { get; set; }

        [JsonProperty("duration")]
        public double? Duration { get; set; }

        [JsonProperty("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonProperty("uploader")]
        public string? Uploader { get; set; }

        [JsonProperty("url")]
        public string? Url { get; set; }

        [JsonProperty("webpage_url")]
        public string? WebpageUrl { get; set; }

        [JsonProperty("is_live")]
        public bool? IsLive { get; set; }

        [JsonProperty("http_headers")]
        public Dictionary<string, string>? HttpHeaders { get; set; }

        [JsonProperty("ext")]
        public string? Extension { get; set; }
    }

    /// <summary>
    /// Manages yt-dlp binary execution for resolving stream URLs and fetching metadata.
    /// Supports YouTube, Twitch, and 1000+ other sites.
    /// </summary>
    public class YtDlpManager : IDisposable
    {
        private string _ytDlpPath;
        private string? _cookiesPath;
        private int _preferredMaxHeight;
        private readonly object _lock = new object();
        private TcpListener? _cookieListener;
        private Thread? _cookieListenerThread;
        private bool _isListeningForCookies;
        private readonly List<Process> _runningProcesses = new();

        public event EventHandler<string>? OnStatusUpdate;
        public event EventHandler<Exception>? OnError;

        /// <summary>
        /// Path to the yt-dlp executable.
        /// </summary>
        public string YtDlpPath
        {
            get => _ytDlpPath;
            set => _ytDlpPath = value;
        }

        /// <summary>
        /// Preferred max video height for quality selection (e.g. 360, 480, 720, 1080).
        /// 0 = best available.
        /// </summary>
        public int PreferredMaxHeight
        {
            get => _preferredMaxHeight;
            set => _preferredMaxHeight = value;
        }

        public YtDlpManager(string pluginDir, int preferredMaxHeight = 720)
        {
            _ytDlpPath = Path.Combine(pluginDir, "yt-dlp.exe");
            _preferredMaxHeight = preferredMaxHeight;
            _cookiesPath = FindCookiesFile();
            StartCookieListener();
        }

        private void StartCookieListener()
        {
            try
            {
                _cookieListener = new TcpListener(IPAddress.Loopback, 9696);
                _cookieListener.Start();

                _isListeningForCookies = true;
                _cookieListenerThread = new Thread(CookieListenerLoop)
                {
                    IsBackground = true,
                    Name = "VRCVideoCacherCookieListener"
                };
                _cookieListenerThread.Start();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new Exception("Failed to start VRCVideoCacher cookie listener. Port 9696 might be in use.", ex));
            }
        }

        private void CookieListenerLoop()
        {
            while (_isListeningForCookies && _cookieListener != null)
            {
                try
                {
                    using var client = _cookieListener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);

                    string? line;
                    int contentLength = 0;
                    bool isPost = false;

                    // Read HTTP headers
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        if (line.StartsWith("POST")) isPost = true;
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(line.Substring(15).Trim(), out int len))
                            {
                                contentLength = len;
                            }
                        }
                    }

                    if (isPost && contentLength > 0)
                    {
                        char[] bodyChars = new char[contentLength];
                        int read = reader.ReadBlock(bodyChars, 0, contentLength);
                        string body = new string(bodyChars, 0, read);

                        // Try to parse out the cookies and save them
                        if (!string.IsNullOrEmpty(body) && body.Contains(".youtube.com", StringComparison.OrdinalIgnoreCase))
                        {
                            if (SaveCookiesFromExtension(body))
                            {
                                OnStatusUpdate?.Invoke(this, "Successfully processed cookies from VRCVideoCacher browser extension!");
                            }
                            else
                            {
                                OnError?.Invoke(this, new Exception("Received cookies from VRCVideoCacher extension but failed to save them."));
                            }
                        }
                    }

                    // Send a CORS-friendly 200 OK
                    string response = "HTTP/1.1 200 OK\r\n" +
                                      "Access-Control-Allow-Origin: *\r\n" +
                                      "Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
                                      "Access-Control-Allow-Headers: Content-Type\r\n" +
                                      "Connection: close\r\n\r\nOK";
                    byte[] buffer = Encoding.UTF8.GetBytes(response);
                    stream.Write(buffer, 0, buffer.Length);
                }
                catch (SocketException)
                {
                    // Thrown when the listener is stopped/aborted
                    break;
                }
                catch (Exception e)
                {
                    OnError?.Invoke(this, new Exception("Error receiving cookies via TcpListener.", e));
                }
            }
        }

        public void Dispose()
        {
            _isListeningForCookies = false;
            try
            {
                _cookieListener?.Stop();
            }
            catch { }

            lock (_runningProcesses)
            {
                foreach (var process in _runningProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch { }
                }
                _runningProcesses.Clear();
            }
        }

        /// <summary>
        /// Returns true if a valid cookies file was found (e.g. from VRCVideoCacher).
        /// </summary>
        public bool HasCookies => !string.IsNullOrEmpty(_cookiesPath) && File.Exists(_cookiesPath);

        /// <summary>
        /// Returns true if the yt-dlp binary exists at the configured path.
        /// </summary>
        public bool IsAvailable()
        {
            return !string.IsNullOrEmpty(_ytDlpPath) && File.Exists(_ytDlpPath);
        }

        /// <summary>
        /// Resolves a URL to a direct stream URL suitable for VLC playback.
        /// Returns null if resolution fails.
        /// </summary>
        public async Task<string[]?> ResolveStreamUrl(string url)
        {
            if (!IsAvailable())
            {
                OnError?.Invoke(this, new FileNotFoundException("yt-dlp binary not found at: " + _ytDlpPath));
                return null;
            }

            try
            {
                OnStatusUpdate?.Invoke(this, "Resolving stream URL...");

                string formatArg = _preferredMaxHeight > 0
                  ? $"bv*[height<={_preferredMaxHeight}]+ba/b[height<={_preferredMaxHeight}]/b"
                  : "bv*+ba/b";

                string result = await RunYtDlp($"--get-url -f \"{formatArg}\" \"{url}\"", url: url);
                string[]? streamUrls = result?.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (streamUrls != null && streamUrls.Length > 0)
                {
                    OnStatusUpdate?.Invoke(this, $"Stream URL resolved ({streamUrls.Length} streams).");
                    return streamUrls;
                }

                OnError?.Invoke(this, new Exception("yt-dlp returned empty URL for: " + url));
                return null;
            } catch (Exception e)
            {
                OnError?.Invoke(this, e);
                return null;
            }
        }

        /// <summary>
        /// Fetches metadata (title, duration, uploader, thumbnail, etc.) for a URL.
        /// Returns null if fetching fails.
        /// </summary>
        public async Task<YtDlpMetadata?> GetMetadata(string url, bool reportErrors = true)
        {
            if (!IsAvailable())
            {
                return null;
            }

            try
            {
                string result = await RunYtDlp($"--dump-json --no-download --no-playlist \"{url}\"", url: url);
                if (!string.IsNullOrEmpty(result))
                {
                    var firstJsonLine = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                              .FirstOrDefault(l => l.TrimStart().StartsWith("{"));
                    if (firstJsonLine != null)
                    {
                        return JsonConvert.DeserializeObject<YtDlpMetadata>(firstJsonLine);
                    }
                }
            } catch (Exception e)
            {
                if (reportErrors)
                {
                    OnError?.Invoke(this, e);
                }
            }
            return null;
        }

        /// <summary>
        /// Resolves a URL to multiple quality stream URLs.
        /// Returns an array where index 0 = audio-only, then ascending quality.
        /// Falls back to single URL if format listing fails.
        /// </summary>
        public async Task<string[]> ResolveMultiQualityUrls(string url)
        {
            if (!IsAvailable())
            {
                return Array.Empty<string>();
            }

            try
            {
                // Try to get URLs at specific quality levels
                var qualities = new[] { 360, 480, 720, 1080 };
                var urls = new List<string>();

                // Audio only
                string? audioUrl = await ResolveUrlWithFormat(url, "bestaudio");
                urls.Add(audioUrl ?? "");

                // Video at each quality level
                foreach (int height in qualities)
                {
                    string? qualityUrl = await ResolveUrlWithFormat(url, $"b[height<={height}]/b");
                    urls.Add(qualityUrl ?? "");
                }

                // If we got at least one valid URL, return the array
                if (urls.Any(u => !string.IsNullOrEmpty(u)))
                {
                    return urls.ToArray();
                }

                // Fallback: just get best URL
                string[]? bestUrls = await ResolveStreamUrl(url);
                if (bestUrls != null && bestUrls.Length > 0 && !string.IsNullOrEmpty(bestUrls[0]))
                {
                    return new string[] { bestUrls[0], bestUrls[0], bestUrls[0], bestUrls[0], bestUrls[0] };
                }
            } catch (Exception e)
            {
                OnError?.Invoke(this, e);
            }
            return Array.Empty<string>();
        }

        private static readonly System.Collections.Generic.HashSet<string> _knownFailedUrls = new();

        /// <summary>
        /// Marks a URL as known to fail with yt-dlp (e.g. 403 or Unsupported).
        /// </summary>
        public static void MarkUrlAsFailed(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            lock (_knownFailedUrls)
            {
                _knownFailedUrls.Add(url);
            }
        }

        /// <summary>
        /// Checks if the given URL is likely supported by yt-dlp
        /// (not a raw stream or local file).
        /// </summary>
        public static bool IsUrlSupported(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            
            lock (_knownFailedUrls)
            {
                if (_knownFailedUrls.Contains(url)) return false;
            }

            // Don't try yt-dlp on raw streams or local files
            if (url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)) return false;
            if (url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(url)) return false;
            // Must be an HTTP URL
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to self-update yt-dlp via yt-dlp -U.
        /// </summary>
        public async Task<bool> SelfUpdate()
        {
            if (!IsAvailable()) return false;

            try
            {
                OnStatusUpdate?.Invoke(this, "Updating yt-dlp...");
                string result = await RunYtDlp("-U", withCommonArgs: false);
                OnStatusUpdate?.Invoke(this, "yt-dlp update complete.");
                return true;
            } catch (Exception e)
            {
                OnError?.Invoke(this, e);
                return false;
            }
        }

        #region Private Helpers

        private const string Deno =
            "https://github.com/denoland/deno/releases/download/v2.8.2/deno-x86_64-pc-windows-msvc.zip";
        private const string YtDlpDownloadUrl =
          "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string ChromeCookieUnlockUrl =
          "https://github.com/seproDev/yt-dlp-ChromeCookieUnlock/releases/latest/download/yt-dlp-ChromeCookieUnlock.zip";

        private string PluginDir => Path.GetDirectoryName(_ytDlpPath) ?? ".";
        private string PluginsDir => Path.Combine(PluginDir, "yt-dlp-plugins");
        private string ChromeCookieUnlockPath => Path.Combine(PluginsDir, "yt-dlp-ChromeCookieUnlock.zip");
        private string DenoPath => Path.Combine(PluginDir, "deno.zip");

        /// <summary>
        /// Ensures yt-dlp and all dependencies are available.
        /// Downloads missing components, then self-updates yt-dlp.
        /// Call this at plugin startup on a background thread.
        /// </summary>
        public async Task EnsureAvailableAsync()
        {
            if (!IsAvailable())
            {
                await DownloadYtDlp();
            }

            // Download companion tools if missing
            await EnsureChromeCookieUnlock();
            await EnsureDeno();

            if (IsAvailable())
            {
                await SelfUpdate();
            }

            // Report cookie status
            if (HasCookies)
            {
                OnStatusUpdate?.Invoke(this, $"Using cookies from: {_cookiesPath}");
            } else if (!string.IsNullOrEmpty(CookieBrowser))
            {
                OnStatusUpdate?.Invoke(this, $"Using cookies from browser: {CookieBrowser}");
            } else
            {
                OnStatusUpdate?.Invoke(this, "No YouTube cookies found. Place cookies.txt in plugin dir, or install VRCVideoCacher browser extension.");
            }
        }

        /// <summary>
        /// Downloads yt-dlp.exe from GitHub releases to the plugin directory.
        /// </summary>
        private async Task DownloadYtDlp()
        {
            await DownloadFile(YtDlpDownloadUrl, _ytDlpPath, "yt-dlp");
        }

        /// <summary>
        /// Downloads the ChromeCookieUnlock plugin if missing.
        /// Placed in yt-dlp-plugins/ next to yt-dlp.exe so it's auto-discovered.
        /// </summary>
        private async Task EnsureChromeCookieUnlock()
        {
            if (File.Exists(ChromeCookieUnlockPath)) return;
            Directory.CreateDirectory(PluginsDir);
            await DownloadFile(ChromeCookieUnlockUrl, ChromeCookieUnlockPath, "ChromeCookieUnlock plugin");
        }

        /// <summary>
        /// Downloads the ChromeCookieUnlock plugin if missing.
        /// Placed in yt-dlp-plugins/ next to yt-dlp.exe so it's auto-discovered.
        /// </summary>
        private async Task EnsureDeno()
        {
            if (File.Exists(DenoPath.Replace(".zip", ".exe"))) return;
            await DownloadFile(Deno, DenoPath, "Deno");
            ZipFile.ExtractToDirectory(DenoPath, PluginDir);
        }

        /// <summary>
        /// Generic file downloader with temp-file-then-move pattern.
        /// </summary>
        private async Task DownloadFile(string url, string targetPath, string displayName)
        {
            try
            {
                string targetDir = Path.GetDirectoryName(targetPath) ?? ".";
                Directory.CreateDirectory(targetDir);

                OnStatusUpdate?.Invoke(this, $"Downloading {displayName}...");

                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XivMediaPlayer/1.0");

                using var response = await httpClient.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // Write to a temp file first, then move (atomic-ish)
                string tempPath = targetPath + ".tmp";
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                // Replace existing if present
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempPath, targetPath);

                OnStatusUpdate?.Invoke(this, $"{displayName} downloaded.");
            } catch (Exception e)
            {
                OnError?.Invoke(this, new Exception($"Failed to download {displayName}: " + e.Message, e));
            }
        }

        private async Task<string?> ResolveUrlWithFormat(string url, string format)
        {
            try
            {
                string result = await RunYtDlp($"--get-url -f \"{format}\" \"{url}\"", url: url);
                return result?.Trim().Split('\n').FirstOrDefault()?.Trim();
            } catch
            {
                return null;
            }
        }

        private async Task<string> RunYtDlp(string arguments, bool withCommonArgs = true, string? url = null)
        {
            return await Task.Run(() =>
            {
                // Inject cookies if available (e.g. from VRCVideoCacher browser extension)
                string fullArgs = (withCommonArgs ? BuildCommonArgs(url) + BuildSiteSpecificArgs(url) : "") + arguments;
                var psi = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = fullArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    throw new Exception("Failed to start yt-dlp process");
                }

                lock (_runningProcesses)
                {
                    _runningProcesses.Add(process);
                }

                bool timedOut = false;
                using var timer = new Timer(_ =>
                {
                    timedOut = true;
                    try { process.Kill(true); } catch { }
                }, null, 30000, Timeout.Infinite);

                // Read stderr asynchronously to prevent buffer deadlocks
                string error = "";
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) error += e.Data + "\n"; };
                process.BeginErrorReadLine();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                timer.Change(Timeout.Infinite, Timeout.Infinite);

                if (timedOut)
                {
                    throw new TimeoutException("yt-dlp timed out after 30 seconds");
                }

                if (process.ExitCode != 0 && string.IsNullOrEmpty(output))
                {
                    throw new Exception($"yt-dlp exited with code {process.ExitCode}: {error}");
                }

                lock (_runningProcesses)
                {
                    _runningProcesses.Remove(process);
                }

                return output;
            });
        }

        /// <summary>
        /// Looks for a cookies file in order of priority:
        /// 1. Plugin directory (cookies.txt — user-provided or auto-saved from clipboard)
        /// 2. VRCVideoCacher's youtube_cookies.txt (from browser extension)
        /// </summary>
        private string? FindCookiesFile()
        {
            // 1. Check plugin directory first
            string pluginDir = Path.GetDirectoryName(_ytDlpPath) ?? ".";
            string localCookies = Path.Combine(pluginDir, "cookies.txt");
            if (File.Exists(localCookies)) return localCookies;

            // 2. Check VRCVideoCacher's youtube_cookies.txt
            string vrcCookies = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher", "youtube_cookies.txt");
            if (File.Exists(vrcCookies)) return vrcCookies;

            return null;
        }

        public bool HasCookiesFile => FindCookiesFile() != null;

        /// <summary>
        /// True when the VRCVideoCacher browser extension listener is running on localhost:9696.
        /// </summary>
        public bool IsCookieListenerActive => _isListeningForCookies && _cookieListener != null;

        /// <summary>
        /// Returns the path where cookies.txt will be saved (plugin directory).
        /// </summary>
        public string CookiesSavePath => Path.Combine(
          Path.GetDirectoryName(_ytDlpPath) ?? ".", "cookies.txt");

        private static readonly string[] KnownVideoCookieDomains =
        {
            ".youtube.com", "youtube.com", ".google.com", ".bilibili.com", "bilibili.com", ".twitch.tv", "twitch.tv",
        };

        /// <summary>
        /// Checks if text looks like Netscape cookie format (tab-separated, 7 fields per line).
        /// Used for auto-detecting cookies on the clipboard.
        /// </summary>
        public static bool IsNetscapeCookieFormat(string? text)
        {
            return ValidateCookieText(text, out _) == CookieValidationResult.Valid;
        }

        public enum CookieValidationResult
        {
            Valid,
            Empty,
            LooksLikeUrl,
            LooksLikeJson,
            NoCookieLines,
            InvalidFormat,
            NoKnownVideoSite,
        }

        /// <summary>
        /// Validates clipboard or pasted text before saving as cookies.txt.
        /// </summary>
        public static CookieValidationResult ValidateCookieText(string? text, out int validCookieCount)
        {
            validCookieCount = 0;
            if (string.IsNullOrWhiteSpace(text)) return CookieValidationResult.Empty;

            string trimmed = text.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return CookieValidationResult.LooksLikeUrl;
            }

            if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return CookieValidationResult.LooksLikeJson;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            bool hasNetscapeHeader = lines.Any(l =>
                l.Contains("Netscape HTTP Cookie File", StringComparison.OrdinalIgnoreCase)
                || l.Contains("HTTP Cookie File", StringComparison.OrdinalIgnoreCase));

            var cookieLines = lines
              .Select(l => l.TrimEnd('\r'))
              .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
              .ToArray();

            if (cookieLines.Length == 0) return CookieValidationResult.NoCookieLines;

            int knownSiteCount = 0;
            foreach (var line in cookieLines)
            {
                if (!TryParseNetscapeCookieLine(line, out var domain)) continue;

                validCookieCount++;
                if (KnownVideoCookieDomains.Any(d => domain.Contains(d, StringComparison.OrdinalIgnoreCase)))
                {
                    knownSiteCount++;
                }
            }

            if (validCookieCount == 0) return CookieValidationResult.InvalidFormat;

            // Reject files where most lines fail parsing — likely random clipboard text.
            if (validCookieCount < Math.Max(1, cookieLines.Length / 2)) return CookieValidationResult.InvalidFormat;

            if (!hasNetscapeHeader && knownSiteCount == 0) return CookieValidationResult.NoKnownVideoSite;

            return CookieValidationResult.Valid;
        }

        private static bool TryParseNetscapeCookieLine(string line, out string domain)
        {
            domain = string.Empty;
            var parts = line.Split('\t');
            if (parts.Length != 7) return false;

            domain = parts[0].Trim();
            string subdomainFlag = parts[1].Trim();
            string path = parts[2].Trim();
            string secure = parts[3].Trim();
            string expiration = parts[4].Trim();
            string name = parts[5].Trim();

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(name)) return false;
            if (!domain.Contains('.') && !domain.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
            if (!path.StartsWith('/')) return false;
            if (subdomainFlag is not ("TRUE" or "FALSE")) return false;
            if (secure is not ("TRUE" or "FALSE")) return false;
            if (!long.TryParse(expiration, out _)) return false;

            return true;
        }

        /// <summary>
        /// Saves cookies pushed by the VRCVideoCacher browser extension (localhost:9696).
        /// Uses standard validation first, then a relaxed check for known extension payloads.
        /// </summary>
        public bool SaveCookiesFromExtension(string cookieText)
        {
            if (string.IsNullOrWhiteSpace(cookieText)) return false;

            var validation = ValidateCookieText(cookieText, out var validCount);
            if (validation == CookieValidationResult.Valid)
            {
                return WriteCookiesFile(cookieText, "VRCVideoCacher browser extension");
            }

            if (!cookieText.Contains(".youtube.com", StringComparison.OrdinalIgnoreCase)) return false;
            if (!IsVRCVideoCacherPayload(cookieText)) return false;
            if (validCount < 1) return false;

            return WriteCookiesFile(cookieText, "VRCVideoCacher browser extension");
        }

        private static bool IsVRCVideoCacherPayload(string body)
        {
            return body.Contains("generated by VRCVideoCacher", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Netscape HTTP Cookie File", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Saves cookie text to the plugin directory and updates the cookie path.
        /// Returns true if saved successfully.
        /// </summary>
        public bool SaveCookiesFromText(string cookieText, string source = "clipboard")
        {
            var validation = ValidateCookieText(cookieText, out _);
            if (validation != CookieValidationResult.Valid) return false;

            return WriteCookiesFile(cookieText, source);
        }

        private bool WriteCookiesFile(string cookieText, string source)
        {
            try
            {
                File.WriteAllText(CookiesSavePath, cookieText);
                _cookiesPath = CookiesSavePath;
                OnStatusUpdate?.Invoke(this, $"YouTube cookies saved from {source}.");
                return true;
            } catch (Exception e)
            {
                OnError?.Invoke(this, new Exception("Failed to save cookies: " + e.Message, e));
                return false;
            }
        }

        /// <summary>
        /// Validates and saves cookie text. Returns validation result when save fails.
        /// </summary>
        public CookieValidationResult TrySaveCookiesFromText(string cookieText, string source = "clipboard")
        {
            var validation = ValidateCookieText(cookieText, out _);
            if (validation != CookieValidationResult.Valid) return validation;

            return SaveCookiesFromText(cookieText, source) ? CookieValidationResult.Valid : CookieValidationResult.InvalidFormat;
        }

        /// <summary>
        /// Optional: set to a browser name (chrome, firefox, edge) to use
        /// yt-dlp's --cookies-from-browser feature instead of a cookie file.
        /// </summary>
        public string? CookieBrowser { get; set; }

        /// <summary>
        /// Builds the common argument prefix (cookies, etc.) for all yt-dlp calls.
        /// </summary>
        private string BuildCommonArgs(string? url)
        {
            var args = new StringBuilder();
            if (!IsBilibiliUrl(url))
            {
                args.Append($"--impersonate chrome --extractor-args \"youtube:player_client=ios,tv,web\" --extractor-args \"youtubetab:skip=authcheck\" --js-runtimes \"deno:{DenoPath.Replace(".zip", ".exe")}\" ");
            }

            args.Append("--socket-timeout 30 ");

            // Cookie injection
            if (!string.IsNullOrEmpty(CookieBrowser))
            {
                args.Append($"--cookies-from-browser {CookieBrowser} ");
            } else if (HasCookies)
             {
                args.Append($"--cookies \"{_cookiesPath}\" ");
            }

            return args.ToString();
        }

        /// <summary>
        /// Bilibili's API rejects yt-dlp with HTTP 412 unless Origin/Referer are set.
        /// See: https://github.com/yt-dlp/yt-dlp/issues/14830
        /// </summary>
        private static string BuildSiteSpecificArgs(string? url)
        {
            if (!IsBilibiliUrl(url)) return string.Empty;

            return "--add-header \"Origin:https://www.bilibili.com\" --add-header \"Referer:https://www.bilibili.com\" ";
        }

        public static bool IsBilibiliUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                var host = new Uri(url).Host.ToLowerInvariant();
                return host == "b23.tv"
                    || host == "bilibili.com"
                    || host.EndsWith(".bilibili.com");
            }
            catch
            {
                return url.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("b23.tv", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Detects URLs that represent live streams rather than seekable VODs.
        /// Live streams should always resume from the live edge (timecode 0).
        /// </summary>
        public static bool IsLiveStreamUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            string urlWithoutQuery = url.Split('?')[0];
            if (urlWithoutQuery.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) return true;
            if (url.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase)
                && !url.Contains("/videos/", StringComparison.OrdinalIgnoreCase)) return true;
            return IsBilibiliLiveUrl(url);
        }

        /// <summary>
        /// Bilibili live room URLs (e.g. live.bilibili.com/8178490).
        /// Used as a fallback when yt-dlp metadata is missing, similar to Twitch channel URLs.
        /// </summary>
        public static bool IsBilibiliLiveUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                return new Uri(url).Host.Equals("live.bilibili.com", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return url.Contains("live.bilibili.com", StringComparison.OrdinalIgnoreCase);
            }
        }



        #endregion
    }
}
