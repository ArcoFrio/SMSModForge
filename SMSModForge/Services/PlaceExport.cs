// Debug builds only, and never in a release: the export draws the game's own levels into pictures anyone could
// pass around, which would be handing out the game's art. It is ours (DialogueForge's places, built from the
// repository), not the editor's. ReleaseReadinessTests reads the shipped editor's bytes to hold that.
#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;

namespace SMSModForge.Services;

/// <summary>
/// The place pictures export (1.9.2): every place drawn as the Places tab's preview draws it, at the game's screen
/// (1920x1080), with the game's look in full (bloom, light streaks, colour - <see cref="PlacePreview.RenderStill"/>),
/// for tools that show places (a pack author's own notes, a dialogue writer). The game's levels from the vanilla
/// level catalog, drawn as the vanilla extension preview draws them (with the pack's extension of a level, when a
/// pack is given); a pack's places as the Places tab does, with the pack's NPCs. The shaders are at rest (a still),
/// the interface (the map buttons) is left out, as the look leaves it out.
/// <para/>
/// A place is drawn as found on arrival (<see cref="PlacePreview.AsTheGameShows"/>), and each of its NPC groups once
/// more on its own, as the game draws it once switched on: a crowd the game picks out of its NPCs object (the Beach's
/// Group_1 to Group_3, one at a time), a pack NPC at her spot in an outfit (<see cref="PlacePreview.ShowAlso"/>). A tool
/// shows the place, and the groups it is asked for over it.
/// <para/>
/// Writes <c>&lt;token&gt;.png</c> per place into the folder (the token with its colon as a dot:
/// <c>vanilla.14_Beach.png</c>, <c>pack.&lt;packId&gt;.&lt;key&gt;.png</c>), <c>&lt;token&gt;.group.N.png</c> per group,
/// and <c>places.json</c> beside them, the list of what was written. Started from the command line:
/// <c>SMSModForge.exe --export-places &lt;folder&gt; [&lt;pack folder&gt;] [--level-art &lt;folder&gt;]</c>, the last
/// the extraction's full-size level art (<c>Resources/VanillaLevelArt</c> in the repository): the editor ships the
/// levels' art as thumbnails, which are drawn when it isn't given.
/// </summary>
public static class PlaceExport
{
    public const string Argument = "--export-places";

    public sealed class Group
    {
        /// <summary>Its path in the place's tree ("NPCs/Group_2", "NPCs/Couchleft/Anis/Default").</summary>
        [JsonProperty("path")] public string Path { get; set; } = "";
        [JsonProperty("file")] public string File { get; set; } = "";
        /// <summary>How many figures it has (the game's NPC sprites; a pack group's placements).</summary>
        [JsonProperty("figures")] public int Figures { get; set; }
        /// <summary>The pack NPCs it places, by key (empty for the game's own crowds).</summary>
        [JsonProperty("npcs")] public List<string> Npcs { get; set; } = new();
        /// <summary>Whether it shows without anything switching it on (then it is in the place's own picture too).</summary>
        [JsonProperty("onArrival")] public bool OnArrival { get; set; }
        /// <summary>Whether it waits on conditions the pack sets (an NPC for her character's Location_ variable).</summary>
        [JsonProperty("conditional")] public bool Conditional { get; set; }
        /// <summary>Whether it is one of a set the game shows one at a time: a crowd its NPCs object switches on
        /// alone (DisableChildren), or one of a spot's poses the game picks at random (<see cref="Random"/>).</summary>
        [JsonProperty("oneOf")] public bool OneOf { get; set; }
        /// <summary>For one of a set, the set: the path of the object it is one of the children of (the NPCs object,
        /// a spot's outfit). A tool shows one of a set at a time.</summary>
        [JsonProperty("set", NullValueHandling = NullValueHandling.Ignore)] public string? Set { get; set; }
        /// <summary>Whether the game picks it at random among its set each time the set switches on
        /// (RandomChildActivator): the same NPC at the same spot in another pose. Never in the place's own picture.</summary>
        [JsonProperty("random")] public bool Random { get; set; }
    }

    public sealed class Entry
    {
        [JsonProperty("token")] public string Token { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("kind")] public string Kind { get; set; } = "";
        [JsonProperty("file")] public string File { get; set; } = "";
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
        [JsonProperty("look")] public bool Look { get; set; }
        [JsonProperty("groups")] public List<Group> Groups { get; set; } = new();
    }

    public sealed class Manifest
    {
        [JsonProperty("generator")] public string Generator { get; set; } = "";
        [JsonProperty("made")] public string Made { get; set; } = "";
        [JsonProperty("places")] public List<Entry> Places { get; set; } = new();
    }

