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

        public bool IsRoomSyncPaused => _suppressServerSync;
        public string CurrentMediaTitle => _currentMediaTitle;

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
                PrintPlainError("找不到这个本地视频文件。");
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
            _mediaQueue.Enqueue(url);
            PrintVerboseFormat("[Media Player] Queued ({0}): {1}", _mediaQueue.Count, url);

            var active = _mediaManager?.ActiveStream;
            bool idle = active == null || active.PlaybackState == NAudio.Wave.PlaybackState.Stopped;
            if (idle && _playerObject != null)
            {
                PlayMediaUrl(_mediaQueue.Dequeue());
                return;
            }

            if (!_suppressServerSync && !LocalMediaPaths.IsLocalFile(url))
                _ = PushMediaToServerAsync(isBackgroundSync: false);
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
