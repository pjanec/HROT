"""The ONE kit for UI mockups (R-4143): ImGui default dark, real FontAwesome 6 glyphs in their meaning's colour, neutral icon tabs,
the forms' status slot. Every mockup generator in scripts/ builds on this - never on its own copies of these helpers (R-174).

Usage:
    from mockup_kit import Svg, C, FA
    s = Svg(1240, 800, "Title", "subtitle")
    s.window(20, 60, 500, 400, "Details — Alpha 1-1", tabs=[("Unit", FA.USER, C.BLUE), ...])
    ...
    s.save("docs/UX/x.svg")

Needs fonttools (pip install fonttools); reads FDP/Data/Fonts/fa-solid-900.ttf.
"""
import os
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen

_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
_F = TTFont(os.path.join(_ROOT, "FDP", "Data", "Fonts", "fa-solid-900.ttf"))
_G = _F.getGlyphSet(); _CMAP = _F.getBestCmap(); _UPM = _F["head"].unitsPerEm; _ASC = _F["hhea"].ascent


class C:
    """Colours. ImGui default dark (flattened over WindowBg), the status palette (Details forms §6.3), the icon hues (Colour Icons §2)."""
    PAGE = "#1B1B1F"; BG = "#0F0F0F"; TITLE = "#294A7A"; FRAME = "#1D2F48"; BTN = "#23415F"; BTNSEL = "#3369AD"; HDR = "#1C344F"
    TXT = "#FFFFFF"; DIS = "#808080"; SUB = "#C8C8D0"; NOTE = "#8A8A96"; BORDER = "#3A3A44"; SEP = "#4A4A55"; ACC = "#4296FA"
    POPUP = "#141418"; TABN = "#24242C"; TABS = "#3A3A48"; TABLINE = "#3369AD"; NEUTRAL = "#1C1C22"; NEUTRALSEL = "#34343E"
    ERR = "#E65555"; ERRROW = "#3A1A1A"; PEND = "#F2C033"; PENDROW = "#3A3112"; INFO = "#6AB0FF"; OK = "#5BD06A"
    TEAL = "#3EC6B0"; ORANGE = "#F29B38"; VIOLET = "#B07CF0"; BLUE = "#5AA9FF"; AMBER = "#F2C033"; GREEN = "#5BD06A"
    RED = "#E65555"; GREY = "#A0A0AA"; PINK = "#E8A0C8"
    SECTION = "#2B2F38"; SECTIONHOV = "#353A45"; AUTH = "#1E4A22"   # collapsing header slate (UI conventions §4) · the raw editor's authority green


class FA:
    """FontAwesome 6 solid code points used by the mockups (the app's IconsFontAwesome6 names)."""
    USER = 0xf007; USERS = 0xf0c0; VAN = 0xf5b6; ROUTE = 0xf4d7; BRAIN = 0xf5dc; SCALE = 0xf24e; PUZZLE = 0xf12e; POLYGON = 0xf5ee
    EARTH = 0xf7a2; BOLT = 0xf0e7; CODE = 0xf121; FILTER = 0xf0b0; PLAY = 0xf04b; GEAR = 0xf013; FLAG_CHECKERED = 0xf11e
    INFO = 0xf05a; LINK = 0xf0c1; CLOCK_BACK = 0xf1da; CLOCK = 0xf017; LIST_CHECK = 0xf0ae; FLAG = 0xf024; TARGET = 0xf140
    CROSSHAIRS = 0xf05b; CHECK = 0xf00c; XMARK = 0xf00d; CIRCLE_XMARK = 0xf057; CIRCLE_CHECK = 0xf058; HOURGLASS = 0xf252
    LOCK = 0xf023; ROTATE_LEFT = 0xf2ea; WARNING = 0xf071; SKULL = 0xf54c; HEART = 0xf21e; FIRE = 0xf06d; PLUS = 0xf067
    ELLIPSIS_V = 0xf142; OBJECT_GROUP = 0xf247; WAND = 0xf0d0; LOCATION = 0xf3c5; PLANE = 0xf072; HELICOPTER = 0xf533
    LAYERS = 0xf5fd; MAP = 0xf279; PEN = 0xf304; EYE = 0xf06e; GAMEPAD = 0xf11b; CHESS = 0xf439; VIDEO = 0xf03d; UNDO = 0xf0e2
    REDO = 0xf01e; TRASH = 0xf1f8; COPY = 0xf0c5; PASTE = 0xf0ea; KEYBOARD = 0xf11c; BOOKMARK = 0xf02e; MOUNTAIN = 0xf6fc
    FILE = 0xf15b; FOLDER_OPEN = 0xf07c; SEARCH = 0xf002; STAR = 0xf005; HOUSE = 0xf015; PERSON_RIFLE = 0xe54e; SHIELD = 0xf3ed
    BULLHORN = 0xf0a1; CROWN = 0xf521; USER_SECRET = 0xf21b; MOVE = 0xf047; ARROWS_ROTATE = 0xf021; BACKSPACE = 0xf55a
    LEVEL_UP = 0xf3bf; BINOCULARS = 0xf1e5; CUBES = 0xf1b3; SITEMAP = 0xf0e8; DRAW = 0xf5ee