    /// <summary>Where the extraction's full-size level art is, when given (<c>--level-art &lt;folder&gt;</c>).</summary>
    public const string LevelArtArgument = "--level-art";

    /// <summary>The export asked for on the command line: (folder, pack folder or null, full-size level art folder or
    /// null), or false.</summary>
    public static bool WasAsked(string[] args, out string folder, out string? pack, out string? levelArt)
    {
        folder = ""; pack = null; levelArt = null;
        int art = Array.IndexOf(args, LevelArtArgument);
        if (art >= 0 && art + 1 < args.Length) levelArt = args[art + 1];
        var rest = args.Where((a, i) => art < 0 || (i != art && i != art + 1)).ToArray();
        int at = Array.IndexOf(rest, Argument);
        if (at < 0 || at + 1 >= rest.Length) return false;
        folder = rest[at + 1];
        pack = at + 2 < rest.Length ? rest[at + 2] : null;
        return true;
    }

    /// <summary>Export every place into <paramref name="folder"/>; on the UI thread. <paramref name="log"/> gets a
    /// line per place; <paramref name="only"/>: just these tokens. Returns what was written (also in places.json).</summary>
    public static Manifest Run(string folder, string? packFolder, Action<string>? log = null, IReadOnlyCollection<string>? only = null,
                               string? levelArt = null)
    {
        bool Wanted(string token) => only == null || only.Contains(token);
        Directory.CreateDirectory(folder);
        // The game's look (its post settings) is read in the background at start: waited for here.
        PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited();
        var manifest = new Manifest
        {
            Generator = "SMSModForge " + typeof(PlaceExport).Assembly.GetName().Version,   // English on purpose: the program's name in a file for tools
            Made = DateTime.UtcNow.ToString("o"),
        };
        var pack = string.IsNullOrWhiteSpace(packFolder) ? null : PackRepository.Load(packFolder!);
        var npcs = pack?.Npcs.Select(n => new NpcViewModel(n)).ToList();
        foreach (var level in VanillaLevelCatalog.All.OrderBy(l => l.SiblingIndex))
        {
            string token = "vanilla:" + level.GoName;
            if (level.GoName == "0_None" || !Wanted(token)) continue;
            var vm = VanillaLevel(token, pack, levelArt);
            PlacePreview Make() => VanillaPreview(vm, packFolder, npcs, levelArt);
            string name = VanillaPlaces.All.FirstOrDefault(p => p.GoName == level.GoName)?.DisplayName ?? level.GoName;
            Write(manifest, folder, Make, Groups(vm.GameObjects, level), token, name, "game", log);
        }
        if (pack != null)
        {
            foreach (var place in pack.Places)
            {
                string token = $"pack:{pack.PackId}.{place.Key}";
                if (!Wanted(token)) continue;
                var vm = new PlaceViewModel(place);
                PlacePreview Make() => new()
                {
                    PackRoot = packFolder!, BaseSprite = vm.BaseSprite, SecondarySprite = vm.SecondarySprite,
                    MaskSprite = vm.MaskSprite, SecondaryMaskSprite = vm.SecondaryMaskSprite,
                    LevelArtOrder = vm.BaseSortingOrder, LevelArtSecondaryOrder = vm.SecondarySortingOrder,
                    GameObjects = vm.GameObjects, NpcCatalog = npcs, Shaders = false, ParallaxPreview = false, AsTheGameShows = true,
                    DimInactive = false, DimUnderInactiveParent = false,
                };
                Write(manifest, folder, Make, Groups(vm.GameObjects, null), token, place.DisplayName, "pack", log);
            }
        }
        File.WriteAllText(Path.Combine(folder, "places.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
        return manifest;
    }

    /// <summary>One of the game's levels as the vanilla extension tab has it: the pack's own extension of the level
    /// (its NPCs there, its exits), or the level as the game has it; its objects' art the full-size art's, when given
    /// (the editor ships the levels' art small, thumbnails for its own lists).</summary>
    internal static VanillaPlaceExtensionViewModel VanillaLevel(string token, ModPack? pack, string? levelArt)
    {
        var def = pack?.VanillaExtensions.FirstOrDefault(e => e.Source == token) ?? new VanillaPlaceExtensionDef { Source = token };
        var vm = new VanillaPlaceExtensionViewModel(def);
        foreach (var node in Walk(vm.GameObjects))
            if (!string.IsNullOrWhiteSpace(node.Model.VanillaArtPath))
                node.Model.VanillaArtPath = Larger(node.Model.VanillaArtPath, levelArt);
        return vm;
    }

    /// <summary>The level drawn as the game shows it on arrival (see the class).</summary>
    internal static PlacePreview VanillaPreview(VanillaPlaceExtensionViewModel vm, string? packFolder,
                                                IReadOnlyList<NpcViewModel>? npcs, string? levelArt) => new()
    {
        // The level's art comes with absolute paths; the folder is only what the preview requires to draw.
        PackRoot = packFolder ?? VanillaLevelCatalog.ArtRoot() ?? AppContext.BaseDirectory,
        BaseSprite = Larger(vm.PreviewBaseSprite, levelArt), LevelArtOrder = vm.PreviewArtOrder,
        LevelArtPpu = vm.PreviewArtPpu, LevelArtIsTheGames = true, LevelArtPivotX = vm.PreviewArtPivotX,
        LevelArtPivotY = vm.PreviewArtPivotY, LevelPositionX = vm.PreviewLevelPositionX,
        LevelPositionY = vm.PreviewLevelPositionY, LevelVideoScreen = vm.PreviewVideoScreen,
        LevelVideoStill = Larger(vm.PreviewVideoStill, levelArt), LevelArtUnlit = vm.PreviewArtUnlit,
        LevelScale = vm.PreviewLevelScale, NpcCatalog = npcs,
        GameObjects = vm.GameObjects, Shaders = false, ParallaxPreview = false, AsTheGameShows = true,
        DimInactive = false, DimUnderInactiveParent = false,
    };

    /// <summary>
    /// A place's NPC groups, by their path in its tree (named as the preview names its rows): each child of an object
    /// called NPCs that holds figures (the game's crowds), and each object that places pack NPCs (a spot in an outfit).
    /// An object that switches on one of its children at random (<see cref="PlacePreview.PicksAtRandom"/>: a spot's
    /// poses of one NPC in one outfit) gives a group for each child, its NPC placements among them, as the game never
    /// shows two of them together.
    /// </summary>
    internal static List<Group> Groups(IEnumerable<GameObjectViewModel> roots, VanillaLevelCatalog.Level? level)
    {
        var groups = new List<Group>();
        void Visit(GameObjectViewModel o, string prefix, bool offAbove, bool condAbove, bool underNpcs, bool oneOf)
        {
            string name = string.IsNullOrWhiteSpace(o.Name) ? "(unnamed)" : o.Name;
            string path = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
            bool off = offAbove || !o.StartActive, cond = condAbove || o.Model.ActiveConditions.Count > 0;
            if (underNpcs)
            {
                int figures = Walk(new[] { o }).Count(n => !string.IsNullOrWhiteSpace(n.PreviewSprite)
                    && VanillaLevelCatalog.IsNpc(VanillaLevelCatalog.FindNode(level, PathOf(n, roots))));
                if (figures > 0)
                    groups.Add(new Group { Path = path, Figures = figures, OnArrival = !off && !cond, Conditional = cond, OneOf = oneOf,
                                           Set = oneOf ? prefix : null });
            }
            if (PlacePreview.PicksAtRandom(o.Model))
            {
                foreach (var pl in o.Npcs)
                {
                    string label = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;   // as the preview names the row
                    groups.Add(new Group
                    {
                        Path = path + "/" + (string.IsNullOrWhiteSpace(label) ? "(unset)" : label), Figures = 1,
                        Npcs = string.IsNullOrEmpty(pl.Npc) ? new() : new() { pl.Npc },
                        Conditional = cond || pl.Model.ActiveConditions.Count > 0, OneOf = true, Set = path, Random = true,
                    });
                }
                foreach (var c in o.Children)
                {
                    var under = Walk(new[] { c }).ToList();
                    int figures = under.Count(n => !string.IsNullOrWhiteSpace(n.PreviewSprite)) + under.Sum(n => n.Npcs.Count);
                    if (figures == 0) continue;
                    string cname = string.IsNullOrWhiteSpace(c.Name) ? "(unnamed)" : c.Name;
                    groups.Add(new Group
                    {
                        Path = path + "/" + cname, Figures = figures,
                        Npcs = under.SelectMany(n => n.Npcs).Select(p => p.Npc).Where(k => !string.IsNullOrEmpty(k)).ToList(),
                        Conditional = cond || under.Any(n => n.Model.ActiveConditions.Count > 0), OneOf = true, Set = path, Random = true,
                    });
                }
                return;   // what is under it is in its children's groups
            }
            if (o.Npcs.Count > 0)
                groups.Add(new Group
                {
                    Path = path, Figures = o.Npcs.Count, Npcs = o.Npcs.Select(p => p.Npc).Where(k => !string.IsNullOrEmpty(k)).ToList(),
                    OnArrival = !off && !cond && o.Npcs.All(p => p.StartActive && p.Model.ActiveConditions.Count == 0),
                    Conditional = cond || o.Npcs.Any(p => p.Model.ActiveConditions.Count > 0),
                });
            bool npcsRoot = name == "NPCs";
            bool children1 = npcsRoot && level != null && (VanillaLevelCatalog.FindNode(level, path)?.Components.Contains("DisableChildren") ?? false);
            foreach (var c in o.Children)
                Visit(c, path, off, cond, npcsRoot && !underNpcs, children1);
        }
        foreach (var r in roots) Visit(r, "", false, false, false, false);
        return groups;
    }

    /// <summary>A node's path among the place's roots (as the catalog addresses the level's objects).</summary>
    private static string PathOf(GameObjectViewModel node, IEnumerable<GameObjectViewModel> roots)
    {
        string? Find(IEnumerable<GameObjectViewModel> nodes, string prefix)
        {
            foreach (var n in nodes)
            {
                string p = string.IsNullOrEmpty(prefix) ? n.Name : prefix + "/" + n.Name;
                if (ReferenceEquals(n, node)) return p;
                var below = Find(n.Children, p);
                if (below != null) return below;
            }
            return null;
        }
        return Find(roots, "") ?? node.Name;
    }

    /// <summary>A place drawn as found on arrival, then each group not already in it on its own over the place.</summary>
    private static void Write(Manifest manifest, string folder, Func<PlacePreview> make, List<Group> groups, string token,
                              string name, string kind, Action<string>? log)
    {
        string stem = token.Replace(':', '.');
        var (file, look) = Draw(make(), folder, stem + ".png", null);
        int n = 0;
        foreach (var g in groups)
        {
            if (g.OnArrival) continue;                    // already in the place's own picture
            n++;
            (g.File, _) = Draw(make(), folder, $"{stem}.group.{n}.png", new[] { g.Path });
        }
        manifest.Places.Add(new Entry { Token = token, Name = name, Kind = kind, File = file, Width = PlaceExport.Width, Height = PlaceExport.Height, Look = look, Groups = groups });
        log?.Invoke($"{token} -> {file}, {n} groups" + (look ? "" : " (without the game's look: its settings did not load)"));   // English on purpose: a line of the export's log, never shown
    }

    /// <summary>A preview laid out at the game's screen, drawn, and written as PNG.</summary>
    private static (string File, bool Look) Draw(PlacePreview preview, string folder, string file, IReadOnlyCollection<string>? also)
    {
        var (pixels, look) = Still(preview, also);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4)));
        using (var stream = File.Create(Path.Combine(folder, file)))
            encoder.Save(stream);
        return (file, look);
    }

    /// <summary>The game's screen, which every picture is drawn at.</summary>
    internal const int Width = 1920, Height = 1080;

    /// <summary>A preview laid out at the game's screen and drawn (BGRA), with the game's look when its settings
    /// loaded (<c>Look</c>), else without.</summary>
    internal static (byte[] Pixels, bool Look) Still(PlacePreview preview, IReadOnlyCollection<string>? also = null)
    {
        preview.ShowAlso = also;
        preview.Width = Width; preview.Height = Height;
        preview.Measure(new Size(Width, Height));
        preview.Arrange(new Rect(0, 0, Width, Height));
        preview.UpdateLayout();
        Settle();
        var pixels = preview.RenderStill(look: true);
        bool look = pixels != null;
        return (pixels ?? preview.RenderStill(look: false)!, look);
    }

    /// <summary>The same file in the full-size art folder (the extraction's <c>VanillaLevelArt</c>), when it is there;
    /// else the shipped one.</summary>
    private static string Larger(string shipped, string? levelArt)
    {
        var root = VanillaLevelCatalog.ArtRoot();
        if (string.IsNullOrWhiteSpace(shipped) || string.IsNullOrWhiteSpace(levelArt) || root == null) return shipped;
        string rel = Path.GetRelativePath(root, shipped);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return shipped;
        string full = Path.Combine(levelArt!, rel);
        return File.Exists(full) ? full : shipped;
    }

    private static IEnumerable<GameObjectViewModel> Walk(IEnumerable<GameObjectViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Walk(n.Children)) yield return c;
        }
    }

    /// <summary>Let what the preview put off to the dispatcher (its scene built after a change) run.</summary>
    private static void Settle()
    {
        foreach (var priority in new[] { DispatcherPriority.Render, DispatcherPriority.Loaded, DispatcherPriority.Background })
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), priority);
            Dispatcher.PushFrame(frame);
        }
    }
}
#endif
