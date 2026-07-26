using System.Collections.ObjectModel;

namespace SMSModForge.ViewModel;

/// <summary>
/// Base for an item in the Integration-tab folder tree: either an
/// <see cref="IntegrationFolderNode"/> or an <see cref="IntegrationLeafNode"/>.
/// The grouping is cosmetic (persisted under the editor-only
/// <c>integrationFolders</c> key). Mirrors <see cref="VariableTreeItem"/>.
/// </summary>
public abstract class IntegrationTreeItem : ObservableObject, IFilterableTreeNode
{
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _isMultiSelected;
    /// <summary>Ctrl/Shift multi-selection for group folder moves — see
    /// <see cref="DialogueTreeItem.IsMultiSelected"/>.</summary>
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

/// <summary>A folder holding nested folders and rule leaves.</summary>
public sealed class IntegrationFolderNode : IntegrationTreeItem
{
    private string _name;
    public IntegrationFolderNode(string name)
    {
        _name = name ?? "";
        Children.CollectionChanged += (_, __) => OnPropertyChanged(nameof(Label));
    }

    public string Name
    {
        get => _name;
        set { _name = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); }
    }

    public ObservableCollection<IntegrationTreeItem> Children { get; } = new();

    /// <summary>Folder name with its direct-child count, e.g. "Schedule (19)".</summary>
    public string Label => $"{_name} ({Children.Count})";

    public override string FilterKey => _name;
    public override System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren => Children;
}

/// <summary>A rule leaf wrapping its <see cref="UpdateRuleViewModel"/>.</summary>
public sealed class IntegrationLeafNode : IntegrationTreeItem
{
    public UpdateRuleViewModel Rule { get; }
    public IntegrationLeafNode(UpdateRuleViewModel rule) { Rule = rule; }

    public override string FilterKey => Rule?.Display ?? "";
    public override System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren
        => System.Linq.Enumerable.Empty<IFilterableTreeNode>();
}
