using TMPro;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Keeps a UI label carrying <c>[PV:name]</c> tokens in step with the
    /// variables it names.
    /// <para/>
    /// The same syntax dialogue lines and button labels already use, so an
    /// author who has written <c>[PV:Coins]</c> once knows it everywhere. A
    /// label showing a count, a price or a name is otherwise a number frozen at
    /// the moment the pack was written.
    /// <para/>
    /// Attached only to labels that actually carry a token - the check is a
    /// substring search done once at build - so a screen of plain text costs
    /// nothing per frame. The raw text is kept rather than the resolved one:
    /// resolving in place would consume the token on the first pass and leave
    /// nothing to re-resolve afterwards.
    /// </summary>
    internal sealed class UiLiveText : MonoBehaviour
    {
        /// <summary>The text as authored, tokens intact.</summary>
        public string Raw;

        /// <summary>Which pack's variables the tokens name.</summary>
        public string PackId;

        private TextMeshProUGUI _label;

        private void Awake()
        {
            _label = GetComponent<TextMeshProUGUI>();
        }

        private void Update()
        {
            if (_label == null || string.IsNullOrEmpty(Raw)) return;

            string resolved = TextPlaceholders.Resolve(Raw, Plugin.TryGetPackVars(PackId));

            // Only on a change: assigning TMP's text marks the mesh dirty and
            // forces a rebuild, and doing that every frame to a label that has
            // not changed is a cost for nothing.
            if (_label.text != resolved) _label.text = resolved;
        }
    }
}
