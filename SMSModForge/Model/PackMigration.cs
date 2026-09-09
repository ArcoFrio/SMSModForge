using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Carries a pack written by an older build into the shape this one uses.
/// <para/>
/// Every migration in the tool runs from here, and that is the point rather
/// than tidiness: a pack has to pick up ALL of them in one pass, and the author
/// has to be told about all of them in one message. A migration bolted on
/// somewhere else is one that some packs miss and nobody hears about.
/// <para/>
/// The contract, which every entry here keeps (see CLAUDE.md):
/// <list type="number">
///   <item>A pack written by any previous build still loads.</item>
///   <item>It is converted in memory, on load, so the author sees the new
///   shape at once.</item>
///   <item>Nothing is written until they save. Opening a pack to look at it and
///   closing it changes no file.</item>
///   <item>They are told what changed, and how much of it there was.</item>
///   <item>The untouched original is kept beside the manifest on the first save
///   after a migration — once, and never when nothing was migrated.</item>
/// </list>
/// <para/>
/// The fifth is the one that matters most and is easiest to skip. An author who
/// opens a pack in a new build, saves, and finds the result wrong has no way
/// back unless the old file is still there.
/// </summary>
public static class PackMigration
{
    /// <summary>What one migration did, in the author's terms.</summary>
    public sealed record Change(string What, int Count)
    {
        public override string ToString()
            => Count > 1 ? $"{What} ({Count})" : What;
    }

    /// <summary>Everything a pack picked up on the way in.</summary>
    public sealed class Report
    {
        private readonly List<Change> _changes = new();

        public IReadOnlyList<Change> Changes => _changes;

        /// <summary>Whether the pack on disk is now behind what is in memory.
        /// This is what arms the backup, and what the editor tells the author
        /// about.</summary>
        public bool Migrated => _changes.Count > 0;

        /// <summary>Where the pack was read from, so the backup lands beside
        /// it rather than beside whatever is being saved.</summary>
        public string? SourceManifest { get; set; }

        /// <summary>Whether the original has already been kept. Set once, when
        /// the backup is written, so a second save does not overwrite the
        /// only copy of the pre-migration file with a post-migration one.</summary>
        public bool BackedUp { get; set; }

        public void Note(string what, int count = 1)
        {
            if (count <= 0) return;

            // Folded rather than appended, so a migration that touches many
            // records reads as one line with a number instead of a wall.
            for (int i = 0; i < _changes.Count; i++)
                if (_changes[i].What == what)
                {
                    _changes[i] = _changes[i] with { Count = _changes[i].Count + count };
                    return;
                }
            _changes.Add(new Change(what, count));
        }

        /// <summary>One paragraph for the author, or empty when nothing
        /// happened.</summary>
        public string Describe()
        {
            if (!Migrated) return "";

            var lines = _changes.Select(c => "  • " + c);
            return "This pack was written by an earlier version of ModForge and has been "
                 + "brought up to date:\n\n"
                 + string.Join("\n", lines)
                 + "\n\nNothing has been written yet — your file is untouched until you save. "
                 + "When you do, a copy of the original is kept beside it.";
        }
    }

    /// <summary>The name a kept original is given. Dated, so a pack migrated
    /// twice by two builds keeps both.</summary>
    public static string BackupName(DateTime when)
        => $"modpack.pre-migration-{when:yyyyMMdd-HHmmss}.json";

