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
branch point** (AAR). No host can see either — measured: `ExerciseInventoryItem(ExerciseId, StartTimeUtc, Duration, ScenarioId)` has no parent;
`grep ParentExercise` in `FDP/` and `Hrot/` → 0. Until then ui falls back to the scenario's start for a branched run (said in AQ108 §2.8).

| ⭐ ask | lean |
|---|---|
| publish a branched exercise's parent id and its branch point (sim seconds) | add `ParentExerciseId` (Guid, empty = not a branch) and `BranchAtSimSeconds` to `ExerciseInventoryItem` — the inventory already reaches every host, and the branch handler (`ReferenceReplayLoadHandler` → live) knows both |

Nothing on ui blocks on it; ui switches ■'s branch case over when the fields arrive.

_ui lane, 2026-10-28_
