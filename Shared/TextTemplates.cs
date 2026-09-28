using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SMSModForge.Shared
{
    /// <summary>
    /// A set of texts, each of which may have places filled in only when it is
    /// shown - <c>{PCName}</c>, which the game fills with the player's name, and
    /// <c>[PV:name]</c>, which ModForge fills with a pack variable - and the
    /// question: is this text on screen one of them?
    /// <para/>
    /// A text without such places is looked up whole. One with them matches any
    /// text that has the same words around them, whatever fills them.
    /// </summary>
    public sealed class TextTemplates
    {
        /// <summary>A place in a text filled when it is shown.</summary>
        private static readonly Regex Place = new Regex(@"\{[^{}]+\}|\[PV:[^\]]+\]", RegexOptions.CultureInvariant);

        private readonly HashSet<string> _whole = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _withPlaces = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Regex> _patterns = new List<Regex>();

        public int Count
        {
            get { return _whole.Count + _withPlaces.Count; }
        }

        public void Clear()
        {
            _whole.Clear();
            _withPlaces.Clear();
            _patterns.Clear();
        }

        public void Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!Place.IsMatch(text))
            {
                _whole.Add(text);
                return;
            }
            if (!_withPlaces.Add(text)) return;

            var pattern = new StringBuilder("^");
            int at = 0;
            foreach (Match place in Place.Matches(text))
            {
                pattern.Append(Regex.Escape(text.Substring(at, place.Index - at)));
                pattern.Append(@"[\s\S]*?");
                at = place.Index + place.Length;
            }
            pattern.Append(Regex.Escape(text.Substring(at))).Append('$');
            _patterns.Add(new Regex(pattern.ToString(), RegexOptions.CultureInvariant));
        }

        /// <summary>Whether <paramref name="shown"/> is one of the texts, with
        /// its places filled or not.</summary>
        public bool Contains(string shown)
        {
            if (string.IsNullOrEmpty(shown)) return false;
            if (_whole.Contains(shown) || _withPlaces.Contains(shown)) return true;
            foreach (var pattern in _patterns)
                if (pattern.IsMatch(shown)) return true;
            return false;
        }
    }
}
