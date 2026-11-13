using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class PlaybackEdgeTabWindow : Window
    {
        private const float MarqueeSecondsPerLine = 0.3f;
        private const float TitleGap = 6f;
        private const float MarqueePad = 4f;
        private const float ChipPad = 3f;
        private const float TextNarrower = 8f;

        private readonly Plugin _plugin;
        private readonly ImGuiWindowFlags _baseFlags;
        private readonly List<GlyphRun> _glyphs = new();
        private bool _dragging;
        private bool _suppressClick;
        private bool _chromePushed;
        private bool _pushedAlpha;
        private Vector2 _pressPos;
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

        public static Vector2 CollapsedWindowPos(Vector2 windowSize, PlaybackScreenEdge edge, float anchor)
        {
            var viewport = ImGui.GetMainViewport();
            EdgePlacement(viewport.Pos, viewport.Size, edge, anchor, out Vector2 pos, out Vector2 pivot);
            return pos - pivot * windowSize;
        }

        public override bool DrawConditions()
            => !_plugin.GameUiHidden;

        public override void PreDraw()
        {
            if (_dragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                FollowPointer();
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
            {
                layout = IconOnlyLayout();
                var loneOrigin = ImGui.GetCursorPos();
                ImGui.Dummy(layout.IconChip);
                DrawEdgeIcon(loneOrigin, layout);
                FinishPointer();
                return;
            }

            var origin = ImGui.GetCursorPos();
            ContentSlots(layout, out Vector2 content, out Vector2 textOffset, out Vector2 iconOffset);
            ImGui.Dummy(content);
            DrawEdgeTitle(layout, origin + textOffset);
            DrawEdgeIcon(origin + iconOffset, layout);

            FinishPointer();
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
            EdgePlacement(viewport.Pos, viewport.Size, edge, _plugin.Config.PlaybackEdgeAnchor, out Vector2 anchor, out _);
            if (!TryGetTitleLayout(out TitleLayout layout))
                layout = IconOnlyLayout();
            WindowFrame(layout, out Vector2 windowSize, out Vector2 pivot);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0f);
            ImGui.SetNextWindowPos(anchor - pivot * windowSize, ImGuiCond.Always);
        }

        private string EdgeTitle()
            => _plugin.Config.ShowEdgePlaybackTitle ? DisplayTitle() : "";

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
            MeasurePair(title, vertical, out _, out Vector2 iconChip);
            return new TitleLayout("", edge, vertical, false, false, default, iconChip);
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

        private static void ContentSlots(TitleLayout layout, out Vector2 content, out Vector2 textOffset, out Vector2 iconOffset)
        {
            textOffset = Vector2.Zero;
            iconOffset = Vector2.Zero;
            if (layout.TextSize == Vector2.Zero)
            {
                content = layout.IconChip;
                return;
            }

            float cross = layout.Vertical
                ? Math.Max(layout.TextSize.X, layout.IconChip.X)
                : Math.Max(layout.TextSize.Y, layout.IconChip.Y);
            if (layout.Vertical)
            {
                content = new Vector2(cross, layout.TextSize.Y + TitleGap + layout.IconChip.Y);
                textOffset.X = (cross - layout.TextSize.X) * 0.5f;
                iconOffset.X = (cross - layout.IconChip.X) * 0.5f;
                if (layout.TextFirst)
                    iconOffset.Y = layout.TextSize.Y + TitleGap;
                else
                    textOffset.Y = layout.IconChip.Y + TitleGap;
                return;
            }

            content = new Vector2(layout.TextSize.X + TitleGap + layout.IconChip.X, cross);
            textOffset.Y = (cross - layout.TextSize.Y) * 0.5f;
            iconOffset.Y = (cross - layout.IconChip.Y) * 0.5f;
            if (layout.TextFirst)
                iconOffset.X = layout.TextSize.X + TitleGap;
            else
                textOffset.X = layout.IconChip.X + TitleGap;
        }

        private bool IconOnStartSide(PlaybackScreenEdge edge)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.Pos + viewport.Size * 0.5f;
            EdgePlacement(viewport.Pos, viewport.Size, edge, _plugin.Config.PlaybackEdgeAnchor, out Vector2 pos, out _);
            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            return vertical ? pos.Y <= center.Y : pos.X <= center.X;
        }

        private static void WindowFrame(TitleLayout layout, out Vector2 windowSize, out Vector2 pivot)
        {
            var style = ImGui.GetStyle();
            float padX = style.WindowPadding.X;
            float padY = style.WindowPadding.Y;
            ContentSlots(layout, out Vector2 content, out _, out Vector2 iconOffset);
            windowSize = new Vector2(
                Math.Max(1f, content.X + padX * 2f),
                Math.Max(1f, content.Y + padY * 2f));
            float along = layout.Vertical
                ? (padY + iconOffset.Y + layout.IconChip.Y * 0.5f) / windowSize.Y
                : (padX + iconOffset.X + layout.IconChip.X * 0.5f) / windowSize.X;
            pivot = layout.Vertical
                ? new Vector2(layout.Edge == PlaybackScreenEdge.Right ? 1f : 0f, along)
                : new Vector2(along, layout.Edge == PlaybackScreenEdge.Bottom ? 1f : 0f);
        }

        private void DrawEdgeIcon(Vector2 cursor, TitleLayout layout)
        {
            ImGui.SetCursorPos(cursor);
            var chipMin = ImGui.GetCursorScreenPos();
            var chipMax = chipMin + layout.IconChip;
            bool clicked = ImGui.InvisibleButton("##edge-icon", layout.IconChip);
            bool hovered = ImGui.IsItemHovered();
            NotePointer(clicked);
            NoteTitleToggle();
            DrawRegion(chipMin, chipMax, raised: true);
            DrawPlayGlyph(chipMin, chipMax, hovered);
            if (_chromePushed)
            {
                var inset = new Vector2(2f, 2f);
                PlaybackPanelChrome.DrawEdgeAtmosphere(chipMin + inset, chipMax - inset);
            }
            if (hovered && !_dragging)
                ImGui.SetTooltip(EdgeTooltip());
        }

        private string EdgeTooltip()
        {
            string action = _plugin.Config.KeepEdgeIconWithPanel && _plugin.IsPlaybackPanelOpen
                ? "关闭播放控制。"
                : "播放控制。";
            return action + "按住可沿屏幕四边拖动。Ctrl+右键切换滚动标题。";
        }

        private void NoteTitleToggle()
        {
            if (!ImGui.IsItemClicked(ImGuiMouseButton.Right) || !ImGui.GetIO().KeyCtrl)
                return;
            _plugin.Config.ShowEdgePlaybackTitle = !_plugin.Config.ShowEdgePlaybackTitle;
            _plugin.Config.SaveLocal();
        }

        private static void DrawPlayGlyph(Vector2 min, Vector2 max, bool hovered)
        {
            string glyph = FontAwesomeIcon.Play.ToIconString();
            var handle = Plugin.PluginInterface.UiBuilder.IconFontHandle;
            var drawList = ImGui.GetWindowDrawList();
            uint color = ImGui.GetColorU32(hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Text);
            if (handle.Available)
                using (handle.Push())
                    DrawGlyph(drawList, glyph, min, max, color);
            else
                DrawGlyph(drawList, glyph, min, max, color);
        }

        private static void DrawGlyph(ImDrawListPtr drawList, string glyph, Vector2 min, Vector2 max, uint color)
        {
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(min + (max - min - glyphSize) * 0.5f, color, glyph);
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
                ImGui.SetTooltip(layout.Title);

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
            => ImGui.GetTextLineHeight() * Math.Clamp(_plugin.Config.EdgeTitleMarqueeLines <= 0f ? 8f : _plugin.Config.EdgeTitleMarqueeLines, 3f, 16f);

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
                    text = ImGui.CalcTextSize(FontAwesomeIcon.Play.ToIconString()).X;
            }

            return text + ImGui.GetStyle().FramePadding.X * 2f;
        }

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

        private static void EdgePlacement(Vector2 viewportPos, Vector2 viewportSize, PlaybackScreenEdge edge, float anchor, out Vector2 pos, out Vector2 pivot)
        {
            if (edge is not (PlaybackScreenEdge.Left or PlaybackScreenEdge.Right or PlaybackScreenEdge.Top or PlaybackScreenEdge.Bottom))
                edge = PlaybackScreenEdge.Right;

            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            float length = vertical ? viewportSize.Y : viewportSize.X;
            GetEdgeRange(length, out float start, out float end);
            float along = start + (end - start) * Math.Clamp(anchor, 0f, 1f);
            const float inset = 6f;

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
