using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A line's outfit and expression picked from buttons with pictures (the
/// author, 1.8.0): only what the speaker has, the line's own marked in every
/// theme, a value the speaker lacks still shown (marked wrong), an outfit
/// still able to come from a variable. And on the Characters tab, a face's
/// picture: picked once the face has a name, guessed from it until then, and
/// written into the pack only when the author has one.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PicturePickerTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-pictures-" + Guid.NewGuid().ToString("N"));

    public PicturePickerTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private const int TabBusts = MainTab.Characters, TabDialogues = MainTab.Dialogues;

    private MainViewModel Opened(MainWindow window, Action<ModPack>? more = null)
    {
        var pack = PackRepository.CreateEmpty("pictures.pack");
        var kiki = new CharacterDef { Key = "kiki", DisplayName = "Kiki", DefaultOutfit = "KikiBase" };
        kiki.Outfits.Add(new OutfitDef { Key = "base", GameObjectName = "KikiBase" });
        kiki.Outfits.Add(new OutfitDef { Key = "swim", GameObjectName = "KikiSwim" });
        kiki.Expressions.Add(new ActorExpressionDef { Key = "neutral", ExpressionGoName = "" });
        kiki.Expressions.Add(new ActorExpressionDef { Key = "Grin", ExpressionGoName = "Grin" });
        pack.Characters.Add(kiki);
        pack.Variables.Add(new PackVariableDef { Name = "kikiLook", Type = PackVariableType.String });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        var first = new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hi!", Outfit = "KikiSwim", Expression = "Grin" };
        first.Children.Add(2);
        d.Nodes.Add(first);
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "kiki", Text = "Hm.", Outfit = "KikiGone", Expression = "" });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        more?.Invoke(pack);
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = TabDialogues;
        vm.SelectedDialogue = vm.Dialogues.Single();
        WindowHarness.Pump();
        return vm;
    }

    private static void Select(MainViewModel vm, int id)
    {
        vm.SelectedNode = vm.SelectedDialogue!.Nodes.Single(n => n.Id == id);
        WindowHarness.Pump();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    /// <summary>The buttons drawn in a picker, with what each stands for.</summary>
    private static List<(string Value, Button Button, PictureChoice Choice)> Tiles(Window window, string picker)
        => Descendants<Button>((DependencyObject)window.FindName(picker))
           .Where(b => b.DataContext is PictureChoice && b.IsVisible)
           .Select(b => (((PictureChoice)b.DataContext).Value, b, (PictureChoice)b.DataContext)).ToList();

    private static Color Fill(Button b) => ((SolidColorBrush)b.Background).Color;

    [Fact]
    public void TheOutfitButtonsAreTheSpeakersAndShowWhichIsTheLines()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 1);
            var tiles = Tiles(window, "NodeOutfitPicker");
            _out.WriteLine(string.Join(", ", tiles.Select(t => $"{(t.Value == "" ? "(keep)" : t.Value)}{(t.Choice.IsSelected ? "*" : "")}")));
            Assert.Equal(new[] { "", "KikiBase", "KikiSwim" }, tiles.Select(t => t.Value));
            var picked = tiles.Single(t => t.Choice.IsSelected);
            Assert.Equal("KikiSwim", picked.Value);
            // Drawn differently from the rest - measured, not assumed.
            Assert.All(tiles.Where(t => !t.Choice.IsSelected), t => Assert.NotEqual(Fill(picked.Button), Fill(t.Button)));

            // Clicking another sets the line, and moves the mark.
            var other = tiles.Single(t => t.Value == "KikiBase").Button;
            other.Command.Execute(other.CommandParameter);
            WindowHarness.Pump();
            Assert.Equal("KikiBase", vm.SelectedNode!.Outfit);
            Assert.Equal("KikiBase", Tiles(window, "NodeOutfitPicker").Single(t => t.Choice.IsSelected).Value);
        });
    }

    [Fact]
    public void AnOutfitTheSpeakerDoesNotHaveIsShownMarkedWrong()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 2);
            var gone = Tiles(window, "NodeOutfitPicker").Single(t => t.Value == "KikiGone");
            Assert.True(gone.Choice.IsUnknown);
            Assert.True(gone.Choice.IsSelected);
            // Leaving it for a real one takes the wrong one off the row.
            var keep = Tiles(window, "NodeOutfitPicker").Single(t => t.Value == "").Button;
            keep.Command.Execute(keep.CommandParameter);
            WindowHarness.Pump();
            Assert.Equal("", vm.SelectedNode!.Outfit);
            Assert.DoesNotContain(Tiles(window, "NodeOutfitPicker"), t => t.Value == "KikiGone");
        });
    }

    [Fact]
    public void AnOutfitCanStillComeFromAVariable()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window, pack =>
            {
                pack.Dialogues[0].Nodes.Add(new DialogueNodeDef { Id = 3, Actor = "kiki", Text = "Ta-da.", Outfit = "$kikiLook" });
                pack.Dialogues[0].Nodes[1].Children.Add(3);
            });
            Select(vm, 1);
            var tick = (CheckBox)window.FindName("NodeOutfitVariableSwitch");
            var variable = (ComboBox)window.FindName("NodeOutfitVariablePicker");
            var buttons = (ItemsControl)window.FindName("NodeOutfitPicker");
            bool ButtonsStandAside() => !buttons.IsEnabled && buttons.Opacity < 0.5
                                        && Tiles(window, "NodeOutfitPicker").All(t => !t.Button.IsEnabled);

            // Like Tag: a tick box, and the list beside it only once ticked.
            Assert.True(tick.IsVisible);
            Assert.False(variable.IsVisible);
            Assert.False(ButtonsStandAside());   // the control

            tick.IsChecked = true;
            WindowHarness.Pump();
            Assert.True(variable.IsVisible);
            Assert.True(ButtonsStandAside());
            Assert.Equal("KikiSwim", vm.SelectedNode!.Outfit);   // ticking alone changes nothing

            Assert.Contains("$kikiLook", variable.Items.Cast<string>());
            variable.SelectedItem = "$kikiLook";
            WindowHarness.Pump();
            Assert.Equal("$kikiLook", vm.SelectedNode!.Outfit);
            Assert.DoesNotContain(Tiles(window, "NodeOutfitPicker"), t => t.Choice.IsSelected);

            // Another node with no variable: the list empties, and that is not
            // taken for an answer.
            Select(vm, 2);
            Assert.Equal("KikiGone", vm.SelectedNode!.Outfit);
            Assert.False(tick.IsChecked == true);
            Select(vm, 1);
            Assert.Equal("$kikiLook", vm.SelectedNode!.Outfit);

            // Unticked, the line has the button it had before.
            tick.IsChecked = false;
            WindowHarness.Pump();
            Assert.Equal("KikiSwim", vm.SelectedNode!.Outfit);
            Assert.False(variable.IsVisible);
            Assert.False(ButtonsStandAside());

            // A line that already takes its outfit from a variable opens ticked;
            // unticked, with nothing before it, it keeps what it is wearing.
            Select(vm, 3);
            Assert.True(tick.IsChecked == true);
            Assert.True(ButtonsStandAside());
            tick.IsChecked = false;
            WindowHarness.Pump();
            Assert.Equal("", vm.SelectedNode!.Outfit);
        });
    }

    [Fact]
    public void KeepSaysWhatItMeans()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 1);
            var tiles = Tiles(window, "NodeOutfitPicker");
            Assert.Equal(Loc.T("dialogues.outfitKeep.tip"), tiles.Single(t => t.Value == "").Button.ToolTip);
            // A bust's button shows its whole name, which the button may cut short.
            Assert.Equal("KikiSwim", tiles.Single(t => t.Value == "KikiSwim").Button.ToolTip);
        });
    }

    [Fact]
    public void TheFacesAreTheSpeakersWithNeutralFirstAndNoFaceIsNeutral()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 2);   // no expression
            var tiles = Tiles(window, "NodeExpressionPicker");
            _out.WriteLine(string.Join(", ", tiles.Select(t => $"{t.Value}[{t.Choice.Icon}]{(t.Choice.IsSelected ? "*" : "")}")));
            Assert.Equal("neutral", tiles[0].Value);
            Assert.True(tiles[0].Choice.IsSelected);
            Assert.Contains(tiles, t => t.Value == "Grin" && t.Choice.Icon == "smile");

            var grin = tiles.Single(t => t.Value == "Grin").Button;
            grin.Command.Execute(grin.CommandParameter);
            WindowHarness.Pump();
            Assert.Equal("Grin", vm.SelectedNode!.Expression);
        });
    }

    [Fact]
    public void AFacesPictureChangedOnTheCharactersTabShowsOnTheLine()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 1);
            var row = vm.Characters.Single(c => c.Key == "kiki").Expressions.Single(e => e.Key == "Grin");
            vm.SelectedTabIndex = TabBusts;
            WindowHarness.Pump();
            row.Icon = "laugh";
            vm.SelectedTabIndex = TabDialogues;
            WindowHarness.Pump();
            Assert.Equal("laugh", Tiles(window, "NodeExpressionPicker").Single(t => t.Value == "Grin").Choice.Icon);
        });
    }

    [Fact]
    public void ANewFacesPictureWaitsForItsNameThenIsGuessedFromIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var kiki = vm.Characters.Single(c => c.Key == "kiki");
            var face = kiki.AddExpression();
            Assert.False(face.IconUnlocked);
            Assert.Null(face.Model.Icon);

            face.Key = "Smirking";
            Assert.True(face.IconUnlocked);
            Assert.Equal("smug", face.Model.Icon);   // picked once, from the name

            face.Key = "Crying";                     // renamed after: what was picked stays
            Assert.Equal("smug", face.Icon);
            face.Icon = "cry";
            Assert.Equal("cry", face.Model.Icon);
        });
    }

    [Fact]
    public void AFaceWithNoPictureSavesAsItWasAndShowsTheGuess()
    {
        // Through the real save: a pack that never picked a picture must not
        // start carrying one because it was opened (CLAUDE.md, migrations).
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var grin = vm.Characters.Single(c => c.Key == "kiki").Expressions.Single(e => e.Key == "Grin");
            Assert.Equal("smile", grin.Icon);
            Assert.Null(grin.Model.Icon);
            Assert.True(vm.SavePack());
        });
        string manifest = File.ReadAllText(Path.Combine(_dir, "modpack.json"));
        Assert.DoesNotContain("\"icon\"", manifest);
    }

    [Fact]
    public void TheBustChildFollowsTheNameUntilThePencilSetsItsOwn()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var face = vm.Characters.Single(c => c.Key == "kiki").AddExpression();
            face.Key = "Wink";
            Assert.Equal("Wink", face.ExpressionGoName);

            vm.PromptForText = (_, _, _) => "WinkLeft";
            vm.EditExpressionChildCommand.Execute(face);
            Assert.Equal("WinkLeft", face.ExpressionGoName);
            face.Key = "Winking";
            Assert.Equal("WinkLeft", face.ExpressionGoName);   // set by hand: it stays
        });
    }

    [Fact]
    public void ANewLineASpeakerSaysStartsOnNeutral()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            Select(vm, 2);   // kiki, no expression
            vm.AddDialogueSiblingNodeCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal("neutral", vm.SelectedNode!.Expression);

            Select(vm, 1);   // a face carried stays that face
            vm.AddDialogueChildNodeCommand.Execute(null);
            WindowHarness.Pump();
            Assert.Equal("Grin", vm.SelectedNode!.Expression);
        });
    }
}
