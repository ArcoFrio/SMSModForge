using BepInEx;
using GameCreator.Runtime.Dialogue;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Entry point. The plugin gates all real work on the <c>CoreGameScene</c>
    /// being loaded and the bust prototype (<c>Anna_YellowSexy</c> under
    /// <c>2_Bust_Manager</c>) being present. It does not depend on
    /// <c>the host mod</c>: every vanilla GameObject it needs is found
    /// directly through <see cref="GameObject.Find"/> or transform traversal.
    /// <para/>
    /// Authored content (<c>modpack.json</c> per folder) lives under
    /// <c>BepInEx/plugins/SMSModForge/ModPacks/&lt;packId&gt;/</c>. Each pack
    /// can carry busts (<c>characters</c>), custom levels (<c>places</c>),
    /// vanilla-level navigator extensions, dialogues, actors and variables —
    /// matching the schema written by the SMSModForge WPF editor.
    /// </summary>
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGuid = "treboy.starmakerstory.smsmodforge.packplugin";
        public const string pluginName = "SMSModForge Pack Plugin";
        public const string pluginVersion = "1.1.0";

        public static bool loaded;
        public static Scene currentScene;
        public static Plugin Instance { get; private set; }

        /// <summary>
        /// One dispatcher per pack, kept around so per-frame ticks can run
        /// independently. Cleaned up on every scene transition.
        /// </summary>
        private static readonly List<DialogueDispatcher> _dispatchers = new List<DialogueDispatcher>();

        /// <summary>Latched true if the legacy Input manager throws (Input System-only builds).</summary>
        private static bool _conditionDebugKeyBroken;
        private static readonly List<PackContext> _contexts = new List<PackContext>();

        /// <summary>Loaded pack contexts, for the manual-save copy hook
        /// (<see cref="PackManualSaveSync"/>) to enumerate each pack's store.</summary>
        internal static IReadOnlyList<PackContext> LoadedContexts => _contexts;

        /// <summary>
        /// Resolve a loaded pack's variable store by packId, or null when
        /// the pack isn't loaded (or contexts haven't been built yet —
        /// they land in Pass 3, after navigator wiring). Used by
        /// <see cref="NavigatorRuntime"/>'s per-frame condition checks.
        /// </summary>
        internal static PackVariableStore TryGetPackVars(string packId)
        {
            for (int i = 0; i < _contexts.Count; i++)
                if (_contexts[i].PackId == packId) return _contexts[i].Vars;
            return null;
        }

        /// <summary>
        /// Cached DialogueSkin from a vanilla dialogue, reused across all
        /// pack-built dialogues. We harvest it once per CoreGameScene
        /// because shipping a DialogueSkin asset would require an asset
        /// bundle, which the pack format intentionally doesn't ship.
        /// </summary>
        private static DialogueSkin _sharedSkin;

        /// <summary>
        /// Sleep-autosave gate, mirroring the host mod's SaveManager. The
        /// vanilla game commits a save at the END of the post-sleep sequence,
        /// not when the <c>Day</c> number ticks over (that happens earlier,
        /// before the after-sleep events and the actual disk write). The
        /// post-sleep <c>AfterSleepEvents</c> canvas runs first, then the
        /// <c>Saved</c> confirmation UI flashes as the slot is written. We
        /// latch once <see cref="_afterSleepEvents"/> goes active, then commit
        /// when <see cref="_savedUI"/> finishes (its falling edge) — requiring
        /// the latch first is what distinguishes a sleep autosave from a manual
        /// save (which shows the Saved UI with no after-sleep sequence).
        /// <para/>
        /// Committing on the Saved-UI <em>falling</em> edge (rather than when
        /// it first appears) matters because the host mod's own after-sleep
        /// turnover (list rebuilds, counters, …) writes pack variables —
        /// through <c>ModForgeBridge</c> into this very store — while the Saved
        /// flash is up. Flushing after the flash ends guarantees those same
        /// night writes are in the committed file. Both objects are resolved
        /// lazily and cached per CoreGameScene.
        /// </summary>
        private static GameObject _afterSleepEvents;
        private static GameObject _savedUI;
        private static bool _afterSleepProc;
        private static bool _savedUiWasActive;

        /// <summary>True once this sleep's gameplay turnover (daily variable
        /// refresh + OnDayChange arming) has run, so it fires once per sleep
        /// while the Saved UI is up rather than every frame of the flash.</summary>
        private static bool _dayTurnoverProc;

        /// <summary>
        /// The in-game day as read at this sleep's turnover, carried to the
        /// commit that follows a few seconds later. <c>-1</c> when no turnover
        /// is in flight.
        /// <para/>
        /// The weekly backup and the daily autosave have to agree about what
        /// day it is: they are one decision taken at one moment in the host
        /// mod, and re-reading the day at the later commit is how they came
        /// apart.
        /// </summary>
        private static int _sleepDay = -1;

        /// <summary>
        /// True once the sleep autosave has fired this gameplay session (reset
        /// on every scene load). The manual-save copy reads this to decide its
        /// source slot — exactly like the host mod's <c>autosaveProcedThisSession</c>:
        /// after a sleep, manual saves copy from the autosave slot (1); before
        /// any sleep they copy from the loaded slot.
        /// </summary>
        internal static bool AutosaveProcedThisSession;

        /// <summary>
        /// The NanoSave slot the pack stores are bound to, or <c>-1</c> before
        /// binding. Set ONCE per CoreGameScene the first frame the slot reads
        /// valid (see <see cref="TickSaveSlot"/>); reset to <c>-1</c> in
        /// <see cref="OnSceneLoaded"/>. We do not rebind on later mid-scene
        /// changes — mirroring the host mod, which reads the slot once on load.
        /// </summary>
        private static int _lastSeenSlot = -1;

        /// <summary>
        /// Active-state snapshot for every level token any pack's
        /// <c>LevelRandom</c> variable depends on. Built lazily on the
        /// first post-load Tick from each pack's
        /// <see cref="PackVariableStore.EnumerateLevelScopes"/>. The
        /// transition detector lives in
        /// <see cref="TickLevelRefresh"/> and only fires the per-pack
        /// re-roll on an inactive→active edge.
        /// <para/>
        /// One watch entry exists per (packId, token) pair rather than
        /// per token — <c>place:&lt;key&gt;</c> tokens resolve through
        /// <see cref="PlaceRegistry"/> with the declaring pack's id, so
        /// two packs that happen to share a place key wouldn't address
        /// the same level GameObject.
        /// </summary>
        private sealed class LevelScopeWatch
        {
            public PackContext OwnerPack;
            public string Token;
            public GameObject Target;  // resolved once; null if the token never resolves
            public bool WasActive;     // last-frame active state, used for edge detection
        }
        private static readonly List<LevelScopeWatch> _levelWatches = new List<LevelScopeWatch>();
        private static bool _levelWatchesBuilt;

        // ── Cross-plugin pack-variable API ────────────────────────────
        //
        // Other BepInEx plugins (gift UI bridge, schedule bridge,
        // anything that needs to read or react to pack state) talk to
        // ModForge through these methods. The shape mirrors
        // the host mod's SaveManager + Proxy Variables but reads
        // straight from the in-memory PackVariableStore — no
        // GlobalNameVariables asset, no asset bundle dependency.
        //
        // Read methods return the supplied default when the variable
        // doesn't exist or its declared type doesn't parse. Write
        // methods return true when the variable existed and was
        // written, false otherwise (so a caller can detect typos).
        // Subscribers to OnPackVariableChanged see ALL changes for
        // every loaded pack and filter by packId / name themselves.

        /// <summary>
        /// Raised every time a pack variable's stored value actually
        /// changes. Signature: <c>(packId, name, oldValue, newValue)</c>.
        /// Subscribers can query the new value via the typed Get*
        /// methods — by the time the event fires, the value is
        /// already committed.
        /// </summary>
        public static event System.Action<string, string, string, string> OnPackVariableChanged;

        /// <summary>Returns the pack ids currently loaded.</summary>
        public IEnumerable<string> GetLoadedPackIds()
        {
            for (int i = 0; i < _contexts.Count; i++) yield return _contexts[i].PackId;
        }

        /// <summary>True when the named pack is currently loaded.</summary>
        public bool HasPack(string packId)
            => FindContext(packId) != null;

        /// <summary>
        /// The NanoSave slot the named pack's store is currently bound to, or
        /// <c>-1</c> when the pack is unloaded or hasn't bound a slot yet.
        /// Callers (e.g. the host mod's one-time legacy import) wait on this so
        /// they only write after the slot's file has been loaded — a write
        /// before binding would be wiped by the bind's reset-then-load.
        /// </summary>
        public int GetPackActiveSlot(string packId)
        {
            var ctx = FindContext(packId);
            return ctx?.Vars != null ? ctx.Vars.ActiveSlot : -1;
        }

        /// <summary>
        /// Force an immediate write of the named pack's current in-memory state
        /// to its bound slot file. Used by the host mod's one-time legacy import
        /// so migrated values are persisted at once rather than waiting for the
        /// next sleep (otherwise a load-then-quit would lose them, with the
        /// legacy file already renamed). Returns true when a write happened.
        /// </summary>
        public bool FlushPackToDisk(string packId)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null || ctx.Vars.ActiveSlot < 1) return false;
            ctx.Vars.SaveToDisk();
            return true;
        }

        /// <summary>True when the named variable is declared in the named pack.</summary>
        public bool HasPackVariable(string packId, string name)
        {
            var ctx = FindContext(packId);
            return ctx?.Vars != null && ctx.Vars.Exists(name);
        }

        /// <summary>
        /// Enumerate every declared variable name in the named pack.
        /// Useful for diagnostic UIs and the gift-UI bridge that needs
        /// to know which variables exist before subscribing.
        /// </summary>
        public IEnumerable<string> EnumeratePackVariables(string packId)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null) yield break;
            foreach (var n in ctx.Vars.EnumerateNames()) yield return n;
        }

        public string GetPackVariableString(string packId, string name, string defaultValue = "")
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null || !ctx.Vars.Exists(name)) return defaultValue;
            return ctx.Vars.GetString(name);
        }

        public bool GetPackVariableBool(string packId, string name, bool defaultValue = false)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null || !ctx.Vars.Exists(name)) return defaultValue;
            return ctx.Vars.GetBool(name);
        }

        public int GetPackVariableInt(string packId, string name, int defaultValue = 0)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null || !ctx.Vars.Exists(name)) return defaultValue;
            return ctx.Vars.GetInt(name);
        }

        public float GetPackVariableFloat(string packId, string name, float defaultValue = 0f)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null || !ctx.Vars.Exists(name)) return defaultValue;
            return ctx.Vars.GetFloat(name);
        }

        public IReadOnlyList<string> GetPackVariableList(string packId, string name)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null || !ctx.Vars.Exists(name))
                return System.Array.Empty<string>();
            return ctx.Vars.GetList(name);
        }

        /// <summary>
        /// Write a string value through the pack store. Returns true
        /// if the pack was found (the variable doesn't need to be
        /// pre-declared — undeclared writes still land in the
        /// in-memory dict, just without clamping or persistence).
        /// </summary>
        public bool SetPackVariable(string packId, string name, string value)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null) return false;
            ctx.Vars.Set(name, value);
            return true;
        }

        public bool SetPackVariableBool(string packId, string name, bool value)
            => SetPackVariable(packId, name, value ? "true" : "false");

        public bool SetPackVariableInt(string packId, string name, int value)
            => SetPackVariable(packId, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public bool SetPackVariableFloat(string packId, string name, float value)
            => SetPackVariable(packId, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>
        /// Append a value to a List-typed variable. Convenience
        /// wrapper around <see cref="PackVariableStore.ListAdd"/> so
        /// external plugins don't have to know the JSON-array storage
        /// format.
        /// </summary>
        public bool AddToPackList(string packId, string name, string value)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null) return false;
            ctx.Vars.ListAdd(name, value);
            return true;
        }

        public bool RemoveFromPackList(string packId, string name, string value)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null) return false;
            ctx.Vars.ListRemove(name, value);
            return true;
        }

        public bool ClearPackList(string packId, string name)
        {
            var ctx = FindContext(packId);
            if (ctx?.Vars == null) return false;
            ctx.Vars.ListClear(name);
            return true;
        }

        private static PackContext FindContext(string packId)
        {
            if (string.IsNullOrEmpty(packId)) return null;
            for (int i = 0; i < _contexts.Count; i++)
                if (string.Equals(_contexts[i].PackId, packId, System.StringComparison.Ordinal))
                    return _contexts[i];
            return null;
        }

        // ── End cross-plugin API ──────────────────────────────────────

        /// <summary>Root for all SMSModForge plugin data, next to the DLL.</summary>
        public static string DataRoot
        {
            get
            {
                string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(exePath, "SMSModForge");
            }
        }

        public static string PacksRoot => Path.Combine(DataRoot, "ModPacks");

        /// <summary>
        /// Base directory pack saves write into. We park them next to the
        /// vanilla save files — <c>%appdata%/../LocalLow/Arvus Games/Starmaker
        /// Story/Saves</c> — inside each <c>NANOSAVE_xxxx</c> folder so the
        /// game's slot lifecycle (create / overwrite / delete) covers our
        /// state too. The PackVariableStore appends
        /// <c>NANOSAVE_{slot:D4}/SMSModForge_&lt;packId&gt;.json</c> per
        /// slot.
        /// <para/>
        /// Mirrors the path computation in
        /// <c>the host mod's save-path helper</c>: same root,
        /// same per-slot folder, different filename.
        /// </summary>
        public static string SavesRoot
        {
            get
            {
                string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                string localLow = Path.Combine(Path.GetDirectoryName(appData), "LocalLow");
                return Path.Combine(localLow, "Arvus Games", "Starmaker Story", "Saves");
            }
        }

        private void Awake()
        {
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // Node conditions are answered by us rather than by GC2's cloned
            // condition runners — patched once here, before any dialogue exists.
            PackNodeConditions.Install(new HarmonyLib.Harmony("smsmodforge.packplugin"), Logger);
            // Drives the manual-save copy hook (mirrors the host mod's SaveManager
            // NanoSave listeners, but for the pack file). Self-gates until a
            // pack is loaded in CoreGameScene.
            gameObject.AddComponent<PackManualSaveSync>();
            Logger.LogInfo("[SMSModForge.PackPlugin] Awake — waiting for CoreGameScene");
        }

        /// <summary>Plugin log, exposed so sibling components
        /// (<see cref="PackManualSaveSync"/>) can route through the same source.</summary>
        internal static BepInEx.Logging.ManualLogSource Log => Instance != null ? Instance.Logger : null;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            currentScene = scene;
            loaded = false;
            _afterSleepEvents = null;
            _savedUI = null;
            _afterSleepProc = false;
            _savedUiWasActive = false;
            _dayTurnoverProc = false;
            _sleepDay = -1;
            AutosaveProcedThisSession = false;
            // Reset slot tracking so the next CoreGameScene fresh-loads
            // the active slot's file (the player might have switched
            // saves via the main menu).
            _lastSeenSlot = -1;
            VanillaSaveSlot.Reset();
            PlaceRegistry.Reset();
            WeatherRuntime.Reset();
            NavigatorRuntime.Reset();
            RadialButtonRuntime.Reset();
            NavigatorGridSetup.Reset();
            foreach (var d in _dispatchers) d.Cleanup();
            foreach (var c in _contexts)
            {
                c.ActorFactory?.Reset();
                c.Scenes?.Reset();
                c.Wallpapers?.Reset();
                c.Sfx?.Reset();
                c.UpdateRules?.Reset();
                c.DailyChances?.Reset();
                TimerRuntime.ResetPack(c.PackId);
            }
            _dispatchers.Clear();
            _contexts.Clear();
            _sharedSkin = null;
            _levelWatches.Clear();
            _levelWatchesBuilt = false;
            _menuBannerAdded = false;
        }

        /// <summary>
        /// Generic query: is any dialogue (pack or vanilla) currently playing —
        /// including a pack dialogue still inside its fade-in window? Host mods
        /// call this (through their own bridge) to gate their per-frame logic
        /// while a ModForge dialogue runs. ModForge never reaches back into the
        /// host, so the dependency only ever points host → ModForge.
        /// </summary>
        public bool IsAnyDialoguePlaying() => DialogueDispatcher.IsAnyDialoguePlayingGlobal();

        /// <summary>
        /// Inject diagnostic banners onto the main-menu UI, mirroring
        /// the host mod's <c>Core.CreateModHeader</c> but emitting one
        /// line per detected pack so authors can see at a glance
        /// which packs are present and whether each one's manifest
        /// parses. Layout:
        /// <list type="bullet">
        ///   <item>Row 0: "SMSModForge v&lt;version&gt;" (white).</item>
        ///   <item>Row 1+: "• &lt;packId&gt;" per pack — white for
        ///   parses-ok, red for parse error, amber if the row is the
        ///   "no packs detected" placeholder when the folder is
        ///   empty.</item>
        /// </list>
        /// Each row clones the menu TMP prototype so styling carries
        /// across; rows are spaced by a constant Y stride. No-ops
        /// cleanly when the prototype isn't found yet — the menu
        /// hasn't finished loading, so the next Update will retry.
        /// </summary>
        private void TryInjectMenuBanner()
        {
            var prototype = GameObject.Find("Part_One")?.transform
                .Find("Canvas_MM")?.Find("MainMenu")?.Find("Text (TMP)")?.gameObject;
            if (prototype == null) return;
            var menuRoot = prototype.transform.parent;

            try
            {
                var packs = DiscoverPacks();
                // Alphabetical, case-insensitive — the order the rows render in.
                packs.Sort((a, b) => string.Compare(a.DisplayLabel, b.DisplayLabel,
                                                    System.StringComparison.OrdinalIgnoreCase));

                int rowCount = packs.Count == 0 ? 1 : packs.Count;

                // Backdrop first (earlier sibling = drawn behind the text rows):
                // a semi-transparent black panel spanning the header + all rows.
                InjectMenuBackdrop(prototype, menuRoot, rowCount);

                int row = 0;
                // Header row — same cloned font as the vanilla menu text.
                InjectMenuRow(prototype, menuRoot, row++, "Mods", Color.white, 40f);

                if (packs.Count == 0)
                {
                    InjectMenuRow(prototype, menuRoot, row++,
                                  "  (no packs detected)",
                                  new Color(1f, 0.75f, 0.2f, 1f), 28f); // amber
                }
                else
                {
                    // Version gate, same trick as the classic mod headers: the
                    // vanilla menu text ends in the game build ("Build 1.8E"),
                    // and each pack carries the gameVersion the editor stamped
                    // at save time. Mismatch → red row + explicit callout.
                    string vanillaVersion = GetVanillaGameVersion(prototype);

                    foreach (var p in packs)
                    {
                        bool incompatible = p.IsValid &&
                            !string.IsNullOrEmpty(p.GameVersion) &&
                            !string.IsNullOrEmpty(vanillaVersion) &&
                            !string.Equals(p.GameVersion, vanillaVersion, System.StringComparison.OrdinalIgnoreCase);

                        // White when the manifest parsed clean, red when it
                        // failed (broken JSON, missing packId, etc.) or when
                        // the pack targets a different game version.
                        Color colour = p.IsValid && !incompatible ? Color.white : Color.red;
                        string label = "  • " + p.DisplayLabel +
                                       (incompatible ? " -  Incompatible (" + p.GameVersion + ")" : "");
                        InjectMenuRow(prototype, menuRoot, row++, label, colour, 28f);

                        if (incompatible)
                            Logger.LogWarning("[SMSModForge.PackPlugin] Pack '" + p.DisplayLabel +
                                              "' was authored for game version " + p.GameVersion +
                                              " but this game is " + vanillaVersion + ".");
                    }
                }

                Logger.LogInfo("[SMSModForge.PackPlugin] Menu banner: " + row +
                               " row(s) injected (" + packs.Count + " pack(s)).");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("[SMSModForge.PackPlugin] Menu banner inject failed: " + ex.Message);
            }
            _menuBannerAdded = true;
        }

        /// <summary>
        /// Vertical stride between stacked banner rows, in anchored-position
        /// units (the units the menu text prototype uses).
        /// </summary>
        private const float MenuRowStride = 32f;

        /// <summary>Extra vertical room the larger "Mods" header row takes
        /// before the first pack row.</summary>
        private const float MenuHeaderStride = 44f;

        /// <summary>
        /// Base offset of the header row from the prototype text — anchored on
        /// the spot the original host-mod header used
        /// (<c>original.anchoredPosition − (−100, 35)</c> = +100 x, −35 y),
        /// nudged slightly down-right to taste. Rows AND backdrop both key off
        /// this, so adjusting it moves the whole banner as one unit.
        /// </summary>
        private static readonly Vector2 MenuBaseOffset = new Vector2(120, -55);

        /// <summary>
        /// Clone the prototype menu text into a single labelled row at the
        /// given vertical index (row 0 = the header, using the wider
        /// header stride). Pure helper for <see cref="TryInjectMenuBanner"/>.
        /// </summary>
        private void InjectMenuRow(GameObject prototype, Transform menuRoot,
                                    int rowIndex, string text, Color colour, float size)
        {
            var banner = UnityEngine.Object.Instantiate(prototype, menuRoot);
            banner.name = "SMSModForgeBanner_" + rowIndex;

            var tmp = FindTmpText(banner);
            if (tmp != null)
            {
                tmp.GetType().GetProperty("text")?.SetValue(tmp, text);
                tmp.GetType().GetProperty("color")?.SetValue(tmp, colour);
                tmp.GetType().GetProperty("fontSize")?.SetValue(tmp, size);
                tmp.GetType().GetProperty("fontSizeMin")?.SetValue(tmp, 18f);
                tmp.GetType().GetProperty("fontSizeMax")?.SetValue(tmp, 84f);
            }

            var protoRect = prototype.GetComponent<RectTransform>();
            var newRect = banner.GetComponent<RectTransform>();
            if (protoRect != null && newRect != null)
            {
                float y = rowIndex == 0 ? 0f : -(MenuHeaderStride + (rowIndex - 1) * MenuRowStride);
                newRect.anchoredPosition = protoRect.anchoredPosition + MenuBaseOffset + new Vector2(0, y);
                newRect.sizeDelta = new Vector2(420, rowIndex == 0 ? 50 : 36);
            }
        }

        /// <summary>
        /// Semi-transparent black panel behind the banner rows so the text
        /// reads over any menu art. Added BEFORE the rows (UI siblings draw
        /// in order, later on top). Copies the prototype's anchors/pivot so
        /// the same anchored-position arithmetic the rows use lines up.
        /// </summary>
        private void InjectMenuBackdrop(GameObject prototype, Transform menuRoot, int packRows)
        {
            var protoRect = prototype.GetComponent<RectTransform>();
            if (protoRect == null) return;

            var panelGo = new GameObject("SMSModForgeBanner_Backdrop",
                                         typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rect = panelGo.GetComponent<RectTransform>();
            rect.SetParent(menuRoot, false);
            rect.anchorMin = protoRect.anchorMin;
            rect.anchorMax = protoRect.anchorMax;
            rect.pivot = protoRect.pivot;

            // Panel spans from just above the header to just below the last
            // row. Row centres: header at 0, pack row i at
            // -(MenuHeaderStride + i*MenuRowStride) — all relative to the base.
            float top = 30f;                                                   // above header centre
            float bottom = -(MenuHeaderStride + (packRows - 1) * MenuRowStride) - 24f; // below last row centre
            float height = top - bottom;
            float centreY = (top + bottom) / 2f;

            rect.anchoredPosition = protoRect.anchoredPosition + MenuBaseOffset + new Vector2(0, centreY);
            rect.sizeDelta = new Vector2(460, height);

            var img = panelGo.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            img.raycastTarget = false; // never swallow menu clicks
        }

        /// <summary>
        /// The running game's build version, parsed from the vanilla main-menu
        /// text the banner clones — its last space-separated token ("Build
        /// 1.8E" → "1.8E"). Same parse the classic SMSAndroids / SMSGallery
        /// headers used for their red incompatibility tint. Empty string when
        /// the text can't be read (no marking happens then).
        /// </summary>
        private string GetVanillaGameVersion(GameObject prototype)
        {
            try
            {
                var tmp = FindTmpText(prototype);
                string text = tmp != null ? tmp.GetType().GetProperty("text")?.GetValue(tmp) as string : null;
                if (string.IsNullOrEmpty(text)) return "";
                int lastSpace = text.LastIndexOf(' ');
                return (lastSpace >= 0 ? text.Substring(lastSpace + 1) : text).Trim();
            }
            catch { return ""; }
        }

        /// <summary>One pack's discovered identity for the menu banner.</summary>
        private struct DiscoveredPack
        {
            public string DirName;
            public string PackId;
            public bool IsValid;       // manifest parses + has packId
            public string GameVersion; // manifest's gameVersion stamp ("" on pre-stamp packs)
            public string DisplayLabel => string.IsNullOrEmpty(PackId) ? DirName : PackId;
        }

        /// <summary>
        /// Walk every subdirectory of <see cref="PacksRoot"/>, look
        /// for a <c>modpack.json</c>, and try to extract its
        /// <c>packId</c>. Returns one entry per directory — folders
        /// without a manifest are still reported (as invalid) so the
        /// author sees that the folder exists but doesn't match the
        /// expected layout.
        /// </summary>
        private static System.Collections.Generic.List<DiscoveredPack> DiscoverPacks()
        {
            var result = new System.Collections.Generic.List<DiscoveredPack>();
            try
            {
                if (!System.IO.Directory.Exists(PacksRoot)) return result;
                // .smspack scan — every file in ModPacks/ with the right
                // extension is a candidate. We peek the packId out of the
                // archive's modpack.json (via PackArchive.TryOpen) to fill
                // out the menu banner row; archives that fail to open or
                // lack a manifest land in the list with IsValid=false so
                // the author can see the file is there but unrecognised.
                foreach (var smspack in System.IO.Directory.GetFiles(PacksRoot, "*" + PackArchive.FileExtension))
                {
                    var entry = new DiscoveredPack { DirName = System.IO.Path.GetFileNameWithoutExtension(smspack) };
                    var archive = PackArchive.TryOpen(smspack, null);
                    if (archive == null) { result.Add(entry); continue; }
                    try
                    {
                        string text = archive.ReadText(PackArchive.ManifestEntryName);
                        if (text != null)
                        {
                            var json = Newtonsoft.Json.Linq.JObject.Parse(text);
                            entry.PackId = (string)json["packId"] ?? entry.DirName;
                            entry.GameVersion = (string)json["gameVersion"] ?? "";
                            entry.IsValid = !string.IsNullOrEmpty(entry.PackId);
                        }
                    }
                    catch
                    {
                        // Bad JSON inside the archive — IsValid stays false.
                    }
                    finally
                    {
                        archive.Dispose();
                    }
                    result.Add(entry);
                }
            }
            catch { /* I/O errors: just return what we have */ }
            return result;
        }

        /// <summary>
        /// Walk the cloned banner's components for one named
        /// "TextMeshProUGUI" — the type the vanilla menu prototype
        /// uses. Component-name lookup keeps the plugin off a
        /// compile-time TextMeshPro reference (the assembly may live
        /// in a different DLL across Unity versions).
        /// </summary>
        private static Component FindTmpText(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n == "TextMeshProUGUI" || n == "TextMeshPro" || n == "TMP_Text")
                    return c;
            }
            return null;
        }

        /// <summary>
        /// Was the GameStart-scene main-menu banner injected already
        /// for this scene visit? Reset by <see cref="OnSceneLoaded"/>
        /// so leaving and re-entering the menu re-adds it (Unity
        /// destroys our cloned text along with the menu canvas).
        /// </summary>
        private static bool _menuBannerAdded;

        private void Update()
        {
            // The GameStart scene init: poll for the main-menu text
            // prototype to appear, then drop our pack-status banner
            // next to it. Pure diagnostics — same shape as the host mod's
            // CreateModHeader, no per-frame work after the inject.
            if (currentScene.name == "GameStart")
            {
                if (!_menuBannerAdded) TryInjectMenuBanner();
                return;
            }
            if (currentScene.name != "CoreGameScene") return;
            if (loaded)
            {
                // Slot-switch detection first — every variable Tick below
                // reads pack state, so we want the file backing it to
                // match the currently-active NanoSave slot.
                TickSaveSlot();
                TickSleepAutosave();
                TickLevelRefresh();
                NavigatorRuntime.Tick();
                RadialButtonRuntime.Tick();
                // Activate vanilla weather particles on active pack levels that
                // declared a weatherType (Inside/Outside) — the pack-place
                // equivalent of the per-level loop vanilla levels get natively.
                WeatherRuntime.Tick();
                // Fire per-place onEnter/onExit action groups on level
                // activation edges — BEFORE the dialogue dispatchers tick, so
                // variables a hook sets are visible to dialogue conditions on
                // the same frame the level activates.
                for (int i = 0; i < _contexts.Count; i++)
                    LevelHooksRuntime.Tick(_contexts[i]);
                // Per-frame wallpaper unlock-condition re-evaluation —
                // each pack's selector buttons appear the moment their
                // unlock condition flips true.
                for (int i = 0; i < _contexts.Count; i++)
                    _contexts[i].Wallpapers?.Tick(Logger);
                for (int i = 0; i < _dispatchers.Count; i++) _dispatchers[i].Tick();
                // Cross-pack fire: each dispatcher only nominates its best
                // candidate; the actual start happens here after comparing
                // Priority across every loaded pack (ties: pack load order).
                // Without this, an earlier-loaded pack's priority-0 dialogue
                // would always beat a later pack's priority-100 one.
                {
                    DialogueDispatcher fireFrom = null;
                    DialogueBuilder.BuiltDialogue fireWhat = null;
                    for (int i = 0; i < _dispatchers.Count; i++)
                    {
                        var c = _dispatchers[i].PeekEligible();
                        if (c != null && (fireWhat == null || c.Priority > fireWhat.Priority))
                        {
                            fireWhat = c;
                            fireFrom = _dispatchers[i];
                        }
                    }
                    if (fireFrom != null) fireFrom.FireEligible(fireWhat);
                }
                // After dialogues, run integration rules — any
                // variable a dialogue node just mutated is visible to
                // the rule's conditions on the same frame.
                for (int i = 0; i < _contexts.Count; i++)
                    _contexts[i].UpdateRules?.Tick(_contexts[i], Logger);

                // Then the condition-gated GameObjects, so an object whose
                // gate reads a variable a rule just wrote settles on the same
                // frame rather than a frame late.
                for (int i = 0; i < _contexts.Count; i++)
                    _contexts[i].Gates?.Tick(_contexts[i], Logger);

                // F12 → dump condition state for every dialogue flagged
                // "Set for condition debugging" in the editor. Legacy Input
                // (available in this game); if a future build disables the
                // legacy manager, fail once and stop probing.
                if (!_conditionDebugKeyBroken)
                {
                    try
                    {
                        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F12))
                            for (int i = 0; i < _dispatchers.Count; i++)
                                _dispatchers[i].DumpConditionDebug();
                    }
                    catch (System.Exception)
                    {
                        _conditionDebugKeyBroken = true;
                        Logger.LogWarning("[SMSModForge.PackPlugin] Legacy Input unavailable — F12 condition debugging disabled.");
                    }
                }
                return;
            }

            var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
            var baseBust = bustManager?.Find("Anna_YellowSexy")?.gameObject;
            if (bustManager == null || baseBust == null) return;

            try { LoadAllPacks(bustManager, baseBust); }
            catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] LoadAllPacks failed: " + ex); }
            finally
            {
                loaded = true;
                Logger.LogInfo("----- SMSMODFORGE PACK PLUGIN LOADED -----");
            }
        }

        /// <summary>
        /// Sprite-focus enforcement lives in LateUpdate on purpose: the
        /// vanilla <c>SpriteManager</c> on <c>2_Bust_Manager</c> re-assigns
        /// its registered busts' sorting orders from its own <c>Update</c>
        /// (<c>UpdateSortingOrders</c> / <c>ApplySortingOrder</c>), stomping
        /// the focus bump. Re-applying after every Update — but before
        /// rendering — means the focused orders are what actually draw.
        /// </summary>
        private void LateUpdate()
        {
            if (!loaded || currentScene.name != "CoreGameScene") return;
            for (int i = 0; i < _contexts.Count; i++)
                _contexts[i].Actors?.EnforceSpriteFocus();
        }

        /// <summary>
        /// Commit pack variables when the player sleeps — the one moment the
        /// vanilla game writes a save, and the mod's <em>daily autosave</em>.
        /// <para/>
        /// The trigger is deliberately NOT the in-game <c>Day</c> number
        /// changing: that ticks over earlier in the sleep sequence, well
        /// before the after-sleep events and the actual disk write, so saving
        /// on it commits a stale snapshot (anything the player did right
        /// before bed is missed) at the wrong time. Instead we reproduce
        /// the host mod's SaveManager gate exactly — latch when the post-sleep
        /// <c>AfterSleepEvents</c> canvas activates, then commit the instant
        /// the <c>Saved</c> confirmation UI appears. Requiring the after-sleep
        /// latch first means a manual mid-day save (which flashes the Saved UI
        /// on its own) does NOT trigger a pack commit, matching vanilla, where
        /// progress only persists on sleep.
        /// <para/>
        /// On the commit, for every pack we (1) apply Daily / DailyRandom
        /// refresh policies, (2) flush the persisted slice to disk — capturing
        /// everything the player did up to the sleep — and (3) arm OnDayChange
        /// integration rules (they fire on a following Tick if their
        /// conditions also pass).
        /// </summary>
        private void TickSleepAutosave()
        {
            // Resolve + cache the two gate objects. Both normally sit inactive,
            // so we reach them through an active parent + Transform.Find
            // (GameObject.Find skips inactive objects). Paths match
            // the host mod's Core: 9_MainCanvas/AfterSleepEvents and
            // 6_Effects/Effect_Canvas/Game_Saved/Saved.
            if (_afterSleepEvents == null)
            {
                var mainCanvas = GameObject.Find("9_MainCanvas");
                _afterSleepEvents = mainCanvas != null
                    ? mainCanvas.transform.Find("AfterSleepEvents")?.gameObject : null;
            }
            if (_savedUI == null)
            {
                var effects = GameObject.Find("6_Effects");
                _savedUI = effects != null
                    ? effects.transform.Find("Effect_Canvas/Game_Saved/Saved")?.gameObject : null;
            }
            if (_afterSleepEvents == null || _savedUI == null) return;

            // Stage 1 — the after-sleep sequence has begun; latch it so the
            // upcoming Saved-UI flash is attributed to a sleep, not a manual save.
            if (_afterSleepEvents.activeSelf && !_afterSleepProc)
                _afterSleepProc = true;

            bool savedActive = _savedUI.activeSelf;

            // Stage 2 — GAMEPLAY turnover, the moment the Saved UI appears.
            //
            // This is the host mod's gate exactly: SaveManager latches on
            // afterSleepEvents and runs its turnover on `savedUI.activeSelf`,
            // not on the flash ending. Doing our day-change work at the END of
            // the flash instead left several seconds in which the player has
            // control while every Daily variable still holds yesterday's value
            // — long enough to walk into a room, or open the map, and act on
            // state that is about to change underneath them.
            //
            // Nothing the host writes here is a Daily variable, so running the
            // refresh alongside its turnover cannot clobber it.
            if (savedActive && _afterSleepProc && !_dayTurnoverProc)
            {
                _dayTurnoverProc = true;

                // Read the day HERE, where the host mod reads it, and carry the
                // answer to the commit below. Deciding "is it Monday" at the
                // falling edge instead meant the weekly backup was gated on a
                // reading taken seconds later than the daily one it is supposed
                // to accompany — the two could disagree, and slot 2 would be
                // skipped on a day slot 1 was written.
                _sleepDay = (int)GameVariableBridge.GetNumber("Day");
                foreach (var c in _contexts)
                {
                    if (c.Vars == null) continue;
                    c.Vars.RefreshOnDayChange();  // Daily / DailyRandom refresh policies
                    // Let OnDayChange integration rules know an event happened.
                    // They fire on the next Tick if their conditions also pass —
                    // which is now while the flash is still up, so a schedule has
                    // repopulated before the player can move.
                    c.UpdateRules?.ArmOneShots(UpdateRulesRegistry.TriggerMode.OnDayChange);
                    // Report the new day's DailyChance outcomes. Nothing is
                    // rolled here: the values are derived, so this just prints
                    // what every DailyChance condition will evaluate to today.
                    LogDailyChances(c);
                }
            }

            // Stage 3 — commit on the Saved-UI falling edge (was showing, now
            // gone). The DISK write stays late on purpose: the host mod's own
            // turnover writes pack variables through ModForgeBridge into this
            // store while the flash is up, and flushing afterwards is what
            // guarantees those night writes are in the committed file.
            bool fallingEdge = _savedUiWasActive && !savedActive;
            _savedUiWasActive = savedActive;
            if (!(fallingEdge && _afterSleepProc)) return;
            _afterSleepProc = false;
            _dayTurnoverProc = false;
            AutosaveProcedThisSession = true;

            // The vanilla autosave always targets the dedicated autosave slot
            // (slot 1), NOT the loaded slot — that's what "Continue" reloads
            // and what the player inspects as "the autosave". the host mod does
            // the same (SaveToFile(1), plus a Monday backup to slot 2). The
            // loaded slot is frozen by vanilla until a manual save, so writing
            // our pack file only to slot 1 keeps it in lockstep with the
            // vanilla + the host mod saves.
            // Day as read at the turnover, not re-read now — see _sleepDay.
            // Falling back to a fresh read keeps the commit correct if the
            // turnover stage was somehow skipped, rather than treating the
            // sentinel as a day number and silently dropping the backup.
            int day = _sleepDay >= 0 ? _sleepDay : (int)GameVariableBridge.GetNumber("Day");
            bool monday = day == 1;
            foreach (var c in _contexts)
            {
                if (c.Vars == null) continue;
                // Refresh already happened at stage 2, with the host mod's own
                // turnover — this is purely the disk commit.
                c.Vars.SaveToSlot(1);             // the autosave slot
                if (monday) c.Vars.SaveToSlot(2); // Monday backup, mirroring SaveToFile(2)
            }
            _sleepDay = -1;

            Logger.LogInfo("[SMSModForge.PackPlugin] Player slept (now day " + day +
                           ") — autosave: pack variables committed to slot 1" +
                           (monday ? " (+ Monday backup to slot 2)." : "."));
        }

        /// <summary>
        /// Print every DailyChance gate's outcome for the current in-game
        /// day, mirroring the DailyRandom re-roll lines the variable store
        /// logs. Uses the same <see cref="ConditionEvaluator.StableRoll"/> the
        /// conditions call, so what's printed is exactly what they'll compare
        /// against all day.
        /// </summary>
        private void LogDailyChances(PackContext c)
        {
            if (c?.DailyChances == null || c.DailyChances.All.Count == 0) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int rollDay = ConditionEvaluator.CurrentRollDay();
            foreach (var e in c.DailyChances.All)
            {
                float roll = ConditionEvaluator.StableRoll(c.PackId, e.Id, rollDay, c.Vars?.RollSeed ?? 0) * 100f;
                Logger.LogInfo("[SMSModForge.PackPlugin] " + c.PackId + ": DailyChance " + e.Label +
                               " (" + e.Percent.ToString("0.##", inv) + "%) — rolled " +
                               roll.ToString("0.#", inv) + "% → " + (roll < e.Percent ? "PASS" : "fail") +
                               " for day " + rollDay + ".");
            }
        }

        /// <summary>
        /// Bind the pack stores to the active NanoSave slot ONCE per
        /// CoreGameScene — the first frame the slot reads valid — mirroring
        /// the host mod's SaveManager, which reads <c>SlotLoaded</c> a single
        /// time in its load coroutine and never rebinds afterwards.
        /// <see cref="PackVariableStore.SetActiveSlot"/> resets each store to
        /// declared defaults then overlays the slot's persisted file.
        /// <para/>
        /// Crucially we do NOT flush the in-memory store on slot changes, nor
        /// rebind on a mid-scene <c>SlotLoaded</c> change. In vanilla, mod
        /// progress persists only at sleep (autosave → slot 1) and a manual
        /// save copies the last committed file; writing the live store anywhere
        /// else would leak uncommitted mid-day changes onto disk — that was the
        /// bug where a variable set before sleeping appeared in a manual save
        /// (a manual save can momentarily move <c>SlotLoaded</c>, which the old
        /// per-frame flush wrote out). Loading a different save reloads the
        /// scene, which resets the latch in <see cref="OnSceneLoaded"/> and
        /// rebinds here.
        /// </summary>
        private void TickSaveSlot()
        {
            if (_lastSeenSlot > 0) return;       // already bound this scene
            int slot = VanillaSaveSlot.Current;
            if (slot <= 0) return;               // slot not ready yet — wait

            foreach (var c in _contexts)
            {
                if (c.Vars == null) continue;
                try { c.Vars.SetActiveSlot(slot); }
                catch (System.Exception ex)
                {
                    Logger.LogError("[SMSModForge.PackPlugin] SetActiveSlot(" + slot +
                                    ") failed for pack " + c.PackId + ": " + ex.Message);
                }
            }

            _lastSeenSlot = slot;
            Logger.LogInfo("[SMSModForge.PackPlugin] Bound pack saves to slot " + slot +
                           " (NANOSAVE_" + slot.ToString("D4") + ").");
        }

        /// <summary>
        /// Per-frame poll for every level token any pack's
        /// <c>LevelRandom</c> variable depends on. On an inactive→active
        /// edge the owning pack's <see cref="PackVariableStore.RefreshOnLevelEnter"/>
        /// is fired, re-rolling every variable scoped to that token. This is
        /// the generalised port of the host mod's <c>Places.Update</c> rolls
        /// (<c>MyDailyRandom</c> — both keyed
        /// on the <em>level</em> GameObject's <c>activeSelf</c>, not the
        /// roomtalk): the rolled number sticks for the visit, the next
        /// entry rolls fresh.
        /// </summary>
        private void TickLevelRefresh()
        {
            if (!_levelWatchesBuilt) BuildLevelWatches();
            if (_levelWatches.Count == 0) return;

            for (int i = 0; i < _levelWatches.Count; i++)
            {
                var w = _levelWatches[i];
                if (w.Target == null) continue;          // unresolved token — skipped quietly
                bool nowActive = w.Target.activeInHierarchy;
                if (nowActive && !w.WasActive)
                {
                    // inactive → active edge: re-roll only the owning pack's
                    // matching variables. Per-pack scoping avoids stomping
                    // a sibling pack that re-uses the same place key.
                    w.OwnerPack.Vars?.RefreshOnLevelEnter(w.Token);
                }
                w.WasActive = nowActive;
            }
        }

        /// <summary>
        /// One-time scan after dialogue runtime build: collect each pack's
        /// level-scope tokens and resolve each to its level GameObject
        /// (under <c>5_Levels</c>). Resolution mirrors the
        /// <c>LevelActive</c> condition: <c>place:&lt;key&gt;</c> goes
        /// through <see cref="PlaceRegistry"/> with the declaring pack's
        /// id, <c>vanilla:&lt;goName&gt;</c> resolves to a direct child
        /// of <c>5_Levels</c>, and bare tokens are accepted for legacy /
        /// hand-edited manifests. Initial <c>WasActive</c> is seeded from
        /// the current state so a scope already active at load time
        /// doesn't trigger a spurious re-roll (Declare already rolled).
        /// </summary>
        private void BuildLevelWatches()
        {
            _levelWatches.Clear();
            var level5 = GameObject.Find("5_Levels")?.transform;
            foreach (var c in _contexts)
            {
                if (c.Vars == null) continue;
                foreach (var token in c.Vars.EnumerateLevelScopes())
                {
                    var go = ResolveLevelTarget(token, c.PackId, level5);
                    _levelWatches.Add(new LevelScopeWatch
                    {
                        OwnerPack = c,
                        Token = token,
                        Target = go,
                        WasActive = go != null && go.activeInHierarchy,
                    });
                    if (go == null)
                        Logger.LogWarning("[SMSModForge.PackPlugin] LevelRandom variable in '" +
                                          c.PackId + "' refers to unresolved level scope '" +
                                          token + "' — re-rolls disabled.");
                }
            }
            _levelWatchesBuilt = true;
        }

        /// <summary>
        /// Translate a level token (<c>vanilla:&lt;goName&gt;</c> /
        /// <c>place:&lt;key&gt;</c> / bare name) into the actual level
        /// GameObject under <c>5_Levels</c>. Same shape as the
        /// <c>LevelActive</c> condition evaluator so authoring the same
        /// token in both places resolves to the same level.
        /// <para/>
        /// Exposed <c>internal</c> so the <c>TransitionLevels</c> action
        /// in <see cref="ActionRuntime"/> can resolve its source and
        /// destination tokens through the same path.
        /// </summary>
        internal static GameObject ResolveLevelTarget(string token, string thisPackId, Transform level5)
        {
            if (string.IsNullOrEmpty(token) || level5 == null) return null;
            int colon = token.IndexOf(':');
            if (colon > 0 && colon < token.Length - 1)
            {
                string resolve = token.StartsWith("place:")
                    ? "self:" + token.Substring("place:".Length)
                    : token;
                var entry = PlaceRegistry.Resolve(resolve, thisPackId, level5);
                if (entry?.Level != null) return entry.Level;
                return null;
            }
            // Bare GO name under 5_Levels (back-compat).
            var t = level5.Find(token);
            return t != null ? t.gameObject : null;
        }

        private void LoadAllPacks(Transform bustManager, GameObject baseBust)
        {
            if (!Directory.Exists(PacksRoot))
            {
                Logger.LogInfo("[SMSModForge.PackPlugin] No ModPacks folder at " + PacksRoot + " — nothing to load.");
                return;
            }

            _sharedSkin = HarvestVanillaDialogueSkin();

            var manifests = new List<PackManifest>();
            foreach (var smspack in Directory.GetFiles(PacksRoot, "*" + PackArchive.FileExtension))
            {
                var manifest = PackManifest.TryLoad(smspack, Logger);
                if (manifest != null) manifests.Add(manifest);
            }
            if (manifests.Count == 0)
            {
                Logger.LogInfo("[SMSModForge.PackPlugin] No *" + PackArchive.FileExtension + " files in " + PacksRoot + " — nothing to load.");
                return;
            }

            // Pass 1 — busts.
            foreach (var m in manifests)
            {
                try { BustFactory.BuildAll(m, bustManager, baseBust, Logger); }
                catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Busts failed in " + m.PackId + ": " + ex); }
            }

            // Refresh the SpriteManager once after all busts are built.
            var spriteManager = bustManager.GetComponent("SpriteManager");
            if (spriteManager != null)
            {
                var refresh = spriteManager.GetType().GetMethod("RefreshCache");
                refresh?.Invoke(spriteManager, null);
            }

            // Pass 2a — places. Builds each place's whole GameObject tree,
            // including the level NPCs nested in it (their jiggle material is
            // cloned from the same base bust used above, so no shader ships in
            // the pack).
            foreach (var m in manifests)
            {
                try { PlaceFactory.BuildAll(m, baseBust, Logger); }
                catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Places failed in " + m.PackId + ": " + ex); }
            }

            // Pass 2b — navigator buttons.
            foreach (var m in manifests)
            {
                try { NavigatorRuntime.WireAll(m, Logger); }
                catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Navigator wiring failed in " + m.PackId + ": " + ex); }
            }

            // Pass 2c — World Map radial buttons (e.g. the host mod-style
            // a place button in the Foundry district). Runs after
            // navigator wiring because both resolve targets through the
            // same PlaceRegistry.
            foreach (var m in manifests)
            {
                try { RadialButtonRuntime.WireAll(m, Logger); }
                catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Radial button wiring failed in " + m.PackId + ": " + ex); }
            }

            // Pass 2d — extended navigator bar grid. Deferred via coroutine
            // (waits 2 frames) so it detects an existing custom grid from
            // the host mod and skips if one is present.
            NavigatorGridSetup.EnsureGrid(this);

            // Pass 3 — variables, actors, dialogues. Each pack gets its
            // own PackContext + dispatcher; downstream actions reference
            // it directly so cross-pack state never leaks.
            foreach (var m in manifests)
            {
                try { BuildDialogueRuntimeFor(m); }
                catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Dialogue runtime failed in " + m.PackId + ": " + ex); }
            }
        }

        private void BuildDialogueRuntimeFor(PackManifest m)
        {
            var ctx = new PackContext
            {
                PackId = m.PackId,
                Log = Logger,
                Plugin = this,
                Vars = new PackVariableStore(m.PackId, SavesRoot, Logger),
                Actors = new ActorRegistry(Logger),
                ActorFactory = new RuntimeActorFactory(Logger),
                Scenes = new SceneRegistry(),
                Wallpapers = new WallpaperRegistry(),
                Sfx = new SfxRegistry(),
                UpdateRules = new UpdateRulesRegistry(),
                DailyChances = new DailyChanceRegistry(),
                // Populated during the place build, which ran before this.
                Gates = GameObjectGateRegistry.ForPack(m.PackId),
            };
            // Index DailyChance conditions wherever they appear in the
            // manifest so the day-change hook can report the day's rolls.
            // Report-only: the conditions themselves derive their result and
            // don't consult this.
            try { ctx.DailyChances.CollectFrom(m.Root); }
            catch (System.Exception ex) { Logger.LogWarning("[SMSModForge.PackPlugin] DailyChance scan failed in " + m.PackId + ": " + ex.Message); }

            // Forward this pack's variable-change notifications into
            // the cross-plugin event. Subscribers see (packId, name,
            // oldValue, newValue) and can filter by pack / name.
            string forwardPackId = m.PackId;
            ctx.Vars.ValueChanged += (name, oldVal, newVal) =>
            {
                try { OnPackVariableChanged?.Invoke(forwardPackId, name, oldVal, newVal); }
                catch (System.Exception ex)
                {
                    Logger.LogWarning("[SMSModForge.PackPlugin] OnPackVariableChanged " +
                                      "subscriber threw: " + ex.Message);
                }
            };

            // Build scenes before dialogues so ActivateScene actions resolve.
            // SceneFactory tolerates the absence of a "scenes" key.
            try { SceneFactory.BuildAll(m, ctx.Scenes, Logger); }
            catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Scene build failed in " + m.PackId + ": " + ex); }

            // Build wallpapers — selector buttons start hidden and the
            // per-frame Tick reveals each one once its unlock condition
            // passes. Done before dialogues so an ActivateScene authored
            // alongside a wallpaper isn't accidentally ordering-sensitive.
            try { WallpaperFactory.BuildAll(m, ctx.Wallpapers, ctx.Vars, Logger); }
            catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Wallpaper build failed in " + m.PackId + ": " + ex); }

            // Build music tracks. Audio loading is async on a
            // coroutine — the GO exists immediately so SwitchMusic
            // can find it; the clip lands a frame or two later. The
            // plugin instance hosts the coroutines so they survive
            // even if individual dispatchers get rebuilt.
            try { MusicFactory.BuildAll(m, this, Logger); }
            catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Music build failed in " + m.PackId + ": " + ex); }

            // Build SFX. Same async-load pattern as music, but the
            // clips live in a registry instead of per-clip GOs —
            // PlayOneShot through a single shared AudioSource handles
            // overlap natively, no need for one GO per sound.
            try { SfxFactory.BuildAll(m, ctx.Sfx, this, Logger); }
            catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] SFX build failed in " + m.PackId + ": " + ex); }

            // Build integration rules (Integration tab in the editor).
            // Done after everything else so a rule that references a
            // pack variable / scene / actor finds the entity already
            // registered when its conditions first evaluate.
            try { UpdateRulesFactory.BuildAll(m, ctx.UpdateRules, Logger); }
            catch (System.Exception ex) { Logger.LogError("[SMSModForge.PackPlugin] Integration rules build failed in " + m.PackId + ": " + ex); }
            // Arm OnSceneLoad rules so they fire on the first eligible tick.
            ctx.UpdateRules.ArmOneShots(UpdateRulesRegistry.TriggerMode.OnSceneLoad);

            // Declare variables and load any persisted slice from disk.
            var variables = m.Root["variables"] as Newtonsoft.Json.Linq.JArray;
            if (variables != null)
            {
                foreach (var v in variables)
                {
                    var vo = (Newtonsoft.Json.Linq.JObject)v;
                    string name = (string)vo["name"];
                    if (string.IsNullOrEmpty(name)) continue;
                    string typeStr = (string)vo["type"] ?? "Bool";
                    PackVariableStore.PackVariableType type;
                    switch (typeStr)
                    {
                        case "Int":    type = PackVariableStore.PackVariableType.Int;    break;
                        case "Float":  type = PackVariableStore.PackVariableType.Float;  break;
                        case "String": type = PackVariableStore.PackVariableType.String; break;
                        case "List":   type = PackVariableStore.PackVariableType.List;   break;
                        default:       type = PackVariableStore.PackVariableType.Bool;   break;
                    }
                    // Refresh policy: new `refreshMode` enum (Daily / DailyRandom /
                    // RoomTalkRandom) supersedes the legacy bool `refreshDaily`.
                    // Honour the legacy field when the new one is absent so older
                    // packs keep working without a manifest migration.
                    string modeStr = (string)vo["refreshMode"];
                    PackVariableStore.RefreshMode mode;
                    if (string.IsNullOrEmpty(modeStr))
                        mode = ((bool?)vo["refreshDaily"] ?? false)
                               ? PackVariableStore.RefreshMode.Daily
                               : PackVariableStore.RefreshMode.Never;
                    else if (!System.Enum.TryParse<PackVariableStore.RefreshMode>(modeStr, true, out mode))
                        mode = PackVariableStore.RefreshMode.Never;

                    ctx.Vars.Declare(name, type, (string)vo["defaultValue"] ?? "", (bool?)vo["persisted"] ?? true,
                                     mode, (string)vo["refreshScope"],
                                     (string)vo["minValue"], (string)vo["maxValue"]);
                }
                // Declarations only — disk IO is deferred until a save slot
                // gets picked, which happens later in the load flow. See
                // RebindActiveSlot in the per-frame loop. Until then we run
                // with every var sitting on its declared default.
            }
            ctx.Vars.ResetNonPersistedToDefaults();

            // Declare actors. The bust-side state lives on ctx.Actors; the
            // speech-bubble name colour goes to ctx.ActorFactory so the
            // dispatcher's per-line colour-applier can find it by display
            // name. Colour parsing is best-effort — bad hex strings just
            // skip registration (the colorizer's default colour stands in).
            var actors = m.Root["actors"] as Newtonsoft.Json.Linq.JArray;
            if (actors != null)
            {
                foreach (var a in actors)
                {
                    var ao = (Newtonsoft.Json.Linq.JObject)a;
                    ctx.Actors.Declare(ao);

                    string displayName = (string)ao["displayName"] ?? (string)ao["key"];
                    string hex = (string)ao["nameColor"];
                    if (!string.IsNullOrEmpty(displayName) && TryParseHexColor(hex, out var col))
                        ctx.ActorFactory?.RegisterColor(displayName, col);

                    // Per-actor typewriter voice (frequency + pitch range). The
                    // whole "typewriter" object is optional — when absent the
                    // factory falls back to a neutral audible default.
                    string actorKey = (string)ao["key"];
                    if (!string.IsNullOrEmpty(actorKey) && ao["typewriter"] is Newtonsoft.Json.Linq.JObject tw)
                    {
                        bool enabled = tw["enabled"] == null || (bool)tw["enabled"];
                        int freq = tw["frequency"] != null ? (int)tw["frequency"] : 45;
                        float pmin = tw["pitchMin"] != null ? (float)tw["pitchMin"] : 1f;
                        float pmax = tw["pitchMax"] != null ? (float)tw["pitchMax"] : 1f;
                        ctx.ActorFactory?.RegisterTypewriter(actorKey, enabled, freq, pmin, pmax);
                    }
                }
            }

            // Build dialogues.
            var dialogues = m.Root["dialogues"] as Newtonsoft.Json.Linq.JArray;
            if (dialogues == null || dialogues.Count == 0) return;

            var dispatcher = new DialogueDispatcher(ctx);
            int built = 0;
            foreach (var d in dialogues)
            {
                var dj = (Newtonsoft.Json.Linq.JObject)d;
                var roomTalkParent = ResolveRoomTalk((string)dj["roomTalk"]);
                if (roomTalkParent == null)
                {
                    Logger.LogWarning("[SMSModForge.PackPlugin] Dialogue " + ctx.PackId + "." +
                                      (string)dj["key"] + " — roomtalk '" + (string)dj["roomTalk"] +
                                      "' could not be resolved; skipping");
                    continue;
                }
                var b = DialogueBuilder.Build(dj, ctx, roomTalkParent, _sharedSkin);
                if (b != null) { dispatcher.Add(b); built++; }
            }

            _contexts.Add(ctx);
            _dispatchers.Add(dispatcher);
            if (built > 0)
                Logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + ctx.PackId + "' built " + built + " dialogue(s).");
        }

        /// <summary>
        /// Parse a <c>#RRGGBB</c> or <c>#RRGGBBAA</c> hex string into a
        /// Unity <see cref="Color"/>. Returns false (and outputs white)
        /// when the input is empty or malformed — callers treat that as
        /// "skip registration, use the default colour".
        /// </summary>
        internal static bool TryParseHexColor(string hex, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;
            string s = hex.TrimStart('#');
            if (s.Length == 6) s += "FF";
            if (s.Length != 8) return false;
            try
            {
                byte r = System.Convert.ToByte(s.Substring(0, 2), 16);
                byte g = System.Convert.ToByte(s.Substring(2, 2), 16);
                byte b = System.Convert.ToByte(s.Substring(4, 2), 16);
                byte a = System.Convert.ToByte(s.Substring(6, 2), 16);
                c = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Resolves a <c>vanilla:&lt;name&gt;</c> or <c>place:&lt;key&gt;</c>
        /// roomtalk token into the actual transform under <c>8_Room_Talk</c>.
        /// </summary>
        private Transform ResolveRoomTalk(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var roomTalk = GameObject.Find("8_Room_Talk")?.transform;
            if (roomTalk == null) return null;

            int colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1) return null;
            string scheme = token.Substring(0, colon);
            string rest = token.Substring(colon + 1);

            if (scheme == "vanilla")
            {
                var found = roomTalk.Find(rest);
                if (found != null) return found;
                // A handful of vanilla levels (Hospital / Hotel / Trail)
                // have no roomtalk node in the base game — the host mod
                // creates one on the fly via Places.CreateNewRoomTalk.
                // Mirror that so a dialogue can still be hosted there.
                return CreateRoomTalkNode(roomTalk, rest);
            }
            if (scheme == "place")
            {
                // Pack places are added by PlaceFactory under their internalName.
                // The place key may differ from the internalName, so first try
                // the key (which is what the editor writes), then look up by
                // internalName via the place registry.
                var direct = roomTalk.Find(rest);
                if (direct != null) return direct;

                // Fallback: scan all roomtalk children for one whose name matches
                // any registered place's internalName for the given pack key.
                // Since we don't store internalName in the registry, the most
                // common case (place key == internalName) is the direct lookup
                // above. Pack authors who diverge can author the dialogue's
                // roomTalk as `place:<internalName>` instead.
                return null;
            }
            return null;
        }

        /// <summary>
        /// Create a fresh roomtalk node under <c>8_Room_Talk</c> for a
        /// vanilla level that ships without one. Clones an existing
        /// roomtalk as a template, renames it, strips the inherited
        /// dialogue children (keeping child 0) and the <c>Conditions</c>
        /// component so it starts empty — a faithful port of the host mod's
        /// <c>Places.CreateNewRoomTalk</c>.
        /// </summary>
        private Transform CreateRoomTalkNode(Transform roomTalkRoot, string name)
        {
            var template = roomTalkRoot.Find("Beach");
            if (template == null && roomTalkRoot.childCount > 0)
                template = roomTalkRoot.GetChild(0);
            if (template == null)
            {
                Logger.LogError("[SMSModForge.PackPlugin] CreateRoomTalkNode: no template " +
                                "roomtalk under 8_Room_Talk for '" + name + "'");
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, roomTalkRoot);
            clone.name = name;
            for (int i = clone.transform.childCount - 1; i > 0; i--)
                UnityEngine.Object.Destroy(clone.transform.GetChild(i).gameObject);

            // Drop the cloned Conditions component (looked up by type name
            // to avoid a hard reference to GC2 VisualScripting).
            foreach (var c in clone.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name == "Conditions")
                {
                    UnityEngine.Object.Destroy(c);
                    break;
                }
            }
            Logger.LogInfo("[SMSModForge.PackPlugin] Created roomtalk node '" + name + "'");
            return clone.transform;
        }

        /// <summary>
        /// Find any existing vanilla <see cref="Dialogue"/> in
        /// <c>8_Room_Talk</c> and lift its <see cref="DialogueSkin"/>.
        /// Pack dialogues need a non-null skin or <see cref="Dialogue.Play"/>
        /// bails. The first one found is fine because every vanilla dialogue
        /// uses the same shared skin.
        /// </summary>
        private DialogueSkin HarvestVanillaDialogueSkin()
        {
            var roomTalk = GameObject.Find("8_Room_Talk")?.transform;
            if (roomTalk == null) return null;
            foreach (var dlg in roomTalk.GetComponentsInChildren<Dialogue>(true))
            {
                var skin = dlg.Story?.Content?.DialogueSkin;
                if (skin != null) { Logger.LogInfo("[SMSModForge.PackPlugin] Harvested DialogueSkin from " + dlg.name); return skin; }
            }
            Logger.LogWarning("[SMSModForge.PackPlugin] Could not find a vanilla DialogueSkin — pack dialogues will fail to play");
            return null;
        }
    }
}
