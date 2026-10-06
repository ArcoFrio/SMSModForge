using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;

namespace SMSModForge.Services.Audio;

/// <summary>
/// What saving does with the sounds edited in the SFX and Music tabs: writes
/// each over the file it was made from, so the pack holds one file per sound
/// and ships one (the author, 1.7.0).
/// <para/>
/// <b>The recording is kept, out of the way.</b> Before an edit first replaces
/// a file, the file is moved into <see cref="KeptFolder"/>, at the same path
/// inside it, and every edit after that is made from it again - so a cut can
/// still be put back, Reset still gives the recording back, and a sound is
/// never an edit of an edit. The export leaves that folder out. A recording
/// goes back where it was the first time a save finds no edit using it: the
/// edit was reset, the sound pointed at another file, or removed.
/// <para/>
/// Variants - the <c>_1</c>, <c>_2</c> files the game picks between - are each
/// written over their own file, which is exactly where the game looks for them.
/// A WAV stays a WAV and an OGG an OGG; an MP3, which nothing here can write,
/// becomes an OGG of the same name beside where it was.
/// <para/>
/// Two cases keep a file of their own instead, in an <see cref="Folder"/>
/// folder beside the recording, the way 1.7.0's first builds always did: a
/// recording another sound also uses, which replacing would change for that one
/// too, and an MP3 whose OGG name is already taken.
/// </summary>
public static class SfxEdits
{
    /// <summary>The folder, beside a recording, an edited sound is written to
    /// when it cannot replace the recording.</summary>
    public const string Folder = "edited";

    /// <summary>The folder in the pack the recordings edits have replaced are
    /// kept in, at their own paths inside it. Never exported.</summary>
    public const string KeptFolder = ".originals";

    /// <summary>Where a recording is kept while an edit has replaced it.</summary>
    public static string KeptRel(string rel) => Join(KeptFolder, rel.Replace('\\', '/'));

    /// <summary>Whether a pack path is inside <see cref="KeptFolder"/>.</summary>
    public static bool IsKept(string? rel)
        => !string.IsNullOrEmpty(rel)
           && rel.Replace('\\', '/').StartsWith(KeptFolder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The recording a pack path names, wherever it is now: kept aside
    /// while an edit has replaced it, otherwise where the path says.</summary>
    public static string RecordingAbs(string packRoot, string rel)
    {
        string kept = Abs(packRoot, KeptRel(rel));
        return File.Exists(kept) ? kept : Abs(packRoot, rel);
    }

    public static bool RecordingExists(string packRoot, string rel) => File.Exists(RecordingAbs(packRoot, rel));

    /// <summary>The file an edit of a recording replaces it with: itself, for
    /// a WAV or an OGG; an OGG of the same name for anything else.</summary>
    public static string ReplacementFor(string rel)
    {
        string ext = Path.GetExtension(rel);
        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            return rel;
        return Join(Dir(rel), Path.GetFileNameWithoutExtension(rel) + ".ogg");
    }

    private static readonly string[] Extensions = { ".ogg", ".OGG", ".wav", ".WAV", ".mp3", ".MP3" };

    /// <summary>
    /// A recording and the variants beside it, as paths relative to the pack:
    /// the recording first, then <c>_1</c>, <c>_2</c>... up to the first that
    /// is not there - in any of the extensions the game reads, as it does.
    /// </summary>
    public static List<string> Files(string packRoot, string sourceRel)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(sourceRel)) return files;
        files.Add(sourceRel);

