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
⭐ Every scenario: `scenario.json` (What · Judge), `checks.json` (one `expect` row per Judge line), a golden once it passes.
DONE = `scripts/scenario-regression.sh <name>` passes (the deterministic editor tier).

## The scenarios

| scenario | state | what the run found |
|---|---|---|
| `ua-moto-drive-by` | ✅ PASS (6/6) | the pass aimed past the post along the bike→post bearing: the bike left the road and drove up to the sentries (fixed, R-3089); a test-town escape past x = 500 was off the map (authoring, R-3095) |
| `ua-moto-drive-by-grenade` | ✅ PASS (6/6) | the throw was ordered at 40 m — beyond a hand throw — and burst short (fixed: within 25 m or abeam, R-3093); the 2002 pillion carries no grenade (authoring: a 2004 grenadier) |
| `ua-moto-parked-bomb` | ⏳ re-running | `ChargeTimer` was never registered — no bomb could be armed outside the rails (**CE-3456**, fixed); the bike had no cargo station for the charge (fixed, R-3088); the riders stood beside their bomb (fixed: they walk clear, R-3090); a road start stalls the bike (**CE-3452**, filed; the play retries, R-3092) |
| `ua-moto-swarm-raid` | ⏳ investigating | no bike reached its approach point (first run) |
| `ua-moto-drone-dash` (+ `-offroad`) | ⏳ not yet authored | no scenario uses `DroneOperator` yet |
| `ua-animal-guard-dog` | ⏳ re-running | the dog barks, bites and turns Dangerous ✔; nobody fired — an attacking animal read "unarmed" (**CE-3455**, fixed) |
| `ua-animal-horse-rider` | ⏳ re-running | the horse gallops, never bolts, keeps its rider ✔; the rider never engaged on his own (**CE-3454**, filed to behaviors) — the scenario orders his fire, which works at a gallop (20 rounds, hits) |
| `ua-animal-flock-flush` | ⏳ re-running | the flock lifts off and falls silent ✔; nobody fired — animals crowded every man's target memory (**CE-3453**, fixed); the patrol walked out of sight behind the pasture fence (authoring) |
| `ua-animal-herd-road` | ⏳ re-running | the tank panics the herd, it flees, the convoy goes on, no cow run over ✔; timing re-authored so the convoy is seen to halt first |
| `ua-animal-village-fight` | ⏳ re-running | no shot: the animal crowding (CE-3453) and Red behind the fence (authoring) |

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
| R-3095 | authoring: test-town is 400 m wide, so the drive-by escapes past x = 340 (140 m beyond the post); the sentries hold `ReturnFire` (an unsuspecting post); the grenade pillion is a 2004; the rider is ordered to fire (CE-3454) | a larger terrain; CE-3454 landing |
