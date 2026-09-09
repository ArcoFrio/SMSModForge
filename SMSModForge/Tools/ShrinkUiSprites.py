#!/usr/bin/env python3
"""Reduce the extracted UI sprites in place.

WHY
    The UI preview draws the game's own screens, and to do that the editor
    ships the game's own UI art: 851 sprites, 81 MB, about half of which is 31
    wallpapers and backgrounds at 1920x1080 or 2048 square. That is the single
    largest thing in the editor download.

    Unlike the bust and level art, this is NOT waste that can be reclaimed for
    free. Measured against every extracted surface, 661 of the 851 sprites are
    drawn at or above their native size -- the preview renders at the full
    canvas resolution and scales the finished image down, so a sprite drawn at
    400 canvas pixels genuinely wants 400 pixels. Reducing them trades preview
    sharpness for download size. That is a decision, and this script is where
    it is written down.

THE TIERS
    <= 128 px   untouched. 237 sprites totalling 0.9 MB: nothing to win, and
                these are the icons where a quarter of the detail is most
                obvious.
    >= 1280 px  halved. The 31 heavy-hitters, 40 MB between them. There is a
                real gap in the distribution -- nothing at all between 1280 and
                1600 -- so this threshold sits in empty space and will not
                reclassify anything if the extraction is re-run against a
                slightly different build.
    everything  three quarters. 583 sprites, mostly the 431 that are exactly
    in between  256x256.

WHY IN PLACE
    The bust and level art keep full-resolution originals in the repository and
    write reduced copies into a separate folder. That works because the
    originals earn their place: they are the source the thumbnails are made
    from. Here the extraction IS regenerable -- it comes out of the game with
    the Unity tools named in .gitignore -- so keeping both would be paying
    twice for something a re-run reproduces.

WHAT MAKES IT SAFE
    Nothing downstream is told the files got smaller. index.json records each
    sprite's textureRect, which is its original size, and VanillaUiAssets
    restores every sprite to that on the way in. Sliced borders and
    pixels-per-unit are expressed in those original pixels and keep working.

    Running this twice would otherwise halve the art again, so a file already
    smaller than its recorded size is left alone. That check is the same
    comparison the loader makes.

USAGE
    python Tools/ShrinkUiSprites.py           # do it
    python Tools/ShrinkUiSprites.py --check   # report, write nothing
"""

import argparse
import io
import json
import os
import sys

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("Pillow and NumPy are required:  pip install Pillow numpy")

HERE = os.path.dirname(os.path.abspath(__file__))
SPRITES = os.path.normpath(
    os.path.join(HERE, "..", "Resources", "VanillaOverlays", "Sprites"))

UNTOUCHED_AT_OR_BELOW = 128
HALVED_AT_OR_ABOVE = 1280


def scale_for(longest):
    """Which tier a sprite falls in, by its longest side."""
    if longest <= UNTOUCHED_AT_OR_BELOW:
        return 1.0
    return 0.5 if longest >= HALVED_AT_OR_ABOVE else 0.75


def resize(im, width, height):
    """Reduce, filtering in premultiplied alpha.

    A fully transparent pixel still carries a colour, and in an atlas crop it
    is usually black. Averaging that into an opaque neighbour is what produces
    a dark fringe around everything -- so each channel is weighted by its own
    alpha before the filter runs and unweighted afterwards. Every sprite here
    has transparency; none of them should grow a halo for being smaller.
    """
    a = np.asarray(im, dtype=np.float32)
    alpha = a[:, :, 3:4] / 255.0
    a[:, :, :3] *= alpha

    small = np.asarray(
        Image.fromarray(a.round().clip(0, 255).astype(np.uint8), "RGBA")
             .resize((width, height), Image.LANCZOS),
        dtype=np.float32)

    back = small[:, :, 3:4] / 255.0
    # Where nothing is left there is nothing to divide by, and the colour
    # underneath a zero alpha is not a colour anybody sees.
    np.divide(small[:, :, :3], np.where(back > 0, back, 1.0), out=small[:, :, :3])

    return Image.fromarray(small.round().clip(0, 255).astype(np.uint8), "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="report what would happen, write nothing")
    args = ap.parse_args()

    index_path = os.path.join(SPRITES, "index.json")
    if not os.path.exists(index_path):
        sys.exit("no extraction at %s -- run the Unity extractor first" % SPRITES)

    with io.open(index_path, encoding="utf-8") as f:
        index = json.load(f)

    # The size each sprite was extracted at. A file already below it has been
    # through here before.
    recorded = {}
    for e in index:
        rect = e.get("textureRect") or []
        if e.get("file") and len(rect) >= 4:
            recorded[e["file"]] = (int(round(rect[2])), int(round(rect[3])))

    before = after = 0
    counts = {1.0: 0, 0.75: 0, 0.5: 0}
    already = 0

    for name in sorted(os.listdir(SPRITES)):
        if not name.lower().endswith(".png"):
            continue

        path = os.path.join(SPRITES, name)
        size = os.path.getsize(path)
        before += size

        im = Image.open(path)

        # Already a palette, so already through here. Checked before the size
        # comparison because a sprite in the untouched tier is never resized
        # and would otherwise be quantised again on every run.
        if im.mode == "P":
            already += 1
            after += size
            continue

        was = recorded.get(name)
        k = 1.0 if (was and (im.width < was[0] or im.height < was[1]))             else scale_for(max(im.size))
        counts[k] += 1

        made = im.convert("RGBA")
        if k != 1.0:
            made = resize(made, max(1, round(im.width * k)),
                          max(1, round(im.height * k)))

        # FASTOCTREE because it is the only method Pillow will apply to an
        # image with an alpha channel, and every one of these has one. Text and
        # soft edges survive it; what a palette costs is smooth gradient, and
        # there is little of that in interface art.
        made = made.quantize(colors=256, method=Image.FASTOCTREE)

        if args.check:
            buf = io.BytesIO()
            made.save(buf, "PNG", optimize=True)
            after += buf.tell()
        else:
            made.save(path, "PNG", optimize=True)
            after += os.path.getsize(path)

    mb = lambda b: b / 1048576.0
    print("kept at size %d, three quarters %d, halved %d "
          "-- all of those also put on a 256-colour palette%s"
          % (counts[1.0], counts[0.75], counts[0.5],
             "; %d were already done" % already if already else ""))
    print("%.1f MB -> %.1f MB  (saves %.1f MB, %.0f%%)"
          % (mb(before), mb(after), mb(before - after),
             100.0 * (before - after) / max(1, before)))
    if args.check:
        print("(--check: nothing was written)")


if __name__ == "__main__":
    main()
