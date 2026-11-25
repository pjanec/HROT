# Generates docs/UX/revive-visual.svg — the Revive action on the Unit form (DESIGN_Injury_States.md §9), house style (R-4143).
# Regenerate: python3 scripts/gen-revive-mockup.py docs/UX/revive-visual.svg
# Scope: the Strength row's trailing Revive button and its three states. Every other row and tab is CONTEXT, drawn unchanged (R-4151).
import sys
from mockup_kit import Svg, C, FA
W, H = 1320, 560
s = Svg(W, H, "Revive — one action that puts a man back in the fight (house style; Injury States §9)",
        "Scope: the Strength row's Revive button only. Other rows are context, unchanged. Annotations in grey are not drawn by the app.")
TABS = [("Unit", FA.USER, C.BLUE), ("Crew & cargo", FA.VAN, C.ORANGE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)]


def gauge(x, y, w, frac, label, col):
    s.rect(x, y, w, 19, C.FRAME)
    if frac > 0: s.rect(x + 1, y + 1, (w - 2) * frac, 17, col, op=0.85)
    s.text(x + w / 2, y + 14, label, C.TXT, anchor="middle")


def unit_form(X, Y, title, chip, strength, frac, col, revive, mark=None, posture="Prone"):
    WW, HH = 420, 300
    s.window(X, Y, WW, HH, title, chip=chip)
    s.tabs(X + 8, Y + 26, WW - 16, TABS, 0)
    LX, CX = X + 14, X + 110
    y = Y + 60
    s.separator_text(LX, y, "Identity", X + WW - 14); y += 24
    s.text(LX, y + 14, "Name"); s.field(CX, y, WW - 124, "Pvt. Novak"); y += 26
    s.text(LX, y + 14, "Side"); s.combo(CX, y, WW - 124, "Friend"); y += 30
    s.separator_text(LX, y, "State", X + WW - 14); y += 24
    s.text(LX, y + 14, "Posture"); s.combo(CX, y, WW - 124, posture); y += 26
    s.text(LX, y + 14, "Strength")
    gw = WW - 124 - (30 if revive else 0) - (22 if mark else 0)
    gauge(CX, y, gw, frac, strength, col)
    bx = CX + gw + 6
    if revive:
        s.rect(bx, y - 1, 24, 21, C.NEUTRAL, stroke="#2E2E36", rx=4, sw=1.2)
        s.glyph(FA.HEART, bx + 5, y + 2.5, C.GREEN, 14)
        bx += 30
    if mark == "pending": s.glyph(FA.HOURGLASS, bx, y + 2.5, C.PEND, 13)
    y_strength = y
    y += 26
    s.text(LX, y + 14, "Ammunition"); gauge(CX, y, WW - 124, 0.7, "140 / 200", "#2F5D3A"); y += 30
    return y_strength


# ── A: a DEAD man — the Revive button beside his Strength ───────────────────────────
ya = unit_form(20, 70, "Details — Pvt. Novak (Rifleman)", ("Live", "#F2C033", "#3A3112"), "0 / 100   dead", 0.0, C.RED, revive=True)
s.tooltip(150, ya + 28, 300, ["Revive — back in the fight: full health,", "wounds and stun cleared, can move and fire.", "His brain starts idle and takes orders again."], C.GREEN)
s.note(20, 400, ["A — DEAD or INCAPACITATED: the green heart-pulse", "button trails the Strength row; hover says what it does."])

# ── B: sent — the row is pending until the owner answers (CE-11060's outcome) ──────
yb = unit_form(460, 70, "Details — Pvt. Novak (Rifleman)", ("Live", "#F2C033", "#3A3112"), "0 / 100   dead", 0.0, C.RED, revive=True, mark="pending")
s.note(460, 400, ["B — clicked: one edit to the man's owner (this node or", "another). The row is pending until the answer; a refusal", "shows the usual red cross with the reason."])

# ── C: revived — at full strength the button is gone ───────────────────────────────
unit_form(900, 70, "Details — Pvt. Novak (Rifleman)", ("Live", "#F2C033", "#3A3112"), "100 / 100", 1.0, "#2F5D3A", revive=False, posture="Prone")
s.note(900, 400, ["C — applied: 100 / 100, the button is gone (offered only", "while he is wounded, incapacitated or dead). He gets up", "from where he lay; posture is his brain's again."])

s.note(20, 480, ["Also on: every host that draws the Unit form (Editor, CGF, SimHost station) — Edit mode and Live; greyed in Review / a recording, like every row.",
                 "Not a hack in the Strength field: typing a number there sets ONLY the number (R-11021). Revive is the one action that does the rest."], C.SUB)
s.save(sys.argv[1])
