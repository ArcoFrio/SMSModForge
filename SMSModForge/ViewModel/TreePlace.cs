using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.ViewModel;

/// <summary>
/// Where a node is in one of the editor's folder trees, so that what is picked
/// in the tree can be found again once the tree has been built again - which
/// replaces every node (1.10.0).
/// <para/>
/// A tree built again kept the node that had been picked: a node no longer in
/// it. Whatever was done next to "the picked folder" was done to that one and
/// went nowhere - "+ Folder", an undo of it, and "+ Folder" again added a
/// folder inside the one the undo had taken away, and nothing was added at all
/// (found by the walk over every control, UndoEveryControlTests).
/// </summary>
internal static class TreePlace
{
    /// <summary>The names of the folders from the top down to <paramref name="node"/> - its own last, when it
    /// is a folder - or null when it is not in the tree.</summary>
    public static List<string>? FoldersTo<T>(IEnumerable<T> roots, T node, Func<T, IEnumerable<T>?> children, Func<T, string?> folderName)
        where T : class
    {
        foreach (var root in roots)
        {
            if (ReferenceEquals(root, node)) return folderName(root) is { } own ? new List<string> { own } : new List<string>();
            var inside = children(root);
            if (inside == null) continue;
            var deeper = FoldersTo(inside, node, children, folderName);
            if (deeper == null) continue;
            deeper.Insert(0, folderName(root) ?? "");
            return deeper;
        }
        return null;
    }

    /// <summary>The folder those names lead to, or null when one of them is no longer there.</summary>
    public static T? FolderAt<T>(IEnumerable<T> roots, IReadOnlyList<string> names, Func<T, IEnumerable<T>?> children, Func<T, string?> folderName)
        where T : class
    {
        if (names.Count == 0) return null;
        T? at = null;
        IEnumerable<T>? level = roots;
        foreach (string name in names)
        {
            at = level?.FirstOrDefault(n => folderName(n) == name);
            if (at == null) return null;
            level = children(at);
        }
        return at;
    }

    /// <summary>Open every folder on the way down to <paramref name="node"/>, so its row is drawn; false when it
    /// is not in the tree.</summary>
    public static bool OpenTo<T>(IEnumerable<T> roots, T node, Func<T, IEnumerable<T>?> children, Action<T> open)
        where T : class
    {
        foreach (var root in roots)
        {
            if (ReferenceEquals(root, node)) return true;
            var inside = children(root);
            if (inside == null || !OpenTo(inside, node, children, open)) continue;
            open(root);
            return true;
        }
        return false;
    }

    /// <summary>The first node, top down, that <paramref name="wanted"/> says yes to.</summary>
    public static T? First<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>?> children, Func<T, bool> wanted)
        where T : class
    {
        foreach (var root in roots)
        {
            if (wanted(root)) return root;
            var inside = children(root);
            if (inside != null && First(inside, children, wanted) is { } deeper) return deeper;
        }
        return null;
    }
}
