# Generates docs/UX/menus-visual.svg — the ONE action catalogue's menus, shortcuts and undo (DESIGN_Action_Catalogue.md §2,
# DESIGN_Input_And_Commands.md, DESIGN_Edit_Mode_Undo.md), house style (R-4143). Regenerate: python3 scripts/gen-menus-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA
W, H = 1240, 985
s = Svg(W, H, "Menus, shortcuts and undo — one action catalogue for every surface (house style; Action Catalogue §2, Input, Undo)")
# ── A: a unit's context menu, Live, instructor ───────────────────────
AX, AY = 20, 78
s.text(AX, AY - 8, "A — right-click Alpha 1-1 · Live · Instructor seat", C.SUB, 12)
s.menu(AX, AY, 300, [("#", "Orders"), ("Move here", "M", FA.ROUTE, C.GREEN), ("Attack…", "A", FA.CROSSHAIRS, C.RED), ("Hold", "H", FA.SHIELD, C.BLUE),
                     "---", ("#", "View"), ("Center on it", "F", FA.CROSSHAIRS, C.GREY), ("Follow", "Shift+F", FA.VIDEO, C.VIOLET), ("Possess", "P", FA.GAMEPAD, C.GREEN),
                     "---", ("#", "Edit"), ("Rename…", "F2", FA.PEN, C.BLUE), ("Change side ▸", None, FA.FLAG, C.RED), ("Copy", "Ctrl+C", FA.COPY, C.GREY),
                     "---", ("Delete…", "Del", FA.TRASH, C.RED)])
s.callout(AX + 300, AY + 30, AX + 320, AY + 30, ["GROUPS in a fixed order: Orders · View · Edit ·", "Debug · Destructive (last); a coloured glyph", "per action (R-4141); the SHORTCUT on every", "item — the keymap's binding (E14), never hidden"])
s.callout(AX + 300, AY + 330, AX + 320, AY + 300, ["Delete… in Live asks once (Confirm:", "live-destructive, panel D) — the ONLY", "confirmation in this menu (R-4121)"])
# ── B: the same menu in Review ───────────────────────────────────────
BX, BY = 640, 78
s.text(BX, BY - 8, "B — the same unit · Review (read-only)", C.SUB, 12)
s.menu(BX, BY, 340, [("#", "Orders"), ("Move here", "M", FA.ROUTE, C.GREEN, "Review is read-only"),
                     "---", ("#", "View"), ("Center on it", "F", FA.CROSSHAIRS, C.GREY), ("Follow", "Shift+F", FA.VIDEO, C.VIOLET),
                     "---", ("#", "Review"), ("Bookmark this moment", "B", FA.BOOKMARK, C.AMBER), ("Go live from here", None, FA.PLAY, C.RED),
                     "---", ("#", "Edit"), ("Rename…", "F2", FA.PEN, C.BLUE, "Review is read-only"), ("Delete…", "Del", FA.TRASH, C.RED, "Review is read-only")])
s.callout(BX + 340, BY + 50, BX + 360, BY + 40, ["the SAME catalogue: the mode", "greys what it forbids — with", "the reason — never hides it", "(Modes: Offered · Greyed ·", "Hidden per action, §2)"])
# ── C: the Edit menu — undo ──────────────────────────────────────────
CX0, CY0 = 20, 470
s.rect(CX0, CY0, 600, 22, "#16161B", stroke=C.BORDER)
for i, m in enumerate(["File", "Edit", "View", "Window", "Help"]):
    if m == "Edit": s.rect(CX0 + 8 + i * 60, CY0 + 2, 50, 18, C.BTNSEL, rx=2)
    s.text(CX0 + 16 + i * 60, CY0 + 15, m, C.TXT, 12.5)
