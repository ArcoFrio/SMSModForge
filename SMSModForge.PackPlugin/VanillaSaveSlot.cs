using System;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Reflects the active NanoSave slot out of the vanilla
    /// <c>SaveLoadManager</c> singleton so the plugin can mirror the host mod's
    /// per-slot file layout without a hard reference to the
    /// <c>Fullscreen.NanoSave</c> assembly.
    /// <para/>
    /// Returns <c>-1</c> when no slot is loaded yet (main menu, pre-slot-pick)
    /// or when the type / property can't be resolved. The plugin treats
    /// <c>-1</c> as "no active slot" and skips disk IO accordingly, which
    /// matches the host mod's <c>WaitAndLoadSaveFile</c> coroutine: pack saves
    /// only become real once the player commits to a slot.
    /// </summary>
    internal static class VanillaSaveSlot
    {
        private static Type _slmType;
        private static PropertyInfo _slotLoadedProp;
        private static FieldInfo _slotLoadedField;
        private static UnityEngine.Object _cachedInstance;
        private static bool _resolved;

        /// <summary>Current loaded slot, or -1 when unavailable.</summary>
        public static int Current
        {
            get
            {
                if (!_resolved) ResolveType();
                if (_slmType == null) return -1;

                // FindObjectOfType is cheap on a populated scene but we still
                // cache the instance because the slot watcher runs every
                // frame. When the cached instance dies (e.g. scene reload),
                // null-flag and re-find next frame.
                //
                // Suppress the FindObjectOfType deprecation warning: the
                // FindFirstObjectByType replacement only landed in Unity
                // 2022 LTS, and the target game (Starmaker Story 1.8E)
                // ships on 2020.3 where the new API isn't available.
                if (_cachedInstance == null)
#pragma warning disable 0618
                    _cachedInstance = UnityEngine.Object.FindObjectOfType(_slmType);
#pragma warning restore 0618
                if (_cachedInstance == null) return -1;

                try
                {
                    object raw = _slotLoadedProp != null
                        ? _slotLoadedProp.GetValue(_cachedInstance)
                        : _slotLoadedField?.GetValue(_cachedInstance);
                    return raw is int i ? i : -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// Drop the cached SaveLoadManager reference. Called on scene reload
        /// so the next slot read re-discovers the new scene's instance.
        /// </summary>
        public static void Reset()
        {
            _cachedInstance = null;
        }

        /// <summary>
        /// Walk every loaded assembly and pin down the
        /// <c>SaveLoadManager</c> type + its <c>SlotLoaded</c> member by
        /// name. Tolerates both <c>SaveLoadManager</c> in any namespace
        /// and the <c>Fullscreen.NanoSave.SaveLoadManager</c> namespaced
        /// form so plugin authors don't have to declare a hard reference.
        /// Member is preferred as a property; the field fallback covers
        /// older NanoSave revisions that expose it bare.
        /// </summary>
        private static void ResolveType()
        {
            _resolved = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // First try the simple bare name in any namespace.
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    if (types == null) continue;
                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        if (t.Name != "SaveLoadManager") continue;
                        _slmType = t;
                        _slotLoadedProp = t.GetProperty("SlotLoaded",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (_slotLoadedProp == null)
                            _slotLoadedField = t.GetField("SlotLoaded",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (_slotLoadedProp != null || _slotLoadedField != null) return;
                        // Type matched but neither member found — keep looking
                        // in case another assembly carries a fuller copy.
                        _slmType = null;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SMSModForge.PackPlugin] VanillaSaveSlot: ResolveType failed — " + ex.Message);
            }
        }
    }
}
