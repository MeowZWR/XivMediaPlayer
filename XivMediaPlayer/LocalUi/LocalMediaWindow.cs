using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using NAudio.Wave;
using System;
using System.IO;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class LocalMediaWindow : Window
    {
        private readonly Plugin _plugin;
        private readonly FileDialogManager _fileDialogs = new();
        private string _urlInput = string.Empty;
        private string _filePath = string.Empty;
        private bool _seekDrag;
        private float _seekValue;

        public LocalMediaWindow(Plugin plugin)
            : base("播放控制", ImGuiWindowFlags.None, false)
        {
            _plugin = plugin;
            Size = new Vector2(520, 640);
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            WindowName = "播放控制";

            DrawStatus();
            ImGui.Separator();
            DrawNowPlaying();
            DrawSeek();
            DrawTransport();
            ImGui.Separator();
            DrawUrlRow();
            ImGui.Separator();
            DrawLocalFile();
            ImGui.Separator();
            DrawQueue();
            DrawHistory();
            _fileDialogs.Draw();
        }

        private void DrawStatus()
        {
            if (_plugin.IsRoomSyncPaused)
            {
                ImGui.TextColored(new Vector4(1f, 0.75f, 0.25f, 1f), "正在播放本地文件，房间同步已暂停。");
                if (ImGui.Button("恢复房间同步"))
                    _plugin.Stop();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("停止本地文件，重新跟随房间播放。");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.45f, 0.85f, 0.5f, 1f), "房间同步已开启。");
            }
        }

        private void DrawNowPlaying()
        {
            string title = string.IsNullOrEmpty(_plugin.CurrentMediaTitle)
                ? "未在播放"
                : _plugin.CurrentMediaTitle;
            ImGui.TextWrapped(title);

            string url = _plugin.LastStreamURL;
            if (!string.IsNullOrEmpty(url))
                ImGui.TextWrapped(url);

            if (_plugin.IsMediaLoading)
                ImGui.TextDisabled(_plugin.MediaLoadingMessage);
        }

        private void DrawSeek()
        {
            var active = _plugin.MediaManager?.ActiveStream;
            long durationMs = _plugin.GetPlaybackDurationMs();
            if (active == null || durationMs <= 0) return;

            if (!_seekDrag)
            {
                _plugin.GetSeekBarProgress(out float progress, out _);
                _seekValue = progress;
            }

            bool seekDisabled = _plugin.BlocksYouTubeUserSeek() || !CanControl();
            ImGui.SetNextItemWidth(-1);
            if (seekDisabled)
            {
                ImGui.BeginDisabled();
                ImGui.SliderFloat("##local-seek", ref _seekValue, 0f, 1f, " ");
                ImGui.EndDisabled();
                _seekDrag = false;
            }
            else if (ImGui.SliderFloat("##local-seek", ref _seekValue, 0f, 1f, " "))
            {
                _seekDrag = true;
            }

            if (!seekDisabled && ImGui.IsItemDeactivatedAfterEdit())
            {
                _seekDrag = false;
                _plugin.SeekToMs((long)(_seekValue * durationMs));
            }

            long displayMs = _seekDrag ? (long)(_seekValue * durationMs) : active.Time;
            ImGui.Text(FormatTime(displayMs) + " / " + FormatTime(durationMs));
        }

        private void DrawTransport()
        {
            bool enabled = CanControl();
            if (!enabled) ImGui.BeginDisabled();

            int step = _plugin.Config.SeekIncrementSeconds;
            var active = _plugin.MediaManager?.ActiveStream;
            bool playing = active != null && active.PlaybackState == PlaybackState.Playing && !_plugin.IsIntentionallyPaused;

            if (ImGui.Button("上一首")) _plugin.PlayPrevious();
            ImGui.SameLine();
            if (ImGui.Button("后退")) _plugin.SeekRelative(-step);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"后退 {step} 秒");
            ImGui.SameLine();
            if (ImGui.Button(playing ? "暂停" : "播放")) _plugin.TogglePlayPause();
            ImGui.SameLine();
            if (ImGui.Button("快进")) _plugin.SeekRelative(step);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"快进 {step} 秒");
            ImGui.SameLine();
            if (ImGui.Button("下一首")) _plugin.PlayNext();
            ImGui.SameLine();
            if (ImGui.Button("停止")) _plugin.Stop();

            bool loopOn = _plugin.Config.LoopEnabled;
            if (loopOn) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.55f, 0.3f, 1f));
            if (ImGui.Button("循环"))
            {
                _plugin.Config.LoopEnabled = !_plugin.Config.LoopEnabled;
                _plugin.Config.Save();
            }
            if (loopOn) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(loopOn ? "循环：开" : "循环：关");
            ImGui.SameLine();

            bool shuffleOn = _plugin.Config.ShuffleEnabled;
            if (shuffleOn) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.55f, 0.3f, 1f));
            if (ImGui.Button("随机"))
            {
                _plugin.Config.ShuffleEnabled = !_plugin.Config.ShuffleEnabled;
                _plugin.Config.Save();
            }
            if (shuffleOn) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(shuffleOn ? "随机：开" : "随机：关");
            ImGui.SameLine();

            if (ImGui.Button("刷新")) _plugin.RefreshCurrentMedia();
            ImGui.SameLine();

            bool locked = _plugin.CurrentTvPlacement?.IsLocked == true;
            if (locked) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.75f, 0.55f, 0.15f, 1f));
            if (ImGui.Button(locked ? "已锁定" : "锁定")) _plugin.ToggleCurrentTvLock();
            if (locked) ImGui.PopStyleColor();
            ImGui.SameLine();

            if (ImGui.Button(_plugin.IsMuted ? "取消静音" : "静音")) _plugin.ToggleMute();

            var manager = _plugin.MediaManager;
            if (manager != null)
            {
                float volume = manager.LiveStreamVolume;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("音量", ref volume, 0f, 3f))
                    manager.LiveStreamVolume = volume;
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    _plugin.Config.LivestreamVolume = manager.LiveStreamVolume;
                    _plugin.Config.Save();
                }
            }

            if (ImGui.Button("举报"))
                CopyDmca();
            ImGui.SameLine();
            if (ImGui.Button("重启播放器"))
                _plugin.RequestKillAndRestart();

            if (!enabled) ImGui.EndDisabled();
        }

        private void DrawUrlRow()
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##media-url", ref _urlInput, 1024);
            if (ImGui.Button("播放链接"))
                _plugin.PlayMediaUrl(_urlInput);
            ImGui.SameLine();
            if (ImGui.Button("粘贴"))
            {
                string clip = ImGui.GetClipboardText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(clip))
                    _plugin.PrintPlainError("剪贴板是空的，或读取失败。");
                else
                {
                    _urlInput = clip.Trim();
                    _plugin.PlayMediaUrl(_urlInput);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("加入列表"))
                _plugin.EnqueueMedia(_urlInput);
        }

        private void DrawLocalFile()
        {
            ImGui.Text("本地视频");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##local-file", ref _filePath, 1024);
            if (ImGui.Button("浏览..."))
                OpenLocalFileDialog();
            ImGui.SameLine();
            if (ImGui.Button("播放本地文件"))
                _plugin.PlayLocalFile(_filePath);
            ImGui.TextWrapped("本地文件只在本机播放。停止本地播放或改播网络链接后，才会恢复房间同步。");
        }

        private void DrawQueue()
        {
            if (!ImGui.CollapsingHeader("播放列表")) return;

            var queue = _plugin.CopyMediaQueue();
            if (queue.Length == 0)
                ImGui.TextDisabled("播放列表是空的。");

            ImGui.BeginChild("##local-queue", new Vector2(0, 120), true);
            for (int i = 0; i < queue.Length; i++)
            {
                ImGui.PushID(i);
                if (ImGui.Button("播放"))
                    _plugin.PlayMediaUrl(queue[i]);
                ImGui.SameLine();
                if (ImGui.Button("移除"))
                    _plugin.RemoveQueuedMedia(i);
                ImGui.SameLine();
                ImGui.TextWrapped(queue[i]);
                ImGui.PopID();
            }
            ImGui.EndChild();

            if (queue.Length > 0 && ImGui.Button("清空列表"))
                _plugin.ClearMediaQueue();
        }

        private void DrawHistory()
        {
            if (!ImGui.CollapsingHeader("观看历史")) return;

            var entries = _plugin.Config.WatchHistory.Values;
            if (entries.Count == 0)
            {
                ImGui.TextDisabled("还没有观看记录。");
                return;
            }

            var ordered = new System.Collections.Generic.List<MediaHistoryEntry>(entries);
            ordered.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));

            ImGui.BeginChild("##local-history", new Vector2(0, 120), true);
            int shown = Math.Min(ordered.Count, 20);
            for (int i = 0; i < shown; i++)
            {
                var entry = ordered[i];
                ImGui.PushID(1000 + i);
                if (ImGui.Button("播放"))
                    _plugin.PlayMediaUrl(entry.Url);
                ImGui.SameLine();
                string label = string.IsNullOrEmpty(entry.Title) ? entry.Url : entry.Title;
                ImGui.TextWrapped(label);
                ImGui.PopID();
            }
            ImGui.EndChild();
        }

        private bool CanControl() => _plugin.IsRoomSyncPaused || _plugin.CanControlCurrentTvPlayback();

        private void CopyDmca()
        {
            string url = _plugin.LastStreamURL;
            if (string.IsNullOrEmpty(url))
            {
                _plugin.PrintPlainError("没有正在播放的链接可以复制。");
                return;
            }

            string domain = "网站管理员";
            try
            {
                domain = new Uri(url).Host;
            }
            catch { }

            ImGui.SetClipboardText($"内容链接：{url}\n\n请联系 {domain} 举报此内容。");
            _plugin.PrintPlain("已把举报信息和链接复制到剪贴板。");
        }

        private void OpenLocalFileDialog()
        {
            string? startPath = null;
            if (!string.IsNullOrEmpty(_filePath))
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    startPath = dir;
            }

            _fileDialogs.OpenFileDialog(
                "选择视频文件",
                "视频文件{.mp4,.mkv,.webm,.avi,.mov,.flv,.wmv,.m4v,.ts,.m2ts,.mpg,.mpeg},.*",
                (ok, paths) =>
                {
                    if (!ok || paths == null || paths.Count == 0) return;
                    string path = paths[0];
                    _filePath = path;
                    _plugin.PlayLocalFile(path);
                },
                1,
                startPath,
                true);
        }

        private static string FormatTime(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return ts.Hours > 0
                ? string.Format("{0}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds)
                : string.Format("{0}:{1:D2}", ts.Minutes, ts.Seconds);
        }
    }
}
