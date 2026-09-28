using SMSModForge.Localization;
using SMSModForge.Shared;

namespace SMSModForge.Model;

/// <summary>
/// What to call one of a bust's texture slots on screen. Kept out of
/// <see cref="SpriteSlotNames"/>, which the plugin shares and which has no
/// business holding words: the slots are the program's, their names are the
/// author's language.
/// </summary>
public static class SpriteSlotLabels
{
    public static string Of(string slot)
    {
        string? face = SpriteSlotNames.ExpressionOf(slot);
        if (face != null) return face;

        int frame = SpriteSlotNames.MouthFrame(slot);
        if (frame > 0) return Loc.F("slot.mouth", "frame", frame);

        if (slot == SpriteSlotNames.Base) return Loc.T("slot.base");
        if (slot == SpriteSlotNames.Mask) return Loc.T("slot.mask");
        if (slot == SpriteSlotNames.Blink) return Loc.T("slot.blink");
        return slot ?? "";
    }
}
