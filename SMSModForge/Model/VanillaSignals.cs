namespace SMSModForge.Model;

/// <summary>
/// Catalogue of <em>vanilla</em> Starmaker Story GC2 signal names — the ones the
/// base game already listens for, so a pack can emit them with nothing but the
/// game running. Surfaced as autocomplete on the <see cref="ParamType.SignalRef"/>
/// param (EmitSignal / TransitionLevels' "done" signal).
/// <para/>
/// Target-game vanilla data, in the same spirit as <c>VanillaPlaces</c> /
/// <c>VanillaFrames</c> — deliberately <b>not</b> mod-specific. Signals whose
/// listener lives in a mod plugin (e.g. the host mod's <c>MyUiSignal</c> /
/// <c>MyEventSignal</c>) do NOT belong here; the combo stays editable so authors
/// can type those (or any custom signal) directly.
/// <para/>
/// Not listed any more (1.7.0, the author), though a pack that emits them still
/// does exactly what it did:
/// <list type="bullet">
///   <item><c>DialogueStart</c> / <c>DialogueEnd</c> - a dialogue already sends
///   them itself, at its start and its end.</item>
///   <item>The transitions - <c>FadeInBlack</c> / <c>FadeOutBlack</c>,
///   <c>FadeIn2025</c> / <c>FadeOut2025</c>, <c>Blink</c> and
///   <c>whiteflashnosound</c> - which the Transition action plays, in and out,
///   from one row (<see cref="Transitions"/>).</item>
/// </list>
/// </summary>
public static class VanillaSignals
{
    /// <summary>Known vanilla signal names, alphabetised to match the editor's
    /// other option lists.</summary>
    public static readonly string[] All =
    {
        "drink",
        "FadeUI",
        "flash",
        "ForceEnableUI",
        "kiss",
    };
}
