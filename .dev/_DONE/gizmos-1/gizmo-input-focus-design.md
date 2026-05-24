# Gizmo Input & Focus — Design Document

**Subject:** FDP / HROT GizmoMap — backend-driven interactive tools over a stateless dumb terminal
**Status:** Design proposal, derived from FDP 152 design talk
**Scope:** `GizmoMap.Contracts`, `GizmoMap.Network`, `GizmoMap.Presentation`, `Fdp.Toolkit.Diagnostics.Gizmos`, plus `GizmoMap.Example` reference implementation

---

## 1. Context

GizmoMap is the diagnostic visualisation layer used across all FDP subsystems (IG, SimHost, ExCon, CGF, ClusterRunner). The presentation layer ("dumb terminal") is **the same generic component** running in every viewport — it differs only in which subsystem's primitive stream it is currently bound to (perspective switching by `NodeId` / `PickStreamId`).

Today, the terminal is too smart: it hardcodes that `MouseButton.Left == Commit`, `Escape == Cancel`, and uses `DebugLayer` (a visibility mask) as if it were a Z-order. We need backend-driven interactive tools — for example an *entity rotator* that tracks the mouse and updates entity heading on click, or a *polygon vertex editor* that drags vertices — without leaking any semantic decisions into the terminal.

This document describes the final architecture: a strict immediate-mode pipeline where the terminal blindly reflects hardware events declared by data on the wire, and all interaction semantics, focus, and lifecycle live on the backend in a fully **ECS-agnostic** core.

## 2. Goals & non-goals

### Goals

- **Stateless dumb terminal.** No semantic interpretation of HW events. No domain awareness. No business rules.
- **Backend-driven semantics.** Each tool decides what a click, drag, or keypress means.
- **ECS-agnosticism.** The interaction core works in `GizmoMap.Example` (no ECS) identically to how it works in the FDP backend. ECS becomes a thin adapter, not a foundation.
- **Strong typing at the FSM boundary.** Tools never pattern-match an `actionId` integer; they receive typed events with typed enums.
- **Idiomatic C# lifecycles.** Constructors establish invariants, `IDisposable` tears them down. No two-phase init, no pooling boilerplate.
- **Generic across subsystems.** Same terminal, same primitives, same transport — every subsystem produces its own gizmo stream and the terminal retunes itself for the active perspective.

### Non-goals

- Bandwidth-optimal HW event compression — the existing 64-byte primitive and the existing DDS batch are reused with field repurposing; no struct grows.
- Reproducing every behavior of the legacy `GizmoInteractionProxyTool` — the parts that hardcoded semantic interpretation are explicitly being removed.

## 3. Guiding principles

1. **Unidirectional, immediate-mode data flow.**
   Backend emits primitives → terminal renders & reflects HW → backend FSM consumes events → goto top.
   The terminal reconstructs the UI from scratch every frame from `DebugPrimitivesBatch`. There is no retained state in the terminal that the backend has to "abort" or "reset".

2. **Data declares intent. Code does not.**
   When a backend tool wants raw HW events, it emits a non-visual meta-primitive (`InputCaptureBinding`). When it wants the events to stop, it stops emitting that primitive. No RPC, no handshake, no negotiation.

3. **Two kinds of focus, two mechanisms.**
   - *Spatial focus* (shared) is resolved by hit-testing on the terminal — pure data, no backend coordination.
   - *Logical focus* (exclusive) is arbitrated on the backend by a single registry — the terminal never sees a conflict.

4. **Multiplexing belongs to transport. Demultiplexing happens at ingress.**
   The wire format packs many event kinds into one DDS batch. The ingress translator unpacks them into strongly typed local events before they ever reach a state machine.

5. **The gizmo declares; the host enforces.**
   A gizmo says "I want exclusive focus" via a property. It does not acquire locks, does not emit capture primitives itself, does not know about the registry. The hosting manager does all of that.

