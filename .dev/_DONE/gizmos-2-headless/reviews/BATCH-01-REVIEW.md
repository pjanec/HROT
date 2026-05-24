# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-13
**Status:** APPROVED

---

## Summary

All 8 tasks (GZH-001 through GZH-008) implemented correctly. 178 gizmo tests pass, 0 regressions.
The solution builds cleanly. One notable fix beyond spec: a double-dispose bug in
`GlobalGizmoManager.CancelInteractiveTools()` was caught and correctly resolved.

---

## Issues Found

No blocking issues.

### Minor Observation: `GZH003` integration tests are unit-level (acceptable)

The GZH003 tests exercise `TogglablePostSimulationGroup` in isolation rather than wiring through
the actual subsystem composition roots. This is the right pragmatic choice given the complexity of
bootstrapping full subsystems in tests. The composition-root wiring was verified to compile and
build cleanly (SimHost, Toolkits all build with 0 errors).

### Minor Observation: `GZH001_1` only tests `TerminalConnectedEvent`

The test exercises `TerminalConnectedEvent` but not `TerminalDisconnectedEvent`. Not a blocking
issue since both classes are structurally identical, but a `GZH001_2` covering `TerminalDisconnectedEvent`
would be belt-and-suspenders coverage. Deferred to DEBT-TRACKER as P3.

---

## Test Quality Assessment

Tests verify actual behavior throughout:

- **GZH002**: verifies exact `ListenerCount` values and `group.Enabled` transitions; uses a real
  `GlobalGizmoManager` for `GZH002_2` to confirm `OnCancel` fires synchronously — not a mock
  shortcut.
- **GZH004**: permanent gizmo survival (`ActiveCount == 1`) explicitly asserted; `!permanent.Disposed`
  verified.
- **GZH005**: both `OnCancelCalled` and `Disposed` flags checked, plus `HasInjectedGizmo` confirms
  the gizmo was removed.
- **GZH006**: `GZH006_3` correctly uses `ApplyUpdate` → then verifies no echo-back — this is the
  critical cache invariant from DESIGN.md §2.1 and it is tested properly.
- **GZH007_4**: concurrent-modification test uses a real thread and asserts `null` exception — not
  a fake concurrent test.
- **GZH008**: last-write-wins and empty-after-poll are both verified with actual state values.

---

## Verdict

**Status: APPROVED**

All requirements met. No P1 issues. Ready to commit.

---

## Commit Message

```
feat(gizmos): zero-CPU headless + UI state infrastructure (BATCH-01)

Completes GZH-001, GZH-002, GZH-003, GZH-004, GZH-005, GZH-006, GZH-007, GZH-008

Phase 1 — Zero-CPU Headless Infrastructure:
- TerminalConnectedEvent / TerminalDisconnectedEvent managed events
- GizmoExecutionController: reference-counted TogglablePostSimulationGroup gate
  with synchronous CancelInteractiveTools() teardown at count==0
- GlobalGizmoManager.CancelInteractiveTools(): cancel on-demand tools, keep permanent
  (fix: remove focused gizmo from _activeGizmos before dispose to prevent double-dispose)
- DataDrivenGizmoSystem.CancelInteractiveTools(): cancel all injected gizmos
- All four composition roots wired into TogglablePostSimulationGroup:
  SimHost + CGF: Enabled=false (headless-first); IG + Editor: Enabled=true

Phase 2 — UI State Infrastructure:
- StructInspectorProjector<T>: dual-channel helper with JSON change-detection
  and echo-prevention via cache update on ApplyUpdate
- GizmoUiStateHub: thread-safe snapshot-copy broadcaster
- LocalGizmoUiStateTransport: ConcurrentDictionary last-write-wins in-memory transport

Tests: 178 gizmo tests pass (18 new), 0 regressions
```

---

**Next Batch:** BATCH-02 (GZH-009, GZH-010, GZH-011, GZH-015)
