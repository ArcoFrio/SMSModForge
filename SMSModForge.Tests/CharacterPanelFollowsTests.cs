using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Three things on the Characters and Dialogues tabs that have to follow
/// something else: the Default outfit box following an outfit's rename, the
/// neutral expression every character carries, and the Actor picker filing
/// speakers under whose they are.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class CharacterPanelFollowsTests
{
    private readonly ITestOutputHelper _out;
    public CharacterPanelFollowsTests(ITestOutputHelper o) => _out = o;

    private static T? Named<T>(DependencyObject root, string name) where T : FrameworkElement
        => Descendants<T>(root).FirstOrDefault(e => e.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static void ShowTab(MainWindow window, string header)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == header) { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
    }

    /// <summary>A character of the pack's own with two outfits whose keys are
    /// not their names - the shape a real manifest has.</summary>
    private static CharacterViewModel TwoOutfits(MainViewModel vm)
    {
        var model = new CharacterDef { Key = "anna", DisplayName = "Anna", DefaultOutfit = "AnnaDay" };
        model.Expressions.Add(CharacterDef.NewNeutral());
        model.Outfits.Add(new OutfitDef { Key = "day", GameObjectName = "AnnaDay", BaseSprite = "a.png" });
        model.Outfits.Add(new OutfitDef { Key = "night", GameObjectName = "AnnaNight" });
        var c = new CharacterViewModel(model);
        vm.Pack.Characters.Add(model);
        vm.Characters.Add(c);
        return c;
    }

    // ── The Default outfit box follows a rename ───────────────────────

    [Fact]
    public void RenamingTheDefaultOutfitRenamesTheDefault()
    {
        var vm = new MainViewModel();
        var c = TwoOutfits(vm);
        var day = c.Outfits.Single(o => o.Key == "day");

        day.GameObjectName = "AnnaMorning";

        _out.WriteLine($"default '{c.DefaultOutfit}', offered: {string.Join(", ", c.WearableBusts)}");
        Assert.Equal("AnnaMorning", c.DefaultOutfit);
        Assert.Contains("AnnaMorning", c.WearableBusts);
        Assert.Same(day, c.FindDefaultOutfit());
    }

    [Fact]
    public void ClearingTheNameToRetypeItDoesNotHandTheDefaultToAnotherOutfit()
    {
        // Clearing the box is the ordinary way to rename, and an EMPTY default
        // means the first outfit - so following the name through blank would
        // quietly make a different outfit the default of a character whose
        // default is not first.
        var vm = new MainViewModel();
        var c = TwoOutfits(vm);
        var night = c.Outfits.Single(o => o.Key == "night");
        c.DefaultOutfit = "AnnaNight";

        night.GameObjectName = "";
        _out.WriteLine($"cleared: default '{c.DefaultOutfit}'");
        Assert.Equal("AnnaNight", c.DefaultOutfit);

        night.GameObjectName = "A";
        night.GameObjectName = "AnnaEvening";
        _out.WriteLine($"retyped: default '{c.DefaultOutfit}'");
        Assert.Equal("AnnaEvening", c.DefaultOutfit);
        Assert.Same(night, c.FindDefaultOutfit());
    }

    [Fact]
    public void RenamingAnotherOutfitLeavesTheDefaultAlone()
    {
        var vm = new MainViewModel();
        var c = TwoOutfits(vm);
        c.Outfits.Single(o => o.Key == "night").GameObjectName = "AnnaEvening";
        Assert.Equal("AnnaDay", c.DefaultOutfit);
    }

    private static void TypeInto(OutfitViewModel outfit, string text)
    {
        for (int i = 1; i <= text.Length; i++) outfit.GameObjectName = text.Substring(0, i);
    }

    [Theory]
    [InlineData("day")]     // the default comes first
    [InlineData("night")]   // the default comes second
    public void TypingAnotherOutfitsNamePastTheDefaultsLeavesTheDefault(string defaultKey)
    {
        // Reported: outfit names that start like the default's took the
        // default with them while being typed. Typing "AnnaDay2" passes
        // through "AnnaDay", and the next keystroke looked like the default
        // being renamed.
        var vm = new MainViewModel();
        var c = TwoOutfits(vm);
        var holder = c.Outfits.Single(o => o.Key == defaultKey);
        var other = c.Outfits.Single(o => o.Key != defaultKey);
        c.DefaultOutfit = holder.GameObjectName;
        string name = holder.GameObjectName;

        other.GameObjectName = "";
        TypeInto(other, name + "Swim");
        _out.WriteLine($"default '{c.DefaultOutfit}', other '{other.GameObjectName}'");
        Assert.Equal(name, c.DefaultOutfit);
        Assert.Same(holder, c.FindDefaultOutfit());

        // Pasted over in one go, the same.
        other.GameObjectName = "";
        other.GameObjectName = name;
        other.GameObjectName = name + "Beach";
        Assert.Equal(name, c.DefaultOutfit);
        Assert.Same(holder, c.FindDefaultOutfit());

        // And the default itself still takes the default along.
        TypeInto(holder, name + "Formal");
        Assert.Equal(name + "Formal", c.DefaultOutfit);
        Assert.Same(holder, c.FindDefaultOutfit());
    }

    [Fact]
    public void TypingInTheNameBoxLeavesTheDefaultBoxAlone()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Characters");
            var c = TwoOutfits(vm);
            vm.SelectedCharacter = c;
            vm.SelectedOutfit = c.Outfits.Single(o => o.Key == "night");
            WindowHarness.Pump();

            var picker = Named<ComboBox>(window, "DefaultOutfitPicker")!;
            var nameBox = Descendants<TextBox>(window)
                .Single(t => t.IsVisible && t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "GameObjectName"
                             && !t.IsReadOnly);
            nameBox.Focus();
            nameBox.Clear();
            foreach (char ch in "AnnaDayOff")
            {
                nameBox.AppendText(ch.ToString());
                WindowHarness.Pump();
            }

            _out.WriteLine($"box '{picker.Text}', model '{c.Model.DefaultOutfit}', outfit '{vm.SelectedOutfit!.GameObjectName}'");
            Assert.Equal("AnnaDayOff", vm.SelectedOutfit!.GameObjectName);
            Assert.Equal("AnnaDay", c.Model.DefaultOutfit);
            Assert.Equal("AnnaDay", picker.Text);
        });
    }

    [Fact]
    public void TheBoxOnScreenShowsTheNewNameAtOnce()
    {
        // Measured on the box, which is where it was reported: the name field
        // changed and the Default outfit field beside it did not.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Characters");
            var c = TwoOutfits(vm);
            vm.SelectedCharacter = c;
            vm.SelectedOutfit = c.Outfits.Single(o => o.Key == "day");
            WindowHarness.Pump();

            var box = Named<ComboBox>(window, "DefaultOutfitPicker");
            Assert.True(box != null && box.IsVisible, "the Default outfit box is not on screen");
            Assert.Equal("AnnaDay", box!.Text);

            vm.SelectedOutfit!.GameObjectName = "AnnaMorning";
            WindowHarness.Pump();

            _out.WriteLine($"box '{box.Text}', selected '{box.SelectedItem}', model '{c.Model.DefaultOutfit}'");
            Assert.Equal("AnnaMorning", box.Text);
            Assert.Equal("AnnaMorning", c.Model.DefaultOutfit);
        });
    }

    [Fact]
    public void ADefaultTypedByHandSurvivesAnotherOutfitsRename()
    {
        // The box can name a bust that is not one of the outfits, and refreshing
        // the list it offers is exactly the kind of thing that makes a dropdown
        // write back an empty value it could not find among its items.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Characters");
            var c = TwoOutfits(vm);
            c.DefaultOutfit = "BuiltElsewhere";
            vm.SelectedCharacter = c;
            vm.SelectedOutfit = c.Outfits.Single(o => o.Key == "night");
            WindowHarness.Pump();

            vm.SelectedOutfit!.GameObjectName = "AnnaEvening";
            WindowHarness.Pump();

            _out.WriteLine($"model '{c.Model.DefaultOutfit}'");
            Assert.Equal("BuiltElsewhere", c.Model.DefaultOutfit);
        });
    }

    // ── Neutral ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("pack")]
    [InlineData("voice")]
    public void ANewCharacterStartsWithNeutral(string kind)
    {
        var vm = new MainViewModel();
        (kind == "voice" ? vm.AddVoiceCharacterCommand : vm.AddCharacterCommand).Execute(null);
        var c = vm.Characters.Last();

        var first = c.Model.Expressions.FirstOrDefault();
        Assert.NotNull(first);
        Assert.Equal("neutral", first!.Key);
        Assert.Equal("", first.ExpressionGoName);

        var row = c.Expressions.Single(e => e.Model == first);
        Assert.False(row.CanEdit, "the neutral row can be edited");
        Assert.False(row.RemoveCommand.CanExecute(null), "the neutral row can be removed");
    }

    [Fact]
    public void ItIsWrittenTheWayTheRuntimeReadsNoFace()
    {
        // An EMPTY child name, not an absent one: absent falls back to the key,
        // and the runtime would look for a child called neutral.
        var pack = new ModPack();
        var c = new CharacterDef { Key = "mira" };
        c.Expressions.Add(CharacterDef.NewNeutral());
        pack.Characters.Add(c);

        string json = PackRepository.SerializeAsSaved(pack);
        string compact = string.Concat(json.Where(ch => !char.IsWhiteSpace(ch)));
        _out.WriteLine(compact.Substring(compact.IndexOf("\"expressions\"")));
        Assert.Contains("{\"key\":\"neutral\",\"expressionGoName\":\"\"}", compact);
    }

    [Fact]
    public void AnOlderPackGainsNeutralWhereItHasNone()
    {
        var pack = new ModPack();
        var without = new CharacterDef { Key = "mira" };
        without.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        var already = new CharacterDef { Key = "tove" };
        already.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        already.Expressions.Add(new ActorExpressionDef { Key = "Neutral", ExpressionGoName = "Calm" });
        pack.Characters.Add(without);
        pack.Characters.Add(already);

        var report = PackMigration.Apply(pack);
        foreach (var change in report.Changes) _out.WriteLine("  " + change);

        Assert.Equal("neutral", without.Expressions[0].Key);
        Assert.Equal("", without.Expressions[0].ExpressionGoName);
        Assert.Equal(2, without.Expressions.Count);

        // Kept exactly as it was, where it was.
        Assert.Equal(2, already.Expressions.Count);
        Assert.Equal("Neutral", already.Expressions[1].Key);
        Assert.Equal("Calm", already.Expressions[1].ExpressionGoName);

        var change1 = Assert.Single(report.Changes, ch => ch.What.Contains("neutral"));
        Assert.Equal(1, change1.Count);

        // Told once. Loading it again - after the save - says nothing more.
        var again = PackMigration.Apply(pack);
        Assert.DoesNotContain(again.Changes, ch => ch.What.Contains("neutral"));
    }

    [Fact]
    public void ANeutralThePackAlreadyHadIsFixedToo()
    {
        var model = new CharacterDef { Key = "tove" };
        model.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        model.Expressions.Add(new ActorExpressionDef { Key = "Neutral", ExpressionGoName = "Calm" });
        var c = new CharacterViewModel(model);

        var neutral = c.Expressions.Single(e => e.Key == "Neutral");
        var happy = c.Expressions.Single(e => e.Key == "Happy");
        Assert.False(neutral.CanEdit);
        Assert.True(happy.CanEdit, "every row was locked, which proves nothing about neutral");
    }

    [Fact]
    public void TheGamesOwnCharactersAreLeftToTheGame()
    {
        // Theirs comes from the game's dataset, and a pack's row restating it
        // is dropped by the migration above this one.
        var pack = new ModPack();
        var theirs = new CharacterDef { Key = "kate", VanillaCharacter = "Kate", BustSource = BustSource.Vanilla };
        pack.Characters.Add(theirs);

        PackMigration.Apply(pack);
        Assert.DoesNotContain(theirs.Expressions, CharacterDef.IsNeutral);
    }

    [Fact]
    public void TheNeutralRowIsGreyedOutAndHasNoRemoveButtonOnScreen()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowTab(window, "Characters");
            vm.AddCharacterCommand.Execute(null);
            vm.SelectedCharacter!.AddExpression();
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var boxes = Descendants<TextBox>(window)
                .Where(t => t.IsVisible && t.DataContext is ActorExpressionViewModel).ToList();
            var neutralKey = boxes.First(t => ((ActorExpressionViewModel)t.DataContext).Key == "neutral");
            var happyKey = boxes.First(t => ((ActorExpressionViewModel)t.DataContext).Key == "Happy");
            _out.WriteLine($"neutral enabled {neutralKey.IsEnabled}, Happy enabled {happyKey.IsEnabled}");
            Assert.False(neutralKey.IsEnabled);
            Assert.True(happyKey.IsEnabled);

            var removes = Descendants<Button>(window)
                .Where(b => b.DataContext is ActorExpressionViewModel && (b.Content as string) == "−").ToList();
            Assert.False(removes.Single(b => ((ActorExpressionViewModel)b.DataContext).Key == "neutral").IsVisible);
            Assert.True(removes.Single(b => ((ActorExpressionViewModel)b.DataContext).Key == "Happy").IsVisible);
        });
    }

    // ── The Actor picker ──────────────────────────────────────────────

    [Fact]
    public void ACharacterIsOfferedAsASpeakerTheMomentItIsAdded()
    {
        // The list was rebuilt only when a pack was opened, so a new character
        // could not be picked until the pack was closed and opened again.
        var vm = new MainViewModel();
        vm.AddCharacterCommand.Execute(null);
        var added = vm.Characters.Last();
        Assert.Contains(added.Key, vm.ActorOptions);

        added.DisplayName = "Mira Vale";   // the key follows the name while new
        _out.WriteLine($"key now '{added.Key}'");
        Assert.Contains(added.Key, vm.ActorOptions);
    }

    [Fact]
    public void SpeakersAreFiledUnderWhoseTheyAre()
    {
        var vm = new MainViewModel();
        vm.AddCharacterCommand.Execute(null);
        string mine = vm.Characters.Last().Key;

        var groups = vm.ActorOptionsGrouped.Groups.Cast<System.Windows.Data.CollectionViewGroup>().ToList();
        foreach (var g in groups) _out.WriteLine($"{g.Name}: {g.Items.Count} ({string.Join(", ", g.Items.Take(5))})");

        Assert.Equal("This pack", (string)groups[0].Name);
        var pack = groups.Single(g => (string)g.Name == "This pack");
        var game = groups.Single(g => (string)g.Name == "The game's own");

        Assert.Contains(mine, pack.Items.Cast<string>());
        Assert.Contains(CharacterDef.PlayerKey, game.Items.Cast<string>(), System.StringComparer.OrdinalIgnoreCase);
        var theirs = vm.Characters.First(c => c.Model.IsVanillaCharacter).Key;
        Assert.Contains(theirs, game.Items.Cast<string>());
        Assert.DoesNotContain(theirs, pack.Items.Cast<string>());
    }

    [Fact]
    public void TheNodesActorPickerShowsTheHeadings()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddCharacterCommand.Execute(null);
            ShowTab(window, "Dialogues");
            vm.AddDialogueCommand.Execute(null);
            WindowHarness.Pump();
            if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
            vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
            WindowHarness.Pump();

            var picker = (ComboBox)window.FindName("NodeActorPicker");
            Assert.True(picker.IsVisible, "the Actor picker is not on screen");
            picker.IsDropDownOpen = true;
            WindowHarness.Pump();

            var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
            var headings = Descendants<GroupItem>(popup.Child)
                .Where(g => g.IsVisible)
                .Select(g => (g.DataContext as System.Windows.Data.CollectionViewGroup)?.Name as string)
                .ToList();
            picker.IsDropDownOpen = false;

            _out.WriteLine("headings: " + string.Join(" | ", headings));
            Assert.Contains("This pack", headings);
            Assert.Contains("The game's own", headings);
        });
    }

    // ── The mask editor's sliders ─────────────────────────────────────

    [Theory]
    [InlineData(20.0, 1.0, System.Windows.Input.Key.Right, false, 21.0)]
    [InlineData(20.0, 1.0, System.Windows.Input.Key.Right, true, 25.0)]
    [InlineData(20.0, 1.0, System.Windows.Input.Key.Left, true, 15.0)]
    [InlineData(0.20, 0.01, System.Windows.Input.Key.Right, false, 0.21)]
    [InlineData(0.20, 0.01, System.Windows.Input.Key.Right, true, 0.25)]
    [InlineData(0.80, 0.01, System.Windows.Input.Key.Left, false, 0.79)]
    [InlineData(0.99, 0.01, System.Windows.Input.Key.Right, true, 1.0)]   // held at the end
    [InlineData(1.0, 1.0, System.Windows.Input.Key.Left, false, 1.0)]     // and at the start
    public void AnArrowMovesASliderByWhatItShows(double from, double unit, System.Windows.Input.Key key, bool shift, double expected)
    {
        WindowHarness.Run(_ =>
        {
            var slider = new Slider { Minimum = unit >= 1 ? 1 : 0, Maximum = unit >= 1 ? 80 : 1, Value = from };
            var mods = shift ? System.Windows.Input.ModifierKeys.Shift : System.Windows.Input.ModifierKeys.None;

            Assert.True(SMSModForge.View.MaskEditorWindow.NudgeSlider(slider, key, mods, unit));
            _out.WriteLine($"{from} {key}{(shift ? "+Shift" : "")} -> {slider.Value}");
            Assert.Equal(expected, slider.Value, 6);
        });
    }

    [Fact]
    public void AnyOtherKeyIsLeftToTheSlider()
    {
        WindowHarness.Run(_ =>
        {
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.5 };
            Assert.False(SMSModForge.View.MaskEditorWindow.NudgeSlider(
                slider, System.Windows.Input.Key.Up, System.Windows.Input.ModifierKeys.None, 0.01));
            Assert.Equal(0.5, slider.Value);
        });
    }

    [Fact]
    public void TheMaskEditorsSlidersAnswerTheArrows()
    {
        // The step above is only half: each slider has to be wired to it. Sent
        // as a key event on the real window's sliders, off screen.
        WindowHarness.Run(_ =>
        {
            var outfit = new OutfitViewModel(new OutfitDef { Key = "o", GameObjectName = "O" });
            var editor = new SMSModForge.View.MaskEditorWindow(outfit, System.IO.Path.GetTempPath())
            {
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                editor.Show();
                WindowHarness.Pump();

                foreach (var (name, unit) in new[] { ("SizeSlider", 1.0), ("HardnessSlider", 0.01), ("OpacitySlider", 0.01) })
                {
                    var slider = (Slider)editor.FindName(name);
                    double before = slider.Value;
                    var source = PresentationSource.FromVisual(slider);
                    var args = new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice, source!, 0, System.Windows.Input.Key.Right)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                    slider.RaiseEvent(args);
                    WindowHarness.Pump();

                    _out.WriteLine($"{name}: {before} -> {slider.Value}, handled {args.Handled}");
                    Assert.True(args.Handled, $"{name} does not answer the arrow keys");
                    Assert.Equal(System.Math.Min(slider.Maximum, before + unit), slider.Value, 6);
                }
            }
            finally
            {
                editor.Close();
                WindowHarness.Pump();
            }
        });
    }
}
