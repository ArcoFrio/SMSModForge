using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for one authored <see cref="DialogueDef"/>. Owns observable
/// collections of the dialogue's nodes (flat) and start conditions; the
/// editor handles parent/child node wiring via explicit "add child to
/// selected" / "make root" actions because nesting in WPF list controls is
/// painful to do well, and a flat indexed view is easier to scan.
/// <para/>
/// Every dialogue is guaranteed to have a <see cref="NodeConditionTypes.LevelActive"/>
/// at index 0 of <see cref="StartConditions"/> — auto-injected on
/// construction and pinned non-removable so it can't be deleted by
/// mistake. The pinned condition's level token is what tells the
/// dispatcher which place must be active for this dialogue to start.
/// </summary>
/// <summary>Where a pasted node subtree's root attaches, relative to the
/// selected node.</summary>
public enum NodePastePosition { Sibling, Child, Root }

public sealed class DialogueViewModel : ObservableObject
{
    public DialogueDef Model { get; }
    public ObservableCollection<DialogueNodeViewModel> Nodes { get; }
    public ObservableCollection<NodeConditionViewModel> StartConditions { get; }

    public DialogueViewModel(DialogueDef model)
    {
        Model = model;
        Nodes = new ObservableCollection<DialogueNodeViewModel>(model.Nodes.Select(n => new DialogueNodeViewModel(n)));
        StartConditions = new ObservableCollection<NodeConditionViewModel>();

        EnsureLevelActiveFirst();
        // Hydrate the remaining (non-locked) start conditions from the
        // model. EnsureLevelActiveFirst above only adds the pinned slot.
        for (int i = 1; i < model.StartConditions.Count; i++)
        {
            var def = model.StartConditions[i];
            StartConditions.Add(new NodeConditionViewModel(def, removeCallback: RemoveStartCondition));
        }

        RecomputeDepths();

        // Start-condition copy/paste (the pinned LevelActive at index 0 is
        // preserved on overwrite and excluded from copy).
        CopyStartConditionsCommand      = new RelayCommand(
            () => Services.EditorClipboard.SetConditions(Model.StartConditions.Skip(1).ToList()),
            () => Model.StartConditions.Count > 1);
        PasteStartConditionsCommand     = new RelayCommand(() => PasteStartConditions(overwrite: false),
                                                           () => Services.EditorClipboard.HasConditions);
        OverwriteStartConditionsCommand = new RelayCommand(() => PasteStartConditions(overwrite: true),
                                                           () => Services.EditorClipboard.HasConditions);
    }

    public RelayCommand CopyStartConditionsCommand { get; }
    public RelayCommand PasteStartConditionsCommand { get; }
    public RelayCommand OverwriteStartConditionsCommand { get; }

