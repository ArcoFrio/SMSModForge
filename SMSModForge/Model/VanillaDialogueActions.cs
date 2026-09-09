using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Turns the vanilla game's own instructions into the actions the editor
/// already speaks — the other half of <see cref="VanillaDialogueConditions"/>.
/// <para/>
/// A vanilla conversation does things as well as saying them: it sets the
/// flags that gate everything else, switches busts on, raises the fade signal,
/// waits, and sets the cooldown that stops it repeating. Those run on a node's
/// start and finish, and in the branch that stages the conversation either side
/// of playing it. An author extending a dialogue has to be able to read them
/// beside their own actions, and change them.
/// <para/>
/// Eight instruction types cover the overwhelming majority of what the game
/// does, and those translate exactly. The rest — quests, transforms, particle
/// systems, Game Creator's own Actions assets — have no equivalent here, and
/// say so by returning null rather than by approximating. They are still shown:
/// every instruction carries Game Creator's own one-line description, so
/// "Activate Explore The City" appears in the list as a step the editor cannot
/// edit rather than as a step nobody knows about.
/// </summary>
public static class VanillaDialogueActions
{
    /// <summary>
    /// One vanilla instruction as an editor action, or null when it has no
    /// faithful equivalent.
    /// </summary>
    /// <param name="self">
    /// The object the instruction runs on, for the ones that say "Self". A
    /// start site knows it — the room whose branch plays the conversation — and
    /// every "Set Active Self" in the game is on one, so this resolves exactly
    /// rather than approximately. Absent, those are refused like anything else
    /// that cannot be aimed.
    /// </param>
    public static NodeActionDef? Translate(VanillaDialogueCatalog.Step? step,
                                           string? self = null)
    {
        var fields = step?.Fields;
        if (step == null || fields == null) return null;

        switch (step.Type)
        {
            case "InstructionGameObjectSetActive": return SetActive(fields, self);
            case "InstructionBooleanSetBool": return SetVariable(fields, "m_Set", "m_From");
            case "InstructionArithmeticSetNumber": return SetVariable(fields, "m_Set", "m_From");
            case "InstructionTextSetString": return SetVariable(fields, "m_Set", "m_Text");
            case "InstructionArithmeticIncrementNumber": return Increment(fields);
            case "InstructionCommonTimeWait": return Wait(fields);
            case "InstructionLogicRaiseSignal": return Signal(fields);
            case "InstructionCommonAudioSFXPlay": return Sfx(fields);
            default: return null;
        }
    }

    /// <summary>
    /// One vanilla instruction as an editor action, always — translated where
    /// there is an equivalent, and otherwise kept as a
    /// <see cref="NodeActionTypes.VanillaStep"/> carrying what it is and what
    /// it does.
    /// <para/>
    /// This is what a list shown to an author should be built from.
    /// <see cref="Translate"/> answers "can this be edited"; this answers "what
    /// is there", and those are different questions. A step left out of the
    /// list is one nobody can see, reorder, or delete.
    /// </summary>
    public static NodeActionDef? Represent(VanillaDialogueCatalog.Step? step,
                                           string? self = null)
    {
        if (step == null) return null;

        var translated = Translate(step, self);
        if (translated != null) return translated;

        var made = new NodeActionDef
        {
            Type = NodeActionTypes.VanillaStep,
            Params = new Dictionary<string, string> { ["vanilla"] = step.Type },
        };
        if (!string.IsNullOrEmpty(step.Title)) made.Params["title"] = step.Title!;
        if (step.Fields != null && step.Fields.Count > 0)
            made.Params["details"] = step.Fields.ToString(Newtonsoft.Json.Formatting.None);
        return made;
    }

    /// <summary>
    /// A whole list as the author should see it: every step present, and a
    /// count of how many of them the editor cannot edit.
    /// </summary>
    public static List<NodeActionDef> RepresentAll(
        IEnumerable<VanillaDialogueCatalog.Step>? steps, out int notImplemented,
        string? self = null)
    {
        var done = new List<NodeActionDef>();
        notImplemented = 0;
        if (steps == null) return done;

        foreach (var step in steps)
        {
            var one = Represent(step, self);
            if (one == null) continue;
            if (one.Type == NodeActionTypes.VanillaStep) notImplemented++;
            done.Add(one);
        }
        return done;
    }

    /// <summary>
    /// Every instruction in a list that translates, and a count of those that
    /// do not — so a caller can say "and two others" rather than showing an
    /// author fewer steps than the game runs.
    /// </summary>
    public static List<NodeActionDef> TranslateAll(
        IEnumerable<VanillaDialogueCatalog.Step>? steps, out int untranslated,
        string? self = null)
    {
        var done = new List<NodeActionDef>();
        untranslated = 0;
        if (steps == null) return done;

        foreach (var step in steps)
        {
            var one = Translate(step, self);
            if (one == null) untranslated++;
            else done.Add(one);
        }
        return done;
    }

    // ── the eight ────────────────────────────────────────────────────

