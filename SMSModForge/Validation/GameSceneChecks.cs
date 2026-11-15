using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.Validation;

/// <summary>
/// Changes to the game's own scenes (1.9.0, <see cref="SceneDef.Source"/>):
/// one with no scene picked, one naming a scene the editor does not know, two
/// changing the same scene, and one that changes nothing.
/// </summary>
internal static class GameSceneChecks
{
    public const string CodeNotChosen = "scene.gameNotChosen";
    public const string CodeUnknown = "scene.gameUnknown";
    public const string CodeTwice = "scene.gameTwice";
    public const string CodeNoChange = "scene.gameNoChange";

    public static void Check(List<ValidationIssue> issues, ModPack pack)
    {
        var seen = new Dictionary<string, SceneDef>(System.StringComparer.Ordinal);
        foreach (var sc in pack.Scenes.Where(s => s.IsGameScene))
        {
            string where = $"scenes[{sc.Key}].source";
            string name = string.IsNullOrWhiteSpace(sc.DisplayName) ? sc.Key : sc.DisplayName;

            if (string.IsNullOrEmpty(sc.Source))
            {
                issues.Add(new(Severity.Warning, where, Loc.F("validation.scene.gameNotChosen", "name", name), CodeNotChosen));
                continue;
            }

            // Only when the editor has its list: without it, every scene would
            // read as unknown, and that is the editor's problem, said on the tab.
            if (VanillaScenes.All.Count > 0 && VanillaScenes.Find(sc.Source) == null)
                issues.Add(new(Severity.Warning, where, Loc.F("validation.scene.gameUnknown", "scene", sc.Source!), CodeUnknown));

            if (seen.TryGetValue(sc.Source!, out var first))
                issues.Add(new(Severity.Error, where,
                    Loc.F("validation.scene.gameTwice", "scene", SceneName(sc.Source!),
                          "first", string.IsNullOrWhiteSpace(first.DisplayName) ? first.Key : first.DisplayName),
                    CodeTwice));
            else seen[sc.Source!] = sc;

            bool photo = sc.StarmakerPhoto && VanillaScenes.Find(sc.Source)?.Photo != true;
            if (string.IsNullOrWhiteSpace(sc.SceneSprite) && string.IsNullOrWhiteSpace(sc.VanillaFrame)
                && string.IsNullOrWhiteSpace(sc.CustomFrameSprite) && sc.Sound == SceneSoundMode.None && !photo)
                issues.Add(new(Severity.Info, $"scenes[{sc.Key}]",
                    Loc.F("validation.scene.gameNoChange", "scene", SceneName(sc.Source!)), CodeNoChange));
        }
    }

    private static string SceneName(string path) => path.Contains('/') ? path[(path.IndexOf('/') + 1)..] : path;
}
