using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What a player is told about packs showing the game's own lines - the
    /// lines of the game's conversations a pack extends but did not rewrite -
    /// in the pack's translation of them.
    /// <para/>
    /// They always are, when the pack has them in the player's language (the
    /// author, 2026-09-27: "we should be translating the rest of the vanilla
    /// nodes ... make it obligatory, rather than ask"). It used to be the
    /// player's call, asked once and off until answered. What is left of that
    /// is the reason it was asked: mods exist that translate the whole game,
    /// and a pack's words for the game's lines sit on top of theirs. So the
    /// player is told, once - inside the warning about a save when there is
    /// one, on its own otherwise.
    /// </summary>
    public static class GameLineNotice
    {
        /// <summary>The notice on its own, for a save with nothing else to
        /// warn about.</summary>
        public static SaveWarningText Notice(IList<string> packs, string language)
        {
            var text = new SaveWarningText { Title = GameTexts.T("game.gameLines.title") };
            text.Paragraphs.Add(Body(packs, language));
            return text;
        }

        /// <summary>
        /// The notice's paragraph, for the save warning to carry when there is
        /// one: <paramref name="packs"/> are the packs that translate the
        /// game's lines into the player's language, <paramref name="language"/>
        /// its name.
        /// </summary>
        public static string Body(IList<string> packs, string language)
        {
            var names = (packs ?? new List<string>()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            return GameTexts.P("game.gameLines.body", Math.Max(1, names.Count),
                               "packs", SaveLoadChecks.Quoted(names), "language", language ?? "");
        }
    }
}
