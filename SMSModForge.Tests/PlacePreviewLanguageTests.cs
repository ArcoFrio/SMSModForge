using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.View.Controls;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The toolbar over what is selected in the place preview.
/// <para/>
/// Its chips were typed in English in code, so every language showed "Body",
/// "Shadow" and "Move". The screen walk never saw them: the toolbar only
/// appears once an object is picked in the preview. And the toolbar is built
/// once, so a word set rather than bound would stay in whatever language the
/// editor had when the preview first loaded.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePreviewLanguageTests
{
    /// <summary>The words on the toolbar's buttons, as drawn.</summary>
    private static List<string> ChipWords(PlacePreview preview)
    {
        var toolbar = (Border)typeof(PlacePreview)
            .GetField("_gizmoToolbar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(preview)!;
        var words = new List<string>();
        void Walk(object node)
        {
            if (node is Button { Content: TextBlock t }) words.Add(t.Text);
            if (node is DependencyObject d)
                foreach (object child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        Walk(toolbar);
        return words;
    }

    [Fact]
    public void The_toolbar_chips_follow_the_editors_language()
    {
        WindowHarness.Run(_ =>
        {
            try
            {
                var preview = new PlacePreview();
                var english = ChipWords(preview);
                Assert.Contains("Body", english);      // the control: the walk reads the chips
                Assert.Contains("Rotate", english);

                Loc.Use("de");
                WindowHarness.Pump();
                var german = ChipWords(preview);
                Assert.Equal(english.Count, german.Count);
                foreach (string key in new[] { "body", "shadow", "blink", "wet", "reflection", "move", "rotate", "scale" })
                    Assert.Contains(Loc.T("preview.gizmo." + key), german);
                Assert.DoesNotContain("Body", german);
                Assert.DoesNotContain("Rotate", german);
            }
            finally { Loc.Use(Loc.EnglishCode); }
        });
    }
}
