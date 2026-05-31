# BATCH-05 Report

**Batch:** BATCH-05  
**Developer:** AI (GitHub Copilot)  
**Date:** 2025-06-02  
**Status:** Complete

---

## Task Completion

| Task ID  | Status | Notes |
|----------|--------|-------|
| BPF-033  | Done   | `IBlueprintDebugSession.Attach()` added; `BlueprintDebugSession._isAttached` field wires `DebugProbe.Sink` |
| BPF-031  | Done   | `IBlueprintEditorCoordinator` created; `HotReloadLogWindow` subscribes/unsubscribes via coordinator |
| BPF-032  | Done   | `HotReloadLogModelTests` rewritten to use coordinator-based `FakeCoordinator` |
| BPF-034  | Done   | `DebugPanelWindow`, `WatchPanelWindow`, `CallstackWindow` `DrawUI()` stubs query session data |
| BPF-035  | Done   | `IBlueprintWindowRegistry`, `BlueprintWindowRegistrar` (7 windows) created and wired |
| BPF-006  | Done   | `IReloadLogSink` gains `OnSoftReload`; `OnHardReset` extended with `oldHash`/`newHash`; 4 call sites updated |
| BPF-007  | Done   | `BlueprintRegistry.GetAll()` changed to return `IReadOnlyList<(int Id, BlueprintDefinition Def)>` |
| BPF-008  | Done   | `GetSlotEntry`, `SetChannelStatus<T>`, `SnapshotAllBlackboards` added to `BlueprintTestFixture` |
| BPF-009  | Done   | `InvokeHsmAction` and `InvokeHsmGuard` implemented in `BlueprintTestFixture` |

---

## Testing Results

**Hrot.Blueprints.Tests:** 874 passed, 0 failed, 8 skipped

The 8 skipped tests are pre-existing skips (demo tests and one ALC weak-reference test that require specific compiler phases).

### New tests added this batch

**BPF-033** (`Debug/BlueprintDebugSessionLifecycleTests.cs`, 3 tests):
- `IsAttached_DefaultsToFalse`
- `Attach_SetsIsAttachedTrue`
- `Detach_SetsIsAttachedFalse`

**BPF-032** (`Editor/HotReloadLogModelTests.cs`, 3 tests):
- `OnReloadCompleted_EventFired_AddsEntry`
- `OnReloadFailed_EventFired_AddsFailEntry`
- `Dispose_Unsubscribes_FromCoordinator`

**BPF-034** (`Editor/DebugWindowDrawUITests.cs`, 3 tests):
- `DebugPanelWindow_DrawUI_QueriesIsPausedAndBreakpoints`
- `WatchPanelWindow_DrawUI_QueriesWatches`
- `CallstackWindow_DrawUI_QueriesNodeHistory`

**BPF-035** (`Editor/BlueprintWindowRegistrarTests.cs`, 7 tests):
- `RegisterWindows_AddsAssetBrowser`
- `RegisterWindows_AddsGraphEditor`
- `RegisterWindows_AddsInspector`
- `RegisterWindows_AddsDebugPanel`
- `RegisterWindows_AddsWatchPanel`
- `RegisterWindows_AddsCallstack`
- `RegisterWindows_AddsHotReloadLog`

**BPF-006** (`Runtime/BlueprintTickSystem/ReloadLogSinkTests.cs`, 2 tests):
- `OnHardReset_CalledWith_CorrectEntity_And_Hashes`
- `OnSoftReload_InterfaceMethod_IsCallable`

**BPF-007** (`Runtime/BlueprintRegistryTests.cs`, 1 test added):
- `BPF007_GetAll_Returns_Tuple_With_Correct_Id`

**BPF-008/BPF-009** (`TestHarness/FixtureHelperAndHsmInvokeTests.cs`, 7 tests):
- `GetSlotEntry_ReturnsCorrectBlueprintId_AfterAttach`
- `GetSlotEntry_Throws_WhenNoBlueprintAttached`
- `SetChannelStatus_WritesStatus_ToLocomotionChannel`
- `SnapshotAllBlackboards_ReturnsNonEmpty_ForRunningEntity`
- `SnapshotAllBlackboards_ReturnsEmpty_WhenNoEntities`
- `InvokeHsmAction_DoesNotThrow_And_Returns_True`
- `InvokeHsmGuard_ReturnsTrue_ForUnregisteredGuard`

---

## Files Modified / Created

### Production files modified

| File | Task | Change |
|------|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` | BPF-033 | Added `void Attach()` to interface |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | BPF-033 | Added `_isAttached` field; real `IsAttached` property; `Attach()` wires `DebugProbe.Sink` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/HotReloadLogWindow.cs` | BPF-031 | Constructor changed to accept `IBlueprintEditorCoordinator`; subscribes/unsubscribes events; implements `IDisposable` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs` | BPF-034 | `DrawUI()` calls `_session.IsPaused` and `_session.GetBreakpoints()` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/WatchPanelWindow.cs` | BPF-034 | `DrawUI()` calls `_session.GetWatches()` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/CallstackWindow.cs` | BPF-034 | `DrawUI()` calls `_session.GetRecentNodeHistory()` |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/IReloadLogSink.cs` | BPF-006 | Added `OnSoftReload(int, Entity, ulong)`; changed `OnHardReset` signature to include `oldHash`/`newHash`; updated `NullReloadLogSink` |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs` | BPF-006 | All 4 `OnHardReset` call sites updated; each reads `ulong oldHash = slot.StructureHash` (uint-to-ulong widening, DEBT-014) before calling the sink |
| `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` | BPF-007 | `GetAll()` now returns `IReadOnlyList<(int Id, BlueprintDefinition Def)>` via LINQ projection |

### Production files created

| File | Task | Purpose |
|------|------|---------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IBlueprintEditorCoordinator.cs` | BPF-031 | Interface exposing `OnReloadCompleted` and `OnReloadFailed` events |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IBlueprintWindowRegistry.cs` | BPF-035 | Testable interface with `Register(string name, Func<IBlueprintEditorWindow> factory)` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs` | BPF-035 | Registers all 7 blueprint editor windows via `RegisterWindows(IBlueprintWindowRegistry)` |

