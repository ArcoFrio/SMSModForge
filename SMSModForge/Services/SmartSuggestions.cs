using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Services;

/// <summary>One option of a dropdown, as suggestions read it: the item
/// itself, the text it shows, its value when that differs, and the note beside
/// it (<see cref="View.OptionNotes"/>).</summary>
public readonly record struct SuggestionOption(object Item, string Text, string? Value, string? Note);

/// <summary>What ranks a dropdown's options: the window's view model, which
/// knows the record being edited.</summary>
public interface ISuggestionSource
{
    /// <summary>The options worth putting on top, each with its score - higher
    /// first - or null when there are none, or suggestions are off.</summary>
    IReadOnlyDictionary<object, double>? RankOptions(IReadOnlyList<SuggestionOption> options);
}

/// <summary>
/// Smart suggestions (the author, 1.8.0): a dropdown opened empty puts on top
/// the options the record being edited makes likely. A dialogue that names a
/// character anywhere in it and runs at one place puts that character's scenes
/// at that place first in a Set Active target list - and the same for every
/// dropdown in every tab, from nothing more specific than words.
/// <para/>
/// HOW. Names in a pack are made of words - <c>MiraNightmarketDateScene01</c>
/// is mira, nightmarket, date, scene - and the record being edited is full of
/// them: its key, every identifier in it, every character it names, the
/// folder it is filed in. An option scores by the words it shares with that
/// record, each word weighed by how rare it is among the options (a word half
/// the list shares tells nothing apart) and by how often the record uses it.
/// An option that is a record of the pack brings that record's words too: its
/// names, its folders, faintly what it refers to.
/// <para/>
/// WHEN. The pack is read into words in the background after it is opened and
/// saved; the record being edited is read again after a field is committed
/// (<see cref="Follow"/>, which the window calls once things are idle). The
/// one thing done when a dropdown opens is the scoring, which is a few
/// dictionary lookups per option - nothing is serialized or parsed there.
/// <para/>
/// Generic on purpose: no knowledge of any pack, kind or field name beyond
/// the manifest's own conventions (a record's <c>key</c>/<c>name</c>, folder
/// lists).
/// </summary>
public sealed class SmartSuggestions
{
    /// <summary>At most this many options go on top; more than that tied at
    /// the edge and the edge moves up, so a word every scene shares does not
    /// lift every scene.</summary>
    internal const int MostOnTop = 15;

    /// <summary>The least score that counts as a suggestion: a word the
    /// record names once that one option in thirty has, or twice that three
    /// have. Less, and a word as plain as "room" in two place names put seven
    /// NPCs on top of a list nothing in the record had to do with.</summary>
    internal const double LeastScore = 3;

    /// <summary>Below this share of the best score an option is not suggested -
    /// it only shares the record's background words.</summary>
    internal const double ShareOfBest = 0.25;

    private volatile Index _index = Index.Empty;
    private volatile Context? _context;
    private string? _followed;

    private readonly object _gate = new();
    private Task _work = Task.CompletedTask;

    /// <summary>Done when all the reading queued so far is done. For tests.</summary>
    internal Task Idle { get { lock (_gate) return _work; } }

    /// <summary>The words the record being edited weighs each word with, or
    /// null with no record. For tests.</summary>
    internal IReadOnlyDictionary<string, double>? ContextWords => _context?.Weight;

    /// <summary>Reads the whole pack into words, in the background: after it
    /// is opened, and after a save.</summary>
    public void Rebuild(string packJson)
    {
        string? followed = _followed;
        Queue(() =>
        {
            var index = Index.Parse(packJson);
            var record = followed == null ? null : Record.From(JToken.Parse(followed));
            if (record != null) index = index.With(record);
            _index = index;
            _context = record == null ? null : Context.Of(record, index);
        });
    }

