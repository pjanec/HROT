# BATCH-02: Dynamic Terminal Modules + LayerControlGizmo Refactor

**Batch Number:** BATCH-02
**Tasks:** DEBT-001 (corrective), GZH-009, GZH-010, GZH-015, GZH-011
**Phase:** Phase 3 (Dynamic Terminal Modules) + Phase 4 (LayerControlGizmo Upgrade)
**Estimated Effort:** 10-14 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (all Phase 1 + Phase 2 types must exist)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements two things: the installable terminal module layer on top of BATCH-01's
infrastructure, and the refactoring of `LayerControlGizmo` to use `StructInspectorProjector<T>`.

Also includes a P3 debt fix from BATCH-01 (missing test for `TerminalDisconnectedEvent`).

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Design Document:** `.dev/gizmos-2-headless/DESIGN.md` — focus on §4 (Dynamic Transport Modules),
   §5 (Remote Terminal Connect/Disconnect Detection), §7 (LayerControlGizmo Refactoring), §12
3. **Task Details:** `.dev/gizmos-2-headless/TASK-DETAILS.md` — GZH-009, GZH-010, GZH-015, GZH-011
4. **BATCH-01 Review:** `.dev/gizmos-2-headless/reviews/BATCH-01-REVIEW.md`

### Source Code Locations

- **Primary work area:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/`
- **LayerControlGizmo:** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`
- **INetworkFactory:** `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs`
- **IEcsModule:** `FDP/Engine/Fdp.ModuleHost/Abstractions/IEcsModule.cs`
- **GizmoExecutionController:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs` (BATCH-01)
- **GizmoUiStateHub:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/GizmoUiStateHub.cs` (BATCH-01)
- **StructInspectorProjector:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs` (BATCH-01)
- **Test project (FDP toolkits):** `FDP/Toolkits/Fdp.Toolkits.Tests/`
- **Existing tests (GZH-001..008):** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoHeadlessTests.cs`
- **Hrot test project:** `Hrot/Engine/Hrot.Common.Tests/` (if it exists) or `Hrot/Engine/Hrot.Core.Tests/`
- **GizmoSettingsRegistry:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`
- **SC_GZ067-SC_GZ070 regression tests:** `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts.Tests/GizmoContractsTests.cs`

### Build and Test Commands

```bat
cd FDP
dotnet build FDP.sln
dotnet test Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

For LayerControlGizmo regression tests:
```bat
cd FDP
dotnet test ExtDeps\GizmoMap\GizmoMap.Contracts.Tests\ --filter "FullyQualifiedName~SC_GZ06"
```

For building Hrot.Common:
```bat
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build Hrot/Engine/Hrot.Common/Hrot.Common.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/gizmos-2-headless/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/gizmos-2-headless/questions/BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 established the infrastructure (controller, hub, transport). This batch plugs it together:

- `LocalTerminalModule` is the IEcsModule bridge between a local Raylib window and the hub.
- `GizmoNetworkTransportModule` is the IEcsModule bridge between the DDS network and the hub.
  It also includes the `IGCapabilitiesAnnounce` ingress logic that drives the controller listener
  count as remote terminals connect and disconnect (GZH-015).
- `LayerControlGizmo` is refactored to use `StructInspectorProjector<T>` and a dynamic schema
  hash instead of a hardcoded constant.

Read DESIGN.md §4, §5, §7, and §12 carefully before writing any code.

---

## MANDATORY WORKFLOW: Test-Driven Task Progression

1. **DEBT-001:** Add `GZH001_2` test → all tests pass ✅
2. **GZH-009:** Implement → Write tests → all tests pass ✅
3. **GZH-010 + GZH-015:** Implement → Write tests → all tests pass ✅
4. **GZH-011:** Implement → Write tests → all tests pass ✅

**DO NOT** move to the next task until current task implementation and tests are complete and
all tests pass. Do not stop to ask permission for obvious actions like fixing compile errors,
running tests, or fixing failing tests. Complete everything and then write the report.

---

## Tasks

### Task 0 (Debt): GZH001_2 — `TerminalDisconnectedEvent` Round-Trip Test

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoHeadlessTests.cs` (MODIFY — add to `GZH001_Tests`)

