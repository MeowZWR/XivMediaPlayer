using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal static class EdgeTransportCatalog
    {
        public static readonly Vector4 ToggleActive = new(0.2f, 0.55f, 0.3f, 1f);
        public static readonly Vector4 LockActive = new(0.75f, 0.55f, 0.15f, 1f);

        public const int Count = 10;
        public const int PlayPause = 0;
        public const int Previous = 1;
        public const int Next = 2;
        public const int Paste = 3;
        public const int Loop = 4;
        public const int Lock = 5;
        public const int Crop = 6;
        public const int PlaybackSettings = 7;
        public const int Housing = 8;
        public const int PluginSettings = 9;

        public static List<int> DefaultOrder()
        {
            var order = new List<int>(Count);
            for (int i = 0; i < Count; i++)
                order.Add(i);
            return order;
        }

        public static List<int> NormalizeOrder(IList<int>? order)
        {
            var result = new List<int>(Count);
            var seen = new bool[Count];
            if (order != null)
            {
                foreach (int id in order)
                {
                    if ((uint)id >= Count || seen[id])
                        continue;
                    seen[id] = true;
                    result.Add(id);
                }
            }

            for (int id = 0; id < Count; id++)
            {
                if (!seen[id])
                    result.Add(id);
            }

            return result;
        }

        public static bool RequiresScreen(int action)
            => action is PlayPause or Previous or Next or Paste;

        public static bool GetShow(Configuration config, int action) => action switch
        {
            PlayPause => config.ShowEdgeTransportPlayPause,
            Previous => config.ShowEdgeTransportPrevious,
            Next => config.ShowEdgeTransportNext,
            Paste => config.ShowEdgeTransportPaste,
            Loop => config.ShowEdgeTransportLoop,
            Lock => config.ShowEdgeTransportLock,
            Crop => config.ShowEdgeTransportCrop,
            PlaybackSettings => config.ShowEdgeTransportPlaybackSettings,
            Housing => config.ShowEdgeTransportHousing,
            PluginSettings => config.ShowEdgeTransportPluginSettings,
            _ => false,
        };

        public static void SetShow(Configuration config, int action, bool enabled)
        {
            switch (action)
            {
                case PlayPause: config.ShowEdgeTransportPlayPause = enabled; break;
                case Previous: config.ShowEdgeTransportPrevious = enabled; break;
                case Next: config.ShowEdgeTransportNext = enabled; break;
                case Paste: config.ShowEdgeTransportPaste = enabled; break;
                case Loop: config.ShowEdgeTransportLoop = enabled; break;
                case Lock: config.ShowEdgeTransportLock = enabled; break;
                case Crop: config.ShowEdgeTransportCrop = enabled; break;
                case PlaybackSettings: config.ShowEdgeTransportPlaybackSettings = enabled; break;
                case Housing: config.ShowEdgeTransportHousing = enabled; break;
                case PluginSettings: config.ShowEdgeTransportPluginSettings = enabled; break;
            }
        }

        public static void CopyShow(LocalUiConfig source, Configuration target)
        {
            target.ShowEdgeTransportPlayPause = source.ShowEdgeTransportPlayPause;
            target.ShowEdgeTransportPrevious = source.ShowEdgeTransportPrevious;
            target.ShowEdgeTransportNext = source.ShowEdgeTransportNext;
            target.ShowEdgeTransportPaste = source.ShowEdgeTransportPaste;
            target.ShowEdgeTransportLoop = source.ShowEdgeTransportLoop;
            target.ShowEdgeTransportLock = source.ShowEdgeTransportLock;
            target.ShowEdgeTransportCrop = source.ShowEdgeTransportCrop;
            target.ShowEdgeTransportPlaybackSettings = source.ShowEdgeTransportPlaybackSettings;
            target.ShowEdgeTransportHousing = source.ShowEdgeTransportHousing;
            target.ShowEdgeTransportPluginSettings = source.ShowEdgeTransportPluginSettings;
            target.EdgeTransportOrder = NormalizeOrder(source.EdgeTransportOrder);
        }

        public static void CopyShow(Configuration source, LocalUiConfig target)
        {
            target.ShowEdgeTransportPlayPause = source.ShowEdgeTransportPlayPause;
            target.ShowEdgeTransportPrevious = source.ShowEdgeTransportPrevious;
            target.ShowEdgeTransportNext = source.ShowEdgeTransportNext;
            target.ShowEdgeTransportPaste = source.ShowEdgeTransportPaste;
            target.ShowEdgeTransportLoop = source.ShowEdgeTransportLoop;
            target.ShowEdgeTransportLock = source.ShowEdgeTransportLock;
            target.ShowEdgeTransportCrop = source.ShowEdgeTransportCrop;
            target.ShowEdgeTransportPlaybackSettings = source.ShowEdgeTransportPlaybackSettings;
            target.ShowEdgeTransportHousing = source.ShowEdgeTransportHousing;
            target.ShowEdgeTransportPluginSettings = source.ShowEdgeTransportPluginSettings;
            target.EdgeTransportOrder = NormalizeOrder(source.EdgeTransportOrder);
        }

        public static string ConfigLabel(int action) => action switch
        {
            PlayPause => "播放/暂停",
            Previous => "上一首",
            Next => "下一首",
            Paste => "粘贴并播放",
            Loop => "循环",
            Lock => "锁定",
            Crop => "裁切",
            PlaybackSettings => "播放设置",
            Housing => "房屋菜单",
            PluginSettings => "插件设置",
            _ => "扩展按钮",
        };

        public static FontAwesomeIcon ConfigIcon(int action) => action switch
        {
            PlayPause => FontAwesomeIcon.Play,
            Previous => FontAwesomeIcon.StepBackward,
            Next => FontAwesomeIcon.StepForward,
            Paste => FontAwesomeIcon.Paste,
            Loop => FontAwesomeIcon.Repeat,
            Lock => FontAwesomeIcon.Lock,
            Crop => FontAwesomeIcon.Crop,
            PlaybackSettings => FontAwesomeIcon.Bars,
            Housing => FontAwesomeIcon.HouseChimney,
            PluginSettings => FontAwesomeIcon.Cog,
            _ => FontAwesomeIcon.Circle,
        };

        public static FontAwesomeIcon RuntimeIcon(Plugin plugin, int action, bool playing) => action switch
        {
            PlayPause => playing ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play,
            Lock => plugin.CurrentTvPlacement?.IsLocked == true ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen,
            _ => ConfigIcon(action),
        };

        public static Vector4? RuntimeTint(Plugin plugin, int action) => action switch
        {
            Loop when plugin.Config.LoopEnabled => ToggleActive,
            Crop when plugin.Config.CropToFill => ToggleActive,
            Lock when plugin.CurrentTvPlacement?.IsLocked == true => LockActive,
            _ => null,
        };

        public static string RuntimeTip(Plugin plugin, int action, bool playing, bool blocked)
        {
            if (blocked)
                return "当前区域没有屏幕，无法播放。";

            return action switch
            {
                PlayPause => playing ? "暂停" : "播放",
                Loop => plugin.Config.LoopEnabled ? "循环：开" : "循环：关",
                Lock => plugin.CurrentTvPlacement?.IsLocked == true ? "已锁定" : "锁定",
                Crop => plugin.Config.CropToFill ? "裁切：开，铺满屏幕" : "裁切：关，完整显示",
                _ => ConfigLabel(action),
            };
        }

        public static void Run(Plugin plugin, int action)
        {
            switch (action)
            {
                case PlayPause:
                    plugin.TogglePlayPause();
                    break;
                case Previous:
                    plugin.PlayPrevious();
                    break;
                case Next:
                    plugin.PlayNext();
                    break;
                case Paste:
                    string clip = ImGui.GetClipboardText() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(clip))
                        plugin.PrintPlainError("剪贴板是空的，或读取失败。");
                    else
                        plugin.PlayMediaUrl(clip.Trim());
                    break;
                case Loop:
                    plugin.Config.LoopEnabled = !plugin.Config.LoopEnabled;
                    plugin.Config.Save();
                    break;
                case Lock:
                    plugin.ToggleCurrentTvLock();
                    break;
                case Crop:
                    plugin.Config.CropToFill = !plugin.Config.CropToFill;
                    plugin.Config.SaveLocal();
                    break;
                case PlaybackSettings:
                    plugin.TogglePlaybackSettingsFromEdge();
                    break;
                case Housing:
                    plugin.ExecuteChatCommand("/housing");
                    break;
                case PluginSettings:
                    plugin.ToggleConfigUi();
                    break;
            }
        }

        public static Vector4 Lighten(Vector4 color)
            => new(Math.Min(color.X + 0.08f, 1f), Math.Min(color.Y + 0.08f, 1f), Math.Min(color.Z + 0.08f, 1f), color.W);

        public static int CollectVisible(Configuration config, Span<int> actions)
        {
            var order = NormalizeOrder(config.EdgeTransportOrder);
            int count = 0;
            foreach (int action in order)
            {
                if (!GetShow(config, action))
                    continue;
                actions[count++] = action;
            }

            return count;
        }
    }
}
