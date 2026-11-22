namespace SMSModForge.ViewModel;

/// <summary>
/// Where each main tab stands, first to last - left to right in Normal, top
/// to bottom in EX's rail. The one list of it: MainWindow.xaml's TabItems
/// must come in this order (MainTabOrderTests holds the window to it), and
/// everything that picks a tab by its place - the per-tab commands, the
/// issues list going to a record, the tutorials, the tests - reads it here
/// rather than keeping a number of its own.
/// <para/>
/// 1.8.0: Variables and Integration moved to the end, after UI and Quests
/// (the author: "considering they affect everything else").
/// </summary>
public static class MainTab
{
    public const int ModForge = 0, Characters = 1, Npcs = 2, Places = 3, MapButtons = 4,
                     Dialogues = 5, Scenes = 6, Music = 7, Sfx = 8, Wallpapers = 9,
                     Fx = 10, Ui = 11, Quests = 12, Variables = 13, Integration = 14;

    /// <summary>Each tab's name, by where it stands: the key of its header in the English file (MainTabOrderTests
    /// holds it to the window's). For text that names a tab, as a tutorial's card does (1.10.0).</summary>
    public static readonly string[] HeaderKeys =
    {
        "home.modforge", "characters.characters", "npcs.npcs", "places.places", "mapButtons.mapButtons",
        "dialogues.dialogues", "scenes.scenes", "music.music", "sfx.sfx", "wallpapers.wallpapers",
        "fx.fx", "ui.ui", "quests.quests", "variables.variables", "integration.integration",
    };

    public static string Name(int tab) => tab >= 0 && tab < HeaderKeys.Length ? Localization.Loc.T(HeaderKeys[tab]) : "";
}
