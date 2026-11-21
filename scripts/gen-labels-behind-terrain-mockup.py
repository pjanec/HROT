# Generates docs/UX/labels-behind-terrain.svg — unit cards hidden behind terrain, one card per unit (DESIGN_Map_3D_Mode.md §6am, CE-4582),
# house style (R-4143). Regenerate: python3 scripts/gen-labels-behind-terrain-mockup.py docs/UX/labels-behind-terrain.svg
# SCOPE (R-4151): the unit cards, the place labels and the layers panel's Units group only; the 3-D scene is drawn for context and is UNCHANGED.
import sys
from mockup_kit import Svg, C, FA

W, H = 1240, 980
s = Svg(W, H, "Unit cards behind terrain — one card per unit, every unit text a row of it (Map 3D §6am, CE-4582; house style)",
        "Scope: the cards, place labels and the Units layer rows. The 3-D scene is context (unchanged). Grey notes are not drawn by the app.")

SKY, HILL, HILL2, BLD, TREE, TREE2, GROUND = "#33475E", "#5E6B45", "#4E5A3A", "#8A8478", "#3E6830", "#32562A", "#6B7048"
BLUE_SIDE, RED_SIDE = "#5AA9FF", "#E65555"


def card(x, y, rows, side, w=150, sel=False):
    """A card: rows = [(text, colour) | ('bar', fraction, colour)]; the anchor (its unit) is at (x, y), the card above it."""
    h = 8 + sum(10 if r[0] == 'bar' else 15 for r in rows)
    top = y - 20 - h
    s.rect(x - w / 2, top, w, h, "#101418", stroke=("#FFFFFF" if sel else side), sw=(2 if sel else 1.2), op=0.92)
    yy = top + 6
    for r in rows:
        if r[0] == 'bar':
            s.rect(x - w / 2 + 6, yy, w - 12, 5, "#2A2A30"); s.rect(x - w / 2 + 6, yy, (w - 12) * r[1], 5, r[2]); yy += 10
        else:
            s.text(x - w / 2 + 6, yy + 10, r[0], r[1], 10.5); yy += 15
    s.line(x, top + h, x, y, c=side, sw=1)
    return top


# ── A: the 3-D view — what hides a card and what does not ─────────────────────────────────────────────────────
AX, AY, AW, AH = 20, 70, 760, 430
s.text(AX, AY - 8, "A — the 3-D map, Units ▸ Hide labels behind terrain ON (the default)", C.SUB, 12)
s.rect(AX, AY, AW, AH, SKY)
s.raw(f"<polygon points='{AX},{AY+210} {AX+140},{AY+170} {AX+300},{AY+190} {AX+460},{AY+160} {AX+AW},{AY+175} {AX+AW},{AY+AH} {AX},{AY+AH}' fill='{HILL2}'/>")
# ① a unit beyond the near ridge: its card first, then the ridge drawn over it ⇒ cut at the ridge line
card(AX + 300, AY + 250, [("Bravo 2-1  #1017", C.TXT), ('bar', 0.7, C.GREEN), ("Move (on the march)", C.AMBER)], RED_SIDE)
s.raw(f"<clipPath id='pl'><rect x='{AX+180}' y='{AY+150}' width='80' height='100'/></clipPath>")
s.raw(f"<text x='{AX+205}' y='{AY+238}' fill='{C.ORANGE}' font-size='11' font-family='Roboto,Arial'>WP 3</text>")
s.raw(f"<polygon points='{AX},{AY+300} {AX+170},{AY+232} {AX+300},{AY+212} {AX+430},{AY+246} {AX+520},{AY+270} {AX+AW},{AY+300} {AX+AW},{AY+AH} {AX},{AY+AH}' fill='{HILL}'/>")
# ② a unit behind the building: its card first, then the building over it
card(AX + 615, AY + 262, [("Bravo 1-2  #1022", C.TXT), ('bar', 0.4, C.AMBER), ("Suppress", C.AMBER), ("stress ▮▮▮▯▯", C.PEND)], RED_SIDE)
s.rect(AX + 555, AY + 200, 120, 160, BLD, stroke="#5A5650")
s.rect(AX + 555, AY + 192, 120, 10, "#6A3A30")
# ③ a wood: the trees, then the card ON TOP (step 2 — trees never hide a card)
for tx in (AX + 60, AX + 95, AX + 130, AX + 165):
    s.raw(f"<circle cx='{tx}' cy='{AY+352}' r='26' fill='{TREE}'/>")
    s.rect(tx - 3, AY + 372, 6, 22, "#5A4630")