## 4. Layered architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                         Backend (per subsystem)                  │
│                                                                  │
│   ┌──────────────────────────────────────────────────────┐       │
│   │      Stateful gizmos (FSMs, ECS-free or ECS-bound)   │       │
│   │      EntityRotatorGizmo, VertexEditGizmo, ...        │       │
│   └────────────▲─────────────────────────────────┬───────┘       │
│                │ typed events                    │ primitives    │
│   ┌────────────┴─────────────────────────────────▼───────┐       │
│   │      GizmoInteractionManager  (ECS-agnostic core)    │       │
│   │  • registry by AnchorId   • exclusive-focus lock     │       │
│   │  • emits InputCaptureBinding on tools' behalf        │       │
│   │  • IGizmoSource.Emit                                 │       │
│   └────────────▲─────────────────────────────────┬───────┘       │
│                │                                 │               │
│   ┌────────────┴────────────┐        ┌───────────▼──────────┐    │
│   │ Ingress translator      │        │ Egress / draw buffer │    │
│   │ DDS batch → typed events│        │ DebugPrimitivesBatch │    │
│   └────────────▲────────────┘        └──────────┬───────────┘    │
└────────────────┼───────────────────────────────────┼─────────────┘
                 │  GizmoInteractionBatch (DDS)      │  primitives
┌────────────────┼───────────────────────────────────▼─────────────┐
│                │      Dumb terminal (presentation, generic)      │
│                │                                                 │
│   ┌────────────┴────────────────────────────────────────────┐    │
│   │ DebugGizmoLayer / equivalent                            │    │
│   │  • render primitives                                    │    │
│   │  • spatial hit-test (reverse iteration)                 │    │
│   │  • reflect raw HW when InputCaptureBinding is present   │    │
│   │  • zero domain knowledge                                │    │
│   └─────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
```

The same terminal runs in IG, ExCon, ClusterRunner viewer, etc. Perspective switching just routes a different `NodeId`'s primitive stream into the same component. Each subsystem produces its own stream; the cluster runner's terminal retunes itself when the operator changes perspective.

## 5. Data contracts

### 5.1 New primitive: `InputCaptureBinding`

A non-visual meta-primitive declaring that the bound token wants raw hardware events streamed to it. Modeled on the existing `ContextMenuBinding` so it fits the 64-byte `DebugPrimitive` footprint without growth.

```csharp
public enum DebugPrimitiveShape : byte
{
    // ... existing shapes
    SpatialAnchor       = 10,
    ContextMenuBinding  = 11,
    InputCaptureBinding = 12, // NEW
}
```

Field re-use within the existing payload union:

| Field            | Meaning when `Shape == InputCaptureBinding`              |
|------------------|----------------------------------------------------------|
| `InspNetworkId`  | `AnchorId` of the target (entity / domain object / tool) |
| `SubElementId`   | Specific handle id (0 for the whole tool)                |
| `ConditionMask`  | `1` = Exclusive, `0` = Shared                            |

Factory:

```csharp
public static DebugPrimitive MakeInputCaptureBinding(
    long networkId, uint subElementId, bool exclusive)
{
    var p = default(DebugPrimitive);
    p.Shape          = DebugPrimitiveShape.InputCaptureBinding;
    p.InspNetworkId  = networkId;
    p.SubElementId   = subElementId;
    p.ConditionMask  = exclusive ? 1u : 0u;
    return p;
}
```

### 5.2 Transport: `GizmoInteractionBatch`

Reused as-is in size. We add one event kind and reinterpret two fields when that kind is set.

```csharp
public enum GizmoInteractionEventKind : byte
{
    Started     = 0,
    DragUpdate  = 1,
    Commit      = 2,
    Cancel      = 3,
    MenuAction  = 4,
    RawInput    = 5, // NEW
}
```

When `Kind == RawInput`:

| Field              | Meaning                                                       |
|--------------------|---------------------------------------------------------------|
| `ActionId`         | `(int)MapMouseButton` or `(int)MapKeyboardKey`                |
| `Space` (byte)     | bit 7: `1` = mouse, `0` = keyboard; bit 0: `1` = pressed, `0` = released |
| `WorldX/Y/Z`       | Pointer position in **world space** (terminal unprojects)     |
| `PickAnchorId` / `PickSubElementId` | route the payload to a specific token        |

**Coordinate space rule.** The backend never sees screen pixels. It has no awareness of `MapCamera` zoom/offset/target. The terminal always unprojects via `Raylib.GetScreenToWorld2D` (or equivalent) before packing. The existing `CoordinateSpace` enum on the batch qualifies the data when needed.

### 5.3 Local typed events (backend bus)

The transport multiplexing exists for bandwidth. The bus does not. Ingress translates the wire batch into strongly typed events:

```csharp
[EventId(8056)]
public struct GizmoMouseEvent
{
    public PickToken Token;
    public MapMouseButton Button;
    public bool IsPressed;       // false = released
    public Vector3 WorldPos;
}