        string dir = Dir(sourceRel);
        string name = Path.GetFileNameWithoutExtension(sourceRel);
        string ext = Path.GetExtension(sourceRel);
        for (int i = 1; i < 1000; i++)
        {
            string? found = null;
            foreach (string e in new[] { ext }.Concat(Extensions))
            {
                string candidate = Join(dir, name + "_" + i + e);
                if (RecordingExists(packRoot, candidate)) { found = candidate; break; }
            }
            if (found == null) break;
            files.Add(found);
        }
        return files;
    }

    /// <summary>One edited sound of the pack: an SFX, whose variants are
    /// made too, or a music track, which has none.</summary>
    private sealed class Sound
    {
        public string Key = "";
        public SfxEditDef? Edit;
        public Func<string> Path = null!;
        public Action<string> SetPath = null!;
        public bool Variants;
    }

    private static IEnumerable<Sound> Sounds(ModPack pack)
    {
        foreach (var s in pack.Sfx)
        {
            var x = s;
            yield return new Sound { Key = x.Key, Edit = x.Edit, Path = () => x.AudioPath, SetPath = v => x.AudioPath = v, Variants = true };
        }
        foreach (var m in pack.Music)
        {
            var x = m;
            yield return new Sound { Key = x.Key, Edit = x.Edit, Path = () => x.AudioPath, SetPath = v => x.AudioPath = v, Variants = false };
        }
    }

    /// <summary>
    /// Make every edited sound of the pack - SFX and music - into its files,
    /// where what they were made from has changed since they were last made
    /// (or they are missing), and put back every kept recording no edit uses
    /// any more. Returns the files written, relative to the pack.
    /// </summary>
    public static List<string> RenderAll(ModPack pack, string packRoot)
    {
        var written = new List<string>();
        var sounds = Sounds(pack).ToList();
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sound in sounds)
        {
            var edit = sound.Edit;
            if (edit == null || string.IsNullOrWhiteSpace(edit.Source)) continue;

            var sources = sound.Variants ? Files(packRoot, edit.Source) : new List<string> { edit.Source };
            if (sources.Count == 0 || !RecordingExists(packRoot, sources[0])) continue;   // nothing to make it from

            string? inPlace = InPlace(sound, sounds, packRoot);
            string output = inPlace ?? OutputFor(sound, sounds, packRoot);
            var outputs = Enumerable.Range(0, sources.Count)
                .Select(i => inPlace != null ? ReplacementFor(sources[i]) : VariantOf(output, i)).ToList();
            if (inPlace != null) replaced.UnionWith(sources);

            // Replaced before, and its kept recording gone since - deleted by
            // hand. The file in its place is the last edit of it, and making
            // it again would be an edit of an edit.
            if (inPlace != null && edit.Rendered != null
                && string.Equals(sound.Path(), output, StringComparison.OrdinalIgnoreCase)
                && !File.Exists(Abs(packRoot, KeptRel(sources[0]))))
                continue;

            string fingerprint = Fingerprint(edit, packRoot, sources);
            bool current = edit.Rendered == fingerprint
                           && string.Equals(sound.Path(), output, StringComparison.OrdinalIgnoreCase)
                           && outputs.All(o => File.Exists(Abs(packRoot, o)));
            if (!current)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    // Moved aside first, the first time: from then on the
                    // recording is the kept one, and the file in its place
                    // is the edit.
                    if (inPlace != null) KeepAside(packRoot, sources[i]);
                    var data = AudioData.Load(RecordingAbs(packRoot, sources[i]));
                    var made = SfxRenderer.Render(data, edit.PiecesOf(Path.GetFileName(sources[i])), edit);
                    Write(made, Abs(packRoot, outputs[i]));
                    written.Add(outputs[i]);
                }
                edit.Rendered = fingerprint;
                sound.SetPath(output);
            }

            // A variant the recording no longer has would still be found
            // beside a made file, and played. Not beside a replaced one:
            // those are the author's own files.
            if (inPlace == null)
                for (int i = sources.Count; ; i++)
                {
                    string extra = Abs(packRoot, VariantOf(output, i));
                    if (!File.Exists(extra)) break;
                    File.Delete(extra);
                }
        }

        PutBack(packRoot, replaced);
        return written;
    }

    /// <summary>Whether there is anything for <see cref="RenderAll"/> to do:
    /// an edited sound, or a kept recording that may have to go back.</summary>
    public static bool HasWork(ModPack pack, string packRoot)
        => pack.Sfx.Any(s => s.Edit != null) || pack.Music.Any(m => m.Edit != null)
           || Directory.Exists(Abs(packRoot, KeptFolder));

    /// <summary>
    /// The file an edited sound replaces its recording with - or null when it
    /// cannot: another sound plays that recording too, or (for an MP3) the
    /// OGG of its name is somebody else's file.
    /// </summary>
    private static string? InPlace(Sound sound, List<Sound> all, string packRoot)
    {
        string source = sound.Edit!.Source;
        string output = ReplacementFor(source);
        foreach (var other in all)
        {
            if (other == sound) continue;
            if (Same(other.Path(), source) || Same(other.Edit?.Source, source)) return null;
            if (!Same(output, source) && (Same(other.Path(), output) || Same(other.Edit?.Source, output))) return null;
        }
        if (!Same(output, source) && File.Exists(Abs(packRoot, output)) && !Same(sound.Path(), output)) return null;
        return output;
    }

    private static bool Same(string? a, string? b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
           && string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Move a recording into the kept folder, unless it is there
    /// already.</summary>
    private static void KeepAside(string packRoot, string rel)
    {
        string kept = Abs(packRoot, KeptRel(rel));
        string here = Abs(packRoot, rel);
        if (File.Exists(kept) || !File.Exists(here)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.Move(here, kept);
    }

    /// <summary>
    /// Every kept recording that no edit replaces any more, moved back over
    /// the edit that stood in its place; the folders left empty go too.
    /// </summary>
    private static void PutBack(string packRoot, HashSet<string> replaced)
    {
        string keptRoot = Abs(packRoot, KeptFolder);
        if (!Directory.Exists(keptRoot)) return;
        foreach (string file in Directory.EnumerateFiles(keptRoot, "*", SearchOption.AllDirectories).ToList())
        {
            string rel = Path.GetRelativePath(keptRoot, file).Replace('\\', '/');
            if (replaced.Contains(rel)) continue;
            string back = Abs(packRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(back)!);
            File.Move(file, back, overwrite: true);
        }
        foreach (string dir in Directory.EnumerateDirectories(keptRoot, "*", SearchOption.AllDirectories)
                                        .OrderByDescending(d => d.Length).ToList())
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        if (!Directory.EnumerateFileSystemEntries(keptRoot).Any()) Directory.Delete(keptRoot);
    }

    /// <summary>A WAV as a WAV, anything else as an OGG.</summary>
    private static void Write(AudioData sound, string path)
    {
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)) sound.WriteWav(path);
        else sound.WriteOgg(path);
    }

    /// <summary>
    /// Where an edited sound's file goes when it cannot replace its recording:
    /// where it went before, when it has one
    /// already; otherwise <c>edited/&lt;key&gt;.ogg</c> beside the recording, with
    /// a number added when that name is taken by another sound's file, or by a
    /// file already there.
    /// </summary>
    private static string OutputFor(Sound sound, List<Sound> all, string packRoot)
    {
        var edit = sound.Edit!;
        string now = sound.Path();
        if (IsMade(now) && !string.Equals(now, edit.Source, StringComparison.OrdinalIgnoreCase))
            return now;

        string dir = Join(Dir(edit.Source), Folder);
        string name = Safe(sound.Key);
        var others = all.Where(s => s != sound).Select(s => s.Path())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            string candidate = Join(dir, (n == 1 ? name : name + "-" + n) + ".ogg");
            if (!others.Contains(candidate) && !File.Exists(Abs(packRoot, candidate))) return candidate;
        }
    }

    /// <summary>Whether a path is one of the files this makes: a sound in an
    /// <c>edited</c> folder.</summary>
    public static bool IsMade(string? rel)
    {
        if (string.IsNullOrEmpty(rel)
            || !(rel.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
            return false;
        var parts = rel.Replace('\\', '/').Split('/');
        return parts.Length >= 2 && string.Equals(parts[^2], Folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Files made for sounds that no longer use them - an edit put back to the
    /// recording, a sound removed - read from the pack as it was last saved.
    /// Only ever files this made, beside a recording rather than over it: a
    /// sound in an <c>edited</c> folder, or the OGG an MP3 became, and their
    /// variants. A recording an edit replaced is <see cref="RenderAll"/>'s to
    /// put back.
    /// </summary>
    public static List<string> Stale(string? savedManifest, ModPack pack, string packRoot)
    {
        var stale = new List<string>();
        if (string.IsNullOrWhiteSpace(savedManifest)) return stale;

        JObject saved;
        try { saved = JObject.Parse(savedManifest); }
        catch (JsonException) { return stale; }

        var inUse = Sounds(pack).SelectMany(s => new[] { s.Path(), s.Edit?.Source ?? "" })
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string list in new[] { "sfx", "music" })
        {
            if (saved[list] is not JArray sounds) continue;
            foreach (var s in sounds.OfType<JObject>())
            {
                if (s["edit"] is not JObject edit) continue;
                string made = (string?)s["audioPath"] ?? "";
                string source = (string?)edit["source"] ?? "";
                if (Same(made, source)) continue;
                if (!(IsMade(made) || Same(made, ReplacementFor(source)))) continue;
                if (inUse.Contains(made) || IsKept(made)) continue;
                for (int i = 0; ; i++)
                {
                    string file = VariantOf(made, i);
                    if (!File.Exists(Abs(packRoot, file))) break;
                    stale.Add(file);
                }
            }
        }
        return stale;
    }

    /// <summary>
    /// What an edit's files are made from, as one string that changes when
    /// any of it does: the edit itself, and each recording's size and time.
    /// </summary>
    public static string Fingerprint(SfxEditDef edit, string packRoot, IEnumerable<string> sources)
    {
        var copy = JObject.FromObject(edit);
        copy.Remove("rendered");
        var sb = new StringBuilder(copy.ToString(Formatting.None));
        foreach (string rel in sources)
        {
            var info = new FileInfo(RecordingAbs(packRoot, rel));
            sb.Append('|').Append(rel).Append(':').Append(info.Exists ? info.Length : -1)
              .Append(':').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
        }
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    /// <summary>The file a variant is made into: <c>plap.ogg</c>,
    /// <c>plap_1.ogg</c>, <c>plap_2.ogg</c>.</summary>
    public static string VariantOf(string output, int index)
        => index == 0 ? output
         : Join(Dir(output), Path.GetFileNameWithoutExtension(output) + "_" + index + Path.GetExtension(output));

    public static string Abs(string packRoot, string rel)
        => Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    private static string Dir(string rel)
    {
        int at = rel.Replace('\\', '/').LastIndexOf('/');
        return at < 0 ? "" : rel.Substring(0, at);
    }

    private static string Join(string dir, string name) => dir.Length == 0 ? name : dir + "/" + name;

    /// <summary>A key as a file name: what Windows forbids, made <c>_</c>.</summary>
    private static string Safe(string key)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in string.IsNullOrWhiteSpace(key) ? "sound" : key.Trim())
            sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
