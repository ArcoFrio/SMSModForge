using System.Collections.ObjectModel;

namespace SMSModForge.ViewModel;

/// <summary>
/// Base for an item in the Dialogues-tab folder tree: either a
/// <see cref="DialogueFolderNode"/> or a <see cref="DialogueLeafNode"/>.
/// The grouping is cosmetic (persisted under the editor-only
/// <c>dialogueFolders</c> key) — see <see cref="Model.DialogueFolderDef"/>.
/// </summary>
public abstract class DialogueTreeItem : ObservableObject
{
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }
}

/// <summary>A folder holding nested folders and dialogue leaves.</summary>
public sealed class DialogueFolderNode : DialogueTreeItem
{
    private string _name;
    public DialogueFolderNode(string name)
    {
        _name = name ?? "";
        Children.CollectionChanged += (_, __) => OnPropertyChanged(nameof(Label));
    }

    public string Name
    {
        get => _name;
        set { _name = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); }
    }

    public ObservableCollection<DialogueTreeItem> Children { get; } = new();

    /// <summary>Folder name with its direct-child count, e.g. "Beach (3)".</summary>
    public string Label => $"{_name} ({Children.Count})";
}

/// <summary>A dialogue leaf wrapping its <see cref="DialogueViewModel"/>.</summary>
public sealed class DialogueLeafNode : DialogueTreeItem
{
    public DialogueViewModel Dialogue { get; }
    public DialogueLeafNode(DialogueViewModel dialogue) { Dialogue = dialogue; }
}
