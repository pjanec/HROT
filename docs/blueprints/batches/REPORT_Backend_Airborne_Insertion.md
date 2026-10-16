<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file. It is the report for HANDOFF_Ui_To_Backend_Airborne_Insertion.md (CE-1117) — B-1…B-4 so far; B-5…B-9 follow.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/airborne/DESIGN_Airborne_Insertion.md — THE design; §7a holds the as-built of B-1…B-4 and every deviation
  - docs/blueprints/batches/HANDOFF_Ui_To_Behaviors_Airborne_Insertion.md — the behaviors lane's drills build on what §1 below lists
-->

# REPORT — backend: airborne insertion, the engine half (CE-1117) — B-1…B-4

**For:** ui (the dispatcher) and behaviors (the drills). **From:** backend, `2026-10-16`. Built on `7239d65a4` (backend, which had merged
`ui` and `behaviors` the same night). Scope as dispatched; the ui STEER (`STEER_Backend_HitLocation_And_Airborne.md`) adds one rail, built
here (§3), and a hit-location caption fix that follows in the next commit.

## 1. What the drills can build against (CE-1118)

| you want | raise / read | where |
|---|---|---|
| a man down a rope | `DisembarkEntityCommand { Passenger, Vehicle, Exit = ExitKind.RopeDown, AnchorIndex = k }` — he leaves the list at rope k's anchor and slides (`Descent{FastRope}`) | `Embarkation.Disembark` / `ExitIntoDescent` |
| a man out of the door | the same with `Exit = ExitKind.JumpOut` — a static line under a round canopy, or a free fall under a ram-air one if his type's `Gen.Parachute` says `RamAir` | `Descents.Start` |
| his landing point | `Descent.Target` (≠ 0), else his `NavigationIntent.FinalDestination` — a ram-air canopy flies to it; a round one slips toward it | `ParachuteModel` |
| is he down? | `Descent.Phase == Landed`; the `Descent` is REMOVED 1 s later (rope) or after the canopy's `LandingSeconds` (harness off) — a Pedestrian again | `DescentLandingSystem` |
| the helicopter's stop | a route waypoint with `RouteHold.Rope` (hover `HeightAgl` — default 12 m — above the ROOF) ⇒ `NavigationPhase.Roping`; `RouteHold.Drop` ⇒ `Dropping` along the leg from that waypoint | `RotorcraftModel` |
| the release | nothing to do: `AirHolds.ShouldRelease` releases Roping when nobody is aboard AND nobody is on its ropes, Dropping when nobody is aboard | `AirHolds` (your file — the edit is named in the commit) |

⚠ **An Unload / Pickup hold on an AIR waypoint more than 3 m up now LANDS** (warned) — the hover-teleport is gone (D-8).
⚠ **The next man waits for the one below to be 3 m down** — that pacing is the drill's (H-1), not the model's.

## 2. Built

| item | what |
|---|---|
| B-1 | `Descent` (component 392, `[NoScenario]`, Muscle group, `dtDescent` 137, `EntityDescent` egress on change + one Kind-0 "over" sample, ingress mapping the anchor); `Locomotions.Of` reads it first; `IsAircraft` |
| B-2 | `LocomotionClass.Rope = 8 / Parachute = 9`; `RopeSlideModel`, `ParachuteModel` (§5.2 incl. guidance), `MotionModelTable` rows; `AirMotionSystem` moves a Descent body with no `Locomotion`; `CarKinematicsSystem.Without<Descent>`; `DescentLandingSystem`; `Wind` (393) |
| B-3 | `RouteHold.Rope / Drop`, `NavigationPhase.Roping / Dropping`; `AirMotionSystem.AirborneHolds` (roof height, rope refusal, the >3 m hover lands); `AirRoute.IsFlownThrough`, `AirPath.Dropping`; `AirHolds` |
| B-4 | `DisembarkEntityCommand.Exit / AnchorIndex` (appended; the wire payload too); `Embarkation.ExitIntoDescent`; ⭐ **CE-3059 for a descent:** a `StartDescentCommand` (event 3204, interaction kind 8) carries the decision from the carrier's owner to the man's POSITION owner — §7a's sequence diagram |

The deviations from the design, each argued, are in [design §7a](../../designs/airborne/DESIGN_Airborne_Insertion.md) — and folded there.

## 3. Gates (`--no-build` after one build of each TEST project)

