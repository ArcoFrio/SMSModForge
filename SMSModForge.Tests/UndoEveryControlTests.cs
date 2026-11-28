using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A pack with every kind of record in it, for the walk over every control:
/// the packs the in-game suite is made of, put together (each list's entries
/// by their keys), and a lamp of the pack's own.
/// </summary>
internal static class UndoCrawlPack
{
    public static ModPack Make(string folder)
    {
        var pack = InGameSuitePacks.Story.Make(folder);
        foreach (var other in new[]
                 {
                     InGameSuitePacks.World.Make(folder), InGameSuitePacks.Quests.Make(), InGameSuitePacks.Rules.Make(),
                     InGameSuitePacks.Talk.Make(folder), InGameSuitePacks.Looks.Make(folder), InGameSuitePacks.Saving.Make(),
                     InGameSuitePacks.Effects.Make(folder),
                     InGameCheckPacksTests.GameCheck(folder),
                 })
            Merge(pack, other);
        pack.PackId = "undocrawl";
        // A folder in every list that has them: a row of its own in that list's tree. Found by looking, so a
        // list given folders later has one too (UndoNeedsNothingNewTests holds the pack to having a record of
        // every kind).
        foreach (var p in typeof(ModPack).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.Name.EndsWith("Folders", StringComparison.Ordinal) || p.GetValue(pack) is not IList { Count: 0 } folders) continue;
            var kind = p.PropertyType.GetGenericArguments()[0];
            object apart = Activator.CreateInstance(kind)!;
            kind.GetProperty("Name")?.SetValue(apart, "Kept apart");
            folders.Add(apart);
        }
        if (pack.Places.Count > 0)
            pack.Places[0].GameObjects.Add(new GameObjectDef { Name = "Lamp", X = 2, Y = 1, Light = new LightDef { Strength = 0.8f, Color = "#FFCC88" } });
        return pack;
    }

    private static string? KeyOf(object entry)
    {
        foreach (string name in new[] { "Key", "Name", "Source", "Id" })
            if (entry.GetType().GetProperty(name)?.GetValue(entry) is string key) return name + "=" + key;
        return null;
    }

    private static void Merge(ModPack into, ModPack from)
    {
        foreach (var p in typeof(ModPack).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetValue(into) is not IList mine || p.GetValue(from) is not IList theirs) continue;
            var have = new HashSet<string>(mine.Cast<object>().Select(KeyOf).Where(k => k != null)!, StringComparer.Ordinal);
            foreach (object entry in theirs)
            {
                string? key = KeyOf(entry);
                if (key != null && !have.Add(key)) continue;
                if (key == null && mine.Contains(entry)) continue;
                mine.Add(entry);
            }
        }
    }
}

