using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Shared;

namespace SMSModForge.Tests;

/// <summary>
/// The packs the in-game suite plays (1.8.0, 12.13), made the way an author's
/// are - the editor's model, saved and exported by the editor's own code - so
/// what the game is given is what an author would give it. Each area's pack
/// says beside it what the game must show; <see cref="InGameSuiteTests"/>
/// reads it back.
/// <para/>
/// Nothing here starts on its own: whatever a check needs to happen is done
/// by the check, through the bridge.
/// </summary>
internal static class InGameSuitePacks
{
    /// <summary>The pack ids the suite installs, and nothing else is left in
    /// the test copy's Mods.</summary>
    public static readonly string[] Ids = { "gamecheck", "gameorder", World.Id, Story.Id, Quests.Id, Rules.Id, Saving.Id, Talk.Id, Looks.Id };

    /// <summary>Exports every pack into the test copy's Mods folder, which the
    /// suite owns: a pack left there by anything else is taken out.</summary>
    public static List<string> ExportInto(string mods, string scratch)
    {
        Directory.CreateDirectory(mods);
        var said = new List<string>();
        foreach (var stale in Directory.GetFiles(mods, "*" + PackExporter.FileExtension))
            if (!Ids.Contains(Path.GetFileNameWithoutExtension(stale), StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(stale);
                said.Add("took out " + Path.GetFileName(stale));
            }
        void Export(ModPack pack, string folder, Action<ModPack, string>? alongside = null)
        {
            PackRepository.Save(pack, folder);
            alongside?.Invoke(pack, folder);
            var result = PackExporter.Export(folder, Path.Combine(mods, pack.PackId + PackExporter.FileExtension));
            said.Add($"{pack.PackId}: {result}");
        }
        string Folder(string id) { var f = Path.Combine(scratch, id); Directory.CreateDirectory(f); return f; }
        Export(InGameCheckPacksTests.GameCheck(Folder("gamecheck")), Folder("gamecheck"));
        Export(InGameCheckPacksTests.GameOrder(), Folder("gameorder"));
        Export(World.Make(Folder(World.Id)), Folder(World.Id));
        Export(Story.Make(Folder(Story.Id)), Folder(Story.Id));
        Export(Quests.Make(), Folder(Quests.Id));
        Export(Rules.Make(), Folder(Rules.Id));
        Export(Saving.Make(), Folder(Saving.Id));
        Export(Talk.Make(Folder(Talk.Id)), Folder(Talk.Id), Talk.Translate);
        Export(Looks.Make(Folder(Looks.Id)), Folder(Looks.Id));
        return said;
    }

    // ── The pieces packs are made of ────────────────────────────────────

    /// <summary>A PNG of one colour, written into the pack.</summary>
    public static string Png(string packFolder, string rel, int w, int h, Color colour)
    {
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = colour.B; pixels[i + 1] = colour.G; pixels[i + 2] = colour.R; pixels[i + 3] = colour.A;
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        string path = Path.Combine(packFolder, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
        return rel;
    }

    /// <summary>A picture written into the pack from a colour for each pixel
    /// (x right, y down, as the file lies), straight alpha.</summary>
    public static string PngOf(string packFolder, string rel, int w, int h, Func<int, int, Color> at)
    {
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = at(x, y);
                int i = (y * w + x) * 4;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = c.A;
            }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        string path = Path.Combine(packFolder, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
        return rel;
    }

    /// <summary>A GIF whose frames are each one colour, written into the pack (no delays
    /// of its own: it plays at the default 0.1 s a frame). Saving the pack decodes it.</summary>
    public static string Gif(string packFolder, string rel, int w, int h, params Color[] frames)
    {
        var gif = new GifBitmapEncoder();
        foreach (var colour in frames)
        {
            var pixels = new byte[w * h * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = colour.B; pixels[i + 1] = colour.G; pixels[i + 2] = colour.R; pixels[i + 3] = 255;
            }
            gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4)));
        }
        string path = Path.Combine(packFolder, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        gif.Save(file);
        return rel;
    }

    public static NodeConditionDef Is(string variable, string value) => new()
    {
        Type = NodeConditionTypes.VariableCompare,
        Params = { ["source"] = "pack", ["name"] = variable, ["comparison"] = "equals", ["value"] = value },
    };

    public static NodeActionDef Act(string type, params (string Key, string Value)[] p)
    {
        var a = new NodeActionDef { Type = type };
        foreach (var (k, v) in p) a.Params[k] = v;
        return a;
    }

    public static NodeActionDef Add(string variable, int by = 1)
        => Act(NodeActionTypes.IncrementVariable, ("name", variable), ("delta", by.ToString()));

    public static PackVariableDef Var(string name, PackVariableType type, string value, bool persisted = false)
        => new() { Name = name, Type = type, DefaultValue = value, Persisted = persisted };

    // ── Places, their GameObjects, the game's levels, journeys ──────────

    /// <summary>
    /// <c>suiteworld</c>: a place of its own, and three of the game's levels.
    /// <list type="bullet">
    ///   <item>Place <c>checkroom</c> (map <c>&lt;n&gt;_CheckRoom</c>): its art
    ///   2048x1136 painted #204060 and its backdrop <c>CheckRoom_Secondary</c>
    ///   #102030; on it <c>Lamp</c> (64x32, at 1.5,-2, turned 15, twice as
    ///   wide, order 3), <c>Hidden</c> (starts off), <c>Sign</c> (on while
    ///   <c>signOn</c> is true), the container <c>Holder</c> with <c>Inner</c>
    ///   under it, and in its NPCs <c>Walker</c> (local 2,-1, order 5, eyes,
    ///   shadow, reflection) and <c>Ghost</c> (out while <c>ghostOn</c> is
    ///   true). Arriving adds 1 to <c>entered</c>, leaving 1 to <c>left</c>.
    ///   Its buttons: to the Storage Room, and one shown only while
    ///   <c>navOk</c> is true.</item>
    ///   <item>The Hiking Path's start, never visited: <c>HikeMark</c> made on it
    ///   at 2,1; its own <c>Light 2D</c> moved to 0.25,0.5 (a bound node that asks
    ///   to); its own <c>NPCs</c> bound with nothing asked of it but a child,
    ///   <c>NpcsMark</c> - so it must stay exactly where the game has it. Not the
    ///   Beach (1.9.2): the Beach is a bust preview's backdrop, captured in this
    ///   test copy, and its lamp was captured where this pack had moved it.</item>
    ///   <item>The Beach: a button to the room, <c>Check Room</c>.</item>
    ///   <item>The Storage Room (quiet: nobody talks as you arrive): a button
    ///   back to the room.</item>
    ///   <item>The World Map: <c>Map Room</c> in the Seaside district, to the
    ///   room, while <c>mapOk</c> is true.</item>
    ///   <item>Conversation <c>moves</c>: Lamp to 3,-1, Inner faded to 0.25,
    ///   Hidden given the sign's picture and switched on, Holder spun at 90
    ///   degrees a second; <c>back</c>: Lamp home, the spin stopped.</item>
    ///   <item>Downtown, whose crowd the game puts right on arrival:
    ///   <c>TownWalker</c> among it, out while <c>townNpc</c> is true.</item>
    /// </list>
    /// </summary>
    public static class World
    {
        public const string Id = "suiteworld";
        public const string Room = "checkroom", RoomObject = "CheckRoom";
        public const string Store = "32_StorageRoom", Town = "26_Downtown";
        /// <summary>The game's level the pack's bound objects are checked on (B4): one with a Light 2D and NPCs of
        /// its own, and no bust preview's backdrop (1.9.2).</summary>
        public const string Hike = "138_HikingPath_Start";

        /// <summary>The room's copies of the game's objects, and the game's
        /// effect the second is a copy of.</summary>
        public const string PlantCopy = "PlantCopy", HeartsCopy = "SuiteHearts",
                            HeartsEffect = "CFXR2 Expression Loving (Timed)", PlantLevel = "8_Pool";

        /// <summary>The Pool's potted plant - a prop with a picture and no
        /// scripts - as the editor's catalogue of the game's levels names it.</summary>
        public static string PlantSource()
            => VanillaLevelCatalog.RootChildren(VanillaLevelCatalog.FindLevel(PlantLevel))
                   .First(n => n.SpriteRenderer?.Sprite == "PottedPlant").Name;

        /// <summary>The game's character the pack adopts, and her two busts:
        /// the one given the pack's picture, and one left as the game has it.</summary>
        public const string Adopted = "Samantha";
        public static string OverriddenBust = "", UntouchedBust = "";

