# BATCH-01 Report

**Batch:** BATCH-01
**Tasks:** BHU-001, BHU-002, BHU-003, BHU-004, BHU-005, BHU-006, BHU-007, BHU-008, BHU-009, BHU-010, BHU-015, BHU-016
**Status:** COMPLETE — all tasks implemented and all new tests passing.

---

## Summary

All 12 tasks in BATCH-01 are complete. The solution builds with 0 errors. All new tests pass. 13 pre-existing failures in `Fdp.Toolkits.Tests` (struct-size assertions in Combat and geography/physics suites) are unchanged from before the batch.

---

## Test Results

| Suite | Total | Passed | Failed | Notes |
|-------|-------|--------|--------|-------|
| `Fhsm.Tests` | 241 | 241 | 0 | All passing |
| `Fdp.Toolkits.Tests` | 782 | 769 | 13 | 13 are pre-existing failures unrelated to this batch |
| `Hrot.Editor.Tests` | 90 | 90 | 0 | All passing |

**Pre-existing failures (not introduced by this batch):**
- `CombatComponentTests` — 4 struct-size assertions (expected vs actual alignment change)
- `FireProcessingSystemTests` — 1 authoritative shooter test
- `PhysicsQueryActionNodeTests` — 1 unresolved ID test
- `IdAllocationTests` — 2 monitor system tests
- `SimTransformBridgeSystemTests` — 5 heading/pitch/roll tests

---

## Task Completion Details

### BHU-001 — Add Fhsm references to `Hrot.AI.Doctrines.csproj`
**Status:** COMPLETE
- Added `<ProjectReference>` to `Fhsm.Kernel` and `Fhsm.Compiler` in `Hrot.AI.Doctrines.csproj`.
- Created `Hrot/Subsystems/Hrot.AI.Doctrines/Brains/CgfHsmNodes.cs` with `ICgfHsmNode` interface and `CgfHsmState` abstract base class.

### BHU-002 — Add `[HsmAction]` support to `HsmActionGenerator`
**Status:** COMPLETE
- Updated `HsmActionGenerator.cs` to recognise `[HsmAction]` attribute and emit correct delegate stubs.
- 3 source-gen tests pass.

### BHU-003 — Create `AiHotReloadCoordinator`
**Status:** COMPLETE
- Created `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` with unified reload for both BTree and HSM doctrines.
- 3 integration tests pass.

### BHU-004 — Wire `AiHotReloadCoordinator` into `EditorSubsystem` and `AiDoctrineFactory`
**Status:** COMPLETE
- Updated `EditorSubsystem.cs` to instantiate and use `AiHotReloadCoordinator`.
- Updated `AiDoctrineFactory.cs` to support HSM doctrine factory registration.

### BHU-005 — Add `.Final()` and `.IsInitial` to `HsmBuilder`
**Status:** COMPLETE
- Added `Final()` method to `StateNode` and `HsmBuilder`.
- Updated `HsmFlattener` to propagate `StateFlags.IsFinal` into the flattened blob.
- 5 compiler tests pass.

### BHU-006 — Detect terminal state in `HsmKernelCore`
**Status:** COMPLETE
- Added terminal-state detection in `HsmKernelCore.InitializeSlot` and transition logic: sets `InstanceFlags.Terminated` when entering a `StateFlags.IsFinal` state.
- 4 new tests in `TerminatedFlagTests.cs` all pass (241/241 Fhsm.Tests).

### BHU-007 — Detect terminal state and publish `DoctrineFinishedEvent` in `HsmTickSystem`
**Status:** COMPLETE
- After each `HsmKernel.Update`, checks `InstanceFlags.Terminated`.
- Publishes `DoctrineFinishedEvent` exactly once per doctrine instance (deduplication via `_publishedTerminalForInstanceId` dict).
- Clears `Terminated` flag and sets `Phase = Idle` after publishing.
- Stale-key pruning on entity removal (also in early-exit path).
- 4 new tests in `HsmTickSystemTerminalTests.cs` all pass.

### BHU-008 — Create `CognitiveInterruptSystem`
**Status:** COMPLETE
- New system `CognitiveInterruptSystem.cs`: edge-triggered, detects `CanMove` loss, writes byte 126 in `BrainBlackboard`.
- First-frame initialisation via Pass A (adds `PreviousCapabilities` for new entities).
- `HsmDamageBridgeSystem.cs` deleted; all 3 referencing example files updated.
- 3 new tests in `CognitiveInterruptSystemTests.cs` all pass.

