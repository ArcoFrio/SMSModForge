using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A save with data from a pack that is not running says so once, not every
/// time it is loaded - for each pack on its own - and says it again after the
/// pack has run with the save and gone again (2026-09-27).
/// <para/>
/// "Once" is kept the way a save keeps anything: the autosave on sleeping
/// commits it, and a save from the menu only transfers what the last commit
/// wrote. So the notice stops once the autosave has happened, not when it is
/// read - quit before sleeping and it is said again.
/// <para/>
/// The slots are played here as the record files ModForge keeps in them, and
/// every load reads its record back from that text, so what is remembered is
/// what survives being written down. The game plugin takes the same steps; the
/// last tests read its source to see it does.
/// </summary>
public sealed class MissingPackToldOnceTests
{
    /// <summary>The save slots: each one's record file, as text.</summary>
    private readonly Dictionary<int, string> _slots = new();

    private sealed class Session
    {
        public int Loaded;
        public List<string> Live = new();
        public bool Autosaved;
        public string[] DataFor = new string[0];
        public string[] Running = new string[0];
    }

    private static string FileOf(string id) => SaveLoadChecks.SaveFilePrefix + id + SaveLoadChecks.SaveFileSuffix;

    private string SlotText(int slot) => _slots.TryGetValue(slot, out var text) ? text : "";

    /// <summary>
    /// Load <paramref name="slot"/>, whose folder has data for
    /// <paramref name="dataFor"/>, with <paramref name="running"/> running.
    /// Returns the session and the packs the notice names - read out of the
    /// notice itself.
    /// </summary>
    private (Session Session, List<string> Named) Load(int slot, string[] dataFor, string[] running)
    {
        var record = SaveRecord.Parse(SlotText(slot));
        var absent = SaveLoadChecks.Absent(dataFor.Select(FileOf), running, running, new string[0], record);
        var all = absent.Select(a => a.Id).ToList();
        var before = record?.ToldMissing.ToList() ?? new List<string>();

        var session = new Session { Loaded = slot, DataFor = dataFor, Running = running };
        session.Live = SaveLoadChecks.Told(before, all, null);
        var untold = SaveLoadChecks.NotYetTold(absent, before);

        var notice = SaveLoadChecks.WarningFor(null!, untold);
        var named = notice == null
            ? new List<string>()
            : dataFor.Where(id => notice.Plain().Contains(GameTexts.Quoted(id))).ToList();
        if (notice != null)
            session.Live = SaveLoadChecks.Told(session.Live, all, untold.Select(a => a.Id));
        return (session, named);
    }

    private string RecordFor(Session s, IList<string> told)
    {
        var record = new SaveRecord();
        SaveRecord.AddPacks(record, s.Running.Select(id => new KeyValuePair<string, string>(id, "1.0.0")),
                            s.DataFor.Where(id => !s.Running.Contains(id)).Select(FileOf),
                            s.Running, new string[0], SaveRecord.Parse(SlotText(s.Loaded)));
        record.ToldMissing.AddRange(told);
        return record.ToJson();
    }

    /// <summary>Sleeping: the session's changes are committed to slot 1.</summary>
    private void Autosave(Session s)
    {
        _slots[1] = RecordFor(s, s.Live);
        s.Autosaved = true;
    }

    /// <summary>A save from the menu: what the source slot holds, transferred -
    /// slot 1 once this session has autosaved, the loaded slot before.</summary>
    private void SaveFromMenu(Session s, int target)
    {
        int source = s.Autosaved ? 1 : s.Loaded;
        _slots[target] = RecordFor(s, SaveRecord.ToldIn(SlotText(source)));
    }

    // ── Once, and only once the autosave has it ─────────────────────────

    [Fact]
    public void ReadingTheNoticeIsNotEnough_TheAutosaveIsWhatRemembersIt()
    {
        var (_, named) = Load(3, new[] { "Elfenlied" }, new string[0]);
        Assert.Equal(new[] { "Elfenlied" }, named);

        // Quit without sleeping: nothing was committed, so it is said again.
        var (session, again) = Load(3, new[] { "Elfenlied" }, new string[0]);
        Assert.Equal(new[] { "Elfenlied" }, again);

        // Sleep, and the autosave has it.
        Autosave(session);
        (_, named) = Load(1, new[] { "Elfenlied" }, new string[0]);
        Assert.Empty(named);
        (_, named) = Load(1, new[] { "Elfenlied" }, new string[0]);
        Assert.Empty(named);
    }

    [Fact]
    public void ASaveFromTheMenuBeforeTheAutosave_TransfersTheSaveAsItWas()
    {
        var (session, _) = Load(3, new[] { "Alpha" }, new string[0]);
        SaveFromMenu(session, 4);

        // Slot 3 had never told anybody, and that is what went across.
        var (_, named) = Load(4, new[] { "Alpha" }, new string[0]);
        Assert.Equal(new[] { "Alpha" }, named);
    }

