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
                     Ui = 10, Quests = 11, Variables = 12, Integration = 13;
}
