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
    private void MarkSaved() => _savedSnapshot = PackRepository.SerializeAsSaved(Pack);

    /// <summary>
    /// True when the in-memory pack differs from the last saved snapshot.
    /// Computed by re-serializing the model (the VMs write through to it), so
    /// it catches edits regardless of which field changed. The window's close
    /// handler reads this to prompt before discarding work.
    /// </summary>
    public bool HasUnsavedChanges => PackRepository.SerializeAsSaved(Pack) != _savedSnapshot;

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
        // Remember the selection on the other tabs too, so an undo lands the user
        // back on the same place / NPC (RebindAll resets each to the first item)
        // — otherwise reverting a Places-tab edit would jump away and look broken.
        string? placeKey = SelectedPlace?.Key;
        string? npcKey = SelectedNpc?.Key;
        // Busts too: RebindAll drops the selection onto the first character's
        // first outfit, so without this an undo on the Busts tab throws you onto
        // a different bust than the one you were editing.
        string? outfitKey = SelectedOutfit?.Key;
        // The Places tab shows either a pack place or a vanilla extension, never
        // both — setting one selection clears the other. Only the place was ever
        // restored, so an undo made while editing an extension put the place
        // editor back on screen and moved the user to a different level entirely.
        string? extSource = SelectedVanillaExtension?.Source;

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

        if (placeKey != null)
        {
            var p = Places.FirstOrDefault(x => x.Key == placeKey);
            if (p != null) SelectedPlace = p;
        }
        if (npcKey != null)
        {
            var n = Npcs.FirstOrDefault(x => x.Key == npcKey);
            if (n != null) SelectedNpc = n;
        }
        if (outfitKey != null)
        {
            var o = Characters.SelectMany(c => c.Outfits).FirstOrDefault(x => x.Key == outfitKey);
            if (o != null) SelectedOutfit = o;
        }
        // Last, so it wins: the two are mutually exclusive and at most one of
        // them was set when the snapshot was taken.
        if (extSource != null)
        {
            var e = VanillaExtensions.FirstOrDefault(x => x.Source == extSource);
            if (e != null) SelectedVanillaExtension = e;
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
    public ObservableCollection<NpcViewModel> Npcs { get; } = new();
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

    // The condition-type picker list now lives on the row VM
    // (NodeConditionViewModel.AvailableTypes) because it varies by context:
    // a one-shot host (dialogue node conditions, level hooks) offers the
    // single-roll Random gate, a polled one doesn't.

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
            // The export commands' CanExecute is gated on PackRoot — kick
            // a requery so the menu items flip between disabled (in-
            // memory only) and enabled (saved to disk).
            ExportPackCommand?.Raise();
            ExportPackAsCommand?.Raise();
        }
    }

    public string Title => $"SMSModForge for {ModPack.CurrentGameVersion} — {Pack.PackId}" + (PackRoot is null ? " (unsaved)" : $" — {PackRoot}");

    /// <summary>Menu-bar corner label: the game build this editor targets.</summary>
    public string GameVersionHeader => $"for Starmaker Story {ModPack.CurrentGameVersion}";

    private OutfitViewModel? _selectedOutfit;
    public OutfitViewModel? SelectedOutfit
    {
        get => _selectedOutfit;
        set { _selectedOutfit = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedCharacter)); }
    }

    /// <summary>
    /// The character owning <see cref="SelectedOutfit"/>. Derived rather than
    /// stored, since the tree's selection is whichever row was clicked and an
    /// outfit belongs to exactly one character.
    /// <para/>
    /// Exists so the Busts tab can edit the character's own name and display
    /// name: the sidebar shows both, but nothing anywhere could set them, which
    /// left a new character stuck reading as "NewChar&lt;n&gt;".
    /// </summary>
    public CharacterViewModel? SelectedCharacter =>
        _selectedOutfit == null
            ? null
            : Characters.FirstOrDefault(c => c.Outfits.Contains(_selectedOutfit));

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
            // Refresh global NPC options in case we're looking at an NPC placement.
            RebuildNpcOptions();
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
            // Moving off an extension is the natural moment to re-evaluate
            // whether it still changes anything, so the sidebar markers track
            // edits without every node in every tree notifying the list.
            foreach (var ext in VanillaExtensions) ext.RefreshChangeIndicator();
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
        // In-place sync, never Clear — see SyncOptions. Rebuilds on every node
        // selection, so a Clear could empty the bound Expression combo.
        SyncOptions(SelectedNodeExpressionOptions,
            exprs.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList());
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
        // Build the desired list first, then sync in place — never Clear (see
        // SyncOptions): this rebuilds on every node selection, and a Clear
        // could empty the bound Outfit combo.
        var desired = new System.Collections.Generic.List<string>();
        var actorKey = SelectedNode?.Actor;
        if (!string.IsNullOrEmpty(actorKey))
        {
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
                desired.AddRange(outfits.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                break;
            }
        }
        SyncOptions(SelectedNodeOutfitOptions, desired);
    }

    /// <summary>
    /// Rebuilds <see cref="SelectedNodeOverlayOptions"/> for the current node:
    /// the overlays of the node's inferred level, or — when that can't be
    /// resolved or is empty — every overlay in the pack. Called on each
    /// selected-node change.
    /// </summary>
    private void RebuildSelectedNodeOverlayOptions()
    {
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

        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(SelectedNodeOverlayOptions, names);
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

    /// <summary>Flatten a GameObject tree (each node + all nested children).</summary>
    private static System.Collections.Generic.IEnumerable<GameObjectViewModel> FlattenOverlays(
        System.Collections.Generic.IEnumerable<GameObjectViewModel> overlays)
    {
        foreach (var o in overlays)
        {
            yield return o;
            foreach (var c in FlattenOverlays(o.Children)) yield return c;
        }
    }

    /// <summary>Every GameObject name declared across the pack — pack places
    /// AND vanilla extensions (nested included).</summary>
    private System.Collections.Generic.List<string> AllOverlayNames()
        => FlattenOverlays(Places.SelectMany(p => p.GameObjects)
                 .Concat(VanillaExtensions.SelectMany(v => v.GameObjects)))
                 .Select(o => o.Name)
                 .Where(n => !string.IsNullOrWhiteSpace(n))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                 .ToList();

    /// <summary>GameObject names belonging to a level token: <c>place:&lt;key&gt;</c>
    /// resolves a pack place's GameObjects, <c>vanilla:&lt;goName&gt;</c>
    /// a vanilla extension's. Anything else returns empty.</summary>
    private System.Collections.Generic.List<string> OverlayNamesForLevel(string levelToken)
    {
        System.Collections.Generic.IEnumerable<GameObjectViewModel>? source = null;
        const string placePrefix = "place:";
        if (!string.IsNullOrEmpty(levelToken) &&
            levelToken.StartsWith(placePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = levelToken.Substring(placePrefix.Length);
            source = Places
                .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.GameObjects);
        }
        else if (!string.IsNullOrEmpty(levelToken) &&
                 levelToken.StartsWith("vanilla:", StringComparison.OrdinalIgnoreCase))
        {
            // Extension Source is stored in the same token form, so match whole.
            source = VanillaExtensions
                .Where(v => string.Equals(v.Source, levelToken, StringComparison.OrdinalIgnoreCase))
                .SelectMany(v => v.GameObjects);
        }
        if (source == null) return new();
        return FlattenOverlays(source)
            .Select(o => o.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The same level's GameObjects, but as HIERARCHY PATHS —
    /// <c>NPCs &gt; Shower &gt; Anis &gt; Naked</c> — one entry per node, in tree
    /// order, with NPC placements included as leaves. Backs the Set-Active /
    /// Fade / Move / Spin target dropdowns.
    /// <para/>
    /// Paths rather than bare names because names repeat across the tree (every
    /// slot has a <c>Default</c> / <c>Swim</c> child), and a bare name resolves
    /// to whichever the runtime finds first. The <c>&gt;</c> spelling is what the
    /// runtime's path lookup accepts alongside slashes, so the string the author
    /// picks is stored and resolved verbatim.
    /// </summary>
    private System.Collections.Generic.List<string> OverlayPathsForLevel(string levelToken)
    {
        System.Collections.Generic.IEnumerable<GameObjectViewModel>? source = null;
        const string placePrefix = "place:";
        if (!string.IsNullOrEmpty(levelToken) &&
            levelToken.StartsWith(placePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = levelToken.Substring(placePrefix.Length);
            source = Places
                .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.GameObjects);
        }
        else if (!string.IsNullOrEmpty(levelToken) &&
                 levelToken.StartsWith("vanilla:", StringComparison.OrdinalIgnoreCase))
        {
            source = VanillaExtensions
                .Where(v => string.Equals(v.Source, levelToken, StringComparison.OrdinalIgnoreCase))
                .SelectMany(v => v.GameObjects);
        }
        if (source == null) return new();

        var paths = new System.Collections.Generic.List<string>();
        foreach (var root in source) CollectPaths(root, "", paths);
        return paths;
    }

    private const string PathSeparator = " > ";

    /// <summary>Depth-first walk emitting one path per GameObject node and per
    /// NPC placement, parents before children so the list reads as the tree.</summary>
    private static void CollectPaths(GameObjectViewModel node, string prefix,
                                     System.Collections.Generic.List<string> into)
    {
        string name = node.Name;
        if (string.IsNullOrWhiteSpace(name)) return;   // unnamed nodes aren't addressable
        string path = string.IsNullOrEmpty(prefix) ? name : prefix + PathSeparator + name;
        into.Add(path);
        foreach (var npc in node.Npcs)
        {
            string npcName = string.IsNullOrWhiteSpace(npc.Name) ? npc.Npc : npc.Name;
            if (!string.IsNullOrWhiteSpace(npcName)) into.Add(path + PathSeparator + npcName);
        }
        foreach (var child in node.Children) CollectPaths(child, path, into);
    }

    /// <summary>
    /// Level tokens that actually carry GameObjects — pack places with a
    /// non-empty Overlays tree plus vanilla-extension sources with one. Backs
    /// the Set-Active row's level dropdown so it only offers levels where
    /// picking a target can succeed. Computed fresh per call: the row VMs read
    /// it through a provider whenever their dropdown re-binds, so it can't go
    /// stale between overlay edits the way a cached list would.
    /// </summary>
    private System.Collections.Generic.IEnumerable<NavigatorTargetOption> OverlayLevelOptionTokens()
    {
        var tokens = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var p in Places)
            if (FlattenOverlays(p.GameObjects).Any(o => !string.IsNullOrWhiteSpace(o.Name)))
                tokens.Add(new NavigatorTargetOption("place:" + p.Key, p.Display));
        foreach (var v in VanillaExtensions)
            if (!string.IsNullOrEmpty(v.Source) &&
                FlattenOverlays(v.GameObjects).Any(o => !string.IsNullOrWhiteSpace(o.Name)))
                tokens.Add(new NavigatorTargetOption(v.Source, v.Display));
        return tokens
            .GroupBy(t => t.Token, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t.DisplayLabel, StringComparer.OrdinalIgnoreCase)
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
        set
        {
            if (ReferenceEquals(_selectedVariable, value)) return;
            // Leaving a variable commits whatever was typed into its Name box.
            CommitPendingVariableRename();
            _selectedVariable = value;
            _renameOriginName = value?.Name ?? "";
            OnPropertyChanged();
        }
    }

    // ── Deferred variable rename ──────────────────────────────────────────
    // The Name textbox writes through on every keystroke, so cascading the
    // rename to references per character is nonsense ("V", "Va", "Var"…).
    // Instead we snapshot the name on selection and reconcile at the natural
    // commit points — selecting a different variable, switching tabs, saving
    // (and therefore exporting). That keeps typing free-form while still
    // guaranteeing references never end up dangling on disk.
    private string _renameOriginName = "";

    /// <summary>
    /// If the selected variable's Name has drifted from what it was when
    /// selected, rewrite every reference to it. No-op when nothing changed,
    /// when the variable was deleted rather than renamed, or when the new
    /// name collides with another variable (the rename is left for the
    /// author to resolve rather than silently repointing references at
    /// someone else's variable).
    /// </summary>
    public void CommitPendingVariableRename()
    {
        var vm = _selectedVariable;
        if (vm == null || string.IsNullOrEmpty(_renameOriginName)) return;

        string current = vm.Name;
        if (string.IsNullOrWhiteSpace(current) || current == _renameOriginName) return;
        if (!Variables.Contains(vm)) { _renameOriginName = ""; return; }   // deleted, not renamed
        if (Variables.Any(v => v != vm && v.Name == current))
        {
            _renameOriginName = current;   // don't retry every commit point
            return;
        }

        string from = _renameOriginName;
        _renameOriginName = current;       // set first: RenameReferences can't re-enter
        int refs = Services.VariableRenamer.RenameReferences(Pack, from, current);
        if (refs == 0) return;
        RefreshConditionAndActionRows();
        ShowInfo?.Invoke("Rename",
            $"Renamed '{from}' to '{current}' and updated {refs} reference{(refs == 1 ? "" : "s")}.");
    }

    private SceneViewModel? _selectedScene;
    public SceneViewModel? SelectedScene
    {
        get => _selectedScene;
        set { _selectedScene = value; OnPropertyChanged(); }
    }

    private NpcViewModel? _selectedNpc;
    public NpcViewModel? SelectedNpc
    {
        get => _selectedNpc;
        set { _selectedNpc = value; OnPropertyChanged(); }
    }

    /// <summary>NPC entries for the Places placement picker (editable combo). Includes token and display label
    /// so when an NPC's DisplayName changes, the dropdown immediately reflects it.</summary>
    public ObservableCollection<NavigatorTargetOption> NpcKeyOptions { get; } = new();

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
    public RelayCommand ExportPackAsCommand { get; }
    public RelayCommand AddCharacterCommand { get; }
    public RelayCommand AddOutfitCommand { get; }
    public RelayCommand AddPlaceCommand { get; }
    public RelayCommand RemovePlaceCommand { get; }
    public RelayCommand AddNavigatorButtonCommand { get; }
    public RelayCommand AddGameObjectCommand { get; }
    public RelayCommand AddVanillaExtensionCommand { get; }
    public RelayCommand RemoveVanillaExtensionCommand { get; }
    public RelayCommand AddVanillaExtensionButtonCommand { get; }
    public RelayCommand AddVanillaExtensionGameObjectCommand { get; }
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
    public RelayCommand AddNpcCommand { get; }
    public RelayCommand RemoveNpcCommand { get; }
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

    /// <summary>Preview-plays a music track at its authored volume (blank =
    /// full). Same player as the SFX preview — one clip at a time, so
    /// starting a track stops whatever was playing.</summary>
    public RelayCommand PlayMusicCommand { get; }

    /// <summary>Stops any in-progress music preview.</summary>
    public RelayCommand StopMusicCommand { get; }
    public RelayCommand AddIntegrationRuleCommand { get; }
    public RelayCommand AddIntegrationFolderCommand { get; }
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
        // Shared folder trees for the remaining unit tabs (Dialogues /
        // Variables / Integration have their own hand-rolled trees). One
        // controller per tab; the window wires one set of handlers for all.
        // Folder-def accessors are lambdas because Pack swaps on load.
        ActorTree = new UnitTreeController(() => Pack.ActorFolders,
            o => ((ActorViewModel)o).Key, o => ((ActorViewModel)o).Display,
            o => SelectedActor = (ActorViewModel)o, () => Undo.Checkpoint());
        PlaceTree = new UnitTreeController(() => Pack.PlaceFolders,
            o => ((PlaceViewModel)o).Key, o => ((PlaceViewModel)o).Display,
            o => SelectedPlace = (PlaceViewModel)o, () => Undo.Checkpoint());
        SceneTree = new UnitTreeController(() => Pack.SceneFolders,
            o => ((SceneViewModel)o).Key, o => ((SceneViewModel)o).Display,
            o => SelectedScene = (SceneViewModel)o, () => Undo.Checkpoint());
        NpcTree = new UnitTreeController(() => Pack.NpcFolders,
            o => ((NpcViewModel)o).Key, o => ((NpcViewModel)o).Display,
            o => SelectedNpc = (NpcViewModel)o, () => Undo.Checkpoint());
        WallpaperTree = new UnitTreeController(() => Pack.WallpaperFolders,
            o => ((WallpaperViewModel)o).Key, o => ((WallpaperViewModel)o).Display,
            o => SelectedWallpaper = (WallpaperViewModel)o, () => Undo.Checkpoint());
        MusicTree = new UnitTreeController(() => Pack.MusicFolders,
            o => ((MusicViewModel)o).Key, o => ((MusicViewModel)o).Display,
            o => SelectedMusic = (MusicViewModel)o, () => Undo.Checkpoint());
        SfxTree = new UnitTreeController(() => Pack.SfxFolders,
            o => ((SfxViewModel)o).Key, o => ((SfxViewModel)o).Display,
            o => SelectedSfx = (SfxViewModel)o, () => Undo.Checkpoint());

        // Let Level Overlay action rows list the overlays of whichever level the
        // author picks (see NodeActionViewModel.OverlayOptions).
        NodeActionViewModel.OverlayProvider = OverlayNamesForLevelOrAll;
        // Strict variant for the Set-Active GameObjects target: exactly
        // the chosen level's overlays, no everything-fallback (the row's
        // target combo is disabled until a level is picked).
        // Target dropdowns list hierarchy paths, so repeated child names
        // (every slot has a Default / Swim) stay distinguishable.
        NodeActionViewModel.StrictOverlayProvider = OverlayPathsForLevel;

        // Node rows tint themselves with their speaker's authored colour; they
        // hold only the actor key, so resolution comes from here.
        DialogueNodeViewModel.ActorColorProvider = ActorColorFor;
        // Levels offered in that row's level dropdown: only ones that actually
        // carry GameObjects (pack places + vanilla extensions).
        NodeActionViewModel.OverlayLevelProvider = OverlayLevelOptionTokens;

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

        // Keep the variable pickers live as variables are edited. Adding a
        // variable already rebuilt them, but editing an existing one's Name or
        // Type did not — so a variable created (as Bool, the default) and then
        // switched to List never reached AddToList / RemoveFromList / ClearList
        // or the List conditions, whose dropdowns filter on Type. It only
        // appeared after a reload. Subscribing through the collection covers
        // every construction path (add, paste, duplicate, rebind) at one site.
        Variables.CollectionChanged += OnVariablesChanged;
        // RelayCommand.Executing checkpoints just before these run, so the most
        // recent change is captured before we step back/forward.
        UndoCommand = new RelayCommand(() => Undo.Undo(), () => Undo.CanUndo);
        RedoCommand = new RelayCommand(() => Undo.Redo(), () => Undo.CanRedo);

        NewPackCommand     = new RelayCommand(NewPack);
        OpenPackCommand    = new RelayCommand(OpenPack);
        SavePackCommand    = new RelayCommand(() => SavePack());
        SavePackAsCommand  = new RelayCommand(SavePackAs);
        // Export is only available once the pack has been saved at least
        // once — the exporter zips the on-disk folder, so an in-memory-
        // only pack has nothing to bundle.
        ExportPackCommand   = new RelayCommand(ExportPack, () => PackRoot != null);
        ExportPackAsCommand = new RelayCommand(ExportPackAs, () => PackRoot != null);
        AddCharacterCommand = new RelayCommand(AddCharacter);
        AddOutfitCommand   = new RelayCommand(AddOutfit, () => SelectedOutfit != null || Characters.Count > 0);
        AddPlaceCommand    = new RelayCommand(AddPlace);
        RemovePlaceCommand = new RelayCommand(RemovePlace, () => SelectedPlace != null || PlaceTree.Selected is UnitFolderNode);
        AddNavigatorButtonCommand = new RelayCommand(AddNavigatorButton,
            () => SelectedPlace?.CanAddNavigatorButton == true);
        // Sidebar search boxes for the hand-rolled trees (the shared
        // UnitTreeController-based tabs carry their own Filter).
        DialogueTreeFilter    = new TreeFilterViewModel(() => DialogueTree);
        VariableTreeFilter    = new TreeFilterViewModel(() => VariableTree);
        IntegrationTreeFilter = new TreeFilterViewModel(() => IntegrationTree);
        CharacterTreeFilter   = new TreeFilterViewModel(() => CharacterFilterRoots);

        AddGameObjectCommand = new RelayCommand(() => SelectedPlace?.AddGameObject(),
            () => SelectedPlace != null);
        AddVanillaExtensionCommand    = new RelayCommand(AddVanillaExtension);
        RemoveVanillaExtensionCommand = new RelayCommand(RemoveVanillaExtension, () => SelectedVanillaExtension != null);
        AddVanillaExtensionButtonCommand = new RelayCommand(AddVanillaExtensionButton, () => SelectedVanillaExtension != null);
        AddVanillaExtensionGameObjectCommand = new RelayCommand(
            () => SelectedVanillaExtension?.AddGameObject(), () => SelectedVanillaExtension != null);
        AddMapButtonCommand           = new RelayCommand(AddMapButton);
        RemoveMapButtonCommand        = new RelayCommand(RemoveMapButton, () => SelectedMapButton != null);

        AddDialogueCommand            = new RelayCommand(AddDialogue);
        AddDialogueFolderCommand      = new RelayCommand(AddDialogueFolder);
        DuplicateItemCommand          = new RelayCommand(DuplicateActiveItem);
        CopyItemCommand               = new RelayCommand(CopyActiveItem);
        PasteItemCommand              = new RelayCommand(PasteActiveItem);
        RenameItemCommand             = new RelayCommand(RenameActiveItem);
        DeleteItemCommand             = new RelayCommand(DeleteActiveItem);
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
        RemoveActorCommand            = new RelayCommand(RemoveActor, () => SelectedActor != null || ActorTree.Selected is UnitFolderNode);
        AddActorExpressionCommand     = new RelayCommand(AddActorExpression, () => SelectedActor != null);
        AddActorOutfitCommand         = new RelayCommand(AddActorOutfit, () => SelectedActor != null);

        AddVariableCommand            = new RelayCommand(AddVariable);
        RemoveVariableCommand         = new RelayCommand(RemoveVariable, () => SelectedVariableTreeItem != null || SelectedVariable != null);
        AddVariableFolderCommand      = new RelayCommand(AddVariableFolder);
        AddInitialValueCommand        = new RelayCommand(() => SelectedVariable?.AddInitialValue());
        AddSceneCommand               = new RelayCommand(AddScene);
        RemoveSceneCommand            = new RelayCommand(RemoveScene, () => SelectedScene != null || SceneTree.Selected is UnitFolderNode);
        AddNpcCommand                 = new RelayCommand(AddNpc);
        RemoveNpcCommand              = new RelayCommand(RemoveNpc);
        AddWallpaperCommand           = new RelayCommand(AddWallpaper);
        RemoveWallpaperCommand        = new RelayCommand(RemoveWallpaper, () => SelectedWallpaper != null || WallpaperTree.Selected is UnitFolderNode);
        AddMusicCommand               = new RelayCommand(AddMusic);
        RemoveMusicCommand            = new RelayCommand(RemoveMusic, () => SelectedMusic != null || MusicTree.Selected is UnitFolderNode);
        AddSfxCommand                 = new RelayCommand(AddSfx);
        RemoveSfxCommand              = new RelayCommand(RemoveSfx, () => SelectedSfx != null || SfxTree.Selected is UnitFolderNode);
        PlaySfxCommand                = new RelayCommand(
            p => PlaySfx(p as SfxViewModel ?? SelectedSfx),
            p => CanPlaySfx(p as SfxViewModel ?? SelectedSfx));
        StopSfxCommand                = new RelayCommand(_ => _sfxPreview.Stop());
        PlayMusicCommand              = new RelayCommand(
            p => PlayMusic(p as MusicViewModel ?? SelectedMusic),
            p => CanPlayMusic(p as MusicViewModel ?? SelectedMusic));
        StopMusicCommand              = new RelayCommand(_ => _sfxPreview.Stop());

        AddIntegrationRuleCommand      = new RelayCommand(AddIntegrationRule);
        AddIntegrationFolderCommand    = new RelayCommand(AddIntegrationFolder);
        // Enabled for a selected folder too (deleting one lifts its contents),
        // mirroring RemoveVariableCommand.
        RemoveIntegrationRuleCommand   = new RelayCommand(RemoveIntegrationRule,
            () => SelectedIntegrationTreeItem != null || SelectedIntegrationRule != null);
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
        RebindNpcs();
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
        RebuildNpcOptions();

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
        PlaceTree.Build(Places);
        SelectedPlace = Places.FirstOrDefault();
    }

    private void RebindVanillaExtensions()
    {
        VanillaExtensions.Clear();
        // The VM seeds itself from the vanilla catalog on construction, so the
        // GameObjects list and preview show the level as it really is. Untouched
        // bound nodes are pruned again on save, so this can't grow the manifest.
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
        // In-place sync, never Clear — see SyncOptions. This reruns on every
        // place add/remove/rename while navigator-button target combos are
        // bound to it, so a Clear would blank their authored targets.
        SyncOptions(AllTargetOptions,
            targetOpts.OrderBy(t => t.DisplayLabel, System.StringComparer.OrdinalIgnoreCase).ToList());
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
        RebindNpcs();
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
        RebuildNpcOptions();
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
            PackRepository.ActivePack = Pack;
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

    /// <summary>Save to <see cref="PackRoot"/>. Returns false when the save
    /// didn't happen (failed, or the user cancelled the Save-as it fell back
    /// to) so callers that chain off a save — Export — can bail.</summary>
    private bool SavePack()
    {
        // Any in-progress variable rename becomes real here: saving is a
        // commit point, so references get rewritten before the manifest is
        // written rather than being left dangling on disk.
        CommitPendingVariableRename();
        // Fold the current tree back into the model (catches a dialogue key
        // rename that happened without a tree mutation, so folder membership
        // — keyed by dialogue key — stays correct across a reload).
        SyncFoldersToModel();
        SyncVariableFoldersToModel();
        SyncIntegrationFoldersToModel();
        SyncUnitFoldersToModel();
        // No root yet → Save-as. It sets PackRoot then re-enters this method,
        // so a non-null root afterwards means the save went through; a
        // cancelled dialog leaves it null.
        if (PackRoot is null) { SavePackAs(); return PackRoot != null; }
        try
        {
            PackRepository.Save(Pack, PackRoot);
            Validate();
            MarkSaved();
            foreach (var ext in VanillaExtensions) ext.RefreshChangeIndicator();
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SavePackAs()
    {
        SyncFoldersToModel();   // keep folder membership current (see SavePack)
        SyncVariableFoldersToModel();
        SyncIntegrationFoldersToModel();
        SyncUnitFoldersToModel();
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
    /// <summary>
    /// One-click re-export: saves, then exports straight to wherever this
    /// pack was last exported. Falls back to the choose-a-file flow the
    /// first time (or when the remembered folder is gone). The remembered
    /// path lives in the editor's LOCAL settings, never in the manifest —
    /// an export path carries the author's filesystem layout (usernames,
    /// drives) and must not ship inside a distributed pack.
    /// </summary>
    private void ExportPack()
    {
        var remembered = Services.ExportPathService.Get(PackRoot);
        if (remembered == null) { ExportPackAs(); return; }

        if (!EnsureSavedForExport()) return;
        RunExport(remembered);
    }

    /// <summary>Choose-a-file export. Always shows the dialog; the picked
    /// path becomes this pack's one-click "Export pack" target.</summary>
    private void ExportPackAs()
    {
        if (!EnsureSavedForExport()) return;

        var remembered = Services.ExportPathService.Get(PackRoot);
        var dialog = new SaveFileDialog
        {
            FileName = remembered != null
                ? Path.GetFileName(remembered)
                : Pack.PackId + PackExporter.FileExtension,
            Filter = "ModForge pack (*" + PackExporter.FileExtension + ")|*" + PackExporter.FileExtension,
            Title = "Export pack to .smspack file",
            // Reopen at this pack's last export, else where any pack was last
            // exported (e.g. the game's ModPacks folder), else the pack folder.
            InitialDirectory = (remembered != null ? Path.GetDirectoryName(remembered) : null)
                ?? Services.DialogFoldersService.Get(Services.DialogFoldersService.Key.Export)
                ?? PackRoot,
        };
        if (dialog.ShowDialog() != true) return;
        Services.DialogFoldersService.Set(
            Services.DialogFoldersService.Key.Export, Path.GetDirectoryName(dialog.FileName));
        Services.ExportPathService.Set(PackRoot, dialog.FileName);

        RunExport(dialog.FileName);
    }

    /// <summary>Shared export preamble: needs an on-disk pack, and a real
    /// save first — the .smspack is built from the on-disk folder, so
    /// exporting without one would silently ship the previous snapshot.
    /// Routed through SavePack so the export also commits a pending variable
    /// rename, re-validates, marks the pack clean and flashes the Saved
    /// toast: after an export, "saved" and "exported" always agree.</summary>
    private bool EnsureSavedForExport()
    {
        // Belt and braces: the commands' CanExecute already requires
        // PackRoot, but the user might dock-disable the menu state cache.
        if (PackRoot is null)
        {
            MessageBox.Show(
                "Save the pack to disk before exporting — the .smspack zip " +
                "is built from the on-disk folder.",
                "Export pack", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return SavePack();   // SavePack already reported any failure
    }

    private void RunExport(string outputFile)
    {
        try
        {
            var result = PackExporter.Export(PackRoot!, outputFile);
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
        PlaceTree.PlaceNew(vm);
        RebuildTargetOptions();
        RebuildDialogueRoomTalkOptions();
        RebuildLevelOptions();
    }

    private void RemovePlace()
    {
        // A selected FOLDER deletes the folder and lifts its contents.
        if (PlaceTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedPlace is null) return;
        var vm = SelectedPlace;
        Pack.Places.Remove(vm.Model);
        Places.Remove(vm);
        PlaceTree.RemoveLeafFor(vm);
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

    /// <summary>Backs the Dialogues sidebar search box.</summary>
    public TreeFilterViewModel DialogueTreeFilter { get; }

    /// <summary>Backs the Busts sidebar search box. That tree binds straight to
    /// the character VMs rather than tree-node wrappers, so the roots are the
    /// characters themselves.</summary>
    public TreeFilterViewModel CharacterTreeFilter { get; }
    private System.Collections.Generic.IEnumerable<IFilterableTreeNode> CharacterFilterRoots => Characters;

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
        // A rebuild replaces every node, so re-apply any live search.
        DialogueTreeFilter.Reapply();
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

    /// <summary>Backs the Variables sidebar search box.</summary>
    public TreeFilterViewModel VariableTreeFilter { get; }

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
        // A rebuild replaces every node, so re-apply any live search.
        VariableTreeFilter.Reapply();
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

    // Tab order follows the authoring workflow: who (Busts, Actors) → where
    // (Places, Map Buttons) → what they say (Dialogues) → media (Scenes,
    // Music, SFX, Wallpapers) → state/logic (Variables, Integration).
    // Index 0 is the ModForge landing tab (issues + docs) — it hosts no unit
    // list, so no Tab* constant and the per-tab dispatches all no-op there.
    // MUST stay in lockstep with the TabItem order in MainWindow.xaml and with
    // MainWindow.xaml.cs's own copy of these constants.
    private const int TabBusts = 1, TabActors = 2, TabNpcs = 3, TabPlaces = 4,
                      TabMapButtons = 5, TabDialogues = 6, TabScenes = 7, TabMusic = 8,
                      TabSfx = 9, TabWallpapers = 10, TabVariables = 11, TabIntegration = 12;

    private int _selectedTabIndex;
    /// <summary>Active tab (bound to the TabControl) — drives which list Copy/Paste/Duplicate hit.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        // Leaving the Variables tab is a commit point for a half-typed rename.
        set { if (_selectedTabIndex != value) CommitPendingVariableRename();
              _selectedTabIndex = value; OnPropertyChanged(); }
    }

    // ── Shared unit folder trees (one per remaining tab) ─────────────────
    public UnitTreeController ActorTree { get; }
    public UnitTreeController PlaceTree { get; }
    public UnitTreeController SceneTree { get; }
    public UnitTreeController NpcTree { get; }
    public UnitTreeController WallpaperTree { get; }
    public UnitTreeController MusicTree { get; }
    public UnitTreeController SfxTree { get; }

    /// <summary>Write every unit tree's folder structure to the pack model —
    /// the save-time counterpart of the hand-rolled trees' Sync* calls.</summary>
    public void SyncUnitFoldersToModel()
    {
        ActorTree.SyncToModel();
        PlaceTree.SyncToModel();
        SceneTree.SyncToModel();
        NpcTree.SyncToModel();
        WallpaperTree.SyncToModel();
        MusicTree.SyncToModel();
        SfxTree.SyncToModel();
    }

    public RelayCommand DuplicateItemCommand { get; private set; } = null!;
    public RelayCommand CopyItemCommand { get; private set; } = null!;
    public RelayCommand PasteItemCommand { get; private set; } = null!;
    public RelayCommand RenameItemCommand { get; private set; } = null!;
    public RelayCommand DeleteItemCommand { get; private set; } = null!;

    /// <summary>Text-prompt hook wired by the window (VM stays UI-free):
    /// (title, label, initial) → entered text, or null on cancel.</summary>
    public Func<string, string, string, string?>? PromptForText { get; set; }

    /// <summary>Info-message hook wired by the window (title, message).</summary>
    public Action<string, string>? ShowInfo { get; set; }

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
    { var vm = new ActorViewModel(d); vm.DefaultBustKeyChanged += OnActorDefaultBustKeyChanged;
      vm.PropertyChanged += OnActorPropertyChanged; return vm; }

    /// <summary>
    /// Actor edits that other views mirror. A colour or key change repaints the
    /// dialogue node rows, which wash themselves in their speaker's colour.
    /// </summary>
    private void OnActorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActorViewModel.NameColor) ||
            e.PropertyName == nameof(ActorViewModel.Key) ||
            string.IsNullOrEmpty(e.PropertyName))
            RefreshNodeActorTints();
    }

    /// <summary>Push a tint re-read to every loaded dialogue node. Cheap (a
    /// property-changed per row) and only on an actor colour edit.</summary>
    private void RefreshNodeActorTints()
    {
        foreach (var d in Dialogues)
            foreach (var n in d.Nodes)
                n.RefreshActorTint();
    }

    /// <summary>The colour authored for an actor key, or null when the actor
    /// isn't found or left its colour blank. Backs the node-row speaker tint.</summary>
    private System.Windows.Media.Color? ActorColorFor(string actorKey)
    {
        if (string.IsNullOrWhiteSpace(actorKey)) return null;
        var actor = Actors.FirstOrDefault(
            a => string.Equals(a.Key, actorKey, StringComparison.OrdinalIgnoreCase));
        if (actor == null || string.IsNullOrWhiteSpace(actor.NameColor)) return null;
        return actor.NameColorValue;
    }
    private SfxViewModel MakeSfxVm(SfxDef d)
    { var vm = new SfxViewModel(d); vm.PropertyChanged += OnSfxChanged; return vm; }

    private void DuplicateActiveItem()
    {
        switch (SelectedTabIndex)
        {
            case TabBusts: DuplicateOutfit(EditorClipboard.CloneOne(SelectedOutfit?.Model)); break;
            case TabPlaces: DuplicateFlat(SelectedPlace?.Model, Pack.Places, Places, p => p.Key, (p, k) => p.Key = k, d => new PlaceViewModel(d), vm => { PlaceTree.PlaceNew(vm); RebuildDialogueRoomTalkOptions(); }); break;
            case TabMapButtons: DuplicateFlat(SelectedMapButton?.Model, Pack.MapButtons, MapButtons, _ => "", (_, __) => { }, d => new MapButtonViewModel(d, RemoveMapButtonVm), vm => SelectedMapButton = vm); break;
            case TabDialogues: DuplicateDialogue(SelectedDialogue == null ? null : EditorClipboard.CloneOne(SelectedDialogue.Model)); break;
            case TabActors: DuplicateFlat(SelectedActor?.Model, Pack.Actors, Actors, a => a.Key, (a, k) => a.Key = k, MakeActorVm, vm => { ActorTree.PlaceNew(vm); RebuildActorAndBustOptions(); }); break;
            case TabScenes: DuplicateFlat(SelectedScene?.Model, Pack.Scenes, Scenes, s => s.Key, (s, k) => s.Key = k, d => new SceneViewModel(d), vm => { SceneTree.PlaceNew(vm); RebuildSceneOptions(); }); break;
            case TabNpcs: DuplicateFlat(SelectedNpc?.Model, Pack.Npcs, Npcs, n => n.Key, (n, k) => n.Key = k, d => new NpcViewModel(d), vm => { NpcTree.PlaceNew(vm); RebuildNpcOptions(); }); break;
            case TabVariables: DuplicateFlat(SelectedVariable?.Model, Pack.Variables, Variables, v => v.Name, (v, k) => v.Name = k, d => new PackVariableViewModel(d), vm => PlaceNewVariableInTree(vm)); break;
            case TabWallpapers: DuplicateFlat(SelectedWallpaper?.Model, Pack.Wallpapers, Wallpapers, w => w.Key, (w, k) => w.Key = k, d => new WallpaperViewModel(d), vm => WallpaperTree.PlaceNew(vm)); break;
            case TabMusic: DuplicateFlat(SelectedMusic?.Model, Pack.Music, Music, m => m.Key, (m, k) => m.Key = k, d => new MusicViewModel(d), vm => { MusicTree.PlaceNew(vm); RebuildMusicKeyOptions(); }); break;
            case TabSfx: DuplicateFlat(SelectedSfx?.Model, Pack.Sfx, Sfx, s => s.Key, (s, k) => s.Key = k, MakeSfxVm, vm => { SfxTree.PlaceNew(vm); RebuildSfxKeyOptions(); }); break;
            case TabIntegration: DuplicateFlat(SelectedIntegrationRule?.Model, Pack.IntegrationRules, IntegrationRules, r => r.Key, (r, k) => r.Key = k, d => new UpdateRuleViewModel(d), vm => PlaceNewIntegrationRuleInTree(vm)); break;
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
            case TabNpcs: if (SelectedNpc != null) EditorClipboard.SetItem(SelectedNpc.Model); break;
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
            case TabPlaces: PasteFlat(Pack.Places, Places, p => p.Key, (p, k) => p.Key = k, d => new PlaceViewModel(d), vm => { PlaceTree.PlaceNew(vm); RebuildDialogueRoomTalkOptions(); }); break;
            case TabMapButtons: PasteFlat(Pack.MapButtons, MapButtons, _ => "", (_, __) => { }, d => new MapButtonViewModel(d, RemoveMapButtonVm), vm => SelectedMapButton = vm); break;
            case TabDialogues: DuplicateDialogue(EditorClipboard.GetItem<DialogueDef>()); break;
            case TabActors: PasteFlat(Pack.Actors, Actors, a => a.Key, (a, k) => a.Key = k, MakeActorVm, vm => { ActorTree.PlaceNew(vm); RebuildActorAndBustOptions(); }); break;
            case TabScenes: PasteFlat(Pack.Scenes, Scenes, s => s.Key, (s, k) => s.Key = k, d => new SceneViewModel(d), vm => { SceneTree.PlaceNew(vm); RebuildSceneOptions(); }); break;
            case TabNpcs: PasteFlat(Pack.Npcs, Npcs, n => n.Key, (n, k) => n.Key = k, d => new NpcViewModel(d), vm => { NpcTree.PlaceNew(vm); RebuildNpcOptions(); }); break;
            case TabVariables: PasteFlat(Pack.Variables, Variables, v => v.Name, (v, k) => v.Name = k, d => new PackVariableViewModel(d), vm => PlaceNewVariableInTree(vm)); break;
            case TabWallpapers: PasteFlat(Pack.Wallpapers, Wallpapers, w => w.Key, (w, k) => w.Key = k, d => new WallpaperViewModel(d), vm => WallpaperTree.PlaceNew(vm)); break;
            case TabMusic: PasteFlat(Pack.Music, Music, m => m.Key, (m, k) => m.Key = k, d => new MusicViewModel(d), vm => { MusicTree.PlaceNew(vm); RebuildMusicKeyOptions(); }); break;
            case TabSfx: PasteFlat(Pack.Sfx, Sfx, s => s.Key, (s, k) => s.Key = k, MakeSfxVm, vm => { SfxTree.PlaceNew(vm); RebuildSfxKeyOptions(); }); break;
            case TabIntegration: PasteFlat(Pack.IntegrationRules, IntegrationRules, r => r.Key, (r, k) => r.Key = k, d => new UpdateRuleViewModel(d), vm => PlaceNewIntegrationRuleInTree(vm)); break;
        }
    }

    /// <summary>Del hotkey: delete the selected item of the active tab's list.
    /// Routed through the per-tab Remove commands so CanExecute gating and any
    /// tab-specific cleanup stay in one place. Busts intentionally has no
    /// Del handling — outfit/character removal is a destructive tree edit
    /// with its own UI.</summary>
    private void DeleteActiveItem()
    {
        void Run(RelayCommand c) { if (c.CanExecute(null)) c.Execute(null); }
        switch (SelectedTabIndex)
        {
            case TabPlaces: Run(RemovePlaceCommand); break;
            case TabMapButtons: Run(RemoveMapButtonCommand); break;
            case TabDialogues: Run(RemoveDialogueCommand); break;
            case TabActors: Run(RemoveActorCommand); break;
            case TabScenes: Run(RemoveSceneCommand); break;
            case TabNpcs: Run(RemoveNpcCommand); break;
            case TabVariables: Run(RemoveVariableCommand); break;
            case TabWallpapers: Run(RemoveWallpaperCommand); break;
            case TabMusic: Run(RemoveMusicCommand); break;
            case TabSfx: Run(RemoveSfxCommand); break;
            case TabIntegration: Run(RemoveIntegrationRuleCommand); break;
        }
    }

    /// <summary>F2/F12 hotkey: prompt-rename the selected item's key in the
    /// active tab. Collisions are de-dup'd with the same _copyN scheme as
    /// paste; per-tab option rebuilds keep dropdowns referencing the key
    /// in sync.</summary>
    private void RenameActiveItem()
    {
        switch (SelectedTabIndex)
        {
            case TabBusts:
                var ch = SelectedOutfit != null ? Characters.FirstOrDefault(c => c.Outfits.Contains(SelectedOutfit)) : null;
                if (ch != null)
                    RenameKey("Outfit", SelectedOutfit!, v => v.Key, (v, k) => v.Key = k,
                              ch.Outfits.Where(x => x != SelectedOutfit).Select(x => x.Key));
                break;
            case TabPlaces:
                if (SelectedPlace != null)
                    RenameKey("Place", SelectedPlace, v => v.Key, (v, k) => { v.Key = k; RebuildDialogueRoomTalkOptions(); PlaceTree.Sort(); PlaceTree.SyncToModel(); },
                              Places.Where(x => x != SelectedPlace).Select(x => x.Key));
                break;
            case TabDialogues:
                if (SelectedDialogue != null)
                    RenameKey("Dialogue", SelectedDialogue, v => v.Key, (v, k) => v.Key = k,
                              Dialogues.Where(x => x != SelectedDialogue).Select(x => x.Key));
                break;
            case TabActors:
                if (SelectedActor != null)
                    RenameKey("Actor", SelectedActor, v => v.Key, (v, k) => { v.Key = k; RebuildActorAndBustOptions(); ActorTree.Sort(); ActorTree.SyncToModel(); },
                              Actors.Where(x => x != SelectedActor).Select(x => x.Key));
                break;
            case TabScenes:
                if (SelectedScene != null)
                    RenameKey("Scene", SelectedScene, v => v.Key, (v, k) => { v.Key = k; RebuildSceneOptions(); SceneTree.Sort(); SceneTree.SyncToModel(); },
                              Scenes.Where(x => x != SelectedScene).Select(x => x.Key));
                break;
            case TabNpcs:
                if (SelectedNpc != null)
                    RenameKey("NPC", SelectedNpc, v => v.Key, (v, k) => { v.Key = k; RebuildNpcOptions(); NpcTree.Sort(); NpcTree.SyncToModel(); },
                              Npcs.Where(x => x != SelectedNpc).Select(x => x.Key));
                break;
            case TabVariables:
                if (SelectedVariable != null)
                    RenameKey("Variable", SelectedVariable, v => v.Name,
                              (v, k) =>
                              {
                                  // Rewrite every reference BEFORE the declaration
                                  // changes, while the old name is still what the
                                  // pack refers to.
                                  int refs = Services.VariableRenamer.RenameReferences(Pack, v.Name, k);
                                  v.Name = k;
                                  // Re-baseline the deferred-rename snapshot: this
                                  // rename is already applied, so the next commit
                                  // point must not treat it as pending.
                                  _renameOriginName = k;
                                  RebuildVariableNameOptions();
                                  if (refs > 0) RefreshConditionAndActionRows();
                                  LastRenameSummary = refs > 0
                                      ? $"Renamed to '{k}' and updated {refs} reference{(refs == 1 ? "" : "s")}."
                                      : $"Renamed to '{k}' (no references found).";
                              },
                              Variables.Where(x => x != SelectedVariable).Select(x => x.Name));
                break;
            case TabWallpapers:
                if (SelectedWallpaper != null)
                    RenameKey("Wallpaper", SelectedWallpaper, v => v.Key, (v, k) => { v.Key = k; WallpaperTree.Sort(); WallpaperTree.SyncToModel(); },
                              Wallpapers.Where(x => x != SelectedWallpaper).Select(x => x.Key));
                break;
            case TabMusic:
                if (SelectedMusic != null)
                    RenameKey("Music", SelectedMusic, v => v.Key, (v, k) => { v.Key = k; RebuildMusicKeyOptions(); MusicTree.Sort(); MusicTree.SyncToModel(); },
                              Music.Where(x => x != SelectedMusic).Select(x => x.Key));
                break;
            case TabSfx:
                if (SelectedSfx != null)
                    RenameKey("SFX", SelectedSfx, v => v.Key, (v, k) => { v.Key = k; RebuildSfxKeyOptions(); SfxTree.Sort(); SfxTree.SyncToModel(); },
                              Sfx.Where(x => x != SelectedSfx).Select(x => x.Key));
                break;
            case TabIntegration:
                if (SelectedIntegrationRule != null)
                    // Folder membership is stored BY KEY, so a rename has to be
                    // written back or the rule would fall out of its folder on
                    // the next load.
                    RenameKey("Rule", SelectedIntegrationRule, v => v.Key,
                              (v, k) => { v.Key = k; SortIntegrationTree(IntegrationTree); SyncIntegrationFoldersToModel(); },
                              IntegrationRules.Where(x => x != SelectedIntegrationRule).Select(x => x.Key));
                break;
        }
    }

    /// <summary>Result of the last rename, surfaced to the user (e.g. "…and
    /// updated 7 references"). A rename that silently rewrites parts of the
    /// pack the user isn't looking at should say so.</summary>
    private string _lastRenameSummary = "";
    public string LastRenameSummary
    {
        get => _lastRenameSummary;
        private set { _lastRenameSummary = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Re-read every condition/action param row from its model dict. Rows read
    /// the dict live, so a bulk rewrite (variable rename) needs a nudge for
    /// already-realized bindings to catch up.
    /// </summary>
    private void RefreshConditionAndActionRows()
    {
        foreach (var d in Dialogues)
        {
            foreach (var c in d.StartConditions) RefreshCondition(c);
            foreach (var n in d.Nodes)
            {
                foreach (var c in n.Conditions) RefreshCondition(c);
                foreach (var a in n.ActionsOnStart) RefreshAction(a);
                foreach (var a in n.ActionsOnFinish) RefreshAction(a);
            }
        }
        foreach (var r in IntegrationRules)
        {
            foreach (var c in r.Conditions) RefreshCondition(c);
            foreach (var a in r.Actions) RefreshAction(a);
            foreach (var b in r.Branches)
            {
                foreach (var c in b.Conditions) RefreshCondition(c);
                foreach (var a in b.Actions) RefreshAction(a);
            }
        }
        foreach (var p in Places)
        {
            foreach (var h in p.OnEnterHooks.Concat(p.OnExitHooks))
            {
                foreach (var c in h.Conditions) RefreshCondition(c);
                foreach (var a in h.Actions) RefreshAction(a);
            }
            foreach (var b in p.NavigatorButtons)
                foreach (var c in b.Conditions) RefreshCondition(c);
        }
        foreach (var b in MapButtons)
            foreach (var c in b.Conditions) RefreshCondition(c);
    }

    private static void RefreshCondition(NodeConditionViewModel c)
    {
        foreach (var row in c.ParamRows) row.Refresh();
        foreach (var child in c.Children) RefreshCondition(child);   // groups recurse
    }

    private static void RefreshAction(NodeActionViewModel a)
    {
        foreach (var row in a.ParamRows) row.Refresh();
    }

    private void RenameKey<TVm>(string what, TVm vm, Func<TVm, string> get, Action<TVm, string> set, IEnumerable<string> otherKeys)
    {
        var current = get(vm);
        var entered = PromptForText?.Invoke("Rename " + what, "New name:", current);
        if (string.IsNullOrWhiteSpace(entered) || entered.Trim() == current) return;
        LastRenameSummary = "";
        Undo.Checkpoint();
        set(vm, UniqueKey(entered.Trim(), otherKeys));
        // Only speak up when the rename reached beyond the item itself —
        // rewriting parts of the pack the user can't see shouldn't be silent.
        if (!string.IsNullOrEmpty(LastRenameSummary))
            ShowInfo?.Invoke("Rename", LastRenameSummary);
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
        DialogueDropTarget().Add(leaf);
        SyncFoldersToModel();
        SelectedDialogueTreeItem = leaf;
    }

    /// <summary>
    /// Where a newly added / pasted dialogue should land, based on the tree
    /// selection: into a selected folder, into the folder holding a selected
    /// dialogue, else the root. Single resolver so Add, Duplicate and Paste
    /// can't drift apart — they previously each handled only half the cases
    /// (Add ignored a selected leaf, Duplicate ignored a selected folder).
    /// </summary>
    private ObservableCollection<DialogueTreeItem> DialogueDropTarget()
    {
        switch (SelectedDialogueTreeItem)
        {
            case DialogueFolderNode f:
                f.IsExpanded = true;          // so the new item is visible where it landed
                return f.Children;
            case DialogueLeafNode l:
                return FindParentChildren(l) ?? DialogueTree;
            default:
                return DialogueTree;
        }
    }

    /// <summary>
    /// Insert a just-added (duplicated / pasted) variable into the tree at the
    /// selection, then select it. Deliberately NOT a BuildVariableTree() call:
    /// that rebuilds from the model's folder defs, which the new variable
    /// isn't in yet — so it would always reappear at the root and lose the
    /// folder the user was working in. The tree is the source of truth here
    /// (SyncVariableFoldersToModel writes it back on save).
    /// </summary>
    private void PlaceNewVariableInTree(PackVariableViewModel vm)
    {
        var leaf = new VariableLeafNode(vm);
        VariableDropTarget().Add(leaf);
        SortVariableTree(VariableTree);
        SelectedVariable = vm;
        SelectedVariableTreeItem = leaf;
        RebuildVariableNameOptions();
    }

    /// <summary>Variable-tree counterpart of <see cref="DialogueDropTarget"/>.</summary>
    private ObservableCollection<VariableTreeItem> VariableDropTarget()
    {
        switch (SelectedVariableTreeItem)
        {
            case VariableFolderNode f:
                f.IsExpanded = true;
                return f.Children;
            case VariableLeafNode l:
                return FindVariableParentChildren(l) ?? VariableTree;
            default:
                return VariableTree;
        }
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
    /// <summary>
    /// Replace an options collection's contents IN PLACE, without ever calling
    /// Clear(). This is load-bearing, not a micro-optimisation.
    /// <para/>
    /// These collections feed editable ComboBoxes whose <c>Text</c> (and
    /// sometimes <c>SelectedValue</c>) is TwoWay-bound straight to the model.
    /// Clearing an ItemsSource makes WPF reset the box's selection to null,
    /// which the TwoWay binding then writes BACK as an empty value — silently
    /// destroying the authored value. Because these lists rebuild on ordinary
    /// interactions (selecting a dialogue node rebuilds the variable options;
    /// loading a pack rebuilds the roomtalk options), the effect looked random:
    /// a field would empty itself on click or reload. Keeping surviving items
    /// in the collection means the ComboBox keeps its selection and never
    /// write-backs.
    /// <para/>
    /// Items are matched by value equality (records / strings), so re-created
    /// but equal entries count as surviving.
    /// </summary>
    private static void SyncOptions<T>(ObservableCollection<T> target, System.Collections.Generic.IList<T> desired)
    {
        for (int i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(target[i])) target.RemoveAt(i);

        for (int i = 0; i < desired.Count; i++)
        {
            int cur = target.IndexOf(desired[i]);
            if (cur < 0) target.Insert(i, desired[i]);
            else if (cur != i) target.Move(cur, i);
        }
    }

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
        // In-place sync, never Clear — see SyncOptions. A Clear here nulled the
        // Roomtalk combo's selection and wrote the empty value back, which is
        // why a dialogue's roomtalk kept vanishing on reload.
        SyncOptions(RoomTalkOptions,
            roomTalkOpts.OrderBy(r => r.DisplayLabel, System.StringComparer.OrdinalIgnoreCase).ToList());
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
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(LevelOptions,
            levelOpts.OrderBy(l => l.DisplayLabel, System.StringComparer.OrdinalIgnoreCase).ToList());
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
        // Place the new dialogue where the selection points — selected folder,
        // the folder holding the selected dialogue, else root.
        var leaf = new DialogueLeafNode(vm);
        DialogueDropTarget().Add(leaf);
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
            vm.PropertyChanged += OnActorPropertyChanged;
            Actors.Add(vm);
        }
        ActorTree.Build(Actors);
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
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(ActorOptions,
            Actors.OrderBy(a => a.Key, System.StringComparer.OrdinalIgnoreCase).Select(a => a.Key).ToList());

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
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(BustNameOptions,
            bustOpts.OrderBy(b => b.DisplayLabel, System.StringComparer.OrdinalIgnoreCase).ToList());

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
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(ActorBustOptions,
            actorBustOpts.OrderBy(o => o.DisplayLabel, System.StringComparer.OrdinalIgnoreCase).ToList());

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
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(ExpressionKeyOptions,
            exprOpts.OrderBy(e => e, System.StringComparer.OrdinalIgnoreCase).ToList());
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
        vm.PropertyChanged += OnActorPropertyChanged;
        Actors.Add(vm);
        ActorTree.PlaceNew(vm);
        RebuildActorAndBustOptions();
    }

    private void RemoveActor()
    {
        if (ActorTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedActor is null) return;
        var vm = SelectedActor;
        vm.DefaultBustKeyChanged -= OnActorDefaultBustKeyChanged;
        Pack.Actors.Remove(vm.Model);
        Actors.Remove(vm);
        ActorTree.RemoveLeafFor(vm);
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

    private void OnVariablesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (PackVariableViewModel v in e.OldItems) v.PropertyChanged -= OnVariableIdentityChanged;
        if (e.NewItems != null)
            foreach (PackVariableViewModel v in e.NewItems) v.PropertyChanged += OnVariableIdentityChanged;
        // A Reset (Clear) reports no OldItems, so the dropped VMs keep their
        // handler. That's harmless: the publisher is the dead VM and the
        // subscriber is this long-lived view-model, so nothing is kept alive.
        RebuildVariableNameOptions();
    }

    private void OnVariableIdentityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackVariableViewModel.Name) ||
            e.PropertyName == nameof(PackVariableViewModel.Type))
            RebuildVariableNameOptions();
    }

    public void RebuildVariableNameOptions()
    {
        // In-place sync, never Clear — see SyncOptions. This runs on every
        // dialogue-node selection, and a Clear here nulled the bound Name
        // combo of every open condition/action row, writing the empty value
        // back: the "variable name vanishes when I click a node" bug.
        var all = new System.Collections.Generic.List<string>();
        var lists = new System.Collections.Generic.List<string>();
        foreach (var v in Variables.OrderBy(v => v.Name, System.StringComparer.OrdinalIgnoreCase))
        {
            all.Add(v.Name);
            // List-typed variables also surface in the
            // List-only dropdown used by AddToList /
            // RemoveFromList / ClearList action params.
            if (v.Model.Type == PackVariableType.List) lists.Add(v.Name);
        }
        SyncOptions(VariableNameOptions, all);
        SyncOptions(ListVariableNameOptions, lists);
    }

    /// <summary>
    /// Rebuilds <see cref="MusicKeyOptions"/> from the Music tab. Called
    /// after rebind, add and remove operations so the SwitchMusic
    /// dropdown stays current.
    /// </summary>
    public void RebuildMusicKeyOptions()
    {
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(MusicKeyOptions,
            Music.OrderBy(m => m.Key, System.StringComparer.OrdinalIgnoreCase).Select(m => m.Key).ToList());
    }

    /// <summary>Rebuilds <see cref="SfxKeyOptions"/> from the SFX tab.</summary>
    public void RebuildSfxKeyOptions()
    {
        // In-place sync, never Clear — see SyncOptions.
        SyncOptions(SfxKeyOptions,
            Sfx.OrderBy(s => s.Key, System.StringComparer.OrdinalIgnoreCase).Select(s => s.Key).ToList());
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
            foreach (var o in FlattenOverlays(p.GameObjects))
                if (!string.IsNullOrWhiteSpace(o.Name)) names.Add(o.Name);
        foreach (var ch in Characters)
            foreach (var o in ch.Outfits)
                if (!string.IsNullOrWhiteSpace(o.GameObjectName)) { names.Add(o.GameObjectName); busts.Add(o.GameObjectName); }

        // In-place sync, never Clear — see SyncOptions. This one rebuilds on
        // every node selection, so a Clear here emptied bound Target combos.
        SyncOptions(GameObjectNameOptions, names.ToList());
        SyncOptions(BustNameOnlyOptions, busts.ToList());
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
        // Place the new variable where the selection points — selected folder,
        // the folder holding the selected variable, else root.
        var leaf = new VariableLeafNode(vm);
        VariableDropTarget().Add(leaf);
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
        // Build the folder tree the left-hand list binds to. Missing this call
        // (unlike every other tab) left the Scenes list blank on load even
        // though the collection was populated — the detail pane still opened
        // the first scene, which is how the empty list gave itself away.
        SceneTree.Build(Scenes);
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
        // In-place sync, never Clear — see SyncOptions.
        var sceneOpts = new System.Collections.Generic.List<NavigatorTargetOption>();
        var sceneKeys = new System.Collections.Generic.List<string>();
        foreach (var s in Scenes.OrderBy(
                     s => string.IsNullOrWhiteSpace(s.DisplayName) ? s.Key : s.DisplayName,
                     System.StringComparer.OrdinalIgnoreCase))
        {
            sceneOpts.Add(new NavigatorTargetOption(
                Token: s.Key,
                DisplayLabel: string.IsNullOrWhiteSpace(s.DisplayName) ? s.Key : $"{s.DisplayName} ({s.Key})"));
            if (!string.IsNullOrWhiteSpace(s.Key)) sceneKeys.Add(s.Key);
        }
        SyncOptions(SceneOptions, sceneOpts);
        SyncOptions(SceneKeyOptions, sceneKeys);
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
        SceneTree.PlaceNew(vm);
        RebuildSceneOptions();
    }

    private void RemoveScene()
    {
        if (SceneTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedScene is null) return;
        var vm = SelectedScene;
        Pack.Scenes.Remove(vm.Model);
        Scenes.Remove(vm);
        SceneTree.RemoveLeafFor(vm);
        SelectedScene = Scenes.FirstOrDefault();
        RebuildSceneOptions();
    }

    public void RebuildNpcOptions()
    {
        // In-place sync, never Clear — see SyncOptions.
        var opts = new System.Collections.Generic.List<NavigatorTargetOption>();
        foreach (var n in Npcs.OrderBy(n => n.Key, System.StringComparer.OrdinalIgnoreCase))
            if (!string.IsNullOrWhiteSpace(n.Key))
                opts.Add(new NavigatorTargetOption(
                    Token: n.Key,
                    DisplayLabel: string.IsNullOrWhiteSpace(n.DisplayName) ? n.DisplayName : $"{n.DisplayName} ({n.Key})"));
        SyncOptions(NpcKeyOptions, opts);
    }

    private void AddNpc()
    {
        var def = new NpcDef
        {
            Key = $"npc{Pack.Npcs.Count + 1}",
            DisplayName = "New NPC",
        };
        Pack.Npcs.Add(def);
        var vm = new NpcViewModel(def);
        Npcs.Add(vm);
        NpcTree.PlaceNew(vm);
        RebuildNpcOptions();
    }

    private void RemoveNpc()
    {
        if (NpcTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedNpc is null) return;
        var vm = SelectedNpc;
        Pack.Npcs.Remove(vm.Model);
        Npcs.Remove(vm);
        NpcTree.RemoveLeafFor(vm);
        SelectedNpc = Npcs.FirstOrDefault();
        RebuildNpcOptions();
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
        WallpaperTree.Build(Wallpapers);
        SelectedWallpaper = Wallpapers.FirstOrDefault();
    }

    private void RebindNpcs()
    {
        Npcs.Clear();
        foreach (var n in Pack.Npcs) Npcs.Add(new NpcViewModel(n));
        NpcTree.Build(Npcs);
        SelectedNpc = Npcs.FirstOrDefault();
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
        WallpaperTree.PlaceNew(vm);
    }

    private void RemoveWallpaper()
    {
        if (WallpaperTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedWallpaper is null) return;
        var vm = SelectedWallpaper;
        Pack.Wallpapers.Remove(vm.Model);
        Wallpapers.Remove(vm);
        WallpaperTree.RemoveLeafFor(vm);
        SelectedWallpaper = Wallpapers.FirstOrDefault();
    }

    private void RebindMusic()
    {
        Music.Clear();
        foreach (var m in Pack.Music) Music.Add(new MusicViewModel(m));
        MusicTree.Build(Music);
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
        MusicTree.PlaceNew(vm);
        RebuildMusicKeyOptions();
    }

    private void RemoveMusic()
    {
        if (MusicTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedMusic is null) return;
        var vm = SelectedMusic;
        Pack.Music.Remove(vm.Model);
        Music.Remove(vm);
        MusicTree.RemoveLeafFor(vm);
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
        SfxTree.Build(Sfx);
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
        SfxTree.PlaceNew(vm);
        RebuildSfxKeyOptions();
    }

    private void RemoveSfx()
    {
        if (SfxTree.RemoveSelectedFolderLiftChildren()) return;
        if (SelectedSfx is null) return;
        var vm = SelectedSfx;
        vm.PropertyChanged -= OnSfxChanged;
        Pack.Sfx.Remove(vm.Model);
        Sfx.Remove(vm);
        SfxTree.RemoveLeafFor(vm);
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

    private bool CanPlayMusic(MusicViewModel? music)
        => music != null && !string.IsNullOrWhiteSpace(music.AudioPath) && !string.IsNullOrEmpty(PackRoot);

    /// <summary>
    /// Preview a music track through the editor's audio output. Same
    /// resolution rules as <see cref="PlaySfx"/> (pack-relative path, pack
    /// must be saved); volume falls back to full when unset, matching the
    /// runtime's "inherit from template" default closely enough for preview.
    /// The preview does NOT loop — hearing the track once is what the button
    /// is for; loop is a runtime behavior.
    /// </summary>
    private void PlayMusic(MusicViewModel? music)
    {
        if (!CanPlayMusic(music)) return;
        string rel = music!.AudioPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        string abs = System.IO.Path.Combine(PackRoot!, rel);
        float volume = music.Model.Volume ?? 1f;
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
        BuildIntegrationTree();
        SelectedIntegrationRule = IntegrationRules.FirstOrDefault();
        SelectedIntegrationTreeItem = SelectedIntegrationRule != null
            ? FindIntegrationLeaf(SelectedIntegrationRule) : null;
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
        PlaceNewIntegrationRuleInTree(vm);
    }

    private void RemoveIntegrationRule()
    {
        var sel = SelectedIntegrationTreeItem;
        if (sel is IntegrationFolderNode folder)
        {
            // Delete the folder; its children move up to where the folder was.
            // Deleting a folder must never delete the rules inside it.
            var parent = FindIntegrationParentChildren(folder) ?? IntegrationTree;
            int idx = parent.IndexOf(folder);
            parent.Remove(folder);
            foreach (var c in folder.Children.ToList()) parent.Insert(idx++, c);
            SortIntegrationTree(IntegrationTree);
            SyncIntegrationFoldersToModel();
            SelectedIntegrationTreeItem = null;
            return;
        }
        // Route through the tree remover so the leaf goes too — otherwise the
        // rule would vanish from the model but linger in its folder.
        var leaf = sel as IntegrationLeafNode
                   ?? (SelectedIntegrationRule != null ? FindIntegrationLeaf(SelectedIntegrationRule) : null);
        if (leaf != null) { RemoveIntegrationLeaf(leaf); return; }
        if (SelectedIntegrationRule is null) return;
        Pack.IntegrationRules.Remove(SelectedIntegrationRule.Model);
        IntegrationRules.Remove(SelectedIntegrationRule);
        SelectedIntegrationRule = IntegrationRules.FirstOrDefault();
    }

    // ── Integration folder tree ───────────────────────────────────────────
    // Cosmetic grouping over the flat IntegrationRules list, mirroring the
    // Variables tab exactly: the TREE is the source of truth while the editor
    // is open, and SyncIntegrationFoldersToModel writes it back to the
    // editor-only integrationFolders key on save. Rules not in any folder live
    // at the root.

    public ObservableCollection<IntegrationTreeItem> IntegrationTree { get; } = new();

    /// <summary>Backs the Integration sidebar search box.</summary>
    public TreeFilterViewModel IntegrationTreeFilter { get; }

    private IntegrationTreeItem? _selectedIntegrationTreeItem;
    public IntegrationTreeItem? SelectedIntegrationTreeItem
    {
        get => _selectedIntegrationTreeItem;
        set
        {
            _selectedIntegrationTreeItem = value;
            OnPropertyChanged();
            if (value is IntegrationLeafNode leaf) SelectedIntegrationRule = leaf.Rule;
        }
    }

    public void BuildIntegrationTree()
    {
        IntegrationTree.Clear();
        var placed = new HashSet<string>();
        foreach (var f in Pack.IntegrationFolders)
            IntegrationTree.Add(BuildIntegrationFolderNode(f, placed));
        foreach (var r in IntegrationRules)
            if (!placed.Contains(r.Key))
                IntegrationTree.Add(new IntegrationLeafNode(r));
        SortIntegrationTree(IntegrationTree);
        // A rebuild replaces every node, so re-apply any live search.
        IntegrationTreeFilter.Reapply();
    }


    private IntegrationFolderNode BuildIntegrationFolderNode(IntegrationFolderDef f, HashSet<string> placed)
    {
        var node = new IntegrationFolderNode(f.Name);
        foreach (var sub in f.Folders)
            node.Children.Add(BuildIntegrationFolderNode(sub, placed));
        foreach (var key in f.Rules)
        {
            var r = IntegrationRules.FirstOrDefault(x => x.Key == key);
            if (r != null && placed.Add(key))
                node.Children.Add(new IntegrationLeafNode(r));
        }
        return node;
    }

    private static void SortIntegrationTree(ObservableCollection<IntegrationTreeItem> level)
    {
        var sorted = level
            .OrderBy(i => i is IntegrationFolderNode ? 0 : 1)   // folders first
            .ThenBy(i => i is IntegrationFolderNode f ? f.Name : ((IntegrationLeafNode)i).Rule.Display,
                    System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        level.Clear();
        foreach (var i in sorted) level.Add(i);
        foreach (var i in sorted)
            if (i is IntegrationFolderNode fn) SortIntegrationTree(fn.Children);
    }

    /// <summary>Persist the current tree into the editor-only integrationFolders key. Root leaves stay implicit.</summary>
    public void SyncIntegrationFoldersToModel()
    {
        Pack.IntegrationFolders.Clear();
        foreach (var item in IntegrationTree)
            if (item is IntegrationFolderNode fn)
                Pack.IntegrationFolders.Add(IntegrationFolderNodeToDef(fn));
    }

    private static IntegrationFolderDef IntegrationFolderNodeToDef(IntegrationFolderNode fn)
    {
        var def = new IntegrationFolderDef { Name = fn.Name };
        foreach (var c in fn.Children)
        {
            if (c is IntegrationFolderNode sub) def.Folders.Add(IntegrationFolderNodeToDef(sub));
            else if (c is IntegrationLeafNode leaf) def.Rules.Add(leaf.Rule.Key);
        }
        return def;
    }

    private void AddIntegrationFolder()
    {
        var folder = new IntegrationFolderNode("New Folder");
        if (SelectedIntegrationTreeItem is IntegrationFolderNode target)
        {
            target.Children.Insert(0, folder);
            target.IsExpanded = true;
        }
        else IntegrationTree.Add(folder);
        SortIntegrationTree(IntegrationTree);
        SyncIntegrationFoldersToModel();
        SelectedIntegrationTreeItem = folder;
    }

    /// <summary>Where a new / pasted rule lands — mirrors <see cref="DialogueDropTarget"/>.</summary>
    private ObservableCollection<IntegrationTreeItem> IntegrationDropTarget()
    {
        switch (SelectedIntegrationTreeItem)
        {
            case IntegrationFolderNode f:
                f.IsExpanded = true;
                return f.Children;
            case IntegrationLeafNode l:
                return FindIntegrationParentChildren(l) ?? IntegrationTree;
            default:
                return IntegrationTree;
        }
    }

    /// <summary>Insert a just-added / pasted rule at the selection and select it.
    /// Not a BuildIntegrationTree() call — that rebuilds from the model's folder
    /// defs, which the new rule isn't in yet, so it would always reappear at root.</summary>
    private void PlaceNewIntegrationRuleInTree(UpdateRuleViewModel vm)
    {
        var leaf = new IntegrationLeafNode(vm);
        IntegrationDropTarget().Add(leaf);
        SortIntegrationTree(IntegrationTree);
        SyncIntegrationFoldersToModel();
        SelectedIntegrationRule = vm;
        SelectedIntegrationTreeItem = leaf;
    }

    public ObservableCollection<IntegrationTreeItem>? FindIntegrationParentChildren(IntegrationTreeItem item)
        => IntegrationTree.Contains(item) ? IntegrationTree : FindIntegrationParentIn(IntegrationTree, item);

    private static ObservableCollection<IntegrationTreeItem>? FindIntegrationParentIn(
        ObservableCollection<IntegrationTreeItem> coll, IntegrationTreeItem item)
    {
        foreach (var c in coll)
            if (c is IntegrationFolderNode fn)
            {
                if (fn.Children.Contains(item)) return fn.Children;
                var found = FindIntegrationParentIn(fn.Children, item);
                if (found != null) return found;
            }
        return null;
    }

    private static bool IsIntegrationDescendant(IntegrationFolderNode folder, IntegrationTreeItem? maybe)
    {
        if (maybe == null) return false;
        foreach (var c in folder.Children)
        {
            if (c == maybe) return true;
            if (c is IntegrationFolderNode sub && IsIntegrationDescendant(sub, maybe)) return true;
        }
        return false;
    }

    /// <summary>Move a dragged rule-tree item onto a drop target (into a folder,
    /// beside a leaf, or to root when the target is null). A folder can't drop
    /// into itself or a descendant.</summary>
    public void MoveIntegrationTreeItem(IntegrationTreeItem dragged, IntegrationTreeItem? target)
    {
        if (dragged == null || dragged == target) return;

        ObservableCollection<IntegrationTreeItem> dest;
        IntegrationFolderNode? destFolder = null;
        if (target is IntegrationFolderNode f) { dest = f.Children; destFolder = f; }
        else if (target is IntegrationLeafNode leaf) dest = FindIntegrationParentChildren(leaf) ?? IntegrationTree;
        else dest = IntegrationTree;

        if (dragged is IntegrationFolderNode df &&
            (df == destFolder || IsIntegrationDescendant(df, destFolder) || IsIntegrationDescendant(df, target)))
            return;

        var from = FindIntegrationParentChildren(dragged);
        if (from == null || from == dest) return;

        Undo.Checkpoint();
        from.Remove(dragged);
        dest.Add(dragged);
        if (destFolder != null) destFolder.IsExpanded = true;
        SortIntegrationTree(IntegrationTree);
        SyncIntegrationFoldersToModel();
    }

    private IntegrationLeafNode? FindIntegrationLeaf(UpdateRuleViewModel r) => FindIntegrationLeafIn(IntegrationTree, r);
    private static IntegrationLeafNode? FindIntegrationLeafIn(ObservableCollection<IntegrationTreeItem> coll, UpdateRuleViewModel r)
    {
        foreach (var c in coll)
        {
            if (c is IntegrationLeafNode l && l.Rule == r) return l;
            if (c is IntegrationFolderNode fn) { var found = FindIntegrationLeafIn(fn.Children, r); if (found != null) return found; }
        }
        return null;
    }

    private void RemoveIntegrationLeaf(IntegrationLeafNode leaf)
    {
        FindIntegrationParentChildren(leaf)?.Remove(leaf);
        Pack.IntegrationRules.Remove(leaf.Rule.Model);
        IntegrationRules.Remove(leaf.Rule);
        SyncIntegrationFoldersToModel();
        SelectedIntegrationTreeItem = null;
        SelectedIntegrationRule = IntegrationRules.FirstOrDefault();
    }

    private void Validate()
    {
        Issues.Clear();
        if (PackRoot is null) return;
        foreach (var issue in PackValidator.Validate(Pack, PackRoot))
            Issues.Add(issue);
    }

    // ── Static helper for param-row boolean detection ───────────────────

    /// <summary>Check whether a variable name (from the current pack) is boolean.
    /// Used by ParamRowViewModel to decide whether to show True/False radios.
    /// Returns false if no pack is loaded or the variable isn't found.</summary>
    public static bool IsVariableBoolean(string varName)
    {
        if (string.IsNullOrWhiteSpace(varName)) return false;
        return PackRepository.IsVariableBoolean(varName);
    }

    /// <summary>Sets the active pack for cross-VM lookups (boolean variable detection, etc.).</summary>
    internal void SetActivePack(ModPack pack)
    {
        PackRepository.ActivePack = pack;
    }
}
