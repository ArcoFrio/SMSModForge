using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;

namespace SMSModForge.Services;

/// <summary>
/// In-process clipboard for editor structures — node subtrees, action lists and
/// condition lists — so they can be copied/pasted across dialogues (and across
/// nodes). Static so the payload survives selection changes. Everything is
/// deep-cloned on copy and again on paste, so the clipboard never aliases live
/// model objects and repeated pastes are independent.
/// <para/>
/// Action and condition payloads are kept in separate slots so paste targets can
/// be type-checked: an action list only accepts the action payload, a condition
/// list only the condition payload.
/// </summary>
public static class EditorClipboard
{
    public static List<DialogueNodeDef>? NodeSubtree { get; private set; }
    public static List<NodeActionDef>? Actions { get; private set; }
    public static List<NodeConditionDef>? Conditions { get; private set; }

    public static bool HasNodes => NodeSubtree is { Count: > 0 };
    public static bool HasActions => Actions is { Count: > 0 };
    public static bool HasConditions => Conditions is { Count: > 0 };

    public static void SetNodes(IEnumerable<DialogueNodeDef> nodes) => NodeSubtree = Clone(nodes);
    public static void SetActions(IEnumerable<NodeActionDef> actions) => Actions = Clone(actions);
    public static void SetConditions(IEnumerable<NodeConditionDef> conditions) => Conditions = Clone(conditions);

    // ── Generic single-item slot (left-bar list items) ────────────────────
    // Holds one deep-cloned pack def (a SceneDef, ActorDef, …). Type-checked on
    // paste so a scene list only accepts a scene, etc. Cross-pack within a
    // session because it's static.
    private static object? _item;

    public static void SetItem<T>(T def) where T : class => _item = Clone(new[] { def })[0];
    public static bool Has<T>() where T : class => _item is T;
    public static T? GetItem<T>() where T : class => _item is T t ? Clone(new[] { t })[0] : null;

    /// <summary>Deep-clone a list via a JSON round-trip (respects the model's
    /// JsonProperty / enum-converter / ShouldSerialize attributes).</summary>
    public static List<T> Clone<T>(IEnumerable<T> items)
        => JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(items.ToList()))
           ?? new List<T>();

    /// <summary>Clone a single def (deep copy via JSON round-trip).</summary>
    public static T CloneOne<T>(T def) => Clone(new[] { def })[0];
}
