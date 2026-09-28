using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using GameCreator.Runtime.Dialogue;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The game's own lines, in the conversations a pack extends, shown in the
    /// pack's translation of them - for a player who has said they want that
    /// (<see cref="GameLineNotice"/>).
    /// <para/>
    /// These lines are not in the pack: it saved only what it changed in the
    /// game's conversation. Its translation file has them under the same keys
    /// as its own lines (<see cref="PackTexts.LineKey"/>), each with the game's
    /// words as the note it was translated from, so the manifest pass cannot
    /// reach them and this does it on the game's own lines, once the pack's
    /// changes to the conversation are in.
    /// <para/>
    /// A translation is used only while its note is still what the game's line
    /// says - the same rule as every other text. A game update that rewrites a
    /// line leaves that line in the game's new words rather than showing a
    /// translation of the old ones. Nothing is put back afterwards, and nothing
    /// needs to be: the game builds its conversations again from the scene
    /// every time a game is loaded, and the choice can only change on the main
    /// menu - or on answering the question, before any of it was shown.
    /// </summary>
    internal static class GameLineTranslations
    {
        private const string Tag = "[SMSModForge.PackPlugin] Game lines: ";

        private struct Line
        {
            public Node Node;
            public string Text;
        }

        /// <summary>How many of the game's lines the pack's translation into the
        /// player's language would show - zero when it has none, or the player
        /// reads the pack in its own words.</summary>
        public static int Usable(PackContext ctx) => Find(ctx).Count;

        /// <summary>Show them. Asking again changes nothing: a line already
        /// showing the translation no longer says what it was translated from.</summary>
        public static int Apply(PackContext ctx, ManualLogSource log)
        {
            var lines = Find(ctx);
            int shown = 0;
            foreach (var line in lines)
            {
                if (VanillaDialogueInjector.SetText(line.Node, line.Text)) shown++;
                // In the pack's translation: not for XUnity.AutoTranslator to translate again.
                XUnityLink.Note(line.Text, ctx.Pack.TranslatedInto);
            }
            if (lines.Count > 0)
                log?.LogInfo(Tag + ctx.PackId + ": " + shown + " of the game's own lines shown in "
                             + (ctx.Pack.TranslatedInto ?? "?") + ", as the player chose.");
            return shown;
        }

        private static List<Line> Find(PackContext ctx)
        {
            var found = new List<Line>();
            var pack = ctx == null ? null : ctx.Pack;
            var translation = pack == null ? null : pack.Translation;
            var dialogues = pack == null || pack.Root == null ? null : pack.Root["dialogues"] as JArray;
            if (translation == null || dialogues == null) return found;

            foreach (var d in dialogues.OfType<JObject>())
            {
                string key = (string)d["key"];
                string source = (string)d[VanillaDialogueKeys.Source];
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(source)) continue;

                // The lines the pack rewrote or added are its own texts, and
                // were translated with the rest of it.
                var owned = new HashSet<int>();
                foreach (var n in (d["nodes"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var id = n["id"];
                    if (id != null && id.Type == JTokenType.Integer && !PackTexts.KeepsTheGamesText(n))
                        owned.Add((int)id);
                }

                Dialogue dialogue;
                try { dialogue = VanillaDialogueInjector.Find(source); }
                catch (Exception) { continue; }
                var content = dialogue != null && dialogue.Story != null ? dialogue.Story.Content : null;
                if (content == null || content.Nodes == null) continue;

                foreach (int id in content.Nodes.Keys.ToList())
                {
                    if (owned.Contains(id)) continue;
                    Node node;
                    try { node = content.Get(id); }
                    catch (Exception) { continue; }
                    if (node == null) continue;

                    string theirs = PackTexts.Usable(translation,
                                                     PackTexts.LineKey(key, id.ToString(CultureInfo.InvariantCulture)),
                                                     node.Text ?? "");
                    if (theirs != null) found.Add(new Line { Node = node, Text = theirs });
                }
            }
            return found;
        }
    }
}
