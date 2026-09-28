using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Localization;

/// <summary>
/// What File ▸ Pack translations does: write a pack's text out as a file to
/// translate, bring one up to date after the pack changes, and check what
/// comes back.
/// <para/>
/// The file is the same kind the editor's own translations are - key = text,
/// the pack's words in a note above each - so whatever does the translating,
/// a person in Notepad++ or an AI tool handed the whole file, sees the same
/// thing, and what comes back is checked the same way. The keys come from
/// <see cref="PackTexts"/>, which the plugin uses to put the translation back,
/// so both sides name every text alike.
/// <para/>
/// No windows here: the menu asks and shows, this is the part that has to be
/// right, so it is the part the tests drive.
/// </summary>
public static class PackTranslations
{
    /// <summary>Where a pack's translations live: <c>translations</c> in its folder.</summary>
    public static string FolderOf(string packRoot) => Path.Combine(packRoot, PackTexts.Folder);

    /// <summary>The file for <paramref name="code"/>: <c>translations/es.txt</c>.</summary>
    public static string PathOf(string packRoot, string code) => Path.Combine(FolderOf(packRoot), code + PackTexts.Extension);

    /// <summary>
    /// Every text of the pack a player reads, as the file a translation is
    /// measured against: the pack's own words, each with a note on where it
    /// appears and under a heading for what it belongs to. Read from the pack
    /// as it would be saved, which is what players get.
    /// <para/>
    /// A text the pack leaves empty is in it only where
    /// <paramref name="withWordsIn"/> - the translation it is for - has words
    /// for it: a line typed only in that language. Without it, writing the
    /// file would move that line under "not used", as though the pack had
    /// dropped it. Every other empty text is left out, as it always was.
    /// </summary>
    public static TextFile Source(ModPack pack, TextFile? withWordsIn = null)
    {
        var json = JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var names = new Names(json);
        var file = new TextFile();
        foreach (var site in PackTexts.Of(json, withEmpty: withWordsIn != null))
        {
            if (PackTexts.IsEmpty(site.Text) && PackTexts.IsEmpty(withWordsIn?.Translated(site.Key)))
                continue;
            file.Add(new TextFile.Entry
            {
                Key = site.Key,
                Text = site.Text,
                Notes = new List<string> { Note(site, names) },
                Heading = Heading(site, names),
            });
        }

        // And the game's own lines in the conversations the pack extends, after
        // everything of the pack's: shown only to a player who chooses to see
        // the game's lines translated, and never in the pack's own language,
        // where they are the game's words.
        var taken = new HashSet<string>(file.Entries.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var line in GameLines.Of(pack, taken))
        {
            string id = line.Node.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            file.Add(new TextFile.Entry
            {
                Key = line.Key,
                Text = line.Text,
                Notes = new List<string>
                {
                    string.IsNullOrEmpty(line.Node.Actor)
                        ? Loc.F("packText.note.gameLine", "line", id)
                        : Loc.F("packText.note.gameLineSaidBy", "line", id, "speaker", line.Node.Actor),
                },
                Heading = Loc.F("packText.heading.gameLines", "name", names.Dialogue(line.Dialogue.Key)),
            });
        }
        return file;
    }

    /// <summary>
    /// Write the pack's file for <paramref name="code"/>: brought up to date
    /// if it is there already - every translated line kept, new texts in the
    /// pack's own words, changed ones marked - otherwise started with every
    /// text in the pack's own words, ready to be written over.
    /// </summary>
    public static (string Path, bool Existed) CreateOrUpdate(ModPack pack, string packRoot, string code)
    {
        string path = PathOf(packRoot, code);
        bool existed = File.Exists(path);
        TextFile? existing = existed ? Loc.Read(path) : null;

        string language = TranslationFiles.NativeName(code) ?? code;
        var notes = new List<string>
        {
            Loc.F("packText.file.title", "pack", string.IsNullOrEmpty(pack.PackId) ? Loc.T("packText.file.thisPack") : pack.PackId,
                  "language", language, "code", code),
            "",
        };
        notes.AddRange(Loc.F("packText.file.help", "language", language).Split('\n'));

        Loc.Write(path, TextFileWriter.Build(Source(pack, existing), existing, code, notes));
        return (path, existed);
    }

    /// <summary>
    /// Write <paramref name="file"/> as the pack's translation into
    /// <paramref name="code"/>, in the pack's order and with the notes every
    /// such file carries. <paramref name="source"/> is the pack in its OWN
    /// words, which is what each line's note records it was translated from.
    /// </summary>
    public static string Write(ModPack pack, string packRoot, string code, TextFile source, TextFile file,
                               params string[] moreNotes)
    {
        string path = PathOf(packRoot, code);
        Directory.CreateDirectory(FolderOf(packRoot));
        Loc.Write(path, Text(pack, code, source, file, moreNotes));
        return path;
    }

    /// <summary>What <see cref="Write"/> would put in the file, without writing
    /// it: for a translation edited in the editor, which is written when the
    /// pack is saved rather than when the author switches away from it.</summary>
    public static string Text(ModPack pack, string code, TextFile source, TextFile file, params string[] moreNotes)
    {
        string language = TranslationFiles.NativeName(code) ?? code;
        var notes = new List<string>
        {
            Loc.F("packText.file.title", "pack", string.IsNullOrEmpty(pack.PackId) ? Loc.T("packText.file.thisPack") : pack.PackId,
                  "language", language, "code", code),
            "",
        };
        notes.AddRange(Loc.F("packText.file.help", "language", language).Split('\n'));
        foreach (string note in moreNotes) { notes.Add(""); notes.Add(note); }
        return TextFileWriter.Build(source, file, code, notes);
    }