[EventId(8057)]
public struct GizmoKeyEvent
{
    public PickToken Token;
    public MapKeyboardKey Key;
    public bool IsPressed;
}
```

Plus the existing `GizmoDragUpdateEvent`, `GizmoInteractionCommitEvent`, `GizmoInteractionCancelEvent`, `GizmoMenuActionEvent`.

## 6. Focus arbitration

This is the part that took the longest to converge. The final answer is: **two distinct mechanisms, never mixed.**

### 6.1 Spatial focus (shared) — resolved on the terminal

For interactive handles like polygon vertices: each backend gizmo emits standard visual primitives (e.g. `Box2D`) carrying its own `GizmoPickToken` (entity `AnchorId` + handle `SubElementId`). The terminal hit-tests these on click.

**Z-order rule.** Backend gizmos do not assign meaningful `ZIndex` values — they cannot coordinate to know who should be "on top." Instead:

> The terminal iterates the primitive buffer **in reverse** when hit-testing.

In an immediate-mode pipeline, the last primitive submitted is drawn last and therefore appears topmost. Reverse iteration matches that visually: the first primitive that passes the spatial intersection test in reverse order wins. Submission order alone is the deterministic resolution.

**Important:** `DebugPrimitive.DebugLayer` is a **visibility mask**, not a depth sort key. The current code that compares `prim.DebugLayer > best.DebugLayer` to resolve hit-test priority is wrong and is removed.

Multiple gizmos with overlapping handles need no coordination — only the one whose token shows up in the resulting `Started` event reacts.

### 6.2 Logical focus (exclusive) — arbitrated on the backend

For top-level tools that intercept *everything* (Escape, clicks in empty space, etc.), spatial hit-testing cannot help. If two tools simultaneously emitted `InputCaptureBinding(Exclusive=true)`, the terminal would have no honest way to choose.

We prevent that situation entirely on the backend:

```csharp
public sealed class ActiveGlobalGizmo
{
    public IStatefulGizmo? ActiveInstance { get; set; }
}
```

This registry lives **inside the `GizmoInteractionManager`** (see §8), not in the ECS. It is transient, never serialized, never replicated. A managed reference is fine — the manager knows its own tools' references; nobody else cares.

Rule: **only the holder of the registry slot may emit `InputCaptureBinding(Exclusive=true)`.** Because the manager (not the gizmo) emits the primitive, the gizmo doesn't even know the registry exists — it just declares `RequiresExclusiveFocus => true` and the manager handles the rest.

### 6.3 What the terminal does NOT do

- It does **not** decide which `InputCaptureBinding` "wins" if multiple appear. Backend ensures only one exclusive request exists per frame; if a buggy backend sends two, the terminal may pick the last one — that's a backend bug, not terminal logic.
- It does **not** hardcode `Left == Commit`, `Right == Cancel`, `Escape == Cancel`. Those are removed from `GizmoInteractionProxyTool`. The backend FSM evaluates raw events and decides what they mean.

## 7. Backend interfaces

### 7.1 The interaction-handler contract (common to all stateful gizmos)

```csharp
public interface IGizmoInteractionHandler
{
    bool RequiresExclusiveFocus { get; }

    // Spatial / shared interactions (originating from a hit-test on the terminal)
    void OnInteractionStarted(Vector3 worldPos);
    void OnDragUpdate(Vector3 worldPos);
    void OnCommit(Vector3 worldPos);
    void OnCancel();

