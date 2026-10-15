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

## 4. Next (this lane)
B-5 (TKB: `Gen.Parachute`, rope anchors on UH-60 400 / 404, Paratrooper + SF operator, the scenario's `Wind`, the release-point helper) ·
B-6 (`IsAircraft` in `AmmoChoice` / `AirContacts`, fall damage) · B-7…B-9 (the ejecting pilot).
