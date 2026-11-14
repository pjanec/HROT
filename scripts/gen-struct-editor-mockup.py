# Generates docs/UX/struct-editor-visual.svg — THE struct editor (DESIGN_Struct_Editor.md): every kind of field in Fill mode, the practical
# list forms, the entity picker, the row menu, errors. Define mode is field-editor-visual.svg (same table). House style (R-4143).
# Regenerate: python3 scripts/gen-struct-editor-mockup.py docs/UX/struct-editor-visual.svg
import sys
from mockup_kit import Svg, C, FA, text_width

W, H = 1280, 1720
s = Svg(W, H, "The struct editor — every kind of field, the list forms, entities, errors (DESIGN_Struct_Editor.md)",
        "Scope: the value side of the one table (Fill mode). Define mode adds Name / Type cells — field-editor-visual.svg. Grey notes are not drawn.")

LBL = 190          # the label column (FormGrid's one label width)


def row(x, y, w, label, h=28, depth=0, stripe=False, chev=None, err=False):
    s.rect(x, y, w, h, C.ERRROW if err else ("#151519" if stripe else C.BG))
    for k in range(depth): s.line(x + 15 + k * 18, y, x + 15 + k * 18, y + h, "#34343E")
    lx = x + 10 + depth * 18
    if chev is not None: s.chevron(lx, y + 9, chev); lx += 16
    s.text(lx, y + 18, label, C.TXT, 12.5)
    return x + LBL


def val(x, y, w, txt, changed=False, unit=None, c=None):
    if changed: s.rect(x - 6, y + 3, 4, 22, C.AMBER)
    s.field(x, y + 3, w, txt, c=c or (C.TXT if changed else C.DIS))
    if unit: s.text(x + w + 6, y + 18, unit, C.DIS, 11.5)
    if changed: s.glyph(FA.ROTATE_LEFT, x + w + (30 if unit else 8), y + 7, C.AMBER, 12)


def chip(x, y, glyph, gc, label, sub=None, red=False):
    w = text_width(label, 12.5) + 46 + (text_width(sub, 11) + 12 if sub else 0)
    s.rect(x, y + 3, w, 22, C.ERRROW if red else C.FRAME, stroke=C.ERR if red else C.BORDER, rx=11)
    s.glyph(glyph, x + 8, y + 7, gc, 12); s.text(x + 26, y + 18, label, C.ERR if red else C.TXT, 12.5)
    if sub: s.text(x + 32 + text_width(label, 12.5), y + 18, sub, C.DIS, 11)
    s.text(x + w - 12, y + 17, "▾", C.SUB, 11)
    return x + w


def mapbtns(x, y, pick=True):
    s.glyph(FA.LOCATION, x, y + 7, C.TEAL, 12.5)
    if pick: s.glyph(FA.CROSSHAIRS, x + 20, y + 7, C.GREY, 12.5)


# ═══════════ A — Fill mode: every kind of field ═══════════
X, Y, WW = 20, 60, 620
s.window(X, Y, WW, 940, "Details — AI — Ambush   (FILL — one table, every kind)")
y = Y + 32; x0 = X + 6; w0 = WW - 12
def R(label, **k):
    global y
    v = row(x0, y, w0, label, stripe=(int((y - Y) / 28) % 2 == 1), **k); return v
vx = R("Trigger range"); val(vx, y, 120, "150", True, "m"); y += 28
vx = R("Count"); val(vx, y, 120, "3"); y += 28
vx = R("Enabled"); s.checkbox(vx, y + 6, True); y += 28
vx = R("Callsign"); val(vx, y, 200, "Viper"); y += 28
vx = R("Mode"); s.combo(vx, y + 3, 160, "Advance"); y += 28
vx = R("Support")                                   # flags = toggle chips on ONE row
for i, (lab, on) in enumerate([("Smoke", True), ("Flares", True), ("Mines", False)]):
    cx = vx + i * 78
    s.rect(cx, y + 4, 72, 20, C.BTNSEL if on else C.NEUTRAL, stroke=C.BORDER, rx=10)
    s.text(cx + 36, y + 18, ("✓ " if on else "") + lab, C.TXT if on else C.DIS, 11.5, "middle")
