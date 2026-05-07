# FDP Declarative Gizmo & Presentation Framework -- Task Detail
**Design Reference:** DESIGN.md

---

## Phase 16: Execution Flaw Repairs

**Background:** Review of the codebase against feedback2.md confirmed five structural execution
flaws in tasks previously marked complete. These tasks fix them. All are blockers for a
functionally correct interactive remote-viewer scenario.

---

### TASK-GZ043 — Fix PipelineTarget Enum: Add NodeGraph and Update All

**Design reference:** DESIGN.md §1.1, feedback2.md Flaw B

**Scope:**
Add the `NodeGraph = 4` value to `PipelineTarget` and update `All` from `3` to `7` so the flags
enum is arithmetically consistent. Also add a `SC-GZ001-3` correction: the original success
condition stated `All == Map2D | Viewport3D`; this must be updated to include `NodeGraph`.

**File to modify:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PipelineTarget.cs`

**Required change:**
```csharp
// BEFORE:
[Flags]
public enum PipelineTarget : byte
{
    None       = 0,
    Map2D      = 1,
    Viewport3D = 2,
    All        = 3
}

// AFTER:
[Flags]
public enum PipelineTarget : byte
{
    None       = 0,
    Map2D      = 1,
    Viewport3D = 2,
    NodeGraph  = 4,
    All        = 7
}
```

`All` is the bitwise OR of all defined pipeline targets. It must equal
`Map2D | Viewport3D | NodeGraph = 1 | 2 | 4 = 7`.

**Constraints:**
- Do NOT renumber existing values (`Map2D = 1`, `Viewport3D = 2` are stable; renaming breaks
  serialized DDS payloads and persisted recordings).
- All renderers that filter by `PipelineTarget.All` automatically gain `NodeGraph` filtering for
  free because `All` now covers all three bits. No renderer changes are needed in this task.
- Serialized `.fdprec` recordings that contain `PipelineTarget.All = 3` (old value) will silently
  interpret the byte as `Map2D | Viewport3D` on replay — this is an acceptable degradation.
  Recording re-serialization is out of scope.
- Any test that currently asserts `PipelineTarget.All == Map2D | Viewport3D` (the old SC-GZ001-3
  from TASK-GZ001) must be updated to assert `PipelineTarget.All == Map2D | Viewport3D | NodeGraph`.
  Find all such assertions by searching for `PipelineTarget.All` in the test suite.

**Success conditions:**
- SC-GZ043-1: `(PipelineTarget.Map2D | PipelineTarget.Viewport3D | PipelineTarget.NodeGraph) == PipelineTarget.All`.
  Test: `Assert.Equal(PipelineTarget.All, PipelineTarget.Map2D | PipelineTarget.Viewport3D | PipelineTarget.NodeGraph);`
- SC-GZ043-2: `PipelineTarget.NodeGraph == (PipelineTarget)4`.
  Test: `Assert.Equal((byte)4, (byte)PipelineTarget.NodeGraph);`
- SC-GZ043-3: `(PipelineTarget.All & PipelineTarget.NodeGraph) != 0` — NodeGraph is included in All.
- SC-GZ043-4: `(PipelineTarget.All & PipelineTarget.Map2D) != 0` and
  `(PipelineTarget.All & PipelineTarget.Viewport3D) != 0` — existing targets still covered.
- SC-GZ043-5: A `DebugPrimitive` created with `TargetView = PipelineTarget.All` has the bit pattern
  `0b00000111` at `FieldOffset(6)` — verified by reading the raw byte via `Marshal.SizeOf` inspection.
- SC-GZ043-6: Build succeeds with no errors (`dotnet build FDP/FDP.sln`).

---

### TASK-GZ044 — Fix IGCapabilitiesPublisherSystem: DDS Contract Hygiene and Reflection-Based Capability Discovery

**Design reference:** DESIGN.md §6.4, feedback2.md Flaw A

**Scope:**
Two problems to fix together:

1. **DDS IDL purity (hard requirement):** The existing `IGCapabilitiesAnnounce` struct reuses
   `LayerTreeJson` for unrelated gizmo schema data, conflating two distinct concepts in a single
   field and polluting the IDL contract. A dedicated `RegisteredGizmosJson` field must be added to
   the struct so each field in the IDL carries exactly one semantic purpose.

2. **Hardcoded capability values:** `IGCapabilitiesPublisherSystem.Execute` currently hardcodes
   `0xFF`, `0xFFFF`, and empty JSON strings instead of deriving actual capability facts from
   the registered gizmo registries and the runtime enum.

**Files to modify:**
- `Hrot/Network/Hrot.Network.NED/MapDescriptors.cs` (add `RegisteredGizmosJson` field)
- `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesPublisherSystem.cs` (rewrite Execute)

**Step 1 — Add `RegisteredGizmosJson` to `IGCapabilitiesAnnounce`:**

The struct already carries `[DdsManaged]` at the struct level and uses managed `string` fields.
Add one new field:
```csharp
// JSON array of gizmo type names registered by this IG instance.
// Example: ["HealthBarGizmo","EntityRotationGizmo","HillAttackGizmo"]
// Used by ExCon to build capability-aware UI without referencing gizmo assemblies.
// MUST NOT be conflated with LayerTreeJson (which describes the layer folder hierarchy).
[DdsManaged] public string RegisteredGizmosJson;
```
Place it after `TkbManifestJson` and before the closing brace. The IDL generator will allocate
this as a separate bounded string in the IDL file; do NOT fold the content into an existing field.

**Constructor change for the publisher system:**
```csharp
// BEFORE:
public IGCapabilitiesPublisherSystem(uint nodeId, IDdsWriter<IGCapabilitiesAnnounce>? writer = null)

// AFTER:
public IGCapabilitiesPublisherSystem(
    uint nodeId,
    IDdsWriter<IGCapabilitiesAnnounce>? writer,
    PipelineTarget supportedTargets = PipelineTarget.Map2D)
```

The registries are removed from the constructor because IG runs as a **dumb terminal** after
TASK-GZ038 (no `GizmoRegistrar.RegisterAll`, no local ECS gizmo definitions). The publisher
announces renderer capabilities only — what it can render, not what the backend produces.

**Dynamic capability derivation:**

1. **SupportedTargets:** Use the `supportedTargets` constructor parameter directly. Pass
   `PipelineTarget.Map2D` from `IgApplication`; pass `PipelineTarget.Map2D | PipelineTarget.Viewport3D`
   if a 3D viewport is also configured.

2. **SupportedLayerMask:** Set to `0xFFFF` (all 16 layers supported). The IG renderer accepts
   primitives on any layer; ExCon uses the `LayerTreeJson` hierarchy to decide which layers to
   expose in its UI, not the renderer's mask.

3. **SupportedShapeMask:** Build a `uint` mask by reflecting over `DebugPrimitiveShape` enum
   values. Using `uint` is mandatory — shapes 8, 9, 10 require bits beyond what a `byte` can
   hold (`1 << 8 = 256` overflows a byte):
   ```csharp
   uint shapeMask = 0u;
   foreach (DebugPrimitiveShape shape in Enum.GetValues<DebugPrimitiveShape>())
       shapeMask |= (1u << (int)shape);
   ```
   This dynamically discovers all shapes the runtime knows about, including `SemanticShape`,
   `MilStd2525`, and `SpatialAnchor` added by TASK-GZ050.

   **Update `IGCapabilitiesAnnounce`:** Change the `SupportedShapes` field type from `byte` to
   `uint` in `MapDescriptors.cs`. Because this is a DDS topic struct, the IDL type changes from
   `octet` to `unsigned long`. Any existing reader that parses `SupportedShapes` as a `byte` must
   be updated simultaneously.

4. **RegisteredGizmosJson:** Set to `"[]"` unconditionally. IG is a dumb terminal — it has no
   local gizmo plugins after GZ038. The field is retained in the IDL (one field per semantic
   purpose per IDL purity rules) for the non-dumb-terminal case (e.g. a developer workstation IG
   that runs local presentation plugins). Backend gizmo definitions are NOT published here; they
   belong on the `EntityAttributeSchema` topic (TASK-GZ052).

**Constraints:**
- `LayerTreeJson` (the existing field describing layer folder hierarchy) MUST NOT be modified or
  overloaded with gizmo names. These are two distinct IDL fields for two distinct semantic purposes.
- The reflection call on `DebugPrimitiveShape` is a one-time cold path in `Execute` (called once,
  gated by `_published`). No per-frame allocation.
- `SupportedShapes` field on `IGCapabilitiesAnnounce` must be changed from `byte` to `uint`
  (breaking IDL change — coordinate with any existing ExCon readers of this field).

**Success conditions:**
- SC-GZ044-1: `IGCapabilitiesAnnounce` has a `RegisteredGizmosJson` field of type `string`
  (compile-time check: `typeof(IGCapabilitiesAnnounce).GetField("RegisteredGizmosJson") != null`).
- SC-GZ044-2: `RegisteredGizmosJson` is always `"[]"` when the publisher is constructed without
  local gizmo plugins (dumb-terminal default). Test: `Assert.Equal("[]", announce.RegisteredGizmosJson);`
- SC-GZ044-3: `RegisteredGizmosJson` and `LayerTreeJson` are independent fields — modifying one
  does not affect the other (structural test: both fields exist with distinct names in the IDL).
- SC-GZ044-4: `SupportedShapeMask` field is `uint` (not `byte`). Test:
  `Assert.Equal(typeof(uint), typeof(IGCapabilitiesAnnounce).GetField("SupportedShapeMask")!.FieldType);`
- SC-GZ044-5: `SupportedShapeMask` equals the `uint` bitmask of all values currently defined in
  `DebugPrimitiveShape` at runtime, including shapes 8, 9, 10 (bits 8/9/10 set). Test:
  `Assert.True((announce.SupportedShapeMask & (1u << 10)) != 0, "SpatialAnchor bit must be set");`
- SC-GZ044-6: `SupportedLayerMask` is `0xFFFF` (all layers supported).
- SC-GZ044-7: Calling `Execute` twice (second call gated by `_published`) only writes to the
  DDS writer once.

---

### TASK-GZ045 — Wire Composition Roots: Register Missing Interaction Systems

**Design reference:** DESIGN.md §6.1, feedback2.md "Flaw A: Phantom Network Systems"

**Scope:**
The `GizmoInteractionEgressSystem`, `GizmoInteractionIngressSystem`, and
`DebugPrimitivesIngressTranslator` were implemented as part of TASK-GZ037/38 but were never
registered in the composition roots. Without this wiring, the interaction air-gap remains open.
This task performs the three registration steps required to close it.

**Files to modify:**
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

**Step 1 — Register `GizmoInteractionEgressSystem` in `IgApplication`:**

In `IgApplication.InitializeEmbedded()` (or the equivalent kernel-build method), locate the
block where `IGCapabilitiesPublisherSystem` is registered. Add immediately after:
```csharp
// GZ045: forward local gizmo interaction events to SimHost via DDS.
_kernel.AddSystem(new GizmoInteractionEgressSystem(
    (byte)_effectiveInstanceId,
    _networkAdapter?.GizmoInteractionWriter));
```
The `IDdsWriter<GizmoInteractionBatch>` must be exposed by `IIgNetworkAdapter` (add
`IDdsWriter<GizmoInteractionBatch>? GizmoInteractionWriter { get; }` to the interface if absent).

**Step 2 — Instantiate and wire `DebugPrimitivesIngressTranslator` in `IgApplication`:**

Add a field:
```csharp
private DebugPrimitivesIngressTranslator? _ingressTranslator;
```

In `InitializeEmbedded()`, after `_gizmoBuffer` is created:
```csharp
// GZ045: receive gizmo primitive stream from SimHost.
_ingressTranslator = new DebugPrimitivesIngressTranslator(
    _gizmoBuffer!,
    _networkAdapter?.DebugPrimitivesReader,
    filterNodeId: null);  // accept from any SimHost node
