# Generates docs/UX/unit-intent-mockup.svg - the unit intent section, the Brain tab's monitoring part, the Orders tab's held plan and the
# order menu's notice (docs/DESIGN_Unit_Intent.md §5). Regenerate: python3 scripts/gen-unit-intent-mockup.py docs/UX/unit-intent-mockup.svg
# (needs fonttools; draws through scripts/mockup_kit.py). Scope: see the design's §5 - everything not named there is CONTEXT, unchanged.
import sys
from mockup_kit import Svg, C, FA

W, H = 1320, 960
s = Svg(W, H, "Unit intent — what a unit does NEXT (DESIGN_Unit_Intent.md §5)",
        "Annotations in grey are not drawn by the app. Context drawn dimmed is unchanged (R-4151).")

TABS = [("Unit", FA.USER, C.BLUE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)]
LW = 92          # label column
ROWH = 25


def panel(x, y, w, h, title, chip, chip_col, tab):
    s.window(x, y, w, h, title)
    s.rect(x + w - 104, y + 2, 96, 16, chip_col, rx=8)
    s.text(x + w - 56, y + 14, chip, C.TXT, sz=11, anchor="middle")
    s.tabs(x + 8, y + 25, w - 16, TABS, tab)
    return y + 58


def slot_mark(x, y, kind):
    if kind == "pend": s.glyph(FA.HOURGLASS, x, y + 2, C.PEND)
    elif kind == "lock": s.glyph(FA.LOCK, x, y + 2, C.GREY)


