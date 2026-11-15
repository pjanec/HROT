<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — ⭐ THE design (MC-1, PL-1…PL-3; §6 M-1v, M-2p)
  - ../../UX/DESIGN_Colour_Icons.md — the house style the mockup follows (R-4139)
-->

# HANDOFF — coordinator → `ui`: the motorcycle's look and the player on it (CE-4621 M-1v, M-2p) — OVERNIGHT, UNATTENDED

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `91ab130e1`** *(re-stamped, rule 1a — unstarted; the fixed ids, overnight unattended)*; ⛔ scope FROZEN there.

🔒 User, `2026-11-15`: *"the UI might take the visual part of it"* · *"the motorcycle also needs to be possessable and player can also embark it as a
passenger (the one that can shoot while motorcycle drives)"* (R-4263).


## ⭐⭐⭐ OVERNIGHT, UNATTENDED — read this before anything else

🔒 User, `2026-11-15`: *"make the handoffs the overnight, unattended ones, meaning that the AI agent takes decisions on its own based on his best
judgment and doesn't stop until the feature is finished."*

| the rule | |
|---|---|
| ⭐⭐⭐ **NOBODY IS THERE TO ANSWER** — never ask, never wait for approval, never end a turn on a question | every open choice: measure it, decide by the claim table (how the code IS + how the design MEANT it), build it, and record it as an `R-` lane decision *"for the user's review"* with what would change it |
| ⭐⭐⭐ **DO NOT STOP UNTIL EVERY ITEM IS BUILT, GATED AND REPORTED** | a slice finishing is not the end — go straight to the next; the batch ends only when the last item's rails are green, its live check is done and the report is written |
| ⭐⭐ **a failed premise stops ONE item, never the batch** (R-106) | write what failed and why in the status file, then do every item that does not depend on it; come back to it if a later item unblocks it |
| ⭐⭐ **a defect you find is fixed where it lives**, with a rail in that feature's own suite (T-1), its design folded (obligation ⑤) — even if it is outside your slice, when small; a large one is filed as a row and worked around | |
| ⭐⭐ **the other lane may not have landed** | build against a stand-in (a test template with the fixed ids below), keep going, re-bind when its work merges; `git fetch` the other lane at each slice's end and merge it when it has what you need |
| ⭐ **commit and push at every slice's end**, and update the status file then — a container restart or a compaction resumes from it | |
| ⭐ **when context runs low**: write the status file's *next step* precisely, then continue — never leave the batch half-reported | |
| ⛔ **the only things you may NOT decide**: anything this handoff marks *NOT yours*, and reversing a user ruling — those are filed as findings in the report | |

## ⭐ The entity — fixed for BOTH lanes ([design §2a](../../designs/vehicle-tactics/DESIGN_Motorcycle.md))

| what | the value |
|---|---|
| TKB type | `TkbEntityTypes.Motorcycle = 115` ("Motorcycle"; one type for soldiers, insurgents and civilian traffic) |
| DIS | Kind 1 · Domain 1 · Category 80 (SISO *Motorcycle*) · Subcategory 1 |
| entity class | `"Motorcycle"` = `1.1.*.80` in `EntityClassTable.cs` |
| 3-D family | `VisualFamily.Motorcycle` keyed by `["Motorcycle"]` in `FamilyOfClass` + its `ShapeKit` |
| seats | 0 = Driver (the rider) · 1 = the pillion, a FIRING seat (arc: both sides and the rear) |
| SUMO | the type's own `TrafficVehicleDto { VClass = "motorcycle" }` |
⭐ **YOU declare** the family and its kit and map `["Motorcycle"]`; ⛔ NOT the type, DIS or class (backend does) — until they land, build against a test template with that DIS and class, then bind to `115`.

## 0. Start
`git fetch origin coordinator ui backend`; on `ui`, merge `origin/coordinator`; push `chore: started motorcycle visuals at <sha>`. Status:
`STATUS_Ui_Motorcycle_Visuals.md`. ⭐ `backend` builds the type in its M-1 (CE-4621) at the same time — until it lands, build against a test
template with the class you agree in your notes; when backend's note names the type id and seat ids, bind to them (the radar SAM precedent).

## 1. The items
| # | builds | done when |
|---|---|---|
| **U-1** | ⭐ **a mockup FIRST** (house style, `scripts/mockup_kit.py`, R-4143): the bike in 2-D and 3-D, a rider and a pillion seated, the pillion's fire arc when selected | committed as SVG beside the design |
| **U-2** | the `Motorcycle` visual family + `ShapeKit` (TWO wheels — `CarKits` draws four; the riders seated, the pillion turned to his arc while he fires), the 2-D symbol, the picker glyph and the thumbnail | drawn in 2-D and 3-D; a frame rail under `xvfb-run -a` (report counts — a skip is not a pass); the mockup and the frame side by side in the report (⛔ not in git) |
| **U-3** | **PL-2 in first person**: a possessed man who *Boards* the pillion — the camera at the seat's eye, free look, fire inside the arc; outside it the HUD shows *out of arc* (backend's refusal reason) and nothing fires; ⚠ backend makes the sim side work (its M-2p) — you own the camera, the input and the HUD | a rail: the HUD cue appears outside the arc; a live check: board, fire, see the cue |
| **U-4** | **PL-1 driving** — the bike under the player's *Drive* through the existing first-person driving; the lean in a turn is visual only | a live check: drive, slowed off-road |

## 2. Gates and report
T-1 first (the 3-D model rails, the possession / first-person suites); display rails under `xvfb-run -a`; each new rail red-proved; 0 B per
steady frame (R-2084). Morning report: `STATUS_Ui_Motorcycle_Visuals.md` (with every lane decision taken, for the user's review) — per item, the fidelity side-by-side, the gate-report contract (rule 8), ids.
