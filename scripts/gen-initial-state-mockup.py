# Generates docs/UX/initial-state-visual.svg — the start-state rows (DESIGN_Initial_State.md §3 / §3a), house style (R-4143).
# Regenerate: python3 scripts/gen-initial-state-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA
W, H = 1240, 750
s = Svg(W, H, "Initial state — where he is, what he carries, how he holds it, what he knows; his character (house style; design §3 / §3a)")
TABS_ENTITY = [("Unit", FA.USER, C.BLUE), ("Crew & cargo", FA.VAN, C.ORANGE), ("Orders", FA.ROUTE, C.GREEN), ("Brain", FA.BRAIN, C.PINK), ("Components", FA.CODE, C.GREY)]
# ── A: the Unit form, the start-state rows ───────────────────────────
X, Y, WW, HH = 20, 60, 560, 620
s.window(X, Y, WW, HH, "Details — Pvt. Novak (Rifleman, Friend)", chip=("Edit mode", "#7FD69A", "#163A24"))
s.tabs(X + 8, Y + 26, WW - 16, TABS_ENTITY, 0)
LX, CX, CW, SX = X + 14, X + 140, WW - 186, X + WW - 38
y = Y + 60
def sep(label):
    global y
    s.separator_text(LX, y, label, X + WW - 14); y += 24
def lab(t, c=C.TXT): s.text(LX, y + 14, t, c)
sep("Where he is")
lab("Aboard"); s.combo(CX, y, 190, "BTR-80 \"Kilo 2\"", glyph=FA.VAN, gc=C.ORANGE); s.text(CX + 198, y + 14, "seat", C.DIS); s.combo(CX + 232, y, CW - 232, "Gunner"); y += 26
lab("Carried by", C.DIS); s.combo(CX, y, CW, "— not carried —", hint=True); y += 26
s.text(LX + 14, y + 10, "or, for a casualty:  [Sgt. Brandt]  as  [Stretcher]  second man  [Pvt. Lee]", C.NOTE, 11.5); y += 22
sep("What he carries")
lab("Carries")
for w, st, g, gc in [("M4 carbine", "Hands — drawn", FA.CROSSHAIRS, C.GREEN), ("AT4 launcher", "Back — slung", FA.CROSSHAIRS, C.GREEN), ("Pistol", "Hidden — concealed", FA.USER_SECRET, C.GREY)]:
    s.combo(CX, y, 150, w, glyph=g, gc=gc); s.combo(CX + 156, y, CW - 156, st); y += 26
s.button(CX, y, 80, "Add…", glyph=FA.PLUS, gc=C.OK); s.text(CX + 88, y + 14, "from the map or Add Entity ▸ Weapon systems", C.NOTE, 11.5); y += 30
sep("How he holds it")
lab("Weapon"); s.combo(CX, y, 120, "Aimed"); s.text(CX + 128, y + 14, "at", C.DIS); s.button(CX + 146, y, CW - 146, "     Pick a point or a unit…"); s.glyph(FA.CROSSHAIRS, CX + 154, y + 3.5, C.GREEN, 12); y += 26
lab("In hand"); s.combo(CX, y, CW, "Binoculars", glyph=FA.BINOCULARS, gc=C.BLUE); y += 26
lab("Posture"); s.combo(CX, y, CW, "Prone  (at start)"); y += 30
sep("What he knows")
lab("Knows about")
for i, (n, g, gc) in enumerate([("Red sniper (Tango 3)", FA.USER, C.RED), ("Red BMP (Tango 7)", FA.VAN, C.RED)]):
    s.rect(CX, y, CW, 20, C.FRAME, rx=3); s.glyph(g, CX + 6, y + 3.5, gc, 12); s.text(CX + 24, y + 14.5, n); s.glyph(FA.XMARK, CX + CW - 18, y + 4, C.DIS, 11); y += 24
s.button(CX, y, 150, "Add… (pick on map)", glyph=FA.PLUS, gc=C.OK); y += 34
# expert
s.rect(X + 6, y, WW - 12, 20, C.HDR); s.raw(f"<path d='M{LX} {y + 6} l5 6 5 -6 z' fill='{C.TXT}'/>"); s.text(LX + 14, y + 14, "Details for experts"); y += 28
lab("Damage", C.SUB); s.slider(CX, y, CW, 0.8, "strength 80 %"); y += 26
lab("Alerted", C.DIS); s.text(CX, y + 14, "not set here — a task's lease (§3a)", C.NOTE, 11.5)
# callouts A
CXT = X + WW + 16
s.callout(X + WW - 6, Y + 98, CXT, Y + 98, ["ABOARD: a vehicle and a seat role (driver, gunner,", "commander, passenger) — EmbarkEntityCommand, applied", "at once, saved (WS-4)"])
s.callout(X + WW - 6, Y + 200, CXT, Y + 190, ["CARRIES: each weapon in a station — Hands (drawn),", "Back (slung), Hidden (concealed); a station is a slot of", "his passenger list, so it is saved"])
s.callout(X + WW - 6, Y + 330, CXT, Y + 290, ["HOW HE HOLDS IT: at ease / ready / aimed (+ at what),", "what is in his hand, posture — the start-state rule:", "saved, starts in it, shown at once while paused; his", "first decision may change it (Q1)"])
s.callout(X + WW - 6, Y + 460, CXT, Y + 400, ["KNOWS ABOUT: SeedTargetCommand, on any node (Q4)"])
# ── B: the Brain tab, Character ──────────────────────────────────────
BX, BY, BW = 640, 470, 580
s.window(BX, BY, BW, 270, "Details — Pvt. Novak · Brain", chip=("Live", "#7FD69A", "#163A24"))
s.tabs(BX + 8, BY + 26, BW - 16, TABS_ENTITY, 3)
by = BY + 60; LX2, CX2, CW2, SX2 = BX + 14, BX + 130, BW - 176, BX + BW - 38
s.separator_text(LX2, by, "Character", BX + BW - 14); by += 24
for name, val, lbl, authored in [("Skill", 0.62, "Regular (type: Regular)", False), ("Bravery", 0.8, "0.80  (type 0.50)", True), ("Obedience", 0.8, "0.80", False), ("Nerve", 0.35, "0.35  (type 0.55)", True), ("Resolve", 0.5, "0.50", False)]:
    s.text(LX2, by + 14, name); s.slider(CX2, by, CW2, val, lbl)
    if authored: s.glyph(FA.ROTATE_LEFT, SX2, by + 3, C.INFO, 13)
    by += 26
s.text(LX2, by + 12, "written to the brain's node from this station (R-4137); a platoon's Skill reaches every man under it", C.NOTE, 11.5)
s.note(LX2, BY + 238, ["the blue reset arrow = an authored value that differs from the type; click = back to the type (with its spread)"], C.SUB)
s.save(sys.argv[1])
