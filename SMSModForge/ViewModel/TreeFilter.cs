using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.ViewModel;

/// <summary>
/// What a sidebar search box needs from a tree row. The editor grew several
/// parallel tree hierarchies (unit folders, dialogues, variables, integration
/// rules, characters/outfits) that share a shape but not a base class; this is
/// the small surface the filter needs, so one implementation serves them all.
/// </summary>
public interface IFilterableTreeNode
{
    /// <summary>False hides the row — the shared tree-item style binds
    /// Visibility to this.</summary>
    bool IsFilteredIn { get; set; }

    bool IsExpanded { get; set; }

    /// <summary>Text the search matches against (folder name / item label).</summary>
    string FilterKey { get; }

    /// <summary>Child rows; empty for a leaf.</summary>
    IEnumerable<IFilterableTreeNode> FilterChildren { get; }

    /// <summary>Remember / restore the manual expand state, so filtering can
    /// force folders open and clearing the box puts them back.</summary>
    void StashExpansion();
    void RestoreExpansion();
}

/// <summary>
/// Backs one sidebar search box. Holds the text and re-applies it over a live
/// tree, flipping row visibility rather than rebuilding — so selection,
/// expansion and drag-drop state all survive typing.
/// <para/>
/// A folder stays visible when its own name matches OR any descendant matches,
/// which keeps hits in context; a folder that matches by name shows its whole
/// contents.
/// </summary>
public sealed class TreeFilterViewModel : ObservableObject
{
    private readonly Func<IEnumerable<IFilterableTreeNode>> _roots;

    public TreeFilterViewModel(Func<IEnumerable<IFilterableTreeNode>> roots)
    {
        _roots = roots;
        ClearFilterCommand = new RelayCommand(() => FilterText = "");
    }

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= "";
            if (_filterText == value) return;
            bool wasFiltering = _filterText.Length > 0;
            _filterText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFilter));
            Apply(wasFiltering);
        }
    }

    public bool HasFilter => _filterText.Length > 0;

    public RelayCommand ClearFilterCommand { get; }

    /// <summary>Re-run the current filter — call after the tree is rebuilt, or
    /// the fresh nodes would all show regardless of the box.</summary>
    public void Reapply()
    {
        if (HasFilter) Apply(wasFiltering: true);
    }

    private void Apply(bool wasFiltering)
    {
        var roots = _roots()?.ToList() ?? new List<IFilterableTreeNode>();
        if (!HasFilter)
        {
            foreach (var r in roots) Clear(r);
            return;
        }
        if (!wasFiltering)
            foreach (var r in roots) Stash(r);
        foreach (var r in roots) Match(r, _filterText);
    }

    private static void Stash(IFilterableTreeNode n)
    {
        n.StashExpansion();
        foreach (var c in n.FilterChildren) Stash(c);
    }

    private static void Clear(IFilterableTreeNode n)
    {
        n.IsFilteredIn = true;
        n.RestoreExpansion();
        foreach (var c in n.FilterChildren) Clear(c);
    }

    /// <summary>Depth-first visibility pass; returns whether the subtree holds a
    /// match, which is what keeps a hit's ancestors on screen.</summary>
    private static bool Match(IFilterableTreeNode n, string needle)
    {
        var children = n.FilterChildren.ToList();
        bool selfHit = (n.FilterKey ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        if (children.Count == 0)
        {
            n.IsFilteredIn = selfHit;
            return selfHit;
        }

        bool childHit = false;
        foreach (var c in children)
        {
            bool hit = Match(c, needle);
            // A folder matching by name reveals everything inside it.
            if (selfHit) ShowAll(c);
            childHit |= hit;
        }
        n.IsFilteredIn = selfHit || childHit;
        if (n.IsFilteredIn) n.IsExpanded = true;
        return n.IsFilteredIn;
    }

    private static void ShowAll(IFilterableTreeNode n)
    {
        n.IsFilteredIn = true;
        foreach (var c in n.FilterChildren) ShowAll(c);
    }
}
