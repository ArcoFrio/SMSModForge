using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.ViewModel;

/// <summary>One template in the FX tab's picker: its words and its group.</summary>
public sealed class EffectTemplateOption
{
    public EffectTemplateOption(FX.Template template)
    {
        Template = template;
        Id = template.Id;
    }

    public FX.Template Template { get; }
    public string Id { get; }
    public string Name => Loc.T("fx.template." + Template.Name);
    public string Description => Loc.Optional("fx.template." + Template.Name + ".tip");
    public string Category => Loc.T("fx.category." + Template.Category);
    /// <summary>"The game's" or "ModForge's", shown beside the name.</summary>
    public string Origin => Template.IsVanilla ? Loc.T("fx.origin.game") : Loc.T("fx.origin.forge");
    public override string ToString() => Name;
}

/// <summary>A choice with its words (where to draw, when to play, a game picture).</summary>
public sealed class EffectChoice
{
    public EffectChoice(string value, string text) { Value = value; Text = text; }
    public string Value { get; }
    public string Text { get; }
    public override string ToString() => Text;
}

/// <summary>
/// One of the pack's effects in the FX tab (1.10.0). What the author changes is written to the
/// <see cref="EffectDef"/> straight away (undo reads the pack); a setting at the template's own value writes
/// nothing, so a pack carries only what was changed.
/// </summary>
public sealed class EffectViewModel : ObservableObject, IDerivesKey
{
    public EffectDef Model { get; }

    public ObservableCollection<NodeConditionViewModel> Conditions { get; }

    public EffectViewModel(EffectDef model)
    {
        Model = model;
        Conditions = new ObservableCollection<NodeConditionViewModel>(
            model.Conditions.Select(c => new NodeConditionViewModel(c, RemoveCondition)));
        AddConditionCommand = new RelayCommand(() =>
        {
            var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
            Model.Conditions.Add(def);
            Conditions.Add(new NodeConditionViewModel(def, RemoveCondition));
        });
        AddConditionGroupCommand = new RelayCommand(() =>
        {
            var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
            Model.Conditions.Add(def);
            Conditions.Add(new NodeConditionViewModel(def, RemoveCondition));
        });
        UseGamesPlaceCommand = new RelayCommand(() => { Model.X = null; Model.Y = null; Changed(nameof(X), nameof(Y), nameof(AtGamesPlace)); });
    }

    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddConditionGroupCommand { get; }
    public RelayCommand UseGamesPlaceCommand { get; }

    public void RemoveCondition(NodeConditionViewModel c)
    {
        Model.Conditions.Remove(c.Model);
        Conditions.Remove(c);
    }

    // ── Name ───────────────────────────────────────────────────────────────────────────────────

