using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// What the list before a save says about a translation: the words that
/// change in that language, and nothing that is only the file's bookkeeping.
/// </summary>
public sealed class TranslationDiffTests
{
    private static TextFile File(params (string key, string text, string english, bool same)[] lines)
    {
        var f = new TextFile();
        foreach (var (key, text, english, same) in lines)
            f.Add(new TextFile.Entry { Key = key, Text = text, English = english, Same = same });
        return f;
    }

    [Fact]
    public void OnlyTheWordsInTheLanguageAreListed()
    {
        var before = File(("a", "Hola", "Hello", false),
                          ("b", "Bye", "Bye", false),          // untranslated: still the default words
                          ("c", "Adiós", "Farewell", false),
                          ("d", "Kiki", "Kiki", true));        // the same in this language, on purpose
        var after = File(("a", "¡Hola!", "Hello", false),      // changed
                         ("b", "Good bye", "Good bye", false), // the default words changed; still untranslated
                         ("c", "Farewell", "Farewell", false), // back to the default words: taken out
                         ("d", "Kiki", "Kiki", true),          // unchanged
                         ("e", "Nuevo", "New", false));        // added

        var changes = TranslationDiff.Compute("es", before, after);
        Assert.Equal(new[] { "a", "c", "e" }, changes.Select(c => c.Path));
        Assert.Equal(PackChangeKind.Changed, changes[0].Kind);
        Assert.Equal(("Hola", "¡Hola!"), (changes[0].Before, changes[0].After));
        Assert.Equal(PackChangeKind.Removed, changes[1].Kind);
        Assert.Equal(PackChangeKind.Added, changes[2].Kind);
        Assert.All(changes, c => Assert.Contains("Español", c.Section));
    }

    [Fact]
    public void ALanguageWithNoFileYetListsEveryWordItGets()
    {
        var after = File(("a", "Hola", "Hello", false), ("b", "Bye", "Bye", false));
        var change = Assert.Single(TranslationDiff.Compute("es", null, after));
        Assert.Equal(PackChangeKind.Added, change.Kind);
        Assert.Equal("a", change.Path);
    }
}
