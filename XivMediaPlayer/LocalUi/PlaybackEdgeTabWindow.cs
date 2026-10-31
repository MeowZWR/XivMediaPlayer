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
        private bool _dragging;
        private bool _suppressClick;
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
            RespectCloseHotkey = false;
        }

        public override void PreDraw()
        {
            if (_dragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                FollowPointer();
            PlaceOnEdge();
            base.PreDraw();
        }

        public override void Draw()
        {
            bool clicked = ImGuiComponents.IconButton("open-playback", FontAwesomeIcon.Play);
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
                    _plugin.Config.Save();
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
            PlaybackScreenEdge edge = _plugin.Config.PlaybackEdgeSide;
            if (edge is not (PlaybackScreenEdge.Left or PlaybackScreenEdge.Right or PlaybackScreenEdge.Top or PlaybackScreenEdge.Bottom))
                edge = PlaybackScreenEdge.Right;

            bool vertical = edge is PlaybackScreenEdge.Left or PlaybackScreenEdge.Right;
            float length = vertical ? viewport.Size.Y : viewport.Size.X;
            GetEdgeRange(length, out float start, out float end);
            float along = start + (end - start) * Math.Clamp(_plugin.Config.PlaybackEdgeAnchor, 0f, 1f);
            const float inset = 6f;

            Vector2 pos;
            Vector2 pivot;
            switch (edge)
            {
                case PlaybackScreenEdge.Right:
                    pos = new Vector2(viewport.Pos.X + viewport.Size.X - inset, viewport.Pos.Y + along);
                    pivot = new Vector2(1f, 0.5f);
                    break;
                case PlaybackScreenEdge.Top:
                    pos = new Vector2(viewport.Pos.X + along, viewport.Pos.Y + inset);
                    pivot = new Vector2(0.5f, 0f);
                    break;
                case PlaybackScreenEdge.Bottom:
                    pos = new Vector2(viewport.Pos.X + along, viewport.Pos.Y + viewport.Size.Y - inset);
                    pivot = new Vector2(0.5f, 1f);
                    break;
                default:
                    pos = new Vector2(viewport.Pos.X + inset, viewport.Pos.Y + along);
                    pivot = new Vector2(0f, 0.5f);
                    break;
            }

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
            ImGui.SetNextWindowBgAlpha(0.92f);
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
