using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-pack tracker of built wallpapers + their visibility gates.
    /// The plugin's per-frame Tick re-evaluates each entry's
    /// <see cref="Entry.UnlockCondition"/> through the standard
    /// <see cref="ConditionEvaluator"/> and toggles the selector button.
    /// Display GOs (the actual full-screen wallpaper) stay player-driven
    /// — the click handler activates them; conditions only gate the
    /// selector button's visibility.
    /// </summary>
    public sealed class WallpaperRegistry
    {
        public sealed class Entry
        {
            public string Key;
            public string PackId;
            public GameObject Button;
            public GameObject Display;
            public JObject UnlockCondition;    // null = always visible
            public PackVariableStore Vars;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public void Register(Entry e) => _entries.Add(e);

        public IReadOnlyList<Entry> All => _entries;

        public void Reset() => _entries.Clear();

        /// <summary>
        /// Re-evaluate each wallpaper button's visibility against its
        /// <see cref="Entry.UnlockCondition"/>. Called once per frame
        /// from the plugin's Update; cheap (one condition eval per
        /// pack-authored wallpaper, total).
        /// </summary>
        public void Tick(ManualLogSource log)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Button == null) continue;
                bool visible = e.UnlockCondition == null
                    ? true
                    : ConditionEvaluator.Evaluate(e.UnlockCondition, e.Vars, log, e.PackId);
                if (e.Button.activeSelf != visible)
                    e.Button.SetActive(visible);
            }
        }
    }
}
