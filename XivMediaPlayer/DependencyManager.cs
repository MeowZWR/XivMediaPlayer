using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer
{
    public class DependencyManager
    {
        private const string NativeDepsRepo = "MeowZWR/XivMediaPlayer";
        private const string NativeDepsTag = "native-dependencies";
        private const string CefVersion = NativeDependencyVersions.CefSharpVersion;
        private const string LibVlcVersion = NativeDependencyVersions.LibVlcVersion;
        private const string CefAssetName = "cef-" + CefVersion + ".zip";
        private const string LibVlcAssetName = "libvlc-" + LibVlcVersion + ".zip";
        private const string VersionStampFileName = ".version";

        private readonly IPluginLog _pluginLog;

        public bool IsReady { get; private set; }
        public bool IsDownloading { get; private set; }
        public float DownloadProgress { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public bool HasError { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;

        public string DependenciesDir { get; private set; }

        public DependencyManager(string configDir, string pluginDir, string version, IPluginLog pluginLog)
        {
            _ = version; // kept for call-site compatibility; native deps use fixed release assets
            _pluginLog = pluginLog;
            Status = Loc.T("Deps.Initializing");

            // Check if dependencies exist in the plugin folder (e.g. for local developer compiles)
            if (Directory.Exists(Path.Combine(pluginDir, "cef")) && Directory.Exists(Path.Combine(pluginDir, "libvlc")))
            {
                DependenciesDir = pluginDir;
                CheckDependencies();
                if (IsReady) Status = Loc.T("Deps.ReadyLocal");
            }
            else
            {
                DependenciesDir = Path.Combine(configDir, "Dependencies");
                CheckDependencies();
            }
        }

        private string CefDir => Path.Combine(DependenciesDir, "cef");
        private string LibVlcDir => Path.Combine(DependenciesDir, "libvlc");
        private string CefDllPath => Path.Combine(CefDir, "libcef.dll");
        private string LibVlcDllPath => Path.Combine(LibVlcDir, "win-x64", "libvlc.dll");

        private bool HasCef => File.Exists(CefDllPath);
        private bool HasLibVlc => File.Exists(LibVlcDllPath);

        // Missing .version never forces re-download; only an existing mismatched stamp does (future NuGet bumps).
        private bool NeedsCefDownload => !HasCef || HasMismatchedVersionStamp(CefDir, CefVersion);
        private bool NeedsLibVlcDownload => !HasLibVlc || HasMismatchedVersionStamp(LibVlcDir, LibVlcVersion);

        private void CheckDependencies()
        {
            // Ready when main DLLs exist. Backfill .version for legacy installs without re-downloading.
            if (HasCef)
                EnsureVersionStamp(CefDir, CefVersion);
            if (HasLibVlc)
                EnsureVersionStamp(LibVlcDir, LibVlcVersion);

            if (HasCef && HasLibVlc && !NeedsCefDownload && !NeedsLibVlcDownload)
            {
                IsReady = true;
                Status = Loc.T("Deps.Ready");
            }
            else
            {
                IsReady = false;
                Status = Loc.T("Deps.Missing");
            }

            string ffmpegPath = Path.Combine(DependenciesDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath) && !IsDownloading)
            {
                _pluginLog.Information("ffmpeg.exe not found! Auto-downloading...");
                _ = Task.Run(async () => await DownloadFFmpegAsync());
            }
        }

        private static bool HasMismatchedVersionStamp(string componentDir, string expectedVersion)
        {
            string stampPath = Path.Combine(componentDir, VersionStampFileName);
            if (!File.Exists(stampPath))
                return false;

            try
            {
                string actual = File.ReadAllText(stampPath).Trim();
                return !string.Equals(actual, expectedVersion, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureVersionStamp(string componentDir, string expectedVersion)
        {
            string stampPath = Path.Combine(componentDir, VersionStampFileName);
            if (File.Exists(stampPath))
                return;

            try
            {
                Directory.CreateDirectory(componentDir);
                File.WriteAllText(stampPath, expectedVersion);
            }
            catch
            {
                // Non-fatal: stamp is only for future version bumps.
            }
        }

        private static void WriteVersionStamp(string componentDir, string version)
        {
            Directory.CreateDirectory(componentDir);
            File.WriteAllText(Path.Combine(componentDir, VersionStampFileName), version);
        }

        public async Task DownloadDependenciesAsync()
        {
            if (IsDownloading || IsReady) return;

            IsDownloading = true;
            HasError = false;
            DownloadProgress = 0f;
            Status = Loc.T("Deps.StartingDownload");

            try
            {
                Directory.CreateDirectory(DependenciesDir);

                var missing = new List<(string Name, string Asset, string Version, string ExtractHint)>();
                if (NeedsCefDownload)
                    missing.Add(("CEF", CefAssetName, CefVersion, "cef"));
                if (NeedsLibVlcDownload)
                    missing.Add(("LibVLC", LibVlcAssetName, LibVlcVersion, "libvlc"));

                if (missing.Count == 0)
                {
                    IsReady = true;
                    Status = Loc.T("Deps.Installed");
                    return;
                }

                int total = missing.Count;
                for (int i = 0; i < missing.Count; i++)
                {
                    var component = missing[i];
                    int index = i + 1;
                    float baseProgress = (float)i / total;

                    string githubUrl =
                        $"https://github.com/{NativeDepsRepo}/releases/download/{NativeDepsTag}/{component.Asset}";
                    string zipPath = Path.Combine(DependenciesDir, component.Asset);

                    Status = Loc.T("Deps.DownloadingComponent", component.Name, index, total);
                    _pluginLog.Information($"Downloading {component.Name} from: https://meowrs.com/{githubUrl}");

                    bool success = await TryDownloadDependencies(
                        $"https://meowrs.com/{githubUrl}",
                        zipPath,
                        baseProgress,
                        total,
                        component.Name,
                        index);

                    if (!success)
                    {
                        _pluginLog.Information($"Downloading {component.Name} from: {githubUrl}");
                        success = await TryDownloadDependencies(
                            githubUrl,
                            zipPath,
                            baseProgress,
                            total,
                            component.Name,
                            index);
                    }

                    if (!success)
                    {
                        throw new Exception(
                            $"Failed to download {component.Name} ({component.Asset}). Tried meowrs.com acceleration and direct GitHub.");
                    }

                    Status = Loc.T("Deps.ExtractingComponent", component.Name, index, total);
                    _pluginLog.Information($"Extracting {component.Asset}...");

                    await Task.Run(() => ExtractComponentZip(zipPath, component.ExtractHint, component.Version));

                    if (File.Exists(zipPath))
                        File.Delete(zipPath);

                    DownloadProgress = (float)index / total;
                }

                if (NeedsCefDownload || NeedsLibVlcDownload)
                    throw new Exception("Dependencies extracted but required native libraries are still missing.");

                IsReady = true;
                Status = Loc.T("Deps.Installed");
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
                Status = Loc.T("Deps.DownloadFailed");
                _pluginLog.Error(ex, "Failed to download media dependencies.");
            }
            finally
            {
                IsDownloading = false;
            }

            if (!File.Exists(Path.Combine(DependenciesDir, "ffmpeg.exe")))
            {
                await DownloadFFmpegAsync();
            }
        }

        private void ExtractComponentZip(string zipPath, string componentFolderName, string version)
        {
            string componentDir = Path.Combine(DependenciesDir, componentFolderName);

            try
            {
                if (Directory.Exists(componentDir))
                    Directory.Delete(componentDir, true);

                ZipFile.ExtractToDirectory(zipPath, DependenciesDir, true);
                WriteVersionStamp(componentDir, version);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
            {
                // DLLs locked after /xlplugins reload — treat as already installed.
                _pluginLog.Warning(
                    $"{componentFolderName} files are locked by the process. Skipping extraction and assuming existing files are valid.");
                if (Directory.Exists(componentDir))
                    EnsureVersionStamp(componentDir, version);
            }
        }

        private async Task<bool> TryDownloadDependencies(
            string url,
            string zipPath,
            float baseProgress,
            int totalComponents,
            string componentName,
            int componentIndex)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromHours(2);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("XivMediaPlayer-Plugin");

                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _pluginLog.Warning($"URL returned {(int)response.StatusCode} {response.ReasonPhrase}: {url}");
                            return false;
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            var totalRead = 0L;
                            var isMoreToRead = true;

                            do
                            {
                                var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fileStream.WriteAsync(buffer, 0, read);
                                    totalRead += read;

                                    float fileProgress = canReportProgress ? (float)totalRead / totalBytes : 0f;
                                    DownloadProgress = Math.Min(1f, baseProgress + fileProgress / totalComponents);

                                    if (canReportProgress)
                                    {
                                        Status = Loc.T(
                                            "Deps.DownloadingComponentProgress",
                                            componentName,
                                            componentIndex,
                                            totalComponents,
                                            totalRead / 1024 / 1024,
                                            totalBytes / 1024 / 1024);
                                    }
                                    else
                                    {
                                        Status = Loc.T(
                                            "Deps.DownloadingComponentUnknownSize",
                                            componentName,
                                            componentIndex,
                                            totalComponents,
                                            totalRead / 1024 / 1024);
                                    }
                                }
                            } while (isMoreToRead);
                        }
                    }
                }
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        private async Task DownloadFFmpegAsync()
        {
            try
            {
                Status = Loc.T("Deps.DownloadingFFmpeg");
                _pluginLog.Information("Downloading FFmpeg...");
                string zipPath = Path.Combine(DependenciesDir, "ffmpeg.zip");
                string url = "https://github.com/ffbinaries/ffbinaries-prebuilt/releases/download/v4.4.1/ffmpeg-4.4.1-win-64.zip";

                using (var client = new HttpClient())
                using (var response = await client.GetAsync(url))
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                {
                    await stream.CopyToAsync(fileStream);
                }

                Status = Loc.T("Deps.ExtractingFFmpeg");
                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(zipPath, DependenciesDir, true);
                    File.Delete(zipPath);
                });

                CheckDependencies();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to download FFmpeg.");
            }
        }
    }
}
