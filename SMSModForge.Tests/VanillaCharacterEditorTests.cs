using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Diagnostics;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the Characters tab lets an author do to one of the GAME's characters.
/// <para/>
/// Driven through the real window rather than the view model alone, for the
/// reason the UI tab's tests were written: a binding to a property that is not
/// there fails silently in WPF, and this change moved half a dozen of them from
/// the character onto the outfit. A view-model test would pass on every one of
/// those while the panel showed nothing.
/// </summary>
public sealed class VanillaCharacterEditorTests
{
    private readonly ITestOutputHelper _out;
    public VanillaCharacterEditorTests(ITestOutputHelper o) => _out = o;

    /// <summary>Collects whatever WPF says about bindings, the way
    /// <see cref="UiBindingTests"/> does — installed before the window, since
    /// WPF decides whether to trace when a binding is created.</summary>
    private sealed class BindingWatch : TraceListener, IDisposable
    {
        public List<string> Complaints { get; } = new();
        private readonly SourceLevels _was;

        public BindingWatch()
        {
            PresentationTraceSources.Refresh();
            _was = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (message.Contains("path error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Cannot find", StringComparison.OrdinalIgnoreCase))
                Complaints.Add(message);
        }

        public void Dispose()
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.DataBindingSource.Switch.Level = _was;
        }
    }

    private static void ShowCharacters(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Characters")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }

        throw new Xunit.Sdk.XunitException("no Characters tab");
    }

    /// <summary>One of the game's characters, and one of their own busts.</summary>
    private static (CharacterViewModel Character, OutfitViewModel Outfit) TheirsIn(MainViewModel vm)
    {
        var character = vm.Characters.First(c => c.IsVanillaBust && c.Outfits.Count > 0);
        return (character, character.Outfits[0]);
    }

    [Fact]
    public void Driving_a_borrowed_character_produces_no_broken_bindings()
    {
        using var watch = new BindingWatch();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);

            var (character, outfit) = TheirsIn(vm);
            vm.SelectedOutfit = outfit;
            WindowHarness.Pump();

            // Open the panel that only these characters have.
            outfit.OverridesShown = true;
            WindowHarness.Pump();

            // ...and one that a pack drew for them, which shows the other half
            // of the editor.
            var added = character.AddOutfit();
            vm.SelectedOutfit = added;
            WindowHarness.Pump();

            // Change enough that every reset button is on screen: they are
            // collapsed until there is something to put back, and a binding on
            // a control nobody has realised is a binding nobody has checked.
            character.NameColor = "#123456";
            character.TypewriterFrequencyText = "12";
            character.TypewriterPitchMinText = "0.3";
            character.TypewriterPitchMaxText = "2.5";
            WindowHarness.Pump();
            Assert.True(character.CanResetNameColor && character.CanResetVoice
                        && character.CanResetFrequency && character.CanResetPitchMin
                        && character.CanResetPitchMax,
                        "the buttons never appeared, so their bindings went unchecked");

            foreach (string complaint in watch.Complaints.Distinct().Take(6))
                _out.WriteLine(complaint);

            Assert.True(watch.Complaints.Count == 0,
                        $"{watch.Complaints.Count} broken binding(s); first: "
                        + (watch.Complaints.FirstOrDefault() ?? ""));
        });
    }

    [Fact]
    public void WhatBelongsToTheGameIsNotTheAuthorsToEdit()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, outfit) = TheirsIn(vm);

            // Their name, their key, their GameObject, and which bust they
            // enter in: all the game's, in every mod, whatever this pack says.
            Assert.False(character.CanEditIdentity);
            Assert.False(character.CanEditBustSource);
            Assert.False(character.CanEditDefaultOutfit);
            Assert.False(outfit.CanEditName);

            // The colour is theirs, though, and the runtime replaces the
            // game's rather than adding beside it.
            Assert.True(character.CanEditNameColor);

            // A pack character keeps everything.
            var mine = vm.Characters.FirstOrDefault(c => c.IsPackBust && !c.IsPlayer);
            if (mine != null)
            {
                Assert.True(mine.CanEditIdentity);
                Assert.True(mine.CanEditDefaultOutfit);
            }
        });
    }

    [Fact]
    public void TheBustTheyEnterInIsShownEvenThoughItCannotBeChanged()
    {
        // The field was blank on every one of the game's characters: the
        // runtime falls back to the first outfit, so nothing ever had to write
        // it down, and the editor showed an empty box for something an author
        // cannot edit - which reads as nobody knowing the answer.
        //
        // The order is the scene's, so the first outfit IS the first bust under
        // 2_Bust_Manager for that character.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, first) = TheirsIn(vm);

            _out.WriteLine($"{character.DisplayName} enters in {character.DefaultOutfit}");
            Assert.Equal(first.GameObjectName, character.DefaultOutfit);
            Assert.False(character.CanEditDefaultOutfit);

            // ...and saying it did not make the character look edited, which is
            // what would put a hundred of them back into every manifest.
            Assert.True(VanillaCastSeed.IsUntouched(character.Model));
        });
    }

    [Fact]
    public void ABustThePackAddsToThemIsEditedLikeAnyOther()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, theirs) = TheirsIn(vm);

            var added = character.AddOutfit();
            WindowHarness.Pump();

            // Said outright, so it reads as the pack's before it has any art.
            Assert.True(added.Model.PackArt);
            Assert.Empty(added.Model.BaseSprite);

            Assert.True(added.CanEditName);
            Assert.True(added.ShowsPackArt);
            Assert.False(added.IsVanillaBust);
            Assert.True(added.IsAddedToVanilla);

            // ...and its neighbour in the same wardrobe is still the game's.
            Assert.True(theirs.IsVanillaBust);
            Assert.False(theirs.ShowsPackArt);
        });
    }

    [Fact]
    public void EachOutfitCarriesItsOwnTag()
    {
        // The character's tag says something in there changed. On somebody with
        // sixty-five outfits that is not enough to find the one row to open.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, theirs) = TheirsIn(vm);
            vm.SelectedOutfit = theirs;
            WindowHarness.Pump();

            // One of the game's busts, untouched: nothing.
            Assert.Equal("", theirs.ChangedTag);
            Assert.False(theirs.HasChangedTag);

            // Painted over: changed.
            theirs.OverridesShown = true;
            var row = theirs.Overrides.First(r => r.Slot == SpriteSlotNames.Blink);
            row.Replaced = true;
            row.Path = "art/blink.png";
            WindowHarness.Pump();
            Assert.Equal("changed", theirs.ChangedTag);

            // A bust the pack drew for them: new, not changed. It is not an
            // alteration of anything - the same distinction the UI tree makes.
            var added = character.AddOutfit();
            WindowHarness.Pump();
            Assert.Equal("new", added.ChangedTag);

            // ...and untaking the change takes the tag with it.
            row.Replaced = false;
            WindowHarness.Pump();
            Assert.Equal("", theirs.ChangedTag);
        });
    }

    [Fact]
    public void APacksOwnOutfitsAreNotTagged()
    {
        // The control. Every outfit on a character the pack drew is the pack's,
        // so a tag on all of them distinguishes nothing.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);
            var outfit = mine.Outfits.FirstOrDefault() ?? mine.AddOutfit();
            WindowHarness.Pump();

            outfit.BaseSprite = "art/base.png";
            Assert.Equal("", outfit.ChangedTag);
            Assert.False(outfit.HasChangedTag);
        });
    }

    [Fact]
    public void APacksOwnCharacterIsOfferedNoResets()
    {
        // "Put it back" names nothing on a character the pack drew: every field
        // is the author's and there is nothing behind it to return to.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);

            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);

            Assert.False(mine.ShowsResets);

            // ...even with something set, which is when they would otherwise
            // light up.
            mine.NameColor = "#ABCDEF";
            mine.TypewriterPitchMaxText = "2.5";
            WindowHarness.Pump();
            Assert.False(mine.ShowsResets);

            // And one of the game's still gets them.
            var (theirs, _) = TheirsIn(vm);
            Assert.True(theirs.ShowsResets);
        });
    }

    [Fact]
    public void TheDefaultOutfitCanBePutBack()
    {
        // The field is greyed on one of the game's characters, so the button is
        // the only way back from an answer an older editor wrote: it adopted a
        // character through whichever bust the pack first used and recorded
        // that as the default.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var character = vm.Characters.First(c => c.IsVanillaBust && c.Outfits.Count > 1);

            Assert.False(character.CanResetDefaultOutfit);
            string theirs = character.DefaultOutfit;

            character.DefaultOutfit = character.Outfits[^1].GameObjectName;
            WindowHarness.Pump();
            Assert.True(character.CanResetDefaultOutfit);
            Assert.True(character.IsModified);

            character.ResetFieldCommand.Execute(nameof(CharacterViewModel.DefaultOutfit));
            WindowHarness.Pump();

            _out.WriteLine($"{character.DisplayName} back to {character.DefaultOutfit}");
            Assert.Equal(theirs, character.DefaultOutfit);
            Assert.False(character.CanResetDefaultOutfit);
            Assert.False(character.IsModified);
        });
    }

    [Fact]
    public void NeutralIsListedForTheGamesCharactersToo()
    {
        // A pack's own character has carried a neutral row all along. Leaving
        // it off the game's made the two boxes disagree about what a character
        // can be asked to do - and the game's own conversations ask for it by
        // name seven hundred times.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var character = vm.Characters.First(c => c.IsVanillaBust && c.GameExpressions.Count > 1);

            _out.WriteLine($"{character.DisplayName}: {string.Join(", ", character.GameExpressions)}");
            Assert.Equal("neutral", character.GameExpressions[0]);

            // It names no child, the way a pack's does: it means no expression
            // showing, which is the bust's own face.
            var row = character.Expressions.First(e => e.Key == "neutral");
            Assert.Equal("", row.ExpressionGoName);
            Assert.True(row.FromTheGame);

            // ...and it gets no sprite row, because there is no art for the
            // absence of a face.
            var outfit = character.Outfits[0];
            outfit.OverridesShown = true;
            Assert.DoesNotContain(outfit.Overrides,
                                  r => r.Slot == SpriteSlotNames.Expression("neutral"));
        });
    }

    [Fact]
    public void ACharacterWithNoFacesIsNotGivenNeutralOnItsOwn()
    {
        // The control. "No face" listed by itself would read as an ability they
        // do not have.
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        var mute = pack.Characters.First(c => c.IsVanillaCharacter && VanillaFaces.Of(c).Count == 0);
        _out.WriteLine($"{mute.DisplayName} has nothing to pull a face with");
        Assert.Empty(VanillaFaces.Of(mute));
    }

    [Fact]
    public void APacksNeutralIsNotAskedForArt()
    {
        // The regression this pair guards. An empty child name is how every
        // pack spells neutral; reading it as a face had the validator asking
        // every pack character for a neutral.png that was never meant to exist.
        var pack = PackRepository.CreateEmpty("faces.pack");
        var mine = new CharacterDef { Key = "sarah", Name = "Sarah", DisplayName = "Sarah" };
        mine.Outfits.Add(new OutfitDef
        {
            Key = "sarah", GameObjectName = "Sarah",
            BaseSprite = "art/base.png", BlinkSprite = "art/blink.png",
            Expression = { Enabled = true, Prefix = "art/Expression" },
        });
        mine.Expressions.Add(new ActorExpressionDef { Key = "neutral", ExpressionGoName = "" });
        mine.Expressions.Add(new ActorExpressionDef { Key = "Happy", ExpressionGoName = "Happy" });
        pack.Characters.Add(mine);

        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-neutral-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        List<Validation.ValidationIssue> about;
        try
        {
            about = Validation.PackValidator.Validate(pack, root)
                .Where(i => i.Where.Contains("expression[")).ToList();
        }
        finally { try { Directory.Delete(root, true); } catch { } }

        foreach (var i in about) _out.WriteLine($"{i.Severity} {i.Where}");
        Assert.DoesNotContain(about, i => i.Where.Contains("expression[neutral]"));
        Assert.Contains(about, i => i.Where.Contains("expression[Happy]"));
    }

    [Fact]
    public void TheChangedFieldsAreNamed()
    {
        // The tag says a character differs. This says what differs - including
        // the things an author cannot see anywhere else.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            Assert.Equal("", character.ChangedFields);
            Assert.False(character.HasChangedFields);

            character.NameColor = "#123456";
            character.TypewriterPitchMaxText = "2.5";
            character.AddExpression();
            WindowHarness.Pump();

            _out.WriteLine($"{character.DisplayName} changes: {character.ChangedFields}");
            Assert.Contains("name colour", character.ChangedFields);
            Assert.Contains("voice", character.ChangedFields);
            Assert.Contains("expressions", character.ChangedFields);
            Assert.True(character.HasChangedFields);
        });
    }

    [Fact]
    public void ResettingTheWholeCharacterReachesWhatTheFieldButtonsCannot()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, theirs) = TheirsIn(vm);
            vm.SelectedCharacter = character;

            // One of each kind, including the three no field button reaches.
            character.NameColor = "#123456";
            character.TypewriterPitchMaxText = "2.5";
            character.AddExpression();
            theirs.OverridesShown = true;
            var row = theirs.Overrides.First(r => r.Slot == SpriteSlotNames.Base);
            row.Replaced = true;
            row.Path = "art/base.png";
            character.AddOutfit();
            WindowHarness.Pump();

            _out.WriteLine("would lose: " + character.ResetEverythingSummary);
            Assert.Contains("expression", character.ResetEverythingSummary);
            Assert.Contains("bust", character.ResetEverythingSummary);
            Assert.Contains("texture", character.ResetEverythingSummary);
            Assert.True(character.CanResetEverything);

            character.ResetEverything();
            WindowHarness.Pump();

            Assert.Null(character.Model.NameColor);
            Assert.Null(character.Model.Typewriter);
            Assert.Empty(character.Model.Expressions);
            Assert.DoesNotContain(character.Model.Outfits, o => o.PackArt);
            Assert.All(character.Model.Outfits, o => Assert.Empty(o.SpriteOverrides));

            Assert.False(character.IsModified);
            Assert.True(VanillaCastSeed.IsUntouched(character.Model));
            Assert.Equal("", character.ChangedFields);
        });
    }

    [Fact]
    public void AFullResetKeepsTheKeyDialogueDependsOn()
    {
        // The one thing it must not take. Dialogue nodes name this character by
        // key; rewriting it would silence every line they speak.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            character.Key = "somebody";
            character.NameColor = "#123456";
            WindowHarness.Pump();

            character.ResetEverything();
            Assert.Equal("somebody", character.Key);
            Assert.Null(character.Model.NameColor);

            // ...which is itself a difference, and named as one.
            _out.WriteLine(character.ChangedFields);
            Assert.Contains("dialogue key", character.ChangedFields);
        });
    }

    [Fact]
    public void NobodyIsResetWithoutBeingAsked()
    {
        // Ask(...) refuses when nobody is there, so the harness cannot wipe a
        // character it merely opened - and neither can a stray click while a
        // dialog is unanswered.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);
            vm.SelectedCharacter = character;

            character.NameColor = "#123456";
            WindowHarness.Pump();

            vm.ResetCharacterCommand.Execute(null);
            WindowHarness.Pump();

            Assert.Equal("#123456", character.Model.NameColor);
            Assert.True(character.IsModified);
        });
    }

    // ── Putting a field back ──────────────────────────────────────

    [Fact]
    public void TheColourResetIsSomewhereAnAuthorCanReachIt()
    {
        // Measured, not reasoned about - and written after guessing wrong
        // about why this button could not be found. The guess was that the row
        // had outgrown its column; measuring said it ends at 475 of 526 even
        // at its widest, so it never had. (It was collapsed, not clipped.)
        //
        // The guard is worth keeping anyway, on the strength of that number:
        // this row carries a swatch, a hex box, two buttons and a note inside a
        // fixed-width pane whose label column takes 140 of it, and fifty pixels
        // of headroom is one more control away from being none. Nothing else in
        // this file could see it happen - every property assertion here passes
        // whether or not the button is on screen.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, outfit) = TheirsIn(vm);
            vm.SelectedOutfit = outfit;

            // With a colour set, so the button is live and the "the character's
            // own" note is out of the way - and then again without, which is
            // the wider of the two rows.
            character.NameColor = "#123456";
            WindowHarness.Pump();

            var button = (System.Windows.Controls.Button)window.FindName("ResetNameColorButton");
            Assert.NotNull(button);

            for (int pass = 0; pass < 2; pass++)
            {
                window.UpdateLayout();
                WindowHarness.Pump();

                var pane = Ancestor<System.Windows.Controls.ScrollViewer>(button);
                Assert.NotNull(pane);
                Assert.True(button.ActualWidth > 0, "the button was never laid out");

                var corner = button.TransformToAncestor(pane!)
                                   .Transform(new System.Windows.Point(button.ActualWidth, 0));
                _out.WriteLine($"pass {pass}: right edge at {corner.X:0} of {pane!.ActualWidth:0}"
                               + $" (enabled={button.IsEnabled})");

                Assert.True(corner.X <= pane.ActualWidth,
                            $"the Reset button ends at {corner.X:0} in a pane {pane.ActualWidth:0} wide, "
                            + "so it is off the edge and cannot be clicked");

                // Second pass: nothing set, which is when the extra note shows
                // and the row is at its widest.
                character.ResetFieldCommand.Execute(nameof(CharacterViewModel.NameColor));
                WindowHarness.Pump();
            }

            // ...and it is still the right button: present either way, live
            // only when there is something to put back.
            Assert.False(character.CanResetNameColor);
            Assert.False(button.IsEnabled);
        });
    }

    /// <summary>The nearest ancestor of a given type, for measuring against.</summary>
    private static T? Ancestor<T>(System.Windows.DependencyObject from) where T : class
    {
        var at = System.Windows.Media.VisualTreeHelper.GetParent(from);
        while (at != null && at is not T)
            at = System.Windows.Media.VisualTreeHelper.GetParent(at);
        return at as T;
    }

    [Fact]
    public void OneEditIsOneRoundOfNotifications()
    {
        // A character listens to its own PropertyChanged so the modified tag
        // and the reset buttons can follow any edit without a list of the
        // fields that count. The hazard that comes with it is re-entry: a
        // refresh that raises something the handler answers goes round for
        // ever, and the first time it did, typing a colour overflowed the stack
        // and took the whole test RUN down rather than one test.
        //
        // So this counts instead of relying on the crash. A bounded number is
        // the assertion; the exact number is not the point.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            int raised = 0;
            character.PropertyChanged += (_, _) => raised++;

            character.NameColor = "#123456";
            _out.WriteLine($"a colour edit raised {raised} notification(s)");
            Assert.InRange(raised, 1, 32);

            raised = 0;
            character.TypewriterPitchMaxText = "2.5";
            _out.WriteLine($"a voice edit raised {raised} notification(s)");
            Assert.InRange(raised, 1, 32);
        });
    }

    [Fact]
    public void NothingOffersToResetAFieldNobodyChanged()
    {
        // The rule the rest of the editor already follows for these: a row of
        // buttons that are mostly greyed answers "what did I change here" worse
        // than the two that are not. So the test that matters first is that
        // none of them are offered on a character straight off the shelf.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            Assert.False(character.CanResetNameColor);
            Assert.False(character.CanResetVoice);
            Assert.False(character.CanResetFrequency);
            Assert.False(character.CanResetPitchMin);
            Assert.False(character.CanResetPitchMax);
        });
    }

    [Fact]
    public void ResettingAFieldPutsBackTheCharactersOwnValue()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var character = vm.Characters.First(c => c.IsVanillaBust && c.Speaker != null);
            var speaker = character.Speaker!;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;

            character.TypewriterPitchMaxText = "2.5";
            WindowHarness.Pump();
            Assert.True(character.CanResetPitchMax);
            Assert.True(character.IsModified);

            character.ResetFieldCommand.Execute(nameof(CharacterViewModel.TypewriterPitchMaxText));
            WindowHarness.Pump();

            _out.WriteLine($"{character.DisplayName} back to {character.TypewriterPitchMaxText}");
            Assert.Equal(speaker.PitchMax.ToString(invariant), character.TypewriterPitchMaxText);
            Assert.False(character.CanResetPitchMax);

            // ...and only that field. The frequency was never touched, so
            // neither editing the pitch nor resetting it may have gone near it.
            //
            // This is the assertion that caught a real one: the first edit to
            // any voice field brought a TypewriterDef into existence around it,
            // and a fresh one holds 45 and 1.0-1.5 rather than the character's
            // own. Editing Adrian's pitch quietly moved his frequency from 40
            // to 45 - one field touched, two changed, nothing on screen to say
            // so, and no way back to a number the editor had stopped showing.
            Assert.Equal(speaker.Frequency.ToString(invariant), character.TypewriterFrequencyText);

            // The pitch was the only thing this pack said about the voice, so
            // putting it back leaves nothing to say - and the record goes with
            // it. Otherwise the character stays marked as changed on the
            // strength of an object holding the same numbers the panel shows
            // anyway, with nothing left on screen to point at.
            Assert.Null(character.Model.Typewriter);
            Assert.False(character.CanResetVoice);
            Assert.False(character.IsModified);
        });
    }

    [Fact]
    public void PuttingEveryFieldBackOneAtATimeIsEnough()
    {
        // Reset all was the only thing that cleared the tag, and there was
        // nothing on screen to explain why: every number already read as the
        // character's own. What was left was the RECORD holding them - invisible,
        // and enough to keep the character marked as changed and written to the
        // manifest.
        //
        // So the last field going back takes the record with it, and Reset all
        // is a shortcut rather than the only door.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var character = vm.Characters.First(c => c.IsVanillaBust && c.Speaker != null);

            character.TypewriterFrequencyText = "12";
            character.TypewriterPitchMinText = "0.3";
            character.TypewriterPitchMaxText = "2.5";
            WindowHarness.Pump();
            Assert.True(character.IsModified);

            character.ResetFieldCommand.Execute(nameof(CharacterViewModel.TypewriterFrequencyText));
            WindowHarness.Pump();
            Assert.True(character.IsModified);      // two still differ

            character.ResetFieldCommand.Execute(nameof(CharacterViewModel.TypewriterPitchMinText));
            WindowHarness.Pump();
            Assert.True(character.IsModified);      // one still differs

            character.ResetFieldCommand.Execute(nameof(CharacterViewModel.TypewriterPitchMaxText));
            WindowHarness.Pump();

            _out.WriteLine($"{character.DisplayName}: record={(character.Model.Typewriter == null ? "gone" : "still there")}");
            Assert.Null(character.Model.Typewriter);
            Assert.False(character.IsModified);
            Assert.False(character.CanResetVoice);
        });
    }

    [Fact]
    public void TypingTheCharactersOwnNumbersBackDoesTheSame()
    {
        // Not only the buttons. An author who nudges the pitch and types the
        // old number back has said nothing, and the panel should agree.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var character = vm.Characters.First(c => c.IsVanillaBust && c.Speaker != null);
            var theirs = character.Speaker!;
            var invariant = System.Globalization.CultureInfo.InvariantCulture;

            character.TypewriterPitchMaxText = "2.5";
            WindowHarness.Pump();
            Assert.True(character.IsModified);

            character.TypewriterPitchMaxText = theirs.PitchMax.ToString(invariant);
            WindowHarness.Pump();

            Assert.Null(character.Model.Typewriter);
            Assert.False(character.IsModified);

            // The control: switching the voice OFF is something they said, and
            // survives even though every number matches.
            character.TypewriterEnabled = false;
            WindowHarness.Pump();
            Assert.NotNull(character.Model.Typewriter);
            Assert.True(character.IsModified);

            character.TypewriterEnabled = true;
            WindowHarness.Pump();
            Assert.Null(character.Model.Typewriter);
            Assert.False(character.IsModified);
        });
    }

    [Fact]
    public void ResettingTheWholeVoiceLeavesNothingBehind()
    {
        // Not "set the numbers back to matching": a typewriter object holding
        // the character's own numbers is still written to the manifest, and the
        // character would go on carrying the changed tag for a voice identical
        // to the one they already had.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            character.TypewriterFrequencyText = "12";
            character.TypewriterEnabled = false;
            WindowHarness.Pump();
            Assert.NotNull(character.Model.Typewriter);
            Assert.True(character.IsModified);

            character.ResetFieldCommand.Execute(CharacterViewModel.Voice);
            WindowHarness.Pump();

            Assert.Null(character.Model.Typewriter);
            Assert.True(character.TypewriterEnabled);
            Assert.False(character.CanResetVoice);

            // The whole point: the tag goes with it, and so does the row in the
            // saved file.
            Assert.False(character.IsModified);
            Assert.True(VanillaCastSeed.IsUntouched(character.Model));
        });
    }

    [Fact]
    public void TheBoxShowsTheColourTheCharacterAlreadyHas()
    {
        // Not an empty field with a note off to one side saying what it would
        // have been. An author opening Adrian sees the blue the game writes him
        // in, in the box and on the swatch, and can nudge it from there.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var coloured = vm.Characters.First(c => c.IsVanillaBust && c.HasGameNameColor);

            _out.WriteLine($"{coloured.DisplayName}: box shows {coloured.NameColor}");
            Assert.Equal(coloured.GameNameColor, coloured.NameColor);

            // Showing it is not asserting it: nothing was stored, nothing is
            // offered to reset, and nothing reaches the manifest.
            Assert.Null(coloured.Model.NameColor);
            Assert.False(coloured.CanResetNameColor);
            Assert.False(coloured.IsModified);
        });
    }

    [Fact]
    public void PickingTheColourTheyAlreadyHadIsNotAChange()
    {
        // The half ModForge is responsible for. The box hands an author the
        // character's own colour, so setting it back to that - by typing it, or
        // by coming out of the picker on the same swatch - has to be understood
        // as no change rather than stored as one. Otherwise a pack ends up
        // asserting a colour identical to the game's and the character carries
        // the changed tag for agreeing with it.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var coloured = vm.Characters.First(c => c.IsVanillaBust && c.HasGameNameColor);
            string theirs = coloured.GameNameColor!;

            coloured.NameColor = "#123456";
            WindowHarness.Pump();
            Assert.True(coloured.IsModified);

            coloured.NameColor = theirs;
            WindowHarness.Pump();
            Assert.Null(coloured.Model.NameColor);
            Assert.False(coloured.IsModified);
            Assert.False(coloured.CanResetNameColor);

            // ...however it was spelled. A picker hands back lower case, and a
            // string compare would call that a different colour.
            coloured.NameColor = theirs.ToLowerInvariant();
            WindowHarness.Pump();
            Assert.Null(coloured.Model.NameColor);
            Assert.False(coloured.IsModified);

            // The control: a colour that really is different still counts.
            coloured.NameColor = "#123456";
            Assert.Equal("#123456", coloured.Model.NameColor);
            Assert.True(coloured.IsModified);
        });
    }

    [Fact]
    public void ResettingTheColourPutsTheirOwnBackInTheBox()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var coloured = vm.Characters.First(c => c.IsVanillaBust && c.HasGameNameColor);

            _out.WriteLine($"{coloured.DisplayName}: the game uses {coloured.GameNameColor}");
            Assert.False(coloured.CanResetNameColor);

            coloured.NameColor = "#123456";
            WindowHarness.Pump();
            Assert.True(coloured.CanResetNameColor);
            Assert.True(coloured.IsModified);

            coloured.ResetFieldCommand.Execute(nameof(CharacterViewModel.NameColor));
            WindowHarness.Pump();

            // Nothing stored - and the box showing what that means.
            Assert.Null(coloured.Model.NameColor);
            Assert.Equal(coloured.GameNameColor, coloured.NameColor);
            Assert.False(coloured.CanResetNameColor);
            Assert.False(coloured.IsModified);
        });
    }

    [Fact]
    public void OnAPacksOwnCharacterResettingClearsTheField()
    {
        // There is nothing behind a pack character's colour, so "back to what
        // it was" is nothing at all - and the button has to say so by clearing
        // rather than by refusing to appear.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.Characters.First(c => c.IsPackBust && !c.IsPlayer);

            Assert.False(mine.CanResetNameColor);
            mine.NameColor = "#ABCDEF";
            Assert.True(mine.CanResetNameColor);

            mine.ResetFieldCommand.Execute(nameof(CharacterViewModel.NameColor));
            Assert.Equal("", mine.NameColor);
            Assert.Null(mine.Model.NameColor);
        });
    }

    // ── The modified tag ───────────────────────────────────────

    /// <summary>Runs one edit against a freshly seeded window and reports what
    /// the tag said before and after, plus what it said each time WPF was
    /// told.</summary>
    private void OneEdit(string what, Action<MainViewModel, CharacterViewModel> edit)
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            int told = 0;
            character.PropertyChanged += (_, e) =>
            { if (e.PropertyName == nameof(CharacterViewModel.IsModified)) told++; };

            Assert.False(character.IsModified, "before: " + what);

            edit(vm, character);
            WindowHarness.Pump();

            _out.WriteLine($"{what}: tag={character.IsModified}, notified {told}x");
            Assert.True(character.IsModified, "after: " + what);
            Assert.True(told > 0, $"the tag changed for '{what}' and nothing told the row");
        });
    }

    [Fact]
    public void TheTagFollowsAnyKindOfChange()
    {
        // Not a list of properties this time either: each of these reaches the
        // character through a different route - a field on it, a field on an
        // outfit, a collection, a texture on somebody else's bust - and the tag
        // has to notice all four without knowing about any of them.
        OneEdit("name colour", (_, c) => c.NameColor = "#FF00FF");
        OneEdit("voice", (_, c) => c.TypewriterPitchMaxText = "2.5");
        OneEdit("a face of their own", (_, c) => c.AddExpression());
        OneEdit("a bust of the pack's", (_, c) => c.AddOutfit());
        OneEdit("a replaced texture", (_, c) =>
        {
            var outfit = c.Outfits[0];
            outfit.Overrides.First(r => r.Slot == SpriteSlotNames.Base).Replaced = true;
        });
        OneEdit("art for a replaced texture", (_, c) =>
        {
            var row = c.Outfits[0].Overrides.First(r => r.Slot == SpriteSlotNames.Mask);
            row.Replaced = true;
            row.Path = "art/mask.png";
        });
    }

    [Fact]
    public void TheTagSaysExactlyWhatTheSaveDoes()
    {
        // The tag is worth having only if it is the same question the write
        // asks. A character the editor calls untouched is one the manifest
        // drops; if those two ever disagreed, the tag would be telling an
        // author their work was saved when it was about to be thrown away.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            var untagged = vm.Characters.Where(c => c.IsVanillaBust && !c.IsModified).ToList();
            Assert.NotEmpty(untagged);
            Assert.DoesNotContain(vm.Characters, c => c.IsVanillaBust && c.IsModified);

            character.NameColor = "#123456";
            WindowHarness.Pump();

            var tagged = vm.Characters.Where(c => c.IsVanillaBust && c.IsModified).ToList();
            Assert.Single(tagged);
            Assert.Same(character, tagged[0]);

            // What reaches the file is exactly what carries the tag.
            var restore = VanillaCastSeed.PrepareForSave(vm.Pack);
            try
            {
                var written = vm.Pack.Characters
                    .Where(c => c.IsVanillaCharacter)
                    .Select(c => c.VanillaCharacter).ToList();
                _out.WriteLine("written: " + string.Join(", ", written));
                Assert.Equal(tagged.Select(c => c.Model.VanillaCharacter).OrderBy(x => x),
                             written.OrderBy(x => x));
            }
            finally { restore(); }
        });
    }

    [Fact]
    public void TakingTheChangeBackTakesTheTagWithIt()
    {
        // The control. A tag that only ever went on would be indistinguishable
        // from one that worked, right up until an author undid something.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, outfit) = TheirsIn(vm);

            var row = outfit.Overrides.First(r => r.Slot == SpriteSlotNames.Blink);
            row.Replaced = true;
            row.Path = "art/blink.png";
            WindowHarness.Pump();
            Assert.True(character.IsModified);

            row.Replaced = false;
            WindowHarness.Pump();
            Assert.False(character.IsModified);

            // ...and the row goes quiet again with it, which is the other half
            // of what makes the tagged ones findable among a hundred and
            // nineteen.
            Assert.True(character.IsQuietVanilla);
            row.Replaced = true;
            Assert.False(character.IsQuietVanilla);
            row.Replaced = false;

            // And a character the pack draws never carries the tag at all: the
            // whole entry is theirs, so "changed from what" has no answer.
            var mine = vm.Characters.FirstOrDefault(c => c.IsPackBust && !c.IsPlayer);
            if (mine != null)
            {
                mine.NameColor = "#ABCDEF";
                Assert.False(mine.IsModified);
                // Nor is it ever dimmed: the whole entry is the author's.
                Assert.False(mine.IsQuietVanilla);
            }
        });
    }

    [Fact]
    public void TickingASlotIsTheOnlyThingThatWritesOne()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (_, outfit) = TheirsIn(vm);
            vm.SelectedOutfit = outfit;
            outfit.OverridesShown = true;
            WindowHarness.Pump();

            // Every fixed slot, and a face per expression the bust has.
            var rows = outfit.Overrides;
            _out.WriteLine($"{rows.Count} slots on {outfit.GameObjectName}");
            Assert.True(rows.Count >= SpriteSlotNames.Fixed.Length);
            Assert.Empty(outfit.Model.SpriteOverrides);

            var blink = rows.First(r => r.Slot == SpriteSlotNames.Blink);
            Assert.False(blink.Replaced);

            // A path cannot be typed into a slot nobody ticked, which is what
            // keeps an unticked slot out of the manifest entirely.
            blink.Path = "art/whatever.png";
            Assert.Empty(outfit.Model.SpriteOverrides);
            Assert.Equal("", blink.Path);

            blink.Replaced = true;
            Assert.Single(outfit.Model.SpriteOverrides);
            blink.Path = "art/blink.png";
            Assert.Equal("art/blink.png", outfit.Model.OverrideFor(SpriteSlotNames.Blink));

            // Unticking takes the path with it: there is nowhere else for one
            // to live, and keeping it would put it back on the next save.
            blink.Replaced = false;
            Assert.Empty(outfit.Model.SpriteOverrides);
            Assert.Equal("", blink.Path);
        });
    }

    [Fact]
    public void AFaceAddedInOneBoxGetsARowInTheOther()
    {
        // The sync the two panels need. An expression added to the character is
        // a face the runtime will create on the bust, so it has to have
        // somewhere to put the art - and nothing else would tell the author
        // that art was expected.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, outfit) = TheirsIn(vm);
            vm.SelectedOutfit = outfit;
            outfit.OverridesShown = true;
            WindowHarness.Pump();

            int before = outfit.Overrides.Count;

            var face = character.AddExpression();
            face.Key = "Smirk";
            character.RemoveExpression(face);      // and back again, to prove both ways
            Assert.Equal(before, outfit.Overrides.Count);

            var again = character.AddExpression();
            again.Key = "Smirk";
            // The key was set after the row existed, so ask for the rebuild the
            // editor asks for when the name box loses focus.
            outfit.RefreshOverrides();
            WindowHarness.Pump();

            _out.WriteLine(string.Join(", ", outfit.Overrides.Select(r => r.Slot)));
            Assert.Equal(before + 1, outfit.Overrides.Count);
            Assert.Contains(outfit.Overrides, r => r.Slot == SpriteSlotNames.Expression("Smirk"));
        });
    }

    // ── Where a new face's art comes from ───────────────────────────

    [Fact]
    public void RenamingAFaceRenamesTheRowItsArtGoesIn()
    {
        // A face is added with a placeholder name and typed over - that is the
        // only way to make one. If the art row kept the name the face was born
        // with, an author would fill in a path for "Expression1" and wonder why
        // "Smirk" showed nothing.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, outfit) = TheirsIn(vm);
            vm.SelectedOutfit = outfit;
            outfit.OverridesShown = true;
            WindowHarness.Pump();

            var face = character.AddExpression();
            WindowHarness.Pump();
            string born = face.Key;
            Assert.Contains(outfit.Overrides, r => r.Slot == SpriteSlotNames.Expression(born));

            face.Key = "Smirk";
            WindowHarness.Pump();

            _out.WriteLine(string.Join(", ", outfit.Overrides.Select(r => r.Slot)));
            Assert.Contains(outfit.Overrides, r => r.Slot == SpriteSlotNames.Expression("Smirk"));
            Assert.DoesNotContain(outfit.Overrides, r => r.Slot == SpriteSlotNames.Expression(born));
        });
    }

    [Fact]
    public void ABustThePackDrawsNamesTheFileAfterTheFace()
    {
        // The other half of the same question, and the one that had no answer
        // at all: a pack bust's art comes from one prefix, and the editor said
        // it loaded exactly four files. A face the author added was not among
        // them, so there was nowhere to put its art and it silently did
        // nothing in the game.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, _) = TheirsIn(vm);

            var added = character.AddOutfit();
            vm.SelectedOutfit = added;
            WindowHarness.Pump();

            string before = added.ExpressionFilesHint;
            _out.WriteLine("before: " + before);
            Assert.Equal("Happy.png/Angry.png/Sad.png/Flirty.png", before);

            var face = character.AddExpression();
            face.Key = "Smirk";
            WindowHarness.Pump();

            _out.WriteLine("after:  " + added.ExpressionFilesHint);
            Assert.Contains("Smirk.png", added.ExpressionFilesHint);

            // ...and the four are still there, since the prototype bust has
            // them whatever the pack says.
            Assert.Contains("Happy.png", added.ExpressionFilesHint);
        });
    }

    [Fact]
    public void TheArtIsNamedAfterTheChildNotTheKey()
    {
        // A face row has two columns: what a dialogue node asks for, and what
        // the runtime switches on. The art belongs to the second. Naming it
        // after the first would have the editor asking for smirk.png while the
        // game loaded Smirk.png, with nothing anywhere to say so.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var (character, outfit) = TheirsIn(vm);
            vm.SelectedOutfit = outfit;
            outfit.OverridesShown = true;
            WindowHarness.Pump();

            var face = character.AddExpression();
            face.Key = "smirk";
            face.ExpressionGoName = "Smirk";
            WindowHarness.Pump();

            _out.WriteLine(string.Join(", ", outfit.Overrides.Select(r => r.Slot)));
            Assert.Contains(outfit.Overrides, r => r.Slot == SpriteSlotNames.Expression("Smirk"));
            Assert.DoesNotContain(outfit.Overrides, r => r.Slot == SpriteSlotNames.Expression("smirk"));

            // ...and a pack bust's prefix asks for the same filename.
            var added = character.AddOutfit();
            WindowHarness.Pump();
            _out.WriteLine(added.ExpressionFilesHint);
            Assert.Contains("Smirk.png", added.ExpressionFilesHint);
        });
    }

    [Fact]
    public void AFaceWithNoArtIsReported()
    {
        // Before this, a missing expression file was the one kind of missing
        // art nothing mentioned - the validator only ever looked for four
        // names.
        var pack = PackRepository.CreateEmpty("faces.pack");
        var mine = new CharacterDef { Key = "sarah", Name = "Sarah", DisplayName = "Sarah" };
        mine.Outfits.Add(new OutfitDef
        {
            Key = "sarah", GameObjectName = "Sarah",
            BaseSprite = "art/base.png", BlinkSprite = "art/blink.png",
            Expression = { Enabled = true, Prefix = "art/Expression" },
        });
        mine.Expressions.Add(new ActorExpressionDef { Key = "Smirk", ExpressionGoName = "Smirk" });
        pack.Characters.Add(mine);

        // A real (empty) folder: with no pack root, CheckFile has nothing to
        // resolve a path against and says nothing at all - so a test run
        // without one would pass whatever the validator did.
        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-faces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        List<Validation.ValidationIssue> about;
        try
        {
            about = Validation.PackValidator.Validate(pack, root)
                .Where(i => i.Where.Contains("expression[")).ToList();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        foreach (var i in about) _out.WriteLine($"{i.Severity} {i.Where}");

        Assert.Contains(about, i => i.Where.Contains("expression[Smirk]"));
        // The four are still checked, or this would be reporting the new case
        // by having stopped reporting the old one.
        Assert.Contains(about, i => i.Where.Contains("expression[Happy]"));
    }

    [Fact]
    public void TheGamesOwnFacesAreShownButNotWrittenDown()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);

            // Somebody the game gave faces to.
            var character = vm.Characters.First(
                c => c.IsVanillaBust && c.GameExpressions.Count > 0);

            _out.WriteLine($"{character.DisplayName}: "
                           + string.Join(", ", character.GameExpressions));

            Assert.NotEmpty(character.Expressions);
            Assert.All(character.Expressions, e => Assert.True(e.FromTheGame));
            Assert.All(character.Expressions, e => Assert.False(e.CanEdit));

            // ...and none of it reached the manifest, which is what keeps
            // "untouched" meaning untouched.
            Assert.Empty(character.Model.Expressions);
            Assert.True(VanillaCastSeed.IsUntouched(character.Model));

            // The control: a face the pack adds IS written down, and is the
            // only editable row.
            character.AddExpression();
            Assert.Single(character.Model.Expressions);
            Assert.Single(character.Expressions.Where(e => e.CanEdit));
            Assert.False(VanillaCastSeed.IsUntouched(character.Model));
        });
    }

    [Fact]
    public void TheVoiceShownIsTheOneTheGameGaveThem()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);

            var character = vm.Characters.First(c => c.IsVanillaBust && c.Speaker != null);
            var speaker = character.Speaker!;
            _out.WriteLine($"{character.DisplayName}: {speaker.Frequency} @ "
                           + $"{speaker.PitchMin}-{speaker.PitchMax}");

            // Invariant, as the boxes are: these strings go into a manifest
            // that has to read the same on a machine using a decimal comma.
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(speaker.Frequency.ToString(invariant), character.TypewriterFrequencyText);
            Assert.Equal(speaker.PitchMin.ToString(invariant), character.TypewriterPitchMinText);
            Assert.Equal(speaker.PitchMax.ToString(invariant), character.TypewriterPitchMaxText);
            Assert.True(character.HasGameVoice);

            // Reading it wrote nothing: a character somebody merely looked at
            // still leaves the manifest alone.
            Assert.Null(character.Model.Typewriter);
            Assert.True(VanillaCastSeed.IsUntouched(character.Model));

            // And it is still the author's to change.
            character.TypewriterPitchMaxText = "2.5";
            Assert.NotNull(character.Model.Typewriter);
            Assert.Equal(2.5f, character.Model.Typewriter!.PitchMax);
            Assert.False(VanillaCastSeed.IsUntouched(character.Model));
        });
    }
}
