#!/usr/bin/env python3
"""CE-1017 S5 - rasterize the Add Entity picker's entity icons from their SVG sources.

Source of truth: Hrot/Engine/Hrot.Presentation/Assets/EntityIcons/src/<name>.svg (hand-drawn, 128x128 viewBox,
side profile facing right; edit them in any SVG editor). Output: ../<name>.png at 128 px, embedded into
Hrot.Presentation and served by EntityIconLibrary as entity/<name> (the picker draws rows at 16 px and the
preview pane at 64 px, so 128 stays crisp on HiDPI too).

Usage:  pip install cairosvg   (needs libcairo)
        python3 scripts/render-entity-icons.py [--sheet out.png]   # --sheet also writes a contact sheet
"""
import argparse
import glob
import os
import sys

import cairosvg

HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.normpath(os.path.join(HERE, "..", "Hrot", "Engine", "Hrot.Presentation", "Assets", "EntityIcons"))
SIZE = 128


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet", help="also write a contact sheet PNG (dark background, 64 px and 16 px rows)")
    args = ap.parse_args()

    sources = sorted(glob.glob(os.path.join(ICONS, "src", "*.svg")))
    if not sources:
        sys.exit(f"no SVG sources under {ICONS}/src")
    for svg in sources:
        name = os.path.splitext(os.path.basename(svg))[0]
        cairosvg.svg2png(url=svg, write_to=os.path.join(ICONS, name + ".png"), output_width=SIZE, output_height=SIZE)
    print(f"{len(sources)} icons -> {ICONS}")

    if args.sheet:
        from PIL import Image
        cell = 80
        sheet = Image.new("RGBA", (cell * len(sources), cell + 28), (40, 44, 52, 255))
        for i, svg in enumerate(sources):
            img = Image.open(os.path.join(ICONS, os.path.splitext(os.path.basename(svg))[0] + ".png"))
            big, small = img.resize((64, 64), Image.LANCZOS), img.resize((16, 16), Image.LANCZOS)
            sheet.paste(big, (i * cell + 8, 8), big)
            sheet.paste(small, (i * cell + 32, cell + 4), small)
        sheet.save(args.sheet)
        print("sheet ->", args.sheet)


if __name__ == "__main__":
    main()
