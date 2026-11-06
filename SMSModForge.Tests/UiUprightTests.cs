using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// "Stays upright" on a UI object (found testing a pack, 2026-11-02): what the
/// editor writes. The game side is the in-game suite's F7.
/// </summary>
public sealed class UiUprightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-upright-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static IEnumerable<JObject> All(JToken nodes)
        => nodes.OfType<JObject>().SelectMany(n => new[] { n }.Concat(n["children"] is JArray c ? All(c) : Enumerable.Empty<JObject>()));

    [Fact]
    public void An_upright_object_on_one_of_the_games_screens_is_saved_and_one_left_alone_is_not()
    {
        // One of the game's screens, opened as the editor opens one: every
        // object bound to the game's. Only the one asked to stay upright has
        // anything to say.
        var screen = VanillaUiCatalog.Find("vanillaui:9_MainCanvas/Quitagme")!;
        var root = VanillaUiSeed.FromBase(VanillaUiLibrary.Node(screen)!);
        UiNodeDef At(string bind) => Flatten(root).Single(n => n.Bind == bind);
        At("Button").Upright = true;

        var pack = PackRepository.CreateEmpty("upright");
        pack.Uis.Add(new UiDef { Id = "quit", Name = "quit", Source = screen.Token, Nodes = { root } });
        PackRepository.Save(pack, _root);
        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        var nodes = All(saved["uis"]![0]!["nodes"]!).ToList();

        Assert.True((bool?)nodes.Single(n => (string?)n["bind"] == "Button")["upright"]);
        // Control: the button beside it, untouched, is not written at all.
        Assert.DoesNotContain(nodes, n => (string?)n["bind"] == "Button (2)");

        var back = PackRepository.Load(_root).Uis.Single();
        Assert.True(Flatten(back.Nodes.Single()).Single(n => n.Bind == "Button").Upright);
    }

    private static IEnumerable<UiNodeDef> Flatten(UiNodeDef node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
