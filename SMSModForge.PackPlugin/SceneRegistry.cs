using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Per-pack scene catalog. Holds the GameObject created by
    /// <see cref="SceneFactory"/> for each scene authored on a
    /// <c>SceneDef</c>, keyed by the pack-local scene key. The
    /// <c>ActivateScene</c> action looks the GO up by key and toggles
    /// it on; <c>DeactivateAllScenes</c> iterates the registry's values.
    /// <para/>
    /// Each pack has its own <see cref="SceneRegistry"/> instance hanging
    /// off <see cref="PackContext.Scenes"/>, so two packs can both define
    /// a "kiss01" scene without clashing — actions executed inside a
    /// pack's dialogue look up against that pack's own registry.
    /// </summary>
    public sealed class SceneRegistry
    {
        public sealed class Entry
        {
            public GameObject SceneGo;

            /// <summary>
            /// Signal name to emit alongside activation, or null when no
            /// override applies (the prototype's <c>Trigger</c> was
            /// either kept intact or stripped without replacement).
            /// </summary>
            public string ActivationSignal;
        }

        private readonly Dictionary<string, Entry> _byKey =
            new Dictionary<string, Entry>(System.StringComparer.Ordinal);

        public void Register(string key, GameObject sceneGo, string activationSignal)
        {
            if (string.IsNullOrEmpty(key) || sceneGo == null) return;
            _byKey[key] = new Entry { SceneGo = sceneGo, ActivationSignal = activationSignal };
        }

        public bool TryGet(string key, out Entry entry) => _byKey.TryGetValue(key, out entry);

        /// <summary>Every scene this pack created — used by DeactivateAllScenes.</summary>
        public IEnumerable<Entry> All => _byKey.Values;

        /// <summary>Drop every registered scene's GO without destroying it (scene reload handles cleanup).</summary>
        public void Reset() => _byKey.Clear();
    }
}
