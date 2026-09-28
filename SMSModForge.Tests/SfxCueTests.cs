using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A pack's sound cues ("*slap*") are listened for in the pack's own words,
/// whatever language the line is read in: a cue is written once, and a
/// translation that reads "*шлёп*" is still the same slap.
/// </summary>
public sealed class SfxCueTests
{
    private static JObject Manifest(string text)
        => new()
        {
            ["dialogues"] = new JArray
            {
                new JObject
                {
                    ["key"] = "chat",
                    ["nodes"] = new JArray { new JObject { ["id"] = 1, ["actor"] = "hope", ["text"] = text } },
                },
            },
        };

    private static JObject Line(JObject manifest) => (JObject)manifest["dialogues"]![0]!["nodes"]![0]!;

    [Fact]
    public void ATranslatedLineIsListenedToInThePacksOwnWords()
    {
        var manifest = Manifest("*slap* Ouch!");
        var ru = TextFile.Parse("dialogue.chat.1 = *шлёп* Ой!\n# en: *slap* Ouch!\n");
        PackTexts.Apply(manifest, ru);

        Assert.Equal("*шлёп* Ой!", (string?)Line(manifest)["text"]);
        Assert.Equal("*slap* Ouch!", PackTexts.CueText(Line(manifest)));
    }

    [Fact]
    public void AnUntranslatedLineIsListenedToAsItIs()
    {
        var manifest = Manifest("*slap* Ouch!");
        PackTexts.Apply(manifest, null);
        Assert.Equal("*slap* Ouch!", PackTexts.CueText(Line(manifest)));
    }

    [Fact]
    public void ALineTypedOnlyInATranslationIsListenedToInTheWordsShown()
    {
        // No words of the pack's own - the cue can only be in the translation.
        var node = new JObject { ["text"] = "*slap* Ай!", [PackTexts.OriginalTextKey] = "" };
        Assert.Equal("*slap* Ай!", PackTexts.CueText(node));
    }
}
