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
