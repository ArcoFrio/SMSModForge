using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;

namespace SMSModForge.Model;

/// <summary>
/// One difference between two manifests, said for a person (the author,
/// 1.8.0: the save prompt "shouldn't overwhelm the user with information
/// that's too technical"). Headed by the thing it belongs to - "Dialogue ·
/// Chat at the cave" - and said the way the issues list says where something
/// is (<see cref="Validation.IssuePlace"/>): a line by its first words, an
/// action by when it runs and its type, a field by the name the editor
/// gives it. Values only where they say something: never a line's id, a
/// list of ids, or a whole record dumped into one row.
/// </summary>
internal static class ChangeWords
{
    internal readonly record struct Words(string Group, string Text, string Was, string Now,
                                          string BeforeInFull, string AfterInFull);

    private const string Step = " › ";

    /// <summary>A value longer than this is cut short under the row; the
    /// tooltip has it whole.</summary>
    private const int ShortValue = 70;

    /// <summary>
    /// The words for one difference, or null when another row already says
    /// it: a line's "what follows" changing only because a line was added or
    /// taken out.
    /// </summary>
    public static Words? Describe(PackChangeKind kind, IReadOnlyList<DiffStep> path, JToken? before, JToken? after, string? moved)
    {
        string section = path.Count > 0 ? path[0].Name : "";
        bool inRecord = path.Count >= 2 && path[1].IsElement;
        string group = inRecord ? KindOf(section) + " · " + RecordName(path[1]) : Loc.T("diff.kind.pack");

        if (inRecord && path.Count == 2)
            return kind switch
            {
                PackChangeKind.Added => new Words(group, Loc.T("diff.recordAdded"), "", "", "", ""),
                PackChangeKind.Removed => new Words(group, Loc.T("diff.recordRemoved"), "", "", "", ""),
                _ => new Words(group, Loc.T("diff.recordChanged"), "", "", "", ""),
            };

        // Down to the change: the things it is inside, then the field.
        var where = new List<string>();
        var fields = new List<string>();
        string? type = null;
        for (int i = inRecord ? 2 : 0; i < path.Count; i++)
        {
            var step = path[i];
            if (!step.IsElement) { fields.Add(step.Name); continue; }
            where.Add(ThingName(step));
            fields.Clear();
            type = Str((step.Either as JObject)?["type"]);
        }

        if (fields.Count == 0)
        {
            // A thing added or taken out: a line, an action, an outfit.
            string thing = string.Join(Step, where);
            string summary = Summary((after ?? before) as JObject);
            return kind switch
            {
                PackChangeKind.Added => new Words(group, Loc.F("diff.newThing", "thing", thing), "", Clip(summary), "", summary),
                PackChangeKind.Removed => new Words(group, Loc.F("diff.removedThing", "thing", thing), Clip(summary), "", summary, ""),
                _ => new Words(group, thing, "", "", "", ""),
            };
        }

        string field = FieldName(fields, type);
        string text = where.Count > 0 ? string.Join(Step, where) + Step + field : field;
        string last = fields[^1];

        if (kind == PackChangeKind.Reordered)
            return new Words(group, Loc.F("diff.newOrder", "what", text), "", Clip(moved ?? ""), "", moved ?? "");

        if (last is "children" or "rootNodeIds")
            return Following(group, text, path, before, after);

        // A number nobody reads; that it changed is all there is to say.
        if (last == "id") return new Words(group, text, "", "", "", "");

        if ((before ?? new JArray()) is JArray wasList && (after ?? new JArray()) is JArray nowList
            && wasList.All(IsScalar) && nowList.All(IsScalar))
        {
            string change = ListChange(wasList, nowList);
            return new Words(group, text, "", Clip(change), Joined(wasList), Joined(nowList));
        }

        string wasFull = before == null ? "" : Value(before);
        string nowFull = after == null ? "" : Value(after);
        var (was, now) = Contrast(wasFull, nowFull);
        if (before == null) was = Loc.T("diff.notSet");
        if (after == null) now = Loc.T("diff.notSet");
        return new Words(group, text, was, now, wasFull, nowFull);
    }

    // ── What things are called ───────────────────────────────────────────

