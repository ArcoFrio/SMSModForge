using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What the game checks when a save is loaded: which of the game's own
    /// things a pack changes in a way that can break a save already under way,
    /// which packs a save has data for that are not running, and what the
    /// player is told about either.
    /// <para/>
    /// Compiled into both projects from one file. The runtime reads a pack's
    /// manifest with <see cref="Of"/>; the editor's tests run the same function
    /// on the manifest the editor writes, so the two cannot drift apart. Written
    /// for the plugin's compiler as well as the editor's.
    /// </summary>
    public static class SaveLoadChecks
    {
        // ── What a pack changes ──────────────────────────────────────────

        /// <summary>Tasks added to, or taken out of, one of the game's quests.</summary>
        public const string Quests = "quests";

        /// <summary>Lines of one of the game's conversations changed, added or
        /// taken out, or conditions taken out of what plays one.</summary>
        public const string Dialogues = "dialogues";

        /// <summary>In the order the warning names them.</summary>
        public static readonly string[] All = { Quests, Dialogues };

        /// <summary>
        /// The kinds of the game's own content a pack's manifest changes in a
        /// way that can break a save already under way. Changes to the game's
        /// places, to its screens, and to its characters' art, voice or name
        /// colour are not among them (the user's call, 2026-09-17, for places).
        /// <para/>
        /// A save marked with a kind that is no longer asked about - "places",
        /// written by an earlier build - keeps the word and is not affected by
        /// it: a kind is only asked about when a pack reports it.
        /// </summary>
        public static List<string> Of(JObject manifest)
        {
            var kinds = new List<string>();
            if (manifest == null) return kinds;
            if (ChangesQuests(manifest)) kinds.Add(Quests);
            if (ChangesDialogues(manifest)) kinds.Add(Dialogues);
            return kinds;
        }

        private static IEnumerable<JObject> Entries(JObject manifest, string key)
            => (manifest[key] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        private static bool Named(JObject entry, string key)
            => !string.IsNullOrWhiteSpace((string)entry[key]);

        private static int Count(JToken token) => (token as JArray)?.Count ?? 0;

        private static bool IsTrue(JToken token) => token != null && token.Type == JTokenType.Boolean && (bool)token;

        private static bool ChangesQuests(JObject manifest)
            => Entries(manifest, "quests").Any(ChangesTasks);

        private static bool ChangesTasks(JObject quest)
            => Named(quest, "source")
               && (Count(quest[QuestTreeEdits.AddedTasksKey]) > 0
                   || Entries(quest, "vanillaTasks").Any(h => IsTrue(h[QuestTreeEdits.RemovedKey])));

        /// <summary>Whether the pack has taken one of the game's own places
        /// that start or reset this quest over - then what starts the quest is
        /// the pack's rule rather than the game's own doing.</summary>
        private static bool TakesPlacesOver(JObject quest)
            => Named(quest, "source")
               && Entries(quest, GameConditionEdits.SiteConditionsKey)
                   .Any(p => p[GameConditionEdits.RuleKey] is JObject);

        /// <summary>
        /// The game's quests a manifest changes, as the manifest names them,
        /// each with the keys of the tasks and subtasks the pack adds to it, in
        /// the manifest's order. The ids those keys stand for are the game
        /// quest's (<see cref="QuestIds.AddedTaskId"/>, with the quest's own
        /// name), which only the game can say.
        /// <para/>
        /// Tasks added or taken out, and the game's own places that start or
        /// reset it taken over: both are worth naming to a player whose save is
        /// already part-way through that quest.
        /// </summary>
        public static List<KeyValuePair<string, List<string>>> QuestsChanged(JObject manifest)
        {
            var found = new List<KeyValuePair<string, List<string>>>();
            if (manifest == null) return found;
            foreach (var quest in Entries(manifest, "quests").Where(q => ChangesTasks(q) || TakesPlacesOver(q)))
            {
                string source = ((string)quest["source"]).Trim();
                var added = new List<string>();
                foreach (var task in Entries(quest, QuestTreeEdits.AddedTasksKey))
                {
                    added.Add(((string)task["key"] ?? "").Trim());
                    added.AddRange(Entries(task, "subtasks").Select(s => ((string)s["key"] ?? "").Trim()));
                }
                added.RemoveAll(k => k.Length == 0);
                int at = found.FindIndex(p => string.Equals(p.Key, source, StringComparison.Ordinal));
                if (at < 0) found.Add(new KeyValuePair<string, List<string>>(source, added));
                else found[at].Value.AddRange(added);
            }
            return found;
        }

        /// <summary>A dialogue extension saves only what it changes, so one
        /// with no lines and nothing taken out changes nothing. A condition
        /// taken out of one of the game's scripts changes when a conversation
        /// plays, which is the same kind of change.</summary>
        private static bool ChangesDialogues(JObject manifest)
            => ChangesLines(manifest) || GameConditionEdits.RemovesAny(manifest);

        private static bool ChangesLines(JObject manifest)
            => Entries(manifest, "dialogues").Any(d =>
                   Named(d, VanillaDialogueKeys.Source)
                   && (Count(d["nodes"]) > 0 || Count(d[VanillaDialogueKeys.RemovedNodes]) > 0));

        /// <summary>
        /// What each kind this manifest reports comes to, where the manifest can
        /// say something more exact than the kind's own word.
        /// <para/>
        /// Both of the things the <see cref="Dialogues"/> kind stands for are
        /// really about the game's own story, and which one it is matters to
        /// whoever reads the window. A condition taken out where one of the
        /// game's quests starts changes WHEN THAT QUEST STARTS; the same
        /// condition taken out anywhere else changes when a conversation plays;
        /// a rewritten line changes the conversation itself. Saying
        /// "conversations" for all three tells the author of a quest-only pack
        /// that they rewrote dialogue they never opened. The kinds themselves
        /// stay as they are, because a save is marked with them.
        /// </summary>
        public const string QuestsPart = "quests";
        public const string ConversationsPart = "conversations";
        public const string QuestStartsPart = "questStarts";
        public const string ConversationPlaysPart = "conversationPlays";

        public static Dictionary<string, List<string>> WordsOf(JObject manifest)
        {
            var words = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (manifest == null) return words;

            // Every list and line one of the game's quests is started or reset
            // at, where this pack has taken that place over.
            var lists = new HashSet<string>(StringComparer.Ordinal);
            var lines = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in Entries(manifest, "quests"))
                foreach (var place in Entries(quest, GameConditionEdits.SiteConditionsKey))
                {
                    if (!(place[GameConditionEdits.RuleKey] is JObject rule)) continue;
                    foreach (var scope in Items(rule[GameConditionEdits.AnyKey]))
                        foreach (var path in (scope[GameConditionEdits.ListsKey] as JArray) ?? new JArray())
                            lists.Add(GameConditionEdits.GateKey(
                                (string)scope[GameConditionEdits.ByKey], (string)scope[GameConditionEdits.ScriptKey],
                                GameConditionEdits.PathText(path as JArray)));
                    foreach (var line in Items(rule[GameConditionEdits.LinesKey]))
                        lines.Add(LineKey((string)line[GameConditionEdits.DialogueKey],
                                          (long?)line[GameConditionEdits.NodeKey] ?? 0));
                }

            bool starts = false, plays = false, rewrites = false;
            foreach (var gate in Items(manifest[GameConditionEdits.GatesKey]))
            {
                if (GameConditionEdits.RemovalsOf(gate).Count == 0) continue;
                if (lists.Contains(GameConditionEdits.GateKey(gate))) starts = true;
                else plays = true;
            }
            foreach (var dialogue in Entries(manifest, "dialogues"))
            {
                if (!Named(dialogue, VanillaDialogueKeys.Source)) continue;
                string path = ((string)dialogue[VanillaDialogueKeys.Source] ?? "");
                if (path.StartsWith(VanillaDialogueKeys.SourcePrefix, StringComparison.OrdinalIgnoreCase))
                    path = path.Substring(VanillaDialogueKeys.SourcePrefix.Length);
                if (Count(dialogue[VanillaDialogueKeys.RemovedNodes]) > 0) rewrites = true;

                foreach (var node in Entries(dialogue, "nodes"))
                {
                    var named = ((node[VanillaDialogueKeys.Overrides] as JArray) ?? new JArray())
                        .Select(o => (string)o).ToList();
                    // A line changed only in what it checks, at a line one of
                    // the game's quests starts at, is that quest's start - not
                    // a rewritten conversation.
                    bool onlyConditions = named.Count > 0
                                          && named.All(o => string.Equals(o, GameConditionEdits.ConditionsKey,
                                                                          StringComparison.Ordinal));
                    if (onlyConditions && lines.Contains(LineKey(path, (long?)node["id"] ?? 0))) starts = true;
                    else rewrites = true;
                }
            }

            // Parts, by name: whoever says them picks the words for where each
            // falls in the sentence (Describe).
            var said = new List<string>();
            if (rewrites) said.Add(ConversationsPart);
            if (starts) said.Add(QuestStartsPart);
            if (plays) said.Add(ConversationPlaysPart);
            if (said.Count > 0) words[Dialogues] = said;
            return words;
        }

        private static IEnumerable<JObject> Items(JToken token)
            => (token as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        /// <summary>One line of one conversation, as both sides name it.</summary>
        private static string LineKey(string dialogue, long node)
            => (dialogue ?? "") + "|" + unchecked((int)node).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // ── What a save has already been played with ─────────────────────

        /// <summary>The mark a save keeps: the kinds it has been played with,
        /// in a fixed order so the same set always reads the same.</summary>
        public static string Mark(IEnumerable<string> kinds)
            => string.Join(",", (kinds ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => Array.IndexOf(All, k) < 0 ? int.MaxValue : Array.IndexOf(All, k))
                .ThenBy(k => k, StringComparer.Ordinal)
                .ToArray());

        public static List<string> ReadMark(string mark)
            => (mark ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();

        /// <summary>The kinds a pack changes that the save has not been played
        /// with yet - so a pack that starts changing something new warns
        /// again, and one that changes nothing new does not.</summary>
        public static List<string> NotYetSeen(IEnumerable<string> kinds, string mark)
        {
            var seen = new HashSet<string>(ReadMark(mark), StringComparer.Ordinal);
            return (kinds ?? Enumerable.Empty<string>()).Where(k => !seen.Contains(k)).Distinct().ToList();
        }

        /// <summary>The mark after a player goes on with these kinds as well.</summary>
        public static string Merge(string mark, IEnumerable<string> kinds)
            => Mark(ReadMark(mark).Concat(kinds ?? Enumerable.Empty<string>()));

        // ── Packs a save has data for ────────────────────────────────────

        /// <summary>How a pack's save file is named in a save slot's folder.</summary>
        public const string SaveFilePrefix = "SMSModForge_";
        public const string SaveFileSuffix = ".json";

        /// <summary>The pack id a file in a save slot's folder belongs to, or
        /// null when it is not a pack's save file.</summary>
        public static string PackIdOfSaveFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (!fileName.StartsWith(SaveFilePrefix, StringComparison.OrdinalIgnoreCase)) return null;
            if (!fileName.EndsWith(SaveFileSuffix, StringComparison.OrdinalIgnoreCase)) return null;
            int length = fileName.Length - SaveFilePrefix.Length - SaveFileSuffix.Length;
            return length > 0 ? fileName.Substring(SaveFilePrefix.Length, length) : null;
        }

        /// <summary>The packs a save slot holds data for that are not running,
        /// sorted so the message reads the same every time.</summary>
        public static List<string> MissingPacks(IEnumerable<string> fileNamesInSlot, ICollection<string> runningPackIds)
        {
            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in fileNamesInSlot ?? Enumerable.Empty<string>())
            {
                string id = PackIdOfSaveFile(name);
                if (id == null) continue;
                if (runningPackIds != null && runningPackIds.Any(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                missing.Add(id);
            }
            return missing.ToList();
        }

        // ── What the player is told ──────────────────────────────────────

        /// <summary>
        /// A pack the save has data for that is not running, and why: switched
        /// off on the main menu, installed but not loaded, or not installed.
        /// Its version is the one the save recorded (<see cref="SaveRecord"/>)
        /// where there is a record.
        /// </summary>
        public sealed class AbsentPack
        {
            public string Id;
            public string Version;

            /// <summary><see cref="SaveRecord.SwitchedOff"/>, <see cref="SaveRecord.NotLoaded"/>
            /// or <see cref="SaveRecord.NotInstalled"/>.</summary>
            public string State;

            public AbsentPack(string id, string version, string state)
            {
                Id = id ?? "";
                Version = version ?? "";
                State = state ?? SaveRecord.NotInstalled;
            }

            /// <summary>How the warning names it: quoted, with its version beside it.</summary>
            public string Label
            {
                get { return GameTexts.Quoted(Id) + (string.IsNullOrEmpty(Version) ? "" : "  v" + Version); }
            }
        }

        /// <summary>
        /// The packs a save has data for that are not running, each with why
        /// (<see cref="SaveRecord.WhyNotRunning"/>) and the version the save's
        /// record gives it.
        /// </summary>
        public static List<AbsentPack> Absent(IEnumerable<string> fileNamesInSlot, ICollection<string> runningPackIds,
                                              ICollection<string> installedPackIds, ICollection<string> switchedOffPackIds,
                                              SaveRecord record)
        {
            var absent = new List<AbsentPack>();
            foreach (string id in MissingPacks(fileNamesInSlot, runningPackIds))
            {
                var had = record == null ? null : record.Find(id);
                absent.Add(new AbsentPack(id, had == null ? "" : had.Version,
                                          SaveRecord.WhyNotRunning(id, installedPackIds, switchedOffPackIds)));
            }
            return absent;
        }

        /// <summary>
        /// Of the packs not running, the ones the player has not been told
        /// about for this save yet. Each pack on its own: being told about one
        /// says nothing about another that has gone since.
        /// <para/>
        /// The notice was shown on every load, so a player who had switched a
        /// pack off on purpose was told the same thing every time they played
        /// (2026-09-27).
        /// </summary>
        public static List<AbsentPack> NotYetTold(IEnumerable<AbsentPack> absent, IEnumerable<string> told)
        {
            var already = new HashSet<string>(told ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (absent ?? Enumerable.Empty<AbsentPack>())
                .Where(a => a != null && !string.IsNullOrEmpty(a.Id) && !already.Contains(a.Id))
                .ToList();
        }

        /// <summary>
        /// What a save remembers having told the player, after a load: only the
        /// packs that are still not running, so one that runs with the save
        /// again comes off the list - taking it away after that tells them
        /// once more - and then the ones told now.
        /// </summary>
        /// <param name="toldBefore">The save's list as it was loaded.</param>
        /// <param name="stillAbsent">The packs it has data for that are not running now.</param>
        /// <param name="toldNow">The packs the notice has just named.</param>
        public static List<string> Told(IEnumerable<string> toldBefore, IEnumerable<string> stillAbsent,
                                        IEnumerable<string> toldNow)
        {
            var absentNow = new HashSet<string>(stillAbsent ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return SaveRecord.Ids((toldBefore ?? Enumerable.Empty<string>()).Where(absentNow.Contains)
                                  .Concat(toldNow ?? Enumerable.Empty<string>()));
        }

        /// <summary>One of the game's quests whose tasks a pack changes, and
        /// where the save stands with it.</summary>
        public sealed class ChangedQuest
        {
            public string Name;
            public QuestTreeEdits.SaveProgress Progress;

            public ChangedQuest(string name, QuestTreeEdits.SaveProgress progress)
            {
                Name = name;
                Progress = progress;
            }
        }

        /// <summary>One pack, and what it changes that the save has not been
        /// played with.</summary>
        public sealed class PackChanges
        {
            public string PackId;
            public IList<string> Kinds;

            /// <summary>What this pack's kinds come to in its own case
            /// (<see cref="WordsOf"/>); the kind's own word where it says
            /// nothing.</summary>
            public IDictionary<string, List<string>> Words;

            /// <summary>
            /// The save's own file for this pack was written by a plugin from
            /// before saves were marked with what they had been played with: the
            /// save was played with the pack, but nobody wrote down with which
            /// of its changes.
            /// </summary>
            public bool SaveBeforeMarks;

            public PackChanges(string packId, IList<string> kinds, IDictionary<string, List<string>> words = null)
            {
                PackId = packId;
                Kinds = kinds;
                Words = words;
            }
        }

        /// <summary>
        /// The heading and the paragraphs of the warning shown when a save is
        /// loaded, or null when there is nothing to say. Both reasons can apply
        /// at once, and then both are said, in one window.
        /// <para/>
        /// Kept short on purpose: a player reads a line or two and a list, so
        /// what the packs change is a list, and each quest is a few words on
        /// where this save stands with it. A paragraph that is a list has its
        /// items on lines of their own, each starting with
        /// <see cref="SaveWarningText.Bullet"/>.
        /// </summary>
        /// <param name="unseen">Packs that change the game's own content in
        /// ways this save has not been played with.</param>
        /// <param name="missing">Packs this save has data for that are not
        /// installed.</param>
        /// <param name="quests">The game's quests those packs change the tasks
        /// of, and where this save stands with each.</param>
        public static SaveWarningText Warning(IList<PackChanges> unseen, IList<string> missing,
                                              IList<ChangedQuest> quests = null)
        {
            var absent = (missing ?? new List<string>()).Select(id => new AbsentPack(id, "", SaveRecord.NotInstalled)).ToList();
            return WarningFor(unseen, absent, quests);
        }

        /// <param name="absent">Packs this save has data for that are not
        /// running: switched off on the main menu, or not installed.</param>
        public static SaveWarningText WarningFor(IList<PackChanges> unseen, IList<AbsentPack> absent,
                                              IList<ChangedQuest> quests = null)
        {
            var risky = (unseen ?? new List<PackChanges>()).Where(p => p != null && p.Kinds != null && p.Kinds.Count > 0).ToList();
            var missing = (absent ?? new List<AbsentPack>()).Where(a => a != null && !string.IsNullOrEmpty(a.Id)).ToList();
            bool gone = missing.Count > 0;
            if (risky.Count == 0 && !gone) return null;

            var text = new SaveWarningText { Title = GameTexts.T("game.save.title") };
            if (risky.Count == 1)
            {
                text.Paragraphs.Add(GameTexts.F(
                    CannotTell(risky[0]) ? "game.save.onePack.cannotTell" : "game.save.onePack",
                    "pack", GameTexts.Quoted(risky[0].PackId), "what", Describe(risky[0])));
            }
            else if (risky.Count > 1)
            {
                // What each one changes goes in the list, which scrolls: the
                // sentence stays the same length however many packs there are.
                text.Paragraphs.Add(GameTexts.T(
                    risky.Any(CannotTell) ? "game.save.somePacks.cannotTell" : "game.save.somePacks"));
                text.Details.Add(new SaveWarningSection(
                    GameTexts.T("game.save.eachPack"),
                    risky.Select(p => GameTexts.F("game.save.packChanges",
                        "pack", GameTexts.Quoted(p.PackId),
                        "what", GameTexts.JoinAnd(Said(p).Select(w => Words(w, "listed")).ToList())))));
            }

            var listed = (quests ?? new List<ChangedQuest>())
                .Where(q => q != null && !string.IsNullOrEmpty(q.Name))
                .GroupBy(q => q.Name, StringComparer.Ordinal)
                .Select(g => g.OrderBy(q => Rank(q.Progress)).First())
                .OrderBy(q => Rank(q.Progress))
                .ToList();
            if (risky.Count > 0 && listed.Count > 0)
            {
                text.Details.Add(new SaveWarningSection(
                    GameTexts.P("game.save.questsChanged", listed.Count),
                    listed.Select(q => GameTexts.F("game.save.quest", "quest", q.Name, "standing", Standing(q.Progress)))));
            }
            if (risky.Count > 0) text.After.Add(GameTexts.T("game.save.newGameSafest"));

            if (gone)
            {
                // Named in the list, which scrolls, rather than in the sentence:
                // there can be many, and a player needs to see which are switched
                // off - one tick away - and which have gone altogether.
                string said = GameTexts.P(risky.Count > 0 ? "game.save.missingAlso" : "game.save.missing", missing.Count);
                if (risky.Count > 0) text.After.Add(said);
                else text.Paragraphs.Add(said);

                foreach (var why in new[]
                         {
                             new KeyValuePair<string, string>(SaveRecord.SwitchedOff, "game.save.switchedOff"),
                             new KeyValuePair<string, string>(SaveRecord.NotLoaded, "game.save.notLoaded"),
                             new KeyValuePair<string, string>(SaveRecord.NotInstalled, "game.save.notInstalled"),
                         })
                {
                    var these = missing.Where(a => a.State == why.Key).ToList();
                    if (these.Count > 0)
                        text.Details.Add(new SaveWarningSection(GameTexts.T(why.Value), these.Select(a => a.Label)));
                }
            }
            return text;
        }

        /// <summary>
        /// Whether all that can honestly be said is that nobody knows. A save
        /// whose file for the pack predates the marks was played with the pack,
        /// and the one risky change a pack could make back then is a rewritten
        /// conversation - so for that, and only that, the save may well have
        /// been played with it. Everything else a pack can change here (the
        /// game's quests, when they start, when a conversation plays) is newer
        /// than the marks, so an older save has certainly never seen it.
        /// </summary>
        private static bool CannotTell(PackChanges pack)
            => pack != null && pack.SaveBeforeMarks && Said(pack).Contains(Word(Dialogues));

        /// <summary>Worst first, so the quest that needs reading is on top.</summary>
        private static int Rank(QuestTreeEdits.SaveProgress progress)
        {
            switch (progress)
            {
                case QuestTreeEdits.SaveProgress.MayGetStuck: return 0;
                case QuestTreeEdits.SaveProgress.InProgress: return 1;
                case QuestTreeEdits.SaveProgress.Finished: return 2;
                default: return 3;
            }
        }

        /// <summary>Where the save stands with a quest, in a few words.</summary>
        public static string Standing(QuestTreeEdits.SaveProgress progress)
        {
            switch (progress)
            {
                case QuestTreeEdits.SaveProgress.MayGetStuck:
                    return GameTexts.T("game.save.standing.mayGetStuck");
                case QuestTreeEdits.SaveProgress.InProgress:
                    return GameTexts.T("game.save.standing.inProgress");
                case QuestTreeEdits.SaveProgress.Finished:
                    return GameTexts.T("game.save.standing.finished");
                default:
                    return GameTexts.T("game.save.standing.notStarted");
            }
        }

        /// <summary>"the game's own quests and conversations", or "when the
        /// game's own conversations play" - what this pack changes, said so it
        /// can follow "'X' changes".</summary>
        public static string Describe(PackChanges pack)
        {
            // "the game's own quests and conversations", "when the game's own
            // quests start", "the game's own conversations and when its quests
            // start": only the first thing named names the game, so each part
            // has words for coming first and words for coming after.
            var said = Said(pack);
            var framed = new List<string>();
            for (int i = 0; i < said.Count; i++)
                framed.Add(Words(said[i], i == 0 ? "first" : "then"));
            return GameTexts.JoinAnd(framed);
        }

        /// <summary>A part's words for where it falls: <c>first</c> in the
        /// sentence, <c>then</c> after another, or <c>listed</c> on its own
        /// pack's line. A part this version has no words for is said as its
        /// name.</summary>
        private static string Words(string part, string where)
        {
            string key = "game.change." + part + "." + where;
            return GameTexts.Current.Has(key) ? GameTexts.T(key) : part;
        }

        /// <summary>"the game's own quests and conversations", for a caller
        /// holding only the kinds.</summary>
        public static string Describe(IList<string> kinds) => Describe(new PackChanges("", kinds));

        /// <summary>What one pack changes, kind by kind, as the parts that fit
        /// its own case - quests, conversations, when its conversations
        /// play.</summary>
        private static List<string> Said(PackChanges pack)
        {
            var words = new List<string>();
            foreach (string kind in pack?.Kinds ?? new List<string>())
            {
                List<string> parts;
                if (pack.Words != null && pack.Words.TryGetValue(kind, out parts) && parts != null && parts.Count > 0)
                    words.AddRange(parts);
                else words.Add(Word(kind));
            }
            return words;
        }

        private static string Word(string kind)
        {
            switch (kind)
            {
                case Quests: return QuestsPart;
                case Dialogues: return ConversationsPart;
                default: return kind;
            }
        }

        /// <summary>Names, quoted and joined - a pack id is a name and not a
        /// word: "'A' and 'B'".</summary>
        public static string Quoted(IList<string> names)
            => GameTexts.JoinAnd((names ?? new List<string>()).Select(GameTexts.Quoted).ToList());
    }

    /// <summary>
    /// The warning, in three parts, so the window can keep it to one screen
    /// however much the packs change: what the player has to know
    /// (<see cref="Paragraphs"/>), the list of what changed, which is the part
    /// that grows with every pack and quest (<see cref="Details"/> - smaller,
    /// in a box of its own that scrolls), and what follows from it
    /// (<see cref="After"/>).
    /// </summary>
    public sealed class SaveWarningText
    {
        /// <summary>What starts each item of a list.</summary>
        public const string Bullet = "\u2022 ";

        public string Title = "";

        /// <summary>Said above the list.</summary>
        public readonly List<string> Paragraphs = new List<string>();

        /// <summary>What each pack changes and which quests, each under a
        /// heading. Empty when there is nothing to list.</summary>
        public readonly List<SaveWarningSection> Details = new List<SaveWarningSection>();

        /// <summary>Said below the list.</summary>
        public readonly List<string> After = new List<string>();

        public static string ContinueLabel { get { return GameTexts.T("game.save.continue"); } }
        public static string ReturnLabel { get { return GameTexts.T("game.save.returnToMenu"); } }

        /// <summary>All of it on one line, in the order it is shown - for the
        /// log.</summary>
        public string Plain()
        {
            var parts = new List<string>(Paragraphs);
            foreach (var section in Details)
                parts.Add(section.Heading + " " + string.Join("; ", section.Items.ToArray()));
            parts.AddRange(After);
            return string.Join(" ", parts.ToArray());
        }
    }

    /// <summary>One heading of the list and the items under it.</summary>
    public sealed class SaveWarningSection
    {
        public readonly string Heading;
        public readonly List<string> Items = new List<string>();

        public SaveWarningSection(string heading, IEnumerable<string> items)
        {
            Heading = heading ?? "";
            if (items != null) Items.AddRange(items);
        }
    }
}
