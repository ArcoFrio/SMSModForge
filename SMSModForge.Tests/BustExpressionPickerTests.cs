using System;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The expression picker beside the bust preview.
/// <para/>
/// It used to be five entries written out in the window — none, happy, angry,
/// sad, flirty — which was wrong in two ways that both looked like the preview
/// being broken rather than the list being wrong.
/// <para/>
/// It offered four faces on a bust that has none, so picking one did nothing.
/// And what it handed the preview was the entry's LABEL, so the moment the
/// editor was in any language but English it searched for "Feliz" among sprites
/// named "Happy" and found nothing: every expression in the editor stopped
/// working, silently, with no way to tell that from art that had not loaded.
/// <para/>
/// Both are the same fix — the list comes from the textures the preview
/// actually loaded, and carries the game's name for each rather than the word
/// on screen.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class BustExpressionPickerTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _was = Loc.Current.Code;

    public BustExpressionPickerTests(ITestOutputHelper o) => _out = o;

    /// <summary>Somebody is running this in their own language.</summary>
    public void Dispose() => Loc.Use(_was);

    private static OutfitViewModel Outfit(bool expressions) => new(new OutfitDef
    {
        GameObjectName = "TestBust",
        BaseSprite = "art/base.png",
        Expression = new ExpressionSpec { Enabled = expressions, Prefix = "art/Expression" },
    });

    /// <summary>A preview loaded with one outfit, off the UI thread's window.</summary>
    private static JigglePreview Preview(OutfitViewModel outfit)
    {
        // PackRoot first: the load runs on every property change, and one with
        // no root clears everything and publishes an empty list.
        var preview = new JigglePreview { PackRoot = @"C:\nowhere" };
        preview.Outfit = outfit;
        return preview;
    }

    [Fact]
    public void TheListIsTheFacesTheBustActuallyHas()
    {
        WindowHarness.Run(_ =>
        {
            var preview = Preview(Outfit(expressions: true));
            var keys = preview.ExpressionChoices.Select(c => c.Key).ToArray();
            _out.WriteLine(string.Join(", ", preview.ExpressionChoices.Select(c => $"{c.Key}='{c.Label}'")));

            // None first, then the game's four in the order the editor names
            // them everywhere else.
            Assert.Equal(new[] { "", "Happy", "Angry", "Sad", "Flirty" }, keys);
        });
    }

    [Fact]
    public void ABustWithNoExpressionsOffersNone()
    {
        // The control, and the first of the two bugs: four faces were offered
        // on a bust that cannot pull any, and picking one did nothing at all.
        WindowHarness.Run(_ =>
        {
            var preview = Preview(Outfit(expressions: false));
            _out.WriteLine(string.Join(", ", preview.ExpressionChoices.Select(c => c.Key)));

            var only = Assert.Single(preview.ExpressionChoices);
            Assert.Equal("", only.Key);
        });
    }

    [Fact]
    public void ChangingLanguageChangesTheWordsAndNotTheValues()
    {
        // The second bug, and the one that made every expression stop working:
        // the picker stored what it SHOWED. The names the sprites are called by
        // are the game's and are not translated, so a translated label found
        // nothing - in silence, looking exactly like missing art.
        WindowHarness.Run(_ =>
        {
            Loc.Use("en");
            var english = Preview(Outfit(expressions: true)).ExpressionChoices.ToList();

            Loc.Use("es");
            var spanish = Preview(Outfit(expressions: true)).ExpressionChoices.ToList();

            foreach (var (en, es) in english.Zip(spanish))
                _out.WriteLine($"{en.Key}: '{en.Label}' / '{es.Label}'");

            // What is stored never moves...
            Assert.Equal(english.Select(c => c.Key), spanish.Select(c => c.Key));

            // ...and what is read does. If it did not, this test would pass
            // while proving nothing, because English and Spanish would be the
            // same list.
            Assert.Equal("Happy", english.Single(c => c.Key == "Happy").Label);
            Assert.NotEqual("Happy", spanish.Single(c => c.Key == "Happy").Label);
            Assert.NotEqual(english[0].Label, spanish[0].Label);
        });
    }

    [Fact]
    public void TheWindowsPickerIsTheOneThePreviewPublishes()
    {
        // The two halves are wired in XAML, so a passing control test proves
        // only half of it: the picker could still be reading the old fixed
        // list and nothing here would notice.
        WindowHarness.Run(window =>
        {
            var tabs = (System.Windows.Controls.TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is System.Windows.Controls.TabItem t && (t.Header as string) == "Characters")
                { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var picker = (System.Windows.Controls.ComboBox)window.FindName("ExpressionPicker");
            var preview = (JigglePreview)window.FindName("PreviewControl");
            Assert.NotNull(picker);
            Assert.NotNull(preview);

            _out.WriteLine("picker holds " + picker.Items.Count + " item(s)");
            Assert.Same(preview.ExpressionChoices, picker.ItemsSource);

            // And what a choice hands the preview is the game's name, not the
            // word in the box - which is the whole point.
            Assert.Equal("Key", picker.SelectedValuePath);
            Assert.Equal("Label", picker.DisplayMemberPath);
        });
    }

    [Fact]
    public void ANewBustReplacesTheLastOnesFaces()
    {
        // Switching outfits has to re-publish, or the picker keeps offering the
        // previous bust's faces against art that does not have them.
        WindowHarness.Run(_ =>
        {
            var preview = Preview(Outfit(expressions: true));
            Assert.Equal(5, preview.ExpressionChoices.Count);

            preview.Outfit = Outfit(expressions: false);
            _out.WriteLine(string.Join(", ", preview.ExpressionChoices.Select(c => c.Key)));
            Assert.Single(preview.ExpressionChoices);
        });
    }
}
