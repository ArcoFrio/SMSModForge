using System.Collections.Generic;
using System.IO;
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

            if (place.ParallaxStrength < 0f || place.ParallaxStrength > 1.0f)
                issues.Add(new(Severity.Warning, $"{pWhere}.parallaxStrength",
                    $"parallaxStrength {place.ParallaxStrength} is outside the usual 0..1 range"));

            foreach (var btn in place.NavigatorButtons)
                ValidateNavigatorButton(btn, pWhere, placeKeysInPack, issues);
        }

        // ── Vanilla extensions ───────────────────────────────────
        var seenExtensionSources = new HashSet<string>();
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

                foreach (var c in n.Conditions)
                    ValidateNodeCondition(c, nWhere, packVarNames, issues);
                foreach (var act in n.ActionsOnStart)
                    ValidateNodeAction(act, $"{nWhere}.actionsOnStart", packVarNames, actorKeysInPack, issues);
                foreach (var act in n.ActionsOnFinish)
                    ValidateNodeAction(act, $"{nWhere}.actionsOnFinish", packVarNames, actorKeysInPack, issues);
            }
        }

        return issues;
    }

    /// <summary>
    /// Validates one <see cref="NodeConditionDef"/>. Checks the type is
    /// known and that the params look reasonable for it (referenced
    /// variables exist, etc.). Unknown types are errors so a typo can't
    /// silently produce a no-op condition at runtime.
    /// </summary>
    private static void ValidateNodeCondition(NodeConditionDef c, string whereParent,
        HashSet<string> packVarNames, List<ValidationIssue> issues)
    {
        var cWhere = $"{whereParent}.{c.Type}";
        if (string.IsNullOrEmpty(c.Type))
        {
            issues.Add(new(Severity.Error, cWhere, "Condition type is required"));
            return;
        }
        if (System.Array.IndexOf(NodeConditionTypes.All, c.Type) < 0)
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
                else if (!packVarNames.Contains(n))
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
            case NodeConditionTypes.Random:
                if (!c.Params.TryGetValue("chance", out var ch) ||
                    !float.TryParse(ch, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var chV) ||
                    chV < 0f || chV > 1f)
                    issues.Add(new(Severity.Warning, cWhere, "Param 'chance' should be a float in [0,1]"));
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
                else if (!packVarNames.Contains(n))
                    issues.Add(new(Severity.Warning, aWhere,
                        $"Variable '{n}' isn't declared in this pack"));
                if (a.Type == NodeActionTypes.SetVariable && !a.Params.ContainsKey("value"))
                    issues.Add(new(Severity.Warning, aWhere, "Param 'value' is required"));
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
