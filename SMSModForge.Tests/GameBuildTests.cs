using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// ModForge's own game build: what the editor stamps into packs and what the
/// plugin checks the running game against - one constant, so the two can never
/// disagree about which build ModForge is made for.
/// </summary>
public sealed class GameBuildTests
{
    [Fact]
    public void TheEditorStampsTheBuildThePluginChecks()
        => Assert.Equal(ForgeVersion.GameBuild, ModPack.CurrentGameVersion);

    [Theory]
    [InlineData("1.8E", false)]
    [InlineData("1.8e", false)]
    [InlineData("1.8F", true)]
    [InlineData("1.7A", true)]
    [InlineData("", false)]      // could not be read off the menu: not evidence
    [InlineData(null, false)]
    public void ModForgeSaysSoWhenTheGameIsAnotherBuild(string? running, bool other)
        => Assert.Equal(other, ForgeVersion.MadeForOtherGame(running!));
}
