using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The ids a pack quest and its tasks are saved under in the game.
/// <para/>
/// The game's journal saves quest state by GUID and task state by numeric id,
/// and a pack quest is rebuilt every session - so these have to come out the
/// same every time, or every player's progress is orphaned on the next load
/// without a single error anywhere. That makes the literal values below the
/// point of this file, not an implementation detail: a refactor that changes
/// one of them has broken every saved game, and this is where it shows.
/// </summary>
public sealed class QuestIdsTests
{
    private readonly ITestOutputHelper _out;
    public QuestIdsTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void AQuestsGuidNeverChanges()
    {
        // Pinned from the implementation as first shipped. If this fails,
        // either put the old derivation back or treat it as a migration of
        // every save file that has a pack quest in it.
        Assert.Equal("a8aafa60-5357-5f10-95fc-caa2efd0b938",
                     QuestIds.QuestGuid("examplepack", "secrets"));
    }

    [Fact]
    public void ATasksIdNeverChanges()
    {
        Assert.Equal(-914475020, QuestIds.TaskId("examplepack", "secrets", "find-out"));
        Assert.Equal(-2061628904, QuestIds.TaskId("examplepack", "secrets", "register"));
    }

    [Fact]
    public void TheSameKeysGiveTheSameIdsEveryTime()
    {
        // The property the pinned values stand for, asked directly.
        Assert.Equal(QuestIds.QuestGuid("p", "q"), QuestIds.QuestGuid("p", "q"));
        Assert.Equal(QuestIds.TaskId("p", "q", "t"), QuestIds.TaskId("p", "q", "t"));
    }

    [Fact]
    public void AQuestGuidIsAGuidTheGameCanRead()
    {
        // The game parses it back from its save file. A version-5, RFC 4122
        // GUID in lowercase is the ordinary form, and the form its own quests
        // use.
        string guid = QuestIds.QuestGuid("examplepack", "secrets");
        _out.WriteLine(guid);

        Assert.True(Guid.TryParse(guid, out _), "not a GUID at all");
        Assert.Equal(guid.ToLowerInvariant(), guid);
        Assert.Equal('5', guid[14]);                                    // version
        Assert.Contains(guid[19], "89ab");                              // variant
    }

    [Fact]
    public void ADifferentPackOrQuestIsADifferentQuest()
    {
        // The control for the constancy above: a derivation that returned one
        // fixed GUID would pass every test before this one.
        string a = QuestIds.QuestGuid("examplepack", "secrets");
        Assert.NotEqual(a, QuestIds.QuestGuid("examplepack", "other"));
        Assert.NotEqual(a, QuestIds.QuestGuid("otherpack", "secrets"));
    }

    [Fact]
    public void TheSameTaskKeyInTwoQuestsIsTwoTasks()
    {
        // The game looks tasks up by id across every quest it has, so a task
        // called "intro" in two quests must not be the same task to it.
        Assert.NotEqual(QuestIds.TaskId("p", "first", "intro"),
                        QuestIds.TaskId("p", "second", "intro"));
        Assert.NotEqual(QuestIds.TaskId("p", "q", "intro"),
                        QuestIds.TaskId("other", "q", "intro"));
    }

    [Fact]
    public void ATaskIdIsNeverOneTheGameReservesOrOneThatLooksUnset()
    {
        // -1 is what the game's trees use for "no node"; 0 is what an id that
        // was never set reads as. A few thousand keys is enough to be sure the
        // derivation steps round both rather than getting lucky.
        var ids = new HashSet<int>();
        for (int i = 0; i < 5000; i++)
        {
            int id = QuestIds.TaskId("pack", "quest", "task" + i);
            Assert.NotEqual(QuestIds.NoNode, id);
            Assert.NotEqual(0, id);
            ids.Add(id);
        }

        _out.WriteLine($"{ids.Count} distinct ids from 5000 keys");
        Assert.Equal(5000, ids.Count);
    }

    [Fact]
    public void NothingIsLeftToTheMachine()
    {
        // No clock, no random number, no byte order: the ids do not depend on
        // when or where they are computed. Different keys spread across both
        // signs, which a byte-order mistake would tend to collapse.
        var ids = Enumerable.Range(0, 200).Select(i => QuestIds.TaskId("p", "q", "t" + i)).ToList();
        Assert.Contains(ids, id => id < 0);
        Assert.Contains(ids, id => id > 0);
    }
}
