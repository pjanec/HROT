# NOTE — behaviors → backend: two ground-model measurements from the clearing demos *(2026-10-20/21, CE-2334, AQ99)*

**Context** — the room-clearing demos (`bc-room-clear`, `bc-house-clear`, [Building Clearing §7.1b](../../designs/group-maneuvers/DESIGN_Building_Clearing.md))
traced per man (drill phase, order, behaviour, `NavState`, speed). Two findings sit in your code; **neither is built or changed by
behaviors**. Both are in [`AQ99`](../Architect_Question_99_Clearing_Demos_Person_Spacing_And_Losses.md) for the user, with leans.

| # | measured | where | lean (the user decides) |
|---|---|---|---|
| **A** | a person's avoidance shell is **1.6 m**: Pedestrian preset `AvoidanceRadius = 0.8` × RVO-Lite's `combinedRadius = 2 ×`, with the strong spring. No TKB type overrides it. A stack 0.8 m apart, a 1.25 m walkable hallway, two men passing in a doorway — all fight the spring; men are pushed off the mesh into the wall band | `VehicleClass.cs:127`, `RVOAvoidance.cs` | 0.3 m (the navmesh body). 📐 Tried locally, not committed: room stack 11 s → 5 s; `Fdp.Toolkits.Tests` CarKinem/Navigation/Formation 646/647 (the one is `CE2304_AWalkerPushedByTheMenBesideHim`'s PREMISE — men 0.6 m beside him no longer push him; move them to 0.45 m) · `Hrot.SimHost.Tests` crowds/squads/civilians 210/210 |
| **C** | a man DOWN 0.9 m inside a doorway walls it off for every man behind him — R-3027 keeps him a neighbour | `CarKinematicsSystem.ApplyCollisionAvoidance` | walk round him where there is room (R-3027 as it stands), step over him where there is not |

⚠ **Said plainly:** behaviors built a step-over for C (skip a man down as a neighbour) the same night, before seeing CE-2321 on your
branch — then **reverted it** (it contradicted R-3027 and your `CE2321_AWalker_StillStepsRoundAManDown`). Nothing of it is on `behaviors`.

⭐ If you write a design for either, please link it from [`Building Clearing`](../../designs/group-maneuvers/DESIGN_Building_Clearing.md) §7.1b
(reciprocal `related-designs`) — the room drill's stack spacing (0.8 m) and door legs assume what A decides.

_behaviors lane, 2026-10-21_

## ⭐ Update `2026-10-21` — AQ99 approved (R-2065) and BUILT by behaviors; three edits in your code, said here

| your file | what changed | rail (red under its mutant) |
|---|---|---|
| `Fdp.Toolkits/Tkb/Domain/VehicleParametersDto.cs` · `CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs` | A: an optional `AvoidanceRadius` (guarded `> 0`, like its neighbours) + `CloseOrderAvoidanceRadius = 0.3`; set on soldier / fighter / security person types (Hrot catalogues, `SimVehicleDef` for BDC); civilians unset = the preset's 0.8 | `VehicleKinematicsTkbTranslatorTests.CE2336_*` |
| `CarKinem/Systems/CarKinematicsSystem.cs` · `CarKinem/Controllers/ManualMotion.cs` | C: `ApplyCollisionAvoidance` skips a man down (person, `!Injury.IsInTheFight`) only when `NoWayRound` — the walkable mesh beside him holds no strip of the walker's shell on either side; no navmesh ⇒ unchanged. `ManualMotion.IsOnMesh` made public over the existing `OnMesh`. Your R-3027 rails all still green | `CarKinematicsSystemTests.AQ99C_*` (narrow: over · wide: round); design: Motion Domains §5h.1 |
| `Hrot.Network.NED/…/EntityDoorStateEgressTranslator.cs` | CE-2335 (one defect): a GHOST (no `NetworkAuthority` yet) no longer publishes the door's state — the exclusion `DoorCommandSystem.OwnsDoor` already had. ⏳ The run-1 split itself is NOT explained by it; lean in the tracker row (ddsmonitor + a repair re-send) | `EntityDoorStateTranslatorTests.Stage5b_*` |

_behaviors lane, 2026-10-21_
