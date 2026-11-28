using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.View.Controls;

/// <summary>
/// Effects in a place (1.10.0, the author: "some effects are actual level-related ones, similar to the seagulls we
/// offer for Places. Do you think we could implement those options for Places too?"): an object of the place plays
/// one of the FX tab's templates - fog, a campfire, falling leaves - where it stands, shown with the place. "+ Effect"
/// over the object list makes one in the middle of the view; the panel beside the picture sets it up; the picture
/// shows it moving, at the object, as the FX tab's preview draws it.
/// </summary>
public sealed partial class PlacePreview
{
    public static readonly DependencyProperty AddEffectCommandProperty =
        DependencyProperty.Register(nameof(AddEffectCommand), typeof(ICommand), typeof(PlacePreview),
            new PropertyMetadata(null, (d, _) => ((PlacePreview)d).RefreshObjectMenu()));

    /// <summary>The place's (or vanilla extension's) AddEffectCommand: its parameter is where (float[] { x, y }).</summary>
    public ICommand? AddEffectCommand
    {
        get => (ICommand?)GetValue(AddEffectCommandProperty);
        set => SetValue(AddEffectCommandProperty, value);
    }

    /// <summary>The effects playing over the picture, each at its object; never in the way of a click.</summary>
    private readonly Canvas _effectLayer = new() { Width = CanvasWidth, Height = CanvasHeight, ClipToBounds = true, IsHitTestVisible = false };

    private readonly Dictionary<GameObjectViewModel, EffectPreview> _effectViews = new();

    /// <summary>The layer the objects' effects play on (tests measure what it draws).</summary>
    internal Canvas EffectLayer => _effectLayer;

    /// <summary>Each object's effect playing at the object (its own subtree hidden, none).</summary>
    private void DrawEffects()
    {
        var seen = new HashSet<GameObjectViewModel>();
        foreach (var e in _scene)
        {
            if (e.Node is not { } node || e.Hidden || node.Model.Effect is not { } def) continue;
            seen.Add(node);
            if (!_effectViews.TryGetValue(node, out var view))
            {
                view = new EffectPreview { Width = CanvasWidth, Height = CanvasHeight, Bare = true };
                _effectViews[node] = view;
                _effectLayer.Children.Add(view);
            }
            // At the object, or as far from it as its box was moved, in the object's own units (after 1.10.0).
            var c = EffectCentre(e, def);
            view.AtX = c.X / CanvasWidth;
            view.AtY = 1 - c.Y / CanvasHeight;
            view.PackRoot = PackRoot;
            if (!ReferenceEquals(view.Effect, def)) view.Effect = def;
            int version = node.Effect?.LookVersion ?? 0;
            // Its box being sized: what is out plays on (EffectPreview.Rescale).
            if (_activeHandle == GizmoHandle.EffectSize && ReferenceEquals(node, _selNode)) view.Rescale(def.Scale ?? 1, version);
            else view.Version = version;
        }
        foreach (var gone in _effectViews.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _effectLayer.Children.Remove(_effectViews[gone]);
            _effectViews.Remove(gone);
        }
    }

    /// <summary>"+ Effect", beside "+ Light", when the host can make one.</summary>
    private Button? AddEffectChip()
    {
        if (AddEffectCommand == null) return null;
        var b = MakeChip(Chip("preview.effect.add"), AddEffectHere);
        LocText.Bind(b, ToolTipProperty, "preview.effect.add.tip");
        Paint(b, false);
        TutorialAnchor.SetId(b, "btn:addPlaceEffect");
        return b;
    }

    /// <summary>Make an effect object in the middle of what the view shows, and pick it.</summary>
    internal void AddEffectHere()
    {
        var command = AddEffectCommand;
        if (command == null) return;
        var mid = new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
        Point onCanvas = _viewport.ActualWidth > 0 ? _viewport.TranslatePoint(mid, _canvas) : new Point(CanvasWidth / 2, CanvasHeight / 2);
        var frame = FrameFromParent(WorldToLevel);
        var at = new[] { (float)Math.Round((onCanvas.X - frame.ParentOrigin.X) / frame.Ux.X, 2), (float)Math.Round((onCanvas.Y - frame.ParentOrigin.Y) / frame.Uy.Y, 2) };
        var before = new HashSet<object>(_scene.Where(e => e.Node != null).Select(e => (object)e.Node!));
        if (!command.CanExecute(at)) return;
        command.Execute(at);
        RebuildOverlayGos();
        var made = _scene.LastOrDefault(e => e.Node != null && !before.Contains(e.Node) && e.Node.HasEffect);
        if (made?.Node != null) SelectNode(made.Node, made.Path);
    }