/// <summary>
/// Undo and redo held to every control of every tab (see
/// <see cref="UndoCrawler"/>), on a pack with every kind of record. One class a
/// group of tabs, so the runner's testers share them out.
/// <para/>
/// <c>SMSMODFORGE_UNDO_CENSUS=1</c> lists what would be edited and edits nothing.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public abstract class UndoEveryControlBase : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-undocrawl-" + Guid.NewGuid().ToString("N"));

    protected UndoEveryControlBase(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>What leaves the pack or the editor, or only shows something: never pressed.</summary>
    private static bool Leaves(UndoCrawler.Target t)
    {
        if (t.Kind != "button") return false;
        // A button with a handler of the window's own and no command is a picker, the mask studio, another
        // window, a question. Those a control of the editor's builds for itself (the previews' bars and panels)
        // are pressed. Asked of the handler itself: in EX every panel is framed by one of the editor's own controls
        // (ChamferBorder), and the window's own buttons inside them were pressed - the mask studio opened, a colour
        // wheel and a question waited for a click (1.10.0).
        var button = (System.Windows.Controls.Primitives.ButtonBase)t.Control;
        if (button.Command == null && (ClickOwner(button) is not { } owner || typeof(System.Windows.Window).IsAssignableFrom(owner)
                                       || !InsideAControlOfTheEditors(t.Control))) return true;
        string s = t.Signature;
        if (s.Contains("StartTutorialCommand", StringComparison.Ordinal) || s.Contains("[ValidateCommand]", StringComparison.Ordinal)) return true;
        return s.Contains("[SavePack", StringComparison.Ordinal) || s.Contains("[ExportPack", StringComparison.Ordinal)
            || s.Contains("[NewPack", StringComparison.Ordinal) || s.Contains("[OpenPack", StringComparison.Ordinal)
            || s.Contains("[Publish", StringComparison.Ordinal) || s.Contains("Tutorial", StringComparison.Ordinal)
            || s.Contains("[Undo", StringComparison.Ordinal) || s.Contains("[Redo", StringComparison.Ordinal)
            || s.Contains("Update", StringComparison.Ordinal) && s.Contains("MainViewModel", StringComparison.Ordinal) && s.Contains("Check", StringComparison.Ordinal);
    }

    /// <summary>The type whose code a button's Click runs: the instance its first handler is on, or - a lambda's
    /// closure - the type that wrote the lambda. Null for a button with no Click handler.</summary>
    private static Type? ClickOwner(System.Windows.Controls.Primitives.ButtonBase button)
    {
        var store = typeof(System.Windows.UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(button);
        var get = store?.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (get?.Invoke(store, new object[] { System.Windows.Controls.Primitives.ButtonBase.ClickEvent }) is not System.Windows.RoutedEventHandlerInfo[] { Length: > 0 } handlers)
            return null;
        var target = handlers[0].Handler.Target;
        var type = target?.GetType() ?? handlers[0].Handler.Method.DeclaringType;
        while (type != null && type.Name.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null) type = type.DeclaringType;
        return type;
    }

    private static bool InsideAControlOfTheEditors(System.Windows.DependencyObject d)
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(d); p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p.GetType().Namespace == "SMSModForge.View.Controls") return true;
        return false;
    }

    private protected void Crawl(Action<UndoCrawler, MainViewModel> walk)
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        var failures = new List<string>();
        string summary = "";
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            var crawler = new UndoCrawler(window, _dir, _out)
            {
                CensusOnly = Environment.GetEnvironmentVariable("SMSMODFORGE_UNDO_CENSUS") == "1",
                Leave = Leaves,
            };
            try { walk(crawler, vm); }
            finally { crawler.Done(); }
            failures.AddRange(crawler.Failures);
            summary = $"{crawler.Edited} controls edited and held to undo and redo, {crawler.Unchanged} that edit nothing of the pack, {crawler.Skipped} left alone; "
                      + "put back by: " + string.Join(", ", crawler.Ways.Select(w => w.Key + " " + w.Value))
                      + $"; {crawler.AnsweredByTheBox} presses answered by a box itself";
            // Whatever the walk left unsaved is not asked about as the window closes.
            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
        _out.WriteLine(summary);
        Assert.True(failures.Count == 0, failures.Count + " controls did not undo or redo as they should:\n" + string.Join("\n", failures.Take(60)));
    }

    /// <summary>What a record is made of - its fields' names and the types of what it holds - so the walk opens
    /// one of each kind and not every one.</summary>
    protected static string ShapeOf(object model)
    {
        string text = ModelAssign.Written(model);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, "\"(\\w+)\":")) names.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(text, "\"type\":\\s*\"(\\w+)\"")) names.Add("=" + m.Groups[1].Value);
        return string.Join(",", names);
    }
}

public sealed class UndoEveryControl_PackAndPeopleTests : UndoEveryControlBase
{
    public UndoEveryControl_PackAndPeopleTests(ITestOutputHelper o) : base(o) { }

    [Fact]
    public void ThePacksOwnPage() => Crawl((crawl, vm) => crawl.Explore(MainTab.ModForge, "the pack", () => { }));

