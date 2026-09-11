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

        int reset = ResetBorrowedCharactersWrittenBefore(pack);
        if (reset > 0)
            report.Note("The game's characters put back the way the game has them — nothing a "
                        + "pack said about them before this version reached the game anyway", reset);

        int renamed = NormaliseBorrowedIdentifiers(pack);
        if (renamed > 0)
            report.Note("Machine-written names on the game's characters put back the way "
                        + "the editor derives them now", renamed);

        int reclaimed = TakeBackWhatIsNotThePacksToSay(pack);
        if (reclaimed > 0)
            report.Note("Fields on the game's characters that a pack no longer sets - name, "
                        + "bust source, default outfit - put back to the game's", reclaimed);

        int redundant = DropWhatMatchesTheDefault(pack);
        if (redundant > 0)
            report.Note("Settings dropped from the game's characters that only repeated "
                        + "what those characters already had", redundant);

        int restated = DropRestatedFaces(pack);
        if (restated > 0)
            report.Note("Expressions dropped from the game's characters that only "
                        + "restated faces the game already gives them", restated);

        if (GiveItAVersion(pack))
            report.Note($"Given a version to start from ({pack.Version})");

        if (StampTheToolThatWroteIt(pack))
            report.Note("Stamped with the ModForge version that writes it, so the "
                        + "game can tell you when a pack needs a newer one");

        return report;
    }

    /// <summary>
    /// The version that first made a pack's word about one of the game's
    /// characters mean anything.
    /// </summary>
    private const string FirstVersionThatMeantIt = "1.3.0";

    /// <summary>
    /// Put every borrowed character back the way the game has them, for packs
    /// written before any of it was wired up.
    /// <para/>
    /// Before 1.3.0 a pack could hold a name, a wardrobe, a colour and a set of
    /// expressions for one of the game's characters, and almost none of it went
    /// anywhere: the runtime skipped a borrowed character's whole wardrobe, and
    /// the editor has since taken the name, the bust source and the default
    /// outfit away as fields a pack may set. What is left on those packs is a
    /// sediment of defaults an older editor wrote unbidden, and it is what
    /// makes half the cast read as changed.
    /// <para/>
    /// So the old ones start again. It is a bigger hammer than the passes below
    /// and it makes them no-ops for anything it touched, which is the point:
    /// one rule that leaves no residue, rather than five that each catch a
    /// shape somebody thought of.
    /// <para/>
    /// TWO THINGS SURVIVE.
    /// <list type="bullet">
    ///   <item>A bust the pack DREW for one of the game's characters — an
    ///   outfit carrying its own art. That is work, it is unambiguous, and
    ///   1.3.0 is the version that finally builds it, so it is kept and marked
    ///   as the pack's.</item>
    ///   <item>The dialogue key. Every line the pack wrote names the character
    ///   by it. Where it differs from the one the editor derives it is put back
    ///   — but as a RENAME, so the lines come too.</item>
    /// </list>
    /// <para/>
    /// Not silent, and not written until the author saves: the report names it
    /// and the untouched original is kept beside the manifest on the first save
    /// after. A colour or a typewriter set on one of these DID reach a pack's
    /// own dialogue before now, so this can change how a character sounds in
    /// it; that is what the report and the backup are for.
    /// </summary>
    private static int ResetBorrowedCharactersWrittenBefore(ModPack pack)
    {
        if (pack.Characters == null) return 0;
        if (!WrittenBefore(pack.ForgeVersion, FirstVersionThatMeantIt)) return 0;

        int reset = 0;
        foreach (var character in pack.Characters)
        {
            if (!character.IsVanillaCharacter) continue;

            var one = VanillaCharacters.Find(character.VanillaCharacter);
            if (one == null) continue;

            // Nothing to do for one that already reads as the game's.
            if (VanillaCastSeed.IsUntouched(character)) continue;

            // The busts the pack drew, which are the one thing here that is
            // unambiguously somebody's work. Marked as the pack's so 1.3.0
            // actually builds them - the flag did not exist when they were
            // written.
            var drawn = character.Outfits
                .Where(o => !string.IsNullOrWhiteSpace(o.BaseSprite))
                .ToList();
            foreach (var o in drawn) o.PackArt = true;

            var fresh = VanillaCastSeed.Make(one);

            character.Name = fresh.Name;
            character.DisplayName = fresh.DisplayName;
            character.BustSource = fresh.BustSource;
            character.DefaultOutfit = fresh.DefaultOutfit;
            character.NameColor = null;
            character.Typewriter = null;
            character.GiftLikes = fresh.GiftLikes;
            character.Expressions = fresh.Expressions;
            character.Outfits = fresh.Outfits.Concat(drawn).ToList();

            // The key last, and as a rename: every line the pack wrote names
            // this character by it.
            if (!string.Equals(character.Key, one.Key, StringComparison.OrdinalIgnoreCase)
                && !pack.Characters.Any(c => c != character
                                          && string.Equals(c.Key, one.Key, StringComparison.OrdinalIgnoreCase)))
            {
                Services.ReferenceRenamer.Rename(pack, Services.RefKind.Character, character.Key, one.Key);
                character.Key = one.Key;
            }

            reset++;
        }
        return reset;
    }

    /// <summary>
    /// Whether a pack was written before a given version.
    /// <para/>
    /// An absent or unreadable stamp counts as before, and has to: the stamp
    /// itself is newer than the oldest packs, so "no stamp" means older than
    /// anything that has one. <see cref="Shared.ForgeVersion.Compare"/> answers
    /// zero for a stamp it cannot read — deliberately, so an unreadable one
    /// never yields a verdict - which would otherwise read here as "not older"
    /// and skip exactly the packs this is for.
    /// </summary>
    private static bool WrittenBefore(string stamp, string version)
    {
        if (Shared.ForgeVersion.Parse(stamp) == null) return true;
        return Shared.ForgeVersion.Compare(stamp, version) < 0;
    }

    /// <summary>
    /// Put a borrowed character's DERIVED identifiers back to what the editor
    /// writes today.
    /// <para/>
    /// Two of them, and nobody typed either: the character's GameObject name
    /// (<c>Adrian</c>, where seeding writes <c>adrian</c>) and each outfit's key
    /// (<c>Adrian_bust</c>, where seeding writes <c>adrianbust</c>). An older
    /// editor derived them differently, and the difference has been sitting in
    /// every pack that adopted one of the game's characters ever since.
    /// <para/>
    /// It became worth fixing when the editor started marking changed
    /// characters: Adrian carried the tag on the strength of two strings an
    /// author never typed, cannot see — one is behind an expander that is
    /// greyed out for him, the other is not shown at all — and has no way to
    /// reset. A tag nobody can act on is worse than no tag.
    /// <para/>
    /// Safe because both are inert for a borrowed character. The runtime builds
    /// no bust for one, so it reads the character's name only to check it is
    /// not empty, and an outfit's key only as a fallback for a missing
    /// <c>gameObjectName</c> — which is why an outfit without one is left
    /// alone here. Neither is a dialogue reference; that is <c>key</c> on the
    /// character, which this does not touch.
    /// <para/>
    /// Only outfits the GAME supplies. A bust the pack drew for one of its
    /// characters keeps its key, which is the author's and is load-bearing.
    /// </summary>
    private static int NormaliseBorrowedIdentifiers(ModPack pack)
    {
        if (pack.Characters == null) return 0;

        int renamed = 0;
        foreach (var character in pack.Characters)
        {
            if (!character.IsVanillaCharacter) continue;

            var one = VanillaCharacters.Find(character.VanillaCharacter);
            if (one == null) continue;

            if (!string.Equals(character.Name, one.Key, StringComparison.Ordinal))
            {
                character.Name = one.Key;
                renamed++;
            }

            var theirs = new HashSet<string>(one.Outfits, StringComparer.Ordinal);
            foreach (var outfit in character.Outfits)
            {
                if (outfit.PackArt) continue;
                if (string.IsNullOrEmpty(outfit.GameObjectName)) continue;
                if (!theirs.Contains(outfit.GameObjectName)) continue;

                string derived = VanillaCharacters.KeyFor(outfit.GameObjectName);
                if (string.Equals(outfit.Key, derived, StringComparison.Ordinal)) continue;

                outfit.Key = derived;
                renamed++;
            }

            if (ReorderToTheGames(character, one)) renamed++;
        }
        return renamed;
    }

    /// <summary>
    /// Put back the fields on one of the game's characters that a pack is no
    /// longer allowed to set.
    /// <para/>
    /// The name, the bust source and the default outfit are all greyed out in
    /// the editor now: who Anna is, where her busts come from and which one she
    /// walks in wearing belong to the game, in every scene it wrote. A pack
    /// written before that could still be carrying its own answers, and because
    /// the fields are greyed there is no way to correct them by hand.
    /// <para/>
    /// This is a real change to what a player sees, unlike the derived names
    /// above — which is exactly why it is reported and why the original is kept
    /// beside the manifest on the next save.
    /// </summary>
    private static int TakeBackWhatIsNotThePacksToSay(ModPack pack)
    {
        if (pack.Characters == null) return 0;

        int taken = 0;
        foreach (var character in pack.Characters)
        {
            if (!character.IsVanillaCharacter) continue;

            var one = VanillaCharacters.Find(character.VanillaCharacter);
            if (one == null) continue;

            if (!string.Equals(character.DisplayName, one.Name, StringComparison.Ordinal))
            {
                character.DisplayName = one.Name;
                taken++;
            }

            if (character.BustSource != BustSource.Vanilla)
            {
                character.BustSource = BustSource.Vanilla;
                taken++;
            }

            if (one.Outfits.Count > 0
                && !string.Equals(character.DefaultOutfit, one.Outfits[0], StringComparison.Ordinal))
            {
                character.DefaultOutfit = one.Outfits[0];
                taken++;
            }
        }
        return taken;
    }

    /// <summary>
    /// Drop settings that say what the character already said.
    /// <para/>
    /// A name colour identical to the one the game writes them in, or a
    /// typewriter holding exactly the voice they already have. Both are
    /// invisible on screen — the editor shows the same numbers either way — and
    /// both cost the same thing: the pack asserts a value it did not choose, so
    /// the character is written to the manifest and marked as changed for
    /// agreeing with the game.
    /// <para/>
    /// The editor stops NEW ones happening as they are typed. This is for the
    /// ones already written down.
    /// <para/>
    /// A value that differs — Amelia at 45 where the game speaks her at 25 — is
    /// left alone whether or not anybody meant it. It is a real difference, it
    /// is visible in the panel, and one click resets it; guessing at intent
    /// there would throw away work.
    /// </summary>
    private static int DropWhatMatchesTheDefault(ModPack pack)
    {
        if (pack.Characters == null) return 0;

        int dropped = 0;
        foreach (var character in pack.Characters)
        {
            if (!character.IsVanillaCharacter) continue;

            var speaker = Shared.VanillaSpeech.For(VanillaFaces.KeyOf(character));
            if (speaker == null) continue;

            if (character.NameColor != null && SameColor(character.NameColor, speaker.NameColor))
            {
                character.NameColor = null;
                dropped++;
            }

            var voice = character.Typewriter;
            if (voice != null
                && voice.Enabled == speaker.UseTypewriter
                && voice.Frequency == speaker.Frequency
                && voice.PitchMin == speaker.PitchMin
                && voice.PitchMax == speaker.PitchMax)
            {
                character.Typewriter = null;
                dropped++;
            }
        }
        return dropped;
    }

    /// <summary>Whether two hex colours name the same colour, however they are
    /// spelled. <c>#99c5ff</c> off a colour wheel is the same answer as the
    /// <c>#99C5FF</c> the game has.</summary>
    private static bool SameColor(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            return System.Windows.Media.ColorConverter.ConvertFromString(a!.Trim())
                is System.Windows.Media.Color x
                && System.Windows.Media.ColorConverter.ConvertFromString(b!.Trim())
                is System.Windows.Media.Color y
                && x == y;
        }
        catch { return false; }   // a malformed hex is not the same as anything
    }

    /// <summary>
    /// Put a borrowed character's wardrobe back into the order the game keeps
    /// it in, with anything the pack added after it.
    /// <para/>
    /// Nobody chose the order either. A pack that adopted Amelia through her
    /// Beach bust got that one first and the other seven appended behind it on
    /// the next load, so her list reads Beach, default, Barista where the game
    /// has default, Barista, Beach. It is not visible anywhere an author could
    /// act on, and it was enough to keep her marked as changed after every
    /// difference they COULD act on had been put back.
    /// <para/>
    /// Only when the character names a default outfit outright. Where the field
    /// is blank, "first" IS the default — both here and in the runtime — and
    /// reordering would quietly change which bust the character walks in
    /// wearing.
    /// </summary>
    private static bool ReorderToTheGames(CharacterDef character,
                                          VanillaCharacters.VanillaCharacter one)
    {
        if (string.IsNullOrEmpty(character.DefaultOutfit)) return false;
        if (character.Outfits.Count < 2) return false;

        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < one.Outfits.Count; i++) order[one.Outfits[i]] = i;

        // Theirs in their order, then the pack's own, each keeping the place it
        // had relative to the rest of the pack's.
        var sorted = character.Outfits
            .Select((o, at) => (Outfit: o, At: at))
            .OrderBy(x => !x.Outfit.PackArt && x.Outfit.GameObjectName != null
                       && order.ContainsKey(x.Outfit.GameObjectName)
                          ? order[x.Outfit.GameObjectName]
                          : int.MaxValue)
            .ThenBy(x => x.At)
            .Select(x => x.Outfit)
            .ToList();

        if (sorted.SequenceEqual(character.Outfits)) return false;

        character.Outfits = sorted;
        return true;
    }

    /// <summary>
    /// Drop a borrowed character's expression entries that say nothing.
    /// <para/>
    /// Declaring a vanilla character used to mean writing the whole thing down,
    /// and the editor filled the wardrobe and the five standard expressions in
    /// for you: <c>neutral</c> mapped to nothing, and Happy, Angry, Sad and
    /// Flirty each mapped to a child of their own name. Every one of those is
    /// exactly what the runtime does with no entry at all.
    /// <para/>
    /// They were invisible until the editor started listing the faces the game
    /// gives a character. Now they sit under those, spelling the same four a
    /// second time, and an author cannot tell which row is theirs.
    /// <para/>
    /// Only the ones that RESTATE something. A face pointed at a different
    /// child is an author aiming one of the game's names at their own art,
    /// which is the whole reason the mapping exists — see
    /// <see cref="VanillaFaces.OnlyRestatesTheGame"/>. And only on the game's
    /// characters: on a pack's own, that list is the only list there is.
    /// </summary>
    private static int DropRestatedFaces(ModPack pack)
    {
        if (pack.Characters == null) return 0;

        int dropped = 0;
        foreach (var character in pack.Characters)
        {
            if (!character.IsVanillaCharacter || character.Expressions.Count == 0) continue;

            var faces = VanillaFaces.Of(character);
            dropped += character.Expressions.RemoveAll(
                e => VanillaFaces.OnlyRestatesTheGame(e, faces));
        }
        return dropped;
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
