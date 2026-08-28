using System;
using System.Collections.Generic;
using System.IO;
using SMSModForge.Model;

namespace SMSModForge.Validation;

/// <summary>
/// Checks that a pack's art is the size it will be drawn at.
/// <para/>
/// The runtime fits art of any size to the frame it was authored for, so a
/// wrong size no longer breaks anything. It is still worth saying: fitting is a
/// rescue, not a feature. Art authored at the frame's own size is the only art
/// whose pixels land one-to-one on screen, and an author who did not mean to
/// hand over a 512x512 bust wants to hear about it here rather than wonder why
/// it looks soft in game.
/// <para/>
/// Two things are worth separate messages, because they have different fixes:
/// a size that is simply not the expected one (rescaled, still fine), and an
/// ASPECT that is not the frame's (letterboxed, with transparent bars where the
/// art does not reach). The second is nearly always a mistake.
/// </summary>
internal static class ArtDimensions
{
    // Codes are stable identifiers an author's ignore list is keyed on; see
    // ValidationIssue.Code. Renaming one silently un-ignores it, so don't.
    public const string CodeBustSize    = "art.bustSize";
    public const string CodeBustAspect  = "art.bustAspect";
    public const string CodeLevelSize   = "art.levelSize";
    public const string CodeLevelAspect = "art.levelAspect";
    public const string CodeMaskSize    = "art.maskSize";
    public const string CodeUnreadable  = "art.unreadable";

    /// <summary>Bust art, and every overlay that shares its frame.</summary>
    public const int BustPixels = 256;

    /// <summary>A level layer, matching the game's own.</summary>
    public const int LevelWidth = 2048;
    public const int LevelHeight = 1136;

    /// <summary>A level's mask.</summary>
    public const int LevelMaskWidth = 256;
    public const int LevelMaskHeight = 143;

    /// <summary>
    /// Read a PNG's dimensions without decoding the pixels.
    /// <para/>
    /// Straight from the IHDR chunk, which the PNG format puts first: eight
    /// bytes of signature, then a chunk header, then width and height as
    /// big-endian 32-bit. Decoding whole images would make Validate as slow as
    /// the pack is large, for two numbers sitting at a fixed offset.
    /// </summary>
    public static bool TryReadPngSize(string absPath, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var f = File.OpenRead(absPath);
            Span<byte> head = stackalloc byte[24];
            if (f.Read(head) < 24) return false;

            // 89 P N G \r \n 26 \n
            if (head[0] != 0x89 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G')
                return false;
            // Bytes 12..15 are the chunk type; it has to be IHDR for the
            // dimensions to be where we are about to look.
            if (head[12] != 'I' || head[13] != 'H' || head[14] != 'D' || head[15] != 'R')
                return false;

            width  = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return width > 0 && height > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Compare one file against the frame it is drawn in, adding at most one
    /// issue. A missing file is not our business — the existing checks already
    /// report those, and reporting it twice helps nobody.
    /// </summary>
    public static void Check(List<ValidationIssue> issues, string packRoot, string rel,
                             string where, int frameW, int frameH,
                             string sizeCode, string aspectCode, string what)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || string.IsNullOrWhiteSpace(rel)) return;

        string abs = Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(abs)) return;

        if (!TryReadPngSize(abs, out int w, out int h))
        {
            issues.Add(new(Severity.Warning, where,
                $"{what} could not be read as a PNG. If it is a JPG or a BMP renamed to .png, " +
                "the game will not load it.", CodeUnreadable));
            return;
        }

        if (w == frameW && h == frameH) return;

        // Aspect first: it is the more specific complaint, and saying both
        // about one file is just the same news twice.
        bool sameAspect = (long)w * frameH == (long)h * frameW;
        if (!sameAspect)
        {
            issues.Add(new(Severity.Warning, where,
                $"{what} is {w}x{h}, which is not the shape of a {frameW}x{frameH} frame. " +
                "It will be scaled to fit and centred, leaving transparent bars on two sides.",
                aspectCode));
            return;
        }

        issues.Add(new(Severity.Info, where,
            $"{what} is {w}x{h} rather than {frameW}x{frameH}. It will be scaled to fit, " +
            "so it works — but it is the only size whose pixels land exactly as drawn.",
            sizeCode));
    }

    /// <summary>Every art check across a pack.</summary>
    public static void CheckAll(List<ValidationIssue> issues, ModPack pack, string packRoot)
    {
        if (string.IsNullOrWhiteSpace(packRoot)) return;

        foreach (var c in pack.Characters)
        {
            foreach (var o in c.Outfits)
            {
                string w = $"characters[{c.Name}].outfits[{o.Key}]";

                Check(issues, packRoot, o.BaseSprite, $"{w}.baseSprite",
                      BustPixels, BustPixels, CodeBustSize, CodeBustAspect, "The bust");
                Check(issues, packRoot, o.MaskSprite, $"{w}.maskSprite",
                      BustPixels, BustPixels, CodeMaskSize, CodeMaskSize, "The jiggle mask");
                if (o.BlinkEnabled)
                    Check(issues, packRoot, o.BlinkSprite, $"{w}.blinkSprite",
                          BustPixels, BustPixels, CodeBustSize, CodeBustAspect, "The blink frame");

                // Overlays share the bust's frame exactly: they are separate
                // sprites drawn on the same rig, so a mismatch shows as a mouth
                // that sits in the wrong place rather than as a scaling nicety.
                if (o.Mouth != null && o.Mouth.Enabled && !string.IsNullOrWhiteSpace(o.Mouth.Prefix))
                    for (int i = 1; i <= 4; i++)
                        Check(issues, packRoot, o.Mouth.Prefix + i + ".PNG",
                              $"{w}.mouth[{i}]", BustPixels, BustPixels,
                              CodeBustSize, CodeBustAspect, $"Mouth frame {i}");

                if (o.Expression != null && o.Expression.Enabled &&
                    !string.IsNullOrWhiteSpace(o.Expression.Prefix))
                    foreach (var name in new[] { "Happy", "Angry", "Sad", "Flirty" })
                        Check(issues, packRoot, o.Expression.Prefix + name + ".PNG",
                              $"{w}.expression[{name}]", BustPixels, BustPixels,
                              CodeBustSize, CodeBustAspect, $"The {name} expression");
            }
        }

        foreach (var p in pack.Places)
        {
            string w = $"places[{p.Key}]";
            Check(issues, packRoot, p.BaseSprite, $"{w}.baseSprite",
                  LevelWidth, LevelHeight, CodeLevelSize, CodeLevelAspect, "The front layer");
            Check(issues, packRoot, p.SecondarySprite, $"{w}.secondarySprite",
                  LevelWidth, LevelHeight, CodeLevelSize, CodeLevelAspect, "The back layer");
            Check(issues, packRoot, p.MaskSprite, $"{w}.maskSprite",
                  LevelMaskWidth, LevelMaskHeight, CodeMaskSize, CodeMaskSize, "The mask");
        }
    }
}
