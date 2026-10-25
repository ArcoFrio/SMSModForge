using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The faces in the Actor picker (the author, 1.8.0): found from the bust's
/// blink - its eyes - where those land on the bust, from the top of the art
/// where they do not (Evelyn's alien, the author's own example), and none for a
/// character with no bust. Read off the pixels handed back.
/// </summary>
public sealed class CharacterFaceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-faces-" + Guid.NewGuid().ToString("N"));

    public CharacterFaceTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string GameBust(string bust, string file)
        => Path.Combine(VanillaArtResolver.FindArtRoot() ?? throw new InvalidOperationException("no shipped bust art"), bust, file);

    private static double DrawnShare(BitmapSource face)
    {
        var px = new byte[face.PixelWidth * face.PixelHeight * 4];
        new FormatConvertedBitmap(face, PixelFormats.Bgra32, null, 0).CopyPixels(px, face.PixelWidth * 4, 0);
        int drawn = 0;
        for (int i = 3; i < px.Length; i += 4) if (px[i] > 16) drawn++;
        return (double)drawn / (face.PixelWidth * face.PixelHeight);
    }

    [Fact]
    public void AGameBustsFaceIsFoundFromItsEyes()
    {
        var face = CharacterFace.Cut(GameBust("AliceBust", "Base.PNG"), GameBust("AliceBust", "Blink.PNG"));
        Assert.NotNull(face);
        Assert.Equal(CharacterFace.Side, face!.PixelWidth);
        _out.WriteLine($"drawn: {DrawnShare(face):P0}");
        Assert.True(DrawnShare(face) > 0.8, "a face cut round the eyes is nearly all drawn");
    }

    [Fact]
    public void ABustWhoseEyesMissItIsCutFromItsTop()
    {
        // The alien borrows a crocodile-man's blink, which floats above its
        // head: cut round those "eyes", there is nothing to see. The control:
        // the same eyes, taken at their word, give no face at all.
        string bust = GameBust("S_Evelyn_AlienBust", "Base.PNG"), blink = GameBust("S_Evelyn_AlienBust", "Blink.PNG");
        var face = CharacterFace.Cut(bust, blink);
        Assert.NotNull(face);
        _out.WriteLine($"alien, drawn: {DrawnShare(face!):P0}");
        Assert.True(DrawnShare(face!) > 0.3);
    }

    private string Png(string name, int w, int h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    [Fact]
    public void APacksOwnBustsFaceIsWhereItsEyesAre()
    {
        // A bust drawn off-centre - head on the right - so a face cut from the
        // middle of the picture would miss it. The head is red; the body blue.
        string bust = Png("base.png", 400, 400, dc =>
        {
            dc.DrawRectangle(Brushes.Blue, null, new System.Windows.Rect(40, 200, 320, 200));
            dc.DrawEllipse(Brushes.Red, null, new System.Windows.Point(290, 120), 60, 70);
        });
        string blink = Png("blink.png", 400, 400, dc =>
            dc.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(265, 110, 50, 12)));

        var face = CharacterFace.Cut(bust, blink)!;
        var px = new byte[4];
        new FormatConvertedBitmap(face, PixelFormats.Bgra32, null, 0)
            .CopyPixels(new System.Windows.Int32Rect(CharacterFace.Side / 2, CharacterFace.Side / 2, 1, 1), px, 4, 0);
        _out.WriteLine($"middle of the face: B{px[0]} G{px[1]} R{px[2]} A{px[3]}");
        Assert.True(px[2] > 200 && px[0] < 60, "the middle of the face is not the head");
    }

    [Fact]
    public void ACharacterWithNoBustHasNoFace()
    {
        var pack = PackRepository.CreateEmpty("faces.pack");
        var voice = new CharacterDef { Key = "narrator", DisplayName = "Narrator" };
        Assert.Null(CharacterFace.FilesOf(voice, pack, _dir));
    }

    // ── The issue for a forgotten speaker ──

    private static ModPack PackWith(params DialogueNodeDef[] nodes)
    {
        var pack = PackRepository.CreateEmpty("speaker.pack");
        pack.Characters.Add(new CharacterDef { Key = "kiki", DisplayName = "Kiki" });
        var d = new DialogueDef { Key = "chat", DisplayName = "Chat" };
        foreach (var n in nodes) d.Nodes.Add(n);
        d.RootNodeIds.Add(nodes[0].Id);
        pack.Dialogues.Add(d);
        return pack;
    }

    private static bool Flagged(ModPack pack, int id)
        => PackValidator.Validate(pack, "").Any(i => i.Code == "node.lookWithoutActor" && i.Where.Contains($"[id={id}]"));

    [Fact]
    public void ALineWithALookButNoSpeakerAsksWhetherOneWasForgotten()
    {
        var narrated = new DialogueNodeDef { Id = 1, Text = "The tide comes in." };
        var forgot = new DialogueNodeDef { Id = 2, Text = "Hi!", Expression = "Happy" };
        var spoken = new DialogueNodeDef { Id = 3, Actor = "kiki", Text = "Hello.", Expression = "Happy" };
        narrated.Children.Add(2);
        forgot.Children.Add(3);
        var pack = PackWith(narrated, forgot, spoken);

        Assert.False(Flagged(pack, 1));   // a narrator's line is fine
        Assert.True(Flagged(pack, 2));
        Assert.False(Flagged(pack, 3));
    }

    /// <summary>
    /// Neutral is no face (the author, 1.8.0: 98 narrator lines of one pack
    /// held it, each flagged, and the editor gave no way to clear it - it is
    /// the same button as no face at all). An outfit still asks.
    /// </summary>
    [Fact]
    public void NeutralOnANarratorsLineIsNoFaceToForget()
    {
        var neutral = new DialogueNodeDef { Id = 1, Text = "(She is lost in thought.)", Expression = "neutral" };
        var shouted = new DialogueNodeDef { Id = 2, Text = "...", Expression = "Neutral " };
        var dressed = new DialogueNodeDef { Id = 3, Text = "*zip*", Expression = "neutral", Outfit = "Swimsuit" };
        var happy = new DialogueNodeDef { Id = 4, Text = "Hm.", Expression = "happy" };   // the control
        neutral.Children.Add(2);
        shouted.Children.Add(3);
        dressed.Children.Add(4);
        var pack = PackWith(neutral, shouted, dressed, happy);

        Assert.False(Flagged(pack, 1));
        Assert.False(Flagged(pack, 2));
        Assert.True(Flagged(pack, 3));
        Assert.True(Flagged(pack, 4));
    }

    /// <summary>
    /// A line nobody speaks that only repeats the look of the line played just
    /// before it changes nothing, and is not asked about (the author, 1.8.0:
    /// such lines are the norm in their pack - a "*splash*" between two lines
    /// of the same speaker, wearing what the line before put on).
    /// </summary>
    [Fact]
    public void ALookThatOnlyRepeatsTheLineBeforeIsNotAsked()
    {
        var spoken = new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Hm!", Expression = "neutral", Outfit = "SwimWet" };
        var splash = new DialogueNodeDef { Id = 2, Text = "*splash*", Outfit = "swimwet" };
        var other = new DialogueNodeDef { Id = 3, Text = "*zip*", Outfit = "Dress" };    // the control: a change
        var pack = PackWith(spoken, splash, other);
        pack.Dialogues[0].RootNodeIds.Clear();
        pack.Dialogues[0].RootNodeIds.AddRange(new[] { 1, 2, 3 });   // played top to bottom

        Assert.False(Flagged(pack, 2));
        Assert.True(Flagged(pack, 3));

        // Under a parent: the line before is the parent, or the one above it there.
        var parent = new DialogueNodeDef { Id = 10, Actor = "kiki", Text = "Watch.", Outfit = "Dress" };
        var child = new DialogueNodeDef { Id = 11, Text = "*twirl*", Outfit = "Dress" };
        var second = new DialogueNodeDef { Id = 12, Text = "*bow*", Outfit = "Swim" };   // after *twirl*, which wore Dress
        parent.Children.AddRange(new[] { 11, 12 });
        var nested = PackWith(parent, child, second);
        Assert.False(Flagged(nested, 11));
        Assert.True(Flagged(nested, 12));
    }

    [Fact]
    public void AnOptionOnAChoiceHasNoSpeakerToForget()
    {
        var choice = new DialogueNodeDef { Id = 1, Actor = "kiki", Text = "Swim?", Kind = DialogueNodeKind.Choice };
        var option = new DialogueNodeDef { Id = 2, Text = "Sure", Outfit = "Swimsuit" };
        choice.Children.Add(2);
        Assert.False(Flagged(PackWith(choice, option), 2));
    }
}
