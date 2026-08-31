using System.Collections.Generic;
using System.Linq;

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
/// <param name="Label">What the author reads. Free to be plain English.</param>
/// <param name="Group">
/// The dropdown heading it sits under. Also where the keyboard-layout warning
/// lives: the groups whose position moves between layouts say so in the
/// heading, which is the one place an author cannot miss it.
/// </param>
public readonly record struct InputKeyOption(string Token, string Label, string Group)
{
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
    public const string GroupMouse = "Mouse";
    public const string GroupCommon = "Common keys";
    public const string GroupArrows = "Arrows";
    public const string GroupModifiers = "Modifiers";
    public const string GroupFunction = "Function keys";
    public const string GroupNumpad = "Numpad";
    public const string GroupLetters = "Letters — position varies by layout";
    public const string GroupDigits = "Number row — position varies by layout";
    public const string GroupPunctuation = "Punctuation — position varies by layout";

    /// <summary>Group order in the dropdown: safe-everywhere first.</summary>
    public static readonly string[] GroupOrder =
    {
        GroupMouse, GroupCommon, GroupArrows, GroupModifiers,
        GroupFunction, GroupNumpad, GroupLetters, GroupDigits, GroupPunctuation,
    };

    /// <summary>Device filter shown above the key picker.</summary>
    public const string DeviceKeyboard = "Keyboard";
    public const string DeviceMouse = "Mouse";

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
        _all.Where(o => (o.Group == GroupMouse) == (device == DeviceMouse));

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
        Add("Mouse0", "Left mouse button", GroupMouse);
        Add("Mouse1", "Right mouse button", GroupMouse);
        Add("Mouse2", "Middle mouse button", GroupMouse);
        Add("Mouse3", "Mouse button 4 (back)", GroupMouse);
        Add("Mouse4", "Mouse button 5 (forward)", GroupMouse);

        Add("Space", "Space", GroupCommon);
        Add("Return", "Enter", GroupCommon);
        Add("Escape", "Escape", GroupCommon);
        Add("Tab", "Tab", GroupCommon);
        Add("Backspace", "Backspace", GroupCommon);
        Add("Delete", "Delete", GroupCommon);
        Add("Insert", "Insert", GroupCommon);
        Add("Home", "Home", GroupCommon);
        Add("End", "End", GroupCommon);
        Add("PageUp", "Page Up", GroupCommon);
        Add("PageDown", "Page Down", GroupCommon);

        Add("UpArrow", "Up arrow", GroupArrows);
        Add("DownArrow", "Down arrow", GroupArrows);
        Add("LeftArrow", "Left arrow", GroupArrows);
        Add("RightArrow", "Right arrow", GroupArrows);

        Add("LeftShift", "Left Shift", GroupModifiers);
        Add("RightShift", "Right Shift", GroupModifiers);
        Add("LeftControl", "Left Ctrl", GroupModifiers);
        Add("RightControl", "Right Ctrl", GroupModifiers);
        Add("LeftAlt", "Left Alt", GroupModifiers);
        Add("RightAlt", "Right Alt (AltGr)", GroupModifiers);

        for (int i = 1; i <= 12; i++) Add("F" + i, "F" + i, GroupFunction);

        for (int i = 0; i <= 9; i++)
            Add("Keypad" + i, "Numpad " + i, GroupNumpad);
        Add("KeypadPeriod", "Numpad .", GroupNumpad);
        Add("KeypadDivide", "Numpad /", GroupNumpad);
        Add("KeypadMultiply", "Numpad *", GroupNumpad);
        Add("KeypadMinus", "Numpad -", GroupNumpad);
        Add("KeypadPlus", "Numpad +", GroupNumpad);
        Add("KeypadEnter", "Numpad Enter", GroupNumpad);

        for (char c = 'A'; c <= 'Z'; c++)
            Add(c.ToString(), c.ToString(), GroupLetters);

        for (int i = 0; i <= 9; i++)
            Add("Alpha" + i, i.ToString(), GroupDigits);

        Add("Minus", "-  minus", GroupPunctuation);
        Add("Equals", "=  equals", GroupPunctuation);
        Add("LeftBracket", "[  left bracket", GroupPunctuation);
        Add("RightBracket", "]  right bracket", GroupPunctuation);
        Add("Backslash", @"\  backslash", GroupPunctuation);
        Add("Semicolon", ";  semicolon", GroupPunctuation);
        Add("Quote", "'  apostrophe", GroupPunctuation);
        Add("Comma", ",  comma", GroupPunctuation);
        Add("Period", ".  period", GroupPunctuation);
        Add("Slash", "/  slash", GroupPunctuation);
        Add("BackQuote", "`  backtick", GroupPunctuation);

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
        Pressed => "True once, the moment the key goes down.",
        Down => "True the whole time the key is held.",
        Released => "True once, the moment the key comes back up.",
        Up => "True the whole time the key is NOT held.",
        _ => "",
    };
}
