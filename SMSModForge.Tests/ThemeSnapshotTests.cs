using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Pictures of the real window in every theme, for a person to look at (the
/// author, 2026-10-21: "give yourself some way to visualize the themes").
/// <para/>
/// Off unless asked for: <c>SMSMODFORGE_SNAPSHOTS=&lt;folder&gt;</c>. Writes, per
/// theme, one PNG per tab state and a <c>contrast.tsv</c> of every text that
/// falls short of WCAG's contrast (see <see cref="ContrastAudit"/>); and per
/// state, a sheet with every theme side by side.
/// <list type="bullet">
/// <item><c>SMSMODFORGE_SNAPSHOT_THEMES=Dark,Midnight</c> - only these.</item>
/// <item><c>SMSMODFORGE_SNAPSHOT_STATES=dialogues,scenes</c> - only states whose name contains these.</item>
/// <item><c>SMSMODFORGE_SNAPSHOT_PACK=&lt;pack folder&gt;</c> - a real pack instead of the built-in
/// one. It is copied first; the original is never opened.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class ThemeSnapshotTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-snap-" + Guid.NewGuid().ToString("N"));

    public ThemeSnapshotTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int Width = 1600, Height = 1000;

    /// <summary>A pack with a little of everything, so every tab has rows,
    /// a selection and a filled-in editor to look at.</summary>
    internal static ModPack Fixture()
    {
        var pack = PackRepository.CreateEmpty("snapshots.pack");

        var sarah = new CharacterDef { Key = "sarah", Name = "sarah", DisplayName = "Sarah", NameColor = "#E8A0C0" };
        sarah.Outfits.Add(new OutfitDef { Key = "swim", GameObjectName = "Sarah_Swim" });
        sarah.Outfits.Add(new OutfitDef { Key = "party", GameObjectName = "Sarah_Party" });
        sarah.DefaultOutfit = "Sarah_Swim";
        pack.Characters.Add(sarah);

        // Art and objects, so the preview draws a picture with its hierarchy
        // and selection beside it (1.8.0); the picture is written by PaintCave.
        var cave = new PlaceDef { Key = "cave", DisplayName = "Cave", BaseSprite = "Art/cave.png" };
        var lantern = new GameObjectDef { Name = "Lantern on the long wall of the cave", Sprite = "Art/cave.png", X = -3 };
        lantern.Children.Add(new GameObjectDef { Name = "Glow" });
        cave.GameObjects.Add(lantern);
        cave.GameObjects.Add(new GameObjectDef { Name = "Exit" });
        pack.Places.Add(cave);
        pack.Places.Add(new PlaceDef { Key = "pier", DisplayName = "Pier" });

        pack.Scenes.Add(new SceneDef { Key = "beach_kiss", DisplayName = "Beach kiss", Sound = SceneSoundMode.Kiss });
        pack.Scenes.Add(new SceneDef { Key = "beach_hug", DisplayName = "Beach hug" });
        pack.Scenes.Add(new SceneDef { Key = "cave_rest", DisplayName = "Cave rest" });
        pack.SceneFolders.Add(new UnitFolderDef { Name = "Beach", Items = { "beach_kiss", "beach_hug" } });

        pack.Sfx.Add(new SfxDef { Key = "door", DisplayName = "Door", AudioPath = "Audio/door.wav", TextPatterns = new() { "*door*" } });
        pack.Sfx.Add(new SfxDef { Key = "smooch", DisplayName = "Smooch", AudioPath = "Audio/door.wav", TextPatterns = new() { "*smooch*" } });
        pack.Music.Add(new MusicDef { Key = "rain", DisplayName = "Neon Rain", AudioPath = "Audio/rain.mp3" });

        pack.Variables.Add(new PackVariableDef { Name = "affection", Type = PackVariableType.Int, DefaultValue = "0" });
        pack.Variables.Add(new PackVariableDef { Name = "metSarah", Type = PackVariableType.Bool, DefaultValue = "false" });

        var d = new DialogueDef { Key = "chat", DisplayName = "Chat at the cave", Priority = 5 };
        d.StartConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.LevelActive, Params = { ["level"] = "place:cave" } });
        d.Nodes.Add(new DialogueNodeDef
        {
            Id = 1, Actor = "sarah", Outfit = "Sarah_Swim", Expression = "Happy",
            Text = "Hi {PC}! <b>Look</b> at the <color=#66CCFF>water</color> *door* - 8, 9 and (a whisper).",
            Children = { 2 },
            Conditions = { new NodeConditionDef { Type = NodeConditionTypes.LevelActive, Params = { ["level"] = "place:cave" } } },
            ActionsOnStart =
            {
                new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive, Params = { ["kind"] = "Scene", ["target"] = "beach_kiss", ["active"] = "true" } },
                new NodeActionDef { Type = NodeActionTypes.PlaySFX, Params = { ["clip"] = "door" } },
            },
            // So the Variables tab has somewhere it is used to list (1.8.0).
            ActionsOnFinish = { new NodeActionDef { Type = NodeActionTypes.IncrementVariable, Params = { ["name"] = "affection", ["delta"] = "1" } } },
        });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Kind = DialogueNodeKind.Choice, Text = "", Children = { 3, 4 } });
        d.Nodes.Add(new DialogueNodeDef { Id = 3, Text = "Let's swim.", Actor = "player" });
        d.Nodes.Add(new DialogueNodeDef { Id = 4, Text = "Maybe later.", Actor = "player" });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        pack.Dialogues.Add(new DialogueDef { Key = "pier_talk", DisplayName = "Pier talk" });

        pack.MapButtons.Add(new MapButtonDef { Label = "To the cave", Target = "cave" });
        return pack;
    }

    private sealed record State(string Name, int Tab, Action<MainWindow, MainViewModel>? Then = null);

    private static readonly State[] States =
    {
        new("01-home", 0),
        new("02-characters", 1),
        // A character picked: its panels, the typing voice's sliders (1.8.0) among them.
        new("02b-character-sarah", 1, (w, vm) =>
        {
            vm.SelectedCharacter = vm.Characters.FirstOrDefault(c => c.Key == "sarah");
        }),
        new("03-npcs", 2),
        new("04-places", 3),
        new("05-mapbuttons", 4),
        new("06-dialogues", 5, (w, vm) =>
        {
            vm.SelectedDialogue = vm.Dialogues.FirstOrDefault(x => x.Key == "chat") ?? vm.Dialogues.FirstOrDefault();
            // Selected in the tree too: a selected row is what went white on white.
            vm.SelectedDialogueTreeItem = LeafOf(vm.DialogueTree, vm.SelectedDialogue);
            WindowHarness.Pump();
            if (vm.SelectedDialogue != null)
            {
                // Open, whatever the folded state below left.
                vm.SelectedDialogue.IdentityOpen = vm.SelectedDialogue.StartOpen = true;
                vm.SelectedNode = vm.SelectedDialogue.Nodes.FirstOrDefault();
            }
        }),
        // Identity and Start conditions folded, and a line with no bust (1.8.0).
        new("06b-dialogues-folded", 5, (w, vm) =>
        {
            vm.SelectedDialogue = vm.Dialogues.FirstOrDefault(x => x.Key == "chat") ?? vm.Dialogues.FirstOrDefault();
            WindowHarness.Pump();
            if (vm.SelectedDialogue == null) return;
            vm.SelectedDialogue.IdentityOpen = vm.SelectedDialogue.StartOpen = false;
            vm.SelectedNode = vm.SelectedDialogue.Nodes.FirstOrDefault(n => string.IsNullOrEmpty(n.Actor));
        }),
        new("07-dialogues-gamelook", 5, (w, vm) =>
        {
            vm.SelectedDialogue = vm.Dialogues.FirstOrDefault(x => x.Key == "chat") ?? vm.Dialogues.FirstOrDefault();
            WindowHarness.Pump();
            if (vm.SelectedDialogue != null) vm.SelectedNode = vm.SelectedDialogue.Nodes.FirstOrDefault();
            vm.GameLookNodeRows = true;
        }),
        new("08-scenes", 6),
        new("09-music", 7),
        new("10-sfx", 8),
        new("11-wallpapers", 9),
        new("12-ui", MainTab.Ui),
        new("13-quests", MainTab.Quests),
        new("14-variables", MainTab.Variables),
        new("15-integration", MainTab.Integration),
    };

    private static DialogueTreeItem? LeafOf(System.Collections.Generic.IEnumerable<DialogueTreeItem> level, DialogueViewModel? dialogue)
    {
        foreach (var item in level)
        {
            if (item is DialogueLeafNode leaf && ReferenceEquals(leaf.Dialogue, dialogue)) return leaf;
            if (item is DialogueFolderNode folder && LeafOf(folder.Children, dialogue) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void EveryThemeInEveryState()
    {
        string? outDir = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        string packDir = Path.Combine(_dir, "pack");
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOT_PACK");
        if (!string.IsNullOrWhiteSpace(real)) CopyTree(real, packDir);
        else
        {
            PackRepository.Save(Fixture(), packDir);
            CopyTutorialAudio(packDir);
            PaintCave(packDir);
        }

        var themes = Filter(ThemeManager.All, t => t.Name, "SMSMODFORGE_SNAPSHOT_THEMES");
        var states = Filter(States, s => s.Name, "SMSMODFORGE_SNAPSHOT_STATES");
        var shots = new Dictionary<string, List<(string Theme, string File)>>();
        var summary = new StringBuilder();

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var before = ThemeManager.Current;
            window.Width = Width;
            window.Height = Height;
            vm.OpenPackFromPath(packDir);
            WindowHarness.Pump();
            var tabs = (TabControl)window.FindName("MainTabs");
            try
            {
                foreach (var theme in themes)
                {
                    ThemeManager.Apply(theme);
                    string themeDir = Path.Combine(outDir, theme.Name);
                    Directory.CreateDirectory(themeDir);
                    var report = new StringBuilder("state\tratio\tneeded\tfg\tbg\tkind\tenabled\ttext\tpath\n");
                    // Every text measured over a picture, passing or not: what
                    // was looked at, so an empty contrast.tsv can be trusted.
                    var pictures = new StringBuilder("state\tratio\tneeded\tfg\tbg\ttext\tpath\n");
                    int shortfalls = 0;

                    foreach (var state in states)
                    {
                        vm.GameLookNodeRows = false;
                        tabs.SelectedIndex = state.Tab;
                        WindowHarness.Pump();
                        state.Then?.Invoke(window, vm);
                        WindowHarness.Pump();
                        window.UpdateLayout();
                        WindowHarness.Pump();

                        string file = Path.Combine(themeDir, state.Name + ".png");
                        Save(Render((FrameworkElement)window.Content, window.Background), file);
                        if (!shots.TryGetValue(state.Name, out var list)) shots[state.Name] = list = new();
                        list.Add((theme.Name, file));

                        var measured = ContrastAudit.Walk((Visual)window.Content);
                        foreach (var f in measured.Where(f => f.Kind.EndsWith("over a picture", StringComparison.Ordinal))
                                                  .GroupBy(f => (f.Text, f.Path)).Select(g => g.First()))
                            pictures.Append(CultureInfo.InvariantCulture, $"{state.Name}\t{f.Ratio:0.00}\t{ContrastAudit.Needed(f.Large):0.0}\t")
                                    .Append($"{ContrastAudit.Hex(f.Foreground)}\t{ContrastAudit.Hex(f.Background)}\t")
                                    .Append(f.Text.Replace('\t', ' ').Replace('\n', ' ')).Append('\t').Append(f.Path).Append('\n');

                        // Disabled controls are exempt (WCAG), and meant to look it.
                        foreach (var f in measured
                                     .Where(f => f.Enabled && f.Ratio < ContrastAudit.Needed(f.Large))
                                     .GroupBy(f => (f.Text, f.Foreground, f.Background)).Select(g => g.First())
                                     .OrderBy(f => f.Ratio))
                        {
                            shortfalls++;
                            report.Append(CultureInfo.InvariantCulture, $"{state.Name}\t{f.Ratio:0.00}\t{ContrastAudit.Needed(f.Large):0.0}\t")
                                  .Append($"{ContrastAudit.Hex(f.Foreground)}\t{ContrastAudit.Hex(f.Background)}\t{f.Kind}\t{f.Enabled}\t")
                                  .Append(f.Text.Replace('\t', ' ').Replace('\n', ' ')).Append('\t').Append(f.Path).Append('\n');
                        }
                    }
                    File.WriteAllText(Path.Combine(themeDir, "contrast.tsv"), report.ToString());
                    File.WriteAllText(Path.Combine(themeDir, "over-pictures.tsv"), pictures.ToString());
                    summary.AppendLine($"{theme.Name}: {shortfalls} texts below contrast");
                }
            }
            finally
            {
                vm.GameLookNodeRows = false;
                ThemeManager.Apply(before);
            }
        });

        foreach (var (state, list) in shots)
            Save(Sheet(list), Path.Combine(outDir, "sheet-" + state + ".png"));
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString());
        _out.WriteLine(summary.ToString());
        _out.WriteLine("written to " + outDir);
    }

    private static List<T> Filter<T>(IEnumerable<T> all, Func<T, string> name, string variable)
    {
        var wanted = (Environment.GetEnvironmentVariable(variable) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return wanted.Length == 0 ? all.ToList()
            : all.Where(x => wanted.Any(w => name(x).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>The element as drawn, over <paramref name="background"/> - the
    /// window paints its own background, outside its content, and text drawn
    /// over nothing came out with a halo.</summary>
    internal static BitmapSource Render(FrameworkElement root, Brush? background = null)
    {
        int w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (background != null) dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                             null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    internal static void Save(BitmapSource image, string file)
    {
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using var s = File.Create(file);
        png.Save(s);
    }

    /// <summary>Every theme's picture of one state, three to a row, a third
    /// of their size, each with its theme's name over it.</summary>
    private static BitmapSource Sheet(List<(string Theme, string File)> shots)
    {
        const double scale = 1.0 / 3, label = 22;
        const int columns = 3;
        int rows = (shots.Count + columns - 1) / columns;
        double cw = Width * scale, ch = Height * scale + label;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cw * columns, ch * rows));
            for (int i = 0; i < shots.Count; i++)
            {
                double x = (i % columns) * cw, y = (i / columns) * ch;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(shots[i].File);
                img.EndInit();
                dc.DrawImage(img, new Rect(x, y + label, img.PixelWidth * scale, img.PixelHeight * scale));
                dc.DrawText(new FormattedText(shots[i].Theme, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                              new Typeface("Segoe UI Semibold"), 14, Brushes.Black, 1.0),
                            new Point(x + 4, y + 2));
            }
        }
        var bmp = new RenderTargetBitmap((int)(cw * columns), (int)(ch * rows), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }

    /// <summary>A level picture for the cave: blocks of colour, small, so it is
    /// drawn enlarged - pixel by pixel when crisp, blurred when not.</summary>
    private static void PaintCave(string packDir)
    {
        const int w = 192, h = 108;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool light = ((x / 8) + (y / 8)) % 2 == 0;
                px[i] = (byte)(light ? 0x90 : 0x40); px[i + 1] = (byte)(light ? 0x70 : 0x30); px[i + 2] = (byte)(light ? 0x50 : 0x20); px[i + 3] = 0xFF;
            }
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        Directory.CreateDirectory(Path.Combine(packDir, "Art"));
        Save(image, Path.Combine(packDir, "Art", "cave.png"));
    }

    private static void CopyTutorialAudio(string packDir)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "TutorialAssets", "Audio");
        if (!Directory.Exists(assets)) return;
        Directory.CreateDirectory(Path.Combine(packDir, "Audio"));
        foreach (var f in Directory.GetFiles(assets))
        {
            string name = Path.GetExtension(f).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? "door.wav" : "rain.mp3";
            File.Copy(f, Path.Combine(packDir, "Audio", name), overwrite: true);
        }
    }
}
