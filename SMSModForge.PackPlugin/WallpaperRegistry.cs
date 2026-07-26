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
            /// <summary>Standard condition array (AND semantics, groups) —
            /// null/empty = always visible. The factory folds the legacy
            /// single-object form into this.</summary>
            public JArray UnlockConditions;
            public PackVariableStore Vars;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public void Register(Entry e) => _entries.Add(e);

        public IReadOnlyList<Entry> All => _entries;

        public void Reset() => _entries.Clear();

        /// <summary>
        /// Re-evaluate each wallpaper button's visibility against its
        /// <see cref="Entry.UnlockConditions"/>. Called once per frame
        /// from the plugin's Update; cheap (one condition-list eval per
        /// pack-authored wallpaper, total).
        /// </summary>
        public void Tick(ManualLogSource log)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Button == null) continue;
                // ConditionEvaluator.All treats null/empty as pass — an
                // unconditioned wallpaper is always visible.
                bool visible = ConditionEvaluator.All(e.UnlockConditions, e.Vars, log, e.PackId);
                if (e.Button.activeSelf != visible)
                    e.Button.SetActive(visible);
            }
        }
    }
}
