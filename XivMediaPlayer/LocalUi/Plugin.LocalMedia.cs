using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XivMediaPlayer.LocalUi;
using XivMediaPlayer.Networking.Models;

namespace XivMediaPlayer
{
    public sealed partial class Plugin
    {
        private bool _suppressServerSync;
        private LocalMediaWindow? _localMediaWindow;
        private string _resumeAfterStopUrl = "";
        private string _resumeAfterStopTitle = "";
        private string? _persistedPlaylistKey;

        private void RestoreLocalPlaylist()
        {
            _mediaQueue.Clear();
            if (_config.LocalPlaylist != null)
            {
                foreach (string url in _config.LocalPlaylist)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                        _mediaQueue.Enqueue(url.Trim());
                }
            }
            _persistedPlaylistKey = string.Join("\n", _mediaQueue);
        }

        private void PersistLocalPlaylistIfChanged()
        {
            string key = string.Join("\n", _mediaQueue);
            if (key == _persistedPlaylistKey) return;
            _persistedPlaylistKey = key;
            _config.LocalPlaylist = _mediaQueue.ToList();
            _config.Save();
        }

        public bool IsRoomSyncPaused => _suppressServerSync;
        public string CurrentMediaTitle =>
            !string.IsNullOrEmpty(_currentMediaTitle) ? _currentMediaTitle : _resumeAfterStopTitle;

        public string RememberedMediaUrl =>
            !string.IsNullOrEmpty(_lastStreamURL) ? _lastStreamURL : _resumeAfterStopUrl;

        private void RegisterLocalMediaWindow()
        {
            _localMediaWindow = new LocalMediaWindow(this);
            _windowSystem.AddWindow(_localMediaWindow);
        }

        public void ToggleLocalMediaWindow()
        {
            if (_localMediaWindow == null) return;
            _localMediaWindow.IsOpen = !_localMediaWindow.IsOpen;
        }

        public string[] CopyMediaQueue() => _mediaQueue.ToArray();

        internal void PrintPlain(string text) => _chat.Print(text);

        internal void PrintPlainError(string text) => _chat.PrintError(text);

        private long _loopRestartTick;

        private bool TryRestartForLoop(string reason)
        {
            if (!_config.LoopEnabled || reason == "Emulation" || _lastStreamIsLive)
                return false;
            if (LocalMediaPaths.IsMissing(_lastStreamURL))
                return false;

            long now = Environment.TickCount64;
            if (now - _loopRestartTick < 1500)
                return true;
            _loopRestartTick = now;

            string replayUrl = _lastStreamURL;
            EnqueueFrameworkAction(() =>
            {
                if (_disposed || !_config.LoopEnabled) return;

                var active = _mediaManager?.ActiveStream;
                if (active != null && !string.IsNullOrEmpty(active.SoundPath))
                {
                    PrintVerbose("[Media Player] Looping current media.");
                    SeekToMs(0, syncToServer: true, userInitiated: false);
                    return;
                }

                if (!string.IsNullOrEmpty(replayUrl) && _playerObject != null)
                    PlayRouted(replayUrl, CurrentAudioSource, 0);
            });
            return true;
        }

        private bool AllowPlaybackDuringLocalSession(string url, bool isAutoSync)
        {
            if (LocalMediaPaths.IsLocalFile(url) || !_suppressServerSync) return true;
            if (isAutoSync) return false;
            if (!CanControlCurrentTvPlayback()) return true;
            _suppressServerSync = false;
            return true;
        }

        private void ArmLocalFileSyncBlock(string url)
        {
            if (LocalMediaPaths.IsLocalFile(url))
                _suppressServerSync = true;
        }

        private void ApplyLocalFileLabels(string url)
        {
            if (!_suppressServerSync) return;
            _currentMediaTitle = Path.GetFileName(LocalMediaPaths.Normalize(url));
            _currentStreamer = "Local File";
        }

        public void PlayMediaUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim().Trim('"');
            if (LocalMediaPaths.IsLocalFile(url))
            {
                PlayLocalFile(url);
                return;
            }

            if (_playerObject == null)
            {
                PrintPlainError("播放器还没准备好。请先进入游戏。");
                return;
            }

