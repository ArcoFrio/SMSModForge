using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Pack I/O. The on-disk format is one folder per pack:
/// <code>
/// &lt;PackRoot&gt;\
///   modpack.json              ← new manifest (was bustpack.json)
///   Sprites\&lt;Character&gt;\*.PNG
///   Locations\&lt;PlaceKey&gt;*.PNG
///   Particles\*.json
/// </code>
/// Older packs that still use <c>bustpack.json</c> are accepted on Load
/// (backward-compat) and rewritten as <c>modpack.json</c> on the next Save.
/// </summary>
public static class PackRepository
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public const string ManifestFileName = "modpack.json";
    public const string LegacyManifestFileName = "bustpack.json";

    /// <summary>
    /// Resolves a pack folder to its manifest path. Prefers the new
    /// <c>modpack.json</c>; falls back to <c>bustpack.json</c> for packs
    /// written by the SMSBustForge era. Returns <c>null</c> if neither exists.
    /// </summary>
    public static string? ResolveManifestPath(string packRoot)
    {
        var preferred = Path.Combine(packRoot, ManifestFileName);
        if (File.Exists(preferred)) return preferred;
        var legacy = Path.Combine(packRoot, LegacyManifestFileName);
        if (File.Exists(legacy)) return legacy;
        return null;
    }

    /// <summary>Loads a pack from <paramref name="packRoot"/>. Throws if no manifest is found.</summary>
    public static ModPack Load(string packRoot)
    {
        var manifest = ResolveManifestPath(packRoot)
            ?? throw new FileNotFoundException(
                $"No {ManifestFileName} (or legacy {LegacyManifestFileName}) in {packRoot}",
                Path.Combine(packRoot, ManifestFileName));

        var json = File.ReadAllText(manifest);
        var pack = JsonConvert.DeserializeObject<ModPack>(json, JsonSettings)
                   ?? throw new InvalidDataException($"Empty or invalid manifest: {manifest}");
        // Legacy actors fold into characters on the way in. In memory only —
        // the file is untouched until the author saves.
        CharacterMerge.Apply(pack);
        return pack;
    }

    /// <summary>
    /// Writes the manifest to <c>modpack.json</c>. PNGs are managed by the
    /// editor elsewhere — this only persists the JSON so save is fast and
    /// atomic. If a legacy <c>bustpack.json</c> sits next to it, it is removed
    /// so the loader only sees one manifest.
    /// </summary>
    public static void Save(ModPack pack, string packRoot)
    {
        // Stamp the game version this editor targets — every saved pack
        // records what it was authored against, and the runtime banner flags
        // packs whose stamp doesn't match the running game.
        pack.GameVersion = ModPack.CurrentGameVersion;

        Directory.CreateDirectory(packRoot);
        var manifest = Path.Combine(packRoot, ManifestFileName);

        // Vanilla extensions are authored against the real level hierarchy, so
        // most of their nodes just mirror what the game already has. Reduce
        // them to the actual delta for the write — swapped in around the
        // serialize and restored after, so what's on screen is never rewritten.
        // Deliberately NOT in Serialize(): that also backs undo snapshots, and
        // pruning there would drop bound nodes on undo.
        string json;
        using (GameObjectDef.SaveScope())
        {
            var restore = VanillaDelta.PrepareForSave(pack);
            try { json = JsonConvert.SerializeObject(pack, JsonSettings); }
            finally { restore(); }
        }
        // Atomic-ish write: write to temp, then move.
        var tmp = manifest + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(manifest)) File.Delete(manifest);
        File.Move(tmp, manifest);

        // Clean up legacy file so the loader doesn't see two manifests.
        var legacy = Path.Combine(packRoot, LegacyManifestFileName);
        if (File.Exists(legacy)) File.Delete(legacy);
    }

    /// <summary>
    /// Serialize a pack to the same JSON the on-disk manifest uses (identical
    /// settings). Used by the editor to snapshot saved state for unsaved-change
    /// detection, so the comparison is apples-to-apples with <see cref="Save"/>.
    /// </summary>
    public static string Serialize(ModPack pack) => JsonConvert.SerializeObject(pack, JsonSettings);

    /// <summary>
    /// Exactly what <see cref="Save"/> would write — i.e. with vanilla-extension
    /// deltas reduced. Use this for "does the file differ?" comparisons: the
    /// editor fills every extension's tree in from the vanilla catalog just to
    /// make it editable, and those mirror nodes are dropped on save, so
    /// comparing the RAW form would report unsaved changes for merely opening a
    /// pack. <see cref="Serialize"/> stays lossless for undo snapshots.
    /// </summary>
    public static string SerializeAsSaved(ModPack pack)
    {
        using (GameObjectDef.SaveScope())
        {
            var restore = VanillaDelta.PrepareForSave(pack);
            try { return JsonConvert.SerializeObject(pack, JsonSettings); }
            finally { restore(); }
        }
    }

    /// <summary>Inverse of <see cref="Serialize"/> — rebuilds a pack from an
    /// in-memory snapshot (used by undo/redo). Returns null on malformed JSON.</summary>
    public static ModPack? Deserialize(string json)
    {
        var pack = JsonConvert.DeserializeObject<ModPack>(json, JsonSettings);
        if (pack != null) CharacterMerge.Apply(pack);
        return pack;
    }

    /// <summary>
    /// A new pack, with nothing in it.
    /// <para/>
    /// Deliberately empty. It used to arrive with a placeholder character
    /// pointing at sprite paths that did not exist in the author's folder, so
    /// a brand-new pack failed its own validation and the first thing anyone
    /// had to do was work out whether the errors were theirs. An empty pack
    /// says what it is.
    /// </summary>
    public static ModPack CreateEmpty(string packId)
    {
        var pack = new ModPack { PackId = packId };
        // The player is built in and shared by every pack, so a NEW pack has
        // one too. It used to appear only after a save and a reload, because
        // EnsurePlayer ran on Load and nowhere else -- so the one speaker an
        // author is most likely to want first was missing for exactly as long
        // as they had not saved yet.
        CharacterMerge.EnsurePlayer(pack);
        return pack;
    }

    // ── Active pack tracking for cross-VM lookups ────────────────────────

    /// <summary>Thread-local active pack set by MainViewModel when a pack is loaded.
    /// Used by cross-VM helpers (e.g. boolean variable detection in param rows).</summary>
    internal static ModPack? ActivePack { get; set; }

    /// <summary>Check whether a variable name is boolean in the active pack.
    /// Returns false if no pack is active or the variable isn't found.</summary>
    public static bool IsVariableBoolean(string varName)
    {
        if (string.IsNullOrWhiteSpace(varName)) return false;
        var pack = ActivePack;
        if (pack == null) return false;
        return pack.Variables.Any(v => string.Equals(v.Name, varName, System.StringComparison.OrdinalIgnoreCase)
                                        && v.Type == PackVariableType.Bool);
    }
}
