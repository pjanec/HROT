# Generates docs/UX/vehicle-breaching.svg — DESIGN_Vehicle_Breaching.md, house style (R-4143). §7 FIRST (it overrides the earlier sections):
#   §7.2 V-17 (a ContactBreachEvent ⇒ dust + crash sound + a HUD contact cue) · §7.3 D-3 (ram OR push), D-4 · B-D4 (a tank's bump = a door-sized
#   Passage for infantry, the tank held outside, R-3025 / V-5) · V-9 (the cut at the crossing angle) · V-16 (every node draws the gap) ·
#   §7.6 (ClearObstacle gains a world point At; one click "clear here"; the resolver: an obstacle within 3 m, else the wall panels within 3 m).
# Regenerate: python3 scripts/gen-vehicle-breaching-mockup.py [docs/UX/vehicle-breaching.svg]
# SCOPE (R-4151): the contact cue (three states), the gap drawn on the 2-D map and a display node, the order's At field + its map pick + the
# resolver's verdict line. CONTEXT, drawn as measured and UNCHANGED:
#   the possessed-driving HUD — PossessionStation.cs: the crosshair (:476-479), the compass strip (:731-748), the centred line under the crosshair
#   (OUT OF ARC, :500-526 — the cue takes this slot), the bottom-left block (:528-538) with Hud.Lines (:952-1003) and Hud.ColourOf (:924-947),
#   WeaponLine (:1116-1141), SeatLine (:1015-1020) · the Orders tab (OrdersTab.cs:180-256: ListBlock rows, + Add task, Apply / Revert) and its task form
#   (BehaviorParamsForm.DrawFields → ComponentEditDrawer; the entity chip + teal map-pick glyph ReferenceLeafDrawers.cs:593-615; a point's x y z +
#   Pick Map + List… :500-507, :557-575) · ClearObstacleOrder's fields (ClearObstacleBehavior.cs:130-145) · the 2-D plan's house / road colours.
import os, sys, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

W, H = 1240, 1530
SKY0, SKY1, FIELD, FIELD2 = "#5D7894", "#9DB2C4", "#56653E", "#4A5836"
BRICK, BRICK_D, SAND, SAND_D, CONC = "#9A5A40", "#6E3E2C", "#B9A97A", "#8C7E58", "#8E8A82"
HOUSE = "#E1CDA0"; WALLC = "#96968F"; ROAD = "#E8D58A"; ROAD_EDGE = "#8A7A40"; GROUND = "#3B5131"; ROUTE = "#5BD06A"
HUD_BG = "#000000"
HUD_HEALTH, HUD_SEL, HUD_OTHER, HUD_KEYS, HUD_WHITE, HUD_HOLD = "#78EB78", "#FFDC5A", "#D7D7D7", "#AFAFAF", "#F5F5F5", "#FFAA28"
CUE_RED, CUE_OK = "#FF5A50", "#78EB78"
G = dict(CAR=0xf1b9, TRUCK=0xf5e4, WALK=0xf554, ROAD=0xf018, BARRIER=0xe562, VOLUME=0xf028, WIND=0xf72e, BURST=0xe4dc, WALL=0xf7a4,
         TRACTOR=0xf722, LOCDOT=0xf3c5, BAN=0xf05e, CHECK=0xf058, ENG=0xf6e3, SHIELD=0xf3ed)


def poly(s, pts, fill, stroke=None, op=1.0, sw=1, dash=None):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    st = f" stroke='{stroke}' stroke-width='{sw}'" if stroke else ""
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polygon points='{p}' fill='{fill}' fill-opacity='{op}'{st}{d}/>")


def pline(s, pts, col, w, dash=None, op=1.0):
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    d = f" stroke-dasharray='{dash}'" if dash else ""
    s.raw(f"<polyline points='{p}' fill='none' stroke='{col}' stroke-width='{w}' stroke-linecap='round' stroke-linejoin='round' stroke-opacity='{op}'{d}/>")


def clip(s, cid, x, y, w, h):
    s.raw(f"<clipPath id='{cid}'><rect x='{x}' y='{y}' width='{w}' height='{h}'/></clipPath><g clip-path='url(#{cid})'>")


