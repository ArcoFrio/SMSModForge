using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Dialogue.UnityUI;
using GameCreator.Runtime.VisualScripting;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// <c>timeline &lt;seconds&gt; &lt;name&gt;</c>: what decides whether a pack
    /// dialogue may start, frame by frame, for that long - each line a change,
    /// with the time since the command: the maps that are up, the game's scripts
    /// running (every Trigger and Actions list), its room talks, its lock, the
    /// conversation on screen and whose it is. Written to
    /// <c>timeline-&lt;name&gt;.txt</c> beside the results; answers when the time
    /// is up. For seeing when the game commits to one of its own conversations
    /// on arrival, which no single reading can show.
    /// </summary>
    internal sealed partial class DebugBridge
    {
        private sealed class Timeline
        {
            public float Started;
            public float Until;
            public string Name;
            public readonly Dictionary<string, string> Last = new Dictionary<string, string>();
            public readonly List<string> Lines = new List<string>();
        }

        private Timeline _timeline;

        private static readonly System.Reflection.FieldInfo _pendingStartsField =
            typeof(DialogueDispatcher).GetField("_pendingStarts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        private void StartTimeline(float seconds, string name)
        {
            _timeline = new Timeline { Started = Time.unscaledTime, Until = Time.unscaledTime + seconds, Name = name };
            TimelineTick();
        }

        private void TimelineTick()
        {
            var t = _timeline;
            if (t == null) return;

            Note(t, "levels", Children("5_Levels", false));
            Note(t, "talks", Children("8_Room_Talk", true));
            Note(t, "scripts", RunningScripts());
            Note(t, "lock", Bool("Lock-Game"));
            Note(t, "transfer", Bool("Start-Transfer"));
            var current = Dialogue.Current;
            Note(t, "on screen", current != null ? FullPath(current.transform) : "-");
            Note(t, "box", DialogueUI.IsOpen ? "open" : "closed");
            Note(t, "modforge pending", _pendingStartsField != null ? Convert.ToString(_pendingStartsField.GetValue(null)) : "?");

            if (Time.unscaledTime < t.Until) return;
            _timeline = null;
            string file = Path.Combine(_dir, "timeline-" + t.Name + ".txt");
            File.WriteAllLines(file, t.Lines.ToArray());
            Say("ok timeline " + t.Name + " " + t.Lines.Count + " changes -> " + file);
        }

        private static void Note(Timeline t, string what, string now)
        {
            string was;
            if (t.Last.TryGetValue(what, out was) && was == now) return;
            t.Last[what] = now;
            t.Lines.Add("+" + N(Time.unscaledTime - t.Started, "0.000") + " f" + Time.frameCount + " " + what + ": " + now);
        }

        private static string Bool(string name)
        {
            object v = GameVariableBridge.Get(name);
            return v == null ? "(none)" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The active children of a root, and with
        /// <paramref name="running"/> whether each one's own Trigger runs.</summary>
        private static string Children(string root, bool running)
        {
            var go = GameObject.Find(root);
            if (go == null) return "(no " + root + ")";
            var up = new List<string>();
            foreach (Transform child in go.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                string name = child.name;
                if (running)
                {
                    var trigger = child.GetComponent<Trigger>();
                    if (trigger != null && trigger.IsRunning) name += " (running " + trigger.RunningIndex + ")";
                }
                up.Add(name);
            }
            return up.Count == 0 ? "(none)" : string.Join(", ", up.ToArray());
        }

        private static string RunningScripts()
        {
            var running = UnityEngine.Object.FindObjectsOfType<BaseActions>()
                .Where(s => s != null && s.IsRunning)
                .Select(s => FullPath(s.transform) + " [" + s.GetType().Name + " #" + s.RunningIndex + "]")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            return running.Length == 0 ? "(none)" : string.Join(" | ", running);
        }
    }
}
