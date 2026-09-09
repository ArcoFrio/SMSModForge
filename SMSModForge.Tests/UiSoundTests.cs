using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What a button sounds like.
/// <para/>
/// Set once for a screen, because "my buttons click" is one decision rather
/// than one per object, and overridden on a single object where one of them
/// should differ. The name is resolved at the click, against the pack's own
/// sounds first and the game's second - so a pack can shadow a game sound with
/// one of its own, and a sound that is still loading is not frozen out.
/// </summary>
public sealed class UiSoundTests
{
    private readonly ITestOutputHelper _out;
    public UiSoundTests(ITestOutputHelper o) => _out = o;

    private static ModPack PackWith(string screenSound, string buttonSound)
    {
        var pack = new ModPack { PackId = "sound-test" };
        var ui = new UiDef { Id = "screen", Name = "Screen", ButtonSound = screenSound };

        var root = new UiNodeDef { Name = "Panel" };
        var button = new UiNodeDef { Name = "Button", ClickSound = buttonSound };
        button.OnClick.Add(new NodeActionDef { Type = NodeActionTypes.SetVariable });
        root.Children.Add(button);

        ui.Nodes.Add(root);
        pack.Uis.Add(ui);
        return pack;
    }

    [Fact]
    public void Both_survive_a_save_and_a_reopen()
    {
        var saved = PackRepository.SerializeAsSaved(PackWith("Click", "Cash Register"));
        _out.WriteLine(saved.Contains("buttonSound") ? "buttonSound written" : "buttonSound MISSING");

        var back = PackRepository.Deserialize(saved);
        Assert.NotNull(back);

        var ui = back!.Uis.Single();
        Assert.Equal("Click", ui.ButtonSound);
        Assert.Equal("Cash Register", ui.Nodes[0].Children[0].ClickSound);
    }

    [Fact]
    public void Neither_is_written_when_it_has_nothing_to_say()
    {
        // A field that serialises its own empty default puts a line in every
        // manifest ever written, for nothing.
        string saved = PackRepository.SerializeAsSaved(PackWith("", ""));

        _out.WriteLine(saved.Length + " chars");
        Assert.DoesNotContain("buttonSound", saved);
        Assert.DoesNotContain("clickSound", saved);
    }

    [Fact]
    public void A_screen_sound_alone_is_enough()
    {
        // The ordinary case: one setting, no per-object anything.
        string saved = PackRepository.SerializeAsSaved(PackWith("Click", ""));

        Assert.Contains("buttonSound", saved);
        Assert.DoesNotContain("clickSound", saved);

        var back = PackRepository.Deserialize(saved);
        Assert.Equal("Click", back!.Uis.Single().ButtonSound);
        Assert.Equal("", back.Uis.Single().Nodes[0].Children[0].ClickSound);
    }

    [Fact]
    public void The_games_own_sounds_are_offered_by_name()
    {
        // A pack never ships one of these - the runtime asks the game for the
        // clip it already has - so the only thing the editor needs is to be
        // able to offer the name instead of having it typed from memory.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var names = VanillaUiLibrary.SoundNames.ToList();
        _out.WriteLine(names.Count + " sound(s): " + string.Join(", ", names.Take(8)));

        Assert.NotEmpty(names);

        // The name is the file name, because that is what the game calls the
        // clip and what the runtime looks up - no extension, no path.
        foreach (var n in names)
        {
            Assert.DoesNotContain(".", n);
            Assert.NotNull(VanillaUiLibrary.SoundFile(n));
        }
    }

    [Fact]
    public void A_button_can_be_given_one_from_either_place()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            // Through the editor's own add, because the options list is built
            // from the tab's rows rather than straight off the pack.
            vm.AddSfxCommand.Execute(null);
            vm.Sfx.Last().Key = "my-click";
            vm.RebuildSfxKeyOptions();
            WindowHarness.Pump();

            _out.WriteLine(string.Join(", ", vm.ButtonSoundOptions));

            Assert.Contains("my-click", vm.ButtonSoundOptions);
            foreach (var n in VanillaUiLibrary.SoundNames)
                Assert.Contains(n, vm.ButtonSoundOptions);

