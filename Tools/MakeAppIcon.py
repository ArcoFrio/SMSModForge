"""Build the editor's multi-resolution .ico from Resources/SMSModForge.png.

Why this exists
---------------
A .ico is a CONTAINER, not an image. Windows asks it for a specific size and
draws whatever is closest, scaling if it has to. The icon shipped in 1.0.0 and
1.1.0 held exactly one 32x32 frame, so every place Windows wants something
bigger - the taskbar on a high-DPI display, Alt-Tab, Large Icons in Explorer,
the Programs and Features list - was upscaling 32 pixels to 256. That is the
blur, and no amount of re-exporting a single frame fixes it.

So this writes every size Windows actually asks for, each resampled from the
512x512 source rather than from another downscale.

Encoding: BMP frames up to 64, PNG frames at 128 and 256. That split is what
icon editors conventionally produce - PNG compression is what keeps a 256x256
frame from costing 256 KB on its own, and Windows has understood PNG frames
since Vista, but the small sizes stay in the format every tool reads.

Usage
-----
    python Tools/MakeAppIcon.py

Reads  SMSModForge/Resources/SMSModForge.png
Writes SMSModForge/Resources/SMSModForge.ico

Re-run it whenever the source art changes. The PNG is the master; the .ico is
generated and should never be hand-edited.
"""

import io
import os
import struct
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is needed: python -m pip install Pillow")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(REPO, "SMSModForge", "Resources", "SMSModForge.png")
DST = os.path.join(REPO, "SMSModForge", "Resources", "SMSModForge.ico")

# Every size Windows asks for. 16 title bar / small icons, 24 some shell views,
# 32 taskbar and medium icons, 48 Alt-Tab and large icons, 64 and 128 for
# high-DPI variants of those, 256 for extra-large and the properties dialog.
SIZES = [16, 24, 32, 48, 64, 128, 256]

# Above this, store the frame as PNG.
PNG_FROM = 128


def dib_frame(img):
    """A 32-bit BMP frame, in the shape an .ico expects.

    Two things differ from an ordinary .bmp: the header's height is DOUBLED
    (it covers the colour rows and the AND-mask rows that follow), and there is
    no BITMAPFILEHEADER. The AND mask is vestigial for a 32-bit frame - the
    alpha channel already carries the transparency - but Windows still expects
    the rows to be there, so it is written as all-zero (fully opaque) and
    padded to the 4-byte row alignment BMP requires.
    """
    w, h = img.size
    px = img.load()

    rows = []
    for y in range(h - 1, -1, -1):          # BMP rows run bottom-up
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))      # BGRA, not RGBA
        rows.append(bytes(row))
    colour = b"".join(rows)

    mask_stride = ((w + 31) // 32) * 4      # 1bpp, rows padded to 4 bytes
    mask = b"\x00" * (mask_stride * h)

    header = struct.pack(
        "<IiiHHIIiiII",
        40,          # header size
        w, h * 2,    # width, and height covering both masks
        1, 32,       # planes, bits per pixel
        0,           # BI_RGB, uncompressed
        len(colour) + len(mask),
        0, 0, 0, 0,  # resolution and palette fields, unused here
    )
    return header + colour + mask


def png_frame(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def main():
    if not os.path.exists(SRC):
        sys.exit("source not found: " + SRC)

    source = Image.open(SRC).convert("RGBA")
    print("source: %dx%d" % source.size)
    if min(source.size) < max(SIZES):
        print("  note: source is smaller than %d, so the largest frame is an "
              "upscale" % max(SIZES))

    frames = []
    for size in SIZES:
        # LANCZOS from the ORIGINAL every time. Resizing a resize compounds the
        # softening, which is the thing this script exists to avoid.
        img = source.resize((size, size), Image.LANCZOS)
        data = png_frame(img) if size >= PNG_FROM else dib_frame(img)
        frames.append((size, data))
        print("  %3dx%-3d  %-4s %7d bytes" %
              (size, size, "PNG" if size >= PNG_FROM else "BMP", len(data)))

    out = bytearray()
    out += struct.pack("<HHH", 0, 1, len(frames))       # reserved, type 1 = icon

    offset = 6 + 16 * len(frames)
    for size, data in frames:
        out += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,   # 256 is written as 0 - one byte each
            0 if size >= 256 else size,
            0, 0,                         # palette count, reserved
            1, 32,                        # planes, bits per pixel
            len(data), offset,
        )
        offset += len(data)

    for _, data in frames:
        out += data

    with open(DST, "wb") as f:
        f.write(bytes(out))
    print("wrote %s  (%d frames, %d bytes)" % (DST, len(frames), len(out)))


if __name__ == "__main__":
    main()