    // ── The panel ───────────────────────────────────────────────────────

    private StackPanel _effectPanel = new();
    private Button _effectGive = new(), _effectRemove = new();
    private StackPanel _effectFields = new();
    private CheckBox _effectGlow = new();
    private Border _effectSwatch = new();

    /// <summary>The templates a place's object can play: the particles (the points, a flash and words are the
    /// screen's, in the FX tab).</summary>
    private static readonly IReadOnlyList<EffectTemplateOption> PlaceTemplates =
        EffectViewModel.TemplateOptions.Where(o => o.Template.Kind == FX.Kind.Particles)
                       .OrderBy(o => o.Template.IsVanilla ? 0 : 1).ToList();

    private StackPanel BuildEffectPanel()
    {
        _effectPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        TutorialAnchor.SetId(_effectPanel, "panel:placeEffect");
        var heading = Chip("preview.effect.heading");
        heading.FontWeight = FontWeights.Bold;
        heading.Foreground = ChipText;
        heading.Margin = new Thickness(0, 0, 0, 4);
        _effectPanel.Children.Add(heading);

        _effectGive = MakeChip(Chip("preview.effect.give"), () => { _selNode?.AddEffectCommand.Execute(null); Committed(); UpdateEffectPanel(); DrawEffects(); });
        LocText.Bind(_effectGive, ToolTipProperty, "preview.effect.give.tip");
        _effectPanel.Children.Add(Row(_effectGive));

        _effectFields = new StackPanel();
        var template = new ComboBox { Width = 190, FontSize = 11, ItemsSource = PlaceTemplates, DisplayMemberPath = nameof(EffectTemplateOption.Name) };
        template.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                            new Binding(nameof(EffectViewModel.TemplateOption)) { Mode = BindingMode.TwoWay });
        template.SelectionChanged += (_, _) => { if (template.IsKeyboardFocusWithin || template.IsDropDownOpen) Committed(); };
        template.DropDownClosed += (_, _) => Committed();
        _effectFields.Children.Add(Field("preview.effect.template", "preview.effect.template.tip", OnThemeSurface(template)));

        var picture = new PathPickerBox { Width = 150 };
        picture.SetBinding(PathPickerBox.PathTextProperty, new Binding(nameof(EffectViewModel.Texture)) { Mode = BindingMode.TwoWay });
        picture.SetBinding(PathPickerBox.PackRootProperty, new Binding(nameof(PackRoot)) { Source = this });
        _effectFields.Children.Add(Field("preview.effect.picture", "preview.effect.picture.tip", picture));

