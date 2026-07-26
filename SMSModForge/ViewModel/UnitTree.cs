using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Shared folder-tree infrastructure for the left-bar unit lists (Actors,
/// Places, Scenes, Wallpapers, Music, SFX). One implementation instead of
/// six more copies of the hand-rolled Dialogues / Variables / Integration
/// trees: the item types are shared, so the window can wire ONE set of
/// selection / drag-drop / multi-select handlers for every tree, and the
/// per-tab differences (key, display text, what selecting a leaf means)
/// are delegates on <see cref="UnitTreeController"/>.
/// </summary>
public abstract class UnitTreeItem : ObservableObject, IFilterableTreeNode
{
    /// <summary>Text the sidebar search matches — folder name / leaf label.</summary>
    public abstract string FilterKey { get; }
    public abstract System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren { get; }

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
    /// <summary>Whether the sidebar's search box currently shows this row. The
    /// shared tree-item style binds Visibility to it, so filtering is a
    /// property flip rather than a rebuild — the tree structure, selection and
    /// drag-drop state all survive typing in the box.</summary>
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    private bool _expandedBeforeFilter = true;
    /// <summary>Remembers the manual expand/collapse state while a filter is
    /// forcing folders open, so clearing the box restores what you had.</summary>
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;
}

/// <summary>A folder holding nested folders and unit leaves.</summary>
public sealed class UnitFolderNode : UnitTreeItem
{
    private string _name;
    public UnitFolderNode(string name)
    {
        _name = name ?? "";
        Children.CollectionChanged += (_, __) => OnPropertyChanged(nameof(Label));
    }

    public string Name
    {
        get => _name;
        set { _name = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); }
    }

    public ObservableCollection<UnitTreeItem> Children { get; } = new();

    /// <summary>Folder name with its direct-child count, e.g. "Rooms (4)".</summary>
    public string Label => $"{_name} ({Children.Count})";

    public override string FilterKey => _name;
    public override System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren => Children;
}

/// <summary>A unit leaf wrapping the tab's item view model. Display text is
/// resolved through the owning controller's delegate and tracks the item's
/// own property changes (renames update the tree label live).</summary>
public sealed class UnitLeafNode : UnitTreeItem
{
    private readonly Func<object, string> _displayOf;

    public UnitLeafNode(object item, Func<object, string> displayOf)
    {
        Item = item;
        _displayOf = displayOf;
        if (item is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += (_, __) => OnPropertyChanged(nameof(DisplayText));
    }

    public object Item { get; }
    public string DisplayText => _displayOf(Item);

    public override string FilterKey => DisplayText;
    public override System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren
        => System.Linq.Enumerable.Empty<IFilterableTreeNode>();
}

/// <summary>
/// The behavior of one tab's folder tree: build from the pack's folder defs
/// + flat item list, keep the tree authoritative while the editor is open,
/// write membership back (by unit key) on sync, and provide the placement /
/// move / removal operations every tree shares. The window's shared
/// handlers only ever talk to this type.
/// </summary>
public sealed class UnitTreeController : ObservableObject
{
    private readonly Func<List<UnitFolderDef>> _folderDefs;   // live accessor — Pack swaps on load
    private readonly Func<object, string> _keyOf;
    private readonly Func<object, string> _displayOf;
    private readonly Action<object> _onLeafSelected;
    private readonly Action _checkpoint;

    public UnitTreeController(Func<List<UnitFolderDef>> folderDefs,
                              Func<object, string> keyOf,
                              Func<object, string> displayOf,
                              Action<object> onLeafSelected,
                              Action checkpoint)
    {
        _folderDefs = folderDefs;
        _keyOf = keyOf;
        _displayOf = displayOf;
        _onLeafSelected = onLeafSelected;
        _checkpoint = checkpoint;
        Filter = new TreeFilterViewModel(() => Tree);
    }

    public ObservableCollection<UnitTreeItem> Tree { get; } = new();

    /// <summary>Backs this tab's sidebar search box.</summary>
    public TreeFilterViewModel Filter { get; }

