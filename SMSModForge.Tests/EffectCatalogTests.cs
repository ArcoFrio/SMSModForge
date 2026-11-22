using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using Xunit;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.Tests;

/// <summary>
/// The FX tab's templates (1.10.0, Shared.Effects): every one has its words, a look in the preview, and the
/// picture the preview draws; an effect saves only what the author changed, and reads back the same.
/// </summary>
public sealed class EffectCatalogTests
{
    [Fact]
    public void EveryTemplateHasItsWordsAPreviewLookAndItsPicture()
    {
        string pictures = Path.Combine(AppContext.BaseDirectory, "VanillaFx");
        Assert.True(Directory.Exists(pictures), "the preview's pictures were not deployed beside the editor: " + pictures);
        var missing = new System.Collections.Generic.List<string>();
        foreach (var t in FX.Catalog)
        {
            if (!Loc.English.Has("fx.template." + t.Name)) missing.Add(t.Id + ": no fx.template." + t.Name);
            if (!Loc.English.Has("fx.template." + t.Name + ".tip")) missing.Add(t.Id + ": no description");
            if (!Loc.English.Has("fx.category." + t.Category)) missing.Add(t.Id + ": no words for its group " + t.Category);
            if (t.IsVanilla && string.IsNullOrEmpty(t.GamePath)) missing.Add(t.Id + ": a game template with nowhere in the game");
            if (t.Kind == FX.Kind.Particles && View.Controls.EffectPreview.GamePicture(t.Texture) == null
                && !(t.Name is "seagulls"))
                missing.Add(t.Id + ": no preview picture " + t.Texture);
            var look = View.Controls.EffectPreview.EffectLook.For(t);
            bool moves = look.Rate > 0 || look.Burst > 0 || t.Kind != FX.Kind.Particles;
            if (!moves) missing.Add(t.Id + ": the preview has no look for it (nothing would come)");
        }
        Assert.True(FX.Catalog.Select(t => t.Id).Distinct().Count() == FX.Catalog.Length, "two templates share an id");
        Assert.True(missing.Count == 0, string.Join("\n", missing));
        // Control: a name the catalogue does not have has no words and no look of its own.
        Assert.False(Loc.English.Has("fx.template.nothingLikeThis"));
    }

    [Fact]
    public void AnEffectSavesWhatWasChangedAndNothingElse_AndReadsBackTheSame()
    {
        var plain = new EffectDef { Key = "a", DisplayName = "A", Template = FX.ForgePrefix + "burst" };
        var json = JObject.Parse(JsonConvert.SerializeObject(plain));
        Assert.Equal(new[] { "key", "displayName", "template" }, json.Properties().Select(p => p.Name).ToArray());
        // An object's effect in a place: its template, and nothing to name it by.
        Assert.Equal(new[] { "template" }, JObject.Parse(JsonConvert.SerializeObject(EffectDef.ForAPlace())).Properties().Select(p => p.Name).ToArray());

        var changed = new EffectDef
        {
            Key = "b", Template = FX.FollowerPoints, ChangesGame = true, Amount = 3, Color = "#FF0000FF",
            PlayWhen = FX.WhenUnderTurnsOn, Under = "4_CG_Manager-Photos",
            Conditions = { new NodeConditionDef { Type = NodeConditionTypes.AlwaysTrue } },
        };
        string text = JsonConvert.SerializeObject(changed);
        var back = JsonConvert.DeserializeObject<EffectDef>(text)!;
        Assert.Equal(text, JsonConvert.SerializeObject(back));
        Assert.True(back.ChangesGame);
        Assert.Equal(3f, back.Amount);
        Assert.Equal("4_CG_Manager-Photos", back.Under);
        Assert.Single(back.Conditions);
        // Control: what is not set is not written.
        Assert.DoesNotContain("\"glow\"", text);
        Assert.DoesNotContain("\"x\"", text);
    }

    [Fact]
    public void ThePacksEffectsAreSavedUnderTheKeyThePluginReads()
    {
        var pack = new ModPack { PackId = "p" };
        Assert.DoesNotContain("\"" + FX.ListKey + "\"", JsonConvert.SerializeObject(pack));   // none: not written
        pack.Effects.Add(new EffectDef { Key = "e" });
        var json = JObject.Parse(JsonConvert.SerializeObject(pack));
        Assert.Equal("e", (string?)json[FX.ListKey]?[0]?[FX.KeyField]);
    }
}
