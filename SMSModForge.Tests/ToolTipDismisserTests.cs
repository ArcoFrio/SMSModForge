using System.Windows;
using System.Windows.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Getting a stuck tooltip out of the way of a preview.
/// <para/>
/// A tooltip lives in a window of its own, above everything, and WPF closes it
/// when the pointer leaves the control that raised it. Move fast enough and
/// that does not happen — leaving a yellow box over the art an author is
/// trying to judge.
/// <para/>
/// Worth testing rather than eyeballing, because "no tooltip appeared" is what
/// a dismisser that does nothing at all looks like. Each of these opens a real
/// tooltip first, so the close has something to prove.
/// </summary>
public sealed class ToolTipDismisserTests
{
    private readonly ITestOutputHelper _out;
    public ToolTipDismisserTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void AnOpenToolTipIsClosed()
    {
        WindowHarness.Run(_ =>
        {
            var tip = new ToolTip { Content = "in the way", IsOpen = true };
            WindowHarness.Pump();

            Assert.True(tip.IsOpen, "the tooltip did not open, so closing it proves nothing");

            SMSModForge.View.ToolTipDismisser.CloseAll();
            WindowHarness.Pump();

            _out.WriteLine($"after CloseAll: IsOpen={tip.IsOpen}");
            Assert.False(tip.IsOpen);
        });
    }

    [Fact]
    public void SeveralAtOnceAllGo()
    {
        WindowHarness.Run(_ =>
        {
            var tips = new[]
            {
                new ToolTip { Content = "one", IsOpen = true },
                new ToolTip { Content = "two", IsOpen = true },
                new ToolTip { Content = "three", IsOpen = true },
            };
            WindowHarness.Pump();
            Assert.All(tips, t => Assert.True(t.IsOpen));

            SMSModForge.View.ToolTipDismisser.CloseAll();
            WindowHarness.Pump();

            Assert.All(tips, t => Assert.False(t.IsOpen));
        });
    }

    [Fact]
    public void APreviewOffersNoToolTipOfItsOwn()
    {
        // The other half: closing what is up is no good if the preview then
        // raises one of its own over the same spot.
        WindowHarness.Run(_ =>
        {
            var preview = new SMSModForge.View.Controls.ScenePreview();
            WindowHarness.Pump();

            Assert.False(ToolTipService.GetIsEnabled(preview));
        });
    }

    [Fact]
    public void ClosingWhenNothingIsOpenIsHarmless()
    {
        // It runs on every mouse move over a preview, so the common case is
        // "nothing to do" and it must be cheap and quiet.
        WindowHarness.Run(_ =>
        {
            SMSModForge.View.ToolTipDismisser.CloseAll();
            SMSModForge.View.ToolTipDismisser.CloseAll();
            SMSModForge.View.ToolTipDismisser.KeepClearOf(null!);
        });
    }
}
