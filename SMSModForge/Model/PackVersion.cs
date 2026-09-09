using System;
using System.Globalization;

namespace SMSModForge.Model;

/// <summary>
/// A pack's own version — the number a player sees beside its name.
/// <para/>
/// Three parts, and which one moves says what happened:
/// <list type="bullet">
///   <item><b>Major</b> — only ever by hand. "This is a new thing now" is a
///   judgement about the work, not about the diff, and no amount of counting
///   records can make it.</item>
///   <item><b>Minor</b> — something new. A record the pack did not have
///   before, including the first change to one of the game's own: from a
///   player's side a vanilla conversation the pack now alters IS a new thing
///   the pack does.</item>
///   <item><b>Patch</b> — a change to something the pack already had.</item>
/// </list>
/// <para/>
/// Deliberately not SemVer. This numbers content for a player choosing whether
/// to re-download, not an API for a compiler, and "did it grow or did it get
/// fixed" is the question they are actually asking.
/// </summary>
public readonly struct PackVersion : IEquatable<PackVersion>, IComparable<PackVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public PackVersion(int major, int minor, int patch)
    {
        Major = major < 0 ? 0 : major;
        Minor = minor < 0 ? 0 : minor;
        Patch = patch < 0 ? 0 : patch;
    }

    /// <summary>What a pack created today starts at: nothing has been released
    /// and nothing has been added.</summary>
    public static readonly PackVersion New = new(0, 0, 0);

    /// <summary>
    /// What a pack written before versioning existed becomes.
    /// <para/>
    /// 0.1.0 rather than 0.0.0 on purpose: those packs have content in them,
    /// often a great deal, and calling that "nothing yet" would be wrong the
    /// moment an author looked at it.
    /// </summary>
    public static readonly PackVersion Existing = new(0, 1, 0);

    /// <summary>
    /// Read a version, or null when the text is not one.
    /// <para/>
    /// Strict about shape — three whole numbers separated by dots — because a
    /// version that quietly parses as something else is worse than one that
    /// refuses: the author would see a number they did not type.
    /// </summary>
    public static PackVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string[] parts = text!.Trim().Split('.');
        if (parts.Length != 3) return null;

        var made = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture,
                              out made[i]))
                return null;
        }
        return new PackVersion(made[0], made[1], made[2]);
    }

    /// <summary>Read a version, falling back to <paramref name="whenMissing"/>.</summary>
    public static PackVersion Read(string? text, PackVersion whenMissing)
        => Parse(text) ?? whenMissing;

    /// <summary>A release the author declares. Everything below resets, because
    /// 2.0.3 would claim three fixes that never happened to a 2.0 nobody
    /// shipped.</summary>
    public PackVersion NextMajor() => new(Major + 1, 0, 0);

    /// <summary>Something new arrived.</summary>
    public PackVersion NextMinor() => new(Major, Minor + 1, 0);

    /// <summary>Something that was already there changed.</summary>
    public PackVersion NextPatch() => new(Major, Minor, Patch + 1);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    public bool Equals(PackVersion other)
        => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => obj is PackVersion other && Equals(other);

    public override int GetHashCode() => (Major, Minor, Patch).GetHashCode();

    public int CompareTo(PackVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator ==(PackVersion a, PackVersion b) => a.Equals(b);
    public static bool operator !=(PackVersion a, PackVersion b) => !a.Equals(b);
    public static bool operator <(PackVersion a, PackVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(PackVersion a, PackVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(PackVersion a, PackVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(PackVersion a, PackVersion b) => a.CompareTo(b) >= 0;
}