    private static NodeActionDef? SetActive(JObject fields, string? self)
    {
        // A path, not a name. Names repeat across the scene, and switching on
        // the wrong object is the kind of mistake that only shows up in play.
        var target = fields["m_GameObject"] as JObject;
        string? path = (string?)target?["path"];

        // "Self" is an address too, once you know where it was written. Every
        // one of these in the game is in a room's branch, and that branch knows
        // which room it is - so this is the room switching itself off at the
        // end of a conversation, not a guess.
        if (string.IsNullOrEmpty(path)
            && (string?)target?["type"] == "GetGameObjectSelf"
            && !string.IsNullOrEmpty(self))
            path = self;

        string? active = Literal(fields["m_Active"]);
        if (string.IsNullOrEmpty(path) || active == null) return null;

        return new NodeActionDef
        {
            Type = NodeActionTypes.SetGameObjectActive,
            Params = new Dictionary<string, string>
            {
                ["kind"] = "Direct Path",
                ["target"] = path!,
                ["active"] = active,
            },
        };
    }

    /// <summary>Writing a value into a global, whichever of the three types it
    /// holds — the shape is the same for a bool, a number and a string.</summary>
    private static NodeActionDef? SetVariable(JObject fields, string into, string from)
    {
        string? name = VariableName(fields[into]);
        if (name == null) return null;
        if (!Source(fields[from], out string value, out bool vanillaValue)) return null;

        var made = new NodeActionDef
        {
            Type = NodeActionTypes.SetVariable,
            Params = new Dictionary<string, string>
            {
                ["name"] = name,
                ["value"] = value,
                ["source"] = "vanilla",
            },
        };
        if (vanillaValue) made.Params["valueSource"] = "vanilla";
        return made;
    }

    private static NodeActionDef? Increment(JObject fields)
    {
        string? name = VariableName(fields["m_Set"]);
        if (name == null) return null;
        if (!Source(fields["m_Value"], out string delta, out bool vanillaValue)) return null;

        var made = new NodeActionDef
        {
            Type = NodeActionTypes.IncrementVariable,
            Params = new Dictionary<string, string>
            {
                ["name"] = name,
                ["delta"] = delta,
                ["source"] = "vanilla",
            },
        };
        if (vanillaValue) made.Params["valueSource"] = "vanilla";
        return made;
    }

    private static NodeActionDef? Wait(JObject fields)
    {
        string? seconds = Literal(fields["m_Seconds"]);
        if (seconds == null) return null;

        return new NodeActionDef
        {
            Type = NodeActionTypes.Wait,
            Params = new Dictionary<string, string> { ["seconds"] = seconds },
        };
    }

    private static NodeActionDef? Signal(JObject fields)
    {
        string? name = (string?)(fields["m_Signal"] as JObject)?["m_String"];
        if (string.IsNullOrEmpty(name)) return null;

        return new NodeActionDef
        {
            Type = NodeActionTypes.EmitSignal,
            Params = new Dictionary<string, string> { ["signal"] = name! },
        };
    }

    private static NodeActionDef? Sfx(JObject fields)
    {
        // The clip is named rather than shipped: PlaySFX looks past the pack's
        // own sounds to the game's, so a vanilla clip name resolves to the one
        // the player already has.
        string? clip = (string?)(fields["m_AudioClip"] as JObject)?["name"];
        if (string.IsNullOrEmpty(clip)) return null;

        var made = new NodeActionDef
        {
            Type = NodeActionTypes.PlaySFX,
            Params = new Dictionary<string, string> { ["clip"] = clip! },
        };

        var volume = fields["m_Config"]?["m_Volume"];
        if (volume != null && (volume.Type == JTokenType.Float || volume.Type == JTokenType.Integer))
            made.Params["volume"] = volume.Value<double>().ToString(CultureInfo.InvariantCulture);

        return made;
    }

    // ── reading one side ─────────────────────────────────────────────

    private static string? VariableName(JToken? side)
    {
        if (side is not JObject holder) return null;
        if ((string?)holder["kind"] != "variable") return null;

        var variable = holder["variable"] as JObject;
        if ((string?)variable?["scope"] != "global") return null;

        string? name = (string?)variable["name"];
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>The value being written: a literal, or another variable read
    /// through a <c>${name}</c> the runtime resolves first.</summary>
    private static bool Source(JToken? side, out string value, out bool vanilla)
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

    private static string? Literal(JToken? side)
    {
        if (side is not JObject holder) return null;
        if ((string?)holder["kind"] != "value") return null;

        var value = holder["value"];
        if (value == null || value.Type == JTokenType.Null) return null;

        switch (value.Type)
        {
            case JTokenType.Boolean: return value.Value<bool>() ? "true" : "false";
            case JTokenType.Integer:
            case JTokenType.Float:
                return value.Value<double>().ToString(CultureInfo.InvariantCulture);
            case JTokenType.String: return value.Value<string>();
            default: return null;
        }
    }
}