```

In `IgApplication.Update()` (or equivalent render-loop method), before the map canvas draw:
```csharp
_ingressTranslator?.PollAndApply();
```

**Step 3 — Register `GizmoInteractionIngressSystem` in `SimHostApp`:**

In `SimHostApp.OnLoad()`, locate the block where `DataDrivenGizmoSystem` and
`StatelessGizmoSystem` are registered. Add immediately before them:
```csharp
// GZ045: accept gizmo interaction events from remote IG terminals.
_kernel.AddSystem(new GizmoInteractionIngressSystem(
    _eventBus!,
    _networkAdapter?.GizmoInteractionReader));
```
The `IDdsReader<GizmoInteractionBatch>` must be exposed by the SimHost network adapter.

**Constraints:**
- If `_networkAdapter` is null (local/headless mode), the systems receive null readers/writers and
  are no-ops — they must not throw.
- Do not remove the comment in `IgApplication.cs` that reads
  "DataDrivenGizmoSystem is NOT registered in IG." This is still true.
- `GizmoInteractionEgressSystem` must be registered in `SystemPhase.PreSimulation` (verify the
  `[UpdateInPhase]` attribute on the class — if wrong, correct it here).
- `GizmoInteractionIngressSystem` must also run in `SystemPhase.PreSimulation` so injected events
  are visible to `DataDrivenGizmoSystem` in the same frame (PostSimulation).

**Success conditions:**
- SC-GZ045-1: In a headless integration test with both `IgApplication` and `SimHostApp` running
  in-process, a `GizmoInteractionCommitEvent` published on the IG event bus is visible as a
  `GizmoInteractionCommitEvent` on the SimHost event bus in the next frame (round-trip via the
  in-process DDS mock).
- SC-GZ045-2: `IgApplication.Update()` calls `_ingressTranslator.PollAndApply()` once per frame
  (verified by a spy/mock translator counting calls).
- SC-GZ045-3: With `_networkAdapter == null`, all three registered systems execute without
  throwing in a standalone headless scenario.
- SC-GZ045-4: `_gizmoBuffer` is populated from the ingress translator in the IG render loop
  (verified: after `PollAndApply()` with a mock reader supplying a `DebugPrimitivesBatch`,
  `_gizmoBuffer.GetFrame().Length > 0`).
- SC-GZ045-5: `GizmoInteractionIngressSystem` is registered before `DataDrivenGizmoSystem` in
  the SimHost kernel (ordering assertion — both must be in `PreSimulation` or `PreSimulation` must
  run before `PostSimulation`).

---

### TASK-GZ046 — Fix GizmoInteractionProxyTool Click-Away Commit Hazard

**Design reference:** DESIGN.md §4.3, feedback2.md "Flaw B: Modal Click-Away Commit Hazard"

**Scope:**
The current `GizmoInteractionProxyTool.HandleClick` blindly commits on any left mouse release,
including drags that end over empty map space. The design mandates three distinct deactivation
paths (commit, cancel-via-right/ESC, click-away cancel). Achieving the click-away path requires
`MapCanvas.ProcessInputPipeline` to route `isPressed` state to the active tool so it can detect
presses outside the gizmo domain.

**Files to modify:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
- `FDP/Toolkits/Fdp.Toolkit.Vis2D/Abstractions/IMapTool.cs` (add `HandlePress`)
- `FDP/Toolkits/Fdp.Toolkit.Vis2D/MapCanvas.cs` (route press to active tool)

**Step 1 — Extend `IMapTool` with `HandlePress`:**
```csharp
// New optional default-implemented method on IMapTool:
// Return true to consume the press; false = pass through to layers.
default bool HandlePress(Vector2 worldPos, MouseButton button) => false;
```
Adding a default-implemented method is a non-breaking change (all existing `IMapTool`
implementations continue to compile without modification).

**Step 2 — Route press events to the active tool in `MapCanvas.ProcessInputPipeline`:**
Locate the existing press-event routing block (the block that routes `leftPressed`/`rightPressed`
to layers). Before the layer routing, add:
```csharp
if (_activeTool != null && leftPressed)
{
    bool consumed = _activeTool.HandlePress(worldPos, MouseButton.Left);
    if (consumed) goto skipLayerPress; // or use a flag
}
// ... existing layer press routing ...
skipLayerPress:;
```
Use whatever control-flow idiom is already prevalent in the file (flag variable, early return
section label, etc.) — do NOT introduce `goto` if the file uses `bool` flag patterns.

**Step 3 — Implement click-away detection in `GizmoInteractionProxyTool`:**

Add field: `private bool _dragActive;`

Update `HandlePress`:
```csharp
public bool HandlePress(Vector2 worldPos, MouseButton button)
{
    if (button == MouseButton.Left)
    {
        _dragActive = true;
        return true; // consume, tool is active
    }
    return false;
}
```

Update `HandleDrag`:
```csharp
public bool HandleDrag(Vector2 worldPos, MouseButton button)
{
    if (button == MouseButton.Left && _dragActive)
    {
        _eventBus.Publish(new GizmoDragUpdateEvent { Token = _token, WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f) });
        return true;
    }
    return false;
}
```

Update `HandleClick` (left release):
```csharp
public bool HandleClick(Vector2 worldPos, MouseButton button)
{
    if (button == MouseButton.Left)
    {
        if (_dragActive)
        {
            // Was a genuine drag-and-release: commit at drop position.
            _eventBus.Publish(new GizmoInteractionCommitEvent { Token = _token, WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f) });
            _dragActive = false;
            _canvas?.PopTool();
            return true;
        }
        else
        {
            // Left release without a preceding press through this tool = click-away:
            // cancel and let the underlying tool handle the click.
            _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
            _canvas?.PopTool();
            return false; // pass through to StandardInteractionTool
        }
    }
    if (button == MouseButton.Right)
    {
        _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
        _dragActive = false;
        _canvas?.PopTool();
        return true;
    }
    return false;
}
```

**Constraints:**
- The `IMapTool.HandlePress` default implementation must return `false` so all existing tools
  that do not override it continue to work without change.
- `HandlePress` must only be called for the **active** tool (same as `HandleClick`). It must NOT
  be called for all tools in the tool stack.
- The `_dragActive` flag is reset to `false` on both commit and cancel paths.
- Unit tests do NOT require a real `MapCanvas` — pass `null` for `_canvas` in tests.

**Success conditions:**
- SC-GZ046-1: When `HandlePress` is called followed by `HandleDrag` and then `HandleClick` (left),
  exactly one `GizmoInteractionCommitEvent` is published and `_canvas.PopTool()` is called.
- SC-GZ046-2: When `HandleClick` (left) is called WITHOUT a preceding `HandlePress` (click-away
  scenario), exactly one `GizmoInteractionCancelEvent` is published, `PopTool` is called, and
  `HandleClick` returns `false`.
- SC-GZ046-3: When `HandleClick` (right) is called, exactly one `GizmoInteractionCancelEvent` is
  published and `HandleClick` returns `true` (right-click cancel is consumed by the tool).
- SC-GZ046-4: After a cancel (`HandleClick` right or click-away), `_dragActive == false`.
- SC-GZ046-5: `IMapTool.HandlePress` default returns `false` — compile and verify that existing
  `IMapTool` implementors do not require code changes.
- SC-GZ046-6: `MapCanvas.ProcessInputPipeline` calls the active tool's `HandlePress` before
  routing to layers when a left press is detected (verified by a unit test with a mock tool that
  records `HandlePress` calls).
- SC-GZ046-7 (regression): `HandleKeyPressed(Key.Escape)` still publishes `GizmoInteractionCancelEvent`
  and calls `PopTool`.

---

### TASK-GZ047 — Fix Screen-Space Coordinate Mismatch in Interaction Pipeline

**Design reference:** DESIGN.md §4.2, §4.3, feedback2.md "Flaw C: Screen-Space Coordinate Mismatch"

**Scope:**
When an operator drags a screen-space gizmo handle (e.g. a UI-glued slider using
`CoordinateSpace.Screen`), `GizmoInteractionProxyTool` currently blindly packs the hit position
into a 3D world vector using the IG's local camera matrix. The SimHost backend has no concept of
the remote operator's camera, so the received coordinate is meaningless for screen-space gizmos.

The fix: extend `GizmoDragUpdateEvent` and `GizmoInteractionCommitEvent` to carry the
`CoordinateSpace` of the picked primitive. The backend can then distinguish whether the coordinate
is a world-space absolute position or a camera-relative screen-pixel delta.

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
- `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressSystem.cs`
- `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressSystem.cs`
- `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionBatch.cs`

**Step 1 — Extend event structs:**
```csharp
// Extend GizmoDragUpdateEvent and GizmoInteractionCommitEvent to carry coordinate space:
[EventId(8052)] public struct GizmoDragUpdateEvent
{
    public PickToken Token;
    public Vector3 WorldPos;
    public CoordinateSpace Space; // NEW: World=0, Screen=1, EntityLocal=2
}

[EventId(8053)] public struct GizmoInteractionCommitEvent
{
    public PickToken Token;
    public Vector3 WorldPos;
    public CoordinateSpace Space; // NEW
}
```
`GizmoInteractionStartedEvent` and `GizmoInteractionCancelEvent` do NOT need the field (start
positions and cancellations are always world-space).

**Step 2 — Capture picked primitive's CoordinateSpace in `GizmoInteractionProxyTool`:**

Modify the constructor to accept the `CoordinateSpace` of the picked primitive:
```csharp
// BEFORE:
public GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus, MapCanvas? canvas = null)

// AFTER:
public GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus, MapCanvas? canvas = null,
                                  CoordinateSpace space = CoordinateSpace.World)
```

Store `space` in a field `private readonly CoordinateSpace _space`. Populate `Space = _space` in
`GizmoDragUpdateEvent` and `GizmoInteractionCommitEvent` before publishing.

**Step 3 — Pass `Space` through the DDS transport layer:**

Add field to `GizmoInteractionBatch`:
```csharp
public CoordinateSpace Space; // new field; byte-sized
```

Update `GizmoInteractionEgressSystem` to populate `batch.Space = evt.Space` for drag and commit
events. Update `GizmoInteractionIngressSystem` to restore `Space` when reconstructing typed events.

**Step 4 — Wire `CoordinateSpace` when pushing `GizmoInteractionProxyTool` from `DebugGizmoLayer`:**

In `DebugGizmoLayer.HandleInput`, when a hit-test succeeds:
```csharp
var hitPrimitive = ... // the DebugPrimitive that was hit
var tool = new GizmoInteractionProxyTool(
    hitPrimitive.Token(),
    _eventBus,
    _canvas,
    hitPrimitive.Space); // pass the space of the picked primitive
