using System.Linq;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Packs switched on and off from the game's main menu, and what a save keeps
/// of the packs that are not running. Shared with the plugin, which does the
/// switching and the writing; the rules are checked here.
/// </summary>
public sealed class SaveRecordTests
{
    // ── Switching packs off ───────────────────────────────────────────

    [Fact]
    public void EveryPackIsOnUntilSwitchedOff_AndANewOneIsOnWithoutATick()
    {
        Assert.True(PackSwitches.IsOn("", "Alpha"));
        Assert.True(PackSwitches.IsOn(null!, "Alpha"));

        string setting = PackSwitches.With("", "Beta", on: false);
        Assert.False(PackSwitches.IsOn(setting, "Beta"));
        Assert.False(PackSwitches.IsOn(setting, "BETA"));     // ids are not case-sensitive
        Assert.True(PackSwitches.IsOn(setting, "Alpha"));
        Assert.True(PackSwitches.IsOn(setting, "Installed Tomorrow"));
    }

    [Fact]
    public void SwitchingOneLeavesTheOthersAsTheyWere_AndTheLineReadsTheSameEveryTime()
    {
        string setting = PackSwitches.With(PackSwitches.With("", "Gamma", false), "Alpha", false);
        Assert.Equal("Alpha, Gamma", setting);
        Assert.Equal(setting, PackSwitches.With(PackSwitches.With("", "Alpha", false), "Gamma", false));

        setting = PackSwitches.With(setting, "alpha", true);
        Assert.Equal("Gamma", setting);
        Assert.Equal("", PackSwitches.With(setting, "Gamma", true));

        // An id with the separator in it cannot come back as two packs.
        Assert.Equal(new[] { "ab" }, PackSwitches.Off(PackSwitches.With("", "a,b", false)).ToArray());
    }

    // ── The record in each save ───────────────────────────────────────

    [Fact]
    public void TheRecordReadsBackAsItWasWritten()
    {
        var record = new SaveRecord
        {
            ForgeVersion = "1.6.0", GameBuild = "1.8E", Saved = "2026-09-26T12:00:00Z", Language = "ja",
            XUnityLanguage = "zh-CN",
        };
        record.Packs.Add(new SaveRecord.Pack { Id = "Beta", Version = "2.0.0", State = SaveRecord.SwitchedOff });
        record.Packs.Add(new SaveRecord.Pack { Id = "Alpha", Version = "1.0.0", State = SaveRecord.Running });

        var back = SaveRecord.Parse(record.ToJson())!;
        Assert.Equal("1.6.0", back.ForgeVersion);
        Assert.Equal("1.8E", back.GameBuild);
        Assert.Equal("ja", back.Language);
        Assert.Equal("zh-CN", back.XUnityLanguage);
        Assert.Equal(new[] { "Alpha", "Beta" }, back.Packs.Select(p => p.Id));
        Assert.Equal(SaveRecord.SwitchedOff, back.Find("beta")!.State);
        Assert.Equal("2.0.0", back.Find("Beta")!.Version);

        // A damaged or missing record is no reason to stop a load.
        Assert.Null(SaveRecord.Parse("{ not json"));
        Assert.Null(SaveRecord.Parse(""));
    }

    [Fact]
    public void ThePacksNotRunningAreRecordedWithWhyAndTheVersionTheirDataCameFrom()
    {
        // The loaded save was made while Beta ran at 1.0.0; Beta is 1.2.0 now,
        // and switched off. Its data is still 1.0.0's.
        var loaded = new SaveRecord();
        loaded.Packs.Add(new SaveRecord.Pack { Id = "Beta", Version = "1.0.0", State = SaveRecord.Running });

        var record = new SaveRecord();
        SaveRecord.AddPacks(record,
                            new[] { new System.Collections.Generic.KeyValuePair<string, string>("Alpha", "2.0.0") },
                            new[] { "SMSModForge_Beta.json", "SMSModForge_Gamma.json", "SMSModForge_Delta.json" },
                            installedPackIds: new[] { "Alpha", "Beta", "Delta" }, switchedOffPackIds: new[] { "Beta" },
                            loaded: loaded);

        Assert.Equal(SaveRecord.Running, record.Find("Alpha")!.State);
        Assert.Equal("2.0.0", record.Find("Alpha")!.Version);
        Assert.Equal(SaveRecord.SwitchedOff, record.Find("Beta")!.State);
        Assert.Equal("1.0.0", record.Find("Beta")!.Version);
        Assert.Equal(SaveRecord.NotLoaded, record.Find("Delta")!.State);
        Assert.Equal(SaveRecord.NotInstalled, record.Find("Gamma")!.State);
        // Not in the loaded save's record: no version, rather than a guess.
        Assert.Equal("", record.Find("Gamma")!.Version);
        Assert.Equal("", record.Find("Delta")!.Version);
    }

    [Fact]
    public void OnlyAnInstalledPackCanBeSwitchedOff()
    {
        // The setting outlives an uninstall; the pack is still not installed.
        Assert.Equal(SaveRecord.NotInstalled, SaveRecord.WhyNotRunning("Beta", new string[0], new[] { "Beta" }));
        Assert.Equal(SaveRecord.SwitchedOff, SaveRecord.WhyNotRunning("beta", new[] { "Beta" }, new[] { "BETA" }));
        Assert.Equal(SaveRecord.NotLoaded, SaveRecord.WhyNotRunning("Beta", new[] { "Beta" }, new string[0]));
    }

    // ── What a written save keeps ─────────────────────────────────────

    private static readonly string[] Loaded =
    {
        "SAVE.GZ", "SMSModForge_Alpha.json", "SMSModForge_Beta.json", "SMSModForge_Gamma.json", SaveRecord.FileName,
    };

    [Fact]
    public void ThePacksNotRunningAreCarriedIntoTheSavesTheSessionMakes()
    {
        // Alpha runs; Beta is switched off; Gamma is not installed. Both of the
        // last two keep their progress in every save made while they are off.
        Assert.Equal(new[] { "SMSModForge_Beta.json", "SMSModForge_Gamma.json" },
                     SaveRecord.Carried(Loaded, new[] { "alpha" }));
        Assert.Empty(SaveRecord.Carried(Loaded, new[] { "Alpha", "Beta", "Gamma" }));
    }

    [Fact]
    public void ASlotWrittenOverLosesOnlyThePackFilesOfTheSaveThatWasThere()
    {
        // The slot held another playthrough, with data for Delta and Beta.
        var target = new[] { "SAVE.GZ", "SMSModForge_Alpha.json", "SMSModForge_Beta.json", "SMSModForge_Delta.json",
                             SaveRecord.FileName };
        var carried = SaveRecord.Carried(Loaded, new[] { "Alpha" });

        // Delta belongs to nothing going into the slot: left there it would be
        // read as this save's. Beta is being carried in, and Alpha written.
        Assert.Equal(new[] { "SMSModForge_Delta.json" }, SaveRecord.Stale(target, new[] { "Alpha" }, carried));
        // The game's files and the record are never touched.
        Assert.DoesNotContain("SAVE.GZ", SaveRecord.Stale(target, new string[0], new string[0]));
        Assert.DoesNotContain(SaveRecord.FileName, SaveRecord.Stale(target, new string[0], new string[0]));
    }
}
