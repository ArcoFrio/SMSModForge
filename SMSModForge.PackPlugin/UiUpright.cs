using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Keeps a UI object level while what it hangs on turns: after the frame's
    /// animation, its rotation is set back to its canvas's, so it follows its
    /// parent's place and size but never its turning (a node's
    /// <c>upright</c>, 1.8.3).
    /// <para/>
    /// The game swings some of its groups round - a district's buttons on the
    /// world map turn from 180 to 0 degrees as it opens, through about a second
    /// - and what a pack hung on those buttons turned with them, upside down
    /// for most of it. Unity's own Rotation Constraint needs a source object,
    /// which a pack cannot name, so this is ModForge's.
    /// </summary>
    public class UiUpright : MonoBehaviour
    {
        // LateUpdate: after the game's own scripts have moved things this
        // frame, before the canvas is drawn.
        private void LateUpdate()
        {
            var canvas = GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.transform : null;
            transform.rotation = root != null ? root.rotation : Quaternion.identity;
        }
    }
}
