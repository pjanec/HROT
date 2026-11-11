<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the table below
related-designs:
  - HANDOFF_Coordinator_To_Backend_Terrain_Authoring_2.md — the frame (dispatched at 21392507c)
  - ../../DESIGN_Terrain_Authoring.md — the owning design (as-built folds there, §18f…)
  - STATUS_Backend_Terrain_Overlay.md — the previous batch (O-0…O-8)
-->

# STATUS — backend: terrain authoring, part 2 ([handoff](HANDOFF_Coordinator_To_Backend_Terrain_Authoring_2.md))

Started at `157418c69` (merged `coordinator@157418c69` into `backend@e9a0b213b`).

| # | id | state | sha | note |
|---|---|---|---|---|
| T-0 | CE-4427 | ✅ DONE — 7 of 7 confirmed, none refuted | this commit | **R-1** a failed compile deletes `terrain-overlay/` + `.part` · **R-2** ingress resolves by key + within-building rank only; unknown terrain ⇒ deferred (latest per instance, retried each poll), resident world + unknown key ⇒ dropped with one Warn; wire comments say LOCAL · **R-3** a per-world panel table (O(1)), the egress query cached, a steady tick compares before converting (0 B, no rescan) · **R-4** worse than stated — the bake also laid an old-numbered damage table over the new world: `BuildingDamages.Remap` / `BuildingDamageTable.RemapTo` re-key by building key + panel rank in `CommitObstacles` · **R-5** unbounded only when every remaining ack is an always-acking module; one Warn after 60 s; the bake acks sole waiters it can never bake (missing shape / transform / StaticObstacle unregistered) · **R-6** factory bakes under a lock, serial + keys returned with the result, the residency's dirty bake checked against the committed serial · **R-7** cached expansions stamp their recipe / template files; a once-a-second wall-clock poll moves an epoch into the live signature. Rails each red-proved (⚠ the SimHost and Toolkits mutations ran as combined builds; R-5 sole-waiters and R-6 concurrent-builds are regression guards, not red-proved). ⚠ open edges: a sample for a building key on a building-less terrain is held until its instance is disposed; the once-a-second stat's allocation not measured (CE-3437) |
| T-1 | CE-4428 | ⏳ design (Opus) | | |
| T-2 | CE-4429 | | | |
| T-3 | CE-4430 | | | |
| T-4 | CE-4431 | | | |
| T-5 | CE-3431 | | | |
| T-6 | CE-3432 · CE-3433 · CE-3434 · CE-3435 | | | |
| T-7 | CE-4423 | | | |
| T-8 | CE-4424 | | | |

**Gates (T-0):** `Hrot.SimHost.Tests --filter "StaticObstacleBakeTests|BuildingDamageBakeTests|LiveOverlayBakeTests|EntityBuildingDamageTranslatorTests|TerrainLoadStepTests|PeekAndFireNodesTests|EntityCreationPackRails"` 174/174 (includes the real-DDS damage wire test `TheOwnersBuildingDamage_ReachesTheOtherNodesView_…`) · `Fdp.Toolkits.Tests --filter "Lifecycle|Terrain|Recast"` 580/580 · `Hrot.Orchestrator.Tests --filter StorageProcess` 7/7 · `Hrot.ClusterRunner.Integration.Tests --filter TerrainOverlayClusterLoad` 1/1 (worker; each test project built `--no-restore`, then `--no-build`) · `ned-wire-inventory --check` (wire change is comments only).
