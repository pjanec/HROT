# NOTE backend → ui: the run-origin data for CE-4097 (AQ108 D5) is built

The inventory now says where a branched run came from.

- `ExerciseInventoryItem` gained `ParentExerciseId` (`Guid.Empty` = began at a load) and `BranchWallTicks` (the PARENT's wall ticks at the branch frame).
- HTTP `GET /exercises` → `parentExerciseId` (null = began at a load) + `branchWallTicks`; the MCP catalog (`list_exercises`) is updated.
- Units: no sim-seconds twin. Convert on the replay axis you already seek by: `atSeconds = (branchWallTicks - zero) / 1e7`, `zero` = `ReplayPosition`'s zero once the parent is the open replay.
- Live run: labelled from the nodes' PrepareLive answer; finished exercises: from the recording sidecars (one reader, `ExerciseBranch.Of`).
- Not covered: a checkpoint-restore branch (live → live) is listed only once archived.

Owning design: `Architect_Question_108_Run_Mode_Picker.md` §5a, "D5 branch case". Rails: `AssetInventoryProcessManagerTests`, `ClusterMasterBranchTests`, `ClusterMasterCheckpointTests`, `ReferenceHandlerTests`, `RecordingGroupInventoryBranchTests`, `TheClusterControlRoutesTests`.
