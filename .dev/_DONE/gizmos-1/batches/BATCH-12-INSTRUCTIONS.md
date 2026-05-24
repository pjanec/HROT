# BATCH-12 Developer Instructions

**Tasks:** GZ031, GZ032, GZ033
**Phase:** Phase 11 — System Integration and Wiring
**Goal:** Wire selection filtering, add DebugGizmoLayer to SimHost, and create the DDS egress publisher.

---

## Context

The gizmo framework now has correct primitives, persistence, hit-testing, and rendering (BATCH-11).
The three remaining wiring gaps prevent the framework from behaving correctly in production:

1. **GZ031:** Selection predicate is `null` → every gizmo renders for every entity every frame
   (no selection filtering).
2. **GZ032:** `DebugGizmoLayer` is not in `SimHostVisualization.cs` → gizmo primitives produced
   by gizmo systems in the SimHost process are never rendered.
3. **GZ033:** No publisher broadcasts the `DebugPrimitiveBuffer` contents over DDS → remote
   viewers receive nothing even though `DebugPrimitivesBatch` DDS topic was defined in GZ016.

Build command: `dotnet build IOS-IG-SimHost.sln`
Test command: `dotnet test IOS-IG-SimHost.sln`

---

## TASK-GZ031 — Fix Selection Filtering in IgApplication

### What to change

**File:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

Locate the two gizmo system registrations (around line 1237 and 1246). They both pass
`isSelectedPredicate: null` which means "draw everything always". Replace each `null` with a
delegate that checks `SelectionState.IsSelected`.

Current code:
```csharp
_kernel.RegisterGlobalSystem(new DataDrivenGizmoSystem(
    _gizmoRegistry,
    _gizmoBuffer,
    isSelectedPredicate: null));
```

Replace the `null` argument with:
```csharp
isSelectedPredicate: static (view, entity) =>
    view.HasComponent<SelectionState>(entity) &&
    view.GetComponentRO<SelectionState>(entity).IsSelected
```

Do the same replacement for the `StatelessGizmoSystem` registration a few lines below.

The `static` modifier avoids a closure allocation on the hot path. `SelectionState` is in
`Hrot.Core.Components.Map` namespace — it is already imported in `IgApplication.cs`.

### Success conditions

- SC-GZ031-1: `DataDrivenGizmoPredicateTests` (existing) all pass.
- SC-GZ031-2: `StatelessGizmoSystemTests` (existing, from BATCH-10) all pass.
- Build compiles with 0 errors.

---

## TASK-GZ032 — Wire DebugGizmoLayer into SimHostVisualization

### What to change

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`

Add a `DebugPrimitiveBuffer` field and a `DebugGizmoLayer` to the map layer stack.

**Step 1 — Add field** (near the other private fields, around line 61–78):
```csharp
private DebugPrimitiveBuffer? _gizmoBuffer;
```

**Step 2 — Expose property** (near the other `GetXxx` methods):
```csharp
public DebugPrimitiveBuffer? GizmoBuffer => _gizmoBuffer;
```

**Step 3 — Initialize and add layer** (after the existing
`_map.AddLayer(new SimHostTrajectoryLayer(...))` call, around line 220):
```csharp
// Gizmo debug overlay (GZ032).
_gizmoBuffer = new DebugPrimitiveBuffer();
_map.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer, bus, _map, repo));
```

The `bus` parameter is the `FdpEventBus` that SimHostVisualization already receives or
constructs. If SimHostVisualization receives `bus` through `Initialize(...)`, use that
parameter. Check the `Initialize` signature to confirm. If it doesn't have a bus parameter,
look for how the existing EventBus is constructed in `Initialize` and use the same instance.

**Namespace imports to add** (if not already present):
```csharp
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Layers;
```

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

Register the gizmo systems using the buffer from visualization. Find the block where
`_kernel.RegisterGlobalSystem(...)` calls are made (around lines 365–515). After the last system
registration (before `_kernel.Initialize()` if one exists), add:

```csharp
// Gizmo systems (GZ032) — must be registered before kernel.Initialize().
if (_vis?.GizmoBuffer is { } gizmoBuffer && _gizmoRegistry != null)
{
    _kernel.RegisterGlobalSystem(new DataDrivenGizmoSystem(
        _gizmoRegistry,
        gizmoBuffer,
        isSelectedPredicate: static (view, entity) =>
            view.HasComponent<SelectionState>(entity) &&
            view.GetComponentRO<SelectionState>(entity).IsSelected));

    _kernel.RegisterGlobalSystem(new StatelessGizmoSystem(
        _statelessGizmoRegistry,
        gizmoBuffer,
        isSelectedPredicate: static (view, entity) =>
            view.HasComponent<SelectionState>(entity) &&
            view.GetComponentRO<SelectionState>(entity).IsSelected));
}
```

If `_gizmoRegistry` and `_statelessGizmoRegistry` do not exist in `SimHostApp`, check whether
`SimHostApp` already has a `GizmoRegistrar.RegisterAll(...)` call (look for `GizmoRegistrar`
usage) or if gizmo registration has not yet been done for SimHost. If these fields do not exist,
check how `IgApplication.cs` declares and initializes `_gizmoRegistry` and
`_statelessGizmoRegistry` (search for those fields in IgApplication.cs) and replicate the same
pattern in SimHostApp.cs.

**Important:** If `_vis` is null at system registration time (e.g., visualization is initialized
after system registration), you may need to defer the layer/system wiring or initialize `_vis`
earlier. Look at the `Initialize()` call sequence in SimHostApp.cs.

### Namespace imports to add to SimHostApp.cs (if not already present):
```csharp
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.Core.Components.Map;
```

### Success conditions

- SC-GZ032-1: After `SimHostVisualization.Initialize(...)`, `GizmoBuffer` is non-null.
- SC-GZ032-2: `_map.Layers` contains a `DebugGizmoLayer` instance.
- Build compiles with 0 errors.

---

## TASK-GZ033 — Wire DebugPrimitivesBatch DDS Egress

### What to create

**New file:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DebugPrimitivesBatchPublisherSystem.cs`