        public static ModPack Make(string folder)
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            pack.Variables.Add(Var("entered", PackVariableType.Int, "0"));
            pack.Variables.Add(Var("left", PackVariableType.Int, "0"));
            pack.Variables.Add(Var("signOn", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("navOk", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("ghostOn", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("townNpc", PackVariableType.Bool, "true"));
            pack.Variables.Add(Var("mapOk", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("never", PackVariableType.Bool, "false"));

            string art = Png(folder, "Art/room.png", 2048, 1136, Color.FromRgb(0x20, 0x40, 0x60));
            string back = Png(folder, "Art/room2.png", 2048, 1136, Color.FromRgb(0x10, 0x20, 0x30));
            string lamp = Png(folder, "Art/lamp.png", 64, 32, Color.FromRgb(0xFF, 0xCC, 0x00));
            string sign = Png(folder, "Art/sign.png", 40, 40, Color.FromRgb(0xCC, 0x22, 0x22));

            // An NPC: a pose, eyes that blink, a shadow and a floor reflection.
            pack.Npcs.Add(new NpcDef
            {
                Key = "walker", DisplayName = "Walker",
                Sprite = Png(folder, "Npcs/walker.png", 120, 300, Color.FromRgb(0x33, 0xAA, 0x55)),
                Blink = { Sprite = Png(folder, "Npcs/walker_blink.png", 20, 10, Color.FromRgb(0x00, 0x00, 0x00)) },
                Reflection = { Enabled = true },
            });

            var room = new PlaceDef
            {
                Key = Room, InternalName = RoomObject, DisplayName = "Check Room",
                BaseSprite = art, SecondarySprite = back,
                GameObjects =
                {
                    new GameObjectDef { Name = "Lamp", Sprite = lamp, X = 1.5f, Y = -2f, RotationZ = 15f, ScaleX = 2f, SortingOrder = 3 },
                    new GameObjectDef { Name = "Hidden", Sprite = lamp, StartActive = false },
                    new GameObjectDef { Name = "Sign", Sprite = sign, X = -3f, ActiveConditions = { Is("signOn", "true") } },
                    new GameObjectDef
                    {
                        Name = "Holder",
                        Children = { new GameObjectDef { Name = "Inner", Sprite = sign, X = 4f, Y = 1f } },
                    },
                    // Copies of the game's objects: the Pool's potted plant,
                    // here at 3,2; and the game's hearts effect, beside the
                    // original, its particles and its Hearts' recoloured.
                    new GameObjectDef { Name = PlantCopy, CopyOf = "5_Levels/" + PlantLevel + "/" + PlantSource(), OverrideTransform = true, X = 3f, Y = 2f,
                                        OverrideParallax = true, ParallaxStrength = 0.25f },
                    new GameObjectDef
                    {
                        Name = HeartsCopy, CopyOf = "6_Effects/" + HeartsEffect, CopyBeside = true, ParticleColor = "#FF99E6",
                        Children = { new GameObjectDef { Name = "Hearts", Bind = true, ParticleColor = "#FF59B3" } },
                    },
                    new GameObjectDef
                    {
                        Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
                        Npcs =
                        {
                            new NpcPlacementDef { Npc = "walker", Name = "Walker", StartActive = true, SortingOrder = 5,
                                                  Body = new NpcTransform { X = 2f, Y = -1f } },
                            new NpcPlacementDef { Npc = "walker", Name = "Ghost", StartActive = false,
                                                  ActiveConditions = { Is("ghostOn", "true") } },
                        },
                    },
                },
                OnEnter = { new LevelHookDef { Actions = { Add("entered") } } },
                OnExit = { new LevelHookDef { Actions = { Add("left") } } },
                NavigatorButtons =
                {
                    new NavigatorButtonDef { Target = "vanilla:" + Store, Label = "Check Store" },
                    new NavigatorButtonDef { Target = "vanilla:" + Store, Label = "Check Gated", Conditions = { Is("navOk", "true") } },
                },
            };
            pack.Places.Add(room);

            // One of the game's characters adopted as the editor adopts one,
            // with a picture of the pack's over her first outfit's body.
            var samantha = VanillaCastSeed.Make(VanillaCharacters.Find(Adopted) ?? throw new InvalidOperationException("No " + Adopted + " among the game's characters."));
            if (samantha.Outfits.Count < 2) throw new InvalidOperationException(Adopted + " has fewer than two outfits.");
            samantha.Outfits[0].SpriteOverrides.Add(new SpriteOverrideDef
            {
                Slot = SpriteSlotNames.Base,
                Sprite = Png(folder, "Busts/override.png", 256, 512, Color.FromRgb(0xAA, 0x33, 0xAA)),
            });
            OverriddenBust = samantha.Outfits[0].GameObjectName;
            UntouchedBust = samantha.Outfits[1].GameObjectName;
            pack.Characters.Add(samantha);

            // Actions on the room's objects, as a line of a conversation does them.
            NodeActionDef On(string type, string target, params (string Key, string Value)[] more)
            {
                var a = Act(type, ("kind", "GameObjects"), ("target", target), ("overlayLevel", "self:" + Room));
                foreach (var (k, v) in more) a.Params[k] = v;
                return a;
            }
            pack.Dialogues.Add(Script("moves", "Things move.",
                On(NodeActionTypes.MoveGameObject, "Lamp", ("x", "3"), ("y", "-1"), ("seconds", "0.3")),
                On(NodeActionTypes.FadeSprite, "Inner", ("to", "0.25"), ("seconds", "0.2")),
                On(NodeActionTypes.SetSprite, "Hidden", ("sprite", sign)),
                On(NodeActionTypes.SetGameObjectActive, "Hidden", ("active", "true")),
                On(NodeActionTypes.SpinGameObject, "Holder", ("speed", "90"), ("enable", "true"))));
            pack.Dialogues.Add(Script("back", "Things go back.",
                On(NodeActionTypes.MoveGameObject, "Lamp", ("home", "true"), ("seconds", "0.2")),
                On(NodeActionTypes.SpinGameObject, "Holder", ("enable", "false"))));
            // A picture framed as the game frames one: Inner to x 0.5 at twice
            // its size over 1.5 s, its y left as it is; the Lamp moved (held)
            // and then placed, which lets it go.
            pack.Dialogues.Add(Script("frame", "Zoom in.",
                On(NodeActionTypes.TransformGameObject, "Inner", ("x", "0.5"), ("y", ""), ("scale", "2"), ("seconds", "1.5")),
                On(NodeActionTypes.MoveGameObject, "Lamp", ("x", "3"), ("y", "-1"), ("seconds", "0")),
                On(NodeActionTypes.TransformGameObject, "Lamp", ("x", "7"))));
            pack.Dialogues.Add(Script("unframe", "Zoom out.",
                On(NodeActionTypes.TransformGameObject, "Inner", ("x", "4"), ("scale", "1")),
                On(NodeActionTypes.TransformGameObject, "Lamp", ("x", "1.5"), ("y", "-2"))));

            // Journeys asked for from a line: to the room, to the Storage
            // Room (one of the game's), and to the Storage Room again - where
            // the player already is.
            pack.Dialogues.Add(Script("goroom", "Off to the room.", Act(NodeActionTypes.Travel, ("target", "place:" + Room))));
            pack.Dialogues.Add(Script("gostore", "Off to the store.", Act(NodeActionTypes.Travel, ("target", "vanilla:" + Store))));
            pack.Dialogues.Add(Script("gostoreagain", "The store again.", Act(NodeActionTypes.Travel, ("target", "vanilla:" + Store))));

            // On the World Map, in the Seaside district, once mapOk is true.
            pack.MapButtons.Add(new MapButtonDef { Target = "self:" + Room, District = "Seaside", Label = "Map Room",
                                                   Conditions = { Is("mapOk", "true") } });

            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef
            {
                Source = "vanilla:14_Beach",
                NavigatorButtons = { new NavigatorButtonDef { Target = "self:" + Room, Label = "Check Room" } },
            });
            // A level that is no backdrop (1.9.2): the Beach is one, captured in this copy of the game.
            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef
            {
                Source = "vanilla:" + World.Hike,
                GameObjects =
                {
                    new GameObjectDef { Name = "HikeMark", Sprite = sign, X = 2f, Y = 1f },
                    new GameObjectDef { Name = "Light 2D", Bind = true, OverrideTransform = true, X = 0.25f, Y = 0.5f },
                    new GameObjectDef
                    {
                        Name = "NPCs", Bind = true,
                        Children = { new GameObjectDef { Name = "NpcsMark", Sprite = sign } },
                    },
                },
            });
            // Bound nodes as the editor makes them: seeded from the game's own
            // level, so one asked for nothing keeps what the game has.
            Seed(pack.VanillaExtensions[^1]);

            // The Storage Room - quiet: nobody starts talking as you arrive -
            // with the way back to the room.
            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef
            {
                Source = "vanilla:" + Store,
                NavigatorButtons = { new NavigatorButtonDef { Target = "self:" + Room, Label = "Check Room" } },
            });

            // Downtown, whose own crowd is put right as somebody arrives: every
            // child of its NPCs object off, then one group back on.
            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef
            {
                Source = "vanilla:" + Town,
                GameObjects =
                {
                    new GameObjectDef
                    {
                        Name = "NPCs", Bind = true,
                        Npcs = { new NpcPlacementDef { Npc = "walker", Name = "TownWalker", StartActive = true,
                                                       ActiveConditions = { Is("townNpc", "true") } } },
                    },
                },
            });
            Seed(pack.VanillaExtensions[^1]);
            return pack;
        }

