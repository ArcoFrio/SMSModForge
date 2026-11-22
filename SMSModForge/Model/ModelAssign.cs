using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SMSModForge.Model;

/// <summary>
/// One record of the pack given every saved value of another of its kind,
/// staying the object it is (1.10.0, for undo). A step put back used to
/// replace the record - and so everything that showed it: the line's row in
/// the list, the editor beside it, the light's panel, built again or, worse,
/// left pointing at the record that was replaced (the place preview's light
/// panel kept the old light after every undo: its sliders showed what had been
/// undone, and moving one changed a light nobody was looking at). Given the
/// step's values instead, the record is still the one on screen, and what
/// shows it only has to read it again.
/// <para/>
/// "Every saved value" is the serializer's own list of them, so a value added
/// to a record later is carried without anyone remembering to add it here.
/// What a record keeps beside them (what it was read from in the game's
/// catalog, say) stays as it is.
/// </summary>
public static class ModelAssign
{
    private static readonly Lazy<IContractResolver> Resolver =
        new(() => JsonSerializer.Create(PackRepository.ManifestSettings).ContractResolver);

    /// <summary>
    /// Give <paramref name="target"/> every saved value <paramref name="source"/> has (their lists and records
    /// handed over as they are: the source is one read for this and let go of). False, with nothing changed, when
    /// the two are not of one kind or the kind keeps values this cannot hand over.
    /// </summary>
    public static bool Into(object target, object source)
    {
        if (target.GetType() != source.GetType()) return false;
        if (Resolver.Value.ResolveContract(target.GetType()) is not JsonObjectContract contract) return false;
        if (contract.ExtensionDataGetter != null) return false;
        var todo = new List<Action>();
        foreach (var p in contract.Properties)
        {
            if (p.Ignored || !p.Readable || p.ValueProvider == null) continue;
            var provider = p.ValueProvider;
            object? value = provider.GetValue(source);
            if (p.Writable)
            {
                todo.Add(() => provider.SetValue(target, value));
                continue;
            }
            // No way to set it: a list or a table the record makes for itself is emptied and filled.
            object? mine = provider.GetValue(target);
            if (mine is IList list && value is IList from && !list.IsReadOnly && !list.IsFixedSize)
                todo.Add(() => { list.Clear(); foreach (var item in from) list.Add(item); });
            else if (mine is IDictionary table && value is IDictionary entries && !table.IsReadOnly)
                todo.Add(() => { table.Clear(); foreach (DictionaryEntry e in entries) table.Add(e.Key, e.Value); });
            else if (Equals(mine, value)) continue;
            else return false;
        }
        foreach (var act in todo) act();
        return true;
    }

    /// <summary>A record as the manifest would write it, alone - for telling whether two are the same.</summary>
    public static string Written(object? record) => JsonConvert.SerializeObject(record, PackRepository.ManifestSettings);
}
