namespace SMSModForge.Shared
{
    /// <summary>
    /// The player's speaker label, which no pack recolours (the author, 1.7.0).
    /// <para/>
    /// The game writes "You" above every line the player speaks, and colours it
    /// the way it colours every name: its TMPWordColorizer matches the label's
    /// WHOLE text against a list - "you" is #B0B0B0. A pack's colour goes into
    /// that same list, keyed by the name shown, so a colour given to the player
    /// - or to any character of a pack that is also shown as "You" - would
    /// repaint the player in every scene of the game for as long as the pack is
    /// loaded. The editor never writes one; this is the plugin's answer to a
    /// pack that has one anyway, written before the player was fixed or edited
    /// by hand and never saved again.
    /// <para/>
    /// Compiled into both projects.
    /// </summary>
    public static class PlayerLabel
    {
        /// <summary>Whether a pack's name colour for this speaker may be put
        /// into the game's: not for the player, nor for anybody shown as the
        /// player is.</summary>
        public static bool MayRecolour(string key, string displayName)
        {
            if (string.Equals(key, VanillaSpeech.Player.Key, System.StringComparison.OrdinalIgnoreCase)) return false;
            if (displayName != null
                && string.Equals(displayName.Trim(), VanillaSpeech.Player.Name, System.StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
    }
}
