using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How the game's own characters speak.
/// <para/>
/// Read out of a running game rather than out of its files, because none of it
/// is in the files: the Actor assets carry no references an extractor can
/// follow, and the name colours live in a private list on a component that
/// does not exist until a conversation has started. See
/// Tools/regen_vanilla_speech.py for how the dumps are taken.
/// <para/>
/// What is asserted here is the part the editor and the runtime are built on —
/// the numbering, and the join to the catalog — rather than any one
/// character's pitch.
/// </summary>
public sealed class VanillaSpeechTests
{
    private readonly ITestOutputHelper _out;
    public VanillaSpeechTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryFaceIsTheSameNumberForEverybody()
    {
        // The whole reason the runtime can map a name an author typed onto a
        // number the game understands. The generator refuses to write if this
        // breaks, but the generator only runs when somebody remembers to.
        var expected = new (string Name, int Value)[]
        {
            ("neutral", 0), ("Happy", 1), ("Angry", 2), ("Sad", 3), ("Flirty", 4),
        };

        int checkedFaces = 0;
        foreach (var s in VanillaSpeech.All)
            foreach (var (name, value) in expected)
                if (s.Expressions.Contains(name))
                {
                    Assert.Equal(value, s.ValueOf(name));
                    checkedFaces++;
                }

        _out.WriteLine($"{checkedFaces} faces across {VanillaSpeech.All.Length} characters");
        Assert.True(checkedFaces > 200, "far too few - did the dataset get truncated?");
    }

    [Fact]
    public void ACharacterWhoCanEmoteHasSomewhereToPutIt()
    {
        // An expression is a number written into a named variable. A character
        // with faces and no variable would have the editor offering an author
        // something the runtime cannot deliver.
        var stranded = VanillaSpeech.All
            .Where(s => s.Expressions.Length > 0 && string.IsNullOrEmpty(s.ExpressionVariable))
            .ToList();

        Assert.True(stranded.Count == 0,
                    "faces with no variable to write: " + string.Join(", ", stranded.Select(s => s.Name)));

        var speakers = VanillaSpeech.All.Where(s => s.Expressions.Length > 0).ToList();
        _out.WriteLine($"{speakers.Count} of {VanillaSpeech.All.Length} characters can pull a face");
        Assert.True(speakers.Count > 40);
    }

    [Fact]
    public void EverybodyHereIsSomebodyTheCatalogHas()
    {
        // The do-not-ship rule reaches this file too: characters excluded from
        // the catalog are excluded from the repository, and a dataset
        // generated out of a running game is a side door into it.
        var known = new System.Collections.Generic.HashSet<string>(
            VanillaCharacters.All.Select(c => VanillaCastData.KeyFor(c.Name)),
            StringComparer.OrdinalIgnoreCase);

        var strangers = VanillaSpeech.All.Where(s => !known.Contains(s.Key)).ToList();
        Assert.True(strangers.Count == 0,
                    "in the speech data but not in the catalog: "
                    + string.Join(", ", strangers.Select(s => s.Name)));

        _out.WriteLine($"{VanillaSpeech.All.Length} of {VanillaCharacters.All.Count} "
                       + "catalogued characters were given a voice");
    }

    [Fact]
    public void ANameColourIsAColour()
    {
        var coloured = VanillaSpeech.All.Where(s => s.NameColor != null).ToList();
        _out.WriteLine($"{coloured.Count} characters have a name colour");
        Assert.True(coloured.Count > 30);

        foreach (var s in coloured)
            Assert.Matches(new Regex("^#[0-9A-F]{6}$"), s.NameColor);
    }

    [Fact]
    public void AVoiceIsSomethingYouCouldPlay()
    {
        foreach (var s in VanillaSpeech.All)
        {
            Assert.InRange(s.Frequency, 1, 200);
            Assert.InRange(s.PitchMin, 0.05f, 5f);
            Assert.True(s.PitchMax >= s.PitchMin, s.Name);
        }

        // The open question this dataset settled: whether the typewriter is a
        // property of the speech skin, in which case there would be nothing
        // per-character to offer an author. It is not - the pitch ranges are
        // genuinely different per person.
        int distinct = VanillaSpeech.All
            .Select(s => (s.PitchMin, s.PitchMax, s.Frequency))
            .Distinct().Count();
        _out.WriteLine($"{distinct} distinct voices across {VanillaSpeech.All.Length} characters");
        Assert.True(distinct > 10, "one voice for everybody - is this per-skin after all?");
    }

