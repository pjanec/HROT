<!--STATUS
state: LIVE — in progress, overnight unattended
updated: 2026-11-16
current-answer: the slice table, then §1 decisions, §2 gates
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — THE design; the behaviours as built fold into §11
  - HANDOFF_Coordinator_To_Behaviors_Minefields.md — the dispatch (re-stamped at b0393bbbb)
  - NOTE_Terrain_To_All_Minefield_Contract.md — the engine contract in numbers (terrain)
-->

# STATUS — `behaviors`: minefields, the BEHAVIOURS (CE-4624, MS-B1…MS-B4) — overnight, unattended

Started at `0c2195f1d` (merged `coordinator@66d47feba`); merged `terrain@b1f83aeb0` (the contract NOTE + stubs, and its fix of the component-id
collision 500 that broke every SimHost world after the coordinator merge).

| item | state | result |
|---|---|---|
| §0a approvals | ✅ | R-2106…R-2124 marked approved in both status files; the dash row closed by R-4275 |
| CE-2441 (StationWeapon cross-entity write) | ✅ `aa96708ae` | always ask (`RestationEvent`, serial `CarryStationSystem`); red without / green with under PAR + checks |
| **MS-B1** `MineStrike` | ✅ built | the reflex (`MineStrikeReflexSystem`, CGF pack) + the drill: halt, every man holds in place, a vehicle stops (crew aboard), hold until marked / 120 s; Halts \| LeavesRoute — design §11 |
| **MS-B2** `ReconMinefield` | ✅ built `b19a3da10` | seek → probe at detector pace → left-hand trace on swept-clear cells → marked when closed; every probe a `MinefieldCellsSwept` — design §11 |
| **MS-B3** `ClearLane` + breach + crossing | ✅ built | `ClearLane` (mouth → lane at the tool's pace, the switch through ONE seam — ⚠ a STAND-IN until terrain MS-T4: `NOTE_Behaviors_To_Terrain_Minefield_Switch.md`), `CrossLane` (single file on the lane's axis), `Breach` (far-side fire + smoke → the lane → the crossing) — design §11 |
| **MS-B4** the march | ✅ built | the march asks for `Minefield`; a minefield ahead is its OWN SOP row (never a crossing): halt, every man holds, ONE `Engineers` request at the near side, the march resumes when the area is gone — design §11 · ⚠ no HQ answers the request yet (CE-2442) |
| **Acceptance demo** (MR-19) | ⛔ STOPPED (R-106) | needs terrain MS-T1 (the trigger), MS-T3 (the danger kind from knowledge) and MS-T4 (the swath clear + the switch) — none on `origin/terrain` at `2026-11-16`; filed CE-2443 with the plan |

⚠ **The engine is a stub today** (terrain MS-T1…MS-T4 in progress): `PlaceKnowledgeTable` answers Unknown / not marked; no trigger raises
`MineStruck` yet. The behaviours bind to the §6.1 names and are railed by raising the events themselves.

## 1. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2127 | MS-B4: a minefield ahead is its own SOP row before the crossing row and `DangerAreaAhead` is false for it — never crossed as a drill; the squad halts in place, every man holds, ONE `Engineers` request (new `RequestKind.Engineers` = 6, appended) at the field's near side; Success when the area is gone or after 900 s, and the march resumes. No HQ answer yet (CE-2442: no engineer asset kind, the request carries no field id) | an HQ engineer asset landing (then the request is answered with `ReconMinefield` → `Breach`) · a live run where 900 s is too short |
| R-2126 | MS-B3: the clearer's tool switch is ONE seam (`ClearerEquipment.SetActive`), a stand-in until terrain names its switch (a NOTE sent); the brain raises no `LaneCleared` (the event carries no lane geometry — the engine's swath clear owns the new lane); `CrossLane` assembles on the lane's AXIS 10 m short and sends one man at a time 5 m apart straight along it — no man leaves the lane by construction; `Breach` roles from the roster (the vehicle clears, the last foot unit crosses, the rest support from 40 m back on the axis, firing 60 m past the far end, smoke asked there) | terrain's switch / lane bookkeeping landing (re-point the seam) · a live run where the assault should cross before the clearer has left the lane |
| R-2125 | MS-B1: the drill HOLDS IN PLACE instead of backing out — no per-mover breadcrumb record exists (MR-5 puts it in the engine, terrain MS-T2); every live man of the struck unit is ordered to hold, a vehicle stops, the crew stays aboard; the unit goes on once its side has MARKED the field or after 120 s. The "report up the chain" IS the knowledge path (the trigger's `MineStruck` → the side's `PlaceKnowledgeSystem`). `MineStrike` is the ONE reaction allowed to declare `LeavesRoute` (the conduct rail names it): a unit holding a position that sets off a mine keeps holding | terrain's per-mover breadcrumbs landing (then: back out along them) |

## 2. Gates

| command | `--no-build` | result |
|---|---|---|
| MS-B1: `--filter MineStrike\|SquadManeuverTests\|BasicInfantrySop\|BasicVehicleSop\|GrenadeReflex\|CE1431\|SopParamsRoundTrip\|ShippedTreesAreCSharp` plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 348 / 348 · 344 / 348 — the 4 reds are the long-standing `BasicInfantrySopTests` CE3306 / CE4035 / CE2332 / CE1269 (red in every PAR gate of the assault batch, before this work) |
| hosting golden | ✓ | additive: `+MineStrike` 40 B; ⚠ also `+AnimalHsm` (0 B) — the coordinator's animals, merged without its golden line |
| the conduct rail `CE4621_TheConductTable_…` | ✓ | names `MineStrike` as the one ruled `LeavesRoute` declarer (MR-6) |
| MS-B1 red-proofs (`mutate20.py`) | — | hold orders dropped · the stop dropped · the conduct dropped ⇒ each RED; ⭐ the reflex's per-field dedupe dropped ⇒ GREEN — it was redundant (`SopActions.React` already refuses a running identical reaction), so it was REMOVED |
| MS-B2: `--filter` minefield + squad suites, plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 322 / 322 · 322 / 322; golden additive `+ReconMinefield` 520 B; red-proofs (`mutate21/22.py`): the left-hand order reversed ⇒ RED · the closure test dropped ⇒ RED · the never-enter guard dropped (`if (!mined \|\| s.Phase == Trace)`) ⇒ RED |
| MS-B3: same, plain · PAR | ✓ | 325 / 325 · 325 / 325; golden additive `+Breach` 56 B, `+ClearLane` 44 B, `+CrossLane` 48 B; red-proofs (`mutate22.py`): the lane at the driving speed ⇒ RED · the 5 m spacing wait dropped ⇒ RED · the crossing ordered with the clearing ⇒ RED |
| MS-B4: `--filter SquadManeuverTests\|MineStrike…\|ReconMinefield…\|MinefieldBreach…\|BasicSquadSop` (+ the hosting golden plain) plain · PAR | ✓ | 294 / 294 · 291 / 291; golden additive `+MinefieldAhead` 0 B; red-proofs (`mutate23.py`): the SOP row's condition swapped ⇒ RED · the crossing exclusion dropped ⇒ RED · (the hold, the request, the march kind and the resume — running) |

## 3. Ids allocated
rulings `R-2125`, `R-2126`, `R-2127` · tracker `CE-2442` (no HQ answers an Engineers request), `CE-2443` (the acceptance demo, blocked on terrain)