```csharp
using Fdp.Core;
using Fdp.Network.Cyclone;
using Fdp.Toolkit.Diagnostics.Gizmos.Primitives;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems;

/// <summary>
/// Post-simulation system that reads the current frame from <see cref="DebugPrimitiveBuffer"/>
/// and publishes a <see cref="DebugPrimitivesBatch"/> DDS sample.
/// When no DDS writer is provided, the system is a no-op (local-only mode).
/// </summary>
public sealed class DebugPrimitivesBatchPublisherSystem : IEcsModuleSystem
{
    private readonly DebugPrimitiveBuffer _buffer;
    private readonly IDdsWriter<DebugPrimitivesBatch>? _writer;
    private readonly byte _nodeId;
    private uint _frameNumber;

    public DebugPrimitivesBatchPublisherSystem(
        DebugPrimitiveBuffer buffer,
        byte nodeId,
        IDdsWriter<DebugPrimitivesBatch>? writer = null)
    {
        _buffer  = buffer;
        _nodeId  = nodeId;
        _writer  = writer;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (_writer == null) return;

        var frame = _buffer.GetFrame();
        if (frame.Length == 0) return;

        var primitives = new DebugPrimitive[frame.Length];
        frame.CopyTo(primitives);

        _writer.Write(new DebugPrimitivesBatch
        {
            FrameNumber = _frameNumber++,
            NodeId      = _nodeId,
            Primitives  = primitives,
        });
    }
}
```

**Check** what namespace/type `DebugPrimitivesBatch` lives in — search for `class DebugPrimitivesBatch`
or `struct DebugPrimitivesBatch` in the codebase. It was created in TASK-GZ016 and is likely in
`Fdp.Toolkit.Diagnostics.Gizmos.Network` or similar. Add the correct `using` directive.

**Check** what namespace/type `IDdsWriter<T>` lives in — search for `IDdsWriter` in the codebase
(probably in `FDP/Network/Fdp.Network.Cyclone/` or similar). Add the correct `using` directive.

**Check** `IEcsModuleSystem` — make sure the interface is available (it's used by all ECS systems
in the project — look at how other system files import it, e.g., `DataDrivenGizmoSystem.cs`).

**Note:** If `DebugPrimitivesBatch.Primitives` is a fixed-size array (e.g.,
`[MarshalAs(UnmanagedType.ByValArray)]`), the copy/assignment approach may need adjustment. Check
the actual field type and adapt accordingly.

### Tests

Add a new test file: `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/DebugPrimitivesBatchPublisherTests.cs`

Use a mock `IDdsWriter<DebugPrimitivesBatch>` (implement a simple capturing class) to verify:

- **SC-GZ033-1:** Buffer with N primitives → exactly one Write call with `Primitives.Length == N`.
- **SC-GZ033-2:** Empty buffer → zero Write calls.
- **SC-GZ033-3:** Null writer → Execute returns without exception.
- **SC-GZ033-4:** `FrameNumber` increments by 1 per Execute call.

### Success conditions

- All 4 SC-GZ033 tests pass.
- Build compiles with 0 errors.

---

## Build Validation

After completing all three tasks:
```
dotnet build IOS-IG-SimHost.sln
```
Must succeed with 0 errors.

Run the full test suite and verify:
- No NEW failures compared to the pre-existing 30 failures (26 in Fdp.Toolkits.Tests + 4 in Hrot.IG.Tests).
- SC-GZ031, SC-GZ032, SC-GZ033 tests pass.

---

## Notes

- `DataDrivenGizmoPredicateTests` in `Hrot.ClusterRunner.Tests` tests the `isSelectedPredicate`
  contract end-to-end — these must remain passing.
- Do NOT change `DebugPrimitiveBuffer`, `DebugPrimitive`, or any existing test files.
- The `static` lambda modifier for `isSelectedPredicate` is important to avoid per-frame closures.
- GZ033 uses `IDdsWriter<T>` which wraps the Cyclone DDS writer — the `null` case is the
  local-only mode where no network is present.