y += 28
vx = R("Side"); s.raw(f"<circle cx='{vx + 8}' cy='{y + 14}' r='5' fill='{C.RED}'/>"); s.combo(vx + 18, y + 3, 140, "Hostile"); y += 28
s.separator_text(x0 + 8, y + 10, "semantic — entities and places", x0 + w0 - 8); y += 26
vx = R("Target"); e = chip(vx, y, FA.USER, C.RED, "Bravo 2-3", "Hostile · inf"); mapbtns(e + 10, y); y += 28
vx = R("Leader"); e = chip(vx, y, FA.WARNING, C.ERR, "gone · #42", red=True); mapbtns(e + 10, y)
y += 28; s.text(vx, y + 13, "the entity no longer exists — pick another or Clear", C.ERR, 11); y += 20
vx = R("Escort"); e = chip(vx, y, FA.BOOKMARK, C.AMBER, "@Escort", "→ Charlie 1"); mapbtns(e + 10, y); y += 28
vx = R("Rally"); e = chip(vx, y, FA.LOCATION, C.TEAL, "@RallyA", "668 · 427 m"); mapbtns(e + 10, y); y += 28
vx = R("Position"); val(vx, y, 220, "668.0 · 427.0 · 2.1", unit="m", c=C.DIS); mapbtns(vx + 252, y); y += 28
vx = R("Heading"); val(vx, y, 70, "242", True, "°"); s.raw(f"<circle cx='{vx + 132}' cy='{y + 14}' r='10' fill='none' stroke='{C.SUB}'/>")
s.line(vx + 132, y + 14, vx + 141, y + 18, C.AMBER, 2); y += 28
s.separator_text(x0 + 8, y + 10, "optional, structure, error", x0 + w0 - 8); y += 26
vx = R("Optional range"); s.checkbox(vx, y + 6, False); s.text(vx + 24, y + 18, "not set", C.DIS, 12.5); y += 28
vx = R("Max range"); s.checkbox(vx, y + 6, True); val(vx + 24, y, 100, "300", True, "m"); y += 28
vx = R("Timing", chev=False); s.text(vx, y + 18, "4 – 9 s · waits for contact", C.DIS, 12); y += 28
vx = R("Fallback", chev=True); s.text(vx, y + 18, "● 1 changed", C.AMBER, 11.5); y += 28
vx = R("Route", depth=1); e = chip(vx, y, FA.ROUTE, C.BLUE, "Route North"); mapbtns(e + 10, y); y += 28
vx = R("Speed", depth=1); val(vx, y, 90, "4", True, "m/s"); y += 28
vx = R("Count", err=True); s.field(vx, y + 3, 120, "3x", border=C.ERR); y += 28
s.rect(x0, y, w0, 22, C.ERRROW); s.glyph(FA.WARNING, x0 + 26, y + 5, C.ERR, 11); s.text(x0 + 44, y + 15, "'3x' is not a whole number — the value stays 3", C.ERR, 11.5); y += 30
s.note(X + 12, y + 4, ["⌖ teal = show on map (hover previews, click pins) · ⊕ = pick on the map · ▾ = the picker (search · map · key · clear)",
                       "grey value = the default · amber bar + ↺ = changed · a closed structure says what changed or shows its summary",
                       "flags = toggle chips on one row · optional = a 'set' box · an error keeps the typed text and the old value"], C.SUB, 11)

# ═══════════ B — the list forms ═══════════
BX, BY, BW = 660, 60, 600
s.window(BX, BY, BW, 940, "Lists — the form follows the element (B1–B6)")
y = BY + 34
def head(t):
    global y
    s.text(BX + 12, y + 14, t, C.SUB, 12.5, weight="bold"); y += 22
head("B1  short list of words / numbers — inline chips")
s.text(BX + 14, y + 18, "Tags", C.TXT, 12.5); cx = BX + LBL - 20
for t in ["night", "urban"]:
    w = text_width(t, 12) + 30; s.rect(cx, y + 3, w, 22, C.FRAME, stroke=C.BORDER, rx=11); s.text(cx + 10, y + 18, t, C.TXT, 12)
    s.text(cx + w - 14, y + 18, "✕", C.DIS, 11); cx += w + 6