    /// <summary>One translation the pack has, and what its check found.</summary>
    public sealed record Checked(string Code, string Path, TextCheck.Result Result);

    /// <summary>Every translation in the pack's folder, checked against the
    /// pack as it stands.</summary>
    public static List<Checked> CheckAll(ModPack pack, string packRoot)
    {
        var list = new List<Checked>();
        string folder = FolderOf(packRoot);
        if (!Directory.Exists(folder)) return list;
        foreach (string path in Directory.EnumerateFiles(folder, "*" + PackTexts.Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string code = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!TextFile.IsKey(code)) continue;
            var file = Loc.Read(path);
            if (file == null) continue;
            list.Add(new Checked(code, path, TextCheck.Run(Source(pack, file), file, code, pack.OwnLanguage)));
        }
        return list;
    }

    // ── Where each text is, in words ────────────────────────────────

    /// <summary>What the pack calls its things, for the notes: a dialogue's
    /// name rather than its key, a character's name rather than theirs.</summary>
    private sealed class Names
    {
        private readonly Dictionary<string, string> _dialogues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _characters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _quests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _places = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _uis = new(StringComparer.OrdinalIgnoreCase);

        public Names(JObject json)
        {
            Collect(json["dialogues"], "key", "displayName", _dialogues);
            Collect(json["characters"], "key", "displayName", _characters);
            Collect(json["actors"], "key", "displayName", _characters);
            Collect(json["quests"], "key", "title", _quests);
            Collect(json["places"], "key", "displayName", _places);
            Collect(json["uis"], "id", "name", _uis);
            // One of the game's quests goes by the game's name for it.
            foreach (var q in (json["quests"] as JArray ?? new JArray()).OfType<JObject>())
                if (!string.IsNullOrEmpty((string?)q["source"]) && (string?)q["key"] is { Length: > 0 } key)
                    _quests[key] = (string)q["source"]!;
        }

        private static void Collect(JToken? list, string keyField, string nameField, Dictionary<string, string> into)
        {
            foreach (var o in (list as JArray ?? new JArray()).OfType<JObject>())
            {
                string? key = (string?)o[keyField];
                if (string.IsNullOrEmpty(key) || into.ContainsKey(key)) continue;
                string? name = (string?)o[nameField];
                into[key] = string.IsNullOrWhiteSpace(name) ? key : name;
            }
        }

        private static string Of(Dictionary<string, string> map, string key)
            => map.TryGetValue(key ?? "", out var name) ? name : key ?? "";

        public string Dialogue(string key) => Of(_dialogues, key);
        public string Character(string key) => Of(_characters, key);
        public string Quest(string key) => Of(_quests, key);
        public string Place(string key) => Of(_places, key);
        public string Ui(string key) => Of(_uis, key);
    }

    private static string Heading(PackTexts.Site site, Names names) => site.Kind switch
    {
        PackTexts.Kind.CharacterName => Loc.T("packText.heading.characters"),
        PackTexts.Kind.Line => Loc.F("packText.heading.dialogue", "name", names.Dialogue(site.Owner)),
        PackTexts.Kind.NavigatorLabel or PackTexts.Kind.MapLabel => Loc.T("packText.heading.buttons"),
        PackTexts.Kind.UiText => Loc.F("packText.heading.screen", "name", names.Ui(site.Owner)),
        _ => Loc.F("packText.heading.quest", "name", names.Quest(site.Owner)),
    };

    private static string Note(PackTexts.Site site, Names names)
    {
        switch (site.Kind)
        {
            case PackTexts.Kind.CharacterName:
                return Loc.T("packText.note.name");
            case PackTexts.Kind.Line:
                if (string.Equals((string?)site.Holder["kind"], nameof(DialogueNodeKind.Choice), StringComparison.OrdinalIgnoreCase))
                    return Loc.F("packText.note.choice", "line", site.Detail);
                return string.IsNullOrEmpty(site.Speaker)
                    ? Loc.F("packText.note.line", "line", site.Detail)
                    : Loc.F("packText.note.lineSaidBy", "line", site.Detail, "speaker", names.Character(site.Speaker));
            case PackTexts.Kind.QuestTitle:
                return Loc.T("packText.note.questTitle");
            case PackTexts.Kind.QuestDescription:
                return Loc.T("packText.note.questDescription");
            case PackTexts.Kind.TaskName:
                return Loc.F("packText.note.taskName", "task", site.Detail);
            case PackTexts.Kind.TaskQuestDescription:
                return Loc.F("packText.note.taskQuestDescription", "task", site.Detail);
            case PackTexts.Kind.GameTaskQuestDescription:
                return Loc.F("packText.note.gameTaskQuestDescription", "task", site.Detail);
            case PackTexts.Kind.NavigatorLabel:
                return Loc.F("packText.note.navigator", "place", names.Place(site.Owner), "target", site.Detail);
            case PackTexts.Kind.MapLabel:
                return Loc.F("packText.note.mapButton", "district", site.Owner, "target", site.Detail);
            case PackTexts.Kind.UiText:
                return Loc.F("packText.note.ui", "path", site.Detail);
            default:
                return "";
        }
    }
}
