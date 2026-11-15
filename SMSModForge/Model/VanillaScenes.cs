using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// The game's scenes a pack can change in place (1.9.0): every child of its six
/// <c>4_CG_Manager-*</c> roots that the game can show, each with a half-size
/// picture to know it by.
/// <para/>
/// Read from <c>VanillaScenes/index.json</c> beside the editor, which
/// Tools/Scenes/make_scene_catalog.py writes from a dump of the running game.
/// Scenes the game never shows are not in it - not as entries, not as pictures -
/// so nothing the game does not release ships with the editor. Absent, the list is
/// empty and the Scenes tab says so.
/// </summary>
public static class VanillaScenes
{
    /// <summary>One of the game's scenes.</summary>
    public sealed class Scene
    {
        /// <summary>Where it sits: the CG root and its name.</summary>
        [JsonProperty("path")] public string Path { get; set; } = "";

        /// <summary>Its picture beside the index, relative to it.</summary>
        [JsonProperty("picture")] public string Picture { get; set; } = "";

        /// <summary>The art's sprite name in the game.</summary>
        [JsonProperty("art")] public string Art { get; set; } = "";

        /// <summary>The game's frame for it: SexyFrame or PhotoFrame.</summary>
        [JsonProperty("frame")] public string Frame { get; set; } = "";

        /// <summary>What its own trigger raises when it is shown: "kiss", "flash", or
        /// nothing.</summary>
        [JsonProperty("signal")] public string Signal { get; set; } = "";

        /// <summary>Whether it is one of the game's Starmaker photos (its trigger pays
        /// out through PhotoBonus).</summary>
        [JsonProperty("photo")] public bool Photo { get; set; }

        /// <summary>The conversations and scripts that show it, by the path the
        /// research gives them.</summary>
        [JsonProperty("shownBy")] public List<string> ShownBy { get; set; } = new();

        /// <summary>Its name, without the root.</summary>
        [JsonIgnore]
        public string Name => Path.Contains('/') ? Path.Substring(Path.IndexOf('/') + 1) : Path;

        /// <summary>The root it sits under, without "4_CG_Manager-": Sexy, Sexy_Two,
        /// Photos...</summary>
        [JsonIgnore]
        public string Group
        {
            get
            {
                string root = Path.Contains('/') ? Path.Substring(0, Path.IndexOf('/')) : "";
                return root.StartsWith(RootPrefix, StringComparison.Ordinal) ? root.Substring(RootPrefix.Length) : root;
            }
        }

        /// <summary>Its picture's file on disk.</summary>
        [JsonIgnore]
        public string PictureFile => PictureOf(this);

        /// <summary>The frame's file among the editor's VanillaFrames.</summary>
        [JsonIgnore]
        public string FrameFile => string.IsNullOrEmpty(Frame) ? "" : Frame + ".png";

        public override string ToString() => Name;
    }

    private sealed class Index
    {
        [JsonProperty("scenes")] public List<Scene> Scenes { get; set; } = new();
    }

    public const string RootPrefix = "4_CG_Manager-";

    private static readonly object Gate = new();
    private static IReadOnlyList<Scene>? _all;
    private static Dictionary<string, Scene>? _byPath;

    /// <summary>Where the catalogue is: beside the editor; a test may point it elsewhere.</summary>
    public static string Folder { get; set; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "VanillaScenes");

    /// <summary>Every scene the game can show, in the game's order.</summary>
    public static IReadOnlyList<Scene> All
    {
        get
        {
            lock (Gate)
            {
                if (_all != null) return _all;
                var scenes = new List<Scene>();
                try
                {
                    string file = System.IO.Path.Combine(Folder, "index.json");
                    if (File.Exists(file))
                        scenes = JsonConvert.DeserializeObject<Index>(File.ReadAllText(file))?.Scenes ?? new();
                }
                catch (IOException) { }
                catch (JsonException) { }
                _all = scenes;
                _byPath = scenes.GroupBy(s => s.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                return _all;
            }
        }
    }

    /// <summary>One by its path, or null: one the game never shows, a mistyped
    /// path, or the catalogue missing.</summary>
    public static Scene? Find(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        _ = All;
        return _byPath != null && _byPath.TryGetValue(path, out var s) ? s : null;
    }

    /// <summary>Its picture's file on disk, or empty.</summary>
    public static string PictureOf(Scene? scene)
        => scene == null || string.IsNullOrEmpty(scene.Picture) ? "" : System.IO.Path.Combine(Folder, scene.Picture.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Forget what was read, for a test that changes <see cref="Folder"/>.</summary>
    public static void Reload()
    {
        lock (Gate) { _all = null; _byPath = null; }
    }
}
