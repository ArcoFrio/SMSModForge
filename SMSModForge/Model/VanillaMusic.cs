using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// The game's own music objects: every direct child of <c>12_AudioPlayer</c>.
/// <para/>
/// A button's Music field and the <c>SwitchMusic</c> action both resolve a name
/// against that object's children, so the game's tracks and a pack's own sit in
/// one namespace and either can be named. Without this list an author had to
/// know the spelling of a vanilla track to use one, which meant reading a
/// hierarchy dump.
/// <para/>
/// Read out of <c>InitialHierarchy_CoreGameScene_1.8C.txt</c>. That is a dump of
/// 1.8C and the editor targets 1.8E, so a track added since will be missing:
/// the fields stay editable for exactly that reason, and nothing validates
/// against this list. It is an offer, not a rule.
/// <para/>
/// DIRECT children only. <c>Transform.Find</c> takes a path, so the two nested
/// sources — <c>CityNoises/Bakery</c> and <c>Mall/Mall (1)</c> — are reachable
/// by typing that path, but they are not tracks anybody switches to and are
/// left out of the list.
/// </summary>
public static class VanillaMusic
{
    /// <summary>The game's ordinary music, and what a button leaving a themed
    /// area wants to switch back to. It is the only one of these objects the
    /// dump shows as active when the scene loads — the other thirty-seven all
    /// start off — which is what makes it the resting state rather than one
    /// theme among many.</summary>
    public const string Default = "Music";

    /// <summary>In hierarchy order, the order the dump lists them, with
    /// <see cref="Default"/> first.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Music",
        "ChurchMusic",
        "GabrielMusic",
        "SamanthaLofi",
        "evelynlabmusic",
        "LibraryLofi",
        "CityNoises",
        "Shower",
        "ShowerQuiet",
        "Driving",
        "DrivingQuiet",
        "Mall",
        "ClubMusic",
        "RoyalMusic",
        "Suburban",
        "Beach",
        "GymMusic",
        "Romantic",
        "scary",
        "Hiking",
        "fantasy",
        "Scifi",
        "forestchinese",
        "CelesteAudio",
        "JapanLofi",
        "TropicalLofi",
        "WinterLofi",
        "News",
        "Badlands",
        "Mafia",
        "KenMusic",
        "HalloweenMusic",
        "DemonicMusic",
        "SadDemonMusic",
        "CountrySideMusic",
        "HarborMusic",
        "CyberMusic",
        "CasinoMusic",
    };

    private static readonly HashSet<string> Lookup =
        new(All, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a name belongs to the game rather than to a pack.
    /// <para/>
    /// Case-insensitive: the game's own names are not consistently cased
    /// (evelynlabmusic, scary, fantasy), so a name retyped with the wrong case
    /// should still be filed under the game's heading rather than look like
    /// something the author wrote. Whether the game would then FIND it is a
    /// different question, and not one this list answers.</summary>
    public static bool Contains(string? name) =>
        !string.IsNullOrEmpty(name) && Lookup.Contains(name!);
}
