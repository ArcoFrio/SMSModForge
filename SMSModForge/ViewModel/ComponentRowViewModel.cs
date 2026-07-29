using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One name/value pair on a game component. Writes straight through to the
/// def's extension data, so what the author types is what the manifest carries
/// and what the runtime sets by reflection. An empty value removes the key
/// rather than writing a blank, so a parameter left alone stays at the
/// component's own default.
/// </summary>
public sealed class ComponentParamRow : ObservableObject
{
    private readonly ComponentDef _def;

    public ComponentParamRow(ComponentDef def, string name, string observed)
    {
        _def = def;
        Name = name;
        Observed = observed;
    }

    public string Name { get; }

    /// <summary>What the extraction saw on a vanilla level — a starting point,
    /// shown beside the field rather than filled in, so an untouched parameter
    /// isn't silently pinned to some other level's value.</summary>
    public string Observed { get; }

    public string Value
    {
        get => _def.Params.TryGetValue(Name, out var v) ? v?.ToString() ?? "" : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) _def.Params.Remove(Name);
            else _def.Params[Name] = ParseValue(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Keep numbers and bools typed in the JSON. The runtime converts to
    /// the member's real type either way, but a manifest reading
    /// <c>"parallaxStrength": 0.75</c> beats one reading <c>"0.75"</c>.</summary>
    private static JToken ParseValue(string s)
    {
        s = s.Trim();
        if (bool.TryParse(s, out var b)) return new JValue(b);
        if (int.TryParse(s, System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture, out var i)) return new JValue(i);
        if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var f)) return new JValue(f);
        return new JValue(s);
    }
}

/// <summary>
/// INPC wrapper for one <see cref="ComponentDef"/> attached to an overlay
/// (GameObject). Exposes the component <see cref="Type"/> plus every
/// config field; the <c>Is*</c> flags let the editor show only the fields the
/// chosen type actually uses (same conditional-visibility idea as the node
/// action rows).
/// </summary>
public sealed class ComponentRowViewModel : ObservableObject
{
    public ComponentDef Model { get; }
    private readonly Action<ComponentRowViewModel> _remove;

    public ComponentRowViewModel(ComponentDef model, Action<ComponentRowViewModel> remove)
    {
        Model = model;
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
        RebuildParams();
    }

    public RelayCommand RemoveCommand { get; }

    /// <summary>The pack's own four, then every type the vanilla extraction
    /// found — so a pack can attach the game's own behaviour scripts and isn't
    /// limited to what the plugin reimplements.</summary>
    public IReadOnlyList<string> ComponentTypes { get; } = VanillaComponentCatalog.TypeNames();

    public string Type
    {
        get => Model.Type;
        set
        {
            if (Model.Type == value) return;
            Model.Type = value;
            // Values authored for the previous type mean nothing to the new one
            // and would be written out as junk.
            Model.Params.Clear();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFadeIn));
            OnPropertyChanged(nameof(IsFadeOut));
            OnPropertyChanged(nameof(IsRandom));
            OnPropertyChanged(nameof(IsBlinking));
            OnPropertyChanged(nameof(IsGameComponent));
            OnPropertyChanged(nameof(TypeNote));
            OnPropertyChanged(nameof(Display));
            RebuildParams();
        }
    }

    public bool IsFadeIn   => Model.Type == PackComponentType.FadeInSprite;
    public bool IsFadeOut  => Model.Type == PackComponentType.FadeOutSprite;
    public bool IsRandom   => Model.Type == PackComponentType.RandomChildActivator;
    public bool IsBlinking => Model.Type == PackComponentType.BlinkingSprite;

    // ── A component the GAME defines ──────────────────────────────────────

    /// <summary>True for anything the plugin doesn't reimplement itself — the
    /// runtime resolves it by name and sets these values by reflection.</summary>
    public bool IsGameComponent => !PackComponentType.IsBuiltIn(Model.Type);

    /// <summary>What the author needs to know about the selected type.</summary>
    public string TypeNote
    {
        get
        {
            if (!IsGameComponent) return "";
            var e = VanillaComponentCatalog.Find(Model.Type);
            if (e == null)
                return "Not seen in any extracted level. The runtime will still try to find a loaded type with this name.";
            return e.IsEngineComponent
                ? "A Unity engine component. It will be attached, but its parameter names are engine internals and mostly won't apply — the values below are what the extraction saw, not a promise they can be set."
                : "One of the game's own scripts. Parameter names come from the extraction, so they match its real fields.";
        }
    }

    /// <summary>Editable name/value rows for a game component. Seeded from the
    /// parameters the extraction observed on that type, so the author picks from
    /// real names instead of guessing spellings.</summary>
    public ObservableCollection<ComponentParamRow> ParamRows { get; } = new();

    private void RebuildParams()
    {
        ParamRows.Clear();
        if (!IsGameComponent) return;
        var e = VanillaComponentCatalog.Find(Model.Type);
        if (e == null) return;
        foreach (var kv in e.Parameters)
            ParamRows.Add(new ComponentParamRow(Model, kv.Key, kv.Value));
        OnPropertyChanged(nameof(ParamRows));
    }

    // ── FadeInSprite ──────────────────────────────────────────────────
    public float FadeDuration { get => Model.FadeDuration; set { Model.FadeDuration = value; OnPropertyChanged(); } }
    public float TargetAlpha  { get => Model.TargetAlpha;  set { Model.TargetAlpha = value;  OnPropertyChanged(); } }

    // ── FadeOutSprite ─────────────────────────────────────────────────
    public float Duration            { get => Model.Duration;            set { Model.Duration = value;            OnPropertyChanged(); } }
    public bool DeactivateOnComplete { get => Model.DeactivateOnComplete; set { Model.DeactivateOnComplete = value; OnPropertyChanged(); } }

    // ── Shared by both fades ──────────────────────────────────────────
    public float StartDelay { get => Model.StartDelay; set { Model.StartDelay = value; OnPropertyChanged(); } }

    // ── RandomChildActivator ──────────────────────────────────────────
    public bool ReshuffleOnEnable { get => Model.ReshuffleOnEnable; set { Model.ReshuffleOnEnable = value; OnPropertyChanged(); } }

    // ── BlinkingSprite ────────────────────────────────────────────────
    public float BlinkInterval { get => Model.BlinkInterval; set { Model.BlinkInterval = value; OnPropertyChanged(); } }
    public float MinAlpha      { get => Model.MinAlpha;      set { Model.MinAlpha = value;      OnPropertyChanged(); } }
    public float MaxAlpha      { get => Model.MaxAlpha;      set { Model.MaxAlpha = value;      OnPropertyChanged(); } }

    public string Display => Type.ToString();
}
