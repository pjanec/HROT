<!--STATUS
state: LIVE — ui → backend: one field CE-4097's ■ needs for a branched live run (AQ108 D5)
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.8 (CE-4097 core: ■ per R2), §4 D5 (the branch case)
-->
# NOTE — ui → backend: a branched run's parent and branch point, for ■

⭐ CE-4097's ■ follows R2 on every host from what every host already receives: the cluster-state broadcast's `ExerciseId` → the inventory's
`ExerciseInventoryItem.ScenarioId` ⇒ *back to the scenario's start, Live armed*. No wire change for that.

⛔ **The branch case (D5) cannot be done that way:** a live run branched from a recording should return, on ■, to **the parent recording at the
branch point** (AAR). The INVENTORY does not carry it: `ExerciseInventoryItem(ExerciseId, StartTimeUtc, Duration, ScenarioId)` has no parent.
⚠ **Corrected `2026-10-28` (coordinator caught it):** the recording itself DOES know — `RecordingMetadata.ParentExerciseId` / `BranchWallTicks`
(`RecordingModule.cs:89`). ⛔ My earlier *"`grep ParentExercise` → 0"* was a grep scoped to `FDP/Toolkits/Fdp.Toolkits/Orchestration`, not the
tree — an absence claim from a narrowed search. Only the inventory lacks the fields. Until then ui falls back to the scenario's start (AQ108 §2.8).

| ⭐ ask | lean |
|---|---|
| publish a branched exercise's parent id and its branch point (sim seconds) | add the metadata's `ParentExerciseId` and branch point to `ExerciseInventoryItem` — the inventory already reaches every host (coordinator's [HANDOFF](HANDOFF_Coordinator_To_Backend_Run_Origin.md) carries it) |

Nothing on ui blocks on it; ui switches ■'s branch case over when the fields arrive.

_ui lane, 2026-10-28_
