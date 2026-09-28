using System.Collections.Generic;
using System.Globalization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Localization;

/// <summary>
/// The game's own lines in the conversations a pack extends, as texts a
/// translation of the pack can have.
/// <para/>
/// They are not the pack's: the pack saves only what it changed in the game's
/// conversation, so a line it left alone is not in it at all, and its words
/// stay the game's in every language the pack is written in. A translation can
/// still give them words - the author decided it (2026-09-24) - because a
/// conversation half in the player's language and half in English reads worse
/// than either. The PLAYER then decides whether to see them: mods exist that
/// translate the whole game, and a pack's words would sit on top of theirs.
/// <para/>
/// Keyed as the pack's own lines are (<see cref="PackTexts.LineKey"/>), with
/// the game's words as the note they were translated from - so rewriting one of
/// them in the pack puts its translation out of date, as it should.
/// </summary>
public static class GameLines
{
    public sealed record Line(string Key, DialogueDef Dialogue, DialogueNodeDef Node, string Text);

    /// <summary>
    /// Every line of the game's, in the pack's extended conversations, whose
    /// words the pack has not changed - in each conversation's order. Only
    /// lines of plain text; a line the extraction holds in styled runs has no
    /// one text to translate. None when the editor has no extraction.
    /// </summary>
    /// <param name="taken">Keys the pack's own texts already use, left alone.</param>
    public static List<Line> Of(ModPack? pack, ISet<string>? taken = null)
    {
        var lines = new List<Line>();
        if (pack?.Dialogues == null || !VanillaDialogueCatalog.IsAvailable) return lines;

        foreach (var d in pack.Dialogues)
        {
            if (!d.IsVanillaBased || string.IsNullOrEmpty(d.Key)) continue;
            var game = VanillaDialogueCatalog.Open(d.Source);
            if (game == null) continue;

            foreach (var n in d.Nodes)
            {
                var baseline = game.Node(n.Id);
                if (baseline == null) continue;              // a line the pack added: its own

                string words = baseline.PlainText ?? "";
                if (PackTexts.IsEmpty(words)) continue;
                // Rewritten by the pack: its words, and one of its own texts.
                if (PackTexts.Normal(n.Text) != PackTexts.Normal(words)) continue;

                string key = PackTexts.LineKey(d.Key, n.Id.ToString(CultureInfo.InvariantCulture));
                if (taken != null && taken.Contains(key)) continue;
                lines.Add(new Line(key, d, n, words));
            }
        }
        return lines;
    }
}