_canvas.PushTool(tool);
```

**Constraints:**
- The new `Space` field in the event structs must remain blittable (byte enum). No managed types
  introduced.
- Existing callers that construct `GizmoInteractionProxyTool` without the `space` parameter
  default to `CoordinateSpace.World` (backward compatible).
- The SimHost gizmo handler receiving `GizmoInteractionCommitEvent` with `Space == Screen`
  should interpret `WorldPos.XY` as screen-pixel delta relative to the gizmo anchor position,
  NOT as absolute world coordinates. Document this convention in a code comment.
- The actual backend handler logic (interpreting screen-pixel deltas) is OUT OF SCOPE for this
  task — the task only ensures the `Space` field is populated and transported correctly.

**Success conditions:**
- SC-GZ047-1: `GizmoDragUpdateEvent` with `Space = CoordinateSpace.Screen` is published by
  `GizmoInteractionProxyTool` when the proxy was created with `space = CoordinateSpace.Screen`.
- SC-GZ047-2: `GizmoInteractionBatch` carries the `Space` field; a round-trip
  (egress system writes, ingress system reads) preserves the `Space` value.
- SC-GZ047-3: `GizmoInteractionProxyTool` constructed without the `space` parameter defaults to
  `CoordinateSpace.World` (no existing tests break).
- SC-GZ047-4: `GizmoInteractionStartedEvent` and `GizmoInteractionCancelEvent` do NOT have a
  `Space` field (confirmed by compile check — adding one would be a breaking change if tests
  rely on struct size).
- SC-GZ047-5: `Marshal.SizeOf<GizmoDragUpdateEvent>()` is correct (includes the byte for Space).

---

## Phase 17: Expanded Feature Set

**Background:** feedback2.md identifies two significant features from the original design-talk
that were not covered by Phases 1–15: flight-recorder integration for post-mortem replay of
diagnostic geometry streams, and a three-scope settings model (Global / Project / Session).
These features are new; there are no existing placeholder tasks for them.

---

### TASK-GZ048 — Integrate DebugPrimitiveBuffer into FlightRecorder (Post-Mortem Replay)

**Design reference:** feedback2.md Gap A, DESIGN.md §1.3, FDP-FlightRecorder.md

**Scope:**
The existing `RecorderSystem.RecordDeltaFrame` serializes unmanaged ECS component tables only.
The `DebugPrimitiveBuffer` (diagnostic geometry and AI text traces) is completely ignored, meaning
no gizmo primitives are visible during `.fdprec` replay. This task adds a dedicated "diagnostic
channel" to the flight recorder binary format that captures the full primitive frame alongside
the component snapshot.

**Background on `RecordDeltaFrame`:**
`RecorderSystem.RecordDeltaFrame(repo, prevTick, writer, wallClockTicks, eventBus)` writes a
binary delta frame. The format already supports an optional event bus channel (second channel).
This task adds a third optional channel: the `DebugPrimitiveBuffer`.

**Files to modify:**
- `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs`
- `FDP/Engine/Fdp.Core/FlightRecorder/AsyncRecorder.cs`

**File to create:**
- `FDP/Engine/Fdp.Core/FlightRecorder/DebugPrimitivesRecorderChannel.cs`

**`DebugPrimitivesRecorderChannel` format:**

The channel is identified by a marker byte `0xDB` written before the primitive array. Layout:
```
byte  : 0xDB (channel marker)
int32 : primitive count (N)
N * 64 bytes : raw DebugPrimitive blobs (direct struct copy, no serialization)
```
If `primitiveBuffer` is null or empty (count == 0), write `0xDB` followed by `int32 = 0`
(present but empty channel). This preserves the frame structure for replay compatibility.

**`DebugPrimitivesRecorderChannel`:**
```csharp
public static class DebugPrimitivesRecorderChannel
{
    public const byte ChannelMarker = 0xDB;

    // Writes the current frame's primitives to the binary writer.
    // Caller must have already written the frame metadata.
    public static void Write(BinaryWriter writer, DebugPrimitiveBuffer? buffer)
    {
        writer.Write(ChannelMarker);
        if (buffer == null || buffer.Count == 0)
        {
            writer.Write(0);
            return;
        }
        var span = buffer.GetFrame();
        writer.Write(span.Length);
        foreach (ref readonly var p in span)
        {
            // Write raw 64 bytes per primitive.
            unsafe
            {
                fixed (DebugPrimitive* ptr = &p)
                    writer.Write(new ReadOnlySpan<byte>((byte*)ptr, 64));
            }
        }
    }

    // Reads primitives back from the binary reader into a buffer.
    // Returns the number of primitives read, or -1 if the channel marker is absent (format mismatch).
    public static int Read(BinaryReader reader, DebugPrimitiveBuffer target)
    {
        byte marker = reader.ReadByte();
        if (marker != ChannelMarker) return -1;
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            unsafe
            {
                DebugPrimitive p = default;
                var bytes = new Span<byte>((byte*)&p, 64);
                reader.Read(bytes);
                target.AppendRaw(in p);
            }
        }
        return count;
    }
}
```

**Extend `RecorderSystem.RecordDeltaFrame`:**
Add an optional `DebugPrimitiveBuffer? primitiveBuffer = null` parameter at the end. After all
existing channel writes, call:
```csharp
if (primitiveBuffer != null)
    DebugPrimitivesRecorderChannel.Write(writer, primitiveBuffer);
```

**Extend `AsyncRecorder`:**
Add an optional `DebugPrimitiveBuffer? PrimitiveBuffer` property. When set, pass it to
`RecorderSystem.RecordDeltaFrame` on each frame.

**Wiring (SimHost/ClusterRunner composition roots):**
In `SimHostApp.OnLoad()` (or wherever `AsyncRecorder` is constructed), set:
```csharp
_recorder.PrimitiveBuffer = _gizmoBuffer;
```

**Constraints:**
- The `DebugPrimitivesRecorderChannel` is a new optional channel. Existing `.fdprec` recordings
  (without the channel) must still replay correctly. The reader skips channels it does not
  recognize or reads them if present.
- `unsafe` blocks are required for raw struct-to-byte casting. `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
  is already set in `Fdp.Core`.
- Persistent primitives (`LifetimeSeconds > 0`) are already re-emitted each frame by
  `DebugPrimitiveBuffer.EndFrame` (TASK-GZ029). The recorder captures the full buffer at each
  frame, including re-emitted persistent primitives — they replay correctly without special handling.
- The `RecordDeltaFrame` signature change is backward compatible (optional parameter with default `null`).

**Success conditions:**
- SC-GZ048-1: A recording created with `RecordDeltaFrame(..., primitiveBuffer: buffer)` where
  `buffer` contains 3 primitives, when replayed via `DebugPrimitivesRecorderChannel.Read`, yields
  exactly 3 primitives with the same field values (round-trip fidelity test).
- SC-GZ048-2: A recording created WITHOUT the primitive buffer parameter (`null`) replays without
  error (backward compat — the channel is absent; no crash, no exception).
- SC-GZ048-3: A recording file created with the new format (channel present) can be distinguished
  from an old-format file by checking for the `0xDB` marker byte at the expected offset.
- SC-GZ048-4: `DebugPrimitivesRecorderChannel.Write` with an empty buffer writes exactly 5 bytes
  (1 marker + 4 count) and no primitive data.
- SC-GZ048-5: `DebugPrimitivesRecorderChannel.Read` with a malformed marker byte returns `-1`
  without throwing.
- SC-GZ048-6: `AsyncRecorder` with `PrimitiveBuffer` set calls `DebugPrimitivesRecorderChannel.Write`
  once per recorded frame (verified via a mock writer that counts `0xDB` marker writes).
- SC-GZ048-7: All existing `FlightRecorderTests` continue to pass (no regression from the optional
  parameter addition).

---

### TASK-GZ049 — Settings Scopes: Global / Project / Session Layers

**Design reference:** feedback2.md Gap B, DESIGN.md §3.2

**Scope:**
The original design called for three distinct settings scopes that control persistence and
lifecycle semantics. The current `GizmoSettingsRegistry` uses a flat active/defaults dictionary
that cannot distinguish between a temporary session tweak and a permanent project preference.

Add a `SettingScope` enum and extend `GizmoSettingsRegistry` with a scope-aware write path and
a scope-aware save/load path. The hot-path `Read` method remains O(1) with zero additional cost.

**File to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`

**File to create:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/SettingScope.cs`

**`SettingScope` enum:**
```csharp
public enum SettingScope : byte
{
    // Persists to a global user preferences file. Survives across all scenarios.
    // Example: measurement unit preference, default visibility toggles.
    Global  = 0,

    // Persists to the current project/scenario file. Overrides Global for this scenario.
    // Example: gizmo visibility configured per-scenario for a mission set.
    Project = 1,

    // In-memory only. Discarded when the scenario ends or the application restarts.
    // Example: temporary "show all gizmos" debug override during a live session.
    Session = 2,
}
```

**`GizmoSettingsRegistry` changes:**

1. Add a `Dictionary<uint, SettingScope> _scopes` field (stores the scope of the most recent
   write per key hash).

2. Change `Write` to accept scope:
```csharp
// BEFORE:
public void Write(uint keyHash, GizmoSettingValue value)

// AFTER:
public void Write(uint keyHash, GizmoSettingValue value, SettingScope scope = SettingScope.Global)
```
Store the scope: `_scopes[keyHash] = scope;`

3. Add `SettingScope GetScope(uint keyHash)`:
```csharp
public SettingScope GetScope(uint keyHash)
    => _scopes.TryGetValue(keyHash, out var s) ? s : SettingScope.Global;
```

4. Extend `SaveToDisk(string path)` to accept an optional `SettingScope scope = SettingScope.Global`:
```csharp
// Saves only settings whose stored scope matches the requested scope.
public void SaveToDisk(string path, SettingScope scope = SettingScope.Global)
```
In the enumeration, filter: `if (GetScope(hash) != scope) continue;`

5. Extend `LoadFromDisk(string path)` to accept scope:
```csharp
// Loads settings from disk and assigns them the given scope.
public void LoadFromDisk(string path, SettingScope scope = SettingScope.Global)
```
When calling `Write` internally during load, pass the scope.

6. Add `void DiscardScope(SettingScope scope)`:
```csharp
// Removes all in-memory overrides for the given scope and resets them to default.
// Used for "end of session" cleanup (discards Session-scope settings) or
// "load new project" (discards Project-scope settings before loading a new file).
public void DiscardScope(SettingScope scope)
{
    foreach (var hash in _scopes.Keys.ToArray())
    {
        if (_scopes[hash] == scope)
        {
            ResetToDefault(hash);
            _scopes.Remove(hash);
        }
    }
}
```

**Read hot-path is unchanged:** `Read(uint keyHash)` still does a single dictionary lookup in
`_active`. The scope is purely a metadata annotation used at save/load and session-end time.

**Existing `Write(uint, GizmoSettingValue)` callers:**
All existing callers default to `SettingScope.Global` (backward compatible). No call sites need
updating unless they intentionally want a different scope.

**Constraints:**
- `DiscardScope` is a cold path (called once on scenario unload). The `ToArray()` copy is acceptable.
- The hot-path `Read` method must NOT be modified. No scope check in the read path.
- `SaveToDisk` without the scope parameter continues to save Global settings only (backward compat).
- `LoadFromDisk` without the scope parameter continues to load as Global (backward compat).

**Success conditions:**
- SC-GZ049-1: `Write(hash, value, SettingScope.Session)` followed by `GetScope(hash)` returns
  `SettingScope.Session`.
- SC-GZ049-2: `Write(hash, value, SettingScope.Project)` then `SaveToDisk(path, SettingScope.Global)`
  does NOT include that key in the saved file (scope mismatch — project scope is not saved to
  global file).
- SC-GZ049-3: `Write(hash, value, SettingScope.Global)` then `SaveToDisk(path, SettingScope.Global)`
  DOES include that key in the saved file.
- SC-GZ049-4: `DiscardScope(SettingScope.Session)` resets all session-scoped settings to their
  defaults and removes them from `_scopes`. After discard, `Read(hash)` returns the default value.
- SC-GZ049-5: `DiscardScope(SettingScope.Project)` does NOT affect Global or Session settings.
- SC-GZ049-6: `Write(hash, value)` (no scope argument) defaults to `SettingScope.Global`.
  Test: `GetScope(hash) == SettingScope.Global`.
- SC-GZ049-7 (regression): All existing SC-GZ007 and SC-GZ008 tests continue to pass after the
  `Write` signature change (the extra parameter is optional with a default).
- SC-GZ049-8: `LoadFromDisk(path, SettingScope.Project)` assigns `SettingScope.Project` to all
  loaded settings. `GetScope(hash)` returns `Project` after the load.

---

## Phase 18: Data Plane Correctness and Schema Discovery

