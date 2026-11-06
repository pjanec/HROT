# Generates docs/UX/seats-hats-visual.svg — the seat and hat in the mode indicator, the hat menu, a Player station, the director's camera
# (DESIGN_Seats_And_Hats.md §2.3 / §2.5), house style (R-4143). Regenerate: python3 scripts/gen-seats-hats-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA, text_width
W, H = 1240, 700
s = Svg(W, H, "Seats and hats — shown in the mode indicator, switched in one click (house style; design §2.3 / §2.5)")
HATS = [("Edit", FA.PEN, C.BLUE, "E"), ("Command", FA.CHESS, C.ORANGE, "C"), ("Embodied", FA.GAMEPAD, C.GREEN, "P"), ("Observe", FA.EYE, C.TEAL, "O"), ("Direct", FA.VIDEO, C.VIOLET, "D")]
def toolbar(x, y, w, seat, seat_g, seat_c, hat, hat_g, hat_c, mode="LIVE", mode_c=C.RED):
    s.rect(x, y, w, 30, "#16161B", stroke=C.BORDER)
    for i, g in enumerate([FA.PLAY, FA.CLOCK_BACK]): s.glyph(g, x + 12 + i * 26, y + 8, C.GREY, 13)
    ix = x + 80
    s.rect(ix, y + 4, 330, 22, C.NEUTRAL, stroke="#2E2E36", rx=11)
    s.raw(f"<circle cx='{ix + 14}' cy='{y + 15}' r='5' fill='{mode_c}'/>"); s.text(ix + 25, y + 20, mode, mode_c, 12.5)
    cx = ix + 25 + text_width(mode, 12.5) + 10; s.text(cx, y + 20, "·", C.DIS); cx += 10
    s.glyph(seat_g, cx, y + 8, seat_c, 12); s.text(cx + 18, y + 20, seat, C.TXT, 12.5); cx += 18 + text_width(seat, 12.5) + 8
    s.text(cx, y + 20, "·", C.DIS); cx += 10
    s.glyph(hat_g, cx, y + 8, hat_c, 12); s.text(cx + 18, y + 20, hat, C.TXT, 12.5)
    s.raw(f"<path d='M{ix + 314} {y + 12} l4 5 l4 -5 z' fill='{C.DIS}'/>")
    return ix
# ── A: the editor / an instructor's station ───────────────────────────
AX, AY = 20, 82
ix = toolbar(AX, AY, 760, "Instructor", FA.CROWN, C.AMBER, "Command", FA.CHESS, C.ORANGE)
s.text(AX, AY - 8, "A — an instructor's station (Live)", C.SUB, 12)
mh = s.menu(ix, AY + 34, 330, [("#", "Hat — one at a time"),
                               ("Edit", "E", FA.PEN, C.BLUE), ("Command  ✓", "C", FA.CHESS, C.ORANGE), ("Embodied — possess a unit", "P", FA.GAMEPAD, C.GREEN),
                               ("Observe", "O", FA.EYE, C.TEAL), ("Direct", "D", FA.VIDEO, C.VIOLET, "Author seat only"), "---",
                               ("#", "Seat"), ("Instructor  ✓", None, FA.CROWN, C.AMBER), ("Author", None, FA.PEN, C.BLUE), ("Side controller", None, FA.FLAG, C.RED, "not on this host"), "---",
                               ("Back to my view", "Home", FA.HOUSE, C.GREY)])
s.callout(ix + 330, AY + 15, AX + 800, AY + 6, ["ONE indicator: run mode · seat · hat (§2.3);", "a click opens this menu — never an action (R-3061)"])
s.callout(ix + 330, AY + 150, AX + 800, AY + 120, ["a hat the seat does not allow stays visible, greyed", "WITH the reason (R-4119) — never hidden"])
s.callout(ix + 330, AY + 210, AX + 800, AY + 190, ["the seat changes only to a seat this host allows", "(the editor: Author ⇄ Instructor)"])
s.callout(ix + 330, AY + 300, AX + 800, AY + 265, ["leaving a hat keeps the view; Back to my view", "restores the one you had before (R-260)"])
s.note(AX + 800, AY + 330, ["Esc = one level out: a tool inside the hat → the hat → Observe.", "Shortcuts are the action ids' keys (E14) — shown, never hidden."], C.SUB)
# ── B: a Player station ──────────────────────────────────────────────
BX, BY = 20, 470
s.text(BX, BY - 8, "B — a Player station", C.SUB, 12)
toolbar(BX, BY, 560, "Player", FA.GAMEPAD, C.GREEN, "Embodied", FA.GAMEPAD, C.GREEN)
s.menu(BX + 80, BY + 34, 330, [("#", "Hat"), ("Embodied  ✓", "P", FA.GAMEPAD, C.GREEN), ("Observe", "O", FA.EYE, C.TEAL),
                               ("Edit", "E", FA.PEN, C.BLUE, "a Player seat cannot edit"), ("Command", "C", FA.CHESS, C.ORANGE, "a Player seat cannot command")])
# ── C: the director shares the camera ───────────────────────────────
CX0, CY0 = 640, 470
s.text(CX0, CY0 - 8, "C — the director is playing; the user moved the camera", C.SUB, 12)
s.rect(CX0, CY0, 580, 200, "#1E2A22", stroke=C.BORDER)
s.rect(CX0 + 10, CY0 + 10, 360, 24, "#000000", rx=4, op=0.6); s.glyph(FA.VIDEO, CX0 + 18, CY0 + 15, C.VIOLET, 13)
s.text(CX0 + 38, CY0 + 27, "You have the camera — the director retakes it at its next shot", "#E8E8EE", 11.5)
s.rect(CX0 + 10, CY0 + 160, 196, 26, C.NEUTRAL, stroke="#2E2E36", rx=4); s.checkbox(CX0 + 16, CY0 + 164, True)
s.text(CX0 + 42, CY0 + 178, "Director camera", C.TXT, 12.5)
s.note(CX0 + 220, CY0 + 172, ["untick: the director never takes the camera back"], C.NOTE)
s.save(sys.argv[1])
