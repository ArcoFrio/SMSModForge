using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Opening another pack asks about unsaved changes - including the one still
/// being typed (the author, 1.7.0: "if you open a modpack from recent files, it
/// doesn't give you a prompt for unsaved changes").
/// <para/>
/// It did ask, when the pack had changed. What it did not see was an edit
/// still in a box that writes only when it is left, like a line's timeout: a
/// menu item does not take the keyboard from the box it is clicked over, so
/// the edit was not in the pack yet, nothing looked unsaved, and opening the
/// recent pack threw it away.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UnsavedPromptTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _a, _b;

    public UnsavedPromptTests(ITestOutputHelper o)
    {
        _out = o;
        _a = Path.Combine(Path.GetTempPath(), "smsmodforge-unsaved-a-" + Guid.NewGuid().ToString("N"));
        _b = Path.Combine(Path.GetTempPath(), "smsmodforge-unsaved-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_a);
        Directory.CreateDirectory(_b);

        var a = PackRepository.CreateEmpty("unsaved.a");
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "hi", Duration = NodeDurationMode.Timeout, Timeout = 3 });
        d.RootNodeIds.Add(1);
        a.Dialogues.Add(d);
        PackRepository.Save(a, _a);
        PackRepository.Save(PackRepository.CreateEmpty("unsaved.b"), _b);
    }

    public void Dispose()
    {
        try { Directory.Delete(_a, true); } catch (IOException) { }
        try { Directory.Delete(_b, true); } catch (IOException) { }
    }

    private static TextBox? BoxFor(DependencyObject root, string path)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox tb && tb.IsVisible
                && tb.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == path) return tb;
            var found = BoxFor(child, path);
            if (found != null) return found;
        }
        return null;
    }

    [Fact]
    public void AnEditStillBeingTypedCountsAsUnsavedWhenARecentPackIsOpened()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_a);
            vm.SelectedTabIndex = 5;   // Dialogues
            vm.SelectedDialogue = vm.Dialogues.Single(x => x.Key == "chat");
            vm.SelectedNode = vm.SelectedDialogue.Nodes[0];
            WindowHarness.Pump();

            var box = BoxFor(window, "Timeout");
            Assert.NotNull(box);
            Assert.True(box!.Focus() || Keyboard.Focus(box) == box, "the timeout box could not be given the keyboard");
            box.Text = "7";
            WindowHarness.Pump();
            // The control: typed, and not yet in the pack.
            Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(3f, vm.Pack.Dialogues[0].Nodes[0].Timeout);

            // A recent pack, opened while the box still has the keyboard. With
            // nobody to answer, the question counts as Cancel.
            vm.OpenRecentCommand.Execute(_b);
            WindowHarness.Pump();

            _out.WriteLine($"pack after: {vm.Pack.PackId}, timeout {vm.Pack.Dialogues.FirstOrDefault()?.Nodes[0].Timeout}");
            Assert.Equal("unsaved.a", vm.Pack.PackId);       // it asked, and nothing was thrown away
            Assert.Equal(7f, vm.Pack.Dialogues[0].Nodes[0].Timeout);
            Assert.True(vm.HasUnsavedChanges);
        });
    }
}