Add `GZH001_2`: publish `TerminalDisconnectedEvent` with a specific `TerminalId` on `FdpEventBus`,
`SwapBuffers()`, then read back via `bus.ReadManaged<TerminalDisconnectedEvent>()` and assert the
ID matches.

---

### Task 1: GZH-009 — `LocalTerminalModule`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/LocalTerminalModule.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-009](../TASK-DETAILS.md#gzh-009--localterminalmodule) and DESIGN.md §4.1

**Requirements:**
- Implements `IEcsModule` from `Fdp.ModuleHost.Abstractions`.
- Constructor: `public LocalTerminalModule(GizmoExecutionController controller, GizmoUiStateHub uiHub)`.
  - Creates `_localUiTransport = new LocalGizmoUiStateTransport()`.
  - Calls `uiHub.AddEndpoint(_localUiTransport)`.
  - Calls `controller.AddListener()`.
  - Stores both arguments as fields.
- `public LocalGizmoUiStateTransport LocalUiTransport { get; }` — exposes the transport.
- `RegisterSystems()`: empty (local terminal reads `DebugPrimitiveBuffer` directly, zero-copy).
- `Tick()`: empty.
- `Name` = `"LocalTerminal"`.
- `Policy` = `ExecutionPolicy.Synchronous()` (or the equivalent — check `IEcsModule` interface for
  the exact signature). If `IEcsModule` does not have `Name`/`Policy` as interface members, make
  them public properties.
- `Dispose()`: calls `_uiHub.RemoveEndpoint(_localUiTransport)`, then `_controller.RemoveListener()`.

**Tests Required:**
- `GZH009_1`: instantiate the module; verify `controller.ListenerCount == 1`. Dispose the module;
  verify `controller.ListenerCount == 0`.
- `GZH009_2`: publish a `GizmoUiState` via the hub after constructing the module; verify
  `LocalUiTransport` receives it (use `PollAndApply`). Then dispose the module; publish another
  state via the hub; verify `LocalUiTransport` does NOT receive it.

---

### Task 2: GZH-010 + GZH-015 — `GizmoNetworkTransportModule`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/GizmoNetworkTransportModule.cs` (NEW FILE)