            // The pack's own lead: a name in both places resolves to the pack's,
            // so that is the order the list should read in.
            if (vm.ButtonSoundOptions.Count > 1 && VanillaUiLibrary.SoundNames.Any())
                Assert.Equal("my-click", vm.ButtonSoundOptions[0]);
        });
    }

    // -- The default -------------------------------------------------

    [Fact]
    public void Buttons_make_a_sound_without_anyone_asking()
    {
        // A screen that says nothing about sound still has clicking buttons:
        // "I have not chosen a sound" and "I want no sound" are different
        // answers, and only the first is what leaving a field alone should mean.
        var ui = new UiDef { Id = "screen" };

        Assert.False(ui.SilentButtons);
        Assert.Equal("", ui.ButtonSound);

        string saved = PackRepository.SerializeAsSaved(
            new ModPack { PackId = "p", Uis = { ui } });

        // And it says so by saying nothing - the ordinary answer costs no line.
        Assert.DoesNotContain("buttonSound", saved);
        Assert.DoesNotContain("silentButtons", saved);
    }

    [Fact]
    public void Silence_is_a_choice_that_has_to_be_written_down()
    {
        var ui = new UiDef { Id = "screen", SilentButtons = true };
        string saved = PackRepository.SerializeAsSaved(
            new ModPack { PackId = "p", Uis = { ui } });

        _out.WriteLine(saved.Contains("silentButtons") ? "written" : "MISSING");
        Assert.Contains("silentButtons", saved);

        var back = PackRepository.Deserialize(saved);
        Assert.True(back!.Uis.Single().SilentButtons);
    }

    [Fact]
    public void The_checkbox_reads_the_way_an_author_thinks_about_it()
    {
        // Stored as silence, shown as sound: an absent field means the ordinary
        // behaviour, and the box in front of a person is ticked for a screen
        // whose buttons click.
        var row = new UiViewModel(new UiDef { Id = "screen" });
        Assert.True(row.ButtonsMakeSound);

        row.ButtonsMakeSound = false;
        Assert.True(row.Model.SilentButtons);

        row.ButtonsMakeSound = true;
        Assert.False(row.Model.SilentButtons);
    }

    [Fact]
    public void The_editor_and_the_runtime_agree_on_what_the_default_is()
    {
        // Two projects that cannot reference each other holding the same string:
        // the runtime's copy is the one that plays, the editor's is what the
        // field says it will do when left blank, and a change to one that misses
        // the other is a screen that sounds different from what it says.
        string? plugin = FindPluginSource();
        if (plugin == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(plugin);
        string expected = "internal const string DefaultButtonSound = \"" +
                          UiDef.DefaultButtonSound + "\";";

        _out.WriteLine("editor says: " + UiDef.DefaultButtonSound);
        Assert.Contains(expected, text);
    }

    [Fact]
    public void The_default_is_a_sound_the_editor_can_actually_offer()
    {
        // Naming a clip nothing knows about would be a default that plays
        // nothing, which is the same as having no default at all.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        Assert.Contains(UiDef.DefaultButtonSound, VanillaUiLibrary.SoundNames);
    }

    private static string? FindPluginSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge.PackPlugin", "UiFactory.cs");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void The_click_is_a_little_quieter_than_everything_else()
    {
        // 85%: a button is pressed far more often than anything else on screen
        // happens, so its click sits behind whatever is being said or played
        // rather than on top of it.
        string? plugin = FindPluginFile("UiButtonClick.cs");
        if (plugin == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(plugin);
        _out.WriteLine(text.Contains("0.85f") ? "85%" : "NOT 85%");
        Assert.Contains("public const float DefaultClickVolume = 0.85f;", text);
    }

    [Fact]
    public void A_sound_that_is_not_there_says_so()
    {
        // A button that makes no noise looks exactly like a button that was
        // never meant to, so the difference between "silent on purpose" and
        // "the name is wrong" has to reach the log - once per name, not once
        // per click.
        string? plugin = FindPluginFile("UiFactory.cs");
        if (plugin == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(plugin);
        Assert.Contains("no sound called", text);
        Assert.Contains("MissingSounds.Add(name)", text);   // once, not every click
    }

    private static string? FindPluginFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge.PackPlugin", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void A_blank_field_says_what_it_is_going_to_play()
    {
        // Reported as "my screens have an empty button sound". Blank IS the
        // default - the runtime falls back to the game's click - but a field
        // that looks empty reads as silent to everyone who did not write it.
        var row = new UiViewModel(new UiDef { Id = "s" });

        Assert.True(row.ShowsDefaultButtonSound);
        _out.WriteLine(row.ButtonSoundInEffect);
        Assert.Contains(UiDef.DefaultButtonSound, row.ButtonSoundInEffect);
    }

    [Fact]
    public void It_stops_saying_so_once_the_screen_has_chosen()
    {
        var row = new UiViewModel(new UiDef { Id = "s" });

        row.ButtonSound = "my-click";
        Assert.False(row.ShowsDefaultButtonSound);   // the field speaks for itself

        row.ButtonSound = "";
        Assert.True(row.ShowsDefaultButtonSound);
    }

    [Fact]
    public void A_silenced_screen_does_not_claim_to_play_anything()
    {
        var row = new UiViewModel(new UiDef { Id = "s" });
        row.ButtonsMakeSound = false;

        Assert.False(row.ShowsDefaultButtonSound);
        Assert.Equal("", row.ButtonSoundInEffect);
    }
}
