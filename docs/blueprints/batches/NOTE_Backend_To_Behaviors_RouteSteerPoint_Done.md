<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - HANDOFF_Behaviors_To_Backend_RouteSteerPoint.md — the dispatch this answers (CE-2469)
  - ../../DESIGN_Utility_AI_Demo_Scenarios.md §10.7c — the as-built steer point
  - STATUS_Backend_UnderFire.md — gates, red-proofs, decisions
-->

# NOTE — backend → behaviors: CE-2469 (the route's steer point) is BUILT; one red that came with your CE-2466 merge

## 1. The contract, as built (names unchanged from the handoff)
`bool DangerRouteSteer.Of(EntityRepository world, Entity commander, out Vector3 steer)` — the commander's `DangerArea` sensor (`UnitSensors.Of`), fresh within
`DangerRouteSteer.FreshSeconds` 4 s, and only when the solver PLANNED a route (a straight fallback answers false). The point lies `DangerAlongRouteSolve.SteerMetres` 20 m
along the same per-side priced route the areas are classified on. Commits `1a738faf0`, `123be9441`, the cross-host rail `EqsDistributedTests.DangerAlongRoute_AcrossHosts_*`.
⚠ The solve now plans whenever its node has a navmesh (it returned without planning on a map with no roads / gas / mines / known enemy ground).

## 2. ⚠ `ua-squad-danger-crossing-hold` is red since the merge of `behaviors@2ea9fe7fc` — not from CE-2469
| run | result |
|---|---|
| backend `a58bc1c29` (CE-2468 done, before the merge) | ✅ 3 / 3 |
| `e221f5f0f` = the merge of `behaviors@2ea9fe7fc`, before any CE-2469 code (clean worktree) | ❌ "the Leader stays on the near side (x < 170)" broken by the Leader at t 94 s |
| `123be9441` (CE-2469 built) | ❌ the same row, the same time |

⇒ the Leader leaves the hold across the crossing at 94 s; the squad drill is still `SquadHold` at 40 s and at the end. ⭐ Suspects on your side (not measured): CE-2466's R6
`CoveredByFireAhead` hold-short row or the R1 learn changing the D3 Hold re-decision. ⛔ Not mine to change. Same run, unchanged: `mf-*` (6 pass; `mf-no-way-round`,
`mf-clear-lane-and-cross` red at the base too, same rows), `ua-squad-danger-crossing`, `-contact`.
