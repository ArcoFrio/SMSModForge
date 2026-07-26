using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameCreator.Runtime.Common.UnityUI; // ButtonInstructions
using TMPro;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Copies each loaded pack's per-slot save file when the player performs a
    /// <em>manual</em> save through the vanilla NanoSave menu, mirroring
    /// the host mod's <c>SaveManager</c> save-slot listeners — except it copies
    /// the pack's <c>SMSModForge_&lt;packId&gt;.json</c> rather than
    /// the host mod's own <c>.txt</c>.
    /// <para/>
    /// Why this is needed: the sleep autosave only ever writes the dedicated
    /// autosave slot (slot 1). The loaded slot — and any other slot the player
    /// saves to from the menu — is frozen by vanilla until the player explicitly
    /// saves there. To keep the pack file in lockstep with the vanilla + the host mod
    /// saves, we hook the NanoSave "save to new slot" / "overwrite slot" buttons
    /// and, after the vanilla write settles, copy the right source slot's pack
    /// file into the target slot.
    /// <para/>
    /// The source slot matches the host mod exactly: the autosave slot (1) when a
    /// sleep autosave already happened this session (or it's a brand-new game),
    /// otherwise the currently loaded slot.
    /// </summary>
    internal sealed class PackManualSaveSync : MonoBehaviour
    {
        private bool _listenersAttached;
        private int _pendingOverwriteSlot = -1;

        // Marker components stamped on hooked buttons so re-scans don't stack
        // duplicate listeners. Distinct from SaveManager's own markers.
        private sealed class EmptySlotMarker : MonoBehaviour { }
        private sealed class OverwriteSlotMarker : MonoBehaviour { }

        private void Update()
        {
            if (Plugin.currentScene.name != "CoreGameScene" || !Plugin.loaded) return;

            // NanoSave is a root GameObject that activates when the save menu
            // opens. GetRootGameObjects includes inactive roots, so we find it
            // either way and gate on activeSelf.
            Transform nano = Plugin.currentScene.GetRootGameObjects()
                .FirstOrDefault(go => go.name == "NanoSave")?.transform;
            bool active = nano != null && nano.gameObject.activeSelf;

            if (active && !_listenersAttached)
            {
                AttachSaveSlotListeners(nano);
                _listenersAttached = true;
            }
            else if (!active && _listenersAttached)
            {
                _listenersAttached = false;
            }
        }

        // ── Listener attachment ──────────────────────────────────────────────

        private void AttachSaveSlotListeners(Transform nanoSave)
        {
            // Path: NanoSave > Content > Content > List > Viewport > Content
            Transform content = nanoSave.Find("Content/Content/List/Viewport/Content");
            if (content == null)
            {
                Log("Could not navigate NanoSave hierarchy to the slot list.");
                return;
            }

            for (int i = 0; i < content.childCount; i++)
            {
                Transform child = content.GetChild(i);
                if (child.name.StartsWith("EmptySaveSlot")) AttachEmptySlotListener(child);
                else if (child.name.StartsWith("SaveSlots")) AttachOverwriteListener(child);
            }
        }

        private void AttachEmptySlotListener(Transform emptySlot)
        {
            if (emptySlot.GetComponent<EmptySlotMarker>() != null) return;

            var button = emptySlot.GetComponent<ButtonInstructions>()
                         ?? emptySlot.GetComponentsInChildren<ButtonInstructions>(true).FirstOrDefault();
            if (button == null) { Log("EmptySaveSlot has no ButtonInstructions."); return; }

            button.onClick.AddListener(SaveToNextAvailableSlot);
            emptySlot.gameObject.AddComponent<EmptySlotMarker>();
        }

        private void AttachOverwriteListener(Transform saveSlot)
        {
            if (saveSlot.GetComponent<OverwriteSlotMarker>() != null) return;

            // The slot number is shown in SaveSlots(Clone) > Right > Save 0001.
            // The label reads "Save XXXX" in vanilla, but translation mods
            // replace the prefix (e.g. "保存 1"), so extract the first run of
            // digits instead of anchoring on the English word. The GO name
            // ("Save 0001") is not localised, only the TMP text is.
            var label = saveSlot.Find("Right/Save 0001")?.GetComponent<TextMeshProUGUI>();
            if (label == null) { Log("Overwrite slot label not found."); return; }
            string text = label.text ?? "";
            Match digits = Regex.Match(text, @"\d+");
            if (!digits.Success || !int.TryParse(digits.Value, out int targetSlot))
            {
                Log("Could not parse slot number from '" + text + "'.");
                return;
            }

            var button = saveSlot.Find("Image (1)/Button (1)")?.GetComponent<ButtonInstructions>();
            if (button == null) { Log("Overwrite ButtonInstructions not found for slot " + targetSlot + "."); return; }

            button.onClick.AddListener(() => OverwriteSlot(targetSlot));
            saveSlot.gameObject.AddComponent<OverwriteSlotMarker>();
        }

        // ── Click handlers (deferred so the vanilla write lands first) ────────

        private void SaveToNextAvailableSlot()
        {
            if (!InMyRoom()) return;
            CancelInvoke(nameof(CopyToLatestSlot));
            Invoke(nameof(CopyToLatestSlot), 0.2f);
        }

        private void OverwriteSlot(int targetSlot)
        {
            if (!InMyRoom() || targetSlot < 1) return;
            _pendingOverwriteSlot = targetSlot;
            CancelInvoke(nameof(PerformOverwrite));
            Invoke(nameof(PerformOverwrite), 0.2f);
        }

        // The two manual-save buttons mirror SaveManager's two paths exactly,
        // and they differ ONLY in what happens when the source file is missing:
        //   • New/empty slot (SaveToLatestSlot): copy source → latest folder;
        //     if the source file doesn't exist, do NOTHING (no live write).
        //   • Overwrite (PerformOverwriteSaveSlot): copy source → target; if the
        //     source file doesn't exist, write the current in-memory state.
        private void CopyToLatestSlot()
        {
            int latest = FindLatestSlot();
            if (latest > 0) CopyPacks(latest, liveFallback: false);
        }

        private void PerformOverwrite()
        {
            if (_pendingOverwriteSlot > 0) CopyPacks(_pendingOverwriteSlot, liveFallback: true);
        }

        // ── Copy logic ───────────────────────────────────────────────────────

        /// <summary>
        /// Source slot for a manual save, identical to SaveManager: the autosave
        /// slot (1) for a new game (no loaded slot) or once a sleep autosave has
        /// happened this session; otherwise the loaded slot ("the current save").
        /// </summary>
        private static int SourceSlot()
        {
            int loaded = VanillaSaveSlot.Current;
            return (loaded == -1 || Plugin.AutosaveProcedThisSession) ? 1 : loaded;
        }

        /// <summary>
        /// Copy each loaded pack's file from <see cref="SourceSlot"/> into
        /// <paramref name="targetSlot"/>. When the source file is missing,
        /// <paramref name="liveFallback"/> chooses between SaveManager's two
        /// behaviours: overwrite writes the current in-memory state; a new-slot
        /// save writes nothing (so a manual save still never captures
        /// uncommitted mid-day changes when a committed source exists).
        /// </summary>
        private void CopyPacks(int targetSlot, bool liveFallback)
        {
            int source = SourceSlot();

            foreach (var c in Plugin.LoadedContexts)
            {
                var store = c?.Vars;
                if (store == null) continue;
                try
                {
                    string src = store.SlotFilePath(source);
                    string dst = store.SlotFilePath(targetSlot);
                    if (src != null && File.Exists(src))
                    {
                        // Copying a file onto itself throws; skip (matches the
                        // net effect of SaveManager's caught self-copy).
                        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Copy(src, dst, true);
                    }
                    else if (liveFallback)
                    {
                        // Overwrite with no committed source — write the current
                        // cache, mirroring SaveManager's SaveToFile(targetSlot).
                        store.SaveToSlot(targetSlot);
                    }
                    else
                    {
                        Log("No committed pack file at slot " + source + " for '" + c.PackId +
                            "'; new-slot save left slot " + targetSlot + " untouched.");
                    }
                }
                catch (Exception ex)
                {
                    Log("Copy of pack '" + c.PackId + "' to slot " + targetSlot + " failed: " + ex.Message);
                }
            }
            Log("Manual save (" + (liveFallback ? "overwrite" : "new slot") +
                "): pack files from slot " + source + " → slot " + targetSlot + ".");
        }

        /// <summary>Most-recently-modified NANOSAVE_xxxx slot number, or -1.</summary>
        private int FindLatestSlot()
        {
            var store = Plugin.LoadedContexts.FirstOrDefault(c => c?.Vars != null)?.Vars;
            string anyPath = store?.SlotFilePath(1);
            if (anyPath == null) return -1;
            string savesRoot = Path.GetDirectoryName(Path.GetDirectoryName(anyPath));
            if (!Directory.Exists(savesRoot)) return -1;

            string latest = Directory.GetDirectories(savesRoot, "NANOSAVE_*")
                .OrderByDescending(Directory.GetLastWriteTime)
                .FirstOrDefault();
            if (latest == null) return -1;

            string name = Path.GetFileName(latest);
            return name.StartsWith("NANOSAVE_") &&
                   int.TryParse(name.Substring("NANOSAVE_".Length), out int slot)
                   ? slot : -1;
        }

        private static bool InMyRoom()
        {
            Transform myRoom = GameObject.Find("5_Levels")?.transform.Find("5_MyRoom");
            return myRoom != null && myRoom.gameObject.activeSelf;
        }

        private static void Log(string msg)
        {
            var log = Plugin.Log;
            if (log != null) log.LogInfo("[SMSModForge.PackPlugin] [ManualSave] " + msg);
            else Debug.Log("[SMSModForge.PackPlugin] [ManualSave] " + msg);
        }
    }
}
