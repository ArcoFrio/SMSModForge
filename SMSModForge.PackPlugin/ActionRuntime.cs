using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Executes pack-authored node actions. Called by the dispatcher's
    /// <c>EventStartNext</c> / <c>EventFinishNext</c> handlers with the
    /// current dialogue's id-to-action-list maps. Stateless on its own —
    /// every state mutation goes through the variable store, the actor
    /// registry, or direct scene calls.
    /// <para/>
    /// <see cref="ExecuteList"/> returns true if any persisted variable was
    /// touched, so the dispatcher knows to flush the store to disk.
    /// </summary>
    public static class ActionRuntime
    {
        /// <summary>
        /// Run every action in <paramref name="actions"/> in declaration
        /// order. Returns true if the variable store needs to be flushed.
        /// </summary>
        public static bool ExecuteList(JArray actions, PackContext ctx)
        {
            if (actions == null) return false;
            bool dirty = false;
            foreach (var a in actions)
            {
                try { if (ExecuteOne((JObject)a, ctx)) dirty = true; }
                catch (System.Exception ex)
                {
                    ctx.Log?.LogError("[SMSModForge.PackPlugin] Action " +
                        (string)((JObject)a)["type"] + " threw: " + ex.Message);
                }
            }
            return dirty;
        }

        private static bool ExecuteOne(JObject a, PackContext ctx)
        {
            string type = (string)a["type"];
            var p = a["params"] as JObject ?? new JObject();

            switch (type)
            {
                case "SetVariable":
                    {
                        string name = (string)p["name"];
                        string value = (string)p["value"] ?? "";
                        // Vanilla writes go to the GC2 GlobalNameVariable store and
                        // don't touch the pack store, so they never need a flush.
                        if (IsVanilla(p)) { SetGameVar(name, value); return false; }
                        return ctx.Vars.Set(name, value);
                    }

                case "IncrementVariable":
                    {
                        string name = (string)p["name"];
                        float.TryParse((string)p["delta"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var delta);
                        if (IsVanilla(p))
                        {
                            GameVariableBridge.SetDouble(name, GameVariableBridge.GetNumber(name) + delta);
                            return false;
                        }
                        return ctx.Vars.Increment(name, delta);
                    }

                case "SetActorBust":
                    ctx.Actors.SetBust((string)p["actor"] ?? "", (string)p["bustKey"] ?? "");
                    return false;

                case "SetActorExpression":
                    // Apply immediately so the visual updates even between nodes.
                    ctx.Actors.ApplyNodeVisuals((string)p["actor"] ?? "", (string)p["expression"] ?? "");
                    return false;

                case "SetSpriteFocus":
                    {
                        // Raise/lower the whole cast's busts over the CG layer —
                        // the ModForge replacement for the vanilla SpriteFocus
                        // signal marker. Focuses every actor in the dialogue (no
                        // per-actor target); the dispatcher resets it on end.
                        bool.TryParse((string)p["focused"] ?? "true", out var focused);
                        ctx.Actors.SetSpriteFocusAll(focused);
                        return false;
                    }

                case "DeactivateBust":
                    {
                        var bust = ctx.Actors.GetCurrentBustGo((string)p["actor"] ?? "");
                        if (bust != null) bust.SetActive(false);
                        else ctx.Log?.LogWarning("[SMSModForge.PackPlugin] DeactivateBust: no current bust for actor '" +
                                                 (string)p["actor"] + "'");
                        return false;
                    }

                case "LeaveBust":
                    {
                        // Trigger the bust's vanilla fade-out animation by
                        // activating the MBase1/Leave child (which carries a
                        // FadeInSprite + Trigger that handle the staged exit
                        // + eventual parent deactivation). If the bust GO
                        // doesn't have a Leave child (e.g. a pack-built
                        // bust without that prefab branch), we fall back to
                        // an immediate SetActive(false) so the author still
                        // gets the bust off-stage.
                        var bust = ctx.Actors.GetCurrentBustGo((string)p["actor"] ?? "");
                        if (bust == null)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] LeaveBust: no current bust for actor '" +
                                                (string)p["actor"] + "'");
                            return false;
                        }
                        var leave = bust.transform.Find("MBase1/Leave");
                        if (leave != null)
                            leave.gameObject.SetActive(true);
                        else
                            bust.SetActive(false);
                        return false;
                    }

                case "SetGameObjectActive":
                    {
                        // Unified Set-Active. 'kind' routes the target: Scene goes
                        // through the pack's scene registry (and plays its sound on
                        // activate); every other kind (Bust / Level Overlay / Direct
                        // Path) resolves a GameObject by name/path and toggles it.
                        // 'target' is canonical; 'path'/'scene' are read as legacy
                        // fallbacks so pre-unify packs keep working.
                        string kind = (string)p["kind"] ?? "";
                        string target = (string)p["target"] ?? (string)p["path"] ?? (string)p["scene"] ?? "";
                        bool.TryParse((string)p["active"] ?? "true", out var active);

                        if (kind == "Scene")
                        {
                            ToggleScene(target, active, ctx);
                        }
                        else
                        {
                            // Level Overlay with an explicit 'overlayLevel': resolve the
                            // overlay strictly WITHIN that level (including inactive
                            // children), so an overlay in a level we're transitioning into
                            // is found there — not in a same-named object still active in
                            // the previous level. Any other kind, or no level token, uses
                            // the global resolve (which also finds inactive objects, so an
                            // overlay that starts disabled can still be activated).
                            GameObject levelGo = null;
                            if (IsOverlayKind(kind))
                            {
                                string overlayLevel = (string)p["overlayLevel"] ?? "";
                                if (!string.IsNullOrEmpty(overlayLevel))
                                {
                                    var level5 = GameObject.Find("5_Levels")?.transform;
                                    levelGo = Plugin.ResolveLevelTarget(overlayLevel, ctx.PackId, level5);
                                }
                            }

                            GameObject go;
                            if (levelGo != null)
                            {
                                go = TransformExtensions.FindDescendantIncludingInactive(levelGo.transform, target);
                                if (go == null)
                                    ctx.Log?.LogWarning("[SMSModForge.PackPlugin] SetGameObjectActive: overlay '" +
                                        target + "' not found under level '" + (string)p["overlayLevel"] + "'");
                            }
                            else
                            {
                                go = TransformExtensions.ResolveGameObject(target);
                                if (go == null)
                                    ctx.Log?.LogWarning("[SMSModForge.PackPlugin] SetGameObjectActive: '" + target + "' not found");
                            }
                            if (go != null) go.SetActive(active);
                        }
                        return false;
                    }

                case "EmitSignal":
                    EmitGc2Signal((string)p["signal"], ctx);
                    return false;

                case "EmitSignalDelayed":
                    {
                        string sig = (string)p["signal"];
                        float.TryParse((string)p["seconds"] ?? "0", NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var delay);
                        if (!string.IsNullOrEmpty(sig) && ctx.Plugin != null)
                            ctx.Plugin.StartCoroutine(EmitSignalAfter(delay, sig, ctx));
                        return false;
                    }

                case "TransitionLevels":
                    {
                        // Cross-fade primitive — the host mod's EmitSignalGameObjectDelayed.
                        // Resolves both level tokens through the same path
                        // LevelActive uses, then drives the staged
                        // enable/wait/disable/wait/emit sequence on a
                        // fire-and-forget coroutine so the dialogue keeps
                        // running while the transition unfolds.
                        string fromToken = (string)p["fromLevel"] ?? "";
                        string toToken = (string)p["toLevel"] ?? "";
                        string sig = (string)p["signal"] ?? "";
                        float.TryParse((string)p["seconds"] ?? "3", NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var seconds);

                        var level5 = GameObject.Find("5_Levels")?.transform;
                        var fromGo = Plugin.ResolveLevelTarget(fromToken, ctx.PackId, level5);
                        var toGo = Plugin.ResolveLevelTarget(toToken, ctx.PackId, level5);
                        if (fromGo == null && toGo == null)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] TransitionLevels: " +
                                "neither fromLevel '" + fromToken + "' nor toLevel '" +
                                toToken + "' resolved — skipping.");
                            return false;
                        }
                        if (ctx.Plugin != null)
                            ctx.Plugin.StartCoroutine(TransitionLevelsCoroutine(
                                seconds, fromGo, toGo, sig, ctx));
                        return false;
                    }

                case "FadeSprite":
                    {
                        float.TryParse((string)p["to"] ?? "0", NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var to);
                        float.TryParse((string)p["seconds"] ?? "1", NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out var seconds);
                        // Category-aware resolve (Direct Path / Level Overlay / Places / Bust),
                        // finding inactive objects. Falls back to legacy 'path'.
                        var go = ResolveByKind(p, ctx);
                        var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                        if (sr == null)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] FadeSprite: target has no " +
                                "SpriteRenderer (or wasn't found).");
                            return false;
                        }
                        if (ctx.Plugin != null)
                            ctx.Plugin.StartCoroutine(FadeSpriteCoroutine(sr, to, seconds));
                        return false;
                    }

                case "MoveGameObject":
                    {
                        // (target resolved via ResolveByKind below)
                        // Eased move that HOLDS the target (a level pan
                        // slides the level), or — when 'home' is set — eases back
                        // to where the object started and releases. See PackMover.
                        var go = ResolveByKind(p, ctx);
                        if (go == null)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] MoveGameObject: target '" +
                                (string)p["target"] + "' not found.");
                            return false;
                        }
                        float.TryParse((string)p["seconds"] ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var mSecs);
                        bool.TryParse((string)p["home"] ?? "false", out var home);

                        var mover = go.GetComponent<PackMover>() ?? go.AddComponent<PackMover>();
                        if (home)
                        {
                            // Return to the position captured before the first move.
                            mover.MoveHome(mSecs);
                            return false;
                        }

                        float.TryParse((string)p["x"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var mx);
                        float.TryParse((string)p["y"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var my);
                        bool.TryParse((string)p["relative"] ?? "false", out var relative);

                        // World position — matches the host mod's pan and stays
                        // meaningful despite the level parent's small scale.
                        Vector3 cur = go.transform.position;
                        Vector3 dest = relative
                            ? new Vector3(cur.x + mx, cur.y + my, cur.z)
                            : new Vector3(mx, my, cur.z);
                        mover.MoveTo(dest, mSecs);
                        return false;
                    }

                case "SpinGameObject":
                    {
                        // Start/stop a constant Z-spin via a PackSpin component.
                        var go = ResolveByKind(p, ctx);
                        if (go == null)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] SpinGameObject: target '" +
                                (string)p["target"] + "' not found.");
                            return false;
                        }
                        float.TryParse((string)p["speed"] ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var speed);
                        bool.TryParse((string)p["enable"] ?? "true", out var enable);

                        var spin = go.GetComponent<PackSpin>();
                        if (spin == null) spin = go.AddComponent<PackSpin>();
                        spin.DegreesPerSecond = speed;
                        spin.enabled = enable;
                        return false;
                    }

                case "PlaySFX":
                    {
                        // Look up the entry, pick a random clip from
                        // its loaded variants, then schedule a
                        // PlayOneShot (immediately if delay is 0,
                        // otherwise on a fire-and-forget coroutine so
                        // the dialogue doesn't block).
                        string clipName = (string)p["clip"];
                        if (string.IsNullOrEmpty(clipName)) return false;
                        var entry = ctx.Sfx?.Get(clipName);
                        if (entry == null)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] PlaySFX: " +
                                "clip '" + clipName + "' not declared in pack '" + ctx.PackId + "'.");
                            return false;
                        }
                        float volume = entry.DefaultVolume;
                        if (p["volume"] != null)
                            float.TryParse((string)p["volume"], NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out volume);
                        float delay = 0f;
                        if (p["delay"] != null)
                            float.TryParse((string)p["delay"], NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out delay);

                        var clip = ctx.Sfx.PickRandomClip(entry);
                        if (delay > 0f && ctx.Plugin != null)
                            ctx.Plugin.StartCoroutine(PlayOneShotAfter(delay, clip, volume, ctx));
                        else
                            TryPlayOneShot(clip, volume, ctx);
                        return false;
                    }

                case "SwitchMusic":
                    {
                        string music = (string)p["music"];
                        var audioPlayer = GameObject.Find("12_AudioPlayer");
                        if (audioPlayer != null)
                        {
                            foreach (Transform child in audioPlayer.transform)
                                child.gameObject.SetActive(false);
                            var t = audioPlayer.transform.Find(music);
                            if (t != null) t.gameObject.SetActive(true);
                        }
                        return false;
                    }

                case "EndDialogue":
                    ctx.RequestStop = true;
                    return false;

                case "ActivateScene":
                    {
                        // Legacy alias — pre-unify packs (and the original extracted
                        // dialogues) emit this activate-only action. Newer packs use
                        // SetGameObjectActive { kind: Scene }. Both land in ToggleScene.
                        string sceneKey = (string)p["scene"] ?? (string)p["target"] ?? "";
                        ToggleScene(sceneKey, true, ctx);
                        return false;
                    }

                case "DeactivateAllScenes":
                    {
                        if (ctx.Scenes == null) return false;
                        foreach (var entry in ctx.Scenes.All)
                            if (entry.SceneGo != null) entry.SceneGo.SetActive(false);
                        return false;
                    }

                case "Wait":
                    {
                        float.TryParse((string)p["seconds"] ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
                        ctx.Plugin?.StartCoroutine(WaitCoroutine(seconds));
                        return false;
                    }

                case "PickRandomFromList":
                    {
                        // Generalised port of a host random-target picker.
                        // `source` accepts three shapes:
                        //   1. literal CSV  -- "A,B,C"
                        //   2. $varName referencing a String var with CSV
                        //   3. $varName referencing a List var (JSON array)
                        // The third form is preferred for any list maintained
                        // by AddToList / RemoveFromList — no parser dance.
                        // Target: standalone action uses 'target'; the Variable
                        // action's "Random from list" operation uses 'name'.
                        string target = (string)p["target"] ?? (string)p["name"] ?? "";
                        if (string.IsNullOrEmpty(target)) return false;

                        var items = new System.Collections.Generic.List<string>();
                        // 'fromList' (Variable operation) names a List variable
                        // directly — no '$' prefix. The standalone 'source' param
                        // accepts a literal CSV, '$varName', or a bare list name.
                        string fromList = (string)p["fromList"];
                        string source = !string.IsNullOrEmpty(fromList) ? fromList : ((string)p["source"] ?? "");
                        string varRef = source.StartsWith("$") ? source.Substring(1)
                                      : !string.IsNullOrEmpty(fromList) ? fromList : null;

                        if (varRef != null && ctx.Vars != null)
                        {
                            // Prefer the List variable's JSON array; fall back to a
                            // CSV string variable of the same name.
                            var listItems = ctx.Vars.GetList(varRef);
                            if (listItems.Count > 0)
                                items.AddRange(listItems);
                            else
                                foreach (var part in (ctx.Vars.GetString(varRef) ?? "").Split(','))
                                {
                                    var trimmed = part.Trim();
                                    if (!string.IsNullOrEmpty(trimmed)) items.Add(trimmed);
                                }
                        }
                        else
                        {
                            foreach (var part in source.Split(','))
                            {
                                var trimmed = part.Trim();
                                if (!string.IsNullOrEmpty(trimmed)) items.Add(trimmed);
                            }
                        }
                        if (items.Count == 0)
                        {
                            ctx.Log?.LogInfo("[SMSModForge.PackPlugin] Random-from-list: source '" +
                                             source + "' is empty — " + target + " = \"\"");
                            ctx.Vars?.Set(target, "");
                            return false;
                        }
                        string picked = items[UnityEngine.Random.Range(0, items.Count)];
                        ctx.Log?.LogInfo("[SMSModForge.PackPlugin] Random-from-list: " + target + " = '" +
                                         picked + "' (picked from " + items.Count + ": " +
                                         string.Join(", ", items.ToArray()) + ")");
                        ctx.Vars?.Set(target, picked);
                        return false;
                    }

                case "AddToList":
                    {
                        string listName = (string)p["list"] ?? "";
                        string value = (string)p["value"] ?? "";
                        bool unique = bool.TryParse((string)p["unique"], out var u) && u;
                        if (unique)
                        {
                            var cur = ctx.Vars?.GetList(listName);
                            if (cur != null && cur.Contains(value)) return false; // already present
                        }
                        ctx.Vars?.ListAdd(listName, value);
                        return false;
                    }

                case "RemoveFromList":
                    {
                        string listName = (string)p["list"] ?? "";
                        string value = (string)p["value"] ?? "";
                        ctx.Vars?.ListRemove(listName, value);
                        return false;
                    }

                case "ClearList":
                    {
                        string listName = (string)p["list"] ?? "";
                        ctx.Vars?.ListClear(listName);
                        return false;
                    }

                case "DiceRoll":
                    {
                        // Weighted one-of-N: roll once, execute EXACTLY ONE
                        // branch. The editor enforces chances summing to 100;
                        // the runtime rolls against the actual total anyway so
                        // a hand-edited pack still picks proportionally.
                        var branches = a["branches"] as JArray;
                        if (branches == null || branches.Count == 0)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] DiceRoll has no branches — skipped.");
                            return false;
                        }
                        int total = 0;
                        foreach (var b in branches) total += (int?)((JObject)b)["chance"] ?? 0;
                        if (total <= 0)
                        {
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] DiceRoll chances sum to 0 — skipped.");
                            return false;
                        }
                        if (total != 100)
                            ctx.Log?.LogWarning("[SMSModForge.PackPlugin] DiceRoll chances sum to " +
                                                total + "% (expected 100) — rolling proportionally.");

                        int roll = UnityEngine.Random.Range(1, total + 1); // 1..total inclusive
                        int cumulative = 0;
                        for (int i = 0; i < branches.Count; i++)
                        {
                            var branch = (JObject)branches[i];
                            cumulative += (int?)branch["chance"] ?? 0;
                            if (roll > cumulative) continue;

                            var chosen = branch["action"] as JObject;
                            ctx.Log?.LogInfo("[SMSModForge.PackPlugin] DiceRoll: rolled " + roll + "/" + total +
                                             " → branch " + (i + 1) + " of " + branches.Count +
                                             " (" + ((int?)branch["chance"] ?? 0) + "%): " +
                                             (chosen != null ? (string)chosen["type"] : "<no action>"));
                            if (chosen == null) return false;
                            // Recurse through the normal dispatcher so every
                            // action type (nested dice rolls included) works.
                            return ExecuteOne(chosen, ctx);
                        }
                        return false; // unreachable when chances are sane
                    }

                case "CountList":
                    {
                        // The Variable action's "List count" operation: write the
                        // number of entries in a List variable into the target.
                        string listName = (string)p["fromList"] ?? (string)p["list"] ?? "";
                        string target = (string)p["name"] ?? (string)p["target"] ?? "";
                        if (string.IsNullOrEmpty(listName) || string.IsNullOrEmpty(target) || ctx.Vars == null)
                            return false;
                        int count = ctx.Vars.GetList(listName).Count;
                        ctx.Log?.LogInfo("[SMSModForge.PackPlugin] List-count: " + target + " = " +
                                         count + " (entries in '" + listName + "')");
                        return ctx.Vars.Set(target, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                default:
                    ctx.Log?.LogWarning("[SMSModForge.PackPlugin] Unknown action type '" + type + "'");
                    return false;
            }
        }

        private static IEnumerator WaitCoroutine(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        /// <summary>
        /// Public entry for code-driven signal emission (the dialogue
        /// dispatcher's automatic <c>FadeUI</c> on start/end uses this).
        /// Same reflection path the <c>EmitSignal</c> action takes.
        /// </summary>
        public static void EmitSignal(string signalName, PackContext ctx)
            => EmitGc2Signal(signalName, ctx);

        /// <summary>
        /// Fire-and-forget delayed signal emit. Driven by the
        /// <see cref="NodeActionTypes.EmitSignalDelayed"/> action — the
        /// action itself returns immediately, this coroutine waits the
        /// requested seconds and then routes through the same
        /// <see cref="EmitGc2Signal"/> path as a regular EmitSignal.
        /// </summary>
        private static IEnumerator EmitSignalAfter(float seconds, string signalName, PackContext ctx)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
            EmitGc2Signal(signalName, ctx);
        }

        /// <summary>
        /// Play a one-shot SFX clip through the game's managed UI audio
        /// channel (see <see cref="GameAudio"/>). Tolerant of an unloaded
        /// clip (the async loader hasn't finished) — silently no-ops in that
        /// case rather than throwing, so a node firing during the load window
        /// doesn't crash the dialogue.
        /// </summary>
        private static void TryPlayOneShot(AudioClip clip, float volume, PackContext ctx)
        {
            if (clip == null) return;
            GameAudio.PlayUi(clip, volume);
        }

        /// <summary>
        /// Fire-and-forget delayed SFX play. Used by
        /// <see cref="NodeActionTypes.PlaySFX"/> when the action's
        /// <c>delay</c> param is non-zero — the action returns
        /// immediately so the dialogue keeps flowing, the clip
        /// triggers after the wait.
        /// </summary>
        private static IEnumerator PlayOneShotAfter(float seconds, AudioClip clip,
                                                     float volume, PackContext ctx)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
            TryPlayOneShot(clip, volume, ctx);
        }

        /// <summary>
        /// Mirrors the host mod's <c>EmitSignalGameObjectDelayedCoroutine</c>:
        /// enables GC2 trigger components on the source level, waits
        /// 2/3 of the delay, swaps the from/to levels, waits the
        /// remaining third, then emits the signal. Tolerates either
        /// from or to being null so partial transitions still work
        /// (e.g. only fading out the current level when there's no
        /// destination to bring in).
        /// </summary>
        private static IEnumerator TransitionLevelsCoroutine(float seconds,
            GameObject fromGo, GameObject toGo, string signalName, PackContext ctx)
        {
            // Step 1: re-enable the source's Trigger + Conditions in case
            // a previous transition disabled them. Components are looked
            // up by type name so we don't hard-link to GC2's
            // VisualScripting assembly.
            SetBehaviourEnabledByName(fromGo, "Trigger", true);
            SetBehaviourEnabledByName(fromGo, "Conditions", true);

            // Step 2: wait the bulk of the duration.
            float twoThirds = Mathf.Max(0f, seconds) * 2f / 3f;
            if (twoThirds > 0f) yield return new WaitForSeconds(twoThirds);

            // Step 3: take the source down.
            if (fromGo != null) fromGo.SetActive(false);

            // Step 4: silence the destination's triggers so its
            // OnEnable doesn't fire a spurious vanilla-dialogue start
            // (matches what the host mod's coroutine does to suppress
            // the new level's roomtalk trigger during the swap).
            SetBehaviourEnabledByName(toGo, "Trigger", false);
            SetBehaviourEnabledByName(toGo, "Conditions", false);

            // Step 5: bring the destination up.
            if (toGo != null) toGo.SetActive(true);

            // Step 6: the remaining third — typically the fade-out → emit
            // window where the player is looking at a black screen.
            float remaining = Mathf.Max(0f, seconds) - twoThirds;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);

            // Step 7: fire the signal that drives the fade-back-in.
            if (!string.IsNullOrEmpty(signalName)) EmitGc2Signal(signalName, ctx);
        }

        /// <summary>
        /// Set <c>Behaviour.enabled</c> on every component of a given
        /// type-name on <paramref name="go"/>. Looked up by type name
        /// so we don't have to hard-link the GC2 VisualScripting
        /// assembly the <c>Trigger</c> / <c>Conditions</c> types live
        /// in.
        /// </summary>
        private static void SetBehaviourEnabledByName(GameObject go, string typeName, bool enabled)
        {
            if (go == null) return;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name != typeName) continue;
                if (c is Behaviour b) b.enabled = enabled;
            }
        }

        /// <summary>A variable action whose 'source' param targets the vanilla GC2
        /// GlobalNameVariable store rather than the per-pack store.</summary>
        private static bool IsVanilla(JObject p)
            => string.Equals((string)p["source"], "vanilla", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Write a vanilla GC2 global by name, inferring the stored type from the
        /// authored string: <c>true</c>/<c>false</c> → bool, a parseable number →
        /// double, anything else → string. Mirrors how the editor's value box is
        /// interpreted for pack variables.
        /// </summary>
        private static void SetGameVar(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (bool.TryParse(value, out var b)) GameVariableBridge.SetBool(name, b);
            else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                GameVariableBridge.SetDouble(name, d);
            else GameVariableBridge.Set(name, value);
        }

        /// <summary>
        /// Tween a <see cref="SpriteRenderer"/>'s alpha to a target
        /// over a duration. Generic alpha-tween primitive ported
        /// from a host fade helper. Safe across the
        /// sprite being destroyed mid-tween — bails out cleanly if
        /// the renderer goes null.
        /// </summary>
        private static IEnumerator FadeSpriteCoroutine(SpriteRenderer sr, float to, float seconds)
        {
            if (sr == null) yield break;
            float startAlpha = sr.color.a;
            float t = 0f;
            float dur = Mathf.Max(0.0001f, seconds);
            while (t < dur)
            {
                if (sr == null) yield break;
                t += Time.deltaTime;
                float alpha = Mathf.Lerp(startAlpha, to, Mathf.Clamp01(t / dur));
                var c = sr.color; c.a = alpha; sr.color = c;
                yield return null;
            }
            if (sr != null)
            {
                var c = sr.color; c.a = to; sr.color = c;
            }
        }

        /// <summary>
        /// Resolve a Move/Spin target string: a level token
        /// (<c>self:</c> / <c>place:</c> / <c>vanilla:</c>) goes through the same
        /// resolver TransitionLevels uses; anything else is treated as a plain
        /// GameObject / overlay name (active or inactive).
        /// </summary>
        private static GameObject ResolveActionTarget(string s, PackContext ctx)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Contains(":"))
            {
                var level5 = GameObject.Find("5_Levels")?.transform;
                var lvl = Plugin.ResolveLevelTarget(s, ctx.PackId, level5);
                if (lvl != null) return lvl;
            }
            return TransformExtensions.ResolveGameObject(s);
        }

        /// <summary>
        /// Resolve a GameObject-targeting action's target by its <c>kind</c>
        /// category (Direct Path / Level Overlay / Places / Bust). Shared by
        /// FadeSprite / MoveGameObject / SpinGameObject. Reads the canonical
        /// <c>target</c> param (falling back to legacy <c>path</c>); a
        /// <c>Level Overlay</c> with an <c>overlayLevel</c> resolves strictly
        /// inside that level (including inactive children). The default —
        /// Direct Path, or no <c>kind</c> at all — routes through
        /// <see cref="ResolveActionTarget"/>, so actions authored before
        /// categories (a bare level token or GO name) keep resolving.
        /// </summary>
        /// <summary>True for the Extra-GameObject (level-scoped) target category.
        /// Accepts the legacy "Level Overlay" token so packs authored before the
        /// rename still resolve.</summary>
        private static bool IsOverlayKind(string kind)
            => kind == "Extra GameObjects" || kind == "Level Overlay";

        private static GameObject ResolveByKind(JObject p, PackContext ctx)
        {
            string target = (string)p["target"] ?? (string)p["path"] ?? "";
            if (string.IsNullOrEmpty(target)) return null;

            string kind = (string)p["kind"] ?? "";
            if (IsOverlayKind(kind))
            {
                string overlayLevel = (string)p["overlayLevel"] ?? "";
                if (!string.IsNullOrEmpty(overlayLevel))
                {
                    var level5 = GameObject.Find("5_Levels")?.transform;
                    var levelGo = Plugin.ResolveLevelTarget(overlayLevel, ctx.PackId, level5);
                    if (levelGo != null)
                        return TransformExtensions.FindDescendantIncludingInactive(levelGo.transform, target);
                }
                return TransformExtensions.ResolveGameObject(target);
            }
            switch (kind)
            {
                case "Places":
                {
                    var level5 = GameObject.Find("5_Levels")?.transform;
                    return Plugin.ResolveLevelTarget(target, ctx.PackId, level5)
                           ?? TransformExtensions.ResolveGameObject(target);
                }
                case "Bust":
                {
                    var bustManager = GameObject.Find("2_Bust_Manager")?.transform;
                    return bustManager?.FindChildIgnoreCase(target)?.gameObject
                           ?? TransformExtensions.ResolveGameObject(target);
                }
                default:
                    return ResolveActionTarget(target, ctx);
            }
        }

        /// <summary>
        /// Emit a GC2 signal by name via reflection. Avoids a compile-time
        /// reference to <c>GameCreator.Runtime.Signals</c> et al — the
        /// plugin already pulls in GC2 Dialogue but the signal API is in
        /// a separate namespace and the call is rare.
        /// </summary>
        /// <summary>
        /// Activate or deactivate a pack scene by key, looked up in the per-pack
        /// scene registry. Activation also emits the scene's authored activation
        /// signal (its sound override) atomically; deactivation is silent.
        /// Shared by the unified <c>SetGameObjectActive { kind: Scene }</c> action
        /// and the legacy <c>ActivateScene</c> alias.
        /// </summary>
        private static void ToggleScene(string sceneKey, bool active, PackContext ctx)
        {
            if (ctx.Scenes != null && ctx.Scenes.TryGet(sceneKey, out var entry))
            {
                if (entry.SceneGo != null) entry.SceneGo.SetActive(active);
                // Only emit the activation sound when turning the scene ON.
                if (active && !string.IsNullOrEmpty(entry.ActivationSignal))
                    EmitGc2Signal(entry.ActivationSignal, ctx);
            }
            else ctx.Log?.LogWarning("[SMSModForge.PackPlugin] Scene '" + sceneKey +
                                     "' not found in pack " + ctx.PackId);
        }

        private static void EmitGc2Signal(string signalName, PackContext ctx)
        {
            if (string.IsNullOrEmpty(signalName)) return;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var signalsType = asm.GetType("GameCreator.Runtime.Common.Signals");
                if (signalsType == null) continue;
                var argsType = asm.GetType("GameCreator.Runtime.Common.SignalArgs");
                if (argsType == null) continue;

                // SignalArgs has multiple ctors; we use the (PropertyName, GameObject) one.
                var pnType = typeof(UnityEngine.PropertyName);
                var argsCtor = argsType.GetConstructor(new[] { pnType, typeof(GameObject) });
                if (argsCtor == null) continue;
                var emit = signalsType.GetMethod("Emit", new[] { argsType });
                if (emit == null) continue;
                try
                {
                    var propertyName = new UnityEngine.PropertyName(signalName);
                    var args = argsCtor.Invoke(new object[] { propertyName, null });
                    emit.Invoke(null, new object[] { args });
                }
                catch (System.Exception ex)
                {
                    ctx.Log?.LogWarning("[SMSModForge.PackPlugin] EmitSignal '" + signalName + "' failed: " + ex.Message);
                }
                return;
            }
        }
    }

    /// <summary>
    /// Bundle of per-pack runtime state shared between conditions,
    /// actions, the dialogue builder, and the dispatcher. Created once
    /// per pack at load time and reused for the lifetime of the
    /// CoreGameScene.
    /// </summary>
    public sealed class PackContext
    {
        public string PackId;
        public PackVariableStore Vars;
        public ActorRegistry Actors;
        public RuntimeActorFactory ActorFactory;
        public SceneRegistry Scenes;
        public WallpaperRegistry Wallpapers;
        public SfxRegistry Sfx;
        public UpdateRulesRegistry UpdateRules;
        public ManualLogSource Log;
        public MonoBehaviour Plugin;

        /// <summary>
        /// Set by actions like <c>EndDialogue</c> to request the
        /// dispatcher stop the currently-playing dialogue. The
        /// dispatcher inspects this flag after each node finishes.
        /// </summary>
        public bool RequestStop;
    }
}
