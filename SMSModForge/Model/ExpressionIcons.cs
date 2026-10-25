using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// The pictures a face can be shown with in the editor (the author, 1.8.0:
/// "find/make some very basic ones that cover all human facial expressions you
/// can think of"). Drawn by the editor itself (View.Controls.ExpressionIconView),
/// in the theme's colours - no borrowed art.
/// <para/>
/// A face of the pack's own keeps the one its author picks, in the manifest
/// (<see cref="ActorExpressionDef.Icon"/>); until then, and for every face of
/// the game's, the picture is guessed from the face's name, which is also what
/// a new face is given when its name is first set.
/// </summary>
public static class ExpressionIcons
{
    /// <summary>Every icon, in the order the picker offers them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "neutral", "happy", "smile", "laugh", "love", "flirty", "smug", "talk",
        "sad", "cry", "angry", "furious", "pout", "disgusted",
        "surprised", "shocked", "scared", "nervous", "embarrassed", "shy",
        "confused", "thinking", "determined", "bored", "sleepy", "pained",
    };

    /// <summary>Words that mean each icon, as a face's name would contain
    /// them - the first match wins, so the more particular come first.</summary>
    private static readonly (string Icon, string[] Words)[] Meanings =
    {
        ("neutral", new[] { "neutral", "normal", "default", "idle", "blank", "calm", "none" }),
        ("furious", new[] { "furious", "rage", "livid", "enrag" }),
        ("laugh", new[] { "laugh", "giggl", "lol", "haha", "chuckl" }),
        ("cry", new[] { "cry", "tear", "sob", "weep" }),
        ("love", new[] { "love", "heart", "adore", "crush", "smitten" }),
        ("flirty", new[] { "flirt", "wink", "tease", "seduc", "sultry", "lewd", "horny", "naughty", "aroused" }),
        ("smug", new[] { "smug", "smirk", "proud", "cocky", "sly", "mischiev", "confident" }),
        ("embarrassed", new[] { "embarrass", "blush", "flustered", "awkward" }),
        ("shy", new[] { "shy", "timid", "bashful" }),
        ("shocked", new[] { "shock", "gasp", "stunned", "horrif" }),
        ("surprised", new[] { "surpris", "wow", "amazed", "astonish" }),
        ("scared", new[] { "scare", "fear", "afraid", "terrif", "frighten", "panic" }),
        ("nervous", new[] { "nervous", "anxious", "worr", "sweat", "uneasy" }),
        ("disgusted", new[] { "disgust", "gross", "repuls", "yuck" }),
        ("pout", new[] { "pout", "sulk", "huff", "grump" }),
        ("angry", new[] { "angry", "anger", "mad", "annoy", "irritat", "upset", "frown" }),
        ("sad", new[] { "sad", "unhappy", "sorrow", "down", "gloom", "hurt" }),
        ("pained", new[] { "pain", "ouch", "wince", "grimace" }),
        ("confused", new[] { "confus", "puzzl", "huh", "unsure", "doubt" }),
        ("thinking", new[] { "think", "ponder", "hmm", "curious", "wonder" }),
        ("determined", new[] { "determin", "serious", "focus", "resolute", "stern" }),
        ("bored", new[] { "bored", "meh", "unimpress", "annoyed" }),
        ("sleepy", new[] { "sleep", "tired", "yawn", "drowsy", "exhaust" }),
        ("talk", new[] { "talk", "speak", "say", "chat" }),
        ("smile", new[] { "smile", "grin", "content", "pleased", "soft" }),
        ("happy", new[] { "happy", "joy", "glad", "cheer", "excit", "delight" }),
    };

    /// <summary>The icon a face named <paramref name="key"/> most likely
    /// wants; neutral when its name says nothing about it.</summary>
    public static string Guess(string? key)
    {
        string name = (key ?? "").Trim().ToLowerInvariant();
        if (name.Length == 0) return "neutral";
        foreach (var (icon, words) in Meanings)
            if (words.Any(w => name.Contains(w, StringComparison.Ordinal)))
                return icon;
        return "neutral";
    }

    /// <summary>The text an icon is called by, in the language files.</summary>
    public static string NameKey(string icon) => "expressionIcon." + icon;

    /// <summary>Whether <paramref name="icon"/> is one of ours.</summary>
    public static bool IsKnown(string? icon) => icon != null && All.Contains(icon);

    /// <summary>Names to suggest while a face's name is typed: one plain word
    /// per icon, neutral left out (every character has it already).</summary>
    public static IReadOnlyList<string> SuggestedNames { get; } = All.Where(i => i != "neutral").ToArray();
}
