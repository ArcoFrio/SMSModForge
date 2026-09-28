using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Editing a pack in one of its translations: every text a player reads shows
/// that language's words, and editing one edits the translation.
/// <para/>
/// <b>What changes and what does not.</b> Only the fields
/// <see cref="PackTexts"/> lists as something a player reads - lines, names,
/// quest and task wording, button labels, the text on a screen. Everything the
/// game FINDS things by - keys, ids, targets, the names of objects - is shared
/// by every language and is never touched here, which is the same rule the
/// runtime follows when it lays a translation over a pack. The tests hold both
/// to it by comparing a whole manifest before and after, leaf by leaf.
/// <para/>
/// <b>How it works.</b> Entering a language puts that language's words into the
/// live model, remembering what each field said before - by the OBJECT, not by
/// its key. Leaving puts the pack's own words back and hands over what the
/// fields said in the language as the new translation. Keying the memory on the
/// object rather than on its key is what lets an author rename a dialogue while
/// editing its Spanish: the lines are the same objects, so their own words go
/// back where they belong and the Spanish moves to the new keys.
/// <para/>
/// <b>Something typed while in a language that had no words of the pack's
/// own</b> - a new line, or a field that was empty - belongs to that language
/// only. The pack's own words for it stay empty, which the pack's check reports
/// as an error, and in game it is read in the player's language, then in the
/// pack's own words, then in any translation that has it, so it is never blank.
/// The author decided this (2026-09-24): writing the Spanish into the pack
/// would put Spanish in front of every English player without a word.
/// <para/>
/// Structure is still shared: the new line exists in every language, and only
/// its words are this language's.
/// </summary>
public sealed class LanguageSession
{
    /// <summary>The language being edited.</summary>
    public string Code { get; }

    /// <summary>One text on the live model: where it is, and how to read and
    /// write it.</summary>
    public sealed class Slot
    {
        public string Key = "";
        public PackTexts.Kind Kind;

        /// <summary>The model object holding the text. What the memory of the
        /// pack's own words is keyed on.</summary>
        public object Holder = null!;

        /// <summary>Which of its texts, when an object holds more than one.</summary>
        public string Field = "";

        /// <summary>What it belongs to, as <see cref="PackTexts"/> names it:
        /// the character's key, the quest's, the screen's id.</summary>
        public string Owner = "";

        /// <summary>Which one of the owner's, when that takes naming - the
        /// task's key in a quest.</summary>
        public string Detail = "";

        public Func<string> Get = null!;
        public Action<string> Set = null!;
    }

    /// <summary>What a remembered field said in the pack's own words, and how
    /// to read and write it without finding it again.</summary>
    private sealed class Remembered
    {
        public string Own = "";
        public Func<string> Get = null!;
        public Action<string> Set = null!;

        /// <summary>What it showed when the session last counted as saved -
        /// see <see cref="Unsaved"/>.</summary>
        public string AtMark = "";
    }

    /// <summary>The pack's own words, by holder and field.</summary>
    private readonly Dictionary<(object, string), Remembered> _own =
        new(new IdentityComparer());

    /// <summary>
    /// Every text field the model had when the session began, whether or not
    /// it is one of the pack's texts. What tells a field added during the
    /// session - which has no words of the pack's own - from one that was
    /// always there: a line of the game's that the pack has not rewritten is
    /// not the pack's text, but it is not new either, and its words are the
    /// game's, not empty.
    /// </summary>
    private readonly HashSet<(object, string)> _seen = new(new IdentityComparer());

    /// <summary>Texts the pack has that could not be matched to the model, so
    /// could not be shown in the language. Zero on every pack the tests build;
    /// said rather than hidden if that ever stops being true.</summary>
    public int Unmatched { get; private set; }

    private LanguageSession(string code) => Code = code;

    // ── Entering and leaving ────────────────────────────────────────────