        /// <summary>What the editor does to an extension's bound nodes as it
        /// opens one: each anchored to the object the game has.</summary>
        private static void Seed(VanillaPlaceExtensionDef ext)
        {
            var level = VanillaLevelCatalog.FindLevelByToken(ext.Source)
                        ?? throw new InvalidOperationException("The editor's catalog has no " + ext.Source);
            foreach (var node in ext.GameObjects.Where(n => n.Bind))
                VanillaDelta.Rebase(node, VanillaLevelCatalog.FindNode(level, node.Name)
                                          ?? throw new InvalidOperationException(ext.Source + " has no " + node.Name), level);
        }
    }

    // ── Scenes, photos, wallpapers, the pack's own screens ──────────────

    /// <summary>
    /// <c>suitestory</c>: what a pack shows over the game.
    /// <list type="bullet">
    ///   <item>Scenes <c>photo</c> (painted #8844CC, a Starmaker photo) and
    ///   <c>still</c> (#44CC88, not one); conversation <c>showphoto</c> shows the
    ///   first, <c>showstill</c> the second, <c>hidescenes</c> takes every scene
    ///   away.</item>
    ///   <item>Wallpaper <c>sunset</c> (#E06030), unlocked while <c>wallOk</c> is
    ///   true.</item>
    ///   <item>Screen <c>checkpanel</c> on a canvas of its own: a #224466 panel,
    ///   a button saying "Check Press" that adds 1 to <c>uiClicked</c> - only
    ///   while <c>uiClickOk</c> is true - and a red badge out while
    ///   <c>badgeOn</c> is true. <c>openpanel</c> / <c>closepanel</c> switch it.</item>
    ///   <item>Screens <c>opener</c>, which opens by itself when <c>openerGo</c>
    ///   becomes true ("Opens when"), and <c>nocond</c>, with no conditions,
    ///   which nothing opens.</item>
    ///   <item>The game's quit screen, one of its labels reworded to
    ///   "Check Quit" (see <see cref="PatchedLabel"/>).</item>
    /// </list>
    /// </summary>
    public static class Story
    {
        public const string Id = "suitestory";

        // The game's scenes the pack changes (1.9.0), all ones the game shows.
        /// <summary>Picture, frame and sound (kiss to flash).</summary>
        public const string GameSceneKiss = "4_CG_Manager-Sexy/AmeliaStretching";
        /// <summary>Sound only: silent.</summary>
        public const string GameSceneQuiet = "4_CG_Manager-Sexy/ameliajog";
        /// <summary>Nobody changes it: the control.</summary>
        public const string GameSceneControl = "4_CG_Manager-Sexy/ameliastrip";
        /// <summary>One of the game's photos whose trigger raises no signal: given a kiss.</summary>
        public const string GameSceneNoSignal = "4_CG_Manager-Photos/01_MSmile";
        /// <summary>Made a Starmaker photo.</summary>
        public const string GameScenePhoto = "4_CG_Manager-Sexy/ameliablow";
        /// <summary>The one scene whose art is drawn at half its size: a picture of the
        /// common size must come out the common size.</summary>
        public const string GameSceneBig = "4_CG_Manager-Photos/17_M1Poolpose5";
        /// <summary>Its row in the Starmaker site's gallery (shown by Gallery[16], which
        /// the photo sets), and a row nobody changes (B1_AnnaDowntown (1)'s).</summary>
        public const string GameSceneBigRow = "9_MainCanvas/Starmaker/Starmaker_Core/Gallery/Scrollarea/CoreGallery/Image (15)";
        public const string GalleryControlRow = "9_MainCanvas/Starmaker/Starmaker_Core/Gallery/Scrollarea/CoreGallery/Image (193)";
        /// <summary>Shown by the first line of one of the game's own conversations.</summary>
        public const string GameSceneClean = "4_CG_Manager-Sexy_Two/AnnaCleaning";
        public const string CleanTalk = "8_Core_Events/SmallTalks/Annacleansfloor/GameObject";
        /// <summary>Given a two-frame GIF.</summary>
        public const string GameSceneGif = "4_CG_Manager-Sexy/ameliastrip (1)";
        /// <summary>Given a frame of the pack's own (300x320).</summary>
        public const string GameSceneFrame = "4_CG_Manager-Sexy/ameliastrip (2)";
        /// <summary>Changed by this pack (picture, photo frame, kiss) and by the Talk pack, loaded
        /// later (another picture, silent).</summary>
        public const string GameSceneShared = "4_CG_Manager-Sexy/ameliablow (1)";
        /// <summary>Given a GIF and made a Starmaker photo by this pack, then a still and made a
        /// photo by the Talk pack (D5): the still shows, and both packs record the one photo.</summary>
        public const string GameSceneMoving = "4_CG_Manager-Sexy/ameliablow (2)";
        /// <summary>The scene every pack scene is cloned from, changed by this pack (D5): the
        /// Talk pack's own scene, built after, must not take the change.</summary>
        public const string GameScenePrototype = "4_CG_Manager-Sexy/Samanthabeach";
        /// <summary>Not a photo, but drawn with the picture of one (08_MPark's "Park1"), whose
        /// gallery row must keep the game's picture when this scene is given another (D5).</summary>
        public const string GameSceneSharesPhotoArt = "4_CG_Manager-Sexy/gropepark";
        public const string SharedArtRow = "9_MainCanvas/Starmaker/Starmaker_Core/Gallery/Scrollarea/CoreGallery/Image (7)";
        /// <summary>Nobody changes it: the pack shows it, repaints it and hides it again by where
        /// it sits, in the Scene category (D6), and a condition asks whether it is showing.</summary>
        public const string GameSceneByPath = "4_CG_Manager-Sexy_Four/EmmaXBigfoot (0)";
        /// <summary>The colour a sprite swap paints on a scene (D6).</summary>
        public const string SwapColour = "#55CC22";

        public static ModPack Make(string folder)
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            pack.Variables.Add(Var("never", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("wallOk", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("uiClickOk", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("uiClicked", PackVariableType.Int, "0"));
            pack.Variables.Add(Var("badgeOn", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("openerGo", PackVariableType.Bool, "false"));

            pack.Scenes.Add(new SceneDef { Key = "photo", DisplayName = "Check Photo", StarmakerPhoto = true,
                                           SceneSprite = Png(folder, "Scenes/photo.png", 256, 256, Color.FromRgb(0x88, 0x44, 0xCC)) });
            pack.Scenes.Add(new SceneDef { Key = "still", DisplayName = "Check Still",
                                           SceneSprite = Png(folder, "Scenes/still.png", 256, 256, Color.FromRgb(0x44, 0xCC, 0x88)) });

            // Changes to the game's own scenes (1.9.0), each of one kind, beside
            // GameSceneControl, which nobody changes.
            pack.Scenes.Add(new SceneDef { Source = GameSceneKiss, Key = "gkiss", DisplayName = "Check Game Kiss",
                                           SceneSprite = Png(folder, "Scenes/gkiss.png", 256, 256, Color.FromRgb(0xCC, 0x44, 0x88)),
                                           VanillaFrame = "PhotoFrame.png", Sound = SceneSoundMode.Flash });
            pack.Scenes.Add(new SceneDef { Source = GameSceneQuiet, Key = "gquiet", Sound = SceneSoundMode.Silent });
            pack.Scenes.Add(new SceneDef { Source = GameSceneNoSignal, Key = "gnosig", Sound = SceneSoundMode.Kiss });
            pack.Scenes.Add(new SceneDef { Source = GameScenePhoto, Key = "gphoto", Sound = SceneSoundMode.None, StarmakerPhoto = true });
            pack.Scenes.Add(new SceneDef { Source = GameSceneBig, Key = "gbig", Sound = SceneSoundMode.None,
                                           SceneSprite = Png(folder, "Scenes/gbig.png", 256, 256, Color.FromRgb(0x22, 0x88, 0xCC)) });
            // The rest of what a change can be (D4): shown by one of the game's own
            // conversations, an animated picture, a frame of the pack's own, and one
            // scene the Talk pack (loaded later) changes too.
            pack.Scenes.Add(new SceneDef { Source = GameSceneClean, Key = "gclean", Sound = SceneSoundMode.Flash,
                                           SceneSprite = Png(folder, "Scenes/gclean.png", 256, 256, Color.FromRgb(0x44, 0xAA, 0x22)) });
            pack.Scenes.Add(new SceneDef { Source = GameSceneGif, Key = "ggif", Sound = SceneSoundMode.None,
                                           SceneSprite = Gif(folder, "Scenes/ggif.gif", 64, 64, Color.FromRgb(0xDD, 0x22, 0x22), Color.FromRgb(0x22, 0x22, 0xDD)) });
            pack.Scenes.Add(new SceneDef { Source = GameSceneFrame, Key = "gframe", Sound = SceneSoundMode.None,
                                           CustomFrameSprite = Png(folder, "Scenes/frame.png", 300, 320, Color.FromRgb(0x80, 0x80, 0x80)) });
            pack.Scenes.Add(new SceneDef { Source = GameSceneShared, Key = "gshared", Sound = SceneSoundMode.Kiss, VanillaFrame = "PhotoFrame.png",
                                           SceneSprite = Png(folder, "Scenes/gshared.png", 256, 256, Color.FromRgb(0xAA, 0x66, 0x00)) });
            // What the 1.9.0 review found (D5).
            pack.Scenes.Add(new SceneDef { Source = GameSceneMoving, Key = "gmoving", Sound = SceneSoundMode.None, StarmakerPhoto = true,
                                           SceneSprite = Gif(folder, "Scenes/gmoving.gif", 64, 64, Color.FromRgb(0xAA, 0x22, 0x88), Color.FromRgb(0x22, 0x88, 0xAA)) });
            pack.Scenes.Add(new SceneDef { Source = GameScenePrototype, Key = "gsam", Sound = SceneSoundMode.Flash, StarmakerPhoto = true,
                                           SceneSprite = Png(folder, "Scenes/gsam.png", 256, 256, Color.FromRgb(0x77, 0x33, 0xAA)),
                                           CustomFrameSprite = Png(folder, "Scenes/gsamframe.png", 310, 330, Color.FromRgb(0x60, 0x60, 0x60)) });
            pack.Scenes.Add(new SceneDef { Source = GameSceneSharesPhotoArt, Key = "ggrope", Sound = SceneSoundMode.None,
                                           SceneSprite = Png(folder, "Scenes/ggrope.png", 256, 256, Color.FromRgb(0x33, 0x55, 0xDD)) });

            pack.Wallpapers.Add(new WallpaperDef
            {
                Key = "sunset", DisplayName = "Check Sunset",
                SpritePath = Png(folder, "Wallpapers/sunset.png", 1920, 1080, Color.FromRgb(0xE0, 0x60, 0x30)),
                UnlockConditions = { Is("wallOk", "true") },
            });

            NodeActionDef Show(string kind, string target, bool on)
                => Act(NodeActionTypes.SetGameObjectActive, ("kind", kind), ("target", target), ("active", on ? "true" : "false"));
            pack.Dialogues.Add(Script("showphoto", "The photo.", Show("Scene", "photo", true)));
            pack.Dialogues.Add(Script("showstill", "The still.", Show("Scene", "still", true)));
            pack.Dialogues.Add(Script("hidescenes", "No scenes.", Act(NodeActionTypes.DeactivateAllScenes)));
            pack.Dialogues.Add(Script("showgkiss", "The game's scene.", Show("Scene", "gkiss", true)));
            // One of the game's scenes by where it sits (1.9.0, D6), and a sprite swap on
            // it and on one of the pack's own.
            pack.Dialogues.Add(Script("showgamepath", "The game's scene by where it sits.", Show("Scene", GameSceneByPath, true)));
            string swap = Png(folder, "Scenes/swap.png", 256, 256, Color.FromRgb(0x55, 0xCC, 0x22));
            pack.Dialogues.Add(Script("swapgamepath", "Repainted.",
                Act(NodeActionTypes.SetSprite, ("kind", "Scene"), ("target", GameSceneByPath), ("sprite", swap))));
            pack.Dialogues.Add(Script("swapstill", "Repainted too.",
                Act(NodeActionTypes.SetSprite, ("kind", "Scene"), ("target", "still"), ("sprite", swap))));
            pack.Dialogues.Add(Script("openpanel", "The panel.", Show("UI", "checkpanel", true)));
            pack.Dialogues.Add(Script("closepanel", "No panel.", Show("UI", "checkpanel", false)));

            pack.Uis.Add(new UiDef
            {
                Id = "checkpanel", Name = "Check Panel", HidesWithGameplayUi = false, SortingOrder = 40,
                Nodes =
                {
                    new UiNodeDef
                    {
                        // A picture tinted: an image with no picture is no image,
                        // in the editor's preview and in the game alike.
                        Name = "Panel", Rect = { Size = new[] { 500f, 300f } },
                        Image = new UiImageDef { Sprite = Png(folder, "Ui/panel.png", 8, 8, Colors.White), Tint = "#224466FF" },
                        Children =
                        {
                            new UiNodeDef
                            {
                                Name = "Go", Rect = { Position = new[] { 0f, 40f }, Size = new[] { 300f, 60f } },
                                Text = new UiTextDef { Value = "Check Press", Size = 32f },
                                OnClick = { Add("uiClicked") }, ClickConditions = { Is("uiClickOk", "true") },
                            },
                            new UiNodeDef
                            {
                                Name = "Badge", Rect = { Position = new[] { 0f, -60f }, Size = new[] { 80f, 80f } },
                                Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#FF0000FF" }, ActiveConditions = { Is("badgeOn", "true") },
                            },
                            // In while the game's scene GameSceneByPath is showing (D6).
                            new UiNodeDef
                            {
                                Name = "SceneBadge", Rect = { Position = new[] { 100f, -60f }, Size = new[] { 40f, 40f } },
                                Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#00FF00FF" },
                                ActiveConditions = { new NodeConditionDef { Type = NodeConditionTypes.GameObjectActive,
                                                                            Params = { ["kind"] = "Scene", ["target"] = GameSceneByPath } } },
                            },
                        },
                    },
                },
            });

            // Two small screens: one that opens by its own conditions, and one
            // with none - which must not mean always open.
            UiDef Small(string id, string tint, params NodeConditionDef[] opensWhen)
            {
                var ui = new UiDef
                {
                    Id = id, Name = id, HidesWithGameplayUi = false, SortingOrder = 41,
                    Nodes = { new UiNodeDef { Name = "Panel", Rect = { Size = new[] { 200f, 120f } },
                                              Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = tint } } },
                };
                ui.OpenConditions.AddRange(opensWhen);
                return ui;
            }
            pack.Uis.Add(Small("opener", "#33AA55FF", Is("openerGo", "true")));
            pack.Uis.Add(Small("nocond", "#AA3355FF"));

            // One of the game's own screens, opened as the editor opens one -
            // every object seeded from the game's, bound to it - with one
            // label's words changed. The save keeps only that change.
            var screen = VanillaUiCatalog.Find(GameScreen) ?? throw new InvalidOperationException("No " + GameScreen + " in the editor's catalogue.");
            var root = VanillaUiSeed.FromBase(VanillaUiLibrary.Node(screen)!);
            var labels = Flatten(root).Where(n => n.Text != null && n.Text.Value.Trim().Length > 0 && !n.Bind.Contains('#') && n.Bind != "."
                                                  && n.Bind != DeepLabel && n.Bind != DeepKeptLabel).ToList();
            if (labels.Count < 2) throw new InvalidOperationException(GameScreen + " has fewer than two labels to work with.");
            PatchedLabel = labels[0].Bind;
            KeptLabel = labels[1].Bind;
            KeptWords = labels[1].Text!.Value;
            labels[0].Text!.Value = PatchedWords;
            // Further down: a button's own label reworded two levels down, and
            // an object added three levels down. Each is saved with its path
            // from the screen, as every bound object is.
            Flatten(root).Single(n => n.Bind == DeepLabel).Text!.Value = DeepWords;
            Flatten(root).Single(n => n.Bind == DeepParent).Children.Add(new UiNodeDef
            {
                Name = DeepAdded, Rect = { Size = new[] { 10f, 10f } },
                Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#FF00FFFF" },
            });
            PatchedUi = new UiDef { Id = "quitpatch", Name = "Quit, patched", Source = screen.Token, Nodes = { root } };
            pack.Uis.Add(PatchedUi);

            // Screens of the pack's own, one on a canvas of its own and one on
            // the game's interface, each with an object in the bottom-right
            // corner, one stretched over the screen and one in the middle.
            UiNodeDef Placed(string name, float[] anchorMin, float[] anchorMax, float[] pivot, float[] size) => new()
            {
                Name = name,
                Rect = { AnchorMin = anchorMin, AnchorMax = anchorMax, Pivot = pivot, Size = size },
                Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#20C0C040" },
            };
            foreach (var (id, hides) in new[] { (CornersOwn, false), (CornersGame, true) })
                pack.Uis.Add(new UiDef
                {
                    Id = id, Name = id, HidesWithGameplayUi = hides, SortingOrder = 43,
                    Nodes =
                    {
                        Placed("Corner", new[] { 1f, 0f }, new[] { 1f, 0f }, new[] { 1f, 0f }, new[] { 200f, 100f }),
                        Placed("Fill", new[] { 0f, 0f }, new[] { 1f, 1f }, new[] { 0.5f, 0.5f }, new[] { 0f, 0f }),
                        Placed("Middle", new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 100f, 100f }),
                    },
                });

            // A group turned 90 degrees with two objects on it: one kept
            // upright, one not. And a picture of the pack's drawn tiled,
            // beside the plain ones above.
            var turned = Placed("Turned", new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 120f, 60f });
            turned.Rect.RotationZ = 90f;
            var level = Placed("Level", new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 40f, 20f });
            level.Upright = true;
            turned.Children.Add(level);
            turned.Children.Add(Placed("Along", new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 40f, 20f }));
            var tiles = Placed("Tiles", new[] { 0f, 1f }, new[] { 0f, 1f }, new[] { 0f, 1f }, new[] { 64f, 64f });
            tiles.Image = new UiImageDef { Sprite = Png(folder, "Ui/tile.png", 8, 8, Colors.White), Type = "Tiled", Tint = "#C02020FF" };
            pack.Uis.Single(u => u.Id == CornersOwn).Nodes.AddRange(new[] { turned, tiles });
            pack.Dialogues.Add(Script("opencorners", "Corners.", Show("UI", CornersOwn, true), Show("UI", CornersGame, true)));
            pack.Dialogues.Add(Script("closecorners", "No corners.", Show("UI", CornersOwn, false), Show("UI", CornersGame, false)));

            // Grids of eight 40x40 columns, 4 apart, each sized to what it asks
            // for by a Content Size Fitter: three cells fitted to what they hold,
            // ten fitted, and three in Unity's own grid.
            UiNodeDef Grid(string name, float y, int cells, bool fit)
            {
                var grid = new UiNodeDef
                {
                    Name = name, Rect = { Position = new[] { 0f, y } },
                    Layout = new UiLayoutDef
                    {
                        Kind = UiLayoutKinds.Grid, Constraint = "FixedColumnCount", ConstraintCount = 8,
                        CellSize = new[] { 40f, 40f }, Spacing = new[] { 4f, 4f }, FitToContent = fit,
                    },
                    Components = { new ComponentDef { Type = "ContentSizeFitter",
                                                      Params = { ["horizontalFit"] = "PreferredSize", ["verticalFit"] = "PreferredSize" } } },
                };
                for (int i = 1; i <= cells; i++)
                    grid.Children.Add(new UiNodeDef { Name = "C" + i, Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#C0C02080" } });
                return grid;
            }
            pack.Uis.Add(new UiDef
            {
                Id = Grids, Name = Grids, HidesWithGameplayUi = false, SortingOrder = 44,
                Nodes = { Grid("Fit", 200f, 3, true), Grid("Full", 0f, 10, true), Grid("Unfit", -200f, 3, false) },
            });
            // Card-style buttons, their look a child of the clickable object:
            // one with the game's hover bounce on it, one without.
            pack.Variables.Add(Var("cardClicks", PackVariableType.Int, "0"));
            UiNodeDef Card(string name, string words, float x, bool bounce)
            {
                var card = new UiNodeDef
                {
                    Name = name, Rect = { Position = new[] { x, 0f }, Size = new[] { 128f, 196f } },
                    Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#FFFFFF00" },
                    OnClick = { Add("cardClicks") },
                    Children = { new UiNodeDef { Name = "Face", Rect = { Size = new[] { 120f, 60f } },
                                                 Text = new UiTextDef { Value = words, Size = 28f } } },
                };
                if (bounce)
                    card.Components.Add(new ComponentDef { Type = "UIHoverBounce", Params = { ["bounceType"] = "Scale", ["bounceAmount"] = 1.05,
                                                                                             ["speed"] = 12, ["bounciness"] = 0.3 } });
                return card;
            }
            // One that shrinks while held, on top of the bounce; and three that
            // keep moving: a fade pulse, a sweep, and one left still.
            var pressy = Card("Pressy", "Check Pressy", 0f, true);
            pressy.PressScale = 0.9f;
            UiNodeDef Mover(string name, float y, UiLoopDef? loop) => new()
            {
                Name = name, Rect = { Position = new[] { 0f, y }, Size = new[] { 40f, 10f } },
                Image = new UiImageDef { Sprite = "Ui/panel.png", Tint = "#FFE080FF" }, Loop = loop,
            };
            pack.Uis.Add(new UiDef
            {
                Id = Cards, Name = Cards, HidesWithGameplayUi = false, SortingOrder = 45,
                Nodes =
                {
                    Card("Bouncy", "Check Bouncy", -150f, true), Card("Plain", "Check Plain", 150f, false), pressy,
                    Mover("Glow", -200f, new UiLoopDef { Alpha = new[] { 0.2f, 1f }, Seconds = 1f }),
                    Mover("Shine", -240f, new UiLoopDef { MoveFrom = new[] { -100f, 0f }, MoveTo = new[] { 100f, 0f }, Seconds = 1f }),
                    Mover("Still", -280f, null),
                },
            });
            pack.Dialogues.Add(Script("opencards", "Cards.", Show("UI", Cards, true)));
            pack.Dialogues.Add(Script("closecards", "No cards.", Show("UI", Cards, false)));

            pack.Dialogues.Add(Script("opengrids", "Grids.", Show("UI", Grids, true)));
            pack.Dialogues.Add(Script("closegrids", "No grids.", Show("UI", Grids, false)));

            // Every setting of a UI object on one screen, for the preview to be
            // compared with what the game draws.
            pack.Uis.Add(InGameSuitePreviewSheet.Build());
            pack.Dialogues.Add(Script("opensheet", "Sheet.", Show("UI", InGameSuitePreviewSheet.Id, true)));
            pack.Dialogues.Add(Script("closesheet", "No sheet.", Show("UI", InGameSuitePreviewSheet.Id, false)));
            return pack;
        }

        /// <summary>The screen with grids fitted to what they hold, and one not.</summary>
        public const string Grids = "fitgrids";

        /// <summary>The screen with card-style buttons.</summary>
        public const string Cards = "cards";

        /// <summary>Deeper on the game's screen: a label two levels down the
        /// pack rewords and its neighbour's it leaves, and an object it adds
        /// three levels down.</summary>
        public const string DeepLabel = "Button/Text (TMP)", DeepWords = "Check Deep",
                            DeepKeptLabel = "Button (2)/Text (TMP)", DeepKeptWords = "Main Menu",
                            DeepParent = "Slider/Fill Area/Fill", DeepAdded = "CheckDeepMark";

        /// <summary>The two screens with objects in the corner, stretched and
        /// in the middle: on a canvas of their own, and on the game's interface.</summary>
        public const string CornersOwn = "cornersown", CornersGame = "cornersgame";

        /// <summary>The game's screen the pack changes, a label on it it
        /// rewords, and one it leaves alone (filled in as the pack is made).</summary>
        public const string GameScreen = "vanillaui:9_MainCanvas/Quitagme", PatchedWords = "Check Quit";
        public static string PatchedLabel = "", KeptLabel = "", KeptWords = "";

        /// <summary>That change as the pack holds it, for the preview to draw.</summary>
        public static UiDef? PatchedUi;

        private static IEnumerable<UiNodeDef> Flatten(UiNodeDef node)
            => new[] { node }.Concat(node.Children.SelectMany(Flatten));
    }

    // ── Quests ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>suitequest</c>: quest <c>chase</c> ("Check Chase for {name:josef}" - the
    /// game's Josef, renamed by gameorder: K9), starting once
    /// <c>questGo</c> is true and back to not started while <c>questReset</c> is.
    /// Its tasks in order: <c>first</c>, done once <c>firstDone</c> is true,
    /// adding 1 to <c>rewards</c> as it is; <c>count</c>, counting to 3 with
    /// <c>tally</c>; <c>last</c>, done by a Quest action (conversation
    /// <c>finishlast</c>). And the game's own "Explore The City" (The World Just
    /// Got Bigger) with a task <c>extra</c> added after the game's.
    /// </summary>
    public static class Quests
    {
        public const string Id = "suitequest";

        /// <summary>The game's quest the pack adds <c>extra</c> to.</summary>
        public const string GameQuest = "Explore The City";

        /// <summary>The quest's title as written, and as the journal should read it.</summary>
        public const string ChaseTitle = "Check Chase for {name:josef}";
        public const string ChaseTitleShown = "Check Chase for Joseph";

        public static ModPack Make()
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            pack.Variables.Add(Var("never", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("questGo", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("questReset", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("firstDone", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("tally", PackVariableType.Int, "0"));
            pack.Variables.Add(Var("rewards", PackVariableType.Int, "0"));

            pack.Quests.Add(new QuestDef
            {
                Key = "chase", Title = ChaseTitle, Description = "Chase it down.",
                StartConditions = { Is("questGo", "true") },
                ResetConditions = { Is("questReset", "true") },
                Tasks =
                {
                    new QuestTaskDef { Key = "first", Name = "Find it", Conditions = { Is("firstDone", "true") }, Actions = { Add("rewards") } },
                    new QuestTaskDef { Key = "count", Name = "Count three", CountTo = 3, CountFrom = QuestVocabulary.CountFromVariable, CountVariable = "tally" },
                    new QuestTaskDef { Key = "last", Name = "Finish" },
                },
            });
            // One of the game's own quests, with a task of the pack's added
            // after the game's.
            pack.Quests.Add(new QuestDef
            {
                Key = "explore", Source = GameQuest,
                AddedTasks = { new AddedTaskDef { Key = "extra", Name = "Check the extra stop" } },
            });
            pack.Dialogues.Add(Script("finishlast", "Done.",
                Act(QuestVocabulary.ActionType, (QuestVocabulary.QuestParam, "chase"),
                    (QuestVocabulary.OperationParam, QuestVocabulary.CompleteTask), (QuestVocabulary.TaskParam, "last"))));
            return pack;
        }
    }

    // ── Integration rules ───────────────────────────────────────────────

    /// <summary>
    /// <c>suiterule</c>, every rule run by the game each frame:
    /// <list type="bullet">
    ///   <item><c>rise</c>: 1 more <c>fired</c> each time <c>ruleGo</c> turns true;
    ///   <c>fall</c>: 1 more <c>falls</c> each time it turns false.</item>
    ///   <item><c>pick</c>: <c>mode</c> "a" adds to <c>branchA</c>, "b" to
    ///   <c>branchB</c>, anything else to <c>branchElse</c> - once each time the
    ///   branch that holds changes.</item>
    ///   <item><c>each</c>, for Ann and Bob: <c>go_Ann</c> turning true adds 1 to
    ///   <c>count_Ann</c>, and Bob the same for his.</item>
    ///   <item><c>loaded</c>: set true as the game scene loads.</item>
    /// </list>
    /// </summary>
    public static class Rules
    {
        public const string Id = "suiterule";

        public static ModPack Make()
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            foreach (var (name, type, value) in new[]
            {
                ("ruleGo", PackVariableType.Bool, "false"), ("fired", PackVariableType.Int, "0"), ("falls", PackVariableType.Int, "0"),
                ("mode", PackVariableType.String, ""), ("branchA", PackVariableType.Int, "0"), ("branchB", PackVariableType.Int, "0"),
                ("branchElse", PackVariableType.Int, "0"),
                ("go_Ann", PackVariableType.Bool, "false"), ("go_Bob", PackVariableType.Bool, "false"),
                ("count_Ann", PackVariableType.Int, "0"), ("count_Bob", PackVariableType.Int, "0"),
                ("loaded", PackVariableType.Bool, "false"),
            })
                pack.Variables.Add(Var(name, type, value));

            pack.IntegrationRules.Add(new UpdateRuleDef { Key = "rise", TriggerMode = UpdateRuleTriggerMode.OnRisingEdge,
                                                          Conditions = { Is("ruleGo", "true") }, Actions = { Add("fired") } });
            pack.IntegrationRules.Add(new UpdateRuleDef { Key = "fall", TriggerMode = UpdateRuleTriggerMode.OnFallingEdge,
                                                          Conditions = { Is("ruleGo", "true") }, Actions = { Add("falls") } });
            pack.IntegrationRules.Add(new UpdateRuleDef
            {
                Key = "pick", TriggerMode = UpdateRuleTriggerMode.OnRisingEdge,
                Conditions = { Is("mode", "a") }, Actions = { Add("branchA") },
                Branches =
                {
                    new LevelHookDef { Conditions = { Is("mode", "b") }, Actions = { Add("branchB") } },
                    new LevelHookDef { Actions = { Add("branchElse") } },
                },
            });
            pack.IntegrationRules.Add(new UpdateRuleDef
            {
                Key = "each", TriggerMode = UpdateRuleTriggerMode.OnRisingEdge, ForEach = "Ann,Bob", ForEachAs = "who",
                Conditions = { Is("go_{who}", "true") }, Actions = { Add("count_{who}") },
            });
            pack.IntegrationRules.Add(new UpdateRuleDef
            {
                Key = "loaded", TriggerMode = UpdateRuleTriggerMode.OnSceneLoad,
                Actions = { Act(NodeActionTypes.SetVariable, ("name", "loaded"), ("value", "true")) },
            });
            return pack;
        }
    }

    // ── Dialogues ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>suitetalk</c>, every conversation started by the bridge except
    /// <c>auto</c>:
    /// <list type="bullet">
    ///   <item><c>flow</c>: "Pick one." (1 more <c>started</c> as it starts, 1
    ///   more <c>finished</c> as it ends) offering Left, Right, and Secret -
    ///   the last only while <c>gateOk</c> is true. Each sets <c>took</c>; then
    ///   is followed by "You took [PV:took]." (ids 5, 6, 7) and the end.</item>
    ///   <item><c>jumpy</c>: "One.", then a jump to the line tagged <c>end</c> (a
    ///   top-level line of its own), past "Skipped." (One's next, which would set
    ///   <c>skipped</c>); "End." sets <c>jumped</c>.</item>
    ///   <item><c>dice</c>: one of two lines at random, adding to
    ///   <c>rolledA</c> or <c>rolledB</c>.</item>
    ///   <item><c>auto</c>: starts by itself once <c>autoGo</c> is true, setting
    ///   <c>autoRan</c> (and <c>autoGo</c> back to false).</item>
    ///   <item><c>dress</c>: Bea, a bust of the pack's own, says a line in her
    ///   first outfit (<c>SuiteBeaDay</c>, #3366FF), then one wearing whatever
    ///   <c>beaOutfit</c> names (<c>SuiteBeaNight</c>, #112244).</item>
    ///   <item><c>arrive</c>: starts by itself on arriving at the gas station
    ///   while <c>arriveGo</c> is true, adding 1 to <c>arrived</c> - no box
    ///   checked, so never while a conversation of the game's plays.</item>
    /// </list>
    /// </summary>
    public static class Talk
    {
        public const string Id = "suitetalk";

        public static ModPack Make(string folder)
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            // Two of the game's scenes the Story pack changes too (D4): this pack loads
            // later, so where both set something, this one's shows.
            pack.Scenes.Add(new SceneDef { Source = Story.GameSceneShared, Key = "tshared", Sound = SceneSoundMode.Silent,
                                           SceneSprite = Png(folder, "Scenes/tshared.png", 256, 256, Color.FromRgb(0x00, 0x66, 0xAA)) });
            pack.Scenes.Add(new SceneDef { Source = Story.GameSceneNoSignal, Key = "tnosig", Sound = SceneSoundMode.Flash });
            // D5: a still over the Story pack's GIF, both a photo; and a scene of this pack's
            // own, cloned after the Story pack changed the scene pack scenes are cloned from.
            pack.Scenes.Add(new SceneDef { Source = Story.GameSceneMoving, Key = "tmoving", Sound = SceneSoundMode.None, StarmakerPhoto = true,
                                           SceneSprite = Png(folder, "Scenes/tmoving.png", 256, 256, Color.FromRgb(0x55, 0xAA, 0x33)) });
            pack.Scenes.Add(new SceneDef { Key = "tstill", DisplayName = "Check Talk Still",
                                           SceneSprite = Png(folder, "Scenes/tstill.png", 256, 256, Color.FromRgb(0x33, 0xAA, 0x77)) });
            foreach (var (name, type, value) in new[]
            {
                ("never", PackVariableType.Bool, "false"), ("gateOk", PackVariableType.Bool, "false"),
                ("started", PackVariableType.Int, "0"), ("finished", PackVariableType.Int, "0"),
                ("took", PackVariableType.String, ""), ("skipped", PackVariableType.Bool, "false"),
                ("jumped", PackVariableType.Bool, "false"), ("rolledA", PackVariableType.Int, "0"),
                ("rolledB", PackVariableType.Int, "0"), ("autoGo", PackVariableType.Bool, "false"),
                ("autoRan", PackVariableType.Bool, "false"),
                ("kidA", PackVariableType.Bool, "false"), ("kidB", PackVariableType.Bool, "false"),
                ("exitA", PackVariableType.Bool, "false"), ("exitB", PackVariableType.Bool, "false"),
                ("rootExit", PackVariableType.Bool, "false"), ("rootAfter", PackVariableType.Bool, "false"),
            })
                pack.Variables.Add(Var(name, type, value));

            static DialogueNodeDef Node(int id, string text, params NodeActionDef[] onStart)
            {
                var n = new DialogueNodeDef { Id = id, Text = text };
                n.ActionsOnStart.AddRange(onStart);
                return n;
            }
            static NodeActionDef Set(string name, string value) => Act(NodeActionTypes.SetVariable, ("name", name), ("value", value));

            var flow = new DialogueDef { Key = "flow", DisplayName = "flow", StartConditions = { Is("never", "true") } };
            var pick = Node(1, "Pick one.", Add("started"));
            pick.Kind = DialogueNodeKind.Choice;
            pick.ActionsOnFinish.Add(Add("finished"));
            pick.Children.AddRange(new[] { 2, 3, 4 });
            // An option's actions as the editor offers them: on finish.
            DialogueNodeDef Option(int id, string text, string took)
            {
                var o = Node(id, text);
                o.ActionsOnFinish.Add(Set("took", took));
                return o;
            }
            var secret = Option(4, "Secret", "secret");
            secret.Conditions.Add(Is("gateOk", "true"));
            flow.Nodes.AddRange(new[] { pick, Option(2, "Left", "left"), Option(3, "Right", "right"), secret });
            // Each choice its own line after it: a node is under one parent.
            foreach (var choice in flow.Nodes.Skip(1).ToList())
            {
                choice.Children.Add(choice.Id + 3);
                flow.Nodes.Add(Node(choice.Id + 3, "You took [PV:took]."));
            }
            flow.RootNodeIds.Add(1);
            pack.Dialogues.Add(flow);

            var jumpy = new DialogueDef { Key = "jumpy", DisplayName = "jumpy", StartConditions = { Is("never", "true") } };
            var one = Node(1, "One.");
            one.Jump = new JumpDef { Mode = JumpMode.Jump, TargetTag = "end" };
            one.Children.Add(2);
            var skippedLine = Node(2, "Skipped.", Set("skipped", "true"));
            // The target a line of its own at the top: Game Creator's jump
            // plays the target's plain-line ancestors again after it, so one
            // under the jumping line would come back round to the jump forever.
            var end = Node(3, "End.", Set("jumped", "true"));
            end.Tag = "end";
            jumpy.Nodes.AddRange(new[] { one, skippedLine, end });
            jumpy.RootNodeIds.AddRange(new[] { 1, 3 });
            pack.Dialogues.Add(jumpy);

            // How Game Creator plays a line's children, which the research into the
            // game's variables rests on (Tools/Research/Variables): both children of
            // a plain line, in order? An Exit in the first, does it stop the second?
            // And an Exit in a first root, the second root?
            var kids = new DialogueDef { Key = "kids", DisplayName = "kids", StartConditions = { Is("never", "true") } };
            var parent = Node(1, "Parent.");
            parent.Children.AddRange(new[] { 2, 3 });
            kids.Nodes.AddRange(new[] { parent, Node(2, "First child.", Set("kidA", "true")), Node(3, "Second child.", Set("kidB", "true")) });
            kids.RootNodeIds.Add(1);
            pack.Dialogues.Add(kids);

            var kidExit = new DialogueDef { Key = "kidexit", DisplayName = "kidexit", StartConditions = { Is("never", "true") } };
            var exitParent = Node(1, "Parent.");
            exitParent.Children.AddRange(new[] { 2, 3 });
            var leaving = Node(2, "Leaving.", Set("exitA", "true"));
            leaving.Jump = new JumpDef { Mode = JumpMode.Exit };
            kidExit.Nodes.AddRange(new[] { exitParent, leaving, Node(3, "After the exit.", Set("exitB", "true")) });
            kidExit.RootNodeIds.Add(1);
            pack.Dialogues.Add(kidExit);

            var rootExit = new DialogueDef { Key = "rootexit", DisplayName = "rootexit", StartConditions = { Is("never", "true") } };
            var firstRoot = Node(1, "First root.", Set("rootExit", "true"));
            firstRoot.Jump = new JumpDef { Mode = JumpMode.Exit };
            rootExit.Nodes.AddRange(new[] { firstRoot, Node(2, "Second root.", Set("rootAfter", "true")) });
            rootExit.RootNodeIds.AddRange(new[] { 1, 2 });
            pack.Dialogues.Add(rootExit);

            var dice = new DialogueDef { Key = "dice", DisplayName = "dice", StartConditions = { Is("never", "true") } };
            // A random line is a line like any other - said, then one of its
            // own picked - as the game's are; this one goes on by itself.
            var roll = Node(1, "Rolling.");
            roll.Kind = DialogueNodeKind.Random;
            roll.Duration = NodeDurationMode.Timeout;
            roll.Timeout = 0.3f;
            roll.Children.AddRange(new[] { 2, 3 });
            foreach (var (nid, side) in new[] { (2, "A"), (3, "B") })
            {
                var face = Node(nid, "Side " + side + ".", Add("rolled" + side));
                face.Duration = NodeDurationMode.Timeout;
                face.Timeout = 0.3f;
                dice.Nodes.Add(face);
            }
            dice.Nodes.Insert(0, roll);
            dice.RootNodeIds.Add(1);
            pack.Dialogues.Add(dice);

            // A character with a bust of the pack's own, in two outfits: plain
            // pictures, nothing that blinks, talks or pulls a face.
            pack.Variables.Add(Var("beaOutfit", PackVariableType.String, BeaNight));
            OutfitDef Outfit(string key, string go, Color colour)
            {
                var outfit = new OutfitDef
                {
                    Key = key, GameObjectName = go, BlinkEnabled = false,
                    BaseSprite = Png(folder, "Busts/" + key + ".png", 256, 512, colour),
                    Mouth = { Enabled = false }, Expression = { Enabled = false },
                };
                outfit.Particles.Clear();
                return outfit;
            }
            var bea = new CharacterDef { Key = "bea", Name = "SuiteBea", DisplayName = "Bea", Pronouns = Pronouns.Female, BustSource = BustSource.Pack };
            bea.Outfits.Add(Outfit("day", BeaDay, Color.FromRgb(0x33, 0x66, 0xFF)));
            bea.Outfits.Add(Outfit("night", BeaNight, Color.FromRgb(0x11, 0x22, 0x44)));
            pack.Characters.Add(bea);
            var dress = new DialogueDef { Key = "dress", DisplayName = "dress", StartConditions = { Is("never", "true") } };
            var day = new DialogueNodeDef { Id = 1, Actor = "bea", Text = "Day clothes." };
            day.Children.Add(2);
            var night = new DialogueNodeDef { Id = 2, Actor = "bea", Outfit = "$beaOutfit", Text = "Changed." };
            dress.Nodes.AddRange(new[] { day, night });
            dress.RootNodeIds.Add(1);
            pack.Dialogues.Add(dress);
            // One of the game's characters whose portrait follows ANOTHER
            // character's face number: Chloe's busts copy Charlotte's (found by the
            // variable research, 1.9.0; Shared/VanillaBustWatches.cs).
            var chloe = VanillaCastSeed.Make(VanillaCharacters.Find(Follower) ?? throw new InvalidOperationException("No " + Follower + " among the game's characters."));
            var chloeBust = chloe.Outfits.FirstOrDefault(o => o.GameObjectName == FollowerBust)
                            ?? throw new InvalidOperationException(Follower + " has no outfit on " + FollowerBust + ".");
            pack.Characters.Add(chloe);
            var smile = new DialogueDef { Key = "face", DisplayName = "face", StartConditions = { Is("never", "true") } };
            // A line's Outfit names the bust's object, as Bea's lines do.
            smile.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = chloe.Key, Outfit = chloeBust.GameObjectName, Expression = "Happy", Text = "Smiling." });
            smile.RootNodeIds.Add(1);
            pack.Dialogues.Add(smile);

            // The same speaker in a dialogue that leaves its busts at the end.
            var bow = new DialogueDef { Key = "bow", DisplayName = "bow", LeaveBustsAtEnd = true, StartConditions = { Is("never", "true") } };
            bow.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "bea", Outfit = BeaDay, Text = "Goodbye.", Duration = NodeDurationMode.Timeout, Timeout = 0.5f });
            bow.RootNodeIds.Add(1);
            pack.Dialogues.Add(bow);

            var auto = Script("auto", "By itself.", Set("autoRan", "true"), Set("autoGo", "false"));
            auto.StartConditions.Clear();
            auto.StartConditions.Add(Is("autoGo", "true"));
            pack.Dialogues.Add(auto);

            // On arriving at one of the game's places where the game may start
            // a conversation of its own, every box on the Dialogues tab left
            // unchecked: it plays only while none of the game's is playing.
            pack.Variables.Add(Var("arriveGo", PackVariableType.Bool, "false"));
            pack.Variables.Add(Var("arrived", PackVariableType.Int, "0"));
            var arrive = Script("arrive", "Here already.", Add("arrived"));
            arrive.StartConditions.Clear();
            arrive.StartConditions.Add(new NodeConditionDef
            {
                Type = NodeConditionTypes.LevelActive,
                Params = { ["level"] = "vanilla:" + ArrivalLevel },
            });
            arrive.StartConditions.Add(Is("arriveGo", "true"));
            pack.Dialogues.Add(arrive);

            // Play dialogue: "caller" asks for "target" on its line, "callgame"
            // for one of the game's conversations; each says when it ended.
            pack.Variables.Add(Var("playedTarget", PackVariableType.Int, "0"));
            pack.Variables.Add(Var("afterTarget", PackVariableType.String, ""));
            pack.Variables.Add(Var("afterGame", PackVariableType.String, ""));
            pack.Dialogues.Add(Script("target", "Played for.", Add("playedTarget")));
            pack.Dialogues.Add(Script("caller", "Calling.",
                Act(NodeActionTypes.PlayDialogue, ("source", "pack"), ("dialogue", "target"),
                    ("afterName", "afterTarget"), ("afterValue", "done"))));
            pack.Dialogues.Add(Script("callgame", "Calling the game.",
                Act(NodeActionTypes.PlayDialogue, ("source", "vanilla"), ("conversation", GameConversation),
                    ("afterName", "afterGame"), ("afterValue", "done"))));

            // Z5: one of the game's conversations, extended by one change (a line's
            // timeout), so its lines go into the translation file too.
            var gameLines = new SMSModForge.ViewModel.DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
            gameLines.VanillaSource = VanillaDialogueCatalog.Find(GameLinesConversation);
            var timed = gameLines.Model.Nodes.Single(n => n.Id == GameLineTimed);
            timed.Duration = NodeDurationMode.Timeout;
            timed.Timeout = 2f;
            pack.Dialogues.Add(gameLines.Model);
            return pack;
        }

        /// <summary>Z5: the game's conversation the pack extends; its line the pack's
        /// translation has, in it; a line it does not; the line it changed.</summary>
        public const string GameLinesConversation = "8_Room_Talk/Kitchen/AnnaGropeLove";
        public const int GameLineTranslated = -1831575103;   // "T-Thank you, hun..."
        public const int GameLineLeft = 1550987397;          // "Maybe next time."
        public const int GameLineTimed = -1215123634;
        public const string GameLineInPortuguese = "O-Obrigada, querido...";

        /// <summary>The language the pack carries a translation into, and its
        /// one translated line: <c>flow</c>'s after Right.</summary>
        public const string TranslatedInto = "pt-BR", TranslatedLine = "Você pegou [PV:took].";

        /// <summary>Bea's two outfits' bust objects.</summary>
        public const string BeaDay = "SuiteBeaDay", BeaNight = "SuiteBeaNight";
        /// <summary>A game character whose portrait follows another's face number, and that portrait.</summary>
        public const string Follower = "Chloe", FollowerBust = "Choe_Base";

        /// <summary>Where <c>arrive</c> waits: the gas station, whose room talk
        /// starts a car-wash conversation of the game's on arrival when the car
        /// wash is unlocked and not cooling down, on a Monday (1.8E).</summary>
        public const string ArrivalLevel = "59_Gasstation";

        /// <summary>The game's conversation "callgame" plays: one the suite
        /// already plays (K3), Anna not being in the kitchen.</summary>
        public const string GameConversation = "8_Room_Talk/Kitchen/Annanotthere";

        /// <summary>The pack's translation, made the way the editor makes the
        /// file - every text of the pack listed - with one line filled in.</summary>
        public static void Translate(ModPack pack, string folder)
        {
            var (path, _) = PackTranslations.CreateOrUpdate(pack, folder, TranslatedInto);
            string key = PackTexts.LineKey("flow", "6");
            // Z5: and one of the game's lines in the conversation it extends.
            string gameKey = PackTexts.LineKey(pack.Dialogues.Single(d => d.Source != null && d.Source.EndsWith(GameLinesConversation, StringComparison.Ordinal)).Key,
                                               GameLineTranslated.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var lines = File.ReadAllLines(path)
                            .Select(l => l.StartsWith(key + " ", StringComparison.Ordinal) || l.StartsWith(key + "=", StringComparison.Ordinal)
                                         ? key + " = " + TranslatedLine
                                         : l.StartsWith(gameKey + " ", StringComparison.Ordinal) || l.StartsWith(gameKey + "=", StringComparison.Ordinal)
                                         ? gameKey + " = " + GameLineInPortuguese : l)
                            .ToArray();
            if (!lines.Contains(key + " = " + TranslatedLine)) throw new InvalidOperationException("No " + key + " in " + path);
            if (!lines.Contains(gameKey + " = " + GameLineInPortuguese)) throw new InvalidOperationException("No " + gameKey + " in " + path);
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false));
        }
    }

    // ── What a save keeps ───────────────────────────────────────────────

    /// <summary>
    /// <c>suitesave</c>: <c>keptNumber</c>, <c>keptWords</c> and <c>keptList</c>
    /// are kept by a save; <c>lostNumber</c> is not. <c>dailyCount</c> goes back
    /// to 0 each morning; rule <c>newday</c> sets <c>dayTurned</c> as a day turns.
    /// </summary>
    /// <summary>
    /// The bust shader check (1.9.1, S areas): a character whose art says where
    /// each of its texels is, so every pixel the game draws says which texel it
    /// read, and the editor's port must name the same one. Coded with gaps,
    /// because the game draws through an HDR buffer that gives some 8-bit values
    /// back one lower (measured: red 147, 154, 191... never came back as
    /// themselves): red is x mod 64 and green the row from the top mod 64, each
    /// x4 + 2; blue says which 64-texel block, x16 + 8. A rounding of one level
    /// cannot turn one texel into another. One outfit a channel of the mask, one with all
    /// three, one with none. Blink, mouth and expressions off; an empty border
    /// eight texels wide, as a bust's edges are. And one more outfit,
    /// <see cref="PartsBust"/>, for how the art is loaded: the same art and a
    /// blink of the pack's own (S2).
    /// </summary>
    public static class Looks
    {
        public const string Id = "suitelook";
        public static readonly (string Key, string Bust, Color Mask)[] Outfits =
        {
            ("noise", "SuiteLookNoise", Color.FromArgb(255, 0, 0, 255)),
            ("wave", "SuiteLookWave", Color.FromArgb(255, 0, 255, 0)),
            ("bounce", "SuiteLookBounce", Color.FromArgb(255, 255, 0, 0)),
            ("all", "SuiteLookAll", Color.FromArgb(255, 255, 255, 255)),
            ("still", "SuiteLookStill", Color.FromArgb(0, 0, 0, 0)),
        };
        public const int Size = 256, Border = 8;
        public const string PartsBust = "SuiteLookParts";

        /// <summary>Two flat grey busts, opaque edge to edge (no border, so no
        /// rim): what the game draws over them is what its lights do to a bust,
        /// a multiply and an add per screen pixel (S4).</summary>
        public static readonly (string Key, string Bust, byte Grey)[] Greys =
        {
            ("grey-dark", "SuiteLookGreyDark", 64),
            ("grey-light", "SuiteLookGreyLight", 160),
        };

        /// <summary>A bust shaped like a person - head, neck, shoulders and a
        /// torso cut off by the frame - with see-through space around it and
        /// smooth edges, as a pack's art has: what a bust's shadow and its edge
        /// glow are measured on (S6). Still (no mask), coloured as the art.</summary>
        public const string FigureBust = "SuiteLookFigure";

        public static Color FigureAt(int x, int y)
        {
            int hits = 0;
            for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (InFigure(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4)) hits++;
            if (hits == 0) return Color.FromArgb(0, 0, 0, 0);
            var c = ArtAt(Math.Clamp(x, Border, Size - Border - 1), Math.Clamp(y, Border, Size - Border - 1));
            return Color.FromArgb((byte)(hits * 255 / 16), c.R, c.G, c.B);
        }

        /// <summary>Texel space, rows from the top.</summary>
        private static bool InFigure(double x, double y)
        {
            double dx = x - 128;
            if (dx * dx + (y - 82) * (y - 82) < 40 * 40) return true;              // head
            if (Math.Abs(dx) < 18 && y >= 110 && y <= 140) return true;             // neck
            if (y >= 182) return Math.Abs(dx) < 100 + (y - 182) * 0.15;            // torso
            return y >= 130 && dx * dx / (100.0 * 100) + (y - 182) * (y - 182) / (52.0 * 52) < 1; // shoulders
        }

        public static Color ArtAt(int x, int y)
            => x < Border || y < Border || x >= Size - Border || y >= Size - Border
                ? Color.FromArgb(0, 0, 0, 0)
                : Color.FromArgb(255, (byte)(x % 64 * 4 + 2), (byte)(y % 64 * 4 + 2), (byte)(((x / 64) * 4 + y / 64) * 16 + 8));

        /// <summary>The texel (x, row from the top) a drawn colour names, or null
        /// for a colour that is not one of the art's (the game's interface over it).</summary>
        public static (int x, int y)? TexelOf(byte r, byte g, byte b)
        {
            int xr = (int)Math.Round((r - 2) / 4.0), yr = (int)Math.Round((g - 2) / 4.0), block = (int)Math.Round((b - 8) / 16.0);
            if (xr < 0 || xr > 63 || yr < 0 || yr > 63 || block < 0 || block > 15) return null;
            if (Math.Abs(r - (xr * 4 + 2)) > 1 || Math.Abs(g - (yr * 4 + 2)) > 1 || Math.Abs(b - (block * 16 + 8)) > 3) return null;
            return ((block / 4) * 64 + xr, (block % 4) * 64 + yr);
        }

        public static ModPack Make(string folder)
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            string art = PngOf(folder, "Busts/look.png", Size, Size, ArtAt);
            var look = new CharacterDef { Key = "look", Name = "SuiteLook", DisplayName = "Look", Pronouns = Pronouns.Neutral, BustSource = BustSource.Pack };
            foreach (var (key, bust, mask) in Outfits)
            {
                var outfit = new OutfitDef
                {
                    Key = key, GameObjectName = bust, BlinkEnabled = false, BaseSprite = art,
                    MaskSprite = PngOf(folder, "Busts/" + key + "-mask.png", Size, Size, (_, _) => mask),
                    Mouth = { Enabled = false }, Expression = { Enabled = false },
                };
                outfit.Particles.Clear();
                look.Outfits.Add(outfit);
            }
            var parts = new OutfitDef
            {
                Key = "parts", GameObjectName = PartsBust, BaseSprite = art,
                BlinkSprite = PngOf(folder, "Busts/parts-blink.png", Size, Size, (x, y) => x is >= 100 and < 156 && y is >= 90 and < 100 ? Color.FromRgb(0x20, 0x10, 0x10) : Color.FromArgb(0, 0, 0, 0)),
                Mouth = { Enabled = false }, Expression = { Enabled = false },
            };
            parts.Particles.Clear();
            look.Outfits.Add(parts);
            var figure = new OutfitDef
            {
                Key = "figure", GameObjectName = FigureBust, BlinkEnabled = false,
                BaseSprite = PngOf(folder, "Busts/figure.png", Size, Size, FigureAt),
                Mouth = { Enabled = false }, Expression = { Enabled = false },
            };
            figure.Particles.Clear();
            look.Outfits.Add(figure);
            foreach (var (key, bust, grey) in Greys)
            {
                var flat = new OutfitDef
                {
                    Key = key, GameObjectName = bust, BlinkEnabled = false,
                    BaseSprite = PngOf(folder, "Busts/" + key + ".png", Size, Size, (_, _) => Color.FromRgb(grey, grey, grey)),
                    Mouth = { Enabled = false }, Expression = { Enabled = false },
                };
                flat.Particles.Clear();
                look.Outfits.Add(flat);
            }
            pack.Characters.Add(look);

            // An NPC of the pack's own, shaped as the figure and moved by noise,
            // standing in the PC's room - off until a check turns it on: how the
            // game draws a pack NPC (S9; 1.9.1, the author: "the NPC preview
            // should match the Character one because I assume they behave very
            // similarly. We might need to confirm"). No shadow: the NPC alone.
            pack.Npcs.Add(new NpcDef
            {
                Key = "looknpc", DisplayName = "Look NPC",
                Sprite = PngOf(folder, "Npcs/figure.png", Size, Size, FigureAt),
                Mask = PngOf(folder, "Npcs/figure-mask.png", Size, Size, (_, _) => Color.FromArgb(255, 0, 0, 255)),
                Shadow = { Enabled = false },
            });
            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef
            {
                Source = "vanilla:" + NpcLevel,
                GameObjects =
                {
                    new GameObjectDef
                    {
                        Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
                        Npcs = { new NpcPlacementDef { Npc = "looknpc", Name = NpcName, StartActive = false, SortingOrder = 5,
                                                       Body = new NpcTransform { ScaleX = 2f, ScaleY = 2f } } },
                    },
                },
            });

            // A place of the pack's own: coded art that changes smoothly (red across,
            // green down, in rising and falling ramps), moved by its mask's left half
            // only - the right half is the control: how the game moves a pack place's
            // art (S10; 1.9.1, the author's "full shader check").
            pack.Places.Add(new PlaceDef
            {
                Key = PlaceKey, InternalName = PlaceObject, DisplayName = "Look Place",
                BaseSprite = PngOf(folder, "Places/look.png", PlaceW, PlaceH, PlaceArtAt),
                // A place has both layers (PlaceFactory skips one without); the far one
                // behind the opaque front, flat.
                SecondarySprite = PngOf(folder, "Places/look-back.png", PlaceW, PlaceH, (_, _) => Color.FromRgb(0x30, 0x30, 0x30)),
                MaskSprite = PngOf(folder, "Places/look-mask.png", PlaceMaskW, PlaceMaskH,
                                   (x, _) => x < PlaceMaskW / 2 ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(0, 0, 0, 0)),
            });
            return pack;
        }

        /// <summary>The look place (<see cref="Make"/>): its key, object, art and mask.</summary>
        public const string PlaceKey = "lookplace", PlaceObject = "LookPlace";
        public const int PlaceW = 1024, PlaceH = 576, PlaceMaskW = 256, PlaceMaskH = 144;

        public static Color PlaceArtAt(int x, int y) => Color.FromRgb(Ramp(x, 64), Ramp(y, 48), 128);

        private static byte Ramp(int p, int period)
        {
            int k = p % period, half = period / 2;
            return (byte)((k < half ? k : period - k) * 255 / half);
        }

        /// <summary>The look NPC (<see cref="Make"/>): its object, the level it stands in, its path.</summary>
        public const string NpcName = "SuiteLookNpc", NpcLevel = "5_MyRoom";
        public static string NpcPath => "5_Levels/" + NpcLevel + "/NPCs/" + NpcName;
    }

    public static class Saving
    {
        public const string Id = "suitesave";

        public static ModPack Make()
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            pack.Variables.Add(Var("keptNumber", PackVariableType.Int, "0", persisted: true));
            pack.Variables.Add(Var("keptWords", PackVariableType.String, "", persisted: true));
            pack.Variables.Add(Var("keptList", PackVariableType.List, "", persisted: true));
            pack.Variables.Add(Var("lostNumber", PackVariableType.Int, "0", persisted: false));
            // A new day: one back to its default each morning, and a rule that
            // runs as the day turns.
            var daily = Var("dailyCount", PackVariableType.Int, "0", persisted: true);
            daily.RefreshMode = PackVariableRefreshMode.Daily;
            pack.Variables.Add(daily);
            pack.Variables.Add(Var("dayTurned", PackVariableType.Bool, "false", persisted: true));
            pack.IntegrationRules.Add(new UpdateRuleDef
            {
                Key = "newday", TriggerMode = UpdateRuleTriggerMode.OnDayChange,
                Actions = { Act(NodeActionTypes.SetVariable, ("name", "dayTurned"), ("value", "true")) },
            });
            return pack;
        }
    }

    /// <summary>A conversation of one short line nobody has to click past,
    /// doing its actions as it starts - and never starting on its own: the
    /// bridge starts it (its start condition never holds).</summary>
    public static DialogueDef Script(string key, string text, params NodeActionDef[] actions)
    {
        var d = new DialogueDef { Key = key, DisplayName = key };
        var line = new DialogueNodeDef { Id = 1, Text = text, Duration = NodeDurationMode.Timeout, Timeout = 0.5f };
        line.ActionsOnStart.AddRange(actions);
        d.Nodes.Add(line);
        d.RootNodeIds.Add(1);
        d.StartConditions.Add(Is("never", "true"));
        return d;
    }
}
