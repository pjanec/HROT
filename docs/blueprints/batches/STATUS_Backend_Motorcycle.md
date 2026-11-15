<!--STATUS
state: LIVE — in progress, overnight unattended
updated: 2026-11-15
current-answer: the whole file (the slice table first, then "Next step")
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — THE design; as-built folded there (§9)
  - HANDOFF_Coordinator_To_Backend_Motorcycle.md — the dispatch (frozen at 464ca44f9)
-->

# STATUS — the motorcycle, backend half (CE-4621) — overnight, unattended

Started at `94c637bee` (merged `coordinator@f5e0a1df8`, `behaviors@9099edff5`); marker `a2e399011`.

| slice | state | result |
|---|---|---|
| **M-1** MC-2 / MC-2a / MC-1 | ✅ built | per-type factors on `VehicleParametersDto` → `VehicleParams` (0 = 1.0); `SurfaceClassGrid` (5 m, forest from the woods grid, scrub, roads from the road network; built once in the integrator's serial pre-pass, only when a factored type moves); the integrator applies the factor + the 0.45 g dirt grip; nav: `NavLayerSelection` woods-factor bits 9…12 + the Recast provider prices woods at 1 / factor; the `Motorcycle` type 115 (DIS 1.1.*.80.1, class `Motorcycle`, rider + pillion, SUMO `motorcycle` weight 0); the traffic catalogue registers zero-weight types |
| **M-2** PF-1…PF-4 | ✅ built | `CarrierStations.FiringMask` / `StoppedOnlyMask` (a person's seat with an arc); `StrippedOn` keeps CanShoot on a firing seat; `InStationArc` → `ArcSeat` + the wrapping `InTraverse`; the man sits at the seat's place (`EmbarkedStateSystem`); `RestoreHitCapabilities` follows the seat; the AI path (`HoldOf`) and the operator path (`OperatorFire`) both ask ONE `SeatHold` (OutOfArc / HoldMoving for a stopped-only seat) |
| **M-2p** sim half | ✅ built | measured in code: an embarked possessed man's `OperatorControl` runs `OperateWeapon` (his own weapon channel); before, `OperatorFire` ignored the seat ⇒ a pillion fired anywhere. Now `OperatorFire` asks `SeatHold` (OutOfArc / HoldMoving, said to the HUD through `WeaponPostureOf`); a possessed RIDER drives through `UpdateVehicle`, so the off-road factor applies (PL-1) |
| **M-3** conduct + the gate | ✅ built | `Conduct` flags on `[Behavior]` (13 reactions declared); `OrderConduct` beside `OrderKind` (+ `Dash = 14`); `OrderKinds.Build` installs the FDP `BehaviorConduct` table; THE ONE GATE (`AdmitsWithReactions`) refuses `ForbiddenByOrder`; `SopActions.React` asks with the reaction's id ⇒ Failure ⇒ the next row; journaled; CD-5 `CannotComply`; `HoldsFirm` reads the table |
| **M-5** DriveBy · ParkedBomb · ChargeTimer | ✅ built | `ChargeTimer` (recorded, id 486) + `ChargeTimers.Arm` (parallel-safe follow-up) ticked in `ChargeSystem` → `Charges.Ask(charge, Null)`; `ParkedBomb` (vehicle: ride, park, arm, eject the riders); `DriveBy` (bike: ride up, the pillion ordered to `FireAtTarget`, a slow pass, the escape) |
| **M-6** SwarmRaid | ✅ built | a commander over bikes: N bearings → converge → fight → divergent dispersal → rally |

## Lane decisions (for the user's review — each says what would change it)

| # | decision | why · what would change it |
|---|---|---|
| L-1 | the surface grid's cell is **5 m**, not the woods' 25 m | a 25 m cell cannot tell a carriageway from the field beside it; the forest is still read from the woods grid (one source). A memory limit on huge maps doubles the cell |
| L-2 | **road = the road network's carriageway** stamped into the grid (lanes × width / 2), not the nav mode | a vehicle on a navmesh route along a road is on a road; ⚠ a road the road network lacks (only drawn) is open ground |
| L-3 | the grid is built only once a type with authored factors moves | no scenario without a bike pays the build (hashes and timings unchanged) |
| L-4 | the tracked-in-woods cap is KEPT as is, beside the factor | folding it into the factor table would change every tracked scenario; a tracked type that authors a forest factor gets both (the lower wins) |
| L-5 | **PF-4 grows the `HoldMoving` gate by a seat flag** (`SeatDto.FiresStoppedOnly`), not the mount's `FiresOnTheMove` | the design assumed the mount gate; a rifle fires on the move, so only a SEAT rule holds the rider. The rider seat is a firing seat (front and sides) |
| L-6 | the SUMO catalogue registers EVERY `TrafficVehicleDto` type, a zero weight included | SumoSharp never spawns a zero weight (its cumulative mix skips it) ⇒ the traffic mix is unchanged; our handed-over bike drives as a motorcycle (MV-10). Civilian bikes in traffic (IM-7) = give the type a positive weight |
| L-7 | the motorcycle has NO faction | "the side from the faction" — the scenario sets it |
| L-8 | PF-2's **rough-ride factor is NOT built**; the moving-shooter law applies (the man's velocity is the bike's) | the hit model has no access to the surface grid; filed |
| L-9 | **no current reaction declares `LeavesRoute`** — `ShelterFromAir` / `SquadShelterFromAir` declare Halts \| SeeksCover (§8a's example said LeavesRoute \| SeeksCover) | the table makes Defend / Hold forbid LeavesRoute (CD-2, approved); declaring it on shelter would make every DEFENDING unit stay put under drones — a behaviour change nobody measured. With SeeksCover the dash still refuses shelter. Declaring LeavesRoute on a reaction = one attribute edit (a rail lists any that does) |
| L-10 | **the conduct check sits in `AdmitsWithReactions`** (the check `SopActions.React` asks first), with the reaction's id passed in | a refusal only in `Admit` arrives after the SOP row already returned Success ⇒ no fall-through (the inventory measured it, `SopActions.cs:59`) |
| L-11 | **no exemption for `ReactionUrgency.Break`** (unlike the ROE's StayOnTask) | the dash's order is absolute until the unit cannot comply (CD-5); self-defence fire declares no conduct (CD-6) |
| L-12 | **no production behaviour fulfils `Dash` yet** | the dash play (`GreyZoneDash`, MC-3) sits on the FPV work (M-4, not this handoff); the kind, the table and the gate are ready for it |
| L-13 | `ParkedBomb` runs on the VEHICLE and puts ALL riders out (`EjectPassengersCommand`); their leaving is their own SOP; no getaway bike, no secondary device | the design's "leave on foot / a waiting bike" needs a two-entity play; the timer + the eject are the engine half. A getaway = a second unit's `MoveToLocation` |
| L-14 | `DriveBy` starts from OUR bike already in play — no `TrafficHandover` before the act, no re-blend after | the handover is an engine path of its own (MV-10) that no rail drives yet; the play is reachable from a mission today |
| L-15 | `SwarmRaid` fights MOUNTED (held at the objective, the rider's stopped-only seat fires too); no dismount, no casualty pick-up, no night rule | dismounting a roster of bikes and re-boarding them is SquadDismount / SquadEmbark's work at the squad level; filed as the next step |
| L-16 | `ChargeTimer`'s component id is **486** (the coordinator / new-lane block) | the backend block 434–449 is full; 486 measured free on every lane |

## Gates (rule 8) — M-1 … M-3

| gate | command | result |
|---|---|---|
| T-1 toolkit suite (all) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 5092 / 5100 pass, 1 skip; ⚠ 7 red at the time: 1 mine (the woods rail's assertion — fixed, `PathCost` is the route LENGTH) + 6 `S0*TypesRail` / `S0Walker` reds naming ONLY other lanes' members (terrain `BladeCut` / `CraterRuleTable` / `TerrainBlock.HeadingDeg`, `SupportByFire*` / `DroneTeam*` DTOs) — none of my types; arrived with the coordinator / behaviors merge at the start marker |
| woods + nav + gate + SOP | `--filter TerrainWoodsTests\|CE3427\|BehaviorIngress\|SopActions` then `SopSlotTests\|BrainJournalTests` | 85 / 85 · 44 / 44 (the CE3427 0 B warm path-cost rail included — a closure fix in `FilterFor` made it 0 B again) |
| Hrot.Core | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build` | 373 / 373 |
| Hrot.Traffic | `dotnet test Hrot/Engine/Hrot.Traffic.Tests --no-build` | 85 / 85 (N2_T1 now expects the weight-0 bike registered) |
| squad / dismount / shelter / convoy | `dotnet test Hrot.SimHost.Tests --filter SquadManeuverTests\|SquadDismount\|ShelterFromAir\|Convoy` | 278 / 278; ⭐ again with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`: 286 / 286 (the conduct check's member / carrier reads raised no undeclared read) |
| red-proofs (compiling mutations, each restored) | `scratchpad/redproof.py` | StrippedOn firing branch · InTraverse as min–max · FiresStoppedOnly false · the gate's conduct check · CannotComply false · SeatHold null · the factor block · the provider's factor filter ⇒ each ITS rail red (table below) |

## Ids allocated
`TkbEntityTypes.Motorcycle = 115` (fixed by the handoff) · `OrderKind.Dash = 14` · `BrainRefusal.ForbiddenByOrder` (next member) · no CE / R ids beyond CE-4621 (the handoff's) and the lane decisions L-1…L-12 here (to be ledgered on review).

## ⭐ Note to `ui` (M-1v / M-2p)
- The type: **`TkbEntityTypes.Motorcycle = 115`**, DIS **1.1.*.80.1**, class **`Motorcycle`** (`1.1.*.80`) — key `VisualFamily.Motorcycle` on `["Motorcycle"]`.
- Seats: **0 = the rider** (Driver; fires his own weapon only STOPPED, front and sides −120°…120°) · **1 = the pillion** (a firing seat, 30°…330° — both sides and the rear, never ahead). Seat places: rider X 0.1 / Z 0.75, pillion X −0.45 / Z 0.85 (the man is placed there, `EmbarkedStateSystem`).
- The HUD's refusal: a possessed pillion's trigger outside his arc is held with **`ActionReason.OutOfArc`**, a rider's while moving with **`ActionReason.HoldMoving`** — both reach the posture the HUD reads (`WeaponPostureOf.Report`, via `SayAt`), the same channel as every other operator-fire reason.

## Next step
All six slices built. Open: L-13…L-15 follow-ups (a getaway bike, the traffic handover / re-blend, a dismounted swarm fight); M-4 and MC-4a are behaviors' (not this handoff).
