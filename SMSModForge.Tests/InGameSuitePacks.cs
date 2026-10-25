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
    public static readonly string[] Ids = { "gamecheck", "gameorder", World.Id, Story.Id, Quests.Id, Rules.Id, Saving.Id, Talk.Id };

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
    ///   <item>The Beach, never visited: <c>BeachMark</c> made on it at 2,1;
    ///   its own <c>Light 2D</c> moved to 0.25,0.5 (a bound node that asks
    ///   to); its own <c>NPCs</c> bound with nothing asked of it but a child,
    ///   <c>NpcsMark</c> - so it must stay exactly where the game has it.</item>
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

            // On the World Map, in the Seaside district, once mapOk is true.
            pack.MapButtons.Add(new MapButtonDef { Target = "self:" + Room, District = "Seaside", Label = "Map Room",
                                                   Conditions = { Is("mapOk", "true") } });

            pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef
            {
                Source = "vanilla:14_Beach",
                NavigatorButtons = { new NavigatorButtonDef { Target = "self:" + Room, Label = "Check Room" } },
                GameObjects =
                {
                    new GameObjectDef { Name = "BeachMark", Sprite = sign, X = 2f, Y = 1f },
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
            var labels = Flatten(root).Where(n => n.Text != null && n.Text.Value.Trim().Length > 0 && !n.Bind.Contains('#') && n.Bind != ".").ToList();
            if (labels.Count < 2) throw new InvalidOperationException(GameScreen + " has fewer than two labels to work with.");
            PatchedLabel = labels[0].Bind;
            KeptLabel = labels[1].Bind;
            KeptWords = labels[1].Text!.Value;
            labels[0].Text!.Value = PatchedWords;
            pack.Uis.Add(new UiDef { Id = "quitpatch", Name = "Quit, patched", Source = screen.Token, Nodes = { root } });
            return pack;
        }

        /// <summary>The game's screen the pack changes, a label on it it
        /// rewords, and one it leaves alone (filled in as the pack is made).</summary>
        public const string GameScreen = "vanillaui:9_MainCanvas/Quitagme", PatchedWords = "Check Quit";
        public static string PatchedLabel = "", KeptLabel = "", KeptWords = "";

        private static IEnumerable<UiNodeDef> Flatten(UiNodeDef node)
            => new[] { node }.Concat(node.Children.SelectMany(Flatten));
    }

    // ── Quests ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>suitequest</c>: quest <c>chase</c> ("Check Chase"), starting once
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
                Key = "chase", Title = "Check Chase", Description = "Chase it down.",
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
    /// </list>
    /// </summary>
    public static class Talk
    {
        public const string Id = "suitetalk";

        public static ModPack Make(string folder)
        {
            var pack = PackRepository.CreateEmpty(Id);
            pack.PackId = Id;
            foreach (var (name, type, value) in new[]
            {
                ("never", PackVariableType.Bool, "false"), ("gateOk", PackVariableType.Bool, "false"),
                ("started", PackVariableType.Int, "0"), ("finished", PackVariableType.Int, "0"),
                ("took", PackVariableType.String, ""), ("skipped", PackVariableType.Bool, "false"),
                ("jumped", PackVariableType.Bool, "false"), ("rolledA", PackVariableType.Int, "0"),
                ("rolledB", PackVariableType.Int, "0"), ("autoGo", PackVariableType.Bool, "false"),
                ("autoRan", PackVariableType.Bool, "false"),
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

            var auto = Script("auto", "By itself.", Set("autoRan", "true"), Set("autoGo", "false"));
            auto.StartConditions.Clear();
            auto.StartConditions.Add(Is("autoGo", "true"));
            pack.Dialogues.Add(auto);
            return pack;
        }

        /// <summary>The language the pack carries a translation into, and its
        /// one translated line: <c>flow</c>'s after Right.</summary>
        public const string TranslatedInto = "pt-BR", TranslatedLine = "Você pegou [PV:took].";

        /// <summary>Bea's two outfits' bust objects.</summary>
        public const string BeaDay = "SuiteBeaDay", BeaNight = "SuiteBeaNight";

        /// <summary>The pack's translation, made the way the editor makes the
        /// file - every text of the pack listed - with one line filled in.</summary>
        public static void Translate(ModPack pack, string folder)
        {
            var (path, _) = PackTranslations.CreateOrUpdate(pack, folder, TranslatedInto);
            string key = PackTexts.LineKey("flow", "6");
            var lines = File.ReadAllLines(path)
                            .Select(l => l.StartsWith(key + " ", StringComparison.Ordinal) || l.StartsWith(key + "=", StringComparison.Ordinal)
                                         ? key + " = " + TranslatedLine : l)
                            .ToArray();
            if (!lines.Contains(key + " = " + TranslatedLine)) throw new InvalidOperationException("No " + key + " in " + path);
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false));
        }
    }

    // ── What a save keeps ───────────────────────────────────────────────

    /// <summary>
    /// <c>suitesave</c>: <c>keptNumber</c>, <c>keptWords</c> and <c>keptList</c>
    /// are kept by a save; <c>lostNumber</c> is not. <c>dailyCount</c> goes back
    /// to 0 each morning; rule <c>newday</c> sets <c>dayTurned</c> as a day turns.
    /// </summary>
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
