using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Which packs need the journal's subtask counter switched on.
/// <para/>
/// The journal builds a counter under its subtask rows and then never enables
/// it: only the top-level prefab names that counter in
/// <c>m_ActiveElements.m_ActiveIfIsCounter</c>. So a counting subtask draws as
/// a plain line while its number ticks up unseen — which is exactly what it
/// looks like when a quest is broken, and is why it took a prefab dump rather
/// than a guess to tell the two apart.
/// <para/>
/// The runtime points that field at the counter already there. It does so only
/// for somebody who has a pack that needs it, because the prefab is a shared
/// asset and the change reaches the game's own quests for the session — so
/// this answer decides whether another person's game is altered. Too eager and
/// it changes a game nobody asked it to; too shy and the number somebody is
/// waiting for never appears. Both are silent, which is why the rule is here
/// rather than in the plugin.
/// </summary>
public sealed class QuestCounterTests
{
    private readonly ITestOutputHelper _out;
    public QuestCounterTests(ITestOutputHelper o) => _out = o;

    private bool Wanted(string json)
    {
        bool wanted = QuestCounters.OnASubtask(JObject.Parse(json));
        _out.WriteLine($"{wanted}  <-  {json}");
        return wanted;
    }

    [Fact]
    public void TheShapeThatFoundIt()
    {
        // Beachside Visitors, as the pack actually writes it: a top-level task
        // with a counting subtask under it. This is the case the whole thing
        // exists for, so it is written out rather than reduced.
        Assert.True(Wanted(@"{ ""quests"": [ {
            ""key"": ""BeachsideVisitorsAndroids2"",
            ""tasks"": [
              { ""key"": ""MeetTheScientistIn"", ""name"": ""Meet the scientist."" },
              { ""key"": ""KeepRelaxingAtThe"", ""name"": ""Keep relaxing."",
                ""subtasks"": [ { ""key"": ""ParanormalPhenomena"", ""name"": ""Paranormal phenomena"", ""countTo"": 1.0 } ] }
            ] } ] }"));
    }

    [Fact]
    public void ACountOnATopLevelTaskIsAlreadyDrawn()
    {
        // The control, and the one that matters most: the game draws this one
        // itself. Answering true here would reach into every player's game to
        // fix something that was never broken.
        Assert.False(Wanted(@"{ ""quests"": [ { ""key"": ""q"", ""tasks"": [
            { ""key"": ""collect"", ""countTo"": 3 } ] } ] }"));
    }

    [Fact]
    public void ASubtaskThatDoesNotCountAsksForNothing()
    {
        Assert.False(Wanted(@"{ ""quests"": [ { ""key"": ""q"", ""tasks"": [
            { ""key"": ""top"", ""subtasks"": [ { ""key"": ""under"" } ] } ] } ] }"));
    }

    [Fact]
    public void ACounterTurnedBackOffIsNotCounting()
    {
        // countTo stays on a task whose counter was switched off, at zero. A
        // zero that read as counting would turn this on for most packs.
        Assert.False(Wanted(@"{ ""quests"": [ { ""key"": ""q"", ""tasks"": [
            { ""key"": ""top"", ""subtasks"": [ { ""key"": ""under"", ""countTo"": 0 } ] } ] } ] }"));
    }

    [Fact]
    public void ItReachesAllTheWayDown()
    {
        Assert.True(Wanted(@"{ ""quests"": [ { ""key"": ""q"", ""tasks"": [
            { ""key"": ""a"", ""subtasks"": [
              { ""key"": ""b"", ""subtasks"": [
                { ""key"": ""c"", ""countTo"": 2 } ] } ] } ] } ] }"));
    }

    [Fact]
    public void ATaskAddedUnderOneOfTheGamesIsASubtaskToo()
    {
        // The other shape, and it is not nested: the editor writes added tasks
        // flat and says where each goes with "under". Reading only the nesting
        // would miss every pack that extends one of the game's quests.
        Assert.True(Wanted(@"{ ""quests"": [ { ""key"": ""e"", ""source"": ""Astrid Quest"",
            ""addedTasks"": [ { ""key"": ""count"", ""under"": ""FindHer"", ""countTo"": 4 } ] } ] }"));

        // ...and one added at the top level of that quest is not.
        Assert.False(Wanted(@"{ ""quests"": [ { ""key"": ""e"", ""source"": ""Astrid Quest"",
            ""addedTasks"": [ { ""key"": ""count"", ""countTo"": 4 } ] } ] }"));
    }

    [Theory]
    [InlineData(@"{}")]
    [InlineData(@"{ ""quests"": [] }")]
    [InlineData(@"{ ""quests"": [ {} ] }")]
    [InlineData(@"{ ""quests"": [ { ""tasks"": [] } ] }")]
    [InlineData(@"{ ""quests"": ""not a list"" }")]
    [InlineData(@"{ ""quests"": [ { ""tasks"": [ { ""countTo"": ""three"" } ] } ] }")]
    public void APackWithNothingOfTheSortIsLeftAlone(string json)
    {
        // Every pack written before this existed goes through here, so the
        // shapes that must answer false include the malformed ones.
        Assert.False(Wanted(json));
    }

    [Fact]
    public void NoManifestAtAllIsNotACrash()
    {
        Assert.False(QuestCounters.OnASubtask(null!));
    }
}
