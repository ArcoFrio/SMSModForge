using BepInEx.Logging;
using GameCreator.Runtime.Common.UnityUI;
using GameCreator.Runtime.Dialogue;
using HarmonyLib;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A pack's line types out for as long as it takes to show, and no longer.
    /// <para/>
    /// The game times its typewriter by the length of the line as written,
    /// markup and all: <c>&lt;color=#FF0000&gt;</c> is seventeen characters
    /// nobody sees. The letters appear one visible character at a time, so a
    /// line with markup finished appearing while the typing sound was still
    /// playing - and a click in that time only stopped the sound, where it
    /// should have moved on, and the arrow to move on came late.
    /// <para/>
    /// Pack lines only, as the author asked: the lines a pack writes, whether
    /// in its own conversations or into the game's. Everything else keeps the
    /// game's own timing.
    /// </summary>
    internal static class TypewriterTiming
    {
        private const string Tag = "[SMSModForge.PackPlugin] Typing: ";

        /// <summary>Every line a pack has put in a conversation, with any
        /// <c>{name}</c> the game fills in as it shows it.</summary>
        private static readonly TextTemplates _lines = new TextTemplates();

        /// <summary>A new scene: the packs are read again.</summary>
        public static void Reset()
        {
            _lines.Clear();
        }

        /// <summary>A line a pack wrote. Only one with markup can be timed
        /// wrong, so only those are kept.</summary>
        public static void PackLine(string text)
        {
            if (RichText.MayHaveMarkup(text)) _lines.Add(text);
        }

        public static void Install(Harmony harmony, ManualLogSource log)
        {
            try
            {
                var duration = AccessTools.Method(typeof(Typewriter), "GetDuration", new[] { typeof(string) });
                if (duration == null)
                    log?.LogWarning(Tag + "Typewriter.GetDuration(string) is not there in this game build, so a pack "
                                    + "line with markup types on after it has appeared.");
                else
                    harmony.Patch(duration, postfix: new HarmonyMethod(typeof(TypewriterTiming), nameof(DurationPostfix)));

                var allShown = AccessTools.PropertyGetter(typeof(TextReference), "AreAllCharactersVisible");
                if (allShown == null)
                    log?.LogWarning(Tag + "TextReference.AreAllCharactersVisible is not there in this game build, so "
                                    + "the arrow after a pack line with markup comes late.");
                else
                    harmony.Patch(allShown, postfix: new HarmonyMethod(typeof(TypewriterTiming), nameof(AllShownPostfix)));
            }
            catch (System.Exception ex)
            {
                log?.LogError(Tag + "could not be patched - pack lines keep the game's timing. " + ex.Message);
            }
        }

        /// <summary>
        /// The game's time, cut to the share of the line that shows. Scaled
        /// rather than worked out again, so whatever the game's own sum is, a
        /// line without markup is timed exactly as before.
        /// </summary>
        private static void DurationPostfix(string text, ref float __result)
        {
            if (__result <= 0f || !RichText.MayHaveMarkup(text) || !_lines.Contains(text)) return;
            int visible = RichText.VisibleLength(text);
            if (visible < text.Length) __result *= visible / (float)text.Length;
        }

        /// <summary>
        /// "Has the whole line appeared?" - which the game answers by the line
        /// as written, and so says no until the letters that are markup have
        /// had their turn too. Only for Text Mesh Pro, which the conversation
        /// box is and which reveals by visible character; the older text
        /// component reveals the line as written, markup included.
        /// </summary>
        private static void AllShownPostfix(TextReference __instance, string ___m_Value, TMPro.TMP_Text ___m_TMP,
                                            ref bool __result)
        {
            if (__result || __instance == null || ___m_TMP == null) return;
            if (!RichText.MayHaveMarkup(___m_Value) || !_lines.Contains(___m_Value)) return;
            if (__instance.CharactersVisible >= RichText.VisibleLength(___m_Value)) __result = true;
        }
    }
}
