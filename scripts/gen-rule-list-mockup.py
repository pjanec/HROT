# Generates docs/UX/rule-list-visual.svg — the Rules window's List view and the + New rule gallery (DESIGN_Rule_Editor_UX.md §2.1 / §2.5),
# house style (R-4143). Regenerate: python3 scripts/gen-rule-list-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA, text_width
W, H = 1240, 730
s = Svg(W, H, "Rules — the List view (sentences) and the + New rule gallery (house style; design §2.1 / §2.5)")
X, Y, WW, HH = 20, 60, 760, 400
s.window(X, Y, WW, HH, "Rules", chip=("Edit mode", "#7FD69A", "#163A24"))
# view switch (glyph toggles, approved switch style) + search + new
s.glyph_toggle(X + 10, Y + 28, FA.LIST_CHECK, C.BLUE, True); s.glyph_toggle(X + 44, Y + 28, FA.CLOCK, C.AMBER, False)
s.text(X + 82, Y + 46, "List", C.TXT, 12.5)
s.field(X + 130, Y + 31, 300, "      search rules", hint=True); s.glyph(FA.SEARCH, X + 136, Y + 34, C.DIS, 12)
s.text(X + 444, Y + 46, "group by", C.DIS, 12); s.combo(X + 500, Y + 31, 110, "Macro")
s.button(X + WW - 126, Y + 31, 114, "New rule", glyph=FA.PLUS, gc=C.OK, fill=C.BTNSEL)
y = Y + 66
def group(label, n):
    global y
    s.rect(X + 6, y, WW - 12, 20, C.HDR); s.raw(f"<path d='M{X + 14} {y + 6} l5 6 5 -6 z' fill='{C.TXT}'/>")
    s.text(X + 28, y + 14, label); s.text(X + WW - 16, y + 14, f"{n} rule" + ("" if n == 1 else "s"), C.DIS, 11.5, "end"); y += 24
def rule(parts, kind_glyph, kind_col, sel=False, req=None, bad=None):
    """parts: [(text, grammar?)] — the sentence; grammar words in blue (the font has no bold)."""
    global y
    if sel: s.rect(X + 6, y - 2, WW - 12, 24, "#2C3A52", rx=2)
    s.glyph(kind_glyph, X + 16, y + 3, kind_col, 13)
    cx = X + 38
    for txt, bold in parts:
        s.text(cx, y + 15, txt, "#9FB7D8" if bold else C.TXT, 13); cx += text_width(txt) * 1.06 + 7
    if req: s.text(X + WW - 16, y + 15, req, C.DIS, 11.5, "end")
    if bad:
        s.glyph(FA.CIRCLE_XMARK, X + WW - 30, y + 3, C.ERR, 13)
    y += 26
group("Phase 2 — the assault (macro)", 3)
rule([("When", True), ("Alpha 1-1 is inside OBJ BLUE", False), ("for", True), ("30 s", False), ("→", True), ("Begin", True), ("Phase 2,", False), ("Message", True), ("Blue: \"Phase 2\"", False)], FA.BOLT, C.AMBER, sel=True)
rule([("Take and hold", True), ("OBJ BLUE", False), ("with", True), ("4 men", False), ("for", True), ("5 min", False)], FA.TARGET, C.GREEN, req="required")
rule([("When", True), ("at least 3 of Red side are destroyed", False), ("→", True), ("Bring in", True), ("Red QRF at Gate 2", False)], FA.BOLT, C.AMBER)
group("Scenario", 2)
rule([("Protect", True), ("Convoy 1", False), ("— lose no more than", True), ("1 vehicle", False)], FA.SHIELD, C.BLUE, req="required")
rule([("When", True), ("Bravo 1 is inside", False), ("(no area chosen)", False)], FA.BOLT, C.AMBER, bad=True)
group("Scoring (exercise designers)", 1)
rule([("Measure", True), ("Blue hit ratio", False), ("— target", True), ("≥ 40 %", False)], FA.FLAG_CHECKERED, C.AMBER)
s.note(X + 14, Y + HH - 30, ["Blue words are the rule's grammar (no bold in the font); the rest is the author's content. Click a rule ⇒ its detail opens in Details (R-4118).",
                                "Timeline view (the clock toggle) stays the default in Live and Review."], C.NOTE)
CXT = X + WW + 16
s.callout(X + WW - 8, Y + 100, CXT, Y + 92, ["ONE LINE PER RULE, AS A SENTENCE: the trigger", "(When … for … → do …) and an objective (Take and", "hold … with … for …) read the same way the", "condition editor writes them (ONE sentence renderer)"])
s.callout(X + WW - 34, Y + 236, CXT, Y + 210, ["an incomplete rule is marked, never hidden"])
s.note(CXT, Y + 40, ["List ⇄ Timeline (the two toggles, top left): two views", "of the same rows (Q1)"])
# ── the gallery ──────────────────────────────────────────────────────
GX, GY, GW, GH = 20, 480, 1200, 236
s.window(GX, GY, GW, GH, "New rule — choose a starting point")
s.field(GX + 12, GY + 30, 300, "      search", hint=True); s.glyph(FA.SEARCH, GX + 18, GY + 33, C.DIS, 12)
cols = [("Objectives", FA.TARGET, C.GREEN, ["Take and hold an area", "Reach an area", "Destroy", "Protect", "Survive"]),
        ("Phases and timing", FA.CLOCK, C.BLUE, ["Begin a phase when …", "End the exercise when …"]),
        ("Reinforcements", FA.USERS, C.ORANGE, ["Bring in … when …"]),
        ("Messages", FA.BULLHORN, C.VIOLET, ["Brief Blue when …", "Tell everyone when …"]),
        ("Scoring", FA.FLAG_CHECKERED, C.AMBER, ["Hit ratio", "Report latency", "… (exercise designers)"])]
cx = GX + 12
for title, g, gc, items in cols:
    s.glyph(g, cx, GY + 62, gc, 14); s.text(cx + 22, GY + 75, title, C.TXT, 13.5)
    for i, it in enumerate(items):
        if title == "Objectives" and i == 0: s.rect(cx + 16, GY + 84 + i * 22, 210, 21, "#2C3A52", rx=2)
        s.text(cx + 22, GY + 99 + i * 22, it, C.SUB, 12.5)
    cx += 236
s.text(GX + 12, GY + GH - 14, "Each opens pre-filled in Details; from a unit's menu (Add rule about this unit…) the unit is already filled in (§2.5).", C.NOTE, 11.5)
s.save(sys.argv[1])
