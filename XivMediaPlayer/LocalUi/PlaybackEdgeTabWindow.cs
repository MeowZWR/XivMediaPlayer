using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class PlaybackEdgeTabWindow : Window
    {
        private const float MarqueeSecondsPerLine = 0.3f;
        private const float TitleGap = 12f;
        private const float MarqueePad = 4f;

        private readonly Plugin _plugin;
        private readonly ImGuiWindowFlags _baseFlags;
        private readonly List<GlyphRun> _glyphs = new();
        private bool _dragging;
        private bool _suppressClick;
        private bool _chromePushed;
        private bool _splitTitle;
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
            _splitTitle = EdgeTitle().Length > 0;
            AllowBackgroundBlur = !_chromePushed && !_splitTitle;
            Flags = _baseFlags;
            if (_plugin.PlaybackDockMoving && _plugin.PlaybackEdgeReveal < 0.99f)
                Flags |= ImGuiWindowFlags.NoInputs;
            if (_chromePushed)
                PlaybackPanelChrome.Push();
            if (_splitTitle)
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
            if (_splitTitle)
                ImGui.PopStyleVar();
            if (_chromePushed)
                PlaybackPanelChrome.Pop();
        }

        public override void Draw()
        {
            bool split = TryGetTitleLayout(out TitleLayout layout);
            if (_chromePushed && !split)
                PlaybackPanelChrome.DrawBackdrop();

            if (!split)
            {
                DrawEdgeIcon(0f, false, 0f, framed: false);
                FinishPointer();
                return;
            }

            float originX = ImGui.GetCursorPosX();
            bool alignEnd = layout.Edge == PlaybackScreenEdge.Right;
            float column = Math.Max(layout.TextSize.X, EdgeIconWidth());
            if (layout.TextFirst)
            {
                DrawEdgeTitle(layout, originX, column, alignEnd);
                MovePastGap(layout.Vertical, originX);
                DrawEdgeIcon(originX, alignEnd, column, framed: true);
            }
            else
            {
                DrawEdgeIcon(originX, alignEnd, column, framed: true);
                MovePastGap(layout.Vertical, originX);
                DrawEdgeTitle(layout, originX, column, alignEnd);
            }

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
            EdgePlacement(viewport.Pos, viewport.Size, edge, _plugin.Config.PlaybackEdgeAnchor, out Vector2 pos, out Vector2 pivot);
            bool split = TryGetTitleLayout(out TitleLayout layout);
            if (split)
            {
                pivot = IconPivot(layout);
                ImGui.SetNextWindowBgAlpha(0f);
            }

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
            if (!_chromePushed && !split)
                ImGui.SetNextWindowBgAlpha(0.92f);
        }

        private string EdgeTitle()
        {
            if (!_plugin.Config.ShowEdgePlaybackTitle)
                return "";
            string title = _plugin.CurrentMediaTitle;
            return string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
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
            float length = MarqueeLength();
            float thick = MarqueeThickness(title, vertical);
            var text = vertical
                ? new Vector2(thick + MarqueePad * 2f, length + MarqueePad * 2f)
                : new Vector2(length + MarqueePad * 2f, thick + MarqueePad * 2f);
            layout = new TitleLayout(title, edge, vertical, textFirst: !onStartSide, scrollPositive: onStartSide, text);
            return true;
        }

        private bool IconOnStartSide(PlaybackScreenEdge edge)
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.Pos + viewport.Size * 0.5f;
            EdgePlacement(viewport.Pos, viewport.Size, edge, _plugin.Config.PlaybackEdgeAnchor, out Vector2 pos, out _);
            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            return vertical ? pos.Y <= center.Y : pos.X <= center.X;
        }

        private Vector2 IconPivot(TitleLayout layout)
        {
            var style = ImGui.GetStyle();
            float padX = style.WindowPadding.X;
            float padY = style.WindowPadding.Y;
            float iconW = EdgeIconWidth();
            float iconH = ImGui.GetFrameHeight();
            float iconX;
            float iconY;
            float contentW;
            float contentH;
            if (layout.Vertical)
            {
                contentW = Math.Max(iconW, layout.TextSize.X);
                contentH = layout.TextSize.Y + TitleGap + iconH;
                iconY = layout.TextFirst ? layout.TextSize.Y + TitleGap : 0f;
                iconX = layout.Edge == PlaybackScreenEdge.Right ? contentW - iconW : 0f;
            }
            else
            {
                contentW = layout.TextSize.X + TitleGap + iconW;
                contentH = Math.Max(iconH, layout.TextSize.Y);
                iconX = layout.TextFirst ? layout.TextSize.X + TitleGap : 0f;
                iconY = 0f;
            }

            float windowW = Math.Max(1f, contentW + padX * 2f);
            float windowH = Math.Max(1f, contentH + padY * 2f);
            float along = layout.Vertical
                ? (padY + iconY + iconH * 0.5f) / windowH
                : (padX + iconX + iconW * 0.5f) / windowW;
            return layout.Vertical
                ? new Vector2(layout.Edge == PlaybackScreenEdge.Right ? 1f : 0f, along)
                : new Vector2(along, layout.Edge == PlaybackScreenEdge.Bottom ? 1f : 0f);
        }

        private void DrawEdgeIcon(float originX, bool alignEnd, float column, bool framed)
        {
            float width = EdgeIconWidth();
            if (alignEnd)
                ImGui.SetCursorPosX(originX + Math.Max(0f, column - width));

            var drawList = ImGui.GetWindowDrawList();
            if (framed)
            {
                drawList.ChannelsSplit(2);
                drawList.ChannelsSetCurrent(1);
            }

            int pushedColors = 0;
            bool pushedBorder = false;
            if (framed)
            {
                var clear = Vector4.Zero;
                ImGui.PushStyleColor(ImGuiCol.Button, clear);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, clear);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, clear);
                ImGui.PushStyleColor(ImGuiCol.Border, clear);
                pushedColors = 4;
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
                pushedBorder = true;
            }
            else if (_chromePushed)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                pushedColors = 1;
            }

            bool clicked = ImGuiComponents.IconButton("open-playback", FontAwesomeIcon.Play);
            var iconMin = ImGui.GetItemRectMin();
            var iconMax = ImGui.GetItemRectMax();
            bool hovered = ImGui.IsItemHovered();
            NotePointer(clicked);
            if (pushedBorder)
                ImGui.PopStyleVar();
            if (pushedColors > 0)
                ImGui.PopStyleColor(pushedColors);

            if (framed)
            {
                drawList.ChannelsSetCurrent(0);
                var pad = new Vector2(3f, 3f);
                DrawRegion(iconMin - pad, iconMax + pad, raised: true);
                drawList.ChannelsMerge();
                PlaybackPanelChrome.DrawEdgeAtmosphere(iconMin, iconMax);
            }
            else if (_chromePushed)
            {
                PlaybackPanelChrome.DrawEdgeAtmosphere(iconMin, iconMax);
            }

            if (hovered && !_dragging)
                ImGui.SetTooltip("播放控制。按住可沿屏幕四边拖动。");
        }

        private void DrawEdgeTitle(TitleLayout layout, float originX, float column, bool alignEnd)
        {
            if (layout.Vertical && alignEnd)
                ImGui.SetCursorPosX(originX + Math.Max(0f, column - layout.TextSize.X));

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

        private void MovePastGap(bool stacked, float originX)
        {
            if (!stacked)
            {
                ImGui.SameLine(0f, TitleGap);
                return;
            }

            float y = ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y + TitleGap;
            ImGui.SetCursorPos(new Vector2(originX, y));
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
                _plugin.OpenPlaybackPanel();
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
            public TitleLayout(string title, PlaybackScreenEdge edge, bool vertical, bool textFirst, bool scrollPositive, Vector2 textSize)
            {
                Title = title;
                Edge = edge;
                Vertical = vertical;
                TextFirst = textFirst;
                ScrollPositive = scrollPositive;
                TextSize = textSize;
            }

            public string Title { get; }
            public PlaybackScreenEdge Edge { get; }
            public bool Vertical { get; }
            public bool TextFirst { get; }
            public bool ScrollPositive { get; }
            public Vector2 TextSize { get; }
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
