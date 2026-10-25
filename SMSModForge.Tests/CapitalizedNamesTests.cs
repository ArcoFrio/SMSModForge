using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Actor names in the node editor start with a capital letter (the author,
/// 1.8.0: "purely aesthetic") - in the Actor box, its list and the node rows
/// - and nothing in the pack changes case: a speaker's key is what the line
/// holds, and the box shows it as it is the moment it is clicked into.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class CapitalizedNamesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-capitals-" + Guid.NewGuid().ToString("N"));

    public CapitalizedNamesTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    [Fact]
    public void ActorNamesReadWithACapitalAndTheLineKeepsItsKey()
    {
        WindowHarness.Run(window =>
        {
            var pack = PackRepository.CreateEmpty("capitals.pack");
            pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "kiki" });
            var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hi!" });
            d.RootNodeIds.Add(1);
            pack.Dialogues.Add(d);
            PackRepository.Save(pack, _dir);

            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = MainTab.Dialogues;
            vm.SelectedDialogue = vm.Dialogues.Single();
            vm.SelectedNode = vm.SelectedDialogue.Nodes.Single();
            WindowHarness.Pump();
            window.UpdateLayout();
            string saved = PackRepository.SerializeAsSaved(vm.Pack);

            // The box: "Kiki" drawn over it, its own "kiki" hidden underneath.
            var picker = (ComboBox)window.FindName("NodeActorPicker");
            var text = (TextBox)picker.Template.FindName("PART_EditableTextBox", picker);
            Assert.Equal("kiki", text.Text);
            Assert.Equal("Kiki", CapitalizedBox.ShownOver(picker));
            Assert.Equal(0, text.Opacity);
            Assert.True(InkIn(window, text) > 0, "nothing is drawn where the box's name is");

            // The node row, and the list's items.
            Assert.Equal("[Kiki]", vm.SelectedNode!.SpeakerPrefix);
            var item = new ContentPresenter { Content = "kiki", ContentTemplate = (DataTemplate)window.FindResource("ActorWithFaceTemplate") };
            item.Measure(new Size(400, 40));
            item.Arrange(new Rect(0, 0, 400, 40));
            WindowHarness.Pump();
            Assert.Contains(Descendants<TextBlock>(item), t => t.Text == "Kiki");

            // The control: the Game look shows the name as the game does.
            Assert.Equal("kiki", vm.SelectedNode!.SpeakerName);

            // Clicked into, the box shows what is there, to be typed over.
            Keyboard.Focus(text);
            WindowHarness.Pump();
            Assert.Null(CapitalizedBox.ShownOver(picker));
            Assert.Equal(1, text.Opacity);

            // Nothing in the pack changed case.
            Assert.Equal("kiki", vm.SelectedNode!.Actor);
            Assert.Equal(saved, PackRepository.SerializeAsSaved(vm.Pack));
        });
    }

    /// <summary>How many pixels inside the box differ from the box's own
    /// background, rendered as the window draws it - the adorner included.</summary>
    private static int InkIn(Window window, FrameworkElement box)
    {
        var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(window);
        var at = box.TranslatePoint(new Point(0, 0), window);
        int x0 = (int)at.X + 2, y0 = (int)at.Y + 2, w = (int)box.ActualWidth - 4, h = (int)box.ActualHeight - 4;
        if (w <= 0 || h <= 0) return 0;
        var pixels = new byte[w * h * 4];
        bmp.CopyPixels(new Int32Rect(x0, y0, w, h), pixels, w * 4, 0);
        // The corner pixel is the box's background; ink is anything well apart from it.
        int bg = pixels[0] + pixels[1] + pixels[2], ink = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (Math.Abs(pixels[i] + pixels[i + 1] + pixels[i + 2] - bg) > 150) ink++;
        return ink;
    }
}