    /// <summary>
    /// The record being edited now - a dialogue, a scene, a rule - read again.
    /// Serialized here, on the caller's thread, because the model is the
    /// window's; turned into words in the background. Nothing happens when it
    /// reads the same as last time, so calling this after every commit costs
    /// one serialization of one record.
    /// </summary>
    public void Follow(object? model)
    {
        string? json = model == null ? null : JsonConvert.SerializeObject(model, Model.PackRepository.ManifestSettings);
        if (json == _followed) return;
        _followed = json;
        Queue(() =>
        {
            var record = json == null ? null : Record.From(JToken.Parse(json));
            if (record == null) { _context = null; return; }
            var index = record.Key.Length > 0 ? _index.With(record) : _index;
            _index = index;
            _context = Context.Of(record, index);
        });
    }

    /// <summary>The options worth putting on top, with their scores, or null.
    /// Cheap: called as a dropdown opens.</summary>
    public IReadOnlyDictionary<object, double>? Rank(IReadOnlyList<SuggestionOption> options)
    {
        var context = _context;
        return context == null ? null : Rank(options, context, _index);
    }

    private void Queue(Action step)
    {
        lock (_gate)
            _work = _work.ContinueWith(_ =>
            {
                // A pack the reader cannot make sense of means no suggestions,
                // never an editor that falls over.
                try { step(); } catch (Exception) { }
            }, TaskScheduler.Default);
    }

    internal static IReadOnlyDictionary<object, double>? Rank(IReadOnlyList<SuggestionOption> options, Context context, Index index)
    {
        int n = options.Count;
        if (n < 3) return null;   // nothing worth reordering

        var words = new Dictionary<string, double>?[n];
        var shared = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var option = options[i];
            var record = index.Find(option.Value) ?? index.Find(option.Text);
            // The record being edited is not its own suggestion: a dialogue
            // shares every word with itself.
            if (record != null && string.Equals(record.Key, context.Key, StringComparison.OrdinalIgnoreCase)) continue;

            var mine = new Dictionary<string, double>(StringComparer.Ordinal);
            void Put(string word, double weight)
            {
                if (!mine.TryGetValue(word, out double had) || had < weight) mine[word] = weight;
            }
            foreach (var w in SuggestionWords.Of(option.Text)) Put(w, 1);
            if (!string.IsNullOrEmpty(option.Value)) foreach (var w in SuggestionWords.Of(option.Value!)) Put(w, 1);
            if (!string.IsNullOrEmpty(option.Note)) foreach (var w in SuggestionWords.Of(option.Note!)) Put(w, 0.7);
            if (record != null)
                foreach (var (word, weight) in index.OptionWordsOf(record)) Put(word, weight);

            words[i] = mine;
            foreach (var word in mine.Keys) shared[word] = shared.TryGetValue(word, out int c) ? c + 1 : 1;
        }

        var scored = new List<(int At, double Telling, double Score)>();
        for (int i = 0; i < n; i++)
        {
            if (words[i] is not { } mine) continue;
            double telling = 0, common = 0;
            foreach (var (word, weight) in mine)
            {
                if (!context.Weight.TryGetValue(word, out double used)) continue;
                int having = shared[word];
                double part = weight * used * Math.Log(1.0 + (double)n / having);
                // A word half the list has cannot make an option a suggestion -
                // every scene says "scene" - but it still orders the ones that
                // are: of a character's scenes, the ones at the dialogue's
                // place come first, though most of the pack's may be there.
                if (having * 2 > n) common += part;
                else telling += part;
            }
            if (telling >= LeastScore) scored.Add((i, telling, telling + common));
        }
        if (scored.Count == 0) return null;

        // Whether it is close enough to the best is a question for the words
        // that tell options apart; the order among those that are takes the
        // common ones too.
        double best = scored.Max(s => s.Telling);
        var kept = scored.Where(s => s.Telling >= best * ShareOfBest)
                         .Select(s => (s.At, s.Score))
                         .OrderByDescending(s => s.Score).ThenBy(s => s.At).ToList();
        if (kept.Count > MostOnTop)
        {
            double edge = kept[MostOnTop].Score;
            kept = kept.Where(s => s.Score > edge).ToList();
            if (kept.Count == 0) return null;   // all tied: nothing stands out
        }

