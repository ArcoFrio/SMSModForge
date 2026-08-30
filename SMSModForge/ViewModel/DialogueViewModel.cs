using System;
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

/// <summary>
/// Where a dragged node lands relative to the row it was dropped on.
/// The node list picks this from the cursor's position within the target
/// row — top/bottom edge bands mean "sibling before/after", the middle
/// band means "make it a child". Same convention as Unity's hierarchy,
/// the VS Code explorer and Explorer's folder tree.
/// </summary>
public enum NodeDropMode { Before, After, Into }

public sealed class DialogueViewModel : ObservableObject
{
    public DialogueDef Model { get; }
    public ObservableCollection<DialogueNodeViewModel> Nodes { get; }
    public ObservableCollection<NodeConditionViewModel> StartConditions { get; }

    /// <summary>
    /// Whether the runtime name still follows the display name.
    /// <para/>
    /// Same arrangement as characters: a NEW dialogue derives its key from
    /// whatever gets typed as the display name, and stops the moment the
    /// author edits the key themselves. An existing dialogue never re-derives,
    /// because its key is what logs and other packs already refer to it by and
    /// renaming it underneath them is not something a display-name edit should
    /// do.
    /// </summary>
    private bool _keyFollowsDisplay;

    /// <summary>Other dialogues in the pack, for keeping derived keys unique.
    /// Supplied by the owner rather than looked up, since a view model has no
    /// business knowing where the list lives.</summary>
    private Func<IEnumerable<DialogueViewModel>>? _siblings;

    public bool KeyIsDerived => _keyFollowsDisplay;

    /// <summary>Start deriving the key from the display name. Called for a
    /// dialogue the author has just added, never for one being loaded.</summary>
    public void DeriveKeyFromDisplayName(Func<IEnumerable<DialogueViewModel>> siblings)
    {
        _siblings = siblings;
        _keyFollowsDisplay = true;
    }