### Test files modified

| File | Task | Change |
|------|------|--------|
| `Hrot.Blueprints.Tests/BlueprintTestFixture.cs` | BPF-008/009 | Added `GetSlotEntry`, `SetChannelStatus<T>`, `SnapshotAllBlackboards`, `InvokeHsmAction`, `InvokeHsmGuard` methods; added `System.Collections.Immutable` and `System.IO` usings |
| `Hrot.Blueprints.Tests/CapturingDebugSession.cs` | BPF-033 | Added `public void Attach() { }` stub |
| `Hrot.Blueprints.Tests/Editor/MockDebugSession.cs` | BPF-033 | Added `public void Attach() { }` stub |
| `Hrot.Blueprints.Tests/Runtime/BlueprintRegistryTests.cs` | BPF-007 | Added `BPF007_GetAll_Returns_Tuple_With_Correct_Id` test |
| `Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs` | BPF-006 | `CapturingSink` updated to match new `IReloadLogSink` signature |

### Test files created

| File | Task | Tests |
|------|------|-------|
| `Hrot.Blueprints.Tests/Debug/BlueprintDebugSessionLifecycleTests.cs` | BPF-033 | 3 lifecycle tests |
| `Hrot.Blueprints.Tests/Editor/HotReloadLogModelTests.cs` | BPF-032 | 3 coordinator-based model tests |
| `Hrot.Blueprints.Tests/Editor/DebugWindowDrawUITests.cs` | BPF-034 | 3 window DrawUI tests |
| `Hrot.Blueprints.Tests/Editor/BlueprintWindowRegistrarTests.cs` | BPF-035 | 7 window registration tests |
| `Hrot.Blueprints.Tests/Runtime/BlueprintTickSystem/ReloadLogSinkTests.cs` | BPF-006 | 2 sink interface/call tests |
| `Hrot.Blueprints.Tests/TestHarness/FixtureHelperAndHsmInvokeTests.cs` | BPF-008/009 | 7 fixture helper tests |

---

## Issues Encountered

### 1. NodeStatus ambiguity (CS0104)

`using Fbt;` in `BlueprintTestFixture.cs` and `FixtureHelperAndHsmInvokeTests.cs` clashed with `using Hrot.Blueprints.Core.Assets;` (both namespaces define `NodeStatus`). Fixed by removing the bare `using Fbt;` and using the fully qualified `Fbt.NodeStatus` in `BlueprintTestFixture.cs`, and adding a `using FbtNodeStatus = Fbt.NodeStatus;` alias in the test file.

### 2. CompileOptions constructor signature (CS7036 + CS0234)

The `HsmOptions()` helper in `FixtureHelperAndHsmInvokeTests.cs` was missing `ChannelCommands` and `WaitPrimitives` parameters, and referenced `Hrot.Blueprints.Core.Compiler.Ir.BlueprintSignature` (wrong namespace). Also, `SiblingSignatures` takes `IReadOnlyList<BlueprintSignature>` not a dictionary. Fixed to use `BuiltInChannelCommandCatalog.Instance`, `BuiltInWaitPrimitiveCatalog.Instance`, and `Array.Empty<BlueprintSignature>()`.

### 3. BlueprintDispatchKind ambiguity (CS0104)

`ReloadLogSinkTests.cs` imported both `Fdp.Toolkit.Blueprints` and `Hrot.Blueprints.Core.Assets`, both defining `BlueprintDispatchKind`. Fixed by using the fully qualified `Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance`.

### 4. uint overflow in constant cast (CS0221)

`(uint)FakeInstanceBp.StructureHash` where `FakeInstanceBp.StructureHash = 0x0123456789ABCDEFU` overflows `uint` at compile time. Fixed with `unchecked((ulong)(uint)FakeInstanceBp.StructureHash)`.

### 5. ALC GC-reclaim failures in new tests

Five new tests that called `CompileAndLoad` failed with "1 ALC(s) not GC-reclaimed after 50 retries" because the default fixture options have `VerifyAlcUnloadOnDispose = true`. Following the established pattern in the codebase (e.g., `WhenNodeRuntimeTests.cs`), tests that compile blueprints but do not specifically test ALC lifecycle must use `VerifyAlcUnloadOnDispose = false`. Added `private static BlueprintTestFixtureOptions NoAlcCheck` helper to each test class and applied it to all compile-using fixtures.

### 6. DEBT-014 hash truncation in test assertion

`BlueprintSlotEntry.StructureHash` is `uint` (lower 32 bits of the 64-bit `BlueprintDefinition.StructureHash`). The `OnHardReset` call in `BlueprintTickSystem` widens this to `ulong` via implicit cast (zero-extends). The `OldHash` assertion must compare `unchecked((ulong)(uint)FakeInstanceBp.StructureHash)`, not the full 64-bit constant.
