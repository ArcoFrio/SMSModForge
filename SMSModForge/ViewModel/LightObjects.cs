using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.ViewModel;

/// <summary>
/// Making a light object (1.10.0), shared by a pack place and a vanilla
/// extension: the preview window's "+ Light" asks for one where the view is.
/// </summary>
internal static class LightObjects
{
    /// <summary>What a new light object is called: "Light", then "Light 2", "Light 3"... - unique among
    /// <paramref name="taken"/>, since dialogues reach an object by its name.</summary>
    public static string NewName(IEnumerable<string> taken, string name = "Light")   // English on purpose: an object's name, which dialogues address, like "GameObject".
    {
        var set = new HashSet<string>(taken.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!set.Contains(name + " " + i)) return name + " " + i;
    }

    /// <summary>Every object name in a tree, to keep a new one apart from them.</summary>
    public static IEnumerable<string> Names(IEnumerable<GameObjectViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n.Name;
            foreach (var c in Names(n.Children)) yield return c;
        }
    }

    /// <summary>Make <paramref name="made"/> an effect object (1.10.0) at the command's parameter (float[] { x, y }):
    /// one of the game's level effects, the fog, to change.</summary>
    public static GameObjectViewModel DressAsEffect(GameObjectViewModel made, IEnumerable<GameObjectViewModel> tree, object? at)
    {
        made.Name = NewName(Names(tree), "Effect");   // English on purpose: an object's name, which dialogues address.
        if (at is float[] { Length: >= 2 } xy) { made.X = xy[0]; made.Y = xy[1]; }
        made.AddEffect();
        return made;
    }

    /// <summary>The command's parameter for the place's own base light, darkened (1.10.0), rather than a lamp.</summary>
    public const string Darkness = "darkness";

    /// <summary>Make <paramref name="made"/> a light object at the command's parameter (float[] { x, y }) - or, for
    /// <see cref="Darkness"/>, the place's base light at a night's blue, a third of the day's.</summary>
    /// <param name="withTheBack">Whether the place has a back picture for the light to drift with: a new lamp drifts
    /// with it as the cursor moves (after 1.10.0, the author: "Moving with background should default to on"), and
    /// with the front where there is none. The whole place's light lights it all and does not drift.</param>
    public static GameObjectViewModel Dress(GameObjectViewModel made, IEnumerable<GameObjectViewModel> tree, object? at, bool withTheBack = false)
    {
        if (at is string kind && kind == Darkness)
        {
            made.Name = NewName(Names(tree), "Darkness");   // English on purpose: an object's name, which dialogues address.
            made.AddLight(new Model.LightDef { Shape = Model.LightDef.ShapeAmbient, Color = "#8C9CCC", Strength = 0.35f });
            return made;
        }
        made.Name = NewName(Names(tree));
        if (at is float[] { Length: >= 2 } xy) { made.X = xy[0]; made.Y = xy[1]; }
        made.AddLight();
        if (withTheBack) made.DriftWith(GameObjectViewModel.DriftBack);
        return made;
    }
}
