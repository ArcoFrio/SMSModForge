using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SMSModForge.Model;

/// <summary>
/// One piece of a pack as an undo step keeps it: the text of one entry of one
/// of its lists (a place, a dialogue, a character) or of one of its other
/// values. Kept once however many steps hold it - a step that changed one
/// dialogue shares every other piece with the step before - and, once no step
/// that is current needs to read it, packed small (<see cref="Pack"/>).
/// </summary>
public class PackPiece
{
    private readonly object _gate = new();
    private string? _text;
    private byte[]? _packed;
    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);

    public PackPiece(string text)
    {
        _text = text;
        Length = text.Length;
    }

    /// <summary>How long its text is - known without unpacking it, for telling two pieces apart cheaply.</summary>
    public int Length { get; }

    /// <summary>The piece's text, exactly as it was written.</summary>
    public virtual string Text
    {
        get
        {
            lock (_gate)
            {
                if (_text != null) return _text;
                using var packed = new MemoryStream(_packed!);
                using var deflate = new DeflateStream(packed, CompressionMode.Decompress);
                using var plain = new MemoryStream();
                deflate.CopyTo(plain);
                _text = Utf8.GetString(plain.GetBuffer(), 0, (int)plain.Length);
                _packed = null;
                return _text;
            }
        }
    }

    /// <summary>Whether it is kept packed now - for tests.</summary>
    internal virtual bool IsPacked { get { lock (_gate) return _text == null; } }

    /// <summary>
    /// Keep it small: its text as UTF-8, deflated. A long session's steps were
    /// the whole pack written out again each, six megabytes a step on a large
    /// pack; a piece is a few hundred kilobytes at most and packs to a tenth.
    /// A text UTF-8 cannot hold (a lone half of a pair) is kept as it is, and so
    /// is one too short to be worth it.
    /// </summary>
    public virtual void Pack()
    {
        lock (_gate)
        {
            if (_text == null || _text.Length < 2048) return;
            byte[] bytes;
            try { bytes = Utf8.GetBytes(_text); }
            catch (EncoderFallbackException) { return; }
            using var packed = new MemoryStream(bytes.Length / 6 + 64);
            using (var deflate = new DeflateStream(packed, CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(bytes, 0, bytes.Length);
            _packed = packed.ToArray();
            _text = null;
        }
    }

    public virtual bool SameTextAs(PackPiece other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is SplitPiece) return other.SameTextAs(this);
        return string.Equals(Text, other.Text, StringComparison.Ordinal);
    }

    /// <summary>The pieces that hold text: this one - or, of an entry kept in parts, each part.</summary>
    public virtual IEnumerable<PackPiece> Leaves() { yield return this; }
}

/// <summary>
/// An entry kept in parts (1.10.0): its own values, and each object inside it on
/// its own - a screen and every object on it, a place or a game's level and
/// every object in it. One of the author's packs changes a
/// screen of the game's that is three megabytes written out; as one piece, finding
/// out whether anything had changed wrote all of it out again on one core (24 ms of
/// a 31 ms look), and taking back one object moved read all of it and built the
/// whole screen's rows again (a third of a second). In parts, the objects are
/// written side by side on every core, a step shares every object that did not
/// change, and two steps say which objects differ - so one object is put back.
/// </summary>
public sealed class SplitPiece : PackPiece
{
    /// <summary>[0] the entry without its objects; then each object without what is under it, in the order
    /// the tree is read: an object, then everything under it.</summary>
    public PackPiece[] Parts { get; }

    /// <summary>[0] how many objects the entry has at the top; [k] how many are under the object in part k.</summary>
    public int[] Kids { get; }

    private readonly PackPieces.Splitting _how;
    private string? _whole;

    internal SplitPiece(PackPiece[] parts, int[] kids, PackPieces.Splitting how) : base("")
    {
        Parts = parts;
        Kids = kids;
        _how = how;
    }

    /// <summary>The whole entry as one text, made from its parts when somebody needs it so.</summary>
    public override string Text => _whole ??= JsonConvert.SerializeObject(Build(), _how.EntryType, PackRepository.ManifestSettings);

    internal override bool IsPacked => Parts.All(p => p.IsPacked);

    public override void Pack()
    {
        _whole = null;
        foreach (var part in Parts) part.Pack();
    }

    /// <summary>The whole text, if it was made, let go: the parts say it all.</summary>
    internal void ForgetWhole() => _whole = null;

    public override IEnumerable<PackPiece> Leaves() => Parts;

