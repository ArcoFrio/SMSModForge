using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Turns the vanilla game's own conditions into the ones the editor already
/// speaks.
/// <para/>
/// A vanilla dialogue is gated twice over: each root node carries conditions
/// deciding whether that branch of the conversation is eligible, and the room
/// above it carries the branch that decides whether the conversation plays at
/// all. Both are Game Creator conditions, and both have to arrive as
/// <see cref="NodeConditionDef"/> for an author to read them beside their own.
/// <para/>
/// Everything is written in the spelling the editor uses TODAY, not the one it
/// still reads. A seeded condition in a legacy form is rewritten the instant
/// the editor shows it, and a rewritten condition is indistinguishable from one
/// the author changed - nine lines of the beach conversation looked edited
/// before anyone had touched them.
/// <para/>
/// The whole game uses six condition types — a boolean, an integer and a
/// decimal comparison, a chance, an is-active check and an OR of any of those —
/// and between them six numeric comparisons. Every one has an equivalent here,
/// addressed the same way <see cref="VanillaGameVariables"/> addresses a
/// global: by name, with the owning list kept only for display.
/// <para/>
/// A comparison against another variable is written as a <c>${name}</c>
/// reference with <c>valueSource</c> naming the store to read it from, which
/// the runtime resolves before comparing. Every condition in the game
/// translates, including the three that compare two globals against each other.
/// <para/>
/// What would NOT translate still says so by returning null rather than by
/// guessing — an unrecognised type, a comparison this has never seen, an
/// is-active check with no scene path. Inventing a translation would be worse
/// than admitting there isn't one, because a gate that reads plausibly and
/// evaluates differently is not something an author can see.
/// </summary>
public static class VanillaDialogueConditions
{
    /// <summary>
    /// One vanilla condition as an editor condition, or null when it has no
    /// faithful equivalent.
    /// </summary>
    public static NodeConditionDef? Translate(VanillaDialogueCatalog.Step? step)
    {
        var fields = step?.Fields;
        if (step == null || fields == null) return null;

        switch (step.Type)
        {
            case "ConditionMathCompareBooleans": return Boolean(fields);

            // Decimals and integers differ only in what Game Creator calls
            // them; both hold a variable, a comparison and a literal.
            case "ConditionMathCompareIntegers":
            case "ConditionMathCompareDecimals": return Number(fields);

            case "ConditionChance": return Chance(fields);
            case "ConditionGameObjectActive": return Active(fields);
            case "ConditionVisualScriptingConditionsOR": return Either(fields);
            default: return null;
        }
    }

    /// <summary>
    /// Every condition in a list that translates, and a count of those that do
    /// not — so a caller can say "and three others" rather than quietly
    /// showing fewer conditions than the game applies.
    /// </summary>
    public static List<NodeConditionDef> TranslateAll(
        IEnumerable<VanillaDialogueCatalog.Step>? steps, out int untranslated)
    {
        var done = new List<NodeConditionDef>();
        untranslated = 0;
        if (steps == null) return done;

        foreach (var step in steps)
        {
            var one = Translate(step);
            if (one == null) untranslated++;
            else done.Add(one);
        }
        return done;
    }

    // ── the six ──────────────────────────────────────────────────────

    private static NodeConditionDef? Boolean(JObject fields)
    {
        string? name = VariableName(fields["m_Value"]);
        if (name == null) return null;
        if (!Right(fields["m_CompareTo"], out string value, out bool vanillaValue)) return null;

        // Every boolean comparison in the game is Equals; anything else would
        // be a shape this has not seen, and guessing at it is how a gate ends
        // up inverted.
        string comparison = (string?)fields["m_Comparison"] ?? "Equals";
        if (comparison != "Equals" && comparison != "Different") return null;

        var made = new NodeConditionDef
        {
            Type = NodeConditionTypes.VariableCompare,
            Params = new Dictionary<string, string>
            {
                ["name"] = name, ["value"] = value, ["source"] = "vanilla",
                ["comparison"] = "equals",
            },
            Negate = comparison == "Different",
        };
        if (vanillaValue) made.Params["valueSource"] = "vanilla";
        return made;
    }

