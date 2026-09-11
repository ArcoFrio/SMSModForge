using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every bust in the preview moves by the pack's jiggle settings, one of the
/// game's included.
/// <para/>
/// It used to prefer the uniforms the extractor read off the game's own
/// material — speed, strength, frequency, the noise trio — on the reasoning that
/// a borrowed bust should move the way the game moves it. That reasoning holds
/// only if this shader IS the game's, and it is not: it is an approximation.
/// Putting the game's numbers through a different shader does not reproduce the
/// game, it produces a third thing — one that disagreed with every pack bust
/// beside it for reasons an author could not see or change, since the jiggle
/// sliders are hidden on a borrowed bust.
/// <para/>
/// Reported as one of the game's characters looking "much more slowed down"
/// while being edited. It was: Adrian's own frequency is 1 where a pack bust
/// starts at 4.
/// <para/>
/// The extracted <c>Jiggle.txt</c> files still ship beside the art. They are the
/// game's real numbers and worth keeping if these ever become something a pack
/// can set; they are simply not what the preview runs on.
/// </summary>
public sealed class PreviewJiggleAlignmentTests
{
    private readonly ITestOutputHelper _out;
    public PreviewJiggleAlignmentTests(ITestOutputHelper o) => _out = o;

    private static (OutfitViewModel Bust, JigglePreview Preview) Borrowed()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        var them = new CharacterViewModel(
            pack.Characters.First(c => !string.IsNullOrEmpty(c.VanillaCharacter)));
        var bust = them.Outfits[0];
        Assert.True(bust.IsVanillaBust, "this test is about a bust the GAME draws");

        var preview = new JigglePreview { Outfit = bust, VanillaBustKey = bust.GameObjectName };
        return (bust, preview);
    }

    /// <summary>What the game ships for this bust, or null when the art
    /// extraction is not beside the tests.</summary>
    private static JiggleParams? TheGames(string bustKey)
    {
        string? root = SMSModForge.Rendering.VanillaArtResolver.FindArtRoot();
        if (root == null) return null;
        string abs = Path.Combine(root, bustKey, "Jiggle.txt");
        if (!File.Exists(abs)) return null;

        var p = new JiggleParams();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var raw in File.ReadAllLines(abs))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == '#' || eq <= 0) continue;
            if (!float.TryParse(line.Substring(eq + 1).Trim(),
                                System.Globalization.NumberStyles.Float, inv, out float f)) continue;
            switch (line.Substring(0, eq).Trim())
            {
                case "JiggleSpeed": p.Speed = f; break;
                case "JiggleFrequency": p.Frequency = f; break;
                case "NoiseScale": p.NoiseScale = f; break;
            }
        }
        return p;
    }

    [Fact]
    public void ABorrowedBustRunsOnThePacksNumbers()
    {
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();
            var running = preview.JiggleInEffect;
            var theirs = bust.Model.Jiggle;

            _out.WriteLine($"{bust.GameObjectName}: speed {running.Speed}, "
                           + $"frequency {running.Frequency}, noiseScale {running.NoiseScale}");

            Assert.Equal(theirs.Speed, running.Speed);
            Assert.Equal(theirs.Strength, running.Strength);
            Assert.Equal(theirs.Frequency, running.Frequency);
            Assert.Equal(theirs.NoiseScale, running.NoiseScale);
            Assert.Equal(theirs.NoiseSpeed, running.NoiseSpeed);
            Assert.Equal(theirs.NoiseStrength, running.NoiseStrength);
        });
    }

    [Fact]
    public void AndTheGamesOwnNumbersAreGenuinelyDifferent()
    {
        // Without this the test above could pass on a bust whose shipped
        // numbers happen to equal the defaults - 78 of the cast's 318 busts do
        // - and would then be proving nothing at all.
        WindowHarness.Run(_ =>
        {
            var pack = new ModPack();
            VanillaCastSeed.Seed(pack);

            JiggleParams? differs = null;
            OutfitViewModel? subject = null;
            foreach (var def in pack.Characters.Where(c => !string.IsNullOrEmpty(c.VanillaCharacter)))
            {
                var them = new CharacterViewModel(def);
                foreach (var bust in them.Outfits)
                {
                    var shipped = TheGames(bust.GameObjectName);
                    if (shipped == null) continue;
                    if (shipped.Frequency == bust.Model.Jiggle.Frequency
                        && shipped.Speed == bust.Model.Jiggle.Speed) continue;
                    differs = shipped;
                    subject = bust;
                    break;
                }
                if (subject != null) break;
            }

            if (subject == null)
            {
                // Skipping is only honest when there is nothing to look at. If
                // the art IS here and no bust disagreed with the defaults, this
                // test has been passing on nothing and should say so.
                Assert.True(SMSModForge.Rendering.VanillaArtResolver.FindArtRoot() == null,
                            "the art extraction is here, but no bust was found whose shipped "
                            + "numbers differ from the defaults - this test proved nothing");
                _out.WriteLine("no extraction beside the tests - skipping");
                return;
            }

            var preview = new JigglePreview
            { Outfit = subject, VanillaBustKey = subject.GameObjectName };

            _out.WriteLine($"{subject.GameObjectName}: the game says speed {differs!.Speed} / "
                           + $"frequency {differs.Frequency}; the preview runs "
                           + $"{preview.JiggleInEffect.Speed} / {preview.JiggleInEffect.Frequency}");

            Assert.Equal(subject.Model.Jiggle.Frequency, preview.JiggleInEffect.Frequency);
            Assert.NotEqual(differs.Frequency, preview.JiggleInEffect.Frequency);
        });
    }

    [Fact]
    public void APackBustIsUnchanged()
    {
        // The control on the other side: aligning the borrowed ones must not
        // have quietly taken the pack's own settings away from a pack bust.
        WindowHarness.Run(_ =>
        {
            var character = new CharacterDef { Key = "mine", DisplayName = "Mine" };
            character.Outfits.Add(new OutfitDef { Key = "mine", GameObjectName = "Mine" });
            var them = new CharacterViewModel(character);
            var bust = them.Outfits[0];
            Assert.False(bust.IsVanillaBust);

            bust.Model.Jiggle.Frequency = 7.5f;
            var preview = new JigglePreview { Outfit = bust };

            Assert.Equal(7.5f, preview.JiggleInEffect.Frequency);
        });
    }

    [Fact]
    public void ThePreviewFollowsTheSlidersOnEitherKind()
    {
        // What "aligned" has to mean in practice: one set of settings drives
        // the picture, so moving a slider moves the bust - and a borrowed one
        // is no longer pinned to numbers nothing on screen can reach.
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();

            bust.JiggleFrequency = 9f;
            Assert.Equal(9f, preview.JiggleInEffect.Frequency);

            bust.JiggleSpeed = 0.25f;
            Assert.Equal(0.25f, preview.JiggleInEffect.Speed);
        });
    }
}