s.rect(cx, y + 3, 24, 22, C.NEUTRAL, stroke=C.BORDER, rx=11); s.glyph(FA.PLUS, cx + 7, y + 8, C.OK, 10); y += 30
s.text(BX + 14, y + 18, "Weights", C.TXT, 12.5); cx = BX + LBL - 20
for t in ["0.5", "0.3", "0.2"]:
    s.field(cx, y + 3, 52, t); cx += 58
s.rect(cx, y + 3, 24, 22, C.NEUTRAL, stroke=C.BORDER, rx=11); s.glyph(FA.PLUS, cx + 7, y + 8, C.OK, 10)
s.text(cx + 34, y + 18, "drag a chip to reorder", C.DIS, 11); y += 40

head("B2  list of structures — one SUMMARY row per item")
items = [("1", FA.ROUTE, C.BLUE, "Advance", "to @PhaseLine · 5 s", False), ("2", FA.HOURGLASS, C.AMBER, "Hold", "30 s · until contact", True),
         ("3", FA.CROSSHAIRS, C.RED, "Fire", "at Bravo 2-3 · 3 bursts", False)]
for n, g, gc, kind, summ, open_ in items:
    s.rect(BX + 8, y, BW - 16, 28, C.BTNSEL if open_ else C.BG)
    s.text(BX + 14, y + 18, "⋮⋮", C.DIS, 12); s.text(BX + 32, y + 18, n, C.DIS, 12)
    s.chevron(BX + 50, y + 9, open_); s.glyph(g, BX + 68, y + 7, gc, 12); s.text(BX + 88, y + 18, kind, C.TXT, 12.5)
    s.text(BX + 160, y + 18, summ, C.SUB, 12); s.glyph(FA.ELLIPSIS_V, BX + BW - 30, y + 7, C.GREY, 12)
    y += 28
    if open_:
        for lab, v, ch in [("Seconds", "30", True), ("Until", "contact", False)]:
            s.rect(BX + 8, y, BW - 16, 26, "#151519"); s.line(BX + 57, y, BX + 57, y + 26, "#34343E")
            s.text(BX + 70, y + 17, lab, C.TXT, 12); val(BX + 210, y - 1, 110, v, ch); y += 26
s.button(BX + 16, y + 6, 120, "Add step ▾", glyph=FA.PLUS, gc=C.OK)
s.text(BX + 148, y + 22, "⋮ = Insert above / below · Duplicate · Move · Delete", C.DIS, 11); y += 44

head("B3  small flat structures — a GRID (columns = fields)")
gx = [BX + 16, BX + 160, BX + 380, BX + 470]
s.rect(BX + 8, y, BW - 16, 22, C.HDR)
for t, x in zip(["Name", "Position", "Wait", ""], gx): s.text(x, y + 15, t, C.SUB, 11.5)
y += 22
for nm, pos, wt in [("Alpha", "@WP1", "10 s"), ("Bravo", "@WP2", "0 s")]:
    s.rect(BX + 8, y, BW - 16, 26, C.BG); s.text(gx[0] - 6, y + 17, "⋮⋮", C.DIS, 11)
    s.field(gx[0] + 10, y + 2, 120, nm); s.field(gx[1], y + 2, 140, pos); s.glyph(FA.LOCATION, gx[1] + 146, y + 6, C.TEAL, 11)
    s.field(gx[2], y + 2, 70, wt); s.glyph(FA.TRASH, gx[3] + 80, y + 6, C.GREY, 11); y += 26
s.button(BX + 16, y + 6, 120, "Add waypoint", glyph=FA.PLUS, gc=C.OK); s.text(BX + 148, y + 22, "grid ⇄ rows: a setting per type (≤ 4 plain fields)", C.DIS, 11); y += 44

head("B4  fixed size — slots, no add / remove")
for i, v in enumerate(["Rifle", "Grenade", "—", "—"]):
    s.rect(BX + 8, y, BW - 16, 24, C.BG if i % 2 else "#151519"); s.text(BX + 16, y + 16, f"Slot {i + 1}", C.TXT, 12)
    s.combo(BX + LBL - 20, y + 2, 160, v); y += 24
s.text(BX + 16, y + 16, "4 / 4 — fixed size", C.DIS, 11); y += 30

