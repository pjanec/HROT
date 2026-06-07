# BATCH-12 Review

**Verdict: APPROVED**

---

## Test Coverage Assessment

### EQS-030 — Action_MoveToOptimalCover

All three success conditions from TASK-DETAIL covered:

| SC | Test | Coverage |
|----|------|----------|
| SC1 | T-COV1: buffer ready, asserts `ActiveAction == ActionIdMoveTo` and `Destination == (10,20)` | Full |
| SC2 | T-COV2: buffer not ready (`Count=0, LastUpdateTick=0`), asserts `NodeStatus.Failure` | Full |
| SC3 | T-COV3: channel already `Status=Success`, asserts forwarded `NodeStatus.Success` | Full |

T-COV1 uses `buffer.GetSpanRW()[0]` for writing and unsafe pointer cast to read `MoveToParams` back from the channel — both are correct patterns.

### EQS-031 — HideInCover_BT

| SC | Test | Coverage |
|----|------|----------|
| SC1 (build) | Verified: 0 errors, 0 warnings | Full |
| SC2 (channel set with threat) | T-COV5 Phase A: `MaintainEqsSensor` -> pre-populate buffer -> `MoveToOptimalCover` -> assert `ActionIdMoveTo` | Full |
| SC3 (sensor removed without threat) | T-COV5 Phase B: clear `TargetMemory` -> `Deactivate_MaintainEqsSensor` -> assert both components gone | Full |

T-COV4 additionally validates `Condition_HasTarget` across three cases (no component, empty memory, live threat) — good edge coverage.

---

## Code Quality

- `Action_MoveToOptimalCover` correctly: checks both components before reading, respects `IsReady && Count > 0`, propagates `BehaviorInstanceId`, checks terminal status before re-activation, increments `ActionInstanceId` to signal dispatcher.
- Zero-allocation param copy via `fixed (byte* dst = channel.Params) { *(MoveToParams*)dst = moveToParams; }` — correct.
- `Condition_HasTarget` uses an `unsafe` block for fixed-array access, correctly bounded by `mem.Count`.
- `HideInCoverBlackboard` has the correct sequential layout. Field order matches usage in the tree (`EqsConfig` first for `MaintainEqsSensor`/`WaitForSensor`, `MoveConfig` for locomotion nodes).
- `BuildHideInCoverTree` tree structure matches spec exactly: `ObserverSelector -> Sequence(Condition, Parallel(RequireOne, Maintain, Sequence(Wait, Move, Hold))) / Wander`.

## Deviation Assessment

**`Policy` local class**: Acceptable. `Policy.RequireOne` does not exist in `Fbt` in this codebase. The local `internal static class Policy` with `RequireOne = 1` matches the semantics documented in `Fbt.Compiler.md` (Parallel policy 1 = RequireOne). The class is `internal` so it will not leak into consuming assemblies.

---

## Result

**33 / 33 EQS integration tests PASS. Build: 0 warnings, 0 errors. APPROVED for commit.**
