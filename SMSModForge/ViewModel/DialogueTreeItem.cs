using System.Collections.ObjectModel;

namespace SMSModForge.ViewModel;

/// <summary>
/// Base for an item in the Dialogues-tab folder tree: either a
/// <see cref="DialogueFolderNode"/> or a <see cref="DialogueLeafNode"/>.
/// The grouping is cosmetic (persisted under the editor-only
/// <c>dialogueFolders</c> key) — see <see cref="Model.DialogueFolderDef"/>.
/// </summary>
public abstract class DialogueTreeItem : ObservableObject, IFilterableTreeNode
{
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _isMultiSelected;
    /// <summary>
    /// Part of the Ctrl/Shift multi-selection used for group folder moves.
    /// Deliberately separate from the TreeView's own (single) selection: the
    /// anchor item keeps driving the detail pane, these only ride along on
    /// drag-drop. Highlighted via an ItemContainerStyle trigger.
    /// </summary>
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set { if (_isMultiSelected == value) return; _isMultiSelected = value; OnPropertyChanged(); }
    }
    private bool _isFilteredIn = true;
    /// <summary>Whether the sidebar's search box currently shows this row.</summary>
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    private bool _expandedBeforeFilter = true;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    /// <summary>Text the sidebar search matches against.</summary>
    public abstract string FilterKey { get; }
    public abstract System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren { get; }
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

    public override string FilterKey => _name;
    public override System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren => Children;
}

/// <summary>A dialogue leaf wrapping its <see cref="DialogueViewModel"/>.</summary>
public sealed class DialogueLeafNode : DialogueTreeItem
{
    public DialogueViewModel Dialogue { get; }
    public DialogueLeafNode(DialogueViewModel dialogue) { Dialogue = dialogue; }

    public override string FilterKey => Dialogue?.Display ?? "";
    public override System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren
        => System.Linq.Enumerable.Empty<IFilterableTreeNode>();
}
