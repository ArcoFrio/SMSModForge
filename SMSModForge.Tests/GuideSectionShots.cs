using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Documentation;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The guide's small pictures (1.10.0, the author: "We should be not only telling the user where something is, but
/// also adding a small image to show them"): for each section of a page, the part of the editor it is about, marked,
/// with a little of what is around it dimmed so it can be found again. Taken into SMSMODFORGE_GUIDE_SHOTS/Sections
/// in the run's look, as <see cref="GuidePictureShots"/> takes the pages' own; Tools/Docs/guide-pictures.py brings
/// them into the editor. A section is found by its heading on its tab - the guide's headings are the editor's own
/// group captions - or by the spot <see cref="Spots"/> names for it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GuideSectionShots
{
    private readonly ITestOutputHelper _out;
    public GuideSectionShots(ITestOutputHelper o) => _out = o;

    internal static IEnumerable<DependencyObject> Below(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Below(c)) yield return d;
        }
    }

    private static string HeaderText(object? h) => h switch
    {
        string s => s,
        TextBlock t => t.Text,
        ContentControl c => HeaderText(c.Content),
        Panel p => p.Children.OfType<object>().Select(HeaderText).FirstOrDefault(x => x.Length > 0) ?? "",
        _ => "",
    };

    private static string Plain(string a) => a.Replace(" & ", " and ").Replace("\u2026", "...").Trim().TrimEnd(':').Trim();

    private static bool Same(string a, string b) => string.Equals(Plain(a), Plain(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>A caption the heading names: the same words, or the heading followed by the caption's own
    /// explanation ("Jiggle" for "Jiggle (mask-driven shader)", "Sprites" for "Sprites and expressions (PNG ...)").</summary>
    private static bool Names(string caption, string heading)
    {
        string c = Plain(caption), h = Plain(heading);
        if (h.Length == 0) return false;
        if (string.Equals(c, h, StringComparison.OrdinalIgnoreCase)) return true;
        return c.StartsWith(h, StringComparison.OrdinalIgnoreCase) && c.Length > h.Length && !char.IsLetterOrDigit(c[h.Length]);
    }

    private static bool Shown(FrameworkElement f) => f.IsVisible && f.ActualWidth > 4 && f.ActualHeight > 4;

    /// <summary>The group whose caption reads the heading: a GroupBox, an Expander, any headed control.</summary>
    internal static FrameworkElement? ByHeading(DependencyObject root, string heading)
        => Below(root).OfType<HeaderedContentControl>().Where(h => Shown(h) && h is not TabItem)
                      .FirstOrDefault(h => Same(HeaderText(h.Header), heading))
           ?? Below(root).OfType<HeaderedContentControl>().Where(h => Shown(h) && h is not TabItem)
                      .FirstOrDefault(h => Names(HeaderText(h.Header), heading));

    /// <summary>A control that says these words: a label, a button, a tick box.</summary>
    internal static FrameworkElement? ByText(DependencyObject root, string text)
        => Below(root).OfType<FrameworkElement>().FirstOrDefault(f => Shown(f) && f switch
        {
            TextBlock t => Same(t.Text, text),
            ButtonBase b => Same(HeaderText(b.Content), text),
            Label l => Same(HeaderText(l.Content), text),
            _ => false,
        });

    internal static FrameworkElement? Anchor(DependencyObject root, string id) => View.TutorialAnchor.Find(root, id);

    /// <summary>Where one picture is: its tab (-1: stay), what to do first, and how to find the part to mark. A spot
    /// with a menu pictures that menu open, the item named marked; one in the mask studio opens it first.</summary>
    internal sealed record Spot(int Tab, Action<MainViewModel>? Setup, Func<MainWindow, FrameworkElement?> Find, double Around = 28)
    {
        public string? Menu { get; init; }
        public string? MenuItem { get; init; }
        public bool Studio { get; init; }
        /// <summary>On-screen labels to box in red, numbered in this order (an example's step: what to click).</summary>
        public string[]? Labels { get; init; }
        /// <summary>Put back what the setup changed for the picture only.</summary>
        public Action<MainViewModel>? After { get; init; }
    }

    /// <summary>The group a caption or label sits in: its nearest headed ancestor, else its parent.</summary>
    private static FrameworkElement? Holder(FrameworkElement? f)
    {
        for (DependencyObject? d = f; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is HeaderedContentControl h && h is not TabItem) return h;
        return Up(f);
    }

    private static FrameworkElement? Up(FrameworkElement? f, int levels = 1)
    {
        DependencyObject? d = f;
        for (int i = 0; i < levels && d != null; i++) d = VisualTreeHelper.GetParent(d);
        return d as FrameworkElement;
    }

    private static FrameworkElement Tabs(MainWindow w) => (FrameworkElement)w.FindName("MainTabs");

    /// <summary>The row a condition or action of the pack is drawn in.</summary>
    internal static FrameworkElement? RowOf(DependencyObject root, object model)
    {
        foreach (var f in Below(root).OfType<FrameworkElement>())
        {
            if (!Shown(f)) continue;
            object? m = f.DataContext switch { NodeActionViewModel a => a.Model, NodeConditionViewModel c => c.Model, _ => null };
            if (m == null || !ReferenceEquals(m, model)) continue;
            if (VisualTreeHelper.GetParent(f) is FrameworkElement p && ReferenceEquals(p.DataContext, f.DataContext)) continue;
            return f;
        }
        return null;
    }

    private static bool IsOf(string term, string type, bool action)
    {
        if (term == "Variable")
            return action ? type is NodeActionTypes.SetVariable or NodeActionTypes.IncrementVariable
                          : type.StartsWith("Variable", StringComparison.Ordinal) && type != "VariableStartsWith";
        return type == term;
    }

    /// <summary>A condition or action of one of the terms in the story pack, on a rule or a line: its tab, how to
    /// choose it, and the model to find the row of.</summary>
    private static (int Tab, Action<MainViewModel> Choose, object Model)? Used(MainViewModel vm, IEnumerable<string> terms, bool action)
    {
        foreach (string term in terms)
        {
            foreach (var r in vm.IntegrationRules)
            {
                var hooks = new[] { (r.Model.Conditions, r.Model.Actions) }
                    .Concat(r.Model.Branches.Select(b => (b.Conditions, b.Actions)));
                foreach (var (conds, acts) in hooks)
                {
                    object? hit = action ? acts.FirstOrDefault(a => IsOf(term, a.Type, true))
                                         : conds.FirstOrDefault(c => IsOf(term, c.Type, false));
                    if (hit != null) return (MainTab.Integration, v => v.SelectedIntegrationRule = r, hit);
                }
            }
            foreach (var d in vm.Dialogues)
            {
                if (!action && d.Model.StartConditions.FirstOrDefault(c => IsOf(term, c.Type, false)) is { } sc)
                    return (MainTab.Dialogues, v => v.SelectedDialogue = d, sc);
                foreach (var n in d.Nodes)
                {
                    object? hit = action
                        ? n.Model.ActionsOnStart.Concat(n.Model.ActionsOnFinish).FirstOrDefault(a => IsOf(term, a.Type, true))
                        : n.Model.Conditions.FirstOrDefault(c => IsOf(term, c.Type, false));
                    if (hit != null) return (MainTab.Dialogues, v => { v.SelectedDialogue = d; v.SelectedNode = n; }, hit);
                }
            }
        }
        return null;
    }

    private static Spot? RowSpot(MainViewModel vm, IEnumerable<string> terms, bool action)
        => Used(vm, terms, action) is { } u ? new Spot(u.Tab, u.Choose, w => RowOf(Tabs(w), u.Model), 36) : null;

    private static Spot Menu(string menu, string? item) => new(-1, null, _ => null) { Menu = menu, MenuItem = item };

    /// <summary>The Issues page: its list, or its "nothing found" when the pack is clean.</summary>
    private static FrameworkElement? IssuesPage(MainWindow w) => w.FindName("HomePageIssues") is FrameworkElement f && Shown(f) ? f : null;

    /// <summary>Sections, and pages that are not about one tab, not found by their heading: by picture name.</summary>
    internal static Dictionary<string, Spot> Spots(MainViewModel vm)
    {
        string T(string key) => Localization.Loc.T(key);
        var her = StoryPack.Dummy(vm);
        void Her(MainViewModel v) { if (her != null) { v.SelectedCharacter = her; her.IsExpanded = true; } }
        void PackQuest(MainViewModel v)
        {
            if (v.Quests.FirstOrDefault(q => q.Model.Source.Length == 0 && !q.IsVanillaExtension) is { } q) v.SelectedQuest = q;
        }
        void Issues(MainViewModel v) { v.ValidateCommand.Execute(null); v.HomePage = HomePage.Issues; }
        var spots = new Dictionary<string, Spot>
        {
            // Start here.
            ["Sections/modPack.packOneFolderEverything"] = new(MainTab.ModForge, v => v.HomePage = HomePage.Home, w => ByHeading(Tabs(w), "Your pack")),
            ["Sections/modPack.heading.worthKnowing"] = new(MainTab.ModForge, v => v.HomePage = HomePage.Home, w => ByHeading(Tabs(w), "Getting started")),
            ["Sections/orderWork.fileThenNewPack"] = new(MainTab.Characters, Her, w => Up(Below(Tabs(w)).OfType<TabItem>().FirstOrDefault(Shown)), 16),
            ["Sections/orderWork.heading.worthKnowing"] = new(MainTab.Places, null, w => Anchor(w, "btn:addPlaceCharacter"), 60),
            ["Sections/thingsPointEachOther.mostReferencesJustOther"] = new(MainTab.Dialogues, null, w => Anchor(w, "panel:nodeEditor")),
            // A dropdown showing the stored word with its readable name beside it.
            ["Sections/thingsPointEachOther.heading.worthKnowing"] = new(MainTab.MapButtons, null, w => Anchor(w, "panel:mapButtonList"), 16)
            {
                Labels = new[] { "Target place" },
            },
            ["Sections/puttingValuesIntoText.dialogueTextButtonLabels"] = new(MainTab.Dialogues, v =>
            {
                foreach (var d in v.Dialogues)
                    if (d.Nodes.FirstOrDefault(n => n.Model.Text.Contains("[PV:", StringComparison.Ordinal)) is { } n)
                    { v.SelectedDialogue = d; v.SelectedNode = n; return; }
            }, w => Anchor(w, "panel:nodeEditor")),
            ["Sections/puttingValuesIntoText.heading.markupLine"] = new(MainTab.Dialogues, null,
                w => w.FindName("NodeTextToolbar") is FrameworkElement t && Shown(t) ? t : null, 40),
            ["Sections/whenSomethingDoesNotWork.everyCheckAcrossWhole"] = new(MainTab.ModForge, Issues,
                IssuesPage),
            ["Sections/whenSomethingDoesNotWork.heading.worthKnowing"] = new(MainTab.Integration, null, w => Anchor(w, "field:ruleDebug"), 40),
            // Concepts.
            ["Sections/packsSmspack.whereWorkHoldsModpack"] = Menu(T("menu.file"), T("menu.exportPack")),
            ["Sections/packsSmspack.heading.worthKnowing"] = Menu(T("menu.options"), T("menu.warnAboutLargeExports")),
            ["Sections/runtimeNamesDisplayNames.identifierEverythingElsePoints"] = new(MainTab.Characters, Her, w =>
            {
                var e = Below(Tabs(w)).OfType<Expander>().FirstOrDefault(x => Shown(x) && Same(HeaderText(x.Header), "Internal names"));
                if (e == null) return null;
                e.IsExpanded = true;
                w.UpdateLayout();
                WindowHarness.Pump();
                return e;
            }, 40),
            ["Sections/runtimeNamesDisplayNames.heading.worthKnowing"] = Menu(T("menu.edit"), T("menu.renameItem")),
            ["foldersLists"] = new(MainTab.Dialogues, null, w => Anchor(w, "panel:dialogueList"), 20),
            ["Sections/foldersLists.makeFolderCurrentTab"] = new(MainTab.Places, null, w => Up(ByText(Tabs(w), "+ Folder")), 30),
            ["Sections/conditionActionLists.addRequirementEveryCondition"] = new(MainTab.Integration, null, w => Anchor(w, "panel:ruleConditions")),
            ["Sections/conditionActionLists.heading.worthKnowing"] = new(MainTab.Dialogues, null, w => Up(ByText(Tabs(w), T("dialogues.copyStartConditions"))), 40),
            ["maskPainter"] = new(MainTab.Characters, Her, w => w.CurrentMaskStudio, 0) { Studio = true },
            ["Sections/maskPainter.maskNotArtworkRecords"] = new(MainTab.Characters, Her,
                w => Up(w.CurrentMaskStudio?.FindName("LayerR") as FrameworkElement)) { Studio = true },
            ["Sections/maskPainter.heading.whichLayerMoves"] = new(MainTab.Characters, Her, w => ByHeading(Tabs(w), "Jiggle")),
            ["Sections/maskPainter.heading.hotkeys"] = new(MainTab.Characters, Her,
                w => w.CurrentMaskStudio?.FindName("StudioHeader") as FrameworkElement, 8) { Studio = true },
            ["previews"] = new(MainTab.Places, v => { if (StoryPack.Gallery(v) is { } g) v.SelectedPlace = g; }, w => Anchor(w, "panel:placePreview"), 16),
            ["Sections/previews.previewsComposeArtWay"] = new(MainTab.Characters, Her, w => Anchor(w, "panel:characterPreview")),
            // The workspace.
            ["Sections/fileMenu.startEmptyPackSave"] = Menu(T("menu.file"), T("menu.newPack")),
            ["Sections/fileMenu.heading.worthKnowing"] = Menu(T("menu.file"), T("menu.exportPack")),
            ["Sections/editMenu.undo"] = Menu(T("menu.edit"), T("menu.undo")),
            ["Sections/editMenu.heading.worthKnowing"] = Menu(T("menu.edit"), T("menu.renameItem")),
            ["Sections/optionsMenu.heading.look"] = Menu(T("menu.options"), T("menu.themes")),
            ["Sections/optionsMenu.heading.writing"] = Menu(T("menu.options"), T("menu.spellCheckNodeText")),
            ["Sections/optionsMenu.heading.saving"] = Menu(T("menu.options"), T("menu.confirmChangesSave")),
            ["Sections/optionsMenu.heading.game"] = Menu(T("menu.options"), T("menu.setGameFolder")),
            ["Sections/optionsMenu.heading.updates"] = Menu(T("menu.options"), T("menu.checkUpdatesStart")),
            ["themes"] = new(MainTab.Characters, Her, w => (FrameworkElement)w.Content, 0),
            ["Sections/themes.ex"] = Menu(T("menu.options"), T("menu.themes")),
            ["Sections/keyboardShortcuts.heading.pack"] = Menu(T("menu.file"), T("menu.savePack")),
            ["Sections/keyboardShortcuts.heading.undo"] = Menu(T("menu.edit"), T("menu.undo")),
            ["Sections/keyboardShortcuts.heading.selectedItem"] = Menu(T("menu.edit"), T("menu.duplicateItem")),
            ["Sections/modforgeTab.heading.pages"] = new(MainTab.ModForge, v => v.HomePage = HomePage.Home, w => Up(Anchor(w, "btn:validate")), 12),
            ["Sections/modforgeTab.heading.issues"] = new(MainTab.ModForge, Issues, IssuesPage),
            ["Sections/modforgeTab.heading.debugTracking"] = new(MainTab.ModForge, v =>
            {
                if (v.Dialogues.Count > 0) v.Dialogues[0].DebugConditions = true;
                v.RefreshDebuggedDialogues();
                v.HomePage = HomePage.Home;
            }, w => Holder(ByText(Tabs(w), T("home.clearDebugDialogues"))))
            {
                After = v => { if (v.Dialogues.Count > 0) v.Dialogues[0].DebugConditions = false; v.RefreshDebuggedDialogues(); },
            },
            // The tabs: what is not a group named like its heading.
            ["Sections/characters.heading.addingOne"] = new(MainTab.Characters, null, w => Up(Anchor(w, "btn:addCharacter"), 2), 30),
            ["Sections/characters.heading.games"] = new(MainTab.Characters, v =>
            {
                if (v.Characters.FirstOrDefault(c => c.Model.IsVanillaCharacter && !c.IsPlayer && !string.IsNullOrWhiteSpace(c.Model.NameColor)) is { } cop)
                    v.SelectedCharacter = cop;
            }, w => Anchor(w, "panel:characterTree"), 16),
            ["Sections/characters.heading.player"] = new(MainTab.Characters, v =>
            {
                if (v.Characters.FirstOrDefault(c => c.IsPlayer) is { } you) v.SelectedCharacter = you;
            }, w => ByHeading(Tabs(w), "Identity")),
            ["Sections/characters.heading.outfits"] = new(MainTab.Characters, Her, w => Anchor(w, "panel:characterTree"), 16),
            ["Sections/characters.heading.preview"] = new(MainTab.Characters, null, w => Anchor(w, "panel:characterPreview")),
            ["Sections/npcs.heading.list"] = new(MainTab.Npcs, null, w => Up(Anchor(w, "btn:addNpc"), 2), 16),
            ["Sections/npcs.heading.preview"] = new(MainTab.Npcs, null, w => Holder(ByText(Tabs(w), "Preview (live jiggle + shadow + blink)"))),
            ["Sections/places.heading.twoLists"] = new(MainTab.Places, null, w => Anchor(w, "panel:placeList"), 16),
            ["Sections/places.heading.contents"] = new(MainTab.Places, null, w => Anchor(w, "panel:placeHierarchy"), 16),
            ["Sections/places.heading.lights"] = new(MainTab.Places, null, w => Up(Anchor(w, "btn:addLight")), 40),
            ["Sections/places.heading.enterExit"] = new(MainTab.Places, null, w => ByHeading(Tabs(w), "On enter")),
            ["Sections/dialogues.heading.starts"] = new(MainTab.Dialogues, null, w => ByHeading(Tabs(w), "Start conditions")),
            ["Sections/dialogues.heading.gameSConversations"] = new(MainTab.Dialogues, v =>
            {
                if (v.Dialogues.FirstOrDefault(d => d.Model.Source.Length > 0) is { } d) v.SelectedDialogue = d;
            }, w => ByHeading(Tabs(w), "Vanilla conversation")),
            ["Sections/dialogues.heading.selectedNode"] = new(MainTab.Dialogues, null, w => ByHeading(Tabs(w), "Node")),
            ["Sections/dialogues.heading.perNodeConditionsActions"] = new(MainTab.Dialogues, null, w => ByHeading(Tabs(w), "Conditions (gate this node)")),
            ["Sections/scenes.heading.frame"] = new(MainTab.Scenes, null, w => ByHeading(Tabs(w), "Frame, sound and Starmaker")),
            ["Sections/scenes.heading.gameScenes"] = new(MainTab.Scenes, v =>
            {
                if (v.Scenes.FirstOrDefault(x => x.IsGameScene) is { } sc) v.SelectedScene = sc;
            }, w => ByHeading(Tabs(w), "The game's scene")),
            ["Sections/fx.heading.template"] = new(MainTab.Fx, null, w => ByHeading(Tabs(w), "Template")),
            ["Sections/ui.heading.screens"] = new(MainTab.Ui, null, w => Up(Anchor(w, "btn:addUiExtension"), 2), 16),
            ["Sections/ui.heading.object"] = new(MainTab.Ui, null, w => Anchor(w, "panel:uiObjectDetail")),
            ["Sections/ui.heading.whole"] = new(MainTab.Ui, v => { if (v.SelectedUi is { } u) u.SelectedNode = null; }, w => Anchor(w, "panel:uiDetail")),
            ["Sections/variables.heading.refreshing"] = new(MainTab.Variables, null, w => Up(ByText(Tabs(w), T("variables.refreshMode"))), 30),
            ["Sections/integration.heading.whenFires"] = new(MainTab.Integration, null, w => Up(Anchor(w, "field:triggerMode")), 30),
            ["Sections/integration.heading.conditionsActionsBranches"] = new(MainTab.Integration, null, w => Anchor(w, "panel:ruleBranches")),
            ["Sections/quests.heading.twoLists"] = new(MainTab.Quests, null, w => Up(Anchor(w, "btn:addQuest"), 2), 16),
            ["Sections/quests.heading.quest"] = new(MainTab.Quests, PackQuest, w => ByHeading(Tabs(w), "Quest")),
            ["Sections/quests.heading.startsWhen"] = new(MainTab.Quests, PackQuest, w => ByHeading(Tabs(w), "Starts when")),
            ["Sections/quests.heading.resetsWhen"] = new(MainTab.Quests, PackQuest, w => ByHeading(Tabs(w), "Resets when")),
            ["Sections/quests.heading.tasks"] = new(MainTab.Quests, PackQuest, w => ByHeading(Tabs(w), "Tasks")),
            ["Sections/quests.heading.gameSQuests"] = new(MainTab.Quests, v =>
            {
                if (v.Quests.FirstOrDefault(q => q.Model.Source.Length > 0) is { } q) v.SelectedQuest = q;
            }, w => ByHeading(Tabs(w), "The game's quest")),
            ["Sections/mapButtons.addButtonWorldMap"] = new(MainTab.MapButtons, null, w => Anchor(w, "panel:mapButtonList"), 16),
            // Finishing a pack.
            ["Sections/validating.validateF5ChecksWhole"] = new(MainTab.ModForge, Issues, IssuesPage),
            ["Sections/validating.heading.worthKnowing"] = Menu(T("menu.file"), T("menu.validate")),
            ["Sections/exportingInstalling.savesThenBundlesPack"] = Menu(T("menu.file"), T("menu.exportPack")),
            ["Sections/exportingInstalling.heading.worthKnowing"] = new(MainTab.ModForge, v => v.HomePage = HomePage.Home,
                w => Up(ByText(Tabs(w), "Play it"), 2)),
            ["Sections/translations.heading.editor"] = Menu("#LanguageMenu", null),
            ["Sections/translations.heading.packs"] = new(MainTab.ModForge, v => v.HomePage = HomePage.Home, w => Anchor(w, "panel:packLanguage"), 30),
            ["Sections/translations.heading.worthKnowing"] = Menu(T("menu.file"), T("menu.packTranslations")),
        };

        // The conditions and actions: each kind's row where the story pack uses one; the target picker on rows of
        // the kinds that have one.
        foreach (var topic in DocTopics.Parts.SelectMany(p => p.Topics))
        {
            bool picker = topic.TitleKey == "docs.targetPicker.title";
            bool action = topic.TitleKey == "docs.actions.title";
            if (!action && !picker && topic.TitleKey != "docs.conditions.title") continue;
            foreach (var section in topic.Sections)
            {
                var terms = section.Bullets.Select(b => b.Term).Where(t => t.Length > 0).ToList();
                bool asAction = action;
                if (picker)
                {
                    asAction = !section.HeadingKey.EndsWith("worthKnowing", StringComparison.Ordinal);
                    terms = !asAction ? new() { NodeConditionTypes.GameObjectActive }
                          : section.HeadingKey.EndsWith("restRow", StringComparison.Ordinal) ? new() { NodeActionTypes.MoveGameObject }
                          : new() { NodeActionTypes.SetGameObjectActive };
                }
                if (RowSpot(vm, terms, asAction) is { } spot) spots[section.PictureName] = spot;
            }
        }

        // The words a beginner meets: what is on screen, and what makes things happen.
        spots["Sections/words.heading.screen"] = new(MainTab.Places, v => { if (StoryPack.Gallery(v) is { } g) v.SelectedPlace = g; },
                                                     w => Anchor(w, "panel:placePreview"), 16);
        spots["Sections/words.heading.logic"] = new(MainTab.Integration, null, w => Anchor(w, "panel:ruleConditions"));

        // What the examples assume: a character and a place.
        spots["Sections/example.before.s1"] = new(MainTab.Characters, null, w => Up(Anchor(w, "btn:addCharacter"), 2), 30)
        {
            Labels = GuideExamples.Labels("before", 0),
        };
        spots["Sections/example.before.s2"] = new(MainTab.Places, null, w => Anchor(w, "panel:placeList"), 16)
        {
            Labels = GuideExamples.Labels("before", 1),
        };

        // The examples: each step done, then pictured (in order, so each builds on the last).
        foreach (var e in GuideExamples.All)
        {
            var sections = GuideExamples.Topic(e.Id).Sections;
            for (int i = 0; i < e.Steps.Length && i < sections.Count; i++)
                spots[sections[i].PictureName] = new Spot(e.Steps[i].Tab, e.Steps[i].Do, e.Steps[i].Find, e.Steps[i].Around)
                {
                    Labels = GuideExamples.Labels(e.Id, i),
                };
        }
        return spots;
    }

    /// <summary>A brush that draws a visual at exactly its own size. A plain VisualBrush fits the visual's CONTENT
    /// bounds into the rectangle instead, and where content overhangs (a tab's scrolled panel) every word came out
    /// squeezed sideways (the author, 2026-11-21: the Map Buttons page's picture).</summary>
    internal static VisualBrush Exact(Visual v, Size size) => new(v)
    {
        Stretch = Stretch.Fill,
        ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size),
        ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(size),
    };

    /// <summary>The red of a beginner's tutorial picture: a box round what to click, a number, an arrow.</summary>
    internal static readonly Color Red = Color.FromRgb(0xE5, 0x39, 0x35);

    private static Rect? Bounds(FrameworkElement f, FrameworkElement host)
    {
        try { return f.TransformToAncestor(host).TransformBounds(new Rect(f.RenderSize)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>The label's row: the label and its box beside it in a grid row, or the short panel they share.</summary>
    private static Rect? RowOf(FrameworkElement label, FrameworkElement host)
    {
        // A label inside a box (a dropdown's chosen text, a button's words) is that box.
        for (DependencyObject? d = label; d != null && d != host; d = VisualTreeHelper.GetParent(d))
            if (d is ComboBox or ButtonBase or TextBox && d is FrameworkElement box) return Bounds(box, host);
        FrameworkElement cell = label;
        for (int i = 0; i < 3 && VisualTreeHelper.GetParent(cell) is FrameworkElement up && up is not Grid && up is not Panel; i++) cell = up;
        if (VisualTreeHelper.GetParent(cell) is Grid g && g.RowDefinitions.Count > 1)
        {
            int row = Grid.GetRow(cell);
            Rect? all = null;
            foreach (var c in g.Children.OfType<FrameworkElement>())
                if (Grid.GetRow(c) == row && Shown(c) && Bounds(c, host) is { } b) all = all is { } a ? Rect.Union(a, b) : b;
            return all;
        }
        if (VisualTreeHelper.GetParent(cell) is Panel p and (StackPanel { Orientation: Orientation.Horizontal } or DockPanel or WrapPanel)
            && p.ActualHeight < 70 && Shown(p))
            return Bounds(p, host);
        return Bounds(label, host);
    }

    private static IEnumerable<string> Spellings(string label)
    {
        foreach (string piece in label.Split(" / "))
        {
            string p = piece.Trim();
            if (p.Length == 0) continue;
            yield return p;
            int paren = p.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && p.EndsWith(')')) yield return p[..paren];
        }
    }

    /// <summary>Where on screen a label of the guide's is, inside a part: a button, a tick, a group, or a label with
    /// its box. One rectangle per piece of a label like "+ Folder / Rename / - Remove".</summary>
    internal static IEnumerable<Rect> Find(FrameworkElement within, FrameworkElement host, string label, bool exactOnly = false)
    {
        var all = Below(within).OfType<FrameworkElement>().Where(Shown).ToList();
        foreach (string want in Spellings(label))
        {
            string w = want.Replace("−", "-");
            bool Is(string text) => Same(text.Replace("_", "").Replace("−", "-"), w);
            bool Starts(string text) => Names(text.Replace("_", "").Replace("−", "-"), w);
            FrameworkElement? hit = (FrameworkElement?)all.OfType<ButtonBase>().FirstOrDefault(b => Is(HeaderText(b.Content)))
                ?? (FrameworkElement?)all.OfType<HeaderedContentControl>().FirstOrDefault(h => h is not TabItem && Is(HeaderText(h.Header)))
                ?? all.OfType<TextBlock>().FirstOrDefault(t => Is(t.Text))
                ?? (exactOnly ? null : (FrameworkElement?)all.OfType<ButtonBase>().FirstOrDefault(b => Starts(HeaderText(b.Content))))
                ?? (exactOnly ? null : all.OfType<TextBlock>().FirstOrDefault(t => Starts(t.Text)));
            if (hit == null) continue;
            Rect? r = hit is TextBlock tb ? RowOf(tb, host) : Bounds(hit, host);
            if (r is { } found && found.Width > 2 && found.Height > 2) yield return found;
        }
    }

    private static void Badge(DrawingContext dc, Point at, int number)
    {
        var red = new SolidColorBrush(Red);
        dc.DrawEllipse(Brushes.White, null, at, 12, 12);
        dc.DrawEllipse(red, null, at, 10.5, 10.5);
        var text = new FormattedText(number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 13, Brushes.White, 1.0);
        dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
    }

    /// <summary>A thick red arrow ending at the target's edge, from a point the crop has room for; false when none.</summary>
    private static bool Arrow(DrawingContext dc, Rect target, Rect crop)
    {
        const double len = 70, pad = 6;
        var tries = new (Point From, Point To)[]
        {
            (new(target.Left - len, target.Top - len * 0.55), new(target.Left - 3, target.Top + Math.Min(12, target.Height / 2))),
            (new(target.Right + len, target.Top - len * 0.55), new(target.Right + 3, target.Top + Math.Min(12, target.Height / 2))),
            (new(target.Left - len, target.Bottom + len * 0.55), new(target.Left - 3, target.Bottom - Math.Min(12, target.Height / 2))),
            (new(target.Right + len, target.Bottom + len * 0.55), new(target.Right + 3, target.Bottom - Math.Min(12, target.Height / 2))),
        };
        var room = crop;
        room.Inflate(-pad, -pad);
        foreach (var (from, to) in tries)
        {
            if (!room.Contains(from)) continue;
            var v = to - from;
            v.Normalize();
            var n = new Vector(-v.Y, v.X);
            var tip = to;
            var baseMid = tip - v * 16;
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(tip, true, true);
                g.LineTo(baseMid + n * 9, true, false);
                g.LineTo(baseMid - n * 9, true, false);
            }
            head.Freeze();
            dc.DrawLine(new Pen(Brushes.White, 8) { StartLineCap = PenLineCap.Round }, from, baseMid);
            dc.DrawGeometry(Brushes.White, new Pen(Brushes.White, 4) { LineJoin = PenLineJoin.Round }, head);
            dc.DrawLine(new Pen(new SolidColorBrush(Red), 4.5) { StartLineCap = PenLineCap.Round }, from, baseMid);
            dc.DrawGeometry(new SolidColorBrush(Red), null, head);
            return true;
        }
        return false;
    }

    private static void Marks(DrawingContext dc, IReadOnlyList<(Rect R, int N)> marks, bool numbered, bool arrow, Rect crop)
    {
        var red = new Pen(new SolidColorBrush(Red), 2.5);
        var white = new Pen(Brushes.White, 5);
        foreach (var (m0, _) in marks)
        {
            var m = m0;
            m.Inflate(3, 3);
            dc.DrawRoundedRectangle(null, white, m, 4, 4);
            dc.DrawRoundedRectangle(null, red, m, 4, 4);
        }
        if (arrow && marks.Count > 0)
        {
            var first = marks[0].R;
            first.Inflate(3, 3);
            Arrow(dc, first, crop);
        }
        if (!numbered) return;
        foreach (var (m, n) in marks)
        {
            if (n <= 0) continue;
            var at = new Point(Math.Clamp(m.Left - 15, crop.Left + 13, crop.Right - 13),
                               Math.Clamp(m.Top + Math.Min(11, m.Height / 2), crop.Top + 13, crop.Bottom - 13));
            Badge(dc, at, n);
        }
    }

    private static void Write(DrawingVisual dv, Rect crop, string file)
    {
        var bmp = new RenderTargetBitmap((int)crop.Width, (int)crop.Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var f = File.Create(file);
        png.Save(f);
    }

    /// <summary>A menu of the window's, open, with an item boxed in red and an arrow at it: the menu bar above it,
    /// as a person sees it.</summary>
    internal static bool MenuPicture(MainWindow window, string menuHeader, string? itemHeader, string file)
    {
        var host = (FrameworkElement)window.Content;
        var top = menuHeader.StartsWith('#') ? window.FindName(menuHeader[1..]) as MenuItem
                : Below(window).OfType<MenuItem>().FirstOrDefault(m => m.Parent is Menu && Shown(m) && Same(HeaderText(m.Header), menuHeader));
        if (top == null) return false;
        top.IsSubmenuOpen = true;
        WindowHarness.Pump();
        top.ApplyTemplate();
        if (top.Template.FindName("PART_Popup", top) is not Popup { Child: FrameworkElement content }) return false;
        content.Measure(new Size(1200, 2000));
        content.Arrange(new Rect(content.DesiredSize));
        content.UpdateLayout();
        WindowHarness.Pump();
        MenuItem? item = null;
        if (itemHeader != null)
            item = top.Items.Cast<object>().Select(i => top.ItemContainerGenerator.ContainerFromItem(i)).OfType<MenuItem>()
                      .FirstOrDefault(m => Same(HeaderText(m.Header).Replace("_", ""), itemHeader.Replace("_", "")));
        var bar = top.TransformToAncestor(host).TransformBounds(new Rect(top.RenderSize));
        Rect? marked = null;
        if (item != null)
            try { marked = item.TransformToAncestor(content).TransformBounds(new Rect(item.RenderSize)); } catch (InvalidOperationException) { }

        // Room on the right for the arrow pointing back at the item.
        double width = Math.Ceiling(Math.Max(content.ActualWidth, bar.Width) + 110);
        double x0 = Math.Max(0, bar.X - 12);
        double barTop = Math.Max(0, bar.Y - 6), barBottom = bar.Bottom;
        // A long menu is cut to the part around the marked item.
        double from = 0, to = content.ActualHeight;
        const double tallest = 380;
        if (to > tallest)
        {
            double middle = marked is { } m0 ? m0.Y + m0.Height / 2 : 0;
            from = Math.Clamp(middle - tallest / 2, 0, to - tallest);
            to = from + tallest;
        }
        double height = Math.Ceiling(barBottom - barTop + (to - from) + 14);
        var surface = (window.TryFindResource("Theme.Surface") as SolidColorBrush)?.Color ?? Colors.White;
        var crop = new Rect(0, 0, width, height);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(surface), null, crop);
            // The menu bar.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, barBottom - barTop)));
            dc.PushTransform(new TranslateTransform(-x0, -barTop));
            dc.DrawRectangle(Exact(host, host.RenderSize), null, new Rect(host.RenderSize));
            dc.Pop(); dc.Pop();
            // The menu under its header.
            double mx = bar.X - x0, my = barBottom - barTop;
            dc.PushClip(new RectangleGeometry(new Rect(mx, my, content.ActualWidth, to - from)));
            dc.PushTransform(new TranslateTransform(mx, my - from));
            dc.DrawRectangle(Exact(content, content.RenderSize), null, new Rect(content.RenderSize));
            dc.Pop(); dc.Pop();
            var target = marked is { } m
                ? new Rect(mx + m.X + 1, my + m.Y - from, m.Width - 2, m.Height)
                : new Rect(bar.X - x0, bar.Y - barTop, bar.Width, bar.Height);
            Marks(dc, new[] { (target, 0) }, numbered: false, arrow: true, crop);
        }
        top.IsSubmenuOpen = false;
        WindowHarness.Pump();
        Write(dv, crop, file);
        return true;
    }

    /// <summary>Puts the part on screen and writes it: what is around it dimmed, and the controls the text names boxed
    /// in red - numbered when there are several, an arrow when one is the thing to click.</summary>
    /// <summary>Where the last picture was cut from the window: the report says which pictures hold the game's HUD.</summary>
    internal static Rect LastCrop { get; private set; }

    /// <summary>Whether the last picture shows a place preview with the game's HUD drawn on it.</summary>
    private static bool HudInLastPicture(MainWindow window)
    {
        var host = (FrameworkElement)window.Content;
        return Below(window).OfType<View.Controls.PlacePreview>()
                            .Any(p => Shown(p) && p.HudDrawn && Bounds(p, host) is { } b && b.IntersectsWith(LastCrop));
    }

    internal static bool Picture(MainWindow window, FrameworkElement part, string file, double around,
                                 IReadOnlyList<(Rect R, int N)>? marks = null, bool numbered = true, bool arrow = false)
    {
        part.BringIntoView();
        WindowHarness.Wait(TimeSpan.FromMilliseconds(250));
        window.UpdateLayout();
        WindowHarness.Pump();
        if (!Shown(part)) return false;
        var host = (FrameworkElement)window.Content;
        if (Bounds(part, host) is not { } r) return false;
        var whole = new Rect(host.RenderSize);
        r.Intersect(whole);
        if (r.IsEmpty || r.Width < 4 || r.Height < 4) return false;
        marks ??= Array.Empty<(Rect R, int N)>();
        var crop = r;
        crop.Inflate(around, around);
        if (marks.Count > 0) crop = new Rect(crop.X - 14, crop.Y, crop.Width + 14, crop.Height);
        // An arrow needs room beside its target.
        if (arrow && marks.Count > 0)
        {
            var reach = marks[0].R;
            reach.Inflate(84, 50);
            crop.Union(reach);
        }
        crop.Intersect(whole);
        const double tallest = 460;
        bool cut = crop.Height > tallest;
        if (cut)
        {
            // Keep the first mark in the picture when the part is too tall for it.
            double top = marks.Count > 0 ? Math.Clamp(marks[0].R.Top - 60, crop.Top, crop.Bottom - tallest) : crop.Top;
            crop = new Rect(crop.X, top, crop.Width, tallest);
        }
        crop = new Rect(Math.Floor(crop.X), Math.Floor(crop.Y), Math.Ceiling(crop.Width), Math.Ceiling(crop.Height));
        LastCrop = crop;

        var surface = (window.TryFindResource("Theme.Surface") as SolidColorBrush)?.Color ?? Colors.White;
        var accent = (window.TryFindResource("Theme.Accent") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            dc.DrawRectangle(new SolidColorBrush(surface), null, whole);
            dc.DrawRectangle(Exact(host, host.RenderSize), null, whole);
            var frame = r;
            frame.Inflate(2, 2);
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(crop), new RectangleGeometry(frame, 3, 3));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(130, surface.R, surface.G, surface.B)), null, outside);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(accent), 1.5), frame, 3, 3);
            Marks(dc, marks, numbered && marks.Select(m => m.N).Distinct().Count() > 1, arrow, crop);
            if (cut)
            {
                var fade = new Rect(crop.X, crop.Bottom - 34, crop.Width, 34);
                dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, surface.R, surface.G, surface.B), surface, 90), null, fade);
            }
            dc.Pop();
        }
        Write(dv, new Rect(0, 0, crop.Width, crop.Height), file);
        return true;
    }

    [Fact]
    public void TakeThem()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_GUIDE_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        var report = new List<string>();
        WindowHarness.Run(window =>
        {
            // Narrower than the pages' own pictures: a group as wide as the tab would be shrunk to half its size to
            // fit the reader, and its words with it.
            window.Width = 1180; window.Height = 900;
            var vm = (MainViewModel)window.DataContext;
            using var walker = new TutorialWalker(vm);
            var last = TutorialCatalog.All.Last(t => t.IsOnLadder);
            StoryPack.BringIn(vm, last);
            StoryPack.Replay(vm, last);
            Services.PreviewBackdropManager.Apply(Services.PreviewBackdropManager.All.First(b => b.IsNone));
            string look = Services.ThemeManager.Current.IsEx ? ".EX" : "";
            var picks = GuidePictureShots.Picks(vm).ToDictionary(p => p.Topic);
            var spots = Spots(vm);
            if (Environment.GetEnvironmentVariable("SMSMODFORGE_GUIDE_DUMP") == "1")
                foreach (var (tab, topic, pick) in picks.Values)
                {
                    vm.SelectedTabIndex = tab;
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    pick();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(700));
                    window.UpdateLayout();
                    var tabs = (TabControl)window.FindName("MainTabs");
                    report.Add($"== {topic}");
                    foreach (var h in Below(tabs).OfType<HeaderedContentControl>())
                        if (HeaderText(h.Header) is { Length: > 0 } t && h is not TabItem)
                            report.Add($"   {(Shown(h) ? "shown" : "     ")} {h.GetType().Name} \"{t}\" {h.Name}");
                    foreach (var f in Below(tabs).OfType<FrameworkElement>())
                        if (View.TutorialAnchor.GetId(f) is { } id)
                            report.Add($"   {(Shown(f) ? "shown" : "     ")} anchor {id}");
                    foreach (var t in Below(tabs).OfType<TextBlock>())
                        if (Shown(t) && t.FontWeight.ToOpenTypeWeight() >= 600 && t.Text.Length is > 2 and < 40)
                            report.Add($"   shown bold \"{t.Text}\"");
                }

            // Which bullet each numbered box is, for the reader to show the same number beside it.
            var numbers = new Dictionary<string, int>(StringComparer.Ordinal);

            // The controls to box: the labels a spot names (an example's step, in the order they are used), or the
            // section's bullets' own labels - found in the part, or close beside it on its tab.
            List<(Rect R, int N)> MarksFor(FrameworkElement part, Spot? spot, DocSection? section, out bool arrow)
            {
                var host = (FrameworkElement)window.Content;
                var near = Bounds(part, host) ?? Rect.Empty;
                near.Inflate(420, 160);
                IEnumerable<Rect> Look(string label)
                {
                    var inside = Find(part, host, label).ToList();
                    if (inside.Count > 0) return inside;
                    return Find(Tabs(window), host, label, exactOnly: true).Where(r => near.Contains(r)).Take(1);
                }
                var marks = new List<(Rect, int)>();
                arrow = false;
                if (spot?.Labels is { Length: > 0 } labels)
                {
                    int n = 0;
                    foreach (string l in labels)
                        if (Look(l).FirstOrDefault() is { IsEmpty: false } r) marks.Add((r, ++n));
                    arrow = marks.Count > 0;
                    return marks;
                }
                if (section == null) return marks;
                int number = 0;
                var keys = new List<string>();
                foreach (var b in section.Bullets)
                {
                    if (b.TermKey.Length == 0 || marks.Count >= 9) continue;
                    var found = Find(part, host, b.Term).Take(4).ToList();
                    if (found.Count == 0) continue;
                    number++;
                    keys.Add(b.TextKey);
                    foreach (var r in found) marks.Add((r, number));
                }
                if (number > 1)
                    for (int i = 0; i < keys.Count; i++) numbers[keys[i]] = i + 1;
                arrow = number == 1;
                return marks;
            }

            string? only = Environment.GetEnvironmentVariable("SMSMODFORGE_GUIDE_ONLY");
            void Take(string name, DocSection? section, (int Tab, string Topic, Action Pick) page)
            {
                if (!string.IsNullOrEmpty(only) && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
                string heading = section is { HasHeading: true } ? section.Heading : "";
                string file = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar) + look + ".png");
                FrameworkElement? part = null;
                double around = 28;
                bool studio = false;
                if (spots.TryGetValue(name, out var spot))
                {
                    if (spot.Menu != null)
                    {
                        bool shot = MenuPicture(window, spot.Menu, spot.MenuItem, file);
                        report.Add((shot ? "OK     " : "MISS   ") + $"{name}  menu {spot.Menu} > {spot.MenuItem}");
                        return;
                    }
                    if (spot.Tab >= 0) vm.SelectedTabIndex = spot.Tab;
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    if (page.Pick != null && spot.Tab == page.Tab) page.Pick();
                    spot.Setup?.Invoke(vm);
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(700));
                    window.UpdateLayout();
                    if (spot.Studio && Anchor(window, "btn:editMask") is ButtonBase edit)
                    {
                        edit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        WindowHarness.Wait(TimeSpan.FromMilliseconds(1200));
                        window.UpdateLayout();
                        studio = true;
                    }
                    part = spot.Find(window);
                    around = spot.Around;
                }
                else if (page.Pick != null && heading.Length > 0)
                {
                    vm.SelectedTabIndex = page.Tab;
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    page.Pick();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(700));
                    window.UpdateLayout();
                    part = ByHeading(Tabs(window), heading);
                }
                else { report.Add($"NONE   {name}  ({(page.Pick == null ? "no tab" : "no heading")})"); return; }

                if (part == null) report.Add($"MISS   {name}  \"{heading}\"");
                else
                {
                    part.BringIntoView();
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(250));
                    window.UpdateLayout();
                    var marks = MarksFor(part, spot, section, out bool arrow);
                    bool ok = Picture(window, part, file, around, marks, numbered: true, arrow);
                    report.Add((ok ? "OK     " : "HIDDEN ") + $"{name}  {part.GetType().Name} {part.ActualWidth:0}x{part.ActualHeight:0}  marks {marks.Count}"
                               + (ok && HudInLastPicture(window) ? "  HUD" : ""));
                }
                if (studio) window.CurrentMaskStudio?.TryClose();
                if (spot?.After != null) { spot.After(vm); WindowHarness.Pump(); }
            }

            foreach (var topic in DocTopics.Parts.SelectMany(p => p.Topics))
            {
                picks.TryGetValue(topic.PictureName, out var page);
                if (spots.ContainsKey(topic.PictureName)) Take(topic.PictureName, null, page);
                foreach (var section in topic.Sections)
                    Take(section.PictureName, section, page);
            }
            // A partial run keeps the whole run's numbers: it would write only its own.
            if (string.IsNullOrEmpty(only))
                File.WriteAllText(Path.Combine(dir, "Sections", "marks" + look + ".json"),
                              Newtonsoft.Json.JsonConvert.SerializeObject(numbers, Newtonsoft.Json.Formatting.Indented));
            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "sections-report" + (Services.ThemeManager.Current.IsEx ? ".EX" : "") + ".txt"), report);
        foreach (var l in report) _out.WriteLine(l);
    }
}
