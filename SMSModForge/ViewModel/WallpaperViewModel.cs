using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper around a <see cref="WallpaperDef"/> for the
/// Wallpapers tab. Both <see cref="SpritePath"/> (pack-relative) and
/// <see cref="ExternalSpritePath"/> (transitional absolute path) are
/// exposed so authors can move between bundled and external sources
/// without losing the other slot.
/// </summary>
public sealed class WallpaperViewModel : ObservableObject
{
    public WallpaperDef Model { get; }

    public WallpaperViewModel(WallpaperDef model) { Model = model; }

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>
    /// Pack-relative path to the wallpaper PNG. Wins over
    /// <see cref="ExternalSpritePath"/> at runtime when both are set.
    /// </summary>
    public string SpritePath
    {
        get => Model.SpritePath ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.SpritePath == normalised) return;
            Model.SpritePath = normalised;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Absolute path used during the host mod transition. Fallback
    /// when <see cref="SpritePath"/> is empty.
    /// </summary>
    public string ExternalSpritePath
    {
        get => Model.ExternalSpritePath ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.ExternalSpritePath == normalised) return;
            Model.ExternalSpritePath = normalised;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Unlock condition's <c>params.name</c> — the variable name the
    /// runtime checks (e.g. <c>Event_SeenAnisMall01</c>). Stored in
    /// <see cref="WallpaperDef.UnlockCondition"/> as a
    /// <c>VariableEquals</c> condition with this name. Lazily creates
    /// the condition object on first edit so the JSON doesn't carry
    /// an empty stub for wallpapers that should always be visible.
    /// </summary>
    public string UnlockVariableName
    {
        get => Model.UnlockCondition?.Params != null
            && Model.UnlockCondition.Params.TryGetValue("name", out var n) ? n : "";
        set
        {
            string normalised = (value ?? "").Trim();
            if (string.IsNullOrEmpty(normalised))
            {
                if (Model.UnlockCondition == null) return;
                Model.UnlockCondition = null;
            }
            else
            {
                if (Model.UnlockCondition == null)
                    Model.UnlockCondition = new NodeConditionDef
                    {
                        Type = NodeConditionTypes.VariableEquals,
                        Params = new System.Collections.Generic.Dictionary<string, string>
                        {
                            ["name"] = normalised,
                            ["value"] = UnlockVariableValue,
                        },
                    };
                else
                    Model.UnlockCondition.Params["name"] = normalised;
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The expected value of <see cref="UnlockVariableName"/>. Defaults
    /// to <c>"true"</c> — the most common case is gating wallpapers on
    /// an <c>Event_Seen*</c> bool flipping true.
    /// </summary>
    public string UnlockVariableValue
    {
        get => Model.UnlockCondition?.Params != null
            && Model.UnlockCondition.Params.TryGetValue("value", out var v) ? v : "true";
        set
        {
            string normalised = (value ?? "true").Trim();
            if (Model.UnlockCondition != null)
                Model.UnlockCondition.Params["value"] = normalised;
            OnPropertyChanged();
        }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";
}
