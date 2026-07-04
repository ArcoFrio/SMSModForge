using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Click handler for pack-created navigator buttons. Implements
    /// <see cref="IPointerClickHandler"/> directly rather than relying on
    /// a Unity <c>Button</c> component, because vanilla map buttons were
    /// never built around Unity's UI Button — they use GC2's
    /// <c>ButtonInstructions</c> for click handling. Cloned buttons that
    /// had ButtonInstructions stripped need a replacement click path.
    /// <para/>
    /// On click, mirrors the GC2 <c>ButtonInstructions</c> click flow:
    /// <list type="number">
    ///   <item>Scale self to (0.80, 0.80, 0.80) over 0.2s with Quad InOut
    ///         easing, wait to complete.</item>
    ///   <item>Activate the <see cref="Sentinel"/> child (<c>ButtonPressed</c>),
    ///         which <see cref="NavigatorRuntime.Tick"/> picks up next frame
    ///         to fire navigation.</item>
    ///   <item>Scale self back to (1.00, 1.00, 1.00) over 0.2s with Quad
    ///         InOut easing, wait to complete.</item>
    /// </list>
    /// <para/>
    /// Keyboard shortcuts are assigned dynamically by
    /// <see cref="NavigatorRuntime.Tick"/> via <see cref="AssignShortcutNumber"/>,
    /// which counts all active siblings under <c>MapButtons</c> in sibling
    /// order and gives each pack button its positional number (so it never
    /// collides with vanilla buttons, which sit earlier in the hierarchy).
    /// </summary>
    public class NavigatorButtonClick : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>
        /// The <c>ButtonPressed</c> child GameObject. Activated on click,
        /// polled and deactivated by <see cref="NavigatorRuntime.Tick"/>.
        /// </summary>
        public GameObject Sentinel;

        /// <summary>Duration of each scale tween (seconds). Matches vanilla.</summary>
        private const float ScaleDuration = 0.2f;

        private static readonly Vector3 PressedScale = new Vector3(0.80f, 0.80f, 0.80f);

        private bool _animating;

        /// <summary>
        /// The <see cref="KeyCode"/> mapped to this button's current
        /// keyboard shortcut. Assigned each frame by
        /// <see cref="NavigatorRuntime.Tick"/> — not read from the cloned
        /// template (which always carries Beach's number).
        /// </summary>
        private KeyCode _shortcutKey = KeyCode.None;

        /// <summary>
        /// Cached reference to the <c>keyboardnumber</c> child's TMP,
        /// used by <see cref="AssignShortcutNumber"/> to update the
        /// displayed shortcut hint.
        /// </summary>
        private TextMeshProUGUI _keyboardLabel;

        /// <summary>Last number assigned — avoids redundant TMP writes.</summary>
        private int _assignedNumber;

        private void Start()
        {
            // Cache the keyboardnumber TMP child for later updates.
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).name == "keyboardnumber")
                {
                    _keyboardLabel = transform.GetChild(i).GetComponent<TextMeshProUGUI>();
                    break;
                }
            }
        }

        private void Update()
        {
            if (_shortcutKey == KeyCode.None) return;
            if (_animating) return;
            if (UnityEngine.Input.GetKeyDown(_shortcutKey))
                StartCoroutine(ClickAnimation());
        }

        /// <summary>
        /// Assigns a keyboard shortcut number to this button. Updates both
        /// the internal key listener and the displayed <c>keyboardnumber</c>
        /// label. Called each frame by <see cref="NavigatorRuntime.Tick"/>
        /// with this button's position among all active <c>MapButtons</c>
        /// siblings (1-based). Pass &lt;= 0 to disable.
        /// </summary>
        public void AssignShortcutNumber(int number)
        {
            if (_assignedNumber == number) return;
            _assignedNumber = number;

            if (number >= 1 && number <= 9)
            {
                _shortcutKey = KeyCode.Alpha1 + (number - 1);
                if (_keyboardLabel != null) _keyboardLabel.text = number.ToString();
            }
            else if (number == 10)
            {
                _shortcutKey = KeyCode.Alpha0;
                if (_keyboardLabel != null) _keyboardLabel.text = "0";
            }
            else
            {
                _shortcutKey = KeyCode.None;
                if (_keyboardLabel != null) _keyboardLabel.text = "";
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_animating) return;
            StartCoroutine(ClickAnimation());
        }

        private IEnumerator ClickAnimation()
        {
            _animating = true;

            // --- Scale down: 1 → 0.80 over ScaleDuration, Quad InOut ---
            yield return ScaleTo(Vector3.one, PressedScale);

            // --- Activate sentinel ---
            if (Sentinel != null) Sentinel.SetActive(true);

            // --- Scale up: 0.80 → 1 over ScaleDuration, Quad InOut ---
            yield return ScaleTo(PressedScale, Vector3.one);

            _animating = false;
        }

        /// <summary>
        /// Smoothly interpolates <c>localScale</c> from <paramref name="from"/>
        /// to <paramref name="to"/> over <see cref="ScaleDuration"/> seconds
        /// using Quad InOut easing (the same curve GC2's Scale instruction
        /// uses for its "Quad In Out" preset).
        /// </summary>
        private IEnumerator ScaleTo(Vector3 from, Vector3 to)
        {
            float elapsed = 0f;
            while (elapsed < ScaleDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / ScaleDuration);
                float eased = QuadInOut(t);
                transform.localScale = Vector3.LerpUnclamped(from, to, eased);
                yield return null;
            }
            transform.localScale = to;
        }

        /// <summary>
        /// Quad InOut easing: accelerates in, decelerates out.
        /// <c>f(0)=0, f(0.5)=0.5, f(1)=1</c>.
        /// </summary>
        private static float QuadInOut(float t)
        {
            if (t < 0.5f) return 2f * t * t;
            float u = -2f * t + 2f;
            return 1f - u * u * 0.5f;
        }

        /// <summary>
        /// Ensures the Image on this button has <c>raycastTarget = true</c>
        /// so the EventSystem can deliver pointer events. Called once after
        /// the component is added. Vanilla buttons may have raycastTarget
        /// disabled since GC2's ButtonInstructions handles clicks via its
        /// own path.
        /// </summary>
        public void EnsureRaycastTarget()
        {
            var img = GetComponent<Image>();
            if (img != null)
                img.raycastTarget = true;
        }
    }
}