head("B5  polymorphic — the kind picked when adding")
s.button(BX + 16, y + 2, 110, "Add ▾", glyph=FA.PLUS, gc=C.OK)
s.menu(BX + 130, y + 2, 200, [(" Move", None, FA.ROUTE, C.BLUE), (" Wait", None, FA.HOURGLASS, C.AMBER), (" Fire", None, FA.CROSSHAIRS, C.RED),
                              (" Say", None, FA.BULLHORN, C.GREY), (" Insert macro", None, FA.CUBES, C.VIOLET)])
y += 130
head("B6  long list — filter and pages")
s.text(BX + 16, y + 16, "Spawn points", C.TXT, 12.5); s.text(BX + 120, y + 16, "312 items", C.DIS, 12)
s.field(BX + 200, y + 2, 170, "     filter", hint=True); s.glyph(FA.SEARCH, BX + 205, y + 5, C.DIS, 11)
s.text(BX + 380, y + 16, "1 – 50   ‹  ›", C.SUB, 12); y += 28
s.text(BX + 16, y + 14, "a list over 50 items starts closed and pages; the filter matches the item summary", C.DIS, 11)

# ═══════════ C — the entity picker (the chip's ▾) ═══════════
CX, CY = 20, 1030
s.window(CX, CY, 620, 330, "Target ▾ — the one entity picker")
s.field(CX + 12, CY + 34, 596, "      bra", c=C.TXT); s.glyph(FA.SEARCH, CX + 18, CY + 37, C.DIS, 12)
py = CY + 66
for g, gc, nm, side, sc, typ, d, sel in [(FA.USER, C.RED, "Bravo 2-3", "Hostile", C.RED, "infantry team", "210 m", True),
                                         (FA.USERS, C.RED, "Bravo 2", "Hostile", C.RED, "platoon", "340 m", False),
                                         (FA.VAN, C.BLUE, "Brava supply", "Friend", C.BLUE, "truck", "1.2 km", False)]:
    if sel: s.rect(CX + 10, py - 4, 600, 28, C.BTNSEL)
    s.glyph(g, CX + 20, py + 2, gc, 12); s.text(CX + 42, py + 14, nm, C.TXT, 12.5)
    s.raw(f"<circle cx='{CX + 200}' cy='{py + 10}' r='4' fill='{sc}'/>"); s.text(CX + 210, py + 14, side, C.SUB, 12)
    s.text(CX + 300, py + 14, typ, C.DIS, 12); s.text(CX + 590, py + 14, d, C.DIS, 12, "end"); py += 30
s.line(CX + 10, py, CX + 610, py, C.SEP); py += 8
for g, gc, t in [(FA.CROSSHAIRS, C.GREY, "Pick on the map"), (FA.BOOKMARK, C.AMBER, "By key (@name) — follows whoever holds the key"),
                 (FA.XMARK, C.GREY, "Clear")]:
    s.glyph(g, CX + 20, py + 2, gc, 12); s.text(CX + 42, py + 14, t, C.TXT, 12.5); py += 26
s.note(CX + 12, py + 6, ["type-ahead over names · side colour · kind glyph · distance from the selection",
                         "a field's [MapPickableEntity] presets narrow the list (infantry only, …)"], C.SUB, 11)

# ═══════════ D — the item / row menu, and the type codec ═══════════
DX, DY = 660, 1030
s.menu(DX, DY, 260, [("Insert above", None, FA.PLUS, C.OK), ("Insert below", None, FA.PLUS, C.OK), ("Duplicate", "Ctrl+D", FA.COPY, C.GREY),
                     ("Move up", "Alt+↑", None, None), ("Move down", "Alt+↓", None, None), "---",
                     ("Copy value", "Ctrl+C", FA.COPY, C.GREY), ("Paste value", "Ctrl+V", FA.PASTE, C.GREY), ("Reset to default", None, FA.ROTATE_LEFT, C.AMBER),
                     "---", ("Delete", "Del", FA.TRASH, C.RED)])
