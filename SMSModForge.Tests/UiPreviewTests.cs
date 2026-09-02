using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The UI tab's preview control: what it puts on screen, and what it says when
/// it cannot.
/// <para/>
/// A WPF control needs an STA thread, so each case runs on one of its own. No
/// window and no dispatcher loop — this control does its work in
/// <see cref="UiPreview.Refresh"/> rather than in a layout pass, which is what
/// makes it testable at all.
/// </summary>
public class UiPreviewTests
{
    private readonly ITestOutputHelper _out;
    public UiPreviewTests(ITestOutputHelper o) => _out = o;

    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { body(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception(failure.Message, failure);
    }

    private static BitmapSource? Drawn(UiPreview preview)
        => ((Image)preview.Children[0]).Source as BitmapSource;

    private static string Said(UiPreview preview)
    {
        var text = (TextBlock)preview.Children[1];
        return text.Visibility == System.Windows.Visibility.Visible ? text.Text : "";
    }

    [Fact]
    public void A_vanilla_base_is_drawn_at_the_canvas_size()
    {
        OnSta(() =>
        {
            if (!VanillaUiLibrary.IsAvailable)
            {
                _out.WriteLine("no extraction beside the tests - skipping");
                return;
            }

            var preview = new UiPreview { BaseToken = "vanillaui:9_MainCanvas/Quitagme" };
            var image = Drawn(preview);

            Assert.NotNull(image);
            Assert.Equal(1920, image!.PixelWidth);
            Assert.Equal(1080, image.PixelHeight);
            Assert.True(image.IsFrozen, "a frozen bitmap can cross threads");
            Assert.Equal("", Said(preview));

            Assert.NotNull(preview.Report);
            Assert.Empty(preview.Report!.MissingSprites);
            Assert.Empty(preview.Report.MissingFonts);
            _out.WriteLine($"drew {preview.Report.Drawn} graphics; trouble: " +
                           $"'{preview.Trouble()}'");
        });
    }

    [Fact]
    public void Choosing_nothing_says_so_rather_than_showing_a_blank()
    {
        OnSta(() =>
        {
            var preview = new UiPreview();
            preview.Refresh();
            Assert.Null(Drawn(preview));
            Assert.NotEqual("", Said(preview));
            _out.WriteLine(Said(preview));
        });
    }

    [Fact]
    public void A_name_the_catalog_does_not_have_is_explained()
    {
        OnSta(() =>
        {
            if (!VanillaUiLibrary.IsAvailable) return;
            var preview = new UiPreview { BaseToken = "vanillaui:9_MainCanvas/NotAThing" };
            Assert.Null(Drawn(preview));
            Assert.Contains("NotAThing", Said(preview));
        });
    }

    [Fact]
    public void A_canvas_the_game_never_sized_explains_itself_instead_of_drawing_nothing()
    {
        OnSta(() =>
        {
            if (!VanillaUiLibrary.IsAvailable) return;

            // One of the six whose Canvas component is disabled. An author who
            // picked it deserves the reason rather than an empty rectangle.
            var entry = System.Linq.Enumerable.FirstOrDefault(
                VanillaUiCatalog.AllBases, b => !b.Surface.Usable);
            if (entry == null) { _out.WriteLine("no unusable surface - skipping"); return; }

            var preview = new UiPreview { BaseToken = entry.Token };
            Assert.Null(Drawn(preview));
            Assert.Contains("Canvas component is disabled", Said(preview));
            _out.WriteLine(entry.Token + " -> " + Said(preview));
        });
    }

    [Fact]
    public void The_whole_surface_draws_more_than_one_base_does()
    {
        OnSta(() =>
        {
            if (!VanillaUiLibrary.IsAvailable) return;

            var one = new UiPreview { BaseToken = "vanillaui:9_MainCanvas/Quitagme" };
            var all = new UiPreview
            {
                BaseToken = "vanillaui:9_MainCanvas/Quitagme",
                ShowWholeSurface = true,
            };

            Assert.NotNull(one.Report);
            Assert.NotNull(all.Report);
            Assert.True(all.Report!.Drawn > one.Report!.Drawn,
                        $"whole surface drew {all.Report.Drawn}, one base drew {one.Report.Drawn}");
            _out.WriteLine($"one base {one.Report.Drawn}, whole surface {all.Report.Drawn}");
        });
    }

    [Fact]
    public void Trouble_is_empty_when_there_was_none_and_names_it_when_there_was()
    {
        OnSta(() =>
        {
            var fresh = new UiPreview();
            Assert.Equal("", fresh.Trouble());

            if (!VanillaUiLibrary.IsAvailable) return;

            // Quitagme has one legacy UI.Text object, which is a thing this
            // cannot draw and says so - not a failure, a different mechanism.
            var preview = new UiPreview { BaseToken = "vanillaui:9_MainCanvas/Quitagme" };
            _out.WriteLine("trouble: " + preview.Trouble());
            Assert.Contains("legacy", preview.Trouble());
        });
    }
}
