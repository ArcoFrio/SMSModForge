"""The guide's pictures into the editor (1.10.0): the PNGs GuidePictureShots and GuideSectionShots take
(SMSMODFORGE_GUIDE_SHOTS) become SMSModForge/Resources/GuidePictures/<topic>[.EX].png and, for the sections,
Sections/<section>[.EX].png.

Lossless and at the size they were taken (since 1.10.0's round 3): they were JPGs at 80 and cut to 820 wide, and
the author found them "poorly compressed" - the red boxes and the editor's small words smeared - once a picture
opened at full size to zoom into. Measured over all 384: 16 MB as PNGs, 11 MB as those JPGs, 19 MB as JPGs at 92
without colour subsampling; a JPG of the same picture is left behind and removed.

    python -I Tools/Docs/guide-pictures.py <the folder of the PNGs>
"""
import os, sys
from PIL import Image

src = sys.argv[1]
dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "SMSModForge", "Resources", "GuidePictures")
total = 0
for folder, _, files in os.walk(src):
    rel = os.path.relpath(folder, src)
    out_dir = dst if rel == "." else os.path.join(dst, rel)
    for name in sorted(files):
        if name.lower().endswith(".json"):
            # Which bullet each numbered red box is (GuideSectionShots): the reader numbers the bullets to match.
            os.makedirs(out_dir, exist_ok=True)
            with open(os.path.join(folder, name), "rb") as a, open(os.path.join(out_dir, name), "wb") as b:
                b.write(a.read())
            print(os.path.join(rel, name), "copied")
            continue
        if not name.lower().endswith(".png"): continue
        # The capture keeps see-through parts (a panel with no background of its own); they go on the look's own
        # background first, dark for EX and light for Classic, as the reader shows them.
        rgba = Image.open(os.path.join(folder, name)).convert("RGBA")
        ground = Image.new("RGBA", rgba.size, (24, 22, 30, 255) if ".EX." in name else (246, 246, 248, 255))
        im = Image.alpha_composite(ground, rgba).convert("RGB")
        os.makedirs(out_dir, exist_ok=True)
        out = os.path.join(out_dir, name)
        im.save(out, "PNG", optimize=True)
        stale = out[:-4] + ".jpg"
        if os.path.exists(stale): os.remove(stale)
        total += os.path.getsize(out)
        print(os.path.join(rel, name[:-4]), os.path.getsize(out) // 1024, "KB")
print("total", total // 1024, "KB")