s.window(DX + 280, DY, 320, 330, "One registration per type")
s.note(DX + 292, DY + 40, ["TypeEditor<T> — everything a type needs:", "", "  Shape    leaf · structure · list", "  Draw     the ImGui cell",
                           "  Format   value → text (cell, summary,", "           copy)", "  Parse    text → value (typing, paste)",
                           "  Validate value → problems (per field)", "  Default  a new item's value", "  Map      ⌖ show · ⊕ pick (semantic)",
                           "", "Registered ONCE in the library's", "catalogue → every host draws it.", "",
                           "⛔ today: two registries (StructEdit", "node + host ImGui drawer) — most", "hosts forget the second."], C.SUB, 11.5)


# ═══════════ E — an ENTITY FORM (FormView) drawn with the SAME kit ═══════════
EX, EY = 20, 1390
s.window(EX, EY, 620, 300, "Details — Unit — Alpha 1-1   (an entity FORM: FormDecl rows, the SAME widgets)")
y = EY + 32
vx = row(EX + 6, y, 608, "Name"); val(vx, y, 200, "Alpha 1-1", True); y += 28
vx = row(EX + 6, y, 608, "Side", stripe=True); s.raw(f"<circle cx='{vx + 8}' cy='{y + 14}' r='5' fill='{C.BLUE}'/>"); s.combo(vx + 18, y + 3, 140, "Friend"); y += 28
vx = row(EX + 6, y, 608, "Commander"); e = chip(vx, y, FA.USERS, C.BLUE, "1st Platoon", "Friend"); mapbtns(e + 10, y); y += 28
vx = row(EX + 6, y, 608, "Strength", stripe=True); val(vx, y, 80, "80"); s.text(vx + 90, y + 18, "/ 100", C.DIS, 12); y += 28
vx = row(EX + 6, y, 608, "Knows about", chev=True); s.text(vx, y + 18, "2 entities", C.DIS, 12); y += 28
for n, nm, st, sc in [("1", "Bravo 2-3", "applied", C.OK), ("2", "Bravo 2", "pending", C.AMBER)]:
    s.rect(EX + 6, y, 608, 26, C.BG); s.line(EX + 21, y, EX + 21, y + 26, "#34343E"); s.text(EX + 30, y + 17, "⋮⋮", C.DIS, 11)
    chip(EX + 50, y - 1, FA.USER, C.RED, nm, "Hostile"); s.text(EX + 330, y + 17, st, sc, 11.5); s.glyph(FA.ELLIPSIS_V, EX + 590, y + 6, C.GREY, 11); y += 26
s.button(EX + 30, y + 6, 120, "Add ▾", glyph=FA.PLUS, gc=C.OK); y += 40
s.note(EX + 12, y, ["the SAME FormGrid rows · the SAME entity chip and picker · the SAME list block, item menu and error line —",
                    "FormView keeps its declared rows (R-4120); only its hand-made buttons go"], C.SUB, 11)

# ═══════════ F — the widget kit and what ImGui needs for each ═══════════
FX, FY = 660, 1390
s.window(FX, FY, 600, 300, "The ONE widget kit (NodeEditor.Core.Widgets) — and its ImGui cost")
kit = [("FormGrid", "label | value | status", "exists (C1)"), ("Chip", "glyph · text · ▾ / ✕ / toggle", "exists (U-8): InvisibleButton + rounded DrawList rect"),
       ("ChipRow", "B1 chips, wrap, ⊕, drag", "cursor wrap + ImGui drag-drop (used 3× already)"), ("ListBlock", "B2 B5 B6 summary rows, ⋮, Add ▾", "Selectable rows + BeginPopupContextItem"),
       ("GridBlock", "B3 columns = fields", "BeginTable"), ("RowMarks", "changed bar + ↺ · error line · set box", "DrawList rect + one text row"),
       ("EntityField", "the chip + picker popup", "InputText + filtered Selectables (Fdp.Presentation)")]
ky = FY + 36
for nm, what, cost in kit:
    s.text(FX + 14, ky, nm, C.TXT, 12.5); s.text(FX + 112, ky, what, C.SUB, 11.5); s.text(FX + 112, ky + 15, cost, C.DIS, 11); ky += 36
s.note(FX + 14, ky + 2, ["drag is a BONUS: the ⋮ menu and Alt+↑/↓ always move an item"], C.SUB, 11)

s.save(sys.argv[1] if len(sys.argv) > 1 else "docs/UX/struct-editor-visual.svg")
