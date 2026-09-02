using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One object in a UI tree, for the UI tab's editor.
/// <para/>
/// Two kinds of row share this type, and the difference matters at every turn:
/// a row BOUND to something the game already has, and a row the pack added. A
/// bound row can be edited but not deleted — deleting it would mean deleting an
/// object out of the vanilla screen, which a pack cannot do and should not
/// pretend to. An added row can be deleted, because it is the pack's.
/// <para/>
/// Shaped after <see cref="GameObjectViewModel"/>, which does the same job for
/// places, so an author who has built a place already knows how this behaves.
/// </summary>
public sealed class UiNodeViewModel : ObservableObject
{
    private readonly Action<UiNodeViewModel>? _remove;
    private readonly Func<UiNodeDef, bool>? _hasChanges;

    public UiNodeDef Model { get; }
    public ObservableCollection<UiNodeViewModel> Children { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand AddChildCommand { get; }

    /// <summary>Raised whenever anything under this row changes, so the preview
    /// can redraw without every property having to remember to say so.</summary>
    public event Action? Changed;

    public UiNodeViewModel(UiNodeDef model, Action<UiNodeViewModel>? remove = null,
                           Func<UiNodeDef, bool>? hasChanges = null)
    {
        Model = model;
        _remove = remove;
        _hasChanges = hasChanges;

        Children = new ObservableCollection<UiNodeViewModel>(
            model.Children.Select(c => new UiNodeViewModel(c, RemoveChild, hasChanges)));
        foreach (var child in Children) child.Changed += Bubble;

        RemoveCommand = new RelayCommand(() => _remove?.Invoke(this),
                                         () => _remove != null && !IsVanilla);
        AddChildCommand = new RelayCommand(() => AddChild());
    }

    // ── What this row is ─────────────────────────────────────────────

    /// <summary>Whether this row stands for an object the game already has.
    /// The one fact that decides what may be done to it.</summary>
    public bool IsVanilla => Model.IsBound;

    /// <summary>Whether the pack added this object, so it can be removed.</summary>
    public bool IsMine => !Model.IsBound;

    /// <summary>Where this sits in the vanilla screen, or empty for an
    /// addition. Shown so an author can tell which is which without guessing
    /// from an icon.</summary>
    public string Bind => Model.Bind;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            // A bound row's name is the vanilla object's name and renaming it
            // would break the binding, so it is read-only rather than quietly
            // corrupting the link. The editor disables the box; this is the
            // second lock.
            if (IsVanilla) return;
            Model.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            Bubble();
        }
    }

    public string Display
        => string.IsNullOrWhiteSpace(Model.Name) ? "(unnamed)" : Model.Name;

    /// <summary>
    /// What has happened to this object, in one word for the tree.
    /// <para/>
    /// The distinction the whole tab turns on, and the reason it is worth a
    /// marker: a screen can hold 1434 objects and an author needs to see the
    /// three they touched. "changed" uses exactly the rule that decides what
    /// gets saved, so a marked row and a stored row are the same rows.
    /// </summary>
    public string Status
        => IsMine ? "new"
           : _hasChanges?.Invoke(Model) == true ? "changed"
           : "";

    public bool IsChanged => Status == "changed";
    public bool IsUntouched => Status.Length == 0;

    /// <summary>A short line saying what this object is made of, for a tree row
    /// that would otherwise be a name and nothing else.</summary>
    public string Summary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Model.Image != null && !string.IsNullOrEmpty(Model.Image.Sprite))
                parts.Add(Model.Image.Type.Equals("Sliced", StringComparison.OrdinalIgnoreCase)
                    ? Model.Image.Sprite + " (sliced)" : Model.Image.Sprite);
            if (Model.Text != null && !string.IsNullOrEmpty(Model.Text.Value))
                parts.Add('“' + Trim(Model.Text.Value, 28) + '”');
            if (Model.Components.Count > 0) parts.Add($"{Model.Components.Count} component(s)");
            if (!Model.StartActive) parts.Add("starts hidden");
            return string.Join(" · ", parts);
        }
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

    public bool StartActive
    {
        get => Model.StartActive;
        set
        {
            if (Model.StartActive == value) return;
            Model.StartActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    // ── Geometry ─────────────────────────────────────────────────────

    public float PositionX
    {
        get => At(Model.Rect.Position, 0);
        set { Set(Model.Rect.Position, 0, value); OnPropertyChanged(); Bubble(); }
    }

    public float PositionY
    {
        get => At(Model.Rect.Position, 1);
        set { Set(Model.Rect.Position, 1, value); OnPropertyChanged(); Bubble(); }
    }

    public float Width
    {
        get => At(Model.Rect.Size, 0);
        set { Set(Model.Rect.Size, 0, value); OnPropertyChanged(); Bubble(); }
    }

    public float Height
    {
        get => At(Model.Rect.Size, 1);
        set { Set(Model.Rect.Size, 1, value); OnPropertyChanged(); Bubble(); }
    }

    public float RotationZ
    {
        get => Model.Rect.RotationZ;
        set { Model.Rect.RotationZ = value; OnPropertyChanged(); Bubble(); }
    }

    // ── Picture ──────────────────────────────────────────────────────

    public bool HasImage => Model.Image != null;

    public string Sprite
    {
        get => Model.Image?.Sprite ?? "";
        set
        {
            // Setting a sprite on an object that had none gives it a picture,
            // which is a thing an author does on purpose - a container becomes
            // a panel. Clearing it back to nothing takes the picture away
            // rather than leaving an empty one behind to be saved.
            if (string.IsNullOrEmpty(value) && Model.Image != null)
            {
                Model.Image = null;
            }
            else if (!string.IsNullOrEmpty(value))
            {
                Model.Image ??= new UiImageDef();
                if (Model.Image.Sprite == value) return;
                Model.Image.Sprite = value;
            }
            else return;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    public string Tint
    {
        get => Model.Image?.Tint ?? "#FFFFFFFF";
        set
        {
            if (Model.Image == null || Model.Image.Tint == value) return;
            Model.Image.Tint = value;
            OnPropertyChanged();
            Bubble();
        }
    }

    // ── Words ────────────────────────────────────────────────────────

    public bool HasText => Model.Text != null;

    public string Text
    {
        get => Model.Text?.Value ?? "";
        set
        {
            if (string.IsNullOrEmpty(value) && Model.Text != null) Model.Text = null;
            else if (!string.IsNullOrEmpty(value))
            {
                Model.Text ??= new UiTextDef();
                if (Model.Text.Value == value) return;
                Model.Text.Value = value;
            }
            else return;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasText));
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    public string Font
    {
        get => Model.Text?.Font ?? "";
        set
        {
            if (Model.Text == null || Model.Text.Font == value) return;
            Model.Text.Font = value;
            OnPropertyChanged();
            Bubble();
        }
    }

    public float FontSize
    {
        get => Model.Text?.Size ?? 36f;
        set
        {
            if (Model.Text == null || Math.Abs(Model.Text.Size - value) < 0.001f) return;
            Model.Text.Size = value;
            OnPropertyChanged();
            Bubble();
        }
    }

    // ── Children ─────────────────────────────────────────────────────

    /// <summary>Add an object of the pack's own, inside this one.</summary>
    public UiNodeViewModel AddChild(string name = "New object")
    {
        var def = new UiNodeDef { Name = Unique(name) };
        Model.Children.Add(def);
        var vm = new UiNodeViewModel(def, RemoveChild, _hasChanges);
        vm.Changed += Bubble;
        Children.Add(vm);
        Bubble();
        return vm;
    }

    /// <summary>A name not already taken by a sibling. Duplicate names are how
    /// bind paths become ambiguous, and this is the cheapest place to stop
    /// that.</summary>
    private string Unique(string wanted)
    {
        var taken = Children.Select(c => c.Model.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted)) return wanted;
        for (int n = 2; ; n++)
        {
            string candidate = $"{wanted} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private void RemoveChild(UiNodeViewModel child)
    {
        // Only the pack's own objects. A vanilla one has no delete command, and
        // this is the second lock in case something else calls in.
        if (child.IsVanilla) return;
        child.Changed -= Bubble;
        Model.Children.Remove(child.Model);
        Children.Remove(child);
        Bubble();
    }

    private void Bubble()
    {
        // The marker is derived from the model, so it has to be re-asked after
        // anything that could change the answer.
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(IsUntouched));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke();
    }

    private static float At(float[]? pair, int i)
        => pair != null && pair.Length > i ? pair[i] : 0f;

    private static void Set(float[] pair, int i, float value)
    {
        if (pair.Length > i) pair[i] = value;
    }
}
