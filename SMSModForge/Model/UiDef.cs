using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A UI the pack adds of its own — a window, a panel, a HUD element.
/// <para/>
/// The one decision that changes what this actually is, is
/// <see cref="HidesWithGameplayUi"/>. Everything else is content.
/// </summary>
public sealed class UiDef
{
    /// <summary>Stable reference other parts of the pack use to open or close
    /// this UI. Renaming the display name must not break those, which is why
    /// this is separate from <see cref="Name"/>.</summary>
    [JsonProperty("id", Order = 1)]
    public string Id { get; set; } = "";

    /// <summary>What the author calls it in the editor.</summary>
    [JsonProperty("name", Order = 2)]
    public string Name { get; set; } = "";

    /// <summary>
    /// Whether this UI is part of the gameplay layer that the game dims and
    /// hides together — during a cutscene, a transition, a scene.
    /// <para/>
    /// It decides parentage, and parentage is the mechanism. The gameplay
    /// canvas carries a CanvasGroup, and every object beneath it inherits that
    /// group's alpha; so a UI parented there disappears whenever the game
    /// hides the rest of the interface, and a UI outside it stays up. There is
    /// no separate switch and nothing named "fade" to look for in the game —
    /// being a child of that canvas <em>is</em> the setting.
    /// <para/>
    /// True suits anything that belongs to normal play: a stats panel, an
    /// inventory, a shop. False suits anything that must survive a fade —
    /// a subtitle layer, a debug overlay, a UI that is itself part of a
    /// cutscene.
    /// </summary>
    [JsonProperty("hidesWithGameplayUi", Order = 3)]
    public bool HidesWithGameplayUi { get; set; } = true;

    /// <summary>Whether the UI is on screen as soon as the game loads. Most are
    /// not: the vanilla scene keeps 34 of its 49 canvases switched off and
    /// turns one on when its moment arrives.</summary>
    [JsonProperty("startsOpen", Order = 4)]
    public bool StartsOpen { get; set; }

    /// <summary>
    /// Draw order against other independent UI. Higher is nearer the front.
    /// <para/>
    /// Only consulted when <see cref="HidesWithGameplayUi"/> is false, because
    /// that is the only case where this UI is a canvas of its own. Inside the
    /// gameplay canvas, order is position among siblings, exactly as it is for
    /// the vanilla screens there.
    /// </summary>
    [JsonProperty("sortingOrder", Order = 5)]
    public int SortingOrder { get; set; }

    /// <summary>The template this was created from, kept for the author's
    /// benefit rather than the runtime's — it is a record of where the shape
    /// came from, and the objects below are free to have diverged from it.</summary>
    [JsonProperty("template", Order = 6)]
    public string Template { get; set; } = "";

    /// <summary>The UI itself.</summary>
    [JsonProperty("nodes", Order = 7)]
    public List<UiNodeDef> Nodes { get; set; } = new();

    /// <summary>Conditions gating whether this UI can be shown at all.</summary>
    [JsonProperty("openConditions", Order = 8)]
    public List<NodeConditionDef> OpenConditions { get; set; } = new();

    public bool ShouldSerializeStartsOpen() => StartsOpen;
    public bool ShouldSerializeSortingOrder() => SortingOrder != 0;
    public bool ShouldSerializeTemplate() => !string.IsNullOrEmpty(Template);
    public bool ShouldSerializeNodes() => Nodes.Count > 0;
    public bool ShouldSerializeOpenConditions() => OpenConditions.Count > 0;
}
