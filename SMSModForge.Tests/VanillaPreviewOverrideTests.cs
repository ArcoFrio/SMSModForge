using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The preview shows the textures a pack replaces on one of the game's busts.
/// <para/>
/// It did not. The preview of a borrowed bust loaded the shipped vanilla art and
/// stopped there, with no notion that a pack could paint over any of it — so an
/// author ticked a slot, chose their PNG, looked at the picture beside the panel
/// and saw the game's bust, unchanged. The runtime had been applying these since
/// the feature landed; only the picture had not, which reads as the replacement
/// not working at all.
/// <para/>
/// Two halves, and both were missing: the preview never READ the overrides, and
/// nothing told it to look again when one changed — an override row raises its
/// own change notification, not the outfit's.
/// </summary>
public sealed class VanillaPreviewOverrideTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public VanillaPreviewOverrideTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmf-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* tidying is not the test */ }
    }

    /// <summary>A 256x256 PNG of one flat colour, so a loaded buffer can be
    /// identified by reading a pixel out of it rather than by trusting a path.</summary>
    private string Png(string name, byte b, byte g, byte r)
    {
        const int size = 256;
        int stride = size * 4;
        var px = new byte[stride * size];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, stride);
        string abs = Path.Combine(_dir, name);
        using var fs = File.Create(abs);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(fs);
        return name;
    }

    /// <summary>The colour of a loaded texture's first pixel, as (B, G, R).</summary>
    private static (byte B, byte G, byte R) FirstPixel(byte[]? buffer)
    {
        Assert.NotNull(buffer);
        return (buffer![0], buffer[1], buffer[2]);
    }

    /// <summary>
    /// This slot did not get the pack's art.
    /// <para/>
    /// Not "it is null": whether it holds anything depends on whether the
    /// machine running this has the vanilla art extraction beside it, and on a
    /// machine that does, the game's own texture is there and is usually
    /// transparent in its top-left corner. What the claim actually is, either
    /// way, is that the pack's colour is not what landed here.
    /// </summary>
    private void AssertNotThePacks(byte[]? buffer, (byte B, byte G, byte R) theirs, string what)
    {
        if (buffer == null) { _out.WriteLine($"{what}: nothing loaded (no extraction here)"); return; }
        var pixel = FirstPixel(buffer);
        _out.WriteLine($"{what}: {pixel}");
        Assert.True(pixel != theirs, $"{what} was given the pack's texture");
    }

    /// <summary>One of the game's characters, with a bust of theirs selected and
    /// the preview pointed at it exactly as MainWindow points it.</summary>
    private (OutfitViewModel Bust, JigglePreview Preview) Borrowed()
    {
        var pack = new ModPack();
        VanillaCastSeed.Seed(pack);
        var them = new CharacterViewModel(
            pack.Characters.First(c => !string.IsNullOrEmpty(c.VanillaCharacter)));
        var bust = them.Outfits[0];
        Assert.True(bust.IsVanillaBust, "this test is about a bust the GAME draws");

        var preview = new JigglePreview
        {
            PackRoot = _dir,
            Outfit = bust,
            VanillaBustKey = bust.GameObjectName,
        };
        return (bust, preview);
    }

    [Fact]
    public void AReplacedTextureIsWhatThePreviewDraws()
    {
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();

            string art = Png("base.png", 0, 0, 255);           // flat red
            bust.Model.SpriteOverrides.Add(new SpriteOverrideDef
            {
                Slot = SpriteSlotNames.Base,
                Sprite = art,
            });
            preview.Outfit = null;                              // force a reload
            preview.Outfit = bust;

            var pixel = FirstPixel(preview.Loaded().Base);
            _out.WriteLine($"base pixel: {pixel}");
            Assert.Equal<(byte, byte, byte)>((0, 0, 255), pixel);
        });
    }

    [Fact]
    public void EverySlotKindLandsWhereItBelongs()
    {
        // Five kinds of slot, five different places in the renderer, and a
        // mapping that is easy to get subtly wrong - a mouth frame is 1-based,
        // and a face is named by a prefixed slot rather than by itself.
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();
            var m = bust.Model;

            m.SpriteOverrides.Add(new SpriteOverrideDef
            { Slot = SpriteSlotNames.Blink, Sprite = Png("blink.png", 0, 255, 0) });
            m.SpriteOverrides.Add(new SpriteOverrideDef
            { Slot = SpriteSlotNames.Mouth[2], Sprite = Png("mouth3.png", 255, 0, 0) });
            m.SpriteOverrides.Add(new SpriteOverrideDef
            { Slot = SpriteSlotNames.Expression("Happy"), Sprite = Png("happy.png", 0, 255, 255) });

            preview.Outfit = null;
            preview.Outfit = bust;
            var drawn = preview.Loaded();

            Assert.Equal<(byte, byte, byte)>((0, 255, 0), FirstPixel(drawn.Blink));

            // Mouth[2] is the slot named "mouth3", so it is frame 3 - not index
            // 2. Getting that off by one would put the pack's art on the wrong
            // frame, which is why the neighbour is checked as well.
            _out.WriteLine("mouth slot used: " + SpriteSlotNames.Mouth[2]);
            Assert.Equal<(byte, byte, byte)>((255, 0, 0), FirstPixel(drawn.Mouth[3]));
            AssertNotThePacks(drawn.Mouth[2], (255, 0, 0), "mouth frame 2");

            Assert.True(drawn.Faces.ContainsKey("Happy"),
                        "the face is filed under the name the renderer looks it up by");
            Assert.Equal<(byte, byte, byte)>((0, 255, 255), FirstPixel(drawn.Faces["Happy"]));
        });
    }

    [Fact]
    public void ASlotThePackNeverMentionedIsNotTouched()
    {
        // The control, and the promise the whole feature rests on. Replacing
        // the base must not blank the blink, the mouths or the faces.
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();

            bust.Model.SpriteOverrides.Add(new SpriteOverrideDef
            { Slot = SpriteSlotNames.Base, Sprite = Png("base.png", 0, 0, 255) });
            preview.Outfit = null;
            preview.Outfit = bust;

            var drawn = preview.Loaded();
            _out.WriteLine($"base set: {drawn.Base != null}; blink: {drawn.Blink != null}; "
                           + $"faces: {drawn.Faces.Count}");

            Assert.NotNull(drawn.Base);
            AssertNotThePacks(drawn.Blink, (0, 0, 255), "blink");
            AssertNotThePacks(drawn.Mouth[1], (0, 0, 255), "mouth frame 1");
        });
    }

    [Fact]
    public void TickedWithNoArtChosenLeavesTheGamesTextureAlone()
    {
        // An author part-way through: the row is ticked and no file picked yet.
        // Blanking the picture here would say the pack had removed a texture,
        // which is not what an empty path means - the game keeps its own.
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();

            bust.Model.SpriteOverrides.Add(new SpriteOverrideDef
            { Slot = SpriteSlotNames.Base, Sprite = "" });
            preview.Outfit = null;
            preview.Outfit = bust;

            var drawn = preview.Loaded();
            _out.WriteLine("base after an empty override: " + (drawn.Base?.Length.ToString() ?? "null"));

            // Nothing of the pack's was loaded over it. With no shipped art
            // beside the tests this is null; with art it is the game's own.
            // What it must never be is a blank buffer the pack put there.
            Assert.True(drawn.Base == null || drawn.Base.Any(x => x != 0),
                        "an empty path blanked the texture instead of leaving it");
        });
    }

    [Fact]
    public void ChangingARowRedrawsThePreview()
    {
        // The second half. The preview reloads off the OUTFIT's change
        // notifications, and an override row raises its own - so without the
        // outfit passing it on, the picture stayed on whatever it had loaded
        // when the bust was selected.
        WindowHarness.Run(_ =>
        {
            var (bust, preview) = Borrowed();

            string first = Png("first.png", 0, 0, 255);         // red
            string second = Png("second.png", 255, 0, 0);       // blue

            var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Base);
            row.Replaced = true;
            row.Path = first;
            WindowHarness.Pump();
            Assert.Equal<(byte, byte, byte)>((0, 0, 255), FirstPixel(preview.Loaded().Base));

            // No re-selection, no reload by hand: just the author typing a
            // different path into the row, which is the reported case.
            row.Path = second;
            WindowHarness.Pump();

            var pixel = FirstPixel(preview.Loaded().Base);
            _out.WriteLine($"after pointing the row at a second file: {pixel}");
            Assert.Equal<(byte, byte, byte)>((255, 0, 0), pixel);
        });
    }

    // ── The painter's live buffer ──────────────────────────────────
    //
    // While the mask editor is open it publishes each stroke into its host and
    // the preview draws that instead of the file, so the jiggle changes under
    // the brush. On a pack outfit the host is the outfit. On one of the game's
    // busts it is the override ROW - the path lives there, so the buffer does
    // too - and the preview reads the outfit. So the strokes went nowhere.

    /// <summary>A buffer the size the painter publishes, filled so it cannot be
    /// mistaken for an empty one.</summary>
    private static byte[] Strokes(byte value)
    {
        var buf = new byte[SMSModForge.Rendering.MaskBuffer.BgraStride
                           * SMSModForge.Rendering.MaskBuffer.Size];
        for (int i = 0; i < buf.Length; i++) buf[i] = value;
        return buf;
    }

    [Fact]
    public void StrokesOnAVanillaBustsMaskReachThePreview()
    {
        WindowHarness.Run(_ =>
        {
            var (bust, _) = Borrowed();
            var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Mask);
            row.Replaced = true;

            Assert.Null(bust.LiveMaskBgra);

            // Exactly what MaskEditorWindow does on open, through the same
            // interface it holds the host by.
            var host = (IMaskEditorHost)row;
            var painting = Strokes(0x7F);
            host.LiveMaskBgra = painting;

            // This property IS the preview's render input - JigglePreview reads
            // Outfit.LiveMaskBgra when choosing the mask for the shader pass.
            Assert.Same(painting, bust.LiveMaskBgra);

            // ...and closing the painter hands the picture back to the file.
            host.LiveMaskBgra = null;
            Assert.Null(bust.LiveMaskBgra);
        });
    }

    [Fact]
    public void TheOutfitSaysSoWhenTheBufferChanges()
    {
        // The preview is told, not polled: it reloads off the outfit's change
        // notifications, so a buffer that arrives silently is a buffer nothing
        // repaints for.
        WindowHarness.Run(_ =>
        {
            var (bust, _) = Borrowed();
            var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Mask);
            row.Replaced = true;

            int told = 0;
            bust.PropertyChanged += (_, e) =>
            { if (e.PropertyName == nameof(OutfitViewModel.LiveMaskBgra)) told++; };

            ((IMaskEditorHost)row).LiveMaskBgra = Strokes(0x40);

            _out.WriteLine($"LiveMaskBgra announced {told} time(s)");
            Assert.True(told > 0, "the buffer arrived and nothing said so");
        });
    }

    [Fact]
    public void APerStrokeBumpDoesNotReloadEveryTextureFromDisk()
    {
        // The trap this wiring sits next to. The preview reloads its PNGs when
        // the outfit raises Overrides, and the painter bumps its revision
        // several times a second while somebody is drawing - so mapping every
        // row notification onto Overrides would put file I/O under the brush.
        WindowHarness.Run(_ =>
        {
            var (bust, _) = Borrowed();
            var row = bust.Overrides.Single(o => o.Slot == SpriteSlotNames.Mask);
            row.Replaced = true;

            int reloads = 0;
            bust.PropertyChanged += (_, e) =>
            { if (e.PropertyName == nameof(OutfitViewModel.Overrides)) reloads++; };

            var host = (IMaskEditorHost)row;
            for (int i = 0; i < 20; i++)
            {
                host.LiveMaskBgra = Strokes((byte)i);
                host.LiveMaskRevision++;
            }

            _out.WriteLine($"20 strokes -> {reloads} texture reload(s)");
            Assert.Equal(0, reloads);

            // ...and the control: choosing a different file still does reload.
            row.Path = Png("newmask.png", 10, 20, 30);
            Assert.True(reloads > 0, "picking new art no longer reloads the preview");
        });
    }
}
