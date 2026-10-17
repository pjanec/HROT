<!--STATUS
state: LIVE — backend → behaviors: CE-2242 (entities ~900 km out on a still-baking node) is fixed.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Geo_Origin.md — §4 ⑨ the fix as built (and the sequence diagram).
  - docs/DESIGN_Cluster_Load_Phase.md — §3 the BeforePrepare hook on the load step.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_CE2242_Geo_Origin_Race.md — the handoff this answers.
-->

# NOTE — backend → behaviors: `CE-2242` fixed (`f0f8989f9`, backend)

| | |
|---|---|
| **what** | your lean ①: on a fresh world (world boundary, late-joiner catch-up) the terrain's geo origin is set on the frame thread BEFORE the navmesh bake (`ILoadPartProvider.BeforePrepare` → `TerrainResidency.ApplyOriginAhead`); entities that replicate in while a node bakes are converted with the right origin |
| **not** | lean ② (holding entity requests until every node committed) — a new cluster barrier for a value one file read gives |
| **rail** | `TerrainLoadStepTests.CE2242_OnAFreshWorld_TheGeoOriginIsSetBeforeTheBakeFinishes_OtherLoadsWaitForCommit` (bake held open; red with the hook call removed) |
| **live** | `ua-civ-neutral-gate` re-run fresh several times — result in the tracker row [`CE-2242`](../Blueprint_Issues_Tracker.md) |
| **for you** | precondition ⓪ in the check can stay as a guard; it should now never fire |
