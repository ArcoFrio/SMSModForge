using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.Validation;

namespace SMSModForge.ViewModel;

/// <summary>
/// Top-level VM bound by <c>MainWindow</c>. Owns the loaded pack, the selected
/// outfit (which drives the preview + editor panel), and the file-system root
/// for the pack (so file pickers can resolve relative paths).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private ModPack _pack = PackRepository.CreateEmpty("Untitled");
    public ModPack Pack
    {
        get => _pack;
        private set { _pack = value; OnPropertyChanged(); }
    }

    /// <summary>JSON snapshot of the pack as of the last load/save. Compared
    /// against the live serialization to detect unsaved edits — see
    /// <see cref="HasUnsavedChanges"/>.</summary>
    private string _savedSnapshot = "";

    /// <summary>Records the current pack state as "saved". Called after New,
    /// Open, and a successful Save.</summary>
    private void MarkSaved() => _savedSnapshot = PackRepository.Serialize(Pack);

    /// <summary>
    /// True when the in-memory pack differs from the last saved snapshot.
    /// Computed by re-serializing the model (the VMs write through to it), so
    /// it catches edits regardless of which field changed. The window's close
    /// handler reads this to prompt before discarding work.
    /// </summary>
    public bool HasUnsavedChanges => PackRepository.Serialize(Pack) != _savedSnapshot;

    /// <summary>Whole-editor undo/redo. Snapshots the pack at commit boundaries
    /// (driven by the window's focus + Ctrl+Z/Y handlers) and restores by
    /// reloading + rebinding. See <see cref="Services.UndoService"/>.</summary>
    public Services.UndoService Undo { get; }

    /// <summary>Restores an undo/redo snapshot: reload the pack, rebind every
    /// view-model, then re-select the dialogue + node that were selected before,
    /// so the editor lands back where the user was.</summary>
    private void OnUndoRestore(string json)
    {
        var pack = PackRepository.Deserialize(json);
        if (pack == null) return;

        string? dlgKey = SelectedDialogue?.Key;
        int? nodeId = SelectedNode?.Id;

        Undo.Suspended = true;
        try
        {
            Pack = pack;
            RebindAll();
            Validate();
        }
        finally { Undo.Suspended = false; }
        Undo.AbsorbCurrentAsBaseline();

        if (dlgKey != null)
        {
            var d = Dialogues.FirstOrDefault(x => x.Key == dlgKey);
            if (d != null)
            {
                SelectedDialogue = d;
                if (nodeId != null)
                    SelectedNode = d.Nodes.FirstOrDefault(n => n.Id == nodeId.Value);
            }
        }
    }

    public ObservableCollection<CharacterViewModel> Characters { get; } = new();
    public ObservableCollection<PlaceViewModel> Places { get; } = new();
    public ObservableCollection<VanillaPlaceExtensionViewModel> VanillaExtensions { get; } = new();
    public ObservableCollection<MapButtonViewModel> MapButtons { get; } = new();
    public ObservableCollection<DialogueViewModel> Dialogues { get; } = new();
    public ObservableCollection<ActorViewModel> Actors { get; } = new();
    public ObservableCollection<PackVariableViewModel> Variables { get; } = new();
    public ObservableCollection<SceneViewModel> Scenes { get; } = new();
    public ObservableCollection<WallpaperViewModel> Wallpapers { get; } = new();
    public ObservableCollection<MusicViewModel> Music { get; } = new();
    public ObservableCollection<SfxViewModel> Sfx { get; } = new();
    public ObservableCollection<UpdateRuleViewModel> IntegrationRules { get; } = new();

    /// <summary>
    /// The trigger-mode options shown in the Integration tab's per-rule combo box.
    /// Order is the order the user sees in the dropdown.
    /// </summary>
    public IReadOnlyList<UpdateRuleTriggerMode> UpdateRuleTriggerModes { get; } = new[]
    {
        UpdateRuleTriggerMode.OnRisingEdge,
        UpdateRuleTriggerMode.OnFallingEdge,
        UpdateRuleTriggerMode.WhilePassing,
        UpdateRuleTriggerMode.OnSceneLoad,
        UpdateRuleTriggerMode.OnDayChange,
    };

    /// <summary>
    /// World Map district picker options for the Map Buttons tab.
    /// Static (the district list never changes during a session).
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> WorldMapDistrictOptions { get; } = new();

    // ── Option lists for combo-box pickers ───────────────────────────

    /// <summary>
    /// All roomtalks (vanilla + pack-place-derived) available as dialogue
    /// parents. Tokens are <c>vanilla:&lt;name&gt;</c> or <c>place:&lt;key&gt;</c>.
    /// Rebuilt by <see cref="RebuildDialogueRoomTalkOptions"/>.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> RoomTalkOptions { get; } = new();

    /// <summary>All actor keys for node-actor pickers.</summary>
    public ObservableCollection<string> ActorOptions { get; } = new();

    /// <summary>
    /// Available busts for actor → bust pickers. Combines every vanilla bust
    /// under <c>2_Bust_Manager</c> (from <see cref="VanillaBusts"/>) with every
    /// pack-authored outfit's <see cref="OutfitDef.GameObjectName"/>. Tokens
    /// are bare GO names — the runtime does a single
    /// <see cref="UnityEngine.GameObject.Find"/> regardless of origin. Display
    /// labels carry an "(Vanilla — &lt;Character&gt;)" / "(Pack)" prefix so
    /// the dropdown stays scannable across 300+ entries.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> BustNameOptions { get; } = new();

    /// <summary>Variable names for action/condition pickers (pack source).</summary>
    public ObservableCollection<string> VariableNameOptions { get; } = new();

    /// <summary>Vanilla GC2 Global-Name variables (the 1.8E catalog) — the
    /// 'Vanilla' source on a Variable condition/action. Static, never changes
    /// for a session, so a plain list is fine for the picker's ItemsSource.</summary>
    public System.Collections.Generic.IReadOnlyList<string> VanillaGameVariableOptions
        => Model.VanillaGameVariables.AllNames;

    /// <summary>
    /// Levels available to a <see cref="NodeConditionTypes.LevelActive"/>
    /// condition. Every vanilla place (<c>vanilla:&lt;goName&gt;</c>) plus
    /// every pack place (<c>place:&lt;key&gt;</c>). Rebuilt whenever the
    /// pack's place list changes, same as <see cref="RoomTalkOptions"/>.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> LevelOptions { get; } = new();

    /// <summary>
    /// Bust GO names referenced by the pack's declared actors —
    /// specifically each actor's <see cref="ActorViewModel.DefaultBustKey"/>.
    /// Drives the <c>bustKey</c> dropdown on the <c>SetActorBust</c>
    /// dialogue-node action so the picker shows the busts most likely
    /// to be relevant (the ones actors in this pack actually use). The
    /// underlying combo stays <c>IsEditable</c> so authors can still
    /// type any bust GO name that isn't in the actor catalog.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> ActorBustOptions { get; } = new();

    /// <summary>
    /// Expression keys available to the <c>SetActorExpression</c>
    /// action. Combines the four vanilla expressions
    /// (<c>Happy/Angry/Sad/Flirty</c>) with any custom expression
    /// keys declared on this pack's actors. Editable — authors can
    /// type an expression name that maps to a custom bust child.
    /// </summary>
    public ObservableCollection<string> ExpressionKeyOptions { get; } = new();

    /// <summary>
    /// Scene keys available to the <c>ActivateScene</c> action's
    /// dropdown — one entry per <see cref="SceneViewModel"/> on this
    /// pack. The combo stays <c>IsEditable</c> so an author can still
    /// reference a scene that hasn't been added to the list yet (for
    /// example while reorganising the Scenes tab).
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> SceneOptions { get; } = new();

    /// <summary>
    /// Pack music keys available to <see cref="NodeActionTypes.SwitchMusic"/>'s
    /// <c>music</c> param. Rebuilt from the Music tab; <c>IsEditable</c>
    /// stays on so authors can still pick a vanilla <c>12_AudioPlayer</c>
    /// child by name.
    /// </summary>
    public ObservableCollection<string> MusicKeyOptions { get; } = new();

    /// <summary>
    /// Pack SFX keys for <see cref="NodeActionTypes.PlaySFX"/>'s <c>clip</c>
    /// param. Rebuilt from the SFX tab; editable, same rationale as
    /// <see cref="MusicKeyOptions"/>.
    /// </summary>
    public ObservableCollection<string> SfxKeyOptions { get; } = new();

    /// <summary>
    /// Autocomplete suggestions for the "GO path" box on GameObject-targeting
    /// actions (<c>SetGameObjectActive</c> / <c>FadeSprite</c> / <c>MoveGameObject</c>
    /// / <c>SpinGameObject</c>) — the GameObjects this pack creates and names:
    /// place overlays and outfit busts. The box stays editable so any vanilla
    /// GO or full hierarchy path can still be typed.
    /// </summary>
    public ObservableCollection<string> GameObjectNameOptions { get; } = new();

    /// <summary>
    /// Bust GameObject names only (no overlays) — the "Bust" category of the
    /// unified Set-Active action. Split out of <see cref="GameObjectNameOptions"/>
    /// so the category dropdown can offer just busts. Rebuilt alongside it.
    /// </summary>
    public ObservableCollection<string> BustNameOnlyOptions { get; } = new();

    /// <summary>
    /// Pack scene keys as plain strings — the "Scene" category of the unified
    /// Set-Active action (the string-typed sibling of <see cref="SceneOptions"/>,
    /// which is used by the older typed SceneRef editor).
    /// </summary>
    public ObservableCollection<string> SceneKeyOptions { get; } = new();

    /// <summary>
    /// Overlay GameObject names for the "Level Overlay" category of the unified
    /// Set-Active action, filtered to the <em>currently selected node's inferred
    /// level</em> (see <see cref="DialogueViewModel.InferLevelTokenForNode"/>).
    /// Falls back to every overlay in the pack when the level can't be inferred
    /// or has no overlays, so the author is never left with an empty list.
    /// Rebuilt whenever the selected node changes.
    /// </summary>
    public ObservableCollection<string> SelectedNodeOverlayOptions { get; } = new();

    /// <summary>
    /// Autocomplete suggestions for the "Signal" box on the signal-emitting
    /// actions (<c>EmitSignal</c> / <c>EmitSignalDelayed</c> and the optional
    /// "done" signal on <c>TransitionLevels</c>). Backed by the static
    /// <see cref="Model.VanillaSignals"/> catalogue (vanilla game signals only —
    /// mod-listened signals aren't baked in); the box stays editable so authors
    /// can type any custom or mod-specific signal.
    /// </summary>
    public IReadOnlyList<string> SignalOptions { get; } = Model.VanillaSignals.All;

    /// <summary>
    /// Subset of <see cref="VariableNameOptions"/> filtered to
    /// <see cref="PackVariableType.List"/> entries. Drives the dropdowns
    /// on the list-only actions (<c>AddToList</c> / <c>RemoveFromList</c>
    /// / <c>ClearList</c>) so authors aren't offered scalar variable names
    /// where a List is required.
    /// </summary>
    public ObservableCollection<string> ListVariableNameOptions { get; } = new();

    /// <summary>
    /// Vanilla-frame picker entries. Tokens are the bare filenames
    /// (e.g. <c>PhotoFrame.png</c>) — they round-trip into
    /// <see cref="SceneDef.VanillaFrame"/> as authored. The runtime
    /// resolves them against the plugin's bundled
    /// <c>VanillaFrames\</c> folder by the same filename.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> VanillaFrameOptions { get; } = new();

    /// <summary>Sound override options for the Scenes tab's combo box.</summary>
    public IReadOnlyList<SceneSoundMode> SceneSoundModes { get; } = new[]
    {
        SceneSoundMode.Silent, SceneSoundMode.Kiss, SceneSoundMode.Flash, SceneSoundMode.None,
    };

    /// <summary>The recognised action-type identifiers, sorted alphabetically
    /// for the picker (the underlying <see cref="NodeActionTypes.All"/> keeps
    /// its category grouping for the runtime).
    /// <para/>
    /// <c>ActivateScene</c> is intentionally omitted: it's folded into
    /// <c>SetGameObjectActive</c>'s "Scene" category in the row editor, so the
    /// picker shows a single unified Set-Active action. Loaded ActivateScene
    /// actions still display (via <c>NodeActionViewModel.DisplayType</c>).</summary>
    public IReadOnlyList<string> ActionTypes { get; } =
        NodeActionTypes.All
            .Where(t => t != NodeActionTypes.ActivateScene
                     && t != NodeActionTypes.SetVariable
                     && t != NodeActionTypes.IncrementVariable
                     && t != NodeActionTypes.PickRandomFromList    // folded into the Variable operation
                     && t != NodeActionTypes.CountList)            // folded into the Variable operation
            .Concat(new[] { NodeActionViewModel.VariableFamilyType })   // Set / Increment / Random-from-list / List-count folded into one
            .OrderBy(t => t, System.StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Condition-type identifiers for the picker. The six Variable* and
    /// five GameVariable* comparison types are folded into one "Variable" entry
    /// (the row exposes Source + Comparison); legacy GameVariable* are migrated
    /// to Variable* + source=vanilla on load.</summary>
    public IReadOnlyList<string> ConditionTypes { get; } =
        NodeConditionTypes.All
            .Where(t => !_variableConditionTypes.Contains(t))
            .Concat(new[] { NodeConditionViewModel.VariableFamilyType })
            .OrderBy(t => t, System.StringComparer.OrdinalIgnoreCase).ToArray();

    private static readonly HashSet<string> _variableConditionTypes = new()
    {
        NodeConditionTypes.VariableEquals, NodeConditionTypes.VariableGreaterThan,
        NodeConditionTypes.VariableGreaterOrEqual, NodeConditionTypes.VariableLessThan,
        NodeConditionTypes.VariableLessOrEqual, NodeConditionTypes.VariableExists,
        NodeConditionTypes.GameVariableEquals, NodeConditionTypes.GameVariableNumberGreaterThan,
        NodeConditionTypes.GameVariableNumberGreaterOrEqual, NodeConditionTypes.GameVariableNumberLessThan,
        NodeConditionTypes.GameVariableNumberLessOrEqual,
    };

    /// <summary>The kind options shown in the per-node Kind combo box.</summary>
    public IReadOnlyList<DialogueNodeKind> NodeKinds { get; } = new[]
    {
        DialogueNodeKind.Text, DialogueNodeKind.Choice, DialogueNodeKind.Random,
    };

    /// <summary>The jump-mode options shown in the per-node Jump combo.</summary>
    public IReadOnlyList<JumpMode> JumpModes { get; } = new[]
    {
        JumpMode.Continue, JumpMode.Exit, JumpMode.Jump,
    };

    /// <summary>The duration options shown in the per-node Duration combo.</summary>
    public IReadOnlyList<NodeDurationMode> NodeDurations { get; } = new[]
    {
        NodeDurationMode.UntilInteraction, NodeDurationMode.Timeout,
    };

    /// <summary>The variable-type options.</summary>
    public IReadOnlyList<PackVariableType> VariableTypes { get; } = new[]
    {
        PackVariableType.Bool, PackVariableType.Int, PackVariableType.Float, PackVariableType.String,
        PackVariableType.List,
    };

    /// <summary>
    /// The auto-refresh modes a variable can declare. Drives the Variables
    /// tab combo box; the order here is the order the user sees.
    /// </summary>
    public IReadOnlyList<PackVariableRefreshMode> VariableRefreshModes { get; } = new[]
    {
        PackVariableRefreshMode.Never,
        PackVariableRefreshMode.Daily,
        PackVariableRefreshMode.DailyRandom,
        PackVariableRefreshMode.LevelRandom,
    };

    /// <summary>
    /// Vanilla GO-name options for the source-picker on a vanilla extension.
    /// Same source data as <see cref="VanillaPlaces"/>, exposed as
    /// <see cref="NavigatorTargetOption"/> so the existing combo-box item
    /// template can be reused.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> VanillaSourceOptions { get; } = new();

    /// <summary>
    /// Flat list of every valid navigator-target token in the current state:
    /// every vanilla place plus every place authored in this pack (as
    /// <c>self:&lt;key&gt;</c>). Bound by the Places editor's target combo box.
    /// Rebuilt whenever the pack changes or a place is added/removed/renamed.
    /// </summary>
    public ObservableCollection<NavigatorTargetOption> AllTargetOptions { get; } = new();

    private string? _packRoot;
    /// <summary>Absolute path to the pack folder on disk, or null if unsaved.</summary>
    public string? PackRoot
    {
        get => _packRoot;
        set
        {
            _packRoot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            // ExportPackCommand's CanExecute is gated on PackRoot — kick
            // a requery so the menu item flips between disabled (in-
            // memory only) and enabled (saved to disk).
            ExportPackCommand?.Raise();
        }
    }

    public string Title => $"SMSModForge — {Pack.PackId}" + (PackRoot is null ? " (unsaved)" : $" — {PackRoot}");

    private OutfitViewModel? _selectedOutfit;
    public OutfitViewModel? SelectedOutfit
    {
        get => _selectedOutfit;
        set { _selectedOutfit = value; OnPropertyChanged(); }
    }

    private PlaceViewModel? _selectedPlace;
    public PlaceViewModel? SelectedPlace
    {
        get => _selectedPlace;
        set
        {
            _selectedPlace = value;
            // Mutually exclusive with vanilla-extension selection — picking
            // a place hides the extension editor.
            if (value != null && _selectedVanillaExtension != null)
            {
                _selectedVanillaExtension = null;
                OnPropertyChanged(nameof(SelectedVanillaExtension));
            }
            OnPropertyChanged();
        }
    }

    private MapButtonViewModel? _selectedMapButton;
    public MapButtonViewModel? SelectedMapButton
    {
        get => _selectedMapButton;
        set { _selectedMapButton = value; OnPropertyChanged(); }
    }

    private VanillaPlaceExtensionViewModel? _selectedVanillaExtension;
    public VanillaPlaceExtensionViewModel? SelectedVanillaExtension
    {
        get => _selectedVanillaExtension;
        set
        {
            _selectedVanillaExtension = value;
            if (value != null && _selectedPlace != null)
            {
                _selectedPlace = null;
                OnPropertyChanged(nameof(SelectedPlace));
            }
            OnPropertyChanged();
        }
    }

    private DialogueViewModel? _selectedDialogue;
    public DialogueViewModel? SelectedDialogue
    {
        get => _selectedDialogue;
        set { _selectedDialogue = value; OnPropertyChanged(); SelectedNode = null; }
    }

    private DialogueNodeViewModel? _selectedNode;
    public DialogueNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            // Capture the incoming node's pristine values BEFORE any WPF
            // binding notification fires. Once OnPropertyChanged() runs,
            // the ComboBoxes rebind to the new node — if the old ItemsSource
            // doesn't contain the new value, the two-way SelectedItem
            // binding writes null back and wipes the authored data.
            // Likewise, the rebuild's .Clear() triggers the same cascade.
            // Saving here (from `value`, before `_selectedNode` is set)
            // guarantees we hold the original values.
            var prevActor       = _selectedNode?.Actor;
            var savedExpression = value?.Expression;
            var savedOutfit     = value?.Outfit;

            // Detach from the previous node's Actor-change notification so the
            // bust-preview key tracks the *current* node's actor.
            if (_selectedNode != null) _selectedNode.PropertyChanged -= OnSelectedNodeActorMaybeChanged;
            _selectedNode = value;
            if (_selectedNode != null) _selectedNode.PropertyChanged += OnSelectedNodeActorMaybeChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedNodeActorBustKey));
            // Refresh the GO-path autocomplete from the current model so newly
            // added / renamed overlays show up when editing this node's actions.
            RebuildGameObjectNameOptions();
            // Same for the variable / list-variable pickers — a variable whose
            // type was just changed to List must appear in AddToList etc.
            RebuildVariableNameOptions();
            // Overlay list is filtered to this node's inferred level, so it must
            // rebuild on every node change (not just actor changes).
            RebuildSelectedNodeOverlayOptions();

            // Only rebuild the option lists when the actor actually changed.
            // Same actor → same outfit / expression options → no need to
            // disrupt the ComboBox (which avoids the clear-cascade entirely).
            if (_selectedNode?.Actor != prevActor)
            {
                RebuildSelectedNodeExpressionOptions();
                RebuildSelectedNodeOutfitOptions();
            }

            // Restore — WPF binding or the rebuild may have cleared these.
            if (_selectedNode != null)
            {
                _selectedNode.Expression = savedExpression ?? "";
                _selectedNode.Outfit     = savedOutfit ?? "";
            }
        }
    }

    private void OnSelectedNodeActorMaybeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DialogueNodeViewModel.Actor))
        {
            OnPropertyChanged(nameof(SelectedNodeActorBustKey));
            RebuildSelectedNodeExpressionOptions();
            RebuildSelectedNodeOutfitOptions();
        }
        else if (e.PropertyName == nameof(DialogueNodeViewModel.Outfit))
        {
            // A node outfit override changes which bust the preview shows.
            OnPropertyChanged(nameof(SelectedNodeActorBustKey));
        }
    }

    /// <summary>
    /// The bust GO name to preview for the currently-selected node. A
    /// node <see cref="DialogueNodeViewModel.Outfit"/> override wins; with
    /// none set, the node's authored <c>Actor</c> key is looked up in the
    /// pack's <c>Actors</c> table and that actor's
    /// <see cref="ActorViewModel.DefaultBustKey"/> is used. Empty when no
    /// node is selected, the node has no actor, or no matching actor exists.
    /// </summary>
    public string SelectedNodeActorBustKey
    {
        get
        {
            // An explicit per-node outfit switch is what the player sees.
            var outfit = SelectedNode?.Outfit;
            if (!string.IsNullOrEmpty(outfit)) return outfit;

            var actorKey = SelectedNode?.Actor;
            if (string.IsNullOrEmpty(actorKey)) return "";
            foreach (var a in Actors)
                if (a.Key == actorKey) return a.DefaultBustKey ?? "";
            return "";
        }
    }

    /// <summary>
    /// Expression keys available for the currently selected dialogue node,
    /// filtered to the node's actor. Always includes the four vanilla
    /// expressions; custom expressions from the matching actor are appended.
    /// Rebuilt when the selected node or its actor changes.
    /// </summary>
    public ObservableCollection<string> SelectedNodeExpressionOptions { get; } = new();

    private void RebuildSelectedNodeExpressionOptions()
    {
        // The four vanilla expression children are always available; fold in
        // the actor's custom keys, then surface the whole list alphabetically.
        var exprs = new System.Collections.Generic.List<string> { "Happy", "Angry", "Sad", "Flirty" };
        var actorKey = SelectedNode?.Actor;
        if (!string.IsNullOrEmpty(actorKey))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal)
                { "Happy", "Angry", "Sad", "Flirty" };
            foreach (var a in Actors)
            {
                if (a.Key != actorKey) continue;
                foreach (var e in a.Expressions)
                    if (!string.IsNullOrEmpty(e.Key) && seen.Add(e.Key))
                        exprs.Add(e.Key);
                break;
            }
        }
        SelectedNodeExpressionOptions.Clear();
        foreach (var e in exprs.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            SelectedNodeExpressionOptions.Add(e);
    }

    /// <summary>
    /// Bust GO names the currently-selected dialogue node can switch its
    /// actor to — the speaking actor's <see cref="ActorViewModel.Outfits"/>
    /// (with its <see cref="ActorViewModel.DefaultBustKey"/> folded in so
    /// the default is always offered). Drives the node's Outfit dropdown;
    /// rebuilt when the selected node or its actor changes.
    /// </summary>
    public ObservableCollection<string> SelectedNodeOutfitOptions { get; } = new();

    private void RebuildSelectedNodeOutfitOptions()
    {
        SelectedNodeOutfitOptions.Clear();

        var actorKey = SelectedNode?.Actor;
        if (string.IsNullOrEmpty(actorKey)) return;

        foreach (var a in Actors)
        {
            if (a.Key != actorKey) continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outfits = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(a.DefaultBustKey) && seen.Add(a.DefaultBustKey))
                outfits.Add(a.DefaultBustKey);
            foreach (var o in a.Outfits)
                if (!string.IsNullOrEmpty(o.BustGoName) && seen.Add(o.BustGoName))
                    outfits.Add(o.BustGoName);
            foreach (var name in outfits.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                SelectedNodeOutfitOptions.Add(name);
            break;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="SelectedNodeOverlayOptions"/> for the current node:
    /// the overlays of the node's inferred level, or — when that can't be
    /// resolved or is empty — every overlay in the pack. Called on each
    /// selected-node change.
    /// </summary>
    private void RebuildSelectedNodeOverlayOptions()
    {
        SelectedNodeOverlayOptions.Clear();
        System.Collections.Generic.List<string> names;
        if (SelectedDialogue != null && SelectedNode != null)
        {
            var level = SelectedDialogue.InferLevelTokenForNode(SelectedNode.Id);
            // Fallback level for Level Overlay rows whose own level isn't set.
            NodeActionViewModel.InferredOverlayLevel = level;
            names = OverlayNamesForLevel(level);
            if (names.Count == 0) names = AllOverlayNames();   // never strand the author
        }
        else
        {
            NodeActionViewModel.InferredOverlayLevel = "";
            names = AllOverlayNames();
        }

        foreach (var n in names) SelectedNodeOverlayOptions.Add(n);
    }

    /// <summary>Overlay names for a level token, falling back to every overlay in
    /// the pack when the token is empty or carries none. Backs
    /// <see cref="NodeActionViewModel.OverlayOptions"/>.</summary>
    private System.Collections.Generic.IEnumerable<string> OverlayNamesForLevelOrAll(string levelToken)
    {
        if (string.IsNullOrEmpty(levelToken)) return AllOverlayNames();
        var named = OverlayNamesForLevel(levelToken);
        return named.Count > 0 ? named : AllOverlayNames();
    }

    /// <summary>Flatten an Extra GameObject tree (each node + all nested children).</summary>
    private static System.Collections.Generic.IEnumerable<OverlayViewModel> FlattenOverlays(
        System.Collections.Generic.IEnumerable<OverlayViewModel> overlays)
    {
        foreach (var o in overlays)
        {
            yield return o;
            foreach (var c in FlattenOverlays(o.Children)) yield return c;
        }
    }

    /// <summary>Every overlay GameObject name declared across the pack's places (nested included).</summary>
    private System.Collections.Generic.List<string> AllOverlayNames()
        => FlattenOverlays(Places.SelectMany(p => p.Overlays))
                 .Select(o => o.Name)
                 .Where(n => !string.IsNullOrWhiteSpace(n))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                 .ToList();

    /// <summary>Overlay names belonging to a level token. Only <c>place:&lt;key&gt;</c>
    /// levels carry pack overlays; anything else returns empty.</summary>
    private System.Collections.Generic.List<string> OverlayNamesForLevel(string levelToken)
    {
        const string prefix = "place:";
        if (string.IsNullOrEmpty(levelToken) ||
            !levelToken.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return new();
        var key = levelToken.Substring(prefix.Length);
        return FlattenOverlays(Places
            .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Overlays))
            .Select(o => o.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ActorViewModel? _selectedActor;
    public ActorViewModel? SelectedActor
    {
        get => _selectedActor;
        set { _selectedActor = value; OnPropertyChanged(); }
    }

    private PackVariableViewModel? _selectedVariable;
    public PackVariableViewModel? SelectedVariable
    {
        get => _selectedVariable;
        set { _selectedVariable = value; OnPropertyChanged(); }
    }

    private SceneViewModel? _selectedScene;
    public SceneViewModel? SelectedScene
    {
        get => _selectedScene;
        set { _selectedScene = value; OnPropertyChanged(); }
    }

    private WallpaperViewModel? _selectedWallpaper;
    public WallpaperViewModel? SelectedWallpaper
    {
        get => _selectedWallpaper;
        set { _selectedWallpaper = value; OnPropertyChanged(); }
    }

    private MusicViewModel? _selectedMusic;
    public MusicViewModel? SelectedMusic
    {
        get => _selectedMusic;
        set { _selectedMusic = value; OnPropertyChanged(); }
    }

    private SfxViewModel? _selectedSfx;
    public SfxViewModel? SelectedSfx
    {
        get => _selectedSfx;
        set { _selectedSfx = value; OnPropertyChanged(); }
    }

    private UpdateRuleViewModel? _selectedIntegrationRule;
    public UpdateRuleViewModel? SelectedIntegrationRule
    {
        get => _selectedIntegrationRule;
        set
        {
            _selectedIntegrationRule = value;
            OnPropertyChanged();
            // Same GO-path + variable/list autocomplete refresh as for dialogue nodes.
            RebuildGameObjectNameOptions();
            RebuildVariableNameOptions();
        }
    }

    public ObservableCollection<ValidationIssue> Issues { get; } = new();

    // ── Commands

    public RelayCommand NewPackCommand { get; }
    public RelayCommand OpenPackCommand { get; }
    public RelayCommand SavePackCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand SavePackAsCommand { get; }

    /// <summary>
    /// File > Export pack… — zips the loose pack folder into a single
    /// <c>.smspack</c> file the runtime plugin reads. Requires the pack
    /// to be saved to disk first (the exporter walks the on-disk folder).
    /// </summary>
    public RelayCommand ExportPackCommand { get; }
    public RelayCommand AddCharacterCommand { get; }
    public RelayCommand AddOutfitCommand { get; }
    public RelayCommand AddPlaceCommand { get; }
    public RelayCommand RemovePlaceCommand { get; }
    public RelayCommand AddNavigatorButtonCommand { get; }
    public RelayCommand AddOverlayCommand { get; }
    public RelayCommand AddVanillaExtensionCommand { get; }
    public RelayCommand RemoveVanillaExtensionCommand { get; }
    public RelayCommand AddVanillaExtensionButtonCommand { get; }
    public RelayCommand AddMapButtonCommand { get; }
    public RelayCommand RemoveMapButtonCommand { get; }
    public RelayCommand AddDialogueCommand { get; }
    public RelayCommand AddDialogueFolderCommand { get; }
    public RelayCommand RemoveDialogueCommand { get; }
    public RelayCommand AddDialogueRootNodeCommand { get; }
    public RelayCommand AddDialogueChildNodeCommand { get; }
    public RelayCommand AddDialogueSiblingNodeCommand { get; }
    public RelayCommand RemoveDialogueNodeCommand { get; }
    public RelayCommand CopyNodeCommand { get; }
    public RelayCommand PasteNodeSiblingCommand { get; }
    public RelayCommand PasteNodeChildCommand { get; }
    public RelayCommand PasteNodeRootCommand { get; }
    public RelayCommand AddDialogueStartConditionCommand { get; }
    public RelayCommand AddDialogueStartConditionGroupCommand { get; }
    public RelayCommand AddNodeActionOnStartCommand { get; }
    public RelayCommand AddNodeActionOnFinishCommand { get; }
    public RelayCommand AddNodeConditionCommand { get; }
    public RelayCommand AddNodeConditionGroupCommand { get; }
    public RelayCommand AddActorCommand { get; }
    public RelayCommand RemoveActorCommand { get; }
    public RelayCommand AddActorExpressionCommand { get; }
    public RelayCommand AddActorOutfitCommand { get; }
    public RelayCommand AddVariableCommand { get; }
    public RelayCommand RemoveVariableCommand { get; }
    public RelayCommand AddVariableFolderCommand { get; }
    public RelayCommand AddInitialValueCommand { get; }
    public RelayCommand AddSceneCommand { get; }
    public RelayCommand RemoveSceneCommand { get; }
    public RelayCommand AddWallpaperCommand { get; }
    public RelayCommand RemoveWallpaperCommand { get; }
    public RelayCommand AddMusicCommand { get; }
    public RelayCommand RemoveMusicCommand { get; }
    public RelayCommand AddSfxCommand { get; }
    public RelayCommand RemoveSfxCommand { get; }

    /// <summary>Preview-plays an SFX clip (the row's <see cref="SfxViewModel"/>,
    /// or the selected one) at its authored default volume. Needs the pack
    /// saved to disk so the audio file can be read.</summary>
    public RelayCommand PlaySfxCommand { get; }

    /// <summary>Stops any in-progress SFX preview.</summary>
    public RelayCommand StopSfxCommand { get; }
    public RelayCommand AddIntegrationRuleCommand { get; }
    public RelayCommand RemoveIntegrationRuleCommand { get; }
    public RelayCommand AddIntegrationConditionCommand { get; }
    public RelayCommand AddIntegrationConditionGroupCommand { get; }
    public RelayCommand AddIntegrationActionCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand OpenRecentCommand { get; }

    /// <summary>Bound by the Themes menu. Switching applies + persists the
    /// chosen colour palette (see <see cref="Services.ThemeManager"/>).</summary>
    public IReadOnlyList<Services.ThemeDef> Themes => Services.ThemeManager.All;
    public RelayCommand ApplyThemeCommand { get; }

    /// <summary>Bound by Options ▸ Preview quality. Switching changes how often
    /// the live bust preview runs its CPU shader (and whether it supersamples),
    /// trading smoothness for CPU. See <see cref="Services.PreviewQualityManager"/>.</summary>
    public IReadOnlyList<Services.PreviewQualityDef> PreviewQualities => Services.PreviewQualityManager.All;
    public RelayCommand ApplyPreviewQualityCommand { get; }

    public ObservableCollection<string> RecentFiles { get; } = new();

    public MainViewModel()
    {
        // Let Level Overlay action rows list the overlays of whichever level the
        // author picks (see NodeActionViewModel.OverlayOptions).
        NodeActionViewModel.OverlayProvider = OverlayNamesForLevelOrAll;

        // Keep the left-bar lists alphabetical in the UI (their default views),
        // independent of the pack's on-disk order. New/duplicated items sort in
        // automatically. The Dialogues folder tree is sorted in BuildDialogueTree.
        SortView(Characters, nameof(CharacterViewModel.Name));
        SortView(Places, nameof(PlaceViewModel.Display));
        SortView(VanillaExtensions, nameof(VanillaPlaceExtensionViewModel.Display));
        SortView(Actors, nameof(ActorViewModel.Display));
        SortView(Scenes, nameof(SceneViewModel.Display));
        SortView(Variables, nameof(PackVariableViewModel.Name));
        SortView(Wallpapers, nameof(WallpaperViewModel.Display));
        SortView(Music, nameof(MusicViewModel.Display));
        SortView(Sfx, nameof(SfxViewModel.Display));
        SortView(IntegrationRules, nameof(UpdateRuleViewModel.Display));

        Undo = new Services.UndoService(() => PackRepository.Serialize(Pack));
        Undo.RestoreRequested += OnUndoRestore;
        Undo.StateChanged += () => UndoCommand?.Raise();  // global requery refreshes both
        // Snapshot the pre-command state before every command-driven mutation,
        // so each add/remove/toggle is its own undo step (text-field edits are
        // checkpointed separately on focus-loss by the window).
        RelayCommand.Executing += () => Undo.Checkpoint();
        // RelayCommand.Executing checkpoints just before these run, so the most
        // recent change is captured before we step back/forward.
        UndoCommand = new RelayCommand(() => Undo.Undo(), () => Undo.CanUndo);
        RedoCommand = new RelayCommand(() => Undo.Redo(), () => Undo.CanRedo);

        NewPackCommand     = new RelayCommand(NewPack);
        OpenPackCommand    = new RelayCommand(OpenPack);
        SavePackCommand    = new RelayCommand(SavePack);
        SavePackAsCommand  = new RelayCommand(SavePackAs);
        // Export is only available once the pack has been saved at least
        // once — the exporter zips the on-disk folder, so an in-memory-
        // only pack has nothing to bundle.
        ExportPackCommand  = new RelayCommand(ExportPack, () => PackRoot != null);
        AddCharacterCommand = new RelayCommand(AddCharacter);
        AddOutfitCommand   = new RelayCommand(AddOutfit, () => SelectedOutfit != null || Characters.Count > 0);
        AddPlaceCommand    = new RelayCommand(AddPlace);
        RemovePlaceCommand = new RelayCommand(RemovePlace, () => SelectedPlace != null);
        AddNavigatorButtonCommand = new RelayCommand(AddNavigatorButton,
            () => SelectedPlace?.CanAddNavigatorButton == true);
        AddOverlayCommand = new RelayCommand(() => SelectedPlace?.AddOverlay(),
            () => SelectedPlace != null);
        AddVanillaExtensionCommand    = new RelayCommand(AddVanillaExtension);
        RemoveVanillaExtensionCommand = new RelayCommand(RemoveVanillaExtension, () => SelectedVanillaExtension != null);
        AddVanillaExtensionButtonCommand = new RelayCommand(AddVanillaExtensionButton, () => SelectedVanillaExtension != null);
        AddMapButtonCommand           = new RelayCommand(AddMapButton);
        RemoveMapButtonCommand        = new RelayCommand(RemoveMapButton, () => SelectedMapButton != null);

        AddDialogueCommand            = new RelayCommand(AddDialogue);
        AddDialogueFolderCommand      = new RelayCommand(AddDialogueFolder);
        DuplicateItemCommand          = new RelayCommand(DuplicateActiveItem);
        CopyItemCommand               = new RelayCommand(CopyActiveItem);
        PasteItemCommand              = new RelayCommand(PasteActiveItem);
        RemoveDialogueCommand         = new RelayCommand(RemoveDialogue, () => SelectedDialogue != null);
        AddDialogueRootNodeCommand    = new RelayCommand(AddDialogueRootNode, () => SelectedDialogue != null);
        AddDialogueChildNodeCommand   = new RelayCommand(AddDialogueChildNode, () => SelectedDialogue != null && SelectedNode != null);
        AddDialogueSiblingNodeCommand = new RelayCommand(AddDialogueSiblingNode, () => SelectedDialogue != null && SelectedNode != null);
        RemoveDialogueNodeCommand     = new RelayCommand(RemoveDialogueNode, () => SelectedDialogue != null && SelectedNode != null);
        CopyNodeCommand               = new RelayCommand(() => SelectedDialogue?.CopyNode(SelectedNode), () => SelectedNode != null);
        PasteNodeSiblingCommand       = new RelayCommand(() => SelectedDialogue?.PasteNodes(SelectedNode, NodePastePosition.Sibling),
                                                         () => SelectedDialogue != null && SelectedNode != null && Services.EditorClipboard.HasNodes);
        PasteNodeChildCommand         = new RelayCommand(() => SelectedDialogue?.PasteNodes(SelectedNode, NodePastePosition.Child),
                                                         () => SelectedDialogue != null && SelectedNode != null && Services.EditorClipboard.HasNodes);
        PasteNodeRootCommand          = new RelayCommand(() => SelectedDialogue?.PasteNodes(null, NodePastePosition.Root),
                                                         () => SelectedDialogue != null && Services.EditorClipboard.HasNodes);
        AddDialogueStartConditionCommand = new RelayCommand(AddDialogueStartCondition, () => SelectedDialogue != null);
        AddDialogueStartConditionGroupCommand = new RelayCommand(() => SelectedDialogue?.AddStartConditionGroup(), () => SelectedDialogue != null);
        AddNodeActionOnStartCommand   = new RelayCommand(AddNodeActionOnStart, () => SelectedNode != null);
        AddNodeActionOnFinishCommand  = new RelayCommand(AddNodeActionOnFinish, () => SelectedNode != null);
        AddNodeConditionCommand       = new RelayCommand(AddNodeCondition, () => SelectedNode != null);
        AddNodeConditionGroupCommand  = new RelayCommand(() => SelectedNode?.AddConditionGroup(), () => SelectedNode != null);

        AddActorCommand               = new RelayCommand(AddActor);
        RemoveActorCommand            = new RelayCommand(RemoveActor, () => SelectedActor != null);
        AddActorExpressionCommand     = new RelayCommand(AddActorExpression, () => SelectedActor != null);
        AddActorOutfitCommand         = new RelayCommand(AddActorOutfit, () => SelectedActor != null);

        AddVariableCommand            = new RelayCommand(AddVariable);
        RemoveVariableCommand         = new RelayCommand(RemoveVariable, () => SelectedVariableTreeItem != null || SelectedVariable != null);
        AddVariableFolderCommand      = new RelayCommand(AddVariableFolder);
        AddInitialValueCommand        = new RelayCommand(() => SelectedVariable?.AddInitialValue());
        AddSceneCommand               = new RelayCommand(AddScene);
        RemoveSceneCommand            = new RelayCommand(RemoveScene, () => SelectedScene != null);
        AddWallpaperCommand           = new RelayCommand(AddWallpaper);
        RemoveWallpaperCommand        = new RelayCommand(RemoveWallpaper, () => SelectedWallpaper != null);
        AddMusicCommand               = new RelayCommand(AddMusic);
        RemoveMusicCommand            = new RelayCommand(RemoveMusic, () => SelectedMusic != null);
        AddSfxCommand                 = new RelayCommand(AddSfx);
        RemoveSfxCommand              = new RelayCommand(RemoveSfx, () => SelectedSfx != null);
        PlaySfxCommand                = new RelayCommand(
            p => PlaySfx(p as SfxViewModel ?? SelectedSfx),
            p => CanPlaySfx(p as SfxViewModel ?? SelectedSfx));
        StopSfxCommand                = new RelayCommand(_ => _sfxPreview.Stop());

        AddIntegrationRuleCommand      = new RelayCommand(AddIntegrationRule);
        RemoveIntegrationRuleCommand   = new RelayCommand(RemoveIntegrationRule, () => SelectedIntegrationRule != null);
        AddIntegrationConditionCommand = new RelayCommand(() => SelectedIntegrationRule?.AddCondition(),
                                                          () => SelectedIntegrationRule != null);
        AddIntegrationConditionGroupCommand = new RelayCommand(() => SelectedIntegrationRule?.AddConditionGroup(),
                                                          () => SelectedIntegrationRule != null);
        AddIntegrationActionCommand    = new RelayCommand(() => SelectedIntegrationRule?.AddAction(),
                                                          () => SelectedIntegrationRule != null);

        ValidateCommand               = new RelayCommand(Validate);
        OpenRecentCommand             = new RelayCommand(p => OpenRecentPack((string)p!));
        ApplyThemeCommand             = new RelayCommand(p =>
        {
            if (p is Services.ThemeDef theme) Services.ThemeManager.Apply(theme);
        });
        ApplyPreviewQualityCommand    = new RelayCommand(p =>
        {
            if (p is Services.PreviewQualityDef q) Services.PreviewQualityManager.Apply(q);
        });

        // Alphabetical display order for every tab's left-hand record list.
        // View-layer only — the model collections (and the saved JSON order)
        // keep creation order; live sorting re-slots records as they're
        // renamed. Sort by the SAME property each list displays
        // (DisplayMemberPath) so the visible order is actually alphabetical:
        // most lists show "Display" (e.g. "Bedroom (MyRoom)"),
        // not the raw key. MapButtons is intentionally absent — it's an
        // inline editor list, not a selectable left-hand record list, and
        // live-sorting it would reorder rows mid-edit.
        ViewSort.Alphabetical(Characters, nameof(CharacterViewModel.Name));
        ViewSort.Alphabetical(Places, nameof(PlaceViewModel.Display));
        ViewSort.Alphabetical(VanillaExtensions, nameof(VanillaPlaceExtensionViewModel.Display));
        ViewSort.Alphabetical(Dialogues, nameof(DialogueViewModel.Display));
        ViewSort.Alphabetical(Actors, nameof(ActorViewModel.Display));
        ViewSort.Alphabetical(Variables, nameof(PackVariableViewModel.Name));
        ViewSort.Alphabetical(Scenes, nameof(SceneViewModel.Display));
        ViewSort.Alphabetical(Wallpapers, nameof(WallpaperViewModel.Display));
        ViewSort.Alphabetical(Music, nameof(MusicViewModel.Display));
        ViewSort.Alphabetical(Sfx, nameof(SfxViewModel.Display));
        ViewSort.Alphabetical(IntegrationRules, nameof(UpdateRuleViewModel.Display));

        LoadRecentFiles();
        RebindCharacters();
        RebindPlaces();
        RebindVanillaExtensions();
        RebindMapButtons();
        RebindDialogues();
        RebindActors();
        RebindVariables();
        RebindScenes();
        RebindWallpapers();
        RebindMusic();
        RebindSfx();
        RebindIntegrationRules();
        RebuildTargetOptions();
        RebuildVanillaSourceOptions();
        RebuildWorldMapDistrictOptions();
        RebuildVanillaFrameOptions();
        RebuildDialogueRoomTalkOptions();
        RebuildLevelOptions();
        RebuildActorAndBustOptions();
        RebuildVariableNameOptions();
        RebuildSceneOptions();

        // Baseline snapshot: a freshly-opened editor (empty starter pack) is
        // "clean", so closing without edits won't prompt.
        MarkSaved();
        Undo.Reset();
    }

    private void RebindCharacters()
    {
        Characters.Clear();
        foreach (var c in Pack.Characters)
            Characters.Add(new CharacterViewModel(c));
        SelectedOutfit = Characters.FirstOrDefault()?.Outfits.FirstOrDefault();
    }

    private void RebindPlaces()
    {
        Places.Clear();
        foreach (var p in Pack.Places)
            Places.Add(new PlaceViewModel(p));
        SelectedPlace = Places.FirstOrDefault();
    }

    private void RebindVanillaExtensions()
    {
        VanillaExtensions.Clear();
        foreach (var v in Pack.VanillaExtensions)
            VanillaExtensions.Add(new VanillaPlaceExtensionViewModel(v));
        SelectedVanillaExtension = null;
    }

    private void RebindMapButtons()
    {
        MapButtons.Clear();
        foreach (var b in Pack.MapButtons)
            MapButtons.Add(new MapButtonViewModel(b, RemoveMapButtonVm));
        SelectedMapButton = null;
    }

    /// <summary>
    /// Builds the World Map district picker once at construction time.
    /// Districts are baked into the base game; the list never changes.
    /// </summary>
    private void RebuildWorldMapDistrictOptions()
    {
        WorldMapDistrictOptions.Clear();
        foreach (var d in WorldMapDistricts.All.OrderBy(d => d.DisplayName, System.StringComparer.OrdinalIgnoreCase))
            WorldMapDistrictOptions.Add(new NavigatorTargetOption(
                Token: d.GoName,
                DisplayLabel: d.DisplayName));
    }

    /// <summary>Builds the vanilla-source picker list once at construction
    /// time. The vanilla catalog never changes during a session.</summary>
    private void RebuildVanillaSourceOptions()
    {
        VanillaSourceOptions.Clear();
        foreach (var v in VanillaPlaces.All.OrderBy(v => v.DisplayName, System.StringComparer.OrdinalIgnoreCase))
            VanillaSourceOptions.Add(new NavigatorTargetOption(
                Token: $"vanilla:{v.GoName}",
                DisplayLabel: $"{v.DisplayName} ({v.GoName})"));
    }

    /// <summary>
    /// Rebuilds <see cref="AllTargetOptions"/> from the vanilla catalog plus
    /// the current pack's authored places. The Places editor's target combo
    /// is bound to this list. Re-run after every place add/remove/rename
    /// (Place keys feed into the <c>self:&lt;key&gt;</c> options).
    /// </summary>
    public void RebuildTargetOptions()
    {
        var targetOpts = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var v in VanillaPlaces.All)
            targetOpts.Add(new NavigatorTargetOption(
                Token: $"vanilla:{v.GoName}",
                DisplayLabel: $"Vanilla — {v.DisplayName} ({v.GoName})"));
        foreach (var p in Places)
            targetOpts.Add(new NavigatorTargetOption(
                Token: $"self:{p.Key}",
                DisplayLabel: $"This pack — {p.DisplayName} ({p.Key})"));
        AllTargetOptions.Clear();
        foreach (var t in targetOpts.OrderBy(t => t.DisplayLabel, System.StringComparer.OrdinalIgnoreCase))
            AllTargetOptions.Add(t);
    }

    private void NewPack()
    {
        Pack = PackRepository.CreateEmpty("Untitled");
        PackRoot = null;
        RebindAll();
        MarkSaved();
        Undo.Reset();
    }

    /// <summary>Re-fans out the entire Pack into ViewModels + option lists. Called after New / Open.</summary>
    private void RebindAll()
    {
        RebindCharacters();
        RebindPlaces();
        RebindVanillaExtensions();
        RebindMapButtons();
        RebindDialogues();
        RebindActors();
        RebindVariables();
        RebindScenes();
        RebindWallpapers();
        RebindMusic();
        RebindSfx();
        RebindIntegrationRules();
        RebuildTargetOptions();
        RebuildDialogueRoomTalkOptions();
        RebuildLevelOptions();
        RebuildActorAndBustOptions();
        RebuildVariableNameOptions();
        RebuildSceneOptions();
        RebuildGameObjectNameOptions();
        // Seed the overlay list (no node selected yet → all-overlays fallback) so
        // the Set-Active row's Overlay category isn't empty before a node is picked.
        RebuildSelectedNodeOverlayOptions();
    }

    private void OpenPack()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Mod pack manifest|modpack.json;bustpack.json|All files|*.*",
            Title = "Open modpack.json",
        };
        // Reopen where the last pack was opened from (independent of the Export
        // cache); first run leaves it at the OS default.
        var lastOpen = Services.DialogFoldersService.Get(Services.DialogFoldersService.Key.Open);
        if (lastOpen != null) dialog.InitialDirectory = lastOpen;

        if (dialog.ShowDialog() != true) return;
        var dir = Path.GetDirectoryName(dialog.FileName)!;
        Services.DialogFoldersService.Set(Services.DialogFoldersService.Key.Open, dir);
        OpenPackFromPath(dir);
    }

    private void OpenPackFromPath(string dir)
    {
        try
        {
            Pack = PackRepository.Load(dir);
            PackRoot = dir;
            RecordRecentFile(dir);
            RebindAll();
            Validate();
            MarkSaved();
            Undo.Reset();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenRecentPack(string path)
    {
        if (!Directory.Exists(path))
        {
            MessageBox.Show($"Folder no longer exists:\n{path}", "Open failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        OpenPackFromPath(path);
    }

    private void LoadRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var p in RecentFilesService.Load()) RecentFiles.Add(p);
    }

    private void RecordRecentFile(string path)
    {
        RecentFilesService.Add(path);
        LoadRecentFiles();
    }

    /// <summary>
    /// Raised after a successful <see cref="SavePack"/> (manifest written to
    /// disk). The view subscribes to flash a brief "Saved" indicator; nothing in
    /// the VM depends on it, so it's safe for there to be no subscribers.
    /// </summary>
    public event EventHandler? Saved;

    private void SavePack()
    {
        // Fold the current tree back into the model (catches a dialogue key
        // rename that happened without a tree mutation, so folder membership
        // — keyed by dialogue key — stays correct across a reload).
        SyncFoldersToModel();
        SyncVariableFoldersToModel();
        if (PackRoot is null) { SavePackAs(); return; }
        try
        {
            PackRepository.Save(Pack, PackRoot);
            Validate();
            MarkSaved();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePackAs()
    {
        SyncFoldersToModel();   // keep folder membership current (see SavePack)
        SyncVariableFoldersToModel();
        var dialog = new SaveFileDialog
        {
            FileName = PackRepository.ManifestFileName,
            Filter = "Mod pack manifest (modpack.json)|modpack.json",
            Title = "Save pack to folder",
        };
        if (dialog.ShowDialog() != true) return;
        var dir = Path.GetDirectoryName(dialog.FileName)!;
        PackRoot = dir;
        Pack.PackId = Path.GetFileName(dir);
        OnPropertyChanged(nameof(Title));
        SavePack();
        RecordRecentFile(dir);
    }

    /// <summary>
    /// Save the current edits and then bundle the on-disk pack folder
    /// into a <c>.smspack</c> archive picked by the user. The default
    /// filename is <c>&lt;PackId&gt;.smspack</c> and the dialog opens in
    /// the same folder as <see cref="PackRoot"/> so a "Save → Export" loop
    /// drops the archive next to the loose files.
    /// </summary>
    private void ExportPack()
    {
        // Belt and braces: the command's CanExecute already requires
        // PackRoot, but the user might dock-disable the menu state cache.
        if (PackRoot is null)
        {
            MessageBox.Show(
                "Save the pack to disk before exporting — the .smspack zip " +
                "is built from the on-disk folder.",
                "Export pack", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Auto-save first so the export reflects the latest in-memory edits
        // rather than the previously-saved snapshot. The user typically
        // expects "Export" to mean "what I see, packed up".
        SyncFoldersToModel();
        SyncVariableFoldersToModel();
        try { PackRepository.Save(Pack, PackRoot); }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Couldn't save before export: " + ex.Message,
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = Pack.PackId + PackExporter.FileExtension,
            Filter = "ModForge pack (*" + PackExporter.FileExtension + ")|*" + PackExporter.FileExtension,
            Title = "Export pack to .smspack file",
            // Reopen where you last exported (e.g. the game's ModPacks folder),
            // not the pack's source folder. Falls back to PackRoot on first export.
            InitialDirectory = Services.DialogFoldersService.Get(Services.DialogFoldersService.Key.Export) ?? PackRoot,
        };
        if (dialog.ShowDialog() != true) return;
        Services.DialogFoldersService.Set(
            Services.DialogFoldersService.Key.Export, Path.GetDirectoryName(dialog.FileName));

        try
        {
            var result = PackExporter.Export(PackRoot, dialog.FileName);
            double srcMb = result.SourceBytes / 1024.0 / 1024.0;
            double outMb = result.CompressedBytes / 1024.0 / 1024.0;
            MessageBox.Show(
                $"Exported {result.FileCount} file(s) — {srcMb:N1} MB source → {outMb:N1} MB packed:\n{result.OutputPath}",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddCharacter()
    {
        var def = new CharacterDef
        {
            Name = $"NewChar{Pack.Characters.Count + 1}",
            DisplayName = "New Character",
            Outfits = { new OutfitDef { Key = "newchar", GameObjectName = "NewCharBase" } },
        };
        Pack.Characters.Add(def);
        var vm = new CharacterViewModel(def);
        Characters.Add(vm);
        SelectedOutfit = vm.Outfits.FirstOrDefault();
    }

    private void AddOutfit()
    {
        var ch = (SelectedOutfit != null
            ? Characters.FirstOrDefault(c => c.Outfits.Contains(SelectedOutfit))
            : null) ?? Characters.FirstOrDefault();
        if (ch == null) return;
        ch.AddOutfit();
        SelectedOutfit = ch.Outfits.Last();
    }

    private void AddPlace()
    {
        var def = new PlaceDef
        {
            Key = $"place{Pack.Places.Count + 1}",
            InternalName = $"Place{Pack.Places.Count + 1}",
            DisplayName = "New Place",
            BaseSprite = "",
            SecondarySprite = "",
            MaskSprite = "",
        };
        Pack.Places.Add(def);
        var vm = new PlaceViewModel(def);
        Places.Add(vm);
        SelectedPlace = vm;
        RebuildTargetOptions();
        RebuildDialogueRoomTalkOptions();
        RebuildLevelOptions();
    }

    private void RemovePlace()
    {
        if (SelectedPlace is null) return;
        Pack.Places.Remove(SelectedPlace.Model);
        Places.Remove(SelectedPlace);
        SelectedPlace = Places.FirstOrDefault();
        RebuildTargetOptions();
        RebuildDialogueRoomTalkOptions();
        RebuildLevelOptions();
    }

    private void AddNavigatorButton()
    {
        SelectedPlace?.AddNavigatorButton();
    }

    private void AddVanillaExtension()
    {
        // Default to the first vanilla source (Beach is index 14 — a sensible
        // common case — but anything is fine; the user will pick the real
        // source in the editor).
        var def = new VanillaPlaceExtensionDef
        {
            Source = $"vanilla:{VanillaPlaces.All[14].GoName}",
        };
        Pack.VanillaExtensions.Add(def);
        var vm = new VanillaPlaceExtensionViewModel(def);
        VanillaExtensions.Add(vm);
        SelectedVanillaExtension = vm;
    }

    private void RemoveVanillaExtension()
    {
        if (SelectedVanillaExtension is null) return;
        Pack.VanillaExtensions.Remove(SelectedVanillaExtension.Model);
        VanillaExtensions.Remove(SelectedVanillaExtension);
        SelectedVanillaExtension = VanillaExtensions.FirstOrDefault();
    }

    private void AddVanillaExtensionButton()
    {
        SelectedVanillaExtension?.AddNavigatorButton();
    }

    // ── Map buttons (World Map radial entries) ────────────────────────

    private void AddMapButton()
    {
        // Default to Foundry (the host mod reference case, since the
        // canonical example is the a place button there). The author
        // can change it in the editor.
        var def = new MapButtonDef
        {
            District = WorldMapDistricts.All[4].GoName, // Foundry
        };
        Pack.MapButtons.Add(def);
        var vm = new MapButtonViewModel(def, RemoveMapButtonVm);
        MapButtons.Add(vm);
        SelectedMapButton = vm;
    }

    private void RemoveMapButton()
    {
        if (SelectedMapButton is null) return;
        RemoveMapButtonVm(SelectedMapButton);
    }

    /// <summary>
    /// Invoked from the per-row Remove button on a <see cref="MapButtonViewModel"/>,
    /// keeping <see cref="Pack.MapButtons"/> and the observable collection
    /// in sync.
    /// </summary>
    private void RemoveMapButtonVm(MapButtonViewModel vm)
    {
        Pack.MapButtons.Remove(vm.Model);
        MapButtons.Remove(vm);
        if (SelectedMapButton == vm)
            SelectedMapButton = MapButtons.FirstOrDefault();
    }

    // ── Dialogue rebind / options / commands ──────────────────────────

    private void RebindDialogues()
    {
        Dialogues.Clear();
        foreach (var d in Pack.Dialogues)
            Dialogues.Add(new DialogueViewModel(d));
        BuildDialogueTree();
        SelectedDialogue = Dialogues.FirstOrDefault();
    }

    // ── Dialogue folder tree (cosmetic grouping) ─────────────────────────
    //
    // A parallel view over the flat Dialogues list: folders (nestable) hold
    // dialogue leaves; ungrouped dialogues sit at the root. Persisted into the
    // editor-only Pack.DialogueFolders key. The tree drives SelectedDialogue
    // when a leaf is picked; drag-drop + the toolbar mutate it, then
    // SyncFoldersToModel writes it back.

    public ObservableCollection<DialogueTreeItem> DialogueTree { get; } = new();

    private DialogueTreeItem? _selectedDialogueTreeItem;
    public DialogueTreeItem? SelectedDialogueTreeItem
    {
        get => _selectedDialogueTreeItem;
        set
        {
            _selectedDialogueTreeItem = value;
            OnPropertyChanged();
            if (value is DialogueLeafNode leaf) SelectedDialogue = leaf.Dialogue;
        }
    }

    public void BuildDialogueTree()
    {
        DialogueTree.Clear();
        var placed = new HashSet<string>();
        foreach (var f in Pack.DialogueFolders)
            DialogueTree.Add(BuildFolderNode(f, placed));
        foreach (var d in Dialogues)
            if (!placed.Contains(d.Key))
                DialogueTree.Add(new DialogueLeafNode(d));
        SortTree(DialogueTree);
    }

    /// <summary>Sort a tree level alphabetically — folders first, then dialogue leaves — recursively.</summary>
    private static void SortTree(ObservableCollection<DialogueTreeItem> level)
    {
        var sorted = level
            .OrderBy(i => i is DialogueFolderNode ? 0 : 1)
            .ThenBy(i => i is DialogueFolderNode f ? f.Name : ((DialogueLeafNode)i).Dialogue.Display,
                    System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        level.Clear();
        foreach (var i in sorted) level.Add(i);
        foreach (var i in sorted)
            if (i is DialogueFolderNode fn) SortTree(fn.Children);
    }

    /// <summary>Keep a list's UI (its default view) alphabetical by <paramref name="property"/>.</summary>
    private static void SortView(System.Collections.IEnumerable collection, string property)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(collection);
        if (view == null) return;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
            property, System.ComponentModel.ListSortDirection.Ascending));
    }

    private DialogueFolderNode BuildFolderNode(DialogueFolderDef f, HashSet<string> placed)
    {
        var node = new DialogueFolderNode(f.Name);
        foreach (var sub in f.Folders)
            node.Children.Add(BuildFolderNode(sub, placed));
        foreach (var key in f.Dialogues)
        {
            var d = Dialogues.FirstOrDefault(x => x.Key == key);
            if (d != null && placed.Add(key))
                node.Children.Add(new DialogueLeafNode(d));
        }
        return node;
    }

    /// <summary>Persist the current tree into the editor-only dialogueFolders key. Root leaves stay implicit.</summary>
    public void SyncFoldersToModel()
    {
        Pack.DialogueFolders.Clear();
        foreach (var item in DialogueTree)
            if (item is DialogueFolderNode fn)
                Pack.DialogueFolders.Add(FolderNodeToDef(fn));
    }

    private static DialogueFolderDef FolderNodeToDef(DialogueFolderNode fn)
    {
        var def = new DialogueFolderDef { Name = fn.Name };
        foreach (var c in fn.Children)
        {
            if (c is DialogueFolderNode sub) def.Folders.Add(FolderNodeToDef(sub));
            else if (c is DialogueLeafNode leaf) def.Dialogues.Add(leaf.Dialogue.Key);
        }
        return def;
    }

    private void AddDialogueFolder()
    {
        var folder = new DialogueFolderNode("New Folder");
        if (SelectedDialogueTreeItem is DialogueFolderNode target)
        {
            target.Children.Insert(0, folder);
            target.IsExpanded = true;
        }
        else DialogueTree.Add(folder);
        SortTree(DialogueTree);
        SyncFoldersToModel();
        SelectedDialogueTreeItem = folder;
    }

    // ── Variables folder tree (cosmetic; mirrors the Dialogues tree) ─────
    // Persisted under the editor-only Pack.VariableFolders key. The tree
    // drives SelectedVariable (via SelectedVariableTreeItem);
    // SyncVariableFoldersToModel writes membership back.

    public ObservableCollection<VariableTreeItem> VariableTree { get; } = new();

    private VariableTreeItem? _selectedVariableTreeItem;
    public VariableTreeItem? SelectedVariableTreeItem
    {
        get => _selectedVariableTreeItem;
        set
        {
            _selectedVariableTreeItem = value;
            OnPropertyChanged();
            if (value is VariableLeafNode leaf) SelectedVariable = leaf.Variable;
        }
    }

    public void BuildVariableTree()
    {
        VariableTree.Clear();
        var placed = new HashSet<string>();
        foreach (var f in Pack.VariableFolders)
            VariableTree.Add(BuildVariableFolderNode(f, placed));
        foreach (var v in Variables)
            if (!placed.Contains(v.Name))
                VariableTree.Add(new VariableLeafNode(v));
        SortVariableTree(VariableTree);
    }

    private static void SortVariableTree(ObservableCollection<VariableTreeItem> level)
    {
        var sorted = level
            .OrderBy(i => i is VariableFolderNode ? 0 : 1)
            .ThenBy(i => i is VariableFolderNode f ? f.Name : ((VariableLeafNode)i).Variable.Name,
                    System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        level.Clear();
        foreach (var i in sorted) level.Add(i);
        foreach (var i in sorted)
            if (i is VariableFolderNode fn) SortVariableTree(fn.Children);
    }

    private VariableFolderNode BuildVariableFolderNode(VariableFolderDef f, HashSet<string> placed)
    {
        var node = new VariableFolderNode(f.Name);
        foreach (var sub in f.Folders)
            node.Children.Add(BuildVariableFolderNode(sub, placed));
        foreach (var name in f.Variables)
        {
            var v = Variables.FirstOrDefault(x => x.Name == name);
            if (v != null && placed.Add(name))
                node.Children.Add(new VariableLeafNode(v));
        }
        return node;
    }

    /// <summary>Persist the current tree into the editor-only variableFolders key. Root leaves stay implicit.</summary>
    public void SyncVariableFoldersToModel()
    {
        Pack.VariableFolders.Clear();
        foreach (var item in VariableTree)
            if (item is VariableFolderNode fn)
                Pack.VariableFolders.Add(VariableFolderNodeToDef(fn));
    }

    private static VariableFolderDef VariableFolderNodeToDef(VariableFolderNode fn)
    {
        var def = new VariableFolderDef { Name = fn.Name };
        foreach (var c in fn.Children)
        {
            if (c is VariableFolderNode sub) def.Folders.Add(VariableFolderNodeToDef(sub));
            else if (c is VariableLeafNode leaf) def.Variables.Add(leaf.Variable.Name);
        }
        return def;
    }

    private void AddVariableFolder()
    {
        var folder = new VariableFolderNode("New Folder");
        if (SelectedVariableTreeItem is VariableFolderNode target)
        {
            target.Children.Insert(0, folder);
            target.IsExpanded = true;
        }
        else VariableTree.Add(folder);
        SortVariableTree(VariableTree);
        SyncVariableFoldersToModel();
        SelectedVariableTreeItem = folder;
    }

    public ObservableCollection<VariableTreeItem>? FindVariableParentChildren(VariableTreeItem item)
        => VariableTree.Contains(item) ? VariableTree : FindVariableParentIn(VariableTree, item);

    private static ObservableCollection<VariableTreeItem>? FindVariableParentIn(
        ObservableCollection<VariableTreeItem> coll, VariableTreeItem item)
    {
        foreach (var c in coll)
            if (c is VariableFolderNode fn)
            {
                if (fn.Children.Contains(item)) return fn.Children;
                var found = FindVariableParentIn(fn.Children, item);
                if (found != null) return found;
            }
        return null;
    }

    private static bool IsVariableDescendant(VariableFolderNode folder, VariableTreeItem? maybe)
    {
        if (maybe == null) return false;
        foreach (var c in folder.Children)
        {
            if (c == maybe) return true;
            if (c is VariableFolderNode sub && IsVariableDescendant(sub, maybe)) return true;
        }
        return false;
    }

    /// <summary>Move a dragged variable-tree item onto a drop target (into a folder,
    /// beside a leaf, or to root when the target is null). A folder can't drop into
    /// itself or a descendant.</summary>
    public void MoveVariableTreeItem(VariableTreeItem dragged, VariableTreeItem? target)
    {
        if (dragged == null || dragged == target) return;

        ObservableCollection<VariableTreeItem> dest;
        VariableFolderNode? destFolder = null;
        if (target is VariableFolderNode f) { dest = f.Children; destFolder = f; }
        else if (target is VariableLeafNode leaf) dest = FindVariableParentChildren(leaf) ?? VariableTree;
        else dest = VariableTree;

        if (dragged is VariableFolderNode df &&
            (df == destFolder || IsVariableDescendant(df, destFolder) || IsVariableDescendant(df, target)))
            return;

        var from = FindVariableParentChildren(dragged);
        if (from == null || from == dest) return;

        Undo.Checkpoint();
        from.Remove(dragged);
        dest.Add(dragged);
        if (destFolder != null) destFolder.IsExpanded = true;
        SortVariableTree(VariableTree);
        SyncVariableFoldersToModel();
    }

    private VariableLeafNode? FindVariableLeaf(PackVariableViewModel v) => FindVariableLeafIn(VariableTree, v);
    private static VariableLeafNode? FindVariableLeafIn(ObservableCollection<VariableTreeItem> coll, PackVariableViewModel v)
    {
        foreach (var c in coll)
        {
            if (c is VariableLeafNode l && l.Variable == v) return l;
            if (c is VariableFolderNode fn) { var found = FindVariableLeafIn(fn.Children, v); if (found != null) return found; }
        }
        return null;
    }

    private void RemoveVariableLeaf(VariableLeafNode leaf)
    {
        FindVariableParentChildren(leaf)?.Remove(leaf);
        Pack.Variables.Remove(leaf.Variable.Model);
        Variables.Remove(leaf.Variable);
        SyncVariableFoldersToModel();
        SelectedVariableTreeItem = null;
        SelectedVariable = Variables.FirstOrDefault();
    }

    // ── Copy / Paste / Duplicate for left-bar list items ─────────────────
    //
    // One set of commands that act on whichever tab's list is showing (bound
    // via SelectedTabIndex). Copy stores a deep clone in the static
    // EditorClipboard (survives pack switches — cross-pack within a session);
    // Paste is type-checked, so pasting only works into a matching list. Both
    // paste + duplicate de-collide the key/name so nothing clobbers an existing
    // item. Multi-list tabs act on their primary list (Busts→outfit, Places→place).

    private const int TabBusts = 0, TabPlaces = 1, TabMapButtons = 2, TabDialogues = 3,
                      TabActors = 4, TabScenes = 5, TabVariables = 6, TabWallpapers = 7,
                      TabMusic = 8, TabSfx = 9, TabIntegration = 10;

    private int _selectedTabIndex;
    /// <summary>Active tab (bound to the TabControl) — drives which list Copy/Paste/Duplicate hit.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public RelayCommand DuplicateItemCommand { get; private set; } = null!;
    public RelayCommand CopyItemCommand { get; private set; } = null!;
    public RelayCommand PasteItemCommand { get; private set; } = null!;

    /// <summary>Unique key: appends _copy / _copy2… (stripping any existing _copyN first).</summary>
    private static string UniqueKey(string baseKey, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(baseKey) || !set.Contains(baseKey)) return baseKey;
        var root = System.Text.RegularExpressions.Regex.Replace(baseKey, @"_copy\d*$", "");
        for (int i = 1; ; i++)
        {
            var cand = i == 1 ? root + "_copy" : root + "_copy" + i;
            if (!set.Contains(cand)) return cand;
        }
    }

    private void DuplicateFlat<TDef, TVm>(TDef? sel, List<TDef> model, ObservableCollection<TVm> vms,
        Func<TDef, string> keyGet, Action<TDef, string> keySet, Func<TDef, TVm> makeVm, Action<TVm> onAdded)
        where TDef : class
        => AddClone(sel == null ? null : EditorClipboard.CloneOne(sel), model, vms, keyGet, keySet, makeVm, onAdded);

    private void PasteFlat<TDef, TVm>(List<TDef> model, ObservableCollection<TVm> vms,
        Func<TDef, string> keyGet, Action<TDef, string> keySet, Func<TDef, TVm> makeVm, Action<TVm> onAdded)
        where TDef : class
        => AddClone(EditorClipboard.GetItem<TDef>(), model, vms, keyGet, keySet, makeVm, onAdded);

    private void AddClone<TDef, TVm>(TDef? clone, List<TDef> model, ObservableCollection<TVm> vms,
        Func<TDef, string> keyGet, Action<TDef, string> keySet, Func<TDef, TVm> makeVm, Action<TVm> onAdded)
        where TDef : class
    {
        if (clone == null) return;
        keySet(clone, UniqueKey(keyGet(clone), model.Select(keyGet)));
        model.Add(clone);
        var vm = makeVm(clone);
        vms.Add(vm);
        onAdded(vm);
    }

    private ActorViewModel MakeActorVm(ActorDef d)
    { var vm = new ActorViewModel(d); vm.DefaultBustKeyChanged += OnActorDefaultBustKeyChanged; return vm; }
    private SfxViewModel MakeSfxVm(SfxDef d)
    { var vm = new SfxViewModel(d); vm.PropertyChanged += OnSfxChanged; return vm; }

    private void DuplicateActiveItem()
    {
        switch (SelectedTabIndex)
        {
            case TabBusts: DuplicateOutfit(EditorClipboard.CloneOne(SelectedOutfit?.Model)); break;
            case TabPlaces: DuplicateFlat(SelectedPlace?.Model, Pack.Places, Places, p => p.Key, (p, k) => p.Key = k, d => new PlaceViewModel(d), vm => { SelectedPlace = vm; RebuildDialogueRoomTalkOptions(); }); break;
            case TabMapButtons: DuplicateFlat(SelectedMapButton?.Model, Pack.MapButtons, MapButtons, _ => "", (_, __) => { }, d => new MapButtonViewModel(d, RemoveMapButtonVm), vm => SelectedMapButton = vm); break;
            case TabDialogues: DuplicateDialogue(SelectedDialogue == null ? null : EditorClipboard.CloneOne(SelectedDialogue.Model)); break;
            case TabActors: DuplicateFlat(SelectedActor?.Model, Pack.Actors, Actors, a => a.Key, (a, k) => a.Key = k, MakeActorVm, vm => { SelectedActor = vm; RebuildActorAndBustOptions(); }); break;
            case TabScenes: DuplicateFlat(SelectedScene?.Model, Pack.Scenes, Scenes, s => s.Key, (s, k) => s.Key = k, d => new SceneViewModel(d), vm => { SelectedScene = vm; RebuildSceneOptions(); }); break;
            case TabVariables: DuplicateFlat(SelectedVariable?.Model, Pack.Variables, Variables, v => v.Name, (v, k) => v.Name = k, d => new PackVariableViewModel(d), vm => { BuildVariableTree(); SelectedVariable = vm; SelectedVariableTreeItem = FindVariableLeaf(vm); RebuildVariableNameOptions(); }); break;
            case TabWallpapers: DuplicateFlat(SelectedWallpaper?.Model, Pack.Wallpapers, Wallpapers, w => w.Key, (w, k) => w.Key = k, d => new WallpaperViewModel(d), vm => SelectedWallpaper = vm); break;
            case TabMusic: DuplicateFlat(SelectedMusic?.Model, Pack.Music, Music, m => m.Key, (m, k) => m.Key = k, d => new MusicViewModel(d), vm => { SelectedMusic = vm; RebuildMusicKeyOptions(); }); break;
            case TabSfx: DuplicateFlat(SelectedSfx?.Model, Pack.Sfx, Sfx, s => s.Key, (s, k) => s.Key = k, MakeSfxVm, vm => { SelectedSfx = vm; RebuildSfxKeyOptions(); }); break;
            case TabIntegration: DuplicateFlat(SelectedIntegrationRule?.Model, Pack.IntegrationRules, IntegrationRules, r => r.Key, (r, k) => r.Key = k, d => new UpdateRuleViewModel(d), vm => SelectedIntegrationRule = vm); break;
        }
    }

    private void CopyActiveItem()
    {
        switch (SelectedTabIndex)
        {
            case TabBusts: if (SelectedOutfit != null) EditorClipboard.SetItem(SelectedOutfit.Model); break;
            case TabPlaces: if (SelectedPlace != null) EditorClipboard.SetItem(SelectedPlace.Model); break;
            case TabMapButtons: if (SelectedMapButton != null) EditorClipboard.SetItem(SelectedMapButton.Model); break;
            case TabDialogues: if (SelectedDialogue != null) EditorClipboard.SetItem(SelectedDialogue.Model); break;
            case TabActors: if (SelectedActor != null) EditorClipboard.SetItem(SelectedActor.Model); break;
            case TabScenes: if (SelectedScene != null) EditorClipboard.SetItem(SelectedScene.Model); break;
            case TabVariables: if (SelectedVariable != null) EditorClipboard.SetItem(SelectedVariable.Model); break;
            case TabWallpapers: if (SelectedWallpaper != null) EditorClipboard.SetItem(SelectedWallpaper.Model); break;
            case TabMusic: if (SelectedMusic != null) EditorClipboard.SetItem(SelectedMusic.Model); break;
            case TabSfx: if (SelectedSfx != null) EditorClipboard.SetItem(SelectedSfx.Model); break;
            case TabIntegration: if (SelectedIntegrationRule != null) EditorClipboard.SetItem(SelectedIntegrationRule.Model); break;
        }
    }

    private void PasteActiveItem()
    {
        switch (SelectedTabIndex)
        {
            case TabBusts: DuplicateOutfit(EditorClipboard.GetItem<OutfitDef>()); break;
            case TabPlaces: PasteFlat(Pack.Places, Places, p => p.Key, (p, k) => p.Key = k, d => new PlaceViewModel(d), vm => { SelectedPlace = vm; RebuildDialogueRoomTalkOptions(); }); break;
            case TabMapButtons: PasteFlat(Pack.MapButtons, MapButtons, _ => "", (_, __) => { }, d => new MapButtonViewModel(d, RemoveMapButtonVm), vm => SelectedMapButton = vm); break;
            case TabDialogues: DuplicateDialogue(EditorClipboard.GetItem<DialogueDef>()); break;
            case TabActors: PasteFlat(Pack.Actors, Actors, a => a.Key, (a, k) => a.Key = k, MakeActorVm, vm => { SelectedActor = vm; RebuildActorAndBustOptions(); }); break;
            case TabScenes: PasteFlat(Pack.Scenes, Scenes, s => s.Key, (s, k) => s.Key = k, d => new SceneViewModel(d), vm => { SelectedScene = vm; RebuildSceneOptions(); }); break;
            case TabVariables: PasteFlat(Pack.Variables, Variables, v => v.Name, (v, k) => v.Name = k, d => new PackVariableViewModel(d), vm => { BuildVariableTree(); SelectedVariable = vm; SelectedVariableTreeItem = FindVariableLeaf(vm); RebuildVariableNameOptions(); }); break;
            case TabWallpapers: PasteFlat(Pack.Wallpapers, Wallpapers, w => w.Key, (w, k) => w.Key = k, d => new WallpaperViewModel(d), vm => SelectedWallpaper = vm); break;
            case TabMusic: PasteFlat(Pack.Music, Music, m => m.Key, (m, k) => m.Key = k, d => new MusicViewModel(d), vm => { SelectedMusic = vm; RebuildMusicKeyOptions(); }); break;
            case TabSfx: PasteFlat(Pack.Sfx, Sfx, s => s.Key, (s, k) => s.Key = k, MakeSfxVm, vm => { SelectedSfx = vm; RebuildSfxKeyOptions(); }); break;
            case TabIntegration: PasteFlat(Pack.IntegrationRules, IntegrationRules, r => r.Key, (r, k) => r.Key = k, d => new UpdateRuleViewModel(d), vm => SelectedIntegrationRule = vm); break;
        }
    }

    /// <summary>Duplicate/paste an outfit into the selected outfit's character (else the first character).</summary>
    private void DuplicateOutfit(OutfitDef? clone)
    {
        if (clone == null) return;
        var ch = (SelectedOutfit != null ? Characters.FirstOrDefault(c => c.Outfits.Contains(SelectedOutfit)) : null)
                 ?? Characters.FirstOrDefault();
        if (ch == null) return;
        clone.Key = UniqueKey(clone.Key, ch.Model.Outfits.Select(o => o.Key));
        ch.Model.Outfits.Add(clone);
        var vm = new OutfitViewModel(clone);
        ch.Outfits.Add(vm);
        SelectedOutfit = vm;
    }

    /// <summary>Duplicate/paste a dialogue: clone, de-collide key, add next to the original in the tree.</summary>
    private void DuplicateDialogue(DialogueDef? clone)
    {
        if (clone == null) return;
        clone.Key = UniqueKey(clone.Key, Pack.Dialogues.Select(d => d.Key));
        Pack.Dialogues.Add(clone);
        var vm = new DialogueViewModel(clone);
        Dialogues.Add(vm);
        var leaf = new DialogueLeafNode(vm);
        var origLeaf = SelectedDialogue != null ? FindLeaf(SelectedDialogue) : null;
        (origLeaf != null ? FindParentChildren(origLeaf) ?? DialogueTree : DialogueTree).Add(leaf);
        SyncFoldersToModel();
        SelectedDialogueTreeItem = leaf;
    }

    /// <summary>The collection that directly contains <paramref name="item"/> (its parent's Children, or the root).</summary>
    public ObservableCollection<DialogueTreeItem>? FindParentChildren(DialogueTreeItem item)
        => DialogueTree.Contains(item) ? DialogueTree : FindParentIn(DialogueTree, item);

    private static ObservableCollection<DialogueTreeItem>? FindParentIn(
        ObservableCollection<DialogueTreeItem> coll, DialogueTreeItem item)
    {
        foreach (var c in coll)
            if (c is DialogueFolderNode fn)
            {
                if (fn.Children.Contains(item)) return fn.Children;
                var found = FindParentIn(fn.Children, item);
                if (found != null) return found;
            }
        return null;
    }

    private static bool IsDescendant(DialogueFolderNode folder, DialogueTreeItem? maybe)
    {
        if (maybe == null) return false;
        foreach (var c in folder.Children)
        {
            if (c == maybe) return true;
            if (c is DialogueFolderNode sub && IsDescendant(sub, maybe)) return true;
        }
        return false;
    }

    /// <summary>
    /// Move a dragged tree item onto a drop target: into a folder, beside a
    /// leaf (into the leaf's containing folder), or to the root when the target
    /// is null (empty space). A folder can't be dropped into itself or one of
    /// its own descendants.
    /// </summary>
    public void MoveTreeItem(DialogueTreeItem dragged, DialogueTreeItem? target)
    {
        if (dragged == null || dragged == target) return;

        ObservableCollection<DialogueTreeItem> dest;
        DialogueFolderNode? destFolder = null;
        if (target is DialogueFolderNode f) { dest = f.Children; destFolder = f; }
        else if (target is DialogueLeafNode leaf) dest = FindParentChildren(leaf) ?? DialogueTree;
        else dest = DialogueTree;

        if (dragged is DialogueFolderNode df &&
            (df == destFolder || IsDescendant(df, destFolder) || IsDescendant(df, target)))
            return;

        var from = FindParentChildren(dragged);
        if (from == null || from == dest) return;

        Undo.Checkpoint();
        from.Remove(dragged);
        dest.Add(dragged);
        if (destFolder != null) destFolder.IsExpanded = true;
        SortTree(DialogueTree);
        SyncFoldersToModel();
    }

    /// <summary>
    /// Rebuild the roomtalk picker the dialogue editor uses. Includes every
    /// vanilla roomtalk plus one entry per pack place (each pack place
    /// constructs a same-named roomtalk under <c>8_Room_Talk</c> at runtime).
    /// </summary>
    public void RebuildDialogueRoomTalkOptions()
    {
        var roomTalkOpts = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var r in VanillaRoomTalks.All)
            roomTalkOpts.Add(new NavigatorTargetOption(
                Token: $"vanilla:{r.Name}",
                DisplayLabel: $"Vanilla — {r.DisplayName} ({r.Name})"));
        foreach (var p in Places)
            roomTalkOpts.Add(new NavigatorTargetOption(
                Token: $"place:{p.Key}",
                DisplayLabel: $"This pack — {p.DisplayName} (place:{p.Key})"));
        foreach (var name in Pack.CustomRoomTalks)
            roomTalkOpts.Add(new NavigatorTargetOption(
                Token: $"vanilla:{name}",
                DisplayLabel: $"This pack — custom roomtalk ({name})"));
        RoomTalkOptions.Clear();
        foreach (var r in roomTalkOpts.OrderBy(r => r.DisplayLabel, System.StringComparer.OrdinalIgnoreCase))
            RoomTalkOptions.Add(r);
    }

    /// <summary>
    /// Register a new custom roomtalk and point the selected dialogue at it
    /// (token <c>vanilla:&lt;name&gt;</c>). The runtime creates the node on the
    /// fly by cloning an existing roomtalk — same as the host's CreateNewRoomTalk.
    /// </summary>
    public void AddCustomRoomTalk(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return;
        Undo.Checkpoint();
        if (!Pack.CustomRoomTalks.Contains(name) && VanillaRoomTalks.FindByName(name) == null)
            Pack.CustomRoomTalks.Add(name);
        RebuildDialogueRoomTalkOptions();
        if (SelectedDialogue != null) SelectedDialogue.RoomTalk = $"vanilla:{name}";
    }

    /// <summary>
    /// Rebuild <see cref="LevelOptions"/> — every vanilla level GO plus
    /// every pack place. Tokens match what the plugin's
    /// <c>LevelActive</c> condition resolves: <c>vanilla:&lt;goName&gt;</c>
    /// against <c>5_Levels.Find</c>, <c>place:&lt;key&gt;</c> against the
    /// pack's own place registry.
    /// </summary>
    public void RebuildLevelOptions()
    {
        var levelOpts = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var v in VanillaPlaces.All)
            levelOpts.Add(new NavigatorTargetOption(
                Token: $"vanilla:{v.GoName}",
                DisplayLabel: $"Vanilla — {v.DisplayName} ({v.GoName})"));
        foreach (var p in Places)
            levelOpts.Add(new NavigatorTargetOption(
                Token: $"place:{p.Key}",
                DisplayLabel: $"This pack — {p.DisplayName} (place:{p.Key})"));
        LevelOptions.Clear();
        foreach (var l in levelOpts.OrderBy(l => l.DisplayLabel, System.StringComparer.OrdinalIgnoreCase))
            LevelOptions.Add(l);
    }

    private void AddDialogue()
    {
        var def = new DialogueDef
        {
            Key = $"dialogue{Pack.Dialogues.Count + 1}",
            DisplayName = "New Dialogue",
            RoomTalk = "vanilla:Beach",
        };
        Pack.Dialogues.Add(def);
        var vm = new DialogueViewModel(def);
        Dialogues.Add(vm);
        // Place the new dialogue in the currently-selected folder (if any), else at root.
        var leaf = new DialogueLeafNode(vm);
        if (SelectedDialogueTreeItem is DialogueFolderNode target)
        {
            target.Children.Add(leaf);
            target.IsExpanded = true;
        }
        else DialogueTree.Add(leaf);
        SortTree(DialogueTree);
        SyncFoldersToModel();
        SelectedDialogueTreeItem = leaf;
        SelectedDialogue = vm;
    }

    private void RemoveDialogue()
    {
        var sel = SelectedDialogueTreeItem;
        if (sel is DialogueFolderNode folder)
        {
            // Delete the folder; its children move up to where the folder was.
            var parent = FindParentChildren(folder) ?? DialogueTree;
            int idx = parent.IndexOf(folder);
            parent.Remove(folder);
            foreach (var c in folder.Children.ToList()) parent.Insert(idx++, c);
            SortTree(DialogueTree);
            SyncFoldersToModel();
            SelectedDialogueTreeItem = null;
            return;
        }
        var leaf = sel as DialogueLeafNode ?? (SelectedDialogue != null ? FindLeaf(SelectedDialogue) : null);
        if (leaf != null) RemoveLeaf(leaf);
    }

    private void RemoveLeaf(DialogueLeafNode leaf)
    {
        FindParentChildren(leaf)?.Remove(leaf);
        Pack.Dialogues.Remove(leaf.Dialogue.Model);
        Dialogues.Remove(leaf.Dialogue);
        SyncFoldersToModel();
        SelectedDialogueTreeItem = null;
        SelectedDialogue = Dialogues.FirstOrDefault();
    }

    private DialogueLeafNode? FindLeaf(DialogueViewModel d) => FindLeafIn(DialogueTree, d);
    private static DialogueLeafNode? FindLeafIn(ObservableCollection<DialogueTreeItem> coll, DialogueViewModel d)
    {
        foreach (var c in coll)
        {
            if (c is DialogueLeafNode l && l.Dialogue == d) return l;
            if (c is DialogueFolderNode fn) { var found = FindLeafIn(fn.Children, d); if (found != null) return found; }
        }
        return null;
    }

    private void AddDialogueRootNode()
    {
        if (SelectedDialogue is null) return;
        var n = SelectedDialogue.AddNode(parentId: null);
        SelectedNode = n;
    }

    private void AddDialogueChildNode()
    {
        if (SelectedDialogue is null || SelectedNode is null) return;
        var n = SelectedDialogue.AddNode(parentId: SelectedNode.Id);
        SelectedNode = n;
    }

    private void AddDialogueSiblingNode()
    {
        if (SelectedDialogue is null || SelectedNode is null) return;
        var n = SelectedDialogue.AddSibling(SelectedNode);
        if (n != null) SelectedNode = n;
    }

    private void RemoveDialogueNode()
    {
        if (SelectedDialogue is null || SelectedNode is null) return;
        SelectedDialogue.RemoveNode(SelectedNode);
        SelectedNode = SelectedDialogue.Nodes.FirstOrDefault();
    }

    private void AddDialogueStartCondition() => SelectedDialogue?.AddStartCondition();
    private void AddNodeActionOnStart()       => SelectedNode?.AddActionOnStart();
    private void AddNodeActionOnFinish()      => SelectedNode?.AddActionOnFinish();
    private void AddNodeCondition()           => SelectedNode?.AddCondition();

    // ── Actor rebind / options / commands ─────────────────────────────

    private void RebindActors()
    {
        // Unhook old subscriptions before clearing — otherwise the now-detached
        // ActorViewModels would keep this MainViewModel alive via the
        // DefaultBustKeyChanged delegate.
        foreach (var a in Actors) a.DefaultBustKeyChanged -= OnActorDefaultBustKeyChanged;
        Actors.Clear();
        foreach (var a in Pack.Actors)
        {
            var vm = new ActorViewModel(a);
            vm.DefaultBustKeyChanged += OnActorDefaultBustKeyChanged;
            Actors.Add(vm);
        }
        SelectedActor = Actors.FirstOrDefault();
    }

    private void OnActorDefaultBustKeyChanged(object? sender, System.EventArgs e)
        => OnPropertyChanged(nameof(SelectedNodeActorBustKey));

    /// <summary>
    /// Rebuild the actor + bust pickers. Actor keys feed the per-node
    /// actor combo. The bust list pulls from <see cref="VanillaBusts"/>
    /// plus every pack-authored outfit's GameObjectName — both forms
    /// resolve to the same <see cref="UnityEngine.GameObject.Find"/> at
    /// runtime so the picker doesn't need to differentiate beyond labels.
    /// </summary>
    public void RebuildActorAndBustOptions()
    {
        ActorOptions.Clear();
        foreach (var a in Actors.OrderBy(a => a.Key, System.StringComparer.OrdinalIgnoreCase))
            ActorOptions.Add(a.Key);

        var bustOpts = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var v in VanillaBusts.All)
            bustOpts.Add(new NavigatorTargetOption(
                Token: v.GoName,
                DisplayLabel: $"Vanilla — {v.Character} ({v.GoName})"));
        foreach (var ch in Characters)
            foreach (var o in ch.Outfits)
                bustOpts.Add(new NavigatorTargetOption(
                    Token: o.GameObjectName,
                    DisplayLabel: $"Pack ({ch.Name}) — {o.GameObjectName}"));
        BustNameOptions.Clear();
        foreach (var b in bustOpts.OrderBy(b => b.DisplayLabel, System.StringComparer.OrdinalIgnoreCase))
            BustNameOptions.Add(b);

        // ActorBustOptions: distinct default-bust GO names from the
        // pack's actors. Falls back to BustNameOptions when no actors
        // are declared yet so the dropdown isn't empty when the user
        // first opens it. De-duped by Token.
        var seenBust = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var actorBustOpts = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var a in Actors)
        {
            if (string.IsNullOrEmpty(a.DefaultBustKey)) continue;
            if (!seenBust.Add(a.DefaultBustKey)) continue;
            var vb = VanillaBusts.FindByGoName(a.DefaultBustKey);
            string label = vb != null
                ? $"{a.DisplayName} → Vanilla {vb.Character} ({a.DefaultBustKey})"
                : $"{a.DisplayName} → {a.DefaultBustKey}";
            actorBustOpts.Add(new NavigatorTargetOption(
                Token: a.DefaultBustKey, DisplayLabel: label));
        }
        ActorBustOptions.Clear();
        foreach (var o in actorBustOpts.OrderBy(o => o.DisplayLabel, System.StringComparer.OrdinalIgnoreCase))
            ActorBustOptions.Add(o);

        // Vanilla bust prefab carries these four expression children; any pack
        // actor that doesn't declare overrides expects the same names. Seed
        // them, fold in custom keys, then surface the whole list alphabetically.
        var exprOpts = new System.Collections.Generic.List<string> { "Happy", "Angry", "Sad", "Flirty" };
        var seenExpr = new HashSet<string>(System.StringComparer.Ordinal)
            { "Happy", "Angry", "Sad", "Flirty" };
        foreach (var a in Actors)
            foreach (var e in a.Expressions)
                if (!string.IsNullOrEmpty(e.Key) && seenExpr.Add(e.Key))
                    exprOpts.Add(e.Key);
        ExpressionKeyOptions.Clear();
        foreach (var e in exprOpts.OrderBy(e => e, System.StringComparer.OrdinalIgnoreCase))
            ExpressionKeyOptions.Add(e);
    }

    private void AddActor()
    {
        var def = new ActorDef
        {
            Key = $"actor{Pack.Actors.Count + 1}",
            DisplayName = "New Actor",
        };
        Pack.Actors.Add(def);
        var vm = new ActorViewModel(def);
        vm.DefaultBustKeyChanged += OnActorDefaultBustKeyChanged;
        Actors.Add(vm);
        SelectedActor = vm;
        RebuildActorAndBustOptions();
    }

    private void RemoveActor()
    {
        if (SelectedActor is null) return;
        SelectedActor.DefaultBustKeyChanged -= OnActorDefaultBustKeyChanged;
        Pack.Actors.Remove(SelectedActor.Model);
        Actors.Remove(SelectedActor);
        SelectedActor = Actors.FirstOrDefault();
        RebuildActorAndBustOptions();
    }

    private void AddActorExpression() => SelectedActor?.AddExpression();

    private void AddActorOutfit() => SelectedActor?.AddOutfit("");

    // ── Variable rebind / options / commands ──────────────────────────

    private void RebindVariables()
    {
        Variables.Clear();
        foreach (var v in Pack.Variables) Variables.Add(new PackVariableViewModel(v));
        BuildVariableTree();
        SelectedVariable = Variables.FirstOrDefault();
    }

    public void RebuildVariableNameOptions()
    {
        VariableNameOptions.Clear();
        ListVariableNameOptions.Clear();
        foreach (var v in Variables.OrderBy(v => v.Name, System.StringComparer.OrdinalIgnoreCase))
        {
            VariableNameOptions.Add(v.Name);
            // List-typed variables also surface in the
            // List-only dropdown used by AddToList /
            // RemoveFromList / ClearList action params.
            if (v.Model.Type == PackVariableType.List)
                ListVariableNameOptions.Add(v.Name);
        }
    }

    /// <summary>
    /// Rebuilds <see cref="MusicKeyOptions"/> from the Music tab. Called
    /// after rebind, add and remove operations so the SwitchMusic
    /// dropdown stays current.
    /// </summary>
    public void RebuildMusicKeyOptions()
    {
        MusicKeyOptions.Clear();
        foreach (var m in Music.OrderBy(m => m.Key, System.StringComparer.OrdinalIgnoreCase))
            MusicKeyOptions.Add(m.Key);
    }

    /// <summary>Rebuilds <see cref="SfxKeyOptions"/> from the SFX tab.</summary>
    public void RebuildSfxKeyOptions()
    {
        SfxKeyOptions.Clear();
        foreach (var s in Sfx.OrderBy(s => s.Key, System.StringComparer.OrdinalIgnoreCase))
            SfxKeyOptions.Add(s.Key);
    }

    /// <summary>
    /// Rebuilds <see cref="GameObjectNameOptions"/> from the GameObjects this
    /// pack names: every place overlay plus every outfit bust GO. Read straight
    /// from the models so a just-renamed entry is picked up. Called whenever a
    /// dialogue node / integration rule is selected (when the GO-path editors
    /// appear) and on pack load.
    /// </summary>
    public void RebuildGameObjectNameOptions()
    {
        var names = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var busts = new System.Collections.Generic.SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in Places)
            foreach (var o in FlattenOverlays(p.Overlays))
                if (!string.IsNullOrWhiteSpace(o.Name)) names.Add(o.Name);
        foreach (var ch in Characters)
            foreach (var o in ch.Outfits)
                if (!string.IsNullOrWhiteSpace(o.GameObjectName)) { names.Add(o.GameObjectName); busts.Add(o.GameObjectName); }

        GameObjectNameOptions.Clear();
        foreach (var n in names) GameObjectNameOptions.Add(n);
        BustNameOnlyOptions.Clear();
        foreach (var n in busts) BustNameOnlyOptions.Add(n);
    }

    private void AddVariable()
    {
        var def = new PackVariableDef
        {
            Name = $"var{Pack.Variables.Count + 1}",
            Type = PackVariableType.Bool,
            DefaultValue = "false",
            Persisted = true,
        };
        Pack.Variables.Add(def);
        var vm = new PackVariableViewModel(def);
        Variables.Add(vm);
        // Place the new variable in the currently-selected folder (if any), else root.
        var leaf = new VariableLeafNode(vm);
        if (SelectedVariableTreeItem is VariableFolderNode target)
        {
            target.Children.Add(leaf);
            target.IsExpanded = true;
        }
        else VariableTree.Add(leaf);
        SortVariableTree(VariableTree);
        SyncVariableFoldersToModel();
        SelectedVariableTreeItem = leaf;
        SelectedVariable = vm;
        RebuildVariableNameOptions();
    }

    private void RemoveVariable()
    {
        var sel = SelectedVariableTreeItem;
        if (sel is VariableFolderNode folder)
        {
            // Delete the folder; its children move up to where the folder was.
            var parent = FindVariableParentChildren(folder) ?? VariableTree;
            int idx = parent.IndexOf(folder);
            parent.Remove(folder);
            foreach (var c in folder.Children.ToList()) parent.Insert(idx++, c);
            SortVariableTree(VariableTree);
            SyncVariableFoldersToModel();
            SelectedVariableTreeItem = null;
            return;
        }
        var leaf = sel as VariableLeafNode ?? (SelectedVariable != null ? FindVariableLeaf(SelectedVariable) : null);
        if (leaf != null) RemoveVariableLeaf(leaf);
        RebuildVariableNameOptions();
    }

    // ── Scene rebind / options / commands ─────────────────────────────

    private void RebindScenes()
    {
        Scenes.Clear();
        foreach (var s in Pack.Scenes) Scenes.Add(new SceneViewModel(s));
        SelectedScene = Scenes.FirstOrDefault();
    }

    /// <summary>
    /// Rebuilds the <see cref="VanillaFrameOptions"/> list once. The catalog
    /// is static across the editor's lifetime so there's no input event
    /// that needs to re-run this; it's separated out only because
    /// construction-time and after-Open both need it.
    /// </summary>
    public void RebuildVanillaFrameOptions()
    {
        VanillaFrameOptions.Clear();
        foreach (var f in VanillaFrames.All.OrderBy(f => f.DisplayName, System.StringComparer.OrdinalIgnoreCase))
            VanillaFrameOptions.Add(new NavigatorTargetOption(
                Token: f.FileName,
                DisplayLabel: $"{f.DisplayName} ({f.FileName})"));
    }

    /// <summary>
    /// Rebuilds <see cref="SceneOptions"/> from the pack's scene list. The
    /// <c>ActivateScene</c> action's combo binds to this. Tokens are bare
    /// scene keys — the runtime looks them up in its per-pack scene
    /// registry.
    /// </summary>
    public void RebuildSceneOptions()
    {
        SceneOptions.Clear();
        SceneKeyOptions.Clear();
        foreach (var s in Scenes.OrderBy(
                     s => string.IsNullOrWhiteSpace(s.DisplayName) ? s.Key : s.DisplayName,
                     System.StringComparer.OrdinalIgnoreCase))
        {
            SceneOptions.Add(new NavigatorTargetOption(
                Token: s.Key,
                DisplayLabel: string.IsNullOrWhiteSpace(s.DisplayName) ? s.Key : $"{s.DisplayName} ({s.Key})"));
            if (!string.IsNullOrWhiteSpace(s.Key)) SceneKeyOptions.Add(s.Key);
        }
    }

    private void AddScene()
    {
        var def = new SceneDef
        {
            Key = $"scene{Pack.Scenes.Count + 1}",
            DisplayName = "New Scene",
            VanillaFrame = VanillaFrames.All[0].FileName,
            Sound = SceneSoundMode.Silent,
        };
        Pack.Scenes.Add(def);
        var vm = new SceneViewModel(def);
        Scenes.Add(vm);
        SelectedScene = vm;
        RebuildSceneOptions();
    }

    private void RemoveScene()
    {
        if (SelectedScene is null) return;
        Pack.Scenes.Remove(SelectedScene.Model);
        Scenes.Remove(SelectedScene);
        SelectedScene = Scenes.FirstOrDefault();
        RebuildSceneOptions();
    }

    // ── Wallpaper / Music / SFX rebind + commands ─────────────────────
    //
    // These three lists round-trip through the pack manifest as
    // plain JSON arrays; the tabs are bare collection editors (no
    // option dropdowns to rebuild). Add / Remove mirrors the Scenes
    // pattern. Selecting a freshly added entry steers the editor
    // straight at it so the user can start filling in fields without
    // a second click.

    private void RebindWallpapers()
    {
        Wallpapers.Clear();
        foreach (var w in Pack.Wallpapers) Wallpapers.Add(new WallpaperViewModel(w));
        SelectedWallpaper = Wallpapers.FirstOrDefault();
    }

    private void AddWallpaper()
    {
        var def = new WallpaperDef
        {
            Key = $"wallpaper{Pack.Wallpapers.Count + 1}",
            DisplayName = "New Wallpaper",
        };
        Pack.Wallpapers.Add(def);
        var vm = new WallpaperViewModel(def);
        Wallpapers.Add(vm);
        SelectedWallpaper = vm;
    }

    private void RemoveWallpaper()
    {
        if (SelectedWallpaper is null) return;
        Pack.Wallpapers.Remove(SelectedWallpaper.Model);
        Wallpapers.Remove(SelectedWallpaper);
        SelectedWallpaper = Wallpapers.FirstOrDefault();
    }

    private void RebindMusic()
    {
        Music.Clear();
        foreach (var m in Pack.Music) Music.Add(new MusicViewModel(m));
        SelectedMusic = Music.FirstOrDefault();
        RebuildMusicKeyOptions();
    }

    private void AddMusic()
    {
        var def = new MusicDef
        {
            Key = $"music{Pack.Music.Count + 1}",
            DisplayName = "New Music",
        };
        Pack.Music.Add(def);
        var vm = new MusicViewModel(def);
        Music.Add(vm);
        SelectedMusic = vm;
        RebuildMusicKeyOptions();
    }

    private void RemoveMusic()
    {
        if (SelectedMusic is null) return;
        Pack.Music.Remove(SelectedMusic.Model);
        Music.Remove(SelectedMusic);
        SelectedMusic = Music.FirstOrDefault();
        RebuildMusicKeyOptions();
    }

    private void RebindSfx()
    {
        foreach (var s in Sfx) s.PropertyChanged -= OnSfxChanged;
        Sfx.Clear();
        foreach (var s in Pack.Sfx)
        {
            var vm = new SfxViewModel(s);
            vm.PropertyChanged += OnSfxChanged;
            Sfx.Add(vm);
        }
        SelectedSfx = Sfx.FirstOrDefault();
        RebuildSfxKeyOptions();
    }

    /// <summary>Keep the PlaySFX <c>clip</c> dropdown current when an SFX's key
    /// is renamed (otherwise it showed the stale key until an app restart).</summary>
    private void OnSfxChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SfxViewModel.Key)) RebuildSfxKeyOptions();
    }

    private void AddSfx()
    {
        var def = new SfxDef
        {
            Key = $"sfx{Pack.Sfx.Count + 1}",
            DisplayName = "New SFX",
        };
        Pack.Sfx.Add(def);
        var vm = new SfxViewModel(def);
        vm.PropertyChanged += OnSfxChanged;
        Sfx.Add(vm);
        SelectedSfx = vm;
        RebuildSfxKeyOptions();
    }

    private void RemoveSfx()
    {
        if (SelectedSfx is null) return;
        SelectedSfx.PropertyChanged -= OnSfxChanged;
        Pack.Sfx.Remove(SelectedSfx.Model);
        Sfx.Remove(SelectedSfx);
        SelectedSfx = Sfx.FirstOrDefault();
        RebuildSfxKeyOptions();
    }

    private readonly Services.SfxPreviewPlayer _sfxPreview = new();

    private bool CanPlaySfx(SfxViewModel? sfx)
        => sfx != null && !string.IsNullOrWhiteSpace(sfx.AudioPath) && !string.IsNullOrEmpty(PackRoot);

    /// <summary>
    /// Preview the SFX through the editor's own audio output at its authored
    /// default volume (blank volume = full). Resolves the pack-relative
    /// <see cref="SfxViewModel.AudioPath"/> against <see cref="PackRoot"/>, so
    /// the pack must be saved to disk. Plays the base clip (not a random
    /// variant) so previews are repeatable.
    /// </summary>
    private void PlaySfx(SfxViewModel? sfx)
    {
        if (!CanPlaySfx(sfx)) return;
        string rel = sfx!.AudioPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        string abs = System.IO.Path.Combine(PackRoot!, rel);
        float volume = sfx.Model.DefaultVolume ?? 1f;
        _sfxPreview.Play(abs, volume);
    }

    // ── Integration rules ─────────────────────────────────────────────
    //
    // Pack-side "if condition then actions" rules evaluated each
    // frame by the plugin's update loop. The editor's Integration
    // tab binds to IntegrationRules + SelectedIntegrationRule;
    // per-rule conditions / actions surface as ObservableCollections
    // on the UpdateRuleViewModel so the picker UI reuses the same
    // row rendering pattern as the dialogue node editor.

    private void RebindIntegrationRules()
    {
        IntegrationRules.Clear();
        foreach (var r in Pack.IntegrationRules)
            IntegrationRules.Add(new UpdateRuleViewModel(r));
        SelectedIntegrationRule = IntegrationRules.FirstOrDefault();
    }

    private void AddIntegrationRule()
    {
        var def = new UpdateRuleDef
        {
            Key = $"rule{Pack.IntegrationRules.Count + 1}",
            DisplayName = "New Rule",
        };
        Pack.IntegrationRules.Add(def);
        var vm = new UpdateRuleViewModel(def);
        IntegrationRules.Add(vm);
        SelectedIntegrationRule = vm;
    }

    private void RemoveIntegrationRule()
    {
        if (SelectedIntegrationRule is null) return;
        Pack.IntegrationRules.Remove(SelectedIntegrationRule.Model);
        IntegrationRules.Remove(SelectedIntegrationRule);
        SelectedIntegrationRule = IntegrationRules.FirstOrDefault();
    }

    private void Validate()
    {
        Issues.Clear();
        if (PackRoot is null) return;
        foreach (var issue in PackValidator.Validate(Pack, PackRoot))
            Issues.Add(issue);
    }
}
