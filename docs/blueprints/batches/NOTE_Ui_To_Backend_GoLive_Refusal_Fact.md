# NOTE — ui → backend: the Go-Live refusal needs to be a replicated fact (CE-4331 ⑥)

<!--STATUS
state: LIVE
updated: 2026-11-09
current-answer: the table below
related-designs:
  - docs/DESIGN_Recording_Storage.md — §9b (Go Live from a replay), §10 row "CE-4331 ③ … ⑥ Go Live refused", §11 (ui as-built)
  - docs/blueprints/batches/NOTE_Backend_To_Ui_Recording_Storage_2.md — the note this answers ("grey it with that text")
-->

Your note 2 says *"Go Live from a replay — refused by the master with the reason when the cluster differs — grey it with that text"*. ui cannot grey it yet:
the text is not on any fact a station reads.

| measured | where |
|---|---|
| the refusal is computed on the master only, from `_placements` (not replicated) | `ClusterMaster.Storage.GoLiveRefusal` (`ClusterMaster.Storage.cs:200`), used at `ClusterMaster.cs:1411` |
| the import job's `Reason` (fact ①) carries `PlacementNote` — **Unplaced + Listeners only** | `ClusterMaster.Storage.PlacementNote` (`:170`) |
| ⚠ a file placed on a node with ANOTHER id is refused by `GoLiveRefusal` but is NOT in `PlacementNote` | so a station that greys on "the import had a note" would let that case through and grey a listeners-only case that the master allows |

**Ask (lean):** put the master's own words on the replicated record of the exercise — e.g. `StorageJob.GoLiveRefusal` on the import job (fact ①, beside `Reason`),
or a field on `ExerciseInventoryItem`. ui then greys *Go Live* (`RunModeChoice` AAR → Live, the branch) with exactly that text. Until then the master's refusal
stays the only gate (the click is refused with its reason), which is safe.

**Built on ui meanwhile (`2026-11-09`):** the AAR start's *Start / Cancel* from the import job's `Reason` (the import dialog holds the opened run until *Start AAR*) —
`StorageTransfers.PlacementNote / StartAfterPlacement`, rail `CE4331_AnImportPlacedOnAnotherCluster_AsksStartOrCancel_WithTheMastersNote`.

---
_ui lane, 2026-11-09_