    public override bool SameTextAs(PackPiece other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is not SplitPiece split) return string.Equals(Text, other.Text, StringComparison.Ordinal);
        if (!SameShapeAs(split)) return false;
        for (int k = 0; k < Parts.Length; k++)
            if (!Parts[k].SameTextAs(split.Parts[k])) return false;
        return true;
    }

    /// <summary>Whether another has the same objects hung the same way, whatever each says.</summary>
    public bool SameShapeAs(SplitPiece other)
    {
        if (Parts.Length != other.Parts.Length) return false;
        for (int k = 0; k < Kids.Length; k++)
            if (Kids[k] != other.Kids[k]) return false;
        return true;
    }

    /// <summary>The entry, read from its parts and put together.</summary>
    public object Build()
    {
        object entry = JsonConvert.DeserializeObject(Parts[0].Text, _how.EntryType, _how.Apart)!;
        int k = 1;
        void Read(IList into, int count)
        {
            for (int i = 0; i < count; i++)
            {
                object node = JsonConvert.DeserializeObject(Parts[k].Text, _how.NodeType, _how.Apart)!;
                int under = Kids[k];
                k++;
                into.Add(node);
                Read(_how.Under(node), under);
            }
        }
        Read(_how.Top(entry), Kids[0]);
        return entry;
    }

    /// <summary>The object of one part, on its own: with nothing under it.</summary>
    public object ReadNode(int part) => JsonConvert.DeserializeObject(Parts[part].Text, _how.NodeType, _how.Apart)!;

    /// <summary>The object of one part with everything under it.</summary>
    public object BuildNode(int part)
    {
        int k = part;
        object Read()
        {
            object node = JsonConvert.DeserializeObject(Parts[k].Text, _how.NodeType, _how.Apart)!;
            int under = Kids[k];
            k++;
            var into = _how.Under(node);
            for (int i = 0; i < under; i++) into.Add(Read());
            return node;
        }
        return Read();
    }

    /// <summary>For each part, the parts of the objects right under it, in their order ([0]: the entry's top
    /// objects).</summary>
    public int[][] ChildParts()
    {
        if (_childParts != null) return _childParts;
        var of = new int[Parts.Length][];
        int next = 1;
        void Lay(int k)
        {
            var mine = new int[Kids[k]];
            for (int i = 0; i < mine.Length; i++)
            {
                mine[i] = next++;
                Lay(mine[i]);
            }
            of[k] = mine;
        }
        Lay(0);
        return _childParts = of;
    }

    private int[][]? _childParts;
}

/// <summary>
/// A pack as an undo step keeps it (1.10.0): section by section - the pack's
/// own top-level names, in its own order - and, for a section that is a list
/// of records, entry by entry.
/// <para/>
/// Until 1.10.0 a step was the whole pack written out as one text. Finding out
/// whether anything had changed meant writing all of it again and comparing -
/// 126 ms on a large pack, at every click of a button (twice) and every time a
/// box was left - and putting a step back meant reading all of it and building
/// every tab again, a second and more. Taken in pieces:
/// <list type="bullet">
/// <item>the entries are written out side by side, on every core at once (they
/// do not share anything), and each is held against the piece the last step
/// has for it - the same text is the same piece, kept once;</item>
/// <item>two steps differ exactly where their pieces do (<see cref="Diff"/>),
/// so what an undo has to put back - one dialogue, one place - is known
/// without reading anything, and only that is read and built again
/// (<see cref="Apply"/>).</item>
/// </list>
/// Put together again (<see cref="ToJson"/>) the pieces read back as the pack
/// the whole text reads back as: PackPiecesTests holds that on real packs.
/// </summary>
public sealed class PackPieces
{
    /// <summary>The sections, by their names in the manifest, in the pack's order.</summary>
    public string[] Names { get; }

    /// <summary>Whether each section is a list of records, kept entry by entry (else one piece: its whole value).</summary>
    public bool[] Lists { get; }

    /// <summary>Each section's pieces.</summary>
    public PackPiece[][] Entries { get; }

    /// <summary>What the editing language showed at this step (LanguageSession.Wrap's header: its code and its
    /// texts), when the step was taken with one up; null for the pack alone.</summary>
    public string? Language { get; }

    private PackPieces(string[] names, bool[] lists, PackPiece[][] entries, string? language)
    {
        Names = names; Lists = lists; Entries = entries; Language = language;
    }