def unclip(s): s.raw("</g>")


def grad(s, gid, c0, c1):
    s.raw(f"<defs><linearGradient id='{gid}' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{c0}'/><stop offset='1' stop-color='{c1}'/></linearGradient></defs>")


def scene(s, x, y, w, h, horizon=0.48):
    grad(s, f"sky{int(x)}{int(y)}", SKY0, SKY1)
    s.rect(x, y, w, h * horizon, f"url(#sky{int(x)}{int(y)})")
    s.rect(x, y + h * horizon, w, h * (1 - horizon), FIELD)
    poly(s, [(x, y + h * horizon), (x + w * 0.3, y + h * (horizon - 0.06)), (x + w * 0.6, y + h * (horizon - 0.03)), (x + w, y + h * (horizon - 0.07)), (x + w, y + h * horizon)], "#5E6B45")


def hud_line(s, x, y, t, c, sz=12): s.text(x, y, t, c, sz)


def hud_text_w(t, sz=12): return text_width(t, sz) * 0.98


def compass(s, cx, top, half, heading_txt="352"):
    s.rect(cx - half - 5, top - 2, 2 * half + 10, 22, HUD_BG, op=0.43)
    labels = {0: "N", 2: "NE", -2: "NW"}
    for k in range(-4, 5):
        xx = cx + k * half / 4 - 8
        major = k in labels
        s.line(xx, top, xx, top + (6 if major else 4), "#FFFFFF", 1)
        if major: s.text(xx, top + 16, labels[k], "#FFFFFF", 10, anchor="middle")
    s.rect(cx - 13, top + 21, 26, 15, HUD_BG, op=0.6); s.text(cx, top + 32, heading_txt, "#FFD24A", 10.5, anchor="middle")


def crosshair(s, cx, cy):
    for (x1, y1, x2, y2) in [(-7, 0, -2, 0), (2, 0, 7, 0), (0, -7, 0, -2), (0, 2, 0, 7)]:
        s.line(cx + x1, cy + y1, cx + x2, cy + y2, "#FFFFFF", 1.3)


def cue(s, cx, y, text, col, bar=None, glyph=None):
    """The NEW contact cue — in the slot under the crosshair the OUT OF ARC line uses (a centred line on a black box), + a progress bar for a push."""
    tw = hud_text_w(text, 12.5) * 1.08 + (20 if glyph else 0)
    s.rect(cx - tw / 2 - 6, y - 3, tw + 12, 18 + (12 if bar is not None else 0), HUD_BG, op=0.55)
    tx = cx - tw / 2
    if glyph: s.glyph(glyph, tx, y, col, 12); tx += 20
    s.text(tx, y + 11, text, col, 12.5, weight="bold")
    if bar is not None:
        s.rect(cx - tw / 2, y + 17, tw, 6, "#2A2A30"); s.rect(cx - tw / 2, y + 17, tw * bar, 6, col)


def hud_block(s, x, y, lines, sz=11.5):
    lh = sz * 1.45
    w = max(hud_text_w(t, sz) for t, _ in lines)
    s.rect(x - 5, y - 4, w + 10, lh * len(lines) + 6, HUD_BG, op=0.59)
    for i, (t, c) in enumerate(lines): s.text(x, y + 11 + i * lh, t, c, sz)


def dust(s, cx, cy, r, seed=3):
    rr = random.Random(seed)
    for _ in range(22):
        a, d = rr.uniform(0, 2 * math.pi), rr.uniform(0, r)
        s.raw(f"<circle cx='{cx + d * math.cos(a):.1f}' cy='{cy + d * math.sin(a) * 0.6:.1f}' r='{rr.uniform(r * 0.18, r * 0.42):.1f}' fill='#C8B898' fill-opacity='{rr.uniform(0.25, 0.5):.2f}'/>")


