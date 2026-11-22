using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What keeps undo and redo working for a feature added later, with nothing
/// done to undo itself (the author, 1.10.0: "we need to also make sure we don't
/// have to do additional fixes to the undo/redo system for new future UI
/// features").
/// <para/>
/// Undo needs nothing per feature by construction: a step is the whole pack,
/// compared piece by piece; steps are cut by the window's own mouse and keys;
/// a step is put back by reading what differs, and the quicker ways hold
/// themselves to the step and give way when they are not sure. What could
/// still go wrong is a feature added where none of that looks - so each of
/// these fails, saying what to do, the moment one is:
/// <list type="bullet">
/// <item>a list of records added to the pack that the walk over every control
/// has no record of (the walk finds controls by itself; it cannot show a page
/// for a record that is not there);</item>
/// <item>a tab the walk does not go to;</item>
/// <item>something picked from a list that an undo could not pick again;</item>
/// <item>a row that keeps rows of its own (what a click does, components) and
/// does not make them again when it is given other values where it is;</item>
/// <item>something a record keeps beside what is saved, which a step read
/// back does not have.</item>
/// </list>
/// </summary>
public sealed class UndoNeedsNothingNewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-undonew-" + Guid.NewGuid().ToString("N"));

    public UndoNeedsNothingNewTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static bool IsRecord(Type t) => t.IsClass && t != typeof(string) && t.Namespace == typeof(ModPack).Namespace;

    private static Type? ElementOf(Type list)
        => list.IsGenericType && list.GetGenericArguments() is { Length: 1 } args && typeof(IList).IsAssignableFrom(list) ? args[0] : null;

    // ── The walk has a record of every kind ─────────────────────────────

    /// <summary>Lists of the pack the walk needs no record of, each with why.</summary>
    private static readonly Dictionary<string, string> NoRecordNeeded = new(StringComparer.Ordinal)
    {
        // name of the ModPack property => why nothing of it is on a page
        ["Actors"] = "no page shows them any more: kept for the packs that have them, and for what refers to them",
        ["LegacyVanillaUiExtensions"] = "never kept: folded into Uis as a pack is opened (ModPack.MigrateUi), and not written",
        ["VanillaGates"] = "made by the buttons on a game's quest and a game's conversation that take one of the game's conditions off, which the walk presses",
    };

    [Fact]
    public void TheWalkOverEveryControlHasARecordOfEveryKindThePackKeeps()
    {
        var pack = UndoCrawlPack.Make(_dir);
        var empty = new List<string>();
        foreach (var p in typeof(ModPack).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
            if (ElementOf(p.PropertyType) is not { } element || !IsRecord(element)) continue;
            int count = (p.GetValue(pack) as IList)?.Count ?? 0;
            _out.WriteLine($"{p.Name}: {count}");
            if (count == 0 && !NoRecordNeeded.ContainsKey(p.Name)) empty.Add(p.Name);
        }
        Assert.True(empty.Count == 0,
            "The pack keeps a list of records the walk over every control (UndoEveryControlTests) has none of: " + string.Join(", ", empty)
            + ". The walk finds every control of a page by itself, but there is no page to walk for a record that is not there. "
            + "Give one of the packs UndoCrawlPack is made of (InGameSuitePacks) a record of that kind - or, if nothing of it is ever on a page, say why in NoRecordNeeded.");
    }

    // ── The walk goes to every tab ──────────────────────────────────────

    [Fact]
    public void TheWalkOverEveryControlGoesToEveryTab()
    {
        string walk = File.ReadAllText(Path.Combine(Here(), "UndoEveryControlTests.cs"));
        var walked = new HashSet<string>(Regex.Matches(walk, @"MainTab\.(\w+)").Select(m => m.Groups[1].Value), StringComparer.Ordinal);
        var tabs = typeof(MainTab).GetFields(BindingFlags.Public | BindingFlags.Static)
                                  .Where(f => f.IsLiteral && f.FieldType == typeof(int)).Select(f => f.Name).ToList();
        Assert.True(tabs.Count >= 10, "control: the tabs were not found - this proves nothing");
        var missed = tabs.Where(t => !walked.Contains(t)).ToList();
        _out.WriteLine("walked: " + string.Join(", ", tabs.Where(walked.Contains)));
        Assert.True(missed.Count == 0,
            "A tab the walk over every control never goes to: " + string.Join(", ", missed)
            + ". Add it to UndoEveryControlTests (a crawl.Explore of the tab, with each kind of record it shows picked): "
            + "every control on it is then edited, undone and redone without anything else being written.");
    }

    // ── Whatever is picked from a list can be picked again ──────────────

    [Fact]
    public void WhateverIsPickedFromAListHasAListAnUndoCanFindItIn()
    {
        // Picked from a list that is another pick's, which the editor names itself (MainViewModel.Picks).
        var named = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MainViewModel.SelectedOutfit"] = "Picks.Outfit: found among the picked character's outfits",
            ["MainViewModel.SelectedNode"] = "Picks.Line: found among the picked dialogue's lines",
        };
        var lost = new List<string>();
        var seen = new HashSet<Type>();
        void Look(Type kind, int deeper)
        {
            if (!seen.Add(kind)) return;
            foreach (var (selected, lists) in MainViewModel.SelectablesOf(kind))
            {
                _out.WriteLine($"{kind.Name}.{selected.Name}: {string.Join(", ", lists.Select(l => l.Name))}");
                if (lists.Length == 0 && !named.ContainsKey(kind.Name + "." + selected.Name)) lost.Add(kind.Name + "." + selected.Name);
                if (deeper > 0) Look(selected.PropertyType, deeper - 1);
            }
        }
        Look(typeof(MainViewModel), 2);
        Assert.True(seen.Count > 3, "control: nothing picked was found - this proves nothing");
        Assert.True(lost.Count == 0,
            "Something that can be picked has no list beside it to be found in again: " + string.Join(", ", lost)
            + ". An undo goes back to what was picked when its step was made, by finding the record in the list it was picked from "
            + "(MainViewModel.OtherPicksOf). Keep the records in a list of the same owner, of the same kind as the pick.");
    }

    // ── A row given other values where it is makes its own rows again ───

    /// <summary>The rows that are given a step's values where they are, each with how a row of it is made over a
    /// record and how it is told to read the record again.</summary>
    private static IEnumerable<(Type Record, Func<object, object> Row, Action<object> ReadAgain)> RowsPutBackInPlace()
    {
        yield return (typeof(UiNodeDef), def => new UiNodeViewModel((UiNodeDef)def), row => ((UiNodeViewModel)row).RefreshAll());
        yield return (typeof(GameObjectDef), def => new GameObjectViewModel((GameObjectDef)def), row => ((GameObjectViewModel)row).ReadAgain());
        yield return (typeof(DialogueNodeDef), def => new DialogueNodeViewModel((DialogueNodeDef)def), row => ((DialogueNodeViewModel)row).RefreshAll());
    }

    /// <summary>A record with one entry in every list of records it keeps.</summary>
    private static object Filled(Type record)
    {
        object made = Activator.CreateInstance(record)!;
        foreach (var p in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
            if (ElementOf(p.PropertyType) is not { } element || !IsRecord(element) || element == record) continue;
            if (p.GetValue(made) is IList list && element.GetConstructor(Type.EmptyTypes) != null) list.Add(Activator.CreateInstance(element)!);
        }
        return made;
    }

    private static IEnumerable<(PropertyInfo Property, IList Rows)> RowLists(object row)
    {
        foreach (var p in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.PropertyType.IsGenericType || p.PropertyType.GetGenericTypeDefinition() != typeof(ObservableCollection<>)) continue;
            if (p.PropertyType.GetGenericArguments()[0].GetProperty("Model") == null) continue;
            if (p.GetValue(row) is IList rows) yield return (p, rows);
        }
    }

    private static object ModelOf(object row) => row.GetType().GetProperty("Model")!.GetValue(row)!;

    [Fact]
    public void ARowGivenOtherValuesWhereItIsShowsTheRecordsOwnListsAgain()
    {
        WindowHarness.Run(_ =>
        {
            foreach (var (record, rowFor, readAgain) in RowsPutBackInPlace())
            {
                object live = Filled(record);
                object row = rowFor(live);
                var lists = record.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && ElementOf(p.PropertyType) is { } e && IsRecord(e) && e != record && p.GetValue(live) is IList { Count: > 0 })
                    .ToList();
                // Which of the row's own rows show which of the record's lists: by what they hold.
                var shows = new List<(PropertyInfo Rows, PropertyInfo List)>();
                foreach (var (property, rows) in RowLists(row))
                {
                    if (rows.Count == 0) continue;
                    var list = lists.FirstOrDefault(l => ((IList)l.GetValue(live)!).Cast<object>().SequenceEqual(rows.Cast<object>().Select(ModelOf), ReferenceEqualityComparer.Instance));
                    if (list != null) shows.Add((property, list));
                }
                _out.WriteLine($"{record.Name}: " + string.Join(", ", shows.Select(s => s.Rows.Name + " shows " + s.List.Name)));
                Assert.True(shows.Count > 0, $"control: a row of {record.Name} shows none of its lists - this proves nothing");

                // Given another record's values where it is, as an undo gives them - and told to read again.
                object other = Filled(record);
                Assert.True(ModelAssign.Into(live, other));
                readAgain(row);
                foreach (var (rows, list) in shows)
                {
                    var now = ((IList)rows.GetValue(row)!).Cast<object>().Select(ModelOf).ToList();
                    var wanted = ((IList)list.GetValue(live)!).Cast<object>().ToList();
                    Assert.True(wanted.SequenceEqual(now, ReferenceEqualityComparer.Instance),
                        $"A row of {record.Name} still shows the old {list.Name} after it was given other values where it is: its {rows.Name} are not the record's. "
                        + "An undo puts such a record back without making its row again (see GameObjectRows, UiViewModel.ReadNodesAgain, DialogueViewModel.ReadLinesAgain): "
                        + "the row's own read-again must make the rows of every list again, as its constructor makes them.");
                }
            }
        });
    }

    // ── What a record keeps beside what is saved ────────────────────────

    /// <summary>
    /// What records keep that is not saved, each with how it is there again on a record read back from an undo
    /// step. A step is the pack as text: what is not written is not in it.
    /// </summary>
    private static readonly Dictionary<string, string> KeptBesideWhatIsSaved = new(StringComparer.Ordinal)
    {
        ["GameObjectDef.SpritePpu"] = "from the game's catalog, by VanillaDelta.Rebase when the level is filled in",
        ["GameObjectDef.SpritePivotX"] = "from the game's catalog, by VanillaDelta.Rebase when the level is filled in",
        ["GameObjectDef.SpritePivotY"] = "from the game's catalog, by VanillaDelta.Rebase when the level is filled in",
        ["GameObjectDef.VanillaArtPath"] = "from the game's catalog, by VanillaDelta.Rebase when the level is filled in",
        ["GameObjectDef.Baseline"] = "found again by GameObjectDef.BaselineName, which a step does keep",
        ["LightDef.GameLayers"] = "kept in a step as LightDef.GameLayersKept",
        ["UiNodeDef.HiddenInPreview"] = "the editor's own memory of what is hidden (Services.UiPreviewHiding), put on the rows as they are made",
        ["UiNodeDef.GameTint"] = "from the game's screen, by VanillaUiSeed.GameLookOnto when the screen is read back",
        ["UiTextDef.GameStyle"] = "from the game's screen, by VanillaUiSeed.GameLookOnto when the screen is read back",
        ["CharacterDef.Pronouns"] = "saved, under another member: CharacterDef.PronounsText writes it as \"pronouns\"",
        ["ModPack.PackVersion"] = "saved, under another member: it is ModPack.Version read as a number",
    };

    [Fact]
    public void WhatARecordKeepsBesideWhatIsSavedIsKnownToComeBackAfterAnUndo()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<Type>();
        void Look(Type t)
        {
            if (!IsRecord(t) || !seen.Add(t)) return;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                // Something the record can be given and that is never written: kept beside what is saved.
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                {
                    if (p.SetMethod is { IsPublic: true }) found.Add(t.Name + "." + p.Name);
                    continue;
                }
                var of = p.PropertyType;
                Look(of);
                if (of.IsGenericType) foreach (var arg in of.GetGenericArguments()) Look(arg);
                if (Nullable.GetUnderlyingType(of) is { } inner) Look(inner);
            }
        }
        Look(typeof(ModPack));
        Assert.True(seen.Count > 20, "control: the pack's records were not found - this proves nothing");
        _out.WriteLine(string.Join("\n", found));
        var unknown = found.Where(f => !KeptBesideWhatIsSaved.ContainsKey(f)).ToList();
        Assert.True(unknown.Count == 0,
            "A record keeps something that is never saved, and nothing says how it comes back: " + string.Join(", ", unknown)
            + ". An undo step is the pack as text, so a record read back from one does not have it - whatever shows it is wrong after an undo "
            + "(the game's label style was lost that way in the UI preview). Either keep it in the editor's own copies as LightDef.GameLayersKept does "
            + "(a property written only outside GameObjectDef.SaveScope), or give it back where the record is read back, and say which here.");
        var gone = KeptBesideWhatIsSaved.Keys.Where(k => !found.Contains(k)).ToList();
        Assert.True(gone.Count == 0, "No longer kept beside what is saved - take it off the list: " + string.Join(", ", gone));
    }
}