    /// <summary>
    /// Whether a file is a migration backup, and so must never be exported.
    /// <para/>
    /// Matched on the prefix rather than an exact name because the name
    /// carries a timestamp. Anything here ships to players if it is missed.
    /// </summary>
    public static bool IsBackup(string? fileName)
        => !string.IsNullOrEmpty(fileName)
           && Path.GetFileName(fileName)!.StartsWith("modpack.pre-migration-",
                                                     StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bring a freshly loaded pack up to date, reporting what changed.
    /// <para/>
    /// In memory only. Every entry must be idempotent — running it on an
    /// already-current pack has to report nothing — because this runs on every
    /// load, including the load right after a save.
    /// </summary>
    public static Report Apply(ModPack? pack)
    {
        var report = new Report();
        if (pack == null) return report;

        // The game's own cast, adopted from whatever an older pack called them.
        // Counted rather than assumed: a pack that never declared a vanilla
        // character reports nothing, which is what keeps this quiet for packs
        // that have nothing to migrate.
        int adopted = CharacterMerge.Apply(pack);
        if (adopted > 0)
            report.Note("Actors folded into characters", adopted);

        int recognised = CharacterMerge.LastAdoptedVanilla;
        if (recognised > 0)
            report.Note("Vanilla characters recognised and linked to the game's cast", recognised);

        int folded = FoldNegatedBooleans(pack);
        if (folded > 0)
            report.Note("\"Negate\" folded into True/False on boolean checks", folded);

        int merged = MergeSupersededActions(pack);
        if (merged > 0)
            report.Note("Actions merged into the ones that replaced them", merged);

        int variables = MergeVariableConditions(pack);
        if (variables > 0)
            report.Note("Variable checks merged into one with a Comparison field", variables);

        if (GiveItAVersion(pack))
            report.Note($"Given a version to start from ({pack.Version})");

        if (StampTheToolThatWroteIt(pack))
            report.Note("Stamped with the ModForge version that writes it, so the "
                        + "game can tell you when a pack needs a newer one");

        return report;
    }

    /// <summary>
    /// Number a pack that predates versioning.
    /// <para/>
    /// 0.1.0 rather than 0.0.0, and deliberately: a pack being migrated has
    /// content in it - often a great deal of it - and calling that "nothing
    /// yet" would be wrong the moment its author looked at the field. A pack
    /// created fresh starts at 0.0.0, which it has earned.
    /// </summary>
    private static bool GiveItAVersion(ModPack pack)
    {
        if (Model.PackVersion.Parse(pack.Version) != null) return false;

        pack.PackVersion = Model.PackVersion.Existing;
        return true;
    }

    /// <summary>
    /// Record which ModForge a pack belongs to.
    /// <para/>
    /// The runtime uses it to warn about a pack built by a newer ModForge than
    /// itself, which may use things it has never heard of. Without the stamp it
    /// cannot tell, and says nothing.
    /// <para/>
    /// The value is the CURRENT ModForge rather than a guess at the one that
    /// originally wrote the pack, because there is no way to know that and a
    /// guess would be a confident wrong answer. It is honest by the time it
    /// reaches disk: this build is the one about to save the file.
    /// <para/>
    /// Only the ABSENT case is a migration. Keeping the stamp up to date on
    /// later saves is <see cref="PackRepository.Save"/>'s job - reporting a
    /// migration every time somebody updates ModForge would tell an author
    /// their pack had changed when the only thing that changed was the tool,
    /// and would take a full backup of the manifest to say it.
    /// </summary>
    private static bool StampTheToolThatWroteIt(ModPack pack)
    {
        if (!string.IsNullOrEmpty(pack.ForgeVersion)) return false;

        pack.ForgeVersion = Shared.ForgeVersion.Current;
        return true;
    }

    /// <summary>
    /// Fold the ten superseded variable conditions into <c>VariableCompare</c>.
    /// <para/>
    /// The operator and the store were both encoded in the TYPE, which is why
    /// there were ten of them; both are fields now. See
    /// <see cref="VariableMerge"/> for the mapping, which the view model and the
    /// vanilla translator share so the three cannot drift.
    /// </summary>
    private static int MergeVariableConditions(ModPack pack)
    {
        int changed = 0;
        foreach (var condition in EveryCondition(pack))
            if (VariableMerge.Rewrite(condition)) changed++;
        return changed;
    }

    /// <summary>
    /// Rewrite actions that have been folded into another.
    /// <para/>
    /// Two of them, and both keep exactly what they did:
    /// <list type="bullet">
    ///   <item><c>EmitSignalDelayed</c> is <c>EmitSignal</c> with a delay. One
    ///   action with a delay that defaults to none says the same thing, and the
    ///   pair of them made an author choose before knowing they had a
    ///   choice.</item>
    ///   <item><c>ActivateScene</c> is <c>SetGameObjectActive</c> aimed at a
    ///   scene. That merge was made in the runtime and never finished in the
    ///   editor, so the tool has been offering an action it already treated as
    ///   an alias.</item>
    /// </list>
    /// <para/>
    /// The runtime still answers to both old names, so a pack works whether or
    /// not its author has re-saved. This is about what the EDITOR shows.
    /// </summary>
    private static int MergeSupersededActions(ModPack pack)
    {
        int changed = 0;
        foreach (var action in EveryAction(pack))
        {
            if (action.Type == NodeActionTypes.EmitSignalDelayed)
            {
                action.Type = NodeActionTypes.EmitSignal;

                // Its delay was required and defaulted to 1; the merged action
                // defaults to none. Anything already written stays as it is,
                // and anything missing means the old default rather than 0.
                if (action.Params != null
                    && (!action.Params.TryGetValue("seconds", out string? had)
                        || string.IsNullOrWhiteSpace(had)))
                    action.Params["seconds"] = "1";

                changed++;
                continue;
            }

            if (action.Type == NodeActionTypes.ActivateScene)
            {
                action.Type = NodeActionTypes.SetGameObjectActive;
                if (action.Params != null)
                {
                    // It only ever switched a scene ON, and it named the scene
                    // in its own param.
                    action.Params.TryGetValue("scene", out string? scene);
                    if (string.IsNullOrEmpty(scene))
                        action.Params.TryGetValue("target", out scene);

                    action.Params["kind"] = "Scene";
                    action.Params["target"] = scene ?? "";
                    action.Params["active"] = "true";
                    action.Params.Remove("scene");
                }
                changed++;
            }
        }
        return changed;
    }

    /// <summary>Every action a pack holds, wherever it lives.</summary>
    private static IEnumerable<NodeActionDef> EveryAction(ModPack pack)
    {
        IEnumerable<NodeActionDef> Walk(IEnumerable<NodeActionDef>? list)
        {
            foreach (var one in list ?? Enumerable.Empty<NodeActionDef>())
            {
                if (one == null) continue;
                yield return one;

                // An action can carry branches, each with actions of its own.
                foreach (var branch in one.Branches ?? new List<DiceBranchDef>())
                    foreach (var inner in Walk(branch.Action == null
                                               ? Enumerable.Empty<NodeActionDef>()
                                               : new[] { branch.Action }))
                        yield return inner;
            }
        }

        foreach (var d in pack.Dialogues ?? new List<DialogueDef>())
            foreach (var node in d.Nodes)
            {
                foreach (var a in Walk(node.ActionsOnStart)) yield return a;
                foreach (var a in Walk(node.ActionsOnFinish)) yield return a;
            }

        foreach (var r in pack.IntegrationRules ?? new List<UpdateRuleDef>())
        {
            foreach (var a in Walk(r.Actions)) yield return a;
            foreach (var b in r.Branches ?? new List<LevelHookDef>())
                foreach (var a in Walk(b.Actions)) yield return a;
        }

        foreach (var place in pack.Places ?? new List<PlaceDef>())
            foreach (var hook in place.OnEnter.Concat(place.OnExit))
                foreach (var a in Walk(hook.Actions)) yield return a;
    }

    /// <summary>
    /// Turn "not (x is true)" into "x is false".
    /// <para/>
    /// A boolean check now offers True and False rather than a single tick, so
    /// Negate beside it is a second way to say the same thing — and the two
    /// together read as a double negative nobody should have to unpick. The
    /// checkbox is gone from boolean checks, so a pack still carrying the flag
    /// would have it applied invisibly and could not clear it.
    /// <para/>
    /// The rewrite is exact rather than approximate: negating an equality
    /// against a boolean is equality against the other boolean. Nothing else is
    /// touched — Negate on "= 5" still means "is not 5", still shows, and still
    /// works.
    /// </summary>
    private static int FoldNegatedBooleans(ModPack pack)
    {
        var booleans = new HashSet<string>(
            (pack.Variables ?? new List<PackVariableDef>())
                .Where(v => v.Type == PackVariableType.Bool)
                .Select(v => v.Name),
            StringComparer.OrdinalIgnoreCase);

        int folded = 0;
        foreach (var condition in EveryCondition(pack))
        {
            if (!condition.Negate) continue;
            if (condition.Type != NodeConditionTypes.VariableEquals) continue;
            if (condition.Params == null
                || !condition.Params.TryGetValue("value", out string? value)) continue;

            string trimmed = (value ?? "").Trim();
            bool literal = trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase);

            // A bool variable whose value is not spelled as one still reads as
            // false everywhere else in the tool, so it folds the same way.
            condition.Params.TryGetValue("name", out string? name);
            if (!literal && !booleans.Contains((name ?? "").Trim())) continue;

            bool wasTrue = trimmed.Equals("true", StringComparison.OrdinalIgnoreCase);
            condition.Params["value"] = wasTrue ? "false" : "true";
            condition.Negate = false;
            folded++;
        }
        return folded;
    }

