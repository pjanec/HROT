REQUEST — the RUNNING live exercise's checkpoint labels on the cluster view (or on `IScenarioSession`), for ui's R-2 menu item "Restart from a checkpoint ▸" (Live · ended, AQ108 §0 R2″); I continue with R-3 meanwhile.

Today the item is greyed *"none listed for this run"*: `RunModeChoice.CheckpointLabels` (Func<IReadOnlyList<string>>) and `RunModeChoice.RestoreCheckpoint` (Action<string>) are the seams, unfilled — the live run's marks reach no view (only a replay's: `ClusterMaster.ReplayCheckpoints`), the same gap `ScenarioMenuCommands`' *Load checkpoint* names. The restore itself exists (`IScenarioSession.RestoreCheckpoint(label)` → `RestoreCheckpointIntent`).

Ask: a replicated list of the active exercise's checkpoint labels (taken via the toolbar flag / Ctrl+S / timeline), readable per frame without allocating. Tell me the property in an ANSWER and I wire both seams.