    /// <summary>The kind of record a top-level list holds, for the heading.</summary>
    private static string KindOf(string section)
    {
        string? key = section switch
        {
            "dialogues" => "diff.kind.dialogue",
            "places" => "diff.kind.place",
            "characters" or "actors" => "diff.kind.character",
            "scenes" => "diff.kind.scene",
            "sfx" => "diff.kind.sound",
            "music" => "diff.kind.music",
            "variables" => "diff.kind.variable",
            "integrationRules" => "diff.kind.rule",
            "npcs" => "diff.kind.npc",
            "uis" => "diff.kind.screen",
            "quests" => "diff.kind.quest",
            "wallpapers" => "diff.kind.wallpaper",
            "mapButtons" => "diff.kind.mapButton",
            "vanillaExtensions" => "diff.kind.gamePlace",
            "customRoomTalks" => "diff.kind.roomTalk",
            "dialogueFolders" => "diff.kind.dialogueFolder",
            "sceneFolders" => "diff.kind.sceneFolder",
            "variableFolders" => "diff.kind.variableFolder",
            "integrationFolders" => "diff.kind.ruleFolder",
            _ when section.EndsWith("Folders", StringComparison.Ordinal) => "diff.kind.folder",
            _ => null,
        };
        return key != null ? Loc.T(key) : Humanize(section);
    }

    /// <summary>A record by the name the editor lists it under.</summary>
    private static string RecordName(DiffStep step)
    {
        if (step.Either is not JObject o) return step.Name;
        foreach (var field in new[] { "displayName", "title", "label", "name", "key", "source", "target", "id" })
        {
            string s = Plain(Str(o[field]));
            if (s.Length > 0) return s;
        }
        return Loc.F("diff.thing.item", "n", step.Position);
    }

    /// <summary>Something inside a record: a line by its words, an action or
    /// condition by when it runs and its type, anything else by its name.</summary>
    private static string ThingName(DiffStep step)
    {
        var o = step.Either as JObject;
        string list = step.List;
        if (list == "nodes") return LineName(o);

        string type = Str(o?["type"]);
        if (o != null && type.Length > 0 && o["params"] != null)
        {
            string when = list switch
            {
                "actionsOnStart" => Loc.T("issue.place.onStart"),
                "actionsOnFinish" => Loc.T("issue.place.onFinish"),
                "startConditions" => Loc.T("issue.place.startCondition"),
                _ when list.Contains("onditions", StringComparison.Ordinal) => Loc.T("issue.place.condition"),
                _ => Loc.T("issue.place.action"),
            };
            return when + ": " + type;
        }

        if (o != null)
            foreach (var field in new[] { "displayName", "title", "label", "name", "key", "goName", "type" })
            {
                string s = Plain(Str(o[field]));
                if (s.Length > 0) return s;
            }
        return Loc.F("diff.thing.item", "n", step.Position);
    }

    /// <summary>A line by its first words, as the node list shows it.</summary>
    private static string LineName(JObject? node)
    {
        string text = Plain(Str(node?["text"]));
        if (text.Length > 0) return "“" + ClipAtWord(text, 40) + "”";
        return Str(node?["kind"]) == "Choice" ? Loc.T("diff.thing.choice") : Loc.T("diff.thing.lineNoWords");
    }

    /// <summary>A field by the name the editor gives it; an action's or
    /// condition's setting by its row's label.</summary>
    private static string FieldName(List<string> fields, string? type)
    {
        if (fields.Count >= 2 && fields[0] == "params")
            return string.Join(Step, new[] { ParamName(type, fields[1]) }.Concat(fields.Skip(2).Select(FieldLabel)));
        return string.Join(Step, fields.Select(FieldLabel));
    }

    private static string ParamName(string? type, string key)
    {
        if (!string.IsNullOrEmpty(type))
            foreach (var schemas in new[] { ActionSchemas.ByType, ConditionSchemas.ByType })
                if (schemas.TryGetValue(type!, out var rows))
                    foreach (var row in rows)
                        if (row.Key == key && row.Label.Length > 0 && row.Label != row.LabelKey)
                            return row.Label.TrimEnd(':', ' ');
        return FieldLabel(key);
    }

