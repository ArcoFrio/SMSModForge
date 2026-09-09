using System.Text;

namespace SMSModForge.View;

/// <summary>
/// What the "choose a file" dialogs offer.
/// <para/>
/// Built from <see cref="Shared.MediaKinds"/> rather than written out, because
/// the two drifting apart is a real failure and it happened: scenes learned to
/// accept GIFs and videos, the picker went on offering PNGs only, and the only
/// way to choose one's own animation was to know to switch the dropdown to "All
/// files". A list of extensions that decides what the tool accepts should also
/// decide what it lets an author pick.
/// <para/>
/// Every filter ends with "All files", so nothing here can stop somebody
/// choosing a file the tool has not heard of. That is deliberate — the picker
/// is a convenience, and validation is what actually decides.
/// </summary>
public static class PickerFilters
{
    /// <summary>Art that is one picture: everywhere except a scene.</summary>
    public static string StillArt { get; } = Build(
        ("Image files", Shared.MediaKinds.StillExtensions));

    /// <summary>
    /// A scene's art, which may move.
    /// <para/>
    /// Everything first, so the default view shows an author every file they
    /// could pick, then split into stills and animations for a folder holding
    /// both.
    /// </summary>
    public static string SceneArt { get; } = Build(
        ("Scene art", All()),
        ("Still images", Shared.MediaKinds.StillExtensions),
        ("Animated", Animated()));

    /// <summary>The extensions a scene's art may have.</summary>
    private static string[] All() => Join(Shared.MediaKinds.StillExtensions, Animated());

    /// <summary>The extensions that move.</summary>
    private static string[] Animated()
        => Join(Shared.MediaKinds.GifExtensions, Shared.MediaKinds.VideoExtensions);

    private static string[] Join(string[] first, string[] second)
    {
        var made = new string[first.Length + second.Length];
        first.CopyTo(made, 0);
        second.CopyTo(made, first.Length);
        return made;
    }

    /// <summary>
    /// Assemble a dialog filter: groups in order, then "All files".
    /// <para/>
    /// The format is Windows': alternating description and pattern, separated
    /// by pipes.
    /// </summary>
    private static string Build(params (string Name, string[] Extensions)[] groups)
    {
        var made = new StringBuilder();

        foreach (var (name, extensions) in groups)
        {
            var patterns = new StringBuilder();
            foreach (string extension in extensions)
            {
                if (patterns.Length > 0) patterns.Append(';');
                patterns.Append('*').Append(extension);
            }

            // The description repeats the patterns because the dialog shows
            // only the description, and an author choosing between "Scene art"
            // and "Still images" should not have to guess what is in each.
            made.Append(name).Append(" (").Append(patterns).Append(")|")
                .Append(patterns).Append('|');
        }

        return made.Append("All files (*.*)|*.*").ToString();
    }
}