    /// <summary>
    /// Every condition a pack holds, wherever it lives.
    /// <para/>
    /// Groups are walked into, because a condition nested two groups deep is
    /// exactly as invisible to an author as one at the top — and a migration
    /// that missed it would leave the flag applied where nobody could see it.
    /// </summary>
    private static IEnumerable<NodeConditionDef> EveryCondition(ModPack pack)
    {
        IEnumerable<NodeConditionDef> Walk(IEnumerable<NodeConditionDef>? list)
        {
            foreach (var one in list ?? Enumerable.Empty<NodeConditionDef>())
            {
                if (one == null) continue;
                yield return one;
                foreach (var inner in Walk(one.Conditions)) yield return inner;
            }
        }

        foreach (var d in pack.Dialogues ?? new List<DialogueDef>())
        {
            foreach (var c in Walk(d.StartConditions)) yield return c;
            foreach (var node in d.Nodes)
                foreach (var c in Walk(node.Conditions)) yield return c;
        }

        foreach (var r in pack.IntegrationRules ?? new List<UpdateRuleDef>())
        {
            foreach (var c in Walk(r.Conditions)) yield return c;
            foreach (var b in r.Branches ?? new List<LevelHookDef>())
                foreach (var c in Walk(b.Conditions)) yield return c;
        }

        foreach (var place in pack.Places ?? new List<PlaceDef>())
        {
            foreach (var hook in place.OnEnter.Concat(place.OnExit))
                foreach (var c in Walk(hook.Conditions)) yield return c;
            foreach (var b in place.NavigatorButtons)
                foreach (var c in Walk(b.Conditions)) yield return c;
        }

        foreach (var v in pack.VanillaExtensions ?? new List<VanillaPlaceExtensionDef>())
            foreach (var b in v.NavigatorButtons)
                foreach (var c in Walk(b.Conditions)) yield return c;

        foreach (var b in pack.MapButtons ?? new List<MapButtonDef>())
            foreach (var c in Walk(b.Conditions)) yield return c;

        foreach (var w in pack.Wallpapers ?? new List<WallpaperDef>())
            foreach (var c in Walk(w.UnlockConditions)) yield return c;
    }

    /// <summary>
    /// Keep the pre-migration manifest, once, before a save overwrites it.
    /// <para/>
    /// Does nothing when the pack was not migrated, when the original is gone,
    /// or when a copy has already been kept — so an author who saves ten times
    /// gets one backup of the file as it was, not ten of increasingly migrated
    /// ones.
    /// <para/>
    /// Returns the path written, or null.
    /// </summary>
    public static string? KeepOriginal(Report? report, string packRoot)
    {
        if (report == null || !report.Migrated || report.BackedUp) return null;
        if (string.IsNullOrWhiteSpace(packRoot)) return null;

        string? source = report.SourceManifest;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return null;

        try
        {
            string backup = Path.Combine(packRoot, BackupName(DateTime.Now));
            File.Copy(source, backup, overwrite: false);
            report.BackedUp = true;
            return backup;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
