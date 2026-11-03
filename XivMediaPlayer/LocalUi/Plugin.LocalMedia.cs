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
        private int _localPlaylistIndex = -1;

        private void ApplySavedPlaybackMode()
        {
            _config.LocalPlaylist ??= new List<string>();
            _config.RegisteredPlots ??= new List<RegisteredPlot>();
            if (_config.LocalPlaybackMode)
                _suppressServerSync = true;
        }

        internal bool GameUiHidden => _gameGui.GameUiHidden;

        public bool IsLocalPlaybackMode => _config.LocalPlaybackMode;
        public bool IsRoomSyncPaused => IsLocalPlaybackMode;

        public void SetLocalPlaybackMode(bool enabled)
        {
            if (_config.LocalPlaybackMode == enabled) return;
            _config.LocalPlaybackMode = enabled;
            _config.Save();
            if (enabled)
            {
                _suppressServerSync = true;
                PrintPlain("已切换到本地模式。播放不会同步到房间，播完也不会从列表移除。");
                return;
            }

            _suppressServerSync = false;
            _isLocalDj = false;
            if (!string.IsNullOrEmpty(_lastStreamURL))
            {
                _mediaManager?.StopStream();
                ResetStreamValues(false);
            }
            _ = FetchMediaFromServerAsync();
            PrintPlain("已切换到同步模式。本地视频不能加入播放列表。");
        }

        internal void EnterSyncModeForScreenPaste(string url)
        {
            if (LocalMediaPaths.IsLocalFile(url)) return;
            if (!_config.LocalPlaybackMode && !_suppressServerSync) return;
            _config.LocalPlaybackMode = false;
            _suppressServerSync = false;
            _config.Save();
            PrintPlain("已切换到同步模式。");
        }
        public string CurrentMediaTitle =>
            !string.IsNullOrEmpty(_currentMediaTitle) ? _currentMediaTitle : _resumeAfterStopTitle;

        public string RememberedMediaUrl =>
            !string.IsNullOrEmpty(_lastStreamURL) ? _lastStreamURL : _resumeAfterStopUrl;

        private void RegisterLocalMediaWindow()
        {
            _localMediaWindow = new LocalMediaWindow(this);
            _windowSystem.AddWindow(_localMediaWindow);
            _playbackEdgeWindow = new PlaybackEdgeTabWindow(this);
            _windowSystem.AddWindow(_playbackEdgeWindow);
        }

        public void ToggleLocalMediaWindow()
        {
            if (_localMediaWindow == null) return;
            if (_localMediaWindow.IsOpen)
            {
                _localMediaWindow.IsOpen = false;
                OnPlaybackPanelClosed();
            }
            else
            {
                OpenPlaybackPanel();
            }
        }

        public string[] CopyMediaQueue() =>
            IsLocalPlaybackMode
                ? (_config.LocalPlaylist ?? new List<string>()).ToArray()
                : _mediaQueue.ToArray();

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
            if (!IsLocalPlaybackMode && LocalMediaPaths.IsLocalFile(url))
            {
                if (!isAutoSync)
                    PrintPlainError("同步模式不能播放本地视频。请切换到本地模式。");
                return false;
            }

            if (IsLocalPlaybackMode)
                return !isAutoSync;

            return true;
        }

        private void ApplyLocalFileLabels(string url)
        {
            if (!LocalMediaPaths.IsLocalFile(url)) return;
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

            NoteLocalPlaylistIndex(url);

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
            if (!IsLocalPlaybackMode)
            {
                PrintPlainError("同步模式不能播放本地视频。请切换到本地模式。");
                return;
            }

            path = LocalMediaPaths.Normalize(path);
            if (!File.Exists(path))
            {
                bool removed = RemoveQueuedPath(path);
                int remaining = _config.LocalPlaylist?.Count ?? 0;
                if (removed && remaining > 0 && _playerObject != null)
                {
                    PrintPlainError("这个本地文件已经不存在，改为播放下一项。");
                    if (_localPlaylistIndex < 0 || _localPlaylistIndex >= remaining)
                        _localPlaylistIndex = 0;
                    PlayMediaUrl(_config.LocalPlaylist![_localPlaylistIndex]);
                    return;
                }

                PrintPlainError(removed
                    ? "这个本地文件已经不存在，已从播放列表移除。"
                    : "找不到这个本地视频文件。");
                return;
            }

            NoteLocalPlaylistIndex(path);

            if (_playerObject == null)
            {
                PrintPlainError("播放器还没准备好。请先进入游戏。");
                return;
            }

            _lastStreamObject = CurrentAudioSource;
            PlayRouted(path, CurrentAudioSource);
        }

        public void EnqueueMedia(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!IsLocalPlaybackMode && !CanControlCurrentTvPlayback())
            {
                PrintPlainError("这台电视已被房主锁定，无法播放。");
                return;
            }

            url = url.Trim().Trim('"');
            if (LocalMediaPaths.IsLocalFile(url))
            {
                if (!IsLocalPlaybackMode)
                {
                    PrintPlainError("同步模式不能添加本地视频。请切换到本地模式。");
                    return;
                }

                url = LocalMediaPaths.Normalize(url);
                if (!File.Exists(url))
                {
                    PrintPlainError("找不到这个本地视频文件。");
                    return;
                }
            }

            if (IsLocalPlaybackMode)
            {
                _config.LocalPlaylist ??= new List<string>();
                _config.LocalPlaylist.Add(url);
                _config.Save();
                return;
            }

            _mediaQueue.Enqueue(url);
            PrintVerboseFormat("[Media Player] Queued ({0}): {1}", _mediaQueue.Count, url);
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
                if (!IsLocalPlaybackMode && _mediaQueue.Count > 0)
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

        private void NoteLocalPlaylistIndex(string url)
        {
            if (!IsLocalPlaybackMode) return;
            _localPlaylistIndex = FindLocalPlaylistIndex(url);
        }

        private int FindLocalPlaylistIndex(string url)
        {
            var list = _config.LocalPlaylist;
            if (list == null || string.IsNullOrEmpty(url)) return -1;
            string key = PlaylistKey(url);
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(PlaylistKey(list[i]), key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private static string PlaylistKey(string value)
        {
            value = value.Trim().Trim('"');
            if (!LocalMediaPaths.IsLocalFile(value)) return value;
            return LocalMediaPaths.Normalize(value).Replace('/', '\\');
        }

        private void PlayAdjacentLocalItem(int delta)
        {
            var list = _config.LocalPlaylist;
            if (list == null || list.Count == 0 || _playerObject == null) return;

            if (delta > 0 && _config.ShuffleEnabled && list.Count > 1)
            {
                for (int attempt = 0; attempt < list.Count; attempt++)
                {
                    int index = _shuffleRandom.Next(list.Count);
                    if (index == _localPlaylistIndex || LocalMediaPaths.IsMissing(list[index])) continue;
                    _localPlaylistIndex = index;
                    PlayMediaUrl(list[index]);
                    return;
                }
            }

            int start = _localPlaylistIndex;
            if (start < 0)
                start = delta >= 0 ? -1 : list.Count;
            int next = start + delta;
            int guard = list.Count;
            while (guard-- > 0)
            {
                if (next < 0 || next >= list.Count) return;
                if (!LocalMediaPaths.IsMissing(list[next]))
                {
                    _localPlaylistIndex = next;
                    PlayMediaUrl(list[next]);
                    return;
                }

                next += delta == 0 ? 1 : delta;
            }
        }

        private bool RemoveQueuedPath(string path)
        {
            string key = LocalMediaPaths.Normalize(path).Replace('/', '\\');
            if (IsLocalPlaybackMode)
            {
                var local = _config.LocalPlaylist;
                if (local == null) return false;
                int removed = local.RemoveAll(item =>
                    LocalMediaPaths.IsLocalFile(item)
                    && string.Equals(PlaylistKey(item), key, StringComparison.OrdinalIgnoreCase));
                if (removed == 0) return false;
                _config.Save();
                return true;
            }

            var list = _mediaQueue.ToList();
            int removedSync = list.RemoveAll(item =>
                LocalMediaPaths.IsLocalFile(item)
                && string.Equals(PlaylistKey(item), key, StringComparison.OrdinalIgnoreCase));
            if (removedSync == 0) return false;
            _mediaQueue = new Queue<string>(list);
            return true;
        }

        public void RemoveQueuedMedia(int index)
        {
            if (IsLocalPlaybackMode)
            {
                var local = _config.LocalPlaylist;
                if (local == null || index < 0 || index >= local.Count) return;
                local.RemoveAt(index);
                if (_localPlaylistIndex == index) _localPlaylistIndex = -1;
                else if (_localPlaylistIndex > index) _localPlaylistIndex--;
                _config.Save();
                return;
            }

            if (!CanControlCurrentTvPlayback()) return;
            var list = _mediaQueue.ToList();
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            _mediaQueue = new Queue<string>(list);
            _ = PushMediaToServerAsync(isBackgroundSync: false);
        }

        public void ClearMediaQueue()
        {
            if (IsLocalPlaybackMode)
            {
                _config.LocalPlaylist = new List<string>();
                _localPlaylistIndex = -1;
                _config.Save();
                return;
            }

            if (!CanControlCurrentTvPlayback()) return;
            _mediaQueue.Clear();
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
