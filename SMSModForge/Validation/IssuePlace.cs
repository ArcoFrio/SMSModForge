using System;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.Validation;

/// <summary>
/// Where an issue is, in words an author recognises: the dialogue's name, the
/// line by its first words, the action by its type - "Chat at the cave ›
/// "Huff!" › on start: CharacterFocus" - instead of the path the editor uses
/// to find it (<see cref="ValidationIssue.Where"/>), which the list used to
/// show cut off at its first twenty letters (the author, 2026-10-21: a
/// warning that did not say where it was).
/// <para/>
/// Read off the pack as it is, so it names things by what they are called
/// now; anything it cannot find falls back to the key it was given.
/// </summary>
public static class IssuePlace
{
    private const string Step = " › ";
    private const int LineWords = 32;

    public static string Describe(string? where, ModPack pack)
    {
        if (string.IsNullOrEmpty(where)) return "";
        var head = Regex.Match(where, @"^(?<g>[A-Za-z]+)\[(?<inner>[^\]]*)\]");
        if (!head.Success) return Loc.T("issue.place.pack");
        string group = head.Groups["g"].Value, inner = head.Groups["inner"].Value;
        string rest = where[head.Length..].TrimStart('.');

        switch (group)
        {
            case "dialogues":
            {
                var d = pack.Dialogues.FirstOrDefault(x => x.Key == inner);
                string place = Named(d?.DisplayName, inner);
                var node = Regex.Match(rest, @"^nodes\[id=(?<id>-?\d+)\]\.?");
                if (node.Success && int.TryParse(node.Groups["id"].Value, out int id))
                {
                    var n = d?.Nodes.FirstOrDefault(x => x.Id == id);
                    place += Step + LineOf(n, id);
                    rest = rest[node.Length..];
                }
                return place + Field(rest);
            }
            case "places":
                return Named(pack.Places.FirstOrDefault(p => p.Key == inner)?.DisplayName, inner) + Field(rest);
            case "characters":
            case "actors":
            {
                var c = pack.Characters.FirstOrDefault(x => x.Name == inner || x.Key == inner);
                string place = Named(c?.DisplayName, inner);
                var outfit = Regex.Match(rest, @"^outfits\[(?<k>[^\]]*)\]\.?");
                if (outfit.Success)
                {
                    place += Step + outfit.Groups["k"].Value;
                    rest = rest[outfit.Length..];
                }
                return place + Field(rest);
            }
            case "quests":
            {
                var q = pack.Quests.FirstOrDefault(x => x.Key == inner);
                string place = Named(q?.Title, inner);
                var task = Regex.Match(rest, @"^(tasks|addedTasks|vanillaTasks)\[(?<k>[^\]]*)\]\.?");
                if (task.Success)
                {
                    string k = task.Groups["k"].Value;
                    var named = q?.Tasks.FirstOrDefault(t => t.Key == k)?.Name;
                    place += Step + Named(named, k);
                    rest = rest[task.Length..];
                }
                return place + Field(rest);
            }
            case "integrationRules":
                return Named(pack.IntegrationRules.FirstOrDefault(r => r.Key == inner)?.DisplayName, inner) + Field(rest);
            case "vanillaExtensions":
            case "mapButtons":
            {
                // "3:Beach" / "1:District→Target": the position is for finding it.
                int colon = inner.IndexOf(':');
                return (colon >= 0 ? inner[(colon + 1)..] : inner) + Field(rest);
            }
            default:
                return inner + Field(rest);
        }
    }

    private static string Named(string? name, string key) => string.IsNullOrWhiteSpace(name) ? key : name!;

    /// <summary>A line by its first words; one with none by its number.</summary>
    private static string LineOf(DialogueNodeDef? node, int id)
    {
        string text = Regex.Replace(node?.Text ?? "", "<[^>]*>", "").Trim();
        if (text.Length == 0) return Loc.F("issue.place.line", "id", id);
        if (text.Length > LineWords) text = text[..LineWords].TrimEnd() + "…";
        return "“" + text + "”";
    }

    /// <summary>The list an action or condition sits in, and its type; the
    /// name of a field as it stands otherwise.</summary>
    private static string Field(string rest)
    {
        if (rest.Length == 0) return "";
        var row = Regex.Match(rest, @"^(?<list>actionsOnStart|actionsOnFinish|startConditions|conditions|actions)(\[\d+\])?\.(?<type>[A-Za-z]+)");
        if (row.Success)
        {
            string list = row.Groups["list"].Value switch
            {
                "actionsOnStart" => Loc.T("issue.place.onStart"),
                "actionsOnFinish" => Loc.T("issue.place.onFinish"),
                "startConditions" => Loc.T("issue.place.startCondition"),
                "conditions" => Loc.T("issue.place.condition"),
                _ => Loc.T("issue.place.action"),
            };
            return Step + list + ": " + row.Groups["type"].Value;
        }
        return "";
    }
}
