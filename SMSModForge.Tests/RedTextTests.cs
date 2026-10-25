using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A field's text in red when it is irrefutably wrong, and only then (the
/// author, 1.8.0: "no assumptions"): a name that is none of the things its
/// list offers, a number that is not one, a path with no file at it. Not an
/// empty box, a <c>{placeholder}</c>, a <c>$variable</c>. Read off the colour
/// the text is drawn in.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class RedTextTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-redtext-" + Guid.NewGuid().ToString("N"));

    public RedTextTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MainViewModel Opened(MainWindow window)
    {
        var pack = PackRepository.CreateEmpty("redtext.pack");
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        pack.Sfx.Add(new SfxDef { Key = "Ping", DisplayName = "Ping" });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hi!", Tag = "start" });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "kiki", Text = "Again." });
        d.Nodes[0].Children.Add(2);
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);

        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        vm.SelectedTabIndex = 5;
        vm.SelectedDialogue = vm.Dialogues.Single();
        vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 2);
        WindowHarness.Pump();
        return vm;
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

    /// <summary>Whether the text is drawn in the danger colour - what is on
    /// screen, not what the field thinks.</summary>
    private static bool DrawnRed(Window window, Control field)
        => field.Foreground is SolidColorBrush b && window.FindResource("Theme.Danger") is SolidColorBrush danger
           && b.Color == danger.Color;

    private static void Type(ComboBox box, string text)
    {
        box.Text = text;
        WindowHarness.Pump();
    }

    [Fact]
    public void AnActorNobodyIsIsRedAndOnlyThat()
    {
        WindowHarness.Run(window =>
        {
            Opened(window);
            var actor = (ComboBox)window.FindName("NodeActorPicker");
            Assert.False(DrawnRed(window, actor));        // the control: a known name

            Type(actor, "Nobody");
            Assert.True(DrawnRed(window, actor));
            Assert.True(KnownValue.GetIsWrong(actor));

            foreach (string fine in new[] { "", "{speaker}", "$whoTalks", "KIKI" })
            {
                Type(actor, fine);
                _out.WriteLine($"'{fine}': red {DrawnRed(window, actor)}");
                Assert.False(DrawnRed(window, actor), $"'{fine}' is drawn as wrong");
            }
        });
    }

    [Fact]
    public void AJumpToATagNoLineHasIsRed()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            vm.SelectedNode!.JumpMode = JumpMode.Jump;
            WindowHarness.Pump();
            var jump = (ComboBox)window.FindName("NodeJumpToPicker");
            Type(jump, "start");
            Assert.False(DrawnRed(window, jump));
            Type(jump, "nowhere");
            Assert.True(DrawnRed(window, jump));
        });
    }

    [Fact]
    public void AnActionsUnknownSoundAndNotANumberAreRed()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            var action = vm.SelectedNode!.AddActionOnStart();
            action.Type = NodeActionTypes.PlaySFX;
            WindowHarness.Pump();
            window.UpdateLayout();

            var clip = Descendants<ComboBox>(window).Single(c => c.IsVisible && c.DataContext is ParamRowViewModel { Key: "clip" });
            var volume = Descendants<TextBox>(window).Single(t => t.IsVisible && t.DataContext is ParamRowViewModel { Key: "volume" });

            Type(clip, "Ping");
            Assert.False(DrawnRed(window, clip));
            Type(clip, "Pong");
            Assert.True(DrawnRed(window, clip));

            volume.Text = "0.8";
            WindowHarness.Pump();
            Assert.False(DrawnRed(window, volume));
            volume.Text = "loud";
            WindowHarness.Pump();
            Assert.True(DrawnRed(window, volume));
        });
    }

    [Fact]
    public void APathWithNoFileIsRed()
    {
        WindowHarness.Run(window =>
        {
            var vm = Opened(window);
            File.WriteAllBytes(Path.Combine(_dir, "here.png"), new byte[] { 1 });
            vm.SelectedTabIndex = 6;
            vm.AddSceneCommand.Execute(null);
            WindowHarness.Pump();
            var picker = Descendants<PathPickerBox>(window).First(p => p.IsVisible);
            var box = Descendants<TextBox>(picker).Single();

            picker.PathText = "here.png";
            WindowHarness.Pump();
            Assert.False(DrawnRed(window, box));
            picker.PathText = "gone.png";
            WindowHarness.Pump();
            Assert.True(DrawnRed(window, box));
            Assert.True(picker.IsMissing);
        });
    }
}
