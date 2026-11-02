using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// "Leave the busts at the end" (found testing a pack, 2026-11-01): on for a
/// dialogue made in the editor, off - as before - for one that never said.
/// </summary>
public sealed class LeaveBustsAtEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForge-leavebusts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_new_dialogue_leaves_its_busts_and_an_older_one_keeps_doing_what_it_did()
    {
        var vm = new MainViewModel();
        vm.AddDialogueCommand.Execute(null);
        var made = vm.SelectedDialogue!;
        Assert.True(made.LeaveBustsAtEnd);

        // One from before: nothing in the file says so.
        var older = new DialogueDef { Key = "older", DisplayName = "older" };
        older.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hi." });
        older.RootNodeIds.Add(1);
        var pack = PackRepository.CreateEmpty("busts");
        pack.Dialogues.Add(older);
        pack.Dialogues.Add(made.Model);
        PackRepository.Save(pack, _root);

        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        var dialogues = (JArray)saved["dialogues"]!;
        Assert.True((bool?)dialogues.Single(d => (string?)d["key"] == made.Key)["leaveBustsAtEnd"]);
        Assert.Null(dialogues.Single(d => (string?)d["key"] == "older")["leaveBustsAtEnd"]);

        var back = PackRepository.Load(_root);
        Assert.False(back.Dialogues.Single(d => d.Key == "older").LeaveBustsAtEnd);
        Assert.True(back.Dialogues.Single(d => d.Key == made.Key).LeaveBustsAtEnd);
    }
}
