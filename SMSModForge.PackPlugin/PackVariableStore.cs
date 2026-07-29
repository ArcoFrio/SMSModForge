using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-pack variable store, used by dialogue actions and conditions.
    /// Each variable carries a declared type (Bool / Int / Float / String),
    /// a default value, and a "persisted" flag. Persisted variables survive
    /// between sessions via a JSON file under
    /// <c>BepInEx/plugins/SMSModForge/Saves/&lt;packId&gt;.json</c>;
    /// non-persisted variables reset to their defaults on every scene-load.
    /// <para/>
    /// The store is intentionally separate from GC2's
    /// <c>GlobalNameVariables</c> — packs author their own state without
    /// needing to ship a GNV asset, and runs without the host mod.
    /// </summary>
    public sealed class PackVariableStore
    {
        public sealed class Declaration
        {
            public string Name = "";
            public PackVariableType Type;
            public string DefaultValue = "";
            public bool Persisted;

            /// <summary>
            /// Auto-refresh policy. Mirrors the editor-side
            /// <c>PackVariableRefreshMode</c>; encoded as the integer
            /// underlying value so the dispatcher can switch on it
            /// without a hard reference to the editor assembly.
            /// </summary>
            public RefreshMode Refresh;

            /// <summary>
            /// Level token (<c>vanilla:&lt;goName&gt;</c> /
            /// <c>place:&lt;key&gt;</c>) the runtime watches for the
            /// <see cref="RefreshMode.LevelRandom"/> trigger — the same
            /// format the <c>LevelActive</c> condition reads. Empty for
            /// every other mode.
            /// </summary>
            public string Scope = "";

            /// <summary>Optional numeric clamp bounds (null = unbounded). Only used for Int/Float.</summary>
            public double? Min;
            public double? Max;
        }

        public enum PackVariableType { Bool, Int, Float, String, List }

        /// <summary>
        /// Per-variable auto-refresh policy. Mirrors the editor-side
        /// <c>PackVariableRefreshMode</c> enum exactly so manifest values
        /// round-trip through the wire format as their string name.
        /// </summary>
        public enum RefreshMode
        {
            Never,
            Daily,
            DailyRandom,
            LevelRandom,
        }

        public string PackId { get; }

        /// <summary>
        /// Active NanoSave slot the store is currently bound to, or
        /// <c>-1</c> when no slot is loaded (main menu / pre-slot-pick).
        /// Changed exclusively through <see cref="SetActiveSlot"/>.
        /// </summary>
        public int ActiveSlot { get; private set; } = -1;

        /// <summary>
        /// Absolute path to the per-slot save file, or null while
        /// <see cref="ActiveSlot"/> is unset. The file lives next to
        /// the host mod's own save inside the matching
        /// <c>NANOSAVE_xxxx</c> folder so the game's slot lifecycle
        /// (create / overwrite / delete) covers our state too.
        /// Computed by <see cref="SetActiveSlot"/>; null otherwise.
        /// </summary>
        public string SaveFilePath { get; private set; }

        private readonly Dictionary<string, Declaration> _decls = new Dictionary<string, Declaration>();
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private readonly ManualLogSource _log;

        /// <summary>
        /// Base directory under which per-slot folders live — typically
        /// <c>%appdata%/../LocalLow/Arvus Games/Starmaker Story/Saves</c>.
        /// Slot N's pack file resolves to
        /// <c>&lt;_savesRoot&gt;/NANOSAVE_{N:D4}/SMSModForge_&lt;packId&gt;.json</c>.
        /// </summary>
        private readonly string _savesRoot;

        public PackVariableStore(string packId, string savesRoot, ManualLogSource log)
        {
            PackId = packId;
            _log = log;
            _savesRoot = savesRoot;
            // We intentionally don't create _savesRoot here — the vanilla
            // save manager owns it. We only create the per-slot folder on
            // first write in case the player hasn't actually saved there
            // yet (matching the host mod's SaveManager.EnsureSaveDirectoryExists
            // semantics).

            // Intrinsic, not manifest-authored: every store has the roll seed
            // so DailyChance works regardless of what the pack declares.
            Declare(RollSeedName, PackVariableType.Int, "0", persisted: true);
        }

        /// <summary>Internal seed variable. Underscored so
        /// <see cref="EnumerateNames"/> keeps it out of the public surface —
        /// it's plumbing, not pack state an author or host mod should see.</summary>
        private const string RollSeedName = "__rollSeed";

        /// <summary>
        /// Per-SAVE random seed mixed into every <c>DailyChance</c> roll.
        /// <para/>
        /// Without it the rolls would be a pure function of (pack, condition,
        /// day) — identical for every player and every new game, i.e. a fixed
        /// calendar rather than a random gate. The seed is generated ONCE, the
        /// first time a save has no value for it, and then persists with that
        /// save: so a given playthrough keeps a stable calendar (reloading
        /// can't re-roll it), while a different save — anyone's, in any slot —
        /// gets a completely independent one. It is NOT derived from the slot
        /// number; slot 1 of two different playthroughs seeds differently.
        /// </summary>
        public int RollSeed =>
            _values.TryGetValue(RollSeedName, out var s) &&
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        /// <summary>Mint a seed when the freshly-bound save doesn't carry one.
        /// Called after the disk load so an existing save's seed always wins.</summary>
        private void EnsureRollSeed()
        {
            if (RollSeed != 0) return;
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            if (seed == 0) seed = 1;   // 0 is the "unset" sentinel
            _values[RollSeedName] = seed.ToString(CultureInfo.InvariantCulture);
            _log?.LogInfo("[SMSModForge.PackPlugin] " + PackId +
                          ": minted a new DailyChance seed for this save (" + seed +
                          ") — its daily rolls are unique to this playthrough.");
        }

        /// <summary>
        /// Bind the store to a NanoSave slot. Pass <c>-1</c> to detach
        /// (returns the store to its "no slot active" state — used when
        /// the game returns to the main menu).
        /// <para/>
        /// Behaviour on a real slot:
        /// <list type="number">
        ///   <item>Reset every in-memory value to its declared default —
        ///   so values left over from the previous slot don't bleed in.</item>
        ///   <item>Recompute <see cref="SaveFilePath"/> from
        ///   <paramref name="slot"/> using the canonical
        ///   <c>NANOSAVE_xxxx/SMSModForge_&lt;packId&gt;.json</c> layout.</item>
        ///   <item>Load whatever's on disk for the new slot via
        ///   <see cref="LoadFromDisk"/> — missing file = stay at
        ///   defaults, fresh save.</item>
        /// </list>
        /// The caller is responsible for flushing the outgoing slot's
        /// in-memory state via <see cref="SaveToDisk"/> first if any
        /// dirty values should persist across the switch.
        /// </summary>
        public void SetActiveSlot(int slot)
        {
            ActiveSlot = slot;
            if (slot < 1)
            {
                SaveFilePath = null;
                ResetAllToDefaults();
                return;
            }
            SaveFilePath = Path.Combine(_savesRoot,
                                        "NANOSAVE_" + slot.ToString("D4"),
                                        "SMSModForge_" + PackId + ".json");
            ResetAllToDefaults();
            LoadFromDisk();
            // After the load, so a save that already has a seed keeps it and
            // only a genuinely fresh save mints one.
            EnsureRollSeed();
        }

        /// <summary>
        /// Reset every variable (persisted and non-persisted alike) to its
        /// declared default. Used during slot switches to scrub the outgoing
        /// slot's state before the new slot's file overlays on top.
        /// Random-mode numeric variables reset to a fresh roll instead of the
        /// bare default (same as the Declare-time seed) — for persisted ones a
        /// following <see cref="LoadFromDisk"/> overlays the saved roll, so a
        /// loaded save keeps its committed value; only a fresh save (no file)
        /// keeps the new roll.
        /// </summary>
        public void ResetAllToDefaults()
        {
            foreach (var d in _decls.Values)
                _values[d.Name] = ResetValueFor(d);
        }

        /// <summary>The value a reset should give a declaration: a fresh roll
        /// for numeric random-refresh variables, the declared default otherwise.</summary>
        private static string ResetValueFor(Declaration d)
        {
            if ((d.Refresh == RefreshMode.DailyRandom || d.Refresh == RefreshMode.LevelRandom) &&
                (d.Type == PackVariableType.Int || d.Type == PackVariableType.Float))
                return RollRandom(d);
            return d.DefaultValue ?? "";
        }

        /// <summary>
        /// Register a variable declaration parsed from the pack manifest.
        /// Resets the value to default unless a persisted value is later
        /// loaded via <see cref="LoadFromDisk"/>. <paramref name="minValue"/>
        /// / <paramref name="maxValue"/> are raw manifest strings — parsed
        /// to numeric bounds here, ignored when blank or unparseable.
        /// <para/>
        /// Variables flagged <see cref="RefreshMode.DailyRandom"/> /
        /// <see cref="RefreshMode.RoomTalkRandom"/> roll their initial
        /// value here too so a brand-new save isn't forced through a
        /// roomtalk-enter cycle to get a sensible value (e.g. roomtalk
        /// gating reads correctly the first time the player visits even
        /// if the level was already active when the dialogue runtime
        /// finished building).
        /// </summary>
        public void Declare(string name, PackVariableType type, string defaultValue, bool persisted,
                            RefreshMode refreshMode = RefreshMode.Never, string scope = null,
                            string minValue = null, string maxValue = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var decl = new Declaration
            {
                Name = name,
                Type = type,
                DefaultValue = defaultValue ?? "",
                Persisted = persisted,
                Refresh = refreshMode,
                Scope = scope ?? "",
                Min = ParseBound(minValue),
                Max = ParseBound(maxValue),
            };
            _decls[name] = decl;
            // List variables need a JSON-array literal as their stored
            // form. If the manifest carries a non-array default (or an
            // empty string), substitute the canonical "[]" so GetList /
            // ListAdd / etc. find a parseable seed.
            if (type == PackVariableType.List &&
                (string.IsNullOrEmpty(decl.DefaultValue) || !decl.DefaultValue.TrimStart().StartsWith("[")))
            {
                decl.DefaultValue = "[]";
            }
            _values[name] = decl.DefaultValue;

            // Seed an initial random roll for random modes so the first
            // read returns a sensible number rather than the default.
            if (refreshMode == RefreshMode.DailyRandom ||
                refreshMode == RefreshMode.LevelRandom)
            {
                if (type == PackVariableType.Int || type == PackVariableType.Float)
                {
                    _values[name] = RollRandom(decl);
                    UnityEngine.Debug.Log("[SMSModForge] " + PackId + ": " + refreshMode +
                                          " initial roll — " + name + " = " + _values[name]);
                }
            }
        }

        private static double? ParseBound(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d : (double?)null;
        }

        /// <summary>
        /// Load the persisted slice of values from disk. Variables that are
        /// not <see cref="Declaration.Persisted"/> are ignored even if
        /// present in the file — they always start at default. No-op when
        /// <see cref="SaveFilePath"/> is null (no active slot) or the
        /// file doesn't exist (fresh save on this slot).
        /// </summary>
        public void LoadFromDisk()
        {
            if (string.IsNullOrEmpty(SaveFilePath)) return;
            if (!File.Exists(SaveFilePath)) return;
            try
            {
                var obj = JObject.Parse(File.ReadAllText(SaveFilePath));
                foreach (var prop in obj.Properties())
                {
                    if (!_decls.TryGetValue(prop.Name, out var decl) || !decl.Persisted) continue;
                    _values[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }
            catch (System.Exception ex)
            {
                _log?.LogWarning("[SMSModForge.PackPlugin] Variable save read failed for " + PackId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Write the persisted slice back to disk. Invoked at the sleep
        /// autosave — when the post-sleep "Saved" UI fires (see
        /// <c>Plugin.TickSleepAutosave</c>) — plus on slot-change to flush
        /// the outgoing slot. Variable writes made during the day live in
        /// memory until then, mirroring vanilla: progress is persisted
        /// only when the player sleeps, and a manual save mid-day copies
        /// the last committed file rather than the current in-memory state.
        /// <para/>
        /// No-op when <see cref="SaveFilePath"/> is null. Creates the
        /// per-slot folder on first write — vanilla's save manager will
        /// also create it but if the player saves to a brand-new slot,
        /// we might race ahead of NanoSave creating <c>NANOSAVE_xxxx</c>.
        /// </summary>
        public void SaveToDisk()
        {
            if (string.IsNullOrEmpty(SaveFilePath)) return;
            try { WritePersistedSlice(SaveFilePath); }
            catch (System.Exception ex)
            {
                _log?.LogError("[SMSModForge.PackPlugin] Variable save write failed for " + PackId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Resolve the on-disk pack file for an explicit NanoSave slot,
        /// independent of the currently bound <see cref="ActiveSlot"/>. Same
        /// canonical layout <see cref="SetActiveSlot"/> uses.
        /// </summary>
        public string SlotFilePath(int slot)
            => slot < 1 ? null
               : Path.Combine(_savesRoot, "NANOSAVE_" + slot.ToString("D4"), "SMSModForge_" + PackId + ".json");

        /// <summary>
        /// Write the persisted slice to an explicit slot's file, regardless of
        /// which slot is currently bound. The sleep autosave uses this to
        /// target the dedicated autosave slot (slot 1) — and the Monday
        /// backup slot (slot 2) — exactly like the host mod's SaveManager calls
        /// <c>SaveToFile(1)</c> / <c>SaveToFile(2)</c>. Writing to the loaded
        /// slot instead left the autosave the player actually reloads (slot 1)
        /// stale.
        /// </summary>
        public void SaveToSlot(int slot)
        {
            var path = SlotFilePath(slot);
            if (path == null) return;
            try { WritePersistedSlice(path); }
            catch (System.Exception ex)
            {
                _log?.LogError("[SMSModForge.PackPlugin] Variable save write to slot " + slot +
                               " failed for " + PackId + ": " + ex.Message);
            }
        }

        /// <summary>Serialize the persisted variables to <paramref name="path"/>,
        /// creating the slot folder if the player hasn't saved there yet.</summary>
        private void WritePersistedSlice(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var obj = new JObject();
            foreach (var kv in _values)
            {
                if (_decls.TryGetValue(kv.Key, out var decl) && decl.Persisted)
                    obj[kv.Key] = kv.Value;
            }
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>
        /// Reset every <em>non-persisted</em> variable back to its
        /// declared default. Called on every <c>CoreGameScene</c> entry so
        /// per-session flags start fresh. Non-persisted random-mode numeric
        /// variables re-roll instead (a bare default would leave them
        /// meaningless until the first sleep's refresh).
        /// </summary>
        public void ResetNonPersistedToDefaults()
        {
            foreach (var d in _decls.Values)
                if (!d.Persisted) _values[d.Name] = ResetValueFor(d);
        }

        /// <summary>
        /// Apply the day-change refresh: every <see cref="RefreshMode.Daily"/>
        /// variable resets to its default, and every
        /// <see cref="RefreshMode.DailyRandom"/> variable rolls a fresh
        /// random number within its declared bounds. Called by the daily
        /// autosave right before the disk commit — the generalised form of
        /// the host mod's <c>RefreshDailyVariables</c>. The bool return (true
        /// when a refreshed variable was persisted) is informational only;
        /// the daily autosave commits unconditionally.
        /// </summary>
        public bool RefreshOnDayChange()
        {
            bool anyPersisted = false;
            int dailyReset = 0;
            System.Text.StringBuilder changed = null;

            foreach (var d in _decls.Values)
            {
                if (d.Refresh == RefreshMode.Daily)
                {
                    // Report only the ones that actually MOVED. A Daily variable
                    // still holding yesterday's value the morning after is the
                    // signature of this pass not running at all, and telling
                    // "reset it" apart from "it was already default" is the
                    // difference between blaming the refresh and blaming the
                    // rules that repopulate it.
                    _values.TryGetValue(d.Name, out var before);
                    string after = d.DefaultValue ?? "";
                    _values[d.Name] = after;
                    dailyReset++;
                    if (before != after)
                    {
                        changed = changed ?? new System.Text.StringBuilder();
                        if (changed.Length > 0) changed.Append(", ");
                        changed.Append(d.Name).Append(": ").Append(before).Append(" -> ").Append(after);
                    }
                }
                else if (d.Refresh == RefreshMode.DailyRandom)
                {
                    _values[d.Name] = RollRandom(d);
                    _log?.LogInfo("[SMSModForge.PackPlugin] " + PackId + ": DailyRandom re-roll — " +
                                  d.Name + " = " + _values[d.Name]);
                }
                else
                    continue;
                if (d.Persisted) anyPersisted = true;
            }

            _log?.LogInfo("[SMSModForge.PackPlugin] " + PackId + ": daily refresh reset " +
                          dailyReset + " variable(s)" +
                          (changed != null ? " — " + changed : " — none had drifted from default"));
            return anyPersisted;
        }

        /// <summary>
        /// Re-roll every <see cref="RefreshMode.LevelRandom"/> variable
        /// whose <see cref="Declaration.Scope"/> matches the given level
        /// token. Called by the plugin's per-frame Tick when the matching
        /// level's GameObject under <c>5_Levels</c> flips from inactive to
        /// active — a generalised "roll a fresh number on entry" pattern.
        /// Watching the level (and not the roomtalk) means every place can use
        /// it; many vanilla levels ship without a roomtalk node.
        /// </summary>
        public void RefreshOnLevelEnter(string scopeToken)
        {
            if (string.IsNullOrEmpty(scopeToken)) return;
            foreach (var d in _decls.Values)
            {
                if (d.Refresh != RefreshMode.LevelRandom) continue;
                if (!string.Equals(d.Scope, scopeToken, StringComparison.Ordinal)) continue;
                _values[d.Name] = RollRandom(d);
                _log?.LogInfo("[SMSModForge.PackPlugin] " + PackId + ": LevelRandom re-roll on " +
                              scopeToken + " — " + d.Name + " = " + _values[d.Name]);
            }
        }

        /// <summary>
        /// Enumerate the distinct level-scope tokens that any
        /// <see cref="RefreshMode.LevelRandom"/> variable in this pack
        /// depends on. The plugin uses this list to know which levels
        /// to poll for inactive→active transitions, so unused scopes never
        /// cost a per-frame check.
        /// </summary>
        public IEnumerable<string> EnumerateLevelScopes()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in _decls.Values)
            {
                if (d.Refresh != RefreshMode.LevelRandom) continue;
                if (string.IsNullOrEmpty(d.Scope)) continue;
                if (seen.Add(d.Scope)) yield return d.Scope;
            }
        }

        /// <summary>
        /// Roll a fresh random value for a numeric variable within its
        /// declared <see cref="Declaration.Min"/>/<see cref="Declaration.Max"/>
        /// bounds, defaulting to 0..100 when unset (the same range the host mod
        /// uses for <c>MyDailyRandom</c>). Int
        /// vars roll inclusive-max via <c>Random.Range(min, max + 1)</c> so
        /// a 0..100 declaration can actually produce 100. Float vars roll
        /// <c>Random.Range(min, max)</c> (max-exclusive, the Unity default).
        /// Non-numeric types fall back to the declared default — random
        /// rolls on bool / string don't make sense, and silently emitting
        /// "0" / "1" would mask an author's mistake.
        /// </summary>
        private static string RollRandom(Declaration d)
        {
            double lo = d.Min ?? 0.0;
            double hi = d.Max ?? 100.0;
            if (hi < lo) hi = lo;

            if (d.Type == PackVariableType.Int)
            {
                int rolled = UnityEngine.Random.Range((int)System.Math.Ceiling(lo),
                                                       (int)System.Math.Floor(hi) + 1);
                return rolled.ToString(CultureInfo.InvariantCulture);
            }
            if (d.Type == PackVariableType.Float)
            {
                float rolled = UnityEngine.Random.Range((float)lo, (float)hi);
                return rolled.ToString(CultureInfo.InvariantCulture);
            }
            return d.DefaultValue ?? "";
        }

        /// <summary>
        /// Clamp a numeric variable's string value to the declared
        /// <see cref="Declaration.Min"/> / <see cref="Declaration.Max"/>
        /// bounds. No-op for non-numeric types, unbounded declarations, or
        /// values that don't parse — the raw string is returned unchanged.
        /// </summary>
        private static string ClampValue(Declaration d, string value)
        {
            if (d.Min == null && d.Max == null) return value;

            if (d.Type == PackVariableType.Int)
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return value;
                if (d.Min.HasValue) i = Math.Max(i, (int)Math.Ceiling(d.Min.Value));
                if (d.Max.HasValue) i = Math.Min(i, (int)Math.Floor(d.Max.Value));
                return i.ToString(CultureInfo.InvariantCulture);
            }
            if (d.Type == PackVariableType.Float)
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return value;
                if (d.Min.HasValue) f = Math.Max(f, (float)d.Min.Value);
                if (d.Max.HasValue) f = Math.Min(f, (float)d.Max.Value);
                return f.ToString(CultureInfo.InvariantCulture);
            }
            return value;
        }

        /// <summary>
        /// Read a list-typed variable as a parsed <see cref="List{T}"/>.
        /// Tolerates an empty / malformed backing string by returning
        /// an empty list — list-type variables default to "[]" so the
        /// normal case always parses cleanly.
        /// </summary>
        public List<string> GetList(string name)
        {
            var raw = GetString(name);
            if (string.IsNullOrEmpty(raw)) return new List<string>();
            try
            {
                var arr = Newtonsoft.Json.Linq.JArray.Parse(raw);
                var result = new List<string>(arr.Count);
                foreach (var item in arr)
                {
                    var s = (string)item;
                    if (!string.IsNullOrEmpty(s)) result.Add(s);
                }
                return result;
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Write a list back as a JSON array literal. Persisted under
        /// the same _values dict the other types use.
        /// </summary>
        public bool SetList(string name, IList<string> items)
        {
            var arr = new Newtonsoft.Json.Linq.JArray();
            if (items != null)
                foreach (var s in items)
                    if (!string.IsNullOrEmpty(s)) arr.Add(s);
            return Set(name, arr.ToString(Newtonsoft.Json.Formatting.None));
        }

        /// <summary>Append a value to a list-typed variable; returns true if persisted.</summary>
        public bool ListAdd(string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var list = GetList(name);
            list.Add(value);
            return SetList(name, list);
        }

        /// <summary>Remove the first matching value from a list-typed variable.</summary>
        public bool ListRemove(string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var list = GetList(name);
            int idx = list.IndexOf(value);
            if (idx < 0) return false;
            list.RemoveAt(idx);
            return SetList(name, list);
        }

        /// <summary>Reset a list to empty.</summary>
        public bool ListClear(string name) => SetList(name, new List<string>());

        public bool Exists(string name) => _values.ContainsKey(name);

        /// <summary>
        /// Snapshot of every declared variable name in this store —
        /// the cross-plugin API surfaces this so external code can
        /// browse without holding an internal reference. Returns the
        /// declared names; undeclared keys that landed via untyped
        /// <see cref="Set"/> writes are not enumerated (they're an
        /// authoring escape hatch, not part of the public schema).
        /// </summary>
        /// <summary>Every pack-authored variable name. Runtime-internal
        /// declarations (underscored, e.g. the DailyChance seed) are hidden —
        /// host mods and the debug dumps should only see authored state.</summary>
        public IEnumerable<string> EnumerateNames()
        {
            foreach (var k in _decls.Keys)
                if (!k.StartsWith("__", StringComparison.Ordinal)) yield return k;
        }

        public string GetString(string name)
            => _values.TryGetValue(name, out var v) ? v : (_decls.TryGetValue(name, out var d) ? d.DefaultValue : "");

        public bool GetBool(string name)
        {
            var s = GetString(name);
            return bool.TryParse(s, out var b) && b;
        }

        public int GetInt(string name)
        {
            var s = GetString(name);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
        }

        public float GetFloat(string name)
        {
            var s = GetString(name);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        }

        /// <summary>
        /// Fires whenever any variable's stored value actually changes.
        /// Signature is <c>(name, oldValue, newValue)</c>. Subscribers
        /// see the new value already committed to the store, so a
        /// handler that re-queries the variable through
        /// <see cref="GetString"/> / <see cref="GetBool"/> / etc.
        /// gets the post-change state.
        /// <para/>
        /// Used by <c>Plugin</c> to forward variable changes into a
        /// process-wide event other BepInEx plugins can subscribe to
        /// — the canonical handshake mechanism that replaces the
        /// the host mod "Proxy Variables" GNV mirroring layer.
        /// </summary>
        public event Action<string, string, string> ValueChanged;

        /// <summary>
        /// Centralised value-write path. Stores the new value (after
        /// clamping for numeric types), fires <see cref="ValueChanged"/>
        /// when the stored value differs from what was there before,
        /// and returns whether the variable was persisted (so callers
        /// know if a disk flush is wanted).
        /// </summary>
        private bool ApplyWrite(string name, Declaration d, string newValue)
        {
            string clamped = d != null ? ClampValue(d, newValue ?? "") : (newValue ?? "");
            _values.TryGetValue(name, out var oldValue);
            _values[name] = clamped;
            // Only raise on an actual change — otherwise refresh-daily
            // resets and idempotent SetVariable actions would spam
            // subscribers with no-op notifications.
            if (!string.Equals(oldValue, clamped, StringComparison.Ordinal))
                ValueChanged?.Invoke(name, oldValue ?? "", clamped);
            return d != null && d.Persisted;
        }

        /// <summary>
        /// Set a variable's value. Numeric variables with declared bounds
        /// are clamped first. Returns true if the variable was persisted
        /// (so the dispatcher can flush to disk). Returns false for
        /// undeclared or non-persisted names.
        /// </summary>
        public bool Set(string name, string value)
        {
            _decls.TryGetValue(name, out var d);
            return ApplyWrite(name, d, value);
        }

        /// <summary>
        /// Add <paramref name="delta"/> to a numeric variable. No-op for
        /// non-numeric types (logs a warning).
        /// </summary>
        public bool Increment(string name, float delta)
        {
            if (!_decls.TryGetValue(name, out var d))
            {
                _log?.LogWarning("[SMSModForge.PackPlugin] Increment on undeclared variable '" + name + "'");
                return false;
            }
            if (d.Type == PackVariableType.Int)
            {
                int cur = GetInt(name);
                return ApplyWrite(name, d, (cur + (int)delta).ToString(CultureInfo.InvariantCulture));
            }
            if (d.Type == PackVariableType.Float)
            {
                float cur = GetFloat(name);
                return ApplyWrite(name, d, (cur + delta).ToString(CultureInfo.InvariantCulture));
            }
            _log?.LogWarning("[SMSModForge.PackPlugin] Increment on non-numeric variable '" + name + "' type=" + d.Type);
            return false;
        }

        /// <summary>
        /// Best-effort string-to-comparable coercion, used by the condition
        /// evaluator for equality and ordering checks. Numeric types are
        /// compared as floats; bools as booleans; strings as ordinal.
        /// </summary>
        public int Compare(string name, string against)
        {
            if (!_decls.TryGetValue(name, out var d)) return string.CompareOrdinal(GetString(name), against);
            switch (d.Type)
            {
                case PackVariableType.Bool:
                    bool.TryParse(against, out var bAgainst);
                    return GetBool(name).CompareTo(bAgainst);
                case PackVariableType.Int:
                    int.TryParse(against, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iAgainst);
                    return GetInt(name).CompareTo(iAgainst);
                case PackVariableType.Float:
                    float.TryParse(against, NumberStyles.Float, CultureInfo.InvariantCulture, out var fAgainst);
                    return GetFloat(name).CompareTo(fAgainst);
                default:
                    return string.CompareOrdinal(GetString(name), against);
            }
        }
    }
}