    private readonly DerivedKey _derivedKey = new();
    public bool KeyIsDerived => _derivedKey.IsDerived;
    public void DeriveKeyFromDisplayName(Func<IEnumerable<string>> siblingKeys) => _derivedKey.Follow(siblingKeys);
    public void WatchDisplayName(Func<IEnumerable<string>> siblingKeys) => _derivedKey.Watch(siblingKeys, Model.DisplayName);

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            _derivedKey.Stop();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            if (Model.DisplayName == value) return;
            Model.DisplayName = value;
            if (_derivedKey.Next(value, Model.Key) is { } derived && derived != Model.Key)
            {
                Model.Key = derived;
                OnPropertyChanged(nameof(Key));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Display => ListNames.Of(DisplayName, Key);

    // ── The template ───────────────────────────────────────────────────────────────────────────

    /// <summary>Every template, grouped as the picker shows them.</summary>
    public static IReadOnlyList<EffectTemplateOption> TemplateOptions { get; } =
        FX.Categories.SelectMany(c => FX.Catalog.Where(t => t.Category == c)).Select(t => new EffectTemplateOption(t)).ToArray();

    /// <summary>The same, grouped by what they are (Basic, Social, Love...) for the picker.</summary>
    public static System.ComponentModel.ICollectionView TemplateOptionsGrouped { get; } = MakeGrouped();

    private static System.ComponentModel.ICollectionView MakeGrouped()
    {
        var view = new System.Windows.Data.ListCollectionView(TemplateOptions.ToList());
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(EffectTemplateOption.Category)));
        return view;
    }

    public FX.Template Info => FX.Find(Model.Template) ?? FX.Catalog[0];

    public EffectTemplateOption? TemplateOption
    {
        get => TemplateOptions.FirstOrDefault(o => o.Id == Model.Template);
        set
        {
            if (value == null || value.Id == Model.Template) return;
            Model.Template = value.Id;
            // What only the old template had goes; the rest is kept, so trying a few templates loses nothing
            // that still means something.
            var t = value.Template;
            if (!t.CanChangeGames) Model.ChangesGame = false;
            if (!t.Has(FX.Settings.Text)) Model.Text = null;
            if (!t.Has(FX.Settings.EndPosition)) { Model.EndX = null; Model.EndY = null; }
            if (!t.Has(FX.Settings.Spread)) Model.Spread = null;
            if (!t.Has(FX.Settings.Times)) { Model.FloatSeconds = null; Model.TravelSeconds = null; }
            if (!t.Has(FX.Settings.Glow)) Model.Glow = false;
            if (t.Kind == FX.Kind.Points && Model.Amount is null or < 1) Model.Amount = null;
            RaiseAll();
        }
    }

    public string TemplateName => TemplateOption?.Name ?? Model.Template;
    public string TemplateDescription => TemplateOption?.Description ?? "";

    public bool CanChangeGames => Info.CanChangeGames;

    /// <summary>Change the game's own (in place) rather than add one like it.</summary>
    public bool ChangesGame
    {
        get => Model.ChangesGame && Info.CanChangeGames;
        set
        {
            if (Model.ChangesGame == value) return;
            Model.ChangesGame = value;
            RaiseAll();
        }
    }

    public bool ShowPosition => Info.Has(FX.Settings.Position) && !ChangesGame;
    public bool ShowGamesPlace => Info.IsVanilla && Info.Kind == FX.Kind.Particles;
    public bool ShowLayer => Info.Has(FX.Settings.Layer) && !ChangesGame;
    public bool ShowScale => Info.Has(FX.Settings.Scale);
    public bool ShowTexture => Info.Has(FX.Settings.Texture);
    public bool ShowColor => Info.Has(FX.Settings.Color);
    public bool ShowAmount => Info.Has(FX.Settings.Amount);
    public bool ShowSize => Info.Has(FX.Settings.Size);
    public bool ShowSpeed => Info.Has(FX.Settings.Speed);
    public bool ShowLifetime => Info.Has(FX.Settings.Lifetime);
    public bool ShowGravity => Info.Has(FX.Settings.Gravity);
    public bool ShowSeconds => Info.Has(FX.Settings.Seconds) && !ChangesGame;
    public bool ShowEnd => Info.Has(FX.Settings.EndPosition);
    public bool ShowSpread => Info.Has(FX.Settings.Spread);
    public bool ShowTimes => Info.Has(FX.Settings.Times);
    public bool ShowText => Info.Has(FX.Settings.Text);
    public bool ShowGlow => Info.Has(FX.Settings.Glow);
    public bool ShowWhen => !ChangesGame;
    public bool IsPoints => Info.Kind == FX.Kind.Points;

    // ── Where ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The game's effect where the game has it (no place of the author's).</summary>
    public bool AtGamesPlace => Model.X == null && Model.Y == null;

    public double X
    {
        get => Model.X ?? DefaultX;
        set { Model.X = Round(value); Changed(nameof(X), nameof(AtGamesPlace)); }
    }

    public double Y
    {
        get => Model.Y ?? DefaultY;
        set { Model.Y = Round(value); Changed(nameof(Y), nameof(AtGamesPlace)); }
    }

    private double DefaultX => Info.Kind == FX.Kind.Points ? FX.PointsStartX : 0.5;
    private double DefaultY => Info.Kind == FX.Kind.Points ? FX.PointsStartY : Info.Name == "falling" ? 1.04 : Info.Kind == FX.Kind.Popup ? 0.55 : 0.5;

    public double EndX
    {
        get => Model.EndX ?? FX.PointsEndX;
        set { Model.EndX = Round(value); Changed(nameof(EndX)); }
    }

    public double EndY
    {
        get => Model.EndY ?? FX.PointsEndY;
        set { Model.EndY = Round(value); Changed(nameof(EndY)); }
    }

    public static IReadOnlyList<EffectChoice> LayerChoices { get; } = new[]
    {
        new EffectChoice(FX.Behind, Loc.T("fx.layer.behind")),
        new EffectChoice(FX.Front, Loc.T("fx.layer.front")),
    };

    public EffectChoice Layer
    {
        get => LayerChoices.First(c => c.Value == (Model.Layer ?? Info.DefaultLayer));
        set
        {
            if (value == null) return;
            Model.Layer = value.Value == Info.DefaultLayer ? null : value.Value;
            Changed(nameof(Layer));
        }
    }

    // ── How it looks ───────────────────────────────────────────────────────────────────────────

    public double Scale { get => Model.Scale ?? 1; set { Model.Scale = OneIsNone(value); Changed(nameof(Scale)); } }
    public double Size { get => Model.Size ?? 1; set { Model.Size = OneIsNone(value); Changed(nameof(Size)); } }
    public double Speed { get => Model.Speed ?? 1; set { Model.Speed = OneIsNone(value); Changed(nameof(Speed)); } }
    public double Lifetime { get => Model.Lifetime ?? 1; set { Model.Lifetime = OneIsNone(value); Changed(nameof(Lifetime)); } }

    /// <summary>Particles: times the template's. The points: how many (new), or times the game's (changed).</summary>
    public double Amount
    {
        get => Model.Amount ?? (IsPoints && !ChangesGame ? FX.DefaultPointsCount : 1);
        set
        {
            double v = IsPoints && !ChangesGame ? Math.Max(1, Math.Round(value)) : value;
            Model.Amount = IsPoints && !ChangesGame
                ? (Math.Abs(v - FX.DefaultPointsCount) < 0.001 ? null : (float)v)
                : OneIsNone(v);
            Changed(nameof(Amount));
        }
    }

    public string AmountLabel => Loc.T(IsPoints ? (ChangesGame ? "fx.pointsTimes" : "fx.pointsCount") : "fx.amount");
    public double AmountMaximum => IsPoints && !ChangesGame ? 200 : 5;

    /// <summary>Empty = the template's; a number = how strongly the particles fall.</summary>
    public string GravityText
    {
        get => Model.Gravity?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        set { Model.Gravity = ParseOrNull(value); Changed(nameof(GravityText)); }
    }

    public string SecondsText
    {
        get => Model.Seconds?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        set { Model.Seconds = ParseOrNull(value); Changed(nameof(SecondsText)); }
    }

    public double Spread { get => Model.Spread ?? FX.DefaultPointsSpread; set { Model.Spread = Round(value); Changed(nameof(Spread)); } }
    public double FloatSeconds { get => Model.FloatSeconds ?? FX.DefaultFloatSeconds; set { Model.FloatSeconds = Round(value); Changed(nameof(FloatSeconds)); } }
    public double TravelSeconds { get => Model.TravelSeconds ?? FX.DefaultTravelSeconds; set { Model.TravelSeconds = Round(value); Changed(nameof(TravelSeconds)); } }

    public string Color
    {
        get => Model.Color ?? "";
        set { Model.Color = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Changed(nameof(Color), nameof(HasColor)); }
    }

    public bool HasColor => !string.IsNullOrEmpty(Model.Color);

    public string Texture
    {
        get => Model.Texture ?? "";
        set { Model.Texture = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Changed(nameof(Texture)); }
    }

    /// <summary>The game's pictures an author can pick instead of one of the pack's: the template's own first.</summary>
    public static IReadOnlyList<EffectChoice> GameTextureChoices { get; } =
        new[] { new EffectChoice("", Loc.T("fx.gameTexture.templates")) }
            .Concat(FX.GameTextures.Select(n => new EffectChoice(n, Loc.Optional("fx.gameTexture." + Slug(n)) is { Length: > 0 } t ? t : n)))
            .ToArray();

    public EffectChoice GameTexture
    {
        get => GameTextureChoices.FirstOrDefault(c => c.Value == (Model.GameTexture ?? "")) ?? GameTextureChoices[0];
        set
        {
            if (value == null) return;
            Model.GameTexture = value.Value.Length == 0 ? null : value.Value;
            Changed(nameof(GameTexture));
        }
    }

    public bool Glow { get => Model.Glow; set { Model.Glow = value; Changed(nameof(Glow)); } }

    public string Text
    {
        get => Model.Text ?? "";
        set { Model.Text = string.IsNullOrEmpty(value) ? null : value; Changed(nameof(Text)); }
    }

    // ── When ───────────────────────────────────────────────────────────────────────────────────

    public static IReadOnlyList<EffectChoice> PlayWhenChoices { get; } =
        FX.PlayWhens.Select(w => new EffectChoice(w, Loc.T("fx.playWhen." + w))).ToArray();

    public EffectChoice PlayWhen
    {
        get => PlayWhenChoices.FirstOrDefault(c => c.Value == Model.PlayWhen) ?? PlayWhenChoices[0];
        set
        {
            if (value == null || value.Value == Model.PlayWhen) return;
            Model.PlayWhen = value.Value;
            if (value.Value != FX.WhenUnderTurnsOn) Model.Under = null;
            Changed(nameof(PlayWhen), nameof(ShowConditions), nameof(ShowUnder), nameof(Under));
        }
    }

    public bool ShowConditions => Model.PlayWhen != FX.OnAction;
    public bool ShowUnder => Model.PlayWhen == FX.WhenUnderTurnsOn;

    public string Under
    {
        get => Model.Under ?? "";
        set { Model.Under = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Changed(nameof(Under)); }
    }

    /// <summary>Objects of the game's with things under them that turn on - a Starmaker photo, a scene.</summary>
    public static IReadOnlyList<string> UnderSuggestions { get; } = new[]
    {
        "4_CG_Manager-Photos", "4_CG_Manager-PrivatePhotos", "4_CG_Manager-Sexy", "4_CG_Manager-Sexy_Two",
        "4_CG_Manager-Sexy_Three", "4_CG_Manager-Sexy_Four", "5_CG_EndingArt",
    };

    // ── Plumbing ───────────────────────────────────────────────────────────────────────────────

    private void Changed(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
        LookVersion++;
        OnPropertyChanged(nameof(LookVersion));
    }

    /// <summary>Goes up whenever anything about how it looks changes: the preview starts it again.</summary>
    public int LookVersion { get; private set; }

    /// <summary>After a template change, or the record read back from a step: every row again.</summary>
    public void RaiseAll()
    {
        LookVersion++;
        OnPropertyChanged(string.Empty);
    }

    private static float? Round(double v) => (float)Math.Round(v, 3);
    private static float? OneIsNone(double v) => Math.Abs(v - 1) < 0.0005 ? null : (float)Math.Round(v, 3);

    private static float? ParseOrNull(string? text)
        => float.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    /// <summary>Every text key this view builds from a name (templates, groups, ways to play, the game's pictures),
    /// for the check that every text in the English file is one the editor shows.</summary>
    public static IEnumerable<string> ComposedTextKeys()
    {
        foreach (var t in FX.Catalog) { yield return "fx.template." + t.Name; yield return "fx.template." + t.Name + ".tip"; }
        foreach (var c in FX.Categories) yield return "fx.category." + c;
        foreach (var w in FX.PlayWhens) yield return "fx.playWhen." + w;
        foreach (var n in FX.GameTextures) yield return "fx.gameTexture." + Slug(n);
    }

    private static string Slug(string name)
        => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