    private UnitTreeItem? _selected;
    /// <summary>The tree's selected node. Selecting a leaf forwards to the
    /// tab's Selected&lt;Item&gt; property so the detail pane follows.</summary>
    public UnitTreeItem? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            OnPropertyChanged();
            if (value is UnitLeafNode leaf) _onLeafSelected(leaf.Item);
        }
    }

    // ── Build / persist ───────────────────────────────────────────────────

    /// <summary>Rebuild from the pack's folder defs over the current flat
    /// item list. Items not claimed by any folder land at the root.</summary>
    public void Build(System.Collections.IEnumerable items)
    {
        Tree.Clear();
        var byKey = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            var k = _keyOf(it);
            if (!string.IsNullOrEmpty(k) && !byKey.ContainsKey(k)) byKey[k] = it;
        }
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _folderDefs())
            Tree.Add(BuildFolder(f, byKey, placed));
        foreach (var it in items)
        {
            var k = _keyOf(it);
            if (string.IsNullOrEmpty(k) || !placed.Contains(k))
                Tree.Add(new UnitLeafNode(it, _displayOf));
        }
        Sort();
        // A rebuild replaces every node, so a live search box has to be
        // re-applied or the new nodes all show regardless of the filter.
        Filter.Reapply();
    }

    private UnitFolderNode BuildFolder(UnitFolderDef def, Dictionary<string, object> byKey, HashSet<string> placed)
    {
        var node = new UnitFolderNode(def.Name);
        foreach (var sub in def.Folders)
            node.Children.Add(BuildFolder(sub, byKey, placed));
        foreach (var key in def.Items)
            if (byKey.TryGetValue(key, out var it) && placed.Add(key))
                node.Children.Add(new UnitLeafNode(it, _displayOf));
        return node;
    }

    /// <summary>Write the tree's folder structure back into the pack's
    /// folder-def list (membership by unit key; root leaves stay implicit).</summary>
    public void SyncToModel()
    {
        var defs = _folderDefs();
        defs.Clear();
        foreach (var item in Tree)
            if (item is UnitFolderNode fn)
                defs.Add(FolderToDef(fn));
    }

    private UnitFolderDef FolderToDef(UnitFolderNode fn)
    {
        var def = new UnitFolderDef { Name = fn.Name };
        foreach (var c in fn.Children)
        {
            if (c is UnitFolderNode sub) def.Folders.Add(FolderToDef(sub));
            else if (c is UnitLeafNode leaf) def.Items.Add(_keyOf(leaf.Item));
        }
        return def;
    }

    // ── Shared operations ─────────────────────────────────────────────────

    public void Sort()
    {
        SortLevel(Tree);
    }

    private void SortLevel(ObservableCollection<UnitTreeItem> level)
    {
        var sorted = level
            .OrderBy(i => i is UnitFolderNode ? 0 : 1)   // folders first
            .ThenBy(i => i is UnitFolderNode f ? f.Name : ((UnitLeafNode)i).DisplayText,
                    StringComparer.OrdinalIgnoreCase)
            .ToList();
        level.Clear();
        foreach (var i in sorted) level.Add(i);
        foreach (var i in sorted)
            if (i is UnitFolderNode fn) SortLevel(fn.Children);
    }

    public void AddFolder()
    {
        var folder = new UnitFolderNode("New Folder");
        if (Selected is UnitFolderNode target)
        {
            target.Children.Insert(0, folder);
            target.IsExpanded = true;
        }
        else Tree.Add(folder);
        Sort();
        SyncToModel();
        Selected = folder;
    }

    /// <summary>Where a new / duplicated / pasted unit lands: the selected
    /// folder, the folder holding the selected leaf, else the root.</summary>
    private ObservableCollection<UnitTreeItem> DropTarget()
    {
        switch (Selected)
        {
            case UnitFolderNode f:
                f.IsExpanded = true;
                return f.Children;
            case UnitLeafNode l:
                return ParentChildren(l) ?? Tree;
            default:
                return Tree;
        }
    }

    /// <summary>Insert a just-added unit at the selection and select it.
    /// NOT a Build() — the new unit isn't in the folder defs yet, so a
    /// rebuild would always bounce it to the root.</summary>
    public void PlaceNew(object itemVm)
    {
        var leaf = new UnitLeafNode(itemVm, _displayOf);
        DropTarget().Add(leaf);
        Sort();
        SyncToModel();
        Selected = leaf;
    }

    public UnitLeafNode? FindLeaf(object itemVm) => FindLeafIn(Tree, itemVm);

    private static UnitLeafNode? FindLeafIn(ObservableCollection<UnitTreeItem> coll, object itemVm)
    {
        foreach (var c in coll)
        {
            if (c is UnitLeafNode l && ReferenceEquals(l.Item, itemVm)) return l;
            if (c is UnitFolderNode fn) { var found = FindLeafIn(fn.Children, itemVm); if (found != null) return found; }
        }
        return null;
    }

    /// <summary>Drop the leaf for a removed unit (the unit itself is removed
    /// by the tab's own command). No-op when it isn't in the tree.</summary>
    public void RemoveLeafFor(object itemVm)
    {
        var leaf = FindLeaf(itemVm);
        if (leaf == null) return;
        ParentChildren(leaf)?.Remove(leaf);
        SyncToModel();
        if (ReferenceEquals(Selected, leaf)) Selected = null;
    }

    /// <summary>When a FOLDER is selected: delete it and lift its children
    /// into its place (never deletes the units inside). Returns true when
    /// that's what happened — callers skip their unit-removal path then.</summary>
    public bool RemoveSelectedFolderLiftChildren()
    {
        if (Selected is not UnitFolderNode folder) return false;
        var parent = ParentChildren(folder) ?? Tree;
        int idx = parent.IndexOf(folder);
        parent.Remove(folder);
        foreach (var c in folder.Children.ToList()) parent.Insert(idx++, c);
        Sort();
        SyncToModel();
        Selected = null;
        return true;
    }

    public ObservableCollection<UnitTreeItem>? ParentChildren(UnitTreeItem item)
        => Tree.Contains(item) ? Tree : ParentIn(Tree, item);

    private static ObservableCollection<UnitTreeItem>? ParentIn(
        ObservableCollection<UnitTreeItem> coll, UnitTreeItem item)
    {
        foreach (var c in coll)
            if (c is UnitFolderNode fn)
            {
                if (fn.Children.Contains(item)) return fn.Children;
                var found = ParentIn(fn.Children, item);
                if (found != null) return found;
            }
        return null;
    }

    private static bool IsDescendant(UnitFolderNode folder, UnitTreeItem? maybe)
    {
        if (maybe == null) return false;
        foreach (var c in folder.Children)
        {
            if (c == maybe) return true;
            if (c is UnitFolderNode sub && IsDescendant(sub, maybe)) return true;
        }
        return false;
    }

    /// <summary>Move a dragged item onto a drop target (into a folder, beside
    /// a leaf, or to root when the target is null). A folder can't drop into
    /// itself or a descendant.</summary>
    public void Move(UnitTreeItem dragged, UnitTreeItem? target)
    {
        if (dragged == null || dragged == target) return;

        ObservableCollection<UnitTreeItem> dest;
        UnitFolderNode? destFolder = null;
        if (target is UnitFolderNode f) { dest = f.Children; destFolder = f; }
        else if (target is UnitLeafNode leaf) dest = ParentChildren(leaf) ?? Tree;
        else dest = Tree;

        if (dragged is UnitFolderNode df &&
            (df == destFolder || IsDescendant(df, destFolder) || IsDescendant(df, target)))
            return;

        var from = ParentChildren(dragged);
        if (from == null || from == dest) return;

        _checkpoint();
        from.Remove(dragged);
        dest.Add(dragged);
        if (destFolder != null) destFolder.IsExpanded = true;
        Sort();
        SyncToModel();
    }

    // ── Multi-selection support (shared window handlers) ──────────────────

    public IEnumerable<UnitTreeItem> FlattenAll() => FlattenAllIn(Tree);

    private static IEnumerable<UnitTreeItem> FlattenAllIn(ObservableCollection<UnitTreeItem> coll)
    {
        foreach (var c in coll)
        {
            yield return c;
            if (c is UnitFolderNode fn)
                foreach (var x in FlattenAllIn(fn.Children)) yield return x;
        }
    }

    public IEnumerable<UnitTreeItem> FlattenVisible() => FlattenVisibleIn(Tree);

    private static IEnumerable<UnitTreeItem> FlattenVisibleIn(ObservableCollection<UnitTreeItem> coll)
    {
        foreach (var c in coll)
        {
            yield return c;
            if (c is UnitFolderNode fn && fn.IsExpanded)
                foreach (var x in FlattenVisibleIn(fn.Children)) yield return x;
        }
    }

    public void ClearMultiSelection()
    {
        foreach (var i in FlattenAll()) i.IsMultiSelected = false;
    }

    /// <summary>Group-move every multi-selected item, skipping items already
    /// carried by a multi-selected ancestor folder; dissolves the group.</summary>
    public void MoveMultiSelected(UnitTreeItem? target)
    {
        var group = FlattenAll().Where(i => i.IsMultiSelected).ToList();
        var folders = group.OfType<UnitFolderNode>().ToList();
        foreach (var item in group)
        {
            if (folders.Any(f => f != item && IsDescendant(f, item))) continue;
            Move(item, target);
        }
        ClearMultiSelection();
    }
}
