namespace SMSModForge.Model;

/// <summary>
/// What kind of value a variable holds, in the words an author uses rather
/// than the words a compiler does.
/// </summary>
public enum VariableKind
{
    /// <summary>Nothing here knows. A variable the pack does not declare, a
    /// vanilla name the game does not have, or a name nobody has typed yet.</summary>
    Unknown,

    /// <summary>A flag. Compared and written with a tick box, never by typing
    /// the word.</summary>
    YesNo,

    /// <summary>Any number. Whole or fractional is a distinction the runtime
    /// makes and an author does not — both compare and increment the same way,
    /// so both are said the same way.</summary>
    Number,

    /// <summary>Words.</summary>
    Text,

    /// <summary>An ordered list of strings, added to and taken from.</summary>
    List,

    /// <summary>A place in the world. One vanilla variable is one of these; no
    /// pack variable can be.</summary>
    Position,
}

/// <summary>
/// What kind a variable is, whichever side it comes from.
/// <para/>
/// A condition and an action both need this and neither can reach the pack on
/// its own, so it lives here rather than in either. Two questions are answered
/// from it: which editor belongs in the Value cell — a tick box for a flag, a
/// text field otherwise — and the small note beside the name that says what
/// the author is looking at.
/// <para/>
/// <b>Why a note at all.</b> A condition compares STRINGS at runtime, so
/// <c>True</c> against a variable holding <c>true</c> simply does not match,
/// and nothing says why. The tick box fixes that where the kind is known; the
/// note is what makes the kind known to the person, so an unexpected text box
/// reads as "the editor does not know this variable" rather than as
/// "this variable is text".
/// </summary>
public static class VariableTypes
{
    /// <summary>One of the pack's own.</summary>
    public static VariableKind Of(PackVariableType type) => type switch
    {
        PackVariableType.Bool => VariableKind.YesNo,
        PackVariableType.Int => VariableKind.Number,
        PackVariableType.Float => VariableKind.Number,
        PackVariableType.String => VariableKind.Text,
        PackVariableType.List => VariableKind.List,
        _ => VariableKind.Unknown,
    };

    /// <summary>
    /// One of the game's, from the type its dump recorded.
    /// <para/>
    /// <c>null</c> is a real answer in that dump and not a missing one: it is
    /// what a GC2 global reads as before anything has written to it, so the
    /// game does not know its kind either until a save does. Unknown is the
    /// honest reading.
    /// </summary>
    public static VariableKind OfVanilla(string? declared) => (declared ?? "").Trim() switch
    {
        "Boolean" => VariableKind.YesNo,
        "Double" => VariableKind.Number,
        "Single" => VariableKind.Number,
        "Int32" => VariableKind.Number,
        "String" => VariableKind.Text,
        "Vector3" => VariableKind.Position,
        _ => VariableKind.Unknown,
    };

    /// <summary>What to call it beside the name. Empty when nothing here
    /// knows, because a label reading "unknown" is a label that makes the
    /// editor look broken rather than uninformed.</summary>
    public static string Label(VariableKind kind) => kind switch
    {
        VariableKind.YesNo => "yes/no",
        VariableKind.Number => "number",
        VariableKind.Text => "text",
        VariableKind.List => "list",
        VariableKind.Position => "position",
        _ => "",
    };

    /// <summary>
    /// The kind of a named variable on one side or the other.
    /// <para/>
    /// The pack side needs the loaded pack, which this cannot see, so the
    /// caller passes the lookup in. The game side is a shipped catalogue and
    /// is answered here.
    /// </summary>
    public static VariableKind Of(string? name, bool vanilla,
                                  System.Func<string, PackVariableType?>? packLookup)
    {
        if (string.IsNullOrWhiteSpace(name)) return VariableKind.Unknown;

        if (vanilla) return OfVanilla(VanillaGameVariables.TypeOf(name));

        var declared = packLookup?.Invoke(name!);
        return declared == null ? VariableKind.Unknown : Of(declared.Value);
    }
}