def bricks(s, pts_top_left, w, h, col=BRICK, line=BRICK_D):
    x, y = pts_top_left
    s.rect(x, y, w, h, col)
    for r in range(int(h // 7) + 1):
        s.line(x, y + r * 7, x + w, y + r * 7, line, 0.6)
        off = 0 if r % 2 else 7
        for c in range(int(w // 14) + 1): s.line(x + off + c * 14, y + r * 7, x + off + c * 14, y + r * 7 + 7, line, 0.5)


def hull_front(s, x, y, w, h, col="#4A5236"):
    poly(s, [(x, y + h), (x + w * 0.08, y), (x + w * 0.92, y), (x + w, y + h)], col, "#2A2E20", sw=1)
    s.line(x + w * 0.1, y + 6, x + w * 0.9, y + 6, "#5E6844", 1.5)


s = Svg(W, H, "Vehicle breaching — the contact cue, the gaps, the bulldozer's point order (DESIGN_Vehicle_Breaching.md §7, §7.6; house style)",
        "Scope: the NEW contact cue, the gaps as drawn, the ClearObstacle At field and its verdict. The HUD, the Orders tab and the maps are context (UNCHANGED). Grey notes are not drawn by the app.")

# ═══════════════════ A — the possessed-driving HUD with the contact cue ═══════════════════
AX, AY, AW, AH = 20, 76, 760, 430
s.text(AX, AY - 8, "A1 — driving a possessed IFV into a sandbag wall, throttle held: the PUSH (D-3)", C.SUB, 12)
clip(s, "a1", AX, AY, AW, AH)
scene(s, AX, AY, AW, AH)
# a garden wall across the field and the sandbag row in front of the hull
bricks(s, (AX + 40, AY + 196), 680, 22)
poly(s, [(AX + 210, AY + 300), (AX + 550, AY + 300), (AX + 560, AY + 336), (AX + 200, AY + 336)], SAND, SAND_D, sw=1)
for r in range(3):
    for c in range(12):
        s.raw(f"<ellipse cx='{AX + 222 + c * 28 + (r % 2) * 14}' cy='{AY + 306 + r * 11}' rx='14' ry='6' fill='{SAND}' stroke='{SAND_D}' stroke-width='0.8'/>")
dust(s, AX + 380, AY + 330, 26, 5)
hull_front(s, AX + 150, AY + 352, 460, 78)
compass(s, AX + AW / 2, AY + 10, 220)
CXH, CYH = AX + AW / 2, AY + 215
crosshair(s, CXH, CYH)
cue(s, CXH, CYH + 18, "BREAKING — hold throttle", HUD_HOLD, bar=0.62, glyph=G["WALL"])
LINES = [("HEALTH 100/100", HUD_HEALTH), ("> 1 AUTOCANNON  HE 1  280/300", HUD_SEL), ("  2 MISSILE  ATGM  2/2", HUD_OTHER),
         ("  3 MG  7.62 mm  800/800  [200]", HUD_OTHER), ("SEAT DRIVER  (G: next seat)", HUD_WHITE),
         ("W/S throttle  A/D steer  V free look  Esc release", HUD_KEYS),
         ("LMB fire  RMB zoom  X cancel  1-4 weapon  R ammo  F use (hold: more)  Q/E lean  T orders  J next man …", HUD_KEYS)]
hud_block(s, AX + 14, AY + AH - 140, LINES, 10.5)
unclip(s)
s.rect(AX, AY, AW, AH, "none", stroke=C.BORDER)
s.rect(AX + 540, AY + 136, 214, 64, "#0F0F12", op=0.8, rx=3)
s.callout(CXH + 100, CYH + 34, AX + 548, AY + 150, ["NEW — the contact cue, in the slot", "under the crosshair (where OUT OF", "ARC shows); the bar = the push held", "(the row's PushSeconds)"])
s.rect(AX + 474, AY + 340, 252, 18, "#0F0F12", op=0.8, rx=3)
s.callout(AX + 400, AY + 330, AX + 482, AY + 352, ["NEW: dust where the hull meets it (V-17)"])
s.note(AX, AY + AH + 18, ["Context, unchanged: the compass strip, the crosshair, the bottom-left block (HEALTH green, the selected weapon yellow,", "the others grey-white, the seat line, the keys grey). A RAM (speed into the wall ≥ the row's minimum) breaks at once —",
                          "no bar; a PUSH fills it, and it empties if the throttle is released or the hull leaves contact. A break costs speed,", "never stops the hull dead (V-3)."], C.NOTE, 11.5)

# ── A2: too strong ──
RX, RW, RH = 800, 420, 196
s.text(RX, AY - 8, "A2 — a possessed HMMWV at a building wall: held", C.SUB, 12)
clip(s, "a2", RX, AY, RW, RH)
scene(s, RX, AY, RW, RH, 0.4)
s.rect(RX + 30, AY + 24, 360, 140, "#B8A890"); s.rect(RX + 80, AY + 50, 40, 46, "#4A4A52"); s.rect(RX + 280, AY + 50, 40, 46, "#4A4A52")
bricks(s, (RX + 30, AY + 110), 360, 56, "#A89880", "#7E705C")
hull_front(s, RX + 90, AY + 160, 240, 40, "#5C5A48")
crosshair(s, RX + RW / 2, AY + 92)
cue(s, RX + RW / 2, AY + 108, "TOO STRONG for this vehicle", CUE_RED, glyph=G["BAN"])
unclip(s)
s.rect(RX, AY, RW, RH, "none", stroke=C.BORDER)
s.note(RX, AY + RH + 16, ["Red, held while in contact: a wheeled vehicle never breaks (B-D3);", "an IFV at a building wall says the same. Also at a low wall (D-4)."], C.NOTE, 11)
# ── A3: the moment of the break ──
A3Y = AY + RH + 60
s.text(RX, A3Y - 8, "A3 — a possessed tank's bump on a building wall: the break", C.SUB, 12)
clip(s, "a3", RX, A3Y, RW, RH)
scene(s, RX, A3Y, RW, RH, 0.4)
s.rect(RX + 30, A3Y + 24, 360, 150, "#B8A890")
bricks(s, (RX + 30, A3Y + 110), 360, 64, "#A89880", "#7E705C")
s.rect(RX + 180, A3Y + 92, 52, 82, "#1E1C1A")          # the door-sized Passage
dust(s, RX + 206, A3Y + 130, 50, 9)
hull_front(s, RX + 60, A3Y + 166, 300, 30, "#4A5236")
crosshair(s, RX + RW / 2, A3Y + 74)
cue(s, RX + RW / 2, A3Y + 90, "BREACHED — a way in for infantry", CUE_OK, glyph=FA.CHECK)
s.glyph(G["VOLUME"], RX + RW - 30, A3Y + 12, C.AMBER, 14)
unclip(s)
s.rect(RX, A3Y, RW, RH, "none", stroke=C.BORDER)
s.note(RX, A3Y + RH + 16, ["Green for ~2 s, then gone. Dust + a crash sound (AI hears it, V-17).", "The tank stays OUT of the gap it made (B-D4, V-5)."], C.NOTE, 11)

# ═══════════════════ B — a tank's bump on a building wall, 2-D map ═══════════════════
BX, BY, BW, BH = 20, 640, 590, 330
s.text(BX, BY - 8, "B — the bump on the 2-D map: a door-sized Passage, the tank held outside, infantry through it", C.SUB, 12)
s.rect(BX, BY, BW, BH, GROUND, stroke=C.BORDER)
clip(s, "bm", BX, BY, BW, BH)
pline(s, [(BX - 10, BY + 290), (BX + BW + 10, BY + 270)], ROAD_EDGE, 26); pline(s, [(BX - 10, BY + 290), (BX + BW + 10, BY + 270)], ROAD, 22)
HXX, HYY, HWW, HHH = BX + 160, BY + 40, 300, 170
s.rect(HXX, HYY, HWW, HHH, HOUSE, stroke="#2A2A2A", sw=1)
s.line(HXX + 150, HYY, HXX + 150, HYY + HHH, "#5A5650", 2); s.line(HXX, HYY + 90, HXX + 150, HYY + 90, "#5A5650", 2)
for wx in (HXX + 40, HXX + 230): s.rect(wx, HYY + HHH - 3, 24, 6, "#6AB0FF")
# the existing door, and the Passage (the gap in the south wall)
s.rect(HXX + 92, HYY + HHH - 4, 18, 8, "#7A5A3A")
PX0, PX1 = HXX + 182, HXX + 202
s.rect(HXX, HYY + HHH - 2, HWW, 4, "#3A3632")
s.rect(PX0, HYY + HHH - 4, PX1 - PX0, 8, GROUND)
s.rect(PX0, HYY + HHH - 4, PX1 - PX0, 8, "none", stroke=C.ACC, sw=1.5, dash="3 2")
# the tank, held outside
TX, TY = HXX + 192, HYY + HHH + 34
s.rect(TX - 14, TY - 22, 28, 44, "#4A5236", stroke="#1E2214", sw=1.2, rx=2); s.line(TX, TY, TX, TY - 40, "#2A2E20", 3)
s.text(TX + 22, TY + 6, "M1 — held (never enters, V-5)", C.TXT, 11)
# infantry through it
pline(s, [(BX + 40, BY + 250), (TX - 34, TY + 6), (TX - 22, HYY + HHH + 10), ((PX0 + PX1) / 2, HYY + HHH - 10), (HXX + 230, HYY + 120), (HXX + 220, HYY + 60)], ROUTE, 2, dash="6 4")
for k in range(3): s.raw(f"<circle cx='{BX + 40 + k * 14}' cy='{BY + 250 - k * 4}' r='4' fill='{C.BLUE}' stroke='#0F0F0F' stroke-width='1'/>")
unclip(s)
s.callout(PX1 + 4, HYY + HHH - 2, BX + 486, BY + 170, ["Passage 1.1 m", "door-sized (B-D4)"])
s.text(BX + 40, BY + 236, "rifle team: the path through the gap", ROUTE, 11)
s.note(BX, BY + BH + 18, ["NEW: the Passage drawn as a gap with a dashed outline. The path is the planner's ordinary", "route once the navmesh has the gap (the bake); sight and fire pass at once (query time,", "B-D8). The tank's own hull purpose ignores a building-wall breach (V-5)."], C.NOTE, 11.5)

# ═══════════════════ C — a garden wall a tank drove through ═══════════════════
CX, CY, CW, CH = 630, 640, 590, 330
s.text(CX, CY - 8, "C — a garden wall a tank drove through: the 2-D map and a display node", C.SUB, 12)
s.rect(CX, CY, 286, CH, GROUND, stroke=C.BORDER)
clip(s, "cm", CX, CY, 286, CH)
ang = math.radians(-35)
wy = CY + 160
s.line(CX - 5, wy, CX + 110, wy, WALLC, 5); s.line(CX + 176, wy, CX + 295, wy, WALLC, 5)
s.line(CX + 110, wy - 6, CX + 110, wy + 6, C.ACC, 1.5); s.line(CX + 176, wy - 6, CX + 176, wy + 6, C.ACC, 1.5)
# the tank's track crossing at an angle
for k in range(-1, 2, 2):
    pline(s, [(CX + 60 + k * 12, CY + 300), (CX + 230 + k * 12, CY + 30)], "#2A2418", 4, op=0.7)
s.rect(CX + 190, CY + 34, 28, 46, "#4A5236", stroke="#1E2214", sw=1.2, rx=2)
unclip(s)
s.text(CX + 8, CY + 20, "the gap = the hull's footprint", C.TXT, 11); s.text(CX + 8, CY + 34, "at the crossing angle + a margin", C.TXT, 11)
s.text(CX + 8, CY + 48, "(V-9): 6.6 m here for a 3.7 m hull", C.TXT, 11)
s.text(CX + 8, CY + CH - 14, "2-D map — every node", C.NOTE, 11)
# display node
DXN = CX + 304
clip(s, "cd", DXN, CY, 286, CH)
scene(s, DXN, CY, 286, CH, 0.42)
bricks(s, (DXN - 10, CY + 176), 120, 34); bricks(s, (DXN + 190, CY + 176), 110, 34)
poly(s, [(DXN + 110, CY + 176), (DXN + 124, CY + 186), (DXN + 118, CY + 210), (DXN + 110, CY + 210)], BRICK)
poly(s, [(DXN + 190, CY + 176), (DXN + 178, CY + 190), (DXN + 184, CY + 210), (DXN + 190, CY + 210)], BRICK)
for k in range(9): s.rect(DXN + 112 + k * 8, CY + 206 + (k % 3) * 2, 6, 4, BRICK_D)
poly(s, [(DXN + 120, CY + 120), (DXN + 196, CY + 120), (DXN + 206, CY + 150), (DXN + 110, CY + 150)], "#4A5236", "#1E2214")
s.rect(DXN + 140, CY + 106, 36, 16, "#3E4630"); s.line(DXN + 158, CY + 112, DXN + 220, CY + 98, "#2A2E20", 3)
unclip(s)
s.rect(DXN, CY, 286, CH, "none", stroke=C.BORDER)
s.text(DXN + 8, CY + CH - 14, "a display node's 3-D view", C.NOTE, 11)
s.note(CX, CY + CH + 18, ["Both draw the gap from the damage RECORD (V-16 — today the re-cut lives in the bake, which only", "a navmesh node runs): the editor, the player, display nodes and the Replay Browser alike. The", "rubble line is the mesh's; vehicles and infantry route through it (acceptance 6, 14)."], C.NOTE, 11.5)

# ═══════════════════ D — the bulldozer's order: Obstacle OR At ═══════════════════
DX, DY, DW, DH = 20, 1056, 600, 350
s.text(DX, DY - 8, "D — the bulldozer's ClearObstacle order in the Orders tab: an obstacle OR a point (§7.6)", C.SUB, 12)
s.window(DX, DY, DW, DH, "Details — Dozer 1 (Engineer, Friend)")
s.tabs(DX + 8, DY + 26, DW - 16, [("Unit", FA.USER, C.BLUE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)], 1)
y = DY + 58
s.rect(DX + 8, y - 2, DW - 16, 22, "#1E2A3E")
s.raw(f"<circle cx='{DX + 22}' cy='{y + 9}' r='4' fill='{C.DIS}'/>")
s.text(DX + 34, y + 13, "1", C.DIS); s.text(DX + 50, y + 13, "Clear obstacle at 412, 1180, then when done", C.TXT)
y += 30
s.header(DX + 8, y, DW - 16, "Task 1 — Clear obstacle", G["TRACTOR"], C.ORANGE); y += 30
LX, CC, CWW = DX + 20, DX + 150, 420
s.text(LX, y + 14, "Obstacle"); s.combo(CC, y, 294, "(none)", hint=True)
s.rect(CC + 300, y, 22, 20, C.NEUTRAL, rx=3); s.glyph(G["LOCDOT"], CC + 306, y + 3, C.TEAL, 12)
s.text(CC + 330, y + 14, "or a point:", C.NOTE, 11); y += 28
s.rect(DX + 10, y - 3, DW - 20, 50, "#16263A", rx=2)
s.text(LX, y + 14, "At"); s.text(LX + 22, y + 14, "NEW", C.ACC, 10.5, weight="bold")
for i, v in enumerate(["412.0", "1180.0", "86.4"]): s.field(CC + i * 74, y, 70, v)
s.button(CC + 228, y, 70, "Pick Map"); s.button(CC + 304, y, 44, "List…")
s.glyph(FA.CIRCLE_CHECK, LX + 2, y + 27, C.OK, 12); s.text(LX + 22, y + 38, "a wall — a cut 4 m wide here (garden wall, brick)", C.OK, 12)
y += 54
s.text(LX, y + 14, "Speed"); s.field(CC, y, 90, "0", hint=True); s.text(CC + 100, y + 14, "0 = 4 m/s", C.NOTE, 11); y += 26
s.text(LX, y + 14, "Push seconds"); s.field(CC, y, 90, "0", hint=True); s.text(CC + 100, y + 14, "0 = 10 s", C.NOTE, 11); y += 34
s.button(DX + 10, y, 100, "+ Add task"); s.button(DX + 116, y, 70, "Apply", fill=C.BTNSEL); s.button(DX + 192, y, 70, "Revert")
s.note(DX, DY + DH + 18, ["Context, unchanged: the Orders tab's list (a state dot · number · sentence), the task header, the", "ParamsDto drawn by the ONE field drawer — the entity chip + teal pick glyph (EntityLink); x y z +",
                          "Pick Map + List… (a point; List… only where named points exist); defaults grey at 0. NEW: the At", "row and under it the resolver's VERDICT (green: a wall cut / an obstacle; amber: nothing within",
                          "3 m — the order would fail). Obstacle set ⇒ today's path, unchanged (the company's roadblock", "call). The 3 m pick radius and a 60 s timeout are open tunables (§7.6). Task group: Library/Engineer."], C.NOTE, 11.5)

# the map with the two picks
MX, MY, MW, MH = 640, 1056, 580, 350
s.text(MX, MY - 8, "the map: Pick Map → one click \"clear here\"", C.SUB, 12)
s.rect(MX, MY, MW, MH, GROUND, stroke=C.BORDER)
clip(s, "dm", MX, MY, MW, MH)
pline(s, [(MX - 10, MY + 280), (MX + MW + 10, MY + 250)], ROAD_EDGE, 28); pline(s, [(MX - 10, MY + 280), (MX + MW + 10, MY + 250)], ROAD, 24)
s.rect(MX + 60, MY + 40, 150, 100, HOUSE, stroke="#2A2A2A")
s.line(MX + 30, MY + 170, MX + 330, MY + 170, WALLC, 5); s.line(MX + 330, MY + 170, MX + 330, MY + 60, WALLC, 5)
for k in range(5): s.rect(MX + 420 + k * 16, MY + 250 + (k % 2) * 4, 13, 9, "#8C8C96", stroke="#222", sw=0.6)
s.rect(MX + 140, MY + 220, 22, 34, "#C8A040", stroke="#3A3010", rx=2); s.rect(MX + 136, MY + 214, 30, 6, "#888070")
unclip(s)
s.text(MX + 128, MY + 274, "Dozer 1", C.TXT, 11)
# pick 1: on the wall
px, py = MX + 210, MY + 170
s.raw(f"<circle cx='{px}' cy='{py}' r='30' fill='{C.TEAL}' fill-opacity='0.10' stroke='{C.TEAL}' stroke-dasharray='4 3'/>")
s.rect(px - 20, py - 4, 40, 8, "none", stroke=C.ACC, sw=1.5, dash="3 2")
s.glyph(G["LOCDOT"], px - 6, py - 22, C.TEAL, 13)
pline(s, [(MX + 151, MY + 214), (px, py + 26)], C.ORANGE, 1.5, dash="5 4")
s.rect(px + 34, py - 40, 238, 24, "#0F0F12", rx=3, op=0.9); s.text(px + 42, py - 24, "a wall — a cut 4 m wide here", C.OK, 11.5)
s.text(px - 26, py + 46, "3 m", C.TEAL, 10.5)
# pick 2: on the roadblock
qx, qy = MX + 452, MY + 256
s.raw(f"<circle cx='{qx}' cy='{qy}' r='30' fill='{C.TEAL}' fill-opacity='0.10' stroke='{C.TEAL}' stroke-dasharray='4 3'/>")
s.glyph(G["LOCDOT"], qx - 6, qy - 22, C.TEAL, 13)
s.rect(qx - 170, qy - 64, 200, 24, "#0F0F12", rx=3, op=0.9); s.text(qx - 162, qy - 48, "an obstacle — Roadblock 3", C.OK, 11.5)
s.rect(MX + 360, MY + 20, 210, 24, "#0F0F12", rx=3, op=0.9); s.text(MX + 368, MY + 36, "[Picking...]  click the wall or obstacle", "#FFFF00", 11)
s.note(MX, MY + MH + 18, ["The ring = the resolver's 3 m: an obstacle in it wins (today's push, the", "entity's Health); else the wall panels in it ⇒ a blade-wide, full-height", "Passage at the point (V-6, V-13). The verdict shows at the pick and in", "the form; the brain drives, the Muscle cuts (one breach writer)."], C.NOTE, 11.5)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "vehicle-breaching.svg")
s.save(out)
print("written:", out)
