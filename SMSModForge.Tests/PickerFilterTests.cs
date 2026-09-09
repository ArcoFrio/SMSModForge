using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the "choose a file" dialogs offer.
/// <para/>
/// The failure this guards against is quiet and was found by a person rather
/// than by anything here: scenes learned to accept GIFs and videos, the picker
/// went on offering PNGs only, and the only way to choose an animation was to
/// know to switch the dropdown to "All files". A tool that accepts a file it
/// will not let anybody pick has a feature nobody can reach.
/// <para/>
/// So what is checked is AGREEMENT — between what the picker offers and what
/// <see cref="SMSModForge.Shared.MediaKinds"/> classifies — rather than a
/// string being what it was on the day it was written.
/// </summary>
public sealed class PickerFilterTests
{
    private readonly ITestOutputHelper _out;
    public PickerFilterTests(ITestOutputHelper o) => _out = o;

    /// <summary>The extensions one filter actually offers, "All files" aside.</summary>
    private static HashSet<string> Offered(string filter)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Alternating description|pattern pairs; the patterns are the odd ones.
        var parts = filter.Split('|');
        for (int i = 1; i < parts.Length; i += 2)
            foreach (string pattern in parts[i].Split(';'))
                if (pattern.StartsWith("*.", StringComparison.Ordinal) && pattern != "*.*")
                    found.Add(pattern[1..]);

        return found;
    }

    [Fact]
    public void ASceneOffersEverythingASceneCanBe()
    {
        var offered = Offered(PickerFilters.SceneArt);
        _out.WriteLine("scene art: " + string.Join(" ", offered.OrderBy(x => x)));

        // Every extension the tool classifies as art is offered...
        var known = SMSModForge.Shared.MediaKinds.StillExtensions
            .Concat(SMSModForge.Shared.MediaKinds.GifExtensions)
            .Concat(SMSModForge.Shared.MediaKinds.VideoExtensions)
            .ToList();

        Assert.Equal(known.OrderBy(x => x), offered.OrderBy(x => x));

        // ...and each really is classified, so the list cannot grow a typo.
        foreach (string extension in offered)
            Assert.NotEqual(MediaProbe.MediaKind.Unknown, MediaProbe.KindOf("art" + extension));

        // The specific thing that was wrong: an author could not pick their own
        // video without knowing to change the dropdown.
        Assert.Contains(".webm", offered);
        Assert.Contains(".gif", offered);
        Assert.Contains(".mp4", offered);
    }

    [Fact]
    public void EverywhereElseOffersStillsOnly()
    {
        // The control. A filter that offered everything everywhere would pass
        // the test above and quietly invite an author to animate a bust, which
        // the runtime cannot do and the validator rejects.
        var offered = Offered(PickerFilters.StillArt);
        _out.WriteLine("still art: " + string.Join(" ", offered.OrderBy(x => x)));

        Assert.Equal(SMSModForge.Shared.MediaKinds.StillExtensions.OrderBy(x => x),
                     offered.OrderBy(x => x));

        foreach (string extension in offered)
            Assert.False(SMSModForge.Shared.MediaKinds.IsAnimated("art" + extension));
    }

    [Fact]
    public void EveryFilterStillLetsSomebodyChooseAnythingAtAll()
    {
        // The picker is a convenience; validation is what decides. A filter
        // with no way out would make an unrecognised file unpickable rather
        // than merely unusual.
        foreach (string filter in new[] { PickerFilters.SceneArt, PickerFilters.StillArt })
            Assert.EndsWith("All files (*.*)|*.*", filter);
    }

    [Fact]
    public void TheDescriptionSaysWhatIsInIt()
    {
        // The dialog shows only the description, so an author choosing between
        // "Scene art" and "Still images" would otherwise be guessing.
        var parts = PickerFilters.SceneArt.Split('|');
        for (int i = 0; i < parts.Length - 1; i += 2)
            Assert.Contains(parts[i + 1], parts[i]);
    }

    // ── The action row, which decides per target ─────────────────────

    private static ParamRowViewModel SpriteRowFor(string kind)
    {
        var values = new Dictionary<string, string> { ["kind"] = kind };
        var schema = new ParamSchema("sprite", "Sprite", ParamType.SpriteRef, "", "");
        return new ParamRowViewModel(values, schema);
    }

    [Fact]
    public void ASpriteAimedAtASceneOffersAnimation()
    {
        Assert.Equal(PickerFilters.SceneArt, SpriteRowFor("Scene").PickerFilter);
        Assert.Equal(PickerFilters.SceneArt, SpriteRowFor("scene").PickerFilter);
    }

    [Fact]
    public void ASpriteAimedAtAnythingElseDoesNot()
    {
        // Same condition the validator uses to reject an animated sprite
        // pointed elsewhere, so the picker cannot offer a file that would then
        // be refused.
        foreach (string kind in new[] { "Character", "Place", "", "Bust" })
        {
            _out.WriteLine($"kind '{kind}' -> stills");
            Assert.Equal(PickerFilters.StillArt, SpriteRowFor(kind).PickerFilter);
        }
    }

    [Fact]
    public void ChangingTheTargetChangesWhatThePickerOffers()
    {
        // Without the notification the dialog would still be offering stills
        // after the author switched the action to a scene, and the only way out
        // would be to know about "All files".
        var values = new Dictionary<string, string> { ["kind"] = "Character" };
        var row = new ParamRowViewModel(values, new ParamSchema(
            "sprite", "Sprite", ParamType.SpriteRef, "", ""));

        var told = new List<string?>();
        row.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        values["kind"] = "Scene";
        row.RefreshEnabled();          // what a sibling's write triggers

        Assert.Contains(nameof(ParamRowViewModel.PickerFilter), told);
        Assert.Equal(PickerFilters.SceneArt, row.PickerFilter);
    }

    [Fact]
    public void TheScenesTabActuallyUsesTheSceneFilter()
    {
        // Through the real window, because everything above would pass with the
        // XAML still saying nothing: the default is PNG-only, and a filter that
        // is never wired up is exactly the bug this fixes. Nothing here proves
        // the field is connected until the field itself is asked.
        const int scenesTab = 6;

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.AddSceneCommand.Execute(null);
            vm.SelectedTabIndex = scenesTab;
            WindowHarness.Pump();

            var pickers = Descendants<SMSModForge.View.Controls.PathPickerBox>(window)
                .Where(p => BindsTo(p, nameof(SceneViewModel.SceneSprite)))
                .ToList();

            _out.WriteLine($"{pickers.Count} scene-sprite picker(s) on the Scenes tab");
            Assert.NotEmpty(pickers);

            foreach (var picker in pickers)
            {
                Assert.Equal(PickerFilters.SceneArt, picker.Filter);
                Assert.Contains("*.webm", picker.Filter);
            }
        });
    }

    /// <summary>Whether this picker's path is bound to the named property.</summary>
    private static bool BindsTo(SMSModForge.View.Controls.PathPickerBox picker, string property)
    {
        var binding = System.Windows.Data.BindingOperations.GetBinding(
            picker, SMSModForge.View.Controls.PathPickerBox.PathTextProperty);
        return binding?.Path?.Path == property;
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject from)
        where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(from);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(from, i);
            if (child is T found) yield return found;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void ARowThatIsNotASpriteIsNotGivenAnArtFilter()
    {
        var row = new ParamRowViewModel(
            new Dictionary<string, string> { ["kind"] = "Scene" },
            new ParamSchema("amount", "Amount", ParamType.Int, "", ""));

        Assert.Equal(PickerFilters.StillArt, row.PickerFilter);
    }
}
