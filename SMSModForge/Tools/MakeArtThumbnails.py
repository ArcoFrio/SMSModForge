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
#   busts   BICUBIC. This used to be NEAREST, on the reasoning that a bust is
#           upscaled again for display and so wants hard pixels. That was
#           wrong, and looking at the result is what settled it: the ratio is
#           1.5, not an integer, so point-sampling drops every third row at
#           uneven intervals. It does not read as "low resolution art" -- it
#           deforms faces. Eyes end up different sizes.
#
#           The argument against smoothing was that a soft downscale followed
#           by a point upscale gives the worst of both. That was a fair
#           objection, and the answer is that the upscale no longer point-
#           samples either: VanillaArtSizes interpolates on the way back up,
#           so both halves of the pipeline agree.
#   levels  LANCZOS. A level is only ever viewed SMALLER than the thumbnail, so
#           it is never upscaled and never shows its pixels. Point-sampling a
#           4x reduction of detailed art throws away fifteen pixels in sixteen
#           and shimmers; a proper filter keeps it clean at the size it is
#           actually seen.
FILTERS = {"VanillaBustArt": "bicubic", "VanillaLevelArt": "lanczos"}

# Which sets are reduced to a 256-colour palette on the way out, and which are
# not. This is the one lossy step here that is not about size on screen -- it is
# about size on disk, and it buys a lot: a level layer comes out between a sixth
# and a half of its 24-bit weight.
#
#   levels  YES. Flat, illustrated backgrounds with large areas of one colour,
#           which is what a palette is good at. Compared side by side the two
#           are hard to tell apart; what suffers is smooth gradients, and the
#           worst of those is a sky reading as bands rather than a wash.
#   busts   NO. A character's face is the thing an author looks at most closely,
#           the art is already the smallest of the three sets, and banding on
#           skin is exactly where 256 colours shows.
#
# NPCs are excluded too, wherever they sit: they are people drawn at the same
# scrutiny as a bust, and they live inside the level folders rather than beside
# them. See NPC_FOLDER.
QUANTIZE = {"VanillaBustArt": False, "VanillaLevelArt": True}

# Anything under a folder starting with this, or a file starting with it, is
# somebody rather than somewhere.
NPC_PREFIX = "npc"
OUT = "VanillaArtThumbs"

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.normpath(os.path.join(HERE, "..", "Resources"))
# The names live in Shared/, compiled into the runtime plugin as well.
# They used to be in Model/VanillaBusts.cs, which now only projects them --
# so this pointed at a file with no names in it and the guard below would
# have refused to run. That is the right way round for a guard to fail, but
# it does mean nobody had re-run this since the move.
CATALOG = os.path.normpath(
    os.path.join(HERE, "..", "..", "Shared", "VanillaCastData.cs"))


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
    names = set(re.findall(r'new VanillaBust\("([^"]+)"', src))
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
    # Defaulted rather than left to whoever runs this. The bust scale was
    # being passed on the command line, which meant the number lived in
    # somebody's shell history: a re-run without it would have quietly shipped
    # quarter-scale busts. 1.25 is gentle enough that a face survives the round
    # trip and back, and busts are a tenth of the bytes of the level art, so
    # buying quality here is cheap.
    ap.add_argument("--scale-bust", type=float, default=1.25,
                    help="override --scale for VanillaBustArt (default 1.25)")
    ap.add_argument("--scale-level", type=float, default=None,
                    help="override --scale for VanillaLevelArt")
    ap.add_argument("--filter", choices=("nearest", "bicubic", "lanczos"), default=None,
                    help="override the per-folder resampling filter for both sets")
    ap.add_argument("--only", choices=tuple(SOURCES), default=None,
                    help="rescale just this set, leaving the other one on disk "
                         "as it is (sizes.json is merged, not replaced)")
    ap.add_argument("--check", action="store_true",
                    help="report what would happen, write nothing")
    args = ap.parse_args()
    per_source = {
        "VanillaBustArt": args.scale_bust,
        "VanillaLevelArt": args.scale_level or args.scale,
    }
    if any(v < 1 for v in per_source.values()):
        sys.exit("scales must be 1 or more")
    filters = {n: (args.filter or FILTERS[n]) for n in SOURCES}
    RESAMPLE = {"nearest": Image.NEAREST,
                "bicubic": Image.BICUBIC,
                "lanczos": Image.LANCZOS}

    out_root = os.path.join(RES, OUT)
    sizes = {}
    stats = {"png": 0, "copied": 0, "src_bytes": 0, "out_bytes": 0, "skipped": 0}

    allowed = catalogued_busts()
    print("catalogued busts: %d" % len(allowed))
    excluded = 0

    for src_name in SOURCES:
        if args.only and src_name != args.only:
            print("  (left as it is) %s" % src_name)
            continue

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
                        # A person, not a place: left in full colour wherever
                        # the extraction happened to put them.
                        parts = os.path.relpath(abs_in, src_root).replace("\\", "/").split("/")
                        is_npc = any(part.lower().startswith(NPC_PREFIX) for part in parts)
                        w, h = im.size
                        # Recorded before resizing: the preview restores the
                        # image to this, and it cannot be recovered from the
                        # thumbnail because the division rounds down.
                        sizes[rel_out(rel)] = [w, h]
                        nw, nh = max(1, int(round(w / k))), max(1, int(round(h / k)))
                        if not args.check:
                            im = im.convert("RGBA")
                            made = im.resize((nw, nh), resample)
                            if QUANTIZE.get(src_name) and not is_npc:
                                # FASTOCTREE because it is the only method
                                # Pillow will apply to an image with an alpha
                                # channel, and these have one.
                                made = made.quantize(colors=256,
                                                     method=Image.FASTOCTREE)
                            made.save(abs_out, "PNG", optimize=True)
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
        manifest = {"scale": dict(per_source), "filter": dict(filters),
                    "palette": {n: bool(QUANTIZE.get(n)) for n in SOURCES},
                    "originals": sizes}

        # Rescaling one set must not wipe the other set's recorded sizes: the
        # preview restores every thumbnail through this file, and a missing
        # entry means art drawn at a quarter of its size with nothing to say
        # why.
        if args.only:
            existing = os.path.join(out_root, "sizes.json")
            if os.path.exists(existing):
                with open(existing, encoding="utf-8") as f:
                    was = json.load(f)
                merged = dict(was.get("originals", {}))
                merged.update(sizes)
                manifest["originals"] = merged
                for key in ("scale", "filter", "palette"):
                    kept = dict(was.get(key, {}))
                    kept[args.only] = manifest[key][args.only]
                    manifest[key] = kept

        with open(os.path.join(out_root, "sizes.json"), "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=0, sort_keys=True)

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
