namespace SMSModForge.Model;

/// <summary>
/// Catalogue of <em>vanilla</em> Starmaker Story GC2 signal names — the ones the
/// base game already listens for, so a pack can emit them with nothing but the
/// game running. Surfaced as autocomplete on the <see cref="ParamType.SignalRef"/>
/// param (EmitSignal / EmitSignalDelayed / TransitionLevels' "done" signal).
/// <para/>
/// Target-game vanilla data, in the same spirit as <c>VanillaPlaces</c> /
/// <c>VanillaFrames</c> — deliberately <b>not</b> mod-specific. Signals whose
/// listener lives in a mod plugin (e.g. the host mod's <c>MyUiSignal</c> /
/// <c>MyEventSignal</c>) do NOT belong here; the combo stays editable so authors
/// can type those (or any custom signal) directly.
/// </summary>
public static class VanillaSignals
{
    /// <summary>Known vanilla signal names, alphabetised to match the editor's
    /// other option lists.</summary>
    public static readonly string[] All =
    {
        "Blink",
        "DialogueEnd",
        "DialogueStart",
        "drink",
        "FadeIn2025",
        "FadeInBlack",
        "FadeOut2025",
        "FadeOutBlack",
        "FadeUI",
        "flash",
        "ForceEnableUI",
        "kiss",
        "whiteflashnosound",
    };
}
