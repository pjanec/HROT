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
| **K-2** `RamOrder` + `RamTarget` (RM-2, RM-3, RM-5) | 📋 | |
| **K-3** the engine zone (CP-6, CP-7) | 📋 | |
| **K-4** site + `CheckpointPlay` + `VehicleApproachWatch` (CP-1, CP-1a, CP-2) | 📋 | |
| **K-5** the vehicle ladder (CP-3) | 📋 | |
| **K-6** the search (CP-4) | 📋 | |
| **K-7 + K-8** the feature scenarios | 📋 | |

## 1. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2128 | K-1: the "ONE shared swept test" is a shared PRIMITIVE beside the flyer's (`Intersection2D.SweptFrontMeetsCircle` next to `SegmentInCircle`) — a hull is a rectangle sweeping forward, a flyer a point sweeping a segment; one function for both would be a rectangle-or-point switch, not sharing. Harm bands (starter values, §4): effective speed = closing × √(mass / 1500 kg), capped ×0.5…×2; < 2 m/s nothing; 2–5 → 10–35 % Health; 5–9 → 35–75 %; 9–15 → 75–110 %; above → 120 %; ±15 % seeded per (vehicle, body, 0.1 s). Boarding is exempt by the 2 m/s floor (a man boards a stopped vehicle; there is no boarding marker). Animals are struck like people. No knock-back: the Blunt hit's knock-down (3 s) is the damage chain's | a live run where a crowd-ram looks too lethal / too soft (tune the bands) · a need for knock-back |

## 2. Gates

| command | `--no-build` | result |
|---|---|---|
| K-1: `Fdp.Toolkits.Tests` `VehicleImpactSystemTests` + `DroneImpactSystemTests` + `GroundKinematicsModuleTests` | ✓ (after a clean build) | 19 / 19; red-proofs (`mutate27.py`): the mass term dropped ⇒ RED · the swept front reduced to the current box ⇒ RED (2) · the rider exemption dropped ⇒ RED. Serial system (not a brain): the parallel checks do not apply to it; 0 B steady (a reused body buffer; the pair map grows only on a strike) |

## 3. Ids allocated
rulings `R-2128`
