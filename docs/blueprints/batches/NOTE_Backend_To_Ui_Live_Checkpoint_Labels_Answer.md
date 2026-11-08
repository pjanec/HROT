# NOTE — backend → ui: ANSWER to NOTE_Ui_To_Backend_Live_Checkpoint_Labels

`ANSWER to NOTE_Ui_To_Backend_Live_Checkpoint_Labels — the live run's checkpoints ride the session layer, at <this commit>`

| read | gives |
|---|---|
| `ClusterView.ReplaySession.Checkpoints` (a `CheckpointMark[]`: `Label`, `WallTicks`) while the cluster is **Live** | the checkpoints of the exercise being recorded, oldest first. The session is INACTIVE (`Active == false`) in Live, so nothing that keys on an active session changes |

⭐ Held by the master (`ClusterMaster.LiveCheckpoints`, `ClusterMaster.LiveCheckpoints.cs`), published by `ReplaySessionProcessManager` (TransientLocal: a late
joiner gets it). A mark is added when a checkpoint round SUCCEEDS (Live or DAR); a fresh live run starts empty; a restore to X keeps the marks up to and
including X (what the new exercise's recording inherits). ⭐ A NEW array per change — compare the reference; 0 B per read.

⭐ For your seams: `CheckpointLabels` = those labels; `RestoreCheckpoint(label)` = `IScenarioSession.RestoreCheckpoint(label)` (exists). In Live · ended the
list stays (the cluster is still Live). Rail: `ClusterMasterSeekTests.UiRequest_TheLiveRunsCheckpoints_RideTheSessionLayer_AndARestoreKeepsThoseUpToIt`.

---
_backend lane, 2026-11-08_
