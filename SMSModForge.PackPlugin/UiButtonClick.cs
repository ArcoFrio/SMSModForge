using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Makes a pack's UI object clickable, and makes it FEEL like the game's.
    /// <para/>
    /// Implements the pointer interfaces directly rather than adding a Unity
    /// <c>Button</c>, for the same reason <see cref="NavigatorButtonClick"/>
    /// does: this game has some 1885 <c>Trigger</c> components and ten uGUI
    /// Buttons. Its buttons are not Buttons, so matching a Button's behaviour
    /// would match nothing an author has ever seen in this game.
    /// <para/>
    /// The press is the game's own, taken from <c>ButtonInstructions</c>: scale
    /// to 0.80 over 0.2s with quadratic in-out easing, act, and scale back. A
    /// button that jumps instantly is the single clearest tell that a UI was
    /// not made by the people who made the game.
    /// <para/>
    /// Hover is a tint the pack declares. The game does the same: every one of
    /// its 756 buttons carries a Unity ColorBlock on ColorTint - #F5F5F5 on
    /// hover, #C8C8C8 on press, 0.1s fade - so a pack matching those looks
    /// native.
    /// </summary>
    public class UiButtonClick : MonoBehaviour,
                                 IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>The actions to run, as authored.</summary>
        public JArray Actions;

        /// <summary>Everything the actions resolve against - the pack's
        /// variables, actors, scenes and the rest. Handed in rather than built
        /// here: it is assembled once when the pack loads.</summary>
        public PackContext Context;

        /// <summary>What has to be true for the click to do anything. Null or
        /// empty means it always does.</summary>
        public JArray Conditions;

        /// <summary>Tint while hovered. Unset leaves the colour alone.</summary>
        public Color? HoverTint;

        /// <summary>What the button sounds like, by name: one of the pack's
        /// own sounds or one of the game's. Empty is silent, which is what a
        /// pack that has not said gets - a sound nobody asked for is worse than
        /// none.
        /// <para/>
        /// Kept as a name and resolved on the click rather than when the screen
        /// is built: a pack's sounds load asynchronously, and an entry can hold
        /// several variants to pick between.</summary>
        public string ClickSound;

        /// <summary>
        /// How loud, 0-1. Under the game's own so a click sits behind whatever
        /// is being said or played rather than on top of it - a button is
        /// pressed far more often than anything else on screen happens.
        /// </summary>
        public float ClickVolume = DefaultClickVolume;

        public const float DefaultClickVolume = 0.85f;

        private const float ScaleDuration = 0.2f;
        private static readonly Vector3 PressedScale = new Vector3(0.80f, 0.80f, 0.80f);

        private Graphic _graphic;
        private Color _restingColour;
        private bool _hasResting;
        private bool _pressing;

        /// <summary>The scale to come back to. Read at the moment of the press
        /// rather than assumed to be 1, because a pack can author a button at
        /// any scale - and the press used to tween to an absolute 0.80 and back
        /// to an absolute 1, which quietly resized any button that was not
        /// authored at 1 the first time it was clicked.</summary>
        private Vector3 _resting = Vector3.one;

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
            if (_graphic != null)
            {
                _restingColour = _graphic.color;
                _hasResting = true;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_graphic != null && HoverTint.HasValue) _graphic.color = HoverTint.Value;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_graphic != null && _hasResting) _graphic.color = _restingColour;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // One press at a time. Without this a fast double-click starts a
            // second tween from a half-shrunk scale and the button never comes
            // back to full size.
            if (_pressing) return;

            _resting = transform.localScale;
            StartCoroutine(Press());

            // Before the actions, not after: one of them may switch this screen
            // off, and a sound started from an object that is about to go away
            // still plays because it goes through the game's own UI channel
            // rather than a source on this object.
            var clip = UiFactory.ResolveSound(Context, ClickSound);
            if (clip != null) GameAudio.PlayUi(clip, ClickVolume);

            // Acted on NOW, with the press running alongside. This used to wait
            // for the shrink to finish first, which put a fifth of a second
            // between the click and anything happening - long enough to read as
            // the click not having registered.
            Run();
        }

        private IEnumerator Press()
        {
            _pressing = true;

            yield return ScaleTo(Vector3.Scale(_resting, PressedScale));
            yield return ScaleTo(_resting);

            _pressing = false;
        }

        /// <summary>
        /// Put the button back when it is switched off part-way through a press.
        /// <para/>
        /// Unity stops a coroutine the moment its GameObject stops being active,
        /// and the commonest thing for a close button to do is deactivate the
        /// screen it sits on - which kills the press between the two tweens. The
        /// scale stayed at 0.80 and, worse, the busy flag stayed set, so the
        /// button never accepted another click for the rest of the session.
        /// Hover went on working, since it never looks at that flag, which is
        /// what made a dead button look like a live one.
        /// <para/>
        /// Fires for a deactivated parent too, which is the case that matters:
        /// the button is a descendant of the screen being switched off.
        /// </summary>
        private void OnDisable()
        {
            // The hover tint would otherwise still be on when the screen comes
            // back, showing a button as hovered with the pointer nowhere near.
            if (_graphic != null && _hasResting) _graphic.color = _restingColour;

            if (!_pressing) return;
            StopAllCoroutines();
            transform.localScale = _resting;
            _pressing = false;
        }

        private void Run()
        {
            if (Actions == null || Actions.Count == 0 || Context == null) return;
            try
            {
                // The question the button asks before it acts - "is there
                // enough money". Failing it runs nothing at all, which is the
                // whole point: without this, anything conditional had to be
                // pushed out into a rule per item.
                if (Conditions != null && Conditions.Count > 0
                    && !ConditionEvaluator.All(Conditions, Context.Vars, Context.Log, Context.PackId)) return;

                ActionRuntime.ExecuteList(Actions, Context);
            }
            catch (System.Exception ex)
            {
                // A throwing action must not leave the button stuck shrunk and
                // unclickable - the coroutine has to reach its second tween.
                Debug.LogError("[UI] a click action failed: " + ex);
            }
        }

        private IEnumerator ScaleTo(Vector3 target)
        {
            Vector3 from = transform.localScale;
            for (float t = 0f; t < ScaleDuration; t += Time.unscaledDeltaTime)
            {
                transform.localScale = Vector3.LerpUnclamped(from, target, QuadInOut(t / ScaleDuration));
                yield return null;
            }
            transform.localScale = target;
        }

        /// <summary>Quadratic in-out, the easing the game's own button uses.</summary>
        private static float QuadInOut(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
        }
    }
}
