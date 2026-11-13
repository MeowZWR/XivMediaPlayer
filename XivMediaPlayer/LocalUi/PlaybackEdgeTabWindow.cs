using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class PlaybackEdgeTabWindow : Window
    {
        private const float MarqueeSecondsPerLine = 0.3f;
        private const float MarqueePad = 4f;
        private const float ChipPad = 3f;
        private const float IconGlyphScale = 0.75f;
        private const float TextNarrower = 8f;
        private const float TooltipGap = 8f;
        private const float TransportSeconds = 0.2f;

        private readonly Plugin _plugin;
        private readonly ImGuiWindowFlags _baseFlags;
        private readonly List<GlyphRun> _glyphs = new();
        private bool _dragging;
        private bool _suppressClick;
        private bool _chromePushed;
        private bool _pushedAlpha;
        private bool _transportOpen;
        private bool _transportShown;
        private bool _transportFrame;
        private float _transportPos;
        private float _transportReveal;
        private double _transportClock;
        private Vector2 _pressPos;
        private Vector2 _iconMin;
        private Vector2 _iconMax;
        private Vector2 _transportMin;
        private Vector2 _transportMax;
        private string _marqueeTitle = "";
        private double _marqueeStamp;
        private float _marqueeOffset;

        public PlaybackEdgeTabWindow(Plugin plugin)
            : base(
                "###xmp-playback-edge",
                ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.NoFocusOnAppearing,
                false)
        {
            _plugin = plugin;
            _baseFlags = Flags;
            RespectCloseHotkey = false;
        }

        public static Vector2 CollapsedWindowPos(Vector2 windowSize, PlaybackScreenEdge edge, float anchor, float screenMargin)
        {
            var viewport = ImGui.GetMainViewport();
            EdgePlacement(viewport.Pos, viewport.Size, edge, anchor, screenMargin, out Vector2 pos, out Vector2 pivot);
            return pos - pivot * windowSize;
        }

        public override bool DrawConditions()
            => !_plugin.GameUiHidden;

        public override void PreDraw()
        {
            if (!_transportFrame)
                _transportOpen = false;
            _transportFrame = false;
            if (_dragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                FollowPointer();
            _transportShown = _transportOpen && !_dragging;
            AdvanceTransport(_transportShown);
            _chromePushed = _plugin.Config.PlaybackLightlessStyle;
            AllowBackgroundBlur = false;
            Flags = _baseFlags;
            if (_plugin.PlaybackDockMoving && _plugin.PlaybackEdgeReveal < 0.99f)
                Flags |= ImGuiWindowFlags.NoInputs;
            if (_chromePushed)
                PlaybackPanelChrome.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            _pushedAlpha = _plugin.PlaybackDockMoving;
            if (_pushedAlpha)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _plugin.PlaybackEdgeReveal);
            PlaceOnEdge();
            base.PreDraw();
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (_pushedAlpha)
                ImGui.PopStyleVar();
            ImGui.PopStyleVar();
            if (_chromePushed)
                PlaybackPanelChrome.Pop();
        }

        public override void Draw()
        {
            bool split = TryGetTitleLayout(out TitleLayout layout);
            if (!split)
                layout = IconOnlyLayout();

            Span<int> actions = stackalloc int[EdgeTransportCatalog.Count];
            int actionCount = EdgeTransportCatalog.CollectVisible(_plugin.Config, actions);
            var origin = ImGui.GetCursorPos();
            ContentSlots(layout, TitleIconGap(), _transportReveal, actionCount, out Vector2 content, out Vector2 textOffset, out Vector2 iconOffset, out Vector2 transportOffset);
            ImGui.Dummy(content);
            bool showTransport = actionCount > 0 && _transportReveal > 0f;
            if (split)
                DrawEdgeTitle(layout, origin + textOffset);
            if (showTransport)
                DrawTransportCapsule(origin, iconOffset, transportOffset, layout, actionCount);
            bool iconHover = DrawEdgeIcon(origin + iconOffset, layout);
            bool transportEngaged = showTransport && DrawTransport(origin + transportOffset, layout, actions, actionCount);
            FinishPointer();
            _transportFrame = true;
            _transportOpen = actionCount > 0 && !_dragging && (iconHover || transportEngaged || PointerOnTransportSpan());
        }

        private void FollowPointer()
        {
            var viewport = ImGui.GetMainViewport();
            var mouse = ImGui.GetMousePos();
            float localX = mouse.X - viewport.Pos.X;
            float localY = mouse.Y - viewport.Pos.Y;
            float fromLeft = localX;
            float fromRight = viewport.Size.X - localX;
            float fromTop = localY;
            float fromBottom = viewport.Size.Y - localY;

            PlaybackScreenEdge edge = PlaybackScreenEdge.Left;
            float nearest = fromLeft;
            if (fromRight < nearest)
            {
                nearest = fromRight;
                edge = PlaybackScreenEdge.Right;
            }
            if (fromTop < nearest)
            {
                nearest = fromTop;
                edge = PlaybackScreenEdge.Top;
            }
            if (fromBottom < nearest)
                edge = PlaybackScreenEdge.Bottom;

            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            float along = vertical ? localY : localX;
            float length = vertical ? viewport.Size.Y : viewport.Size.X;
            GetEdgeRange(length, out float start, out float end);
            _plugin.Config.PlaybackEdgeSide = edge;
            _plugin.Config.PlaybackEdgeAnchor = Math.Clamp((along - start) / Math.Max(1f, end - start), 0f, 1f);
        }

        private void PlaceOnEdge()
        {
            var viewport = ImGui.GetMainViewport();
            PlaybackScreenEdge edge = NormalizeEdge(_plugin.Config.PlaybackEdgeSide);
            EdgePlacement(viewport.Pos, viewport.Size, edge, _plugin.Config.PlaybackEdgeAnchor, ScreenMargin(), out Vector2 anchor, out _);
            if (!TryGetTitleLayout(out TitleLayout layout))
                layout = IconOnlyLayout();
            WindowFrame(layout, out Vector2 windowSize, out Vector2 pivot);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0f);
            ImGui.SetNextWindowPos(anchor - pivot * windowSize, ImGuiCond.Always);
        }

        private string EdgeTitle()
        {
            if (!_plugin.Config.ShowEdgePlaybackTitle)
                return "";
            string title = DisplayTitle();
            if (LocalMediaPaths.TrySplitLocalName(title, _plugin.RememberedMediaUrl, out string name, out _))
                return name;
            return title;
        }

        private bool TryGetTitleLayout(out TitleLayout layout)
        {
            layout = default;
            string title = EdgeTitle();
            if (title.Length == 0)
                return false;

            PlaybackScreenEdge edge = NormalizeEdge(_plugin.Config.PlaybackEdgeSide);
            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            bool onStartSide = IconOnStartSide(edge);
            MeasurePair(title, vertical, out Vector2 text, out Vector2 iconChip);
            layout = new TitleLayout(title, edge, vertical, textFirst: !onStartSide, scrollPositive: onStartSide, text, iconChip);
            return true;
        }

        private TitleLayout IconOnlyLayout()
        {
            string title = DisplayTitle();
            if (title.Length == 0)
                title = "国";
            PlaybackScreenEdge edge = NormalizeEdge(_plugin.Config.PlaybackEdgeSide);
            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            bool onStartSide = IconOnStartSide(edge);
            MeasurePair(title, vertical, out _, out Vector2 iconChip);
            return new TitleLayout("", edge, vertical, textFirst: !onStartSide, scrollPositive: onStartSide, default, iconChip);
        }

        private string DisplayTitle()
        {
            if (!_plugin.HasPlayableScreen())
                return "";
            return MediaTitle();
        }

        private string MediaTitle()
        {
            string title = _plugin.CurrentMediaTitle;
            return string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
        }

        private void MeasurePair(string title, bool vertical, out Vector2 textSize, out Vector2 iconChip)
        {
            float iconW = EdgeIconWidth();
            float iconH = ImGui.GetFrameHeight();
            float iconAlong = (vertical ? iconH : iconW) + ChipPad * 2f;
            float textCross = (vertical ? MarqueeThickness(title, true) : ImGui.GetTextLineHeight()) + MarqueePad * 2f;
            float iconCross = textCross;
            if (vertical && iconAlong <= iconCross)
                iconAlong = iconCross + TextNarrower;
            float along = MarqueeLength() + MarqueePad * 2f;
            textSize = vertical ? new Vector2(textCross, along) : new Vector2(along, textCross);
            iconChip = vertical ? new Vector2(iconCross, iconAlong) : new Vector2(iconAlong, iconCross);
        }

        private static void ContentSlots(TitleLayout layout, float titleGap, float transportReveal, int transportSlots, out Vector2 content, out Vector2 textOffset, out Vector2 iconOffset, out Vector2 transportOffset)
        {
            textOffset = Vector2.Zero;
            iconOffset = Vector2.Zero;
            transportOffset = Vector2.Zero;

            bool hasTitle = layout.TextSize != Vector2.Zero;
            var transportSize = RevealedStripSize(layout.IconChip, layout.Vertical, transportReveal, transportSlots);
            float transportAlong = layout.Vertical ? transportSize.Y : transportSize.X;
            bool showTransport = transportAlong > 0f;
            float titleAlong = layout.Vertical ? layout.TextSize.Y : layout.TextSize.X;
            float iconAlong = layout.Vertical ? layout.IconChip.Y : layout.IconChip.X;

            float textAt;
            float transportAt;
            float iconAt;
            float total;
            if (layout.TextFirst)
            {
                float cursor = 0f;
                textAt = cursor;
                if (hasTitle)
                    cursor += titleAlong + titleGap;
                transportAt = cursor;
                if (showTransport)
                    cursor += transportAlong;
                iconAt = cursor;
                total = cursor + iconAlong;
            }
            else
            {
                iconAt = 0f;
                transportAt = iconAlong;
                float cursor = iconAlong;
                if (showTransport)
                    cursor += transportAlong;
                if (hasTitle)
                    cursor += titleGap;
                textAt = cursor;
                total = hasTitle ? cursor + titleAlong : cursor;
            }

            float cross = layout.Vertical
                ? Math.Max(layout.IconChip.X, hasTitle ? layout.TextSize.X : 0f)
                : Math.Max(layout.IconChip.Y, hasTitle ? layout.TextSize.Y : 0f);

            if (layout.Vertical)
            {
                content = new Vector2(cross, total);
                textOffset = new Vector2(hasTitle ? (cross - layout.TextSize.X) * 0.5f : 0f, textAt);
                iconOffset = new Vector2((cross - layout.IconChip.X) * 0.5f, iconAt);
                transportOffset = new Vector2((cross - transportSize.X) * 0.5f, transportAt);
                return;
            }

            content = new Vector2(total, cross);
            textOffset = new Vector2(textAt, hasTitle ? (cross - layout.TextSize.Y) * 0.5f : 0f);
            iconOffset = new Vector2(iconAt, (cross - layout.IconChip.Y) * 0.5f);
            transportOffset = new Vector2(transportAt, (cross - transportSize.Y) * 0.5f);
        }

        private bool IconOnStartSide(PlaybackScreenEdge edge)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.Pos + viewport.Size * 0.5f;
            EdgePlacement(viewport.Pos, viewport.Size, edge, _plugin.Config.PlaybackEdgeAnchor, ScreenMargin(), out Vector2 pos, out _);
            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            return vertical ? pos.Y <= center.Y : pos.X <= center.X;
        }

        private void WindowFrame(TitleLayout layout, out Vector2 windowSize, out Vector2 pivot)
        {
            var style = ImGui.GetStyle();
            float padX = style.WindowPadding.X;
            float padY = style.WindowPadding.Y;
            Span<int> visible = stackalloc int[EdgeTransportCatalog.Count];
            ContentSlots(layout, TitleIconGap(), _transportReveal, EdgeTransportCatalog.CollectVisible(_plugin.Config, visible), out Vector2 content, out _, out Vector2 iconOffset, out _);
            windowSize = new Vector2(
                Math.Max(1f, content.X + padX * 2f),
                Math.Max(1f, content.Y + padY * 2f));
            float along = layout.Vertical
                ? (padY + iconOffset.Y + layout.IconChip.Y * 0.5f) / windowSize.Y
                : (padX + iconOffset.X + layout.IconChip.X * 0.5f) / windowSize.X;
            float outer = layout.Vertical
                ? (padX + iconOffset.X + (layout.Edge == PlaybackScreenEdge.Right ? layout.IconChip.X : 0f)) / windowSize.X
                : (padY + iconOffset.Y + (layout.Edge == PlaybackScreenEdge.Bottom ? layout.IconChip.Y : 0f)) / windowSize.Y;
            pivot = layout.Vertical
                ? new Vector2(outer, along)
                : new Vector2(along, outer);
        }

        private bool DrawEdgeIcon(Vector2 cursor, TitleLayout layout)
        {
            ImGui.SetCursorPos(cursor);
            var chipMin = ImGui.GetCursorScreenPos();
            var chipMax = chipMin + layout.IconChip;
            _iconMin = chipMin;
            _iconMax = chipMax;
            bool clicked = ImGui.InvisibleButton("##edge-icon", layout.IconChip);
            bool hovered = ImGui.IsItemHovered();
            NotePointer(clicked);
            NoteTitleToggle();
            if (_transportReveal <= 0f)
            {
                DrawRegion(chipMin, chipMax, raised: true);
                if (_chromePushed)
                {
                    var inset = new Vector2(2f, 2f);
                    PlaybackPanelChrome.DrawEdgeAtmosphere(chipMin + inset, chipMax - inset);
                }
            }
            DrawIconGlyph(FontAwesomeIcon.Video, chipMin, chipMax, hovered, true);
            if (hovered && !_dragging)
                ShowEdgeTooltip(EdgeTooltip(), chipMin, chipMax, layout.Edge);
            return hovered;
        }

        private void AdvanceTransport(bool open)
        {
            double now = ImGui.GetTime();
            float dt = _transportClock <= 0 ? 0.016f : (float)Math.Min(now - _transportClock, 0.05);
            _transportClock = now;
            float target = open ? 1f : 0f;
            float step = dt / TransportSeconds;
            _transportPos = _transportPos < target
                ? Math.Min(target, _transportPos + step)
                : Math.Max(target, _transportPos - step);
            float t = _transportPos;
            _transportReveal = t * t * (3f - 2f * t);
        }

        private bool DrawTransport(Vector2 cursor, TitleLayout layout, ReadOnlySpan<int> actions, int actionCount)
        {
            if (actionCount <= 0)
                return false;

            var button = layout.IconChip;
            bool vertical = layout.Vertical;
            bool playing = EdgePlaybackActive();
            bool noScreen = !_plugin.HasPlayableScreen();
            bool engaged = false;
            var stripSize = RevealedStripSize(button, vertical, _transportReveal, actionCount);
            float buttonAlong = vertical ? button.Y : button.X;
            float revealedAlong = vertical ? stripSize.Y : stripSize.X;

            ImGui.SetCursorPos(cursor);
            var stripMin = ImGui.GetCursorScreenPos();
            var stripMax = stripMin + stripSize;
            _transportMin = stripMin;
            _transportMax = stripMax;

            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(stripMin, stripMax, true);
            for (int slot = 0; slot < actionCount; slot++)
            {
                int action = actions[slot];
                bool blocked = noScreen && EdgeTransportCatalog.RequiresScreen(action);
                float start = layout.TextFirst
                    ? revealedAlong - (slot + 1) * buttonAlong
                    : slot * buttonAlong;
                float visStart = Math.Max(0f, start);
                float visEnd = Math.Min(revealedAlong, start + buttonAlong);
                float visible = (visEnd - visStart) / buttonAlong;
                if (visible <= 0f)
                    continue;

                var fullMin = stripMin + (vertical ? new Vector2(0f, start) : new Vector2(start, 0f));
                var fullMax = fullMin + button;
                var icon = EdgeTransportCatalog.RuntimeIcon(_plugin, action, playing);
                var tint = EdgeTransportCatalog.RuntimeTint(_plugin, action);
                if (visible < 0.45f)
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * (visible / 0.45f));
                    DrawIconGlyph(icon, fullMin, fullMax, false, !blocked, tint);
                    ImGui.PopStyleVar();
                    continue;
                }

                var hitSize = vertical
                    ? new Vector2(button.X, visEnd - visStart)
                    : new Vector2(visEnd - visStart, button.Y);
                ImGui.SetCursorPos(cursor + (vertical ? new Vector2(0f, visStart) : new Vector2(visStart, 0f)));
                if (blocked)
                    ImGui.BeginDisabled();
                bool clicked = ImGui.InvisibleButton($"##edge-transport-{action}", hitSize);
                bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem | ImGuiHoveredFlags.AllowWhenDisabled);
                if (blocked)
                    ImGui.EndDisabled();

                engaged |= hovered || ImGui.IsItemActive();
                DrawIconGlyph(icon, fullMin, fullMax, hovered, !blocked, tint);
                if (hovered && !_dragging)
                    ShowEdgeTooltip(EdgeTransportCatalog.RuntimeTip(_plugin, action, playing, blocked), fullMin, fullMax, layout.Edge);
                if (clicked && !blocked)
                    EdgeTransportCatalog.Run(_plugin, action);
            }

            drawList.PopClipRect();
            return engaged;
        }

        private bool EdgePlaybackActive()
        {
            var active = _plugin.MediaManager?.ActiveStream;
            return active != null && active.PlaybackState == PlaybackState.Playing && !_plugin.IsIntentionallyPaused;
        }

        private bool PointerOnTransportSpan()
        {
            if (!_transportShown || !ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                return false;

            var mouse = ImGui.GetMousePos();
            var min = Vector2.Min(_iconMin, _transportMin);
            var max = Vector2.Max(_iconMax, _transportMax);
            return mouse.X >= min.X && mouse.Y >= min.Y && mouse.X < max.X && mouse.Y < max.Y;
        }

        private static Vector2 TransportStripSize(Vector2 button, bool vertical, int slots)
        {
            slots = Math.Max(0, slots);
            if (vertical)
                return new Vector2(button.X, button.Y * slots);
            return new Vector2(button.X * slots, button.Y);
        }

        private static Vector2 RevealedStripSize(Vector2 button, bool vertical, float reveal, int slots)
        {
            var full = TransportStripSize(button, vertical, slots);
            reveal = Math.Clamp(reveal, 0f, 1f);
            if (vertical)
                return new Vector2(full.X, full.Y * reveal);
            return new Vector2(full.X * reveal, full.Y);
        }

        private void DrawTransportCapsule(Vector2 origin, Vector2 iconOffset, Vector2 transportOffset, TitleLayout layout, int slots)
        {
            ImGui.SetCursorPos(origin + iconOffset);
            var iconMin = ImGui.GetCursorScreenPos();
            var iconMax = iconMin + layout.IconChip;
            var strip = RevealedStripSize(layout.IconChip, layout.Vertical, _transportReveal, slots);
            ImGui.SetCursorPos(origin + transportOffset);
            var transportMin = ImGui.GetCursorScreenPos();
            var min = Vector2.Min(iconMin, transportMin);
            var max = Vector2.Max(iconMax, transportMin + strip);
            DrawRegion(min, max, raised: true);
            if (_chromePushed)
            {
                var pad = new Vector2(2f, 2f);
                PlaybackPanelChrome.DrawEdgeAtmosphere(min + pad, max - pad);
            }
            float cross = Math.Min(max.X - min.X, max.Y - min.Y);

            bool transportAfter = !layout.TextFirst;
            float split = layout.Vertical
                ? (transportAfter ? iconMax.Y : iconMin.Y)
                : (transportAfter ? iconMax.X : iconMin.X);
            float inset = Math.Min(6f, cross * 0.22f);
            uint color = ImGui.GetColorU32(ImGuiCol.Separator);
            var list = ImGui.GetWindowDrawList();
            if (layout.Vertical)
                list.AddLine(new Vector2(min.X + inset, split), new Vector2(max.X - inset, split), color, 1f);
            else
                list.AddLine(new Vector2(split, min.Y + inset), new Vector2(split, max.Y - inset), color, 1f);
        }

        private static void ShowEdgeTooltip(string text, Vector2 anchorMin, Vector2 anchorMax, PlaybackScreenEdge edge)
        {
            var style = ImGui.GetStyle();
            var textSize = ImGui.CalcTextSize(text);
            float border = style.WindowBorderSize;
            var tipSize = textSize + style.WindowPadding * 2f + new Vector2(border, border) * 2f;
            var center = (anchorMin + anchorMax) * 0.5f;
            Vector2 pos;
            Vector2 pivot;
            switch (NormalizeEdge(edge))
            {
                case PlaybackScreenEdge.Right:
                    pos = new Vector2(anchorMin.X - TooltipGap, center.Y);
                    pivot = new Vector2(1f, 0.5f);
                    break;
                case PlaybackScreenEdge.Top:
                    pos = new Vector2(center.X, anchorMax.Y + TooltipGap);
                    pivot = new Vector2(0.5f, 0f);
                    break;
                case PlaybackScreenEdge.Bottom:
                    pos = new Vector2(center.X, anchorMin.Y - TooltipGap);
                    pivot = new Vector2(0.5f, 1f);
                    break;
                default:
                    pos = new Vector2(anchorMax.X + TooltipGap, center.Y);
                    pivot = new Vector2(0f, 0.5f);
                    break;
            }

            var viewport = ImGui.GetMainViewport();
            var viewMin = viewport.Pos + new Vector2(4f, 4f);
            var viewMax = viewport.Pos + viewport.Size - tipSize - new Vector2(4f, 4f);
            var topLeft = Vector2.Clamp(pos - pivot * tipSize, viewMin, Vector2.Max(viewMin, viewMax));
            ImGui.SetNextWindowPos(topLeft + pivot * tipSize, ImGuiCond.Always, pivot);
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(text);
            ImGui.EndTooltip();
        }

        private string EdgeTooltip()
        {
            string action = _plugin.Config.KeepEdgeIconWithPanel && _plugin.IsPlaybackPanelOpen
                ? "关闭播放控制面板。"
                : "打开播放控制面板。";
            return action + "\n按住可沿屏幕四边拖动。\nCtrl+右键切换滚动标题显示。";
        }

        private void NoteTitleToggle()
        {
            if (!ImGui.IsItemClicked(ImGuiMouseButton.Right) || !ImGui.GetIO().KeyCtrl)
                return;
            _plugin.Config.ShowEdgePlaybackTitle = !_plugin.Config.ShowEdgePlaybackTitle;
            _plugin.Config.SaveLocal();
        }

        private static void DrawIconGlyph(FontAwesomeIcon icon, Vector2 min, Vector2 max, bool hovered, bool enabled, Vector4? tint = null)
        {
            string glyph = icon.ToIconString();
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            var drawList = ImGui.GetWindowDrawList();
            uint color;
            if (!enabled)
                color = ImGui.GetColorU32(ImGuiCol.TextDisabled);
            else if (tint.HasValue)
                color = ImGui.GetColorU32(hovered ? EdgeTransportCatalog.Lighten(tint.Value) : tint.Value);
            else
                color = ImGui.GetColorU32(hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Text);
            if (handle.Available)
                using (handle.Push())
                    DrawGlyph(drawList, glyph, min, max, color);
            else
                DrawGlyph(drawList, glyph, min, max, color);
        }

        private static void DrawGlyph(ImDrawListPtr drawList, string glyph, Vector2 min, Vector2 max, uint color)
        {
            ImGui.SetWindowFontScale(IconGlyphScale);
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(min + (max - min - glyphSize) * 0.5f, color, glyph);
            ImGui.SetWindowFontScale(1f);
        }

        private void DrawEdgeTitle(TitleLayout layout, Vector2 cursor)
        {
            ImGui.SetCursorPos(cursor);
            var origin = ImGui.GetCursorScreenPos();
            var size = layout.TextSize;
            bool showBackground = !_plugin.Config.HideEdgeTitleBackground;
            if (showBackground)
                DrawRegion(origin, origin + size, raised: true);
            bool clicked = ImGui.InvisibleButton("##edge-title", size);
            NotePointer(clicked);
            if (ImGui.IsItemHovered() && !_dragging)
            {
                string tip = DisplayTitle();
                if (tip.Length > 0)
                    ShowEdgeTooltip(tip, origin, origin + size, layout.Edge);
            }

            var inset = layout.Vertical
                ? new Vector2(MarqueePad * 0.5f, MarqueePad)
                : new Vector2(MarqueePad, MarqueePad * 0.5f);
            var min = origin + inset;
            var max = origin + size - inset;
            if (max.X <= min.X || max.Y <= min.Y)
                return;

            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(min, max, true);
            DrawScrollingText(drawList, layout.Title, min, max, layout.Vertical, layout.ScrollPositive);
            drawList.PopClipRect();
            if (showBackground && _chromePushed)
            {
                var pad = new Vector2(2f, 2f);
                PlaybackPanelChrome.DrawTitleAtmosphere(origin + pad, origin + size - pad);
            }
        }

        private void FinishPointer()
        {
            if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                return;
            if (_dragging)
                _plugin.Config.SaveLocal();
            _dragging = false;
            _suppressClick = false;
        }

        private void DrawRegion(Vector2 min, Vector2 max, bool raised)
        {
            if (_chromePushed)
            {
                PlaybackPanelChrome.DrawChip(min, max, raised);
                return;
            }

            var drawList = ImGui.GetWindowDrawList();
            float rounding = ImGui.GetStyle().FrameRounding;
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(raised ? ImGuiCol.FrameBg : ImGuiCol.Button), rounding);
            drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), rounding, ImDrawFlags.None, 1f);
        }

        private void NotePointer(bool clicked)
        {
            if (ImGui.IsItemActivated())
                _pressPos = ImGui.GetMousePos();
            if (ImGui.IsItemActive() && Vector2.Distance(ImGui.GetMousePos(), _pressPos) >= 4f)
            {
                _dragging = true;
                _suppressClick = true;
            }

            if (clicked && !_suppressClick)
                _plugin.TogglePlaybackPanelFromEdge();
        }

        private void DrawScrollingText(ImDrawListPtr drawList, string title, Vector2 min, Vector2 max, bool vertical, bool positive)
        {
            float line = ImGui.GetTextLineHeight();
            float extent = 0f;
            _glyphs.Clear();
            if (vertical)
            {
                foreach (var rune in title.EnumerateRunes())
                {
                    if (rune.Value is '\r' or '\n' or '\t')
                        continue;
                    string piece = rune.ToString();
                    var measured = ImGui.CalcTextSize(piece);
                    float along = Math.Max(measured.Y, line);
                    _glyphs.Add(new GlyphRun(piece, along, measured.X));
                    extent += along;
                }
            }
            else
            {
                extent = ImGui.CalcTextSize(title).X;
            }

            if (extent <= 0f)
                return;

            float view = vertical ? max.Y - min.Y : max.X - min.X;
            float gap = Math.Max(line * 2f, view * 0.45f);
            float cycle = Math.Max(1f, extent + gap);
            double now = ImGui.GetTime();
            if (!string.Equals(_marqueeTitle, title, StringComparison.Ordinal) || _marqueeStamp <= 0d)
            {
                _marqueeTitle = title;
                _marqueeStamp = now;
                _marqueeOffset = 0f;
            }
            else
            {
                float scale = Math.Clamp(_plugin.Config.EdgeTitleScrollSpeed, 0f, 3f);
                float dt = (float)Math.Min(now - _marqueeStamp, 0.05d);
                _marqueeStamp = now;
                if (scale > 0f)
                    _marqueeOffset = (_marqueeOffset + dt * (line / MarqueeSecondsPerLine) * scale) % cycle;
            }

            float offset = _marqueeOffset;
            float minAlong = vertical ? min.Y : min.X;
            float maxAlong = vertical ? max.Y : max.X;
            float blockStart = positive ? minAlong - extent + offset : maxAlong - offset;
            uint color = ImGui.GetColorU32(ImGuiCol.Text);
            int copies = Math.Max(3, (int)Math.Ceiling(view / cycle) + 2);
            float textY = min.Y + (max.Y - min.Y - line) * 0.5f;
            int vertexStart = drawList.VtxBuffer.Size;

            for (int copy = -1; copy <= copies; copy++)
            {
                float cursor = blockStart + copy * cycle;
                if (!vertical)
                {
                    if (cursor < maxAlong && cursor + extent > minAlong)
                        drawList.AddText(new Vector2(cursor, textY), color, title);
                    continue;
                }

                for (int i = 0; i < _glyphs.Count; i++)
                {
                    GlyphRun glyph = _glyphs[i];
                    float next = cursor + glyph.Along;
                    if (next > minAlong && cursor < maxAlong && glyph.Text != " ")
                    {
                        drawList.AddText(
                            new Vector2(min.X + (max.X - min.X - glyph.Width) * 0.5f, cursor),
                            color,
                            glyph.Text);
                    }

                    cursor = next;
                }
            }

            FadeMarqueeEnds(drawList, vertexStart, minAlong, maxAlong, vertical);
        }

        private static unsafe void FadeMarqueeEnds(ImDrawListPtr drawList, int firstVertex, float start, float end, bool vertical)
        {
            int count = drawList.VtxBuffer.Size;
            if (count <= firstVertex)
                return;

            float span = end - start;
            if (span <= 1f)
                return;

            float fade = Math.Min(ImGui.GetTextLineHeight() * 1.25f, span * 0.22f);
            if (fade < 1f)
                return;

            ImDrawVert* vertices = drawList.VtxBuffer.Data;
            for (int i = firstVertex; i < count; i++)
            {
                float along = vertical ? vertices[i].Pos.Y : vertices[i].Pos.X;
                float t = Math.Min(along - start, end - along) / fade;
                if (t >= 1f)
                    continue;

                uint color = vertices[i].Col;
                float shaped = t <= 0f ? 0f : t * t * (3f - 2f * t);
                uint alpha = (uint)(((color >> 24) & 0xFFu) * shaped);
                vertices[i].Col = (color & 0x00FFFFFFu) | (alpha << 24);
            }
        }

        private float MarqueeLength()
            => ImGui.GetTextLineHeight() * Math.Clamp(
                _plugin.Config.EdgeTitleMarqueeLines <= 0f ? LocalUiConfig.DefaultEdgeTitleMarqueeLines : _plugin.Config.EdgeTitleMarqueeLines,
                3f,
                16f);

        private static float MarqueeThickness(string title, bool vertical)
        {
            float line = ImGui.GetTextLineHeight();
            if (!vertical)
                return line;

            float width = line;
            foreach (var rune in title.EnumerateRunes())
            {
                if (rune.Value is '\r' or '\n' or '\t')
                    continue;
                float glyph = ImGui.CalcTextSize(rune.ToString()).X;
                if (glyph > width)
                    width = glyph;
            }

            return width;
        }

        private static float EdgeIconWidth()
        {
            float text = ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.Y * 2f;
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            if (handle.Available)
            {
                using (handle.Push())
                    text = Math.Max(
                        Math.Max(IconTextWidth(FontAwesomeIcon.Video), IconTextWidth(FontAwesomeIcon.Play)),
                        Math.Max(IconTextWidth(FontAwesomeIcon.Pause), Math.Max(IconTextWidth(FontAwesomeIcon.StepBackward), IconTextWidth(FontAwesomeIcon.StepForward))));
            }

            return text + ImGui.GetStyle().FramePadding.X * 2f;
        }

        private static float IconTextWidth(FontAwesomeIcon icon)
            => ImGui.CalcTextSize(icon.ToIconString()).X;

        private static PlaybackScreenEdge NormalizeEdge(PlaybackScreenEdge edge)
            => edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right or PlaybackScreenEdge.Top or PlaybackScreenEdge.Bottom
                ? edge
                : PlaybackScreenEdge.Right;

        private readonly struct TitleLayout
        {
            public TitleLayout(string title, PlaybackScreenEdge edge, bool vertical, bool textFirst, bool scrollPositive, Vector2 textSize, Vector2 iconChip)
            {
                Title = title;
                Edge = edge;
                Vertical = vertical;
                TextFirst = textFirst;
                ScrollPositive = scrollPositive;
                TextSize = textSize;
                IconChip = iconChip;
            }

            public string Title { get; }
            public PlaybackScreenEdge Edge { get; }
            public bool Vertical { get; }
            public bool TextFirst { get; }
            public bool ScrollPositive { get; }
            public Vector2 TextSize { get; }
            public Vector2 IconChip { get; }
        }

        private readonly struct GlyphRun
        {
            public GlyphRun(string text, float along, float width)
            {
                Text = text;
                Along = along;
                Width = width;
            }

            public string Text { get; }
            public float Along { get; }
            public float Width { get; }
        }

        private float ScreenMargin()
            => Math.Clamp(_plugin.Config.EdgeIconScreenMargin, 0f, LocalUiConfig.MaxEdgeIconScreenMargin);

        private float TitleIconGap()
            => Math.Clamp(_plugin.Config.EdgeTitleIconGap, 0f, LocalUiConfig.MaxEdgeTitleIconGap);

        private static void EdgePlacement(Vector2 viewportPos, Vector2 viewportSize, PlaybackScreenEdge edge, float anchor, float screenMargin, out Vector2 pos, out Vector2 pivot)
        {
            if (edge is not (PlaybackScreenEdge.Left or PlaybackScreenEdge.Right or PlaybackScreenEdge.Top or PlaybackScreenEdge.Bottom))
                edge = PlaybackScreenEdge.Right;

            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            float length = vertical ? viewportSize.Y : viewportSize.X;
            GetEdgeRange(length, out float start, out float end);
            float along = start + (end - start) * Math.Clamp(anchor, 0f, 1f);
            float inset = Math.Clamp(screenMargin, 0f, LocalUiConfig.MaxEdgeIconScreenMargin);

            switch (edge)
            {
                case PlaybackScreenEdge.Right:
                    pos = new Vector2(viewportPos.X + viewportSize.X - inset, viewportPos.Y + along);
                    pivot = new Vector2(1f, 0.5f);
                    break;
                case PlaybackScreenEdge.Top:
                    pos = new Vector2(viewportPos.X + along, viewportPos.Y + inset);
                    pivot = new Vector2(0.5f, 0f);
                    break;
                case PlaybackScreenEdge.Bottom:
                    pos = new Vector2(viewportPos.X + along, viewportPos.Y + viewportSize.Y - inset);
                    pivot = new Vector2(0.5f, 1f);
                    break;
                default:
                    pos = new Vector2(viewportPos.X + inset, viewportPos.Y + along);
                    pivot = new Vector2(0f, 0.5f);
                    break;
            }
        }

        private static void GetEdgeRange(float length, out float start, out float end)
        {
            var style = ImGui.GetStyle();
            float margin = ImGui.GetFrameHeight() * 0.5f + style.WindowPadding.Y;
            start = margin;
            end = Math.Max(margin + 1f, length - margin);
        }
    }
}