    // Semantic actions (e.g. context-menu items)
    void OnMenuAction(int actionId);

    // Raw HW events delivered while exclusive capture is held
    void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos);
    void OnKeyEvent(MapKeyboardKey key, bool isPressed);
}
```

**Why these specific shapes:**
- *Specialized methods, not a chameleon `OnInteraction(kind, payload)`.* Forcing the FSM to `switch(kind)` and unpack a multiplexed payload bleeds the transport into the domain.
- *`OnMouseEvent(button, isPressed, worldPos)` not `OnMouseButtonPressed/Released`.* The button is already strongly typed; bundling pressed/released avoids interface bloat.
- *`worldPos`, never `screenPos`.* The backend has no `MapCamera` to project against.
- *`RequiresExclusiveFocus` is a property, not a method call or an event.* The host inspects it once and acts.

### 7.2 Producer & lifecycle interfaces

```csharp
// Pure ECS-free producer. Standalone tools, GizmoMap.Example, test harnesses.
public interface IGizmoSource
{
    void Emit(float deltaTime, IGizmoDrawBuilder draw);
}

// Global stateful gizmo. No entity binding. No ISimulationView in hot path.
public interface IStatefulGizmo : IGizmoInteractionHandler, IDisposable
{
    void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder);
}

// Entity-bound stateful gizmo. Specialised; lives in the FDP adapter layer.
public interface IEntityStatefulGizmo : IGizmoInteractionHandler, IDisposable
{
    void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder);
}

// Stateless variants — pure functions over current state.
public interface IStatelessGizmo
{
    void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder);
}

public interface IEntityStatelessGizmo
{
    void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder);
}
```

**Why no `OnInitialize` / `OnTeardown`:**
Two-phase init creates temporal coupling and breaks invariants — the FSM can be observed half-built. We use the constructor for setup and `IDisposable` for teardown. No factory pooling either: gizmo construction is not a hot path, and Gen0 GC is built for exactly this kind of transient object.

**Why no `ISimulationView` parameter on `UpdateAndDraw`:**
Stateful gizmos already received it in the constructor and cached it. Passing it every frame is parameter bloat. Stateless gizmos do receive it per-call because they have no state to hold it in.

## 8. The ECS-agnostic core: `GizmoInteractionManager`

This is the single owner of the interaction model. It lives in `GizmoMap.Contracts` (or an equivalent core library) with **zero ECS dependency**.

```csharp
public sealed class GizmoInteractionManager : IGizmoSource
{
    private readonly Dictionary<long, IStatefulGizmo> _activeTools = new();
    private IStatefulGizmo? _exclusiveFocusHolder;

    public void AddTool(long anchorId, IStatefulGizmo tool)
    {
        _activeTools[anchorId] = tool;
        if (tool.RequiresExclusiveFocus && _exclusiveFocusHolder == null)
            _exclusiveFocusHolder = tool;
    }

    public void RemoveTool(long anchorId)
    {
        if (_activeTools.Remove(anchorId, out var tool))
        {
            if (_exclusiveFocusHolder == tool) _exclusiveFocusHolder = null;
            tool.Dispose();
        }
    }

    public void DispatchEvent(GizmoPickToken token,
                              GizmoInteractionEventKind kind,
                              Vector3 worldPos,
                              int actionId,
                              byte stateFlags)
    {
        // O(1) lookup by token.AnchorId, then specialised method on the matched tool.
        // No global event filtering inside FSMs.
    }

