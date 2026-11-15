<!--STATUS
state: LIVE — dispatched, not started — ⭐ OVERNIGHT, UNATTENDED
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — ⭐ THE design (§2 decisions as folded, §3 UML, §6 slices; §8 / §8a the review record)
  - ../../DESIGN_Operations_Behaviour_Catalogue.md — §12 the analysis and the open sources
-->

# HANDOFF — coordinator → `backend`: the motorcycle, the engine half and the insurgent plays (CE-4621) — OVERNIGHT, UNATTENDED

**For:** the `backend` lane (its own id blocks). **From:** coordinator. **Dispatched at `91ab130e1`** *(re-stamped, rule 1a — unstarted; the fixed ids, overnight unattended)*; ⛔ scope FROZEN there — later documents are
FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-15`: *"I think the backend lane is already finished with the air defense job and might take this one. And the UI might take the
visual part of it."* · decisions R-4261, R-4262, R-4263.


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
⭐ **YOU declare** the type, DIS, class, seats and the SUMO row; ⛔ NOT the family (ui does).

## 0. Start
`git fetch origin coordinator backend ui behaviors`; on `backend`, merge `origin/coordinator` (and `origin/behaviors` — it carries the newest
behaviour code you build beside); restore the test projects you run; push `chore: started motorcycle at <sha>`. Status:
`STATUS_Backend_Motorcycle.md`, updated at each slice. Read the design END TO END first (R-129) — §2 and §6 are current, §8 is the record.

## 1. How you work
| | |
|---|---|
| ⭐⭐ **decide, never wait** — every default the design names is yours to tune, recorded as an `R-` lane decision with what would change it | |
| ⭐⭐ **every engine change GROWS an existing seam** (§3): the integrator's params, the woods grid, the station arc, `StrippedOn`, the acoustic mask, THE ONE GATE — ⛔ never a parallel copy | ruling 9 |
| ⭐⭐ **never compare a behaviour name** — the conduct categories (CD-1…CD-6) are the mechanism | 🔒 user |
| ⭐⭐ **the TERRAIN work is YOURS tonight** — no terrain lane runs (🔒 user: *"Let backend do it, or the UI do it. No third lane will be running overnight."*): the surface-class grid (generalising `TerrainWoods`, `TerrainWorld`), the per-mover nav pricing (`NavDoorways` / `NavLayerSelection` / the Recast provider's filter), the woods opened to a wheeled type at its factor — cross-lane edits into the terrain lane's files, said in each commit | the terrain lane's id block is NOT yours — allocate from your own |
| ⭐ **cross-lane edits allowed and said** — `Hrot.AI.Behaviors` (the SOP reactions' conduct flags, the plays), `Hrot.Traffic` (the vType) | |
| ⭐ **ids**: allocate the missile / type ids from what is FREE on every lane (the radar SAM's 6047 collided — measure first) | |
| ⛔ **NOT yours**: MC-4a (the FPV's realistic terminal guidance) and the dash's acceptance runs (M-4) — they change every drone scenario and wait for `behaviors` after its AU-6; the 3-D model and the first-person HUD are `ui`'s (CE-4621 M-1v / M-2p) | |

## 2. The order — the design's §6
| # | slice | done when (§6) |
|---|---|---|
| 1 | **M-1** MC-2 / MC-2a the per-type off-road factors, the surface-class grid, the nav pricing + MC-1 the `Motorcycle` type (Driver + a firing pillion seat, its SUMO vType) | §6 M-1 — ⭐ every existing vehicle suite and scenario hash unchanged |
| 2 | **M-2** PF-1…PF-4 the firing seat (the station arc generalised + wrap, seat placement, `StrippedOn` + `RestoreHitCapabilities`, the rider's `HoldMoving`) | §6 M-2 |
| 3 | **M-2p, the sim half** — PL-1…PL-3: a possessed man DRIVES the bike (*Drive*) and RIDES PILLION and fires (*Board*): ⚠ measure the first-person fire path for an EMBARKED man first; make the sim side work (the fire request from a seated, possessed man; the arc refusal as a reason the HUD can show) — the camera and HUD are ui's | possess → Drive → slowed off-road; possess → Board → fire inside the arc only, a refusal reason outside it |
| 4 | **M-3** CD-1…CD-6 the conduct flags + `OrderConduct` + `OrderKind.Dash` + THE ONE GATE's `ForbiddenByOrder` (and `HoldsFirm` folded into the table) | §6 M-3 — ⚠ the refused row falls through, never loops |
| 5 | **M-5** `DriveBy`, `ParkedBomb`, `ChargeTimer` (`Charges.Ask(charge, Null)`) | §6 M-5 |
| 6 | **M-6** `SwarmRaid` + dispersal | §6 M-6 |

## 3. Gates — every slice
T-1 first (each feature's own suites: the kinematics, embark / seats, weapon systems, the ingress gate, charges, traffic); every behaviour rail
with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`; each new rail red-proved by a compiling mutation; `stepped-hash.py
--threads 1,4,8`; 0 B per steady frame (R-2084); invariant output (R-4221). Build the TEST project in the fix loop; live runs in the background.

## 4. The morning report — `STATUS_Backend_Motorcycle.md`
per slice built / stopped and the design folded (obligation ⑤) · lane decisions with what would change each · the gate-report contract (rule 8)
· ids allocated · ⭐ a note to `ui` naming the type id, the seat ids and the refusal reason the HUD shows.
