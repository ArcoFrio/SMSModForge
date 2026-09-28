using System;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The game's own words in braces - {PC} the player's name, {M} what they
    /// call their mother, and the rest - filled in wherever a pack's text is
    /// shown, not only in dialogue.
    /// <para/>
    /// They are Game Creator's dialogue values: a list in its dialogue
    /// settings, each a key and a string, which it puts in place of "{key}" in
    /// a line just before the line is shown (<c>NodeText.Get</c>, read out of
    /// the game's assembly, 2026-09-27). Only a line of dialogue ever went
    /// through that, so a quest titled "{PC}'s first day", a button, a screen
    /// or a speaker's name showed the braces as typed. This does the same
    /// replacement, from the same list, dressed the same way - so every word
    /// the game defines works, not only the ones anybody has listed.
    /// </summary>
    internal static class GameTextTokens
    {
        /// <summary>Whether <paramref name="text"/> might hold one - worth the
        /// look. A brace is all it takes; what the braces hold is looked up.</summary>
        public static bool MayHave(string text)
            => !string.IsNullOrEmpty(text) && text.IndexOf('{') >= 0;

        /// <summary>
        /// <paramref name="text"/> with every "{key}" the game defines put in
        /// its place - bold, italic or coloured where the game's settings say
        /// so, as a line of dialogue would have it. A word in braces the game
        /// does not define is left as it was written.
        /// </summary>
        public static string Resolve(string text)
        {
            if (!MayHave(text)) return text;
            Value[] values;
            try { values = TRepository<DialogueRepository>.Get.Values.Get; }
            catch (Exception) { return text; }
            if (values == null) return text;

            foreach (var value in values)
            {
                if (value == null || string.IsNullOrEmpty(value.Key)) continue;
                string token = "{" + value.Key + "}";
                if (text.IndexOf(token, StringComparison.Ordinal) < 0) continue;

                string shown;
                try { shown = value.GetText(Args.EMPTY) ?? ""; }
                catch (Exception) { continue; }
                if (value.InBold) shown = "<b>" + shown + "</b>";
                if (value.InItalic) shown = "<i>" + shown + "</i>";
                if (value.UseColor)
                {
                    try { shown = "<color=#" + ColorUtility.ToHtmlStringRGBA(value.GetColor(Args.EMPTY)) + ">" + shown + "</color>"; }
                    catch (Exception) { }
                }
                text = text.Replace(token, shown);
            }
            return text;
        }
    }

    /// <summary>
    /// A pack's text held where the game reads a string from - a quest's title,
    /// a task's name, a speaker's name - that fills in its tokens every time
    /// the game reads it: the game's words in braces (<see cref="GameTextTokens"/>)
    /// and the pack's <c>[PV:name]</c>.
    /// <para/>
    /// Every time, not once: the player's name is the loaded save's, and a
    /// pack variable changes as the game is played. The journal reads a quest's
    /// title each time it draws it, so the title follows.
    /// </summary>
    [Serializable]
    internal sealed class GetStringPackText : PropertyTypeGetString
    {
        private readonly string _raw;
        private readonly string _packId;

        public GetStringPackText(string raw, string packId)
        {
            _raw = raw ?? "";
            _packId = packId ?? "";
        }

        public override string String => _raw;
        public override string EditorValue => _raw;

        public override string Get(Args args)
            => TextPlaceholders.ResolveAll(_raw, _packId.Length == 0 ? null : Plugin.TryGetPackVars(_packId));

        public override string Get(GameObject gameObject) => Get(Args.EMPTY);

        /// <summary>
        /// What to hand the game for <paramref name="text"/>: one that fills
        /// in its tokens when it has any, and the plain string - what it was
        /// always given - when it has none.
        /// </summary>
        public static PropertyGetString For(string text, string packId)
            => TextPlaceholders.HasAnyToken(text)
                ? new PropertyGetString(new GetStringPackText(text, packId))
                : new PropertyGetString(text ?? "");
    }
}
