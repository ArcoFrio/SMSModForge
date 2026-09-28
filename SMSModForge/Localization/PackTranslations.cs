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

    // ── The pack's translations, as a list to work on ───────────────

    /// <summary>One of the pack's translations, as the Edit window lists it.</summary>
    /// <param name="Translated">Texts it has done, by the rule the Translate
    /// window and the offer before an export use
    /// (<see cref="Services.Translation.PackTranslationJob.Missing"/>): not
    /// still the default text word for word, not translated from default text
    /// that has changed since - and a line with nothing in it to translate,
    /// "&lt;size=60%&gt;..." or "{PC}...", is done as it is.</param>
    /// <param name="Total">Texts the pack has, with something in them.</param>
    /// <param name="OutOfDate">Texts translated from default text that has
    /// changed since: they count as still to do.</param>
    public sealed record Summary(string Code, string Path, int Translated, int Total, int OutOfDate)
    {
        /// <summary>How much of it is translated, rounded down: 100 only when
        /// every text is.</summary>
        public int Percent => Total <= 0 ? 0 : (int)Math.Floor(Translated * 100.0 / Total);
    }

    /// <summary>
    /// Every translation beside the pack, with how far along it is.
    /// <para/>
    /// Counted as the Translate window counts what is still to do, so the two
    /// agree: a translation at 100% here is one it has nothing left to send
    /// for. They were counted by the check at first, which took the letters of
    /// "&lt;size=60%&gt;..." for words and a line whose default text had
    /// changed for done - and no finished translation ever reached 100%
    /// (the author, 2026-09-28).
    /// </summary>
    public static List<Summary> Summaries(ModPack pack, string packRoot)
    {
        var list = new List<Summary>();
        foreach (var c in CheckAll(pack, packRoot))
        {
            var file = Loc.Read(c.Path);
            var source = Source(pack, file);
            int total = source.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Text));
            int left = Services.Translation.PackTranslationJob.Missing(source, file, c.Code).Count;
            list.Add(new Summary(c.Code, c.Path, total - left, total, c.Result.Of(TextCheck.Kind.EnglishChanged)));
        }
        return list;
    }

    /// <summary>
    /// Copy what <paramref name="from"/> has translated over <paramref name="to"/>,
    /// as it is: where <paramref name="from"/> has a text, <paramref name="to"/>
    /// takes it word for word - with the note of what it was translated from,
    /// so it goes out of date when that changes, as it would have there. What
    /// <paramref name="to"/> has and <paramref name="from"/> does not is left
    /// alone, and <paramref name="from"/> keeps its own.
    /// <para/>
    /// For a translation typed into the wrong language - Spanish written while
    /// French was up - which would otherwise have to be typed again (the
    /// author, 2026-09-28). Returns how many texts were copied.
    /// </summary>
    public static int Transfer(ModPack pack, string packRoot, string from, string to)
    {
        var source = Loc.Read(PathOf(packRoot, from));
        if (source == null || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 0;
        var target = Loc.Read(PathOf(packRoot, to)) ?? new TextFile();
        int copied = CopyTexts(source, target, Source(pack, source));
        if (copied > 0) Write(pack, packRoot, to, Source(pack, target), target);
        return copied;
    }

    /// <summary>
    /// The copying of <see cref="Transfer"/>, on the files themselves. Only
    /// what is translated: a file the editor writes has every text in it, the
    /// ones nobody has translated yet filled with the default text - and copied
    /// as they are, those would put the default text over what the other
    /// language had translated.
    /// </summary>
    /// <param name="defaults">The pack's default texts, by key.</param>
    public static int CopyTexts(TextFile from, TextFile onto, TextFile defaults)
    {
        int copied = 0;
        foreach (var e in from.Entries)
        {
            if (e.Text.Length == 0 && !e.Blank) continue;   // nothing translated
            // Still the default text: now, or when it was filled in.
            if (!e.Same && !e.Blank
                && (PackTexts.Normal(e.Text) == PackTexts.Normal(defaults.Get(e.Key)) || (e.English != null && e.Text == e.English)))
                continue;
            onto.Remove(e.Key);
            onto.Add(new TextFile.Entry
            {
                Key = e.Key, Text = e.Text, Blank = e.Blank, English = e.English,
                ChangedFrom = e.ChangedFrom, Same = e.Same, Notes = new List<string>(e.Notes),
                Heading = e.Heading,
            });
            copied++;
        }
        return copied;
    }

    /// <summary>Take a translation away, every text in it: its file goes to
    /// the Recycle Bin, where Windows can put it back - a translation can be
    /// hours of somebody's work, and one wrong pick in a list is all it takes.
    /// Under the test harness it is simply deleted: a test's temporary files
    /// are not the author's to find in their bin. False when there was none.</summary>
    public static bool Delete(string packRoot, string code)
    {
        string path = PathOf(packRoot, code);
        if (!File.Exists(path)) return false;
        if (Services.TestMode.Active)
            File.Delete(path);
        else
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        return !File.Exists(path);
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
