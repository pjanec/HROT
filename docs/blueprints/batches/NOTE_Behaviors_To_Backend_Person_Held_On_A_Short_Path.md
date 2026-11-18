<!--STATUS
state: OPEN — for backend (the person mover)
updated: 2026-11-18
current-answer: the whole note
related-designs:
  - ../../DESIGN_Defend_And_Attack_Position.md — not changed; the posted man's post (D-2m) is reachable and on his floor
  - ../Blueprint_Issues_Tracker.md — CE-2450 (the row, behaviors' id block)
-->

# NOTE — behaviors → backend: a man inside a house is held still on a short, planned, same-floor path (CE-2450)

📐 **Measured `2026-11-18`, behaviors, `pc-defend-house` in the editor (stepped), 106–116 s.** Red Squad 1 Rifleman 1 stands at (119.93, 155.78, 0) inside
the defended house. His posture (`PostureNodes.AdvanceAndAttack`, a posted man) sends him to his post (115.9, 157.4, 0), on the **same** floor. The move
plans. The mover never moves him. After ~4 s `MoveToExecutor` fails it with `FailedBlocked`, and the posture sends it again, for minutes. The brain view
shows him as "moving" while he stands still.

| what was read (`/entities/{id}` every ~0.5 s) | value |
|---|---|
| `NavigationIntent` | PathToPoint to (115.9, 157.4, 0), speed 4, arrival 1 m |
| `NavigationCorridorMuscle` / `PathTrace` | planned: 3 points, 4.49 m — (119.55, 155.78, 0.2) → (116.25, 156.3, 0.2) → (115.9, 157.4, 0) |
| `NavState` | `CustomTrajectory`, TargetSpeed 4, `IsBlocked` 0, `ProgressS` 0 throughout |
| `VehicleState` | **Speed 0 with Accel 2.5 on every sample** — the controller asks for speed and the step gives none |
| `SimVelocity` | Linear 0; Angular non-zero (he turns on the spot) |
| `SimTransform` | unchanged to the last digit for the whole 10 s |
| `ActorCapabilityState` | CanMove; not stunned, not down; stance Crouched (×0.5, not 0) |
| `NavigationStatus` | Phase **Idle**, RouteHandle 0 (a neighbour moving through the same house reads Following with a handle); then `FailedBlocked` |
| `FrustrationTicks` | `NoProgressSeconds` grows; `WallHeldSeconds` 0 |
| his neighbour Rifleman 2, same squad, same house | walks 14 m in the same window |

⚠ **Suspect, not measured:** the path's first point is 0.38 m from where he stands, so he is in the margin by a wall, off the mesh (the navmesh stops a body
radius short of every wall). In `CarKinematicsSystem`, a walker's step that avoidance bent is traced against closed barriers (`bentWalker`, CE-2304 N3).
A step bent into the wall beside him is refused every frame (`pos2D = here`, `state.Speed = 0`). This would also explain the turning on the spot. ⛔ No line
was instrumented to confirm it.

| | |
|---|---|
| ⭐ lean | a held walker standing off the mesh steps back ONTO it (towards the path's first point) instead of being refused in place; or the bent step is retried unbent before it is refused |
| why behaviors does not build it | the mover (`CarKinematicsSystem`, the barrier trace) is backend's; the posture's re-send is its correct answer to `FailedBlocked` |
| how to verify | `pc-defend-house`, editor: Red Squad 1 Rifleman 1's journal shows no `MoveStarted … (SquadPost)` repeating every ~4 s after 100 s, and his position changes |
| probe | `scripts/utility-demo-check.py` helpers: launch editor, load `pc-defend-house`, play to 106 s, read `/entities/{id}` components every 0.5 s |