    /// <summary>
    /// Show <paramref name="pack"/> in <paramref name="code"/>: each text the
    /// translation has words for takes them, and the rest keep the pack's own.
    /// <para/>
    /// The same rule as the runtime's: a translation is used only while the
    /// words it was translated from are still what the pack says. One made from
    /// words the pack has since changed is shown as the pack's words, so the
    /// author sees what the line now says and can translate THAT.
    /// </summary>
    public static LanguageSession Enter(ModPack pack, string code, TextFile? translation)
    {
        var session = new LanguageSession(code);
        var slots = Slots(pack, out int unmatched);
        session.Unmatched = unmatched;
        foreach (var c in Candidates(pack)) session._seen.Add((c.Holder, c.Field));

        foreach (var slot in slots)
        {
            string own = slot.Get();
            var remembered = new Remembered { Own = own, Get = slot.Get, Set = slot.Set };
            session._own[(slot.Holder, slot.Field)] = remembered;

            string? theirs = translation?.Translated(slot.Key);
            var entry = translation?.Find(slot.Key);
            bool current = entry?.English == null || PackTexts.Normal(entry.English) == PackTexts.Normal(own);

            // Not a translation of words the pack no longer says: the author
            // sees what the line says NOW, and can translate that.
            if (theirs != null && current) slot.Set(theirs);
            remembered.AtMark = slot.Get();
        }
        return session;
    }

    /// <summary>
    /// Put the pack's own words back, and return what the language now says as
    /// a translation file.
    /// <para/>
    /// Every text is written, translated or not: a pack's translation file
    /// holds the pack's own words on a line nobody has translated yet, so a
    /// line whose words are still the pack's reads as untranslated - which is
    /// the truth. Nothing here decides what counts as translated; the file
    /// does, the same way it does for a file written by hand.
    /// <para/>
    /// A text typed while in the language that had no words of the pack's own
    /// goes back to having none, and its words are this language's alone.
    /// </summary>
    public TextFile Leave(ModPack pack, TextFile? translation)
    {
        var file = Translation(pack, translation);
        Restore();
        return file;
    }

    /// <summary>
    /// What the language says now, as a translation file, without leaving it.
    /// For saving while the language is still up: the file is written and the
    /// author goes on editing in the same language.
    /// <para/>
    /// Keyed by the pack in its OWN words, which is what the game keys it by.
    /// A key made from what the language shows could differ - two buttons to
    /// one level are numbered by order, and an empty one counts after the rest -
    /// and a translation filed under a key the game never makes is never seen.
    /// </summary>
    public TextFile Translation(ModPack pack, TextFile? translation)
    {
        var file = translation ?? new TextFile();
        foreach (var slot in SlotsByOwnWords(pack))
        {
            string shown = slot.Get();
            string own = _own.TryGetValue((slot.Holder, slot.Field), out var was) ? was.Own : shown;

            // No words in the pack and none in the language: not a text. One
            // this language had words for and has just lost is taken out,
            // rather than left behind as an empty translation of nothing.
            if (PackTexts.IsEmpty(own) && PackTexts.IsEmpty(shown))
            {
                var stale = file.Find(slot.Key);
                if (stale != null && PackTexts.IsEmpty(stale.English)) file.Remove(slot.Key);
                continue;
            }
            Put(file, slot.Key, shown, own);
        }
        return file;
    }

    /// <summary>
    /// Put the pack's own words back and forget them. Nothing is found again:
    /// every field that had words of its own is already known, and one given
    /// words during the session that had none goes back to none.
    /// </summary>
    public void Restore()
    {
        foreach (var one in _own.Values) one.Set(one.Own);
        _own.Clear();
        _seen.Clear();
    }

    /// <summary>
    /// The pack in its own words for as long as the returned scope is open,
    /// and back in the language after.
    /// <para/>
    /// For everything that must see the pack as it is, not as it is being
    /// shown: saving it, comparing it with what was saved, taking an undo
    /// step. Each of those reads the live model, and while a language is up the
    /// live model holds that language's words - so without this, saving while
    /// looking at the Spanish would write the Spanish into the pack itself.
    /// <para/>
    /// Cheap on purpose, because the unsaved-changes check runs on every edit:
    /// one walk over the model for fields added since the last call, and a swap
    /// of strings on objects already known. A field added during the session
    /// has no words of the pack's own, so it is empty in here.
    /// </summary>
    public IDisposable OwnWords(ModPack pack)
    {
        Adopt(pack);
        var shown = new List<(Remembered one, string value)>(_own.Count);
        foreach (var one in _own.Values)
        {
            shown.Add((one, one.Get()));
            one.Set(one.Own);
        }
        return new Scope(() => { foreach (var (one, value) in shown) one.Set(value); });
    }

