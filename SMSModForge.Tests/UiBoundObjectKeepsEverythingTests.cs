using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Everything an author can set on one of the game's own objects has to come
/// back: through the save, which keeps only what differs from the game, and
/// through reopening, which lays what was kept over a fresh copy of the screen.
/// <para/>
/// Both passes list the settings by hand, and both had fallen behind the model:
/// a click, a layout or a loop on a game's object came back from a reopen gone,
/// and the next save wrote the loss to disk. So this walks the model's own saved
/// settings by reflection. A new one that a pass forgets fails here, by name,
/// without anyone having to remember this test exists.
/// </summary>
public sealed class UiBoundObjectKeepsEverythingTests
{
    private readonly ITestOutputHelper _out;
    public UiBoundObjectKeepsEverythingTests(ITestOutputHelper output) => _out = output;

    /// <summary>The settings that say which object this is or how the save
    /// recorded it, rather than what the author set on it. Each is checked by a
    /// test of its own in UiOrderTests or by the rest of this file.</summary>
    private static readonly string[] NotSettings =
    {
        "name", "bind", "children", "siblingIndex",
        "overrideRect", "overrideImage", "overrideText", "overrideActive",
    };

    /// <summary>A value for one saved setting, unlike what any object in the game
    /// has - so it being there afterwards can only mean it survived.</summary>
    private static void SetDistinct(UiNodeDef node, string setting)
    {
        switch (setting)
        {
            case "rect": node.Rect.Position = new[] { 321f, -123f }; break;
            case "image": node.Image = new UiImageDef { Sprite = "Rounded", Type = "Tiled", Tint = "#123456FF" }; break;
            case "text": node.Text = new UiTextDef { Value = "kept", Font = "Curse Casual SDF", Size = 33f }; break;
            case "alpha": node.Alpha = 0.37f; break;
            case "shadow": node.Shadow = new UiEffectDef { Color = "#11223344", Distance = new[] { 7f, -7f } }; break;
            case "outline": node.Outline = new UiEffectDef { Color = "#55667788", Distance = new[] { 9f, -9f } }; break;
            case "clipChildren": node.ClipChildren = true; break;
            case "startActive": node.StartActive = !node.StartActive; break;
            // VariableEquals would be rewritten by the load's own migration.
            case "activeConditions": node.ActiveConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.LevelActive }); break;
            case "clickConditions": node.ClickConditions.Add(new NodeConditionDef { Type = NodeConditionTypes.LevelActive }); break;
            case "components": node.Components.Add(new ComponentDef { Type = "LayoutElement" }); break;
            case "onClick": node.OnClick.Add(new NodeActionDef { Type = NodeActionTypes.SetVariable }); break;
            case "hoverTint": node.HoverTint = "#AABBCCDD"; break;
            case "clickSound": node.ClickSound = "kept-sound"; break;
            case "open": node.Open = new UiOpenDef { Fade = true, Duration = 0.77f }; break;
            case "close": node.Close = new UiOpenDef { Fade = true, Duration = 0.66f }; break;
            case "layout": node.Layout = new UiLayoutDef { Kind = UiLayoutKinds.All.First() }; break;
            case "upright": node.Upright = true; break;
            case "pressScale": node.PressScale = 0.91f; break;
            case "loop": node.Loop = new UiLoopDef { Alpha = new[] { 0.2f, 0.8f }, Seconds = 3.3f }; break;
            default:
                throw new InvalidOperationException(
                    $"\"{setting}\" is a new saved setting of a UI object. Give it a value here, and make sure "
                    + "VanillaUiDelta (Asserts, Copy) and VanillaUiSeed (Apply, ResetTo) both carry it.");
        }
    }

    /// <summary>How the model saves one setting, for comparing without caring
    /// which object holds it.</summary>
    private static string Saved(UiNodeDef node, string setting)
    {
        var property = typeof(UiNodeDef).GetProperties()
            .First(p => p.GetCustomAttributes(typeof(JsonPropertyAttribute), false)
                         .Cast<JsonPropertyAttribute>().Any(a => a.PropertyName == setting));
        return JsonConvert.SerializeObject(property.GetValue(node));
    }

    private static string[] SavedSettings()
        => typeof(UiNodeDef).GetProperties()
            .SelectMany(p => p.GetCustomAttributes(typeof(JsonPropertyAttribute), false)
                              .Cast<JsonPropertyAttribute>().Select(a => a.PropertyName!))
            .Except(NotSettings)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

    private static UiNodeDef? Find(UiNodeDef node, string bind)
        => node.Bind == bind ? node : node.Children.Select(c => Find(c, bind)).FirstOrDefault(f => f != null);

    public static TheoryData<string> Settings()
    {
        var data = new TheoryData<string>();
        foreach (string setting in SavedSettings()) data.Add(setting);
        return data;
    }

    /// <summary>On a game's object: set, written to disk by the real save, read
    /// back, and opened the way the editor opens a pack.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void A_setting_on_a_games_object_comes_back_from_a_save_and_a_reopen(string setting)
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var pack = PackRepository.CreateEmpty("keeps.everything");
        var ui = new UiViewModel(new UiDef { Source = "vanillaui:9_MainCanvas/Quitagme" });
        pack.Uis.Add(ui.Model);

        var target = ui.Nodes[0].Children[0];
        string bind = target.Model.Bind;
        string before = Saved(target.Model, setting);
        SetDistinct(target.Model, setting);
        string wanted = Saved(target.Model, setting);
        Assert.NotEqual(before, wanted);      // the control: this did change something

        Assert.True(ui.HasChanges(target.Model),
                    $"\"{setting}\" changed and the tree would not mark the object as changed");

        string folder = Path.Combine(Path.GetTempPath(), "smsmf-keeps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            PackRepository.Save(pack, folder);
            string written = File.ReadAllText(Path.Combine(folder, "modpack.json"));

            // What the disk holds, before any editor code has had a say.
            var stored = PackRepository.Deserialize(written).Uis.Single();
            var storedNode = stored.Nodes.Select(n => Find(n, bind)).FirstOrDefault(n => n != null);
            Assert.True(storedNode != null, $"\"{setting}\" alone did not make the save keep the object");
            Assert.Equal(wanted, Saved(storedNode!, setting));

            // What an author gets when they open it again.
            var reopened = new UiViewModel(stored);
            var back = Find(reopened.Nodes[0].Model, bind);
            Assert.NotNull(back);
            _out.WriteLine($"{setting}: wanted {wanted}, reopened as {Saved(back!, setting)}");
            Assert.Equal(wanted, Saved(back!, setting));

            // And saving that again changes nothing: a loss on the second save
            // is the one that reaches the author's file.
            var second = PackRepository.CreateEmpty("keeps.everything");
            second.Uis.Add(reopened.Model);
            PackRepository.Save(second, folder);
            var storedAgain = PackRepository.Deserialize(File.ReadAllText(Path.Combine(folder, "modpack.json"))).Uis.Single();
            var nodeAgain = storedAgain.Nodes.Select(n => Find(n, bind)).FirstOrDefault(n => n != null);
            Assert.NotNull(nodeAgain);
            Assert.Equal(wanted, Saved(nodeAgain!, setting));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Put back the way the game has it, an object asserts nothing: a
    /// setting Reset leaves behind can never be taken off again.</summary>
    [Theory]
    [MemberData(nameof(Settings))]
    public void Reset_takes_a_setting_off_again(string setting)
    {
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var ui = new UiViewModel(new UiDef { Source = "vanillaui:9_MainCanvas/Quitagme" });
        var target = ui.Nodes[0].Children[0];
        Assert.False(ui.HasChanges(target.Model));

        SetDistinct(target.Model, setting);
        Assert.True(ui.HasChanges(target.Model));   // the control

        ui.Reset(target);
        Assert.False(ui.HasChanges(target.Model), $"\"{setting}\" is still there after Reset");
    }
}
