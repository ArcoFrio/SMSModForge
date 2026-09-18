using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// What the game does with its own quests, and from where: what starts or
/// resets each quest, and what completes, counts, fails or asks about each
/// task.
/// <para/>
/// Read from <c>VanillaQuests/references.json</c>, written by
/// <c>Tools/Dialogue/BuildQuestReferences.py</c> from the dialogue catalogue
/// and, when it has one, the F8 scene extraction. The catalogue of the quests
/// themselves (<see cref="VanillaQuests"/>) says what a quest IS; this says
/// what the game DOES with it, which is what an author changing one needs
/// before touching it - the same way a conversation shows "the game plays this
/// when".
/// <para/>
/// A task never starts by a script in this game - Game Creator has no
/// instruction for it - so nothing here is about starting tasks. That follows
/// from the quest's structure, and the editor works it out from the catalogue.
/// </summary>
public static class VanillaQuestReferences
{
    /// <summary>One line leading to the line a step sits on, with what had to
    /// be true for it to be offered.</summary>
    public sealed class Lead
    {
        [JsonProperty("node")] public long Node { get; set; }
        [JsonProperty("line")] public string Line { get; set; } = "";
        [JsonProperty("actor")] public string? Actor { get; set; }
        [JsonProperty("when")] public List<VanillaDialogueCatalog.Step> When { get; set; } = new();
    }

