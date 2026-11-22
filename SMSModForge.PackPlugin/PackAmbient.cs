using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A place's own base light (1.10.0, the author: darkening a place - "Sure,
    /// let's go ahead"): the light every picture on its layers starts from,
    /// instead of the scene's one global light (white, 1, on every layer). URP
    /// takes the first global light it finds for a layer and complains of a
    /// second, so while this object is on, the scene's global lights let go of
    /// the layers it takes (the place's; the characters' too when asked) and its
    /// own global light comes on; switched off - the object, its parent, the
    /// place left by any road - its light goes off first and they take them back.
    /// Tied to the object so it cannot outlive the place: the game switches a
    /// place's objects off with it. Several on at once: the last switched on is
    /// the one, as a dialogue switching day to night expects.
    /// </summary>
    internal sealed class PackAmbient : MonoBehaviour
    {
        /// <summary>Its global light, kept off until it is the one.</summary>
        internal Behaviour Light;
        /// <summary>The sorting layers it takes over.</summary>
        internal int[] Layers = new int[0];

        private static readonly List<PackAmbient> _on = new List<PackAmbient>();
        /// <summary>The scene's own global lights' layers, as they were before any place took one.</summary>
        private static readonly Dictionary<Behaviour, int[]> _own = new Dictionary<Behaviour, int[]>();

        private void OnEnable()
        {
            _on.Remove(this);
            _on.Add(this);
            Settle();
        }

        private void OnDisable()
        {
            _on.Remove(this);
            if (Light != null) Light.enabled = false;
            Settle();
        }

        private static void Settle()
        {
            var type = PackLights.Light2DType();
            if (type == null) return;
            _on.RemoveAll(a => a == null);
            foreach (var gone in _own.Keys.Where(k => k == null).ToList()) _own.Remove(gone);
            var winner = _on.Count > 0 ? _on[_on.Count - 1] : null;
            foreach (var a in _on)
                if (a != winner && a.Light != null) a.Light.enabled = false;

            var lightType = type.GetProperty("lightType");
            var layers = type.GetProperty("targetSortingLayers");
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(type))
            {
                var light = obj as Behaviour;
                if (light == null || light.GetComponent<PackAmbient>() != null) continue;
                if (!string.Equals(Convert.ToString(lightType?.GetValue(light, null)), "Global", StringComparison.Ordinal)) continue;
                if (!_own.TryGetValue(light, out var own))
                    _own[light] = own = (layers?.GetValue(light, null) as int[]) ?? new int[0];
                var want = winner == null ? own : own.Where(l => !winner.Layers.Contains(l)).ToArray();
                layers?.SetValue(light, want, null);
            }
            if (winner != null && winner.Light != null) winner.Light.enabled = true;
        }
    }
}