    [Fact]
    public void ABustWithFacesBelongsToSomebodyWhoCanWearThem()
    {
        // The runtime decides whether to write a character's expression number
        // by asking whether the bust on screen is one of the game's own with
        // the four faces. If these two datasets stopped agreeing - a key
        // derivation drifting, a regenerated catalog - that gate would simply
        // stop firing, silently, and expressions would go back to being undone
        // by the next global variable write.
        var wearable = VanillaCharacters.All
            .Where(c => c.Outfits.Any(VanillaBustExpressions.Has))
            .ToList();

        var canWrite = wearable
            .Where(c => VanillaSpeech.For(VanillaCastData.KeyFor(c.Name))?.ExpressionVariable != null)
            .ToList();

        foreach (var c in wearable.Except(canWrite))
            _out.WriteLine($"   has the art but no number: {c.Name}");

        _out.WriteLine($"{canWrite.Count} of {wearable.Count} characters with expression art "
                       + "have a number to write");
        Assert.True(canWrite.Count >= wearable.Count - 3,
                    "too many characters have the four faces and nowhere to say so - "
                    + "did the two datasets stop agreeing on keys?");
        Assert.True(canWrite.Count > 40);
    }

    [Fact]
    public void TheKeyAPackWritesDownIsTheKeyTheRuntimeLooksUp()
    {
        // The join that decides whether one of the game's characters sounds
        // like themselves in a mod. The editor asks this dataset by the
        // character's name; the runtime asks it by the key sitting in the
        // manifest. If those two derivations ever drift, the runtime simply
        // finds nothing and falls back to a generic voice - which is not an
        // error anybody sees, just Adrian sounding like a stranger.
        var pack = new SMSModForge.Model.ModPack();
        SMSModForge.Model.VanillaCastSeed.Seed(pack);

        var written = pack.Characters.Where(c => c.IsVanillaCharacter).ToList();
        Assert.NotEmpty(written);

        var found = written.Where(c => VanillaSpeech.For(c.Key) != null).ToList();
        _out.WriteLine($"{found.Count} of {written.Count} seeded characters resolve to a voice");
        Assert.Equal(VanillaSpeech.All.Length, found.Count);

        // ...and it is the RIGHT one, not merely some entry.
        foreach (var c in found)
            Assert.Equal(c.VanillaCharacter is null ? "" : VanillaSpeech.For(c.Key)!.Key,
                         VanillaCastData.KeyFor(
                             VanillaCastData.SpokenName(c.VanillaCharacter)));
    }

    [Fact]
    public void PhoenixSmilesWithHerOwnFace()
    {
        // The game's own data has Phoenix's Happy setting GABRIEL's number:
        // four of her five faces write Phoenix-expression and Happy writes
        // his, which reads as a copy-paste. In the game, asking Phoenix to
        // smile changes Gabriel's face instead.
        //
        // The author's decision (2026-09-09) is that ModForge does not
        // reproduce it - fixed for pack dialogue, left alone everywhere else,
        // since nothing here touches the game's own conversations. Pinned
        // because the generator takes the majority variable silently, and
        // "make it faithful to the dump" is an obvious-looking change for
        // somebody to make later.
        var phoenix = VanillaSpeech.For("phoenix");
        Assert.NotNull(phoenix);
        Assert.Equal("Phoenix-expression", phoenix!.ExpressionVariable);
        Assert.Equal(1, phoenix.ValueOf("Happy"));

        var gabriel = VanillaSpeech.For("gabriel");
        Assert.NotNull(gabriel);
        Assert.Equal("Gabriel-expression", gabriel!.ExpressionVariable);
        Assert.NotEqual(gabriel.ExpressionVariable, phoenix.ExpressionVariable);
    }

    [Fact]
    public void NobodyTheGameNeverGaveAVoiceGetsOne()
    {
        // The control. A lookup that answered for everything would have the
        // editor showing a pack's own character the game's defaults.
        Assert.Null(VanillaSpeech.For("a-character-the-game-never-had"));
        Assert.Null(VanillaSpeech.For(""));
        Assert.Null(VanillaSpeech.For(null));
        Assert.False(VanillaSpeech.Has("a-character-the-game-never-had"));

        // And a face nobody has resolves to nothing rather than to zero, which
        // is neutral and would read as a real answer.
        var anyone = VanillaSpeech.All.First(s => s.Expressions.Length > 0);
        Assert.Equal(-1, anyone.ValueOf("a-face-the-game-never-had"));
        Assert.Equal(-1, anyone.ValueOf(null));
    }

    [Fact]
    public void TheGeneratedFileSaysWhereItCameFrom()
    {
        // Somebody will open this file, see 84 characters' pitch ranges and
        // wonder where they were typed. They were not typed.
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here != null && !Directory.Exists(Path.Combine(here.FullName, "Shared")))
            here = here.Parent;
        Assert.NotNull(here);

        string source = File.ReadAllText(
            Path.Combine(here!.FullName, "Shared", "VanillaSpeech.cs"));
        Assert.Contains("regen_vanilla_speech.py", source);
        Assert.Contains("Do not edit by hand", source);

        Assert.True(File.Exists(Path.Combine(here.FullName, "Tools", "regen_vanilla_speech.py")),
                    "the generated file names a generator that is not in the repository");
    }
}
