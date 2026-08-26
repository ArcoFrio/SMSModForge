using System;
using System.Collections;
using System.Reflection;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Reflection-only bridge to GC2's <c>GlobalNameVariablesManager</c>.
    /// <para/>
    /// The manager's <c>Values</c> dictionary (private) maps asset IDs to
    /// <c>NameVariableRuntime</c> instances. Each runtime exposes public
    /// <c>Exists</c>, <c>Get</c>, and <c>Set</c> methods — the last of
    /// which fires <c>EventChange</c> so GC2 triggers and conditions react.
    /// <para/>
    /// We only need deep reflection for the one private property
    /// (<c>Values</c>) on the manager. Everything else goes through the
    /// public <c>NameVariableRuntime</c> API, which is far more reliable
    /// than the direct-mutation approach (setting <c>nameVar.Value</c>
    /// bypasses <c>EventChange</c> and silently breaks listener-driven
    /// flows like <c>TransferScene</c>).
    /// </summary>
    internal static class GameVariableBridge
    {
        private static bool _initialised;
        private static object _manager;

        // The one piece of deep reflection we need: the private Values
        // dictionary on GlobalNameVariablesManager.
        private static PropertyInfo _valuesProp;

        // Cached MethodInfos on NameVariableRuntime (public methods).
        private static MethodInfo _nvrExists;
        private static MethodInfo _nvrGet;
        private static MethodInfo _nvrSet;

        private static void Init()
        {
            if (_initialised) return;
            _initialised = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var gnvmType = asm.GetType("GameCreator.Runtime.Variables.GlobalNameVariablesManager");
                if (gnvmType == null) continue;

                // Get the singleton instance. `Instance` is declared on the
                // generic base class `Singleton<GlobalNameVariablesManager>`,
                // not on GlobalNameVariablesManager itself. Reflection
                // requires BindingFlags.FlattenHierarchy to surface static
                // members inherited from base classes — without it,
                // GetProperty/GetField returns null even though
                // `GlobalNameVariablesManager.Instance` compiles fine.
                const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic
                                                 | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                _manager = FindInstance(gnvmType, staticFlags);
                if (_manager == null) return;

                // Private Values property → Dictionary<IdString, NameVariableRuntime>
                _valuesProp = gnvmType.GetProperty("Values", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_valuesProp == null) return;

                // Cache NameVariableRuntime's public methods.
                var nvrType = asm.GetType("GameCreator.Runtime.Variables.NameVariableRuntime");
                if (nvrType != null)
                {
                    _nvrExists = nvrType.GetMethod("Exists", new[] { typeof(string) });
                    _nvrGet = nvrType.GetMethod("Get", new[] { typeof(string) });
                    _nvrSet = nvrType.GetMethod("Set", new[] { typeof(string), typeof(object) });
                }
                return;
            }
        }

        /// <summary>
        /// Looks for the singleton <c>Instance</c> member on the given
        /// type, walking up the inheritance chain explicitly as a belt-
        /// and-suspenders backup in case <see cref="BindingFlags.FlattenHierarchy"/>
        /// alone doesn't surface it. Tries property then field at each
        /// level.
        /// </summary>
        private static object FindInstance(System.Type startType, BindingFlags staticFlags)
        {
            for (var t = startType; t != null && t != typeof(object); t = t.BaseType)
            {
                var prop = t.GetProperty("Instance", staticFlags);
                if (prop != null)
                {
                    try { var v = prop.GetValue(null); if (v != null) return v; }
                    catch { /* keep walking */ }
                }
                var field = t.GetField("Instance", staticFlags);
                if (field != null)
                {
                    try { var v = field.GetValue(null); if (v != null) return v; }
                    catch { /* keep walking */ }
                }
            }
            return null;
        }

        /// <summary>
        /// Iterates every <c>NameVariableRuntime</c> in the manager's
        /// <c>Values</c> dictionary and returns the first one that
        /// contains <paramref name="name"/>.
        /// </summary>
        private static object FindRuntime(string name)
        {
            if (_manager == null || _valuesProp == null || _nvrExists == null) return null;
            var values = _valuesProp.GetValue(_manager) as IDictionary;
            if (values == null) return null;

            foreach (DictionaryEntry pair in values)
            {
                var nvr = pair.Value;
                if (nvr == null) continue;
                bool exists = (bool)_nvrExists.Invoke(nvr, new object[] { name });
                if (exists) return nvr;
            }
            return null;
        }

        /// <summary>
        /// Reads a GC2 global variable by name. Returns <c>null</c> when
        /// the variable is not found or reflection fails.
        /// </summary>
        public static object Get(string name)
        {
            Init();
            if (_nvrGet == null) return null;
            try
            {
                var nvr = FindRuntime(name);
                if (nvr == null) return null;
                return _nvrGet.Invoke(nvr, new object[] { name });
            }
            catch { return null; }
        }

        /// <summary>Reads a GC2 global boolean variable.</summary>
        public static bool GetBool(string name)
        {
            var v = Get(name);
            return v is bool b && b;
        }

        /// <summary>Reads a GC2 global numeric variable.</summary>
        public static double GetNumber(string name)
        {
            var v = Get(name);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            return 0.0;
        }

        /// <summary>
        /// A hash over the name and value of EVERY global variable the game
        /// currently holds, plus how many there are. Same numbers → almost
        /// certainly the same state; different numbers → something moved.
        /// <para/>
        /// Exists so a caller can tell whether the vanilla save has finished
        /// restoring. There is no "loaded" flag to read, and sampling a couple
        /// of variables only proves those two settled — the values are restored
        /// as a batch, so watching the whole set is what actually answers the
        /// question. Returns <c>(0, 0)</c> while the manager is unreachable,
        /// which reads as "not settled" to a caller comparing successive
        /// samples.
        /// <para/>
        /// Walks every variable, so it is meant for the handful of frames
        /// before a caller latches — not for per-frame use forever.
        /// </summary>
        public static void StateFingerprint(out int hash, out int count)
        {
            hash = 0;
            count = 0;
            Init();
            if (_manager == null || _valuesProp == null) return;
            try
            {
                var values = _valuesProp.GetValue(_manager) as IDictionary;
                if (values == null) return;

                int h = 17;
                int n = 0;
                foreach (DictionaryEntry pair in values)
                {
                    // Each NameVariableRuntime enumerates its NameVariables, and
                    // NameVariable.Title is already "<name>: <value>" — so one
                    // string per variable covers both without reaching into the
                    // value's type.
                    if (!(pair.Value is IEnumerable vars)) continue;
                    foreach (var v in vars)
                    {
                        if (v == null) continue;
                        if (_titleProp == null)
                            _titleProp = v.GetType().GetProperty("Title",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                        var title = _titleProp?.GetValue(v) as string;
                        h = unchecked(h * 31 + (title == null ? 0 : title.GetHashCode()));
                        n++;
                    }
                }
                hash = h;
                count = n;
            }
            catch { hash = 0; count = 0; }
        }

        /// <summary>Cached <c>TVariable.Title</c> getter, resolved off the first
        /// variable seen (the concrete type isn't known up front).</summary>
        private static PropertyInfo _titleProp;

        /// <summary>
        /// Writes a value into a GC2 global variable using the public
        /// <c>NameVariableRuntime.Set(string, object)</c> method. This
        /// properly sets the value AND fires <c>EventChange</c> so GC2
        /// triggers and conditions that listen on the variable react
        /// immediately.
        /// </summary>
        public static void Set(string name, object value)
        {
            Init();
            if (_nvrSet == null) return;
            try
            {
                var nvr = FindRuntime(name);
                if (nvr == null) return;
                _nvrSet.Invoke(nvr, new object[] { name, value });
            }
            catch { /* swallow — pack-side variable writes shouldn't crash the game */ }
        }

        /// <summary>Convenience — sets a boolean GC2 variable.</summary>
        public static void SetBool(string name, bool value) => Set(name, (object)value);

        /// <summary>
        /// Convenience — sets a numeric GC2 variable. GC2 stores these
        /// as <c>double</c>.
        /// </summary>
        public static void SetDouble(string name, double value) => Set(name, (object)value);
    }
}
