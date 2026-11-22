using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What undo costs on a large pack (a probe: it measures, and holds nothing).
/// The pack is named by <c>SMSMODFORGE_UNDO_PROBE_PACK</c> (a folder with a
/// modpack.json); without it the probe does nothing. Opened, never saved.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class UndoCostProbe
{
    private readonly ITestOutputHelper _out;
    public UndoCostProbe(ITestOutputHelper o) => _out = o;

    private static int Count(UiNodeViewModel n) => 1 + n.Children.Sum(Count);

    private static double Ms(Action a, int runs = 1)
    {
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) a();
        return clock.Elapsed.TotalMilliseconds / runs;
    }

    [Fact]
    public void WhatUndoCostsOnALargePack()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_UNDO_PROBE_PACK");
        if (string.IsNullOrWhiteSpace(dir) || !File.Exists(Path.Combine(dir, "modpack.json"))) return;
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            double open = Ms(() => vm.OpenPackFromPath(dir));
            WindowHarness.Pump();
            _out.WriteLine($"opened in {open:0} ms: {vm.Dialogues.Count} dialogues, {vm.Dialogues.Sum(d => d.Nodes.Count)} lines, {vm.Variables.Count} variables, "
                           + $"{vm.Places.Count} places, {vm.VanillaExtensions.Count} extensions, {vm.Characters.Count} characters, {vm.Npcs.Count} NPCs, {vm.Scenes.Count} scenes, {vm.Quests.Count} quests");
            string json = "";
            double serialize = Ms(() => json = PackRepository.Serialize(vm.Pack), 5);
            _out.WriteLine($"the whole pack written out: {serialize:0.0} ms, {json.Length / 1048576.0:0.0} million characters");
            double saved = Ms(() => PackRepository.SerializeAsSaved(vm.Pack), 3);
            _out.WriteLine($"... as saved: {saved:0.0} ms");
            double read = Ms(() => PackRepository.Deserialize(json), 3);
            _out.WriteLine($"read back: {read:0.0} ms");
            PackPieces.OneCoreForTests = true;
            double oneCore = Ms(() => PackPieces.Take(vm.Pack, vm.Undo.Baseline), 3);
            PackPieces.OneCoreForTests = false;
            double allCores = Ms(() => PackPieces.Take(vm.Pack, vm.Undo.Baseline), 5);
            _out.WriteLine($"taken in pieces: {oneCore:0.0} ms on one core, {allCores:0.0} ms on all; put together again: {Ms(() => vm.Undo.Baseline.ToJson(), 3):0.0} ms");
            // Which pieces take the time: the slowest written out on their own.
            {
                var settings = PackRepository.ManifestSettings;
                var slow = new System.Collections.Generic.List<(double Ms, string What)>();
                void Time(string what, object? value)
                {
                    var c = Stopwatch.StartNew();
                    string text = Newtonsoft.Json.JsonConvert.SerializeObject(value, settings);
                    slow.Add((c.Elapsed.TotalMilliseconds, $"{what} ({text.Length / 1000} thousand characters)"));
                }
                for (int i = 0; i < vm.Pack.Dialogues.Count; i++) Time("dialogue " + i, vm.Pack.Dialogues[i]);
                for (int i = 0; i < vm.Pack.VanillaExtensions.Count; i++) Time("extension " + i, vm.Pack.VanillaExtensions[i]);
                for (int i = 0; i < vm.Pack.Places.Count; i++) Time("place " + i, vm.Pack.Places[i]);
                for (int i = 0; i < vm.Pack.Uis.Count; i++) Time("ui " + i, vm.Pack.Uis[i]);
                for (int i = 0; i < vm.Pack.Characters.Count; i++) Time("character " + i, vm.Pack.Characters[i]);
                Time("scenes (all)", vm.Pack.Scenes);
                Time("variables (all)", vm.Pack.Variables);
                Time("quests (all)", vm.Pack.Quests);
                Time("rules (all)", vm.Pack.IntegrationRules);
                _out.WriteLine("the slowest pieces: " + string.Join("; ", slow.OrderByDescending(x => x.Ms).Take(10).Select(x => $"{x.What} {x.Ms:0.0} ms")));
                _out.WriteLine($"all of those: {slow.Sum(x => x.Ms):0.0} ms");
            }
            double same = Ms(() => vm.Undo.Checkpoint(), 5);
            _out.WriteLine($"a checkpoint that looks, nothing changed: {same:0.0} ms");
            double untouched = Ms(() => vm.Undo.CheckpointIfTouched(), 5);
            _out.WriteLine($"a checkpoint with nothing said to have changed: {untouched:0.000} ms");

            var tabs = (TabControl)window.FindName("MainTabs");
            // A command that changes nothing in the pack (a selection), and one that does.
            tabs.SelectedIndex = MainTab.Dialogues;
            WindowHarness.Pump();
            vm.SelectedDialogue = vm.Dialogues.OrderByDescending(d => d.Nodes.Count).First();
            WindowHarness.Pump();
            var node = vm.SelectedDialogue.Nodes.First(n => !string.IsNullOrEmpty(n.Text));
            vm.SelectedNode = node;
            WindowHarness.Pump();
            vm.Undo.Checkpoint();
            int depth = vm.Undo.Depth;
            string was = node.Text;
            double edit = Ms(() => { node.Text = was + " x"; vm.Undo.Checkpoint(); });
            _out.WriteLine($"a line's text changed and the step taken: {edit:0.0} ms (steps {depth} -> {vm.Undo.Depth})");
            double undo = Ms(() => vm.UndoCommand.Execute(null));
            WindowHarness.Pump();
            _out.WriteLine($"undo of it: {undo:0} ms ({vm.LastRestore}: {vm.LastRestoreTimes}); the text back: {vm.SelectedNode?.Text == was}");
            double redo = Ms(() => vm.RedoCommand.Execute(null));
            WindowHarness.Pump();
            _out.WriteLine($"redo: {redo:0} ms ({vm.LastRestore}); the text again: {vm.SelectedNode?.Text == was + " x"}");
            vm.UndoCommand.Execute(null);
            WindowHarness.Pump();

            // A variable added by its command.
            tabs.SelectedIndex = MainTab.Variables;
            WindowHarness.Pump();
            double add = Ms(() => vm.AddVariableCommand.Execute(null));
            WindowHarness.Pump();
            _out.WriteLine($"Add variable (its command, with the steps before and after): {add:0} ms");
            double undoAdd = Ms(() => vm.UndoCommand.Execute(null));
            WindowHarness.Pump();
            _out.WriteLine($"undo of it: {undoAdd:0} ms ({vm.LastRestore}: {vm.LastRestoreTimes})");

            // Each kind of edit, undone and redone: how long the press takes, and how it was put back.
            void Timed(string what, int tab, Action select, Action edit)
            {
                tabs.SelectedIndex = tab;
                WindowHarness.Pump();
                select();
                WindowHarness.Pump();
                vm.Undo.Checkpoint();
                string before = PackRepository.Serialize(vm.Pack);
                double did = Ms(() => { edit(); vm.Undo.Checkpoint(); });
                WindowHarness.Pump();
                if (PackRepository.Serialize(vm.Pack) == before) { _out.WriteLine($"{what}: changed nothing"); return; }
                double undo = Ms(() => vm.UndoCommand.Execute(null));
                string way = vm.LastRestore, times = vm.LastRestore == "sections" ? vm.LastRestoreTimes : vm.LastRestorePhases;
                double settle = Ms(WindowHarness.Pump);
                bool back = PackRepository.Serialize(vm.Pack) == before;
                double redo = Ms(() => vm.RedoCommand.Execute(null));
                string wayRedo = vm.LastRestore + ": " + (vm.LastRestore == "sections" ? vm.LastRestoreTimes : vm.LastRestorePhases);
                double settleRedo = Ms(WindowHarness.Pump);
                vm.UndoCommand.Execute(null);
                WindowHarness.Pump();
                _out.WriteLine($"{what}: the edit and its step {did:0} ms; undo {undo:0} ms + {settle:0} ms for the page ({way}: {times}){(back ? "" : " NOT AS BEFORE")}; redo {redo:0} + {settleRedo:0} ms ({wayRedo})");
            }

            var big = vm.Dialogues.OrderByDescending(d => d.Nodes.Count).First();
            // What picking a record costs by itself: an undo goes to the record its step was made on.
            {
                tabs.SelectedIndex = MainTab.Dialogues;
                WindowHarness.Pump();
                var small = vm.Dialogues.OrderBy(d => d.Nodes.Count).First();
                for (int round = 0; round < 2; round++)
                {
                    vm.SelectedDialogue = small;
                    WindowHarness.Pump();
                    double pick = Ms(() => vm.SelectedDialogue = big);
                    double page = Ms(WindowHarness.Pump);
                    double line = Ms(() => vm.SelectedNode = big.Nodes[big.Nodes.Count / 2]);
                    double linePage = Ms(WindowHarness.Pump);
                    _out.WriteLine($"picking the largest dialogue ({big.Nodes.Count} lines): {pick:0} ms + {page:0} ms for the page; a line of it: {line:0} + {linePage:0} ms");
                }
                // What showing it costs, piece by piece: the speller, and the rows drawn the game's way instead.
                void Showing(string how)
                {
                    vm.SelectedDialogue = small;
                    WindowHarness.Pump();
                    double pick = Ms(() => vm.SelectedDialogue = big);
                    double page = Ms(WindowHarness.Pump);
                    _out.WriteLine($"picking the largest dialogue, {how}: {pick:0} ms + {page:0} ms for the page");
                }
                bool spell = vm.SpellCheckNodeText, game = vm.GameLookNodeRows;
                try
                {
                    vm.SpellCheckNodeText = false;
                    WindowHarness.Pump();
                    Showing("no spelling checked");
                    vm.SpellCheckNodeText = spell;
                    vm.GameLookNodeRows = !game;
                    WindowHarness.Pump();
                    Showing(game ? "plain rows" : "the game's look on the rows");
                }
                finally
                {
                    vm.SpellCheckNodeText = spell;
                    vm.GameLookNodeRows = game;
                    WindowHarness.Pump();
                }
                tabs.SelectedIndex = MainTab.Variables;
                WindowHarness.Pump();
                if (vm.Variables.Count > 1)
                {
                    vm.SelectedVariable = vm.Variables[0];
                    WindowHarness.Pump();
                    double pick = Ms(() => vm.SelectedVariable = vm.Variables[vm.Variables.Count / 2]);
                    _out.WriteLine($"picking a variable: {pick:0} ms + {Ms(WindowHarness.Pump):0} ms for the page");
                }
                tabs.SelectedIndex = MainTab.Characters;
                WindowHarness.Pump();
                if (vm.Characters.Count > 1)
                {
                    vm.SelectedCharacter = vm.Characters[0];
                    WindowHarness.Pump();
                    double pick = Ms(() => vm.SelectedCharacter = vm.Characters[vm.Characters.Count / 2]);
                    _out.WriteLine($"picking a character: {pick:0} ms + {Ms(WindowHarness.Pump):0} ms for the page");
                }
                tabs.SelectedIndex = MainTab.Npcs;
                WindowHarness.Pump();
                if (vm.Npcs.Count > 1)
                {
                    vm.SelectedNpc = vm.Npcs[0];
                    WindowHarness.Pump();
                    double pick = Ms(() => vm.SelectedNpc = vm.Npcs[vm.Npcs.Count / 2]);
                    _out.WriteLine($"picking an NPC: {pick:0} ms + {Ms(WindowHarness.Pump):0} ms for the page");
                }
                double tab = Ms(() => tabs.SelectedIndex = MainTab.Dialogues);
                _out.WriteLine($"going to the Dialogues tab: {tab:0} ms + {Ms(WindowHarness.Pump):0} ms for the page");
            }
            Timed("a line's text", MainTab.Dialogues, () => { vm.SelectedDialogue = big; vm.SelectedNode = big.Nodes[big.Nodes.Count / 2]; }, () => vm.SelectedNode!.Text += " x");
            Timed("a line added (+ Child)", MainTab.Dialogues, () => { vm.SelectedDialogue = big; vm.SelectedNode = big.Nodes[big.Nodes.Count / 2]; }, () => vm.AddDialogueChildNodeCommand.Execute(null));
            Timed("an action added to a line", MainTab.Dialogues, () => { vm.SelectedDialogue = big; vm.SelectedNode = big.Nodes[big.Nodes.Count / 2]; }, () => vm.AddNodeActionOnStartCommand.Execute(null));
            Timed("a dialogue added", MainTab.Dialogues, () => { }, () => vm.AddDialogueCommand.Execute(null));
            Timed("a variable added", MainTab.Variables, () => { }, () => vm.AddVariableCommand.Execute(null));
            if (vm.Variables.Count > 0)
                Timed("a variable's default value", MainTab.Variables, () => vm.SelectedVariable = vm.Variables[vm.Variables.Count / 2], () => vm.SelectedVariable!.DefaultValue += "1");
            if (vm.Characters.Count > 0)
                Timed("a character's name colour", MainTab.Characters, () => vm.SelectedCharacter = vm.Characters[vm.Characters.Count / 2], () => vm.SelectedCharacter!.NameColor = vm.SelectedCharacter.NameColor == "#336699" ? "#996633" : "#336699");
            Timed("a character added", MainTab.Characters, () => { }, () => vm.AddCharacterCommand.Execute(null));
            if (vm.Scenes.Count > 0)
                Timed("a scene added", MainTab.Scenes, () => { }, () => vm.AddSceneCommand.Execute(null));
            Timed("a rule added", MainTab.Integration, () => { }, () => vm.AddIntegrationRuleCommand.Execute(null));
            Timed("a quest added", MainTab.Quests, () => { }, () => vm.AddQuestCommand.Execute(null));
            Timed("an NPC added", MainTab.Npcs, () => { }, () => vm.AddNpcCommand.Execute(null));
            if (vm.Uis.Count > 0)
            {
                // Found again each time: a step put back the longer way makes the screen's view again.
                string biggest = vm.Uis.OrderByDescending(u => SMSModForge.Model.ModelAssign.Written(u.Model).Length).First().Model.Id;
                UiViewModel Screen() => vm.Uis.First(u => u.Model.Id == biggest);
                UiNodeViewModel Deep(UiNodeViewModel n) { while (n.Children.Count > 0) n = n.Children[n.Children.Count / 2]; return n; }
                _out.WriteLine($"the largest screen: {Screen().Nodes.Sum(Count)} objects");
                Timed("an object of the largest screen moved", MainTab.Ui, () => { vm.SelectedUi = Screen(); Screen().SelectedNode = Deep(Screen().Nodes[0]); },
                      () => Screen().SelectedNode!.PositionX += 5);
                Timed("an object added to the largest screen", MainTab.Ui, () => { vm.SelectedUi = Screen(); Screen().SelectedNode = Screen().Nodes[0]; }, () => Screen().AddChildCommand.Execute(null));
                Timed("an object of the largest screen duplicated", MainTab.Ui, () => { vm.SelectedUi = Screen(); Screen().SelectedNode = Deep(Screen().Nodes[0]); }, () => Screen().DuplicateNodeCommand.Execute(null));
            }
            if (vm.VanillaExtensions.Count > 0)
            {
                string source = vm.VanillaExtensions.OrderByDescending(v => v.GameObjects.Count).First().Model.Source;
                VanillaPlaceExtensionViewModel Ext() => vm.VanillaExtensions.First(v => v.Model.Source == source);
                Timed("an object of a game's level moved", MainTab.Places, () => vm.SelectedVanillaExtension = Ext(), () => Ext().GameObjects[0].X += 1);
                Timed("an object added to a game's level", MainTab.Places, () => vm.SelectedVanillaExtension = Ext(), () => Ext().AddGameObject());
            }
            if (vm.Places.Count > 0)
            {
                string key = vm.Places.OrderByDescending(p => p.GameObjects.Count).First().Model.Key;
                PlaceViewModel Place() => vm.Places.First(p => p.Model.Key == key);
                Timed("an object added to a place", MainTab.Places, () => vm.SelectedPlace = Place(), () => Place().AddGameObject());
            }

            // A place's object moved (the fast way back).
            if (vm.Places.Count > 0)
            {
                tabs.SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.OrderByDescending(p => p.GameObjects.Count).First();
                WindowHarness.Pump();
                var obj = vm.SelectedPlace.GameObjects.FirstOrDefault(g => !g.Model.IsNpcRoot);
                if (obj != null)
                {
                    vm.Undo.Checkpoint();
                    double move = Ms(() => { obj.X += 1; vm.Undo.Checkpoint(); });
                    WindowHarness.Pump();
                    double undoMove = Ms(() => vm.UndoCommand.Execute(null));
                    WindowHarness.Pump();
                    _out.WriteLine($"an object moved and the step taken: {move:0.0} ms; undo of it: {undoMove:0} ms ({vm.LastRestore})");
                }
            }
        });
    }
}
