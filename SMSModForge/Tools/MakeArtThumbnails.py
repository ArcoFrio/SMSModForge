#!/usr/bin/env python3
"""Generate the shipped, downscaled copy of the vanilla art.

WHY
    The extracted vanilla art is 461 MB. It exists so the editor can preview
    what a pack borrows -- a bust an actor points at, the level a vanilla
    extension attaches to -- and nothing else reads it: the pack plugin never
    touches it, and a pack builds, validates and runs identically without it.
    Shipping half a gigabyte of somebody else's artwork to make a preview
    sharper is a poor trade, so the build ships quarter-scale copies instead.
    Recognisable at a glance, useless as source art.

    The full-resolution originals stay in the repository for reference. This
    script is the bridge between them: run it whenever the art is re-extracted.

WHY A UNIFORM RATIO AND NOT A TARGET SIZE
    The _extra folders hold 277 distinct image sizes, from a 223x51 sign to a
    full 2048x1148 backdrop, and the preview lays them out against each other
    by their pixel dimensions. Resizing each to some fixed target would destroy
    every relationship between them -- the sign would come out the size of the
    room. One ratio applied to everything preserves all of it automatically.

    Downstream, world size is computed as PixelWidth / ppu, so a uniform k
    cancels exactly: scale the art by k and the preview compensates by loading
    it back at its recorded original size. sizes.json carries those originals,
    because integer division is lossy -- 223/4 is 55, and 55*4 is 220, not 223.

USAGE
    python Tools/MakeArtThumbnails.py            # write Resources/VanillaArtThumbs
    python Tools/MakeArtThumbnails.py --scale 8  # eighth-scale instead
    python Tools/MakeArtThumbnails.py --check    # report sizes, write nothing
"""

import argparse
import io
import json
import os
import re
import shutil
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is required:  pip install Pillow")

# Source folders, and the output folder name each keeps. The names are
# deliberately unchanged: the csproj links them to the same place in the build
# output, so every resolver in the editor keeps working untouched.
SOURCES = ["VanillaBustArt", "VanillaLevelArt"]

# How each set is resampled on the way DOWN, which is where detail is actually
# lost -- point-sampling on the way back up cannot recover what a smoothing
# filter discarded.
#
#   busts   NEAREST. A bust is upscaled again for display, so it wants hard
#           pixels: visibly low resolution rather than softened. A smooth
#           downscale followed by a point upscale gives the worst of both --
#           blurred art with blocky edges.
#   levels  LANCZOS. A level is only ever viewed SMALLER than the thumbnail, so
#           it is never upscaled and never shows its pixels. Point-sampling a
#           4x reduction of detailed art throws away fifteen pixels in sixteen
#           and shimmers; a proper filter keeps it clean at the size it is
#           actually seen.
FILTERS = {"VanillaBustArt": "nearest", "VanillaLevelArt": "lanczos"}
OUT = "VanillaArtThumbs"

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.normpath(os.path.join(HERE, "..", "Resources"))
CATALOG = os.path.normpath(os.path.join(HERE, "..", "Model", "VanillaBusts.cs"))


