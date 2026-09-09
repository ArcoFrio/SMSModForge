using System.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Controls that edit a record wait until there is a record to edit.
/// <para/>
/// Reported from a tutorial recording: nodes could be added to a dialogue that
/// did not exist yet, and most tabs let their detail pane be typed into with
/// nothing selected — the edit going into a binding with no target and simply
/// vanishing.
/// <para/>
/// The awkward case is a "+ Vanilla" row before a conversation has been chosen
/// for it. It IS a selected dialogue, so a null check passes, but it has no
/// lines and nothing to attach a change to — and picking a conversation
/// afterwards replaces whatever was typed.
/// </summary>
public sealed class EmptyStateGatingTests
{
    private readonly ITestOutputHelper _out;
    public EmptyStateGatingTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void NothingCanBeAddedToADialogueThatIsNotThere()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            Assert.Null(vm.SelectedDialogue);

            Assert.False(vm.AddDialogueRootNodeCommand.CanExecute(null));
            Assert.False(vm.AddDialogueChildNodeCommand.CanExecute(null));
            Assert.False(vm.AddDialogueSiblingNodeCommand.CanExecute(null));
            Assert.False(vm.RemoveDialogueNodeCommand.CanExecute(null));
            Assert.False(vm.AddDialogueStartConditionCommand.CanExecute(null));
            Assert.False(vm.RemoveDialogueCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AVanillaRowWaitsUntilAConversationIsChosen()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            var row = new DialogueViewModel(new DialogueDef()) { WantsVanilla = true };
            vm.Dialogues.Add(row);
            vm.SelectedVanillaDialogue = row;
            WindowHarness.Pump();

            // Selected, but there is nothing behind it yet.
            Assert.Same(row, vm.SelectedDialogue);
            Assert.False(row.IsVanillaBased);
            Assert.False(row.IsEditable);

            Assert.False(vm.AddDialogueRootNodeCommand.CanExecute(null));
            Assert.False(vm.AddDialogueStartConditionCommand.CanExecute(null));

            // Choosing one opens it up.
            row.VanillaSource = VanillaDialogueCatalog.Find("8_Room_Talk/Beach/AnnaBeachDefault");
            WindowHarness.Pump();

            Assert.True(row.IsEditable);
            Assert.True(vm.AddDialogueRootNodeCommand.CanExecute(null));
            Assert.True(vm.AddDialogueStartConditionCommand.CanExecute(null));
        });
    }

    [Fact]
    public void ADialogueOfThePacksOwnNeverWaits()
    {
        // The control. It IS the thing being made, so there is nothing to
        // wait for - gating it would make a new dialogue unusable.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();

            Assert.True(vm.SelectedDialogue!.IsEditable);
            Assert.True(vm.AddDialogueRootNodeCommand.CanExecute(null));
        });
    }

    [Fact]
    public void NodeControlsWaitForANode()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddDialogueCommand.Execute(null);
            vm.SelectedNode = null;
            WindowHarness.Pump();

            Assert.False(vm.AddDialogueChildNodeCommand.CanExecute(null));
            Assert.False(vm.AddDialogueSiblingNodeCommand.CanExecute(null));
            Assert.False(vm.RemoveDialogueNodeCommand.CanExecute(null));
            Assert.False(vm.AddNodeActionOnStartCommand.CanExecute(null));
            Assert.False(vm.AddNodeActionOnFinishCommand.CanExecute(null));

            // A root can still be added: that is how the first node arrives.
            Assert.True(vm.AddDialogueRootNodeCommand.CanExecute(null));

            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();
            Assert.NotNull(vm.SelectedNode);

            Assert.True(vm.AddDialogueChildNodeCommand.CanExecute(null));
            Assert.True(vm.AddNodeActionOnStartCommand.CanExecute(null));
        });
    }

    [Fact]
    public void AChoiceOptionIsNotAskedAboutASpeaker()
    {
        // An option is a button rather than a line: the actor, expression and
        // outfit on it change nothing, so setting them is an invitation to
        // wonder why nothing happened.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddDialogueCommand.Execute(null);
            vm.AddDialogueRootNodeCommand.Execute(null);
            WindowHarness.Pump();

            var prompt = vm.SelectedNode!;
            prompt.Kind = DialogueNodeKind.Choice;
            vm.AddDialogueChildNodeCommand.Execute(null);
            WindowHarness.Pump();

            var option = vm.SelectedNode!;
            _out.WriteLine($"option: IsChoiceChild={option.IsChoiceChild}");

            Assert.True(option.IsChoiceChild);
            Assert.False(vm.SelectedNodeShowsSpeaker);

            // And the prompt itself still is asked - it is a real line.
            vm.SelectedNode = prompt;
            WindowHarness.Pump();
            Assert.False(prompt.IsChoiceChild);
            Assert.True(vm.SelectedNodeShowsSpeaker);
        });
    }
}
