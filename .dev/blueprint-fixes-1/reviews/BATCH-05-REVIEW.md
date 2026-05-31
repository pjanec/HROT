# BATCH-05 Review

**Batch:** BATCH-05  
**Reviewer:** Development Lead  
**Date:** 2026-06-02  
**Status:** APPROVED

---

## Summary

9 tasks completed (editor windows, runtime, test harness). 874 blueprint tests passing, 8 pre-existing skips. 26 new tests added.

---

## Issues Found

No issues found.

---

## Test Quality Assessment

- **BPF-033**: `IsAttached_IsFalse_BeforeAttach`, `IsAttached_IsTrue_AfterAttach`, `IsAttached_IsFalse_AfterDetach` -- full lifecycle. `Attach_Routes_DebugProbe_To_Session` uses `Assert.Same(_session, DebugProbe.Sink)` -- verifies actual routing, not just a flag.
- **BPF-031/032**: `FakeCoordinator` implements `IBlueprintEditorCoordinator`; tests fire event via coordinator and assert window received it. `Dispose_Unsubscribes_From_Coordinator` verifies cleanup. No direct method calls.
- **BPF-006**: `OnHardReset_CalledWith_CorrectEntity_And_Hashes` uses spy sink and verifies `Entity`, `oldHash`, and `newHash` args. DEBT-014 hash truncation handled correctly with `unchecked` cast.
- **BPF-007**: `BPF007_GetAll_Returns_Tuple_With_Correct_Id` asserts the `Id` in the returned tuple matches the registered blueprint Id.
- **BPF-034**: Three `DrawUI()` tests verify the window queries the session (`GetBreakpoints`, `GetWatches`, `GetRecentNodeHistory`).
- **BPF-035**: 7 separate tests each verify one window factory is registered by name.
- **BPF-008/009**: `SnapshotAllBlackboards_ReturnsNonEmpty_ForRunningEntity`, `SetChannelStatus_WritesStatus_ToLocomotionChannel`, `InvokeHsmAction_DoesNotThrow_And_Returns_True` -- behavioral fixture tests.

---

## Design Notes

- `IBlueprintEditorCoordinator` as a new interface is the correct pattern -- allows `HotReloadLogWindow` to be tested without the production module.
- `IBlueprintWindowRegistry` with `Register(string, Func<IBlueprintEditorWindow>)` is clean and testable.
- ALC `VerifyAlcUnloadOnDispose = false` pattern is established in the codebase; applied correctly.
- DEBT-014 hash truncation is a known issue (`uint` slot hash vs `ulong` definition hash); the `unchecked` cast is the correct safe workaround.

---

## Verdict

**Status: APPROVED**

All requirements met. Ready to merge.

---

## Commit Message

```
fix: Editor windows + Runtime + Test Harness fixes (BATCH-05)

Completes BPF-031, BPF-032, BPF-033, BPF-034, BPF-035, BPF-006, BPF-007, BPF-008, BPF-009

Editor windows:
- BPF-033: IsAttached backed by field; Attach/Detach implemented; DebugProbe.Sink routed
- BPF-031: HotReloadLogWindow subscribes to IBlueprintEditorCoordinator events
- BPF-032: Tests rewritten to use FakeCoordinator (coordinator path, not direct calls)
- BPF-034: DebugPanelWindow/WatchPanelWindow/CallstackWindow DrawUI() query session data
- BPF-035: BlueprintWindowRegistrar wires all 7 blueprint editor windows

Runtime:
- BPF-006: IReloadLogSink gains OnSoftReload; OnHardReset extended with Entity + oldHash/newHash
- BPF-007: BlueprintRegistry.GetAll() returns IReadOnlyList<(int Id, BlueprintDefinition Def)>

Test Harness:
- BPF-008: BlueprintTestFixture gains SnapshotAllBlackboards/SetChannelStatus/GetSlotEntry
- BPF-009: InvokeHsmAction and InvokeHsmGuard implemented in fixture

Tests: 874 blueprint tests passing. 26 new tests.
```

---

**Next Batch:** BATCH-06 (Hot Reload + Medium fixes + Cross-cutting debt)