    /// <summary>The step itself: Game Creator's type and its own words for it,
    /// and for a counter how much it moves the count.</summary>
    public sealed class Brief
    {
        [JsonProperty("type")] public string Type { get; set; } = "";
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("value")] public JToken? Value { get; set; }
        [JsonProperty("state")] public JToken? State { get; set; }
    }

    /// <summary>One Play of a conversation beside a step.</summary>
    public sealed class Play
    {
        /// <summary>The conversation, by its object path. Null when the script
        /// picks it while the game runs.</summary>
        [JsonProperty("dialogue")] public string? Dialogue { get; set; }

        /// <summary>Whether the Play waits for the conversation to finish, so
        /// that the steps after it run once it has. Null when the script plays
        /// it some other way.</summary>
        [JsonProperty("waits")] public bool? Waits { get; set; }
    }

    /// <summary>The conversations a script plays just before and just after a
    /// step, in the same list of instructions.</summary>
    public sealed class Around
    {
        /// <summary>Played before the step: the step follows it.</summary>
        [JsonProperty("after")] public Play? After { get; set; }

        /// <summary>Played after the step: the step leads into it.</summary>
        [JsonProperty("before")] public Play? Before { get; set; }
    }

    /// <summary>One place the game does something to a quest or a task.</summary>
    public sealed class Site
    {
        /// <summary><c>dialogue</c>: a line of a conversation.
        /// <c>script</c>: a script on an object - a Trigger, a Conditions
        /// component, an Actions list.</summary>
        [JsonProperty("via")] public string Via { get; set; } = "";

        // A line of a conversation.
        [JsonProperty("dialogue")] public string? Dialogue { get; set; }
        [JsonProperty("node")] public long? Node { get; set; }

        /// <summary><c>onStart</c> or <c>onFinish</c>: whether the step runs as
        /// the line appears or once it has been read.</summary>
        [JsonProperty("moment")] public string? Moment { get; set; }
        [JsonProperty("actor")] public string? Actor { get; set; }
        [JsonProperty("line")] public string? Line { get; set; }

        /// <summary>The lines the player passes through first, top first.</summary>
        [JsonProperty("after")] public List<Lead> After { get; set; } = new();

        /// <summary>What plays the conversation, and what it needs to.</summary>
        [JsonProperty("plays")] public List<VanillaDialogueCatalog.Start> Plays { get; set; } = new();

        // A script.
        [JsonProperty("by")] public string? By { get; set; }
        [JsonProperty("script")] public string? Script { get; set; }
        [JsonProperty("event")] public string? Event { get; set; }

        /// <summary>Whether the object was switched on when the extraction
        /// ran. Null when the extraction did not say.</summary>
        [JsonProperty("active")] public bool? Active { get; set; }

        /// <summary>On a prefab the game spawns, rather than in the scene.</summary>
        [JsonProperty("prefab")] public bool Prefab { get; set; }

        [JsonProperty("branches")] public List<string> Branches { get; set; } = new();
        [JsonProperty("around")] public Around? AroundDialogue { get; set; }

        /// <summary>What has to be true where the step sits: the line's own
        /// conditions and the branches it is inside.</summary>
        [JsonProperty("when")] public List<VanillaDialogueCatalog.Step> When { get; set; } = new();

        /// <summary>For a script: where each run of <see cref="When"/> lives in
        /// it, as a conversation's start records it. For a line of a
        /// conversation, <see cref="When"/> is the line's own conditions and
        /// there are none of these.</summary>
        [JsonProperty("gates")] public List<VanillaDialogueCatalog.GateLocation> Gates { get; set; } = new();

        /// <summary>The branches the script tries before the one holding the
        /// step (<see cref="VanillaDialogueCatalog.Start.Ahead"/>).</summary>
        [JsonProperty("ahead")] public List<VanillaDialogueCatalog.EarlierBranch> Ahead { get; set; } = new();

        [JsonProperty("step")] public Brief Step { get; set; } = new();

        public bool IsDialogue => string.Equals(Via, "dialogue", StringComparison.Ordinal);
    }

    /// <summary>Everything the game does to one task.</summary>
    public sealed class TaskSites
    {
        [JsonProperty("completes")] public List<Site> Completes { get; set; } = new();
        [JsonProperty("counts")] public List<Site> Counts { get; set; } = new();
        [JsonProperty("fails")] public List<Site> Fails { get; set; } = new();
        [JsonProperty("abandons")] public List<Site> Abandons { get; set; } = new();
        [JsonProperty("checks")] public List<Site> Checks { get; set; } = new();
        [JsonProperty("other")] public List<Site> Other { get; set; } = new();
    }

    /// <summary>Everything the game does to one quest.</summary>
    public sealed class QuestSites
    {
        [JsonProperty("starts")] public List<Site> Starts { get; set; } = new();
        [JsonProperty("resets")] public List<Site> Resets { get; set; } = new();
        [JsonProperty("checks")] public List<Site> Checks { get; set; } = new();
        [JsonProperty("other")] public List<Site> Other { get; set; } = new();
        [JsonProperty("tasks")] public Dictionary<string, TaskSites> Tasks { get; set; } = new();
    }

    private sealed class FileShape
    {
        [JsonProperty("version")] public int Version { get; set; }

        /// <summary>Whether scripts outside the conversations were read too.</summary>
        [JsonProperty("complete")] public bool Complete { get; set; }

        [JsonProperty("quests")] public Dictionary<string, QuestSites> Quests { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static FileShape? _file;
    private static bool _loaded;

    /// <summary>Whether the file was found and read.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _file != null;
        }
    }

    /// <summary>
    /// Whether every script in the game was searched, not only the
    /// conversations. False means the lists are what the conversations do, and
    /// the editor says so beside them - a list with half its sources missing
    /// reads exactly like a whole one.
    /// </summary>
    public static bool IsComplete
    {
        get
        {
            EnsureLoaded();
            return _file?.Complete == true;
        }
    }

    /// <summary>What the game does to a quest, by the name its instructions
    /// use. Null when nothing is known of it.</summary>
    public static QuestSites? For(string? quest)
    {
        EnsureLoaded();
        if (_file == null || string.IsNullOrEmpty(quest)) return null;
        return _file.Quests.TryGetValue(quest!, out var found) ? found : null;
    }

    /// <summary>What the game does to one task of a quest. Null when nothing is
    /// known of it.</summary>
    public static TaskSites? For(string? quest, string? taskId)
    {
        var sites = For(quest);
        if (sites == null || string.IsNullOrEmpty(taskId)) return null;
        return sites.Tasks.TryGetValue(taskId!, out var found) ? found : null;
    }

    /// <summary>Every quest the file has something for.</summary>
    public static IEnumerable<string> Quests
    {
        get
        {
            EnsureLoaded();
            return _file == null ? Array.Empty<string>() : (IEnumerable<string>)_file.Quests.Keys;
        }
    }

    private static string FilePath()
        => Path.Combine(AppContext.BaseDirectory, "VanillaQuests", "references.json");

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = FilePath();
                if (File.Exists(path))
                    _file = JsonConvert.DeserializeObject<FileShape>(File.ReadAllText(path));
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }
}
