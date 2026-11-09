# Generates docs/UX/better-merge-visual.svg — DESIGN_Review_Perspective.md §6.11 (the better merge, CE-4354): what the merged view ADDS —
# ① Details "By node" (I5, the views lens), ② the "Replica differs" timeline lane (I5), ③ the Source menu's local-entity filter (I7),
# ④ ▶ in Merged (I3). Scope: only these; the map, the transport, the rest of Details and the timeline are context, drawn unchanged.
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mockup_kit import Svg, C, FA, render, text_width

SITEMAP, CROWN, DIFF, PLAY = 0xf0e8, 0xf521, 0xf0ec, FA.PLAY
W, H = 1240, 680
s = Svg(W, H, "The better merge — what the merged view adds (DESIGN_Review_Perspective.md §6.11)")

# ── ① Details · By node ─────────────────────────────────────────────────────────────────────────────────
Y = 62
s.separator_text(20, Y, "① Details — a component's value on EVERY node (I5): the owner's and each replica's", 600)
DX, DY, DW, DH = 20, Y + 24, 580, 250
s.window(DX, DY, DW, DH, "Details — Bravo 2-3")
s.tabs(DX + 8, DY + 26, DW - 16, [("Unit", FA.USER, C.BLUE), ("Components", FA.CODE, C.GREY)], 1)
hy = DY + 56
s.header(DX + 8, hy, DW - 16, "Position", FA.LOCATION, C.TEAL)
s.form_row(DX + 18, hy + 28, "value", 120, "field", DW - 160, "668.2 · 427.9 m", dis=True)
s.header(DX + 18, hy + 56, DW - 36, "By node", SITEMAP, C.TEAL)
ry = hy + 84
for node, val, delta, owner, warn in [("CGF-1 (Brain)", "668.2 · 427.9", "", True, False),
                                      ("SimHost-1 (Muscle)", "668.2 · 427.9", "—", False, False),
                                      ("IG-1 (Map2D)", "667.1 · 427.4", "1.2 m", False, True),
                                      ("Player-1", "668.2 · 427.9", "—", False, False)]:
    if owner: s.glyph(CROWN, DX + 30, ry + 2, C.AMBER, 12)
    s.text(DX + 50, ry + 14, node, C.TXT, 12)
    s.text(DX + 260, ry + 14, val, C.AMBER if warn else C.SUB, 12)
    s.text(DX + 400, ry + 14, ("owner" if owner else delta), C.AMBER if warn else C.NOTE, 11.5)
    ry += 24
s.note(DX, DY + DH + 16, ["the crown = the owner (from the recorded ownership, I8)", "amber = differs from the owner beyond the existing diff ε (0.001)",
                          "shown only in the merged view, for the selected unit's components"], C.NOTE, 11)

# ── ③ the Source menu with the local-entity filter ───────────────────────────────────────────────────────
s.separator_text(620, Y, "③ the Source menu — local entities from every node (I7)", W - 20)
MX, MY = 620, Y + 24
s.rect(MX, MY, 300, 22, C.FRAME, rx=3, stroke="#3EC6B0"); s.glyph(SITEMAP, MX + 7, MY + 4, C.TEAL, 12)
s.text(MX + 26, MY + 15, "Merged · 4 participants", C.TXT, 12); s.text(MX + 288, MY + 15, "▾", C.TXT, 11)
h = s.menu(MX, MY + 26, 340, [("#", "Source of the world"), ("Merged — every part", None, SITEMAP, C.TEAL), ("CGF-1's part", None, 0xf544, C.BLUE),
                              ("SimHost-1's part", None, 0xf1b2, C.AMBER), "---", ("#", "Local entities shown from (no network id)")])
cy = MY + 26 + h
s.rect(MX, cy - 4, 340, 4 * 24 + 40, C.POPUP, stroke=C.SEP)
for i, (node, on) in enumerate([("CGF-1 — markers", True), ("SimHost-1 — effects", True), ("IG-1", False), ("Player-1", False)]):
    s.checkbox(MX + 12, cy + 2 + i * 24, on); s.text(MX + 40, cy + 16 + i * 24, node, C.TXT, 12)
s.rect(MX, cy + 4 * 24 + 4, 340, 1, C.SEP)
s.text(MX + 12, cy + 4 * 24 + 24, "Offsets…  (expert — a manual slider per node, default 0)", C.SUB, 12)
s.note(MX, cy + 4 * 24 + 54, ["a local entity is tagged with its node in Details (\"local to SimHost-1\")",
                               "Offsets… opens the Federation window (debug): each node's measured latency, overridable"], C.NOTE, 11)

# ── ② the Replica-differs lane + ④ ▶ in Merged ────────────────────────────────────────────────────────────
Y2 = DY + DH + 100
s.separator_text(20, Y2, "② the Event Timeline gains one lane for the selected unit (I5) · ④ ▶ plays in Merged (I3)", W - 20)
TX, TY, TW, TH = 20, Y2 + 24, 1200, 130
s.rect(TX, TY, TW, TH, C.BG, stroke=C.BORDER); s.rect(TX, TY, TW, 20, C.TITLE); s.text(TX + 8, TY + 14, "Event Timeline")
bx = TX + TW - 150
s.rect(bx, TY + 2, 24, 16, C.BTN, rx=3); s.glyph(PLAY, bx + 7, TY + 4, C.BLUE, 11)
s.text(bx + 32, TY + 14, "▶ enabled in Merged", C.SUB, 11)
rx0, rx1 = TX + 200, TX + TW - 14
for i, (name, col, evs) in enumerate([("Shot", C.AMBER, [0.2, 0.31, 0.62]), ("Hit", C.ORANGE, [0.33, 0.63]),
                                      ("Replica differs · Bravo 2-3", C.AMBER, [0.40, 0.41, 0.42, 0.71])]):
    yy = TY + 30 + i * 30
    s.text(TX + 12, yy + 13, name, col, 11.5); s.rect(rx0, yy + 2, rx1 - rx0, 18, "#16161B")
    for f in evs:
        x = rx0 + (rx1 - rx0) * f
        if i == 2: s.rect(x - 3, yy + 4, 6, 14, col, rx=1)
        else: s.o.append(f"<circle cx='{x}' cy='{yy + 11}' r='4' fill='{col}'/>")
s.line(rx0 + (rx1 - rx0) * 0.45, TY + 24, rx0 + (rx1 - rx0) * 0.45, TY + TH - 6, C.RED, sw=1.5)
s.note(TX, TY + TH + 16, ["the lane marks frames where any replica of the selected unit differs from its owner (the same ε) · click = seek there",
                          "④ ▶ enabled once the merge is incremental (S4, 0 B steady — R-2084); until then ui's A-9 greys it with the reason"], C.NOTE, 11)

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "UX", "better-merge-visual.svg")
s.save(out)
if len(sys.argv) > 1: render(out, sys.argv[1], W, H)