    /// <summary>
    /// The fields with a name of their own in the language files
    /// (<see cref="FieldKey"/>): the ones an author meets in every pack.
    /// The rest - a place object's noise scale, a layout's cell size - are
    /// their own names in words (<c>noiseScale</c> is "Noise scale"), which
    /// is near enough what their rows are called on screen.
    /// </summary>
    internal static readonly string[] NamedFields =
    {
        "text", "actor", "expression", "outfit", "kind", "tag", "jump", "mode", "timeout", "duration",
        "conditions", "startConditions", "actionsOnStart", "actionsOnFinish", "actions", "children",
        "rootNodeIds", "nodes", "displayName", "key", "name", "title", "description", "label", "priority",
        "queued", "type", "params", "defaultValue", "persisted", "sound", "sceneSprite", "vanillaFrame",
        "externalSpritePath", "audioPath", "defaultVolume", "textPatterns", "loop", "spritePath",
        "unlockConditions", "items", "folders", "dialogues", "scenes", "places", "characters", "sfx", "music",
        "variables", "integrationRules", "npcs", "quests", "wallpapers", "uis", "mapButtons", "tasks",
        "nameColor", "outfits", "expressions", "defaultOutfit", "pronouns", "typewriter", "target", "district",
        "version", "packId", "gameVersion", "forgeVersion", "ignoredIssues", "triggerMode", "sprite", "mask",
        "sortingOrder", "position", "active", "enabled", "color", "tint", "gameObjects", "components",
        "internalName", "navigatorButtons", "value",
    };

    internal static string FieldKey(string name) => "diff.field." + name;

    internal static string FieldLabel(string name)
        => Array.IndexOf(NamedFields, name) >= 0 ? Loc.T(FieldKey(name)) : Humanize(name);

    internal static string Humanize(string name)
    {
        string spaced = Regex.Replace(name.TrimStart('$'), "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
        return spaced.Length == 0 ? name : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    // ── Values ───────────────────────────────────────────────────────────

    /// <summary>
    /// What follows a line, or which lines open the conversation. Ids on
    /// disk; said here as the lines' words - and not at all when the only
    /// difference is a line added or taken out, which has its own row.
    /// </summary>
    private static Words? Following(string group, string text, IReadOnlyList<DiffStep> path, JToken? before, JToken? after)
    {
        var dialogue = path.Count >= 2 ? path[1] : null;
        var nodesBefore = Nodes(dialogue?.Before);
        var nodesAfter = Nodes(dialogue?.After);
        var was = Ids(before);
        var now = Ids(after);

        var common = was.Where(now.Contains).ToList();
        bool explained = now.Where(id => !was.Contains(id)).All(id => nodesAfter.ContainsKey(id) && !nodesBefore.ContainsKey(id))
                         && was.Where(id => !now.Contains(id)).All(id => nodesBefore.ContainsKey(id) && !nodesAfter.ContainsKey(id))
                         && common.SequenceEqual(now.Where(was.Contains));
        if (explained) return null;

        string Lines(List<long> ids, Dictionary<long, JObject> nodes)
            => ids.Count == 0 ? Loc.T("diff.nothing")
                              : Loc.JoinList(ids.Select(id => nodes.TryGetValue(id, out var n) ? LineName(n) : Loc.T("diff.thing.lineNoWords")));
        string wasText = Lines(was, nodesBefore), nowText = Lines(now, nodesAfter);
        return new Words(group, text, Clip(wasText), Clip(nowText), wasText, nowText);
    }

    private static Dictionary<long, JObject> Nodes(JToken? dialogue)
    {
        var nodes = new Dictionary<long, JObject>();
        if ((dialogue as JObject)?["nodes"] is JArray list)
            foreach (var n in list.OfType<JObject>())
                if (n["id"] is JValue { Type: JTokenType.Integer } id) nodes[(long)id] = n;
        return nodes;
    }

    private static List<long> Ids(JToken? list)
        => list is JArray a ? a.OfType<JValue>().Where(v => v.Type == JTokenType.Integer).Select(v => (long)v).ToList() : new();

    /// <summary>A list of names: what joined it, what left it, or only its
    /// order.</summary>
    private static string ListChange(JArray before, JArray after)
    {
        var was = before.Select(Value).ToList();
        var now = after.Select(Value).ToList();
        var added = now.Where(v => !was.Contains(v)).ToList();
        var removed = was.Where(v => !now.Contains(v)).ToList();
        var said = new List<string>();
        if (added.Count > 0) said.Add(Loc.F("diff.listAdded", "items", Few(added)));
        if (removed.Count > 0) said.Add(Loc.F("diff.listRemoved", "items", Few(removed)));
        if (said.Count == 0) said.Add(Loc.T("diff.newOrderShort"));
        return string.Join("; ", said);
    }

    private static string Few(List<string> items)
        => items.Count <= 4 ? Loc.JoinList(items) : Loc.P("diff.andMore", items.Count - 3, "items", Loc.JoinList(items.Take(3)));

    private static string Joined(JArray list) => list.Count == 0 ? Loc.T("diff.nothing") : string.Join(", ", list.Select(Value));

    /// <summary>
    /// A thing added or taken out, in a few words: an action's or
    /// condition's settings by their labels ("Clip: door"). Nothing for a
    /// line or a named thing - the row already names it.
    /// </summary>
    private static string Summary(JObject? o)
    {
        if (o?["params"] is not JObject settings) return "";
        string type = Str(o["type"]);
        return string.Join(", ", settings.Properties()
            .Where(p => IsScalar(p.Value) && Value(p.Value).Length > 0)
            .Take(3)
            .Select(p => ParamName(type, p.Name) + ": " + Value(p.Value)));
    }

    /// <summary>A value as the editor would show it: numbers with a point,
    /// whatever the system's decimal comma; yes and no; words with their
    /// spacing tidied; a list or a record by how much is in it.</summary>
    private static string Value(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Null:
                return Loc.T("common.none");
            case JTokenType.Boolean:
                return Loc.T((bool)token ? "diff.yes" : "diff.no");
            case JTokenType.Integer:
            case JTokenType.Float:
                return Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture) ?? "";
            case JTokenType.String:
            {
                string s = Regex.Replace(token.ToString(), @"\s+", " ").Trim();
                return s.Length == 0 ? Loc.T("diff.empty") : s;
            }
            case JTokenType.Array:
            {
                var list = (JArray)token;
                return list.All(IsScalar) ? Joined(list) : Loc.P("diff.entries", list.Count);
            }
            case JTokenType.Object:
            {
                var o = (JObject)token;
                string summary = Summary(o);
                if (summary.Length > 0) return summary;
                foreach (var field in new[] { "displayName", "title", "label", "name", "key", "text" })
                {
                    string s = Plain(Str(o[field]));
                    if (s.Length > 0) return s;
                }
                return Loc.P("diff.settings", o.Properties().Count());
            }
            default:
                return token.ToString();
        }
    }

