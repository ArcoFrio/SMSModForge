using System;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The Transitions action (the author, 1.7.0): a transition chosen by
    /// style, on screen for as long as asked, then gone - where a pack used to
    /// emit one signal to go in and another, timed by hand, to come out.
    /// <para/>
    /// Only what the game uses AS a transition: covering the screen while
    /// something changes behind it. Checked against how the game's own
    /// conversations use each signal (the extracted dialogues, 1.7.0):
    /// <list type="bullet">
    ///   <item><c>FadeInBlack</c> / <c>FadeOutBlack</c> - always a pair, with
    ///   the scene changed between them.</item>
    ///   <item><c>FadeIn2025</c> / <c>FadeOut2025</c> - the same, the game's
    ///   travel fade.</item>
    ///   <item><c>Blink</c> - one signal, closing and opening by itself; the
    ///   game changes the scene a second in, while it is closed (210 uses).</item>
    ///   <item><c>whiteflashnosound</c> - a white flash the game cuts on: a
    ///   blow lands and the scene is another.</item>
    ///   <item>The "A few minutes later..." screen - not a signal but an object
    ///   the game switches on - made here into a black screen with any words
    ///   on it.</item>
    /// </list>
    /// Left out, because they mark a moment rather than hide a change:
    /// <c>kiss</c>, <c>drink</c>, the camera <c>flash</c>, and the UI's own
    /// <c>FadeUI</c> / <c>ForceEnableUI</c>. EmitSignal still has those.
    /// <para/>
    /// Compiled into the plugin, which plays them, and the editor, which offers
    /// them.
    /// </summary>
    public static class Transitions
    {
        public const string ActionType = "Transitions";

        public const string StyleParam = "style";
        public const string SecondsParam = "seconds";
        public const string TextParam = "text";

        public const string FadeToBlack = "FadeToBlack";
        public const string TextScreen = "TextScreen";
        public const string Blink = "Blink";
        public const string WhiteFlash = "WhiteFlash";
        public const string TravelFade = "TravelFade";

        /// <summary>Every style, in the order the editor lists them.</summary>
        public static readonly string[] Styles = { FadeToBlack, TextScreen, Blink, WhiteFlash, TravelFade };

        /// <summary>The styles whose time on screen the author sets. The game's
        /// blink and flash run their own course.</summary>
        public static readonly string[] Timed = { FadeToBlack, TextScreen, TravelFade };

        /// <summary>The words on a black screen nobody has typed any into: the
        /// game's own.</summary>
        public const string DefaultText = "A few minutes later...";   // English on purpose: the game's words, the starting text an author types over.

        public const float DefaultSeconds = 2f;

        public static bool IsTimed(string style) => Array.IndexOf(Timed, style) >= 0;

        public static bool HasText(string style) => style == TextScreen;

        /// <summary>The game's signal that starts a style, or null for one the
        /// plugin draws itself.</summary>
        public static string InSignal(string style)
        {
            switch (style)
            {
                case FadeToBlack: return "FadeInBlack";
                case TravelFade: return "FadeIn2025";
                case Blink: return "Blink";
                case WhiteFlash: return "whiteflashnosound";
                default: return null;
            }
        }

        /// <summary>The game's signal that ends a style, or null for one that
        /// ends by itself.</summary>
        public static string OutSignal(string style)
        {
            switch (style)
            {
                case FadeToBlack: return "FadeOutBlack";
                case TravelFade: return "FadeOut2025";
                default: return null;
            }
        }

        /// <summary>
        /// Seconds from the start until the screen is covered - when the actions
        /// after a transition run, so what they change is not seen. From the
        /// game's own conversations: it waits a second after a fade or a blink
        /// before changing anything, and a tenth of one after the flash.
        /// </summary>
        public static float CoverSeconds(string style)
        {
            switch (style)
            {
                case WhiteFlash: return 0.1f;
                case TextScreen: return 0.75f;
                default: return 1f;
            }
        }

        /// <summary>Seconds a style takes to clear once it starts going.</summary>
        public static float ClearSeconds(string style)
        {
            switch (style)
            {
                case WhiteFlash: return 0.9f;
                case Blink: return 1.5f;
                case TextScreen: return 0.75f;
                default: return 1f;
            }
        }
    }
}