    [Fact]
    public void CharactersAndTheirOutfits() => Crawl((crawl, vm) =>
    {
        for (int i = 0; i < Math.Min(vm.Characters.Count, 3); i++)
        {
            int c = i;
            crawl.Explore(MainTab.Characters, "character " + c, () => vm.SelectedCharacter = vm.Characters[c]);
            for (int j = 0; j < Math.Min(vm.Characters[c].Outfits.Count, 2); j++)
            {
                int o = j;
                crawl.Explore(MainTab.Characters, $"character {c}, outfit {o}", () =>
                {
                    vm.SelectedCharacter = vm.Characters[c];
                    WindowHarness.Pump();
                    vm.SelectedOutfit = vm.Characters[c].Outfits[o];
                });
            }
        }
    });

    [Fact]
    public void Npcs() => Crawl((crawl, vm) =>
    {
        for (int i = 0; i < Math.Min(vm.Npcs.Count, 3); i++)
        {
            int n = i;
            crawl.Explore(MainTab.Npcs, "NPC " + n, () => vm.SelectedNpc = vm.Npcs[n]);
        }
    });
}

public sealed class UndoEveryControl_PlacesTests : UndoEveryControlBase
{
    public UndoEveryControl_PlacesTests(ITestOutputHelper o) : base(o) { }

    [Fact]
    public void PlacesOfThePack() => Crawl((crawl, vm) =>
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < vm.Places.Count; i++)
        {
            int p = i;
            if (p < 4) crawl.Explore(MainTab.Places, "place " + p, () => vm.SelectedPlace = vm.Places[p]);
            ExplorePicked(crawl, "place " + p, () => vm.SelectedPlace = vm.Places[p], () => vm.Places[p].GameObjects, "PlacePreviewMain", shapes);
        }
    });

    [Fact]
    public void LevelsOfTheGameExtended() => Crawl((crawl, vm) =>
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < vm.VanillaExtensions.Count; i++)
        {
            int e = i;
            if (e < 2) crawl.Explore(MainTab.Places, "extension " + e, () => vm.SelectedVanillaExtension = vm.VanillaExtensions[e]);
            ExplorePicked(crawl, "extension " + e, () => vm.SelectedVanillaExtension = vm.VanillaExtensions[e],
                          () => vm.VanillaExtensions[e].GameObjects, "ExtPreview", shapes);
        }
    });

    /// <summary>A place's objects and the characters standing in it, its whole tree, in order.</summary>
    private static IEnumerable<object> Rows(IEnumerable<GameObjectViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var pl in n.Npcs)
            {
                yield return pl;
                foreach (var c in Rows(pl.Children)) yield return c;
            }
            foreach (var c in Rows(n.Children)) yield return c;
        }
    }

    /// <summary>The most picks a test makes: one of each shape of object and character is plenty.</summary>
    private const int MostPicks = 40;

    /// <summary>
    /// Each object and character of <paramref name="tree"/> of a shape not met yet, picked in the place preview as a
    /// click picks it, and the panel beside the picture then walked: its settings, a character's part transforms and
    /// wet effect, an object's light and effect. That panel took over from the GameObjects list under the preview
    /// (after 1.10.0) and the walk, which picked nothing, stopped meeting its controls - the wet effect's included.
    /// The row is found again by its place in the tree each time: an undo makes rows anew.
    /// </summary>
    private static void ExplorePicked(UndoCrawler crawl, string where, Action select, Func<IEnumerable<GameObjectViewModel>> tree,
                                      string previewName, HashSet<string> shapes)
    {
        var rows = Rows(tree()).ToList();
        for (int k = 0; k < rows.Count && shapes.Count < MostPicks; k++)
        {
            object model = rows[k] switch
            {
                GameObjectViewModel g => g.Model,
                NpcPlacementViewModel pl => pl.Model,
                _ => rows[k],
            };
            if (!shapes.Add(rows[k].GetType().Name + ":" + ShapeOf(model))) continue;
            int r = k;
            string what = rows[k] is NpcPlacementViewModel ? "character" : "object";
            crawl.Explore(MainTab.Places, $"{where}, {what} {r} picked", () =>
            {
                select();
                WindowHarness.Pump();
                var row = Rows(tree()).ElementAtOrDefault(r);
                var preview = (SMSModForge.View.Controls.PlacePreview)crawl.Window.FindName(previewName);
                Assert.True(row != null && preview.Pick(row), $"{where}: {what} {r} is not in the preview to pick");
            });
        }
    }

    [Fact]
    public void MapButtons() => Crawl((crawl, vm) =>
    {
        for (int i = 0; i < Math.Min(vm.MapButtons.Count, 2); i++)
        {
            int b = i;
            crawl.Explore(MainTab.MapButtons, "map button " + b, () => vm.SelectedMapButton = vm.MapButtons[b]);
        }
    });
}