        var result = new Dictionary<object, double>();
        foreach (var (at, score) in kept) result.TryAdd(options[at].Item, score);
        return result;
    }

    // ── What a pack is made of, in words ─────────────────────────────────

    /// <summary>One record's words.</summary>
    internal sealed class Record
    {
        private static readonly string[] KeyFields = { "key", "name", "id", "target" };
        private static readonly string[] NameFields = { "key", "name", "id", "displayName", "internalName", "title", "label" };

        public string Key = "";
        /// <summary>The name it shows, for an option that shows that rather
        /// than its key.</summary>
        public string? Display;
        /// <summary>The words of its key and names.</summary>
        public readonly Dictionary<string, double> Names = new(StringComparer.Ordinal);
        /// <summary>The words of every identifier in it - a value with no
        /// space - counted.</summary>
        public readonly Dictionary<string, double> Words = new(StringComparer.Ordinal);
        /// <summary>The words of its sentences, counted. Only a word that is
        /// part of some record's name counts from these: "look at the sea"
        /// says nothing, "Mira, look!" names somebody.</summary>
        public readonly Dictionary<string, double> Prose = new(StringComparer.Ordinal);
        /// <summary>The identifiers themselves, counted - to follow the ones
        /// that are another record's key.</summary>
        public readonly Dictionary<string, int> Values = new(StringComparer.OrdinalIgnoreCase);

        public static Record? From(JToken token)
        {
            if (token is not JObject o) return null;
            var record = new Record
            {
                Key = KeyFields.Select(f => o[f]).OfType<JValue>().Select(v => v.Value as string)
                               .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "",
                Display = (o["displayName"] as JValue)?.Value as string,
            };
            foreach (var field in NameFields)
                if (o[field] is JValue { Value: string name })
                    SuggestionWords.Each(name, w => record.Names[w] = 1);
            record.Read(o);
            return record;
        }

        private void Read(JToken token)
        {
            switch (token)
            {
                case JObject o:
                    foreach (var p in o.Properties()) Read(p.Value);
                    break;
                case JArray a:
                    foreach (var v in a) Read(v);
                    break;
                case JValue { Value: string s } when s.Length > 0:
                    if (s.Any(char.IsWhiteSpace))
                        SuggestionWords.Each(s, w => Prose[w] = Prose.TryGetValue(w, out double c) ? c + 1 : 1);
                    else
                    {
                        Values[s] = Values.TryGetValue(s, out int c) ? c + 1 : 1;
                        SuggestionWords.Each(s, w => Words[w] = Words.TryGetValue(w, out double k) ? k + 1 : 1);
                    }
                    break;
            }
        }
    }

    /// <summary>The whole pack's records by name, and the words of the
    /// folders they are filed in. Never changed once made: a change makes a
    /// new one, so the window can read it while the background builds.</summary>
    internal sealed class Index
    {
        public static readonly Index Empty = new(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
                                                 new(StringComparer.Ordinal));

        private readonly Dictionary<string, Record> _byName;
        private readonly Dictionary<string, Dictionary<string, double>> _folders;
        private readonly Dictionary<Record, List<(string, double)>> _optionWords;

        /// <summary>Every word some record's name is made of.</summary>
        public readonly HashSet<string> NameWords;

        private Index(Dictionary<string, Record> byName, Dictionary<string, Dictionary<string, double>> folders, HashSet<string> nameWords,
                      Dictionary<Record, List<(string, double)>>? optionWords = null)
        {
            _byName = byName;
            _folders = folders;
            NameWords = nameWords;
            _optionWords = optionWords ?? new();
        }

        public static Index Parse(string packJson)
        {
            var byName = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            var nameWords = new HashSet<string>(StringComparer.Ordinal);
            if (JToken.Parse(packJson) is not JObject root) return Empty;

            foreach (var list in root.Properties())
            {
                if (list.Value is not JArray items) continue;
                if (list.Name.EndsWith("Folders", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var folder in items) File(folder, Array.Empty<string>(), folders);
                    continue;
                }
                foreach (var item in items)
                {
                    var record = Record.From(item);
                    if (record == null || record.Key.Length == 0) continue;
                    byName[record.Key] = record;
                    nameWords.UnionWith(record.Names.Keys);
                }
            }
            // Display names after every key, so a key always wins a clash.
            foreach (var record in byName.Values.Distinct().ToList())
                if (!string.IsNullOrWhiteSpace(record.Display)) byName.TryAdd(record.Display!, record);
            return new Index(byName, folders, nameWords);
        }

        /// <summary>A folder, its subfolders, and the words each member is
        /// filed under - a folder's name and every folder it is in.</summary>
        private static void File(JToken folder, string[] above, Dictionary<string, Dictionary<string, double>> into)
        {
            if (folder is not JObject o) return;
            var words = new List<string>(above);
            if (o["name"] is JValue { Value: string name }) SuggestionWords.Each(name, words.Add);
            var under = words.ToArray();
            foreach (var p in o.Properties())
            {
                if (p.Name == "name" || p.Value is not JArray members) continue;
                foreach (var m in members)
                {
                    if (m is JValue { Value: string member })
                    {
                        if (!into.TryGetValue(member, out var have)) into[member] = have = new(StringComparer.Ordinal);
                        foreach (var w in under) have[w] = 1;
                    }
                    else File(m, under, into);
                }
            }
        }

        /// <summary>The same, with one record read again.</summary>
        public Index With(Record record)
        {
            var byName = new Dictionary<string, Record>(_byName, StringComparer.OrdinalIgnoreCase) { [record.Key] = record };
            if (!string.IsNullOrWhiteSpace(record.Display) &&
                (!byName.TryGetValue(record.Display!, out var had) || string.Equals(had.Key, record.Key, StringComparison.OrdinalIgnoreCase)))
                byName[record.Display!] = record;
            var nameWords = new HashSet<string>(NameWords, StringComparer.Ordinal);
            nameWords.UnionWith(record.Names.Keys);
            // What the other records bring as options is kept: a commit
            // changes one record, and the first list opened after it should
            // not pay for every other one again.
            Dictionary<Record, List<(string, double)>> optionWords;
            lock (_optionWords) optionWords = new(_optionWords);
            if (_byName.TryGetValue(record.Key, out var replaced)) optionWords.Remove(replaced);
            return new Index(byName, _folders, nameWords, optionWords);
        }

        /// <summary>The record an option names: by its whole text, or by what
        /// follows a prefix (<c>place:NightMarket</c>).</summary>
        public Record? Find(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            if (_byName.TryGetValue(name, out var record)) return record;
            int colon = name.LastIndexOf(':');
            return colon >= 0 && colon < name.Length - 1 && _byName.TryGetValue(name[(colon + 1)..], out record) ? record : null;
        }

        public IReadOnlyDictionary<string, double>? FoldersOf(string key)
            => _folders.TryGetValue(key, out var words) ? words : null;

        /// <summary>
        /// What an option that is this record brings: its names in full, its
        /// folders' names nearly so, and faintly the names of the records it
        /// refers to - a bust's character, a scene's place. Not every word in
        /// it: a rule full of actions shares some word with anything. Worked
        /// out once per record, not per opening.
        /// </summary>
        public List<(string Word, double Weight)> OptionWordsOf(Record record)
        {
            lock (_optionWords)
            {
                if (_optionWords.TryGetValue(record, out var known)) return known;
                var words = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var value in record.Values.Keys)
                    if (Find(value) is { } other && other != record)
                        foreach (var w in other.Names.Keys) words[w] = 0.3;
                if (FoldersOf(record.Key) is { } filed) foreach (var w in filed.Keys) words[w] = 0.7;
                foreach (var w in record.Names.Keys) words[w] = 1;
                return _optionWords[record] = words.Select(kv => (kv.Key, kv.Value)).ToList();
            }
        }
    }

    /// <summary>The record being edited, as the weight it gives each word.</summary>
    internal sealed class Context
    {
        public string Key = "";
        public Dictionary<string, double> Weight = new(StringComparer.Ordinal);

        public static Context Of(Record record, Index index)
        {
            var used = new Dictionary<string, double>(StringComparer.Ordinal);
            void Add(string word, double by) => used[word] = used.TryGetValue(word, out double c) ? c + by : by;

            foreach (var (word, count) in record.Words) Add(word, count);
            foreach (var (word, count) in record.Prose)
                if (index.NameWords.Contains(word)) Add(word, 0.5 * count);
            // A record it names brings that record's names, as fully as its
            // own words: an actor key "char07" stands for "Mira", whose scenes
            // say Mira.
            foreach (var (value, count) in record.Values)
            {
                var other = index.Find(value);
                if (other == null || string.Equals(other.Key, record.Key, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var word in other.Names.Keys) Add(word, count);
            }
            // The folder it is filed in: a dialogue under "Mira" is Mira's.
            if (index.FoldersOf(record.Key) is { } filed)
                foreach (var word in filed.Keys) Add(word, 1);

            var context = new Context { Key = record.Key };
            foreach (var (word, count) in used) context.Weight[word] = Math.Log2(1 + count);
            return context;
        }
    }
}