    /// <summary>A step that is one text and nothing else known about it - for a pack the pieces cannot be taken
    /// of (see <see cref="Parts"/>), and for tests.</summary>
    public static PackPieces Whole(string text, string? language = null)
        => new(new[] { WholeName }, new[] { false }, new[] { new[] { new PackPiece(text) } }, language);

    private const string WholeName = "*";

    /// <summary>Whether this is one text for the whole pack.</summary>
    public bool IsWhole => Names.Length == 1 && Names[0] == WholeName;

    // ── Entries kept in parts ───────────────────────────────────────────

    /// <summary>How the entries of one section are kept in parts: what an entry holds its objects in, what an
    /// object holds those under it in, and how each is written without them.</summary>
    internal sealed class Splitting
    {
        public Type EntryType = null!, NodeType = null!;
        public Func<object, IList> Top = null!, Under = null!;
        public JsonSerializerSettings Apart = null!;
    }

    /// <summary>
    /// Writes an entry without its objects and an object without those under it: of the one thing being written
    /// (<see cref="Root"/>), and only of it. An object of the same kind further in - one hung on an NPC that
    /// stands on the object being written - is not a part of its own, and is written whole where it is.
    /// </summary>
    private sealed class ApartResolver : DefaultContractResolver
    {
        /// <summary>The entry or object being written apart on this thread.</summary>
        [ThreadStatic] public static object? Root;

