# Generates docs/UX/field-editor-visual.svg — THE shared field editor (DESIGN_Field_Declaration_Editor.md §6): one table, two modes (DEFINE the
# fields · FILL the values), substructures and lists as expanding rows, and three hosts that differ only by their settings. House style (R-4143).
# Regenerate: python3 scripts/gen-field-editor-mockup.py docs/UX/field-editor-visual.svg
import sys
from mockup_kit import Svg, C, FA, text_width

W, H = 1280, 1850
s = Svg(W, H, "The shared field editor — one table, two modes, substructures; hosts differ only by settings (design §6)",
        "Scope: the table and its popups. Window chrome and the hosts' other sections are context — unchanged.")

KIND = {"plain": (C.SUB, None), "enum": (C.TEAL, None), "ref": (C.BLUE, None), "struct": (C.VIOLET, FA.CUBES), "list": (C.ORANGE, None)}


def type_chip(x, y, label, kind):
    """The type: plain text in the colour of its kind (user 2026-11-07: no ovals)."""
    col, _ = KIND[kind]
    s.text(x, y + 17, label, col, 12)


def table(x, y, w, cols, rows, vcol=None):
    """cols: [(label, rel_x)]; rows: dicts {depth, open, name, type, kind, value, state ('def'|'over'|'ro'|'live'), extra:{col:text}, badge, sel, err}"""
    s.rect(x, y, w, 24, C.HDR)
    for lab, rx in cols: s.text(x + rx, y + 16, lab, C.SUB, 12)
    yy = y + 24
    for i, r in enumerate(rows):
        h = 28
        s.rect(x, yy, w, h, C.BTNSEL if r.get("sel") else ("#151519" if i % 2 else C.BG))
        d = r.get("depth", 0); ix = x + 8 + d * 18
        for k in range(d): s.line(x + 15 + k * 18, yy, x + 15 + k * 18, yy + h, "#34343E")       # indent guides
        if r.get("open") is not None: s.chevron(ix, yy + 9, r["open"])
        s.text(ix + 16, yy + 18, r["name"], C.DIS if r.get("state") == "ro_name" else C.TXT, 12.5)
        if r.get("badge"): s.text(ix + 22 + text_width(r["name"], 12.5), yy + 18, r["badge"], C.AMBER, 11)
        tx = x + dict(cols)["Type"] if "Type" in dict(cols) else None
        if tx: type_chip(tx, yy + 1, r["type"], r["kind"])
        vx = x + dict(cols)[vcol or ("Value" if "Value" in dict(cols) else "Default")]
        v, st = r.get("value"), r.get("state", "def")
        if v is not None:
            if st == "over":
                s.rect(vx - 6, yy + 3, 4, 22, C.AMBER); s.field(vx, yy + 3, 170, v)
                s.glyph(FA.ROTATE_LEFT, vx + 178, yy + 7, C.AMBER, 12)
            elif st == "summary":
                s.text(vx, yy + 18, v, C.DIS, 12)
            elif st == "live":
                s.text(vx, yy + 18, v, C.GREEN, 12.5)
            else:
                s.field(vx, yy + 3, 170, v, c=C.DIS if st == "def" else C.TXT)
        if r.get("kind") == "ref" and v:                                         # a SEMANTIC type: ⌖ show on map (always), ⊕ pick (when editable)
            mx = vx + (204 if st == "over" else 178)
            s.glyph(FA.LOCATION, mx, yy + 7, C.TEAL, 12.5)
            if st not in ("live", "ro"): s.glyph(FA.CROSSHAIRS, mx + 20, yy + 7, C.GREY, 12.5)
        for col, txt in (r.get("extra") or {}).items():
            s.text(x + dict(cols)[col], yy + 18, txt, C.DIS, 12)
        if r.get("err"):
            yy += h; s.rect(x, yy, w, 22, C.ERRROW); s.glyph(FA.WARNING, ix + 16, yy + 5, C.ERR, 11)
            s.text(ix + 34, yy + 15, r["err"], C.ERR, 11.5); h = 22
        yy += h
    return yy


