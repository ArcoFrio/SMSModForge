using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The ModForge tab's one search (1.10.0, from a pack author's feedback on finding things): what is typed in the
/// box at the top of the column narrows the tutorials and the guide, opens both, and lists the tutorial steps that
/// mention it, each with a button that starts its tutorial right there. Held in the real window, by what is drawn:
/// a word only one tutorial uses (the story's track, "Neon Rain") finds its step and starts it there; a word no
/// tutorial has (the control) finds none, says so, and lists no step.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class HomeSearchTests
{
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in All<T>(c)) yield return d;
        }
    }

    private static bool Drawn(FrameworkElement e) => e.IsVisible && e.ActualWidth > 0 && e.ActualHeight > 0;

    private static void Settle()
    {
        WindowHarness.Wait(TimeSpan.FromMilliseconds(400));   // the box hands its text on after 150 ms
        WindowHarness.Pump();
    }

    [Fact]
    public void A_word_finds_the_step_that_uses_it_and_starts_there_and_a_word_nobody_uses_finds_none()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.ModForge;
            var box = (TextBox)window.FindName("HomeSearchBox");
            Settle();
            // The tab opens on Home; the search's page is not drawn.
            Assert.True(Drawn((FrameworkElement)window.FindName("HomePageHome")), "the tab did not open on Home");
            Assert.False(((FrameworkElement)window.FindName("HomePageSearch")).IsVisible);

            // The control: a word no tutorial has.
            box.Text = "qqzzxx";
            Settle();
            var said = All<TextBlock>(window).Where(Drawn).Select(t => t.Text).ToList();
            Assert.Contains(Localization.Loc.T("tutorials.search.none"), said);
            Assert.DoesNotContain(All<Button>(window), b => Drawn(b) && Equals(b.Content, Localization.Loc.T("tutorials.search.go")));

            // The story's track: only the sound tutorial says it.
            box.Text = "Neon Rain";
            Settle();
            Assert.True(Drawn((FrameworkElement)window.FindName("HomePageSearch")), "the search's results were not drawn");
            Assert.False(((FrameworkElement)window.FindName("HomePageHome")).IsVisible, "Home was drawn under the results");
            var go = All<Button>(window).Where(b => Drawn(b) && Equals(b.Content, Localization.Loc.T("tutorials.search.go"))).ToList();
            Assert.NotEmpty(go);
            var hit = (MainViewModel.TutorialStepHit)go[0].DataContext;
            Assert.Equal("sound-in-a-room", hit.Def.Id);
            Assert.DoesNotContain(All<TextBlock>(window).Where(Drawn).Select(t => t.Text), t => t == Localization.Loc.T("tutorials.search.none"));

            go[0].Command.Execute(go[0].CommandParameter);
            Settle();
            Assert.True(vm.TutorialRunner.IsRunning);
            Assert.Equal("sound-in-a-room", vm.TutorialRunner.Tutorial!.Id);
            Assert.Equal(hit.Index, vm.TutorialRunner.Index);
            Assert.True(hit.Index > 0, "the step found was the first one, which proves nothing about starting there");
            vm.TutorialRunner.Stop();
        });
    }

    /// <summary>
    /// A guide page shows "More" under a bullet only when there is more to read, and what it opens is words, never a
    /// key (the author, 2026-11-21: every bullet had a More that opened on "docs.modPack....more"). Counted on the
    /// page as drawn, topic by topic, against the English file.
    /// </summary>
    [Fact]
    public void TheGuideOffersMoreOnlyWhereThereIsMore_AndItOpensOnWords()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.ModForge;
            vm.HomePage = HomePage.Guide;
            int topicsWithMore = 0, topicsWithout = 0;
            foreach (var entry in vm.GuideEntries)
            {
                vm.SelectedGuideEntry = entry;
                Settle();
                var reader = (FrameworkElement)window.FindName("GuideReader");
                var shown = All<Expander>(reader).Where(Drawn).ToList();
                int expected = entry.Topic.Sections.SelectMany(s => s.Bullets)
                                    .Count(b => SMSModForge.Localization.Loc.Current.Has(b.MoreKey));
                Assert.True(expected == shown.Count,
                            $"{entry.Title}: {shown.Count} More drawn, {expected} bullets have more to read");
                foreach (var more in shown)
                {
                    var bullet = (SMSModForge.Documentation.DocBullet)more.DataContext;
                    Assert.False(bullet.More.StartsWith("docs.", StringComparison.Ordinal), $"{entry.Title}: More opens on its key");
                    Assert.NotEqual("", bullet.More);
                }
                if (expected > 0) topicsWithMore++; else topicsWithout++;
            }
            // Both kinds were seen: a page with fold-outs and a page without any.
            Assert.True(topicsWithMore > 0 && topicsWithout > 0, $"{topicsWithMore} topics with More, {topicsWithout} without");
        });
    }

    /// <summary>Pictures of the ModForge tab, searching and not, to look at in each look: nothing is asserted.
    /// Only when SMSMODFORGE_SHOT_DIR names a folder; the theme is SMSMODFORGE_TEST_THEME's.</summary>
    [Fact]
    public void TakePictures()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        WindowHarness.Run(window =>
        {
            window.Width = 1400; window.Height = 900;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.ModForge;
            Settle();
            void Save(string name)
            {
                window.UpdateLayout();
                WindowHarness.Pump();
                var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(root);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"home-{name}-{theme}.png"));
                png.Save(f);
            }
            var vm = (MainViewModel)window.DataContext;
            Save("home");
            ((TextBox)window.FindName("HomeSearchBox")).Text = "light";
            Settle();
            Save("search");
            // A word only a guide bullet's fold-out has: the fold-out opens.
            ((TextBox)window.FindName("HomeSearchBox")).Text = "Translate window tells";
            Settle();
            Save("search-more");
            ((TextBox)window.FindName("HomeSearchBox")).Text = "";
            foreach (var page in new[] { HomePage.Tutorials, HomePage.Guide, HomePage.Issues })
            {
                vm.HomePage = page;
                Settle();
                Save(page.ToString().ToLowerInvariant());
            }
            vm.HomePage = HomePage.Guide;
            vm.SelectedGuideEntry = vm.GuideEntries.First(g => g.Topic.TitleKey == "docs.places.title");
            Settle();
            Save("guide-places");
            // A tutorial's card opened by a rest of the mouse.
            vm.HomePage = HomePage.Tutorials;
            Settle();
            var tutorialsPage = (FrameworkElement)window.FindName("HomePageTutorials");
            var aPlace = All<ContentControl>(tutorialsPage).First(c => c.GetType() == typeof(ContentControl)
                                                                       && c.DataContext is TutorialListItem { Def.Id: "a-place" });
            SMSModForge.View.HoverReveal.Open(aPlace);
            Settle();
            aPlace.BringIntoView();
            Settle();
            Save("tutorial-open");
            // The window at its smallest: EX's rail runs out of room and says it goes on.
            window.Height = 640;
            Settle();
            Save("short");
        });
    }
}
