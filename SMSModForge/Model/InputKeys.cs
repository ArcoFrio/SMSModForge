using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;

namespace SMSModForge.Model;

/// <summary>
/// One key or button an author can gate on, as the editor offers it.
/// </summary>
/// <param name="Token">
/// What is written to the manifest, and what the runtime resolves. These are
/// Unity <c>KeyCode</c> names verbatim — <c>Space</c>, <c>Mouse0</c>,
/// <c>Alpha1</c> — because the runtime parses them straight back into the enum
/// and a friendlier spelling here would mean a translation table on both sides.
/// </param>
/// <param name="Name">What the author reads: a key into the English file,
/// or, for a key whose cap says it all ("F1", "A"), just that.</param>
/// <param name="GroupKey">
/// The dropdown heading it sits under, as a key. Also where the keyboard-layout
/// warning lives: the groups whose position moves between layouts say so in the
/// heading, which is the one place an author cannot miss it.
/// </param>
public readonly record struct InputKeyOption(string Token, string Name, string GroupKey)
{
    public string Label => Name.StartsWith("input.", System.StringComparison.Ordinal) ? Loc.T(Name) : Name;

    public string Group => Loc.T(GroupKey);

    /// <summary>Shown by the ComboBox when the row is collapsed.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// The keys and buttons offered on the <see cref="NodeConditionTypes.InputKey"/>
/// condition.
/// <para/>
/// Curated rather than generated from <c>KeyCode</c>. That enum carries about
/// 320 members — joystick axes, IME keys, obsolete duplicates — and a list of
/// that length is not something anybody picks from. What is here is what a
/// player's hands are actually on.
/// <para/>
/// <b>On keyboard layouts.</b> Unity reports these by POSITION, not by the
/// letter printed on the cap: <c>KeyCode.A</c> is the key where A sits on a US
/// keyboard, which is Q on AZERTY. Nothing can be done about that from a pack,
/// so the lists below name it instead — the groups that move are labelled as
/// moving, and the ones that do not (mouse, arrows, space, modifiers, function
/// keys, numpad) come first, because those are the safe choices for anything
/// a player must be able to press.
/// </summary>
public static class InputKeys
{
    public const string GroupMouse = "input.group.mouse";
    public const string GroupCommon = "input.group.common";
    public const string GroupArrows = "input.group.arrows";
    public const string GroupModifiers = "input.group.modifiers";
    public const string GroupFunction = "input.group.function";
    public const string GroupNumpad = "input.group.numpad";
    public const string GroupLetters = "input.group.letters";
    public const string GroupDigits = "input.group.digits";
    public const string GroupPunctuation = "input.group.punctuation";

    /// <summary>Group order in the dropdown: safe-everywhere first.</summary>
    public static readonly string[] GroupOrder =
    {
        GroupMouse, GroupCommon, GroupArrows, GroupModifiers,
        GroupFunction, GroupNumpad, GroupLetters, GroupDigits, GroupPunctuation,
    };

    /// <summary>Device filter shown above the key picker.</summary>
    public const string DeviceKeyboard = "input.device.keyboard";
    public const string DeviceMouse = "input.device.mouse";

    public static readonly IReadOnlyList<string> Devices =
        new[] { DeviceKeyboard, DeviceMouse };

    private static readonly List<InputKeyOption> _all = Build();

    public static IReadOnlyList<InputKeyOption> All => _all;

    /// <summary>Which device a token belongs to, so the editor can preselect the
    /// filter for a condition it is loading rather than guessing keyboard.</summary>
    public static string DeviceOf(string token) =>
        !string.IsNullOrEmpty(token) && token.StartsWith("Mouse")
            ? DeviceMouse : DeviceKeyboard;

    /// <summary>The options for one device, in group order.</summary>
    public static IEnumerable<InputKeyOption> For(string device) =>
        _all.Where(o => (o.GroupKey == GroupMouse) == (device == DeviceMouse));

    /// <summary>The author-facing label for a stored token, or the token itself
    /// when a pack names something this list does not carry (hand-edited
    /// manifests are allowed to, and the runtime will still resolve it).</summary>
    public static string LabelOf(string token)
    {
        foreach (var o in _all) if (o.Token == token) return o.Label;
        return token ?? "";
    }

    public static bool IsKnown(string token) => _all.Any(o => o.Token == token);

    private static List<InputKeyOption> Build()
    {
        var list = new List<InputKeyOption>();

        void Add(string token, string label, string group) =>
            list.Add(new InputKeyOption(token, label, group));

        // Mouse. KeyCode covers these, so a mouse button needs no separate
        // param or code path at runtime — only its own place in the picker.
        Add("Mouse0", "input.key.mouse0", GroupMouse);
        Add("Mouse1", "input.key.mouse1", GroupMouse);
        Add("Mouse2", "input.key.mouse2", GroupMouse);
        Add("Mouse3", "input.key.mouse3", GroupMouse);
        Add("Mouse4", "input.key.mouse4", GroupMouse);

        Add("Space", "input.key.space", GroupCommon);
        Add("Return", "input.key.return", GroupCommon);
        Add("Escape", "input.key.escape", GroupCommon);
        Add("Tab", "input.key.tab", GroupCommon);
        Add("Backspace", "input.key.backspace", GroupCommon);
        Add("Delete", "input.key.delete", GroupCommon);
        Add("Insert", "input.key.insert", GroupCommon);
        Add("Home", "input.key.home", GroupCommon);
        Add("End", "input.key.end", GroupCommon);
        Add("PageUp", "input.key.pageUp", GroupCommon);
        Add("PageDown", "input.key.pageDown", GroupCommon);

        Add("UpArrow", "input.key.upArrow", GroupArrows);
        Add("DownArrow", "input.key.downArrow", GroupArrows);
        Add("LeftArrow", "input.key.leftArrow", GroupArrows);
        Add("RightArrow", "input.key.rightArrow", GroupArrows);

        Add("LeftShift", "input.key.leftShift", GroupModifiers);
        Add("RightShift", "input.key.rightShift", GroupModifiers);
        Add("LeftControl", "input.key.leftControl", GroupModifiers);
        Add("RightControl", "input.key.rightControl", GroupModifiers);
        Add("LeftAlt", "input.key.leftAlt", GroupModifiers);
        Add("RightAlt", "input.key.rightAlt", GroupModifiers);

        for (int i = 1; i <= 12; i++) Add("F" + i, "F" + i, GroupFunction);

        for (int i = 0; i <= 9; i++)
            Add("Keypad" + i, "input.key.keypad" + i, GroupNumpad);
        Add("KeypadPeriod", "input.key.keypadPeriod", GroupNumpad);
        Add("KeypadDivide", "input.key.keypadDivide", GroupNumpad);
        Add("KeypadMultiply", "input.key.keypadMultiply", GroupNumpad);
        Add("KeypadMinus", "input.key.keypadMinus", GroupNumpad);
        Add("KeypadPlus", "input.key.keypadPlus", GroupNumpad);
        Add("KeypadEnter", "input.key.keypadEnter", GroupNumpad);

        for (char c = 'A'; c <= 'Z'; c++)
            Add(c.ToString(), c.ToString(), GroupLetters);

        for (int i = 0; i <= 9; i++)
            Add("Alpha" + i, i.ToString(), GroupDigits);

        Add("Minus", "input.key.minus", GroupPunctuation);
        Add("Equals", "input.key.equals", GroupPunctuation);
        Add("LeftBracket", "input.key.leftBracket", GroupPunctuation);
        Add("RightBracket", "input.key.rightBracket", GroupPunctuation);
        Add("Backslash", "input.key.backslash", GroupPunctuation);
        Add("Semicolon", "input.key.semicolon", GroupPunctuation);
        Add("Quote", "input.key.quote", GroupPunctuation);
        Add("Comma", "input.key.comma", GroupPunctuation);
        Add("Period", "input.key.period", GroupPunctuation);
        Add("Slash", "input.key.slash", GroupPunctuation);
        Add("BackQuote", "input.key.backQuote", GroupPunctuation);

        return list;
    }
}

/// <summary>
/// What about the key is being asked. Stored in the condition's <c>phase</c>
/// param.
/// <para/>
/// Two of these are STATES and two are EDGES, and the difference decides where
/// each one is usable. <see cref="Down"/> and <see cref="Up"/> answer "right
/// now", so they are true for as long as they are true. <see cref="Pressed"/>
/// and <see cref="Released"/> answer "just now", so they are true once per
/// press — which only means anything somewhere that is re-checked continuously.
/// </summary>
public static class InputPhases
{
    /// <summary>True once, on the moment the key goes down.</summary>
    public const string Pressed = "Pressed";

    /// <summary>True for every check while the key is held.</summary>
    public const string Down = "Down";

    /// <summary>True once, on the moment the key comes back up.</summary>
    public const string Released = "Released";

    /// <summary>True for every check while the key is not held.</summary>
    public const string Up = "Up";

    public static readonly IReadOnlyList<string> All =
        new[] { Pressed, Down, Released, Up };

    /// <summary>Whether this phase is a one-off moment rather than a state.</summary>
    public static bool IsEdge(string phase) =>
        phase == Pressed || phase == Released;

    /// <summary>One line per phase, for the picker's tooltip.</summary>
    public static string Describe(string phase) => phase switch
    {
        Pressed => Loc.T("input.phase.pressed.tip"),
        Down => Loc.T("input.phase.down.tip"),
        Released => Loc.T("input.phase.released.tip"),
        Up => Loc.T("input.phase.up.tip"),
        _ => "",
    };
}
