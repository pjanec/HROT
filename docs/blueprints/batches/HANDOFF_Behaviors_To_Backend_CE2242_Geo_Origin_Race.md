<!--STATUS
state: LIVE — behaviors → backend: a defect found live, with its mechanism measured. Yours to fix (terrain residency / load chain).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Geo_Origin.md — §4 the known-limit note (the measurement); §2 D the ruling "set FIRST, in the same commit".
  - docs/DESIGN_Cluster_Load_Phase.md — §4.1a the terrain step (L3/L5), §7.3 the chain this race lives in.
-->

# behaviors → backend: **CE-2242 — entities land ~900 km out when a node is still baking its navmesh**

**At** `179ffc3dc` (behaviors). Row: [`CE-2242`](../Blueprint_Issues_Tracker.md).

| | |
|---|---|
| **symptom** | on ~half of fresh `--mode all` loads (test-town) every entity sits at `(901880, 5038392)` on some node — outside the perception and collider grids, so nobody sees anybody; a static entity is never re-sent, so it never heals |
| **mechanism (measured)** | `TerrainResidency.Commit` sets the origin first (§2 D) — but `Commit` waits for `Prepare`, and `Prepare` bakes the navmesh (`TerrainResidency.cs:200`, ~0.5–0.6 s). Non-baking nodes commit at once; CGF spawns (`ProcessSpawn` lines at 48.94–49.06 s); the baking node's origin comes after "Navmesh … baked" (~49.18 s), so the geodetic positions it received meanwhile were converted with no origin |
| **leans (unmeasured)** | ① apply the origin before the bake — it needs only `Definition.Origin`, and `WGS84Transform.SetOrigin` is one volatile write (§4 ①); ⚠ every bootstrapper (SimHost, IG, Stride) must hand the residency its transform on the main thread; ② or hold the scenario's entity requests until every node has committed its terrain |
| **evidence you can re-run** | `python3 scripts/utility-demo-check.py --launch ua-civ-neutral-gate` — its precondition ⓪ fails with "off-terrain = CE-2242" exactly when this happens; `grep "outside the perception grid" /tmp/utility-demo-cluster-8111.log` |
| **impact** | every live demo is a coin flip (seen on `ua-civ-neutral-gate` and `ua-threat-ranking`); the user asked for demos usable as tests |
