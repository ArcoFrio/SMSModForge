using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Which installed packs the player has switched off, from the checkboxes
    /// on the game's main menu.
    /// <para/>
    /// Stored as the packs that are OFF rather than the ones that are on, so a
    /// pack installed tomorrow is on without anybody having to tick it - which
    /// is what installing a pack has always meant. One list for every save:
    /// the menu comes before a save is chosen.
    /// <para/>
    /// Compiled into the plugin, which keeps the setting, and into the editor's
    /// tests, which check the rule.
    /// </summary>
    public static class PackSwitches
    {
        /// <summary>Between two pack ids in the setting.</summary>
        public const char Separator = ',';

        /// <summary>The pack ids a setting switches off, whatever their case.</summary>
        public static HashSet<string> Off(string setting)
        {
            var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (setting ?? "").Split(Separator))
            {
                string id = part.Trim();
                if (id.Length > 0) off.Add(id);
            }
            return off;
        }

        /// <summary>Whether <paramref name="packId"/> is on under <paramref name="setting"/>.</summary>
        public static bool IsOn(string setting, string packId)
        {
            if (string.IsNullOrEmpty(packId)) return true;
            return !Off(setting).Contains(packId.Trim());
        }

        /// <summary>
        /// The setting with <paramref name="packId"/> switched on or off, every
        /// other pack left as it was. Sorted, so the same choices always write
        /// the same line into the config file.
        /// </summary>
        public static string With(string setting, string packId, bool on)
        {
            var off = Off(setting);
            string id = (packId ?? "").Trim();
            if (id.Length > 0)
            {
                // A separator in an id would split it in two when read back.
                id = id.Replace(Separator.ToString(), "");
                if (on) off.Remove(id);
                else off.Add(id);
            }
            return string.Join(Separator + " ", off.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }
}
