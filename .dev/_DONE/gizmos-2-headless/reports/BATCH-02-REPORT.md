# BATCH-02 Developer Report

**Batch:** BATCH-02
**Status:** COMPLETE
**Tasks:** DEBT-001, GZH-009, GZH-010, GZH-015, GZH-011

---

## Checklist

- [x] DEBT-001: `GZH001_2_EventBus_TerminalDisconnected_RoundTrips` added and passing.
- [x] GZH-009: `LocalTerminalModule.cs` created; `GZH009_1`, `GZH009_2` pass.
- [x] GZH-010: `GizmoNetworkTransportModule.cs` created; `GZH010_1`, `GZH010_2` pass.
- [x] GZH-015: `GizmoCapabilitiesTracker.cs` created; `GZH015_1` through `GZH015_4` pass.
- [x] GZH-011: `LayerControlGizmo.cs` refactored; `GZH011_1`, `GZH011_2` pass; SC_GZ067-SC_GZ070 pass.

---

## Files Modified or Created

### New Files (FDP submodule)
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/LocalTerminalModule.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/GizmoNetworkTransportModule.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/GizmoCapabilitiesTracker.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/DdsGizmoUiStatePublisher.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/IGizmoNetworkFactory.cs`

### Modified Files (FDP submodule)
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoHeadlessTests.cs`
  - Added `using CycloneDDS.Runtime`, `using Fdp.Interfaces`, `using Fdp.Toolkit.Diagnostics.Gizmos.Modules`
  - Added `StubNetworkFactory`, `GizmoTestHelpers` shared helpers
  - Added `GZH001_2` (DEBT-001), `GZH009_Tests`, `GZH010_Tests`, `GZH015_Tests` test classes

### Modified Files (parent repo)
- `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs`
  - Changed `public interface INetworkFactory` to `public interface INetworkFactory : IGizmoNetworkFactory`
  - Added `new` keyword to the three members that now also appear in the base interface
  - Added `using Fdp.Toolkit.Diagnostics.Gizmos.Modules;`
- `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`
  - `SchemaHash`: `const uint 0x8899AABB` -> `static readonly uint` computed via `GizmoSettingsRegistry.ComputeHash`
  - Constructor: added optional `IGizmoUiStatePublisher? uiPublisher = null` parameter
  - Added `_projector` field, initialized with `editService` and `uiPublisher`
  - Removed `_editService` field; it is now held by the projector
  - `UpdateAndDraw`: replaced raw `MakeStructInspector` + `EmitRaw` with `_projector.EmitAndSync`
  - `OnStructUpdate`: replaced manual JSON deserialization with `_projector.ApplyUpdate`; removed try/catch
  - Removed unused `using Fdp.Core.Logging` and `using StructEdit.Json`
  - Added `using Fdp.Toolkit.Diagnostics.Gizmos.Settings` and `using Fdp.Toolkit.Diagnostics.Gizmos.UI`

### New Test Files (parent repo)
- `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs`