s.text(CX0, CY0 - 8, "C — the Edit menu · Edit mode", C.SUB, 12)
s.menu(CX0 + 68, CY0 + 22, 330, [("Undo  Move Alpha 1-1", "Ctrl+Z", FA.UNDO, C.BLUE), ("Redo  Set side of Bravo 1", "Ctrl+Y", FA.REDO, C.BLUE),
                                  ("History…", None, FA.CLOCK_BACK, C.BLUE), "---",
                                  ("Cut", "Ctrl+X", None, None), ("Copy", "Ctrl+C", FA.COPY, C.GREY), ("Paste", "Ctrl+V", FA.PASTE, C.GREY), "---",
                                  ("Keyboard shortcuts…", "Ctrl+K", FA.KEYBOARD, C.GREY)])
s.note(CX0 + 410, CY0 + 50, ["UNDO NAMES THE STEP:", "one user action = one", "step (R-4133);", "Ctrl+Z / Ctrl+Y only in", "Edit mode — in Live and", "Review they are greyed:", "\"undo works in Edit", "mode only\"", "", "History… opens the list", "of steps (panel E)"], C.SUB)
# ── D: the one confirmation ─────────────────────────────────────────
DX, DY = 640, 470
s.text(DX, DY - 8, "D — Delete… during a live exercise (the scarce confirmation)", C.SUB, 12)
s.window(DX, DY, 420, 130, "Delete Alpha 1-1?")
s.glyph(FA.WARNING, DX + 14, DY + 34, C.PEND, 16)
s.note(DX + 40, DY + 46, ["The exercise is running. Alpha 1-1 and its 4 men", "leave every station now. This cannot be undone."], C.TXT, 12.5)
s.button(DX + 180, DY + 96, 110, "Delete", glyph=FA.TRASH, gc=C.RED); s.button(DX + 298, DY + 96, 110, "Cancel", fill=C.BTNSEL)
s.note(DX + 430, DY + 30, ["Cancel is the default (Enter);", "Esc closes it (§3.7)."], C.SUB)
# ── E: Edit ▸ History… — the steps, click to go back or forward ────
EX, EY = 20, 712
s.text(EX, EY - 8, "E — Edit ▸ History… (Edit mode) · the editor's ONE history: world steps and take edits in one order (R-4133, U4 / U5)", C.SUB, 12)
s.window(EX, EY, 520, 150, "History")
s.text(EX + 8, EY + 40, "Click a step to go back (or forward) to the state after it.", C.DIS, 12)
RY = EY + 48
rows = [("Scenario as opened", "grey", False, "Move Alpha 1-1' can no longer be undone"),
        ("Move Alpha 1-1", "ok", False, None),
        ("Set side of Bravo 1", "ok", True, None),
        ("Move camera key", "undone", False, None),
        ("Delete Charlie 2", "undone", False, None)]
for i, (lab, kind, cur, why) in enumerate(rows):
    y = RY + i * 18
    if cur: s.rect(EX + 4, y - 2, 512, 18, "#2C3A52", rx=2)
    if cur: s.glyph(FA.CIRCLE, EX + 12, y + 1, C.BLUE, 11)
    if kind == "grey":
        s.text(EX + 34, y + 14, lab + "   -   '" + why, C.DIS)
    elif kind == "undone":
        s.text(EX + 34, y + 14, lab, "#8C8C94")
    else:
        s.text(EX + 34, y + 14, lab, C.TXT)
s.note(EX + 540, EY + 50, ["ONE list, oldest first; the state before the first", "step is the top row. The blue dot and the bar mark", "WHERE YOU ARE; the steps below it were undone", "(dimmed) and come back on Redo.", "",
                          "A row you cannot reach is greyed WITH ITS REASON:", "a step on the way aged out of the snapshot ring", "(depth / memory budget) so it cannot be undone.", "",
                          "Outside Edit mode every row is greyed and the", "header reads \"Undo works in Edit mode only.\"", "",
                          "A take edit (Move camera key) is a step like any", "other: Edit ▸ Undo names it. A take is its own file,", "so it never puts the dot on the scenario title."], C.SUB)
s.save(sys.argv[1])
