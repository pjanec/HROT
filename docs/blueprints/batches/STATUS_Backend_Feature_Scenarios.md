<!--STATUS
state: LIVE — in progress (item 2 of HANDOFF_Coordinator_To_Backend_Regression_Suite.md)
updated: 2026-11-16
current-answer: the scenario table, the defects table and the lane decisions
related-designs:
  - ../../DESIGN_Scenario_Regression.md — the checks grammar (§3), the golden (§4), the two tiers (§11)
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — the plays IM-1 / IM-2 / IM-5 (§9), the dash (§9a)
  - ../../designs/animals/DESIGN_Animals.md — the animals (§9)
-->
# STATUS — backend: the FEATURE-TEST SCENARIOS (R-4277; CE-4621, CE-4623)

Handoff: [`HANDOFF_Coordinator_To_Backend_Feature_Scenarios.md`](HANDOFF_Coordinator_To_Backend_Feature_Scenarios.md), dispatched at `816edc3b9`.
⭐ **Goldens captured `2026-11-16` for the 8 that pass** (`--verify-capture`, 8 of 8 PASS). Every scenario: `scenario.json` (What · Judge), `checks.json` (one `expect` row per Judge line), a golden once it passes.
DONE = `scripts/scenario-regression.sh <name>` passes (the deterministic editor tier).

## The scenarios

