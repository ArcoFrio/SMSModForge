using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The order the installed packs load in, as the player arranges them on
    /// the game's main menu (the author, 1.7.0).
    /// <para/>
    /// Order matters where two packs change the same thing of the game's - the
    /// same character's colour, the same object in one of its levels: the pack
    /// loaded LATER applies over the earlier one, so it is the one that is
    /// seen. Lower in the list wins, which is how it was before there was a
    /// list to arrange: packs loaded in alphabetical order, Z over A.
    /// <para/>
    /// Until somebody drags a pack, the order is alphabetical. Packs installed
    /// after the list was arranged go to the bottom, in alphabetical order
    /// among themselves - a new pack gets its way where it clashes, the way a
    /// newly installed mod does in the usual managers.
    /// <para/>
    /// Not the order dialogues fire in: when two packs' dialogues could start
    /// at once, the higher priority starts, and on a tie the pack first in the
    /// ALPHABET does, as it always has - arranging the list for one pack's
    /// looks must not change which conversation plays.
    /// <para/>
    /// Compiled into the plugin, which keeps the setting, and into the
    /// editor's tests, which check the rule.
    /// </summary>
    public static class PackOrder
    {
        public const char Separator = ',';

        /// <summary>The pack ids a setting lists, in its order, each once.</summary>
        public static List<string> Read(string setting)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (setting ?? "").Split(Separator))
            {
                string id = part.Trim();
                if (id.Length > 0 && seen.Add(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>A setting holding <paramref name="ids"/> in their order.</summary>
        public static string Write(IEnumerable<string> ids)
        {
            var clean = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in ids ?? Enumerable.Empty<string>())
            {
                // A separator in an id would split it in two when read back.
                string id = (raw ?? "").Replace(Separator.ToString(), "").Trim();
                if (id.Length > 0 && seen.Add(id)) clean.Add(id);
            }
            return string.Join(Separator + " ", clean.ToArray());
        }

        /// <summary>
        /// <paramref name="installed"/> in load order: the ones the setting
        /// names in its order, then the rest alphabetically. Ids the setting
        /// names that are not installed are left out - and kept in the
        /// setting, so a pack taken out for a while comes back to its place.
        /// </summary>
        public static List<string> Arrange(IEnumerable<string> installed, string setting)
        {
            var have = new List<string>();
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in installed ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(id) && present.Add(id)) have.Add(id);

            var ordered = new List<string>();
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in Read(setting))
            {
                string match = have.FirstOrDefault(h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase));
                if (match != null && placed.Add(match)) ordered.Add(match);
            }
            foreach (string id in have.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
                if (placed.Add(id)) ordered.Add(id);
            return ordered;
        }

        /// <summary>
        /// The setting after <paramref name="packId"/> is dropped at
        /// <paramref name="index"/> of <paramref name="shown"/> - the order the
        /// list showed when the drag began, 0 the top. Packs the setting
        /// remembers that are not installed keep their places after the rest.
        /// </summary>
        public static string Move(string setting, IList<string> shown, string packId, int index)
        {
            var order = new List<string>(shown ?? new List<string>());
            int from = order.FindIndex(id => string.Equals(id, packId, StringComparison.OrdinalIgnoreCase));
            if (from < 0) return Write(order.Concat(Read(setting)));
            string moving = order[from];
            order.RemoveAt(from);
            // The index is a gap in the list as it was shown, which still had
            // the dragged pack in it: a gap below its old place is one less now.
            if (index > from) index--;
            if (index < 0) index = 0;
            if (index > order.Count) index = order.Count;
            order.Insert(index, moving);

            var remembered = Read(setting).Where(id => !order.Any(o => string.Equals(o, id, StringComparison.OrdinalIgnoreCase)));
            return Write(order.Concat(remembered));
        }

        /// <summary>
        /// Which of two packs' dialogues starts when both could at the same
        /// priority: the pack first in the alphabet. Negative when
        /// <paramref name="a"/> goes first.
        /// </summary>
        public static int DialogueTie(string a, string b)
            => string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