def text_width(s, sz=13):
    """Roboto-ish advance estimate: good enough for laying out chips and tabs."""
    k = sz / 13
    return k * sum(8.6 if ch.isupper() else (3.6 if ch == ' ' else (4.0 if ch in "il.,:;|'!" else (9.6 if ch in "mwMW" else 6.9))) for ch in s)


class Svg:
    def __init__(self, w, h, title=None, subtitle="Annotations in grey are not drawn by the app."):
        self.w, self.h, self.o = w, h, []
        self.o.append(f"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>")
        self.rect(0, 0, w, h, C.PAGE)
        if title:
            self.text(20, 26, title, "#E8E8EE", 14)
            if subtitle: self.text(20, 44, subtitle, C.NOTE, 11.5)

    # ── primitives ──────────────────────────────────────────────────────
    def raw(self, s): self.o.append(s)

    def text(self, x, y, s, c=C.TXT, sz=13, anchor="start", weight="normal"):
        s = str(s).replace("&", "&amp;").replace("<", "&lt;").replace("  ", "\u00a0\u00a0")   # keep runs of spaces (SVG collapses them)
        self.o.append(f"<text x='{x}' y='{y}' fill='{c}' font-size='{sz}' font-family='Roboto, Segoe UI, Arial, sans-serif' text-anchor='{anchor}' font-weight='{weight}' xml:space='preserve'>{s}</text>")

    def rect(self, x, y, w, h, fill, stroke=None, rx=0, sw=1, dash=None, op=1):
        st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
        d = f" stroke-dasharray='{dash}'" if dash else ""
        self.o.append(f"<rect x='{x}' y='{y}' width='{w}' height='{h}' fill='{fill}' rx='{rx}'{st}{d} opacity='{op}'/>")

    def line(self, x1, y1, x2, y2, c=C.SEP, sw=1, dash=None):
        d = f" stroke-dasharray='{dash}'" if dash else ""
        self.o.append(f"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{c}' stroke-width='{sw}'{d}/>")

    def glyph(self, cp, x, y, color, sz=13, op=1):
        """A real FontAwesome glyph; (x, y) = top-left, sz = em size in px."""
        pen = SVGPathPen(_G); _G[_CMAP[cp]].draw(pen); k = sz / _UPM
        self.o.append(f"<path d='{pen.getCommands()}' fill='{color}' fill-opacity='{op}' transform='translate({x},{y + _ASC * k}) scale({k},{-k})'/>")

    # ── ImGui widgets ───────────────────────────────────────────────────
    def window(self, x, y, w, h, title, chip=None):
        self.rect(x, y, w, h, C.BG, stroke=C.BORDER)
        self.rect(x, y, w, 20, C.TITLE); self.text(x + 8, y + 14, title)
        if chip:   # (label, fg, bg) in the title bar, right
            lab, fg, bg = chip; cw = text_width(lab, 11) + 18
            self.rect(x + w - cw - 8, y + 2, cw, 16, bg, rx=8); self.text(x + w - 8 - cw / 2, y + 14, lab, fg, 11, "middle")

    def tabs(self, x, y, w_total, items, selected=0):
        """Neutral icon tabs (Colour Icons §3.1): items = [(label, glyph or None, colour)]."""
        cx = x
        for i, (name, g, gc) in enumerate(items):
            w = 22 + (19 if g else 0) + text_width(name)
            c = C.TABS if i == selected else C.TABN; rr = 4; h = 21
            self.o.append(f"<path d='M{cx} {y + h} L{cx} {y + rr} Q{cx} {y} {cx + rr} {y} L{cx + w - rr} {y} Q{cx + w} {y} {cx + w} {y + rr} L{cx + w} {y + h} Z' fill='{c}'/>")
            if i == selected: self.rect(cx + 2, y, w - 4, 2, C.TABLINE)
            if g: self.glyph(g, cx + 8, y + 4, gc, 12.5)
            self.text(cx + 8 + (19 if g else 0), y + 15, name); cx += w + 2
        self.rect(x, y + 21, w_total, 1.5, C.TABLINE)

    def separator_text(self, x, y, label, x_end):
        self.text(x, y + 12, label, "#9FB7D8", 13)
        self.line(x + 8 + text_width(label), y + 8, x_end, y + 8)

    def field(self, x, y, w, txt, c=C.TXT, border=None, hint=False, dis=False):
        self.rect(x, y, w, 19, C.FRAME, stroke=border, sw=1.2, op=0.55 if dis else 1)
        self.text(x + 5, y + 14, txt, C.DIS if (hint or dis) else c)

    def combo(self, x, y, w, txt, hint=False, dis=False, glyph=None, gc=None):
        self.rect(x, y, w, 19, C.FRAME, op=0.55 if dis else 1); self.rect(x + w - 19, y, 19, 19, C.BTN, op=0.55 if dis else 1)
        self.o.append(f"<path d='M{x + w - 14} {y + 7} l4.5 5 l4.5 -5 z' fill='{C.DIS if dis else C.TXT}'/>")
        tx = x + 5
        if glyph: self.glyph(glyph, x + 5, y + 3, gc, 12); tx = x + 22
        self.text(tx, y + 14, txt, C.DIS if (hint or dis) else C.TXT)

    def chip(self, x, y, label, glyph=None, gc=None, border=None, hint=False, arrow=True):
        """A sentence blank (condition editor §4): returns its width."""
        w = text_width(label) + (26 if glyph else 10) + (16 if arrow else 0)
        self.rect(x, y, w, 20, C.FRAME, stroke=border, rx=3, sw=1.3)
        tx = x + 6
        if glyph: self.glyph(glyph, x + 5, y + 3.5, gc, 12); tx = x + 21
        self.text(tx, y + 14.5, label, C.DIS if hint else C.TXT)
        if arrow: self.o.append(f"<path d='M{x + w - 12} {y + 8} l3.5 4 l3.5 -4 z' fill='{C.DIS}'/>")
        return w

    def button(self, x, y, w, txt, glyph=None, gc=None, fill=C.BTN, dis=False):
        self.rect(x, y, w, 20, fill, rx=3, op=0.55 if dis else 1)
        if glyph:
            self.glyph(glyph, x + 7, y + 3.5, gc, 12, op=0.55 if dis else 1); self.text(x + 24, y + 14.5, txt, C.DIS if dis else C.TXT)
        else:
            self.text(x + w / 2, y + 14.5, txt, C.DIS if dis else C.TXT, anchor="middle")

    def glyph_toggle(self, x, y, glyph, gc, selected):
        """A switch option (Colour Icons §2: own colour always, selected framed and full strength)."""
        self.rect(x, y, 30, 26, C.NEUTRALSEL if selected else C.NEUTRAL, stroke="#C8C8D4" if selected else "#2E2E36", rx=4, sw=1.4)
        self.glyph(glyph, x + 7.5, y + 5, gc, 15, op=1 if selected else 0.65)

    def checkbox(self, x, y, on, dis=False, mixed=False):
        self.rect(x, y, 19, 19, C.FRAME, op=0.55 if dis else 1)
        if on: self.o.append(f"<path d='M{x + 4} {y + 10} l4 4 l7 -9' stroke='{C.ACC}' stroke-width='2.2' fill='none'/>")
        if mixed: self.rect(x + 5, y + 8.5, 9, 2.2, C.ACC)

    def slider(self, x, y, w, val, label, dis=False):
        self.rect(x, y, w, 19, C.FRAME, op=0.55 if dis else 1)
        gx = x + 2 + val * (w - 14); self.rect(gx, y + 2, 10, 15, C.ACC if not dis else "#3B4C63", rx=1)
        self.text(x + w / 2, y + 14, label, C.DIS if dis else C.TXT, anchor="middle")

    def menu(self, x, y, w, items):
        """A popup menu: items = [(label, shortcut, glyph, gc, disabled_reason)] or "---" for a separator or ("#", header)."""
        h = 8 + sum(9 if it == "---" else 22 for it in items)
        self.rect(x, y, w, h, C.POPUP, stroke=C.SEP, rx=3); cy = y + 4
        for it in items:
            if it == "---": self.line(x + 6, cy + 4, x + w - 6, cy + 4); cy += 9; continue
            if it[0] == "#": self.text(x + 10, cy + 15, it[1], C.NOTE, 11.5); cy += 22; continue
            label, sc, g, gc, why = (list(it) + [None] * 5)[:5]
            dis = why is not None
            if g: self.glyph(g, x + 10, cy + 4, gc, 12.5, op=0.45 if dis else 1)
            self.text(x + 30, cy + 15, label, C.DIS if dis else C.TXT)
            if sc: self.text(x + w - 10, cy + 15, sc, C.DIS, 12, "end")
            if dis and why: self.text(x + 30 + text_width(label) + 10, cy + 15, "— " + why, "#6E6E78", 11)
            cy += 22
        return h

    def chevron(self, x, y, open_, c=C.TXT):
        """The ONE tree chevron (UI conventions §2): ImGui's own filled tree arrow, 10 px, at (x, y) = its box's top-left."""
        d = f"M{x} {y + 2} l10 0 -5 6 z" if open_ else f"M{x + 2} {y} l6 5 -6 5 z"
        self.o.append(f"<path d='{d}' fill='{c}'/>")

    def tree_row(self, x, y, depth, label, glyph=None, gc=None, open_=None, sel=False, sub=None, w=330, indent=18):
        """One tree row (UI conventions §2): CHEVRON · ICON · LABEL. open_=None is a leaf (no chevron, its slot kept,
        so every label at one depth starts at one x). Returns the row height."""
        cx = x + depth * indent
        if sel: self.rect(x, y - 2, w, 22, "#2C3A52", rx=2)
        if open_ is not None: self.chevron(cx, y + 3, open_)
        gx = cx + 15
        if glyph: self.glyph(glyph, gx, y + 2, gc, 12.5)
        self.text(gx + (19 if glyph else 0), y + 14, label)
        if sub: self.text(x + w - 6, y + 14, sub, C.DIS, 11.5, "end")
        return 22

    def header(self, x, y, w, label, glyph=None, gc=None, open_=True, fill=None):
        """A collapsing header (UI conventions §4): slate, not the selection blue; CHEVRON · coloured ICON · LABEL."""
        self.rect(x, y, w, 21, fill or C.SECTION, rx=2)
        self.chevron(x + 6, y + 5, open_)
        if glyph: self.glyph(glyph, x + 22, y + 4, gc, 12.5)
        self.text(x + 22 + (19 if glyph else 0), y + 15, label)
        return 21

    def form_row(self, x, y, label, label_w, ctrl, *a, **k):
        """A label-first row (UI conventions §3): label in column 1, the control at x + label_w in column 2."""
        self.text(x, y + 14, label, C.TXT)
        getattr(self, ctrl)(x + label_w, y, *a, **k)

    def tooltip(self, x, y, w, lines, edge=C.SEP):
        h = 10 + 16 * len(lines); self.rect(x, y, w, h, C.POPUP, stroke=edge, rx=3)
        for i, l in enumerate(lines): self.text(x + 8, y + 16 + i * 16, l, sz=12)

    def notification(self, x, y, w, col, glyph, title, body, buttons=()):
        self.rect(x, y, w, 62, "#18181C", stroke=C.BORDER, rx=3); self.rect(x, y, 4, 62, col)
        self.glyph(glyph, x + 14, y + 9, col, 13); self.text(x + 34, y + 20, title, C.TXT, 13); self.text(x + 34, y + 37, body, C.SUB, 12)
        bx = x + 34
        for b in buttons:
            bw = 14 + text_width(b, 12); self.button(bx, y + 41, bw, b); bx += bw + 6
        self.text(x + w - 14, y + 18, "×", C.DIS, 16, "middle")

    def callout(self, x1, y1, x2, y2, lines):
        self.o.append(f"<path d='M{x1} {y1} L{x2 - 6} {y2}' stroke='#5A5A66' stroke-width='1' stroke-dasharray='3 3' fill='none'/>")
        for i, l in enumerate(lines): self.text(x2, y2 + 4 + i * 14, l, C.SUB, 11.5)

    def note(self, x, y, lines, c=C.SUB, sz=11.5):
        for i, l in enumerate(lines): self.text(x, y + i * 14, l, c, sz)

    def save(self, path):
        self.o.append("</svg>")
        with open(path, "w", encoding="utf-8") as f: f.write("\n".join(self.o))


def render(svg_path, png_path, w, h, scale=2):
    """Render an SVG to PNG with the preinstalled Playwright Chromium (for the chat; never commit the PNG)."""
    import subprocess, json
    js = ("const { chromium } = require('playwright');(async()=>{const b=await chromium.launch();"
          f"const p=await b.newPage({{viewport:{{width:{w},height:{h}}},deviceScaleFactor:{scale}}});"
          f"await p.goto('file://'+{json.dumps(os.path.abspath(svg_path))});await p.screenshot({{path:{json.dumps(png_path)}}});await b.close();}})();")
    subprocess.run(["node", "-e", js], check=True, timeout=120)
