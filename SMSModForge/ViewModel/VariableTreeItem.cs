using System.Collections.ObjectModel;

namespace SMSModForge.ViewModel;

/// <summary>
/// Base for an item in the Variables-tab folder tree: either a
/// <see cref="VariableFolderNode"/> or a <see cref="VariableLeafNode"/>.
/// The grouping is cosmetic (persisted under the editor-only
/// <c>variableFolders</c> key). Mirrors <see cref="DialogueTreeItem"/>.
/// </summary>
public abstract class VariableTreeItem : ObservableObject
{
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }
}

/// <summary>A folder holding nested folders and variable leaves.</summary>
public sealed class VariableFolderNode : VariableTreeItem
{
    private string _name;
    public VariableFolderNode(string name)
    {
        _name = name ?? "";
        Children.CollectionChanged += (_, __) => OnPropertyChanged(nameof(Label));
    }

    public string Name
    {
        get => _name;
        set { _name = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); }
    }

    public ObservableCollection<VariableTreeItem> Children { get; } = new();

    /// <summary>Folder name with its direct-child count, e.g. "Voyeur (3)".</summary>
    public string Label => $"{_name} ({Children.Count})";
}

/// <summary>A variable leaf wrapping its <see cref="PackVariableViewModel"/>.</summary>
public sealed class VariableLeafNode : VariableTreeItem
{
    public PackVariableViewModel Variable { get; }
    public VariableLeafNode(PackVariableViewModel variable) { Variable = variable; }
}