    private static NodeConditionDef? Number(JObject fields)
    {
        string? name = VariableName(fields["m_Value"]);
        var compare = fields["m_CompareTo"] as JObject;
        if (name == null || compare == null) return null;

        if (!Right(compare["m_CompareTo"], out string value, out bool vanillaValue)) return null;

        // The game's operator, as the field the merged condition carries.
        string comparison = (string?)compare["m_Comparison"] ?? "";
        string how;
        bool negate = false;
        switch (comparison)
        {
            case "Equals": how = "equals"; break;
            case "Different": how = "equals"; negate = true; break;
            case "Greater": how = "greater than"; break;
            case "GreaterOrEqual": how = "greater or equal"; break;
            case "Less": how = "less than"; break;
            case "LessOrEqual": how = "less or equal"; break;
            default: return null;
        }

        var made = new NodeConditionDef
        {
            Type = NodeConditionTypes.VariableCompare,
            Params = new Dictionary<string, string>
            {
                ["name"] = name, ["value"] = value, ["source"] = "vanilla",
                ["comparison"] = how,
            },
            Negate = negate,
        };
        if (vanillaValue) made.Params["valueSource"] = "vanilla";
        return made;
    }

    /// <summary>
    /// Game Creator's OR, which the editor already has as a group.
    /// <para/>
    /// All or nothing: a group missing one branch is a STRICTER gate than the
    /// game applies, so one child that does not translate refuses the whole
    /// thing rather than quietly narrowing it.
    /// </summary>
    private static NodeConditionDef? Either(JObject fields)
    {
        var inner = fields["m_Conditions"]?["conditions"] as JArray;
        if (inner == null || inner.Count == 0) return null;

        var children = new List<NodeConditionDef>();
        foreach (var child in inner)
        {
            var step = child.ToObject<VanillaDialogueCatalog.Step>();
            var one = Translate(step);
            if (one == null) return null;
            children.Add(one);
        }

        return new NodeConditionDef
        {
            Type = NodeConditionTypes.GroupAny,
            Conditions = children,
        };
    }

    private static NodeConditionDef? Chance(JObject fields)
    {
        string? threshold = Literal(fields["m_Threshold"]);
        if (threshold == null) return null;

        // Game Creator rolls this on every evaluation, and so does Random —
        // which is why that type is deprecated for authoring but still exactly
        // right for describing what the game does.
        return new NodeConditionDef
        {
            Type = NodeConditionTypes.Random,
            Params = new Dictionary<string, string> { ["chance"] = threshold },
        };
    }

    private static NodeConditionDef? Active(JObject fields)
    {
        var target = fields["m_GameObject"] as JObject;
        string? path = (string?)target?["path"];
        if (string.IsNullOrEmpty(path)) return null;   // a name alone is not an address

        return new NodeConditionDef
        {
            Type = NodeConditionTypes.GameObjectActive,
            Params = new Dictionary<string, string>
            {
                // Named the way the editor names one today. "path" is the
                // spelling it migrates AWAY from, and seeding the old one meant
                // every seeded line was rewritten the instant it was shown -
                // which reads as the author having changed it.
                ["kind"] = "Direct Path", ["target"] = path!,
            },
        };
    }

    // ── reading one side ─────────────────────────────────────────────

    /// <summary>The variable a side of the comparison reads, by name — which is
    /// how the runtime addresses a global, the owning list being display
    /// only.</summary>
    private static string? VariableName(JToken? side)
    {
        if (side is not JObject holder) return null;
        if ((string?)holder["kind"] != "variable") return null;

        var variable = holder["variable"] as JObject;
        if ((string?)variable?["scope"] != "global") return null;

        string? name = (string?)variable["name"];
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// The right-hand side of a comparison: a literal, or a reference to
    /// another variable.
    /// <para/>
    /// The game compares one global against another in a few places
    /// ("If Mainstory[MLove] &gt; Mainstory[MCorruption]"), and the runtime
    /// resolves a <c>${name}</c> in a value before comparing — so that is what
    /// those become, with <c>valueSource</c> saying which store to read. Braced
    /// rather than bare because the braces say exactly where the name ends, and
    /// these names are the game's rather than ours to vouch for.
    /// </summary>
    private static bool Right(JToken? side, out string value, out bool vanilla)
    {
        value = "";
        vanilla = false;

        string? literal = Literal(side);
        if (literal != null) { value = literal; return true; }

        string? name = VariableName(side);
        if (name == null) return false;

        value = "${" + name + "}";
        vanilla = true;
        return true;
    }

    /// <summary>The literal a side of the comparison holds, written the way the
    /// runtime's own comparer reads it back.</summary>
    private static string? Literal(JToken? side)
    {
        if (side is not JObject holder) return null;
        if ((string?)holder["kind"] != "value") return null;

        var value = holder["value"];
        if (value == null || value.Type == JTokenType.Null) return null;

        switch (value.Type)
        {
            case JTokenType.Boolean:
                return value.Value<bool>() ? "true" : "false";
            case JTokenType.Integer:
            case JTokenType.Float:
                return value.Value<double>().ToString(CultureInfo.InvariantCulture);
            case JTokenType.String:
                return value.Value<string>();
            default:
                return null;
        }
    }
}
