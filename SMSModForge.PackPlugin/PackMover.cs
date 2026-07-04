using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Eases a transform's <em>world</em> position from where it started to a
    /// target over a fixed duration with a smoothstep curve (ease-in AND
    /// ease-out), then <em>holds</em> it there every <c>LateUpdate</c> — the
    /// component behind the <c>MoveGameObject</c> dialogue action.
    /// <para/>
    /// World position (not local): the original the host mod pan moved
    /// <c>transform.position</c>, and the levels live under a parent scaled
    /// ~1/100, so local units are ~100× smaller — using world keeps the
    /// authored numbers (e.g. a level pan to y = -17) meaningful.
    /// <para/>
    /// Important: the mover keeps its OWN authoritative position
    /// (<see cref="_current"/>) and never reads <c>transform.position</c> back to
    /// decide where the next move starts. A <c>ParallaxMouseEffect</c> rewrites
    /// the transform toward its home every <c>Update</c>; the mover re-asserts
    /// in <c>LateUpdate</c> (high execution order) so the rendered position is
    /// held, but mid-frame the transform reads as ≈home. Starting a new move
    /// from <see cref="_current"/> instead of the transform is what stops the
    /// next action snapping back to the original position.
    /// <para/>
    /// Two explicit modes (chosen by the action, so the intent is visible in the
    /// editor): <see cref="MoveTo"/> goes to a target and HOLDS it (parallax
    /// stays overridden); <see cref="MoveHome"/> eases back to the position
    /// captured before the first move and RELEASES (parallax resumes).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class PackMover : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _target;
        private Vector3 _home;
        private Vector3 _current;   // authoritative position the mover maintains
        private float _duration = 0.5f;
        private float _elapsed;
        private bool _initialized;
        private bool _running;
        private bool _releaseOnArrive;

        /// <summary>Move to <paramref name="worldTarget"/> and hold it.</summary>
        public void MoveTo(Vector3 worldTarget, float duration)
            => Begin(worldTarget, duration, releaseOnArrive: false);

        /// <summary>Return to the position captured before the first move, then
        /// release control so the object's own behaviour (parallax) resumes.</summary>
        public void MoveHome(float duration)
            => Begin(_initialized ? _home : transform.position, duration, releaseOnArrive: true);

        private void Begin(Vector3 target, float duration, bool releaseOnArrive)
        {
            if (!_initialized)
            {
                _current = transform.position;
                _home = transform.position;
                _initialized = true;
            }
            _start = _current;          // from the mover's own held position,
                                        // NOT transform.position (parallax
                                        // corrupts that mid-frame).
            _target = target;
            _duration = Mathf.Max(0.0001f, duration);
            _elapsed = 0f;
            _releaseOnArrive = releaseOnArrive;
            _running = true;
            enabled = true;
        }

        private void LateUpdate()
        {
            if (!_running) return;

            if (_elapsed < _duration)
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / _duration);
                float e = t * t * (3f - 2f * t);   // smoothstep — ease in + out
                _current = Vector3.Lerp(_start, _target, e);
            }
            else
            {
                _current = _target;                 // hold (beats parallax)
            }
            transform.position = _current;

            if (_elapsed >= _duration && _releaseOnArrive)
            {
                _running = false;
                enabled = false;                    // back home → parallax resumes
            }
        }
    }
}