    public void Emit(float deltaTime, IGizmoDrawBuilder draw)
    {
        foreach (var (anchorId, tool) in _activeTools)
        {
            tool.UpdateAndDraw(deltaTime, draw);

            // The MANAGER emits the capture binding, not the gizmo.
            if (tool == _exclusiveFocusHolder)
                draw.EmitRaw(DebugPrimitive.MakeInputCaptureBinding(
                    networkId: anchorId, subElementId: 0, exclusive: true));
        }
    }
}
```

Responsibilities:
- Own the tool registry, keyed by 64-bit `AnchorId` (network id, semantic id, anything stable — never an ECS handle).
- Own the exclusive-focus lock.
- **Emit `InputCaptureBinding` on the tool's behalf** when the tool declares it needs exclusive capture.
- O(1) push-based dispatch of typed events to the matching tool, so individual FSMs never need `if (evt.Token != mine) continue` filtering.

The manager exposes itself as an `IGizmoSource` so the host loop just calls `manager.Emit(dt, draw)` once per frame.

## 9. ECS adapter (when ECS is the host)

In FDP, `DataDrivenGizmoSystem` shrinks dramatically. It becomes a lifecycle bridge plus an event router:

- On `ConstructionOrder` for an entity matching a gizmo registry rule: resolve the entity's network id, `new MyGizmo(view, entity)`, `manager.AddTool(networkId, gizmo)`.
- On `DestructionOrder`: `manager.RemoveTool(networkId)` (which calls `Dispose`).
- Each frame, read typed events (`GizmoMouseEvent`, `GizmoKeyEvent`, `GizmoDragUpdateEvent`, `GizmoInteractionCommitEvent`, …) from the bus and forward each to `manager.DispatchEvent`.

`GizmoInteractionIngressSystem` translates the DDS batch into typed local events using bit flags from the `Space` byte to discriminate mouse vs. keyboard and pressed vs. released.

`StatelessGizmoSystem` similarly demotes to a thin bridge.

The ECS adapter contains **no** focus logic, **no** capture-binding emission, and **no** event multiplexing.

### 9.1 Tool entities (a convenient ECS pattern, not a requirement)

For transient tools like the rotator, hosting the gizmo on a small dedicated "tool entity" that exists for the duration of the interaction is convenient: when the FSM decides the interaction is over, it issues `DestroyEntity(toolEntity)` and the standard `DestructionOrder` path triggers `RemoveTool` → `Dispose`. No special teardown plumbing.

But this is purely an ECS-host convenience. Non-ECS hosts call `RemoveTool` directly. A gizmo never `cares` whether it lives on a tool entity, on a domain entity, or on no entity at all.

## 10. End-to-end flows

### 10.1 Spatial drag (polygon vertex editor)

```
1. VertexEditGizmo emits, every frame, a Box2D for each vertex with
   AnchorId = polygonId, SubElementId = vertexIndex+1, Color = idle.
2. User clicks. Terminal hit-tests in reverse buffer order, finds the topmost
   Box2D with non-zero SubElementId, builds a GizmoPickToken, sends a
   GizmoInteractionBatch{Kind=Started, Token, WorldXYZ}.
3. Ingress turns it into a GizmoInteractionStartedEvent. Manager dispatches
   to the gizmo registered under polygonId. Gizmo updates its "active vertex"
   field, switches color of that handle to red.
4. While active, gizmo also emits InputCaptureBinding(Shared) for that token.
   (Or, equivalently, the manager emits it on the gizmo's behalf if the
   gizmo declares shared focus — see implementation note below.)
5. User drags. Terminal sends DragUpdate events with new world coords.
   Manager dispatches OnDragUpdate(worldPos). Gizmo updates vertex position.
6. User releases. Terminal sends Commit. Gizmo clears active vertex,
   stops emitting capture binding. Terminal sees absence next frame and
   reverts to ordinary spatial hit-testing. No RPC, no abort.
```

Note: shared capture is the simpler path — the example may keep the gizmo in charge of emitting the shared `InputCaptureBinding` and reserve manager-driven emission for the exclusive case only. The contract works either way.

### 10.2 Exclusive logical capture (entity rotator)

```
1. Operator right-clicks tank → "Rotate". Backend: new EntityRotatorGizmo(view, tank);
   manager.AddTool(rotatorAnchorId, gizmo). RequiresExclusiveFocus = true.
2. Each frame, manager calls gizmo.UpdateAndDraw, which draws a yellow arrow
   from tank center toward the current heading. Manager emits
   InputCaptureBinding(Exclusive=true, AnchorId=rotatorAnchorId).
3. Terminal sees the exclusive binding, suspends spatial hit-testing,
   streams every mouse move and key as RawInput addressed to that token.
