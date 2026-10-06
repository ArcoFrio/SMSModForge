using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Everything of one kind a pack saves, found by walking what the pack writes
/// rather than a list of places to look.
/// <para/>
/// A list goes stale the day something new learns to hold an action: the
/// hand-written walks had already missed dice branches, a screen's buttons and
/// an object's own conditions by the time renames were checked against them
/// (1.7.0). This is the walk that cannot miss one, for whoever has to be sure
/// it saw them all - migrations, and the backstop under
/// <see cref="Services.PackWalk"/>.
/// </summary>
public static class SavedObjects
{
    /// <summary>
    /// Every <typeparamref name="T"/> the pack saves, wherever it is, in the
    /// order the manifest holds them.
    /// <para/>
    /// Follows the members that are written to the manifest (a public setter,
    /// not [JsonIgnore]) through the pack's own model types, so nothing the
    /// editor only computes or borrows from the game's catalog is visited.
    /// </summary>
    public static List<T> All<T>(ModPack pack, Func<object, bool>? where = null) where T : class
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var found = new List<T>();
        Visit(pack);
        return found;

        void Visit(object? o)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o.GetType().IsEnum) return;
            if (!seen.Add(o)) return;
            if (o is T t && (where == null || where(o))) found.Add(t);

            if (o is System.Collections.IDictionary dict)
            {
                foreach (var v in dict.Values) Visit(v);
                return;
            }
            if (o is System.Collections.IEnumerable list)
            {
                foreach (var v in list) Visit(v);
                return;
            }
            if (o.GetType().Namespace != typeof(ModPack).Namespace) return;

            foreach (var prop in SavedMembers(o.GetType()))
            {
                object? value;
                try { value = prop.GetValue(o); }
                catch (Exception) { continue; }
                Visit(value);
            }
        }
    }

    private static readonly Dictionary<Type, System.Reflection.PropertyInfo[]> _savedMembers = new();

    private static System.Reflection.PropertyInfo[] SavedMembers(Type type)
    {
        lock (_savedMembers)
        {
            if (_savedMembers.TryGetValue(type, out var known)) return known;
            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0
                            && p.SetMethod != null && p.SetMethod.IsPublic
                            && !Attribute.IsDefined(p, typeof(Newtonsoft.Json.JsonIgnoreAttribute))
                            && !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string)
                            && !p.PropertyType.IsEnum)
                .ToArray();
            _savedMembers[type] = props;
            return props;
        }
    }
}