card(AX + 112, AY + 360, [("Alpha 1-1  #1004", C.TXT), ('bar', 0.9, C.GREEN), ("CombatPosture: cover", C.AMBER)], BLUE_SIDE)
# ④ the selected unit, also beyond the ridge — drawn whole, on top
card(AX + 440, AY + 262, [("Bravo 3  #1030  (selected)", C.TXT), ('bar', 0.55, C.AMBER), ("Attack", C.AMBER)], RED_SIDE, sel=True, w=172)
s.rect(AX, AY + 395, AW, 35, GROUND)
s.note(AX + 8, AY + 412, ["① Bravo 2-1 beyond the ridge: the card is CUT at the ridge line (per pixel) · the waypoint text WP 3 beyond it is hidden the same way",
                          "② Bravo 1-2 behind the building: only the part above the roof shows — frame, bar and stress row cut together, never one piece alone",
                          "③ Alpha 1-1 in a wood: trees never hide a card (§6am D-3, step 2) · ④ the selected unit: always whole, on top (as its body, §6ak D-7)"], C.TXT, 10.5)

# ── B: one card per unit — the moved rows ─────────────────────────────────────────────────────────────────────
BX, BY = 820, 70
s.text(BX, BY - 8, "B — the squad leader's ONE card, every row on", C.SUB, 12)
rows = [("Alpha 1  #1001", C.TXT, "Labels · 20 name"), ('bar', 0.8, C.GREEN, "Labels · 10 health"),
        ("stress ▮▮▮▯▯   (BROKEN in red)", C.PEND, "Mind state · 11–12"),
        ("SQUAD 4 ph2", C.TEAL, "Squad command · 60 (was free text)"), ("no picture on this node yet", C.TEAL, "Squad command · 63 (was free text)"),
        ("CombatPosture: advance", C.AMBER, "Actions · 70–72, one per channel"), ("PF hidden 2.1s", C.AMBER, "Actions · 73 (was free text)"),
        ("D3: TakeCover Δ0.12", C.VIOLET, "Utility · 74–79, one per decision"), ("Crouched", C.ORANGE, "Unit details · 80 stance"), ("312*", C.ORANGE, "Unit details · 81 heading"),
        ("hidden: AK-47", C.GREY, "Concealed · 46"), ("BOUNTY", "#E8C04A", "Labels · 10b")]
y = BY + 12
s.rect(BX, y, 190, 8 + 15 * len(rows), "#101418", stroke=BLUE_SIDE, sw=1.2)
yy = y + 6
for r in rows:
    if r[0] == 'bar':
        s.rect(BX + 6, yy + 3, 178, 5, "#2A2A30"); s.rect(BX + 6, yy + 3, 178 * r[1], 5, r[2])
    else:
        s.text(BX + 6, yy + 11, r[0], r[1], 10.5)
    s.text(BX + 200, yy + 11, r[-1], C.NOTE, 10)
    yy += 15
s.note(BX, yy + 22, ["each row keeps ITS layer and shows only while that layer is on", "AND the card's own layer (Units ▸ Labels) is on — no orphan card"], C.SUB, 11)

