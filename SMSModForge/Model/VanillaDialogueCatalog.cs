using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// The vanilla game's dialogues, as the conversations a pack can extend.
/// <para/>
/// Built by <c>Tools/Dialogue/BuildDialogueCatalog.py</c> from the runtime
/// extraction, and shipped as <c>VanillaDialogues/</c> next to the exe: one
/// <c>index.json</c> holding what a picker needs, and one file per conversation
/// under <c>Dialogues/</c>. The split is what keeps opening one dialogue from
/// costing all 722 — the whole catalog is ~8 MB, the index is a fifth of a
/// megabyte, and the median conversation is 7 KB.
/// <para/>
/// Absent catalog means every lookup returns null and the tab offers nothing to
/// extend, the same as the level and UI catalogs. An extraction that has not
/// been run is a missing convenience, not a broken install.
/// </summary>
public static class VanillaDialogueCatalog
{
    /// <summary>Token prefix for a vanilla dialogue, matching the
    /// <c>vanilla:</c> and <c>vanillaui:</c> forms already in use.</summary>
    public const string TokenPrefix = "vanilladialogue:";

    // ── The shipped shape ────────────────────────────────────────────

    /// <summary>One row of the index: enough to choose a conversation without
    /// reading it.</summary>
    public sealed class Entry
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";

        /// <summary>Where the conversation itself lives, relative to the
        /// catalog folder.</summary>
        [JsonProperty("file")] public string File { get; set; } = "";

        [JsonProperty("nodes")] public int Nodes { get; set; }
        [JsonProperty("roots")] public int Roots { get; set; }

        /// <summary>Who speaks, in the order they first do.</summary>
        [JsonProperty("actors")] public List<string> Actors { get; set; } = new();

        [JsonProperty("choices")] public int Choices { get; set; }
        [JsonProperty("conditions")] public int Conditions { get; set; }
        [JsonProperty("instructions")] public int Instructions { get; set; }

        /// <summary>How many places play this conversation. Zero means the
        /// game picks it at runtime rather than naming it.</summary>
        [JsonProperty("starts")] public int Starts { get; set; }

        public string Token => TokenPrefix + Id;

        /// <summary>The folder the conversation sits in, which is how the game
        /// groups them — a room, an event, an ending.</summary>
        public string Folder
        {
            get
            {
                int cut = Id.LastIndexOf('/');
                return cut < 0 ? "" : Id.Substring(0, cut);
            }
        }

