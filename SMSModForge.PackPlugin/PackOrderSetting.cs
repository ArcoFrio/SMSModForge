using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The order the player put the packs in on the main menu, kept in the
    /// plugin's config file beside the ones switched off. The rule is
    /// <see cref="PackOrder"/>; this only holds the setting.
    /// </summary>
    internal static class PackOrderSetting
    {
        private const string Tag = "[SMSModForge.PackPlugin] Packs: ";
        private static ConfigEntry<string> _order;

        public static void Configure(ConfigFile config)
        {
            _order = config.Bind("Packs", "Order", "",
                "The order packs load in, by pack id, separated by commas - lower in the list wins when two packs "
                + "change the same thing of the game's. Packs not named here come after, alphabetically. Also "
                + "chosen on the main menu, by dragging a pack up or down the list.");
        }

        public static string Setting
        {
            get { return _order == null ? "" : _order.Value ?? ""; }
        }

        /// <summary>The installed packs in load order.</summary>
        public static List<string> Arrange(IEnumerable<string> installed)
        {
            return PackOrder.Arrange(installed, Setting);
        }

        /// <summary>A pack dropped at a place in the list as it was shown.
        /// Written straight away: BepInEx saves the file when a setting is set.</summary>
        public static void Move(IList<string> shown, string packId, int index, ManualLogSource log)
        {
            if (_order == null) return;
            _order.Value = PackOrder.Move(Setting, shown, packId, index);
            log?.LogInfo(Tag + "'" + packId + "' moved; the order is now " + _order.Value + ".");
        }
    }
}
