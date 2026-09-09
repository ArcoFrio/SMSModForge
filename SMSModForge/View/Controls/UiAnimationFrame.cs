using SMSModForge.Model;

namespace SMSModForge.View.Controls;

/// <summary>
/// One moment of an arrival animation, as the preview should draw it.
/// <para/>
/// A single object rather than a property per value, because the preview draws
/// the whole screen again for every property that changes: three pieces meant
/// three full composites per frame, and an animation that crawled.
/// <para/>
/// Immutable, so a frame handed over cannot be edited underneath the render
/// that is drawing it.
/// </summary>
/// <param name="Target">The object being animated.</param>
/// <param name="Alpha">Opacity, 0-1. 1 leaves the object's own alone.</param>
/// <param name="Scale">Scale, or null to leave the object's own alone.</param>
public sealed record UiAnimationFrame(UiNodeDef Target, double Alpha, float[]? Scale);