    [Fact]
    public void ASaveFromTheMenuAfterTheAutosave_TransfersWhatItCommitted()
    {
        var (session, _) = Load(3, new[] { "Alpha" }, new string[0]);
        Autosave(session);
        SaveFromMenu(session, 5);

        var (_, named) = Load(5, new[] { "Alpha" }, new string[0]);
        Assert.Empty(named);
    }

    // ── Each pack on its own ────────────────────────────────────────────

    [Fact]
    public void EachPackIsToldOnItsOwn()
    {
        var (session, _) = Load(1, new[] { "Alpha", "Beta" }, new[] { "Beta" });
        Autosave(session);

        // Beta has gone since: that is news, and Alpha is not.
        (session, var named) = Load(1, new[] { "Alpha", "Beta" }, new string[0]);
        Assert.Equal(new[] { "Beta" }, named);
        Autosave(session);

        (_, named) = Load(1, new[] { "Alpha", "Beta" }, new string[0]);
        Assert.Empty(named);
    }

    // ── A pack that runs again ──────────────────────────────────────────

    [Fact]
    public void APackThatRunsAgain_IsToldAgainOnceThatIsCommitted()
    {
        var (session, _) = Load(1, new[] { "Alpha" }, new string[0]);
        Autosave(session);

        // Back on for a while, but nothing committed: the save still says it
        // told them, and taking it away again says nothing.
        Load(1, new[] { "Alpha" }, new[] { "Alpha" });
        var (_, named) = Load(1, new[] { "Alpha" }, new string[0]);
        Assert.Empty(named);

        // Back on, and slept with it: the autosave forgets, so taking it away
        // says it once more.
        (session, named) = Load(1, new[] { "Alpha" }, new[] { "Alpha" });
        Assert.Empty(named);
        Autosave(session);
        Assert.Empty(SaveRecord.Parse(SlotText(1))!.ToldMissing);

        (_, named) = Load(1, new[] { "Alpha" }, new string[0]);
        Assert.Equal(new[] { "Alpha" }, named);
    }

    [Fact]
    public void OnlyThePackThatRanIsForgotten()
    {
        var (session, _) = Load(1, new[] { "Alpha", "Beta" }, new string[0]);
        Autosave(session);
        Assert.Equal(new[] { "Alpha", "Beta" }, SaveRecord.Parse(SlotText(1))!.ToldMissing);

        (session, _) = Load(1, new[] { "Alpha", "Beta" }, new[] { "Beta" });
        Autosave(session);
        Assert.Equal(new[] { "Alpha" }, SaveRecord.Parse(SlotText(1))!.ToldMissing);
    }

    // ── The record ──────────────────────────────────────────────────────

    [Fact]
    public void TheListReadsBackAsItWasWritten_AndIsLeftOutWhenEmpty()
    {
        var record = new SaveRecord { ForgeVersion = "1.6.0" };
        record.ToldMissing.AddRange(new[] { "beta", "Alpha", "BETA" });
        Assert.Equal(new[] { "Alpha", "beta" }, SaveRecord.ToldIn(record.ToJson()));

        Assert.DoesNotContain(SaveRecord.ToldMissingField, new SaveRecord().ToJson());
        Assert.Empty(SaveRecord.ToldIn(""));
        Assert.Empty(SaveRecord.ToldIn("{ not json"));
    }

    [Fact]
    public void PackChangesAreStillSaid_WhenTheMissingPacksHaveAllBeenToldAbout()
    {
        var risky = new SaveLoadChecks.PackChanges("Beta", new[] { SaveLoadChecks.Quests });
        var notice = SaveLoadChecks.WarningFor(new[] { risky }, SaveLoadChecks.NotYetTold(
            new[] { new SaveLoadChecks.AbsentPack("Alpha", "", SaveRecord.NotInstalled) }, new[] { "Alpha" }));

        Assert.NotNull(notice);
        Assert.Contains(GameTexts.Quoted("Beta"), notice!.Plain());
        Assert.DoesNotContain(GameTexts.Quoted("Alpha"), notice.Plain());
    }

    // ── The game plugin takes the same steps ────────────────────────────

    private static string PluginSource(string name)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "SMSModForge.PackPlugin", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("the plugin's source is not beside the tests, so nothing here is checked", name);
    }

    [Fact]
    public void ThePluginWarnsOnlyAboutPacksNotToldYet_AndOnlyNotes_WritingNothing()
    {
        string text = PluginSource("SaveLoadWarning.cs");
        Assert.Contains("SaveLoadChecks.NotYetTold(absent, toldBefore)", text);
        Assert.Contains("absent = untold;", text);
        Assert.Contains("SaveCarry.NoteTold(", text);
        Assert.DoesNotContain("File.Write", text);
    }

    [Fact]
    public void OnlyTheAutosaveCommits_AndASaveFromTheMenuTransfersTheSourcesList()
    {
        Assert.Contains("SaveCarry.Complete(SavesRoot, 1, _contexts, Logger, SaveCarry.LiveTold);", PluginSource("Plugin.cs"));

        string manual = PluginSource("PackManualSaveSync.cs");
        Assert.Contains("SaveCarry.CommittedTold(Plugin.SavesRoot, source, Plugin.Log)", manual);
        Assert.DoesNotContain("LiveTold", manual);
    }
}