public sealed class UndoEveryControl_DialoguesTests : UndoEveryControlBase
{
    public UndoEveryControl_DialoguesTests(ITestOutputHelper o) : base(o) { }

    [Fact]
    public void DialoguesAndTheirLines() => Crawl((crawl, vm) =>
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < vm.Dialogues.Count; i++)
        {
            int d = i;
            var mine = new List<int>();
            for (int k = 0; k < vm.Dialogues[d].Nodes.Count && mine.Count < 6; k++)
                if (shapes.Add(ShapeOf(vm.Dialogues[d].Nodes[k].Model))) mine.Add(k);
            if (mine.Count == 0 && d > 2) continue;
            crawl.Explore(MainTab.Dialogues, "dialogue " + d, () => vm.SelectedDialogue = vm.Dialogues[d]);
            foreach (int k in mine)
            {
                int n = k;
                crawl.Explore(MainTab.Dialogues, $"dialogue {d}, line {n}", () =>
                {
                    vm.SelectedDialogue = vm.Dialogues[d];
                    WindowHarness.Pump();
                    vm.SelectedNode = vm.Dialogues[d].Nodes[n];
                });
            }
        }
    });
}

public sealed class UndoEveryControl_MediaAndUiTests : UndoEveryControlBase
{
    public UndoEveryControl_MediaAndUiTests(ITestOutputHelper o) : base(o) { }

    [Fact]
    public void ScenesMusicSoundsAndWallpapers() => Crawl((crawl, vm) =>
    {
        for (int i = 0; i < Math.Min(vm.Scenes.Count, 2); i++) { int k = i; crawl.Explore(MainTab.Scenes, "scene " + k, () => vm.SelectedScene = vm.Scenes[k]); }
        for (int i = 0; i < Math.Min(vm.Music.Count, 2); i++) { int k = i; crawl.Explore(MainTab.Music, "music " + k, () => vm.SelectedMusic = vm.Music[k]); }
        for (int i = 0; i < Math.Min(vm.Sfx.Count, 2); i++) { int k = i; crawl.Explore(MainTab.Sfx, "sound " + k, () => vm.SelectedSfx = vm.Sfx[k]); }
        for (int i = 0; i < Math.Min(vm.Wallpapers.Count, 2); i++) { int k = i; crawl.Explore(MainTab.Wallpapers, "wallpaper " + k, () => vm.SelectedWallpaper = vm.Wallpapers[k]); }
        if (vm.Scenes.Count == 0) crawl.Explore(MainTab.Scenes, "no scene", () => { });
        if (vm.Music.Count == 0) crawl.Explore(MainTab.Music, "no music", () => { });
        if (vm.Sfx.Count == 0) crawl.Explore(MainTab.Sfx, "no sound", () => { });
        if (vm.Wallpapers.Count == 0) crawl.Explore(MainTab.Wallpapers, "no wallpaper", () => { });
        // The FX tab: each effect shows the rows of its own template (particles, the points, floating words).
        for (int i = 0; i < Math.Min(vm.Effects.Count, 5); i++) { int k = i; crawl.Explore(MainTab.Fx, "effect " + k, () => vm.SelectedEffect = vm.Effects[k]); }
        if (vm.Effects.Count == 0) crawl.Explore(MainTab.Fx, "no effect", () => { });
    });

    private static IEnumerable<UiNodeViewModel> Flat(IEnumerable<UiNodeViewModel> nodes)
    {
        foreach (var n in nodes) { yield return n; foreach (var c in Flat(n.Children)) yield return c; }
    }