See: [TASK-DETAILS.md GZH-010](../TASK-DETAILS.md#gzh-010--gizmonetworktransportmodule),
[TASK-DETAILS.md GZH-015](../TASK-DETAILS.md#gzh-015--dds-lifecycle-connect-and-disconnect-detection),
and DESIGN.md §4.2 and §5.

**Important design note on testing:**
The DDS participant and writers are unavailable in unit tests. Design the module so the
`IGCapabilitiesAnnounce` lifecycle tracking logic lives in a testable inner class or is injectable
via a seam. The recommended pattern:

```csharp
// Internal class, testable without DDS:
internal sealed class GizmoCapabilitiesTracker
{
    private readonly GizmoExecutionController _controller;
    private readonly FdpEventBus _interactionBus;
    private readonly HashSet<uint> _connectedTerminalIds = new();

    public GizmoCapabilitiesTracker(GizmoExecutionController controller, FdpEventBus interactionBus)
    { ... }

    // Called by the real translator per DDS sample, or by tests directly:
    public void OnSample(uint nodeId, bool isAlive)
    {
        if (isAlive)
        {
            if (_connectedTerminalIds.Add(nodeId))
            {
                _controller.AddListener();
                _interactionBus.PublishManaged(new TerminalConnectedEvent { TerminalId = nodeId });
            }
        }
        else
        {
            if (_connectedTerminalIds.Remove(nodeId))
            {
                _controller.RemoveListener();
                _interactionBus.PublishManaged(new TerminalDisconnectedEvent { TerminalId = nodeId });
            }
        }
    }

    // Used by Dispose() to drain unbalanced counts:
    public int ConnectedCount => _connectedTerminalIds.Count;
    public void DrainAll() { foreach (var _ in _connectedTerminalIds) _controller.RemoveListener(); _connectedTerminalIds.Clear(); }
}
```

**GizmoNetworkTransportModule requirements:**
- Implements `IEcsModule`.
- Constructor accepts: `GizmoExecutionController controller`, `GizmoUiStateHub uiHub`,
  `INetworkFactory networkFactory`, `DebugPrimitiveBuffer gizmoBuffer`, `long localNodeId`,
  `FdpEventBus interactionBus`.
- When `networkFactory.Participant` is null (headless test mode): skip DDS writer adapter
  creation entirely; `_ddsUiPublisher` stays null.
- When `networkFactory.Participant` is non-null: create `DdsWriterGizmoAdapter<GizmoUiState>`
  and wrap it as a `DdsGizmoUiStatePublisher`, register with `uiHub`.
- Creates `_primitivePublisherSystem` via `networkFactory.CreateGizmoPublisherSystem(gizmoBuffer, localNodeId)` — may return null; skip `RegisterSystems` registration if null.
- Creates translators via `networkFactory.CreateGizmoTranslators(interactionBus, localNodeId, headless: false)` — registers each non-null translator.
- Creates a `GizmoCapabilitiesTracker` internally.
- In `RegisterSystems()`: also registers an internal `IEcsModuleSystem` that per-frame polls the
  `IGCapabilitiesAnnounce` DDS reader and calls `_tracker.OnSample(nodeId, isAlive)` for each
  sample. When `networkFactory.Participant` is null, this system is a no-op.
- Does NOT call `controller.AddListener()` in the constructor — that is done by `_tracker.OnSample`
  when a real terminal announces.
- `Dispose()`: calls `_tracker.DrainAll()` (balances unmatched `AddListener` calls); removes hub
  endpoint if `_ddsUiPublisher` was registered.

For the real `IGCapabilitiesAnnounce` reader, look at how `NedNetworkFactory.CreateGizmoTranslators`
works at `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs` for reference on how to read
DDS topics from the participant.

**Tests Required (all in `Fdp.Toolkits.Tests`):**

For GZH010_1 and GZH010_2, use a minimal stub `INetworkFactory` with `Participant = null` (no real
DDS), and call `_tracker.OnSample(...)` directly on the exposed `internal` tracker:

- `GZH010_1`: construct the module with a null-participant fake factory. Verify
  `controller.ListenerCount == 0`. Dispose. Verify `controller.ListenerCount == 0`.
- `GZH010_2`: using the fake factory, call `_tracker.OnSample(nodeId: 1, isAlive: true)`. Verify
  `controller.ListenerCount == 1`. Call with `nodeId: 2, isAlive: true`. Verify count == 2. Call
  with `nodeId: 1, isAlive: false`. Verify count == 1.
- Skip `GZH010_3` (real DDS integration test) — mark as deferred to DEBT-TRACKER.

For GZH-015 tests (in the same test file):
- `GZH015_1`: call `_tracker.OnSample(nodeId: 42, isAlive: true)`. After `bus.SwapBuffers()`,
  verify `TerminalConnectedEvent` is readable from the bus with `TerminalId == 42` and
  `controller.ListenerCount == 1`.
- `GZH015_2`: after connect (same node 42), call `_tracker.OnSample(nodeId: 42, isAlive: false)`.
  After `SwapBuffers()`, verify `TerminalDisconnectedEvent` on bus and `ListenerCount == 0`.
- `GZH015_3`: call `_tracker.OnSample` with `isAlive: false` for a node ID that was never added.
  Verify `TerminalDisconnectedEvent` NOT emitted (`bus.HasManagedEvent<TerminalDisconnectedEvent>()
  == false`) and `ListenerCount` unchanged.
- `GZH015_4`: call `_tracker.OnSample(nodeId: 99, isAlive: true)` twice. Verify `AddListener` was
  called only once (count == 1, not 2).

**`INetworkFactory` stub for tests:**
The existing `MockNetworkFactory` in `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` has
`Participant = null` and stub implementations. However, since the new module lives in `Fdp.Toolkits`,
which cannot reference Hrot assemblies, write a minimal local `StubNetworkFactory` directly in
the test file. It only needs to implement `Participant`, `CreateGizmoPublisherSystem()`, and
`CreateGizmoTranslators()` returning null/empty.

---

### Task 3: GZH-011 — Refactor `LayerControlGizmo`

**File:** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs` (MODIFY)

See: [TASK-DETAILS.md GZH-011](../TASK-DETAILS.md#gzh-011--refactor-layercontrolgizmo) and
DESIGN.md §7.

**Change 1 — Dynamic schema hash:**
```csharp
// BEFORE:
public const uint SchemaHash = 0x8899AABB;

// AFTER:
public static readonly uint SchemaHash =
    GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!);
```

**Change 2 — Constructor adds optional `IGizmoUiStatePublisher?`:**
```csharp
// AFTER:
public LayerControlGizmo(
    long anchorId,
    FdpEventBus interactionBus,
    IComponentEditService editService,
    IGizmoUiStatePublisher? uiPublisher = null)
```

**Change 3 — Replace raw `MakeStructInspector` + manual JSON tracking with `StructInspectorProjector<LayerControlDto>`:**
- Add `private readonly StructInspectorProjector<LayerControlDto> _projector;` field.
- Initialise in constructor: `_projector = new StructInspectorProjector<LayerControlDto>(editService, uiPublisher);`
- In `UpdateAndDraw`: replace the raw `DebugPrimitive.MakeStructInspector(...)` + `draw.EmitRaw` with:
  ```csharp
  if (_isEditing)
      _projector.EmitAndSync(draw, _anchorId, SchemaHash, _dto, ScreenAnchor.Center, SizeMode.ScreenPercent);
  ```
- In `OnStructUpdate`: replace the manual JSON deserialisation block with:
  ```csharp
  public void OnStructUpdate(string payloadJson)
  {
      if (string.IsNullOrWhiteSpace(payloadJson)) return;
      _projector.ApplyUpdate(payloadJson, ref _dto);
      _activeLayers = _dto.ToMask();
      _isEditing = false;
  }
  ```
  (Remove the `try/catch` here — `StructInspectorProjector.ApplyUpdate` handles exceptions internally.)

**Change 4 — Update composition root call sites** that construct `LayerControlGizmo` to pass the
`GizmoUiStateHub` as the `uiPublisher`. Search for `new LayerControlGizmo(` in:
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Each call site already has access to `_gizmoUiHub` (or equivalent, added in BATCH-01 GZH-003 wiring).

**Change 5 — Update terminal-side schema registry pre-seeding:**
Search for `0x8899AABB` in the codebase. Anywhere this magic constant pre-seeds a
`LayerControlDto` schema on the terminal side, replace it with:
```csharp
GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!)
```
or
```csharp
LayerControlGizmo.SchemaHash
```
(The two are equivalent; use `LayerControlGizmo.SchemaHash` where the reference is accessible.)

**Tests Required:**

Determine whether `Hrot.Common` has its own test project. If yes, add the tests there. If not,
find the nearest applicable test project that has a reference to `Hrot.Common`.

- `GZH011_1`: call `LayerControlGizmo.SchemaHash` and verify it equals
  `GizmoSettingsRegistry.ComputeHash("Hrot.Common.Diagnostics.Gizmos.LayerControlDto")`.
- `GZH011_2`: construct `LayerControlGizmo` with a mock `IGizmoUiStatePublisher`. Set `_isEditing`
  by simulating an `OpenLayerEditorEvent` on the interaction bus. Call `UpdateAndDraw`. Verify
  publisher receives exactly one `Publish` call. Call `UpdateAndDraw` again with the same DTO
  state. Verify publisher still has exactly one total call (no echo on second draw).
- `GZH011_3` (regression): run `SC_GZ067` through `SC_GZ070` and confirm they still pass.
  ```bat
  cd FDP
  dotnet test ExtDeps\GizmoMap\GizmoMap.Contracts.Tests\ --filter "FullyQualifiedName~SC_GZ06"
  ```
  These tests are in `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts.Tests/GizmoContractsTests.cs`. They
  must pass without modification.

---

## Testing Requirements

- Minimum **14 new tests** across this batch (1 debt + 2 for GZH-009 + 7 for GZH-010/015 + 3 for GZH-011).
- All tests must verify actual behavior, not just construction.
- GZH-011 regression tests count as tests; do not skip them.

---

## Quality Standards

**TEST QUALITY EXPECTATIONS:**
- GZH-010/015 tests must call `_tracker.OnSample(...)` directly and verify both the `ListenerCount`
  and the `FdpEventBus` state after `SwapBuffers()`. Simply verifying `ListenerCount` without
  checking the event bus is insufficient for GZH015_1 and GZH015_2.
- GZH-011_2 must use a real `GizmoExecutionController` path or a real `StructInspectorProjector`
  with a recording publisher — not just check that no exception was thrown.

**CODE QUALITY:**
- `GizmoCapabilitiesTracker` should be `internal sealed` in `Fdp.Toolkits`. Tests access it via
  `InternalsVisibleTo` (check if the test project already has this, or add `[assembly:
  InternalsVisibleTo("Fdp.Toolkits.Tests")]` to `Fdp.Toolkits`'s `AssemblyInfo` or
  `GlobalUsings.cs`).
- Do not add XML doc comments to new internal types.

---

## Success Criteria

- [ ] DEBT-001: `GZH001_2` added; passes.
- [ ] GZH-009: `LocalTerminalModule.cs` created; `GZH009_1` and `GZH009_2` pass.
- [ ] GZH-010: `GizmoNetworkTransportModule.cs` created; `GZH010_1` and `GZH010_2` pass.
- [ ] GZH-015: `GZH015_1` through `GZH015_4` pass.
- [ ] GZH-011: `LayerControlGizmo.cs` refactored; `GZH011_1`, `GZH011_2` pass; SC_GZ067-SC_GZ070 pass.
- [ ] All 192+ tests pass (178 from BATCH-01 + 14+ new).
- [ ] `Fdp.Toolkits.csproj` builds cleanly.
- [ ] `Hrot.Common.csproj` builds cleanly.
- [ ] Report submitted to `.dev/gizmos-2-headless/reports/BATCH-02-REPORT.md`.

---

## Common Pitfalls to Avoid

- **GZH-009**: Don't forget to call `controller.AddListener()` in the **constructor**, not in
  `RegisterSystems()`. The listener count must increment at construction time.
- **GZH-010**: Do not call `controller.AddListener()` in the constructor — only `_tracker.OnSample`
  should drive the count up. The module starts with 0 listeners; they come from terminals announcing.
- **GZH-010**: When `networkFactory.Participant` is null, the module must still function correctly
  (no NullReferenceException). The DDS writer and reader are simply absent.
- **GZH-015**: The idempotency rule (GZH015_4) is critical — arriving with the same node ID twice
  must NOT double-increment the listener count. `_connectedTerminalIds.Add()` returns false if
  already present; use that return value.
- **GZH-011**: After this change, `OnStructUpdate` no longer needs a `try/catch` since
  `StructInspectorProjector.ApplyUpdate` absorbs exceptions. Remove the exception wrapping
  to avoid double-suppression.

---

## Developer Insights (Report)

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Did you spot any weak points or inconsistencies in the existing codebase?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't in the instructions?

**Q5:** Suggested commit message for this batch?

---

## Reference Materials

- **Task Details:** `.dev/gizmos-2-headless/TASK-DETAILS.md` — GZH-009, GZH-010, GZH-015, GZH-011
- **Design:** `.dev/gizmos-2-headless/DESIGN.md` — §4, §5, §7, §12
- **IEcsModule interface:** `FDP/Engine/Fdp.ModuleHost/Abstractions/IEcsModule.cs`
- **INetworkFactory:** `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs`
- **NedNetworkFactory (DDS reference):** `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs`
- **GizmoSettingsRegistry:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`
- **Regression tests:** `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts.Tests/GizmoContractsTests.cs`
- **Existing GizmoHeadlessTests.cs:** `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoHeadlessTests.cs`