            _lastStreamObject = CurrentAudioSource;
            PlayRouted(url, CurrentAudioSource);
        }

        public void PlayLocalFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = LocalMediaPaths.Normalize(path);
            if (!File.Exists(path))
            {
                bool removed = CanControlCurrentTvPlayback() && RemoveQueuedPath(path);
                if (removed && _mediaQueue.Count > 0 && _playerObject != null)
                {
                    PrintPlainError("这个本地文件已经不存在，改为播放下一项。");
                    PlayNext();
                    return;
                }

                PrintPlainError(removed
                    ? "这个本地文件已经不存在，已从播放列表移除。"
                    : "找不到这个本地视频文件。");
                return;
            }

            if (_playerObject == null)
            {
                PrintPlainError("播放器还没准备好。请先进入游戏。");
                return;
            }

            if (!CanControlCurrentTvPlayback())
            {
                PrintPlainError("这台电视已被房主锁定，无法播放。");
                return;
            }

            _lastStreamObject = CurrentAudioSource;
            PlayRouted(path, CurrentAudioSource);
            if (_suppressServerSync)
            {
                PrintPlain("正在播放本地文件。房间同步已暂停，停止播放或改播网络链接后会恢复。");
            }
        }

        public void EnqueueMedia(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!CanControlCurrentTvPlayback())
            {
                PrintPlainError("这台电视已被房主锁定，无法播放。");
                return;
            }

            url = url.Trim().Trim('"');
            if (LocalMediaPaths.IsLocalFile(url))
            {
                url = LocalMediaPaths.Normalize(url);
                if (!File.Exists(url))
                {
                    PrintPlainError("找不到这个本地视频文件。");
                    return;
                }
            }

            _mediaQueue.Enqueue(url);
            PrintVerboseFormat("[Media Player] Queued ({0}): {1}", _mediaQueue.Count, url);

            if (!_suppressServerSync && !LocalMediaPaths.IsLocalFile(url))
                _ = PushMediaToServerAsync(isBackgroundSync: false);
        }

        internal void RememberStoppedMedia()
        {
            if (string.IsNullOrEmpty(_lastStreamURL)) return;
            _resumeAfterStopUrl = _lastStreamURL;
            _resumeAfterStopTitle = string.IsNullOrEmpty(_currentMediaTitle)
                ? _lastStreamURL
                : _currentMediaTitle;
        }

        internal bool TryReplayStoppedMedia()
        {
            string url = !string.IsNullOrEmpty(_lastStreamURL) ? _lastStreamURL : _resumeAfterStopUrl;
            if (string.IsNullOrEmpty(url)) return false;
            if (LocalMediaPaths.IsMissing(url))
            {
                _resumeAfterStopUrl = "";
                _resumeAfterStopTitle = "";
                PrintPlainError("上次播放的本地文件已经不存在。");
                if (_mediaQueue.Count > 0)
                    PlayNext();
                return true;
            }
            if (!CanControlCurrentTvPlayback()) return false;
            _isIntentionallyPaused = false;
            PlayMediaUrl(url);
            return true;
        }

        private bool TryDequeuePlayable(out string url, out int skipped)
        {
            url = "";
            skipped = 0;
            while (_mediaQueue.Count > 0)
            {
                string candidate;
                if (_config.ShuffleEnabled && _mediaQueue.Count > 1)
                {
                    var list = _mediaQueue.ToList();
                    int idx = _shuffleRandom.Next(list.Count);
                    candidate = list[idx];
                    list.RemoveAt(idx);
                    _mediaQueue = new Queue<string>(list);
                }
                else
                {
                    candidate = _mediaQueue.Dequeue();
                }

                if (LocalMediaPaths.IsMissing(candidate))
                {
                    skipped++;
                    continue;
                }

                url = candidate;
                return true;
            }

            return false;
        }

        private bool TryPopPlayableHistory(out string url, out int skipped)
        {
            url = "";
            skipped = 0;
            while (_mediaHistory.Count > 0)
            {
                string candidate = _mediaHistory.Pop();
                if (LocalMediaPaths.IsMissing(candidate))
                {
                    skipped++;
                    continue;
                }

                url = candidate;
                return true;
            }

            return false;
        }

        private void ReportSkippedMissingFiles(int skipped)
        {
            if (skipped <= 0) return;
            PrintPlainError($"有 {skipped} 个本地文件已不存在，已跳过。");
        }

        private bool RemoveQueuedPath(string path)
        {
            string key = LocalMediaPaths.Normalize(path).Replace('/', '\\');
            var list = _mediaQueue.ToList();
            int removed = list.RemoveAll(item =>
                LocalMediaPaths.IsLocalFile(item)
                && string.Equals(
                    LocalMediaPaths.Normalize(item).Replace('/', '\\'),
                    key,
                    StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            _mediaQueue = new Queue<string>(list);
            return true;
        }

        public void RemoveQueuedMedia(int index)
        {
            if (!CanControlCurrentTvPlayback()) return;
            var list = _mediaQueue.ToList();
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            _mediaQueue = new Queue<string>(list);
            if (!_suppressServerSync)
                _ = PushMediaToServerAsync(isBackgroundSync: false);
        }

        public void ClearMediaQueue()
        {
            if (!CanControlCurrentTvPlayback()) return;
            _mediaQueue.Clear();
            if (!_suppressServerSync)
                _ = PushMediaToServerAsync(isBackgroundSync: false);
        }

        public void ToggleCurrentTvLock()
        {
            if (CurrentTvPlacement != null && CurrentTvPlacement.OwnerId == _config.OwnerId)
            {
                CurrentTvPlacement.IsLocked = !CurrentTvPlacement.IsLocked;
                if (!string.IsNullOrEmpty(LocationKey))
                {
                    _screenSettingsWindow.RegisterTvAsync(LocationKey);
                    PrintChat(CurrentTvPlacement.IsLocked
                        ? "[Media Player] TV is now Locked."
                        : "[Media Player] TV is now Unlocked.");
                }
            }
            else if (CurrentTvPlacement == null)
            {
                CurrentTvPlacement = new TvPlacement { OwnerId = _config.OwnerId, IsLocked = false };
                if (!string.IsNullOrEmpty(LocationKey))
                    _screenSettingsWindow.RegisterTvAsync(LocationKey);
                PrintChat("[Media Player] TV registered and Unlocked.");
            }
            else
            {
                PrintChat("[Media Player] You do not own this TV.");
            }
        }
    }
}
