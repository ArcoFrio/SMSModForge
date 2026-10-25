using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A line's sounds as the editor and the game both read them (1.8.0): which
/// patterns are in a line and in what order, the size each is written in,
/// where the typewriter reaches it, what is left of a line whose sounds hide
/// their words, and when each starts.
/// </summary>
public class SoundCuesTests
{
    private static readonly List<KeyValuePair<string, string>> Patterns = new()
    {
        new("*clink*", "Clink"), new("*buzz*", "Buzz"), new("*plap*", "Plap"), new("*plap plap*", "PlapPlap"),
    };

    [Fact]
    public void PatternsAreFoundInReadingOrderAnyCaseTheLongerWinning()
    {
        var found = SoundCues.Find("Oh *CLINK* and *plap plap* then *clink*", Patterns);
        Assert.Equal(new[] { "*clink*", "*plap plap*", "*clink*" }, found.Select(m => m.Pattern));
        Assert.Equal(new[] { 1, 2, 3 }, found.Select(m => m.Order));
        Assert.Equal(new[] { 1, 1, 2 }, found.Select(m => m.OfItsPattern));
        Assert.Equal(new[] { "Clink", "PlapPlap", "Clink" }, found.Select(m => m.Sound));
        Assert.Empty(SoundCues.Find("nothing here", Patterns));
    }

    [Fact]
    public void TheSizeAPatternIsWrittenInIsReadAsItsShare()
    {
        Assert.Equal(1.4, SoundCues.SizeAt("<size=140%><b>*THUD*", 14));              // left open, as the packs write it
        Assert.Equal(1.2, SoundCues.SizeAt("<size=1.2em>*clink*", 11));
        Assert.Null(SoundCues.SizeAt("<size=140%>big</size> *clink*", 22));         // closed before it
        Assert.Equal(0.8, SoundCues.SizeAt("<size=80%>a <size=120%>b</size> *clink*", 32));   // back to the outer one
        Assert.Null(SoundCues.SizeAt("<size=24>*clink*", 9));                         // points say nothing of the line's own
        Assert.Null(SoundCues.SizeAt("*clink*", 0));
    }

    [Fact]
    public void TheTypewriterReachesAPatternAfterTheLettersShownBeforeIt()
    {
        Assert.Equal(3, SoundCues.VisibleIndex("<b>Oh </b>*clink*", 10));
        Assert.Equal(0, SoundCues.VisibleIndex("*clink*", 0));
    }

    [Fact]
    public void AHiddenSoundsWordsLeaveTheShownLineAndATranslationsToo()
    {
        var own = "Oh *clink* there *clink*";
        var matches = SoundCues.Find(own, Patterns);
        var hideSecond = new List<SoundCues.Setting> { new() { At = 2, Pattern = "*clink*", Hidden = true } };
        Assert.Equal("Oh *clink* there", SoundCues.Hide(own, matches, hideSecond));
        // The same sound in the translation: the second *clink* there goes.
        Assert.Equal("Ah *clink* là", SoundCues.Hide("Ah *clink* là *clink*", matches, hideSecond));
        // A translator who wrote the sound another way keeps their words.
        Assert.Equal("Ah *tintín* là", SoundCues.Hide("Ah *tintín* là", matches, hideSecond));
        // The control: nothing hidden, nothing taken.
        Assert.Equal(own, SoundCues.Hide(own, matches, new List<SoundCues.Setting>()));
    }

    [Fact]
    public void ASettingLeftByAnotherPatternIsNotTaken()
    {
        var matches = SoundCues.Find("*buzz* *clink*", Patterns);
        var settings = new List<SoundCues.Setting> { new() { At = 1, Pattern = "*clink*", Volume = 2f } };
        Assert.Null(SoundCues.For(matches[0], settings));   // place 1 is *buzz* now
    }

    [Fact]
    public void EachSoundStartsAfterTheOneBeforeItEndsAndNegativeOverlaps()
    {
        var matches = SoundCues.Find("*clink* *buzz* *clink* *buzz*", Patterns);
        var settings = new List<SoundCues.Setting>
        {
            new() { At = 1, Pattern = "*clink*", Delay = 0.5f },     // the first: after the line begins
            new() { At = 3, Pattern = "*clink*", Delay = -0.25f },   // over the end of the one before
            new() { At = 4, Pattern = "*buzz*", Typed = true, Delay = 0.1f },
        };
        double Length(SoundCues.Match m) => m.Sound == "Clink" ? 1.0 : 2.0;
        double TypedAt(SoundCues.Match m) => 0.75;
        var starts = SoundCues.Plan(matches, settings, Length, TypedAt);
        Assert.Equal(new[] { 0.5, 1.5, 3.25, 0.85 }, starts.Select(s => System.Math.Round(s, 3)));
    }

    [Fact]
    public void ALinesSettingsAreReadFromItsNode()
    {
        var node = JObject.Parse("{ \"cues\": [ { \"at\": 2, \"pattern\": \"*buzz*\", \"volume\": 1.5, \"delay\": -0.2, \"hidden\": true, \"typed\": true } ] }");
        var s = Assert.Single(SoundCues.Read(node["cues"]));
        Assert.Equal(2, s.At);
        Assert.Equal("*buzz*", s.Pattern);
        Assert.Equal(1.5f, s.Volume);
        Assert.Equal(-0.2f, s.Delay);
        Assert.True(s.Hidden);
        Assert.True(s.Typed);
        Assert.Equal(1.5f, SoundCues.VolumeOf(s));
        Assert.Equal(1f, SoundCues.VolumeOf(null));
    }
}
