using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Read-side compatibility shim for map/navigator button condition lists.
/// Button conditions used to be authored as a simpler <c>{variable, minValue?}</c>
/// shape; they now share the typed, groupable <see cref="NodeConditionDef"/>
/// vocabulary. This converter, applied to the button defs' <c>conditions</c>
/// property, reads either form:
/// <list type="bullet">
///   <item>An entry with a <c>type</c> deserializes as a normal
///   <see cref="NodeConditionDef"/> (typed leaf or <c>All</c>/<c>Any</c> group).</item>
///   <item>A legacy entry (<c>variable</c>, optional <c>minValue</c>) is mapped:
///   no <c>minValue</c> → <see cref="NodeConditionTypes.VariableEquals"/> against
///   <c>true</c>; with <c>minValue</c> → <see cref="NodeConditionTypes.VariableGreaterOrEqual"/>.</item>
/// </list>
/// Writing uses the default contract (<see cref="CanWrite"/> is false), so once
/// a pack is re-saved its button conditions persist in the new typed shape.
/// </summary>
public sealed class LegacyButtonConditionsConverter : JsonConverter<List<NodeConditionDef>>
{
    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, List<NodeConditionDef>? value, JsonSerializer serializer)
        => throw new NotSupportedException("CanWrite is false; default serialization is used.");

    public override List<NodeConditionDef> ReadJson(JsonReader reader, Type objectType,
        List<NodeConditionDef>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var list = new List<NodeConditionDef>();
        if (reader.TokenType == JsonToken.Null) return list;

        var arr = JArray.Load(reader);
        foreach (var token in arr)
        {
            if (token is not JObject o) continue;

            if (o["type"] != null)
            {
                var def = o.ToObject<NodeConditionDef>(serializer);
                if (def != null) list.Add(def);
                continue;
            }

            // Legacy {variable, minValue?} shape.
            var variable = (string?)o["variable"];
            if (string.IsNullOrEmpty(variable)) continue;
            int? minValue = (int?)o["minValue"];
            list.Add(new NodeConditionDef
            {
                Type = minValue.HasValue
                    ? NodeConditionTypes.VariableGreaterOrEqual
                    : NodeConditionTypes.VariableEquals,
                Params = new Dictionary<string, string>
                {
                    ["name"] = variable!,
                    ["value"] = minValue.HasValue
                        ? minValue.Value.ToString(CultureInfo.InvariantCulture)
                        : "true",
                },
            });
        }
        return list;
    }
}