        /// <summary>A one-line description for a picker: how big it is, who is
        /// in it, and whether it branches or gates.</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string> { Nodes + (Nodes == 1 ? " line" : " lines") };
                if (Actors.Count > 0) parts.Add(string.Join(", ", Actors.Take(3)));
                if (Choices > 0) parts.Add(Choices + " choice" + (Choices == 1 ? "" : "s"));
                if (Conditions > 0) parts.Add(Conditions + " condition"
                                              + (Conditions == 1 ? "" : "s"));
                return string.Join(" — ", parts);
            }
        }
    }

    /// <summary>
    /// One step a node runs or tests: a condition, or an instruction on start
    /// or finish.
    /// <para/>
    /// <see cref="Kind"/> is set only for the constructs the editor has a model
    /// for; everything else still arrives with its type, its fields, and Game
    /// Creator's own one-line <see cref="Title"/>. That is the difference
    /// between a step the editor cannot EDIT and one it cannot SHOW.
    /// </summary>
    public sealed class Step
    {
        /// <summary>What this does, when the editor models it —
        /// <c>setActive</c>, <c>setBool</c>, <c>compareBool</c>. Null when it
        /// does not, and then only <see cref="Title"/> describes it.</summary>
        [JsonProperty("kind")] public string? Kind { get; set; }

        /// <summary>Game Creator's own type name, always present.</summary>
        [JsonProperty("type")] public string Type { get; set; } = "";

        /// <summary>How the game itself describes this step — "Activate Explore
        /// The City", "Scale BathB = (1.50, 1.50, 1.00)". Written by Game
        /// Creator for its own editor, so it reads as a sentence.</summary>
        [JsonProperty("title")] public string? Title { get; set; }

        /// <summary>Every field the step carries, with variables and object
        /// references already resolved. Kept open rather than typed: the
        /// catalog holds 28 instruction shapes and the editor models a third
        /// of them, and the rest must survive being read and written back.</summary>
        [JsonProperty("fields")] public JObject? Fields { get; set; }

        /// <summary>Whether the editor has a model for this step.</summary>
        public bool IsModelled => !string.IsNullOrEmpty(Kind);

        /// <summary>What to show for this step, in one line.</summary>
        public string Describe() => !string.IsNullOrEmpty(Title) ? Title! : Type;
    }

    /// <summary>
    /// One place a conversation is played from, and what has to be true first.
    /// <para/>
    /// A dialogue holds only what it says; whether it plays at all is decided
    /// on some other object - typically the room's Conditions component, whose
    /// branches each gate one conversation. That branch's conditions are, in
    /// the only sense that matters to an author, this dialogue's start
    /// conditions.
    /// </summary>
    public sealed class Start
    {
        /// <summary>The object holding whatever plays this.</summary>
        [JsonProperty("by")] public string By { get; set; } = "";

        /// <summary>What kind of thing it is — <c>Conditions</c>,
        /// <c>Trigger</c>, <c>ButtonInstructions</c>, <c>Actions</c>.</summary>
        [JsonProperty("script")] public string Script { get; set; } = "";

        /// <summary>What sets it off, when it is a Trigger —
        /// <c>EventOnEnable</c> and the like.</summary>
        [JsonProperty("event")] public string? Event { get; set; }

        /// <summary>The instruction that does the playing.</summary>
        [JsonProperty("how")] public string? How { get; set; }

        /// <summary>The game's own words for it — "Play AnnaBeachDefault and
        /// wait".</summary>
        [JsonProperty("title")] public string? Title { get; set; }

        /// <summary>Whether it was named as a <c>Dialogue</c> or as a plain
        /// <c>GameObject</c> — the second means it is being stored for
        /// something else to play later.</summary>
        [JsonProperty("as")] public string? As { get; set; }

        /// <summary>What has to be true. Empty means nothing gates it.</summary>
        [JsonProperty("when")] public List<Step> When { get; set; } = new();

        /// <summary>The author's own labels for the branches this sits
        /// inside.</summary>
        [JsonProperty("branches")] public List<string> Branches { get; set; } = new();

        /// <summary>What is done to stage the conversation first — the busts
        /// switched on, the fade, the wait.</summary>
        [JsonProperty("before")] public List<Step> Before { get; set; } = new();

        /// <summary>And what is done once it has finished — the cooldown set,
        /// the characters dismissed.</summary>
        [JsonProperty("after")] public List<Step> After { get; set; } = new();
    }

    /// <summary>One line of a conversation, keyed in its dialogue by Game
    /// Creator's own node id.</summary>
    public sealed class Node
    {
        /// <summary><c>text</c>, <c>choice</c> or <c>random</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = "";

        /// <summary>-1 for a root.</summary>
        [JsonProperty("parent")] public long Parent { get; set; } = -1;
        [JsonProperty("children")] public List<long> Children { get; set; } = new();

        [JsonProperty("actor")] public string? Actor { get; set; }
        [JsonProperty("portrait")] public string? Portrait { get; set; }
        [JsonProperty("expression")] public int Expression { get; set; }

        /// <summary>
        /// What that number means, when the actor has a face at that position.
        /// <para/>
        /// A node picks an expression by index into its actor's list, and the
        /// actors are assets rather than anything in a scene — so this is the
        /// only place the number becomes a word. Null for the 127 nodes in the
        /// game that index past their actor's list, which are given no face
        /// rather than a wrong one.
        /// </summary>
        [JsonProperty("expressionName")] public string? ExpressionName { get; set; }

        /// <summary>What is said. A plain string for all but seventeen nodes in
        /// the game, which read theirs from somewhere else — hence a token
        /// rather than a string, so those seventeen are not silently emptied.
        /// <see cref="PlainText"/> is the common case.</summary>
        [JsonProperty("text")] public JToken? Text { get; set; }

        /// <summary>Runs of the text with their own styling, when a node has
        /// any.</summary>
        [JsonProperty("values")] public JArray? Values { get; set; }

        [JsonProperty("audio")] public JObject? Audio { get; set; }
        [JsonProperty("animation")] public JToken? Animation { get; set; }

        /// <summary>Absent means <c>UntilInteraction</c>, which is all but 381
        /// nodes in the game.</summary>
        [JsonProperty("duration")] public string? Duration { get; set; }
        [JsonProperty("timeout")] public JToken? Timeout { get; set; }

        /// <summary>The name a jump can aim at. Empty on all but one node per
        /// dialogue that uses one.</summary>
        [JsonProperty("tag")] public string? Tag { get; set; }

        /// <summary>Absent means the conversation simply continues.</summary>
        [JsonProperty("jump")] public JObject? Jump { get; set; }

        /// <summary>What the node type itself carries — a choice's timer, a
        /// random node's repeat rule.</summary>
        [JsonProperty("settings")] public JObject? Settings { get; set; }

        [JsonProperty("conditions")] public List<Step> Conditions { get; set; } = new();
        [JsonProperty("onStart")] public List<Step> OnStart { get; set; } = new();
        [JsonProperty("onFinish")] public List<Step> OnFinish { get; set; } = new();

        /// <summary>
        /// The bust this line is spoken in, worked out from what the
        /// conversation stages — see <see cref="VanillaDialogueStaging"/>.
        /// <para/>
        /// Not in the file, because it is not in the game either: a node says
        /// who speaks and never what they are wearing. Filled in as the
        /// conversation is loaded, and null for the lines whose outfit was
        /// decided somewhere this cannot see.
        /// </summary>
        [JsonIgnore] public string? Outfit { get; set; }

        /// <summary>What is said, when it is simply written on the node.</summary>
        public string? PlainText
            => Text != null && Text.Type == JTokenType.String ? Text.Value<string>() : null;

        public bool IsRoot => Parent == -1;
    }

    /// <summary>One whole conversation.</summary>
    public sealed class Dialogue
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("skin")] public string? Skin { get; set; }

        /// <summary>Every actor the conversation declares, whether or not it
        /// gives them a line.</summary>
        [JsonProperty("roles")] public List<string> Roles { get; set; } = new();

        /// <summary>Where it starts. More than one root means the game picks
        /// the first whose conditions pass.</summary>
        [JsonProperty("roots")] public List<long> Roots { get; set; } = new();

        /// <summary>Keyed by Game Creator's node id, which is stable across
        /// restarts — that is what lets a pack's change bind to a line rather
        /// than to a position in a list.</summary>
        [JsonProperty("nodes")] public Dictionary<string, Node> Nodes { get; set; } = new();

        /// <summary>Everywhere this conversation is played from. Empty for the
        /// 55 the game picks at runtime through a variable rather than naming
        /// — those carry no reference for anything to find.</summary>
        [JsonProperty("starts")] public List<Start> Starts { get; set; } = new();

        /// <summary>Whether the game chooses this conversation at runtime
        /// rather than naming it anywhere.</summary>
        public bool IsChosenAtRuntime => Starts.Count == 0;

        public Node? Node(long id)
            => Nodes.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                 out var found) ? found : null;

        /// <summary>The conversation in reading order: each root, then its
        /// children depth-first. A node reached twice is yielded once, so a
        /// jump cannot make this run forever.</summary>
        public IEnumerable<KeyValuePair<long, Node>> InOrder()
        {
            var seen = new HashSet<long>();
            var stack = new Stack<long>();
            for (int i = Roots.Count - 1; i >= 0; i--) stack.Push(Roots[i]);

            while (stack.Count > 0)
            {
                long id = stack.Pop();
                if (!seen.Add(id)) continue;
                var node = Node(id);
                if (node == null) continue;

                yield return new KeyValuePair<long, Node>(id, node);
                for (int i = node.Children.Count - 1; i >= 0; i--)
                    stack.Push(node.Children[i]);
            }
        }
    }

    private sealed class IndexFile
    {
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("source")] public string Source { get; set; } = "";

        /// <summary>Actors whose spoken name is not their asset name — the one
        /// filed as "DrFrost" says "Doctor Frost". Only the 69 that differ.</summary>
        [JsonProperty("actorNames")]
        public Dictionary<string, string> ActorNames { get; set; } = new();

        [JsonProperty("dialogues")] public List<Entry> Dialogues { get; set; } = new();
    }

    // ── Access ───────────────────────────────────────────────────────

    private static readonly object Gate = new();
    private static bool _loaded;
    private static List<Entry> _entries = new();
    private static Dictionary<string, string> _actorNames =
        new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Entry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dialogue?> Opened =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsAvailable { get { EnsureLoaded(); return _entries.Count > 0; } }

    /// <summary>Every conversation in the game, in id order.</summary>
    public static IReadOnlyList<Entry> All { get { EnsureLoaded(); return _entries; } }

    /// <summary>
    /// What the game says above an actor's lines, given the name the actor
    /// asset is filed under.
    /// <para/>
    /// The two differ for 69 of the game's 115 actors, and the difference
    /// matters whenever an actor has to be matched to anything outside the
    /// dialogue — the cast, a bust, a character a pack declares. Returns the
    /// name it was given for the ones where they agree.
    /// </summary>
    public static string SpokenActorName(string? actor)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(actor)) return "";
        return _actorNames.TryGetValue(actor!.Trim(), out string? said) ? said : actor!.Trim();
    }

    /// <summary>The folders conversations are grouped into — a room, an event,
    /// an ending — in the order they first appear.</summary>
    public static IEnumerable<string> Folders
        => All.Select(e => e.Folder).Where(f => f.Length > 0).Distinct();

    /// <summary>Look up a conversation's index row by token or by bare path.
    /// Unknown returns null: a pack written against a newer game can name a
    /// dialogue this build has never heard of, and that is the author's
    /// problem to see rather than an error to throw.</summary>
    public static Entry? Find(string? tokenOrPath)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(tokenOrPath)) return null;
        string id = tokenOrPath.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase)
            ? tokenOrPath.Substring(TokenPrefix.Length)
            : tokenOrPath;

        // Exactly as given first, and only then tidied. Two objects in this
        // game are named with a trailing space ("X_Movies/Adult_Dialogue "),
        // so trimming before the lookup made those two unreachable through the
        // very ids the catalog lists them under.
        if (_byId.TryGetValue(id, out var exact)) return exact;
        return _byId.TryGetValue(id.Trim(), out var found) ? found : null;
    }

    /// <summary>
    /// One whole conversation, read from disk the first time it is asked for
    /// and held afterwards.
    /// <para/>
    /// Null when the catalog has no such dialogue, or when its file is missing
    /// or unreadable — the caller shows "not extracted" either way, and a
    /// half-read conversation would be worse than none.
    /// </summary>
    public static Dialogue? Open(string? tokenOrPath)
    {
        var entry = Find(tokenOrPath);
        if (entry == null) return null;

        lock (Gate)
        {
            if (Opened.TryGetValue(entry.Id, out var cached)) return cached;

            Dialogue? loaded = null;
            try
            {
                string path = Path.Combine(Folder(), entry.File.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    loaded = JsonConvert.DeserializeObject<Dialogue>(File.ReadAllText(path));

                // Who is wearing what, before anything reads a line. Done here
                // and only here: the seed and the delta both ask this object
                // what the game has, and a derivation run in two places is one
                // that can disagree with itself.
                VanillaDialogueStaging.Apply(loaded);
            }
            catch (IOException) { }
            catch (JsonException) { }

            Opened[entry.Id] = loaded;
            return loaded;
        }
    }

    // ── Loading ──────────────────────────────────────────────────────

    private static string Folder()
        => Path.Combine(AppContext.BaseDirectory, "VanillaDialogues");

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = Path.Combine(Folder(), "index.json");
                if (!File.Exists(path)) return;

                var parsed = JsonConvert.DeserializeObject<IndexFile>(File.ReadAllText(path));
                if (parsed?.Dialogues == null) return;

                var byId = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in parsed.Dialogues)
                    if (!string.IsNullOrEmpty(entry.Id))
                        byId[entry.Id] = entry;

                _entries = parsed.Dialogues;
                _byId = byId;

                var said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in parsed.ActorNames ?? new Dictionary<string, string>())
                    if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                        said[pair.Key] = pair.Value;
                _actorNames = said;
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }
}
