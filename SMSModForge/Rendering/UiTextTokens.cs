using System;
using System.Text.RegularExpressions;

namespace SMSModForge.Rendering;

/// <summary>
/// Showing a label that names a variable as something an author can read.
/// <para/>
/// A UI label may carry <c>[PV:name]</c>, the same token dialogue lines and
/// button labels use, and at runtime it follows that variable. In the editor
/// there is no running game, so the honest thing to draw is the variable's
/// declared default - which is what it will hold the first time a player sees
/// the screen. Drawing the raw token instead makes every label that uses one
/// look like a mistake, and makes the layout around it wrong: "[PV:PlayerName]"
/// is nineteen characters where "Anna" is four.
/// <para/>
/// The lookup is set once by whoever owns the pack, rather than threaded
/// through the renderer, because it is presentation state for the preview and
/// nothing below here has any business knowing what a pack variable is. Unset,
/// tokens are left exactly as written.
/// </summary>
public static class UiTextTokens
{
    private static readonly Regex Token =
        new(@"\[PV:([^\]]+)\]", RegexOptions.Compiled);

    /// <summary>Name to the value the preview should show, or null to leave the
    /// token alone. Set by the editor when a pack is opened.</summary>
    public static Func<string, string?>? Lookup { get; set; }

    /// <summary>Cheap enough to call on every label: a substring test before
    /// the regex.</summary>
    public static bool HasAny(string? text)
        => !string.IsNullOrEmpty(text)
        && text!.IndexOf("[PV:", StringComparison.Ordinal) >= 0;

    /// <summary>
    /// The text as an author should see it drawn.
    /// <para/>
    /// A token naming a variable the pack does not declare is left verbatim, on
    /// purpose: that is a typo, and the whole point of leaving it visible is
    /// that it stays visible.
    /// </summary>
    public static string Resolve(string? text)
    {
        if (!HasAny(text)) return text ?? "";

        var lookup = Lookup;
        if (lookup == null) return text!;

        return Token.Replace(text!, match =>
        {
            string name = match.Groups[1].Value.Trim();
            string? value = null;
            try { value = lookup(name); }
            catch { /* a broken lookup must not stop the preview drawing */ }
            return value ?? match.Value;
        });
    }
}
