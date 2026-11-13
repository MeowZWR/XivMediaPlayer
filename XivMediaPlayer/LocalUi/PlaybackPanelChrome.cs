using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal static class PlaybackPanelChrome
    {
        public static readonly Vector4 Accent = new(0.678f, 0.541f, 0.961f, 1f);
        private static readonly Vector4 AccentHot = new(0.745f, 0.620f, 1f, 1f);
        private static readonly Vector4 Void = new(0.067f, 0.047f, 0.102f, 1f);
        private static readonly Vector4 VoidRaised = new(0.11f, 0.08f, 0.16f, 1f);

        private const int ColorCount = 28;
        private const int StyleCount = 5;
        private const int RailFadeBands = 6;
        private const int MaxSparks = 16;
        private const float DotSpacing = 8f;
        private const float PillPadX = 8f;
        private const float PillDot = 3f;
        private const float PillGap = 5f;

        private struct Spark
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public float Size;
            public float Twinkle;
            public bool Shooting;
            public Vector2[] Trail;
            public int TrailHead;
            public int TrailCount;
        }

        private sealed class SparkField
        {
            public readonly List<Spark> Sparks = new();
            public float SpawnTimer;
        }

        private static readonly SparkField PanelField = new();
        private static readonly SparkField EdgeField = new();
        private static readonly SparkField TitleField = new();
        private static readonly Random Random = new();

        public static void Push()
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Alpha(Void, 0.94f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Alpha(Void, 0.72f));
            ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.09f, 0.07f, 0.13f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.Border, Alpha(Accent, 0.62f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, Alpha(Void, 0.96f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.11f, 0.08f, 0.17f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Alpha(Void, 0.94f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.14f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.576f, 0.459f, 0.820f, 1f));
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentHot);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, VoidRaised);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.16f, 0.12f, 0.24f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.08f, 0.06f, 0.12f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.14f, 0.10f, 0.22f, 1f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.22f, 0.16f, 0.34f, 1f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.28f, 0.20f, 0.42f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Separator, Alpha(Accent, 0.40f));
            ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.09f, 0.07f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.24f, 0.17f, 0.36f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.34f, 0.25f, 0.50f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabUnfocused, new Vector4(0.09f, 0.07f, 0.13f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, new Vector4(0.26f, 0.19f, 0.40f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Alpha(Void, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Alpha(Accent, 0.72f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Alpha(AccentHot, 0.88f));
            ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, AccentHot);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
            ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        }

        public static void Pop()
        {
            ImGui.PopStyleVar(StyleCount);
            ImGui.PopStyleColor(ColorCount);
        }

        public static void DrawChip(Vector2 min, Vector2 max, bool raised)
        {
            if (max.X <= min.X + 1f || max.Y <= min.Y + 1f)
                return;

            float rounding = raised ? 5f : 6f;
            var drawList = ImGui.GetWindowDrawList();
            ImGuiHelpers.PrependBlurBehind(
                drawList,
                min,
                max,
                2f,
                rounding,
                noiseOpacity: 0.1f * ImGui.GetStyle().Alpha);
            var fill = raised ? new Vector4(VoidRaised.X, VoidRaised.Y, VoidRaised.Z, 0.96f) : Alpha(Void, 0.94f);
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);
            drawList.AddRect(
                min,
                max,
                ImGui.GetColorU32(Alpha(Accent, raised ? 0.9f : 0.45f)),
                rounding,
                ImDrawFlags.None,
                raised ? 1.5f : 1f);
        }

        public static void DrawBackdrop()
        {
            var min = ImGui.GetWindowPos();
            var max = min + ImGui.GetWindowSize();
            if (max.X <= min.X || max.Y <= min.Y)
                return;

            ImGuiHelpers.PrependBlurBehind(
                ImGui.GetWindowDrawList(),
                min,
                max,
                2.5f,
                ImGui.GetStyle().WindowRounding,
                noiseOpacity: 0.12f * ImGui.GetStyle().Alpha);
        }

        public static void DrawAtmosphere()
        {
            var origin = ImGui.GetWindowPos();
            DrawField(
                PanelField,
                origin + ImGui.GetWindowContentRegionMin(),
                origin + ImGui.GetWindowContentRegionMax(),
                MaxSparks,
                24f,
                1f,
                true);
        }

        public static void DrawEdgeAtmosphere(Vector2 min, Vector2 max)
        {
            DrawField(EdgeField, min, max, 3, 2f, 1.22f, false);
        }

        public static void DrawTitleAtmosphere(Vector2 min, Vector2 max)
        {
            DrawField(TitleField, min, max, 3, 2f, 1.22f, false);
        }

        private static void DrawField(SparkField field, Vector2 min, Vector2 max, int maxSparks, float edgeFade, float markScale, bool drawDots)
        {
            if (max.X <= min.X + 4f || max.Y <= min.Y + 4f)
                return;

            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(min, max, true);
            if (drawDots)
                DrawDots(drawList, min, max);
            DrawSparks(drawList, field, min, max - min, maxSparks, edgeFade, markScale);
            drawList.PopClipRect();
        }

        public static void DrawHairline()
        {
            var style = ImGui.GetStyle();
            var left = ImGui.GetCursorScreenPos();
            left.Y -= style.ItemSpacing.Y * 0.5f;
            float width = ImGui.GetContentRegionAvail().X;
            if (width <= 1f)
                return;

            ImGui.GetWindowDrawList().AddLine(
                left,
                new Vector2(left.X + width, left.Y),
                ImGui.GetColorU32(Alpha(Accent, 0.35f)),
                1f);
        }

        public static void DrawRail(float top, float bottom)
        {
            float height = bottom - top;
            if (height <= 4f)
                return;

            float fade = MathF.Min(28f, height * 0.4f);
            DrawFadedRail(ImGui.GetWindowDrawList(), ImGui.GetWindowPos().X + 4f, 3f, top, bottom, Alpha(Accent, 0.9f), fade);
        }

        public static float PillWidth(string label)
            => PillPadX * 2f + PillDot * 2f + PillGap + ImGui.CalcTextSize(label).X;

        public static Vector2 ExtensionTagSize(string tag)
        {
            var text = ImGui.CalcTextSize(tag);
            return new Vector2(text.X + 10f, text.Y + 2f);
        }

        public static void DrawExtensionTag(ImDrawListPtr drawList, Vector2 pos, string tag, Vector4 color)
        {
            var size = ExtensionTagSize(tag);
            var max = pos + size;
            if (max.X <= pos.X || max.Y <= pos.Y)
                return;

            float rounding = size.Y * 0.5f;
            drawList.AddRectFilled(pos, max, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.22f)), rounding);
            drawList.AddRect(pos, max, ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.72f)), rounding, ImDrawFlags.None, 1f);
            drawList.AddText(new Vector2(pos.X + 5f, pos.Y + 1f), ImGui.GetColorU32(color), tag);
        }

        public static bool DrawPill(string id, string label, Vector4 accent, string tooltip)
        {
            float height = ImGui.GetFrameHeight();
            var size = new Vector2(PillWidth(label), height);
            bool clicked = ImGui.InvisibleButton(id, size);
            bool hovered = ImGui.IsItemHovered();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();
            float rounding = height * 0.5f;

            drawList.AddRectFilled(min, max, ImGui.GetColorU32(Alpha(accent, hovered ? 0.22f : 0.12f)), rounding);
            drawList.AddRect(min, max, ImGui.GetColorU32(Alpha(accent, hovered ? 0.72f : 0.46f)), rounding, ImDrawFlags.None, 1f);

            var dotCenter = new Vector2(min.X + PillPadX + PillDot, (min.Y + max.Y) * 0.5f);
            drawList.AddCircleFilled(dotCenter, PillDot, ImGui.GetColorU32(accent), 10);
            drawList.AddText(
                new Vector2(dotCenter.X + PillDot + PillGap, min.Y + (height - ImGui.GetTextLineHeight()) * 0.5f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, hovered ? 0.98f : 0.92f)),
                label);

            if (hovered)
                ImGui.SetTooltip(tooltip);
            return clicked;
        }

        private static void DrawFadedRail(ImDrawListPtr drawList, float railX, float railWidth, float top, float bottom, Vector4 color, float fade)
        {
            float height = bottom - top;
            float right = railX + railWidth;
            fade = Math.Clamp(fade, 0f, height * 0.5f);
            if (fade <= 0f)
            {
                drawList.AddRectFilled(new Vector2(railX, top), new Vector2(right, bottom), ImGui.GetColorU32(color));
                return;
            }

            float solidTop = top + fade;
            float solidBottom = bottom - fade;
            if (solidBottom > solidTop)
                drawList.AddRectFilled(new Vector2(railX, solidTop), new Vector2(right, solidBottom), ImGui.GetColorU32(color));

            for (int band = 0; band < RailFadeBands; band++)
            {
                float near = band / (float)RailFadeBands;
                float far = (band + 1) / (float)RailFadeBands;
                uint nearColor = ImGui.GetColorU32(Alpha(color, color.W * FadeAlpha(near)));
                uint farColor = ImGui.GetColorU32(Alpha(color, color.W * FadeAlpha(far)));

                drawList.AddRectFilledMultiColor(
                    new Vector2(railX, solidTop - fade * far),
                    new Vector2(right, solidTop - fade * near),
                    farColor,
                    farColor,
                    nearColor,
                    nearColor);
                drawList.AddRectFilledMultiColor(
                    new Vector2(railX, solidBottom + fade * near),
                    new Vector2(right, solidBottom + fade * far),
                    nearColor,
                    nearColor,
                    farColor,
                    farColor);
            }
        }

        private static void DrawDots(ImDrawListPtr drawList, Vector2 min, Vector2 max)
        {
            const float radius = 0.8f;
            const float inset = 3f;
            uint color = ImGui.GetColorU32(new Vector4(0.72f, 0.62f, 0.95f, 0.16f));
            for (float y = min.Y + inset; y < max.Y - inset; y += DotSpacing)
            {
                for (float x = min.X + inset; x < max.X - inset; x += DotSpacing)
                {
                    drawList.AddRectFilled(
                        new Vector2(x - radius, y - radius),
                        new Vector2(x + radius, y + radius),
                        color);
                }
            }
        }

        private static void DrawSparks(ImDrawListPtr drawList, SparkField field, Vector2 origin, Vector2 size, int maxSparks, float edgeFade, float markScale)
        {
            float dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.05f);
            float speed = markScale > 1f ? 1f / markScale : 1f;
            var sparks = field.Sparks;
            field.SpawnTimer += dt;
            if (field.SpawnTimer > 0.32f && sparks.Count < maxSparks)
            {
                SpawnTwinkle(sparks, size);
                field.SpawnTimer = 0f;
            }

            if (Random.NextDouble() < (markScale > 1f ? 0.012 : 0.004))
                SpawnShooting(sparks, size);

            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark spark = sparks[i];
                if (spark.Shooting && spark.Trail != null)
                {
                    spark.Trail[spark.TrailHead] = spark.Position;
                    spark.TrailHead = (spark.TrailHead + 1) % spark.Trail.Length;
                    if (spark.TrailCount < spark.Trail.Length)
                        spark.TrailCount++;
                }

                spark.Position += spark.Velocity * dt * speed;
                spark.Life -= dt;
                spark.Twinkle += dt * 3.2f;
                bool outside = spark.Position.X < -30f || spark.Position.Y < -30f
                    || spark.Position.X > size.X + 30f || spark.Position.Y > size.Y + 30f;
                if (spark.Life <= 0f || (spark.Shooting && outside))
                {
                    sparks.RemoveAt(i);
                    continue;
                }

                if (!spark.Shooting)
                {
                    if (spark.Position.X < 0f || spark.Position.X > size.X)
                        spark.Velocity.X = -spark.Velocity.X;
                    if (spark.Position.Y < 0f || spark.Position.Y > size.Y)
                        spark.Velocity.Y = -spark.Velocity.Y;
                }

                sparks[i] = spark;
                float life = Math.Clamp(Math.Min(spark.Life, spark.MaxLife - spark.Life) / 0.35f, 0f, 1f);
                float edge = EdgeFade(spark.Position, size, edgeFade);
                float alpha = life * edge;
                if (alpha <= 0.01f)
                    continue;

                var pos = origin + spark.Position;
                if (spark.Shooting)
                    DrawShooting(drawList, origin, spark, alpha, markScale);
                else
                {
                    float twinkle = alpha * (0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(spark.Twinkle)));
                    drawList.AddCircleFilled(pos, spark.Size * markScale, ImGui.GetColorU32(new Vector4(0.92f, 0.90f, 1f, twinkle)), 8);
                }
            }
        }

        private static void DrawShooting(ImDrawListPtr drawList, Vector2 origin, Spark spark, float alpha, float markScale)
        {
            if (spark.Trail == null || spark.TrailCount < 2)
                return;

            int length = spark.Trail.Length;
            for (int t = 1; t < spark.TrailCount; t++)
            {
                float along = t / (float)spark.TrailCount;
                float trailAlpha = (1f - along) * alpha;
                float width = ((1f - along) * 2.2f + 0.6f) * markScale;
                int newer = (spark.TrailHead - t + length) % length;
                int older = (spark.TrailHead - t - 1 + length) % length;
                var a = origin + spark.Trail[newer];
                var b = origin + spark.Trail[older];
                drawList.AddLine(a, b, ImGui.GetColorU32(new Vector4(0.40f, 0.80f, 1f, trailAlpha * 0.35f)), width + 2.4f * markScale);
                drawList.AddLine(a, b, ImGui.GetColorU32(new Vector4(0.75f, 0.92f, 1f, trailAlpha)), width);
            }
        }

        private static void SpawnTwinkle(List<Spark> sparks, Vector2 size)
        {
            sparks.Add(new Spark
            {
                Position = new Vector2((float)Random.NextDouble() * size.X, (float)Random.NextDouble() * size.Y),
                Velocity = new Vector2((float)Random.NextDouble() * 8f - 4f, (float)Random.NextDouble() * 6f - 3f),
                Life = 2.4f + (float)Random.NextDouble() * 2.2f,
                MaxLife = 4.6f,
                Size = 0.7f + (float)Random.NextDouble() * 0.8f,
                Twinkle = (float)Random.NextDouble() * 6f,
            });
        }

        private static void SpawnShooting(List<Spark> sparks, Vector2 size)
        {
            bool fromTop = Random.NextDouble() < 0.5;
            var position = fromTop
                ? new Vector2((float)Random.NextDouble() * size.X, -8f)
                : new Vector2(-8f, (float)Random.NextDouble() * size.Y * 0.7f);
            var velocity = fromTop
                ? new Vector2(70f + (float)Random.NextDouble() * 50f, 46f + (float)Random.NextDouble() * 30f)
                : new Vector2(90f + (float)Random.NextDouble() * 40f, 28f + (float)Random.NextDouble() * 24f);
            sparks.Add(new Spark
            {
                Position = position,
                Velocity = velocity,
                Life = 1.3f,
                MaxLife = 1.3f,
                Size = 1.4f,
                Shooting = true,
                Trail = new Vector2[12],
            });
        }

        private static float EdgeFade(Vector2 position, Vector2 size, float distance)
        {
            float x = Math.Min(position.X, size.X - position.X);
            float y = Math.Min(position.Y, size.Y - position.Y);
            return Math.Clamp(Math.Min(x, y) / distance, 0f, 1f);
        }

        private static float FadeAlpha(float t)
            => 1f - t * t * (3f - 2f * t);

        private static Vector4 Alpha(Vector4 color, float alpha)
            => new(color.X, color.Y, color.Z, alpha);
    }
}
