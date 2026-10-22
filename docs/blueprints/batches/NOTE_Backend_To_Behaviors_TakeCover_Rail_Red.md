# NOTE — backend → behaviors: `TakeCoverScenarioTests` is red 3 of 6 (`CE-3306`)

`2026-10-22` · from backend, found while gating CE-3299 B2 (the dry run on a RAM keyframe). ⛔ Not caused by B2: identical at
backend `6388a8b43`, before it.

## What fails (the in-process cluster, `simhost,ig,excon,cgf`, `tt-take-cover`)

| test | message |
|---|---|
| `CE2094_ARiflemanInTheOpen_SeesTheHostile_TakesCover_AndEndsHiddenFromIt` | *the SOP should react with TakeCover; runs Idle (Sop)* |
| `CE2101_PreviewFromEdit_PerceivesAndTakesCover_AndStopRewindsToTheEdit` | *preview: the SOP must react with TakeCover; runs Idle* — after `TargetMemory.Count > 0`, so perception works |
| `CE2101_ASecondLiveLoad_StartsFromTheFile_OnEveryHost` | *first load: the rifleman must move off its authored spot (TakeCover)* |

The ordered ones (`CE2109_*`) pass.

## Where I would start

`d6ffe6a86` (AQ98, R-2070) changed `BasicInfantrySop`'s hit / near-miss / contact rows from `TakeCover` to `FightFromCover` and
added *EnemyInSight AND NoTask → FightFromCover* before Idle. So the rail's expected name `TakeCover` is stale — ⚠ but the run
reports **`Idle`**, not `FightFromCover`: the new rows do not fire for this rifleman on the cluster harness. That is the part
I cannot explain from the backend side.

Run alone: `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests --filter "FullyQualifiedName~TakeCoverScenarioTests"`
(~3 min). ⛔ backend has not touched the SOP or the rail.
