using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.Validation;

public enum Severity { Info, Warning, Error }

public sealed record ValidationIssue(Severity Severity, string Where, string Message);

/// <summary>
/// Verifies that a pack on disk is internally consistent and ready to ship to
/// the mod. The mod is forgiving (will run with missing PNGs, just with bad
/// sprites), so most file-missing problems are warnings, not errors.
/// </summary>
public static class PackValidator
{
    public static List<ValidationIssue> Validate(ModPack pack, string packRoot)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(pack.PackId))
            issues.Add(new(Severity.Error, "$.packId", "packId is required"));

        var seenKeys = new HashSet<string>();
        foreach (var character in pack.Characters)
        {
            var where = $"characters[{character.Name}]";
            if (string.IsNullOrWhiteSpace(character.Name))
                issues.Add(new(Severity.Error, where, "Character name is required"));

            foreach (var outfit in character.Outfits)
            {
                var oWhere = $"{where}.outfits[{outfit.Key}]";

                if (string.IsNullOrWhiteSpace(outfit.Key))
                    issues.Add(new(Severity.Error, oWhere, "Outfit key is required"));
                else if (!seenKeys.Add(outfit.Key))
                    issues.Add(new(Severity.Error, oWhere, $"Duplicate outfit key '{outfit.Key}'"));

                if (string.IsNullOrWhiteSpace(outfit.GameObjectName))
                    issues.Add(new(Severity.Error, oWhere, "gameObjectName is required"));

                CheckFile(packRoot, outfit.BaseSprite,  $"{oWhere}.baseSprite",  issues);
                CheckFile(packRoot, outfit.MaskSprite,  $"{oWhere}.maskSprite",  issues);
                CheckFile(packRoot, outfit.BlinkSprite, $"{oWhere}.blinkSprite", issues);

                if (outfit.Mouth.Enabled)
                {
                    for (int i = 1; i <= 4; i++)
                        CheckFile(packRoot, outfit.Mouth.Prefix + i + ".PNG",
                                  $"{oWhere}.mouth[{i}]", issues);
                }
                if (outfit.Expression.Enabled)
                {
                    foreach (var name in ExpressionSpec.Names)
                        CheckFile(packRoot, outfit.Expression.Prefix + name + ".PNG",
                                  $"{oWhere}.expression[{name}]", issues);
                }

                // Jiggle sanity bounds. Values outside these still load — we just warn.
                var j = outfit.Jiggle;
                if (j.Strength < -0.5f || j.Strength > 0.5f)
                    issues.Add(new(Severity.Warning, $"{oWhere}.jiggle.strength",
                        $"strength {j.Strength} is outside the usual -0.5..0.5 range"));
                if (j.NoiseStrength < 0f || j.NoiseStrength > 0.5f)
                    issues.Add(new(Severity.Warning, $"{oWhere}.jiggle.noiseStrength",
                        $"noiseStrength {j.NoiseStrength} is outside the usual 0..0.5 range"));
            }
        }

        // ── Places ───────────────────────────────────────────────
        var seenPlaceKeys = new HashSet<string>();
        var placeKeysInPack = new HashSet<string>();
        foreach (var p in pack.Places) placeKeysInPack.Add(p.Key);

        // NPC keys placements reference.
        var npcKeysInPack = new HashSet<string>();
        foreach (var n in pack.Npcs)
            if (!string.IsNullOrWhiteSpace(n.Key)) npcKeysInPack.Add(n.Key);

        foreach (var place in pack.Places)
        {
            var pWhere = $"places[{place.Key}]";
            if (string.IsNullOrWhiteSpace(place.Key))
                issues.Add(new(Severity.Error, pWhere, "Place key is required"));
            else if (!seenPlaceKeys.Add(place.Key))
                issues.Add(new(Severity.Error, pWhere, $"Duplicate place key '{place.Key}'"));

            if (string.IsNullOrWhiteSpace(place.InternalName))
                issues.Add(new(Severity.Warning, $"{pWhere}.internalName",
                    "internalName is empty; loader will fall back to the key"));

            CheckFile(packRoot, place.BaseSprite,      $"{pWhere}.baseSprite",      issues);
            CheckFile(packRoot, place.SecondarySprite, $"{pWhere}.secondarySprite", issues);
            CheckFile(packRoot, place.MaskSprite,      $"{pWhere}.maskSprite",      issues);

            // 1.5 is a real vanilla value (a backdrop that overshoots the room in
            // front of it), so the old 0..1 ceiling flagged legitimate settings.
            CheckParallax(place.ParallaxStrength, $"{pWhere}.parallaxStrength", issues);
            if (place.ParallaxSecondaryStrength.HasValue)
                CheckParallax(place.ParallaxSecondaryStrength.Value, $"{pWhere}.parallaxSecondaryStrength", issues);

            foreach (var btn in place.NavigatorButtons)
                ValidateNavigatorButton(btn, pWhere, placeKeysInPack, issues);

            // NPC placements: reference an existing NPC, and each GameObject
            // name must be unique within this level (two same-named NPCs would
            // collide in the hierarchy and SetGameObjectActive couldn't tell
            // them apart). Placements live anywhere in the place's GameObject
            // tree, so collect them across the whole hierarchy.
            var placements = new List<(NpcPlacementDef Placement, string Path)>();
            CollectPlacements(place.GameObjects, "", placements);
            var seenPlacementNames = new HashSet<string>();
            for (int pi = 0; pi < placements.Count; pi++)
            {
                var pl = placements[pi].Placement;
                var plWhere = $"{pWhere}.{placements[pi].Path}.npcs[{pi}]";
                if (string.IsNullOrWhiteSpace(pl.Npc))
                    issues.Add(new(Severity.Error, plWhere, "NPC placement has no npc key"));
                else if (!npcKeysInPack.Contains(pl.Npc))
                    issues.Add(new(Severity.Error, plWhere,
                        $"NPC placement references unknown NPC '{pl.Npc}'"));

                string effName = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                if (!string.IsNullOrWhiteSpace(effName) && !seenPlacementNames.Add(effName))
                    issues.Add(new(Severity.Warning, plWhere,
                        $"Two NPC placements resolve to the same GameObject name '{effName}' in this level — " +
                        "give one an explicit Name so they don't collide"));
            }
        }

        // ── Vanilla extensions ───────────────────────────────────
        var seenExtensionSources = new HashSet<string>();
        // (see CollectPlacements below for the tree walk placements use)
        for (int i = 0; i < pack.VanillaExtensions.Count; i++)
        {
            var ext = pack.VanillaExtensions[i];
            var eWhere = $"vanillaExtensions[{i}:{ext.Source}]";

            if (!PlaceTargetRef.TryParse(ext.Source, out var sourceRef) ||
                sourceRef.Kind != PlaceTargetKind.Vanilla)
            {
                issues.Add(new(Severity.Error, $"{eWhere}.source",
                    $"Vanilla source '{ext.Source}' is malformed (expected 'vanilla:<goName>')"));
            }
            else
            {
                if (VanillaPlaces.FindByGoName(sourceRef.Key) == null)
                    issues.Add(new(Severity.Warning, $"{eWhere}.source",
                        $"Vanilla level '{sourceRef.Key}' is not in the 1.8E catalog"));
                if (!seenExtensionSources.Add(ext.Source))
                    issues.Add(new(Severity.Warning, $"{eWhere}.source",
                        $"Multiple vanilla extensions target '{ext.Source}' — buttons will pile up on the same nav strip; consider consolidating"));
            }

            foreach (var btn in ext.NavigatorButtons)
                ValidateNavigatorButton(btn, eWhere, placeKeysInPack, issues);
        }

        // ── NPCs ──────────────────────────────────────────────────
        var seenNpcKeys = new HashSet<string>();
        for (int i = 0; i < pack.Npcs.Count; i++)
        {
            var npc = pack.Npcs[i];
            var nWhere = $"npcs[{i}:{npc.Key}]";
            if (string.IsNullOrWhiteSpace(npc.Key))
                issues.Add(new(Severity.Error, nWhere, "NPC key is required"));
            else if (!seenNpcKeys.Add(npc.Key))
                issues.Add(new(Severity.Error, nWhere, $"Duplicate NPC key '{npc.Key}'"));

            if (string.IsNullOrWhiteSpace(npc.Sprite))
                issues.Add(new(Severity.Error, $"{nWhere}.sprite", "NPC sprite path is required"));
            else
                CheckFile(packRoot, npc.Sprite, $"{nWhere}.sprite", issues);

            if (!string.IsNullOrWhiteSpace(npc.Mask))
                CheckFile(packRoot, npc.Mask, $"{nWhere}.mask", issues);
            if (!string.IsNullOrWhiteSpace(npc.Blink.Sprite))
                CheckFile(packRoot, npc.Blink.Sprite, $"{nWhere}.blink.sprite", issues);

            if (npc.Blink.MaxWait < npc.Blink.MinWait)
                issues.Add(new(Severity.Warning, $"{nWhere}.blink",
                    $"Blink max wait ({npc.Blink.MaxWait}) is below min ({npc.Blink.MinWait})"));
        }

        // ── Map buttons (World Map radial entries) ────────────────
        for (int i = 0; i < pack.MapButtons.Count; i++)
        {
            var mb = pack.MapButtons[i];
            var mWhere = $"mapButtons[{i}:{mb.District}→{mb.Target}]";

            if (!PlaceTargetRef.TryParse(mb.Target, out var tref))
            {
                issues.Add(new(Severity.Error, $"{mWhere}.target",
                    $"Map button target '{mb.Target}' is malformed " +
                    "(expected 'vanilla:<goName>', 'pack:<packId>.<key>', or 'self:<key>')"));
            }
            else
            {
                switch (tref.Kind)
                {
                    case PlaceTargetKind.Vanilla:
                        if (VanillaPlaces.FindByGoName(tref.Key) == null)
                            issues.Add(new(Severity.Warning, $"{mWhere}.target",
                                $"Vanilla level '{tref.Key}' is not in the 1.8E catalog"));
                        break;
                    case PlaceTargetKind.Self:
                        if (!placeKeysInPack.Contains(tref.Key))
                            issues.Add(new(Severity.Error, $"{mWhere}.target",
                                $"self target '{tref.Key}' has no matching place in this pack"));
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(mb.District))
                issues.Add(new(Severity.Error, $"{mWhere}.district",
                    "Map button district is required"));
            else if (WorldMapDistricts.FindByGoName(mb.District) == null)
                issues.Add(new(Severity.Warning, $"{mWhere}.district",
                    $"District '{mb.District}' is not in the 1.8E World Map catalog (Seaside, TheLine, NeonRow, Shopside, Foundry)"));
        }

        // ── Variables ─────────────────────────────────────────────
        var seenVarNames = new HashSet<string>();
        var packVarNames = new HashSet<string>();
        foreach (var v in pack.Variables) packVarNames.Add(v.Name);
        foreach (var v in pack.Variables)
        {
            var vWhere = $"variables[{v.Name}]";
            if (string.IsNullOrWhiteSpace(v.Name))
                issues.Add(new(Severity.Error, vWhere, "Variable name is required"));
            else if (!seenVarNames.Add(v.Name))
                issues.Add(new(Severity.Error, vWhere, $"Duplicate variable name '{v.Name}'"));

            // Verify defaultValue parses for the chosen type.
            switch (v.Type)
            {
                case PackVariableType.Bool:
                    if (!bool.TryParse(v.DefaultValue, out _))
                        issues.Add(new(Severity.Warning, $"{vWhere}.defaultValue",
                            $"Bool default '{v.DefaultValue}' does not parse as true/false; runtime will treat as false"));
                    break;
                case PackVariableType.Int:
                    if (!int.TryParse(v.DefaultValue, System.Globalization.NumberStyles.Integer,
                                      System.Globalization.CultureInfo.InvariantCulture, out _))
                        issues.Add(new(Severity.Warning, $"{vWhere}.defaultValue",
                            $"Int default '{v.DefaultValue}' does not parse as an integer; runtime will treat as 0"));
                    break;
                case PackVariableType.Float:
                    if (!float.TryParse(v.DefaultValue, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out _))
                        issues.Add(new(Severity.Warning, $"{vWhere}.defaultValue",
                            $"Float default '{v.DefaultValue}' does not parse as a number; runtime will treat as 0"));
                    break;
                case PackVariableType.String:
                    // Anything is a valid string.
                    break;
            }
        }

        // ── Actors ────────────────────────────────────────────────
        var seenActorKeys = new HashSet<string>();
        var actorKeysInPack = new HashSet<string>();
        var bustNamesInPack = new HashSet<string>();
        foreach (var a in pack.Actors) actorKeysInPack.Add(a.Key);
        foreach (var ch in pack.Characters)
            foreach (var o in ch.Outfits)
                bustNamesInPack.Add(o.GameObjectName);

        foreach (var a in pack.Actors)
        {
            var aWhere = $"actors[{a.Key}]";
            if (string.IsNullOrWhiteSpace(a.Key))
                issues.Add(new(Severity.Error, aWhere, "Actor key is required"));
            else if (!seenActorKeys.Add(a.Key))
                issues.Add(new(Severity.Error, aWhere, $"Duplicate actor key '{a.Key}'"));

            if (string.IsNullOrWhiteSpace(a.DefaultBustKey))
            {
                issues.Add(new(Severity.Info, $"{aWhere}.defaultBustKey",
                    "No default bust — actor will speak with no visible portrait until SetActorBust runs"));
            }
            else if (!bustNamesInPack.Contains(a.DefaultBustKey) &&
                     VanillaBusts.FindByGoName(a.DefaultBustKey) == null)
            {
                issues.Add(new(Severity.Warning, $"{aWhere}.defaultBustKey",
                    $"Default bust '{a.DefaultBustKey}' isn't a vanilla bust in the 1.8E catalog and isn't a GameObjectName from any outfit in this pack — runtime will still try GameObject.Find under 2_Bust_Manager, but verify the name"));
            }

            var seenExprKeys = new HashSet<string>();
            foreach (var e in a.Expressions)
            {
                if (string.IsNullOrWhiteSpace(e.Key))
                    issues.Add(new(Severity.Error, $"{aWhere}.expressions[empty]",
                        "Expression key is required"));
                else if (!seenExprKeys.Add(e.Key))
                    issues.Add(new(Severity.Error, $"{aWhere}.expressions[{e.Key}]",
                        $"Duplicate expression key '{e.Key}' on actor"));
            }
        }

        // ── Dialogues ─────────────────────────────────────────────
        var seenDialogueKeys = new HashSet<string>();
        foreach (var d in pack.Dialogues)
        {
            var dWhere = $"dialogues[{d.Key}]";
            if (string.IsNullOrWhiteSpace(d.Key))
                issues.Add(new(Severity.Error, dWhere, "Dialogue key is required"));
            else if (!seenDialogueKeys.Add(d.Key))
                issues.Add(new(Severity.Error, dWhere, $"Duplicate dialogue key '{d.Key}'"));

            // RoomTalk target: vanilla:<name> or place:<key>
            if (string.IsNullOrWhiteSpace(d.RoomTalk))
            {
                issues.Add(new(Severity.Error, $"{dWhere}.roomTalk",
                    "RoomTalk target is required — pick a vanilla:* or place:* token"));
            }
            else
            {
                int colon = d.RoomTalk.IndexOf(':');
                if (colon <= 0 || colon == d.RoomTalk.Length - 1)
                {
                    issues.Add(new(Severity.Error, $"{dWhere}.roomTalk",
                        $"RoomTalk '{d.RoomTalk}' is malformed (expected 'vanilla:<name>' or 'place:<key>')"));
                }
                else
                {
                    var scheme = d.RoomTalk.Substring(0, colon);
                    var rest = d.RoomTalk.Substring(colon + 1);
                    if (scheme == "vanilla")
                    {
                        // A pack-registered custom roomtalk is intentional (the runtime
                        // creates it on the fly), so it's not an "unknown vanilla name".
                        if (VanillaRoomTalks.FindByName(rest) == null && !pack.CustomRoomTalks.Contains(rest))
                            issues.Add(new(Severity.Warning, $"{dWhere}.roomTalk",
                                $"Vanilla roomtalk '{rest}' is not in the 1.8E catalog"));
                    }
                    else if (scheme == "place")
                    {
                        if (!placeKeysInPack.Contains(rest))
                            issues.Add(new(Severity.Error, $"{dWhere}.roomTalk",
                                $"place:{rest} has no matching place in this pack"));
                    }
                    else
                    {
                        issues.Add(new(Severity.Error, $"{dWhere}.roomTalk",
                            $"Unknown roomtalk scheme '{scheme}'"));
                    }
                }
            }

            foreach (var c in d.StartConditions)
                ValidateNodeCondition(c, $"{dWhere}.startConditions", packVarNames, issues);

            // Build node-id set + tag set so jumps/children can be cross-checked.
            var nodeIds = new HashSet<int>();
            var tags = new HashSet<string>();
            foreach (var n in d.Nodes)
            {
                if (!nodeIds.Add(n.Id))
                    issues.Add(new(Severity.Error, $"{dWhere}.nodes[id={n.Id}]",
                        $"Duplicate node id {n.Id}"));
                if (!string.IsNullOrEmpty(n.Tag) && !tags.Add(n.Tag))
                    issues.Add(new(Severity.Warning, $"{dWhere}.nodes[id={n.Id}].tag",
                        $"Tag '{n.Tag}' is used by multiple nodes — jumps target the first one found"));
            }

            // Root list references must exist.
            foreach (var rid in d.RootNodeIds)
                if (!nodeIds.Contains(rid))
                    issues.Add(new(Severity.Error, $"{dWhere}.rootNodeIds",
                        $"Root id {rid} doesn't exist among the nodes"));
            if (d.Nodes.Count > 0 && d.RootNodeIds.Count == 0)
                issues.Add(new(Severity.Warning, $"{dWhere}.rootNodeIds",
                    "Dialogue has nodes but no root — runtime will pick the first node as root"));

            foreach (var n in d.Nodes)
            {
                var nWhere = $"{dWhere}.nodes[id={n.Id}]";

                // Actor + expression sanity.
                if (!string.IsNullOrEmpty(n.Actor) && !actorKeysInPack.Contains(n.Actor))
                    issues.Add(new(Severity.Warning, $"{nWhere}.actor",
                        $"Actor '{n.Actor}' isn't defined in this pack"));

                // Children must exist.
                foreach (var cid in n.Children)
                    if (!nodeIds.Contains(cid))
                        issues.Add(new(Severity.Error, $"{nWhere}.children",
                            $"Child id {cid} doesn't exist among the nodes"));

                // Jump tag must exist when the mode is Jump.
                if (n.Jump != null && n.Jump.Mode == JumpMode.Jump)
                {
                    if (string.IsNullOrWhiteSpace(n.Jump.TargetTag))
                        issues.Add(new(Severity.Error, $"{nWhere}.jump",
                            "Jump mode is Jump but targetTag is empty"));
                    else if (!tags.Contains(n.Jump.TargetTag))
                        issues.Add(new(Severity.Error, $"{nWhere}.jump",
                            $"Jump target tag '{n.Jump.TargetTag}' isn't a tag on any node in this dialogue"));
                }

                // A node's own conditions are evaluated once, when GC2 reaches
                // the node — unlike the dialogue's start conditions above,
                // which the dispatcher polls every frame.
                foreach (var c in n.Conditions)
                    ValidateNodeCondition(c, nWhere, packVarNames, issues, ConditionContext.OneShot);
                foreach (var act in n.ActionsOnStart)
                    ValidateNodeAction(act, $"{nWhere}.actionsOnStart", packVarNames, actorKeysInPack, issues);
                foreach (var act in n.ActionsOnFinish)
                    ValidateNodeAction(act, $"{nWhere}.actionsOnFinish", packVarNames, actorKeysInPack, issues);
            }
        }

        // Wallpaper unlock conditions — the standard condition list, polled
        // per frame by the runtime's visibility tick.
        foreach (var w in pack.Wallpapers)
            foreach (var c in w.UnlockConditions)
                ValidateNodeCondition(c, $"wallpapers.{w.Key}.unlockConditions", packVarNames, issues);

        // Integration rules — the IF conditions plus every else-if branch.
        // These carry the Rule context, the one host where a Timer's interval
        // actually restarts.
        foreach (var r in pack.IntegrationRules)
        {
            var rWhere = $"integrationRules.{r.Key}";
            foreach (var c in r.Conditions)
                ValidateNodeCondition(c, $"{rWhere}.conditions", packVarNames, issues, ConditionContext.Rule);
            for (int i = 0; i < r.Branches.Count; i++)
                foreach (var c in r.Branches[i].Conditions)
                    ValidateNodeCondition(c, $"{rWhere}.branches[{i}].conditions", packVarNames,
                                          issues, ConditionContext.Rule);
        }

        return issues;
    }

    /// <summary>
    /// Validates one <see cref="NodeConditionDef"/>. Checks the type is
    /// known and that the params look reasonable for it (referenced
    /// variables exist, etc.). Unknown types are errors so a typo can't
    /// silently produce a no-op condition at runtime.
    /// </summary>
    /// <summary>Accepted <c>ListCount</c> comparison labels. Read off the schema
    /// so the validator can't drift from what the editor's dropdown offers.</summary>
    private static readonly string[] ListComparisons =
        ConditionSchemas.For(NodeConditionTypes.ListCount)
                        .First(s => s.Key == "comparison").FixedOptions;

    private static void ValidateNodeCondition(NodeConditionDef c, string whereParent,
        HashSet<string> packVarNames, List<ValidationIssue> issues,
        ConditionContext context = ConditionContext.Polled)
    {
        var cWhere = $"{whereParent}.{c.Type}";
        if (string.IsNullOrEmpty(c.Type))
        {
            issues.Add(new(Severity.Error, cWhere, "Condition type is required"));
            return;
        }
        // AND/OR groups carry nested children instead of params. They're not in
        // AllRecognized (they're not leaf types, and the Type combo excludes
        // them), so check them before the unknown-type guard and recurse —
        // otherwise a perfectly valid group reads as an unknown type, and its
        // children escape validation entirely.
        if (NodeConditionTypes.IsGroup(c.Type))
        {
            if (c.Conditions != null)
                foreach (var child in c.Conditions)
                    ValidateNodeCondition(child, cWhere, packVarNames, issues, context);
            return;
        }
        if (System.Array.IndexOf(NodeConditionTypes.AllRecognized, c.Type) < 0)
        {
            issues.Add(new(Severity.Error, cWhere, $"Unknown condition type '{c.Type}'"));
            return;
        }

        switch (c.Type)
        {
            case NodeConditionTypes.VariableEquals:
            case NodeConditionTypes.VariableGreaterThan:
            case NodeConditionTypes.VariableLessThan:
            case NodeConditionTypes.VariableGreaterOrEqual:
            case NodeConditionTypes.VariableLessOrEqual:
            case NodeConditionTypes.VariableExists:
            {
                bool vanilla = c.Params.TryGetValue("source", out var src) &&
                               string.Equals(src, "vanilla", System.StringComparison.OrdinalIgnoreCase);
                if (!c.Params.TryGetValue("name", out var n) || string.IsNullOrWhiteSpace(n))
                    issues.Add(new(Severity.Error, cWhere, "Param 'name' is required"));
                else if (vanilla)
                {
                    if (!VanillaGameVariables.Contains(n))
                        issues.Add(new(Severity.Warning, cWhere,
                            $"Vanilla variable '{n}' isn't in the 1.8E catalog"));
                }
                else if (!IsTemplated(n) && !packVarNames.Contains(n))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"Variable '{n}' isn't declared in this pack — condition will read the default"));
                if (c.Type != NodeConditionTypes.VariableExists &&
                    (!c.Params.ContainsKey("value") || string.IsNullOrWhiteSpace(c.Params["value"])))
                    issues.Add(new(Severity.Warning, cWhere, "Param 'value' is required for this condition"));
                break;
            }
            case NodeConditionTypes.GameVariableEquals:
                if (!c.Params.ContainsKey("name")) issues.Add(new(Severity.Error, cWhere, "Param 'name' is required"));
                if (!c.Params.ContainsKey("value")) issues.Add(new(Severity.Warning, cWhere, "Param 'value' is required"));
                break;
            case NodeConditionTypes.LevelActive:
                if (!c.Params.ContainsKey("level")) issues.Add(new(Severity.Error, cWhere, "Param 'level' is required"));
                break;
            case NodeConditionTypes.GameObjectActive:
                if (!c.Params.ContainsKey("path")) issues.Add(new(Severity.Error, cWhere, "Param 'path' is required"));
                break;
            case NodeConditionTypes.VariableStartsWith:
            {
                bool vanillaPfx = c.Params.TryGetValue("source", out var psrc) &&
                                  string.Equals(psrc, "vanilla", System.StringComparison.OrdinalIgnoreCase);
                if (!c.Params.TryGetValue("name", out var pn) || string.IsNullOrWhiteSpace(pn))
                    issues.Add(new(Severity.Error, cWhere, "Param 'name' is required"));
                else if (vanillaPfx)
                {
                    if (!VanillaGameVariables.Contains(pn))
                        issues.Add(new(Severity.Warning, cWhere,
                            $"Vanilla variable '{pn}' isn't in the 1.8E catalog"));
                }
                else if (!IsTemplated(pn) && !packVarNames.Contains(pn))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"Variable '{pn}' isn't declared in this pack — condition will read the default"));

                // An empty prefix would match everything, so the runtime refuses
                // it; that's almost always a half-filled row rather than intent.
                if (!c.Params.TryGetValue("value", out var pv) || string.IsNullOrEmpty(pv))
                    issues.Add(new(Severity.Warning, cWhere,
                        "Param 'value' (the prefix) is empty — this condition never passes"));
                break;
            }
            case NodeConditionTypes.ListContains:
            case NodeConditionTypes.ListCount:
            {
                if (!c.Params.TryGetValue("list", out var ln) || string.IsNullOrWhiteSpace(ln))
                    issues.Add(new(Severity.Error, cWhere, "Param 'list' is required"));
                else if (!IsTemplated(ln) && !packVarNames.Contains(ln))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"Variable '{ln}' isn't declared in this pack — the condition will read an empty list"));

                if (c.Type == NodeConditionTypes.ListContains)
                {
                    if (!c.Params.TryGetValue("value", out var lv) || string.IsNullOrEmpty(lv))
                        issues.Add(new(Severity.Warning, cWhere,
                            "Param 'value' is empty — this will never match an entry"));
                }
                else
                {
                    if (!c.Params.TryGetValue("value", out var cv) ||
                        !float.TryParse(cv, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out _))
                        issues.Add(new(Severity.Error, cWhere, "Param 'value' must be a number"));

                    // Unknown comparison silently falls back to "equals" at
                    // runtime, which is a quiet wrong answer — flag the typo.
                    if (c.Params.TryGetValue("comparison", out var cmp) && !string.IsNullOrEmpty(cmp) &&
                        System.Array.IndexOf(ListComparisons, cmp) < 0)
                        issues.Add(new(Severity.Error, cWhere,
                            $"Unknown comparison '{cmp}' — expected one of: {string.Join(", ", ListComparisons)}"));
                }
                break;
            }
            case NodeConditionTypes.Timer:
            {
                // Only an integration rule has a "fired" event to restart the
                // interval on. Anywhere else the timer elapses once and then
                // stays permanently true — silently a no-op, so flag it.
                if (context != ConditionContext.Rule)
                    issues.Add(new(Severity.Warning, cWhere,
                        "'Timer' only restarts when an integration rule fires. In this " +
                        "host there's no fire event, so it elapses once and then passes " +
                        "forever. Move it to an integration rule."));

                bool randomized = c.Params.TryGetValue("randomize", out var rz) &&
                                  string.Equals(rz, "true", System.StringComparison.OrdinalIgnoreCase);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var num = System.Globalization.NumberStyles.Float;

                if (randomized)
                {
                    bool okMin = c.Params.TryGetValue("minSeconds", out var mn) &&
                                 float.TryParse(mn, num, inv, out var mnV) && mnV >= 0;
                    bool okMax = c.Params.TryGetValue("maxSeconds", out var mx) &&
                                 float.TryParse(mx, num, inv, out var mxV) && mxV >= 0;
                    if (!okMin) issues.Add(new(Severity.Error, cWhere,
                        "Param 'minSeconds' must be a number >= 0 when Randomize is on"));
                    if (!okMax) issues.Add(new(Severity.Error, cWhere,
                        "Param 'maxSeconds' must be a number >= 0 when Randomize is on"));
                    if (okMin && okMax &&
                        float.TryParse(c.Params["minSeconds"], num, inv, out var a) &&
                        float.TryParse(c.Params["maxSeconds"], num, inv, out var b) && b < a)
                        issues.Add(new(Severity.Warning, cWhere,
                            $"'maxSeconds' ({b}) is below 'minSeconds' ({a}) — the runtime " +
                            "swaps them, but the range is probably backwards."));
                }
                else if (!c.Params.TryGetValue("seconds", out var sec) ||
                         !float.TryParse(sec, num, inv, out var secV) || secV < 0)
                {
                    issues.Add(new(Severity.Error, cWhere, "Param 'seconds' must be a number >= 0"));
                }
                break;
            }
            case NodeConditionTypes.Random:
                // Fine in a one-shot host (node conditions, level hooks) — it's
                // rolled exactly once there. In a polled host it re-rolls every
                // frame, so the authored chance is meaningless: flag those.
                if (context == ConditionContext.Polled)
                    issues.Add(new(Severity.Warning, cWhere,
                        "'Random' re-rolls on every evaluation, and this condition is " +
                        "re-checked every frame (~60×/sec), so its chance doesn't mean what " +
                        "it says. Replace with 'DailyChance' (rolls once per in-game day), " +
                        "or a LevelRandom variable + a numeric comparison (once per visit). " +
                        "'Random' is fine on a dialogue NODE's conditions or a level hook, " +
                        "which are evaluated once."));
                if (!c.Params.TryGetValue("chance", out var ch) ||
                    !float.TryParse(ch, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var chV) ||
                    chV < 0f || chV > 1f)
                    issues.Add(new(Severity.Warning, cWhere, "Param 'chance' should be a float in [0,1]"));
                break;
            case NodeConditionTypes.DailyChance:
                if (!c.Params.TryGetValue("chance", out var dch) ||
                    !float.TryParse(dch, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var dchV) ||
                    dchV < 0f || dchV > 100f)
                    issues.Add(new(Severity.Warning, cWhere,
                        "Param 'chance' should be a whole percentage in [0,100]"));
                break;
        }
    }

    /// <summary>Validates one <see cref="NodeActionDef"/>. Mirror of <see cref="ValidateNodeCondition"/>.</summary>
    private static void ValidateNodeAction(NodeActionDef a, string whereParent,
        HashSet<string> packVarNames, HashSet<string> actorKeysInPack, List<ValidationIssue> issues)
    {
        var aWhere = $"{whereParent}.{a.Type}";
        if (string.IsNullOrEmpty(a.Type))
        {
            issues.Add(new(Severity.Error, aWhere, "Action type is required"));
            return;
        }
        if (System.Array.IndexOf(NodeActionTypes.All, a.Type) < 0)
        {
            issues.Add(new(Severity.Error, aWhere, $"Unknown action type '{a.Type}'"));
            return;
        }

        switch (a.Type)
        {
            case NodeActionTypes.DiceRoll:
                {
                    if (a.Branches == null || a.Branches.Count < 2)
                    {
                        issues.Add(new(Severity.Error, aWhere,
                            "DiceRoll needs at least 2 branches"));
                        break;
                    }
                    int total = 0;
                    for (int i = 0; i < a.Branches.Count; i++)
                    {
                        var b = a.Branches[i];
                        if (b.Chance < 1)
                            issues.Add(new(Severity.Error, aWhere,
                                $"Branch {i + 1} chance must be at least 1%"));
                        total += b.Chance;
                        if (b.Action == null)
                            issues.Add(new(Severity.Error, aWhere,
                                $"Branch {i + 1} has no action"));
                        else
                            ValidateNodeAction(b.Action, $"{aWhere}.branch[{i + 1}]",
                                               packVarNames, actorKeysInPack, issues);
                    }
                    if (total != 100)
                        issues.Add(new(Severity.Error, aWhere,
                            $"Branch chances sum to {total}% — must be exactly 100%"));
                    break;
                }
            case NodeActionTypes.SetVariable:
            case NodeActionTypes.IncrementVariable:
                bool varVanilla = a.Params.TryGetValue("source", out var aSrc) &&
                                  string.Equals(aSrc, "vanilla", System.StringComparison.OrdinalIgnoreCase);
                if (!a.Params.TryGetValue("name", out var n) || string.IsNullOrWhiteSpace(n))
                    issues.Add(new(Severity.Error, aWhere, "Param 'name' is required"));
                else if (varVanilla)
                {
                    if (!VanillaGameVariables.Contains(n))
                        issues.Add(new(Severity.Warning, aWhere,
                            $"Vanilla variable '{n}' isn't in the 1.8E catalog"));
                }
                else if (!IsTemplated(n) && !packVarNames.Contains(n))
                    issues.Add(new(Severity.Warning, aWhere,
                        $"Variable '{n}' isn't declared in this pack"));
                // An explicit empty value is legitimate — it clears the variable.
                // Only a wholly ABSENT key reads as an unfilled row.
                if (a.Type == NodeActionTypes.SetVariable && !a.Params.ContainsKey("value"))
                    issues.Add(new(Severity.Warning, aWhere,
                        "Param 'value' is required — leave the field blank to clear the " +
                        "variable and it will be stored as an explicit empty value"));
                if (a.Type == NodeActionTypes.IncrementVariable && !a.Params.ContainsKey("delta"))
                    issues.Add(new(Severity.Warning, aWhere, "Param 'delta' is required"));
                break;
            case NodeActionTypes.SetActorBust:
            case NodeActionTypes.SetActorExpression:
            case NodeActionTypes.DeactivateBust:
            case NodeActionTypes.LeaveBust:
                if (!a.Params.TryGetValue("actor", out var act) || string.IsNullOrWhiteSpace(act))
                    issues.Add(new(Severity.Error, aWhere, "Param 'actor' is required"));
                else if (!actorKeysInPack.Contains(act))
                    issues.Add(new(Severity.Warning, aWhere,
                        $"Actor '{act}' isn't defined in this pack"));
                break;
            case NodeActionTypes.SetGameObjectActive:
                // Unified Set-Active uses 'target' (+ 'kind'); 'path' is the
                // legacy param, still accepted for pre-unify packs.
                if (!a.Params.ContainsKey("target") && !a.Params.ContainsKey("path"))
                    issues.Add(new(Severity.Error, aWhere, "Param 'target' is required"));
                if (!a.Params.ContainsKey("active")) issues.Add(new(Severity.Warning, aWhere, "Param 'active' (true/false) is required"));
                break;
            case NodeActionTypes.EmitSignal:
                if (!a.Params.ContainsKey("signal")) issues.Add(new(Severity.Error, aWhere, "Param 'signal' is required"));
                break;
            case NodeActionTypes.SwitchMusic:
                if (!a.Params.ContainsKey("music")) issues.Add(new(Severity.Error, aWhere, "Param 'music' is required"));
                break;
            case NodeActionTypes.Wait:
                if (!a.Params.TryGetValue("seconds", out var s) ||
                    !float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var sv) ||
                    sv < 0f)
                    issues.Add(new(Severity.Error, aWhere, "Param 'seconds' must be a non-negative float"));
                break;
        }
    }

    /// <summary>
    /// True when a name carries a <c>{placeholder}</c> — a parameterized rule's
    /// per-value substitution (see <see cref="UpdateRuleDef.ForEach"/>). The real
    /// name only exists at runtime, so "is this variable declared?" can't be
    /// answered here and the check is skipped rather than reported as a miss.
    /// </summary>
    private static bool IsTemplated(string name)
        => !string.IsNullOrEmpty(name) && name.IndexOf('{') >= 0 && name.IndexOf('}') > name.IndexOf('{');

    /// <summary>
    /// Every NPC placement in a GameObject tree, paired with the slash path of
    /// the node it hangs under — placements live wherever they're nested, so
    /// per-level checks (unknown NPC key, colliding names) walk the hierarchy.
    /// </summary>
    private static void CollectPlacements(List<GameObjectDef> nodes, string prefix,
        List<(NpcPlacementDef Placement, string Path)> into)
    {
        if (nodes == null) return;
        foreach (var n in nodes)
        {
            string name = string.IsNullOrWhiteSpace(n.Name) ? "(unnamed)" : n.Name;
            string path = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
            foreach (var pl in n.Npcs)
            {
                into.Add((pl, path));
                // An NPC's own children are GameObjects that may host NPCs too.
                string plName = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                CollectPlacements(pl.Children, path + "/" + plName, into);
            }
            CollectPlacements(n.Children, path, into);
        }
    }

    /// <summary>
    /// Shared check for a single <see cref="NavigatorButtonDef"/> target. The
    /// per-place and per-vanilla-extension paths in <see cref="Validate"/>
    /// both go through this method so the rules stay in sync.
    /// </summary>
    private static void ValidateNavigatorButton(NavigatorButtonDef btn, string whereParent,
        HashSet<string> placeKeysInPack, List<ValidationIssue> issues)
    {
        var bWhere = $"{whereParent}.navigatorButtons[→{btn.Target}]";

        if (!PlaceTargetRef.TryParse(btn.Target, out var tref))
        {
            issues.Add(new(Severity.Error, bWhere,
                $"Navigator target '{btn.Target}' is malformed " +
                "(expected 'vanilla:<goName>', 'pack:<packId>.<key>', or 'self:<key>')"));
            return;
        }

        switch (tref.Kind)
        {
            case PlaceTargetKind.Vanilla:
                if (VanillaPlaces.FindByGoName(tref.Key) == null)
                    issues.Add(new(Severity.Warning, bWhere,
                        $"Vanilla level '{tref.Key}' is not in the 1.8E catalog"));
                break;
            case PlaceTargetKind.Self:
                if (!placeKeysInPack.Contains(tref.Key))
                    issues.Add(new(Severity.Error, bWhere,
                        $"self target '{tref.Key}' has no matching place in this pack"));
                break;
            case PlaceTargetKind.Pack:
                // We can't verify a cross-pack reference at author time;
                // surface it as info so the user knows the dependency is implicit.
                issues.Add(new(Severity.Info, bWhere,
                    $"Cross-pack target '{tref.PackId}.{tref.Key}' — relies on the other pack being installed"));
                break;
        }
    }

    /// <summary>Flag a parallax strength outside the range the game itself uses.
    /// Vanilla's 221 ParallaxMouseEffect instances span 0 to 1.5, so that is the
    /// band — anything past it is not wrong, just far outside anything shipped.</summary>
    private static void CheckParallax(float strength, string where, List<ValidationIssue> issues)
    {
        if (strength < 0f || strength > 1.5f)
            issues.Add(new(Severity.Warning, where,
                $"parallax strength {strength} is outside the 0..1.5 range vanilla levels use"));
    }

    private static void CheckFile(string packRoot, string relPath, string where, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(relPath))
        {
            issues.Add(new(Severity.Warning, where, "Empty path"));
            return;
        }
        var abs = Path.Combine(packRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(abs))
            issues.Add(new(Severity.Warning, where, $"File not found: {abs}"));
    }
}
