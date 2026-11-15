using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A new name for one of the game's characters (1.9.0): what the pack writes, and
/// what it must not. The runtime half is in the in-game suite.
/// </summary>
public sealed class GameCharacterNewNameTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public GameCharacterNewNameTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-newname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static JObject? Entry(string savedJson, string vanilla)
        => (JObject.Parse(savedJson)["characters"] as JArray)?
            .OfType<JObject>().FirstOrDefault(c => (string?)c["vanillaCharacter"] == vanilla);

    [Fact]
    public void ANewNameIsWrittenForOneOfTheGamesCharacters_AndOnlyWhenOneIsGiven()
    {
        var pack = PackRepository.CreateEmpty("names.pack");
        var anna = pack.Characters.Single(c => c.VanillaCharacter == "Anna");

        // Untouched, she is not in the file at all.
        Assert.Null(Entry(PackRepository.SerializeAsSaved(pack), "Anna"));

        anna.NewName = "Annie";
        var written = Entry(PackRepository.SerializeAsSaved(pack), "Anna");
        Assert.NotNull(written);
        Assert.Equal("Annie", (string?)written!["newName"]);
        // The game's name stays where it was: every load puts that one back.
        Assert.Equal("Anna", (string?)written["displayName"]);
        Assert.Equal("Annie", anna.ShownName);
        Assert.False(VanillaCastSeed.IsUntouched(anna));

        // Blank is no new name, and leaves her untouched again.
        anna.NewName = "   ";
        Assert.Null(Entry(PackRepository.SerializeAsSaved(pack), "Anna"));
        Assert.Equal("Anna", anna.ShownName);
        Assert.True(VanillaCastSeed.IsUntouched(anna));
    }

    [Fact]
    public void APacksOwnCharacterNeverCarriesOne()
    {
        // The control: the field means nothing on a character the pack drew, whose
        // name is its display name.
        var own = new CharacterDef { Key = "Sakura", Name = "Sakura", DisplayName = "Sakura", NewName = "Blossom" };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(own);
        Assert.DoesNotContain("newName", json);
        Assert.Equal("Sakura", own.ShownName);
    }

    [Fact]
    public void ItSurvivesTheRealSaveTheExportAndTheNextLoad()
    {
        string folder = Path.Combine(_root, "pack");
        var pack = PackRepository.CreateEmpty("names.pack");
        pack.Characters.Single(c => c.VanillaCharacter == "Adrian").NewName = "Adriano";
        PackRepository.Save(pack, folder);

        string archive = Path.Combine(_root, "names.smspack");
        PackExporter.Export(folder, archive);
        using (var zip = ZipFile.OpenRead(archive))
        {
            var entry = zip.Entries.First(e => e.FullName.Replace('\\', '/') == "modpack.json");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var shipped = Entry(reader.ReadToEnd(), "Adrian");
            Assert.Equal("Adriano", (string?)shipped?["newName"]);
        }

        // Loaded again, with every migration run: the game's name is put back on
        // displayName (as it always is) and the new name is left alone.
        var back = PackRepository.Load(folder);
        var adrian = back.Characters.Single(c => c.VanillaCharacter == "Adrian");
        Assert.Equal("Adrian", adrian.DisplayName);
        Assert.Equal("Adriano", adrian.NewName);
        Assert.Equal("Adriano", adrian.ShownName);
    }

    [Fact]
    public void AnOldPacksGuessedNameIsStillTakenBack_NotMistakenForANewOne()
    {
        // Packs before 1.3 wrote their own displayName for the game's characters.
        // That is not a rename and never became one: the migration still puts the
        // game's name back, and no new name appears from it.
        var pack = PackRepository.CreateEmpty("old.pack");
        pack.ForgeVersion = "1.2.0";
        var adrian = pack.Characters.Single(c => c.VanillaCharacter == "Adrian");
        adrian.DisplayName = "Adrian (the neighbour)";

        PackMigration.Apply(pack);

        Assert.Equal("Adrian", adrian.DisplayName);
        Assert.Null(adrian.NewName);
        Assert.Equal("Adrian", adrian.ShownName);
    }
}

