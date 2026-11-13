using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
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
        private static readonly Vector4 VolumeIcon = new(0.68f, 0.68f, 0.70f, 1f);
        private static readonly Vector4 MissingTag = new(0.85f, 0.45f, 0.40f, 1f);
        private static readonly Vector4 MissingText = new(0.62f, 0.62f, 0.62f, 1f);

        private const float CompactPadX = 2f;
        private const float CompactGap = 2f;
        private const float ControlGap = 4f;
        private const float ClusterGap = 10f;
        private const float VolumeTrackWidth = 100f;
        private const float VolumeIconScale = 0.62f;

        private readonly Plugin _plugin;
        private readonly FileDialogManager _fileDialogs = new();
        private static readonly string[] ScopeLabels = { "庭院和室内", "仅室外", "仅室内" };
        private static readonly string[] ArrivalLabels = { "只显示边缘图标", "进入时打开面板" };

        private string _urlInput = string.Empty;
        private string _filePath = string.Empty;
        private bool _seekDrag;
        private float _seekValue;
        private float _seekGrabAlpha;
        private bool _volumeDrag;
        private float _volumeGrabAlpha;
        private float _controlsWidth;
        private bool _plotRegistryOpen;
        private bool _chromePushed;
        private bool _pushedAlpha;

        public LocalMediaWindow(Plugin plugin)
            : base(
                "播放控制###xmp-local",
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoFocusOnAppearing,
                false)
        {
            _plugin = plugin;
            DisableFadeInFadeOut = true;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(460, 96),
                MaximumSize = new Vector2(460, 820),
            };
        }

        public override bool DrawConditions()
            => !_plugin.GameUiHidden;

        public override void PreDraw()
        {
            _chromePushed = _plugin.Config.PlaybackLightlessStyle;
            AllowBackgroundBlur = !_chromePushed;
            if (_chromePushed)
                PlaybackPanelChrome.Push();
            _pushedAlpha = _plugin.PlaybackDockMoving;
            if (_pushedAlpha)
            {
                ImGui.SetNextWindowPos(_plugin.PlaybackPanelMotionPos, ImGuiCond.Always);
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _plugin.PlaybackPanelReveal);
            }
            ApplyWidthLimit();
            base.PreDraw();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (_pushedAlpha)
                ImGui.PopStyleVar();
            if (_chromePushed)
                PlaybackPanelChrome.Pop();
            if (!IsOpen)
                _plugin.OnPlaybackPanelClosed();
        }

        public override void Draw()
        {
            WindowName = "播放控制###xmp-local";
            if (!_plugin.PlaybackDockMoving)
                _plugin.NotePlaybackPanelBounds(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            if (_chromePushed)
            {
                PlaybackPanelChrome.DrawBackdrop();
                PlaybackPanelChrome.DrawAtmosphere();
            }

            DrawTitleRow();
            if (_chromePushed)
                PlaybackPanelChrome.DrawHairline();
            if (_plugin.IsMediaLoading && !string.IsNullOrEmpty(_plugin.MediaLoadingMessage))
                ImGui.TextDisabled(_plugin.MediaLoadingMessage);

            DrawSeek();
            DrawControls();

            ImGui.Spacing();
            ImGui.Separator();
            DrawLibrary();
            if (_plotRegistryOpen)
                DrawPlotRegistry();
            _fileDialogs.Draw();
            ApplyWidthLimit();
        }

        private void ApplyWidthLimit()
        {
            if (_controlsWidth <= 1f)
                return;

            var style = ImGui.GetStyle();
            float scale = Math.Max(ImGuiHelpers.GlobalScale, 0.01f);
            float width = (_controlsWidth + style.WindowPadding.X * 2f + style.WindowBorderSize * 2f) / scale;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(width, 96f),
                MaximumSize = new Vector2(width, 820f),
            };
        }

        private void DrawTitleRow()
        {
            float syncW = ModeSwitchWidth();
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            string raw = _plugin.CurrentMediaTitle;
            bool hasTitle = !string.IsNullOrWhiteSpace(raw);
            string title = hasTitle ? raw.Trim() : "未在播放";
            string url = _plugin.RememberedMediaUrl;
            bool missing = LocalMediaPaths.IsMissing(url);
            string name = title;
            string tag = "";
            bool hasTag = hasTitle && LocalMediaPaths.TrySplitLocalName(title, url, out name, out tag);
            float tagWidth = hasTag ? PlaybackPanelChrome.ExtensionTagSize(tag).X : 0f;
            float textWidth = ImGui.GetContentRegionAvail().X - syncW - spacing * (hasTag ? 2f : 1f) - tagWidth;

            ImGui.AlignTextToFramePadding();
            string shown = Ellipsize(name, Math.Max(48f, textWidth));
            if (missing)
                ImGui.TextColored(MissingText, shown);
            else
                ImGui.TextUnformatted(shown);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(TitleTip(title, url, missing));

            if (hasTag)
            {
                ImGui.SameLine(0, spacing);
                DrawExtensionTag(tag, missing);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(TitleTip(title, url, missing));
            }

            ImGui.SameLine();
            DrawModeSwitch();
        }

        private static string TitleTip(string title, string url, bool missing)
        {
            string tip = string.IsNullOrEmpty(url) ? title : title + "\n" + url;
            if (missing)
                tip += "\n此文件已经不存在。";
            return tip;
        }

        private void DrawExtensionTag(string tag, bool missing)
        {
            Vector4 color = missing
                ? MissingTag
                : _chromePushed
                    ? PlaybackPanelChrome.Accent
                    : new Vector4(0.55f, 0.75f, 0.95f, 1f);
            var size = PlaybackPanelChrome.ExtensionTagSize(tag);
            float frame = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(size.X, frame));
            var item = ImGui.GetItemRectMin();
            PlaybackPanelChrome.DrawExtensionTag(
                ImGui.GetWindowDrawList(),
                new Vector2(item.X, item.Y + (frame - size.Y) * 0.5f),
                tag,
                color);
        }

        private void DrawModeSwitch()
        {
            bool local = _plugin.IsLocalPlaybackMode;
            string label = local ? "本地" : "同步";
            string tooltip = local
                ? "本地模式。播放不参与房间同步，播完不会从列表移除。点击切换到同步模式。"
                : "同步模式。面板里的列表保持不动，房间同步的是当前这首之后的队列。不能添加本地视频。点击切换到本地模式。";
            if (_chromePushed)
            {
                if (PlaybackPanelChrome.DrawPill("##playback-mode", label, local ? SyncOff : SyncOn, tooltip))
                    _plugin.SetLocalPlaybackMode(!local);
                return;
            }

            Vector4 color = local ? SyncOff : SyncOn;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(color.X, color.Y, color.Z, 0.35f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(color.X, color.Y, color.Z, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGui.SmallButton(label + "##playback-mode"))
                _plugin.SetLocalPlaybackMode(!local);
            ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);
        }

        public override void OnClose()
        {
            _plugin.OnPlaybackPanelClosed(hostNotify: true);
        }

        private void DrawTitleTools(float spacing)
        {
            DrawMarkedIcon(FontAwesomeIcon.Bars, "plots", "播放设置", _plotRegistryOpen, () => _plotRegistryOpen = !_plotRegistryOpen);
            ImGui.SameLine(0, spacing);
            if (IconButton(FontAwesomeIcon.HouseChimney, "housing", "房屋菜单"))
                _plugin.ExecuteChatCommand("/housing");
            ImGui.SameLine(0, spacing);
            DrawMarkedIcon(FontAwesomeIcon.Cog, "plugin-settings", "插件设置", _plugin.IsConfigUiOpen, _plugin.ToggleConfigUi);
        }

        private void DrawMarkedIcon(FontAwesomeIcon icon, string id, string tooltip, bool open, Action onClick)
        {
            Vector4? openFace = null;
            Vector4? openHover = null;
            if (open)
            {
                if (_chromePushed)
                {
                    openFace = PlaybackPanelChrome.Accent;
                    openHover = Lighten(openFace.Value);
                }
                else
                {
                    var colors = ImGui.GetStyle().Colors;
                    openFace = colors[(int)ImGuiCol.ButtonActive];
                    openHover = colors[(int)ImGuiCol.ButtonHovered];
                }
            }

            if (IconButton(icon, id, tooltip, open, openFace, openHover))
                onClick();
        }

        private void DrawPlotRegistry()
        {
            ImGui.Spacing();
            ImGui.Separator();

            float tableWidth = ImGui.GetContentRegionAvail().X;
            if (ImGui.BeginTable("##playback-settings", 2, ImGuiTableFlags.SizingStretchSame, new Vector2(tableWidth, 0f)))
            {
                ImGui.TableSetupColumn("left", ImGuiTableColumnFlags.WidthStretch, 1f);
                ImGui.TableSetupColumn("right", ImGuiTableColumnFlags.WidthStretch, 1f);

                bool lightless = _plugin.Config.PlaybackLightlessStyle;
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("Lightless 风格", ref lightless))
                {
                    _plugin.Config.PlaybackLightlessStyle = lightless;
                    _plugin.Config.SaveLocal();
                }

                bool restoreBgm = _plugin.Config.RestoreGameBgmOnStop;
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("停止播放后恢复游戏BGM", ref restoreBgm))
                {
                    _plugin.SetRestoreGameBgmOnStop(restoreBgm);
                }
                bool restoreBgmHovered = ImGui.IsItemHovered();

                bool showEdge = _plugin.Config.ShowEdgeIconWhenScreenDetected;
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("检测到屏幕时显示边缘图标", ref showEdge))
                {
                    _plugin.Config.ShowEdgeIconWhenScreenDetected = showEdge;
                    _plugin.Config.SaveLocal();
                }
                bool showEdgeHovered = ImGui.IsItemHovered();
                bool keepEdge = _plugin.Config.KeepEdgeIconWithPanel;
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("打开面板时保留边缘图标", ref keepEdge))
                {
                    _plugin.Config.KeepEdgeIconWithPanel = keepEdge;
                    _plugin.Config.SaveLocal();
                }
                bool keepEdgeHovered = ImGui.IsItemHovered();

                bool showTitle = _plugin.Config.ShowEdgePlaybackTitle;
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("边缘图标旁显示滚动标题", ref showTitle))
                {
                    _plugin.Config.ShowEdgePlaybackTitle = showTitle;
                    _plugin.Config.SaveLocal();
                }
                bool showTitleHovered = ImGui.IsItemHovered();
                bool hideBackground = _plugin.Config.HideEdgeTitleBackground;
                ImGui.TableNextColumn();
                if (ImGui.Checkbox("隐藏滚动区域背景", ref hideBackground))
                {
                    _plugin.Config.HideEdgeTitleBackground = hideBackground;
                    _plugin.Config.SaveLocal();
                }
                bool hideBackgroundHovered = ImGui.IsItemHovered();

                ImGui.EndTable();

                if (restoreBgmHovered)
                    ImGui.SetTooltip("停止播放后恢复游戏BGM状态。");
                if (showEdgeHovered)
                    ImGui.SetTooltip("只在住宅区生效。当前地点已有屏幕，且没有被登记地块的动作接管时，显示边缘图标。");
                if (keepEdgeHovered)
                    ImGui.SetTooltip("打开播放控制面板后，边缘图标继续留在屏幕边上，不再收起。");
                if (showTitleHovered)
                    ImGui.SetTooltip("有播放内容时，在边缘图标旁用固定长度滚动标题。");
                if (hideBackgroundHovered)
                    ImGui.SetTooltip("隐藏滚动文字的边框、背景，只留下文字。");

                DrawPlaybackSliders();
            }

            ImGui.Spacing();
            DrawCurrentPlotStatus();
            ImGui.Spacing();
            var plots = _plugin.RegisteredPlots;
            if (!ImGui.CollapsingHeader($"已登记房屋 ({plots.Count})"))
                return;
            if (plots.Count == 0)
            {
                ImGui.TextDisabled("未进行过地块登记。");
                return;
            }

            var plotStyle = ImGui.GetStyle();
            float row = ImGui.GetFrameHeightWithSpacing() * 3f + plotStyle.ItemSpacing.Y;
            float visibleRows = Math.Min(plots.Count, 2.5f);
            float height = row * visibleRows + plotStyle.WindowPadding.Y * 2f + plotStyle.WindowBorderSize * 2f;
            ImGui.BeginChild("##registered-plots", new Vector2(0, height), true);
            int removeIndex = -1;
            for (int i = 0; i < plots.Count; i++)
            {
                RegisteredPlot plot = plots[i];
                if (plot == null) continue;
                ImGui.PushID(i);
                if (i > 0)
                    ImGui.Separator();

                float deleteW = CompactButtonWidth();
                float textWidth = ImGui.GetContentRegionAvail().X - deleteW - ImGui.GetStyle().ItemSpacing.X;
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Ellipsize(
                    Plugin.FormatHousePlot(plot.WorldName, plot.District, plot.Subdivision, plot.Ward, plot.Plot, null),
                    Math.Max(48f, textWidth)));
                float deleteX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - deleteW;
                if (deleteX > ImGui.GetCursorPosX())
                    ImGui.SameLine(deleteX);
                else
                    ImGui.SameLine();
                if (CompactIconButton(FontAwesomeIcon.Trash, "delete", "删除"))
                    removeIndex = i;

                float labelColumn = Math.Max(ImGui.CalcTextSize("触发").X, ImGui.CalcTextSize("动作").X);
                int scope = (int)plot.Scope;
                if (scope < 0 || scope >= ScopeLabels.Length) scope = 0;
                if (DrawAlignedCombo("触发", "##scope", labelColumn, ref scope, ScopeLabels))
                {
                    plot.Scope = (PlotTriggerScope)scope;
                    _plugin.Config.SaveLocal();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "庭院和室内：走到庭院里，或进入房屋时，都会执行下面的动作。\n仅室外：只在庭院里触发。\n仅室内：只在房屋里触发。");
                }

                int arrival = (int)plot.Arrival;
                if (arrival < 0 || arrival >= ArrivalLabels.Length) arrival = 0;
                if (DrawAlignedCombo("动作", "##arrival", labelColumn, ref arrival, ArrivalLabels))
                {
                    plot.Arrival = (PlotArrivalAction)arrival;
                    _plugin.Config.SaveLocal();
                }

                ImGui.PopID();
            }
            ImGui.EndChild();

            if (removeIndex >= 0)
                _plugin.RemoveRegisteredPlotAt(removeIndex);
        }

        private void DrawCurrentPlotStatus()
        {
            HousePlotRead read = _plugin.ReadHousePlot();
            if (read.Kind == HousePlotReadKind.Unknown)
            {
                ImGui.TextDisabled("正在读取所在地块…");
                return;
            }

            if (read.Kind != HousePlotReadKind.OnPlot)
            {
                ImGui.TextWrapped("当前不在地块上。走到庭院范围内，或进入房屋后再登记。");
                return;
            }

            DrawCurrentPlotLine(read);
        }

        private void DrawCurrentPlotLine(HousePlotRead read)
        {
            string text = "当前：" + Plugin.FormatHousePlot(read.WorldName, read.District, read.Subdivision, read.Ward, read.Plot, read.Indoor);
            bool registered = _plugin.IsPlotRegistered(read);
            const string registeredLabel = "（已登记）";
            const string registerLabel = "登记";
            float gap = ImGui.GetStyle().ItemSpacing.X;
            float suffixWidth = registered
                ? ImGui.CalcTextSize(registeredLabel).X
                : ImGui.CalcTextSize(registerLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
            float textWidth = ImGui.GetContentRegionAvail().X - suffixWidth - gap;

            ImGui.AlignTextToFramePadding();
            string shown = Ellipsize(text, Math.Max(48f, textWidth));
            ImGui.TextUnformatted(shown);
            if (ImGui.IsItemHovered() && shown != text)
                ImGui.SetTooltip(text);

            ImGui.SameLine(0, gap);
            if (registered)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled(registeredLabel);
                return;
            }

            if (ImGui.SmallButton(registerLabel))
                _plugin.RegisterCurrentPlot();
        }

        private void DrawPlaybackSliders()
        {
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float resetWidth = CompactButtonWidth();
            float labelWidth = 0f;
            foreach (string name in new[] { "边缘图标屏幕边距", "文字滚动速度", "滚动区域长度", "滚动区域间距" })
                labelWidth = Math.Max(labelWidth, ImGui.CalcTextSize(name).X);
            float sliderWidth = Math.Max(72f, ImGui.GetContentRegionAvail().X - resetWidth - labelWidth - spacing * 2f);
            bool done = false;

            float margin = Math.Clamp(_plugin.Config.EdgeIconScreenMargin, 0f, LocalUiConfig.MaxEdgeIconScreenMargin);
            if (DrawResetSlider(
                "edge-margin",
                "边缘图标屏幕边距",
                ref margin,
                0f,
                LocalUiConfig.MaxEdgeIconScreenMargin,
                "%.0f",
                LocalUiConfig.DefaultEdgeIconScreenMargin,
                sliderWidth,
                "边缘图标离屏幕边缘的距离。",
                ref done))
                _plugin.Config.EdgeIconScreenMargin = margin;

            float scrollSpeed = Math.Clamp(_plugin.Config.EdgeTitleScrollSpeed, 0f, 3f);
            if (DrawResetSlider(
                "scroll-speed",
                "文字滚动速度",
                ref scrollSpeed,
                0f,
                3f,
                "%.2f",
                LocalUiConfig.DefaultEdgeTitleScrollSpeed,
                sliderWidth,
                "文字滚动速度。0 为静止，默认值为 1。",
                ref done))
                _plugin.Config.EdgeTitleScrollSpeed = scrollSpeed;

            float marqueeLines = _plugin.Config.EdgeTitleMarqueeLines;
            if (marqueeLines < 3f)
                marqueeLines = LocalUiConfig.DefaultEdgeTitleMarqueeLines;
            marqueeLines = Math.Clamp(marqueeLines, 3f, 16f);
            if (DrawResetSlider(
                "marquee-lines",
                "滚动区域长度",
                ref marqueeLines,
                3f,
                16f,
                "%.0f",
                LocalUiConfig.DefaultEdgeTitleMarqueeLines,
                sliderWidth,
                "滚动区域可见长度。",
                ref done))
                _plugin.Config.EdgeTitleMarqueeLines = marqueeLines;

            float titleGap = Math.Clamp(_plugin.Config.EdgeTitleIconGap, 0f, LocalUiConfig.MaxEdgeTitleIconGap);
            if (DrawResetSlider(
                "title-gap",
                "滚动区域间距",
                ref titleGap,
                0f,
                LocalUiConfig.MaxEdgeTitleIconGap,
                "%.0f",
                LocalUiConfig.DefaultEdgeTitleIconGap,
                sliderWidth,
                "滚动标题和边缘图标之间的空隙。",
                ref done))
                _plugin.Config.EdgeTitleIconGap = titleGap;

            if (done)
                _plugin.Config.SaveLocal();
        }

        private bool DrawResetSlider(
            string id,
            string label,
            ref float value,
            float min,
            float max,
            string format,
            float resetValue,
            float sliderWidth,
            string tooltip,
            ref bool done)
        {
            ImGui.PushID(id);
            ImGui.SetNextItemWidth(sliderWidth);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            if (_chromePushed)
            {
                ImGui.PushStyleColor(
                    ImGuiCol.Border,
                    new Vector4(PlaybackPanelChrome.Accent.X, PlaybackPanelChrome.Accent.Y, PlaybackPanelChrome.Accent.Z, 0.45f));
            }

            bool changed = ImGui.SliderFloat("##value", ref value, min, max, format);
            if (_chromePushed)
                ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            if (ImGui.IsItemDeactivatedAfterEdit())
                done = true;
            bool hovered = ImGui.IsItemHovered();

            ImGui.SameLine();
            bool atDefault = Math.Abs(value - resetValue) < 0.001f;
            if (atDefault)
                ImGui.BeginDisabled();
            if (CompactIconButton(FontAwesomeIcon.Undo, "reset", "重置"))
            {
                value = resetValue;
                changed = true;
                done = true;
            }

            if (atDefault)
                ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(label);
            if (hovered)
                ImGui.SetTooltip(tooltip);
            ImGui.PopID();
            return changed;
        }

        private static bool DrawAlignedCombo(string label, string id, float labelColumn, ref int value, string[] items)
        {
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float rowX = ImGui.GetCursorPosX();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(label);
            float comboX = rowX + labelColumn + spacing;
            if (comboX > ImGui.GetCursorPosX())
                ImGui.SameLine(comboX);
            else
                ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            return ImGui.Combo(id, ref value, items, items.Length);
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

            float muteW = VolumeIconSlotWidth();
            float drawWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - muteW - VolumeTrackWidth - 16f);

            if (!canSeek) ImGui.BeginDisabled();
            ImGui.InvisibleButton("##local-seek", new Vector2(drawWidth, ImGui.GetFrameHeight()));
            bool seekHeld = canSeek && ImGui.IsItemActive();
            if (canSeek && ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var seekMin = ImGui.GetItemRectMin();
            var seekMax = ImGui.GetItemRectMax();
            if (!canSeek) ImGui.EndDisabled();

            if (seekHeld)
            {
                _seekDrag = true;
                float span = Math.Max(1f, seekMax.X - seekMin.X);
                _seekValue = Math.Clamp((ImGui.GetIO().MousePos.X - seekMin.X) / span, 0f, 1f);
            }
            if (canSeek && ImGui.IsItemDeactivated() && _seekDrag)
            {
                _seekDrag = false;
                _plugin.SeekToMs((long)(_seekValue * durationMs));
            }

            bool seekHover = canSeek && (ImGui.IsItemHovered() || seekHeld);
            FadeGrab(ref _seekGrabAlpha, seekHover);
            DrawThinTrack(seekMin, seekMax, _seekValue, _seekGrabAlpha);

            ImGui.SameLine(0, 8f);
            bool volumeOn = CanControl() && _plugin.HasPlayableScreen();
            if (!volumeOn) ImGui.BeginDisabled();
            DrawMute();
            ImGui.SameLine(0, 8f);
            DrawVolume(VolumeTrackWidth);
            if (!volumeOn) ImGui.EndDisabled();
        }

        private static void FadeGrab(ref float alpha, bool visible)
        {
            float target = visible ? 1f : 0f;
            float step = ImGui.GetIO().DeltaTime / 0.16f;
            if (alpha < target)
                alpha = Math.Min(target, alpha + step);
            else
                alpha = Math.Max(target, alpha - step);
        }

        private void DrawThinTrack(Vector2 min, Vector2 max, float t, float grabAlpha)
        {
            float midY = (min.Y + max.Y) * 0.5f;
            float trackH = Math.Clamp(ImGui.GetFontSize() * 0.22f, 3f, 5f);
            float grabH = Math.Max(trackH + 4f, ImGui.GetFontSize() * 0.55f);
            float grabW = grabH * 0.72f;
            var trackMin = new Vector2(min.X, midY - trackH * 0.5f);
            var trackMax = new Vector2(max.X, midY + trackH * 0.5f);
            var drawList = ImGui.GetWindowDrawList();
            uint track = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.22f));
            drawList.AddRectFilled(trackMin, trackMax, track, trackH * 0.5f);

            t = Math.Clamp(t, 0f, 1f);
            float playX = min.X + (max.X - min.X) * t;
            if (playX - trackMin.X >= 2f)
            {
                var colors = ImGui.GetStyle().Colors;
                Vector4 fill = _chromePushed
                    ? PlaybackPanelChrome.Accent
                    : colors[(int)ImGuiCol.SliderGrab];
                PlaybackPanelChrome.DrawTrackFill(trackMin, new Vector2(playX, trackMax.Y), fill);
            }

            if (grabAlpha <= 0.01f)
                return;

            float grabX = Math.Clamp(playX, min.X + grabW * 0.5f, max.X - grabW * 0.5f);
            var grabMin = new Vector2(grabX - grabW * 0.5f, midY - grabH * 0.5f);
            var grabMax = new Vector2(grabX + grabW * 0.5f, midY + grabH * 0.5f);
            bool held = ImGui.IsItemActive();
            var grabColors = ImGui.GetStyle().Colors;
            Vector4 grab = grabColors[(int)(held ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab)];
            grab.W *= grabAlpha;
            drawList.AddRectFilled(grabMin, grabMax, ImGui.GetColorU32(grab), grabH * 0.5f);
        }

        private void DrawControls()
        {
            bool enabled = CanControl() && _plugin.HasPlayableScreen();
            if (!enabled) ImGui.BeginDisabled();
            DrawTransport(ControlGap);
            ImGui.SameLine(0, ControlGap);
            DrawToggles(ControlGap);
            if (!enabled) ImGui.EndDisabled();
            ImGui.SameLine(0, ClusterGap);
            DrawStatusCluster();
            float contentLeft = ImGui.GetWindowPos().X + ImGui.GetCursorStartPos().X;
            _controlsWidth = ImGui.GetItemRectMax().X - contentLeft;
        }

        private void DrawStatusCluster()
        {
            float clockW = PlaybackClockSlotWidth();
            DrawRowSeparator();
            ImGui.SameLine(0, ClusterGap);
            DrawPlaybackClock(clockW);
            ImGui.SameLine(0, ClusterGap);
            DrawRowSeparator();
            ImGui.SameLine(0, ClusterGap);
            DrawTitleTools(ControlGap);
        }

        private static void DrawRowSeparator()
        {
            float height = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(1f, height));
            var min = ImGui.GetItemRectMin();
            float inset = Math.Max(5f, height * 0.28f);
            uint color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.28f));
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(min.X, min.Y + inset),
                new Vector2(min.X, min.Y + height - inset),
                color,
                1f);
        }

        private float PlaybackClockSlotWidth()
        {
            ReadPlaybackClock(out long displayMs, out long durationMs);
            bool hours = TimeSpan.FromMilliseconds(displayMs).Hours > 0
                || TimeSpan.FromMilliseconds(durationMs).Hours > 0;
            return ImGui.CalcTextSize(hours ? "00:00:00 / 00:00:00" : "00:00 / 00:00").X;
        }

        private void ReadPlaybackClock(out long displayMs, out long durationMs)
        {
            var active = _plugin.MediaManager?.ActiveStream;
            durationMs = _plugin.GetPlaybackDurationMs();
            bool hasDuration = active != null && durationMs > 0;
            if (!hasDuration)
                durationMs = 0;
            displayMs = hasDuration && _seekDrag
                ? (long)(_seekValue * durationMs)
                : active?.Time ?? 0;
        }

        private void DrawPlaybackClock(float slotWidth)
        {
            ReadPlaybackClock(out long displayMs, out long durationMs);
            string text = FormatTime(displayMs) + " / " + FormatTime(durationMs);
            float frame = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(slotWidth, frame));
            var min = ImGui.GetItemRectMin();
            var textSize = ImGui.CalcTextSize(text);
            var pos = new Vector2(min.X + Math.Max(0f, (slotWidth - textSize.X) * 0.5f), min.Y + (frame - textSize.Y) * 0.5f);
            ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
        }

        private void DrawTransport(float gap)
        {
            int step = _plugin.Config.SeekIncrementSeconds;
            var active = _plugin.MediaManager?.ActiveStream;
            bool playing = active != null && active.PlaybackState == PlaybackState.Playing && !_plugin.IsIntentionallyPaused;

            if (StartPlaybackButton(FontAwesomeIcon.StepBackward, "prev", "上一首", false))
                _plugin.PlayPrevious();
            ImGui.SameLine(0, gap);
            if (IconButton(FontAwesomeIcon.Backward, "back", $"后退 {step} 秒"))
                _plugin.SeekRelative(-step);
            ImGui.SameLine(0, gap);
            if (playing)
            {
                if (IconButton(FontAwesomeIcon.Pause, "play", "暂停"))
                    _plugin.TogglePlayPause();
            }
            else if (StartPlaybackButton(FontAwesomeIcon.Play, "play", "播放", false))
            {
                _plugin.TogglePlayPause();
            }
            ImGui.SameLine(0, gap);
            if (IconButton(FontAwesomeIcon.Forward, "fwd", $"快进 {step} 秒"))
                _plugin.SeekRelative(step);
            ImGui.SameLine(0, gap);
            if (StartPlaybackButton(FontAwesomeIcon.StepForward, "next", "下一首", false))
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

            bool crop = _plugin.Config.CropToFill;
            if (IconButton(FontAwesomeIcon.Crop, "crop", crop ? "裁切：开，铺满屏幕" : "裁切：关，完整显示", crop, ToggleOn))
            {
                _plugin.Config.CropToFill = !crop;
                _plugin.Config.SaveLocal();
            }
        }

        private static float VolumeIconSlotWidth()
        {
            float glyph = Math.Max(
                IconGlyphWidth(FontAwesomeIcon.VolumeUp),
                IconGlyphWidth(FontAwesomeIcon.VolumeMute));
            return glyph * VolumeIconScale + 4f;
        }

        private void DrawMute()
        {
            float height = ImGui.GetFrameHeight();
            bool pressed = ImGui.InvisibleButton("##mute", new Vector2(VolumeIconSlotWidth(), height));
            bool hover = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            if (hover)
            {
                float wash = ImGui.IsItemActive() ? 0.14f : 0.08f;
                ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, wash)), 3f);
                ImGui.SetTooltip(_plugin.IsMuted ? "取消静音" : "静音");
            }

            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            if (handle.Available)
            {
                using (handle.Push())
                {
                    ImGui.SetWindowFontScale(VolumeIconScale);
                    string glyph = (_plugin.IsMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp).ToIconString();
                    var size = ImGui.CalcTextSize(glyph);
                    var pos = new Vector2((min.X + max.X - size.X) * 0.5f, (min.Y + max.Y - size.Y) * 0.5f);
                    var color = hover ? new Vector4(0.92f, 0.92f, 0.94f, 1f) : VolumeIcon;
                    ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, ImGui.GetColorU32(color), glyph);
                    ImGui.SetWindowFontScale(1f);
                }
            }

            if (pressed)
                _plugin.ToggleMute();
        }

        private void DrawVolume(float sliderWidth)
        {
            var manager = _plugin.MediaManager;
            if (manager == null) return;

            float volume = manager.LiveStreamVolume;
            ImGui.InvisibleButton("##volume", new Vector2(sliderWidth, ImGui.GetFrameHeight()));
            bool held = ImGui.IsItemActive();
            bool hover = ImGui.IsItemHovered();
            if (hover)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            if (held)
            {
                _volumeDrag = true;
                float span = Math.Max(1f, max.X - min.X);
                volume = Math.Clamp((ImGui.GetIO().MousePos.X - min.X) / span, 0f, 1f) * 3f;
                manager.LiveStreamVolume = volume;
            }
            if (ImGui.IsItemDeactivated() && _volumeDrag)
            {
                _volumeDrag = false;
                _plugin.Config.LivestreamVolume = manager.LiveStreamVolume;
                _plugin.Config.Save();
            }

            if (hover)
                ImGui.SetTooltip($"音量 {manager.LiveStreamVolume:0.00}");
            FadeGrab(ref _volumeGrabAlpha, hover || held);
            DrawThinTrack(min, max, manager.LiveStreamVolume / 3f, _volumeGrabAlpha);
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
            if (ImGui.BeginTabItem($"播放列表 ({queue.Length})###xmp-playlist"))
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
            if (StartPlaybackButton(FontAwesomeIcon.Play, "url-play", "播放链接", true))
                _plugin.PlayMediaUrl(_urlInput);
            ImGui.SameLine(0, CompactGap);
            if (StartPlaybackButton(FontAwesomeIcon.Paste, "url-paste", "粘贴并播放", true))
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
            bool localMode = _plugin.IsLocalPlaybackMode;
            bool canPlayFile = localMode && _plugin.HasPlayableScreen();
            string fileTip = !localMode
                ? "同步模式不能播放本地视频"
                : canPlayFile ? "播放本地文件" : NoScreenTip;
            if (!canPlayFile) ImGui.BeginDisabled();
            if (CompactIconButton(FontAwesomeIcon.Play, "file-play", fileTip))
                _plugin.PlayLocalFile(_filePath);
            if (!canPlayFile) ImGui.EndDisabled();
            ImGui.SameLine(0, CompactGap);
            if (!localMode) ImGui.BeginDisabled();
            if (CompactIconButton(FontAwesomeIcon.Plus, "file-queue", localMode ? "加入列表" : "同步模式不能添加本地视频"))
                _plugin.EnqueueMedia(_filePath);
            if (!localMode) ImGui.EndDisabled();
        }

        private void DrawQueue(string[] queue)
        {
            if (queue.Length == 0)
                ImGui.TextDisabled("播放列表是空的。");

            ImGui.BeginChild("##local-queue", new Vector2(0, 140), true);
            for (int i = 0; i < queue.Length; i++)
            {
                ImGui.PushID(i);
                bool current = IsCurrentQueueItem(i, queue[i]);
                if (current)
                    DrawCurrentRowMarker();
                if (StartPlaybackButton(FontAwesomeIcon.Play, "play", "播放", true))
                    _plugin.PlayQueuedMedia(i);
                ImGui.SameLine(0, CompactGap);
                if (CompactIconButton(FontAwesomeIcon.Trash, "remove", "移除"))
                    _plugin.RemoveQueuedMedia(i);
                ImGui.SameLine(0, CompactGap);
                DrawTaggedText(PlaylistLabel(queue[i]), queue[i], current);
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
                if (StartPlaybackButton(FontAwesomeIcon.Play, "play", "播放", true))
                    _plugin.PlayMediaUrl(entry.Url);
                ImGui.SameLine(0, CompactGap);
                if (!QueueContains(queue, entry.Url))
                {
                    bool blockLocalAdd = !_plugin.IsLocalPlaybackMode && LocalMediaPaths.IsLocalFile(entry.Url);
                    if (blockLocalAdd) ImGui.BeginDisabled();
                    if (CompactIconButton(FontAwesomeIcon.Plus, "add", blockLocalAdd ? "同步模式不能添加本地视频" : "加入列表"))
                        _plugin.EnqueueMedia(entry.Url);
                    if (blockLocalAdd) ImGui.EndDisabled();
                    ImGui.SameLine(0, CompactGap);
                }
                string label = string.IsNullOrEmpty(entry.Title) ? entry.Url : entry.Title;
                DrawTaggedText(label, entry.Url);
                ImGui.PopID();
            }
            ImGui.EndChild();
        }

        private bool IsCurrentQueueItem(int index, string url)
        {
            if (_plugin.MediaManager?.ActiveStream == null)
                return false;
            string playing = _plugin.LastStreamURL;
            if (string.IsNullOrEmpty(playing) || !SameMedia(playing, url))
                return false;
            int currentIndex = _plugin.CurrentLocalPlaylistIndex;
            return currentIndex == index;
        }

        private string PlaylistLabel(string url)
        {
            if (TryCurrentTitle(url, out string current))
                return current;
            if (TryHistoryTitle(url, out string history))
                return history;
            if (LocalMediaPaths.IsLocalFile(url))
            {
                string name = Path.GetFileName(LocalMediaPaths.Normalize(url));
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return url;
        }

        private bool TryCurrentTitle(string url, out string title)
        {
            title = "";
            string playing = _plugin.LastStreamURL;
            if (string.IsNullOrEmpty(playing) || !SameMedia(playing, url))
                return false;
            return DistinctTitle(_plugin.PlayingMediaTitle, url, out title);
        }

        private bool TryHistoryTitle(string url, out string title)
        {
            title = "";
            var history = _plugin.Config.WatchHistory;
            if (history == null || history.Count == 0)
                return false;
            if (history.TryGetValue(url, out var exact) && DistinctTitle(exact.Title, url, out title))
                return true;

            foreach (var entry in history.Values)
            {
                if (entry == null || !SameMedia(entry.Url, url))
                    continue;
                if (DistinctTitle(entry.Title, url, out title))
                    return true;
            }

            return false;
        }

        private static bool DistinctTitle(string title, string url, out string shown)
        {
            shown = title?.Trim() ?? "";
            if (shown.Length == 0 || shown == "Loading...")
                return false;
            return !SameMedia(shown, url);
        }

        private static bool SameMedia(string left, string right)
            => string.Equals(MediaKey(left ?? ""), MediaKey(right ?? ""), StringComparison.OrdinalIgnoreCase);

        private Vector4 CurrentAccent()
        {
            Vector4 color = _chromePushed
                ? PlaybackPanelChrome.Accent
                : ImGui.GetStyle().Colors[(int)ImGuiCol.SliderGrab];
            color.W = 1f;
            return color;
        }

        private void DrawCurrentRowMarker()
        {
            float height = ImGui.GetFrameHeight();
            var min = ImGui.GetCursorScreenPos();
            float width = Math.Max(0f, ImGui.GetContentRegionAvail().X);
            var max = new Vector2(min.X + width, min.Y + height);
            var accent = CurrentAccent();
            var fill = accent;
            fill.W = 0.18f;
            var list = ImGui.GetWindowDrawList();
            list.AddRectFilled(min, max, ImGui.GetColorU32(fill), 3f);
            list.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), ImGui.GetColorU32(accent), 2f);
        }

        private void DrawTaggedText(string text, string url, bool current = false)
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
            else if (current)
                ImGui.TextColored(CurrentAccent(), shown);
            else
                ImGui.TextUnformatted(shown);
            bool titled = !string.Equals(text, url, StringComparison.OrdinalIgnoreCase);
            if (ImGui.IsItemHovered() && (missing || titled || shown != text))
            {
                string tip = text;
                if (titled)
                    tip = text + "\n" + url;
                if (missing)
                    tip += "\n此文件已经不存在。";
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

        private const string NoScreenTip = "当前区域没有屏幕，无法播放。";

        private bool StartPlaybackButton(FontAwesomeIcon icon, string id, string tooltip, bool compact)
        {
            bool blocked = !_plugin.HasPlayableScreen();
            if (blocked)
                ImGui.BeginDisabled();
            bool pressed = compact
                ? CompactIconButton(icon, id, blocked ? NoScreenTip : tooltip)
                : IconButton(icon, id, blocked ? NoScreenTip : tooltip);
            if (blocked)
                ImGui.EndDisabled();
            return !blocked && pressed;
        }

        private bool IconButton(FontAwesomeIcon icon, string id, string tooltip, bool highlight = false, Vector4? color = null, Vector4? hoveredColor = null)
        {
            Vector4? face = highlight ? color : null;
            Vector4? hovered = null;
            if (highlight)
                hovered = hoveredColor ?? (color.HasValue ? Lighten(color.Value) : null);
            bool pressed = ImGuiComponents.IconButton(id, icon, face, face, hovered);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(tooltip);
            return pressed;
        }

        private bool CompactIconButton(FontAwesomeIcon icon, string id, string tooltip, bool highlight = false, Vector4? color = null, Vector4? hoveredColor = null)
        {
            var padding = ImGui.GetStyle().FramePadding;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(CompactPadX, padding.Y));
            bool pressed = IconButton(icon, id, tooltip, highlight, color, hoveredColor);
            ImGui.PopStyleVar();
            return pressed;
        }

        private void FitIconField(int buttonCount)
        {
            float reserved = buttonCount * CompactButtonWidth() + buttonCount * CompactGap;
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - reserved));
        }

        private float CompactButtonWidth() => IconWidth() + CompactPadX * 2f;

        private float CompactIconWidth(FontAwesomeIcon icon) => IconGlyphWidth(icon) + CompactPadX * 2f;

        private float IconWidth() => IconGlyphWidth(FontAwesomeIcon.Play);

        private static float IconGlyphWidth(FontAwesomeIcon icon)
        {
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            if (!handle.Available)
                return ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.Y * 2f;
            using (handle.Push())
                return ImGui.CalcTextSize(icon.ToIconString()).X;
        }

        private float ModeSwitchWidth()
        {
            if (_chromePushed)
                return Math.Max(PlaybackPanelChrome.PillWidth("本地"), PlaybackPanelChrome.PillWidth("同步"));

            float text = Math.Max(ImGui.CalcTextSize("本地").X, ImGui.CalcTextSize("同步").X);
            return text + ImGui.GetStyle().FramePadding.X * 2f;
        }

        private bool CanControl() => _plugin.IsRoomSyncPaused || _plugin.CanControlCurrentTvPlayback();

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
                    _filePath = paths[0];
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
