using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Name → place registry. The whole point of this plugin's navigator
    /// design is to <em>never</em> bake sibling indices into manifests, so
    /// every place — vanilla and pack-defined — is addressed by stable name
    /// and resolved to its current sibling index at load time.
    /// <para/>
    /// Keys are wire-format tokens, matching what the editor writes into the
    /// <c>"target"</c> field of a navigator button:
    /// <list type="bullet">
    ///   <item><c>vanilla:&lt;goName&gt;</c> — e.g. <c>vanilla:14_Beach</c>. Populated lazily the first time a button references it.</item>
    ///   <item><c>pack:&lt;packId&gt;.&lt;placeKey&gt;</c> — populated when each pack's places are built.</item>
    /// </list>
    /// The <c>self:&lt;key&gt;</c> sugar is rewritten to <c>pack:</c> form by
    /// the caller before lookup.
    /// </summary>
    public static class PlaceRegistry
    {
        public sealed class Entry
        {
            public int AbsoluteSiblingIndex;
            public GameObject Level;     // may be null for vanilla entries (we don't need it for navigation)
            public GameObject RoomTalk;  // may be null for vanilla entries — vanilla TransferScene activates roomtalk by itself
            public string WeatherType;   // "None", "Inside", or "Outside" — drives vanilla weather system activation
        }

        private static readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>();

        public static void Reset() => _byKey.Clear();

        public static void RegisterPackPlace(string packId, string placeKey, int absoluteIndex, GameObject level, GameObject roomTalk, string weatherType = "None")
            => _byKey["pack:" + packId + "." + placeKey] = new Entry
            {
                AbsoluteSiblingIndex = absoluteIndex, Level = level, RoomTalk = roomTalk, WeatherType = weatherType ?? "None",
            };

        public static Entry Resolve(string token, string thisPackId, Transform level5Root)
        {
            if (string.IsNullOrEmpty(token)) return null;
            int colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1) return null;
            string scheme = token.Substring(0, colon);
            string rest = token.Substring(colon + 1);

            switch (scheme)
            {
                case "vanilla":
                    return ResolveVanilla(rest, level5Root);
                case "self":
                    if (string.IsNullOrEmpty(thisPackId)) return null;
                    return ResolvePack(thisPackId, rest);
                case "pack":
                    int dot = rest.IndexOf('.');
                    if (dot <= 0 || dot == rest.Length - 1) return null;
                    return ResolvePack(rest.Substring(0, dot), rest.Substring(dot + 1));
                default:
                    return null;
            }
        }

        private static Entry ResolveVanilla(string goName, Transform level5Root)
        {
            string key = "vanilla:" + goName;
            if (_byKey.TryGetValue(key, out var existing)) return existing;
            if (level5Root == null) return null;
            var t = level5Root.Find(goName);
            if (t == null) return null;
            var entry = new Entry
            {
                AbsoluteSiblingIndex = t.GetSiblingIndex(),
                Level = t.gameObject,
                RoomTalk = null,
            };
            _byKey[key] = entry;
            return entry;
        }

        private static Entry ResolvePack(string packId, string key)
        {
            _byKey.TryGetValue("pack:" + packId + "." + key, out var entry);
            return entry;
        }
    }
}