    [Fact]
    public void ScreensAndWhatIsOnThem() => Crawl((crawl, vm) =>
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Math.Min(vm.Uis.Count, 4); i++)
        {
            int u = i;
            crawl.Explore(MainTab.Ui, "screen " + u, () => vm.SelectedUi = vm.Uis[u]);
            var all = Flat(vm.Uis[u].Nodes).ToList();
            var mine = new List<int>();
            for (int k = 0; k < all.Count && mine.Count < 8; k++)
                if (shapes.Add(ShapeOf(all[k].Model))) mine.Add(k);
            foreach (int k in mine)
            {
                int n = k;
                crawl.Explore(MainTab.Ui, $"screen {u}, node {n}", () =>
                {
                    vm.SelectedUi = vm.Uis[u];
                    WindowHarness.Pump();
                    vm.Uis[u].SelectedNode = Flat(vm.Uis[u].Nodes).ElementAt(n);
                });
            }
        }
    });
}

public sealed class UndoEveryControl_QuestsVariablesAndRulesTests : UndoEveryControlBase
{
    public UndoEveryControl_QuestsVariablesAndRulesTests(ITestOutputHelper o) : base(o) { }

    [Fact]
    public void QuestsAndTheirTasks() => Crawl((crawl, vm) =>
    {
        for (int i = 0; i < Math.Min(vm.Quests.Count, 4); i++)
        {
            int q = i;
            crawl.Explore(MainTab.Quests, "quest " + q, () =>
            {
                if (vm.Quests[q].ShowsVanillaPanel) vm.SelectedVanillaQuest = vm.Quests[q]; else vm.SelectedQuest = vm.Quests[q];
            });
            for (int j = 0; j < Math.Min(vm.Quests[q].TaskRows.Count, 2); j++)
            {
                int t = j;
                crawl.Explore(MainTab.Quests, $"quest {q}, task {t}", () =>
                {
                    if (vm.Quests[q].ShowsVanillaPanel) vm.SelectedVanillaQuest = vm.Quests[q]; else vm.SelectedQuest = vm.Quests[q];
                    WindowHarness.Pump();
                    vm.Quests[q].SelectedTask = vm.Quests[q].TaskRows[t];
                });
            }
        }
    });

    [Fact]
    public void Variables() => Crawl((crawl, vm) =>
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < vm.Variables.Count; i++)
        {
            int v = i;
            if (!types.Add(vm.Variables[v].Model.Type.ToString())) continue;
            crawl.Explore(MainTab.Variables, "variable " + v, () => vm.SelectedVariable = vm.Variables[v]);
        }
    });

    [Fact]
    public void RulesWithEveryKindOfActionAndCondition() => Crawl((crawl, vm) =>
    {
        for (int i = 0; i < Math.Min(vm.IntegrationRules.Count, 4); i++)
        {
            int r = i;
            crawl.Explore(MainTab.Integration, "rule " + r, () => vm.SelectedIntegrationRule = vm.IntegrationRules[r]);
        }
        // Every type an action or a condition can be, on a row of its own, so each one's fields are on the page.
        int with = Enumerable.Range(0, vm.IntegrationRules.Count).FirstOrDefault(k => vm.IntegrationRules[k].Actions.Count > 0 && vm.IntegrationRules[k].Conditions.Count > 0, -1);
        if (with < 0) return;
        // Picked as the row's own Type list picks it (what that list writes is not always the type by name:
        // the variable actions are one entry, and two others share a row of their own).
        foreach (string type in vm.ActionTypes)
        {
            crawl.Explore(MainTab.Integration, "an action " + type, () =>
            {
                vm.SelectedIntegrationRule = vm.IntegrationRules[with];
                WindowHarness.Pump();
                vm.IntegrationRules[with].Actions[0].DisplayType = type;
            });
        }
        crawl.Recover();
        foreach (string type in vm.IntegrationRules[with].Conditions[0].AvailableTypes.ToList())
        {
            crawl.Explore(MainTab.Integration, "a condition " + type, () =>
            {
                vm.SelectedIntegrationRule = vm.IntegrationRules[with];
                WindowHarness.Pump();
                vm.IntegrationRules[with].Conditions[0].DisplayType = type;
            });
        }
    });
}