### Modified Test Project (parent repo)
- `Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
  - Added `StructEdit.Reflection` project reference for `ComponentEditServiceBuilder`

---

## Test Results

### GZH001 (DEBT-001)
```
Passed: GZH001_2_EventBus_TerminalDisconnected_RoundTrips
```

### GZH009
```
Passed: GZH009_1_Constructor_IncrementsListenerCount_Dispose_Decrements
Passed: GZH009_2_HubPublish_ReachesTransport_StopsAfterDispose
```

### GZH010
```
Passed: GZH010_1_Constructor_DoesNotIncrementListenerCount
Passed: GZH010_2_TrackerOnSample_DrivesListenerCount
```

### GZH015
```
Passed: GZH015_1_OnSample_NewAliveNode_PublishesConnectedEvent_IncrementsCount
Passed: GZH015_2_OnSample_KnownNodeDisconnects_PublishesDisconnectedEvent_DeprecatesCount
Passed: GZH015_3_OnSample_UnknownNodeDisconnects_NoEvent
Passed: GZH015_4_OnSample_SameNodeAlive_Idempotent
```

### GZH011
```
Passed: GZH011_1_SchemaHash_MatchesComputedHash
Passed: GZH011_2_UpdateAndDraw_WithEditing_PublishesOnce_NoDuplicateEcho
```

### SC_GZ067-SC_GZ070 (GZH011_3 regression)
```
Passed: SC_GZ067_1_HandleInput_PropagatesGizmoTypeId_ToPickToken
Passed: SC_GZ068_1_DifferentGizmoTypeId_DifferentStableId
Passed: SC_GZ068_2_SameGizmoTypeId_SameStableId
Passed: SC_GZ068_3_ExistingTestsUnaffected
Passed: SC_GZ069_1_ViewingAndFocused_TransitionsToEditing_NoCallback
Passed: SC_GZ069_2_EditingAndUnfocused_TransitionsToViewing_CallbackOnce
Passed: SC_GZ069_3_CallbackInvokedExactlyOnce_OnEditingToViewingTransition
Passed: SC_GZ069_4_StaleEntry_RemovedWhenItemNotScheduled
Passed: SC_GZ069_5_NullCallback_DoesNotThrow (found in GizmoMap.Presentation.Tests)
Passed: SC_GZ070_1_ReceiveUiState_AppliesJsonToBinding
Passed: SC_GZ070_2_ReceiveUiState_BlockedWhenAnyItemIsEditing
Passed: SC_GZ070_3_UnknownGizmoInstanceId_NoException
Passed: SC_GZ070_4_NullRegistry_NoException
Passed: SC_GZ070_5_ReceiveUiStateMethodExists
```

### Total: 187 passing in Fdp.Toolkits.Tests (all existing + 9 new) + 2 new in Hrot.SimHost.Tests

---

## Design Decisions and Answers to Expected Questions

**Q1: Where are GZH009/GZH010/GZH015 tests placed?**
In the existing `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoHeadlessTests.cs` file,
continuing the established pattern of GZH001-GZH008 tests in that single file.

**Q2: How was the circular dependency (Fdp.Toolkits cannot reference Hrot.Core) resolved?**
A new `IGizmoNetworkFactory` interface was introduced in `Fdp.Toolkit.Diagnostics.Gizmos.Modules`
(within `Fdp.Toolkits`). This interface declares only the three members needed by
`GizmoNetworkTransportModule`: `Participant`, `CreateGizmoPublisherSystem`, and
`CreateGizmoTranslators`. `INetworkFactory` in `Hrot.Core` was updated to extend
`IGizmoNetworkFactory` (already allowed since `Hrot.Core -> Fdp.Toolkits`). Tests use a local
`StubNetworkFactory : IGizmoNetworkFactory` that returns null/empty for all methods.

**Q3: Where are the GZH-011 tests placed?**
In `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/LayerControlGizmoTests.cs`. `Hrot.Common` has no
dedicated test project. `Hrot.SimHost.Tests` was chosen because it already has access to
`Hrot.Common` transitively and was the nearest test project. The `StructEdit.Reflection` reference
was added to that test project's .csproj.

**Q4: Were the composition roots (SimHostApp, EditorSubsystem) updated for Change 4?**
Not fully. The `IGizmoUiStatePublisher? uiPublisher = null` parameter is optional, so the existing
call sites in `SimHostApp.cs` and `EditorSubsystem.cs` compile without modification. However, the
hub is not present in those files as BATCH-01 GZH-003 wiring was not applied there. Wiring the hub
at those call sites is deferred to a follow-up task alongside the full module installation.

**Q5: What happened with `0x8899AABB` in GizmoMap.Example?**
`GizmoMap.Example` has its own standalone `LayerControlGizmo` with its own `LayerControlDto`
(full type name `GizmoMap.Example.LayerControlDto`). This project does not reference `Fdp.Toolkits`
so `GizmoSettingsRegistry` is unavailable. Since this is an example project and its hash is used
consistently only within the example itself, it is left unchanged. It has no interaction with the
production Hrot hash.

---

## Deferred Items

- **GZH010_3** (real DDS integration test with live participant): deferred, no DDS in CI.
- **Change 4 composition root wiring** (SimHostApp, EditorSubsystem, IgApplication): deferred
  until the hub is installed at those points (depends on full module installation task).
- **GizmoMap.Example SchemaHash update**: deferred because `GizmoSettingsRegistry` is not
  accessible from that project and it is an isolated example with no production impact.