| scenario | state | what the run found |
|---|---|---|
| `ua-moto-drive-by` | ✅ PASS (6/6) | the pass aimed past the post along the bike→post bearing: the bike left the road and drove up to the sentries (fixed, R-3089); a test-town escape past x = 500 was off the map (authoring, R-3095) |
| `ua-moto-drive-by-grenade` | ✅ PASS (6/6) | the throw was ordered at 40 m — beyond a hand throw — and burst short (fixed: within 25 m or abeam, R-3093); the 2002 pillion carries no grenade (authoring: a 2004 grenadier) |
| `ua-moto-parked-bomb` | ✅ PASS (9/9) | `ChargeTimer` was never registered — no bomb could be armed outside the rails (**CE-3456**, fixed); the bike had no cargo station for the charge (fixed, R-3088); the riders stood beside their bomb (fixed: they walk clear, R-3090); a road start stalls the bike (**CE-3452**, filed; the play retries, R-3092); "everyone out" set the charge down and it drifted ~270 m with the bike's velocity before it burst, so no shopper was hurt (**CE-3459**, fixed) |
| `ua-moto-swarm-raid` | ⚠ 10/11 — blocked on CE-3454 | every bike reaches its approach point, they close together, ride out and rally ✔ (after `SwarmRaid` fulfils `Dash`, R-3096); the pillions never fire — the play leaves fire to their own weapon brains (its design), and a firing-seat man never engages alone (**CE-3454**, behaviors) |
| `ua-moto-drone-dash` (+ `-offroad`) | ⏳ not authored | no scenario uses `DroneOperator` yet, and the dash's FPV pursuit is the drone lane's machinery; authoring it needs a measured FPV-vs-bike chase first — left for the next batch (R-106: this item stops, the rest go on) |
| `ua-animal-guard-dog` | ✅ PASS (5/5) | an attacking animal read "unarmed" (**CE-3455**, fixed); two soldiers started off the map and never moved (authoring, R-3097 — the engine's silence is **CE-3458**) |
| `ua-animal-horse-rider` | ✅ PASS | the horse gallops, never bolts, keeps its rider ✔; the rider never engaged on his own (**CE-3454**, filed to behaviors) — the scenario orders his fire, which works at a gallop (20 rounds, hits) |
| `ua-animal-flock-flush` | ✅ PASS (6/6) | animals crowded every man's target memory (**CE-3453**, fixed); the patrol's destination and two ambushers were off the 300 m farm and stood still for good (authoring, R-3097; **CE-3458**) |
| `ua-animal-herd-road` | ✅ PASS (6/6) | the tank panics the herd, it flees, the convoy goes on, no cow run over ✔; the second APC started off the map (authoring, R-3097); vehicles never yield to animals (**CE-3457**, filed) |
| `ua-animal-village-fight` | ✅ PASS (5/5) | the animal crowding (CE-3453); Blue started off the map (R-3097); the villagers stood ~180 m from the fight and a civilian hears 100 m (authoring: Red by the farmyard); the anchor waited for a Blue shot — Blue, shot at in the open, break without firing back — the firefight now starts on any shot (R-3097) |

## The defects the scenarios found

| id | what | where fixed / filed | rail |
|---|---|---|---|
| R-3089 | `DriveBy` passed UP to an off-road post, not alongside | `DriveByBehavior.PassPoint` | `DriveByBehaviorTests.CE4621_DriveBy_PassesAlongside_OnItsOwnLine_NotUpToAPostOffTheRoad` (red-proved) |
| R-3093 | the throw ordered out of range | `DriveByBehavior.InThrowingRange` | `…ThePillionThrowsAGrenadeAtThePass…` extended |
| R-3088 | the motorcycle had no cargo station | `NedTkbCatalog.Motorcycle` (the pannier) | the scenario |
| R-3090 | `ParkedBomb`'s riders never walked away | `ParkedBombBehavior.Leaving` + `Leave` param | `ParkedBombBehaviorTests.CE4621_ParkedBomb_OnceTheRidersAreOff_OrdersThemToWalkClear_BackTheWayTheyCame` (red-proved) |
| **CE-3452** | a bike's road start reports `FailedBlocked` while moving | OPEN (navigation) | — |
| **CE-3453** | calm animals + animal areas crowded the target memory | `Animals.NotAContact` in `ThreatEvaluationSystem` | `ThreatEvaluationSystemTests.CE3453_…` |
| **CE-3454** | a firing-seat man never engages on his own | OPEN (behaviors) | — |
| **CE-3455** | an attacking dog read unarmed | `ThreatDanger.Of` | `ArmamentTests.CE3455_AnAttackingDog_IsAFullDanger_ACalmOneIsNone` |
| **CE-3456** | `ChargeTimer` never registered | `HrotSharedComponentRegistry` | `SharedRegistryCoversTheSharedPackTests.CE3456_TheChargeTimer_IsRegisteredOnEveryNode` |
| **CE-3457** | vehicles never yield to animals on their road | OPEN (navigation / traffic) | — |
| **CE-3458** | an unreachable move never fails — the unit stands still for good, re-issuing every 0.1 s | OPEN (navigation + `MoveToLocation`) | — |
| **CE-3459** | "everyone out" set down a stowed cargo charge, which kept the carrier's velocity | `Embarkation.EjectAll` / `Leave` | `ChargesTests.CE3459_EveryoneOut_LeavesTheCargoInTheBoot_AndTheMenSetDownStand` (red-proved) |

## Lane decisions (backend block)

| id | decision | what would change it |
|---|---|---|
| R-3088 | the motorcycle carries one stowed, concealed cargo station after its two seats (the car's boot, CE-3233) | a design that puts a charge elsewhere (worn by the pillion) |
| R-3089 | the pass runs along the bike's own heading to 30 m past the target's abeam point | a road-following pass (the traffic handover, L-14) |
| R-3090 | `ParkedBomb` orders the riders to walk to `Leave` (default 80 m back along the heading) once they are off | a getaway bike (L-13) |
| R-3091 | the farm terrain has no road network: `ua-animal-herd-road` has no SUMO traffic; "drawn dead, no wreck" is the ui lane's drawing, not judged | a farm road network |
| R-3092 | `ParkedBomb` re-orders a blocked ride up to 3 times | CE-3452 fixed at its root |
| R-3093 | a grenade is thrown within 25 m of the target or once it is abeam; else on the way out | a measured throw range per throwable |
| R-3094 | a world with no repository (the cluster tier) judges the state cues on the dumped components through AnchorBook's own comparisons | — |
| R-3096 | `SwarmRaid` fulfils `Dash` (no contact reaction halts it) | a dedicated `Raid` order kind |
| R-3097 | authoring: every authored point inside the terrain (the farm is 300 m); the village fight sits within a civilian's 100 m hearing; its firefight starts on any shot | CE-3458 landing (an off-map order would fail loudly) |
| R-3095 | authoring: test-town is 400 m wide, so the drive-by escapes past x = 340 (140 m beyond the post); the sentries hold `ReturnFire` (an unsuspecting post); the grenade pillion is a 2004; the rider is ordered to fire (CE-3454) | a larger terrain; CE-3454 landing |