**Background:** Three inter-related problems left the primitive data plane unsafe for remote
presentation and left the ExCon UI hardwired to a static compile-time DTO. TASK-GZ050 adds the
higher-order shape primitives required for a decoupled map viewer. TASK-GZ051 fixes the
abstraction leak where ECS-local entity indices escaped into the remote primitive stream.
TASK-GZ052 establishes a runtime schema broadcast channel so the ExCon UI discovers attributes
dynamically rather than from a hardcoded DTO.

---

### TASK-GZ050 — Introduce Semantic and Routing Primitives

**Design reference:** DESIGN.md §1.1 (to be extended), feedback2.md "Introduce Semantic & Routing
Primitives"

**Scope:**
Extend `DebugPrimitiveShape` with three new values. Two are shape vocabulary extensions
(`SemanticShape`, `MilStd2525`). The third — `SpatialAnchor` — is architecturally significant:
it completely severs the presentation layer's compile-time dependency on `SimTransform` by letting
a primitive carry its own pre-resolved world position, enabling standalone map viewers that have
no access to the simulation ECS.

**File to modify:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitiveShape.cs`
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`

**Step 1 — Extend `DebugPrimitiveShape`:**
```csharp
public enum DebugPrimitiveShape : byte
{
    Line               = 0,
    Sphere             = 1,
    Box2D              = 2,
    Arrow              = 3,
    Text               = 4,
    EntityBadge        = 5,
    Icon               = 6,
    ComponentInspector = 7,
    SemanticShape      = 8,  // Entity semantic profile primitive (DIS type / tactical shape)
    MilStd2525         = 9,  // NATO MIL-STD-2525 symbology frame
    SpatialAnchor      = 10  // Pre-resolved world position + orientation; severs SimTransform dependency
}
```

**Step 2 — Add payload layouts to `DebugPrimitive`:**

`SemanticShape` payload (bytes 24–63):
```csharp
// SemanticShape payload: entity semantic shape/profile primitive.
// Carries a shape-profile key, dimensional overrides, and a pre-evaluated condition bitmask
// so the renderer can look up a registered EntityShapeProfile and render a perspective-exaggerated
// multi-part tactical shape without access to the simulation ECS.
// This primitive is emitted in CoordinateSpace.EntityLocal and MUST be preceded by a SpatialAnchor
// carrying the same NetworkId to provide position and orientation for decoupled viewers.
// For free-floating shapes with no backing ECS entity use a synthetic SpatialAnchor with a negative NetworkId.
[FieldOffset(24)] public ulong ProfileId;           // 8 bytes: DIS enumeration / shape profile registry key
[FieldOffset(32)] public float LengthMeters;        // 4 bytes: overall platform length (0 = use profile default)
[FieldOffset(36)] public float WidthMeters;         // 4 bytes: overall platform width (0 = use profile default)
[FieldOffset(40)] public uint  ConditionMask;       // 4 bytes: EntityShapeCondition bitfield (e.g. Damaged, Firing)
// bytes 44-63: unused padding
```

`MilStd2525` payload (bytes 24–63):
```csharp
// MilStd2525 payload: NATO symbol at a world position
[FieldOffset(24)] public float MilWorldPosX;
[FieldOffset(28)] public float MilWorldPosY;
[FieldOffset(32)] public FixedString32 SidcCode; // e.g. "SFGPUCI--------" (15 chars + null)
```
`FixedString32` at offset 32 aliases `TextContent` — same physical storage.

`SpatialAnchor` payload (bytes 24–63):
```csharp
// SpatialAnchor payload: pre-resolved world position and full 3D orientation.
// Severs the renderer's dependency on SimTransform for decoupled map viewers.
// Populated by the gizmo system BEFORE the buffer is shipped over DDS.
// Negative NetworkId values denote synthetic/ephemeral anchors (e.g. drag-preview spawn points)
// that have no backing ECS entity; the dumb terminal caches them identically.
[FieldOffset(24)] public long  NetworkId;           // 8 bytes: globally stable network-level entity ID
[FieldOffset(32)] public float AnchorWorldX;        // 4 bytes: world X (East)
[FieldOffset(36)] public float AnchorWorldY;        // 4 bytes: world Y (North)
[FieldOffset(40)] public float AnchorWorldZ;        // 4 bytes: world Z (Up)
[FieldOffset(44)] public float Heading;             // 4 bytes: heading in degrees (same convention as SimTransform entity inspector)
[FieldOffset(48)] public float Pitch;               // 4 bytes: pitch in degrees (same convention as SimTransform entity inspector)
[FieldOffset(52)] public float Roll;                // 4 bytes: roll in degrees (same convention as SimTransform entity inspector)
// bytes 56-63: unused padding
```
Full 3D orientation is required: entity-local arrows, semantic shapes, and rotated MIL symbols
all need heading (and optionally pitch/roll) to render correctly on a decoupled map viewer.

**`SemanticShape` design rationale:**
`SemanticShape` carries a profile key (`ProfileId`) and dimensional overrides so the renderer
can look up a registered `EntityShapeProfile` and render a complex perspective-exaggerated
multi-part tactical shape. This is distinct from `Line` or `Arrow`: it represents a semantic
entity silhouette (tank outline, helicopter rotor disk, ship hull), not a geometric primitive.
The `ProfileId` typically encodes a DIS enumeration (platform category, domain, country) so
profiles are universally identifiable without ECS access.

The `ConditionMask` is a pre-evaluated rendering instruction, NOT raw ECS state. The authoritative
simulation node evaluates heavy ECS components (e.g. `Health`, `ActorCapabilityState`) and
condenses them into a simple `uint` (`EntityShapeCondition` bitfield: `Damaged`, `Destroyed`,
`Immobile`, `Firing`, etc.). The dumb terminal receives this primitive and iterates the
`EntityShapeProfile`'s polyline definitions, performing a bitwise `AND` between `ConditionMask`
and each polyline's `ShowWhen`/`HideWhen` masks to toggle sub-elements. No ECS knowledge reaches
the terminal — `BitMask256`, `Entity`, and `ISimulationView` are strictly forbidden from
`GizmoMap.Contracts`.

By making `SemanticShape` strictly `SpatialAnchor`-dependent, we strip all spatial data from
the shape payload. This keeps the payload to 20 bytes (ProfileId + LengthMeters + WidthMeters
+ ConditionMask), leaving 20 bytes of unused padding and fully satisfying the 40-byte payload
union budget. Packing a full 3D transform (12 bytes XYZ + 16 bytes quaternion) plus profile
data would require 48 bytes, overflowing the union.

Existing `EntityLocal` primitives are NOT deprecated — they remain correct for local (in-process)
rendering. `SpatialAnchor` is the DDS-transport-safe variant.

**Constraints:**
- `DebugPrimitiveShape` is a `byte` enum. Values 0–10 are now defined. Future values must not
  exceed `byte.MaxValue - 1` (255).
- Adding new enum values does NOT break existing binary frames containing old values — unrecognized
  `Shape` discriminators are silently skipped by the renderer (existing fallthrough `default:` or
  `continue` in the render loop).
- `SidcCode` (`MilStd2525`) aliases `TextContent` at offset 32 — this is intentional and documented.
- `NetworkId` (`SpatialAnchor`) at offset 24 is a `long` (8 bytes, offsets 24–31). Negative values
  denote synthetic/ephemeral anchors. All subsequent fields are 4-byte aligned. Verify that
  `Marshal.SizeOf<DebugPrimitive>() == 64` still holds after adding these fields.
- `ProfileId` (`SemanticShape`) at offset 24 is a `ulong` (8 bytes, offsets 24–31); all
  subsequent fields are 4-byte aligned. Verify struct size invariant.
- `SemanticShape` MUST be emitted in `CoordinateSpace.EntityLocal` and MUST follow a `SpatialAnchor`
  with the same backing `NetworkId` in the same frame's buffer. The two-pass renderer caches
  `SpatialAnchor` in Pass 1 and applies the transform in Pass 2.
- Tests in `Fdp.Toolkits.Tests` that assert `ComponentInspector == 7` must NOT be broken; add new
  assertions for the new shape values alongside.

**Success conditions:**
- SC-GZ050-1: `(int)DebugPrimitiveShape.SemanticShape == 8`, `(int)DebugPrimitiveShape.MilStd2525 == 9`,
  `(int)DebugPrimitiveShape.SpatialAnchor == 10`. Unit test for each.
- SC-GZ050-2: `Marshal.SizeOf<DebugPrimitive>() == 64` after adding all new payload fields
  (struct size invariant). Test: `Assert.Equal(64, Marshal.SizeOf<DebugPrimitive>());`
- SC-GZ050-3: A `DebugPrimitive` with `Shape = SpatialAnchor`, `NetworkId = 42L`,
  `AnchorWorldX = 100f`, `Heading = 45f`, `Pitch = 0f`, `Roll = 0f` round-trips through
  `DebugPrimitivesBatch` DDS serialization with all fields preserved (write/read test).
- SC-GZ050-4: A `DebugPrimitive` with `Shape = SemanticShape`, `ProfileId = 0x3400010001000000UL`,
  `LengthMeters = 12.5f`, `ConditionMask = 0x03u` round-trips through serialization with all
  three fields preserved.
- SC-GZ050-5: A renderer receiving a `Shape` discriminator value it does not recognize (e.g. a
  future value `= 11`) silently skips the primitive without throwing.
- SC-GZ050-6: All existing `GizmosPrimitiveTests` assertions continue to pass (regression).

---

### TASK-GZ051 — Fix ComponentInspector Abstraction Leak: Replace ECS Indices with Network-Stable IDs

**Design reference:** feedback2.md "Fix Dumb Terminal Memory Leak", DESIGN.md §1.2

**Scope:**
The current `ComponentInspector` payload in `DebugPrimitive` stores `InspTargetIndex` (an ECS
entity slot index) and `InspComponentTypeId` (a runtime-assigned component type integer). Both
values are process-local: they are meaningless when the primitive is shipped over DDS to a remote
viewer. A remote IG or ExCon terminal that receives a `ComponentInspector` primitive cannot
resolve which entity or component it refers to. This task replaces those fields with a globally
stable `long NetworkId` and a `uint SchemaHash` — a pure IDL-transportable description.

**Files to modify:**
- `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/DebugPrimitive.cs`
- Any callsite that sets `InspTargetIndex` or `InspComponentTypeId` (search across solution)

**Step 1 — Replace fields in `DebugPrimitive`:**
```csharp
// BEFORE:
// ComponentInspector payload
[FieldOffset(24)] public int InspTargetIndex;
[FieldOffset(28)] public ushort InspTargetGen;
[FieldOffset(30)] public ScreenAnchor InspAnchor;
// byte 31 is unused padding
[FieldOffset(32)] public int InspComponentTypeId;
[FieldOffset(36)] public float InspOffsetX;
[FieldOffset(40)] public float InspOffsetY;
[FieldOffset(44)] public byte InspIsReadOnly;

// AFTER:
// ComponentInspector payload
[FieldOffset(24)] public long InspNetworkId;    // stable network-level entity ID (not ECS slot)
[FieldOffset(32)] public uint InspSchemaHash;   // FNV-1a hash of the component type name
[FieldOffset(36)] public ScreenAnchor InspAnchor;
[FieldOffset(37)] public byte InspIsReadOnly;
// bytes 38-39 unused padding
[FieldOffset(40)] public float InspOffsetX;
[FieldOffset(44)] public float InspOffsetY;
```

`long InspNetworkId` occupies offsets 24–31 (8 bytes).
`uint InspSchemaHash` occupies offsets 32–35 (4 bytes).
Total payload bytes used: 24 bytes (offsets 24–47), leaving bytes 48–63 free. Size invariant holds.

**Step 2 — Define `InspSchemaHash` derivation:**
The hash is computed as `GizmoSettingsRegistry.ComputeHash(typeof(T).FullName!)` (the same FNV-1a
function already used for settings keys). This means a remote viewer can look up the schema for
a component by resolving the hash from the `EntityAttributeSchema` topic (TASK-GZ052) without
needing a shared C# type reference.

