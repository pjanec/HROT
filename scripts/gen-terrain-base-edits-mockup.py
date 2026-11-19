#!/usr/bin/env python3
"""Mockup for docs/DESIGN_Terrain_Base_Edits.md (CE-5024 / CE-5025): the Terrain toolbar's BUILDINGS family — remove a baked-in building,
make a solid one enterable, add a door — on the 2-D map, and the edit's Details. Built on scripts/mockup_kit.py (R-4143).

    python3 scripts/gen-terrain-base-edits-mockup.py      # -> docs/UX/terrain-base-edits.svg
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, text_width

HOUSE = "#E1CDA0"; HOUSE_EDGE = "#6E5A3A"; SOLID = "#B9A88A"; GROUND = "#3B5131"; FLAT = "#4A5C36"; ROAD = "#E8D58A"
ARMED = "#338C80"; HOVER = "#F2C033"; GHOST = "#C8C8D0"
HOUSE_G = 0xf015; BUILDING_G = 0xf1ad; TRASH_G = 0xf1f8; DOOR_G = 0xf52b; WAND_G = 0xf0d0; UNDO_G = FA.ROTATE_LEFT

s = Svg(1240, 880, "Terrain toolbar — Buildings: remove · make enterable · add door  (CE-5024 / CE-5025)",
        "Annotations in grey are not drawn by the app. ⭐ new · everything else on the toolbar is unchanged (DESIGN_Terrain_Editor_UX §6, as built).")


def poly(pts, fill, stroke=None, sw=1, dash=None, op=1.0):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}{d}/>")


def tool_btn(x, y, glyph, gc, label, armed=False, flyout=False):
    w = 34 + text_width(label, 12) + (14 if flyout else 0)
    s.rect(x, y, w, 24, ARMED if armed else C.BTN, rx=3, stroke="#F29B38" if armed else None, sw=1.4)
    s.glyph(glyph, x + 8, y + 5, gc, 13); s.text(x + 28, y + 16, label, C.TXT, 12)
    if flyout: s.raw(f"<path d='M{x + w - 12} {y + 10} l3.5 4 l3.5 -4 z' fill='{C.SUB}'/>")
    return w


def tip(x, y, w, lines):
    """A tooltip whose lines carry their own colour: [(text, colour)]."""
    s.rect(x, y, w, 10 + 16 * len(lines), C.POPUP, stroke=C.SEP, rx=3)
    for i, (t, c) in enumerate(lines): s.text(x + 8, y + 16 + i * 16, t, c, 12)


def map_frame(x, y, w, h, title):
    s.rect(x, y, w, h, GROUND, stroke=C.BORDER); s.rect(x, y, w, 20, "#232328"); s.text(x + 8, y + 14, title, C.SUB, 11.5)
    s.rect(x, y + h / 2 + 30, w, 18, ROAD, op=0.85)          # a road along the frame


def house(x, y, w, h, fill=HOUSE, stroke=HOUSE_EDGE, sw=1, dash=None):
    poly([(x, y), (x + w, y), (x + w, y + h), (x, y + h)], fill, stroke, sw, dash)


# ── ① the toolbar: the BUILDINGS family ───────────────────────────────────────────────────────────────────────────────────────────
s.window(20, 64, 1200, 104, "Editor Toolbar — Terrain", chip=("terrain unlocked", C.TXT, ARMED))
x = 32
for g, c, lab in ((0xf1bb, C.GREEN, "Wood"), (0xf1bb, C.GREEN, "Trees"), (0xf0c8, C.SUB, "Walls"), (0xf0e7, C.ORANGE, "Obstacles"),
                  (0xf6e3, C.AMBER, "Earthworks"), (0xf773, C.BLUE, "Water"), (0xf0d0, C.VIOLET, "Scatter")):
    x += tool_btn(x, 94, g, c, lab, flyout=True) + 4
bx = x + 10
bw = tool_btn(bx, 94, BUILDING_G, C.AMBER, "Buildings", armed=True, flyout=True)
s.text(bx - 2, 134, "⭐ new family", C.AMBER, 11)
# its flyout
fx, fy = bx, 122
s.rect(fx, fy, 250, 92, C.POPUP, stroke=C.ACC, rx=4)
for i, (g, gc, lab, key) in enumerate(((TRASH_G, C.RED, "Remove building", "flat ground, its doors and damage go"),
                                       (WAND_G, C.GREEN, "Make enterable", "floors · stairs · windows · ≥ 1 door"),
                                       (DOOR_G, C.TEAL, "Add door", "click a wall of a doorless house"))):
    ry = fy + 6 + i * 28
    if i == 1: s.rect(fx + 4, ry - 2, 242, 26, C.BTNSEL, rx=3)
    s.glyph(g, fx + 10, ry + 3, gc, 13); s.text(fx + 30, ry + 13, lab, C.TXT, 12); s.text(fx + 30, ry + 23, key, C.NOTE, 9.5)
s.note(32, 150, ["an armed tool, then a click on a BAKED building (one that came with the terrain);",
                 "the cursor highlights the one it would act on; Esc disarms (UX_Feature_Tool_Model, unchanged)"])

# ── ② make enterable: hover → the result ──────────────────────────────────────────────────────────────────────────────────────────
map_frame(20, 230, 590, 300, "2-D map · Make enterable armed")
house(70, 300, 70, 54)                                                    # an enterable house (drawn as today)
house(250, 290, 60, 46, fill=SOLID, stroke=HOVER, sw=3)                  # the SOLID one under the cursor
s.raw("<path d='M300 330 l9 13 l3 -6 l6 1 z' fill='#FFFFFF' stroke='#000' stroke-width='0.8'/>")   # cursor
tip(320, 296, 250, [("Baked building · way/48211211", C.TXT), ("SOLID — no interior · 1 storey · 22 m² · shed", C.SUB),
                          ("click: make it enterable", C.GREEN)])
# after the click (right inset): the generated template
s.text(380, 392, "after the click ▸", C.NOTE, 11)
ax, ay = 470, 372
house(ax, ay, 60, 46, fill="#D8C49A")
for (x1, y1, x2, y2) in ((ax + 22, ay + 46, ax + 36, ay + 46),):            # the door gap facing the road
    s.line(x1, y1, x2, y2, C.GREEN, 4)
for wx in (ax + 6, ax + 44): s.line(wx, ay, wx + 10, ay, "#9FD7FF", 3)    # windows: real openings
s.line(ax, ay + 14, ax, ay + 28, "#9FD7FF", 3)
s.text(ax - 40, ay - 8, "door (green) · windows (blue) · 1 floor", C.SUB, 10.5)
s.note(40, 474, ["⭐ the SAME generator as the importer (BuildingTemplateGenerator): floors, a stair",
                 "per storey pair, windows as wall openings, and ≥ 1 door on the wall facing the",
                 "nearest road — the rule the importer then also guarantees (D-6)"])

# ── ③ remove: the ghost of a removed building ─────────────────────────────────────────────────────────────────────────────────────
map_frame(630, 230, 590, 300, "2-D map · Remove building (done), toolbar open")
house(680, 300, 70, 54)
poly([(860, 290), (930, 290), (930, 346), (860, 346)], FLAT, None)       # the flattened ground
house(860, 290, 70, 56, fill="none", stroke=GHOST, sw=1.5, dash="5 4")   # its ghost — drawn ONLY while the toolbar is open
s.chip(864, 352, "removed", glyph=TRASH_G, gc=C.RED, arrow=False)
s.menu(945, 300, 230, [("Restore building", "", UNDO_G, C.AMBER, None), ("Select the edit", "", 0xf05a, C.INFO, None)])
s.note(650, 470, ["the ground under it is LEVELLED to its floor height (a skirt blends it back);",
                  "its doors and its damage are gone with it; closing the toolbar hides the ghost;",
                  "Restore = delete the edit (one undo step either way)"])

# ── ④ Details of an edit ──────────────────────────────────────────────────────────────────────────────────────────────────────────
s.window(20, 560, 590, 300, "Details — Building edit 3")
s.tabs(30, 588, 570, [("Edit", BUILDING_G, C.AMBER)])
rows = (("Action", "Remove building", TRASH_G, C.RED), ("Building", "way/48211211  (baked)", HOUSE_G, C.SUB),
        ("Ground", "levelled to 312.4 m", None, None), ("Goes with it", "2 doors · its damage (1 breach)", DOOR_G, C.TEAL))
for i, (k, v, g, gc) in enumerate(rows):
    ry = 622 + i * 26
    s.text(40, ry + 14, k, C.SUB, 12)
    s.field(170, ry, 410, ("    " if g else "") + v)
    if g: s.glyph(g, 175, ry + 3, gc, 12)
s.button(170, 734, 160, "Restore building", glyph=UNDO_G, gc=C.AMBER)
s.button(340, 734, 150, "Show on map", glyph=0xf3c5, gc=C.INFO)
s.note(40, 784, ["an edit is an ordinary scenario terrain feature (kind Edit): listed, selectable, undoable, saved with the",
                 "scenario; 'Save terrain as' bakes it into the terrain (the building leaves world.json for good)"])

# ── ⑤ add door ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
map_frame(630, 560, 590, 300, "2-D map · Add door armed")
house(720, 620, 120, 60)                                                  # a terrace's middle house — no door
house(660, 620, 60, 60); house(840, 620, 60, 60)
s.line(760, 680, 790, 680, C.TEAL, 4, dash="4 3")                         # the door preview on the clicked wall
s.raw("<path d='M778 690 l9 13 l3 -6 l6 1 z' fill='#FFFFFF' stroke='#000' stroke-width='0.8'/>")
tip(800, 700, 300, [("Baked building · b-1907 · NO DOOR", C.TXT), ("click a free wall to put a door there", C.TEAL),
                          ("(a wall it shares with a neighbour is refused)", C.NOTE)])
s.note(650, 790, ["a door is placed where the wall is free; the house is regenerated by the",
                  "same generator with that door hint (its windows placed again by the same rule)"])

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "terrain-base-edits.svg")
s.save(out)
print("wrote", os.path.normpath(out))