    /// <summary>
    /// Take on every text field added to the model since the session began, as
    /// one whose words of the pack's own are none. A field that was there all
    /// along and simply is not one of the pack's texts - a line of the game's -
    /// is left alone: it is not new, and its words are not empty.
    /// </summary>
    private void Adopt(ModPack pack)
    {
        if (pack == null) return;
        foreach (var c in Candidates(pack))
        {
            var id = (c.Holder, c.Field);
            if (_own.ContainsKey(id) || !_seen.Add(id)) continue;
            _own[id] = new Remembered { Own = "", Get = c.Get, Set = c.Set, AtMark = "" };
        }
    }

    /// <summary><see cref="Slots"/>, found while the model holds the pack's own
    /// words, and read afterwards in the language.</summary>
    private List<Slot> SlotsByOwnWords(ModPack pack)
    {
        using (OwnWords(pack)) return Slots(pack, out _);
    }
    /// <summary>Whether any text has been changed in the language since it was
    /// entered, or since <see cref="MarkSaved"/>. The pack's own words cannot
    /// show this - they have not changed - so without it, a translation edited
    /// and not saved would be closed without a word.</summary>
    public bool Unsaved
    {
        get
        {
            if (_unsaved) return true;
            foreach (var one in _own.Values)
                if (!string.Equals(one.Get(), one.AtMark, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    private bool _unsaved;

    /// <summary>What is showing now is what has been saved.</summary>
    public void MarkSaved()
    {
        foreach (var one in _own.Values) one.AtMark = one.Get();
        _unsaved = false;
    }

    /// <summary>
    /// Count as unsaved whatever is showing. For a session rebuilt by an undo:
    /// it knows what the language said at that step, not what was last written
    /// to the file - so it cannot tell "unchanged" from "changed and then put
    /// back", and must not claim the first.
    /// </summary>
    public void MarkUnsaved() => _unsaved = true;

    // ── Undo ────────────────────────────────────────────────────────────

    /// <summary>What an undo step taken in a language starts with, so it can be
    /// told from an ordinary one. Not JSON, so it cannot be mistaken for a pack,
    /// and a pack cannot be mistaken for it.</summary>
    private const string SnapshotMark = "\u0001language ";

    /// <summary>
    /// An undo step taken while a language is up: the pack in its own words,
    /// and what the language showed, by key.
    /// <para/>
    /// Both, because each on its own is wrong. The pack as it stood would carry
    /// the language's words into the pack itself when restored; the pack in its
    /// own words alone would throw away every translation made since the last
    /// step. Putting the two back together is what lets undo take back a
    /// correction to the Spanish and a change to the pack alike.
    /// </summary>
    public static string Wrap(string code, IReadOnlyDictionary<string, string> shown, string ownPack)
    {
        var texts = new JObject();
        foreach (var pair in shown) texts[pair.Key] = pair.Value;
        return SnapshotMark + code + "\n"
             + texts.ToString(Newtonsoft.Json.Formatting.None) + "\n"
             + ownPack;
    }

    /// <summary>Read back a step <see cref="Wrap"/> made. False for an ordinary
    /// one, which is a pack and nothing else.</summary>
    public static bool Unwrap(string snapshot, out string code, out TextFile shown, out string ownPack)
    {
        code = ""; shown = new TextFile(); ownPack = snapshot;
        if (snapshot == null || !snapshot.StartsWith(SnapshotMark, StringComparison.Ordinal)) return false;

        int first = snapshot.IndexOf('\n');
        int second = first < 0 ? -1 : snapshot.IndexOf('\n', first + 1);
        if (second < 0) return false;

        code = snapshot.Substring(SnapshotMark.Length, first - SnapshotMark.Length);
        var texts = JObject.Parse(snapshot.Substring(first + 1, second - first - 1));
        foreach (var pair in texts)
            shown.Add(new TextFile.Entry { Key = pair.Key, Text = (string?)pair.Value ?? "" });
        ownPack = snapshot.Substring(second + 1);
        return true;
    }

    /// <summary>
    /// The texts as they are showing now, by the keys the pack has now - what
    /// an undo step needs to remember about the language, alongside the pack in
    /// its own words.
    /// </summary>
    public Dictionary<string, string> Shown(ModPack pack)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in SlotsByOwnWords(pack))
        {
            string shown = slot.Get();
            if (!PackTexts.IsEmpty(shown)) texts[slot.Key] = shown;
        }
        return texts;
    }

    private sealed class Scope : IDisposable
    {
        private Action? _undo;
        public Scope(Action undo) => _undo = undo;
        public void Dispose() { _undo?.Invoke(); _undo = null; }
    }

    /// <summary>One line of the file, set to <paramref name="text"/>, as a
    /// translation of <paramref name="own"/>.</summary>
    private static void Put(TextFile file, string key, string text, string own)
    {
        var entry = file.Find(key);
        if (entry == null)
        {
            file.Add(new TextFile.Entry { Key = key, Text = text, English = own });
            return;
        }
        entry.Text = text;
        entry.Blank = false;
        entry.English = own;
        entry.ChangedFrom = null;
    }

    // ── Finding every text on the live model ────────────────────────────

    /// <summary>
    /// Every text of the pack a player reads, as a field on the live model.
    /// <para/>
    /// The list comes from <see cref="PackTexts"/> - run over the pack as it
    /// is saved, which is the set of texts the pack actually owns (a line of
    /// the game's that the pack did not rewrite is not in it). Each is then
    /// found on the model by the ids its key is made of. Found, and then
    /// CHECKED: a field is only used if what it holds is exactly what the
    /// saved text says, so a mistake in the finding cannot put one field's
    /// words into another. Anything that fails the check is counted in
    /// <paramref name="unmatched"/> and left alone.
    /// </summary>
    public static List<Slot> Slots(ModPack pack, out int unmatched)
    {
        unmatched = 0;
        var slots = new List<Slot>();
        if (pack == null) return slots;

        var saved = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var candidates = Candidates(pack)
            .GroupBy(c => (c.Kind, c.Owner, c.Detail))
            .ToDictionary(g => g.Key, g => new Queue<Candidate>(g));

        // PackTexts numbers repeats - two buttons to one level - in the order
        // it meets them, and Candidates walks in the same order, so the nth
        // text of a (kind, owner, detail) is the nth candidate of it.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in PackTexts.Of(saved, withEmpty: true))
        {
            taken.Add(site.Key);
            if (!candidates.TryGetValue((site.Kind, site.Owner, site.Detail), out var queue)
                || queue.Count == 0)
            {
                unmatched++;
                continue;
            }

            var c = queue.Dequeue();
            if (PackTexts.Normal(c.Get()) != PackTexts.Normal(site.Text)) { unmatched++; continue; }

            slots.Add(new Slot
            {
                Key = site.Key, Kind = site.Kind, Holder = c.Holder, Field = c.Field,
                Owner = site.Owner ?? "", Detail = site.Detail ?? "",
                Get = c.Get, Set = c.Set,
            });
        }

        // And the game's own lines in the conversations the pack extends. Their
        // own words are the game's: remembered and put back like any other
        // text, which is what keeps a Spanish line typed over one of them in
        // the translation and out of the pack - where it would have become the
        // pack's rewrite of the game's line, for every player.
        foreach (var line in GameLines.Of(pack, taken))
        {
            var n = line.Node;
            slots.Add(new Slot
            {
                Key = line.Key, Kind = PackTexts.Kind.Line, Holder = n, Field = "text",
                Get = () => n.Text, Set = v => n.Text = v,
            });
        }
        return slots;
    }

    private sealed class Candidate
    {
        public PackTexts.Kind Kind;
        public string Owner = "";
        public string Detail = "";
        public object Holder = null!;
        public string Field = "";
        public Func<string> Get = null!;
        public Action<string> Set = null!;
    }

    /// <summary>
    /// The model's text fields, empty ones included, in the order
    /// <see cref="PackTexts.Of"/> walks the saved form and with the same
    /// filters - a game quest's title is the game's, a game screen's text is
    /// the pack's only where it says it overrides it. Drift between the two walks cannot put
    /// words in the wrong place, because <see cref="Slots"/> checks each
    /// pairing against the saved text; it would show up as texts left in the
    /// pack's own words, which the tests count.
    /// </summary>
    private static IEnumerable<Candidate> Candidates(ModPack pack)
    {
        Candidate Make(PackTexts.Kind kind, string owner, string detail, object holder, string field,
                       Func<string> get, Action<string> set)
            => new() { Kind = kind, Owner = owner ?? "", Detail = detail ?? "", Holder = holder,
                       Field = field, Get = get, Set = set };

        foreach (var c in pack.Characters)
            if (!string.IsNullOrEmpty(c.Key))
                yield return Make(PackTexts.Kind.CharacterName, c.Key, "", c, "displayName",
                                  () => c.DisplayName, v => c.DisplayName = v);
        foreach (var a in pack.Actors)
            if (!string.IsNullOrEmpty(a.Key))
                yield return Make(PackTexts.Kind.CharacterName, a.Key, "", a, "displayName",
                                  () => a.DisplayName, v => a.DisplayName = v);

        foreach (var d in pack.Dialogues)
        {
            if (string.IsNullOrEmpty(d.Key)) continue;
            foreach (var n in d.Nodes)
                yield return Make(PackTexts.Kind.Line, d.Key,
                                  n.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                  n, "text", () => n.Text, v => n.Text = v);
        }

        foreach (var q in pack.Quests)
        {
            if (string.IsNullOrEmpty(q.Key)) continue;
            bool games = !string.IsNullOrEmpty(q.Source);
            if (!games)
                yield return Make(PackTexts.Kind.QuestTitle, q.Key, "", q, "title",
                                  () => q.Title, v => q.Title = v);
            yield return Make(PackTexts.Kind.QuestDescription, q.Key, "", q, "description",
                              () => q.Description, v => q.Description = v);
            foreach (var t in Tasks(q.Tasks, q.Key)) yield return t;
            if (games)
            {
                foreach (var h in q.VanillaTasks)
                {
                    string task = (h.Task ?? "").Trim();
                    if (task.Length > 0)
                        yield return Make(PackTexts.Kind.GameTaskQuestDescription, q.Key, task, h,
                                          "questDescription", () => h.QuestDescription,
                                          v => h.QuestDescription = v);
                }
                foreach (var t in Tasks(q.AddedTasks, q.Key)) yield return t;
            }
        }

        foreach (var p in pack.Places)
            if (!string.IsNullOrEmpty(p.Key))
                foreach (var b in Buttons(p.NavigatorButtons, p.Key)) yield return b;
        foreach (var e in pack.VanillaExtensions)
            if (!string.IsNullOrEmpty(e.Source))
                foreach (var b in Buttons(e.NavigatorButtons, e.Source)) yield return b;

        foreach (var b in pack.MapButtons)
            yield return Make(PackTexts.Kind.MapLabel, b.District ?? "", b.Target ?? "", b, "label",
                              () => b.Label, v => b.Label = v);

        foreach (var ui in pack.Uis)
        {
            string id = !string.IsNullOrEmpty(ui.Id) ? ui.Id : ui.Name;
            if (string.IsNullOrEmpty(id)) continue;
            bool games = !string.IsNullOrEmpty(ui.Source);
            foreach (var t in UiTexts(ui.Nodes, id, "", games)) yield return t;
        }

        IEnumerable<Candidate> Tasks(IEnumerable<QuestTaskDef> tasks, string quest)
        {
            foreach (var t in tasks)
            {
                string key = (t.Key ?? "").Trim();
                if (key.Length == 0) continue;
                var task = t;
                yield return Make(PackTexts.Kind.TaskName, quest, key, task, "name",
                                  () => task.Name, v => task.Name = v);
                yield return Make(PackTexts.Kind.TaskQuestDescription, quest, key, task, "questDescription",
                                  () => task.QuestDescription, v => task.QuestDescription = v);
                foreach (var s in Tasks(task.Subtasks, quest)) yield return s;
            }
        }

        IEnumerable<Candidate> Buttons(IEnumerable<NavigatorButtonDef> buttons, string owner)
        {
            foreach (var b in buttons)
                yield return Make(PackTexts.Kind.NavigatorLabel, owner, b.Target ?? "", b, "label",
                                  () => b.Label, v => b.Label = v);
        }

        IEnumerable<Candidate> UiTexts(IEnumerable<UiNodeDef> nodes, string ui, string path, bool games)
        {
            foreach (var n in nodes)
            {
                string name = n.Name ?? "";
                string here = path.Length == 0 ? name : path + "/" + name;
                var text = n.Text;
                if (text != null && (!games || n.OverrideText))
                    yield return Make(PackTexts.Kind.UiText, ui, here, text, "value",
                                      () => text.Value, v => text.Value = v);
                foreach (var c in UiTexts(n.Children, ui, here, games)) yield return c;
            }
        }
    }

    /// <summary>Compares holders by reference: two lines with the same words
    /// are two lines, and the memory of what each said must not merge them.</summary>
    private sealed class IdentityComparer : IEqualityComparer<(object, string)>
    {
        public bool Equals((object, string) a, (object, string) b)
            => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;

        public int GetHashCode((object, string) x)
            => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x.Item1), x.Item2);
    }
}
