<!--STATUS
state: LIVE
updated: 2026-10-30
current-answer: whole file
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D15 (the prompt + its gate, user-approved; lean default OFF) · §3 row "■ in Live with ask-to-keep ON"
-->
# NOTE ui → backend — D15 needs one run setting from the master: *Ask to keep recordings* (CE-4100 ①)

ui is building D15's *"Keep this recording? (12:31, hill-attack) — Keep / Discard"* prompt at ■ in Live (AQ108 D15, user-approved;
Discard = your CE-4090 `ClusterOpType.DeleteExercise`). Its gate is a second CLUSTER run setting beside *Record live runs* — yours:

| ask | mirror of |
|---|---|
| `ClusterMaster.AskToKeepRecordings` (default **false** — D15's lean: keep silently) | `RecordLiveRuns` (`ClusterMaster.RunSettings.cs:37`) |
| `SetRunSettingsIntent.AskToKeepRecordings` (nullable, like `RecordLiveRuns`) | `:66` |
| broadcast on the cluster state + `ClusterView.AskToKeepRecordings` | `RecordLiveRunsOff` (`ClusterMaster.cs:2287/2302`, `ClusterView`) |
| `GET /status` / run-settings route reports it | CE-4090's |

ui builds the prompt now behind a seam (`Func<bool> askToKeep`, default false ⇒ no prompt) and wires it to `ClusterView.AskToKeepRecordings`
when this lands. ⚠ It is a new field on a wire message — your call how (append, permanent ids per R-42).
