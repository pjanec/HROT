<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.9 — R3 (a bound takes its direction from a priced path): the consumer
  - ../../DESIGN_Utility_AI_Demo_Scenarios.md §10.7 — OWNS the route danger solve this extends
  - ../../designs/minefields/DESIGN_Minefields.md §9b — the per-side UnderFire cost the solve's route already carries (UF-5)
-->

# HANDOFF — behaviors → `backend`: the route's STEER POINT in the danger-area answer (CE-2469, R-2140 R3)

**For:** `backend`. **From:** behaviors. **Dispatched at `<filled by the commit that adds this file>`**; ⛔ scope frozen there (R-106 on a failed premise).

## Why — a gap found while building CE-2466
Squad Tactics §7.9 R3 says a resumed BOUND takes its direction from a **priced path** to its objective, so it goes round ground a known enemy covers.
📐 Measured: a bound has NO route — each bound is a straight line at the objective (`BoundingOverwatchBehavior.cs:235-238`), and on a cluster the
BRAIN node has no navmesh to plan one. ⭐ The ONE place a priced route to the objective is already planned, with the mover's side (so the UF-5
`UnderFire` cost already applies), is the route danger solve (`DangerAlongRouteSolve.Solve`, on the navmesh node) — but its answer carries only the
areas, never the route.

## The task — SP-1
Add to the danger-area answer the route's **steer point**: the point ON the planned route `SteerMetres` (= 20 m, the bound length) from its start — or
the route's end when shorter — plus `HasSteer` (0 = no plan, a straight leg).
| where | change |
|---|---|
| `DangerAreaCognitiveBuffer` (`FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs`) | `+ Vector3 SteerPoint; + byte HasSteer;` (⚠ a recorded, replicated layout — the DDS result topic + translator follow, the descriptor ordinal unchanged or re-allocated by you) |
| `DangerAlongRouteSolve.Solve` | writes it from the SAME planned route it classifies (no second plan) |
| ⭐ the contract behaviors binds | `bool DangerRouteSteer.Of(EntityRepository world, Entity commander, out Vector3 steer)` — the commander's danger-area sensor's answer (`UnitSensors.Of(..., SensorModality.DangerArea)`), fresh only (`LastUpdateTimeSeconds` within 4 s) |

**Done when:** a rail on a two-street navmesh with a Direct `UnderFire` record over one street: the steer point of a Friend unit lies on the OTHER street;
of a Hostile unit, on the covered one (the cost is per side); the answer replicates (a two-node rail); 0 B on a steady frame.

## Then behaviors does (FYI, not yours)
`BoundingOverwatchPlay.BeginIssue` aims each bound at the steer point when `DangerRouteSteer.Of` answers (the bound's own sensor — `DangerRouteWatch`,
CE-2462 — already asks for this route), else the straight line as today.

## Gates
the danger-area suites, `UnderFireNavTests`, `mf-*`, `ua-squad-danger-crossing*` unchanged; the parallel checks on every rail; invariant output.
