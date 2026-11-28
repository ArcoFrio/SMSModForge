using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SMSModForge.Localization;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// "Moves with", beside the picture (after 1.10.0, the author: "add the checkboxes for either moving with background,
/// foreground, or manually set it. Moving with background should default to on"): how a thing drifts as the cursor
/// moves - with the place's back picture, its front, or by a strength of its own - read from and written to its own
/// Parallax settings (<see cref="Drifting"/>). Two of them: a pack lamp's, in the light's panel, and a character's
/// standing in the place ("parallax settings for individual NPCs"). The game's plugin gives either the effect the
/// preview draws (PlaceFactory.GiveDrift).
/// </summary>
public sealed partial class PlacePreview
{
    /// <summary>One "Moves with": its chips, the strength and reversal shown for Own, and what it shows now.</summary>
    internal sealed class DriftChoice
    {
        public StackPanel Panel = new(), OwnFields = new();
        public Button Back = new(), Front = new(), Own = new();
        public DriftTips Tips = LampTips;
        public IDrifts? Shown;
        public PropertyChangedEventHandler? Watch;
    }

    /// <summary>The tips of a "Moves with": the words on the chips are the same for a lamp and a character, the tips
    /// each its own. Every key written out whole, as the check that every English text is shown reads them.</summary>
    internal sealed record DriftTips(string Row, string Back, string NoBack, string Front, string Own, string Strength);

    private static readonly DriftTips LampTips = new("preview.light.drift.tip", "preview.light.drift.back.tip",
        "preview.light.drift.noBack.tip", "preview.light.drift.front.tip", "preview.light.drift.own.tip", "preview.light.drift.strength.tip");

    private static readonly DriftTips CharacterTips = new("preview.character.drift.tip", "preview.character.drift.back.tip",
        "preview.character.drift.noBack.tip", "preview.character.drift.front.tip", "preview.character.drift.own.tip",
        "preview.character.drift.strength.tip");

    private readonly DriftChoice _lightDrift = new(), _characterDrift = new();

    /// <summary>The picked lamp's "Moves with", for tests.</summary>
    internal DriftChoice LightDrift => _lightDrift;

    /// <summary>The picked character's "Moves with", for tests.</summary>
    internal DriftChoice CharacterDrift => _characterDrift;

    private StackPanel BuildDriftField(DriftChoice d, DriftTips tips, string panelAnchor, string backAnchor)
    {
        d.Tips = tips;
        d.Back = MakeChip(Chip("preview.light.drift.back"), () => SetDrift(d, Drifting.Back));
        d.Front = MakeChip(Chip("preview.light.drift.front"), () => SetDrift(d, Drifting.Front));
        d.Own = MakeChip(Chip("preview.light.drift.own"), () => SetDrift(d, Drifting.Own));
        TutorialAnchor.SetId(d.Back, backAnchor);

        d.OwnFields = new StackPanel();
        d.OwnFields.Children.Add(SliderField("preview.light.drift.strength", tips.Strength,
                                             nameof(IDrifts.ParallaxStrength), 0, 1.5, "0.##"));
        var reversed = new CheckBox { Foreground = ChipText, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), Content = Chip("preview.light.drift.reversed") };
        ((TextBlock)reversed.Content).Foreground = ChipText;
        LocText.Bind(reversed, ToolTipProperty, "preview.light.drift.reversed.tip");
        reversed.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(IDrifts.ParallaxReversed)) { Mode = BindingMode.TwoWay });
        reversed.Click += (_, _) => RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        d.OwnFields.Children.Add(reversed);

        var field = Field("preview.light.drift", tips.Row, d.Back, d.Front, d.Own);
        // The chips keep their own tips (Field gives its editors the row's); Back's says why it cannot be chosen
        // where the level has no back picture (ShowDrift).
        LocText.Bind(d.Front, ToolTipProperty, tips.Front);
        LocText.Bind(d.Own, ToolTipProperty, tips.Own);
        d.Panel = new StackPanel();
        TutorialAnchor.SetId(d.Panel, panelAnchor);
        d.Panel.Children.Add(field);
        d.Panel.Children.Add(d.OwnFields);
        d.Panel.Visibility = Visibility.Collapsed;
        d.Watch = (_, e) => { if (e.PropertyName is nameof(IDrifts.Drift) or "" or null) ShowDrift(d, d.Shown); };
        return d.Panel;
    }

    private void SetDrift(DriftChoice d, string kind)
    {
        var subject = d.Shown;
        if (subject == null || subject.Drift == kind) return;
        if (kind == Drifting.Back && !HasBackLayer) return;
        subject.DriftWith(kind, HasBackLayer ? (float)LevelParallaxSecondary : 0f);
        RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));
        ShowDrift(d, subject);
    }

    /// <summary>Both "Moves with" for what is picked: a pack lamp's (not the whole place's light, and not one of the
    /// game's lamps, which keep what the game gave them) and a character's.</summary>
    private void UpdateDrift()
    {
        var node = _selPlacement == null ? _selNode : null;
        var light = node?.Light;
        bool lamp = node != null && light != null && !light.IsAmbient && !node.Model.KeepsWhatItFinds;
        ShowDrift(_lightDrift, lamp ? node : null);
        ShowDrift(_characterDrift, _selPlacement);
    }

    /// <summary>How <paramref name="subject"/> drifts now - also when its record's Parallax is changed, or an undo
    /// changes it. Nothing shown for none.</summary>
    private void ShowDrift(DriftChoice d, IDrifts? subject)
    {
        if (!ReferenceEquals(d.Shown, subject))
        {
            if (d.Shown != null) d.Shown.PropertyChanged -= d.Watch;
            d.Shown = subject;
            if (subject != null) subject.PropertyChanged += d.Watch;
        }
        d.Panel.Visibility = subject != null ? Visibility.Visible : Visibility.Collapsed;
        d.OwnFields.DataContext = subject;
        if (subject == null) return;
        string now = subject.Drift;
        Paint(d.Back, now == Drifting.Back);
        Paint(d.Front, now == Drifting.Front);
        Paint(d.Own, now == Drifting.Own);
        // A game's level without a back picture has nothing to drift with; a thing already set to it stays with the level.
        d.Back.IsEnabled = HasBackLayer;
        LocText.Bind(d.Back, ToolTipProperty, HasBackLayer ? d.Tips.Back : d.Tips.NoBack);
        ToolTipService.SetShowOnDisabled(d.Back, true);
        d.OwnFields.Visibility = now == Drifting.Own ? Visibility.Visible : Visibility.Collapsed;
    }
}
