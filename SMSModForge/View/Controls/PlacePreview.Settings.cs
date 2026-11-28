using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SMSModForge.Services;

namespace SMSModForge.View.Controls;

/// <summary>
/// The picked object's or character's own settings, beside the picture (after 1.10.0, the author: "all of the
/// settings that already appear for GOs on their records in the bottom to also appear on the selected GO window
/// on the right, for quick editing", and "Same thing for NPCs too").
/// <para/>
/// Not a copy of them: the record's own template, found as the record below finds it, told it stands in a narrow
/// column (<see cref="Narrow"/>). So a setting added to the record is here too, what is typed here is the record's,
/// and it is in the next undo step as anything typed anywhere is. Drawn in the panel's dark
/// (<see cref="ThemeManager.DarkOf"/>) whatever the editor's theme: the panel's own words are light, always.
/// </summary>
public sealed partial class PlacePreview
{
    private ContentControl _settings = new();

    /// <summary>How wide the settings are laid out: the record's 110 for its words and room for a box beside
    /// them, a condition's type and its buttons among them. The panel beside the picture is kept this wide
    /// whether or not anything is picked, so picking something does not move the picture.</summary>
    private const double SettingsWidth = 300;

    private ContentControl BuildSettings()
    {
        _settings = new ContentControl
        {
            Focusable = false,
            IsTabStop = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 4),
            Visibility = Visibility.Collapsed,
        };
        Narrow.SetIs(_settings, true);
        PaintSettings();
        Loaded += (_, _) => { ThemeManager.Changed -= PaintSettings; ThemeManager.Changed += PaintSettings; PaintSettings(); };
        Unloaded += (_, _) => ThemeManager.Changed -= PaintSettings;
        return _settings;
    }

    /// <summary>The theme's colours as they are on the panel's dark, for the template's controls to find first;
    /// and tick boxes whose words wrap, as nothing else in the record's template needs to at its own width.</summary>
    private void PaintSettings()
    {
        var resources = new ResourceDictionary();
        ThemeManager.PutColours(resources, ThemeManager.DarkOf(ThemeManager.Current));
        resources[typeof(CheckBox)] = WrappingTicks();
        _settings.Resources = resources;
    }

    /// <summary>The editor's own tick box, its words on as many lines as the panel needs: "This GameObject already
    /// exists (bind, don't create)" ran a hundred pixels past the panel's edge on one.</summary>
    private static Style WrappingTicks()
    {
        var words = new FrameworkElementFactory(typeof(TextBlock));
        words.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        words.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        var style = new Style(typeof(CheckBox), Application.Current?.TryFindResource(typeof(CheckBox)) as Style);
        style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = words }));
        return style;
    }

    /// <summary>What is picked, shown; nothing when nothing is. The same view model again builds nothing again,
    /// so a box being typed in keeps the keyboard while the picture is drawn again around it.</summary>
    private void UpdateSettings()
    {
        object? picked = (object?)_selPlacement ?? _selNode;
        if (!ReferenceEquals(_settings.Content, picked)) _settings.Content = picked;
        _settings.Visibility = picked != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Where in the tree the picked object or character was when it went out of the place: removed, or
    /// taken out by an undo. Forgotten at the next pick or unpick.</summary>
    private string? _lostNodePath, _lostPlacementPath;

    /// <summary>The objects shown when the pick was made: a pick that goes with them (another place shown,
    /// another pack opened) is not kept to be picked again.</summary>
    private System.Collections.IEnumerable? _pickedFrom;

    /// <summary>An undo or a redo is being put back (from <see cref="UndoBegins"/> until the page has settled
    /// after it): the place's views may be made again, and what was picked goes with them only for a moment.</summary>
    private bool _undoing;

    /// <summary>An undo or a redo is about to be put back (the window calls it).</summary>
    internal void UndoBegins() => _undoing = true;

    /// <summary>
    /// After an undo or a redo (the window calls it): what was picked when it went out of the place, picked
    /// again if the step put it back where it was. With its settings beside the picture, its "−" is under the
    /// hand of whoever is editing it, and Ctrl+Z gave the object back unpicked, the panel gone from under them
    /// (found by the walk over every control, after 1.10.0). A step that does not put it back leaves it
    /// waiting for one that does.
    /// </summary>
    internal void PickAgainAfterUndo()
    {
        // Once everything the step started has run - the verdict on a pick it took away among it.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new System.Action(() => _undoing = false));
        if (_selNode != null || _selPlacement != null) return;
        string? node = _lostNodePath, placed = _lostPlacementPath;
        if (node == null && placed == null) return;
        FlushRebuild();
        var scene = BuildScene();
        if (node != null && AtPath(scene, node, _lostNodeTwin, npc: false) is { Node: { } back } at) SelectNode(back, at.Path);
        else if (placed != null && AtPath(scene, placed, _lostPlacementTwin, npc: true) is { Npc: { } standing } where) SelectPlacement(standing, where.Path);
    }

    /// <summary>The settings beside the picture, for tests: what they show, and where.</summary>
    internal ContentControl SettingsPanel => _settings;

    /// <summary>What the panel says is picked, for tests.</summary>
    internal string PickedTitle => _gizmoTitle.Text;
}
