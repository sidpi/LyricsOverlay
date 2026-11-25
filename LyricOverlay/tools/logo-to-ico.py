"""Generate the Windows icon (and the studio's logo) from Assets/logo.png.

Usage:  python tools/logo-to-ico.py        (or: npm run icon, from LyricsOverlay/)

Drop the artwork at LyricOverlay/Assets/logo.png (square PNG, ideally 1024x1024 or 512x512).
The script centre-crops it to a square and writes:

  LyricOverlay/Assets/app.ico        multi-size icon: 16, 24, 32, 48, 64, 128, 256
  LyricsOverlay/public/logo.png      512x512 copy used by the studio header and favicon

Frames are written as uncompressed 32-bit DIBs on purpose: System.Drawing.Icon (the tray
icon and the WPF window icons) cannot decode PNG-compressed ICO frames.
"""

import struct
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets" / "logo.png"
ICON = ROOT / "Assets" / "app.ico"
STUDIO_LOGO = ROOT.parent / "LyricsOverlay" / "public" / "logo.png"
SIZES = [16, 24, 32, 48, 64, 128, 256]


def square(image: Image.Image) -> Image.Image:
    width, height = image.size
    side = min(width, height)
    left = (width - side) // 2
    top = (height - side) // 2
    return image.crop((left, top, left + side, top + side))


def dib_frame(image: Image.Image) -> bytes:
    width, height = image.size
    pixels = image.load()
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)

    rows = []
    masks = []
    stride = ((width + 31) // 32) * 4
    for y in range(height - 1, -1, -1):
        row = bytearray()
        mask = bytearray(stride)
        for x in range(width):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
            if a == 0:
                mask[x // 8] |= 0x80 >> (x % 8)
        rows.append(bytes(row))
        masks.append(bytes(mask))

    return header + b"".join(rows) + b"".join(masks)


def write_icon(source: Image.Image) -> None:
    frames = [(size, dib_frame(source.resize((size, size), Image.LANCZOS))) for size in SIZES]

    offset = 6 + 16 * len(frames)
    directories = b""
    payload = b""
    for size, blob in frames:
        directories += struct.pack(
            "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset
        )
        offset += len(blob)
        payload += blob

    ICON.write_bytes(struct.pack("<HHH", 0, 1, len(frames)) + directories + payload)


def main() -> int:
    if not SOURCE.exists():
        print(f"missing artwork: {SOURCE}", file=sys.stderr)
        return 1

    image = square(Image.open(SOURCE).convert("RGBA"))
    write_icon(image)

    STUDIO_LOGO.parent.mkdir(parents=True, exist_ok=True)
    image.resize((512, 512), Image.LANCZOS).save(STUDIO_LOGO)

    print(f"wrote {ICON} ({len(SIZES)} sizes)")
    print(f"wrote {STUDIO_LOGO}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