| gate | result | delta |
|---|---|---|
| `dotnet test Fdp.Toolkits.Tests` | 3 604 / 0 fail / 1 skip | +18 (8 DescentModels, 4 Rotorcraft B3, 3 Embarkation B4, …); the steer's rail `RaycastSolverSystemTests.B1_AManUnderACanopy_IsStillAPerson…` added after: that suite 20 / 0 |
| `dotnet test Hrot.SimHost.Tests` | 1 433 / 0 / 6 | +8 (DescentReplication, the Brain's B3 release, the transport's B4 round trip, and rows merged since the last base) |
| `Hrot.Core.Tests` · `Hrot.Network.NED.Tests` · `Hrot.Presentation.Tests` · `Hrot.Map.Common.Tests` | 266 · 142 · 644 (22 skip) · 67 — all 0 fail | descriptor pins: `dtDescent` in the Muscle lists |
| builds: `Hrot.Editor.Tests`, `Hrot.ClusterRunner.Integration.Tests` | ✅ | — |
| red-proofs (`TickCount64` bypasses, restored) | 5 / 5 red: the brake ramp, the roof term, the ownership branch of `ExitIntoDescent`, the Drop fly-through, `AnyOnRopes` | — |
| row 8 — a descending body agrees across nodes | `DescentReplicationTests` (the egress wire form applied by the ingress on a second world: same descent, anchor resolved to THAT node's helicopter, Kind 0 removes it) + `InteractionTransportTests.B4_…` (Brain decides ⇒ the Muscle starts it). ⚠ Not a live two-process DDS run — the `ClusterRunner` harness has no airborne scenario until H-2 | — |
| `tracker-counts.py --check` · `design-digest.py --check` · `mermaid-check` | ✅ · ✅ · 7/7 blocks parse | ids allocated: **none** (component 392 / 393, `dtDescent` 137, event 3204, interaction kind 8 — each measured free on all three lanes) |

⚠ Known flaky, not touched: `SquadCoordinationSystemTests.Execute_DoesNotAllocate` (red only in a full parallel run, green alone).

## 3b. B-5 (`2026-10-16`) — as-built [design §7b](../../designs/airborne/DESIGN_Airborne_Insertion.md)

| | |
|---|---|
| built | types 204 Paratrooper / 205 SF operator (`Gen.Parachute`), fast ropes on the doors-off UH-60 (404), `ReleasePoint.For` |
| ⚠ numbers estimated, not published | the rope arm's place on the UH-60 (1.5, ±1.6, 0.4); the rope's 60 ft is the short end of the published 60–90 ft |
| ⛔ not done | ropes on the plain UH-60 (400) — it carries nobody and does not fly (Air Ops); the scenario's wind — tkb-1's ruling makes it a singleton in a scenario section, design §7b, measuring which nodes load it |
| gates | `Fdp.Toolkits.Tests` 3 605 / 1 fail (`SquadCoordinationSystemTests.Execute_DoesNotAllocate` — the known flaky one) / 1 skip; `Hrot.SimHost.Tests` 1 437 / 0 / 6; `Hrot.Core.Tests` 267 / 0; `Hrot.Presentation.Tests` 644 / 0 / 22 (the catalogue pin gains 204 / 205); `Hrot.Editor.Tests` 509 / 0 / 2; release-point red-proof red |
| ⚠ found while gating | `EditorCapabilitiesTests` ×2 had been RED since WS-1 (`5c3fe8974`, `2026-10-14`, this lane): `WeaponDeploySystem` joined the Interactions capability and the rail's hand-written list never did. Fixed in the rail (the system belongs there). ⚠ the B-1…B-4 gate above did not RUN `Hrot.Editor.Tests` (built only) — so that row was missing, not green |

## 3c. B-6 (`2026-10-16`) — as-built [design §7c](../../designs/airborne/DESIGN_Airborne_Insertion.md)

| | |
|---|---|
| built | guided rounds of either domain refuse a man coming down, a gun's round does not; air contacts and missile warnings are for aircraft; a fall hurts by its speed |
| gates | `Fdp.Toolkits.Tests` 3 611 / 1 (the known flaky allocation test) / 1; `Hrot.SimHost.Tests` 1 437 / 0 / 6; `Hrot.Presentation.Tests` 651 / 1 / 23 — ⚠ `RtsCommandToolTests.CE1077_…` (ui's) red once in the suite run, GREEN 3 / 3 alone, nothing it reads was touched ⇒ intermittent, not chased; `Hrot.Editor.Tests` 509 / 0; `Hrot.Core.Tests` 267 / 0 |
| red-proofs | 3 / 3 red: the descent branch of `InDomain`, the fall's impact (2 rows); the CE-2199 turret path (same run) |
| ⚠ not railed | `AirContacts.IsAir` → `IsAircraft` (a one-line swap; its suite has no TargetMemory fixture — the B-1 `IsAircraft` rail covers the predicate) |

## 3d. B-7…B-9 (`2026-10-16`) — the ejecting pilot, as drawn in [design §7d](../../designs/airborne/DESIGN_Airborne_Insertion.md)

| | |
|---|---|
| for the behaviors lane (H-3) | order an ejection with the interaction action **`ActionIdEject` (11)** on the AIRCRAFT (`EjectExecutor`: raises `EjectRequestEvent`, succeeds once the aircraft is down). The pilot is a new entity of type **206**, coming down under a round canopy; give him `EvadeAndSignal` once his `Descent` is gone. ⚠ The A-10 also ejects ON ITS OWN at 20 % health |
| built | `Gen.Ejection`, `EjectRequestEvent` (3205), `EjectExecutor`, `EjectionSystem` (Interactions capability), the seat's arc, Pilot 206, the A-10's seat |
| ⚠ estimated | the rocket's climb (40 m/s), the seat's time (1.5 s) and drag, the A-10's seat place and 20 % threshold |
| ⛔ not done | F-16 / C-130 (no brain, no combat block); the drawing (U-2, ui) |
| ⚠ cross-lane | `CgfLogicPack` (the executor list), `EditorCapabilitiesTests` (+`EjectionSystem` in both lists), `EntityTypeCatalogTests` (+206) |

## 3e. CE-2200 / CE-2201 (`2026-10-16`, behaviors' [Javelin handoff](HANDOFF_Behaviors_To_Backend_Javelin_Lock.md)) — folded into [Missiles & Ammo §3a](../../designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md)

| | |
|---|---|
| CE-2200 | `AimAndFireExecutor.NeedsLock`: a guided round from a mount with `AimSeconds` never fires blind (`HoldNotSeen`); an RPG / LAW still does |
| CE-2201 | `GuidedRoundSystem.TopAttackFits`: one launch-time answer for `FireProcessingSystem` and `ThreatDanger`. It takes the highest apex that fits the range, otherwise the round flies direct. Javelin: full profile from ~727 m, capped from ~607 m, direct below |
| ⭐ found on the way (both pre-existing, same file) | ① the DIVE branch was dead code (pure pursuit returned first), so the arrival angle was luck: 40° down, on the side, at 900 m. ② the dive trigger assumed a LEVEL entry; with ① fixed it fell 23–73 m short or overflew at 750–1000 m. Both fixed; a 2D replica swept 600–2590 m: every range that fits hits |
| ⚠ for behaviors | `ua-javelin-lock` ②'s 230 m shot is now a DIRECT hit. To show a top attack, put the open tank ≥ ~610 m away |
| gates | `Fdp.Toolkits.Tests` 3 632 / 2 / 1 — both reds are `SquadCoordinationSystemTests` allocation counts (the known flaky family, not touched: alone 1 red then 2 green); `Hrot.SimHost.Tests` 1 441 / 0 / 6; `tracker-counts --check` OK; `design-digest --check` OK; `rulings-check` 215 / 215; Mermaid 13 / 13 parse |
| red-proofs | CE-2200: 2 / 2 locking rows red with the gate bypassed. CE-2201: 6 / 14 rows red with the old range-only rule. The dive fixes: 900 m red with ①, then 750 / 800 / 900 / 1000 red with ② |

## 3f. CE-3214 (`2026-10-16`) — hit location S2b: weapon systems by part, as drawn in [design §11 / §11a](../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md)

| | |
|---|---|
| built | a weapon NOT embarked is met by the kit ui draws (stowed / blended / deployed, laid by its `TurretPose` or its rest elevation): parts Mount / Traverse / Weapon; nine DIS profiles; a Firepower wound breaks it (`IsBroken` ⇒ `CanFire` = Destroyed). Embarked it keeps CE-2182's skip |
| found by the rail | `VehicleBody.MaxBoxes` 16 truncated the ZU-23's 17-part kit — now 24 · the weapon kits needed round-trip precision |
| ⚠ cross-lane | ui's `WeaponKits.Deployment` now delegates to `WeaponDeployment.Fraction` (one formula); ui's `VehicleBodyParityTests` grows the weapon rail and generator |
| gates | `Fdp.Toolkits.Tests` 3 638 / 0 / 1; `Hrot.SimHost.Tests` 1 442 / 0 / 6; `Hrot.Presentation.Tests` 660 / 0 / 25; design-digest OK; Mermaid 9 / 9 |
| red-proofs | 2 / 2 red: the deploy fraction pinned at 1 (the stowed rail), `IsBroken` without the wound (the firepower rail); the skip's own red is the rail's `VehicleParts = false` row |
| ⚠ estimated | the zones' multipliers and thresholds (Mount ×0.5; Traverse at 128; Weapon at 64) — starter numbers |

## 4. Next (this lane)
⏳ only the scenario's wind remains — design §7b **D-13**, a decision for the user (lean ③: the load message, like the terrain name).
The behaviors handoffs CE-2198 / CE-2199 / CE-2200 / CE-2201 are done. CE-3214 (S2b: weapon systems by part) is built. Next: CE-3215 (S2c: aircraft by part).
