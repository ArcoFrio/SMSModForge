using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A face a character is given reaches every place a face is picked or shown,
/// and each shows what the game will.
/// <para/>
/// Four places, each with its own list, and all of them had drifted from the
/// game (checked 2026-09-24): the Busts tab picture loaded only the game's four
/// faces, so a face the pack invented was missing beside it; a line's
/// Expression list was only rebuilt when the line's speaker changed, so a face
/// added on the Busts tab was not offered on a line already selected; the list
/// an action picks an expression from was built from the old actor list, which
/// is empty once actors are folded into characters; and the picture beside a
/// line looked the line's KEY up as though it were the FACE, which only the
/// game's four are.
/// </summary>
public sealed class ExpressionSyncTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public ExpressionSyncTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-faces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "art"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A real picture, one transparent pixel: the previews decode
    /// what they find, and a file that is not a PNG throws there.</summary>
    private void Art(string name) => File.WriteAllBytes(Path.Combine(_dir, "art", name), Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));

    // ── The game's rule ──────────────────────────────────────────────

    [Theory]
    [InlineData("Smirk", "SmirkFace")]     // a row naming its face differently from its key
    [InlineData("neutral", "")]            // a row naming no face: none showing
    [InlineData("Happy", "Happy")]         // not on the list: the key is the face's name
    [InlineData("", "")]
    public void AKeyShowsTheFaceTheSpeakersListNames(string key, string face)
    {
        var declared = new[]
        {
            new ActorExpressionDef { Key = "neutral", ExpressionGoName = "" },
            new ActorExpressionDef { Key = "Smirk", ExpressionGoName = "SmirkFace" },
        };
        Assert.Equal(face, ExpressionFaces.FaceFor(declared, key));
    }

    [Fact]
    public void TheArtIsFoundWhereTheGameGetsIt()
    {
        var pack = new ModPack();
        var own = new CharacterDef { Key = "kiki", Name = "Kiki" };
        own.Outfits.Add(new OutfitDef
        {
            GameObjectName = "KikiBust",
            Expression = new ExpressionSpec { Enabled = true, Prefix = "art/Expression" },
        });
        // One of the game's busts, given a face it never had.
        var borrowed = new CharacterDef { Key = "anna", Name = "Anna", BustSource = BustSource.Vanilla };
        var annaBust = new OutfitDef { GameObjectName = "AnnaBustForTest" };
        annaBust.SpriteOverrides.Add(new SpriteOverrideDef
        {
            Slot = Shared.SpriteSlotNames.Expression("Wink"),
            Sprite = "art/AnnaWink.PNG",
        });
        borrowed.Outfits.Add(annaBust);
        pack.Characters.Add(own);
        pack.Characters.Add(borrowed);
        Art("ExpressionSmirkFace.PNG");
        Art("AnnaWink.PNG");

        Assert.Equal(Path.Combine(_dir, "art", "ExpressionSmirkFace.PNG"),
                     VanillaArtResolver.FindExpressionSpritePath("KikiBust", "SmirkFace", pack, _dir));
        Assert.Equal(Path.Combine(_dir, "art", "AnnaWink.PNG"),
                     VanillaArtResolver.FindExpressionSpritePath("AnnaBustForTest", "Wink", pack, _dir));

        // The control: the key, looked up as though it were the face - what
        // the picture used to do - finds nothing.
        Assert.Null(VanillaArtResolver.FindExpressionSpritePath("KikiBust", "Smirk", pack, _dir));
    }

    // ── Through the editor ───────────────────────────────────────────

    /// <summary>A saved pack: one character with a bust of its own, one line
    /// spoken by them.</summary>
    private void Prepare()
    {
        var pack = PackRepository.CreateEmpty("faces.pack");
        var kiki = new CharacterDef { Key = "kiki", Name = "Kiki", DisplayName = "Kiki", DefaultOutfit = "KikiBust" };
        kiki.Expressions.Add(CharacterDef.NewNeutral());
        kiki.Outfits.Add(new OutfitDef
        {
            Key = "day", GameObjectName = "KikiBust", BaseSprite = "art/Base.PNG",
            Expression = new ExpressionSpec { Enabled = true, Prefix = "art/Expression" },
        });
        pack.Characters.Add(kiki);
        var d = new DialogueDef { Key = "beach" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hello there!" });
        d.RootNodeIds.Add(1);
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);
        Art("Base.PNG");
        Art("ExpressionSmirkFace.PNG");
    }

    [Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
    [Fact]
    public void AFaceAddedOnTheBustsTabReachesTheLineAlreadySelected()
    {
        Prepare();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedDialogue = vm.Dialogues.Single(x => x.Key == "beach");
            WindowHarness.Pump();
            vm.SelectedNode = vm.SelectedDialogue.Nodes.Single(n => n.Id == 1);
            WindowHarness.Pump();
            Assert.DoesNotContain("Smirk", vm.SelectedNodeExpressionOptions);   // the control

            // Added on the Busts tab, with the line still selected.
            var kiki = vm.Characters.Single(c => c.Key == "kiki");
            var row = kiki.AddExpression();
            row.Key = "Smirk";
            row.ExpressionGoName = "SmirkFace";
            WindowHarness.Pump();

            _out.WriteLine("line offers: " + string.Join(", ", vm.SelectedNodeExpressionOptions));
            _out.WriteLine("actions offer: " + string.Join(", ", vm.ExpressionKeyOptions));
            Assert.Contains("Smirk", vm.SelectedNodeExpressionOptions);
            Assert.Contains("Smirk", vm.ExpressionKeyOptions);

            // Picked on the line, the picture beside it shows the face the
            // game will: the one the row names.
            vm.SelectedNode!.Expression = "Smirk";
            Assert.Equal("SmirkFace", vm.SelectedNodeFace);
            Assert.Equal(Path.Combine(_dir, "art", "ExpressionSmirkFace.PNG"),
                         VanillaArtResolver.FindExpressionSpritePath(vm.SelectedNodeActorBustKey, vm.SelectedNodeFace, vm.Pack, vm.PackRoot));

            // And the picture on the Busts tab lists it among the bust's faces.
            var outfit = kiki.Outfits.Single();
            var preview = new JigglePreview { PackRoot = _dir };
            preview.Outfit = outfit;
            _out.WriteLine("bust offers: " + string.Join(", ", preview.ExpressionChoices.Select(c => c.Key)));
            Assert.Contains(preview.ExpressionChoices, c => c.Key == "SmirkFace");

            // ...and follows a face added while it is showing.
            var wink = kiki.AddExpression();
            wink.Key = "Wink";
            WindowHarness.Pump();
            Assert.Contains(preview.ExpressionChoices, c => c.Key == "Wink");
        });
    }
}