    private void DeriveKey()
    {
        var others = (_siblings?.Invoke() ?? Enumerable.Empty<DialogueViewModel>())
            .Where(d => !ReferenceEquals(d, this))
            .Select(d => d.Model.Key);
        Model.Key = CharacterDef.UniqueIdentifier(Model.DisplayName, others);
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Display));
    }

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
        set
        {
            Model.Key = value;
            // Typing a key is a decision, and it sticks: from here the display
            // name no longer touches it.
            _keyFollowsDisplay = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeyIsDerived));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            Model.DisplayName = value;
            OnPropertyChanged();
            if (_keyFollowsDisplay) DeriveKey();
            OnPropertyChanged(nameof(Display));
        }
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

    public bool ReplayOnTalk
    {
        get => Model.ReplayOnTalk;
        set { Model.ReplayOnTalk = value; OnPropertyChanged(); NotifyStartModeGating(); }
    }

    public bool Queued
    {
        get => Model.Queued;
        set { Model.Queued = value; OnPropertyChanged(); NotifyStartModeGating(); }
    }

    // ── Start-mode gating ────────────────────────────────────────────────
    //
    // Queued and ReplayOnTalk are alternatives, not a combination: Queued means
    // "never interrupt, wait for the button", ReplayOnTalk means "interrupt AND
    // stay on the button". Ticking both is not a third behaviour — the runtime
    // takes the Queued branch and ReplayOnTalk silently does nothing — so each
    // greys the other out.
    //
    // The gate is deliberately NOT a plain "other is off". A box that is
    // ALREADY TICKED stays enabled, so a manifest that somehow arrived with
    // both set can be corrected; disabling on the other alone would grey both
    // at once and leave no way back out of that state.

    /// <summary>Whether the Wait-for-Talk box can be toggled right now.</summary>
    public bool CanEditQueued => Queued || !ReplayOnTalk;

    /// <summary>Whether the replay box can be toggled right now.</summary>
    public bool CanEditReplayOnTalk => ReplayOnTalk || !Queued;

    private void NotifyStartModeGating()
    {
        OnPropertyChanged(nameof(CanEditQueued));
        OnPropertyChanged(nameof(CanEditReplayOnTalk));
    }

    public bool DebugConditions
    {
        get => Model.DebugConditions;
        set { Model.DebugConditions = value; OnPropertyChanged(); }
    }

    public bool QueueBehind
    {
        get => Model.QueueBehind;
        set { Model.QueueBehind = value; OnPropertyChanged(); }
    }

    public int Priority
    {
        get => Model.Priority;
        set { Model.Priority = value; OnPropertyChanged(); }
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

    /// <summary>
    /// Adds a new node. If <paramref name="parentId"/> is null, the node becomes
    /// a root. When <paramref name="template"/> is given, the new node inherits
    /// every authored field from it — see <see cref="CloneForNewNode"/>.
    /// </summary>
    public DialogueNodeViewModel AddNode(int? parentId = null, DialogueNodeKind kind = DialogueNodeKind.Text,
                                          DialogueNodeDef? template = null)
    {
        var def = template != null ? CloneForNewNode(template) : new DialogueNodeDef { Kind = kind };
        def.Id = NextNodeId();
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

    /// <summary>
    /// Deep-copies <paramref name="template"/> as the starting point for a new
    /// node: kind, actor, expression, outfit, duration/timeout, jump, and the
    /// whole conditions / start-actions / finish-actions lists all carry over,
    /// so adding the next line of a scene doesn't mean re-picking the speaker
    /// and their expression every time.
    /// <para/>
    /// Four fields deliberately don't:
    /// <list type="bullet">
    ///   <item><c>Id</c> — allocated by the caller.</item>
    ///   <item><c>Text</c> — the one thing that's always different.</item>
    ///   <item><c>Children</c> — a new node starts as a leaf; copying the list
    ///   would give two parents the same children and knot the tree.</item>
    ///   <item><c>Tag</c> — it's a jump TARGET name. Two nodes carrying the
    ///   same tag makes <c>Content.FindByTag</c> ambiguous (it returns whichever
    ///   it enumerates first), so every jump aimed at the original could
    ///   silently start landing on the copy.</item>
    /// </list>
    /// </summary>
    private static DialogueNodeDef CloneForNewNode(DialogueNodeDef template)
    {
        var def = Services.EditorClipboard.CloneOne(template);
        def.Text = "";
        def.Tag = null;
        def.Children = new List<int>();
        return def;
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
    /// a new root. Returns the pasted subtree's root so the caller can select
    /// it; null when the clipboard held no nodes.</summary>
    public DialogueNodeViewModel? PasteNodes(DialogueNodeViewModel? target, NodePastePosition position)
    {
        var src = Services.EditorClipboard.NodeSubtree;
        if (src == null || src.Count == 0) return null;

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
                // Land it directly BELOW the node it was pasted onto — pasting
                // is how a line gets inserted mid-scene, and appending to the
                // end of the sibling row is never what that means.
                InsertAfterSibling(rootNewId, target.Id);
                break;
            default:
                Model.RootNodeIds.Add(rootNewId);
                break;
        }
        OnPropertyChanged(nameof(RootsSummary));
        RecomputeDepths();
        return Nodes.FirstOrDefault(n => n.Id == rootNewId);
    }

    /// <summary>
    /// Add a node next to <paramref name="nodeVm"/> in the tree — same parent
    /// if it has one, otherwise as a new root. Used by the editor's
    /// "+ Sibling" toolbar button so authors can splay choices / branches
    /// at the same level without having to first navigate to the parent
    /// and click "+ Child".
    /// </summary>
    public DialogueNodeViewModel? AddSibling(DialogueNodeViewModel nodeVm,
                                              DialogueNodeKind kind = DialogueNodeKind.Text,
                                              DialogueNodeDef? template = null)
    {
        if (nodeVm == null) return null;
        int? parentId = FindParentId(nodeVm.Id);
        var added = AddNode(parentId, kind, template);
        // AddNode appends to the parent's children (or the root list). A sibling
        // added from a selection belongs directly BELOW that selection, not at
        // the end of the row of siblings — otherwise inserting a line into the
        // middle of a scene silently sends it to the bottom.
        InsertAfterSibling(added.Id, nodeVm.Id);
        return added;
    }

    /// <summary>
    /// Moves <paramref name="id"/> to sit immediately after
    /// <paramref name="afterId"/> in whichever sibling list holds the latter
    /// (its parent's children, or the root list). No-op when the target isn't
    /// in a sibling list, which leaves the caller's append in place.
    /// </summary>
    private void InsertAfterSibling(int id, int afterId)
    {
        int? parentId = FindParentId(afterId);
        var siblings = parentId.HasValue
            ? Model.Nodes.First(n => n.Id == parentId.Value).Children
            : Model.RootNodeIds;

        if (!siblings.Contains(afterId)) return;
        // Detach first, THEN locate the anchor: removing an entry that sat
        // before the anchor shifts it, and an index captured beforehand would
        // land the node one slot too far down.
        siblings.Remove(id);
        siblings.Insert(siblings.IndexOf(afterId) + 1, id);
        RecomputeDepths();
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

    // ── Drag / drop reparenting ───────────────────────────────────────

    /// <summary>
    /// Re-homes <paramref name="dragged"/> relative to <paramref name="target"/>.
    /// <see cref="NodeDropMode.Into"/> appends it to the target's children;
    /// Before/After splice it into the target's own sibling list (the root
    /// list when the target is a root). A null target drops it at the end of
    /// the root list, which is what dropping into the empty space below the
    /// list means.
    /// <para/>
    /// The node keeps its whole subtree — only the one edge from its old
    /// parent is rewritten, so children ride along for free.
    /// <para/>
    /// Returns false (changing nothing) for the two no-op/illegal cases:
    /// dropping a node onto itself, and dropping it somewhere inside its own
    /// subtree — the latter would both create a cycle and strand every node
    /// between the two.
    /// </summary>
    public bool MoveNode(DialogueNodeViewModel dragged, DialogueNodeViewModel? target, NodeDropMode mode)
    {
        if (dragged == null) return false;
        int id = dragged.Id;

        if (target == null)
        {
            Detach(id);
            Model.RootNodeIds.Add(id);
            AfterStructureChanged();
            return true;
        }

        if (!CanMoveNode(dragged, target, mode)) return false;

        Detach(id);

        if (mode == NodeDropMode.Into)
        {
            target.Model.Children.Add(id);
        }
        else
        {
            // Sibling list of the target: its parent's children, or the root
            // list. Detach ran first, so indices here are already correct even
            // when the node is moving within its current parent.
            int? parentId = FindParentId(target.Id);
            var siblings = parentId.HasValue
                ? Model.Nodes.First(n => n.Id == parentId.Value).Children
                : Model.RootNodeIds;

            int at = siblings.IndexOf(target.Id);
            // An orphan target isn't in any sibling list; appending to the
            // roots is the only sane landing spot.
            if (at < 0) siblings.Add(id);
            else siblings.Insert(mode == NodeDropMode.After ? at + 1 : at, id);
        }

        AfterStructureChanged();
        return true;
    }

    /// <summary>
    /// Whether <see cref="MoveNode"/> would actually do something. Split out so
    /// the drag preview can grey out an illegal drop instead of letting the
    /// user commit a move that silently no-ops.
    /// <para/>
    /// A null target (the empty space under the list) is always legal — it
    /// means "make this a trailing root".
    /// </summary>
    public bool CanMoveNode(DialogueNodeViewModel dragged, DialogueNodeViewModel? target, NodeDropMode mode)
    {
        if (dragged == null) return false;
        if (target == null) return true;
        if (target.Id == dragged.Id) return false;
        // Dropping a node into its own subtree would create a cycle and strand
        // everything between the two.
        if (IsInSubtreeOf(target.Id, dragged.Id)) return false;
        return true;
    }

    /// <summary>True when <paramref name="candidateId"/> is <paramref name="ancestorId"/>
    /// itself or sits anywhere beneath it. Iterative + visited-guarded so a
    /// pre-existing cycle in the data can't hang the editor.</summary>
    private bool IsInSubtreeOf(int candidateId, int ancestorId)
    {
        if (candidateId == ancestorId) return true;
        var stack = new Stack<int>();
        var seen = new HashSet<int>();
        stack.Push(ancestorId);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            if (!seen.Add(cur)) continue;
            var def = Model.Nodes.FirstOrDefault(n => n.Id == cur);
            if (def == null) continue;
            foreach (var c in def.Children)
            {
                if (c == candidateId) return true;
                stack.Push(c);
            }
        }
        return false;
    }

    /// <summary>Removes every inbound edge to <paramref name="id"/> — the one
    /// parent that lists it as a child, plus the root list. Mirrors what
    /// <see cref="RemoveNode"/> scrubs, minus deleting the node itself.</summary>
    private void Detach(int id)
    {
        foreach (var n in Model.Nodes) n.Children.RemoveAll(c => c == id);
        Model.RootNodeIds.RemoveAll(r => r == id);
    }

    private void AfterStructureChanged()
    {
        OnPropertyChanged(nameof(RootsSummary));
        RecomputeDepths();
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
        var order = new List<int>();
        foreach (var rootId in Model.RootNodeIds)
            AssignDepth(rootId, 0, depths, choiceChildren, order);

        foreach (var nodeVm in Nodes)
        {
            nodeVm.Depth = depths.TryGetValue(nodeVm.Id, out var d) ? d : 0;
            nodeVm.IsChoiceChild = choiceChildren.Contains(nodeVm.Id);
        }

        ReorderToTreeOrder(order);
    }

    private void AssignDepth(int id, int depth, Dictionary<int, int> depths,
                             HashSet<int> choiceChildren, List<int> order)
    {
        if (depths.ContainsKey(id)) return; // cycle / shared-child guard
        depths[id] = depth;
        order.Add(id);
        var def = Model.Nodes.FirstOrDefault(n => n.Id == id);
        if (def == null) return;
        // A Choice node's direct children are answer buttons.
        if (def.Kind == DialogueNodeKind.Choice)
            foreach (var childId in def.Children) choiceChildren.Add(childId);
        foreach (var childId in def.Children)
            AssignDepth(childId, depth + 1, depths, choiceChildren, order);
    }

    /// <summary>
    /// Rewrites the node list into DFS preorder — the order the rows are
    /// actually indented to look like. Indentation alone used to be the only
    /// tree cue while list position stayed at "whenever the node was added",
    /// so a child could render far away from its parent. Deriving position
    /// from the tree is what makes drag-and-drop legible: dropping between
    /// two rows now lands exactly where the preview line shows.
    /// <para/>
    /// Nodes unreachable from any root keep their relative order and go last,
    /// at depth 0 — they stay visible (and conspicuously un-indented) rather
    /// than silently disappearing.
    /// </summary>
    private void ReorderToTreeOrder(List<int> order)
    {
        var rank = new Dictionary<int, int>();
        for (int i = 0; i < order.Count; i++) rank[order[i]] = i;

        var desired = Nodes.Where(n => rank.ContainsKey(n.Id)).OrderBy(n => rank[n.Id])
                           .Concat(Nodes.Where(n => !rank.ContainsKey(n.Id)))
                           .ToList();

        // In-place Move only. Clear()+re-add would null the ListBox's bound
        // SelectedItem, and the TwoWay binding writes that null straight back
        // into SelectedNode — the same hazard that was silently wiping fields
        // elsewhere in the editor.
        for (int i = 0; i < desired.Count; i++)
        {
            int cur = Nodes.IndexOf(desired[i]);
            if (cur != i) Nodes.Move(cur, i);
        }

        // Mirror into the model so saved JSON reads top-to-bottom the way the
        // editor shows it. Runtime resolves nodes by id, so array order is
        // presentation-only and safe to normalise.
        Model.Nodes.Clear();
        Model.Nodes.AddRange(desired.Select(n => n.Model));
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
                    ["level"] = DefaultLevelTokenForRoomTalk(Model.LegacyRoomTalk),
                },
            };
            Model.StartConditions.Insert(0, def);
        }

        // Re-build the front of the observable list as a locked VM.
        if (StartConditions.Count > 0 && StartConditions[0].IsLocked) return;
        var lockedVm = new NodeConditionViewModel(Model.StartConditions[0],
                                                  removeCallback: RemoveStartCondition,
                                                  isLocked: true);
        // "Prioritize over vanilla" is only meaningful in a room that HAS a
        // vanilla entry dialogue, and that follows from the level — so the
        // checkbox's availability has to track edits to this row.
        lockedVm.PropertyChanged += (_, __) => OnPropertyChanged(nameof(LevelHasVanillaRoomTalk));
        StartConditions.Insert(0, lockedVm);
    }

    /// <summary>
    /// Does this dialogue's level have a vanilla roomtalk to take priority
    /// over? False for pack-authored levels and for the vanilla levels that
    /// ship without one, where the checkbox would have nothing to suppress.
    /// </summary>
    public bool LevelHasVanillaRoomTalk => Model.VanillaRoomTalkAvailable;

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