# ════════ A — the GENERIC form, DEFINE mode, substructures allowed (a params asset — CE-3376) ════════
X, Y, WW = 20, 60, 760
s.window(X, Y, WW, 498, "Params — AmbushParams   (a params asset, CE-3376 — the generic form)")
s.text(X + 14, Y + 46, "DEFINE mode — the author declares the fields; Default is the value a new user of the type starts from.", C.SUB, 12)
cols = [("Name", 8), ("Type", 230), ("Default", 400), ("Used by", 690)]
rows = [
    dict(name="triggerRange", type="float · m", kind="plain", value="120", state="set", extra={"Used by": "3"}),
    dict(name="side", type="ForceId", kind="enum", value="Hostile", state="set", extra={"Used by": "2"}),
    dict(name="killZone", type="AreaLink", kind="ref", value="@KillZone", state="set", extra={"Used by": "1"}),
    dict(name="rallyPoint", type="WorldPoint", kind="ref", value="668 · 427 m", state="set", extra={"Used by": "1"}),
    dict(name="timing", type="AmbushTiming", kind="struct", open=True, value="3 fields", state="summary", extra={"Used by": "2"}),
    dict(depth=1, name="minDelay", type="float · s", kind="plain", value="4", state="set"),
    dict(depth=1, name="maxDelay", type="float · s", kind="plain", value="9", state="set"),
    dict(depth=1, name="waitForContact", type="bool", kind="plain", value="true", state="set"),
    dict(name="fallback", type="RetreatOrder", kind="struct", open=False, value="5 fields", state="summary", extra={"Used by": "1"}),
    dict(name="routes", type="list of PointLink", kind="list", open=True, value="2 items", state="summary", extra={"Used by": "1"}),
    dict(depth=1, name="[0]", type="PointLink", kind="ref", value="@RouteA", state="set"),
    dict(depth=1, name="[1]", type="PointLink", kind="ref", value="@RouteB", state="set", sel=True),
    dict(depth=1, name="max range", type="float", kind="plain", value="", state="set", err="'max range' is not a name — letters, digits and _ only"),
]
yy = table(X + 8, Y + 58, WW - 16, cols, rows)
s.button(X + 16, yy + 8, 116, "Add field", glyph=FA.PLUS, gc=C.OK); s.button(X + 140, yy + 8, 140, "Add to timing", glyph=FA.PLUS, gc=C.OK)
s.button(X + 288, yy + 8, 100, "Add item", glyph=FA.PLUS, gc=C.ORANGE)
s.text(X + 400, yy + 24, "Ctrl+Z / Ctrl+Y · double-click a name to rename · drag ⋮⋮ to move", C.DIS, 11.5)
# the row menu
s.menu(X + WW + 14, Y + 392, 220, [("Rename", "F2", None, None), ("Duplicate", None, FA.COPY, C.GREY), ("Move up / down", None, None, None),
                                     ("Open type AmbushTiming ↗", None, FA.CUBES, C.VIOLET), "---", ("Delete", "Del", FA.TRASH, C.RED, "used by 2")])
s.note(X + WW + 14, Y + 70, ["The type, as text in its kind's colour:", "  grey  = plain value", "  teal  = enum", "  blue  = semantic: a place / an entity",
                             "  violet = structure (expands)", "  orange = list (expands, + item)", "",
                             "A semantic value carries the map:", "  ⌖ teal = show on map (hover previews,", "     click pins) — always, read-only too",
                             "  ⊕ = pick it on the map (when editable)", "",
                             "A structure row shows a summary", "(\"3 fields\") and opens with ▸.", "",
                             "A structure is a NAMED type (D1):", "its fields here are the type's —", "edit the type itself with",
                             "\"Open type ↗\" — the row menu:"], C.SUB, 11.5)