/// <summary>
/// The words a name is made of, lower case: split at anything that is not a
/// letter or digit, between a lower and an upper case letter, between letters
/// and digits, and before the last capital of a run (<c>HHBedroom</c> is hh,
/// bedroom). Neighbouring words are joined too - <c>NightMarket</c> also gives
/// nightmarket, so it meets <c>Nightmarket</c> - and a name of three words or
/// more gives itself whole. Numbers and single letters are not words here.
/// </summary>
internal static class SuggestionWords
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> Known = new(StringComparer.Ordinal);

    /// <summary>The same, kept: an option's text is read every time its list
    /// opens.</summary>
    public static string[] Of(string value)
    {
        if (Known.TryGetValue(value, out var words)) return words;
        if (Known.Count > 200_000) Known.Clear();
        var list = new List<string>();
        Each(value, list.Add);
        return Known[value] = list.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static void Each(string value, Action<string> add)
    {
        var parts = new List<string>(8);
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && !char.IsLetterOrDigit(value[i])) i++;
            int start = i;
            while (i < value.Length && char.IsLetterOrDigit(value[i])) i++;
            if (i > start) Segment(value, start, i, parts, add);
        }
    }

    private static void Segment(string s, int start, int end, List<string> parts, Action<string> add)
    {
        parts.Clear();
        int from = start;
        for (int k = start + 1; k < end; k++)
        {
            char a = s[k - 1], b = s[k];
            bool cut = (char.IsLower(a) && char.IsUpper(b))
                       || char.IsDigit(a) != char.IsDigit(b)
                       || (char.IsUpper(a) && char.IsUpper(b) && k + 1 < end && char.IsLower(s[k + 1]));
            if (!cut) continue;
            parts.Add(s[from..k].ToLowerInvariant());
            from = k;
        }
        parts.Add(s[from..end].ToLowerInvariant());

        string? previous = null;
        foreach (var part in parts)
        {
            bool counts = Counts(part);
            if (counts) add(part);
            if (counts && previous != null) add(previous + part);
            previous = counts ? part : null;
        }
        if (parts.Count > 2) add(s[start..end].ToLowerInvariant());
    }

    /// <summary>"copy" is what the editor itself puts on a duplicate's key
    /// (<c>Scene01_copy2</c>): it says how the record was made, not what it
    /// is, and would make every duplicate in the pack look related.</summary>
    private static bool Counts(string part)
        => part.Length >= 2 && !part.All(char.IsDigit) && part != "copy";
}