        var gamePicture = new ComboBox { Width = 150, FontSize = 11, ItemsSource = EffectViewModel.GameTextureChoices };
        gamePicture.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                               new Binding(nameof(EffectViewModel.GameTexture)) { Mode = BindingMode.TwoWay });
        gamePicture.DropDownClosed += (_, _) => Committed();
        _effectFields.Children.Add(Field("preview.effect.gamePicture", "preview.effect.gamePicture.tip", OnThemeSurface(gamePicture)));

        _effectSwatch = new Border { Width = 22, Height = 18, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand };
        LocText.Bind(_effectSwatch, ToolTipProperty, "preview.effect.colour.tip");
        _effectSwatch.MouseLeftButtonDown += (_, e) => { PickEffectColour(); e.Handled = true; };
        _effectFields.Children.Add(Field("preview.effect.colour", "preview.effect.colour.tip", _effectSwatch, NumberBox(nameof(EffectViewModel.Color), 76, null)));

        _effectFields.Children.Add(SliderField("preview.effect.amount", "preview.effect.amount.tip", nameof(EffectViewModel.Amount), 0, 5, "0.##"));
        _effectFields.Children.Add(SliderField("preview.effect.size", "preview.effect.size.tip", nameof(EffectViewModel.Size), 0.1, 5, "0.##"));
        _effectFields.Children.Add(SliderField("preview.effect.scale", "preview.effect.scale.tip", nameof(EffectViewModel.Scale), 0.1, 5, "0.##"));
        _effectFields.Children.Add(SliderField("preview.effect.speed", "preview.effect.speed.tip", nameof(EffectViewModel.Speed), 0, 5, "0.##"));
        _effectFields.Children.Add(SliderField("preview.effect.lifetime", "preview.effect.lifetime.tip", nameof(EffectViewModel.Lifetime), 0.1, 5, "0.##"));

        var layer = new ComboBox { Width = 150, FontSize = 11, ItemsSource = EffectViewModel.LayerChoices };
        layer.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(EffectViewModel.Layer)) { Mode = BindingMode.TwoWay });
        layer.DropDownClosed += (_, _) => Committed();
        _effectFields.Children.Add(Field("preview.effect.layer", "preview.effect.layer.tip", OnThemeSurface(layer)));

        _effectGlow = new CheckBox { Foreground = ChipText, FontSize = 11, Margin = new Thickness(0, 2, 0, 4), Content = Chip("preview.effect.glow") };
        LocText.Bind(_effectGlow, ToolTipProperty, "preview.effect.glow.tip");
        _effectGlow.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(EffectViewModel.Glow)) { Mode = BindingMode.TwoWay });
        _effectGlow.Click += (_, _) => Committed();
        _effectFields.Children.Add(_effectGlow);

        _effectPanel.Children.Add(_effectFields);
        _effectRemove = MakeChip(Chip("preview.effect.remove"), () => { _selNode?.RemoveEffectCommand.Execute(null); Committed(); UpdateEffectPanel(); DrawEffects(); });
        LocText.Bind(_effectRemove, ToolTipProperty, "preview.effect.remove.tip");
        _effectPanel.Children.Add(Row(_effectRemove));
        foreach (var b in new[] { _effectGive, _effectRemove }) Paint(b, false);

        // A change to the effect shows in the picture at once.
        _effectPanel.DataContextChanged += (_, e) =>
        {
            if (e.OldValue is EffectViewModel old) old.PropertyChanged -= EffectEdited;
            if (e.NewValue is EffectViewModel now) now.PropertyChanged += EffectEdited;
        };
        return _effectPanel;
    }

    private void EffectEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A template changed raises every row at once (a name of none).
        if (e.PropertyName is nameof(EffectViewModel.LookVersion) or nameof(EffectViewModel.LocalX) or nameof(EffectViewModel.LocalY) or "" or null)
            DrawEffects();
        if (e.PropertyName is nameof(EffectViewModel.Color) or "") UpdateEffectSwatch();
        if (e.PropertyName is nameof(EffectViewModel.TemplateOption) or "") UpdateEffectPanel();
    }

    private void Committed() => RaiseEvent(new RoutedEventArgs(EditCommittedEvent, this));

    private void PickEffectColour()
    {
        var effect = _selNode?.Effect;
        if (effect == null || Services.TestMode.Active) return;
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var chosen = ColorPickerWindow.Pick(owner, string.IsNullOrEmpty(effect.Color) ? "#FFFFFFFF" : effect.Color);
        if (chosen == null) return;
        effect.Color = chosen;
        Committed();
        UpdateEffectSwatch();
    }

    private void UpdateEffectSwatch()
    {
        var effect = _selNode?.Effect;
        _effectSwatch.Background = effect != null && EffectPreview.ParseColour(effect.Color) is { } c
            ? new SolidColorBrush(c)
            : new SolidColorBrush(Colors.White);
    }

    /// <summary>What the picked object's effect offers: a way to give it one, or its settings.</summary>
    private void UpdateEffectPanel()
    {
        var node = _selNode;
        if (node == null || _selPlacement != null || AddEffectCommand == null)
        {
            _effectPanel.Visibility = Visibility.Collapsed;
            _effectPanel.DataContext = null;
            return;
        }
        _effectPanel.Visibility = Visibility.Visible;
        var effect = node.Effect;
        _effectGive.Visibility = effect == null ? Visibility.Visible : Visibility.Collapsed;
        _effectFields.Visibility = effect != null ? Visibility.Visible : Visibility.Collapsed;
        _effectRemove.Visibility = effect != null ? Visibility.Visible : Visibility.Collapsed;
        _effectPanel.DataContext = effect;
        if (effect == null) return;
        _effectGlow.Visibility = effect.ShowGlow ? Visibility.Visible : Visibility.Collapsed;
        UpdateEffectSwatch();
    }
}
