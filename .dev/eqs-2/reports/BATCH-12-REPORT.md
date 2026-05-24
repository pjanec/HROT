# BATCH-12 Report — HideInCover BTree (EQS-030, EQS-031)

## Summary

Implemented the final two EQS tasks: the `MoveToOptimalCover` action node (EQS-030) and the
`HideInCover_BT` behavior tree definition (EQS-031). Three new files were created; no existing
files were modified.

---

## New Files Created

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsCombatNodes.cs` | `MoveToOptimalCoverParams` struct + `EqsCombatNodes` static class with 4 node methods |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HideInCoverBehavior.cs` | `Policy` constants, `HideInCoverBlackboard` struct, `TacticsNodes` class with `BuildHideInCoverTree` |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsCombatNodesTests.cs` | 5 integration tests (T-COV1 through T-COV5) |

---

## Implementation Details

### EqsCombatNodes.cs

- `MoveToOptimalCoverParams`: sequential layout struct with `Speed` and `ArrivalRadius` fields.
- `Condition_HasTarget`: iterates `TargetMemory.ThreatScores` in an `unsafe` block; returns
  Success on first positive score.
- `Action_MoveToOptimalCover`: reads top-ranked `EqsResult` from `EqsCognitiveBuffer`, drives
  `LocomotionChannel` with `ActionIdMoveTo`. Forwards terminal status from executor. Uses
  `fixed (byte* dst = channel.Params)` to write `MoveToParams`. Marked `unsafe`.
- `Action_HoldPosition` / `Action_Wander`: intentional stubs; return `NodeStatus.Running`.

### HideInCoverBehavior.cs

- `Policy` internal static class with `RequireOne = 1` and `RequireAll = 0` constants.
  Defined in `Hrot.AI.Behaviors.Brains` namespace since `Policy` does not exist in the
  `Fbt` kernel; this is the minimal addition required for the build.
- `HideInCoverBlackboard`: two fields — `EqsParams EqsConfig` and
  `MoveToOptimalCoverParams MoveConfig` — with sequential layout.
- `TacticsNodes.BuildHideInCoverTree`: `[BTreeDefinition("HideInCover_BT")]` method returning
  `BTreeBuilder<HideInCoverBlackboard, BTreeContext>`. Tree structure:
  `ObserverSelector -> [Sequence(Condition_HasTarget, Parallel(RequireOne, ...) ), Action_Wander]`.

---

## Test Results

### New tests (EqsCombatNodesTests) — 5 / 5 PASS

| Test | Description | Result |
|------|-------------|--------|
| `EqsCombatNodes_MoveToOptimalCover_WritesChannelWithCorrectDestination` | T-COV1 (EQS-030 SC1) | PASS |
| `EqsCombatNodes_MoveToOptimalCover_ReturnsFailureWhenBufferNotReady` | T-COV2 (EQS-030 SC2) | PASS |
| `EqsCombatNodes_MoveToOptimalCover_ForwardsSuccessFromChannel` | T-COV3 (EQS-030 SC3) | PASS |
| `EqsCombatNodes_ConditionHasTarget_SucceedsWithThreatFailsWithout` | T-COV4 (EQS-031) | PASS |
| `HideInCoverBehavior_NodeSequence_SetsChannelThenCleansUpOnThreatRemoval` | T-COV5 (EQS-031 SC2+SC3) | PASS |

### Full EQS suite — 33 / 33 PASS

All pre-existing EQS tests continue to pass. No regressions.

---

## Deviations from Instructions

1. **`Policy` class location**: The instructions state `Policy.RequireOne` is in the `Fbt`
   namespace, but no such class exists anywhere in the repository. A local `internal static
   class Policy` was defined in `Hrot.AI.Behaviors.Brains` namespace inside
   `HideInCoverBehavior.cs`. This is fully consistent with the intended semantics
   (`RequireOne = 1` matches `IntParams = new[] { 1 }` in the kernel tests) and requires no
   modification to existing files.

2. **`using Fbt.Compiler` added to HideInCoverBehavior.cs**: Required for `BTreeBuilder<,>` but
   not listed in the instructions' "Usings required" section. Added to allow compilation.

---

## Build Verification

```
dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj
  => Build succeeded. 0 Warning(s). 0 Error(s).

dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/...csproj
  => Build succeeded. 0 Warning(s). 0 Error(s).
```
