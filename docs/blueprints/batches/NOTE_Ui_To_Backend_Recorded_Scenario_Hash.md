<!--STATUS
state: LIVE — ui → backend: one field CE-4097's AAR → Preview confirmation needs (AQ108 §3, D7)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §2.8a (CE-4097 part two as built: the RecordedVersionDiffers seam), §3 row "AAR → Preview"
-->
# NOTE — ui → backend: the recorded scenario's hash, so AAR → Preview can offer the recorded copy

⭐ CE-4097 part two built AQ108 §3's AAR → Preview confirmation with both buttons — **Open the recorded version (as a copy)** (CE-4091's
`OpenForEdit(RecordedScenarioNames.ForExercise(id))`) · **Open the current scenario** — shown only when the saved scenario's hash differs from
the recording's. The decision is one host seam, `RunModeChoice.RecordedVersionDiffers(exerciseId, scenarioName)`.

⛔ **No host can answer it yet** (measured `2026-10-29`): the recording's hash is in the recorded copy's stamp
(`RecordedScenarioStamp(ScenarioName, ScenarioHash, RecordedAt)`, `FDP/Toolkits/Fdp.Toolkits/Scenario/RecordedScenarioStamp.cs:19`), which sits in
the master's storage; `ExerciseInventoryItem` carries no hash; and no host is given the CURRENT saved scenario's hash either. So today the plain
confirmation shows and the recorded copy is reachable only by its own menu item.

| ⭐ ask | lean |
|---|---|
| put the recorded copy's `ScenarioHash` on `ExerciseInventoryItem` (beside `ScenarioId`, as the branch fields were) | the inventory already reaches every host |
| and the saved scenario's current hash where a host can read it — the scenario inventory (`AvailableScenarios` is names only) or `GET /scenarios` | one hash function — `RecordedScenarioStamp`'s — for both sides, so they compare equal when nothing changed |

Nothing on ui blocks on it; ui wires the seam when the fields arrive.

_ui lane, 2026-10-29_
