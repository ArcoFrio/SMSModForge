using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every kind of thing ModForge can make is made by a tutorial (the author, 2026-11-21: "We need to be teaching
/// and giving tutorial assets to the player for every little thing they can create with ModForge. We need a
/// coverage check, and more tutorials wherever we're lacking").
/// <para/>
/// Not from a list of what the tutorials say: every tutorial's steps are done into one practice pack, as Bring in
/// does them, and the pack that comes out is walked. What the editor offers is read from the editor itself - every
/// kind of record a pack holds, every action and condition its pickers offer, every trigger mode, variable type,
/// refresh mode and kind of line - so a new one fails this until a tutorial makes it. Below them, features that are
/// not a type of their own (a voice-only character, a picture light, a change to one of the game's quests...).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class TutorialFeatureCoverageTests
{
    private readonly ITestOutputHelper _out;
    public TutorialFeatureCoverageTests(ITestOutputHelper o) => _out = o;

    /// <summary>Record lists no tutorial makes, each with why - none may be left here by accident: the test fails
    /// when one of them is made after all, so the reason is read again.</summary>
    private static readonly Dictionary<string, string> NotTaught = new()
    {
        ["IgnoredIssues"] = "the author's own choices about Validate's list, not something a pack makes for players",
        ["Actors"] = "legacy: older packs' actors fold into Characters on load",
        ["LegacyVanillaUiExtensions"] = "legacy: older packs' screen changes, migrated on load",
        ["CustomRoomTalks"] = "no control in the editor makes one any more: older packs only",
        ["VanillaGates"] = "taking a condition out of the game's own conversations changes the game's script for every save - "
                         + "too invasive to teach as practice (the author: \"try not to make it too invasive\"); the guide explains it",
    };

    internal sealed class Inventory
    {
        public HashSet<string> Lists { get; } = new();
        public HashSet<string> Actions { get; } = new();
        public HashSet<string> Conditions { get; } = new();
        public HashSet<string> Settings { get; } = new();
        public List<NodeActionDef> AllActions { get; } = new();
        public List<NodeConditionDef> AllConditions { get; } = new();
        public List<GameObjectDef> Objects { get; } = new();
    }

    internal static Inventory Walk(ModPack pack)
    {
        var inv = new Inventory();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? o)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o is Enum || !seen.Add(o)) return;
            if (o is IDictionary dict) { foreach (var v in dict.Values) Visit(v); return; }
            if (o is IEnumerable list) { foreach (var v in list) Visit(v); return; }
            var t = o.GetType();
            if (t.Namespace?.StartsWith("SMSModForge", StringComparison.Ordinal) != true) return;
            if (o is NodeActionDef a) { inv.Actions.Add(a.Type); inv.AllActions.Add(a); }
            if (o is NodeConditionDef c) { inv.Conditions.Add(c.Type); inv.AllConditions.Add(c); }
            if (o is GameObjectDef g) inv.Objects.Add(g);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                object? v;
                try { v = p.GetValue(o); } catch { continue; }
                if (v is Enum e) inv.Settings.Add($"{t.Name}.{p.Name}={e}");
                else if (v is IList l && l.Count > 0 && o is ModPack) inv.Lists.Add(p.Name);
                if (v is not string) Visit(v);
            }
        }
        Visit(pack);
        return inv;
    }

    /// <summary>The features that are not a type of their own, each read from the pack.</summary>
    private static IEnumerable<(string What, Func<ModPack, Inventory, bool> Made)> Features()
    {
        yield return ("a character with a bust of its own", (p, i) => p.Characters.Any(c => !c.IsVanillaCharacter && !c.IsPlayer && c.BustSource == BustSource.Pack));
        yield return ("a voice-only character (+ Voice)", (p, i) => p.Characters.Any(c => !c.IsVanillaCharacter && !c.IsPlayer && c.BustSource == BustSource.None));
        yield return ("a character wearing one of the game's busts", (p, i) => p.Characters.Any(c => !c.IsVanillaCharacter && !c.IsPlayer && c.BustSource == BustSource.Vanilla));
        yield return ("one of the game's characters changed", (p, i) => p.Characters.Any(c => c.IsVanillaCharacter && !c.IsPlayer && !string.IsNullOrWhiteSpace(c.NameColor)));
        yield return ("an outfit's jiggle mask", (p, i) => p.Characters.SelectMany(c => c.Outfits).Any(o => o.MaskSprite.Length > 0));
        yield return ("an NPC's jiggle mask", (p, i) => p.Npcs.Any(n => !string.IsNullOrEmpty(n.Mask)));
        yield return ("an NPC's wet particles", (p, i) => p.Npcs.Any(n => n.Wet.Enabled));
        yield return ("one of the game's conversations changed", (p, i) => p.Dialogues.Any(d => d.Source.Length > 0));
        yield return ("a dialogue waiting for the Talk button", (p, i) => p.Dialogues.Any(d => d.Queued));
        yield return ("a dialogue also left on the Talk button", (p, i) => p.Dialogues.Any(d => d.ReplayOnTalk));
        yield return ("a dialogue queued behind another", (p, i) => p.Dialogues.Any(d => d.QueueBehind));
        yield return ("a line the same in every language", (p, i) => p.Dialogues.SelectMany(d => d.Nodes).Any(n => n.TextSameEverywhere));
        yield return ("a negated condition", (p, i) => i.AllConditions.Any(c => c.Negate));
        yield return ("a group of conditions (OR)", (p, i) => i.AllConditions.Any(c => c.Type == NodeConditionTypes.GroupAny));
        yield return ("a rule with else-if branches", (p, i) => p.IntegrationRules.Any(r => r.Branches.Count > 0));
        yield return ("a rule repeated for each value", (p, i) => p.IntegrationRules.Any(r => !string.IsNullOrWhiteSpace(r.ForEach)));
        yield return ("one of the game's levels added to (a vanilla extension)", (p, i) => p.VanillaExtensions.Count > 0);
        yield return ("an object with Active when conditions", (p, i) => i.Objects.Any(o => o.ActiveConditions.Count > 0));
        yield return ("a copy of one of the game's objects", (p, i) => i.Objects.Any(o => o.CopyOf.Length > 0));
        foreach (string component in PackComponentType.BuiltIn)
            yield return ($"the {component} component", (p, i) => WalkComponents(p).Contains(component));
        yield return ("a round light", (p, i) => i.Objects.Any(o => o.Light?.Shape == LightDef.ShapeRound));
        yield return ("a free-shape light", (p, i) => i.Objects.Any(o => o.Light?.Shape == LightDef.ShapeFree));
        yield return ("a picture light", (p, i) => i.Objects.Any(o => o.Light?.Shape == LightDef.ShapePicture));
        yield return ("darkness", (p, i) => i.Objects.Any(o => o.Light?.Shape == LightDef.ShapeAmbient));
        yield return ("an effect in a place", (p, i) => i.Objects.Any(o => o.Effect != null));
        yield return ("one of the game's scenes changed", (p, i) => p.Scenes.Any(s => s.IsGameScene));
        yield return ("a scene treated as a Starmaker photo", (p, i) => p.Scenes.Any(s => s.StarmakerPhoto));
        yield return ("a sound played by a word in a line", (p, i) => p.Sfx.Any(s => s.TextPatterns is { Count: > 0 }));
        yield return ("a wallpaper with unlock conditions", (p, i) => p.Wallpapers.Any(w => w.UnlockConditions.Count > 0));
        yield return ("a UI screen on from the start", (p, i) => p.Uis.Any(u => u.StartsOpen));
        yield return ("one of the game's quests changed", (p, i) => p.Quests.Any(q => q.Source.Length > 0));
        yield return ("a counted quest task", (p, i) => p.Quests.SelectMany(q => q.Tasks).Any(t => t.CountTo > 0));
        yield return ("an effect with Glow", (p, i) => p.Effects.Any(e => e.Glow));
        yield return ("an effect changing the game's own", (p, i) => p.Effects.Any(e => e.ChangesGame));
        // Every way an effect can be set to play (a text, not an enum: the settings below do not see it).
        foreach (string when in Shared.Effects.PlayWhens)
            yield return ($"an effect that plays {when}", (p, i) => p.Effects.Any(e => e.PlayWhen == when));
    }

    private static HashSet<string> WalkComponents(ModPack pack)
    {
        var found = new HashSet<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? o)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o is Enum || !seen.Add(o)) return;
            if (o is IEnumerable list) { foreach (var v in list) Visit(v); return; }
            if (o is ComponentDef c) found.Add(c.Type);
            if (o.GetType().Namespace?.StartsWith("SMSModForge", StringComparison.Ordinal) != true) return;
            foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0 && p.CanRead) { try { Visit(p.GetValue(o)); } catch { } }
        }
        Visit(pack);
        return found;
    }

    [Fact]
    public void EveryKindOfThingModForgeMakesIsMadeByATutorial()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SMSModForge-coverage-" + Guid.NewGuid().ToString("N"));
        var gaps = new List<string>();
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                Assert.True(vm.MakePack(folder, StoryPack.Name));
                TutorialAssets.EnsureCopied(vm.PackRoot);
                foreach (var t in TutorialCatalog.All.Where(t => t.IsOnLadder).OrderBy(t => t.Level))
                {
                    StoryPack.Replay(vm, t);
                    WindowHarness.Pump();
                }
                var pack = vm.Pack;
                var inv = Walk(pack);

                // Every kind of record a pack holds.
                foreach (var p in typeof(ModPack).GetProperties().Where(p => typeof(IList).IsAssignableFrom(p.PropertyType)
                                                                             && !p.Name.EndsWith("Folders", StringComparison.Ordinal)))
                {
                    bool made = inv.Lists.Contains(p.Name);
                    if (NotTaught.ContainsKey(p.Name)) { if (made) gaps.Add($"{p.Name} is made after all: take it off NotTaught"); }
                    else if (!made) gaps.Add($"no tutorial makes any {p.Name}");
                }

                // Every action the picker offers; the Variable entry by each of its operations.
                var family = new Dictionary<string, string>
                {
                    ["Set"] = NodeActionTypes.SetVariable, ["Increment"] = NodeActionTypes.IncrementVariable,
                    [NodeActionViewModel.OpRandomFromList] = NodeActionTypes.PickRandomFromList,
                    [NodeActionViewModel.OpCountList] = NodeActionTypes.CountList,
                };
                Assert.Equal(NodeActionViewModel.VariableOperations.OrderBy(x => x), family.Keys.OrderBy(x => x));
                foreach (string type in vm.ActionTypes.Where(t => t != NodeActionViewModel.VariableFamilyType))
                    if (!inv.Actions.Contains(type)) gaps.Add($"no tutorial uses the action {type}");
                foreach (var (operation, type) in family)
                    if (!inv.Actions.Contains(type)) gaps.Add($"no tutorial uses the Variable action's {operation}");

                // Every condition offered anywhere; the Variable entry by its two types.
                var offered = NodeConditionTypes.All.Concat(NodeConditionTypes.AllOneShot).Concat(NodeConditionTypes.AllRule)
                    .Distinct().Where(c => !VariableMerge.SupersededTypes.Contains(c) && !NodeConditionTypes.IsGroup(c));
                foreach (string type in offered)
                    if (!inv.Conditions.Contains(type)) gaps.Add($"no tutorial uses the condition {type}");

                // Every choice of the settings the editor offers in a list.
                void Each<T>(IEnumerable<T> values, string setting, T? isDefault = default)
                {
                    foreach (var v in values)
                        if (!EqualityComparer<T>.Default.Equals(v, isDefault!) && !inv.Settings.Contains($"{setting}={v}"))
                            gaps.Add($"no tutorial chooses {setting} {v}");
                }
                Each(Enum.GetValues<UpdateRuleTriggerMode>(), "UpdateRuleDef.TriggerMode");
                Each(vm.VariableTypes, "PackVariableDef.Type");
                Each(vm.VariableRefreshModes, "PackVariableDef.RefreshMode");
                Each(vm.NodeKinds, "DialogueNodeDef.Kind");
                Each(vm.NodeDurations, "DialogueNodeDef.Duration");
                Each(vm.JumpModes, "JumpDef.Mode", JumpMode.Continue);   // Continue is a line with no jump at all

                // And the features that are not a type of their own.
                foreach (var (what, made) in Features())
                    if (!made(pack, inv)) gaps.Add("no tutorial makes " + what);

                // What the tutorials make is what a beginner ends up with: Validate has nothing to say about it. The
                // guide's pictures of the Issues page found the round-3 chapters leaving two errors and two warnings.
                foreach (var issue in Validation.PackValidator.Validate(pack, vm.PackRoot!).Where(x => x.Severity != Validation.Severity.Info))
                    gaps.Add($"Validate says ({issue.Severity}) {issue.Where}: {issue.Message}");
            });
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        foreach (var g in gaps) _out.WriteLine(g);
        Assert.True(gaps.Count == 0, "what no tutorial makes:\n" + string.Join("\n", gaps));
    }
}