def intent_section(x, y, w, rows, footer, footer_col=C.SUB):
    s.header(x + 6, y, w - 12, "Intent — what the unit does next", glyph=FA.PEN, gc=C.AMBER)
    y += 28
    cx, cw, sx = x + 14 + LW, w - LW - 56, x + w - 30
    for label, kind, txt, mark, bg in rows:
        if bg: s.rect(x + 6, y - 3, w - 12, ROWH, bg)
        dis = mark == "lock"
        s.text(x + 14, y + 14, label, C.DIS if dis else C.TXT)
        if kind == "combo": s.combo(cx, y, cw, txt, dis=dis)
        elif kind == "field": s.field(cx, y, cw, txt, dis=dis)
        elif kind == "roe":
            s.combo(cx, y, cw // 2 - 4, txt[0]); s.combo(cx + cw // 2 + 4, y, cw // 2 - 4, txt[1])
        slot_mark(sx, y, mark)
        y += ROWH
    for line, col in footer:
        s.text(x + 14, y + 12, line, col if col else footer_col, sz=11.5)
        y += 16
    return y + 6


def warn_line(x, y, w, text, col=C.PEND, glyph=FA.WARNING, bg=C.PENDROW):
    lines = text if isinstance(text, list) else [text]
    h = 8 + 16 * len(lines)
    s.rect(x + 6, y, w - 12, h, bg, rx=2)
    s.glyph(glyph, x + 14, y + 4, col)
    for i, l in enumerate(lines): s.text(x + 34, y + 16 + 16 * i, l, col, sz=12)
    return y + h + 6


def monitor(x, y, w, lines, dim=True):
    col = C.DIS if dim else C.TXT
    s.header(x + 6, y, w - 12, "Doing", glyph=FA.BRAIN, gc=C.PINK)
    y += 28
    for l in lines:
        s.text(x + 14, y + 12, l, col, sz=12); y += 18
    return y + 4


# ---------- A: edit mode, no plan ----------
AX, AY, AW, AH = 20, 60, 420, 330
y = panel(AX, AY, AW, AH, "Details — Alpha 1-1", "Edit mode", "#163A24", 2)
y = intent_section(AX, y, AW, [
    ("Task", "combo", "Patrol", None, None),
    ("  Route", "field", "Route 2 (north loop)", None, None),
    ("  Speed", "combo", "Walk", None, None),
    ("SOP", "combo", "Infantry default (type)", None, None),
    ("ROE", "roe", ("Return fire", "Take cover"), None, None),
], [("Starts when time first advances.", C.SUB)])
s.line(AX + 6, y, AX + AW - 6, y, C.SEP); y += 8
s.text(AX + 14, y + 12, "Not started — nothing runs before the first step.", C.NOTE, sz=12); y += 22
s.text(AX + 14, y + 12, "(Running behaviours, Knows, Why … appear once it runs)", C.DIS, sz=11)
s.note(AX, AY + AH + 16, ["A  EDIT MODE: the intent IS the unit's starting AI; the save writes it.",
                          "   The monitoring part has nothing to show until the run starts."])

# ---------- B: edit mode, with a plan ----------
BX, BY, BW, BH = 460, 60, 420, 330
y = panel(BX, BY, BW, BH, "Details — Bravo 1-2", "Edit mode", "#163A24", 2)
y = intent_section(BX, y, BW, [
    ("Task", "combo", "— driven by the mission plan —", "lock", None),
    ("SOP", "combo", "Cautious patrol", None, None),
    ("ROE", "roe", ("Hold fire", "Take cover"), None, None),
], [])
y = warn_line(BX, y, BW, ["Bravo follows a mission plan — edit its first task", "on the Orders tab."], col=C.INFO, glyph=FA.INFO, bg=C.HDR)
s.button(BX + 34, y, 120, "Open Orders", glyph=FA.ROUTE, gc=C.GREEN)
y += 30
s.line(BX + 6, y, BX + BW - 6, y, C.SEP); y += 8
s.text(BX + 14, y + 12, "Not started — nothing runs before the first step.", C.NOTE, sz=12)
s.note(BX, BY + BH + 16, ["B  EDIT MODE + PLAN: task row locked (R8 — one source for the task);",
                          "   SOP and ROE stay editable (plans carry none, R4)."])

# ---------- C: paused mid-run ----------
CX, CY, CW, CH = 900, 60, 400, 560
y = panel(CX, CY, CW, CH, "Details — Bravo 1-2", "Paused 00:04:12", "#4A3A12", 2)
y = intent_section(CX, y, CW, [
    ("Task", "combo", "Attack", "pend", C.PENDROW),
    ("  Position", "field", "House 7", "pend", C.PENDROW),
    ("SOP", "combo", "Cautious patrol", None, None),
    ("ROE", "roe", ("Return fire", "Take cover"), None, None),
], [("Applies when time next advances.", C.PEND)])
y = warn_line(CX, y, CW, ["This order holds the mission plan (task 3,", "Hold at bridge) — resume it on the Orders tab."], col=C.PEND)
s.line(CX + 6, y, CX + CW - 6, y, C.SEP); y += 8
s.header(CX + 6, y, CW - 12, "Doing", glyph=FA.BRAIN, gc=C.PINK); y += 28
s.text(CX + 14, y + 12, "Follow route — mission task 3", C.TXT, sz=12)
s.chip(CX + 226, y - 2, "next: Attack", glyph=FA.HOURGLASS, gc=C.PEND, border=C.PEND, arrow=False); y += 24
s.text(CX + 14, y + 12, "for 1 min 40 s · origin Superior (plan)", C.DIS, sz=11.5); y += 24
s.header(CX + 6, y, CW - 12, "Running behaviours › Follow route › Blackboard", glyph=FA.LIST_CHECK, gc=C.TEAL); y += 28
for k, v, staged in [("WaypointIndex", "4", False), ("Speed", "2.5", True), ("Arrived", "false", False)]:
    if staged: s.rect(CX + 6, y - 3, CW - 12, ROWH, C.PENDROW)
    s.text(CX + 22, y + 14, k, C.TXT, sz=12)
    s.field(CX + 170, y, CW - 220, v + ("  (staged)" if staged else ""), c=C.PEND if staged else C.TXT, border=C.PEND if staged else None)
    y += ROWH
y = warn_line(CX, y + 2, CW, ["When time advances, Attack starts — this staged", "edit to Follow route will be lost."], col=C.PEND)
s.text(CX + 14, y + 10, "Low level: edits the running memory, staged to the next step.", C.DIS, sz=11)
s.note(CX, CY + CH + 16, ["C  PAUSED LIVE: the top EDITS the intent (amber = pending);",
                          "   below MONITORS what runs now; the chip joins the two.",
                          "   The blackboard edit is the one low-level edit left (R10)."])

# ---------- D: Orders tab ----------
DX, DY, DW, DH = 20, 460, 560, 330
y = panel(DX, DY, DW, DH, "Details — Bravo 1-2", "Paused 00:04:12", "#4A3A12", 1)
s.text(DX + 14, y + 12, "Mission plan", "#9FB7D8", sz=13); y += 24
rows = [("1", "Move to the ford", "Done", FA.CIRCLE_CHECK, C.GREEN, None, None),
        ("2", "Clear the farm", "Failed — behaviour fault", FA.CIRCLE_XMARK, C.RED, "Retry", C.ERRROW),
        ("3", "Hold at bridge", "Held by your order Attack (00:04:12)", FA.HOURGLASS, C.PEND, "Resume plan", C.PENDROW),
        ("4", "Withdraw to the road", "Planned", FA.CIRCLE, C.GREY, None, None)]
for n, name, state, g, col, btn, bg in rows:
    if bg: s.rect(DX + 6, y - 3, DW - 12, 26, bg)
    s.glyph(g, DX + 14, y + 3, col)
    s.text(DX + 34, y + 15, f"{n}  {name}", C.TXT, sz=12.5)
    s.text(DX + 220, y + 15, state, col, sz=12)
    if btn: s.button(DX + DW - 124, y, 108, btn, glyph=FA.PLAY if btn == "Resume plan" else FA.ROTATE_LEFT, gc=C.GREEN if btn == "Resume plan" else C.INFO)
    y += 28
s.text(DX + 14, y + 8, "⚠ two rows halted at once is only for the picture: a plan halts on ONE task.", C.NOTE, sz=11)
y += 20
s.line(DX + 6, y, DX + DW - 6, y, C.SEP); y += 10
s.text(DX + 14, y + 12, "Edit mode, a file with BOTH a plan and an intent task:", C.NOTE, sz=11.5); y += 20
y = warn_line(DX, y, DW, ["This unit's intent also sets a task (Patrol) — the plan's first task", "replaces it; the intent's task never starts."])
s.button(DX + 34, y, 180, "Remove the intent task", glyph=FA.TRASH, gc=C.RED)
s.note(DX, DY + DH + 16, ["D  ORDERS TAB: Resume plan restarts the held task (R6); Retry is the same command on a fault.",
                          "   The both-set warning also shows on the Brain tab's intent section."])

# ---------- E: the map order menu ----------
EX, EY = 600, 740
s.text(EX, EY, "E  The map's order form (live, paused):", C.SUB, sz=12, weight="bold")
s.rect(EX, EY + 10, 300, 150, C.POPUP, stroke=C.BORDER, rx=3)
s.text(EX + 12, EY + 32, "Attack — Bravo 1-2", C.TXT, sz=13)
s.form_row(EX + 12, EY + 44, "Position", 70, "field", 196, "House 7")
s.form_row(EX + 12, EY + 72, "Assault", 70, "combo", 196, "Fire and move")
s.glyph(FA.HOURGLASS, EX + 12, EY + 108, C.PEND)
s.text(EX + 32, EY + 120, "Holds Bravo's mission plan — resume it", C.PEND, sz=11.5)
s.text(EX + 32, EY + 134, "on the Orders tab.", C.PEND, sz=11.5)
s.button(EX + 196, EY + 136, 92, "Order", glyph=FA.CHECK, gc=C.GREEN)
s.text(EX + 320, EY, "Edit mode, a planned unit — the menu:", C.SUB, sz=12, weight="bold")
s.menu(EX + 320, EY + 10, 200, [("Attack…", None, FA.CROSSHAIRS, C.RED, ""), ("Move…", None, FA.ROUTE, C.GREEN, ""), ("Defend…", None, FA.SHIELD, C.BLUE, ""), "---", ("Select", None, FA.EYE, C.GREY, None)])
s.tooltip(EX + 330, EY + 104, 250, ["Bravo follows a mission plan —", "edit its first task on the Orders tab"], edge=C.INFO)
s.note(EX + 320, EY + 160, ["order items drawn DISABLED; the tooltip says why (R8)"])

s.save(sys.argv[1])
