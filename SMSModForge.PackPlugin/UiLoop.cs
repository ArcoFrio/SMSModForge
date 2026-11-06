using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// An object that keeps moving while it is shown - a node's <c>loop</c>
    /// (1.8.3). Its <c>open</c> and <c>close</c> play once; this repeats:
    /// <list type="bullet">
    ///   <item>a pulse: its fade and/or size go from one value to the other and
    ///   back, once a pass - a glow breathing;</item>
    ///   <item>a sweep: it moves from one offset to another and starts again -
    ///   a highlight under a parent that clips it becomes a shine running
    ///   along a line.</item>
    /// </list>
    /// A pass takes <see cref="Seconds"/>, with <see cref="Pause"/> resting at
    /// the start between passes, the first after <see cref="Delay"/> so copies
    /// of one object need not beat in step. On unscaled time, from the moment
    /// it is switched on. Drawn through <see cref="UiOverlay"/>, on top of
    /// what the object is: the fade and size multiply its own, the move adds to
    /// its place.
    /// </summary>
    public class UiLoop : MonoBehaviour, IUiOverlayPart
    {
        /// <summary>From and to, or null: the fade, as fractions of its own.</summary>
        public float[] Alpha;

        /// <summary>From and to, or null: the size, as fractions of its own.</summary>
        public float[] Scale;

        /// <summary>The sweep's start and end, as offsets from its place; both
        /// or neither.</summary>
        public Vector2? MoveFrom, MoveTo;

        public float Seconds = 2f;
        public float Pause;
        public float Delay;

        /// <summary>The sweep's curve - the open animation's names.</summary>
        public string Easing = "Linear";

        private float _since;

        private void OnEnable() => _since = 0f;

        void IUiOverlayPart.Shape(ref float scale, ref float alpha, ref Vector2 offset)
        {
            _since += Time.unscaledDeltaTime;
            float p = Progress();

            // There and back within one pass, smoothly at both ends.
            float w = (1f - Mathf.Cos(2f * Mathf.PI * p)) * 0.5f;
            if (Alpha != null && Alpha.Length == 2) alpha *= Mathf.Lerp(Alpha[0], Alpha[1], w);
            if (Scale != null && Scale.Length == 2) scale *= Mathf.Lerp(Scale[0], Scale[1], w);

            // One way, then from the start again.
            if (MoveFrom.HasValue && MoveTo.HasValue)
                offset += Vector2.LerpUnclamped(MoveFrom.Value, MoveTo.Value, UiOpenAnimation.Ease(p, Easing));
        }

        /// <summary>How far through its pass, 0-1; 0 while waiting to start
        /// and through the pause.</summary>
        private float Progress()
        {
            float t = _since - Delay;
            if (t <= 0f || Seconds <= 0f) return 0f;
            float pass = t % (Seconds + Mathf.Max(0f, Pause));
            return pass >= Seconds ? 0f : pass / Seconds;
        }
    }
}
