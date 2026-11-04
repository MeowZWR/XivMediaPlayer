using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class PlaybackEdgeTabWindow : Window
    {
        private readonly Plugin _plugin;
        private readonly ImGuiWindowFlags _baseFlags;
        private bool _dragging;
        private bool _suppressClick;
        private bool _chromePushed;
        private bool _pushedAlpha;
        private Vector2 _pressPos;

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
            AllowBackgroundBlur = !_chromePushed;
            Flags = _baseFlags;
            if (_plugin.PlaybackDockMoving && _plugin.PlaybackEdgeReveal < 0.99f)
                Flags |= ImGuiWindowFlags.NoInputs;
            if (_chromePushed)
                PlaybackPanelChrome.Push();
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
            if (_chromePushed)
                PlaybackPanelChrome.Pop();
        }

        public override void Draw()
        {
            if (_chromePushed)
            {
                PlaybackPanelChrome.DrawBackdrop();
                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            }

            bool clicked = ImGuiComponents.IconButton("open-playback", FontAwesomeIcon.Play);
            if (_chromePushed)
            {
                ImGui.PopStyleColor();
                const float frameInset = 5f;
                var frameMin = ImGui.GetWindowPos() + new Vector2(frameInset, frameInset);
                var frameMax = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(frameInset, frameInset);
                PlaybackPanelChrome.DrawEdgeAtmosphere(frameMin, frameMax);
            }
            if (ImGui.IsItemActivated())
                _pressPos = ImGui.GetMousePos();
            if (ImGui.IsItemActive() && Vector2.Distance(ImGui.GetMousePos(), _pressPos) >= 4f)
            {
                _dragging = true;
                _suppressClick = true;
            }

            if (clicked && !_suppressClick)
                _plugin.OpenPlaybackPanel();

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (_dragging)
                    _plugin.Config.SaveLocal();
                _dragging = false;
                _suppressClick = false;
            }

            if (ImGui.IsItemHovered() && !_dragging)
                ImGui.SetTooltip("播放控制。按住可沿屏幕四边拖动。");
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
            EdgePlacement(viewport.Pos, viewport.Size, _plugin.Config.PlaybackEdgeSide, _plugin.Config.PlaybackEdgeAnchor, out Vector2 pos, out Vector2 pivot);
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
            if (!_chromePushed)
                ImGui.SetNextWindowBgAlpha(0.92f);
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
