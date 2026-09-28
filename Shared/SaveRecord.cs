using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What ModForge writes into a save slot beside the game's own files: which
    /// ModForge, which game build, which language, and every pack the save has
    /// data for - with its version and whether it was running, switched off or
    /// not installed when the save was made.
    /// <para/>
    /// A pack's own file (<c>SMSModForge_&lt;id&gt;.json</c>) says only that the
    /// save has data for it. This says what that pack was: a save loaded after
    /// the pack has gone can name it and its version rather than an id, and
    /// somebody sorting out a broken save can read what it was made with.
    /// <para/>
    /// Named so it can never be read as a pack's file (<see cref="SaveLoadChecks.SaveFilePrefix"/>
    /// is <c>SMSModForge_</c>).
    /// </summary>
    public sealed class SaveRecord
    {
        public const string FileName = "SMSModForge-save.json";

        public const string Running = "running";
        public const string SwitchedOff = "off";

        /// <summary>Installed and switched on, and still not running: it could
        /// not be loaded, and the log says why.</summary>
        public const string NotLoaded = "notLoaded";

        public const string NotInstalled = "notInstalled";

        /// <summary>
        /// Why a pack the save has data for is not running: switched off (only
        /// an installed pack can be - the setting outlives an uninstall), not
        /// loaded (installed and switched on), or not installed.
        /// </summary>
        public static string WhyNotRunning(string id, ICollection<string> installedPackIds, ICollection<string> switchedOffPackIds)
        {
            if (!Has(installedPackIds, id)) return NotInstalled;
            return Has(switchedOffPackIds, id) ? SwitchedOff : NotLoaded;
        }

        public string ForgeVersion = "";
        public string GameBuild = "";

        /// <summary>When the save was written, in UTC, ISO 8601.</summary>
        public string Saved = "";

        /// <summary>The language ModForge was playing packs in.</summary>
        public string Language = "";

        /// <summary>XUnity.AutoTranslator's language when it was loaded; empty when it was not.</summary>
        public string XUnityLanguage = "";

        public readonly List<Pack> Packs = new List<Pack>();

        /// <summary>
        /// The packs the player has been told this save has data for while they
        /// were not running - told once, and not again every time the save is
        /// loaded without them (2026-09-27). A pack comes off the list the
        /// first time it runs with the save again, so taking it away after that
        /// tells them once more. See <see cref="SaveLoadChecks.NotYetTold"/>.
        /// <para/>
        /// Kept like everything else a save keeps: committed by the autosave on
        /// sleeping, and carried unchanged by a save from the menu, which only
        /// transfers what the last commit wrote. So a player who reads the
        /// notice and quits before the autosave is told again next time.
        /// </summary>
        public readonly List<string> ToldMissing = new List<string>();

        public const string ToldMissingField = "toldMissing";

        public sealed class Pack
        {
            public string Id = "";
            public string Version = "";
            public string State = Running;
        }

        /// <summary>The pack with this id, or null.</summary>
        public Pack Find(string id)
        {
            return Packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public string ToJson()
        {
            var json = new JObject
            {
                ["forgeVersion"] = ForgeVersion ?? "",
                ["gameBuild"] = GameBuild ?? "",
                ["saved"] = Saved ?? "",
                ["language"] = Language ?? "",
            };
            if (!string.IsNullOrEmpty(XUnityLanguage)) json["xunityLanguage"] = XUnityLanguage;
            var packs = new JArray();
            foreach (var p in Packs.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
                packs.Add(new JObject { ["id"] = p.Id ?? "", ["version"] = p.Version ?? "", ["state"] = p.State ?? Running });
            json["packs"] = packs;
            SetTold(json, ToldMissing);
            return json.ToString(Formatting.Indented);
        }

        /// <summary>A record read back, or null when the text is not one - an
        /// older save has none, and a damaged file is no reason to stop a load.</summary>
        public static SaveRecord Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                var json = JObject.Parse(text);
                var record = new SaveRecord
                {
                    ForgeVersion = (string)json["forgeVersion"] ?? "",
                    GameBuild = (string)json["gameBuild"] ?? "",
                    Saved = (string)json["saved"] ?? "",
                    Language = (string)json["language"] ?? "",
                    XUnityLanguage = (string)json["xunityLanguage"] ?? "",
                };
                var packs = json["packs"] as JArray;
                if (packs != null)
                    foreach (var p in packs.OfType<JObject>())
                    {
                        string id = (string)p["id"];
                        if (string.IsNullOrEmpty(id)) continue;
                        record.Packs.Add(new Pack { Id = id, Version = (string)p["version"] ?? "", State = (string)p["state"] ?? Running });
                    }
                var told = json[ToldMissingField] as JArray;
                if (told != null)
                    record.ToldMissing.AddRange(Ids(told.Select(t => t.Type == JTokenType.String ? (string)t : null)));
                return record;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The packs of a save being written: the running ones at the version
        /// running, and every carried file's pack with why it is not running
        /// and the version the loaded save recorded for it - the version its
        /// data came from. A save made before records leaves that empty rather
        /// than guessed: the pack installed now may not be the one that wrote
        /// the data.
        /// </summary>
        /// <param name="running">Id and version of each running pack.</param>
        public static void AddPacks(SaveRecord record, IEnumerable<KeyValuePair<string, string>> running,
                                    IEnumerable<string> carriedFileNames, ICollection<string> installedPackIds,
                                    ICollection<string> switchedOffPackIds, SaveRecord loaded)
        {
            foreach (var pack in running ?? Enumerable.Empty<KeyValuePair<string, string>>())
                if (!string.IsNullOrEmpty(pack.Key) && record.Find(pack.Key) == null)
                    record.Packs.Add(new Pack { Id = pack.Key, Version = pack.Value ?? "", State = Running });

            foreach (string name in carriedFileNames ?? Enumerable.Empty<string>())
            {
                string id = SaveLoadChecks.PackIdOfSaveFile(name);
                if (id == null || record.Find(id) != null) continue;
                var had = loaded == null ? null : loaded.Find(id);
                record.Packs.Add(new Pack
                {
                    Id = id,
                    Version = had == null ? "" : had.Version,
                    State = WhyNotRunning(id, installedPackIds, switchedOffPackIds),
                });
            }
        }

        /// <summary>
        /// The told list a save's record file holds, or none: what a save from
        /// the menu carries across, since it only transfers what was committed.
        /// </summary>
        public static List<string> ToldIn(string recordText)
        {
            var record = Parse(recordText);
            return record == null ? new List<string>() : record.ToldMissing.ToList();
        }

        private static void SetTold(JObject json, IEnumerable<string> told)
        {
            var ids = Ids(told);
            if (ids.Count > 0) json[ToldMissingField] = new JArray(ids.Cast<object>().ToArray());
            else json.Remove(ToldMissingField);
        }

        /// <summary>Pack ids once each, in one order, so the same list always
        /// reads the same.</summary>
        internal static List<string> Ids(IEnumerable<string> ids)
        {
            return (ids ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── Which pack files a written save keeps ────────────────────────

        /// <summary>
        /// The pack files in the loaded save that the session carries into every
        /// save it writes: those of packs that are not running - switched off, or
        /// no longer installed. Nothing reads them while the pack is not running,
        /// so they are copied as they are, and the pack finds its progress where
        /// it left it when it runs again.
        /// <para/>
        /// Without this, a pack switched off for one evening lost its progress in
        /// every save made that evening: only running packs' files were ever
        /// written into a new save.
        /// </summary>
        public static List<string> Carried(IEnumerable<string> fileNamesInLoadedSlot, ICollection<string> runningPackIds)
        {
            var carried = new List<string>();
            foreach (string name in fileNamesInLoadedSlot ?? Enumerable.Empty<string>())
            {
                string id = SaveLoadChecks.PackIdOfSaveFile(name);
                if (id == null || Has(runningPackIds, id)) continue;
                carried.Add(name);
            }
            carried.Sort(StringComparer.OrdinalIgnoreCase);
            return carried;
        }

        /// <summary>
        /// The pack files already in a slot that is being written over which
        /// belong to no pack of the save going into it - neither running nor
        /// carried. They are the overwritten save's, and left there they would
        /// be read as this save's: switching that pack on and loading the slot
        /// would bring back another playthrough's progress.
        /// </summary>
        public static List<string> Stale(IEnumerable<string> fileNamesInTargetSlot, ICollection<string> runningPackIds,
                                         ICollection<string> carriedFileNames)
        {
            var stale = new List<string>();
            foreach (string name in fileNamesInTargetSlot ?? Enumerable.Empty<string>())
            {
                string id = SaveLoadChecks.PackIdOfSaveFile(name);
                if (id == null || Has(runningPackIds, id)) continue;
                if (carriedFileNames != null && carriedFileNames.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                stale.Add(name);
            }
            stale.Sort(StringComparer.OrdinalIgnoreCase);
            return stale;
        }

        private static bool Has(ICollection<string> ids, string id)
        {
            return ids != null && ids.Any(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
