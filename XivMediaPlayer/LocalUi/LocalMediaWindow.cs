using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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
        private static readonly Vector4 SyncOn = new(0.45f, 0.85f, 0.5f, 1f);
        private static readonly Vector4 SyncOff = new(1f, 0.75f, 0.25f, 1f);
        private static readonly Vector4 ToggleOn = new(0.2f, 0.55f, 0.3f, 1f);
        private static readonly Vector4 LockOn = new(0.75f, 0.55f, 0.15f, 1f);
        private static readonly Vector4 MuteOn = new(0.55f, 0.28f, 0.28f, 1f);
        private static readonly Vector4 MissingTag = new(0.85f, 0.45f, 0.40f, 1f);
        private static readonly Vector4 MissingText = new(0.62f, 0.62f, 0.62f, 1f);

        private const float CompactPadX = 2f;
        private const float CompactGap = 2f;

        private readonly Plugin _plugin;
        private readonly FileDialogManager _fileDialogs = new();
        private string _urlInput = string.Empty;
        private string _filePath = string.Empty;
        private bool _seekDrag;
        private float _seekValue;

        public LocalMediaWindow(Plugin plugin)
            : base("播放控制###xmp-local", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar, false)
        {
            _plugin = plugin;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(460, 96),
                MaximumSize = new Vector2(460, 820),
            };
        }

        public override void PreDraw()
        {
            ApplyWidthLimit();
            base.PreDraw();
        }

        public override void Draw()
        {
            WindowName = "播放控制###xmp-local";

            DrawTitleRow();
            if (_plugin.IsMediaLoading && !string.IsNullOrEmpty(_plugin.MediaLoadingMessage))
                ImGui.TextDisabled(_plugin.MediaLoadingMessage);

            DrawSeek();
            DrawControls();

            ImGui.Spacing();
            ImGui.Separator();
            DrawLibrary();
            _fileDialogs.Draw();
            ApplyWidthLimit();
        }

        private void ApplyWidthLimit()
        {
            var style = ImGui.GetStyle();
            float width = PlaybackRowWidth() + style.WindowPadding.X * 2f + style.WindowBorderSize * 2f;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(width, 96f),
                MaximumSize = new Vector2(width, 820f),
            };
        }

        private float PlaybackRowWidth()
        {
            const float gap = 4f;
            const float volumeWidth = 116f;
            return PlaybackButtonWidth() * 10f + gap * 8f + 12f + volumeWidth;
        }

        private void DrawTitleRow()
        {
            float menuW = CompactButtonWidth();
            float syncW = SyncMarkWidth();
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float textWidth = ImGui.GetContentRegionAvail().X - menuW - syncW - spacing * 2f;
            string title = string.IsNullOrEmpty(_plugin.CurrentMediaTitle) ? "未在播放" : _plugin.CurrentMediaTitle;

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Ellipsize(title, Math.Max(48f, textWidth)));
            if (ImGui.IsItemHovered())
            {
                string url = _plugin.RememberedMediaUrl;
                ImGui.SetTooltip(string.IsNullOrEmpty(url) ? title : title + "\n" + url);
            }

            ImGui.SameLine();
            DrawSyncMark();

            float menuX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - menuW;
            if (menuX > ImGui.GetCursorPosX())
                ImGui.SameLine(menuX);
            else
                ImGui.SameLine();
            DrawMenu();
        }

        private void DrawSyncMark()
        {
            bool local = _plugin.IsRoomSyncPaused;
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            if (handle.Available)
            {
                using (handle.Push())
                    ImGui.TextColored(local ? SyncOff : SyncOn, (local ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link).ToIconString());
            }
            else
            {
                ImGui.TextColored(local ? SyncOff : SyncOn, local ? "本地" : "同步");
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(local
                    ? "本地文件在本机播放。停止本地播放或改播网络链接后，才会恢复房间同步。"
                    : "房间同步已开启。");
            }
        }

        private void DrawMenu()
        {
            if (CompactIconButton(FontAwesomeIcon.Bars, "more", "更多"))
                ImGui.OpenPopup("xmp-local-more");

            if (!ImGui.BeginPopup("xmp-local-more")) return;
            if (ImGui.MenuItem("刷新"))
                _plugin.RefreshCurrentMedia();
            if (ImGui.MenuItem("举报"))
                CopyDmca();
            if (ImGui.MenuItem("重启播放器"))
                _plugin.RequestKillAndRestart();
            ImGui.EndPopup();
        }

        private void DrawSeek()
        {
            var active = _plugin.MediaManager?.ActiveStream;
            long durationMs = _plugin.GetPlaybackDurationMs();
            bool hasDuration = active != null && durationMs > 0;
            bool canSeek = hasDuration && !_plugin.BlocksYouTubeUserSeek() && CanControl();

            if (!hasDuration)
            {
                _seekValue = 0f;
                _seekDrag = false;
            }
            else if (!_seekDrag)
            {
                _plugin.GetSeekBarProgress(out float progress, out _);
                _seekValue = progress;
            }

            long displayMs = hasDuration && _seekDrag ? (long)(_seekValue * durationMs) : (active?.Time ?? 0);
            string left = FormatTime(displayMs);
            string right = FormatTime(hasDuration ? durationMs : 0);
            float rightWidth = ImGui.CalcTextSize(right).X;

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(left);
            ImGui.SameLine(0, 8);

            float sliderWidth = ImGui.GetContentRegionAvail().X - rightWidth - 8f;
            var padding = ImGui.GetStyle().FramePadding;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(padding.X, 2));
            ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 8);
            ImGui.SetNextItemWidth(Math.Max(40f, sliderWidth));
            if (!canSeek) ImGui.BeginDisabled();
            bool edited = ImGui.SliderFloat("##local-seek", ref _seekValue, 0f, 1f, " ");
            if (!canSeek) ImGui.EndDisabled();
            ImGui.PopStyleVar(2);

            if (canSeek && edited)
                _seekDrag = true;
            if (canSeek && ImGui.IsItemDeactivatedAfterEdit())
            {
                _seekDrag = false;
                _plugin.SeekToMs((long)(_seekValue * durationMs));
            }

            if (sliderWidth >= 40f)
            {
                ImGui.SameLine(0, 8);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(right);
            }
        }

        private void DrawControls()
        {
            bool enabled = CanControl();
            if (!enabled) ImGui.BeginDisabled();

            const float gap = 4f;
            const float volumeWidth = 116f;
            float buttonW = PlaybackButtonWidth();
            float used = buttonW * 10f + gap * 8f + 12f;
            bool oneRow = ImGui.GetContentRegionAvail().X >= used + volumeWidth;

            DrawTransport(gap);
            if (oneRow)
                ImGui.SameLine(0, 12);
            DrawToggles(gap);

            if (oneRow)
            {
                float volumeX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - volumeWidth;
                if (volumeX > ImGui.GetCursorPosX())
                    ImGui.SameLine(volumeX);
                else
                    ImGui.SameLine(0, 12);
                DrawVolume(116f);
            }
            else
            {
                DrawVolume(Math.Max(80f, ImGui.GetContentRegionAvail().X));
            }

            if (!enabled) ImGui.EndDisabled();
        }

        private void DrawTransport(float gap)
        {
            int step = _plugin.Config.SeekIncrementSeconds;
            var active = _plugin.MediaManager?.ActiveStream;
            bool playing = active != null && active.PlaybackState == PlaybackState.Playing && !_plugin.IsIntentionallyPaused;

            if (IconButton(FontAwesomeIcon.StepBackward, "prev", "上一首"))
                _plugin.PlayPrevious();
            ImGui.SameLine(0, gap);
            if (IconButton(FontAwesomeIcon.Backward, "back", $"后退 {step} 秒"))
                _plugin.SeekRelative(-step);
            ImGui.SameLine(0, gap);
            if (IconButton(playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play, "play", playing ? "暂停" : "播放"))
                _plugin.TogglePlayPause();
            ImGui.SameLine(0, gap);
            if (IconButton(FontAwesomeIcon.Forward, "fwd", $"快进 {step} 秒"))
                _plugin.SeekRelative(step);
            ImGui.SameLine(0, gap);
            if (IconButton(FontAwesomeIcon.StepForward, "next", "下一首"))
                _plugin.PlayNext();
            ImGui.SameLine(0, gap);
            if (IconButton(FontAwesomeIcon.Stop, "stop", "停止"))
                _plugin.Stop();
        }

        private void DrawToggles(float gap)
        {
            bool loopOn = _plugin.Config.LoopEnabled;
            if (IconButton(FontAwesomeIcon.Repeat, "loop", loopOn ? "循环：开" : "循环：关", loopOn, ToggleOn))
            {
                _plugin.Config.LoopEnabled = !loopOn;
                _plugin.Config.Save();
            }
            ImGui.SameLine(0, gap);

            bool shuffleOn = _plugin.Config.ShuffleEnabled;
            if (IconButton(FontAwesomeIcon.Random, "shuffle", shuffleOn ? "随机：开" : "随机：关", shuffleOn, ToggleOn))
            {
                _plugin.Config.ShuffleEnabled = !shuffleOn;
                _plugin.Config.Save();
            }
            ImGui.SameLine(0, gap);

            bool locked = _plugin.CurrentTvPlacement?.IsLocked == true;
            if (IconButton(locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen, "lock", locked ? "已锁定" : "锁定", locked, LockOn))
                _plugin.ToggleCurrentTvLock();
            ImGui.SameLine(0, gap);

            if (IconButton(_plugin.IsMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp, "mute", _plugin.IsMuted ? "取消静音" : "静音", _plugin.IsMuted, MuteOn))
                _plugin.ToggleMute();
        }

        private void DrawVolume(float sliderWidth)
        {
            var manager = _plugin.MediaManager;
            if (manager == null) return;

            float volume = manager.LiveStreamVolume;
            ImGui.SetNextItemWidth(sliderWidth);
            if (ImGui.SliderFloat("##volume", ref volume, 0f, 3f, "%.2f"))
                manager.LiveStreamVolume = volume;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("音量");
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _plugin.Config.LivestreamVolume = manager.LiveStreamVolume;
                _plugin.Config.Save();
            }
        }

        private void DrawLibrary()
        {
            if (!ImGui.CollapsingHeader("媒体")) return;

            if (!ImGui.BeginTabBar("xmp-local-tabs")) return;

            if (ImGui.BeginTabItem("链接与文件"))
            {
                DrawUrlRow();
                ImGui.Spacing();
                DrawLocalFile();
                ImGui.EndTabItem();
            }

            var queue = _plugin.CopyMediaQueue();
            if (ImGui.BeginTabItem($"播放列表 ({queue.Length})"))
            {
                DrawQueue(queue);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("观看历史"))
            {
                DrawHistory(queue);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        private void DrawUrlRow()
        {
            FitIconField(3);
            ImGui.InputTextWithHint("##media-url", "输入视频链接", ref _urlInput, 1024);
            ImGui.SameLine(0, CompactGap);
            if (CompactIconButton(FontAwesomeIcon.Play, "url-play", "播放链接"))
                _plugin.PlayMediaUrl(_urlInput);
            ImGui.SameLine(0, CompactGap);
            if (CompactIconButton(FontAwesomeIcon.Paste, "url-paste", "粘贴并播放"))
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
            ImGui.SameLine(0, CompactGap);
            if (CompactIconButton(FontAwesomeIcon.Plus, "url-queue", "加入列表"))
                _plugin.EnqueueMedia(_urlInput);
        }

        private void DrawLocalFile()
        {
            FitIconField(3);
            ImGui.InputTextWithHint("##local-file", "选择或输入本地视频路径", ref _filePath, 1024);
            ImGui.SameLine(0, CompactGap);
            if (CompactIconButton(FontAwesomeIcon.FolderOpen, "browse", "浏览本地文件"))
                OpenLocalFileDialog();
            ImGui.SameLine(0, CompactGap);
            if (CompactIconButton(FontAwesomeIcon.Play, "file-play", "播放本地文件"))
                _plugin.PlayLocalFile(_filePath);
            ImGui.SameLine(0, CompactGap);
            if (CompactIconButton(FontAwesomeIcon.Plus, "file-queue", "加入列表"))
                _plugin.EnqueueMedia(_filePath);
        }

        private void DrawQueue(string[] queue)
        {
            if (queue.Length == 0)
                ImGui.TextDisabled("播放列表是空的。");

            ImGui.BeginChild("##local-queue", new Vector2(0, 140), true);
            for (int i = 0; i < queue.Length; i++)
            {
                ImGui.PushID(i);
                if (CompactIconButton(FontAwesomeIcon.Play, "play", "播放"))
                    _plugin.PlayMediaUrl(queue[i]);
                ImGui.SameLine(0, CompactGap);
                if (CompactIconButton(FontAwesomeIcon.Trash, "remove", "移除"))
                    _plugin.RemoveQueuedMedia(i);
                ImGui.SameLine(0, CompactGap);
                DrawTaggedText(queue[i], queue[i]);
                ImGui.PopID();
            }
            ImGui.EndChild();

            if (queue.Length > 0 && ImGui.Button("清空列表"))
                _plugin.ClearMediaQueue();
        }

        private void DrawHistory(string[] queue)
        {
            var entries = _plugin.Config.WatchHistory.Values;
            if (entries.Count == 0)
            {
                ImGui.TextDisabled("还没有观看记录。");
                return;
            }

            var ordered = new System.Collections.Generic.List<MediaHistoryEntry>(entries);
            ordered.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));

            ImGui.BeginChild("##local-history", new Vector2(0, 140), true);
            int shown = Math.Min(ordered.Count, 20);
            for (int i = 0; i < shown; i++)
            {
                var entry = ordered[i];
                ImGui.PushID(1000 + i);
                if (CompactIconButton(FontAwesomeIcon.Play, "play", "播放"))
                    _plugin.PlayMediaUrl(entry.Url);
                ImGui.SameLine(0, CompactGap);
                if (!QueueContains(queue, entry.Url))
                {
                    if (CompactIconButton(FontAwesomeIcon.Plus, "add", "加入列表"))
                        _plugin.EnqueueMedia(entry.Url);
                    ImGui.SameLine(0, CompactGap);
                }
                string label = string.IsNullOrEmpty(entry.Title) ? entry.Url : entry.Title;
                DrawTaggedText(label, entry.Url);
                ImGui.PopID();
            }
            ImGui.EndChild();
        }

        private void DrawTaggedText(string text, string url)
        {
            const string localKind = "本地";
            const string linkKind = "链接";
            const string missingKind = "缺失";
            bool localFile = LocalMediaPaths.IsLocalFile(url);
            bool missing = LocalMediaPaths.IsMissing(url);
            string kind = missing ? missingKind : localFile ? localKind : linkKind;
            float gap = ImGui.GetStyle().ItemSpacing.X;
            float tagWidth = Math.Max(
                ImGui.CalcTextSize(localKind).X,
                Math.Max(ImGui.CalcTextSize(linkKind).X, ImGui.CalcTextSize(missingKind).X));
            float textWidth = ImGui.GetContentRegionAvail().X - tagWidth - gap;
            if (textWidth < 16f) textWidth = 16f;

            ImGui.AlignTextToFramePadding();
            string shown = Ellipsize(text, textWidth);
            if (missing)
                ImGui.TextColored(MissingText, shown);
            else
                ImGui.TextUnformatted(shown);
            if (ImGui.IsItemHovered() && (missing || shown != text))
            {
                string tip = text;
                if (!string.Equals(text, url, StringComparison.OrdinalIgnoreCase))
                    tip = text + "\n" + url;
                if (missing)
                    tip += "\n这个本地文件已经不存在。";
                ImGui.SetTooltip(tip);
            }

            float tagX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - tagWidth;
            if (tagX > ImGui.GetCursorPosX())
                ImGui.SameLine(tagX);
            else
                ImGui.SameLine(0, gap);
            ImGui.AlignTextToFramePadding();
            Vector4 tagColor = missing ? MissingTag : localFile ? SyncOff : new Vector4(0.55f, 0.75f, 0.95f, 1f);
            ImGui.TextColored(tagColor, kind);
        }

        private static bool QueueContains(string[] queue, string url)
        {
            string key = MediaKey(url);
            foreach (string item in queue)
            {
                if (string.Equals(MediaKey(item), key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string MediaKey(string value)
        {
            value = value.Trim().Trim('"');
            if (!LocalMediaPaths.IsLocalFile(value)) return value;
            return LocalMediaPaths.Normalize(value).Replace('/', '\\');
        }

        private bool IconButton(FontAwesomeIcon icon, string id, string tooltip, bool highlight = false, Vector4? color = null)
        {
            Vector4? face = highlight ? color : null;
            Vector4? hovered = highlight && color.HasValue ? Lighten(color.Value) : null;
            bool pressed = ImGuiComponents.IconButton(id, icon, face, face, hovered);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(tooltip);
            return pressed;
        }

        private bool CompactIconButton(FontAwesomeIcon icon, string id, string tooltip, bool highlight = false, Vector4? color = null)
        {
            var padding = ImGui.GetStyle().FramePadding;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(CompactPadX, padding.Y));
            bool pressed = IconButton(icon, id, tooltip, highlight, color);
            ImGui.PopStyleVar();
            return pressed;
        }

        private void FitIconField(int buttonCount)
        {
            float reserved = buttonCount * CompactButtonWidth() + buttonCount * CompactGap;
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - reserved));
        }

        private float CompactButtonWidth() => IconWidth() + CompactPadX * 2f;

        private float PlaybackButtonWidth() => IconWidth() + ImGui.GetStyle().FramePadding.X * 2f;

        private float IconWidth()
        {
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            if (!handle.Available)
                return ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.Y * 2f;
            using (handle.Push())
                return ImGui.CalcTextSize(FontAwesomeIcon.Play.ToIconString()).X;
        }

        private float SyncMarkWidth()
        {
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            if (!handle.Available)
                return ImGui.CalcTextSize("同步").X;
            using (handle.Push())
                return ImGui.CalcTextSize(FontAwesomeIcon.Link.ToIconString()).X;
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

        private static string Ellipsize(string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
                return text;

            const string suffix = "...";
            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (ImGui.CalcTextSize(text.Substring(0, mid) + suffix).X <= maxWidth)
                    low = mid;
                else
                    high = mid - 1;
            }

            return low == 0 ? suffix : text.Substring(0, low) + suffix;
        }

        private static string FormatTime(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return ts.Hours > 0
                ? string.Format("{0}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds)
                : string.Format("{0}:{1:D2}", ts.Minutes, ts.Seconds);
        }

        private static Vector4 Lighten(Vector4 color)
            => new(Math.Min(color.X + 0.08f, 1f), Math.Min(color.Y + 0.08f, 1f), Math.Min(color.Z + 0.08f, 1f), color.W);
    }
}