    /// <summary>
    /// Two values cut short where they differ: a long line with two words
    /// added at its end reads "…a whisper).  →  …a whisper). Right?" rather
    /// than the same first seventy letters twice.
    /// </summary>
    private static (string Was, string Now) Contrast(string was, string now)
    {
        if (was.Length <= ShortValue && now.Length <= ShortValue) return (was, now);
        int same = 0;
        while (same < was.Length && same < now.Length && was[same] == now[same]) same++;
        int from = Math.Max(0, same - 24);
        if (from > 0)
        {
            int space = was.LastIndexOf(' ', Math.Min(from, was.Length - 1));
            from = space > 0 && space > from - 16 ? space + 1 : from;
        }
        string Cut(string s) => from <= 0 || from >= s.Length ? Clip(s) : "…" + Clip(s[from..], ShortValue - 1);
        return (Cut(was), Cut(now));
    }

    private static bool IsScalar(JToken t) => t.Type is not (JTokenType.Object or JTokenType.Array);

    private static string Str(JToken? token)
        => token == null || token.Type == JTokenType.Null ? "" : token.ToString();

    /// <summary>Words without tags or doubled spacing.</summary>
    private static string Plain(string s) => Regex.Replace(Regex.Replace(s, "<[^>]*>", ""), @"\s+", " ").Trim();

    private static string Clip(string s) => Clip(s, ShortValue);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    /// <summary>Cut short where a word ends, as a line's first words read.</summary>
    private static string ClipAtWord(string s, int max)
    {
        if (s.Length <= max) return s;
        int space = s.LastIndexOf(' ', max - 1);
        return (space > max / 2 ? s[..space] : s[..(max - 1)]).TrimEnd(' ', ',', '.', ';', ':') + "…";
    }
}
