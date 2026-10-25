using System;
using System.Text;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// How a record is named in its tab's list (the author, 1.8.0).
/// <para/>
/// The runtime name in brackets only when it says something the display name
/// does not: "AnisAffection01Scene01 (AnisAffection01Scene01)" was the same
/// name twice on most rows, and it was what got cut off.
/// </summary>
public static class ListNames
{
    /// <summary>
    /// <paramref name="display"/>, with <paramref name="key"/> after it in
    /// brackets only when the key is not simply the display name run together
    /// (<see cref="CharacterDef.Identifier"/>) or the same words. The key alone
    /// when there is no display name.
    /// </summary>
    public static string Of(string? display, string? key)
    {
        display = display?.Trim() ?? "";
        key ??= "";
        if (display.Length == 0) return key;
        if (key.Length == 0 || Same(display, key)) return display;
        return display + " (" + key + ")";
    }

    private static bool Same(string display, string key)
        => string.Equals(display, key, StringComparison.OrdinalIgnoreCase)
           || string.Equals(CharacterDef.Identifier(display), key, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A runtime name spelled out as words, for the Places list (the author,
    /// 1.8.0: display names there can be far more generic - HarborHomeBedroom
    /// is just "Bedroom"): "HarborHomeBedroom" reads "Harbor Home Bedroom",
    /// "MountainLabCorridorNikke1" "Mountain Lab Corridor Nikke 1", and an
    /// underscore is a space.
    /// </summary>
    public static string Spaced(string? key)
    {
        var words = new StringBuilder();
        string text = (key ?? "").Replace('_', ' ').Trim();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (i > 0 && words.Length > 0 && words[words.Length - 1] != ' ')
            {
                char before = text[i - 1];
                bool next = i + 1 < text.Length && char.IsLower(text[i + 1]);
                bool startsWord = (char.IsUpper(c) && (char.IsLower(before) || char.IsDigit(before) || (char.IsUpper(before) && next)))
                                  || (char.IsDigit(c) && char.IsLetter(before))
                                  || (char.IsLetter(c) && char.IsDigit(before));
                if (startsWord) words.Append(' ');
            }
            if (c == ' ' && words.Length > 0 && words[words.Length - 1] == ' ') continue;
            words.Append(c);
        }
        return words.ToString();
    }
}