        private readonly (Type Type, string Member)[] _left;
        public ApartResolver(params (Type, string)[] leftOut) => _left = leftOut;

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            foreach (var (type, name) in _left)
            {
                if (member.DeclaringType != type || member.Name != name) continue;
                var was = property.ShouldSerialize;
                property.ShouldSerialize = record => !ReferenceEquals(record, Root) && (was == null || was(record));
            }
            return property;
        }
    }

    private static readonly JsonSerializerSettings ApartSettings = new()
    {
        Formatting = PackRepository.ManifestSettings.Formatting,
        NullValueHandling = PackRepository.ManifestSettings.NullValueHandling,
        ContractResolver = new ApartResolver(
            (typeof(UiDef), nameof(UiDef.Nodes)), (typeof(UiNodeDef), nameof(UiNodeDef.Children)),
            (typeof(PlaceDef), nameof(PlaceDef.GameObjects)), (typeof(VanillaPlaceExtensionDef), nameof(VanillaPlaceExtensionDef.GameObjects)),
            (typeof(GameObjectDef), nameof(GameObjectDef.Children))),
    };

    /// <summary>The sections kept in parts, by name: what an author changes one object of at a time, and what
    /// can be the whole of something of the game's - a screen, a level.</summary>
    private static readonly Dictionary<string, Splitting> Splits = new(StringComparer.Ordinal)
    {
        ["uis"] = new Splitting
        {
            EntryType = typeof(UiDef),
            NodeType = typeof(UiNodeDef),
            Top = entry => ((UiDef)entry).Nodes,
            Under = node => ((UiNodeDef)node).Children,
            Apart = ApartSettings,
        },
        ["places"] = new Splitting
        {
            EntryType = typeof(PlaceDef),
            NodeType = typeof(GameObjectDef),
            Top = entry => ((PlaceDef)entry).GameObjects,
            Under = node => ((GameObjectDef)node).Children,
            Apart = ApartSettings,
        },
        ["vanillaExtensions"] = new Splitting
        {
            EntryType = typeof(VanillaPlaceExtensionDef),
            NodeType = typeof(GameObjectDef),
            Top = entry => ((VanillaPlaceExtensionDef)entry).GameObjects,
            Under = node => ((GameObjectDef)node).Children,
            Apart = ApartSettings,
        },
    };

    /// <summary>For tests: every entry one piece, as before entries were kept in parts.</summary>
    internal static bool NoSplitsForTests;

    // ── The pack's sections, as the serializer has them ─────────────────

    private sealed class Part
    {
        public JsonProperty Property = null!;
        public string Name = "";
        public bool IsList;
        public Type? ElementType;
        public Splitting? Split;
    }

    /// <summary>
    /// The pack's top-level values as the serializer writes them, or null when one of them is written in a way
    /// this does not reproduce (its own converter, a converter for its items, type names): the step is then the
    /// whole text, as it always was - slower, never wrong. None is today; PackPiecesTests says so if one appears.
    /// </summary>
    private static readonly Lazy<Part[]?> Parts = new(() =>
    {
        var settings = PackRepository.ManifestSettings;
        if (settings.TypeNameHandling != TypeNameHandling.None || settings.Converters.Count > 0
            || settings.PreserveReferencesHandling != PreserveReferencesHandling.None
            || settings.DefaultValueHandling != DefaultValueHandling.Include
            || settings.NullValueHandling != NullValueHandling.Ignore) return null;
        var serializer = JsonSerializer.Create(settings);
        if (serializer.ContractResolver.ResolveContract(typeof(ModPack)) is not JsonObjectContract contract) return null;
        if (contract.ExtensionDataGetter != null || contract.Converter != null) return null;
        var parts = new List<Part>();
        foreach (var p in contract.Properties)
        {
            if (p.Ignored || !p.Readable || p.ValueProvider == null || p.PropertyName == null || p.PropertyType == null) continue;
            if (p.Converter != null || p.ItemConverter != null || p.TypeNameHandling != null || p.ItemTypeNameHandling != null
                || p.IsReference == true || p.ItemIsReference == true || p.DefaultValueHandling != null
                || (p.NullValueHandling != null && p.NullValueHandling != NullValueHandling.Ignore)) return null;
            var part = new Part { Property = p, Name = p.PropertyName };
            var element = ElementOf(p.PropertyType);
            // A list of records: classes of the pack's own, not texts or numbers.
            if (element != null && element.IsClass && element != typeof(string) && typeof(IList).IsAssignableFrom(p.PropertyType)
                && serializer.ContractResolver.ResolveContract(element) is JsonObjectContract)
            {
                part.IsList = true;
                part.ElementType = element;
                if (Splits.TryGetValue(part.Name, out var split) && split.EntryType == element) part.Split = split;
            }
            parts.Add(part);
        }
        return parts.ToArray();
    });

    private static Type? ElementOf(Type list)
    {
        foreach (var i in list.GetInterfaces().Append(list))
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>)) return i.GetGenericArguments()[0];
        return null;
    }

    /// <summary>Whether a pack is taken piece by piece here (else as one text) - for tests.</summary>
    internal static bool InPieces => Parts.Value != null;

    // ── Taking a step ───────────────────────────────────────────────────

    /// <summary>For tests: one core, so a timing is the work's own.</summary>
    internal static bool OneCoreForTests;

    /// <summary>One thing to write out: an entry, a value, or a part of an entry kept in parts.</summary>
    private readonly record struct Job(int Section, int Index, int Part, object? Value, Type Type, JsonSerializerSettings Settings);

    /// <summary>An entry kept in parts, laid out: itself, then each object, a parent before what is under it.</summary>
    private static void Flatten(Splitting split, object entry, int section, int index, List<Job> into, List<int> kids)
    {
        into.Add(new Job(section, index, 0, entry, split.EntryType, split.Apart));
        var top = split.Top(entry);
        kids.Add(top.Count);
        int part = 1;
        void Walk(IList nodes)
        {
            foreach (object? node in nodes)
            {
                into.Add(new Job(section, index, part++, node, split.NodeType, split.Apart));
                var under = split.Under(node!);
                kids.Add(under.Count);
                Walk(under);
            }
        }
        Walk(top);
    }

    private static string Write(Job job)
    {
        if (job.Part < 0) return JsonConvert.SerializeObject(job.Value, job.Type, job.Settings);
        // A part: the entry or the object, without what is kept as parts of their own.
        ApartResolver.Root = job.Value;
        try { return JsonConvert.SerializeObject(job.Value, job.Type, job.Settings); }
        finally { ApartResolver.Root = null; }
    }

    private static string[] Write(List<Job> jobs)
    {
        var texts = new string[jobs.Count];
        if (OneCoreForTests || jobs.Count < 8)
            for (int k = 0; k < jobs.Count; k++) texts[k] = Write(jobs[k]);
        else
            // Side by side: an entry shares nothing with another, and nothing is changing the pack meanwhile -
            // the interface's thread, which is the one that edits it, is here.
            Parallel.For(0, jobs.Count, k => texts[k] = Write(jobs[k]));
        return texts;
    }

    /// <summary>
    /// The pack as it is now. A piece whose text is what <paramref name="like"/> has in the same place is that
    /// very piece - nothing new kept, and <see cref="Diff"/> between the two passes it by at once.
    /// </summary>
    public static PackPieces Take(ModPack pack, PackPieces? like = null, string? language = null)
    {
        var parts = Parts.Value;
        if (parts == null) return WholeLike(PackRepository.Serialize(pack), like, language);

        var settings = PackRepository.ManifestSettings;
        var names = new List<string>();
        var lists = new List<bool>();
        var splits = new List<Splitting?>();
        var jobs = new List<Job>();
        var counts = new List<int>();
        // Of each entry kept in parts: where its jobs start, and how its objects hang.
        var kept = new Dictionary<(int Section, int Index), (int From, List<int> Kids)>();
        foreach (var part in parts)
        {
            var p = part.Property;
            if (p.ShouldSerialize != null && !p.ShouldSerialize(pack)) continue;
            object? value = p.ValueProvider!.GetValue(pack);
            if (value == null) continue;                    // written with nulls left out
            int s = names.Count;
            names.Add(part.Name);
            lists.Add(part.IsList);
            var split = NoSplitsForTests ? null : part.Split;
            splits.Add(split);
            if (part.IsList)
            {
                var list = (IList)value;
                for (int i = 0; i < list.Count; i++)
                {
                    if (split != null && list[i] != null)
                    {
                        var kids = new List<int>();
                        kept[(s, i)] = (jobs.Count, kids);
                        Flatten(split, list[i]!, s, i, jobs, kids);
                    }
                    else jobs.Add(new Job(s, i, -1, list[i], part.ElementType!, settings));
                }
                counts.Add(list.Count);
            }
            else
            {
                jobs.Add(new Job(s, 0, -1, value, p.PropertyType!, settings));
                counts.Add(1);
            }
        }

        var texts = Write(jobs);

        var entries = new PackPiece[names.Count][];
        for (int s = 0; s < entries.Length; s++) entries[s] = new PackPiece[counts[s]];
        // The last step's pieces, by section: the same text in the same place first, then the same text anywhere
        // in the list - one that grew or shrank, and one whose entries changed places (as many as before, each
        // somewhere else: until this looked there too, a list turned round was every entry "changed", and every
        // one of them read and built again to put it back).
        var likeAt = like == null || like.IsWhole ? null : like.Names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.Ordinal);
        var bySize = new Dictionary<int, List<PackPiece>>?[entries.Length];
        for (int k = 0; k < jobs.Count; k++)
        {
            var job = jobs[k];
            int s = job.Section, i = job.Index;
            PackPiece[]? was = likeAt != null && likeAt.TryGetValue(names[s], out int ls) ? like!.Entries[ls] : null;
            if (job.Part < 0)
            {
                string text = texts[k];
                PackPiece? same = null;
                if (was != null)
                {
                    if (i < was.Length && was[i] is not SplitPiece && string.Equals(was[i].Text, text, StringComparison.Ordinal)) same = was[i];
                    else if (lists[s]) same = Find(bySize[s] ??= BySize(was), text);
                }
                entries[s][i] = same ?? new PackPiece(text);
                continue;
            }

            // An entry kept in parts: all its jobs at once.
            var (from, kids) = kept[(s, i)];
            int n = kids.Count;
            entries[s][i] = SplitLike(was, i, texts, from, n, kids, splits[s]!);
            k = from + n - 1;
        }
        return new PackPieces(names.ToArray(), lists.ToArray(), entries, language);
    }

    /// <summary>
    /// An entry in parts, sharing with the last step's: the very entry when every part says what it said
    /// (there, or - the list having changed - anywhere in it), else a new one whose parts are the last step's
    /// wherever they say the same.
    /// </summary>
    private static SplitPiece SplitLike(PackPiece[]? was, int index, string[] texts, int from, int n, List<int> kids, Splitting split)
    {
        bool Says(SplitPiece old)
        {
            if (old.Parts.Length != n) return false;
            for (int k = 0; k < n; k++)
                if (old.Kids[k] != kids[k]) return false;
            for (int k = 0; k < n; k++)
                if (!string.Equals(old.Parts[k].Text, texts[from + k], StringComparison.Ordinal)) return false;
            return true;
        }

        var there = was != null && index < was.Length ? was[index] as SplitPiece : null;
        if (there != null && Says(there)) return there;
        if (was != null)
            foreach (var other in was)
                if (other is SplitPiece old && !ReferenceEquals(old, there) && Says(old)) return old;

        // Part by part: the last step's where it says the same - there, or (an object added, taken out or moved
        // puts every one after it somewhere else) anywhere in the entry.
        var parts = new PackPiece[n];
        Dictionary<int, List<PackPiece>>? elsewhere = null;
        for (int k = 0; k < n; k++)
        {
            string text = texts[from + k];
            PackPiece? same = null;
            if (there != null)
            {
                if (k < there.Parts.Length && string.Equals(there.Parts[k].Text, text, StringComparison.Ordinal)) same = there.Parts[k];
                else same = Find(elsewhere ??= BySize(there.Parts), text);
            }
            parts[k] = same ?? new PackPiece(text);
        }
        return new SplitPiece(parts, kids.ToArray(), split);
    }

    /// <summary>A list's pieces by how long their text is: finding the one that says a given text then compares
    /// only those as long as it, and hashes nothing (an entry can be megabytes).</summary>
    private static Dictionary<int, List<PackPiece>> BySize(PackPiece[] pieces)
    {
        var map = new Dictionary<int, List<PackPiece>>(pieces.Length);
        foreach (var piece in pieces)
        {
            if (piece is SplitPiece) continue;
            int size = piece.Length;
            if (!map.TryGetValue(size, out var of)) map[size] = of = new List<PackPiece>(1);
            of.Add(piece);
        }
        return map;
    }

    private static PackPiece? Find(Dictionary<int, List<PackPiece>> bySize, string text)
    {
        if (!bySize.TryGetValue(text.Length, out var of)) return null;
        foreach (var piece in of)
            if (string.Equals(piece.Text, text, StringComparison.Ordinal)) return piece;
        return null;
    }

    /// <summary>A step that is one text (see <see cref="Whole"/>), the very piece <paramref name="like"/> has when
    /// the text is the same.</summary>
    public static PackPieces WholeLike(string text, PackPieces? like, string? language = null)
    {
        if (like != null && like.IsWhole && string.Equals(like.Entries[0][0].Text, text, StringComparison.Ordinal))
            return new PackPieces(like.Names, like.Lists, like.Entries, language);
        return Whole(text, language);
    }

    // ── Comparing ───────────────────────────────────────────────────────

    /// <summary>Whether two steps are the same pack, shown in the same language's words.</summary>
    public bool SameAs(PackPieces other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (!string.Equals(Language, other.Language, StringComparison.Ordinal)) return false;
        if (Names.Length != other.Names.Length) return false;
        for (int s = 0; s < Names.Length; s++)
        {
            if (Names[s] != other.Names[s] || Lists[s] != other.Lists[s] || Entries[s].Length != other.Entries[s].Length) return false;
            for (int i = 0; i < Entries[s].Length; i++)
                if (!Entries[s][i].SameTextAs(other.Entries[s][i])) return false;
        }
        return true;
    }

    /// <summary>One section that differs between two steps.</summary>
    public sealed class SectionChange
    {
        public string Name = "";
        public bool IsList;
        /// <summary>Its pieces before and after; empty where the section is not written at all.</summary>
        public PackPiece[] From = Array.Empty<PackPiece>(), To = Array.Empty<PackPiece>();
        /// <summary>The entries that differ, when the list has as many as before and so each is where it was;
        /// null when entries were added, removed or moved.</summary>
        public List<int>? Changed;
    }

    /// <summary>Where two steps differ, section by section; none for the same pack.</summary>
    public static List<SectionChange> Diff(PackPieces from, PackPieces to)
    {
        var changes = new List<SectionChange>();
        if (from.IsWhole || to.IsWhole)
        {
            if (!(from.IsWhole && to.IsWhole && from.Entries[0][0].SameTextAs(to.Entries[0][0])))
                changes.Add(new SectionChange { Name = WholeName, From = from.IsWhole ? from.Entries[0] : Array.Empty<PackPiece>(),
                                                To = to.IsWhole ? to.Entries[0] : Array.Empty<PackPiece>() });
            return changes;
        }
        var fromAt = from.Names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int s = 0; s < to.Names.Length; s++)
        {
            string name = to.Names[s];
            seen.Add(name);
            if (!fromAt.TryGetValue(name, out int f))
            {
                changes.Add(new SectionChange { Name = name, IsList = to.Lists[s], To = to.Entries[s] });
                continue;
            }
            var a = from.Entries[f];
            var b = to.Entries[s];
            if (a.Length != b.Length || from.Lists[f] != to.Lists[s])
            {
                changes.Add(new SectionChange { Name = name, IsList = to.Lists[s], From = a, To = b });
                continue;
            }
            List<int>? changed = null;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].SameTextAs(b[i])) (changed ??= new()).Add(i);
            if (changed == null) continue;
            // As many as before, but one of them is where another was: the list was put in another order (an
            // entry moved up, the list sorted), which is entries kept and placed again - not entries changed.
            if (to.Lists[s] && changed.Count > 1 && AnyMoved(a, b, changed)) changed = null;
            changes.Add(new SectionChange { Name = name, IsList = to.Lists[s], From = a, To = b, Changed = changed });
        }
        for (int f = 0; f < from.Names.Length; f++)
            if (!seen.Contains(from.Names[f]))
                changes.Add(new SectionChange { Name = from.Names[f], IsList = from.Lists[f], From = from.Entries[f] });
        return changes;
    }

    /// <summary>Whether any of the entries that differ (<paramref name="changed"/>) is, after, an entry that was
    /// at another of those places before.</summary>
    private static bool AnyMoved(PackPiece[] before, PackPiece[] after, List<int> changed)
    {
        var left = new HashSet<PackPiece>(ReferenceEqualityComparer.Instance);
        foreach (int i in changed) left.Add(before[i]);
        foreach (int i in changed)
            if (left.Contains(after[i])) return true;
        // Two steps not taken one like the other share no piece: by what they say, then.
        foreach (int i in changed)
            foreach (int j in changed)
                if (i != j && SaysTheSame(before[j], after[i])) return true;
        return false;
    }

    /// <summary>Two pieces that say the same, asked the cheap way first: the very piece, then how long each is.</summary>
    internal static bool SaysTheSame(PackPiece a, PackPiece b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is SplitPiece || b is SplitPiece) return a.SameTextAs(b);
        return a.Length == b.Length && a.SameTextAs(b);
    }

    // ── Holding a restore to a step ─────────────────────────────────────

    /// <summary>
    /// Whether one section of the pack, written out now, is what this step has for it - all of it, or the
    /// entries at <paramref name="indexes"/> (a list of as many entries as the step's either way). For a
    /// restore that put back some entries to know it did, without writing out the rest of the pack to find it
    /// as it was: on a large pack that was as long again as the restore itself.
    /// </summary>
    public bool Holds(ModPack pack, string section, IReadOnlyList<int>? indexes = null)
    {
        var parts = Parts.Value;
        if (parts == null || IsWhole) return false;
        var part = Array.Find(parts, p => p.Name == section);
        if (part == null) return false;
        int s = Array.IndexOf(Names, section);
        var settings = PackRepository.ManifestSettings;
        var property = part.Property;
        object? value = property.ShouldSerialize != null && !property.ShouldSerialize(pack) ? null : property.ValueProvider!.GetValue(pack);
        if (value == null) return s < 0;
        if (s < 0) return false;
        var mine = Entries[s];
        if (!part.IsList)
            return string.Equals(JsonConvert.SerializeObject(value, property.PropertyType!, settings), mine[0].Text, StringComparison.Ordinal);

        var list = (IList)value;
        if (list.Count != mine.Length) return false;
        var which = indexes ?? Enumerable.Range(0, list.Count).ToList();
        var jobs = new List<Job>();
        var kept = new List<(int Index, int From, List<int> Kids)>();
        foreach (int i in which)
        {
            if (i < 0 || i >= list.Count) return false;
            if (mine[i] is SplitPiece && part.Split != null && list[i] != null)
            {
                var kids = new List<int>();
                kept.Add((i, jobs.Count, kids));
                Flatten(part.Split, list[i]!, s, i, jobs, kids);
            }
            else jobs.Add(new Job(s, i, -1, list[i], part.ElementType!, settings));
        }
        var texts = Write(jobs);
        foreach (var (index, from, kids) in kept)
        {
            var split = (SplitPiece)mine[index];
            if (split.Parts.Length != kids.Count) return false;
            for (int k = 0; k < kids.Count; k++)
                if (split.Kids[k] != kids[k] || !string.Equals(split.Parts[k].Text, texts[from + k], StringComparison.Ordinal)) return false;
        }
        for (int k = 0; k < jobs.Count; k++)
            if (jobs[k].Part < 0 && !string.Equals(mine[jobs[k].Index].Text, texts[k], StringComparison.Ordinal)) return false;
        return true;
    }

    // ── Reading a step back ─────────────────────────────────────────────

    /// <summary>The step as one text, which <see cref="PackRepository.Deserialize"/> reads as the pack it was.</summary>
    public string ToJson()
    {
        if (IsWhole) return Entries[0][0].Text;
        int length = 16;
        foreach (var section in Entries)
            foreach (var piece in section) length += piece.Text.Length + 8;
        var sb = new StringBuilder(length + Names.Length * 48);
        sb.Append("{\n");
        for (int s = 0; s < Names.Length; s++)
        {
            sb.Append("  ").Append(JsonConvert.ToString(Names[s])).Append(": ");
            if (Lists[s])
            {
                sb.Append("[\n");
                for (int i = 0; i < Entries[s].Length; i++)
                {
                    sb.Append(Entries[s][i].Text);
                    if (i < Entries[s].Length - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append("  ]");
            }
            else sb.Append(Entries[s][0].Text);
            if (s < Names.Length - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append('}');
        // The whole texts of the entries kept in parts were made for this, and are not kept beside the parts.
        foreach (var section in Entries)
            foreach (var piece in section)
                (piece as SplitPiece)?.ForgetWhole();
        return sb.ToString();
    }

    private static object? Read(PackPiece piece, Part part, JsonSerializerSettings settings)
        => piece is SplitPiece split ? split.Build() : JsonConvert.DeserializeObject(piece.Text, part.ElementType!, settings);

    /// <summary>
    /// Put <paramref name="changes"/> (from the step the pack is at, to another) into the pack itself: each
    /// changed entry read from its piece and put where the old one was, a list that grew, shrank or was reordered
    /// made again from the entries it keeps (the very objects, where their text is the same) and those it gains,
    /// any other value read and set. Nothing else of the pack is touched - every other entry stays the object it
    /// was, so whatever shows it need not be built again.
    /// <para/>
    /// False, with nothing changed, when a change is one this cannot put back (the caller then reads the whole
    /// step, as before); the entries replaced, by section, in <paramref name="replaced"/>.
    /// </summary>
    public static bool Apply(ModPack pack, List<SectionChange> changes, out Dictionary<string, List<int>?> replaced)
    {
        replaced = new Dictionary<string, List<int>?>(StringComparer.Ordinal);
        var parts = Parts.Value;
        if (parts == null) return false;
        var settings = PackRepository.ManifestSettings;
        var todo = new List<Action>();
        try
        {
            foreach (var change in changes)
            {
                var part = Array.Find(parts, p => p.Name == change.Name);
                if (part == null) return false;
                var p = part.Property;
                if (part.IsList)
                {
                    if (p.ValueProvider!.GetValue(pack) is not IList list) return false;
                    if (list.Count != change.From.Length) return false;      // the pack is not at the step it is said to be at
                    if (change.Changed != null)
                    {
                        // As many as before: each changed one read and put in its place.
                        var read = change.Changed.Select(i => (i, Read(change.To[i], part, settings))).ToList();
                        todo.Add(() => { foreach (var (i, entry) in read) list[i] = entry; });
                        replaced[change.Name] = change.Changed;
                    }
                    else
                    {
                        // Another number, or another order: the entries kept are the objects they were - found by
                        // the piece itself (two steps taken one like the other share it), else by what it says.
                        var kept = new Dictionary<PackPiece, Queue<int>>(ReferenceEqualityComparer.Instance);
                        for (int i = 0; i < change.From.Length; i++)
                        {
                            if (!kept.TryGetValue(change.From[i], out var q)) kept[change.From[i]] = q = new Queue<int>();
                            q.Enqueue(i);
                        }
                        var used = new bool[change.From.Length];
                        var now = new object?[change.To.Length];
                        var unplaced = new List<int>();
                        for (int t = 0; t < change.To.Length; t++)
                        {
                            if (kept.TryGetValue(change.To[t], out var q) && q.Count > 0)
                            {
                                int i = q.Dequeue();
                                used[i] = true;
                                now[t] = list[i];
                            }
                            else unplaced.Add(t);
                        }
                        foreach (int t in unplaced)
                        {
                            int found = -1;
                            for (int i = 0; i < change.From.Length && found < 0; i++)
                                if (!used[i] && SaysTheSame(change.From[i], change.To[t])) found = i;
                            if (found >= 0) { used[found] = true; now[t] = list[found]; }
                            else now[t] = Read(change.To[t], part, settings);
                        }
                        todo.Add(() => { list.Clear(); foreach (var entry in now) list.Add(entry); });
                        replaced[change.Name] = null;
                    }
                }
                else
                {
                    if (!p.Writable) return false;
                    object? value = change.To.Length == 0 ? null : JsonConvert.DeserializeObject(change.To[0].Text, p.PropertyType!, settings);
                    if (value == null && p.PropertyType!.IsValueType) return false;
                    todo.Add(() => p.ValueProvider!.SetValue(pack, value));
                    replaced[change.Name] = null;
                }
            }
        }
        catch (JsonException) { return false; }
        // Everything read: now, and only now, the pack changes.
        foreach (var act in todo) act();
        return true;
    }
}