    private void PasteStartConditions(bool overwrite)
    {
        var src = Services.EditorClipboard.Conditions;
        if (src == null || src.Count == 0) return;
        if (overwrite)
        {
            // Drop every non-pinned row (keep the LevelActive at index 0).
            while (Model.StartConditions.Count > 1) Model.StartConditions.RemoveAt(Model.StartConditions.Count - 1);
            while (StartConditions.Count > 1) StartConditions.RemoveAt(StartConditions.Count - 1);
        }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            Model.StartConditions.Add(def);
            StartConditions.Add(new NodeConditionViewModel(def, removeCallback: RemoveStartCondition));
        }
    }

    public string Key
    {
        get => Model.Key;
        set { Model.Key = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set { Model.DisplayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string RoomTalk
    {
        get => Model.RoomTalk;
        set { Model.RoomTalk = value; OnPropertyChanged(); }
    }

    public bool DisableVanillaTrigger
    {
        get => Model.DisableVanillaTrigger;
        set { Model.DisableVanillaTrigger = value; OnPropertyChanged(); }
    }

    public bool OneShot
    {
        get => Model.OneShot;
        set { Model.OneShot = value; OnPropertyChanged(); }
    }

    public bool Queued
    {
        get => Model.Queued;
        set { Model.Queued = value; OnPropertyChanged(); }
    }

    public bool DebugConditions
    {
        get => Model.DebugConditions;
        set { Model.DebugConditions = value; OnPropertyChanged(); }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;

    /// <summary>Comma-separated root ids for the right-pane summary.</summary>
    public string RootsSummary => Model.RootNodeIds.Count == 0 ? "(no root)" : string.Join(", ", Model.RootNodeIds);

    // ── Node ops ──────────────────────────────────────────────────────

    /// <summary>Allocates a fresh node id (max existing + 1, starting at 1).</summary>
    private int NextNodeId() => Model.Nodes.Count == 0 ? 1 : Model.Nodes.Max(n => n.Id) + 1;

    /// <summary>
    /// Best-effort inference of which level is active when a node plays, used to
    /// filter the editor's overlay autocomplete. Walks the node's ancestor chain
    /// (root → node) and returns the most recent <c>TransitionLevels.toLevel</c>
    /// found in any node's start actions along that path; if none, falls back to
    /// the dialogue's pinned <c>LevelActive</c> start-condition level. Returns ""
    /// when it can't tell. Purely advisory — the overlay field stays editable, so
    /// an imperfect guess on branching dialogues just means a broader dropdown.
    /// </summary>
    public string InferLevelTokenForNode(int nodeId)
    {
        // Build the root→node path by walking parents (tree case; guarded vs cycles).
        var path = new List<int>();
        int? cur = nodeId;
        var guard = new HashSet<int>();
        while (cur.HasValue && guard.Add(cur.Value))
        {
            path.Add(cur.Value);
            cur = FindParentId(cur.Value);
        }
        path.Reverse();

        string level = "";
        foreach (var id in path)
        {
            var def = Model.Nodes.FirstOrDefault(n => n.Id == id);
            if (def == null) continue;
            foreach (var a in def.ActionsOnStart)
                if (a.Type == NodeActionTypes.TransitionLevels &&
                    a.Params.TryGetValue("toLevel", out var to) && !string.IsNullOrEmpty(to))
                    level = to;
        }
        if (!string.IsNullOrEmpty(level)) return level;

        var lvlCond = Model.StartConditions.FirstOrDefault(c => c.Type == NodeConditionTypes.LevelActive);
        if (lvlCond?.Params != null && lvlCond.Params.TryGetValue("level", out var lv))
            return lv ?? "";
        return "";
    }

    /// <summary>Adds a new node. If <paramref name="parentId"/> is null, the node becomes a root.</summary>
    public DialogueNodeViewModel AddNode(int? parentId = null, DialogueNodeKind kind = DialogueNodeKind.Text)
    {
        var def = new DialogueNodeDef { Id = NextNodeId(), Kind = kind };
        Model.Nodes.Add(def);
        if (parentId.HasValue)
        {
            var parent = Model.Nodes.FirstOrDefault(n => n.Id == parentId.Value);
            if (parent != null) parent.Children.Add(def.Id);
        }
        else
        {
            Model.RootNodeIds.Add(def.Id);
        }
        var vm = new DialogueNodeViewModel(def);
        Nodes.Add(vm);
        OnPropertyChanged(nameof(RootsSummary));
        RecomputeDepths();
        return vm;
    }

    public void RemoveNode(DialogueNodeViewModel nodeVm)
    {
        int id = nodeVm.Id;
        Model.Nodes.Remove(nodeVm.Model);
        Nodes.Remove(nodeVm);
        // Scrub references from other nodes' children + the root list. This
        // keeps the tree consistent even if the user removes a mid-tree node.
        foreach (var n in Model.Nodes) n.Children.RemoveAll(c => c == id);
        Model.RootNodeIds.RemoveAll(r => r == id);
        OnPropertyChanged(nameof(RootsSummary));
        RecomputeDepths();
    }

    public void ToggleRoot(DialogueNodeViewModel nodeVm)
    {
        int id = nodeVm.Id;
        if (Model.RootNodeIds.Contains(id)) Model.RootNodeIds.Remove(id);
        else Model.RootNodeIds.Add(id);
        OnPropertyChanged(nameof(RootsSummary));
        RecomputeDepths();
    }

    // ── Node copy / paste (cross-dialogue via Services.EditorClipboard) ──

    /// <summary>Copies <paramref name="node"/> and its whole descendant subtree
    /// to the editor clipboard (deep-cloned). The copied node is the subtree
    /// root (first entry).</summary>
    public void CopyNode(DialogueNodeViewModel? node)
    {
        if (node == null) return;
        var collected = new List<DialogueNodeDef>();
        var seen = new HashSet<int>();
        void Collect(int id)
        {
            if (!seen.Add(id)) return;
            var def = Model.Nodes.FirstOrDefault(n => n.Id == id);
            if (def == null) return;
            collected.Add(def);
            foreach (var c in def.Children) Collect(c);
        }
        Collect(node.Id);
        Services.EditorClipboard.SetNodes(collected);
    }

    /// <summary>Pastes the clipboard node subtree into this dialogue with fresh
    /// ids, wiring its root as a sibling/child of <paramref name="target"/> or as
    /// a new root.</summary>
    public void PasteNodes(DialogueNodeViewModel? target, NodePastePosition position)
    {
        var src = Services.EditorClipboard.NodeSubtree;
        if (src == null || src.Count == 0) return;

        var pasted = Services.EditorClipboard.Clone(src);   // independent of clipboard
        int next = NextNodeId();
        var idMap = new Dictionary<int, int>();
        foreach (var def in pasted) idMap[def.Id] = next++;
        foreach (var def in pasted)
        {
            def.Id = idMap[def.Id];
            def.Children = def.Children.Where(idMap.ContainsKey).Select(c => idMap[c]).ToList();
        }
        int rootNewId = pasted[0].Id;   // subtree root = the originally-copied node

        foreach (var def in pasted)
        {
            Model.Nodes.Add(def);
            Nodes.Add(new DialogueNodeViewModel(def));
        }

        switch (position)
        {
            case NodePastePosition.Child when target != null:
                target.Model.Children.Add(rootNewId);
                break;
            case NodePastePosition.Sibling when target != null:
                var pid = FindParentId(target.Id);
                if (pid.HasValue) Model.Nodes.First(n => n.Id == pid.Value).Children.Add(rootNewId);
                else Model.RootNodeIds.Add(rootNewId);
                break;
            default:
                Model.RootNodeIds.Add(rootNewId);
                break;
        }
        OnPropertyChanged(nameof(RootsSummary));
        RecomputeDepths();
    }

    /// <summary>
    /// Add a node next to <paramref name="nodeVm"/> in the tree — same parent
    /// if it has one, otherwise as a new root. Used by the editor's
    /// "+ Sibling" toolbar button so authors can splay choices / branches
    /// at the same level without having to first navigate to the parent
    /// and click "+ Child".
    /// </summary>
    public DialogueNodeViewModel? AddSibling(DialogueNodeViewModel nodeVm,
                                              DialogueNodeKind kind = DialogueNodeKind.Text)
    {
        if (nodeVm == null) return null;
        int? parentId = FindParentId(nodeVm.Id);
        return AddNode(parentId, kind);
    }

    /// <summary>
    /// Returns the id of the node whose <c>Children</c> list contains
    /// <paramref name="childId"/>, or null if the child is a root /
    /// orphan. O(N) scan — N is the per-dialogue node count, which is
    /// typically small.
    /// </summary>
    private int? FindParentId(int childId)
    {
        foreach (var n in Model.Nodes)
            if (n.Children.Contains(childId)) return n.Id;
        return null;
    }

    /// <summary>
    /// Walks the tree in DFS preorder from <see cref="DialogueDef.RootNodeIds"/>,
    /// assigning each <see cref="DialogueNodeViewModel.Depth"/>. Nodes that
    /// aren't reachable from any root (orphans) keep depth 0 and surface in
    /// the list without indentation — making them visually distinct so the
    /// author notices the dangling state.
    /// </summary>
    public void RecomputeDepths()
    {
        var depths = new Dictionary<int, int>();
        var choiceChildren = new HashSet<int>();
        foreach (var rootId in Model.RootNodeIds)
            AssignDepth(rootId, 0, depths, choiceChildren);

        foreach (var nodeVm in Nodes)
        {
            nodeVm.Depth = depths.TryGetValue(nodeVm.Id, out var d) ? d : 0;
            nodeVm.IsChoiceChild = choiceChildren.Contains(nodeVm.Id);
        }
    }

    private void AssignDepth(int id, int depth, Dictionary<int, int> depths, HashSet<int> choiceChildren)
    {
        if (depths.ContainsKey(id)) return; // cycle / shared-child guard
        depths[id] = depth;
        var def = Model.Nodes.FirstOrDefault(n => n.Id == id);
        if (def == null) return;
        // A Choice node's direct children are answer buttons.
        if (def.Kind == DialogueNodeKind.Choice)
            foreach (var childId in def.Children) choiceChildren.Add(childId);
        foreach (var childId in def.Children)
            AssignDepth(childId, depth + 1, depths, choiceChildren);
    }

    // ── Start condition ops ───────────────────────────────────────────

    /// <summary>
    /// Add a non-locked start condition (appended after the pinned
    /// LevelActive at index 0). Always uses VariableEquals as the
    /// initial type — the user picks from the combo afterwards.
    /// </summary>
    public NodeConditionViewModel AddStartCondition()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.StartConditions.Add(def);
        var vm = new NodeConditionViewModel(def, removeCallback: RemoveStartCondition);
        StartConditions.Add(vm);
        return vm;
    }

    /// <summary>
    /// Add an empty AND group (the author can flip it to OR and add child
    /// conditions). Appended after the pinned LevelActive at index 0, same as
    /// <see cref="AddStartCondition"/>.
    /// </summary>
    public NodeConditionViewModel AddStartConditionGroup()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
        Model.StartConditions.Add(def);
        var vm = new NodeConditionViewModel(def, removeCallback: RemoveStartCondition);
        StartConditions.Add(vm);
        return vm;
    }

    /// <summary>
    /// Remove a start condition. Pinned (locked) rows never reach here
    /// because <see cref="NodeConditionViewModel.RemoveCommand"/> is
    /// gated by <see cref="NodeConditionViewModel.IsLocked"/>.
    /// </summary>
    public void RemoveStartCondition(NodeConditionViewModel c)
    {
        if (c.IsLocked) return; // defensive
        Model.StartConditions.Remove(c.Model);
        StartConditions.Remove(c);
    }

    /// <summary>
    /// Ensures the dialogue's start-conditions list begins with a
    /// <see cref="NodeConditionTypes.LevelActive"/> condition, inserting
    /// one (with a placeholder empty level) if missing. The injected
    /// row is marked <see cref="NodeConditionViewModel.IsLocked"/> so
    /// the minus button is disabled on it.
    /// <para/>
    /// Called from the constructor on every dialogue load. The model is
    /// mutated so the auto-injection round-trips through save / reload.
    /// </summary>
    private void EnsureLevelActiveFirst()
    {
        if (Model.StartConditions.Count == 0 ||
            Model.StartConditions[0].Type != NodeConditionTypes.LevelActive)
        {
            var def = new NodeConditionDef
            {
                Type = NodeConditionTypes.LevelActive,
                // Default the level param to the dialogue's roomtalk-implied
                // level when we can compute it; otherwise leave empty for
                // the user to pick. The MainViewModel re-runs this on
                // construction *and* whenever the dialogue is added, so the
                // implicit level matches the roomtalk at create-time.
                Params = new Dictionary<string, string>
                {
                    ["level"] = DefaultLevelTokenForRoomTalk(Model.RoomTalk),
                },
            };
            Model.StartConditions.Insert(0, def);
        }

        // Re-build the front of the observable list as a locked VM.
        if (StartConditions.Count > 0 && StartConditions[0].IsLocked) return;
        var lockedVm = new NodeConditionViewModel(Model.StartConditions[0],
                                                  removeCallback: RemoveStartCondition,
                                                  isLocked: true);
        StartConditions.Insert(0, lockedVm);
    }

    /// <summary>
    /// Pure helper: turn a roomtalk token (<c>vanilla:&lt;name&gt;</c> /
    /// <c>place:&lt;key&gt;</c>) into the matching level token used by
    /// <c>LevelActive</c> (<c>vanilla:&lt;goName&gt;</c> /
    /// <c>place:&lt;key&gt;</c>). Returns empty when the mapping is
    /// unknown — the author then picks from the dropdown in the editor.
    /// </summary>
    public static string DefaultLevelTokenForRoomTalk(string roomTalkToken)
    {
        if (string.IsNullOrEmpty(roomTalkToken)) return "";
        int colon = roomTalkToken.IndexOf(':');
        if (colon <= 0 || colon == roomTalkToken.Length - 1) return "";
        var scheme = roomTalkToken.Substring(0, colon);
        var rest = roomTalkToken.Substring(colon + 1);
        if (scheme == "vanilla")
        {
            foreach (var p in VanillaPlaces.All)
                if (p.RoomTalkName == rest) return "vanilla:" + p.GoName;
            return "";
        }
        if (scheme == "place") return "place:" + rest;
        return "";
    }
}
