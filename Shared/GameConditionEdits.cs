using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// A pack's say in the conditions around the places the game starts or
    /// resets its own quests, in terms both sides compute the same way.
    /// <para/>
    /// Two different things, kept apart because they reach different things:
    /// <list type="bullet">
    ///   <item><b>A condition taken out of one of the game's scripts</b> (a
    ///   room's branch, most often). Stored once for the pack under
    ///   <see cref="GatesKey"/>, since the list belongs to the script rather
    ///   than to any one quest or conversation: taking a condition out changes
    ///   everything that list guards - the conversation that room plays, and
    ///   the quest step beside it. The Quests and Dialogues tabs both edit
    ///   it.</item>
    ///   <item><b>Conditions of the pack's that the quest step waits for</b>,
    ///   stored on the quest entry under <see cref="SiteConditionsKey"/>. The
    ///   conversation or script around the step runs as the game has it; only
    ///   the start or reset of the quest is held back until these pass.</item>
    /// </list>
    /// A condition taken out of a conversation's LINE is neither: it is a
    /// change to that line, and is stored with the conversation.
    /// <para/>
    /// Compiled into both projects; written for the plugin's compiler.
    /// </summary>
    public static class GameConditionEdits
    {
        // ── Conditions taken out of the game's scripts ────────────────────

        /// <summary>The pack's list of condition lists it changes.</summary>
        public const string GatesKey = "vanillaGates";

        /// <summary>The scene object holding the script, by path.</summary>
        public const string ByKey = "by";

        /// <summary>The script's type, by its short name ("Conditions").</summary>
        public const string ScriptKey = "script";

        /// <summary>
        /// Where the condition list is inside the script: field names and
        /// positions, from the script down to the list - the same steps the
        /// extraction took to find it, and the ones the plugin takes to change
        /// it.
        /// </summary>
        public const string AtKey = "at";

        /// <summary>The conditions taken out, each by its position in the
        /// game's list and its type, so a game update that reorders the list
        /// is noticed rather than taking out the wrong one.</summary>
        public const string RemovedKey = "removed";

        public const string IndexKey = "index";
        public const string TypeKey = "type";

        // ── On a quest entry ─────────────────────────────────────────────

        /// <summary>The pack's own conditions that put a started quest back to
        /// not started.</summary>
        public const string ResetConditionsKey = "resetConditions";

        /// <summary>Conditions the pack adds to one of the game's places that
        /// start or reset the quest.</summary>
        public const string SiteConditionsKey = "siteConditions";

        /// <summary>A place in a conversation: the conversation's path, the
        /// line's id, and whether the step runs as the line appears or once it
        /// is done.</summary>
        public const string DialogueKey = "dialogue";
        public const string NodeKey = "node";
        public const string MomentKey = "moment";

        /// <summary>What the step does: <see cref="Starts"/> or
        /// <see cref="Resets"/>.</summary>
        public const string DoesKey = "does";

        public const string ConditionsKey = "conditions";

        // ── What a changed place comes to ────────────────────────────

        /// <summary>On a place: the rooms the pack has taken out, so the quest
        /// can start away from where the game's script lives.</summary>
        public const string RoomsOutKey = "roomsOut";

        /// <summary>On a place: everything that has to pass for the quest to
        /// start (or be put back) there, worked out by the editor when the pack
        /// is saved. Its presence is what makes a place the pack's.</summary>
        public const string RuleKey = "rule";

        /// <summary>In a rule: the scripts that can bring the place about - the
        /// step's own, or each one that plays its conversation. Any one of
        /// them.</summary>
        public const string AnyKey = "any";

        /// <summary>In a rule's script: the paths of the condition lists that
        /// guard it, read from the game itself so whatever the pack takes out is
        /// already gone.</summary>
        public const string ListsKey = "lists";

        /// <summary>In a rule's script: where it lives, as a condition.</summary>
        public const string RoomKey = "room";

        /// <summary>In a rule's script: what it checks that the game keeps in no
        /// list, as the editor reads it.</summary>
        public const string FixedKey = "fixed";

        /// <summary>In a rule: the lines the conversation has to be on - the
        /// ones leading to the place, then its own.</summary>
        public const string LinesKey = "lines";

        public const string Starts = "starts";
        public const string Resets = "resets";

        public const string OnStart = "onStart";
        public const string OnFinish = "onFinish";

        /// <summary>The game's instruction for what a place does to its quest.</summary>
        public const string StartStep = "InstructionQuestsActivate";
        public const string ResetStep = "InstructionQuestsDeactivate";

        public static string StepFor(string does)
            => string.Equals(does, Resets, StringComparison.Ordinal) ? ResetStep
             : string.Equals(does, Starts, StringComparison.Ordinal) ? StartStep
             : null;

        // ── Addressing ───────────────────────────────────────────────────

        /// <summary>A location as text: "m_Branches/m_Branches/3/m_ConditionList".</summary>
        public static string PathText(JArray at)
        {
            if (at == null) return "";
            return string.Join("/", at.Select(Step).ToArray());
        }

        private static string Step(JToken step)
        {
            if (step == null) return "";
            if (step.Type == JTokenType.Integer) return ((long)step).ToString(CultureInfo.InvariantCulture);
            return (string)step ?? "";
        }

        /// <summary>One condition list, as one string: which object, which
        /// script on it, and where in the script.</summary>
        public static string GateKey(string by, string script, string pathText)
            => (by ?? "") + "|" + (script ?? "") + "|" + (pathText ?? "");

        public static string GateKey(JObject gate)
            => gate == null ? "" : GateKey((string)gate[ByKey], (string)gate[ScriptKey], PathText(gate[AtKey] as JArray));

        /// <summary>One place a conversation starts or resets a quest, as one
        /// string.</summary>
        public static string DialogueSiteKey(string dialogue, long node, string moment, string does)
            => "dialogue|" + (dialogue ?? "") + "|" + node.ToString(CultureInfo.InvariantCulture) + "|"
               + (moment ?? "") + "|" + (does ?? "");

        /// <summary>One script that starts or resets a quest, as one string.</summary>
        public static string ScriptSiteKey(string by, string script, string does)
            => "script|" + (by ?? "") + "|" + (script ?? "") + "|" + (does ?? "");

        public static string SiteKey(JObject site)
        {
            if (site == null) return "";
            string does = (string)site[DoesKey];
            string dialogue = (string)site[DialogueKey];
            if (!string.IsNullOrEmpty(dialogue))
            {
                long node = site[NodeKey] != null && site[NodeKey].Type == JTokenType.Integer ? (long)site[NodeKey] : 0;
                return DialogueSiteKey(dialogue, node, (string)site[MomentKey], does);
            }
            return ScriptSiteKey((string)site[ByKey], (string)site[ScriptKey], does);
        }

        // ── Applying ─────────────────────────────────────────────────────

        /// <summary>
        /// Which of a list's conditions stay, given what is taken out of it.
        /// <para/>
        /// A removal is honoured only where the list still has a condition of
        /// the recorded type at the recorded position; any other is added to
        /// <paramref name="refused"/> and changes nothing, so a game update that
        /// moved the list's conditions leaves them all in place rather than
        /// taking out whatever now sits where one used to be.
        /// </summary>
        /// <param name="liveTypes">The list as it is, by each condition's type.</param>
        /// <param name="removed">Position and type of each condition taken out.</param>
        public static List<int> Kept(IList<string> liveTypes, IEnumerable<KeyValuePair<int, string>> removed,
                                     List<KeyValuePair<int, string>> refused)
        {
            var count = liveTypes == null ? 0 : liveTypes.Count;
            var drop = new HashSet<int>();
            foreach (var r in removed ?? Enumerable.Empty<KeyValuePair<int, string>>())
            {
                if (r.Key >= 0 && r.Key < count && string.Equals(liveTypes[r.Key], r.Value, StringComparison.Ordinal))
                    drop.Add(r.Key);
                else if (refused != null)
                    refused.Add(r);
            }
            var kept = new List<int>();
            for (int i = 0; i < count; i++)
                if (!drop.Contains(i)) kept.Add(i);
            return kept;
        }

        /// <summary>The removals a gate entry names.</summary>
        public static List<KeyValuePair<int, string>> RemovalsOf(JObject gate)
        {
            var list = new List<KeyValuePair<int, string>>();
            if (!(gate?[RemovedKey] is JArray removed)) return list;
            foreach (var token in removed)
            {
                if (!(token is JObject r) || r[IndexKey] == null || r[IndexKey].Type != JTokenType.Integer) continue;
                list.Add(new KeyValuePair<int, string>((int)r[IndexKey], (string)r[TypeKey] ?? ""));
            }
            return list;
        }

        /// <summary>Whether a pack takes anything out of the game's scripts.</summary>
        public static bool RemovesAny(JObject manifest)
        {
            if (!(manifest?[GatesKey] is JArray gates)) return false;
            return gates.OfType<JObject>().Any(g => RemovalsOf(g).Count > 0);
        }
    }
}
