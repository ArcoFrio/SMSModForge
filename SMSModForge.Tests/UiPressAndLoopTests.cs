using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// "Pressed" and "Keeps moving" on a screen's object (found testing a pack,
/// 2026-11-03): what the editor writes. The game side is the in-game suite's
/// F8 and F9.
/// </summary>
public sealed class UiPressAndLoopTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-pressloop-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static IEnumerable<JObject> All(JToken nodes)
        => nodes.OfType<JObject>().SelectMany(n => new[] { n }.Concat(n["children"] is JArray c ? All(c) : Enumerable.Empty<JObject>()));

    private JObject Saved(UiNodeDef node)
    {
        var pack = PackRepository.CreateEmpty("pressloop");
        pack.Uis.Add(new UiDef { Id = "s", Name = "s", Nodes = { node } });
        PackRepository.Save(pack, _root);
        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        return (JObject)saved["uis"]![0]!["nodes"]![0]!;
    }

    [Fact]
    public void The_boxes_make_a_loop_for_its_first_part_and_take_it_away_with_its_last()
    {
        var node = new UiNodeDef { Name = "Line" };
        var vm = new UiNodeViewModel(node);
        Assert.False(vm.Loops);

        vm.PulsesFade = true;
        vm.FadeFrom = 0.3f;
        vm.Sweeps = true;
        vm.LoopSeconds = 2.5f;
        Assert.True(vm.Loops);
        var loop = (JObject)Saved(node)["loop"]!;
        Assert.Equal(new[] { 0.3f, 1f }, loop["alpha"]!.Select(t => (float)t));
        Assert.Equal(new[] { -100f, 0f }, loop["moveFrom"]!.Select(t => (float)t));
        Assert.Equal(2.5f, (float)loop["seconds"]!);
        Assert.Null(loop["scale"]);
        Assert.Null(loop["easing"]);     // Linear, the default, is not written

        vm.PulsesFade = false;
        vm.Sweeps = false;
        Assert.False(vm.Loops);
        Assert.Null(node.Loop);
        Assert.Null(Saved(node)["loop"]);
    }

    [Theory]
    [InlineData("0.9", 0.9f)]
    [InlineData("0,95", 0.95f)]
    [InlineData("", null)]
    [InlineData("soft", null)]
    public void Pressed_is_a_number_or_nothing(string typed, float? expected)
    {
        var node = new UiNodeDef { Name = "Card" };
        new UiNodeViewModel(node) { PressScale = typed };
        Assert.Equal(expected, node.PressScale);
        Assert.Equal(expected, (float?)Saved(node)["pressScale"]);
    }

    [Fact]
    public void On_one_of_the_games_screens_a_press_or_a_loop_alone_is_kept_by_the_save()
    {
        var screen = VanillaUiCatalog.Find("vanillaui:9_MainCanvas/Quitagme")!;
        var root = VanillaUiSeed.FromBase(VanillaUiLibrary.Node(screen)!);
        UiNodeDef At(string bind) => Flatten(root).Single(n => n.Bind == bind);
        At("Button").PressScale = 0.9f;
        At("Button (1)").Loop = new UiLoopDef { Scale = new[] { 1f, 1.1f } };

        var pack = PackRepository.CreateEmpty("pressloop");
        pack.Uis.Add(new UiDef { Id = "quit", Name = "quit", Source = screen.Token, Nodes = { root } });
        PackRepository.Save(pack, _root);
        var nodes = All(JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)))["uis"]![0]!["nodes"]!).ToList();

        Assert.Equal(0.9f, (float?)nodes.Single(n => (string?)n["bind"] == "Button")["pressScale"]);
        Assert.NotNull(nodes.Single(n => (string?)n["bind"] == "Button (1)")["loop"]);
        // Control: the button beside them, untouched, is not written.
        Assert.DoesNotContain(nodes, n => (string?)n["bind"] == "Button (2)");
    }

    private static IEnumerable<UiNodeDef> Flatten(UiNodeDef node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