**Step 3 — Update `IDebugDrawBuilder.DrawComponentInspector<T>`:**
```csharp
// BEFORE:
void DrawComponentInspector<T>(Entity target, ScreenAnchor anchor, Vector2 offset,
                               bool isReadOnly = false) where T : unmanaged;

// AFTER: signature unchanged; implementation changes internally.
// The implementation now resolves 'target' to its NetworkId via a new ISimulationView extension:
//   long networkId = view.GetEntityNetworkId(target);
// and computes the schema hash:
//   uint schemaHash = GizmoSettingsRegistry.ComputeHash(typeof(T).FullName!);
// Both values are stored in the primitive instead of ECS-local indices.
```

Add `long GetEntityNetworkId(Entity entity)` to the `ISimulationView` interface (or as an
extension method on the existing adapter). The implementation in `HrotSimulationViewAdapter`
resolves via the `EntityMap` that already maps DDS entity IDs to ECS entities.

**Constraints:**
- `ScreenAnchor` is a 1-byte enum. Its new position at offset 36 (previously offset 30) is a
  breaking change to the binary layout — existing unit tests asserting field offsets will need
  updating.
- Any callsite that writes `InspTargetIndex` or `InspComponentTypeId` directly (bypassing the
  builder) must be updated. These callsites are build errors after the field rename.
- Do NOT rename `DrawComponentInspector` — it is part of `IDebugDrawBuilder`.

**Success conditions:**
- SC-GZ051-1: `DebugPrimitive` compiled with `Shape = ComponentInspector` and `InspNetworkId = 12345L`
  round-trips through DDS serialization with `InspNetworkId == 12345L` on the receiver side.
- SC-GZ051-2: `InspComponentTypeId` and `InspTargetIndex` fields no longer exist on `DebugPrimitive`
  (compile-time check: code that references these fields does not compile after the change — all
  callsites must be updated).
- SC-GZ051-3: `InspSchemaHash` for `typeof(SimTransform)` equals
  `GizmoSettingsRegistry.ComputeHash(typeof(SimTransform).FullName!)` (FNV-1a consistency).
- SC-GZ051-4: `Marshal.SizeOf<DebugPrimitive>() == 64` after the field relayout (size invariant).
- SC-GZ051-5: A remote viewer that receives a `ComponentInspector` primitive with `InspNetworkId`
  and `InspSchemaHash` can reconstruct the display label `"Entity:{networkId} Schema:{schemaHash:X8}"`
  without any ECS dependency (verified by a unit test with a mock DDS reader).
- SC-GZ051-6: `GizmosPrimitiveTests` tests that previously asserted on `InspTargetIndex` offset
  are updated to assert on `InspNetworkId` at offset 24 and `InspSchemaHash` at offset 32.

---

### TASK-GZ052 — Entity Attribute Schema Broadcast via `EntityAttributeSchema` DDS Topic

**Design reference:** feedback2.md "Entity Attribute Schema Broadcast", DESIGN.md §6.5 (new
section to be added)

**Scope:**
The ExCon UI currently relies on a hardcoded DTO to know what fields `JsonAttributeCompiler`
supports. This couples the ExCon build to the SimHost attribute processor and prevents runtime
extension (e.g. modding, plugin processors). This task introduces a `TransientLocal` DDS topic
on which SimHost publishes its full attribute schema as a JSON document on startup. The ExCon UI
subscribes and builds its attribute-editing UI dynamically from the received schema.

The `isDefaultProcessor` gate prevents a broadcast storm: in a multi-node SimHost cluster, only
the node elected as the default processor publishes the schema. Other nodes stay silent.

**Files to create:**
- `Hrot/Network/Hrot.Network.NED/Attributes/EntityAttributeSchemaPublisherSystem.cs`