# ── C: a member's card — his own E/R and progress rows ──────────────────────────────────────────────────────
CX, CY = 820, 330
s.text(CX, CY - 8, "C — a member: what was about HIM is on HIS card", C.SUB, 12)
s.rect(CX, CY + 4, 170, 68, "#101418", stroke=BLUE_SIDE, sw=1.2)
for i, (t, c) in enumerate([("Alpha 1-2  #1006", C.TXT), (None, None), ("E1R2", C.TEAL), ("Moving", C.TEAL)]):
    if t is None: s.rect(CX + 6, CY + 26, 158, 5, "#2A2A30"); s.rect(CX + 6, CY + 26, 120, 5, C.GREEN); continue
    s.text(CX + 6, CY + 18 + i * 14 - (6 if i > 1 else 0), t, c, 10.5)
s.note(CX, CY + 92, ["SquadGizmo's E/R (row 61) and CommandPictureGizmo's progress (row 62)", "were drawn at the member as free text by the LEADER's gizmo;", "now a row of the member's own card (Squad command layer)"], C.SUB, 11)

# ── D: the layers panel — Units group ─────────────────────────────────────────────────────────────────────────
DX, DY = 20, 560
s.text(DX, DY - 8, "D — Map ▸ Layers, the Units group (rows unchanged except the NEW one)", C.SUB, 12)
s.window(DX, DY, 360, 230, "Layers")
s.header(DX + 8, DY + 30, 344, "Units", FA.USERS, C.BLUE)
for i, (lab, on, new, gl, gc) in enumerate([("Entities", True, False, FA.USER, C.BLUE), ("Labels", True, False, FA.INFO, C.TXT),
                                            ("Hide labels behind terrain", True, True, FA.MOUNTAIN, C.GREEN), ("Unit details", False, False, FA.SLIDERS, C.ORANGE),
                                            ("Mind state", True, False, FA.BRAIN, C.PEND), ("Concealed", False, False, FA.USER_SECRET, C.GREY)]):
    yy = DY + 60 + i * 26
    s.checkbox(DX + 24, yy, on)
    s.glyph(gl, DX + 48, yy + 12, gc, 12)
    s.text(DX + 68, yy + 12, lab, C.TXT if not new else C.OK, 12.5)
    if new: s.text(DX + 262, yy + 12, "NEW, ON", C.OK, 10.5)
s.note(DX + 380, DY + 70, ["Hide labels behind terrain — ON by default; OFF = today's cards on top (M13).",
                           "Entities now also hides the 3-D BODIES (and they are not pickable) — today",
                           "it hides only the gizmo symbols; the bodies are always drawn.",
                           "Labels off ⇒ no card at all, even with Mind state on (today: a card holding",
                           "only the stress rows — the orphan card)."], C.SUB, 11.5)

# ── E: OFF — today's behaviour, for comparison ──────────────────────────────────────────────────────────────
EX, EY = 20, 830
s.text(EX, EY - 8, "E — the same view with the toggle OFF: every card on top, as today (M13)", C.SUB, 12)
s.rect(EX, EY, 760, 120, SKY)
s.raw(f"<polygon points='{EX},{EY+90} {EX+200},{EY+60} {EX+460},{EY+70} {EX+760},{EY+85} {EX+760},{EY+120} {EX},{EY+120}' fill='{HILL}'/>")
for i, (nx, nm) in enumerate([(EX + 120, "Alpha 1-1"), (EX + 300, "Bravo 2-1"), (EX + 470, "Bravo 3"), (EX + 640, "Bravo 1-2")]):
    s.rect(nx - 55, EY + 18, 110, 30, "#101418", stroke=(BLUE_SIDE if i == 0 else RED_SIDE), sw=1.2)
    s.text(nx - 49, EY + 37, nm, C.TXT, 10.5)
    s.line(nx, EY + 48, nx, EY + 80, c=(BLUE_SIDE if i == 0 else RED_SIDE))
s.save(sys.argv[1] if len(sys.argv) > 1 else "docs/UX/labels-behind-terrain.svg")