def catalogued_busts():
    """The bust names the editor actually offers.

    Art is shipped ONLY for these. Some busts exist in the game's files without
    any content that shows them -- unreleased work -- and those were taken out
    of the catalog deliberately. Removing the NAME while still shipping the
    ARTWORK would be worse than leaving both: the tool would stop advertising
    them and go on distributing them.

    Read from the catalog rather than from a list kept here, so the two cannot
    drift apart and so the excluded names appear nowhere in this repository.
    Drop a bust from VanillaBusts.cs and its art stops shipping on the next run.
    """
    src = io.open(CATALOG, encoding="utf-8-sig").read()
    names = set(re.findall(r'new\("([^"]+)"', src))
    if not names:
        sys.exit("could not read any bust names from %s -- refusing to ship "
                 "art with no catalog to check it against" % CATALOG)
    return names


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scale", type=float, default=4,
                    help="divide every dimension by this (default 4; may be fractional)")
    # Per-folder overrides. The two sets are displayed very differently: a bust
    # is drawn into a fixed 256x256 frame, so it is upscaled again on load and
    # every halving is visible, while a level is only ever shown small. Busts
    # are also a tenth of the bytes, so buying quality there is cheap.
    ap.add_argument("--scale-bust", type=float, default=None,
                    help="override --scale for VanillaBustArt")
    ap.add_argument("--scale-level", type=float, default=None,
                    help="override --scale for VanillaLevelArt")
    ap.add_argument("--filter", choices=("nearest", "lanczos"), default=None,
                    help="override the per-folder resampling filter for both sets")
    ap.add_argument("--check", action="store_true",
                    help="report what would happen, write nothing")
    args = ap.parse_args()
    per_source = {
        "VanillaBustArt": args.scale_bust or args.scale,
        "VanillaLevelArt": args.scale_level or args.scale,
    }
    if any(v < 1 for v in per_source.values()):
        sys.exit("scales must be 1 or more")
    filters = {n: (args.filter or FILTERS[n]) for n in SOURCES}
    RESAMPLE = {"nearest": Image.NEAREST, "lanczos": Image.LANCZOS}

    out_root = os.path.join(RES, OUT)
    sizes = {}
    stats = {"png": 0, "copied": 0, "src_bytes": 0, "out_bytes": 0, "skipped": 0}

    allowed = catalogued_busts()
    print("catalogued busts: %d" % len(allowed))
    excluded = 0

    for src_name in SOURCES:
        src_root = os.path.join(RES, src_name)
        if not os.path.isdir(src_root):
            print("  (missing, skipped) %s" % src_name)
            continue

        k = per_source[src_name]
        resample = RESAMPLE[filters[src_name]]
        for dirpath, _, files in os.walk(src_root):
            # A bust's art lives in a folder named after it. One not in the
            # catalog is one the editor will never offer, so its art has no
            # reason to be in the build.
            if src_name == "VanillaBustArt":
                rel_dir = os.path.relpath(dirpath, src_root).replace("\\", "/")
                if rel_dir != ".":
                    bust = rel_dir.split("/")[0]
                    if bust not in allowed:
                        excluded += 1
                        continue

            for fn in files:
                abs_in = os.path.join(dirpath, fn)
                rel = os.path.relpath(abs_in, RES).replace("\\", "/")
                stats["src_bytes"] += os.path.getsize(abs_in)

                abs_out = os.path.join(out_root, os.path.relpath(abs_in, RES))
                if not args.check:
                    os.makedirs(os.path.dirname(abs_out), exist_ok=True)

                if fn.lower().endswith(".png"):
                    try:
                        im = Image.open(abs_in)
                        w, h = im.size
                        # Recorded before resizing: the preview restores the
                        # image to this, and it cannot be recovered from the
                        # thumbnail because the division rounds down.
                        sizes[rel_out(rel)] = [w, h]
                        nw, nh = max(1, int(round(w / k))), max(1, int(round(h / k)))
                        if not args.check:
                            im = im.convert("RGBA")
                            im.resize((nw, nh), resample).save(
                                abs_out, "PNG", optimize=True)
                            stats["out_bytes"] += os.path.getsize(abs_out)
                        stats["png"] += 1
                    except Exception as ex:
                        stats["skipped"] += 1
                        print("  !! %s: %s" % (rel, ex))
                else:
                    # Jiggle.txt and vanilla_levels.json are data, not art, and
                    # both are read at run time. They go across untouched.
                    if not args.check:
                        shutil.copy2(abs_in, abs_out)
                        stats["out_bytes"] += os.path.getsize(abs_out)
                    stats["copied"] += 1

    if not args.check:
        with open(os.path.join(out_root, "sizes.json"), "w", encoding="utf-8") as f:
            json.dump({"scale": per_source, "filter": filters, "originals": sizes},
                      f, indent=0, sort_keys=True)

    if excluded:
        print("excluded %d bust folder(s) absent from the catalog" % excluded)

    mb = lambda b: b / 1048576.0
    print("\n%d images rescaled (%s), %d data files copied, %d failed"
          % (stats["png"],
             ", ".join("%s 1/%g %s" % (n, v, filters[n]) for n, v in per_source.items()),
             stats["copied"], stats["skipped"]))
    print("source  %7.1f MB" % mb(stats["src_bytes"]))
    if not args.check:
        print("shipped %7.1f MB  (%.1f%%)"
              % (mb(stats["out_bytes"]),
                 100.0 * stats["out_bytes"] / max(1, stats["src_bytes"])))
        print("\nwrote %s" % out_root)


def rel_out(rel):
    """Key as the editor will see it at run time: the path below the output
    folder, e.g. 'VanillaLevelArt/3_LivingRoom/Base.PNG'."""
    return rel


if __name__ == "__main__":
    main()
