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
        private bool _syncDisplayOnly;
        private string _syncDisplayLocationKey = "";
        private readonly HashSet<string> _syncDisplayUrls = new(StringComparer.OrdinalIgnoreCase);
        private LocalMediaWindow? _localMediaWindow;
        private string _resumeAfterStopUrl = "";
        private string _resumeAfterStopTitle = "";
        private long _resumeAfterStopTimecodeMs;
        private bool _resumeAfterStopIsLive;
        private int _localPlaylistIndex = -1;
        private int _syncPlaylistIndex = -1;

        private void ApplySavedPlaybackMode()
        {
            _config.LocalPlaylist ??= new List<string>();
            _config.RegisteredPlots ??= new List<RegisteredPlot>();
            if (_config.LocalPlaybackMode)
            {
                _suppressServerSync = true;
                HydrateLocalPlaybackMemory();
                return;
            }

            HydrateSyncPlaybackMemory();
        }

        internal bool GameUiHidden => _gameGui.GameUiHidden;

        public bool IsLocalPlaybackMode => _config.LocalPlaybackMode;
        public bool IsRoomSyncPaused => IsLocalPlaybackMode;

        public void SetLocalPlaybackMode(bool enabled)
        {
            if (_config.LocalPlaybackMode == enabled) return;
            _config.LocalPlaybackMode = enabled;
            _config.SaveLocal();
            if (enabled)
            {
                _suppressServerSync = true;
                if (_mediaManager?.ActiveStream == null && string.IsNullOrEmpty(_lastStreamURL))
                {
                    ClearSyncDisplay(forgetBookmark: false);
                    HydrateLocalPlaybackMemory();
                }
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
            ClearResumeDisplay();
            HydrateSyncPlaybackMemory();
            _ = FetchMediaFromServerAsync();
            PrintPlain("已切换到同步模式。本地视频不能加入播放列表。");
        }

        internal void EnterSyncModeForScreenPaste(string url)
        {
            if (LocalMediaPaths.IsLocalFile(url)) return;
            if (!_config.LocalPlaybackMode && !_suppressServerSync) return;
            _config.LocalPlaybackMode = false;
            _suppressServerSync = false;
            _config.SaveLocal();
            PrintPlain("已切换到同步模式。");
        }
        public string CurrentMediaTitle =>
            !string.IsNullOrEmpty(_currentMediaTitle) ? _currentMediaTitle : _resumeAfterStopTitle;

        public string PlayingMediaTitle => _currentMediaTitle;

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

        private string[] CopyUpcomingSyncPlaylist()
        {
            var list = _mediaQueue.ToList();
            if (list.Count == 0) return Array.Empty<string>();
            int index = ResolveSyncPlaylistIndex(_lastStreamURL ?? "", _syncPlaylistIndex);
            if (index < 0) return list.ToArray();
            if (index >= list.Count - 1) return Array.Empty<string>();
            var upcoming = new string[list.Count - index - 1];
            list.CopyTo(index + 1, upcoming, 0, upcoming.Length);
            return upcoming;
        }

        public int CurrentLocalPlaylistIndex
        {
            get
            {
                if (string.IsNullOrEmpty(_lastStreamURL))
                    return -1;
                if (IsLocalPlaybackMode)
                    return ResolveLocalPlaylistIndex(_lastStreamURL, _localPlaylistIndex);
                return ResolveSyncPlaylistIndex(_lastStreamURL, _syncPlaylistIndex);
            }
        }

        public void PlayQueuedMedia(int index)
        {
            if (IsLocalPlaybackMode)
            {
                var local = _config.LocalPlaylist;
                if (local == null || index < 0 || index >= local.Count) return;
                _localPlaylistIndex = index;
                PlayMediaUrl(local[index]);
                return;
            }

            if (!CanControlCurrentTvPlayback()) return;
            var list = _mediaQueue.ToList();
            if (index < 0 || index >= list.Count) return;
            _syncPlaylistIndex = index;
            PlayMediaUrl(list[index]);
        }

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

        public void PlayMediaUrl(string url, int startTimeMs = 0)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim().Trim('"');
            if (LocalMediaPaths.IsLocalFile(url))
            {
                PlayLocalFile(url, startTimeMs);
                return;
            }

            NoteLocalPlaylistIndex(url);

            if (_playerObject == null)
            {
                PrintPlainError("播放器还没准备好。请先进入游戏。");
                return;
            }

            _lastStreamObject = CurrentAudioSource;
            PlayRouted(url, CurrentAudioSource, startTimeMs);
        }

        public void PlayLocalFile(string path, int startTimeMs = 0)
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
                    PrintPlainError("此文件已经不存在，改为播放下一项。");
                    if (_localPlaylistIndex < 0 || _localPlaylistIndex >= remaining)
                        _localPlaylistIndex = 0;
                    PlayMediaUrl(_config.LocalPlaylist![_localPlaylistIndex]);
                    return;
                }

                PrintPlainError(removed
                    ? "此文件已经不存在，已从播放列表移除。"
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
            PlayRouted(path, CurrentAudioSource, startTimeMs);
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
                _config.SaveLocal();
                return;
            }

            ReleaseDisplacedSyncDisplay(url);
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
            _resumeAfterStopIsLive = _lastStreamIsLive;
            long time = _mediaManager?.ActiveStream?.Time ?? _resumeAfterStopTimecodeMs;
            _resumeAfterStopTimecodeMs = time < 0 ? 0 : time;
        }

        internal bool TryReplayStoppedMedia()
        {
            if (_syncDisplayOnly && !IsLocalPlaybackMode)
                return TryPlayDisplayedSyncMedia();

            string url = !string.IsNullOrEmpty(_lastStreamURL) ? _lastStreamURL : _resumeAfterStopUrl;
            if (string.IsNullOrEmpty(url)) return false;
            if (LocalMediaPaths.IsMissing(url))
            {
                _resumeAfterStopUrl = "";
                _resumeAfterStopTitle = "";
                _resumeAfterStopTimecodeMs = 0;
                _resumeAfterStopIsLive = false;
                PrintPlainError("上次播放的本地文件已经不存在。");
                if (!IsLocalPlaybackMode && _mediaQueue.Count > 0)
                    PlayNext();
                return true;
            }
            if (!CanControlCurrentTvPlayback()) return false;
            _isIntentionallyPaused = false;
            bool live = _lastStreamIsLive || _resumeAfterStopIsLive;
            int start = live ? 0 : ClampStartTime(_resumeAfterStopTimecodeMs);
            PlayMediaUrl(url, start);
            return true;
        }

        private void PlayAdjacentSyncItem(int delta)
        {
            var list = _mediaQueue.ToList();
            if (list.Count == 0 || _playerObject == null) return;

            if (delta > 0 && _config.ShuffleEnabled && list.Count > 1)
            {
                for (int attempt = 0; attempt < list.Count; attempt++)
                {
                    int index = _shuffleRandom.Next(list.Count);
                    if (index == _syncPlaylistIndex || LocalMediaPaths.IsMissing(list[index])) continue;
                    _syncPlaylistIndex = index;
                    PlayMediaUrl(list[index]);
                    return;
                }
            }

            int start = _syncPlaylistIndex;
            if (start < 0)
            {
                if (delta < 0) return;
                start = -1;
            }
            int next = start + delta;
            int guard = list.Count;
            while (guard-- > 0)
            {
                if (next < 0 || next >= list.Count)
                    return;
                if (!LocalMediaPaths.IsMissing(list[next]))
                {
                    _syncPlaylistIndex = next;
                    PlayMediaUrl(list[next]);
                    return;
                }

                next += delta == 0 ? 1 : delta;
            }
        }

        private void HydrateLocalPlaybackMemory()
        {
            string url = _config.LastPlayedUrl ?? "";
            if (string.IsNullOrWhiteSpace(url)) return;
            _resumeAfterStopUrl = url;
            _resumeAfterStopTitle = string.IsNullOrWhiteSpace(_config.LastPlayedTitle) ? url : _config.LastPlayedTitle;
            _resumeAfterStopTimecodeMs = Math.Max(0, _config.LastPlayedTimecodeMs);
            _resumeAfterStopIsLive = _config.LastPlayedIsLive;
            _localPlaylistIndex = ResolveLocalPlaylistIndex(url, _config.LastPlayedPlaylistIndex);
        }

        private void HydrateSyncPlaybackMemory()
        {
            if (IsLocalPlaybackMode) return;
            string key = _config.LastSyncLocationKey ?? "";
            if (string.IsNullOrEmpty(key)) return;
            if (!_config.RoomMediaStates.TryGetValue(key, out var state)) return;
            ShowRememberedSyncState(key, state);
        }

        private void ShowRememberedSyncState(string key, RoomMediaState state)
        {
            if (IsLocalPlaybackMode || state == null) return;
            if (_mediaManager?.ActiveStream != null || _isResolvingMedia) return;
            if (string.IsNullOrWhiteSpace(state.CurrentUrl) && (state.Playlist == null || state.Playlist.Count == 0))
                return;

            _mediaQueue.Clear();
            _syncPlaylistIndex = -1;
            _syncDisplayUrls.Clear();
            if (state.Playlist != null)
            {
                foreach (string item in state.Playlist)
                {
                    if (string.IsNullOrWhiteSpace(item)) continue;
                    _mediaQueue.Enqueue(item);
                    _syncDisplayUrls.Add(CleanUrl(item));
                }
            }

            _syncDisplayOnly = true;
            _syncDisplayLocationKey = key ?? "";
            _resumeAfterStopUrl = state.CurrentUrl ?? "";
            _resumeAfterStopTimecodeMs = Math.Max(0, state.TimecodeMs);
            _resumeAfterStopIsLive = false;
            _resumeAfterStopTitle = DescribeRememberedMedia(_resumeAfterStopUrl);
            if (!string.IsNullOrWhiteSpace(_resumeAfterStopUrl))
                _syncDisplayUrls.Add(CleanUrl(_resumeAfterStopUrl));
            _lastStreamURL = "";
            _currentMediaTitle = "";
        }

        private void RememberSyncLocationKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.Equals(_config.LastSyncLocationKey, key, StringComparison.Ordinal)) return;
            _config.LastSyncLocationKey = key;
            _config.SaveLocal();
        }

        private bool IsCurrentSyncDisplayLocation()
        {
            if (string.IsNullOrEmpty(_syncDisplayLocationKey)) return false;
            string? here = CurrentTvPlacement?.LocationKey;
            if (string.IsNullOrEmpty(here))
                here = GetLocationKey();
            return string.Equals(here, _syncDisplayLocationKey, StringComparison.Ordinal);
        }

        private bool IsDisplayedSyncUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            url = CleanUrl(url);
            if (string.Equals(url, CleanUrl(_resumeAfterStopUrl ?? ""), StringComparison.OrdinalIgnoreCase)) return true;
            return _syncDisplayUrls.Contains(url);
        }

        private bool BlockDisplacedSyncReplay(string url)
        {
            if (!_syncDisplayOnly) return false;
            if (IsCurrentSyncDisplayLocation()) return false;

            if (IsDisplayedSyncUrl(url))
            {
                PrintPlainError("现在不在上次播放的地点，不能从这里续播。");
                return true;
            }

            ReleaseDisplacedSyncDisplay(url);
            return false;
        }

        private void ReleaseDisplacedSyncDisplay(string url)
        {
            if (!_syncDisplayOnly || IsCurrentSyncDisplayLocation() || IsDisplayedSyncUrl(url)) return;
            _syncDisplayOnly = false;
            _syncDisplayLocationKey = "";
            _syncDisplayUrls.Clear();
            _mediaQueue.Clear();
            _syncPlaylistIndex = -1;
            ClearResumeDisplay();
        }

        private void PromoteSyncDisplay()
        {
            if (!_syncDisplayOnly) return;
            _syncDisplayOnly = false;
            _syncDisplayUrls.Clear();
        }

        private bool TryPlayDisplayedSyncMedia()
        {
            string url = _resumeAfterStopUrl;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!IsCurrentSyncDisplayLocation())
            {
                PrintPlainError("现在不在上次播放的地点，不能从这里续播。");
                return true;
            }
            if (!CanControlCurrentTvPlayback()) return false;
            if (_playerObject == null)
            {
                PrintPlainError("播放器还没准备好。请先进入游戏。");
                return true;
            }

            int start = ClampStartTime(_resumeAfterStopTimecodeMs);
            _syncDisplayOnly = false;
            _syncDisplayUrls.Clear();
            _isIntentionallyPaused = false;
            _lastStreamObject = CurrentAudioSource;
            PlayRouted(url, CurrentAudioSource, start, isAutoSync: false);
            return true;
        }

        private void ClearSyncDisplay(bool forgetBookmark)
        {
            _syncDisplayOnly = false;
            _syncDisplayLocationKey = "";
            _syncDisplayUrls.Clear();
            _mediaQueue.Clear();
            _syncPlaylistIndex = -1;
            ClearResumeDisplay();
            if (!forgetBookmark || string.IsNullOrEmpty(_config.LastSyncLocationKey)) return;
            _config.LastSyncLocationKey = "";
            _config.SaveLocal();
        }

        private void ClearResumeDisplay()
        {
            _resumeAfterStopUrl = "";
            _resumeAfterStopTitle = "";
            _resumeAfterStopTimecodeMs = 0;
            _resumeAfterStopIsLive = false;
        }

        private void NoteLocalPlaybackSnapshot(long? timecodeMs = null)
        {
            if (!IsLocalPlaybackMode || string.IsNullOrEmpty(_lastStreamURL)) return;
            if (_lastStreamURL.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)) return;

            long time = _lastStreamIsLive
                ? 0
                : timecodeMs ?? _mediaManager?.ActiveStream?.Time ?? _resumeAfterStopTimecodeMs;
            if (time < 0) time = 0;
            string title = _currentMediaTitle;
            if (string.IsNullOrWhiteSpace(title) || title == "Loading...")
                title = _lastStreamURL;
            int index = ResolveLocalPlaylistIndex(_lastStreamURL, _localPlaylistIndex);

            if (string.Equals(_config.LastPlayedUrl, _lastStreamURL, StringComparison.Ordinal)
                && string.Equals(_config.LastPlayedTitle, title, StringComparison.Ordinal)
                && _config.LastPlayedTimecodeMs == time
                && _config.LastPlayedPlaylistIndex == index
                && _config.LastPlayedIsLive == _lastStreamIsLive)
            {
                return;
            }

            _config.LastPlayedUrl = _lastStreamURL;
            _config.LastPlayedTitle = title;
            _config.LastPlayedTimecodeMs = time;
            _config.LastPlayedPlaylistIndex = index;
            _config.LastPlayedIsLive = _lastStreamIsLive;
            _config.SaveLocal();
        }

        private void ClearPersistedLocalPlayback()
        {
            if (string.IsNullOrEmpty(_config.LastPlayedUrl)
                && string.IsNullOrEmpty(_config.LastPlayedTitle)
                && _config.LastPlayedTimecodeMs == 0
                && _config.LastPlayedPlaylistIndex < 0
                && !_config.LastPlayedIsLive)
            {
                return;
            }

            _config.LastPlayedUrl = "";
            _config.LastPlayedTitle = "";
            _config.LastPlayedTimecodeMs = 0;
            _config.LastPlayedPlaylistIndex = -1;
            _config.LastPlayedIsLive = false;
            _config.SaveLocal();
        }

        private void ForgetFinishedLocalPlayback(string endedUrl)
        {
            ClearPersistedLocalPlayback();
            if (string.IsNullOrEmpty(endedUrl)) return;
            if (!string.Equals(_resumeAfterStopUrl, endedUrl, StringComparison.OrdinalIgnoreCase)) return;
            ClearResumeDisplay();
        }

        private int ResolveLocalPlaylistIndex(string url, int savedIndex)
        {
            var list = _config.LocalPlaylist;
            if (list != null
                && savedIndex >= 0
                && savedIndex < list.Count
                && string.Equals(PlaylistKey(list[savedIndex]), PlaylistKey(url), StringComparison.OrdinalIgnoreCase))
            {
                return savedIndex;
            }

            return FindLocalPlaylistIndex(url);
        }

        private string DescribeRememberedMedia(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            if (_config.WatchHistory != null
                && _config.WatchHistory.TryGetValue(url, out var entry)
                && !string.IsNullOrWhiteSpace(entry.Title))
            {
                return entry.Title;
            }

            if (LocalMediaPaths.IsLocalFile(url))
                return Path.GetFileName(LocalMediaPaths.Normalize(url));
            return url;
        }

        private static int ClampStartTime(long timecodeMs)
        {
            if (timecodeMs <= 0) return 0;
            if (timecodeMs >= int.MaxValue) return int.MaxValue;
            return (int)timecodeMs;
        }

        private void NoteLocalPlaylistIndex(string url)
        {
            if (!IsLocalPlaybackMode) return;
            var list = _config.LocalPlaylist;
            if (list != null
                && _localPlaylistIndex >= 0
                && _localPlaylistIndex < list.Count
                && string.Equals(PlaylistKey(list[_localPlaylistIndex]), PlaylistKey(url), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _localPlaylistIndex = FindLocalPlaylistIndex(url);
        }

        private void NoteSyncPlaylistIndex(string url)
        {
            if (IsLocalPlaybackMode) return;
            var list = _mediaQueue.ToList();
            if (_syncPlaylistIndex >= 0
                && _syncPlaylistIndex < list.Count
                && string.Equals(PlaylistKey(list[_syncPlaylistIndex]), PlaylistKey(url), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _syncPlaylistIndex = FindSyncPlaylistIndex(list, url);
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

        private int ResolveSyncPlaylistIndex(string url, int savedIndex)
        {
            var list = _mediaQueue.ToList();
            if (savedIndex >= 0
                && savedIndex < list.Count
                && string.Equals(PlaylistKey(list[savedIndex]), PlaylistKey(url), StringComparison.OrdinalIgnoreCase))
            {
                return savedIndex;
            }

            return FindSyncPlaylistIndex(list, url);
        }

        private static int FindSyncPlaylistIndex(List<string> list, string url)
        {
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
                if (delta > 0 && next >= list.Count)
                    next = 0;
                else if (next < 0 || next >= list.Count)
                    return;
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
                _config.SaveLocal();
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
                _config.SaveLocal();
                return;
            }

            if (!CanControlCurrentTvPlayback()) return;
            var list = _mediaQueue.ToList();
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            if (_syncPlaylistIndex == index) _syncPlaylistIndex = -1;
            else if (_syncPlaylistIndex > index) _syncPlaylistIndex--;
            _mediaQueue = new Queue<string>(list);
            _ = PushMediaToServerAsync(isBackgroundSync: false);
        }

        public void ClearMediaQueue()
        {
            if (IsLocalPlaybackMode)
            {
                _config.LocalPlaylist = new List<string>();
                _localPlaylistIndex = -1;
                _config.SaveLocal();
                return;
            }

            if (!CanControlCurrentTvPlayback()) return;
            _mediaQueue.Clear();
            _syncPlaylistIndex = -1;
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
