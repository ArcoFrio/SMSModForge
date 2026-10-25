using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Lets a test drive the game without anybody at the keyboard (the author,
    /// 2026-10-21: the game may be run for checks, in save slot 1, as long as
    /// it never gets in the way of their work).
    /// <para/>
    /// Debug builds only - the csproj compiles this file on the Debug
    /// configuration alone - and asleep unless the game was started with
    /// <c>-mfBridge</c>, which only <c>Tools/Game/launch.ps1</c> does. So the
    /// author's own Debug runs behave as they always have.
    /// <list type="bullet">
    /// <item><c>-mfBridge &lt;name&gt;</c>: read commands from
    /// <c>BepInEx/plugins/SMSModForge/DebugBridge/&lt;name&gt;/commands.txt</c>,
    /// one per line, and append what happened to <c>results.txt</c> beside it,
    /// a time first on every line.</item>
    /// <item><c>-mfLoadSlot &lt;n&gt;</c>: load that save the moment the main
    /// menu's scene is up, through the game's own SaveLoadManager.Load - what
    /// its Load button does, without waiting for the menu to finish arriving.</item>
    /// <item><c>-mfVars &lt;file&gt;</c>: once the game is in, set the game's
    /// variables from the file, <c>name = value</c> per line - a "debug save"
    /// made of slot 1 and the values a test needs.</item>
    /// <item><c>-mfMute</c>: start with the game's sound off (the launcher
    /// mutes the game's audio session as well; this is the second lock).</item>
    /// </list>
    /// It keeps running minimized (runInBackground). Nothing here ever saves.
    /// </summary>
    internal sealed partial class DebugBridge : MonoBehaviour
    {
        private string _dir;
        private StreamWriter _results;
        private readonly Queue<string> _queue = new Queue<string>();
        private int _loadSlot = -1;

        /// <summary>The one save a check may load: the test save the launcher
        /// named, whether it loaded it at start or left the game on the menu.</summary>
        private int _startSlot = -1;
        private string _varsFile;
        private bool _varsApplied;
        private float _waitUntil;
        private string _waitingFor;
        private float _waitDeadline;

        /// <summary>Adds the bridge when the game was started for a test;
        /// nothing otherwise.</summary>
        public static void InstallIfAsked(GameObject host)
        {
            string name = Arg("-mfBridge");
            if (name == null) return;
            var bridge = host.AddComponent<DebugBridge>();
            bridge._dir = Path.Combine(BepInEx.Paths.PluginPath, "SMSModForge", "DebugBridge", name);
            int slot;
            if (int.TryParse(Arg("-mfLoadSlot") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                bridge._loadSlot = bridge._startSlot = slot;
            else if (int.TryParse(Arg("-mfTestSlot") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out slot))
                bridge._startSlot = slot;
            bridge._varsFile = Arg("-mfVars");
            Application.runInBackground = true;
            if (Environment.GetCommandLineArgs().Contains("-mfMute")) AudioListener.volume = 0f;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        private void Start()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "shots"));
            _results = new StreamWriter(Path.Combine(_dir, "results.txt"), append: true) { AutoFlush = true };
            Say("bridge ready, scene " + SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (_results != null) _results.Dispose();
        }

        private void Say(string line)
        {
            if (_results == null) return;
            _results.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line);
        }

        private void Update()
        {
            AutoLoad();
            ApplyVarsOnce();
            ListenTick();
            WatchTick();
            if (_waitingFor != null && !Waiting()) _waitingFor = null;
            if (_waitingFor != null) return;

            string file = Path.Combine(_dir, "commands.txt");
            if (_queue.Count == 0 && File.Exists(file))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(file))
                        if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#")) _queue.Enqueue(line.Trim());
                    File.Delete(file);
                }
                catch (IOException) { return; }   // still being written
            }
            while (_queue.Count > 0 && Time.unscaledTime >= _waitUntil && _waitingFor == null)
            {
                string command = _queue.Dequeue();
                try { Run(command); }
                catch (Exception ex) { Say("error " + command + ": " + ex.Message); }
            }
        }

        // ── The fast way in ────────────────────────────────────────────

        private void AutoLoad()
        {
            if (_loadSlot < 0 || SceneManager.GetActiveScene().name != "GameStart") return;
            var manager = GameCreator.Runtime.Common.SaveLoadManager.Instance;
            if (manager == null || manager.IsLoading) return;
            if (!manager.HasSaveAt(_loadSlot))
            {
                Say("error load: slot " + _loadSlot + " has no save");
                _loadSlot = -1;
                return;
            }
            Say("loading slot " + _loadSlot);
            int slot = _loadSlot;
            _loadSlot = -1;
            var unused = manager.Load(slot);
        }

        private void ApplyVarsOnce()
        {
            if (_varsApplied || _varsFile == null || !InTheGame()) return;
            _varsApplied = true;
            if (!File.Exists(_varsFile)) { Say("error vars: no file " + _varsFile); return; }
            int set = 0;
            foreach (var raw in File.ReadAllLines(_varsFile))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                GameVariableBridge.Set(line.Substring(0, eq).Trim(), Value(line.Substring(eq + 1).Trim()));
                set++;
            }
            Say("vars applied: " + set);
        }

        /// <summary>
        /// In the game, and done arriving. GC2's Load changes the scene first
        /// and puts the save's variables back after: a value set the moment the
        /// scene was up was measured being overwritten by the save a frame later.
        /// </summary>
        private static bool InTheGame()
        {
            if (!Plugin.loaded || SceneManager.GetActiveScene().name != "CoreGameScene") return false;
            var manager = GameCreator.Runtime.Common.SaveLoadManager.Instance;
            return manager == null || !manager.IsLoading;
        }

        /// <summary>A variable's value as GC2 keeps it: true/false, a number
        /// (a double), or text.</summary>
        private static object Value(string text)
        {
            bool b;
            if (bool.TryParse(text, out b)) return b;
            double d;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return text;
        }

        // ── Commands ─────────────────────────────────────────────────────

        private bool Waiting()
        {
            if (Time.unscaledTime > _waitDeadline) { Say("timeout " + _waitingFor); return false; }
            if (_waitingFor == "ingame" && InTheGame()) { Say("ok wait-ingame " + SceneManager.GetActiveScene().name); return false; }
            if (_waitingFor == "dialogue-end" && !DialogueDispatcher.IsAnyDialoguePlayingGlobal()) { Say("ok wait-dialogue-end"); return false; }
            // "frame": a command answering at the end of the frame clears it.
            return true;
        }

        private void Run(string command)
        {
            var parts = command.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts[0].ToLowerInvariant();
            string a = parts.Length > 1 ? parts[1] : "";
            string rest = parts.Length > 2 ? parts[2] : "";
            switch (verb)
            {
                case "ping":
                    Say("ok ping scene=" + SceneManager.GetActiveScene().name + " loaded=" + Plugin.loaded
                        + " slot=" + VanillaSaveSlot.Current);
                    break;
                case "wait":
                    _waitUntil = Time.unscaledTime + Seconds(a, 1f);
                    Say("ok wait " + a);
                    break;
                case "wait-ingame":
                    _waitingFor = "ingame";
                    _waitDeadline = Time.unscaledTime + Seconds(a, 120f);
                    break;
                case "load":
                {
                    // Only the save the launcher started with - the test save
                    // (Tools/Game/make-test-save.ps1). The author's saves are
                    // never loaded by a check (the author, 2026-10-24).
                    int slot;
                    if (!int.TryParse(a, out slot)) slot = _startSlot;
                    if (slot != _startSlot || slot < 1) { Say("error load: slot " + slot + " is not the test save (" + _startSlot + ")"); break; }
                    var manager = GameCreator.Runtime.Common.SaveLoadManager.Instance;
                    if (manager == null || !manager.HasSaveAt(slot)) { Say("error load: slot " + slot + " has no save"); break; }
                    var unused = manager.Load(slot);
                    Say("ok load " + slot);
                    break;
                }
                case "get":
                    Say("ok get " + a + " = " + (GameVariableBridge.Get(a) ?? "(none)"));
                    break;
                case "set":
                    GameVariableBridge.Set(a, Value(rest));
                    Say("ok set " + a + " = " + (GameVariableBridge.Get(a) ?? "(none)"));
                    break;
                case "dialogue":
                {
                    // dialogue <packId> <key>
                    var dispatcher = Plugin.Dispatchers.FirstOrDefault(d => string.Equals(d.PackId, a, StringComparison.OrdinalIgnoreCase));
                    bool started = dispatcher != null && dispatcher.StartForBridge(rest.Trim());
                    Say(started ? "ok dialogue " + a + " " + rest : "error dialogue: no " + a + "/" + rest);
                    break;
                }
                case "wait-dialogue-end":
                    _waitingFor = "dialogue-end";
                    _waitDeadline = Time.unscaledTime + Seconds(a, 120f);
                    break;
                case "playing":
                    Say("ok playing " + DialogueDispatcher.IsAnyDialoguePlayingGlobal());
                    break;
                case "mute":
                    AudioListener.volume = a == "off" ? 1f : 0f;
                    Say("ok mute " + (AudioListener.volume == 0f ? "on" : "off"));
                    break;
                case "shot":
                {
                    string path = Path.Combine(_dir, "shots", (a.Length > 0 ? a : DateTime.Now.ToString("HHmmss")) + ".png");
                    // Through reflection: the plugin does not reference the
                    // module ScreenCapture lives in, for one debug command.
                    var capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
                    var method = capture != null ? capture.GetMethod("CaptureScreenshot", new[] { typeof(string) }) : null;
                    if (method == null) { Say("error shot: no ScreenCapture"); break; }
                    method.Invoke(null, new object[] { path });
                    Say("ok shot " + path);
                    break;
                }
                case "vanilla-dialogue":
                {
                    // vanilla-dialogue <path of one of the game's conversations>
                    string path = (a + " " + rest).Trim();
                    var conversation = VanillaDialogueInjector.Find(path);
                    if (conversation == null) { Say("error vanilla-dialogue: no " + path); break; }
                    var unused = conversation.Play(new GameCreator.Runtime.Common.Args(conversation.gameObject));
                    Say("ok vanilla-dialogue " + path);
                    break;
                }
                case "listen":
                    // listen <seconds> <name>: answers when the time is up
                    StartListening(Seconds(a, 5f), rest.Length > 0 ? rest.Trim() : DateTime.Now.ToString("HHmmss"));
                    break;
                case "watch":
                    // watch <seconds> <packId> <variable,variable>: answers when the time is up
                    StartWatching(Seconds(a, 5f), rest);
                    break;
                case "screen":
                    StartCoroutine(MeasureScreen(a.Length > 0 ? a : DateTime.Now.ToString("HHmmss")));
                    break;
                case "click":
                {
                    // click <a button's words>: what a player's click on it does
                    string words = (a + " " + rest).Trim();
                    Func<Component, bool> says = b => b.GetComponentsInChildren<TMP_Text>()
                        .Any(t => string.Equals(t.text.Trim(), words, StringComparison.OrdinalIgnoreCase));
                    var forge = FindObjectsOfType<ForgeButton>().FirstOrDefault(b => b != null && b.isActiveAndEnabled && b.Interactable && says(b));
                    if (forge != null) { forge.Press(); Say("ok click " + words); break; }
                    var button = FindObjectsOfType<UnityEngine.UI.Button>().FirstOrDefault(b =>
                        b != null && b.isActiveAndEnabled && b.interactable && says(b));
                    if (button == null) { Say("error click: no button '" + words + "'"); break; }
                    button.onClick.Invoke();
                    Say("ok click " + words);
                    break;
                }
                case "inspect":
                {
                    // inspect <root object>/<path below it>: where it is and what it draws
                    string path = (a + " " + rest).Trim();
                    int slash = path.IndexOf('/');
                    string rootName = slash < 0 ? path : path.Substring(0, slash);
                    var root = FindObjectsOfType<Transform>().FirstOrDefault(t => t.parent == null && t.name == rootName);
                    var found = root == null ? null : slash < 0 ? root : root.Find(path.Substring(slash + 1));
                    if (found == null) { Say("error inspect: no " + path); break; }
                    var rt = found as RectTransform;
                    var graphic = found.GetComponent<UnityEngine.UI.Graphic>();
                    var canvas = found.GetComponentInParent<Canvas>();
                    Say("ok inspect " + path + ": active " + found.gameObject.activeInHierarchy
                        + (rt != null ? ", rect " + N(rt.rect.width, "0") + "x" + N(rt.rect.height, "0")
                                        + " at " + N(rt.position.x, "0") + "," + N(rt.position.y, "0")
                                        + " scale " + N(rt.lossyScale.x, "0.###") : "")
                        + (graphic != null ? ", " + graphic.GetType().Name + " colour " + Hex(graphic.color) + " alpha "
                                             + N(graphic.color.a, "0.##") + " raycast " + graphic.raycastTarget
                                             + " enabled " + graphic.enabled + " canvasRenderer alpha "
                                             + N(graphic.canvasRenderer.GetAlpha(), "0.##") : "")
                        + (canvas != null ? ", canvas " + canvas.rootCanvas.name + " order " + canvas.rootCanvas.sortingOrder
                                            + " mode " + canvas.rootCanvas.renderMode : ""));
                    break;
                }
                case "materials":
                {
                    // materials <word>: materials named with it, and what on screen draws with them
                    string word = (a + " " + rest).Trim();
                    var named = Resources.FindObjectsOfTypeAll<Material>()
                        .Where(m => m != null && m.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(m => m.name + " (" + (m.shader != null ? m.shader.name : "-") + ")").Distinct().ToArray();
                    var users = FindObjectsOfType<UnityEngine.UI.Graphic>()
                        .Where(g => g != null && g.material != null && g.material.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(g => PathOf(g.transform) + " [" + g.material.name + ", " + Hex(g.color) + " a" + N(g.color.a, "0.##") + "]").ToArray();
                    Say("ok materials " + word + ": " + string.Join(", ", named) + " || used by: " + string.Join("; ", users));
                    break;
                }
                case "volumes":
                {
                    // Every post-processing volume, its weight and its effects
                    var found = new List<string>();
                    foreach (var b in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                    {
                        if (b == null || b.GetType().Name != "Volume" || !b.gameObject.scene.IsValid()) continue;
                        var type = b.GetType();
                        object weight = type.GetField("weight")?.GetValue(b) ?? type.GetProperty("weight")?.GetValue(b, null);
                        object profile = type.GetProperty("sharedProfile")?.GetValue(b, null) ?? type.GetField("sharedProfile")?.GetValue(b);
                        var components = profile?.GetType().GetField("components")?.GetValue(profile) as System.Collections.IEnumerable;
                        var effects = new List<string>();
                        if (components != null)
                            foreach (var c in components)
                                if (c != null) effects.Add(c.GetType().Name + (((bool?)c.GetType().GetField("active")?.GetValue(c)) == false ? "(off)" : ""));
                        found.Add(PathOf(b.transform) + (b.gameObject.activeInHierarchy ? "" : " (inactive)") + (b.enabled ? "" : " (disabled)")
                                  + " weight " + weight + " [" + string.Join(", ", effects.ToArray()) + "]");
                    }
                    Say("ok volumes " + string.Join(" | ", found.ToArray()));
                    break;
                }
                case "speaker":
                    Say("ok speaker " + Speakers());
                    break;
                case "menu":
                    Say("ok menu " + MenuRows());
                    break;
                case "log":
                    Debug.Log("[SMSModForge bridge] " + a + " " + rest);
                    Say("ok log");
                    break;
                case "quit":
                    Say("ok quit");
                    Application.Quit();
                    break;
                default:
                    if (!RunWorld(verb, a, rest)) Say("error unknown command: " + command);
                    break;
            }
        }

        private static float Seconds(string text, float otherwise)
        {
            float s;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out s) ? s : otherwise;
        }

        private static string N(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Hex(Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");

        private static string PathOf(Transform t)
            => t.parent == null ? t.name : t.parent.name + "/" + t.name;

        // ── Listening: the game's own sound, measured inside it ──────────
        //
        // Each AudioSource gets a filter that reads the samples passing through
        // it - before the game's mixer, the listener's volume and Windows'
        // mute, so it hears a game the launcher has muted. The listener gets
        // one too, for the whole mix.

        /// <summary>Reads what passes through one source, on the audio thread,
        /// and hands it over once a frame.</summary>
        private sealed class Tap : MonoBehaviour
        {
            private readonly object _gate = new object();
            private float _peak;
            private double _squares;
            private int _samples;
            private int _frames;
            private int _crossings;
            private float _last;

            /// <summary>How many times the audio thread has handed this filter
            /// samples at all, silent or not.</summary>
            public int Calls;

            private void OnAudioFilterRead(float[] data, int channels)
            {
                System.Threading.Interlocked.Increment(ref Calls);
                float peak = 0f, last = _last;
                double squares = 0;
                int crossings = 0;
                for (int i = 0; i < data.Length; i++)
                {
                    float s = data[i];
                    if (s > peak) peak = s;
                    else if (-s > peak) peak = -s;
                    squares += s * s;
                }
                // The first channel's zero crossings: the pitch of a tone.
                for (int i = 0; i < data.Length; i += Math.Max(1, channels))
                {
                    float s = data[i];
                    if ((s > 0f && last < 0f) || (s < 0f && last > 0f)) crossings++;
                    if (s != 0f) last = s;
                }
                lock (_gate)
                {
                    if (peak > _peak) _peak = peak;
                    _squares += squares;
                    _samples += data.Length;
                    _frames += data.Length / Math.Max(1, channels);
                    _crossings += crossings;
                    _last = last;
                }
            }

            public void Drain(out float peak, out float rms, out int frames, out int crossings)
            {
                lock (_gate)
                {
                    peak = _peak;
                    rms = _samples > 0 ? (float)Math.Sqrt(_squares / _samples) : 0f;
                    frames = _frames;
                    crossings = _crossings;
                    _peak = 0f;
                    _squares = 0;
                    _samples = _frames = _crossings = 0;
                }
            }
        }

        private sealed class Heard
        {
            public float First, Last, Peak, Volume;
            public long Frames, Crossings;
        }

        private sealed class Listening
        {
            public string Name;
            public float Started, Until;
            public StreamWriter Rows;
            public readonly List<Tap> Taps = new List<Tap>();
            public readonly Dictionary<string, Heard> Heard = new Dictionary<string, Heard>();

            /// <summary>Sources seen playing, and those of them Unity culled
            /// ("virtual"): a culled voice is never mixed, so no filter on it
            /// runs - which is what a listener volume of 0 can do.</summary>
            public readonly HashSet<string> Playing = new HashSet<string>(), Culled = new HashSet<string>();
        }

        private Listening _listening;

        private void StartListening(float seconds, string name)
        {
            if (_listening != null) { Say("error listen: already listening (" + _listening.Name + ")"); return; }
            var l = new Listening { Name = name, Started = Time.unscaledTime, Until = Time.unscaledTime + seconds };
            l.Rows = new StreamWriter(Path.Combine(_dir, "listen-" + name + ".tsv"), append: false);
            l.Rows.WriteLine("seconds\tsource\tvolume\tpeak\trms\thz");
            _listening = l;
            TapEverything(l);
        }

        /// <summary>Every source playing now, every frame: the game makes its
        /// one-shot sources as it plays them.</summary>
        private static void TapEverything(Listening l)
        {
            foreach (var source in FindObjectsOfType<AudioSource>())
                if (source != null && source.GetComponent<Tap>() == null) l.Taps.Add(source.gameObject.AddComponent<Tap>());
            var ear = FindObjectOfType<AudioListener>();
            if (ear != null && ear.GetComponent<Tap>() == null) l.Taps.Add(ear.gameObject.AddComponent<Tap>());
        }

        private void ListenTick()
        {
            var l = _listening;
            if (l == null) return;
            float now = Time.unscaledTime, t = now - l.Started;
            TapEverything(l);
            int rate = Math.Max(1, AudioSettings.outputSampleRate);
            foreach (var tap in l.Taps)
            {
                if (tap == null) continue;
                float peak, rms;
                int frames, crossings;
                tap.Drain(out peak, out rms, out frames, out crossings);
                bool everything = tap.GetComponent<AudioListener>() != null;
                var source = tap.GetComponent<AudioSource>();
                if (!everything && source != null && source.isPlaying)
                {
                    string playing = PathOf(tap.transform) + " " + (source.clip != null ? source.clip.name : "-");
                    l.Playing.Add(playing);
                    if (source.isVirtual) l.Culled.Add(playing);
                }
                if (frames == 0 || peak < 0.0005f) continue;

                string clip = everything ? "(everything)" : source != null && source.clip != null ? source.clip.name : "-";
                float volume = everything ? AudioListener.volume : source != null ? source.volume : 1f;
                string label = (everything ? "listener" : PathOf(tap.transform)) + " " + clip;
                float hz = crossings / 2f / ((float)frames / rate);
                l.Rows.WriteLine(N(t, "0.000") + "\t" + label + "\t" + N(volume, "0.###") + "\t" + N(peak, "0.0000")
                                 + "\t" + N(rms, "0.0000") + "\t" + N(hz, "0"));

                Heard h;
                if (!l.Heard.TryGetValue(label, out h)) l.Heard[label] = h = new Heard { First = t };
                h.Last = t;
                if (peak > h.Peak) { h.Peak = peak; h.Volume = volume; }
                // The pitch from the steady part of a tone only: a frame where
                // it starts or stops is half silence and would read low.
                if (rms > 0.6f * peak) { h.Frames += frames; h.Crossings += crossings; }
            }
            if (now < l.Until) return;

            _listening = null;
            var calls = l.Taps.Where(tap => tap != null)
                              .Select(tap => PathOf(tap.transform) + "=" + tap.Calls).ToArray();
            foreach (var tap in l.Taps) if (tap != null) Destroy(tap);
            l.Rows.Dispose();
            var said = new List<string>();
            foreach (var kv in l.Heard.OrderBy(kv => kv.Value.First))
            {
                var h = kv.Value;
                float hz = h.Frames > 0 ? h.Crossings / 2f / ((float)h.Frames / rate) : 0f;
                said.Add(kv.Key + " from " + N(h.First, "0.00") + " to " + N(h.Last, "0.00") + " s, peak " + N(h.Peak, "0.000")
                         + " at volume " + N(h.Volume, "0.###") + " = " + N(h.Peak * h.Volume, "0.000") + ", " + N(hz, "0") + " Hz");
            }
            Say("ok listen " + l.Name + ": " + (said.Count == 0 ? "nothing" : string.Join(" | ", said.ToArray()))
                + " || listener volume " + N(AudioListener.volume, "0.###") + ", " + l.Playing.Count + " source(s) seen playing, "
                + l.Culled.Count + " culled: " + string.Join(", ", l.Playing.Select(p => p + (l.Culled.Contains(p) ? " (culled)" : "")).ToArray())
                + " || filter calls: " + string.Join(", ", calls));
        }

        // ── Watching pack variables and transitions over time ────────────

        private sealed class Watching
        {
            public float Started, Until;
            public PackContext Pack;
            public string[] Names;
            public string[] Values;
            public int Transitions = -1;
            public readonly List<string> Changes = new List<string>();
        }

        private Watching _watching;

        private static readonly System.Reflection.FieldInfo _transitionsPlaying =
            typeof(TransitionRuntime).GetField("_playing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        private void StartWatching(float seconds, string what)
        {
            var words = what.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var pack = words.Length > 0 ? Plugin.LoadedContexts.FirstOrDefault(c => string.Equals(c.PackId, words[0], StringComparison.OrdinalIgnoreCase)) : null;
            if (pack == null) { Say("error watch: no pack " + (words.Length > 0 ? words[0] : "")); return; }
            var names = words.Length > 1 ? words[1].Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray() : new string[0];
            _watching = new Watching
            {
                Started = Time.unscaledTime, Until = Time.unscaledTime + seconds,
                Pack = pack, Names = names, Values = names.Select(n => pack.Vars.GetString(n)).ToArray(),
            };
        }

        private void WatchTick()
        {
            var w = _watching;
            if (w == null) return;
            string at = "+" + N(Time.unscaledTime - w.Started, "0.000") + " s ";
            for (int i = 0; i < w.Names.Length; i++)
            {
                string now = w.Pack.Vars.GetString(w.Names[i]);
                if (now == w.Values[i]) continue;
                w.Values[i] = now;
                w.Changes.Add(at + w.Names[i] + "=" + now);
                Say("watch " + at + w.Names[i] + " = " + now);
            }

            var playing = _transitionsPlaying != null ? _transitionsPlaying.GetValue(null) as System.Collections.IList : null;
            int count = playing != null ? playing.Count : -1;
            if (count != w.Transitions)
            {
                w.Transitions = count;
                string over = count > 0 ? " - " + Layers(playing) : "";
                w.Changes.Add(at + "transitions=" + count + over);
                Say("watch " + at + "transitions = " + count + over);
            }

            if (Time.unscaledTime < w.Until) return;
            _watching = null;
            Say("ok watch " + string.Join("; ", w.Changes.ToArray()));
        }

        /// <summary>Where a transition's canvas sorts against the dialogue
        /// box's: above it, or the lines show through.</summary>
        private static string Layers(System.Collections.IList playing)
        {
            var orders = new List<string>();
            foreach (var p in playing)
            {
                var raised = p.GetType().GetField("Raised")?.GetValue(p) as Canvas;
                var screen = p.GetType().GetField("Screen")?.GetValue(p) as GameObject;
                var canvas = raised != null ? raised : screen != null ? screen.GetComponentInParent<Canvas>() : null;
                if (canvas != null) orders.Add(canvas.rootCanvas.name + " order " + canvas.rootCanvas.sortingOrder);
            }
            var speech = GameCreator.Runtime.Dialogue.UnityUI.SpeechUI.Current;
            var box = speech != null ? speech.GetComponentInParent<Canvas>() : null;
            orders.Add("dialogue box " + (box != null ? box.rootCanvas.name + " order " + box.rootCanvas.sortingOrder : "not up"));
            return string.Join(", ", orders.ToArray());
        }

        // ── What is on screen ────────────────────────────────────────────

        /// <summary>
        /// The frame as drawn: its mean lightness, how much of it is near black,
        /// and the same for its bottom third, where the dialogue box is. Saved
        /// beside the results as well, for a person to look at.
        /// </summary>
        private System.Collections.IEnumerator MeasureScreen(string name)
        {
            yield return new WaitForEndOfFrame();
            var capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            var method = capture != null ? capture.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes) : null;
            if (method == null) { Say("error screen: no ScreenCapture"); yield break; }
            var texture = method.Invoke(null, null) as Texture2D;
            if (texture == null) { Say("error screen: nothing captured"); yield break; }
            var px = texture.GetPixels32();
            int w = texture.width, h = texture.height, bottomRows = h / 3;
            double all = 0, bottom = 0;
            int dark = 0, bottomDark = 0;
            for (int i = 0; i < px.Length; i++)
            {
                double light = (0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b) / 255.0;
                all += light;
                if (light < 0.04) dark++;
                if (i / w < bottomRows)   // rows run from the bottom up
                {
                    bottom += light;
                    if (light < 0.04) bottomDark++;
                }
            }
            File.WriteAllBytes(Path.Combine(_dir, "shots", name + ".png"), ImageConversion.EncodeToPNG(texture));
            Destroy(texture);
            int bottomCount = Math.Max(1, bottomRows * w);
            Say("ok screen " + name + " mean=" + N((float)(all / px.Length), "0.000") + " dark=" + N((float)dark / px.Length, "0.00")
                + " bottom-mean=" + N((float)(bottom / bottomCount), "0.000") + " bottom-dark=" + N((float)bottomDark / bottomCount, "0.00"));
        }

        /// <summary>Every speaker label the game colours, as set and as drawn.</summary>
        private static string Speakers()
        {
            var found = new List<string>();
            foreach (var text in FindObjectsOfType<TMP_Text>())
            {
                if (text == null || !text.isActiveAndEnabled) continue;
                if (!text.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "TMPWordColorizer")) continue;
                string drawn = "-";
                var info = text.textInfo;
                if (info != null && info.meshInfo != null && info.meshInfo.Length > 0
                    && info.meshInfo[0].colors32 != null && info.meshInfo[0].colors32.Length > 0)
                    drawn = Hex(info.meshInfo[0].colors32[0]);
                found.Add("'" + text.text + "' colour " + Hex(text.color) + " drawn " + drawn);
            }
            return found.Count == 0 ? "(none)" : string.Join(" | ", found.ToArray());
        }

        /// <summary>The pack list on the main menu, row by row, with each
        /// row's colour.</summary>
        private static string MenuRows()
        {
            var rows = new SortedDictionary<int, string>();
            foreach (var rect in FindObjectsOfType<RectTransform>())
            {
                const string prefix = "SMSModForgeBanner_";
                int index;
                if (rect == null || !rect.name.StartsWith(prefix, StringComparison.Ordinal)
                    || !int.TryParse(rect.name.Substring(prefix.Length), out index)) continue;
                var tmp = rect.GetComponentInChildren<TMP_Text>();
                var text = rect.GetComponentInChildren<UnityEngine.UI.Text>();
                string words = tmp != null ? tmp.text : text != null ? text.text : "";
                Color colour = tmp != null ? tmp.color : text != null ? text.color : Color.clear;
                rows[index] = index + ": '" + words.Replace("\n", " / ") + "' " + Hex(colour);
            }
            return rows.Count == 0 ? "(no rows)" : string.Join(" | ", rows.Values.ToArray());
        }
    }
}