# ════════ B — the SAME table, FILL mode, substructures (a unit's behaviour params / an asset's defaults) ════════
BX, BY = 20, 588
s.window(BX, BY, WW, 358, "Details — AI — Ambush   (FILL mode: values for one unit)")
s.text(BX + 14, BY + 46, "FILL mode — names and types are fixed (grey); every value is the default until changed. A closed structure says what changed.", C.SUB, 12)
cols = [("Name", 8), ("Type", 230), ("Value", 400)]
rows = [
    dict(name="triggerRange", type="float · m", kind="plain", value="150", state="over"),
    dict(name="side", type="ForceId", kind="enum", value="Hostile", state="def"),
    dict(name="killZone", type="AreaLink", kind="ref", value="@NorthBridge", state="over"),
    dict(name="rallyPoint", type="WorldPoint", kind="ref", value="668 · 427 m", state="def"),
    dict(name="timing", type="AmbushTiming", kind="struct", open=True, value="1 changed", state="summary"),
    dict(depth=1, name="minDelay", type="float · s", kind="plain", value="4", state="def"),
    dict(depth=1, name="maxDelay", type="float · s", kind="plain", value="15", state="over"),
    dict(depth=1, name="waitForContact", type="bool", kind="plain", value="true", state="def"),
    dict(name="fallback", type="RetreatOrder", kind="struct", open=False, badge="● 2 changed", value="5 fields", state="summary"),
]
table(BX + 8, BY + 58, WW - 16, cols, rows)
s.note(BX + WW + 14, BY + 60, ["The ONE value form (presets §5d):", "  default = grey, still editable", "  changed = amber bar + ↺",
                               "  closed structure = ● N changed", "", "Hosts in FILL mode:", "  • a unit's behaviour params (Details ▸ AI)",
                               "  • a behaviour asset's params defaults", "  • a macro instance's args", "  • a scenario preset's values", "",
                               "Reset all = the ⋮ of the section."], C.SUB, 11.5)

# ════════ C — the SCENARIO PARAMETERS specialization (flat; presets) ════════
CX, CY = 20, 976
s.window(CX, CY, WW, 300, "Details — Scenario — Parameters   (DEFINE + the preset's values)")
s.text(CX + 14, CY + 40, "Preset", C.SUB, 13); s.combo(CX + 70, CY + 26, 240, "Night attack", glyph=FA.EYE, gc=C.VIOLET)
s.text(CX + 324, CY + 40, "(presets design §5a — the combo, + and ⋮ are drawn there)", C.DIS, 11.5)
cols = [("Name", 8), ("Type", 200), ("Value · Night attack", 360), ("Used by", 690)]
rows = [
    dict(name="enemyStrength", type="int", kind="plain", value="3", state="over", extra={"Used by": "4"}),
    dict(name="startTime", type="time", kind="plain", value="02:30", state="over", extra={"Used by": "1"}),
    dict(name="weather", type="Weather", kind="enum", value="Clear", state="def", extra={"Used by": "2"}),
    dict(name="assembly", type="AreaLink", kind="ref", value="@Village", state="def", extra={"Used by": "3"}),
]
yy = table(CX + 8, CY + 58, WW - 16, cols, rows, vcol="Value · Night attack")
s.button(CX + 16, yy + 8, 140, "Add parameter", glyph=FA.PLUS, gc=C.OK)
s.text(CX + 170, yy + 24, "flat: no ▸ rows — the Type list offers no structures or lists", C.DIS, 11.5)

# ════════ D — the BLACKBOARD specialization (live value, bytes, budget) ════════
DX, DY = 20, 1306
s.window(DX, DY, WW, 236, "BTree — Blackboard   (DEFINE + LIVE — the same table)")
s.text(DX + 14, DY + 40, "No byte column, no memory budget (user 2026-11-07: the limit is ~16 KB now, nobody counts bytes).", C.SUB, 12)
cols = [("Name", 8), ("Type", 200), ("Live · Alpha 1-1", 360)]
rows = [
    dict(name="target", type="EntityRef", kind="ref", value="Bravo 2-3", state="live"),
    dict(name="lastSeen", type="WorldPoint", kind="ref", value="(512.3, 88.0, 2.1)", state="live"),
    dict(name="threat", type="float", kind="plain", value="0.72", state="live"),
    dict(name="mode", type="AttackMode", kind="enum", value="Suppress", state="live"),
]
yy = table(DX + 8, DY + 52, WW - 16, cols, rows, vcol="Live · Alpha 1-1")
s.button(DX + 16, yy + 8, 116, "Add field", glyph=FA.PLUS, gc=C.OK); s.text(DX + 144, yy + 24, "live values read-only (green) · ⌖ still shows a place on the map", C.DIS, 11.5)

