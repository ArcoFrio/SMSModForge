using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// An action's or condition's row holds every kind's boxes, collapsed, and
/// two sets of them edit the same params. A collapsed box that wrote back -
/// an empty Level, because its own list did not hold the chosen one - wiped
/// what the author had set. Found by the window tests run under an EX theme
/// (1.8.0); held here without a window, so it does not depend on timing.
/// </summary>
public class HiddenRowsTests
{
    private static NodeActionViewModel SetActive()
    {
        var row = new NodeActionViewModel(new NodeActionDef { Type = NodeActionTypes.SetGameObjectActive }, _ => { });
        row.Category = NodeActionViewModel.CatOverlay;
        row.OverlayLevel = "place:home";
        row.Target = "Lamp";
        return row;
    }

    [Fact]
    public void TheFadeRowsBoxesCannotChangeASetActiveAction()
    {
        var row = SetActive();
        row.GoOverlayLevel = "";                 // what the collapsed box wrote
        row.GoCategory = NodeActionViewModel.CatPath;
        row.GoTarget = "";
        Assert.Equal("place:home", row.OverlayLevel);
        Assert.Equal(NodeActionViewModel.CatOverlay, row.Category);
        Assert.Equal("Lamp", row.Target);
    }

    [Fact]
    public void TheSetActiveRowsBoxesCannotChangeAFade()
    {
        var fade = new NodeActionViewModel(new NodeActionDef { Type = NodeActionTypes.FadeSprite }, _ => { });
        fade.GoCategory = NodeActionViewModel.CatOverlay;
        fade.GoOverlayLevel = "place:home";
        fade.GoTarget = "Lamp";
        fade.OverlayLevel = "";
        fade.Category = NodeActionViewModel.CatPath;
        fade.Target = "";
        Assert.Equal("place:home", fade.GoOverlayLevel);
        Assert.Equal(NodeActionViewModel.CatOverlay, fade.GoCategory);
        Assert.Equal("Lamp", fade.GoTarget);
        Assert.Equal(NodeActionTypes.FadeSprite, fade.Model.Type);   // the Category box used to turn it into a Set-Active
    }

    [Fact]
    public void ARowTheActionShowsStillWrites()
    {
        // The control: the guards only stop the rows that are not showing.
        var row = SetActive();
        row.OverlayLevel = "place:yard";
        Assert.Equal("place:yard", row.OverlayLevel);
        row.OverlayLevel = "";
        Assert.Equal("", row.OverlayLevel);
    }

    [Fact]
    public void AConditionsObjectBoxesWriteOnlyForTheObjectCondition()
    {
        var other = new NodeConditionViewModel(new NodeConditionDef { Type = NodeConditionTypes.LevelActive, Params = { ["level"] = "place:home" } }, _ => { });
        other.GoTarget = "Lamp";
        other.GoOverlayLevel = "place:yard";
        Assert.DoesNotContain("target", other.Model.Params.Keys);
        Assert.DoesNotContain("overlayLevel", other.Model.Params.Keys);

        var go = new NodeConditionViewModel(new NodeConditionDef { Type = NodeConditionTypes.GameObjectActive }, _ => { });
        go.GoCategory = NodeActionViewModel.CatOverlay;
        go.GoOverlayLevel = "place:yard";
        Assert.Equal("place:yard", go.GoOverlayLevel);
    }
}