4. Mouse moves: ingress publishes GizmoMouseEvent (or DragUpdate) → manager
   → gizmo.OnMouseEvent / OnDragUpdate. Gizmo recomputes yaw from
   atan2(worldPos.Y - target.Y, worldPos.X - target.X), updates internal field.
5. Left mouse RELEASED: gizmo.OnMouseEvent(Left, isPressed=false, _).
   Gizmo writes the new yaw onto the target's SimTransform, then calls
   manager.RemoveTool(rotatorAnchorId) (ECS-hosted: destroys tool entity instead).
6. Manager disposes the gizmo and releases the exclusive lock.
   Next frame the capture binding is gone; terminal resumes normal behavior.
7. Right click or Escape pressed: same teardown, no rotation written.
```

The terminal in step 6/7 had no idea what Left/Right/Escape "meant". It only reflected them. The gizmo decided.

## 11. Worked example: `EntityRotatorGizmo`

```csharp
public class EntityRotatorGizmo : IEntityStatefulGizmo
{
    public bool RequiresExclusiveFocus => true;

    private readonly ISimulationView _view;
    private readonly Entity _targetEntity;
    private float _currentYawRad;

    public EntityRotatorGizmo(ISimulationView view, Entity targetEntity)
    {
        _view = view;
        _targetEntity = targetEntity;

        ref readonly var initialTf = ref _view.GetComponentRO<SimTransform>(_targetEntity);
        _currentYawRad = initialTf.Rotation.Yaw;
    }

    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        if (!_view.IsAlive(_targetEntity)) { /* request removal */ return; }

        ref readonly var tf = ref _view.GetComponentRO<SimTransform>(_targetEntity);
        var tip = new Vector3(
            tf.Position.X + MathF.Cos(_currentYawRad) * 30f,
            tf.Position.Y + MathF.Sin(_currentYawRad) * 30f,
            tf.Position.Z);