# ════════ E — the Type picker (Add field / the Type cell) ════════
EX, EY = 810, 876
s.rect(EX, EY, 450, 380, C.POPUP, stroke=C.BORDER)
s.text(EX + 12, EY + 22, "Type", C.SUB, 12.5); s.field(EX + 60, EY + 8, 378, "      search types", hint=True); s.glyph(FA.SEARCH, EX + 66, EY + 11, C.DIS, 12)
gy = EY + 44
for grp, items, dis in [("Values", [("float", "plain"), ("int", "plain"), ("bool", "plain"), ("time", "plain"), ("string", "plain")], False),
                        ("Enums", [("ForceId", "enum"), ("Weather", "enum")], False),
                        ("References / places", [("EntityLink", "ref"), ("PointLink", "ref"), ("AreaLink", "ref")], False),
                        ("Structures", [("AmbushTiming", "struct"), ("RetreatOrder", "struct")], True),
                        ("Lists", [("list of …", "list")], True)]:
    s.text(EX + 14, gy + 14, grp, C.DIS if dis else C.SUB, 12); gy += 20
    x = EX + 20
    for lab, k in items:
        type_chip(x, gy - 6, lab, k); x += text_width(lab, 12) + 18
    if dis:
        s.raw(f"<rect x='{EX + 14}' y='{gy - 6}' width='420' height='24' fill='{C.POPUP}' opacity='0.55'/>")
    gy += 30
s.tooltip(EX + 130, gy - 10, 300, ["Structures and lists are off for", "scenario parameters (the host's setting)."])
s.note(EX, EY + 392, ["The same picker for every host; a group the host's settings forbid", "is shown greyed with the reason, never hidden (§5a of AQ108: gates", "say why)."], C.SUB, 11.5)

# ════════ F — the settings, per host ════════
FX, FY = 810, 1326
s.window(FX, FY, 450, 500, "One table · the hosts differ ONLY by these settings")
hdr = ["host", "mode", "structs", "columns"]
xs = [FX + 12, FX + 190, FX + 260, FX + 320]
for h, x in zip(hdr, xs): s.text(x, FY + 46, h, C.SUB, 12)
s.line(FX + 8, FY + 54, FX + 442, FY + 54, C.SEP)
data = [("Params asset (CE-3376)", "define", "✓", "Default · Used by"), ("Scenario parameters", "define+fill", "—", "Value(preset) · Used by"),
        ("BTree / HSM blackboard", "define", "—", "Live"), ("Blueprint variables (×2)", "define", "—", "Live"),
        ("Unit's behaviour params", "fill", "✓", "Value"), ("Behaviour asset defaults", "fill", "✓", "Value"),
        ("Macro instance args", "fill", "—", "Value"), ("Scenario preset values", "fill", "—", "Value")]
ry = FY + 76
for row in data:
    for v, x in zip(row, xs): s.text(x, ry, v, C.TXT if x == xs[0] else C.DIS, 11.5)
    ry += 26
s.note(FX + 12, ry + 6, ["Settings (DeclarationFeatures + the mode):", "  Mode: Define · Fill · Define+Fill", "  AllowStructFields · AllowLists",
                         "  TypeGroups (what the picker offers)", "  Columns: Default · Value · Live · UsedBy", "  RemoveUnused",
                         "  Words: \"Add parameter\" / \"Add field\" …", "", "⛔ never a host name tested in the table"], C.SUB, 11.5)
s.save(sys.argv[1] if len(sys.argv) > 1 else "docs/UX/field-editor-visual.svg")
