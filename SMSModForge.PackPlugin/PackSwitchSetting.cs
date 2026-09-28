using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The packs the player has switched off on the main menu, kept in the
    /// plugin's config file. The rule is <see cref="PackSwitches"/>; this only
    /// holds the setting.
    /// </summary>
    internal static class PackSwitchSetting
    {
        private const string Tag = "[SMSModForge.PackPlugin] Packs: ";
        private static ConfigEntry<string> _off;

        public static void Configure(ConfigFile config)
        {
            _off = config.Bind("Packs", "SwitchedOff", "",
                "Installed packs that are not loaded, by pack id, separated by commas. Every other installed pack "
                + "is loaded. Also chosen on the main menu: the box beside each pack.");
        }

        private static string Setting
        {
            get { return _off == null ? "" : _off.Value ?? ""; }
        }

        public static bool IsOn(string packId)
        {
            return PackSwitches.IsOn(Setting, packId);
        }

        /// <summary>The ids switched off, whatever their case.</summary>
        public static HashSet<string> Off
        {
            get { return PackSwitches.Off(Setting); }
        }

        /// <summary>Switch one pack on or off. Written straight away: BepInEx
        /// saves the file when a setting is set.</summary>
        public static void Set(string packId, bool on, ManualLogSource log)
        {
            if (_off == null) return;
            _off.Value = PackSwitches.With(Setting, packId, on);
            log?.LogInfo(Tag + "'" + packId + "' switched " + (on ? "on" : "off") + " on the main menu.");
        }
    }
}