/// <summary>
/// The Characters tab's name box on one of the game's characters, driven through the
/// real window: a binding to a property that is not there fails silently in WPF.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GameCharacterNewNameOnScreenTests
{
    private readonly ITestOutputHelper _out;
    public GameCharacterNewNameOnScreenTests(ITestOutputHelper o) => _out = o;

    private static void ShowCharacters(MainWindow window)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Characters")
            { tabs.SelectedIndex = i; WindowHarness.Pump(); return; }
        throw new Xunit.Sdk.XunitException("no Characters tab");
    }

    private static CharacterViewModel Anna(MainViewModel vm)
        => vm.Characters.First(c => c.Model.VanillaCharacter == "Anna");

    [Fact]
    public void TheirNameBoxTakesANewName_AndTheRestOfTheTabFollowsIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var anna = Anna(vm);
            vm.SelectedCharacter = anna;
            WindowHarness.Pump();

            var box = (TextBox)window.FindName("CharacterNameBox");
            var reset = (Button)window.FindName("ResetNameButton");
            var note = (TextBlock)window.FindName("RenameNoteText");

            // As the tab draws her before anything is typed: the game's name, in a
            // box that can be typed in, a Reset with nothing to put back.
            Assert.Equal("Anna", box.Text);
            Assert.True(box.IsEnabled, "the game's character's name box is greyed");
            Assert.True(reset.IsVisible && !reset.IsEnabled);
            Assert.Equal(Localization.Loc.T("characters.rename.note"), note.Text);

            // Typed, as an author types it.
            box.Text = "Annie";
            WindowHarness.Pump();
            Assert.Equal("Annie", anna.Model.NewName);
            Assert.Equal("Anna", anna.Model.DisplayName);
            Assert.Equal("Annie", anna.DisplayName);
            Assert.True(reset.IsEnabled, "nothing offers to put her name back");
            Assert.Contains("Anna", note.Text);
            Assert.True(anna.IsModified);
            Assert.Contains("Annie", anna.ChangedFields);
            _out.WriteLine(note.Text);

            // The rows under the name box don't move off: the note wraps inside the column.
            var grid = (System.Windows.FrameworkElement)((System.Windows.FrameworkElement)box.Parent).Parent;
            var column = (System.Windows.FrameworkElement)grid.Parent;
            double noteRight = note.TranslatePoint(new System.Windows.Point(note.ActualWidth, 0), column).X;
            _out.WriteLine($"note {note.ActualWidth:0} wide, right edge {noteRight:0} of {column.ActualWidth:0}");
            Assert.True(noteRight <= column.ActualWidth + 0.5, "the note runs past the panel");

            // A picture to look at, when asked for (SMSMODFORGE_SNAPSHOTS): one per theme.
            string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
            if (!string.IsNullOrWhiteSpace(shots))
            {
                Directory.CreateDirectory(shots);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(column.ActualWidth), (int)Math.Ceiling(Math.Min(column.ActualHeight, 400)),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(column);
                ThemeSnapshotTests.Save(bmp, Path.Combine(shots, "game-character-new-name-" +
                    (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png"));
            }

            // Reset, by the button.
            reset.Command.Execute(reset.CommandParameter);
            WindowHarness.Pump();
            Assert.Null(anna.Model.NewName);
            Assert.Equal("Anna", box.Text);
            Assert.False(anna.IsModified);
        });
    }

    [Fact]
    public void TypingTheirOwnNameBack_OrEmptyingTheBox_IsNoNewName()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var anna = Anna(vm);
            vm.SelectedCharacter = anna;
            WindowHarness.Pump();
            var box = (TextBox)window.FindName("CharacterNameBox");

            box.Text = "Ann";
            WindowHarness.Pump();
            Assert.Equal("Ann", anna.Model.NewName);

            box.Text = "Anna";
            WindowHarness.Pump();
            Assert.Null(anna.Model.NewName);
            Assert.False(anna.IsModified);

            // Emptied: no new name, and the box stays empty under the caret rather
            // than jumping back to "Anna" mid-word.
            box.Text = "";
            WindowHarness.Pump();
            Assert.Null(anna.Model.NewName);
            Assert.Equal("", box.Text);

            // ...until another character is chosen, when it says Anna again.
            vm.SelectedCharacter = vm.Characters.First(c => !ReferenceEquals(c, anna));
            WindowHarness.Pump();
            vm.SelectedCharacter = anna;
            WindowHarness.Pump();
            Assert.Equal("Anna", box.Text);
        });
    }

    [Fact]
    public void ANameOfTwoWordsCanBeTyped_AndTheNewNameIsKeptWithoutItsSpaces()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            var anna = Anna(vm);
            vm.SelectedCharacter = anna;
            WindowHarness.Pump();
            var box = (TextBox)window.FindName("CharacterNameBox");

            // Typed as a person types it: the space before the second word stays in
            // the box, and the rename is kept trimmed, as the game shows it.
            box.Text = "Ann ";
            WindowHarness.Pump();
            Assert.Equal("Ann ", box.Text);
            Assert.Equal("Ann", anna.Model.NewName);
            box.Text = "Ann Lee ";
            WindowHarness.Pump();
            Assert.Equal("Ann Lee", anna.Model.NewName);
            // The control: her own name with a space after it is no new name.
            box.Text = "Anna ";
            WindowHarness.Pump();
            Assert.Null(anna.Model.NewName);
        });
    }

    [Fact]
    public void WhileATranslationIsEdited_TheirNameBoxIsLocked_SinceANewNameIsEveryLanguages()
    {
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-newname-tr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = PackRepository.CreateEmpty("newname.tr");
            var d = new DialogueDef { Key = "beach" };
            d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hello there!" });
            pack.Dialogues.Add(d);
            PackRepository.Save(pack, dir);
            var source = PackTranslations.Source(pack);
            var es = new TextFile();
            foreach (var e in source.Entries) es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text, English = e.Text });
            PackTranslations.Write(pack, dir, "es", source, es);

            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(dir);
                WindowHarness.Pump();
                ShowCharacters(window);
                var anna = Anna(vm);
                vm.SelectedCharacter = anna;
                WindowHarness.Pump();
                var box = (TextBox)window.FindName("CharacterNameBox");
                Assert.True(box.IsEnabled);        // the control: in the pack's own words

                vm.EditingLanguage = "es";
                WindowHarness.Pump();
                anna = Anna(vm);
                vm.SelectedCharacter = anna;
                WindowHarness.Pump();
                Assert.True(vm.IsEditingTranslation);
                Assert.False(box.IsEnabled, "the game character's name box takes a rename while a translation is edited");
                anna.NameBox = "Anna";            // as typed, even past the lock
                Assert.Null(anna.Model.NewName);
                // A character of the pack's own is still named in the translation.
                vm.AddCharacterCommand.Execute(null);
                WindowHarness.Pump();
                Assert.True(box.IsEnabled);

                vm.EditingLanguage = "";
                WindowHarness.Pump();
                vm.SelectedCharacter = Anna(vm);
                WindowHarness.Pump();
                Assert.True(box.IsEnabled);
            });
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void ThePacksOwnSakura_IsNotTheSpeakerOfTheGamesSakurasLines()
    {
        // As in a pack with a Sakura of its own keyed "sakura" (the game's Sakura's key,
        // so the game's is not added beside her): a line of the game's spoken by its
        // actor V_Sakura took the pack's Sakura's name and colour - and offered her
        // token as "speaking here".
        string dir = Path.Combine(Path.GetTempPath(), "smsmodforge-ownsakura-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pack = PackRepository.CreateEmpty("own.sakura");
            pack.Characters.RemoveAll(c => c.Key == "sakura");
            pack.Characters.Add(new CharacterDef { Key = "sakura", Name = "OwnSakura", DisplayName = "Sakura of Ours",
                                                   NameColor = "#123456", Pronouns = Pronouns.Female, BustSource = BustSource.Pack });
            PackRepository.Save(pack, dir);
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(dir);
                WindowHarness.Pump();
                Assert.DoesNotContain(vm.Characters, c => c.Model.VanillaCharacter == "Sakura");

                var games = new DialogueNodeViewModel(new DialogueNodeDef { Id = 1, Actor = "V_Sakura", Text = "Hello." });
                var ours = new DialogueNodeViewModel(new DialogueNodeDef { Id = 2, Actor = "sakura", Text = "Hello." });
                _out.WriteLine($"V_Sakura: '{games.SpeakerName}' {games.SpeakerColorHex}; sakura: '{ours.SpeakerName}' {ours.SpeakerColorHex}");
                Assert.Equal("Sakura", games.SpeakerName);         // the game's, as the game calls her
                Assert.NotEqual("#123456", games.SpeakerColorHex);
                // The control: the pack's own line of hers is hers.
                Assert.Equal("Sakura of Ours", ours.SpeakerName);
                Assert.Equal("#123456", ours.SpeakerColorHex);
            });
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void APacksOwnCharacterIsRenamedTheWayItAlwaysWas()
    {
        // The control: a character of the pack's own has no new name and no Reset;
        // the box is its display name.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ShowCharacters(window);
            vm.AddCharacterCommand.Execute(null);
            WindowHarness.Pump();
            var mine = vm.SelectedCharacter!;
            Assert.False(mine.Model.IsVanillaCharacter);

            var box = (TextBox)window.FindName("CharacterNameBox");
            var reset = (Button)window.FindName("ResetNameButton");
            box.Text = "Blossom";
            WindowHarness.Pump();
            Assert.Equal("Blossom", mine.Model.DisplayName);
            Assert.Null(mine.Model.NewName);
            Assert.False(reset.IsVisible);
        });
    }
}
