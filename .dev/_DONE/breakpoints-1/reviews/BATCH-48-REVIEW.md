# BATCH-48 Review — UBP-P10T1 + UBP-P10T2

**Reviewer:** Dev Lead  
**Tests before:** 103  
**Tests after:** 108 (all passing)  
**Build:** 0 errors, 0 warnings

---

## Files created / modified

- `Hrot/Subsystems/Hrot.Editor/Hrot.Editor.csproj` — added 2 project references
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — 4 fields, wiring block, 2 test hooks
- `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj` — added 2 project references
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — 4 fields, wiring block, 2 test hooks, `CgfNoOpTimeController`
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs` — 5 new tests

---

## Implementation review

### EditorSubsystem (P10T1) ✓

- Fields `_bpPreTickSnapshot`, `_bpSnapshotProvider`, `_bpManager`, `_bpSystem` correctly added.
- Wiring block placed after component registrations and time controller setup, **before** `_kernel.Initialize()` — correct order.
- `_bpPreTickSnapshot` mirrors all 9 component/managed-component/event registrations that `_world` receives before kernel initialisation. Complete and correct.
- `MasterSyncTimeControllerAdapter` wraps `_timeController` as `IEngineDebugTimeController` — correct.
- `PredicateCompiler(bpEditSvc, _behaviorRegistry)` passes the subsystem's behavior registry — correct; BTree trace-buffer scans will resolve node names.
- `EventScannerCompiler(bpEditSvc)` — no behavior registry; acceptable since event scanning doesn't require BTree introspection.
- Internal test hooks `DataBreakpointManager` and `BpSnapshotProvider` exposed.

### CgfSubsystem (P10T2) ✓

- Same field/using/hook structure as EditorSubsystem.
- **Key design decision:** `CgfNoOpTimeController` — correct. CGF uses a `SlaveSyncController`; it has no authority to independently pause the simulation clock. The no-op implementation means `RequestPause()` / `RequestResume()` are silent on the CGF side; the actual freeze is handled at the orchestrator level. This is consistent with DESIGN §11.2 ("breakpoints are designed for single-node usage").
- `_bpPreTickSnapshot` mirrors only `CgfComponentRegistry.RegisterAll` — slightly narrower than EditorSubsystem's full mirror. Accepted: cognitive components (`BrainBlackboard`, `BTreeTraceWorkingMemory1024`, `HsmTraceWorkingMemory1024`) are what CGF breakpoints target, and those are in `CgfComponentRegistry`. SimHost-level kinematics components are not primary BP targets for the brain node.
- Systems registered via `_context.Kernel.RegisterGlobalSystem(...)` before `_context.Kernel.Initialize()` — correct.

---

## Test quality review

### `EditorSubsystem_Init_RegistersManager` ✓
- Boots headless EditorSubsystem, checks `DataBreakpointManager != null`.
- Minimal but sufficient: proves the wiring block ran to completion.

### `EditorSubsystem_Init_RegistersBreakpointSystems` ✓
- Checks both test hooks non-null, then calls `Kernel.Update()` once.
- A single successful `Update()` proves both `DebugSnapshotProvider.Execute()` and `DataBreakpointSystem.Execute()` were invoked by the kernel without exception — correct proxy for "both systems are registered in the execution pipeline".

### `EditorSubsystem_Boot_NoExtraCost_WhenNoBreakpoints` ✓
- 100 ticks, asserts `IsPaused=false`, `PendingMutationsCount=0`, `HasMountedDelegates=false`.
- **Non-trivial:** `HasMountedDelegates=false` is the actual gate-closed check from the library; if the snapshot provider had incorrectly enabled itself, subsequent ticks would show a non-zero effect. This validates the zero-overhead contract.

### `CgfSubsystem_Init_RegistersManager` ✓
- Full DDS boot via `HrotRunnerHarness("simhost,cgf", domainId)`.
- Asserts `harness.Cgf.DataBreakpointManager != null` — correct; uses the production path.

### `CgfSubsystem_HeavyScenario_NoBreakpoints_ZeroOverhead` ✓
- 50 CGF ticks via `harness.PumpFrames(50)`, gate-closed assertions.
- Validates zero overhead under real CGF kernel execution (not a mock).

---

## Issues found

### P2 — `CgfNoOpTimeController.IsPausedByDebugger` always returns `false`

The no-op controller always returns `false` even when `manager.IsPaused == true`. Any code path that checks `timeController.IsPausedByDebugger` (instead of `manager.IsPaused`) to decide whether to render the temporal status banner or suppress UI updates will get a stale result on CGF. In practice, the temporal status banner (P10T3, P8T4) reads from `IDataBreakpointManager.IsPaused` directly, so this is unlikely to cause visible bugs. Logged as P2 — should be reviewed when P10T3/P8T4 are wired in CGF perspective.

### P3 — CGF snapshot schema is narrower than EditorSubsystem's

`_bpPreTickSnapshot` in CGF only registers `CgfComponentRegistry`. If `_context.World` has additional ad-hoc component registrations (from `HrotNodeBuilder` internals), the snapshot will silently skip those components during `SyncFrom`. For AI debugging this is acceptable; for future spatial/lifecycle breakpoints on CGF-specific non-cognitive components it may cause silent predicate misses. Logged as P3.

---

## Debt tracker additions

| ID | Source | Description | Priority | Target Batch |
|----|--------|-------------|----------|--------------|
| D-BP-01 | BATCH-48 | `CgfNoOpTimeController.IsPausedByDebugger` returns false even when manager is paused; review when P10T3 temporal banner is wired to CGF perspective | P2 | BATCH-50 |
| D-BP-02 | BATCH-48 | CGF `_bpPreTickSnapshot` only mirrors `CgfComponentRegistry`; may miss HrotNodeBuilder-internal component registrations for non-cognitive predicates | P3 | Backlog |

---

## Overall verdict

**APPROVED.** Implementation is correct and well-structured. The `CgfNoOpTimeController` design is correctly reasoned and documented (slave-node constraint). All 5 tests are non-trivial: they exercise the production boot path, kernel execution, and gate state. No P1 issues.

---

## Suggested git commit message

```
feat: UBP-P10T1+P10T2 wire DataBreakpointManager into Editor & CGF subsystems

EditorSubsystem.Initialize():
- Allocates _bpPreTickSnapshot (mirrors full live-repo component schema)
- Constructs DebugSnapshotProvider, DataBreakpointManager (with MasterSyncTimeControllerAdapter),
  DataBreakpointSystem; registers both with kernel before Initialize()
- Exposes internal DataBreakpointManager / BpSnapshotProvider test hooks

CgfSubsystem.Initialize():
- Same wiring via _context.Kernel; uses CgfNoOpTimeController (slave node has no
  MasterSyncController; pause authority belongs to orchestrator layer)
- CgfComponentRegistry mirrored onto _bpPreTickSnapshot

Project refs: Hrot.Editor + Hrot.CGF now reference Hrot.Diagnostics.Breakpoints
and Hrot.Blueprints.Editor.

Tests: 5 new integration tests in BreakpointSubsystemWiringTests.cs (108 total, all green)
```
