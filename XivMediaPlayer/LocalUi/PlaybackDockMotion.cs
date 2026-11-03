using System;
using System.Diagnostics;
using System.Numerics;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class PlaybackDockMotion
    {
        public const float Duration = 0.24f;

        public bool Active { get; private set; }
        public bool Opening { get; private set; }
        public float PanelAlpha { get; private set; } = 1f;
        public Vector2 PanelPos { get; private set; }

        private float _time;
        private long _lastTimestamp;
        private Vector2 _from;
        private Vector2 _to;

        public void Begin(bool opening, Vector2 collapsed, Vector2 rest)
        {
            Vector2 current = Active ? PanelPos : (opening ? collapsed : rest);
            Active = true;
            Opening = opening;
            _time = 0f;
            _lastTimestamp = 0;
            _from = current;
            _to = opening ? rest : collapsed;
            Apply(0f);
        }

        public void Stop()
        {
            Active = false;
            PanelAlpha = 1f;
        }

        public bool Advance()
        {
            if (!Active)
                return false;

            long now = Stopwatch.GetTimestamp();
            float dt = _lastTimestamp == 0
                ? 1f / 60f
                : (float)((now - _lastTimestamp) / (double)Stopwatch.Frequency);
            _lastTimestamp = now;
            _time += Math.Clamp(dt, 0f, 0.05f);
            float t = Math.Clamp(_time / Duration, 0f, 1f);
            Apply(t);
            return t >= 1f;
        }

        private void Apply(float t)
        {
            float eased = 1f - MathF.Pow(1f - t, 3f);
            PanelPos = Vector2.Lerp(_from, _to, eased);
            PanelAlpha = Opening ? eased : 1f - eased;
        }
    }
}
