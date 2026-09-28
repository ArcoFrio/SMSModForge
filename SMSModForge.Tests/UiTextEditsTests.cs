using System;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Corrections to the editor's own words made on screen: saved as a file of
/// only what was changed, laid over the language that shipped, so the next
/// version's better words for everything else still show.
/// </summary>
public sealed class UiTextEditsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "smsmodforge-uiedits-" + Guid.NewGuid().ToString("N"));

    public UiTextEditsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private static TextFile File(string content) => TextFile.Parse(content);

    [Fact]
    public void TheAuthorsWordsShowWhereTheyTranslateTheEnglishAsItIsNow_TheShippedOnesEverywhereElse()
    {
        var english = File("a = Save\nb = Open\nc = Close\nd = Delete\n");
        var shipped = File("# en: Save\na = Speichern\n# en: Open\nb = Öffnen\n# en: Close\nc = Schließen\n# en: Delete\nd = Löschen\n");
        var mine = File(
            "# en: Save\na = Sichern\n" +          // theirs, for the English as it is
            "# en: Opened\nb = Geöffnet\n" +        // made for English that has since changed
            "# en: Close\nc = Close\n" +            // left as the English: nobody translated it
            "# en: Delete\nd = \"\"\n");            // nothing, on purpose

        var layered = UiTextEdits.Layer(shipped, mine, english);
        Assert.Equal("Sichern", layered.Translated("a"));
        Assert.Equal("Öffnen", layered.Translated("b"));
        Assert.Equal("Schließen", layered.Translated("c"));
        Assert.Equal("", layered.Translated("d"));
    }

    [Fact]
    public void ASaveWritesOnlyWhatChanged_UnderTheEnglishItTranslates_AndForgettingItRemovesTheFile()
    {
        string key = Loc.English.Entries.First(e => e.Key.StartsWith("menu.")).Key;
        string path = UiTextEdits.Save("de", key, "Meine Worte", _folder);

        var saved = Loc.Read(path)!;
        Assert.Single(saved.Entries);
        Assert.Equal("Meine Worte", saved.Translated(key));
        Assert.Equal(Loc.English.Get(key), saved.Find(key)!.English);
        Assert.True(UiTextEdits.IsEdited("de", key, _folder));

        UiTextEdits.Forget("de", key, _folder);
        Assert.False(System.IO.File.Exists(path));
        Assert.False(UiTextEdits.IsEdited("de", key, _folder));
    }

    [Fact]
    public void AWholeTranslationOfTheAuthorsOwnStaysWhole()
    {
        var (path, _) = TranslationFiles.CreateOrUpdate("de", _folder, Loc.ShippedFolder);
        int before = Loc.Read(path)!.Entries.Count;
        Assert.True(before * 2 >= Loc.English.Entries.Count);

        string key = Loc.English.Entries.First(e => e.Key.StartsWith("menu.")).Key;
        UiTextEdits.Save("de", key, "Meine Worte", _folder);
        var after = Loc.Read(path)!;
        Assert.Equal("Meine Worte", after.Translated(key));
        Assert.True(after.Entries.Count >= before - 5, $"{before} texts before, {after.Entries.Count} after");
    }

    [Theory]
    [InlineData("'{pack}' changes {count} lines", "'{pack}' ändert {count} Zeilen", "", "")]
    [InlineData("'{pack}' changes {count} lines", "ändert {count} Zeilen", "{pack}", "")]
    [InlineData("Save", "Speichern {name}", "", "{name}")]
    public void AGapTheEnglishFillsMustStay_AndNoGapNothingFills(string english, string text, string lost, string unknown)
    {
        var (l, u) = UiTextEdits.CheckGaps(english, text);
        Assert.Equal(lost, string.Join(",", l));
        Assert.Equal(unknown, string.Join(",", u));
    }

    [Fact]
    public void ATextThatSaysANumberIsEditedInEveryFormTheLanguageUses()
    {
        string plural = Loc.English.Entries.First(e => e.Key.EndsWith(".other")).Key;
        string baseKey = plural.Substring(0, plural.Length - ".other".Length);
        var ru = UiTextEdits.FormsOf(baseKey, "ru");
        Assert.Contains(baseKey + ".few", ru);
        Assert.Contains(baseKey + ".many", ru);
        Assert.Equal(new[] { plural }, UiTextEdits.FormsOf(plural, "ru"));
    }
}
