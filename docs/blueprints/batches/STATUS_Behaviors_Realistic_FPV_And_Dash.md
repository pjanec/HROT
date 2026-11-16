<!--STATUS
state: LIVE — in progress, overnight unattended
updated: 2026-11-16
current-answer: the slice table, then §1 decisions and §3 gates
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — M-4 (MC-3…MC-7, the dash) — the as-built folds there (§9)
  - ../../designs/vehicle-tactics/DESIGN_FPV_Drone.md — MC-4a, as built in §5e
  - HANDOFF_Coordinator_To_Behaviors_Assault_Closeout_And_Drones.md — the dispatch (part 2, frozen at 7bea87df1)
-->

# STATUS — the realistic FPV and the grey-zone dash (CE-4621 M-4), behaviors — overnight, unattended

| item | state | result |
|---|---|---|
| **MC-4a** the FPV's terminal guidance (handoff item 8) | ✅ built | its own perceived track (sightings every 0.2 s, velocity from two, never read), 0.4 s operator delay, commit at time-to-go 0.7 s, a look-ahead dive line, the lead iterated once — FPV §5e. 📐 a 1 s, 8 m/s² break at time-to-go 1 s is missed by **4.53 m** (formula ½·a·1.1² = 4.84 m); the same with no delay 3.33 m |
| **MC-3…MC-7** the dash (handoff item 9) | ⏳ building | — |
| ⚠ the hearing mask (handoff: "backend built it — verify") | ⛔ **NOT BUILT** — measured: `AcousticPerception.TryEars` masks by weather only (`AcousticPerception.cs:261-263`); no moving-vehicle term exists anywhere under `Perception/`. ⇒ built here with the dash | — |
| drone demos before / after | ⏳ after the dash builds | `ua-drone-shelter`, `sam-site`, `city-battle` |

## 1. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2122 | MC-4a: the perceived track is the CAMERA's view (the target's position as this node replicates it, sampled every 0.2 s) — not the drone's `TargetMemory`, whose positions move at the sensor's rate and freshness, while an FPV operator watches a live feed. Its velocity is estimated from two sightings, never read. Delay 0.4 s, commit 0.7 s; the dive flies a look-ahead point down its 60° line (the old point over the target made it curl down and turn at the commit) | a live run where the drone dives on a target its camera could not see (then: the memory slot gates the sighting) |

## 3. Gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet test Hrot.SimHost.Tests --filter AirAttackNodes\|AirDefence\|ShelterFromAir\|DroneTeam\|DroneOperator\|AirPlatform\|CE1431` plain · `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | ✓ | 64 / 64 · 64 / 64 |
| hosting golden (`HROT_RECORD_HOSTING=1 … CE1431`) | ✓ | 3 moved lines, each +80 B (the track in `AirAttackState`): `AirArmedSearch` 288 → 368, `AirPlatformAttack` 216 → 296, `AirPlatformStrike` 216 → 296; nothing else |
| rails reconciled (never loosened) | — | `S6e_AnFpvDrone_…`: every pre-commit order on the 60° line (± 0.5 m), the committed one runs into the ground · `S6e_ADroneDivesOnAnEvadingTank_…`: a swerve seen before the window still hit < 1.5 m, one inside it within Δv·1.1, ≥ 3 / 6 hit — 📐 4 / 6 hit |
| MC-4a red-proofs (`scratchpad/mutate17.py`) | — | no commit ⇒ RED · operator delay 0 ⇒ RED (the rail pins the design's 0.7 + 0.4, not the constants under test); restored, the tree clean |
| 0 B per frame | — | the dive adds value-type maths only (no new allocation path) |

## 4. Ids allocated
rulings `R-2122`