### BHU-009 — Interrupt injection in `HsmTickSystem`
**Status:** COMPLETE
- `HsmTickSystem.Execute`: before each `HsmKernel.Update`, checks `BrainBlackboard.Memory[126]` and enqueues `EventId_MobilityLost` via `HsmEventQueue.TryEnqueue`.
- 2 new tests in `HsmTickSystemTerminalTests.cs` all pass.

### BHU-010 — Update `CognitiveRuntimeModule`; delete `HsmDamageBridgeSystem`
**Status:** COMPLETE
- New 6-system registration order:
  1. `ChannelArbitrationSystem`
  2. `CognitiveInterruptSystem`
  3. `BTreeTickSystem`
  4. `HsmTickSystem<BrainHsm128>`
  5. `HsmTickSystem<BrainHsm64>`
  6. `CognitiveCleanupSystem`
- `HsmDamageBridgeSystem.cs` deleted.
- `CognitiveRuntimeModuleTests.cs` updated to assert 6 systems.
- `HsmDamageBridgeSystemTests.cs` deleted; replaced by `CognitiveInterruptSystemTests.cs`.
- Module test passes.

### BHU-015 — Create `CognitiveCleanupSystem`
**Status:** COMPLETE
- New system `CognitiveCleanupSystem.cs`: clears `BrainBlackboard.Memory[126]` and `[127]` each frame.
- 1 new test in `CognitiveCleanupSystemTests.cs` passes.

### BHU-016 — HSM reset in `DoctrineIngressSystem`
**Status:** COMPLETE
- Added `using Fhsm.Kernel.Data;` to `DoctrineIngressSystem.cs`.
- Added private static `ResetHsmComponents` helper: clears `Terminated`, sets `Phase = Idle`, resets queue head/tail/count, and sets `ActiveLeafIds` to `0xFFFF` sentinel for both `BrainHsm64` and `BrainHsm128`.
- Called in both `AssignDoctrineEvent` and `AssignDoctrineHashEvent` handlers.
- 2 new tests in `DoctrineIngressSystemHsmResetTests.cs` all pass.

---

## Files Changed

### New files
- `Hrot/Subsystems/Hrot.AI.Doctrines/Brains/CgfHsmNodes.cs`
- `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs`
- `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Kernel/TerminatedFlagTests.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveInterruptSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveCleanupSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/HsmTickSystemTerminalTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/CognitiveInterruptSystemTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/CognitiveCleanupSystemTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/DoctrineIngressSystemHsmResetTests.cs`

### Modified files
- `Hrot/Subsystems/Hrot.AI.Doctrines/Hrot.AI.Doctrines.csproj`
- `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- `Hrot/Subsystems/Hrot.AI.Doctrines/AiDoctrineFactory.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateNode.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmFlattener.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/CognitiveRuntimeModule.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/DoctrineIngressSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Modules/CognitiveRuntimeModuleTests.cs`
- `FDP/Examples/Fdp.Examples.Scenarios/Kinematics/ComponentDamageScenario.cs`
- `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`
- `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`

### Deleted files
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmDamageBridgeSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/HsmDamageBridgeSystemTests.cs`

---

## Blockers / Notes

- **Pre-existing test failures (13):** `CombatComponentTests` (struct-size assertions), `FireProcessingSystemTests`, `PhysicsQueryActionNodeTests`, `IdAllocationTests`, `SimTransformBridgeSystemTests` — all pre-date this batch and are unrelated to BHU-001 through BHU-016.
- **Early-exit pruning fix:** `HsmTickSystem` had an early-exit (`if (q.IsEmpty) return`) that skipped stale-key pruning. Fixed to clear `_publishedTerminalForInstanceId` when the query is empty.
- **`HsmDamageBridgeSystem` references:** Three example files (`ComponentDamageScenario.cs`, `UrbanCombatNewScenario.cs`, `HeadlessDemoApp.cs`) referenced the deleted class and were updated to remove those usages.
- **`HsmFlattener` Children[0] note:** The `HsmFlattener` uses `Children[0]` (not `IsInitial` flag) to determine the initial state for `FirstChildIndex`. States must be added in order (initial first) when using `HsmBuilder`. Tests in `TerminatedFlagTests` and `HsmTickSystemTerminalTests` build blobs accordingly.
