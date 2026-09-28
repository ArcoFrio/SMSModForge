using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// What a save made in this session keeps besides the running packs' own
    /// files:
    /// <list type="bullet">
    ///   <item>the loaded save's files of the packs that are not running -
    ///   switched off on the main menu, not loaded, or not installed - carried
    ///   across unchanged (<see cref="SaveRecord.Carried"/>), so a pack left
    ///   off for an evening finds its progress where it left it;</item>
    ///   <item>none of the files the save it replaces had for other packs
    ///   (<see cref="SaveRecord.Stale"/>), which would otherwise be read as
    ///   this save's;</item>
    ///   <item>the save's record (<see cref="SaveRecord"/>).</item>
    /// </list>
    /// Run after each of the session's writes: the autosave on sleeping (and
    /// its Monday copy) and a save from the save menu.
    /// </summary>
    internal static class SaveCarry
    {
        private const string Tag = "[SMSModForge.PackPlugin] Save: ";

        /// <summary>The files carried, by name - every one meant to be, read or
        /// not: one that could not be read is still never taken out of a slot
        /// as somebody else's, which in the loaded slot would lose it.</summary>
        private static readonly List<string> _carriedNames = new List<string>();

        /// <summary>What was read of them when the save was loaded.</summary>
        private static readonly Dictionary<string, byte[]> _carried =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The loaded save's record, or null: a new game, or a save
        /// made before there were records.</summary>
        public static SaveRecord Loaded { get; private set; }

        /// <summary>The names of the files in the loaded save's slot.</summary>
        public static List<string> LoadedFiles { get; private set; } = new List<string>();

        private static HashSet<string> _installed;

        /// <summary>A new scene: a new game, or a save about to be loaded.</summary>
        public static void Reset()
        {
            _carriedNames.Clear();
            _carried.Clear();
            Loaded = null;
            LoadedFiles = new List<string>();
            _installed = null;
        }

        /// <summary>The installed packs' ids, read once a scene.</summary>
        public static HashSet<string> Installed
        {
            get { return _installed ?? (_installed = Plugin.InstalledPackIds()); }
        }

        public static string SlotFolder(string savesRoot, int slot)
        {
            return Path.Combine(savesRoot ?? "", "NANOSAVE_" + slot.ToString("D4"));
        }

        /// <summary>
        /// A save was loaded from <paramref name="slot"/>: read its record, and
        /// the files of its packs that are not running.
        /// </summary>
        public static void Capture(string savesRoot, int slot, ICollection<string> running, ManualLogSource log)
        {
            Reset();
            string folder = SlotFolder(savesRoot, slot);
            LoadedFiles = FilesIn(folder, log);
            if (LoadedFiles.Any(n => string.Equals(n, SaveRecord.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    Loaded = SaveRecord.Parse(File.ReadAllText(Path.Combine(folder, SaveRecord.FileName)));
                    if (Loaded == null) log?.LogWarning(Tag + "slot " + slot + "'s " + SaveRecord.FileName + " could not be read; it is written again at the next save.");
                }
                catch (Exception e)
                {
                    log?.LogWarning(Tag + "slot " + slot + "'s " + SaveRecord.FileName + " could not be read: " + e.Message);
                }
            }

            _carriedNames.AddRange(SaveRecord.Carried(LoadedFiles, running));
            foreach (string name in _carriedNames)
            {
                try { _carried[name] = File.ReadAllBytes(Path.Combine(folder, name)); }
                catch (Exception e)
                {
                    log?.LogWarning(Tag + name + " in slot " + slot + " could not be read, so the saves made from "
                                    + "this one will not have it: " + e.Message);
                }
            }
            if (_carriedNames.Count > 0)
                log?.LogInfo(Tag + "the data of " + string.Join(", ", _carriedNames.Select(SaveLoadChecks.PackIdOfSaveFile).ToArray())
                             + " - not running now - goes into every save made from this one, unchanged.");
        }

        /// <summary>
        /// Change what this session knows it has told the player about packs
        /// that are not running (<see cref="SaveRecord.ToldMissing"/>). Only in
        /// memory: it reaches a save when the autosave commits it
        /// (<see cref="LiveTold"/>), like every other change a session makes.
        /// </summary>
        public static void NoteTold(IList<string> told)
        {
            if (Loaded == null)
            {
                if (told == null || told.Count == 0) return;
                Loaded = new SaveRecord();
            }
            Loaded.ToldMissing.Clear();
            if (told != null) Loaded.ToldMissing.AddRange(told);
        }

        /// <summary>What the autosave commits: the session's told list as it stands.</summary>
        public static List<string> LiveTold
        {
            get { return Loaded != null ? Loaded.ToldMissing.ToList() : new List<string>(); }
        }

        /// <summary>
        /// What a save from the menu carries: the told list the source slot's
        /// record holds - what the last commit wrote there - and never the
        /// session's own, which only the autosave commits. None when the slot
        /// has no record.
        /// </summary>
        public static List<string> CommittedTold(string savesRoot, int slot, ManualLogSource log)
        {
            string path = Path.Combine(SlotFolder(savesRoot, slot), SaveRecord.FileName);
            try
            {
                return File.Exists(path) ? SaveRecord.ToldIn(File.ReadAllText(path)) : new List<string>();
            }
            catch (Exception e)
            {
                log?.LogWarning(Tag + "slot " + slot + "'s " + SaveRecord.FileName + " could not be read: " + e.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// The session has just written its running packs into
        /// <paramref name="slot"/>: put the carried files beside them, take out
        /// the replaced save's files of packs this one has no data for, and
        /// write the record.
        /// </summary>
        /// <param name="told">The told list the record keeps: <see cref="LiveTold"/>
        /// from the autosave, which commits; <see cref="CommittedTold"/> of the
        /// source slot from a save from the menu, which only transfers.</param>
        public static void Complete(string savesRoot, int slot, IReadOnlyList<PackContext> contexts, ManualLogSource log,
                                    IList<string> told)
        {
            if (slot < 1) return;
            string folder = SlotFolder(savesRoot, slot);
            var running = (contexts ?? new List<PackContext>()).Where(c => c != null && c.Vars != null).ToList();
            var runningIds = running.Select(c => c.PackId).ToList();
            try
            {
                Directory.CreateDirectory(folder);
                foreach (var file in _carried)
                    File.WriteAllBytes(Path.Combine(folder, file.Key), file.Value);

                foreach (string name in SaveRecord.Stale(FilesIn(folder, log), runningIds, _carriedNames))
                {
                    File.Delete(Path.Combine(folder, name));
                    log?.LogInfo(Tag + "slot " + slot + ": " + name + " taken out - it was the replaced save's, "
                                 + "and this one has no data for that pack.");
                }

                var record = new SaveRecord
                {
                    ForgeVersion = ForgeVersion.Current,
                    GameBuild = Plugin.GameBuild ?? "",
                    Saved = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    Language = PluginLanguage.Code ?? "",
                    XUnityLanguage = XUnityLink.Loaded ? XUnityLink.Language ?? "" : "",
                };
                SaveRecord.AddPacks(record,
                                    running.Select(c => new KeyValuePair<string, string>(c.PackId, VersionOf(c))),
                                    _carriedNames, Installed, PackSwitchSetting.Off, Loaded);
                if (told != null) record.ToldMissing.AddRange(told);
                File.WriteAllText(Path.Combine(folder, SaveRecord.FileName), record.ToJson());
            }
            catch (Exception e)
            {
                log?.LogError(Tag + "slot " + slot + " could not be finished: " + e.Message);
            }
        }

        private static string VersionOf(PackContext c)
        {
            try { return (string)c.Pack?.Root?["version"] ?? ""; }
            catch { return ""; }
        }

        private static List<string> FilesIn(string folder, ManualLogSource log)
        {
            try
            {
                if (Directory.Exists(folder))
                    return Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
            }
            catch (Exception e)
            {
                log?.LogWarning(Tag + "could not list " + folder + ": " + e.Message);
            }
            return new List<string>();
        }
    }
}
