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
            /// <summary>The pack-local key this was registered under, so
            /// anything walking the registry can name what it found.</summary>
            public string Key;

            public GameObject SceneGo;

            /// <summary>
            /// Signal name to emit alongside activation, or null when no
            /// override applies (the prototype's <c>Trigger</c> was
            /// either kept intact or stripped without replacement).
            /// </summary>
            public string ActivationSignal;

            /// <summary>Whether showing this scene counts as taking a Starmaker
            /// photo - see <see cref="StarmakerPhotos"/>.</summary>
            public bool StarmakerPhoto;
        }

        private readonly Dictionary<string, Entry> _byKey =
            new Dictionary<string, Entry>(System.StringComparer.Ordinal);

        public void Register(string key, GameObject sceneGo, string activationSignal,
                             bool starmakerPhoto = false)
        {
            if (string.IsNullOrEmpty(key) || sceneGo == null) return;
            _byKey[key] = new Entry
            {
                Key = key,
                SceneGo = sceneGo,
                ActivationSignal = activationSignal,
                StarmakerPhoto = starmakerPhoto,
            };
        }

        public bool TryGet(string key, out Entry entry) => _byKey.TryGetValue(key, out entry);

        /// <summary>Every scene this pack created — used by DeactivateAllScenes.</summary>
        public IEnumerable<Entry> All => _byKey.Values;

        // The game's own scenes this pack switched on by where they sit (1.9.0),
        // so "hide every scene this pack showed" hides those too. Objects of a
        // load since replaced are gone (null) and skipped.
        private readonly List<GameObject> _gameShown = new List<GameObject>();

        /// <summary>Remember one of the game's own scenes this pack showed.</summary>
        public void ShowedGameScene(GameObject sceneGo)
        {
            if (sceneGo == null) return;
            _gameShown.RemoveAll(g => g == null);
            if (!_gameShown.Contains(sceneGo)) _gameShown.Add(sceneGo);
        }

        /// <summary>The game's own scenes this pack showed and that still exist.</summary>
        public IEnumerable<GameObject> GameScenesShown
        {
            get { foreach (var g in _gameShown) if (g != null) yield return g; }
        }

        /// <summary>Drop every registered scene's GO without destroying it (scene reload handles cleanup).</summary>
        public void Reset()
        {
            _byKey.Clear();
            _gameShown.Clear();
        }
    }
}
