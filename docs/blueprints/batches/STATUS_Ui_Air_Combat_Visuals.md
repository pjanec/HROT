<!--STATUS
state: LIVE — ui air combat looks (CE-4647 AC-15), unattended
updated: 2026-11-17
current-answer: the slice table
related-designs:
  - ../../designs/air-attack/DESIGN_Air_Combat.md — THE design (§4d the looks, §5a jets, §5b missiles, §7 ids)
  - HANDOFF_Coordinator_To_Ui_Air_Combat_Visuals.md — the dispatch (369177d6d)
  - NOTE_Air_To_Ui_Air_Combat_Contract.md — the air lane's contract (when it lands)
-->

# STATUS — ui: air combat looks (CE-4647 / AC-15)

Started `4fa79837f` (merged `coordinator@369177d6d+`). Engine half (`air-wip`) NOT yet merged — built against `*StandIn`s.

| step | state | evidence |
|---|---|---|
| 1 mockups | ✅ | `scripts/gen-air-combat-mockup.py` → `docs/designs/air-attack/air-combat-{body,stores,hud,arena}.svg`, linked from DESIGN_Air_Combat §4d (one-line cross-lane doc edit, said in the commit); PNGs in the scratchpad `aircombat/`. The MiG numbers in the script ARE the kit table of step 2 |
| 2 MiG body | ⏳ | |
| 3 stores on rails | ⏳ | |
| 4 AAM looks · burn trail · launch | ⏳ | |
| 5 afterburner · vortex · dead-jet smoke | ⏳ | |
| 6 arena gizmo + 2-D/3-D | ⏳ | |
| 7 fighter HUD | ⏳ | |

## Lane decisions (for the user's review)
none yet.

## Gates
none yet.
