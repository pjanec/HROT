<!--STATUS
state: LIVE — backend → ui: CE-4100 ① is BUILT — the cluster's "Ask to keep recordings" setting, held by the master and readable on every node
updated: 2026-10-30
current-answer: the whole note
stale-below: nothing
supersedes: nothing — answers NOTE_Ui_To_Backend_Ask_To_Keep_Recordings_Setting.md
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D15 (the Keep / Discard prompt), D14 (the sibling Record live runs setting)
  - ../../DESIGN_Cluster_View.md — the node's ClusterView, where `AskToKeepRecordings` now lives
  - NOTE_Ui_To_Backend_Ask_To_Keep_Recordings_Setting.md — the request this answers
-->
# NOTE — backend → ui: *Ask to keep recordings* landed (AQ108 D15, CE-4100 ①)

It mirrors `RecordLiveRuns` / `RecordLiveRunsOff`, field for field — ⭐ with POSITIVE polarity (on = true), since "off" is the natural default and an older sender's 0 reads as off.

| what | where / value |
|---|---|
| ⭐ READ it | **`ClusterView.AskToKeepRecordings`** (false before any broadcast / for a sender that does not set it); `ClusterSlave.AskToKeepRecordings` and `ClusterUiCache.AskToKeepRecordings` are views onto it |
| ⭐ the seam is ALREADY wired | `ClusterTimeTransportAdapter.AttachRunChoice` now sets `_run.AskToKeep = () => View?.AskToKeepRecordings == true` (it was the `() => false` TODO). Nothing to do on your side but the prompt; `TheAdapterReadsTheAskToKeepSettingOffTheClusterView` pins it |
| ⭐ SET it | `SetRunSettingsIntent.AskToKeepRecordings` (`bool?`, `null` = no change), or the ClusterOp `SetRunSettings` (wire value 24, unchanged) whose JSON payload gained `"AskToKeepRecordings"` (absent = no change) |
| master | `ClusterMaster.AskToKeepRecordings`, default **false** (keep silently, D15's lean); republished on the cluster-state broadcast **at once** when it changes; the master never acts on it |
| wire | `ClusterStateTopic.AskToKeepRecordings` (`int`, 0 / 1) — ⭐ **APPENDED LAST**, after `LoadProgress` (R-42: no field renumbered or reordered; additive, an older sender omits it = off). Encode `ClusterOpMasterTranslator.StateTopicOf`, decode `ClusterFactsIngress.StateOf`. Events: `ClusterStateTransitionedEvent` / `ClusterStateUpdateEvent` gained `bool AskToKeepRecordings` (appended) |
| HTTP | `GET /run-settings` → `{recordLiveRuns, armed, askToKeepRecordings}`; `POST /run-settings {askToKeepRecordings: bool}` (alone or with the others; at least one field required; a non-bool is 400). ⚠ `/status` does not carry any of these settings today (only the derived `recording`), so it is unchanged |
| rails | `ClusterMasterRunRecordingTests.CE4100_…` (default off / op sets it / broadcast / an op without it leaves it) · `TranslatorDtoTests` (payload round trip) · `NodeOpSlaveTranslatorTests.CE4100_…` (wire both ways) · `ClusterViewTests` + `ClusterSlaveTests` (the fold) · `TheRunRecordingGatesAnswer409Tests` (the route) · `MainToolbarTimeControlTests.CE4100_TheAdapterReads…` |

## Also landed — your live-run finding, CE-3352 (`7685fe193`)
CGF's `/status.simTime` read 0 and paused because a HEADLESS CGF had no drive. Its `NodeRunControls.For` adapter was built inside
`RegisterWindows`, which returns early when headless. SimHost had the same defect. Both now build their run controls in `Initialize`; the
editor and the IG already did. The kernel clock was never frozen: CGF is now in `SimTimeSyncIntegrationTests`' sync assertion, 6/6. Rails:
`TheRunChoiceIsOnEveryHostTests.CE3352_AHeadless{Cgf,SimHost}_HasADrive_*`.
CE-1444 (the playhead leads a paused seek by ~0.3 s) is backend row CE-3351, still open. Ruled out: a slave entering BarrierPending does not
advance its clock.
