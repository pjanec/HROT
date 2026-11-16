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
| **MC-3…MC-7** the dash (handoff item 9) | ✅ built (in-process; ⛔ no live scenario yet) | `GreyZoneDash` + `DashRide` (both `Fulfils = Dash`), the lookout's break, riders out at the edge, the riding hearing mask — Motorcycle §9a |
| ⭐ **the M-4 acceptance — the measured share** | ⚠ **FINDING** | seeded, in process (40 dashes each, the REAL dive guidance + rotorcraft model, the bike driven by the rail): closest pass **no break 0.1–0.8 m** (every burst lethal) · **road break 2.9–4.9 m** (100 % beyond the 2 m lethal radius, **0 % beyond the 5 m casualty radius**) · **off-road break 1.7–2.8 m** (47 % / 0 %). ⇒ at the design's commit 0.7 + delay 0.4 the largest miss a turn can make is ½·8·1.1² = 4.84 m < 5 m: **"riders UNHURT in a measured share" is 0 %**; a break turns a kill into a casualty on the road. ⭐ For your call (my lean below) |
| ⚠ the hearing mask (handoff: "backend built it — verify") | ⛔ was **NOT BUILT** — measured: `AcousticPerception.TryEars` masked by weather only; ✅ built here (`RidingFactor`, × 0.25 above 5 m/s) | R-2123 |
| drone demos before / after | ⏳ after the dash builds | `ua-drone-shelter`, `sam-site`, `city-battle` |

## 1. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2122 | MC-4a: the perceived track is the CAMERA's view (the target's position as this node replicates it, sampled every 0.2 s) — not the drone's `TargetMemory`, whose positions move at the sensor's rate and freshness, while an FPV operator watches a live feed. Its velocity is estimated from two sightings, never read. Delay 0.4 s, commit 0.7 s; the dive flies a look-ahead point down its 60° line (the old point over the target made it curl down and turn at the commit) | a live run where the drone dives on a target its camera could not see (then: the memory slot gates the sighting) |

## 2. ⭐ The finding, and my lean

| option | what changes | lean |
|---|---|---|
| **A** keep the numbers; the acceptance reads *"a break escapes the LETHAL radius"* (road 100 %, off-road 47 %) | the design's §6 M-4 wording | ⭐ **lean** — the guidance is measured to its formula, and an operator delay 0.4 is mid-range of the design's 0.3–0.5; a bike's riders being wounded rather than killed is the realistic outcome |
| B operator delay 0.5 (the design's upper end) | max miss ½·8·1.2² = 5.8 m ⇒ some road breaks beyond 5 m | tuning to pass; changes every drone scenario again |
| C measure the warhead's real casualty radius in the damage chain (the 5 m is MV-1's PG-7 figure) | possibly nothing | worth one rail if A is not accepted |

## 3. Gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet test Hrot.SimHost.Tests --filter AirAttackNodes\|AirDefence\|ShelterFromAir\|DroneTeam\|DroneOperator\|AirPlatform\|CE1431` plain · `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | ✓ | 64 / 64 · 64 / 64 |
| hosting golden (`HROT_RECORD_HOSTING=1 … CE1431`) | ✓ | 3 moved lines, each +80 B (the track in `AirAttackState`): `AirArmedSearch` 288 → 368, `AirPlatformAttack` 216 → 296, `AirPlatformStrike` 216 → 296; nothing else |
| rails reconciled (never loosened) | — | `S6e_AnFpvDrone_…`: every pre-commit order on the 60° line (± 0.5 m), the committed one runs into the ground · `S6e_ADroneDivesOnAnEvadingTank_…`: a swerve seen before the window still hit < 1.5 m, one inside it within Δv·1.1, ≥ 3 / 6 hit — 📐 4 / 6 hit |
| MC-4a red-proofs (`scratchpad/mutate17.py`) | — | no commit ⇒ RED · operator delay 0 ⇒ RED (the rail pins the design's 0.7 + 0.4, not the constants under test); restored, the tree clean |
| 0 B per frame | — | the dive adds value-type maths only (no new allocation path) |
| dash: `--filter GreyZoneDash\|ParkedBomb\|DriveBy\|SwarmRaid\|SquadManeuverTests\|AirAttackNodes\|ShelterFromAir\|BasicVehicleSop\|CE1431\|SopParamsRoundTrip\|ShippedTreesAreCSharp` plain · PAR + READ_CHECK + WORLD_GUARD | ✓ | 365 / 365 · 362 / 365 at first — the 3 reds were the hand-ticked harness's STALE last-tick view (no `Repo.Tick()`, CE-2437's root cause; STATUS_Backend_Motorcycle's finding) — ⭐ after the harness fix: `GreyZoneDash` + `SwarmRaid` + `DriveBy` + `ParkedBomb` all GREEN under PAR + READ_CHECK + WORLD_GUARD (no undeclared read: the `TargetMemory` declaration holds) |
| hosting golden | ✓ | PURELY ADDITIVE: `DashRide` 128 B, `GreyZoneDash` 120 B |
| FDP `--filter Perception\|Behavior` | ✓ | 832 / 832 (the riding rail included) |
| dash red-proofs (`scratchpad/mutate18.py`) | — | spacing dropped · the time-to-go test dropped · the break side toward the drone · no eject · `Fulfils` dropped from `DashRide` · the riding factor dropped ⇒ each RED; restored, the tree clean |
| the share rail `CE4621_M4_SeededDashes_…` | ✓ | 1 / 1 (pins the finding: road 100 % beyond 2 m, 0 % beyond 5 m) |
| ⏳ stepped-hash 1,4,8 · drone demos live (`ua-drone-shelter`, `sam-site`, `city-battle`) | — | in the live batch (below) |

## 4. Ids allocated
rulings `R-2122`, `R-2123`
