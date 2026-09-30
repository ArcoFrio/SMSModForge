using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Paths into the game's own scene worth knowing, for the Direct Path
/// category's list (the author, 1.6.3): the objects a pack most often switches
/// on or off, grouped by what they are.
/// <para/>
/// Chosen from the game itself, not guessed: the objects its own dialogues
/// switch most often (the extracted dialogues, 1.8E - "Disable_All_CG" alone
/// 452 times), and the ones a modder reaches for that the other categories do
/// not already cover - a bust is the Bust category's, a whole place Places',
/// an object of a place the pack extends GameObjects'. Every path is exactly
/// as the game's hierarchy has it, slashes and spelling included
/// ("Raise_Stat_Ahtletic" is the game's), since a Direct Path is looked up
/// as written.
/// <para/>
/// Examples, not a catalogue: the box still takes any path typed into it.
/// </summary>
public static class GamePathExamples
{
    /// <summary>One example: the path, the text key of the heading it is
    /// listed under, and the key of the short note beside it saying what it
    /// is, with an example of using it.</summary>
    public sealed record Entry(string Path, string GroupKey, string NoteKey);

    // Headings.
    public const string GameActions = "directPath.group.gameActions";
    public const string Stats = "directPath.group.stats";
    public const string Interface = "directPath.group.interface";
    public const string Effects = "directPath.group.effects";
    public const string Sound = "directPath.group.sound";

    public static IReadOnlyList<Entry> All { get; } = new Entry[]
    {
        // Objects that make the game do something when they are switched on.
        new("Disable_All_CG", GameActions, "directPath.note.disableAllCg"),
        new("Disable_All_chars", GameActions, "directPath.note.disableAllChars"),
        new("Disable_All_Maps", GameActions, "directPath.note.disableAllMaps"),
        new("Return_Home", GameActions, "directPath.note.returnHome"),
        new("Return_Home_Vacation", GameActions, "directPath.note.returnHomeVacation"),

        new("10_Gameplay/Raise_Stats/Raise_Stat_Ahtletic", Stats, "directPath.note.raiseAthletic"),
        new("10_Gameplay/Raise_Stats/Raise_Stat_Charm", Stats, "directPath.note.raiseCharm"),
        new("10_Gameplay/Raise_Stats/Raise_Stat_Creative", Stats, "directPath.note.raiseCreative"),
        new("10_Gameplay/Raise_Stats/Raise_Stat_Lewd", Stats, "directPath.note.raiseLewd"),
        new("10_Gameplay/Raise_Stats/Raise_Stat_Smart", Stats, "directPath.note.raiseSmart"),

        new("9_MainCanvas", Interface, "directPath.note.mainCanvas"),
        new("9_MainCanvas/Navigator", Interface, "directPath.note.navigator"),
        new("9_MainCanvas/OtherHud", Interface, "directPath.note.otherHud"),
        new("9_MainCanvas/TalkButton", Interface, "directPath.note.talkButton"),
        new("9_MainCanvas/ShopCore", Interface, "directPath.note.shop"),
        new("9_MainCanvas/Inventory", Interface, "directPath.note.inventory"),
        new("9_MainCanvas/Calendar", Interface, "directPath.note.calendar"),
        new("9_MainCanvas/Starmaker", Interface, "directPath.note.starmaker"),

        new("6_Effects/Effect_Canvas/AFewMinutesLater", Effects, "directPath.note.aFewMinutesLater"),
        new("6_Effects/Effect_Canvas/AWhileLater", Effects, "directPath.note.aWhileLater"),
        new("6_Effects/Effect_Canvas/MeanwhileSomewhereElse", Effects, "directPath.note.meanwhile"),
        new("6_Effects/Effect_Canvas/WhiteFlashOnEnable", Effects, "directPath.note.whiteFlash"),
        new("6_Effects/CFXR2 Expression Loving (Timed)", Effects, "directPath.note.hearts"),   // English on purpose: the game's object name.
        new("6_Effects/CFXR2 Expression Sparkles (Timed, Alt)", Effects, "directPath.note.sparkles"),   // English on purpose: the game's object name.

        new("12_AudioPlayer/Music", Sound, "directPath.note.music"),
        new("12_AudioPlayer/CityNoises", Sound, "directPath.note.cityNoises"),
        new("12_AudioPlayer/Shower", Sound, "directPath.note.shower"),
        new("12_AudioPlayer/Driving", Sound, "directPath.note.driving"),
        new("12_AudioPlayer/Romantic", Sound, "directPath.note.romantic"),
        new("12_AudioPlayer/scary", Sound, "directPath.note.scary"),
        new("12_AudioPlayer/ClubMusic", Sound, "directPath.note.clubMusic"),
        new("12_AudioPlayer/HalloweenMusic", Sound, "directPath.note.halloweenMusic"),
    };
}