**Files to modify:**
- `Hrot/Network/Hrot.Network.NED/GenericMessages.cs` (add `EntityAttributeSchema` DDS topic)
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` (register the new system)

**`EntityAttributeSchema` DDS topic:**
```csharp
// One record per SimHost instance. Keyed by NodeId.
// Carries the full JSON schema of entity attributes supported by this node's JsonAttributeCompiler.
// TransientLocal: late-joining ExCon subscribers immediately receive the latest published schema.
[DdsTopic("EntityAttributeSchema")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
[DdsManaged]
public partial struct EntityAttributeSchema
{
    [DdsKey]
    public int NodeId;

    // JSON Schema document describing all attribute paths, types, and validation rules
    // known to this node's JsonAttributeCompiler instance.
    // Compatible with JSON Schema Draft-07 subset (same as StructEdit's EditDocument format).
    [DdsManaged] public string SchemaJson;
}
```

**`EntityAttributeSchemaPublisherSystem`:**
```csharp
[UpdateInPhase(SystemPhase.PreSimulation)]
public sealed class EntityAttributeSchemaPublisherSystem : IEcsModuleSystem
{
    private readonly int                          _nodeId;
    private readonly JsonAttributeCompiler?       _compiler;
    private readonly IDdsWriter<EntityAttributeSchema>? _writer;
    private readonly bool                         _isDefaultProcessor;
    private bool                                  _published;

    public EntityAttributeSchemaPublisherSystem(
        int nodeId,
        JsonAttributeCompiler? compiler,
        IDdsWriter<EntityAttributeSchema>? writer,
        bool isDefaultProcessor)
    {
        _nodeId             = nodeId;
        _compiler           = compiler;
        _writer             = writer;
        _isDefaultProcessor = isDefaultProcessor;
    }

    public void Execute(ISimulationView view)
    {
        // Only the default processor publishes; prevents broadcast storm in multi-node clusters.
        if (!_isDefaultProcessor || _published || _compiler == null || _writer == null)
            return;

        string schemaJson = _compiler.ExportSchema();
        _writer.Write(new EntityAttributeSchema { NodeId = _nodeId, SchemaJson = schemaJson });
        _published = true;
    }
}
```

**`JsonAttributeCompiler.ExportSchema()`:**
Add this method to the existing `JsonAttributeCompiler` class. It iterates the registered
attribute processors and emits a JSON Schema document describing all supported attribute paths,
their types, and validation constraints. The document format is compatible with StructEdit's
`EditDocument` so the ExCon can render it via `ImGuiPropertyTree` without custom code:
```csharp
public string ExportSchema()
{
    // Build JSON Schema from registered processors.
    // Each processor contributes one top-level property key.
    // See existing EditDocumentJsonSerializer for the schema grammar.
    ...
}
```
The implementation detail of `ExportSchema()` is in scope for this task. If `JsonAttributeCompiler`
does not already expose its processor list, add an `IReadOnlyList<string> RegisteredPaths { get; }`
property that the schema exporter enumerates.

**Wiring in SimHostApp:**
```csharp
// Register with isDefaultProcessor matching the node's cluster role.
_kernel.AddSystem(new EntityAttributeSchemaPublisherSystem(
    nodeId:             _nodeId,
    compiler:           _jsonAttributeCompiler,
    writer:             _networkAdapter?.EntityAttributeSchemaWriter,
    isDefaultProcessor: _isDefaultProcessor));
```

**Constraints:**
- `_isDefaultProcessor` is already determined by the SimHost cluster topology (same flag used by
  `CreateEntityRequestSystem`). Reuse the same value.
- Publishing happens once at startup. Subsequent calls are no-ops gated by `_published`.
- If the ExCon cannot parse the schema (e.g. version mismatch), it falls back to its hardcoded
  DTO and logs a warning. The fallback behavior is OUT OF SCOPE for this task.
- `TransientLocal` with `HistoryDepth = 1` ensures late-joining ExCon clients always receive the
  current schema without needing the publisher to be online.

**Success conditions:**
- SC-GZ052-1: `EntityAttributeSchema` struct has `[DdsTopic("EntityAttributeSchema")]`, `[DdsKey]
  int NodeId`, and `[DdsManaged] string SchemaJson` (compile-time structure check).
- SC-GZ052-2: `EntityAttributeSchemaPublisherSystem.Execute` writes to the DDS writer exactly
  once even when called 10 times consecutively (`_published` gate).
- SC-GZ052-3: With `isDefaultProcessor = false`, `Execute` never writes to the DDS writer.
- SC-GZ052-4: `JsonAttributeCompiler.ExportSchema()` returns valid JSON that parses without
  exception via `JsonDocument.Parse(schemaJson)`.
- SC-GZ052-5: The exported schema contains at least one property entry for every path registered
  in `AttributeCompilerFactory.Build(null)` (verified by parsing the JSON and checking keys).
- SC-GZ052-6: `EntityAttributeSchema` uses `DdsDurability.TransientLocal` (reflection check on
  `DdsQosAttribute` applied to the struct).

---

## Phase 19: Library Segregation — Extract GizmoMap to `ExtDeps`

**Background:** The gizmo and debug-visualization framework is currently embedded inside FDP
engine namespaces (`Fdp.Toolkit.Diagnostics.Gizmos`, `Fdp.Toolkit.Vis2D.Gizmos`). External tools
(scenario editors, standalone map viewers, CI validators) that need only the visualization
contracts are forced to take a dependency on the entire FDP engine. The final architectural goal
is to extract the framework into a self-contained external dependency at `ExtDeps/GizmoMap` with
strict internal assembly boundaries and a unified example application that exercises both local
and DDS transport modes.

This phase is **intentionally last** — all prior phases (GZ001–GZ052) must be complete before
library segregation begins, because the API surfaces, payload layouts, and DDS topic contracts
must be stable before the public API is frozen.

---

### TASK-GZ053 — Create `GizmoMap.Contracts` Assembly

**Design reference:** feedback2.md "Extract GizmoMap as External Dependency", DESIGN.md §7 (new)

**Scope:**
Create `ExtDeps/GizmoMap/GizmoMap.Contracts/` as a new C# class library. Migrate all
protocol-level types from `Fdp.Diagnostics.Contracts` and the gizmo contracts from `Fdp.Toolkits`
into this new assembly. `GizmoMap.Contracts` must have ZERO dependencies outside of `netstandard2.1`
/ .NET 8 BCL — it must NOT reference any FDP assembly.

**Assembly boundary rule — hard constraint:**
`GizmoMap.Contracts` must contain ONLY stream-level, protocol-neutral, presentation-neutral
types. ECS concepts (`Entity`, `ISimulationView`, `BitMask256`, `ComponentTypeRegistry`,
`Type[] RequiredComponents`, `SelectionState`, `SimTransform`) must NEVER enter this assembly.
FDP/HROT gizmo interfaces that depend on ECS concepts (`IStatefulGizmo`, `IGizmoDefinition`,
`IStatelessGizmo`, `IGizmoVisibilityPolicy`, `GizmoRegistry`, `StatelessGizmoRegistry`,
`DataDrivenGizmoSystem`, `BehaviorGizmoManagerSystem`, `[GizmoProjector]`) stay in FDP/HROT only.

**Assembly contents (migrated from):**
| Type | Origin | Notes |
|------|--------|-------|
| `Rgba32` | `Fdp.Diagnostics.Contracts` | |
| `DebugPrimitive` (64-byte tagged union) | `Fdp.Diagnostics.Contracts` | |
| `DebugPrimitiveShape`, `PipelineTarget`, `CoordinateSpace`, `SizeMode` | `Fdp.Diagnostics.Contracts` | |
| `DebugPrimitiveBuffer`, `IDebugDrawBuilder` | `Fdp.Diagnostics.Contracts` | |
| `StringInternMap` | `Fdp.Diagnostics.Contracts` | |
| `GizmoPickToken` (NEW — stable IDs) | new in `GizmoMap.Contracts` | Replaces ECS-based `PickToken`; see below |
| `GizmoSettingValue`, `GizmoSettingsRegistry`, `SettingScope` | `Fdp.Toolkits` | Settings values only; no ECS types |
| `GizmoInteractionStartedEvent`, `GizmoDragUpdateEvent`, `GizmoInteractionCommitEvent`, `GizmoInteractionCancelEvent` | `Fdp.Toolkits` | DTOs only; no ECS types |
| `IGizmoSource` (NEW — generic source interface) | new in `GizmoMap.Contracts` | See below |

**Types explicitly NOT migrated to `GizmoMap.Contracts`** (remain in FDP/HROT):
- `IStatefulGizmo` — depends on `Entity` and `ISimulationView`
- `IGizmoDefinition` with `Type[] RequiredComponents` — depends on `ComponentTypeRegistry`
- `IStatelessGizmo` — depends on `ISimulationView`
- `IGizmoVisibilityPolicy` — depends on `SelectionState` from `Hrot.IG.Components`
- `GizmoRegistry`, `StatelessGizmoRegistry` — depend on `BitMask256`
- `DataDrivenGizmoSystem`, `StatelessGizmoSystem`, `BehaviorGizmoManagerSystem` — ECS systems
- `[GizmoProjector]` attribute — ties to ECS component type scanning

**`GizmoPickToken` (stable, DDS-safe):**
Replace the existing ECS-based `PickToken` with a network-stable variant:
```csharp
public struct GizmoPickToken
{
    public long  AnchorId;      // NetworkId / semantic object id (0 = invalid)
    public uint  SubElementId;  // gizmo sub-element index within the anchored entity
    public uint  StreamId;      // publisher stream discriminator (for multi-SimHost clusters)
    public bool  IsValid => AnchorId != 0;
}
```
No local ECS handle. The existing `PickToken` (ECS-based) is kept in `Fdp.Toolkits` for the
FDP adapter layer.

**`IGizmoSource` (generic, ECS-free):**
An optional generic source interface for non-ECS gizmo producers (standalone tools, test harnesses):
```csharp
public interface IGizmoSource
{
    // Called once per frame; emit primitives into 'draw'.
    void Emit(float deltaTime, IDebugDrawBuilder draw);
}
```
FDP-specific producers (`IStatefulGizmo`, `IStatelessGizmo`) are NOT sub-interfaces of this
— they live in FDP only and are unrelated to `IGizmoSource`.

**Assembly boundary rules:**
- `GizmoMap.Contracts` MUST NOT reference `Fdp.Core`, `Fdp.ModuleHost`, `Hrot.*`, or any other
  FDP/HROT assembly.
- `GizmoMap.Contracts` replaces `Fdp.Diagnostics.Contracts` as the canonical home for primitive
  types. `Fdp.Diagnostics.Contracts` becomes a thin facade that re-exports via `global using`
  aliases for backward compatibility during the migration period.

**File to create:**
- `ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj`
- `ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/` (all migrated primitive types)
- `ExtDeps/GizmoMap/GizmoMap.Contracts/Sources/` (`IGizmoSource`, `GizmoPickToken`)
- `ExtDeps/GizmoMap/GizmoMap.Contracts/Settings/` (settings registry)
- `ExtDeps/GizmoMap/GizmoMap.Contracts/Events/` (interaction event DTOs)

**Success conditions:**
- SC-GZ053-1: `GizmoMap.Contracts.csproj` has no `<ProjectReference>` to any FDP or HROT
  project. `dotnet build ExtDeps/GizmoMap/GizmoMap.Contracts/GizmoMap.Contracts.csproj` succeeds
  standalone (without the FDP solution loaded).
- SC-GZ053-2: `Marshal.SizeOf<DebugPrimitive>() == 64` from a unit test in a project that
  references ONLY `GizmoMap.Contracts` (no FDP assemblies).
- SC-GZ053-3: `Fdp.Diagnostics.Contracts` still builds and re-exports all types via type aliases
  or forwarding stubs (backward compat check: `dotnet build FDP/FDP.sln` passes).
- SC-GZ053-4: `GizmoMap.Contracts` targets `net8.0` and `netstandard2.1` dual-targeting so it
  can be consumed by both the FDP engine and hypothetical external tooling.
- SC-GZ053-5: `GizmoMap.Contracts` does NOT contain `IStatefulGizmo`, `IGizmoDefinition`,
  `GizmoRegistry`, or `StatelessGizmoRegistry` (compile-time check: types do not exist in the
  assembly at all; any code referencing them must reference `Fdp.Toolkits` instead).

---

### TASK-GZ054 — Create `GizmoMap.Network` Assembly

**Design reference:** feedback2.md "Extract GizmoMap as External Dependency", DESIGN.md §7

**Scope:**
Create `ExtDeps/GizmoMap/GizmoMap.Network/` containing all DDS schema types and transport
systems. This assembly references ONLY `GizmoMap.Contracts` and the CycloneDDS binding — no FDP
or HROT types.

**Assembly contents (migrated from `Fdp.Diagnostics.Network` and `Hrot.Network.NED`):**
| Type | Origin |
|------|--------|
| `DebugPrimitivesBatch` DDS topic | `Fdp.Diagnostics.Network` |
| `GizmoInteractionBatch` DDS topic | `Fdp.Diagnostics.Network` |
| `GizmoUiState` DDS topic | `Fdp.Diagnostics.Network` |
| `StringInternBatch` DDS topic | `Fdp.Diagnostics.Network` |
| `EntityAttributeSchema` DDS topic | `Hrot.Network.NED` (added by GZ052) |
| `GizmoInteractionEgressSystem` | `Hrot.Network.NED` |
| `GizmoInteractionIngressSystem` | `Hrot.Network.NED` |
| `DebugPrimitivesIngressTranslator` | `Hrot.Network.NED` |
**Assembly boundary rule — hard constraint:**
`GizmoMap.Network` must contain ONLY DDS topic struct definitions and thin stateless transport
adapter classes. ECS systems (`IEcsModuleSystem` implementations such as
`GizmoInteractionEgressSystem`, `GizmoInteractionIngressSystem`,
`DebugPrimitivesIngressTranslator`, `EntityAttributeSchemaPublisherSystem`) must NOT be moved
here — they remain in FDP/HROT where they belong, wrapping the transport adapters from this
assembly.

**Assembly contents (migrated from `Fdp.Diagnostics.Network` and `Hrot.Network.NED`):**
| Type | Origin | Notes |
|------|--------|-------|
| `DebugPrimitivesBatch` DDS topic | `Fdp.Diagnostics.Network` | |
| `GizmoInteractionBatch` DDS topic | `Fdp.Diagnostics.Network` | |
| `GizmoUiState` DDS topic | `Fdp.Diagnostics.Network` | |
| `StringInternBatch` DDS topic | `Fdp.Diagnostics.Network` | |
| `EntityAttributeSchema` DDS topic | `Hrot.Network.NED` (added by GZ052) | |
| `DdsDebugPrimitivePublisher` (NEW) | new in `GizmoMap.Network` | Wraps `IDdsWriter<DebugPrimitivesBatch>`; no ECS |
| `DdsDebugPrimitiveSubscriber` (NEW) | new in `GizmoMap.Network` | Wraps `IDdsReader<DebugPrimitivesBatch>`, populates `DebugPrimitiveBuffer` |
| `DdsGizmoInteractionPublisher` (NEW) | new in `GizmoMap.Network` | Wraps `IDdsWriter<GizmoInteractionBatch>`; no ECS |
| `DdsGizmoInteractionSubscriber` (NEW) | new in `GizmoMap.Network` | Wraps `IDdsReader<GizmoInteractionBatch>`, emits interaction DTOs |

**Types explicitly NOT migrated to `GizmoMap.Network`** (remain in FDP/HROT):
- `GizmoInteractionEgressSystem` — implements `IEcsModuleSystem` (FDP ECS wrapper)
- `GizmoInteractionIngressSystem` — implements `IEcsModuleSystem` (FDP ECS wrapper)
- `DebugPrimitivesIngressTranslator` — called from Raylib render loop, tied to IG lifecycle
- `EntityAttributeSchemaPublisherSystem` — implements `IEcsModuleSystem` with `isDefaultProcessor`

The FDP ECS wrappers delegate to the transport adapters in this assembly. This is the correct
layering: `GizmoMap.Network` is the reusable transport layer; FDP/HROT adds ECS wiring on top.

**Assembly boundary rules:**
- `GizmoMap.Network` references: `GizmoMap.Contracts`, CycloneDDS bindings.
- `GizmoMap.Network` MUST NOT reference `Fdp.Core`, `Fdp.ModuleHost`, or any HROT assembly.

**Success conditions:**
- SC-GZ054-1: `GizmoMap.Network.csproj` references only `GizmoMap.Contracts` and CycloneDDS.
  `dotnet build ExtDeps/GizmoMap/GizmoMap.Network/GizmoMap.Network.csproj` succeeds standalone.
- SC-GZ054-2: `Fdp.Diagnostics.Network` still builds and re-exports DDS topic types via aliases
  (backward compat — `dotnet build FDP/FDP.sln` passes).
- SC-GZ054-3: `DebugPrimitivesBatch` struct definition in `GizmoMap.Network` has identical field
  layout to the original in `Fdp.Diagnostics.Network` (binary compat test via byte-for-byte
  struct comparison).
- SC-GZ054-4: `GizmoMap.Network` does NOT contain any class that implements `IEcsModuleSystem`
  (compile-time check: no `IEcsModuleSystem` reference in the assembly's dependency closure).

---

### TASK-GZ055 — Create `GizmoMap.Presentation` Assembly

**Design reference:** feedback2.md "Extract GizmoMap as External Dependency", DESIGN.md §7

**Scope:**
Create `ExtDeps/GizmoMap/GizmoMap.Presentation/` containing the rendering and UI layer.
This assembly references `GizmoMap.Contracts`, `GizmoMap.Network` (for DDS transport abstraction),
and rendering infrastructure (Raylib, ImGui), but no FDP or HROT simulation types.

Producer-side ECS orchestration systems (`DataDrivenGizmoSystem`, `StatelessGizmoSystem`,
`GizmoSettingsPublisherSystem`) are NOT part of this assembly — they depend on `ISimulationView`,
`Entity`, and `BitMask256` (all FDP/ECS concepts) and remain in `Fdp.Toolkits`. Only the
rendering/UI layer belongs here.

**Assembly contents (migrated from `Fdp.Presentation` and selected types from `Fdp.Toolkits`):**
| Type | Origin | Notes |
|------|--------|-------|
| `DebugPrimitiveRenderer2D` | `Fdp.Presentation` | Add `SemanticShape`, `MilStd2525`, `SpatialAnchor` render paths |
| `GizmoInteractionProxyTool` | `Fdp.Presentation` | Adapted to use `GizmoPickToken` (stable IDs) |
| `RichTextRenderer` | `Fdp.Presentation` | |
| `DebugGizmoLayer` | `Fdp.Presentation` | |
| `GizmoUndoStack` | `Fdp.Toolkits` | No ECS dependency; contains only `IGizmoUndoRecord` stacks |
| `IconAtlasAdapter` (NEW) | new in `GizmoMap.Presentation` | Resolves `FixedString32 AtlasCoord` to icon UV rects |
| `MilStd2525Renderer` (NEW) | new in `GizmoMap.Presentation` | Renders NATO symbology from `SidcCode` |
| `SemanticShapeRenderer` (NEW) | new in `GizmoMap.Presentation` | Resolves `SemProfileId` to an `EntityShapeProfile` and renders it |
| `ImGuiPropertyTreeAdapter` (NEW) | new in `GizmoMap.Presentation` | Renders `EditDocument` JSON via `ImGuiPropertyTree`; no FDP types |

**Types explicitly NOT migrated to `GizmoMap.Presentation`** (remain in FDP/HROT):
- `DataDrivenGizmoSystem` — depends on `ISimulationView`, `Entity`, `BitMask256`
- `StatelessGizmoSystem` — depends on `ISimulationView`
- `GizmoSettingsPublisherSystem` — depends on `GizmoSettingsRegistry` with ECS-aware registries

**Assembly boundary rules:**
- `GizmoMap.Presentation` references: `GizmoMap.Contracts`, `GizmoMap.Network`, Raylib-cs,
  ImGui.NET.
- It MUST NOT reference `Hrot.IG.Components`, `Hrot.Core`, `Fdp.Core`, or any simulation
  domain assembly.
- Types that previously accessed `SimTransform` must now accept `SpatialAnchor` primitives
  (added by GZ050) for decoupled position resolution.

**Success conditions:**
- SC-GZ055-1: `GizmoMap.Presentation.csproj` references no `Hrot.*` or `Fdp.Core` projects.
- SC-GZ055-2: `DebugPrimitiveRenderer2D.RenderPrimitives` processes `SpatialAnchor` primitives
  correctly using the pre-resolved `AnchorWorldX/Y/Z/AnchorYawRad` fields (no ECS lookup).
- SC-GZ055-3: `DebugPrimitiveRenderer2D.RenderPrimitives` processes `SemanticShape` primitives
  by looking up `SemProfileId` in an injected `IEntityShapeProfileRegistry` — no ECS access.
- SC-GZ055-4: `Fdp.Presentation` retains its existing public API by re-exporting from
  `GizmoMap.Presentation` via type aliases (backward compat).
- SC-GZ055-5: `GizmoMap.Presentation` does NOT contain `DataDrivenGizmoSystem`,
  `StatelessGizmoSystem`, or `GizmoSettingsPublisherSystem` (compile-time check).

---

### TASK-GZ056 — Unified Example Application with `--mode local` / `--mode dds`

**Design reference:** feedback2.md "Extract GizmoMap as External Dependency", DESIGN.md §7

**Scope:**
Create `ExtDeps/GizmoMap/GizmoMap.Example/` — a self-contained console/windowed application
that demonstrates the full GizmoMap stack. The application supports two modes selected at startup
via a command-line argument:

- `--mode local`: All gizmo systems run in-process. The primitive buffer flows directly to the
  Raylib renderer. No DDS transport is created. Suitable for unit testing and CI validation.
- `--mode dds`: A SimHost-like producer publishes `DebugPrimitivesBatch` and `GizmoInteractionBatch`
  via CycloneDDS. An IG-like consumer subscribes and renders. Suitable for verifying the full
  remote-viewer scenario.

**Composition root design:**

The mode switch is implemented by selecting between two `IGizmoTransport` implementations:
```csharp
public interface IGizmoTransport : IDisposable
{
    // Producer side: submit a batch for transport.
    void PublishPrimitives(ReadOnlySpan<DebugPrimitive> primitives);
    // Consumer side: drain received primitives into a local buffer.
    void PollAndApply(DebugPrimitiveBuffer target);
}

// Local mode: in-process direct buffer copy
class LocalGizmoTransport : IGizmoTransport { ... }

// DDS mode: CycloneDDS publish/subscribe
class DdsGizmoTransport : IGizmoTransport { ... }
```

The main loop is transport-agnostic. Only the composition root (Program.cs) reads the `--mode`
flag and constructs the appropriate transport.



To rigorously verify the Presentation plane and the transport boundaries without requiring the heavy FDP simulation engine, the `GizmoMap.Example` generator must act as a mock state machine. It will run a purely mathematical `while(true)` loop, updating mock variables (e.g., time, sine waves for movement) and emitting a perfectly deterministic sequence of 64-byte `DebugPrimitive` instructions into the `IDebugDrawBuilder`.

Here is the blueprint for the generator's mock scenario and the renderer's visual smoke test, ensuring every primitive shape, coordinate space, and rendering rule is structurally verified.

### The Generator's Instruction Stream (Frame Layout)

Every frame, the headless generator constructs the following scene directly into the primitive buffer:

**1. The Decoupled Entity (Tests SpatialAnchor, SemanticShape, EntityLocal, ComponentInspector)**
*   **SpatialAnchor:** Emitted for `NetworkId = 100`, carrying an absolute World XYZ that oscillates in a circular path over time, with `YawRad` matching the tangent of the circle.
*   **SemanticShape:** Emitted in `CoordinateSpace.EntityLocal` for `NetworkId = 100`. It carries a valid DIS `ProfileId` (e.g., an APC). Every 2 seconds, the generator toggles the `Damaged` bit in its `ConditionMask` to verify that the dumb terminal correctly evaluates conditional polylines (like damage crosses) without ECS state.
*   **Sphere (Sensor Ring):** Emitted in `CoordinateSpace.EntityLocal` around `NetworkId = 100`. Configured with `SizeMode.WorldMeters`. This proves that geometric scaling respects the map camera's zoom matrix naturally.
*   **ComponentInspector:** Emitted for `NetworkId = 100` with a mocked `SchemaHash`. This instructs the terminal to pin an ImGui property tree to the moving entity on the glass.

**2. NATO Symbology & Interactivity (Tests MilStd2525, PickToken)**
*   **MilStd2525:** A hostile infantry symbol (`SidcCode = "SHGPE----------"`) emitted in `CoordinateSpace.World` at a static location.
*   **EntityBadge (Rich Text):** Attached to the hostile symbol, testing the inline color control bytes. Payload: `"\x01Hostile\x04 - \x02Target"` (Red "Hostile", White " - ", Green "Target").
*   **Interactive Box2D:** A rotated bounding box drawn around the hostile symbol with a non-zero `PickToken` (e.g., `Target.Index = 200, SubElementId = 1`). This proves hit-testing and egress pipeline wiring.

**3. Geometric Connections & Size Modes (Tests Gradient Line, Arrow, ScreenPixels)**
*   **Gradient Line:** A line drawn from the moving `SpatialAnchor` (NetworkId 100) to the static NATO symbol. It uses `Color = Rgba32.Yellow` and `EndColor = Rgba32.Red`. The renderer must synthesize a colored quad to interpolate the alpha/RGB across the vertices.
*   **Arrow (Velocity Vector):** Emitted in `CoordinateSpace.EntityLocal` to show the tank's heading. Configured with `SizeMode.ScreenPixels`. This mathematically defeats the camera zoom, ensuring the arrow remains a constant pixel length regardless of how far the operator zooms out.

**4. UI, Z-Indexing & String Interning (Tests Screen Space, LOD, DrawTextLong)**
*   **HUD Icon:** An `Icon` primitive emitted in `CoordinateSpace.Screen`. Fixed at `(50, 50)`, carrying an `AtlasCoord` like `"b12"`. It proves the camera projection matrix is correctly bypassed for glass-glued UI.
*   **Long Text Diagnostic:** Drawn in `CoordinateSpace.Screen`. The generator calls `DrawTextLong` with a 200-character mock system diagnostic string. This tests the `StringHash` L1-cache escape hatch: the generator hashes the string, stores it in the `StringInternMap`, and the terminal must resolve the full text rather than rendering the 31-character inline fallback.
*   **Z-Index Painter's Test:** Two overlapping `Box2D` primitives in `CoordinateSpace.World` on the same `DebugLayer`. Box A has `ZIndex = 0` (gray), Box B has `ZIndex = 1` (white). Tests the stable sort algorithm in the renderer.
*   **LOD Culling Text:** A `Text` primitive with `MinZoomLod = 4` (Zoom 1.0) and `MaxZoomLod = 12` (Zoom 3.0).

### The Presentation Layer Implementation

The `GizmoMap.Example.Terminal` application wires `DebugPrimitiveRenderer2D` and an ImGui context. It executes the visual smoke test strictly through its two-pass algorithm:

1.  **Pass 1 (Anchor Caching):** It sweeps the received `ReadOnlySpan<DebugPrimitive>` to extract all `SpatialAnchor` structs into a transient frame dictionary.
2.  **Pass 2 (Rendering):** It iterates the sorted span. When it encounters the `SemanticShape` and `Sphere` primitives in `EntityLocal` space, it multiplies their offsets against the cached anchor matrix, never requesting a `SimTransform` from the host.

### Visual Smoke Test Verification Checklist

When you run the unified app with `--mode dds` (or `--mode local`), you perform the following visual validations:

1.  **Dependency Inversion Check:** The APC (`SemanticShape`) moves in a circle. The terminal is successfully interpolating its geometry purely from the `SpatialAnchor` instruction.
2.  **Bandwidth / Semantic Check:** The APC flashes a damage cross every 2 seconds. The frontend is evaluating the `ConditionMask` locally against its polyline definitions; the backend is not resending vertices.
3.  **Orthogonal Scaling Check:** Zooming the camera in and out causes the sensor `Sphere` (`WorldMeters`) to scale correctly with the terrain, while the velocity `Arrow` (`ScreenPixels`) remains a constant screen length.
4.  **Glass UI Check:** Panning the camera leaves the HUD `Icon` and `DrawTextLong` diagnostics perfectly locked to the screen corners (`CoordinateSpace.Screen`).
5.  **Z-Index & String Resolution:** Overlapping boxes render with white strictly over gray. The long diagnostic text renders the full 200 characters, proving the `StringInternBatch` side-channel successfully bypassed the 31-character struct limit.
6.  **Interaction Egress:** Clicking the `Box2D` bounding the NATO symbol must instantly print a log line proving the `GizmoInteractionBatch` was published back over the DDS loopback interface.



**Success conditions:**
- SC-GZ056-1: `dotnet run --project ExtDeps/GizmoMap/GizmoMap.Example -- --mode local` runs to
  completion (draws at least one frame) without error.
- SC-GZ056-2: `dotnet run --project ExtDeps/GizmoMap/GizmoMap.Example -- --mode dds` starts and
  publishes at least one `DebugPrimitivesBatch` DDS sample (verified by log output).
- SC-GZ056-3: The example application references ONLY `GizmoMap.Contracts`, `GizmoMap.Network`,
  `GizmoMap.Presentation` — no FDP or HROT assemblies.

To guarantee the unified example application rigorously proves the dependency inversion and data-plane correctness, the success conditions for `TASK-GZ056` must verify both the structural build boundaries and the runtime semantic payloads. 

Here are the formal success conditions to define for this task, grouped by their architectural concerns:

**Execution and Boundary Constraints**
*   **SC-GZ056-1:** `dotnet run --project ExtDeps/GizmoMap/GizmoMap.Example -- --mode local` runs to completion (draws at least one frame) without error.
*   **SC-GZ056-2:** `dotnet run --project ExtDeps/GizmoMap/GizmoMap.Example -- --mode dds` starts and publishes at least one `DebugPrimitivesBatch` DDS sample, verified by log output.
*   **SC-GZ056-3:** The example application references ONLY `GizmoMap.Contracts`, `GizmoMap.Network`, and `GizmoMap.Presentation` — it contains absolutely no FDP or HROT assemblies.
*   **SC-GZ056-4:** The `IGizmoTransport` interface is defined in `GizmoMap.Contracts` (not in the example application itself), making it available for FDP integration without depending on the example.

**Visual and Semantic Payload Verification**
*   **SC-GZ056-5:** The demo emits and renders at least one `SpatialAnchor` primitive — with `AnchorWorldX/Y/Z` populated and `AnchorYawRad` non-zero — and it appears on screen at the correct world position.
*   **SC-GZ056-6:** The demo emits and renders at least one `SemanticShape` primitive — with a non-zero `SemProfileId` corresponding to a registered profile — and the shape renderer draws the profile silhouette at the local `SemAnchorX/Y`.
*   **SC-GZ056-7:** The demo emits and renders at least one `MilStd2525` primitive with a valid SIDC code (e.g., "SFGPUCI--------"), and the `MilStd2525` renderer draws the NATO symbol at the given world position.
*   **SC-GZ056-8:** The demo emits and renders at least one `Icon` primitive using a registered atlas coordinate, and the `IconAtlasAdapter` resolves the UV rect without throwing.
*   **SC-GZ056-9:** The demo emits and renders at least one `EntityBadge` primitive with rich-text content, and the `RichTextRenderer` draws the badge without truncation.
*   **SC-GZ056-10:** The demo emits at least one long-text primitive via `DrawTextLong`, and the full string is transmitted via the `StringInternBatch` side-channel and resolved correctly on the rendering side.

**Interaction and Transport Loopback Verification**
*   **SC-GZ056-11:** At least one emitted primitive is pickable (its pick token is non-zero). In `--mode local`, clicking on it in the Raylib window publishes a `GizmoInteractionStartedEvent` to the local event bus, verified by a log or counter.
*   **SC-GZ056-12:** In `--mode dds`, a simulated click on the pickable primitive causes a `GizmoInteractionBatch` record with `Kind = Started` to be received by the subscriber side, verified by log output confirming the network round-trip.

Based on our preceding discussion regarding the `StructEdit` integration, I recommend appending a 13th condition to explicitly lock in the out-of-band UI correlation:
*   **SC-GZ056-13:** The demo emits at least one `ComponentInspector` primitive acting as a spatial anchor, which successfully cross-references an out-of-band JSON schema delivered via the DDS UI state topic, resulting in an interactive `ImGuiPropertyTree` rendering on the glass.






### Phase 20: Production Map Rendering Migration
**Goal:** Completely replace the legacy hardcoded map layers and entity visualizers in SimHost, CGF, and IG with pure `GizmoMap`-based declarative rendering.


##### TASK-GZ057 — Convert Base Entity Visualizers to Stateless Gizmos
**Scope:** The current architecture relies on `PerspectiveEntityVisualizerBase` and its concrete implementations (`CgfDebugVisualizerAdapter`, `SimHostVehicleVisualizer`, `NedVisualizerAdapter`),,. These adapters execute direct Raylib draw calls mixed with ECS queries. We must convert them into `IStatelessGizmo` projectors that emit `SpatialAnchor` and `SemanticShape` / `MilStd2525` primitives.

**Execution:**
1. Create `CgfEntityPresentationGizmo`, `SimHostEntityPresentationGizmo`, and `IgEntityPresentationGizmo` implementing `IStatelessGizmo`.
2. Decorate them with `[GizmoProjector]` targeting their respective required components (e.g., `SimTransform`, `VisualData`, `EntityInfo`).
3. Inside the `Draw` method, emit a `SpatialAnchor` to establish the coordinate matrix, followed by a `SemanticShape` or `MilStd2525` in `CoordinateSpace.EntityLocal`,. 
4. Evaluate condition masks locally (e.g., `IgHealthState` or `ActorCapabilityState`) and pack the result into the `ConditionMask` payload of the primitive so the `GizmoMap` renderer handles damage/state overlays automatically.

**Success Conditions:**
* `SC-GZ057-1`: The new gizmos compile and register automatically via the Roslyn source generator.
* `SC-GZ057-2`: Entities render identically on the glass using the `GizmoMap` presentation pipeline without using `PerspectiveEntityVisualizerBase`.

--------------------------------------------------------------------------------

##### TASK-GZ058 — Migrate Domain Map Layers to Gizmo Projectors
**Scope:** The current `MapCanvas` composition roots inject multiple hardcoded `IMapLayer` classes such as `RouteRenderLayer`, `MapOverlayRenderLayer`, `MissionRenderLayer`, `ProjectileLayer`, and `EffectRenderLayer`,,,. These violate the "Evaluate Once, Present Anywhere" mandate by executing domain logic directly in the presentation tier.

**Execution:**
1. **Routes & Overlays:** Convert `RouteRenderLayer` and `MapOverlayRenderLayer` into `IStatelessGizmo` implementations. They will read `RoutePlan` and `EditablePolyline` components and emit `Line` primitives or perspective-exaggerated `SemanticShape` polylines.
2. **Missions:** Convert `MissionRenderLayer` into a `MissionPresentationGizmo` that reads `ActiveMissionPlan` and emits `Line` primitives with gradient alpha (using `Color` and `EndColor`) from the entity to its waypoints.
3. **Effects:** Convert `EffectRenderLayer` and `ProjectileLayer` into stateless gizmos emitting `Sphere` and `Line` primitives based on `VisualEffectState` and `TracerTarget`.

**Success Conditions:**
* `SC-GZ058-1`: All tactical graphics, routes, and effects render successfully through the `DebugPrimitiveBuffer` stream.
* `SC-GZ058-2`: Scaling, zooming, and panning behavior correctly respects `SizeMode.WorldMeters` vs `SizeMode.ScreenPixels` natively.

--------------------------------------------------------------------------------

##### TASK-GZ059 — Eradicate Legacy Rendering Infrastructure & Wire Composition Roots
**Scope:** Strip the legacy `IVisualizerAdapter` and domain-specific `IMapLayer` constructs from the codebase, leaving `GizmoMap` as the absolute authority for map rendering.

**Execution:**
1. Delete `PerspectiveEntityVisualizerBase`, `IVisualizerAdapter`, `CgfDebugVisualizerAdapter`, `SimHostVehicleVisualizer`, and `NedVisualizerAdapter`,,,.
2. Delete `EntityRenderLayer`, `MapOverlayRenderLayer`, `RouteRenderLayer`, `MissionRenderLayer`, `EffectRenderLayer`, and `ProjectileLayerFactory`,.
3. Clean up the composition roots (`SimHostVisualization.cs`, `IgApplication.cs`, and `CgfSubsystem.cs`):
   * Remove all `_map.AddLayer(...)` calls referencing the deleted layers,.
   * Ensure the `DebugGizmoLayer` (which wraps `GizmoMap.Presentation.DebugPrimitiveRenderer2D`) is injected as the sole primary rendering layer,.
4. Verify that selection highlighting works purely through the `SelectionState` ECS component driving gizmo emission (or the `StatelessGizmoSystem` filter) rather than via hardcoded render layers,.

**Success Conditions:**
* `SC-GZ059-1`: The solution compiles cleanly without the legacy visualization interfaces.
* `SC-GZ059-2`: Running the cluster (`simhost`, `cgf`, `ig`) results in a fully functional 2D tactical map where 100% of the graphics are driven by the `GizmoMap` primitive stream.




### Phase 21: Tool Rendering Decoupling


**Goal:** Eradicate direct `Raylib_cs` dependencies from all interactive map tools (`MeasureTool`, `EntityRotationTool`, `RouteEditTool`, `EditTool`, etc.). Convert them into "gizmo generators" that emit backend-neutral `DebugPrimitive`s while remaining in their current domain assemblies and retaining their stateful ECS logic.


The correct architectural path is to retain the existing `IMapTool` state machines on the backend where they already live, maintain their access to the ECS repository, and simply swap their rendering backend from direct `Raylib_cs` calls to `IDebugDrawBuilder` primitive emissions.

Since the IG frontend already forwards raw interactions (`MapClickEvent`, `DragEvent`) via DDS to the backend orchestrator, the tools will continue to process these inputs naturally, mutating ECS state and emitting `DebugPrimitive`s that stream back to the dumb terminal.


--------------------------------------------------------------------------------

##### TASK-GZ060 — Decouple Vis2D Abstractions from Raylib
**Scope:** Purge `Raylib_cs` from the core `Fdp.Toolkit.Vis2D.Abstractions` namespace to ensure tools cannot accidentally bypass the gizmo stream.
**Execution:**
1. Modify `RenderContext` in `Fdp.Toolkit.Vis2D.Abstractions`:
   * Remove the `Raylib_cs.Camera2D Camera` field. The backend tools do not need camera matrices because `DebugPrimitive`s define their own `CoordinateSpace` (World/Screen), leaving projection math entirely to the frontend renderer.
   * Add `IDebugDrawBuilder DrawBuilder` to the struct.
2. Update `MapCanvas.Draw()` to inject the `DebugPrimitiveBuffer` into the `RenderContext` before passing it to tools and layers.
**Success Conditions:**
* `SC-GZ060-1`: `RenderContext.cs` and `IMapTool.cs` contain absolutely no `using Raylib_cs;` directives.
* `SC-GZ060-2`: The `Fdp.Toolkit.Vis2D.Abstractions` namespace is free of Raylib types.

--------------------------------------------------------------------------------

##### TASK-GZ061 — Convert Measurement and Placement Tools to Gizmo Generators
**Scope:** Convert the stateless visual overlays of `MeasureTool`, `CreationTool`, and `ObstaclePlacementTool` to emit primitives.
**Execution:**
1. In `MeasureTool.Draw`, replace `Raylib.DrawLineEx` and `Raylib.DrawText` with `ctx.DrawBuilder.DrawLine` and `ctx.DrawBuilder.DrawText`.
2. In `CreationTool.Draw`, replace the ghost `Raylib.DrawCircle` and `Raylib.DrawText` with `ctx.DrawBuilder.DrawSphere` (or `DrawIcon`) and `ctx.DrawBuilder.DrawText`.
3. In `ObstaclePlacementTool.Draw`, replace `Raylib.DrawCircleLines` with an unfilled `DrawSphere` primitive.
4. Remove `using Raylib_cs;` from all these files.
**Success Conditions:**
* `SC-GZ061-1`: `MeasureTool`, `CreationTool`, and `ObstaclePlacementTool` compile with no Raylib dependencies.
* `SC-GZ061-2`: Activating the measure tool emits a `Line` and `Text` primitive into the `DebugPrimitiveBuffer` which correctly streams to the dumb terminal.

--------------------------------------------------------------------------------

##### TASK-GZ062 — Convert EntityRotationTool to Gizmo Generator
**Scope:** Decouple the modal entity rotation tool while preserving its `SimTransform` ECS mutations.
**Execution:**
1. In `EntityRotationTool.Draw`, calculate the line endpoint from `_entity`'s world position and `_currentPoint`.
2. Replace `Raylib.DrawLineEx` with `ctx.DrawBuilder.DrawLine` using `EntityRotationToolConstants.LineColor`.
3. Replace `Raylib.DrawCircleV` with `ctx.DrawBuilder.DrawSphere`.
4. Replace `Raylib.DrawTextEx` with `ctx.DrawBuilder.DrawText`, formatting the heading degrees.
5. Remove `using Raylib_cs;`.
**Success Conditions:**
* `SC-GZ062-1`: Dragging the rotation handle on the remote terminal streams `DragEvent`s to the backend, updating the ECS `SimTransform`, which causes the tool to emit updated `Line` primitives back to the terminal.

--------------------------------------------------------------------------------

##### TASK-GZ063 — Convert Polyline & Route Edit Tools to Gizmo Generators
**Scope:** Decouple the vertex manipulation tools (`EditTool`, `RouteEditTool`) while retaining their ghost-buffer state machines and `RoutePlan`/`EditablePolyline` ECS commit logic.
**Execution:**
1. In `EditTool.Draw` and `RouteEditTool.Draw`, iterate the `_ghostPoints` list.
2. Replace `Raylib.DrawCircleV` (for normal vertices) and `Raylib.DrawRing` (for the selected vertex) with `ctx.DrawBuilder.DrawSphere` primitives. Use `SizeMode.ScreenPixels` if the handle radius should defeat camera zoom, or `SizeMode.WorldMeters` to match the existing fixed-world-size behavior.
3. For the preview segments connecting the vertices, use `ctx.DrawBuilder.DrawLine`.
4. Remove all `Raylib_cs` references.
**Success Conditions:**
* `SC-GZ063-1`: `EditTool.cs` and `RouteEditTool.cs` compile cleanly without Raylib.
* `SC-GZ063-2`: The vertex handles render flawlessly on the dumb terminal and follow the mouse as `DragEvent`s arrive from the remote client.