        draw.DrawArrow(tf.Position, tip, Rgba32.Yellow, headSize: 3f);
    }

    public void OnDragUpdate(Vector3 worldPos)
    {
        ref readonly var tf = ref _view.GetComponentRO<SimTransform>(_targetEntity);
        var dx = worldPos.X - tf.Position.X;
        var dy = worldPos.Y - tf.Position.Y;
        _currentYawRad = MathF.Atan2(dy, dx);
    }

    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && !isPressed)
        {
            ref var tf = ref ((EntityRepository)_view).GetComponentRW<SimTransform>(_targetEntity);
            tf.Rotation = SimMath.FromYaw(_currentYawRad);
            RequestSelfRemoval();   // → ECS: DestroyEntity(toolEntity); pure: manager.RemoveTool(..)
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            RequestSelfRemoval();   // cancel
        }
    }

    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (key == MapKeyboardKey.Escape && isPressed)
            RequestSelfRemoval();
    }

    public void OnInteractionStarted(Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos)             { }
    public void OnCancel()                             { }
    public void OnMenuAction(int actionId)             { }

    public void Dispose() { /* nothing — manager already released the focus lock */ }
}
```

Things this code is *not* doing, intentionally:
- Not setting `ActiveGlobalGizmo` itself.
- Not emitting `InputCaptureBinding` itself.
- Not filtering events by token.
- Not implementing `OnInitialize` / `OnTeardown`.
- Not mapping `actionId` integers.

## 12. Required changes to current FDP code

1. **Remove hardcoded semantics** from `GizmoInteractionProxyTool` — drop `MouseButton.Left → Commit`, `Right/Escape → Cancel`. Keep only the generic drag-and-drop reflection it already does for hit-testable handles.
2. **Fix terminal hit-test priority.** Replace `prim.DebugLayer > best.DebugLayer` comparison in `DebugGizmoLayer.HandleInput` with reverse iteration over the buffer. Stop using `DebugLayer` as a Z-order.
3. **Add primitive shape** `InputCaptureBinding = 12` and the corresponding factory.
4. **Extend `GizmoInteractionEventKind`** with `RawInput = 5`. Define the `Space`-byte bit-packing for mouse/keyboard and pressed/released.
5. **Add local typed events** `GizmoMouseEvent`, `GizmoKeyEvent`. Update `GizmoInteractionIngressSystem.Translate` to publish them on `RawInput` payloads.
6. **Refactor `IStatefulGizmo`** to:
   - drop `OnInitialize`, `OnTeardown`, `CreateUndoRecord`,
   - add `RequiresExclusiveFocus`,
   - add the typed handlers (`OnMouseEvent`, `OnKeyEvent`, `OnDragUpdate`, etc.),
   - drop `ISimulationView` / `Entity` from `UpdateAndDraw`,
   - extend `IDisposable`.
7. **Split** `IStatefulGizmo` (global) from `IEntityStatefulGizmo` (entity-bound). Same for stateless variants.
8. **Extract `GizmoInteractionManager`** into the ECS-free core. Move the `ActiveGlobalGizmo` registry inside it (delete the ECS singleton).
9. **Demote `DataDrivenGizmoSystem`** and `StatelessGizmoSystem` to thin lifecycle bridges over the manager. They map `ConstructionOrder/DestructionOrder` to `AddTool/RemoveTool` and forward typed events to `DispatchEvent`.
10. **Drop pooling.** Remove `IBehaviorGizmoFactory.Rent/Return`. Use plain `new` and `Dispose`.

## 13. Reference implementation order (in `GizmoMap.Example`)

The example is a non-ECS test bed for the architecture. Implement in this order:

1. Extend `DebugPrimitiveShape` and `GizmoInteractionEventKind` in `GizmoMap.Contracts` / `GizmoMap.Network`.
2. Update `DebugGizmoLayer.HandleInput`: reverse-iteration hit-test, scan for `InputCaptureBinding`, raw-event reflection. Update `onInteraction` delegate to carry `actionId` (so we can pass key/button codes through without enlarging the DDS struct in the demo).
3. Build `GizmoInteractionManager` (pure C#) with `AddTool`, `RemoveTool`, `DispatchEvent`, `Emit`.
4. Implement `EntityRotatorGizmo` and `VertexEditGizmoProjector` as standalone classes, each in its own file, each implementing `IStatefulGizmo` (or `IGizmoInteractionHandler` + a plain emit hook for the demo). Two polygons get their own `VertexEditGizmoProjector` instances — they multiplex purely through tokens and need no shared coordination.
5. `DemoSceneGenerator` becomes the host: holds a `GizmoInteractionManager`, populates it with the two polygon editors at startup, and exposes a `TriggerRotator()` method that constructs and registers a rotator on demand.

When this works end-to-end in the example, port the same `GizmoInteractionManager` and interface set into FDP and rewire `DataDrivenGizmoSystem` over it.

## 14. Open questions / deferred

- *Visual feedback for "I have focus".* A focused gizmo can simply branch its `UpdateAndDraw` on its internal state and emit primitives in a different color. The contract does not need a `bool HasFocus` flag because the gizmo always knows: shared focus = its own active-vertex field is set; exclusive focus = it is alive (the manager wouldn't be ticking it otherwise).
- *Filter mask on `InputCaptureBinding`.* The conversation considered a bitmask in `ConditionMask` to declare "I only want mouse moves, not keys." For now, `ConditionMask` is the exclusive flag; further filter bits can be added later in unused space without breaking the 64-byte footprint.
- *`MapCanvas` tool stack.* The frontend keeps its tool stack for routing, but stripped of business logic — the proxy tool remains as a generic input capturer that pops itself when the backend stops emitting the capture binding. No semantic decisions in the stack.
- *Multiple subsystems emitting capture simultaneously.* Each subsystem has its own backend manager and its own primitive stream. The terminal only listens to one stream at a time (active perspective). So inter-subsystem conflicts cannot reach the terminal; perspective switching does the equivalent of a hard reset of input capture.

---

**Summary of the central idea.** The terminal is an immediate-mode mirror; the backend describes what it wants, frame by frame, in a 64-byte primitive language. Focus is either spatial (resolved by where the cursor is, on the terminal) or logical (resolved by who holds the registry slot, on the backend). Gizmos are plain C# state machines with constructors and `Dispose`. The ECS, when present, is a lifecycle adapter — not the architecture.
