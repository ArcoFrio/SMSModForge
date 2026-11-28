using System;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The FX tab (1.10.0): a pack's effects - particles, the social-media points a Starmaker photo gets, a flash,
    /// words that float up - made from a template, set off by an action or by what happens in the game.
    /// <para/>
    /// The author: "an FX tab, where we can create new FX based on templates, such as the social media points that
    /// get displayed whenever a Starmaker photo is on screen, as well as defining when they should appear (and also
    /// adding actions that proc these effects) ... change a vanilla FX, add a new FX based on a vanilla FX template,
    /// or add a new FX with a new ModForge template ... the user should be able to set the textures and parameters
    /// for the particles and effects."
    /// <para/>
    /// Every game template below was set off in the test copy, copied out of where the game keeps it, and looked at
    /// over a room and over nothing (PLAN-1.10.0.md, E2): each draws on its own, away from its level. Left out: what
    /// did not (the splash on scene pictures emits nothing by itself) and what is too small to see at the game's
    /// scale (birds, leaves).
    /// <para/>
    /// Compiled into the plugin, which plays them, and the editor, which offers them. C# 7.3: the plugin's.
    /// </summary>
    public static class Effects
    {
        // ── The manifest ────────────────────────────────────────────────────────────────────────────
        public const string ListKey = "effects";
        public const string FoldersKey = "effectFolders";

        public const string KeyField = "key";
        public const string TemplateField = "template";
        public const string ChangesGameField = "changesGame";
        public const string XField = "x", YField = "y", EndXField = "endX", EndYField = "endY";
        public const string LayerField = "layer";
        public const string ScaleField = "scale";
        public const string TextureField = "texture";
        public const string GameTextureField = "gameTexture";
        public const string ColorField = "color";
        public const string AmountField = "amount";
        public const string SizeField = "size";
        public const string SpeedField = "speed";
        public const string LifetimeField = "lifetime";
        public const string GravityField = "gravity";
        public const string SecondsField = "seconds";
        public const string SpreadField = "spread";
        public const string FloatSecondsField = "floatSeconds";
        public const string TravelSecondsField = "travelSeconds";
        public const string TextField = "text";
        public const string GlowField = "glow";
        public const string PlayWhenField = "playWhen";
        public const string ConditionsField = "conditions";
        public const string UnderField = "under";
        /// <summary>An object's effect (after 1.10.0): how far from the object it plays, in the object's own units -
        /// the effect's local position under it. Empty = at the object.</summary>
        public const string LocalXField = "localX", LocalYField = "localY";

        // ── Actions ─────────────────────────────────────────────────────────────────────────────────
        public const string PlayAction = "PlayEffect";
        public const string StopAction = "StopEffect";
        public const string EffectParam = "effect";

        /// <summary>The object-active condition's "anything under it" (1.10.0): true asks whether something under
        /// the object is on rather than the object itself - one of the game's photos under the photos' manager,
        /// which is always on.</summary>
        public const string UnderParam = "under";

        // ── When an effect plays ────────────────────────────────────────────────────────────────────
        /// <summary>Only when an action plays it.</summary>
        public const string OnAction = "action";
        /// <summary>Every time its conditions become true.</summary>
        public const string WhenTrue = "becomesTrue";
        /// <summary>For as long as its conditions are true.</summary>
        public const string WhileTrue = "whileTrue";
        /// <summary>Every time something under an object turns on - each new Starmaker photo, though the one
        /// before it was on until that moment (which "becomes true" cannot see).</summary>
        public const string WhenUnderTurnsOn = "underTurnsOn";

        public static readonly string[] PlayWhens = { OnAction, WhenTrue, WhileTrue, WhenUnderTurnsOn };

        // ── Where it is drawn ───────────────────────────────────────────────────────────────────────
        /// <summary>In front of the room, behind the characters (sorting layer Default, the room's art is at
        /// -15..-1).</summary>
        public const string Behind = "behind";
        /// <summary>In front of the characters and the photos (sorting layer Foreground). The game's screens are
        /// drawn over the world, so neither covers the dialogue box.</summary>
        public const string Front = "front";

        public static readonly string[] Layers = { Behind, Front };

        public const string BehindSortingLayer = "Default";
        public const string FrontSortingLayer = "Foreground";
        public const int SortingOrder = 0;

        /// <summary>The screen effects' canvas: over the game's own screens' effects (6_Effects/Effect_Canvas is at
        /// 5), under a conversation's box (125).</summary>
        public const int ScreenCanvasOrder = 6;

        /// <summary>The game's camera: orthographic, 5.4 units from the middle to the top (measured, 1.8E).</summary>
        public const float CameraHalfHeight = 5.4f;

        // ── Kinds of template ───────────────────────────────────────────────────────────────────────
        public const string VanillaPrefix = "vanilla:";
        public const string ForgePrefix = "forge:";

        public enum Kind
        {
            /// <summary>Particle systems: one of the game's copied, or one ModForge builds.</summary>
            Particles,
            /// <summary>Pictures that burst out of a point, drift, then fly to another (the social points).</summary>
            Points,
            /// <summary>The screen flashes a colour.</summary>
            Flash,
            /// <summary>Words that float up and fade.</summary>
            Popup,
        }

        /// <summary>The settings a template's effects can change. The editor shows these rows, the runtime reads
        /// these fields; anything else a manifest holds for it is ignored.</summary>
        [Flags]
        public enum Settings
        {
            None = 0,
            Position = 1,
            Layer = 2,
            Scale = 4,
            Texture = 8,
            Color = 16,
            Amount = 32,
            Size = 64,
            Speed = 128,
            Lifetime = 256,
            Gravity = 512,
            Seconds = 1024,
            EndPosition = 2048,
            Spread = 4096,
            Times = 8192,
            Text = 16384,
            Glow = 32768,
        }

        private const Settings Particle = Settings.Position | Settings.Layer | Settings.Scale | Settings.Texture | Settings.Color
                                          | Settings.Amount | Settings.Size | Settings.Speed | Settings.Lifetime | Settings.Gravity
                                          | Settings.Seconds;

        public sealed class Template
        {
            /// <summary>"vanilla:campfire", "forge:burst".</summary>
            public readonly string Id;
            public readonly Kind Kind;
            /// <summary>Where the game keeps it (vanilla), from the scene's root; null for ModForge's own.</summary>
            public readonly string GamePath;
            /// <summary>The editor's group for it (a text key under fx.category.).</summary>
            public readonly string Category;
            /// <summary>Keeps going until stopped (true) or runs its course (false).</summary>
            public readonly bool Loops;
            /// <summary>Drawn behind or in front of the characters unless the author says otherwise.</summary>
            public readonly string DefaultLayer;
            /// <summary>An author can change the game's own (in place) rather than add one like it.</summary>
            public readonly bool CanChangeGames;
            /// <summary>The picture its particles draw, as the game names it - what the editor's preview shows and
            /// what an author's own picture replaces.</summary>
            public readonly string Texture;
            public readonly Settings Settings;

            public Template(string id, Kind kind, string gamePath, string category, bool loops, string defaultLayer,
                            bool canChangeGames, string texture, Settings settings)
            {
                Id = id; Kind = kind; GamePath = gamePath; Category = category; Loops = loops;
                DefaultLayer = defaultLayer; CanChangeGames = canChangeGames; Texture = texture; Settings = settings;
            }

            public bool IsVanilla => Id.StartsWith(VanillaPrefix, StringComparison.Ordinal);

            /// <summary>The id without its prefix: the text keys' name for it (fx.template.campfire).</summary>
            public string Name => Id.Substring(Id.IndexOf(':') + 1);

            public bool Has(Settings s) => (Settings & s) == s;
        }

        // Categories (text keys fx.category.*), in the order the editor lists them.
        public const string Basic = "basic", Light = "light", Weather = "weather", Magic = "magic", Love = "love",
                            Celebration = "celebration", Social = "social", Screen = "screen";

        public static readonly string[] Categories = { Basic, Social, Love, Light, Weather, Magic, Celebration, Screen };

        private static Template V(string name, string path, string category, bool loops, string layer, string texture,
                                  bool canChange = true)
            => new Template(VanillaPrefix + name, Kind.Particles, path, category, loops, layer, canChange, texture, Particle);

        private static Template F(string name, Kind kind, string category, bool loops, string layer, string texture, Settings settings)
            => new Template(ForgePrefix + name, kind, null, category, loops, layer, false, texture, settings);

        public const string FollowerPoints = VanillaPrefix + "followerPoints";
        public const string SubscriberPoints = VanillaPrefix + "subscriberPoints";

        private const Settings PointSettings = Settings.Position | Settings.EndPosition | Settings.Scale | Settings.Texture
                                               | Settings.Color | Settings.Amount | Settings.Spread | Settings.Times;

        /// <summary>Every template, in the order the editor lists them within a category.</summary>
        public static readonly Template[] Catalog =
        {
            // ModForge's own: built from nothing, any picture.
            F("burst", Kind.Particles, Basic, false, Front, "cfxr magic star", Particle | Settings.Glow),
            F("rising", Kind.Particles, Basic, true, Front, "cfxr card heart", Particle | Settings.Glow),
            F("falling", Kind.Particles, Basic, true, Behind, "Default-ParticleSystem", Particle | Settings.Glow),
            F("drifting", Kind.Particles, Basic, true, Behind, "fx_glow1", Particle | Settings.Glow),
            F("points", Kind.Points, Social, false, Front, "cfxr card heart", PointSettings),
            F("flash", Kind.Flash, Screen, false, Front, null, Settings.Color | Settings.Seconds),
            F("popup", Kind.Popup, Screen, false, Front, null, Settings.Position | Settings.Color | Settings.Size | Settings.Seconds | Settings.Text),

            // The social points a Starmaker photo gets (Attractor_Canvas; PhotoBonus sets them off).
            new Template(FollowerPoints, Kind.Points, "Attractor_Canvas/Attractor_Follower", Social, false, Front, true, "Attractor_NewFollower", PointSettings),
            new Template(SubscriberPoints, Kind.Points, "Attractor_Canvas/Attractor_Subscriber", Social, false, Front, true, "Attractor_NewSubscriber", PointSettings),

            V("heartShower", "6_Effects/NewHeartLevelReached/Yeha (1)/Particle System (1)", Love, false, Front, "Heart"),
            V("giftHearts", "Gift_Effect", Love, false, Front, "cfxr card heart"),
            V("loveHeart", "6_Effects/CFXR2 Expression Loving (Timed)", Love, false, Front, "cfxr card heart"),
            V("sparkleRing", "6_Effects/CFXR2 Expression Sparkles (Timed, Alt)", Love, false, Front, "cfxr magic star"),
            V("bigHeart", "Racing_Minigame/Racing_PC/Racing_Hearts", Love, false, Front, "Heart"),

            V("campfire", "5_Levels/51_HikeCamp/CFXR Fire Plain", Light, true, Behind, "cfxr flamme plain"),
            V("stoveFlame", "5_Levels/73_VIlla_Sauna/CFXR Flame", Light, true, Behind, "cfxr flamme blur"),
            V("candle", "5_Levels/5_MyRoom/Candle/CFXR Flame Candle", Light, true, Behind, "cfxr flamme candle"),
            V("lightOrbs", "5_Levels/33_Club/Particle System", Light, true, Behind, "fx_glow1"),
            V("halo", "Barista_Game/TheEnd/Angeleffect", Light, true, Front, "fx_glow1"),

            V("rain", "Weather_System_Inside/Prefab_Rainy_Day/Rain_Core", Weather, true, Behind, "cfxr stretch rain blur", canChange: false),
            V("snow", "Weather_System_Driving/Prefab_Snowy_Day/Snow_Core", Weather, true, Behind, "Default-ParticleSystem", canChange: false),
            V("fog", "5_Levels/53_Hotelroom/Particle System", Weather, true, Behind, "fogcloud1"),
            V("chimneySmoke", "5_Levels/133_HarborDistrict/Particle System", Weather, true, Behind, "cfxr ww smoke bright"),
            V("speedLines", "5_Levels/22_InsideCar/CFXR4 Wind Trails", Weather, true, Front, "Default-ParticleSystem"),
            V("bubbles", "5_Levels/59_Gasstation/gasstation bubbles", Weather, true, Behind, "cfxr bubble 2"),
            V("ripples", "5_Levels/144_HarborSewers/CFXR Water Ripples (Loop)", Weather, true, Behind, "Default-ParticleSystem"),
            V("drip", "2_Bust_Manager/SuiteBeaDay/MBase1/Wet", Weather, true, Front, "Default-ParticleSystem"),
            // Small at the game's scale - what the beach and the outdoor levels show far off, in a place's sky.
            V("seagulls", "5_Levels/14_Beach/Particle System (2)", Weather, true, Behind, "BirdFlapAnim"),
            V("leaves", "5_Levels/99_TropicalBeach/Particle System (1)", Weather, true, Behind, "equip_icon_leaf"),

            V("portal", "5_Levels/5_MyRoom/SuccubusPortalVFX", Magic, true, Behind, "cfxr ring spikes revert"),
            V("goldenAura", "2_Bust_Manager/XXX_Celeste_Bust/D1Base/Celeste_Aura", Magic, true, Front, "fx_circle_line2"),
            V("magicBurst", "4_Bust_Effects/Particle_NyxaraLeaves", Magic, false, Front, "cfxr magic star"),
            V("darkBats", "4_Bust_Effects/Particle_LadyNoire_Leaves", Magic, false, Front, "NoireBats"),
            V("magicPoof", "5_Levels/19_Bar/SecretEgg_4/CFXR Magic Poof", Magic, false, Front, "cfxr star"),
            V("electricBurst", "5_Levels/136_HarborScifiPrison/CFXR Electric Explosion + Text", Magic, false, Front, "cfxr electric spark"),
            V("souls", "5_Levels/80_Shrine/CFXR2 Wandering Souls", Magic, true, Behind, "Default-ParticleSystem"),
            V("sparks", "5_Levels/135_HarborScifiLab/CFXR2 Sparks Rain", Magic, true, Behind, "cfxr stretch trait"),
            V("gasLeak", "5_Levels/135_HarborScifiLab/CFXR Gas Leak", Magic, true, Behind, "cfxr smoke cloud x4"),

            V("fireworks", "5_Levels/3_LivingRoom/NewYearsFireworkLivingRoom/CFXR4 Firework with Glows HDR Shoot Multiple Loop (Random Color)", Celebration, true, Behind, "Default-ParticleSystem"),
            V("paperBurst", "6_Effects/RipPayout_Particle", Celebration, false, Front, "cfxr debris unlit 3x3"),
            V("fightCloud", "5_Levels/81_Abandonedshack/CFXR2 Cartoon Fight Ground (Loop)", Celebration, true, Front, "cfxr smoke cloud x4"),
        };

        public static Template Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in Catalog)
                if (t.Id == id) return t;
            return null;
        }

        /// <summary>The game's pictures a ModForge template can draw instead of a pack's own (the "game picture"
        /// choice), by the name the game gives them.</summary>
        public static readonly string[] GameTextures =
        {
            "Default-ParticleSystem", "fx_glow1", "cfxr magic star", "cfxr star", "cfxr card heart", "Heart",
            "cfxr bubble 2", "cfxr smoke cloud x4", "cfxr ember blur", "cfxr stretch rain blur", "equip_icon_leaf",
            "JapaneseFlowerParticle", "MoneyBillParticlepng", "XPparticle", "Icon_Gems_Glow", "icon_skull",
        };

        // ── Defaults an effect starts with ──────────────────────────────────────────────────────────
        /// <summary>How far the points spread, as a share of the screen's height: the game's followers spread
        /// 10 of the camera's 10.8 units (its hearts 20).</summary>
        public const float DefaultPointsSpread = 0.93f;
        public const float DefaultFloatSeconds = 2f;         // the game's followers
        public const float DefaultTravelSeconds = 1f;
        public const int DefaultPointsCount = 5;             // a photo's followers
        public const float DefaultFlashSeconds = 0.4f;
        public const float DefaultPopupSeconds = 1.6f;

        /// <summary>Where the game's points come from and go to, as fractions of the screen: from the middle (a
        /// photo's), to below the bottom edge.</summary>
        public const float PointsStartX = 0.5f, PointsStartY = 0.5f, PointsEndX = 0.5f, PointsEndY = -0.16f;
    }
}
