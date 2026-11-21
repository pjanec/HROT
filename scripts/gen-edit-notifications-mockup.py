# Generates docs/UX/edit-notifications-visual.svg — the edit framework's outcome surface (DESIGN_Edit_Framework.md §2.5, F7), house style (R-4143).
# Scope (R-4151): ONLY the notification stack at the window's top-right edge and a field's busy / refused mark. The map and the Details form
# around them are CONTEXT, drawn unchanged. Regenerate: python3 scripts/gen-edit-notifications-mockup.py <out.svg>
import sys
from mockup_kit import Svg, C, FA
W, H = 1240, 560
s = Svg(W, H, "Edit outcomes — a sticky notification per refused / lost / conflicting edit, the field marks (house style; Edit framework §2.5, F7)")
# context: the editor window (unchanged)
X, Y, WW, HH = 20, 60, 860, 470
s.window(X, Y, WW, HH, "HROT Editor — showcase-crossfire", chip=("Live", "#7FD69A", "#163A24"))
s.rect(X + 6, Y + 26, 520, HH - 32, "#14202C"); s.text(X + 266, Y + 250, "map (context — unchanged)", C.NOTE, 12, "middle")
# context: the Details form with two field marks
FX = X + 534; FW = WW - 540
s.rect(FX, Y + 26, FW, HH - 32, C.BG, stroke=C.BORDER)
s.separator_text(FX + 10, Y + 300, "Details — Alpha 1-1 (context)", FX + FW - 10)
LX, CX, CW = FX + 12, FX + 110, FW - 150
y = Y + 324
s.text(LX, y + 14, "Name"); s.field(CX, y, CW, "Alpha 1-1"); y += 26
s.text(LX, y + 14, "Side"); s.combo(CX, y, CW, "Friend"); s.glyph(FA.HOURGLASS, CX + CW + 8, y + 3, C.PEND, 13); y += 26
s.rect(FX + 4, y - 2, FW - 8, 24, C.ERRROW); s.text(LX, y + 14, "Stance"); s.combo(CX, y, CW, "Prone"); s.glyph(FA.CIRCLE_XMARK, CX + CW + 8, y + 3, C.ERR, 13); y += 26
s.tooltip(CX - 20, y + 4, 190, ["Stance refused — this unit's", "type cannot go prone"], edge=C.ERR)
# the stack: top-right of the window, newest on top
NX, NW = X + WW - 360 - 12, 360
ny = Y + 34
s.notification(NX, ny, NW, C.ERR, FA.CIRCLE_XMARK, "Delete refused", "Alpha 1-1 is a player's unit", buttons=("Show",)); ny += 70
s.notification(NX, ny, NW, C.PEND, FA.WARNING, "Commit mission plan conflicts", "the plan changed (version 4, yours 3)", buttons=("Reload", "Overwrite")); ny += 70
s.notification(NX, ny, NW, C.ERR, FA.CLOCK, "Embark got no answer", "the owner did not answer in 5 s", buttons=("Show",)); ny += 70
s.text(NX + NW - 10, ny + 4, "+ 2 more  ·  Dismiss all", C.SUB, 11.5, "end")
# callouts
CXT = X + WW + 24
s.callout(NX + NW, Y + 60, CXT, Y + 60, ["STICKY, NON-MODAL — a refused, lost or conflicting edit", "stays until its × (newest on top); work goes on under it.", "Show selects the unit / opens the field (Lost and Refused);", "Reload / Overwrite on a Conflict (the mission plan)."])
s.callout(NX + NW, Y + 238, CXT, Y + 150, ["at most 3 drawn; '+ N more' and Dismiss all below.", "Applied edits are SILENT (the value is simply there)."])
s.callout(CX + CW + 22, Y + 376, CXT, Y + 240, ["BUSY ⏳ beside the field after 1 s of Pending;", "it clears when the owner answers."])
s.callout(CX + CW + 22, Y + 402, CXT, Y + 296, ["REFUSED ✗ on the field, the row tinted; the reason", "is its tooltip; a click on the mark clears it (FormRow, U-11)."])
s.note(CXT, Y + 370, ["One rule of what shows: EditNotifications (Fdp.Toolkits),", "read from the ONE EditJournal — the overlay and the", "Messages history read the same entries."], C.SUB)
s.save(sys.argv[1])
