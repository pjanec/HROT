<!--STATUS
state: LIVE — in progress, unattended
updated: 2026-11-16
current-answer: the slice table, then §1 decisions, §2 gates
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — THE design; the as-built folds into it
  - HANDOFF_Coordinator_To_Behaviors_Checkpoint_And_Ramming.md — the dispatch (b933ddd7d)
-->

# STATUS — `behaviors`: the checkpoint, ramming and the car bomb (CE-4622, K-1…K-8) — unattended

Started after the minefield batch was reported (`5f02a8cd0`); merged `coordinator` at `f4c649fdb` (contains the dispatch `b933ddd7d`).

| slice | state | result |
|---|---|---|
| **K-1** `VehicleImpactSystem` (RM-1) | ✅ built `bb865ae81` | the ground twin of `DroneImpactSystem` in `GroundKinematicsModule`, right after `CarKinematicsSystem`: a moving hull's FRONT swept this frame (`Intersection2D.SweptFrontMeetsCircle`, beside the flyer's `SegmentInCircle`) strikes a person / an animal with a Blunt `DamageAssessedEvent` (Source = the vehicle) of a Health fraction by closing speed × √(mass / 1500) in bands, ±15 % seeded; never riders, never under 2 m/s, a pair once per 2 s — ⏳ the 30 vehicle scenarios re-checked (running) |
| **K-2** `RamOrder` + `RamTarget` (RM-2, RM-3, RM-5) | ✅ built `21a71e455` | `RamOrder` (126 / `dtRamOrder` 196, Brain group, translators) on the hull; the avoidance skips the target (every person for a crowd); the break gate opens for an active order (never in a replay), full-throttle push; `RamTarget`: the order, LETHAL on the car + its driver (follow-ups), pursuit of the `TargetLead` point every 0.5 s, ends with the order OFF — design §5b · ⭐ cross-lane: `MineClearing` added to the Brain group (terrain's: in no group, so a split cluster never carried the clearer's switch) |
| **K-3** the engine zone (CP-6, CP-7) | ✅ built `d482c771a`, `b7afdb64b` | the shipped Car profile already HAD the front Engine zone (Mobility) — KV-3 was stale; built: `AimPoint.Engine` (the disabling shots' aim), a ramming car whose engine is hit COASTS (`CoastDecel` 0.7 m/s²), a penetrating Kinetic hit on a soft skin (≤ 10 mm side) reaches one non-station occupant at 30 %, the seat-0 man out of the fight clears the car's `CanMove` — design §5b |
| **K-4** site + `CheckpointPlay` + `VehicleApproachWatch` (CP-1, CP-1a, CP-2) | 📋 | |
| **K-5** the vehicle ladder (CP-3) | 📋 | |
| **K-6** the search (CP-4) | 📋 | |
| **K-7 + K-8** the feature scenarios | 📋 | |

## 1. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2130 | K-3: a car whose engine is hit COASTS only while it rams (a committed driver keeps rolling; any other disabled car brakes as before — a coasting routine car would be a hazard nobody chose); the driver is seat 0 (his loss clears the car's `CanMove`, where its capability is owned); a penetrating Kinetic hit on a soft skin (side armour ≤ 10 mm, not a person carrier) reaches ONE non-station occupant at 30 % (seeded per hit), with the same damage — never through armour. Starter values | a live checkpoint run where the coast carries a disabled car through the stand-off too often (shorten it) · an occupant rule that needs the hit's location |
| R-2129 | K-2: the ram order is switched OFF (`Active` 0), never removed — the off then replicates as a change (the `MineClearing` pattern); the driver is the vehicle's seat-0 occupant; both LETHAL acts are follow-ups inside the parallel phase (`HostileActs.Commit` marks witnesses — other entities); ⛔ NO "direct move inside 50 m": the navigation move has no path-less flag and its flags byte is full — inside 50 m the pursuit aims at the target itself through the planner | `cp-ram-barrier` showing the car paths ROUND a weak barrier instead of through it (then a path-less move is needed — a navigation change, backend's) |
| R-2128 | K-1: the "ONE shared swept test" is a shared PRIMITIVE beside the flyer's (`Intersection2D.SweptFrontMeetsCircle` next to `SegmentInCircle`) — a hull is a rectangle sweeping forward, a flyer a point sweeping a segment; one function for both would be a rectangle-or-point switch, not sharing. Harm bands (starter values, §4): effective speed = closing × √(mass / 1500 kg), capped ×0.5…×2; < 2 m/s nothing; 2–5 → 10–35 % Health; 5–9 → 35–75 %; 9–15 → 75–110 %; above → 120 %; ±15 % seeded per (vehicle, body, 0.1 s). Boarding is exempt by the 2 m/s floor (a man boards a stopped vehicle; there is no boarding marker). Animals are struck like people. No knock-back: the Blunt hit's knock-down (3 s) is the damage chain's | a live run where a crowd-ram looks too lethal / too soft (tune the bands) · a need for knock-back |

## 2. Gates

| command | `--no-build` | result |
|---|---|---|
| K-1: `Fdp.Toolkits.Tests` `VehicleImpactSystemTests` + `DroneImpactSystemTests` + `GroundKinematicsModuleTests` | ✓ (after a clean build) | 19 / 19; red-proofs (`mutate27.py`): the mass term dropped ⇒ RED · the swept front reduced to the current box ⇒ RED (2) · the rider exemption dropped ⇒ RED. Serial system (not a brain): the parallel checks do not apply to it; 0 B steady (a reused body buffer; the pair map grows only on a strike) |
| K-2 engine: `Fdp.Toolkits.Tests` `CE4622` + `CE3287` + `CE4598` + `CE6014` + `VehicleImpactSystemTests` | ✓ | 26 / 26 (the ramming hull's four rows, the break gate) |
| K-2 behaviour + wire: `Hrot.SimHost.Tests` `RamTargetBehaviorTests` + `EntityGroundDamageTranslatorTests` + `SopParamsRoundTrip` + `ShippedTreesAreCSharp` + `MinefieldBreachBehaviorTests` (+ the hosting golden plain), plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 47 / 47 · 44 / 44; golden additive `+RamTarget` 40 B |
| K-2 red-proofs (`mutate28.py`) | — | the lead dropped · the car's act dropped · the order left on (2) · the crowd flag dropped · the wire's `Crowd` copy dropped · the avoidance's target skip dropped · the break gate's `RamOrder` test dropped ⇒ each RED (7 / 7) |
| K-3: `Fdp.Toolkits.Tests` `CE4622` + `EmbarkedStateSystemTests` + `CarKinematicsSystemTests` · `Hrot.SimHost.Tests` `SoftSkinCarTests` | ✓ | 31 / 31 · 1 / 1. Red-proofs (`mutate29.py`): the armour test dropped · the driver rule dropped · the coast off · the aim from the rear ⇒ each RED (4 / 4). Serial systems: the parallel checks do not apply; 0 B steady (no buffers added; the occupant hit reads last frame's events) |

## 3. Ids allocated
rulings `R-2128`, `R-2129`, `R-2130`
