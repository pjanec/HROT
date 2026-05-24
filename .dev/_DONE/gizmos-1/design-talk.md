My simulation engine is based on ECS and has 2d debug visualization (imgui + raylib). In the future it will get a 3d visualization as well. I would like to have dynamically installable debug visualizations ("gizmos") for the 2d map  and another for 3d visualization.  Some global (entity independent), some tied to a concrete entity, some tied to a concretely running behavior on concrete entity. The visualization instance would determine when to show - some shows only when entity is selected, some are adding some options to a debug setting option dialog (key - value) and using those to control how they are visualized. Those settings should be remembered across sessions; can be reset to default (no values saved). I would like to keep the format of the debug visulization instructions generic enough to be applicable in different contexts - rendered locally - transportable over network to be rendered remotely in remote visulizer, but still able to communicate back The gizmos should be also interactive - able to install themselves as tools in the map context (so they can get "focus" - be active and taking user input), responding to user actions, and having the possibility to affect the system (change component state, send messages on system event bus etc.) Maybe you could suggest other usefull features and mechanisms used in game engines for debug  visualizations. let's brainstorm what a godd game engine should provide in this regards



A good design is to treat **debug visualization as a runtime-extensible debug tool system**, not just “drawing callbacks.”

## Core concept

Define a **Debug Visualization/Gizmo subsystem** with three layers:

```text
Gizmo Provider
  declares capabilities, settings, tools, supported views

Gizmo Instance
  bound to global / entity / behavior / query / system state

Gizmo Renderer Backend
  local 2D, local 3D, remote 2D/3D, headless capture, recording
```

The key abstraction should not be “draw a line in Raylib.”
It should be:

```text
Emit generic debug primitives + expose optional interaction tools + react to debug events.
```

## Suggested gizmo scopes

You probably want at least these scopes:

```text
Global gizmo
  Example: navmesh overlay, pathfinding heatmap, event bus trace, physics broadphase grid

Entity gizmo
  Example: transform axes, collision bounds, sensor range, AI state label

Component gizmo
  Example: render all Collider2D components, draw Health bars, show Velocity vectors

Behavior-instance gizmo
  Example: show currently running behavior tree node on entity #42

System gizmo
  Example: ECS scheduling graph, pathfinding requests, physics contacts

Query gizmo
  Example: visualize all entities matching Position + Target + Steering

Tool gizmo
  Example: click-to-set-destination, drag collider bounds, paint navigation cost
```

The “behavior-instance” case is important. I would model it explicitly instead of forcing it into entity gizmos.

## Generic debug primitive format

Avoid making gizmos call Raylib/ImGui directly. Let them emit backend-neutral commands.

Example primitives:

```text
Line2D / Line3D
Polyline
Arrow
Circle / Sphere
Box2D / Box3D
Capsule
Polygon
TextBillboard
Label2D
Icon
Heatmap
Grid
TransformAxes
Trajectory
MeshWireframe
Screen-space panel
Table / key-value inspector contribution
Graph / timeline / event trace
```

Each primitive should support metadata:

```text
id
scope
world/local/screen coordinate space
layer
depth behavior
color/style
lifetime
pickable flag
tooltip
selection target
interaction handle id
```

Example conceptual command:

```json
{
  "type": "arrow",
  "space": "world2d",
  "from": [10.0, 4.0],
  "to": [13.0, 6.0],
  "style": {
    "color": "VelocityVector",
    "thickness": 2
  },
  "owner": {
    "entity": 123,
    "component": "Velocity"
  },
  "interaction": {
    "pickable": true,
    "tooltip": "Velocity: 3.6 m/s"
  }
}
```

For 3D later, the same model becomes:

```json
{
  "type": "box",
  "space": "world3d",
  "center": [1, 2, 3],
  "rotation": [0, 0, 0, 1],
  "size": [2, 1, 4]
}
```

## Local vs remote rendering

To support remote visualization, separate:

```text
Gizmo logic
  runs inside game/simulation

Debug protocol
  serializes primitives, settings, tool declarations, input events, commands

Visualizer
  renders primitives and sends interaction events back
```

The protocol should carry:

```text
Frame snapshot
Persistent objects
Transient draw commands
Selection changes
Input events
Tool activation/deactivation
Settings updates
Entity/component edit requests
System commands/messages
```

Important distinction:

```text
Transient primitive
  exists for one frame or N milliseconds

Persistent debug object
  has stable id and can be updated/destroyed
```

Persistent objects are useful for remote visualization because they reduce bandwidth.

## Gizmo lifecycle

A gizmo provider could expose something like:

```text
Register()
  declares name, version, supported views, settings schema, tools

CreateInstance(context)
  for global/entity/component/behavior/etc.

UpdateVisibility(context)
  decides whether it should show

EmitDebugPrimitives(context, sink)
  sends generic draw commands

ContributeUi(context, uiSink)
  adds settings/options/inspector sections

HandleInput(event, context)
  reacts to clicks/drags/keys when active

HandleCommand(command, context)
  receives remote/debug commands
```

I would avoid having gizmos directly own ImGui state. Let them contribute declarative UI.

## Visibility model

Visibility should be policy-driven, not hardcoded.

Useful visibility predicates:

```text
Always
When entity selected
When entity hovered
When component exists
When behavior active
When system debug flag enabled
When query matches
When tool active
When debug category/layer enabled
When camera zoom is within range
When within viewport
```

A good model:

```text
GizmoVisibility =
  provider default
  AND user category/layer settings
  AND instance predicate
  AND context predicate
```

## Settings persistence

Give each gizmo a settings schema:

```json
{
  "gizmo": "AI.PathDebug",
  "version": 2,
  "settings": {
    "showOpenSet": {
      "type": "bool",
      "default": false
    },
    "maxNodes": {
      "type": "int",
      "default": 200,
      "min": 0,
      "max": 10000
    },
    "colorMode": {
      "type": "enum",
      "values": ["cost", "visited", "heuristic"],
      "default": "cost"
    }
  }
}
```

Persist only user overrides:

```text
default setting:
  not stored

changed setting:
  stored

reset to default:
  remove stored value
```

Storage key should include:

```text
engine/project
plugin/gizmo id
setting version
scope if needed
```

Example:

```text
debug.gizmos.AI.PathDebug.v2.showOpenSet = true
```

You may want three setting scopes:

```text
Global user settings
Project settings
Session-only settings
```

And maybe:

```text
Per-entity overrides
Per-behavior-type overrides
Per-map overrides
```

But do not start with too many. Start with global/project/session.

## Tool integration

Interactive gizmos should be able to register **tools** into the map/scene context.

Examples:

```text
Select entity
Move entity
Edit collider
Set AI target
Create pathfinding request
Inspect behavior node
Paint nav costs
Measure distance
Spawn test event
Drag steering target
```

Each tool should declare:

```text
id
display name
icon
supported views: 2D / 3D
activation mode: toggle / hold / modal
input priority
cursor
shortcut
required permissions
selection requirements
```

Tool lifecycle:

```text
OnActivate
OnDeactivate
OnPointerDown
OnPointerMove
OnPointerUp
OnKey
OnCancel
OnCommit
OnUndo
```

For remote use, tool events should be serialized:

```json
{
  "type": "pointer_down",
  "view": "map2d",
  "world": [12.4, 8.1],
  "screen": [522, 340],
  "button": "left",
  "modifiers": ["shift"],
  "activeTool": "AI.SetDestination"
}
```

## Mutating the simulation

Do not let gizmos arbitrarily mutate ECS state unless you are comfortable with chaos.

Better: expose controlled debug actions:

```text
ComponentPatch
Command
EventBusMessage
SimulationRequest
ToolTransaction
```

Example:

```json
{
  "type": "component_patch",
  "entity": 123,
  "component": "Transform2D",
  "patch": {
    "position": [20, 15]
  }
}
```

Or:

```json
{
  "type": "event_bus_message",
  "event": "AI.SetDestination",
  "payload": {
    "entity": 123,
    "target": [20, 15]
  }
}
```

This gives you:

```text
validation
permissions
undo/redo
logging
remote safety
replayability
```

## Recommended architecture

A strong shape would be:

```text
DebugGizmoRegistry
  knows installed gizmo providers

DebugGizmoManager
  creates instances, updates visibility, gathers draw/UI/tool contributions

DebugDrawCommandBuffer
  backend-neutral primitive stream

DebugUiContributionBuffer
  declarative panels/settings/inspectors

DebugToolManager
  manages active tool, focus, input routing

DebugProtocol
  serializes draw commands, UI schema, input, commands

DebugSettingsStore
  persists overrides and reset behavior

DebugAuthority
  validates mutations, permissions, simulation safety
```

## Dynamic installation

For dynamically installable gizmos, each plugin should include a manifest:

```json
{
  "id": "bagira.ai.path_debug",
  "name": "AI Path Debug",
  "version": "1.3.0",
  "engineApi": ">=0.8 <0.9",
  "provides": ["gizmo", "tool", "settings"],
  "views": ["map2d", "scene3d"],
  "ecsAccess": {
    "read": ["Transform2D", "PathAgent", "PathRequest"],
    "write": [],
    "events": ["AI.SetDestination"]
  }
}
```

Useful install-time checks:

```text
engine API compatibility
component schema compatibility
permissions
trusted/untrusted plugin mode
network protocol compatibility
hot reload support
```

## Features worth adding

These are very useful in real engines:

### 1. Debug layers/categories

```text
Physics
AI
Navigation
Animation
Rendering
Networking
Audio
Gameplay
ECS
Performance
```

Allow toggling categories independently.

### 2. Selection-aware overlays

When selected:

```text
show detailed gizmos
show labels
show editable handles
show inspector contributions
```

When unselected:

```text
show cheap summary only
```

### 3. Picking support

Debug primitives should be pickable.

Clicking a shape can select:

```text
entity
component
behavior instance
system event
debug object
```

### 4. Tool focus and input capture

Prevent multiple gizmos from fighting over input.

Use:

```text
hover target
active tool
captured pointer
keyboard focus
modal operation
```

### 5. Undo/redo transactions

Interactive gizmos need transaction semantics:

```text
BeginEdit
PreviewEdit
CommitEdit
CancelEdit
UndoEdit
```

Critical for transform/collider/path editing.

### 6. Time controls

For behavior debugging, add:

```text
pause
step frame
step system
rewind from recorded state
scrub timeline
show historic gizmos
```

Historic gizmos are extremely useful for AI/pathfinding.

### 7. Lifetime and history

Allow primitives to specify:

```text
one frame
N seconds
until replaced
until event resolved
recorded in debug timeline
```

Example: draw last 100 path requests.

### 8. Entity trails

Built-in support for:

```text
position trails
orientation history
velocity history
state changes over time
```

### 9. Debug labels with LOD

Text becomes unreadable fast. Add label LOD:

```text
hide when zoomed out
show only selected
cluster labels
priority-based label culling
```

### 10. Performance budget

Each gizmo should have budgets:

```text
max primitives
max CPU time
max network bytes
max update frequency
```

Example:

```text
AI heatmap updates at 5 Hz, not every frame.
```

### 11. Remote authority model

For remote visualizers, decide:

```text
view-only
interactive but sandboxed
can send debug events
can patch components
can execute tools
```

This should be explicit.

### 12. Recording/export

Allow debug streams to be recorded:

```text
debug capture file
remote replay
bug report attachment
deterministic replay overlay
```

Very valuable for reproducing bugs.

### 13. Declarative inspector extensions

Gizmos should be able to contribute inspector UI:

```text
read-only fields
buttons
sliders
toggles
graphs
event logs
```

But keep it generic, not ImGui-specific.

### 14. Multi-view support

Same gizmo provider can expose:

```text
2D map visualization
3D scene visualization
inspector panel
timeline view
graph view
table view
```

For example, an AI behavior gizmo might show:

```text
2D: target, path, perception cone
3D: perception volume
Inspector: current state
Timeline: behavior transitions
Graph: behavior tree
```

## One important design recommendation

Make the gizmo output **declarative and serializable**, but allow local providers to be native code.

So:

```text
Native plugin can compute gizmo data.
Output must be generic commands.
Input must be generic events.
Mutations must be explicit commands.
```

This gives you local performance while preserving remote visualization.

## Minimal MVP

For a first version, I would build:

```text
1. Gizmo registry
2. Generic 2D primitive command buffer
3. Entity/global/component gizmo scopes
4. Settings schema + persistent overrides
5. Visibility predicates
6. Tool registration + active tool input routing
7. ComponentPatch/EventBusMessage mutation API
8. Debug category/layer toggles
```

Then later add:

```text
remote protocol
3D primitives
timeline/history
record/replay
plugin sandboxing
advanced UI contribution schema
```

## The core principle

A strong engine should provide this contract:

```text
Gizmos observe ECS/debug state,
emit backend-neutral visual/debug/UI/tool descriptions,
receive generic interaction events,
and request controlled changes through validated debug commands.
```

That keeps the system extensible, render-backend-independent, remote-friendly, and safe enough for serious tooling.

--------------------

To maintain the strict performance, zero-allocation standards, and network-ready nature of our engine, we can design these primitives using the exact architectural patterns already established in the codebase. *Please note that the specific C# structs defined below are my architectural synthesis based on the engine's existing paradigms, and are not directly found in the provided sources.*

To ensure these primitives are highly performant and network-transportable without generating Garbage Collection (GC) pressure on the hot path, we should model them as **cache-friendly tagged unions**. This perfectly mirrors the engine's existing `AttributeValueUnion` and `HsmEvent` designs. 

Here is the proposed structural definition:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.IG.Components; // For Color32

namespace Fdp.Core.Diagnostics.Visuals
{
    public enum DebugPrimitiveShape : byte
    {
        Line = 0,
        Sphere = 1,
        Box = 2,
        Text = 3
    }

    public enum CoordinateSpace : byte
    {
        World = 0,
        Screen = 1,
        Local = 2
    }

    /// <summary>
    /// A single, backend-neutral debug drawing instruction. 
    /// Explicit layout ensures a flat, blittable 64-byte struct (single cache line) 
    /// that can be easily transported over CycloneDDS or stored in NativeChunkTables.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DebugPrimitive
    {
        // ── Header (16 bytes) ──────────────────────────────────────────────────
        [FieldOffset(0)] public DebugPrimitiveShape Shape;
        [FieldOffset(1)] public CoordinateSpace Space;
        [FieldOffset(2)] public Color32 Color; // RGBA byte color
        [FieldOffset(6)] public ushort LayerMask; // For debug layers/categories
        
        [FieldOffset(8)] public float Thickness;
        [FieldOffset(12)] public float LifetimeSeconds; // 0.0f = single frame

        // ── Payload Union (48 bytes max to fit in 64 byte cache line) ──────────

        // LINE PAYLOAD
        [FieldOffset(16)] public Vector3 LineStart;
        [FieldOffset(28)] public Vector3 LineEnd;

        // SPHERE PAYLOAD
        [FieldOffset(16)] public Vector3 SphereCenter;
        [FieldOffset(28)] public float SphereRadius;

        // TEXT PAYLOAD
        [FieldOffset(16)] public Vector3 TextPosition;
        [FieldOffset(28)] public FixedString32 TextPayload; // Zero-allocation string
    }
}
```

### Why this architecture fits our engine perfectly:

1. **Flat, Blittable Memory Layout:** By using `[StructLayout(LayoutKind.Explicit, Size = 64)]`, we guarantee the primitive occupies exactly one 64-byte cache line. This allows us to use `NativeArray<DebugPrimitive>` or `NativeChunkTable<DebugPrimitive>` to store thousands of drawing instructions per frame with zero managed heap allocations.
2. **Polymorphism without References:** Instead of using an abstract `IDebugPrimitive` class which would force boxing and virtual dispatch, we use an overlapping field union (like the `AttributeValueUnion`). The `Shape` discriminator tells the rendering backend how to interpret bytes 16 through 64. 
3. **Zero-Allocation Text:** A common pitfall in debug visualizers is thrashing the GC with string allocations for labels. By embedding our existing `FixedString32` struct, we can push formatted text across the network and to the renderer entirely on the stack.
4. **Backend-Neutrality:** Notice there are no Raylib or ImGui types here. This struct is pure data. A `DebugRenderSystem` can simply iterate an array of these structs and map them to `Raylib.DrawLine3D` or `Raylib.DrawSphere`, keeping the simulation layer completely decoupled from the presentation backend.

To route these across the network for remote visualization, we could easily wrap an array of these primitives in a MessagePack payload or a DDS topic (similar to the `RaycastRequestBatch` pattern), enabling the remote authority model discussed in the proposal.



-----------------

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// visualizer for a specific behavior.
    /// </summary>
    public interface IBehaviorGizmo
    {
        void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder drawBuilder);
    }
}


----------------

Moving ephemeral presentation state entirely out of the ECS simulation components is the exact right architectural instinct. Storing debug-only interaction state inside simulation components violates the separation of concerns, risks cross-thread race conditions, and forces us to add explicit `DataPolicy.NoSave` and `DataPolicy.NoRecord` guards to keep the debug state from polluting the network and serialization pipelines. 

Your proposed design—a single registry that tracks stateful, memory-only gizmo instances tied to the lifecycle of behaviors—is a highly elegant solution. To implement this while adhering to the engine's strict performance and zero-allocation standards on the hot path, we should combine a centralized registry with the engine's existing event-driven lifecycle patterns.

Here is how we can architect this cleanly:

### 1. The Event-Driven Registry
Instead of polling the `EntityRepository` every frame to see what behaviors are running, our central registry acts as an event observer. The FDP engine already publishes specific managed and unmanaged events during behavior and entity lifecycles:
*   `AssignBehaviorEvent`: Published to the event bus when a new behavior is assigned and parameters are parsed.
*   `ClearBehaviorEvent`: Published when a behavior is forcibly interrupted or aborted.
*   `DestructionOrder`: Published when an entity is being destroyed.

We create a single `StatefulGizmoManagerSystem` that reads these events from the `ISimulationView`'s read buffer.

### 2. Zero-Allocation Lifecycle Management
When the system reads an `AssignBehaviorEvent`, it checks if the assigned behavior hash has a registered gizmo factory. If it does, we do not simply `new` up an object (which would create GC pressure over time). Instead, we rent a stateful gizmo instance from an object pool and store it in a `Dictionary<Entity, IStatefulGizmo>`.

When we read a `ClearBehaviorEvent` or a `DestructionOrder` for that entity, we remove the gizmo from the dictionary, call a teardown or `Clear` method on it, and return it to the pool. 

### 3. Stateful Execution
Because the gizmo is a persistent C# object residing in our dictionary for the entire duration of the behavior, it can safely maintain rich, ephemeral state. It can track things like:
*   Animation timers for visual pulses.
*   Historic positional trails without needing a dedicated `HistoryTrail` ECS component.
*   Selection states of individual sub-elements (e.g., which specific waypoint vertex the user clicked).
*   Contextual focus, allowing it to seamlessly act as an interactive `IMapTool` that captures mouse input without conflicting with other tools.

During the system's `Execute` phase, we completely bypass ECS queries. We simply iterate the values of our active dictionary:

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    // 1. Process teardowns
    foreach (var evt in view.ReadEvents<DestructionOrder>())
        RemoveAndPoolGizmo(evt.Entity);

    foreach (var evt in view.ReadEvents<ClearBehaviorEvent>())
        RemoveAndPoolGizmo(evt.Entity);

    // 2. Process startups
    foreach (var evt in view.ReadManagedEvents<AssignBehaviorEvent>())
    {
        if (_factories.TryGetValue(evt.BehaviorName, out var factory))
        {
            var gizmo = factory.Rent();
            gizmo.OnInitialize(view, evt.Entity); // Gizmo sets up its initial state
            _activeGizmos[evt.Entity] = gizmo;
        }
    }

    // 3. Execution (Strict O(K) scaling)
    foreach (var kvp in _activeGizmos)
    {
        var entity = kvp.Key;
        var gizmo = kvp.Value;
        
        // Pass the read-only view so the gizmo can project the latest simulation data 
        // (like Blackboard1024 or SimTransform) into its internal state
        gizmo.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
    }
}
```

### Architectural Benefits
1. **True $O(K)$ Scaling:** Where $K$ is the number of entities *actively* running a visualized behavior. We never iterate chunks or execute ECS queries just to find gizmos.
2. **Pristine ECS Data Model:** The simulation components (like `Blackboard1024` or `BehaviorState`) remain pure data. All visual state, historic trails, and UI interaction logic live exclusively in the C# heap memory of the ephemeral gizmo objects.
3. **Safe Interaction:** Since the gizmo persists across frames, it can safely accumulate user input over time (like dragging a path node) and then emit a single structural mutation via the `IEntityCommandBuffer` when the interaction completes.









---------------------
Handling interactive gizmos elegantly requires us to strictly maintain the boundary between the presentation tier and the simulation tier. If we let arbitrary debug UI code reach into the ECS and mutate memory, we instantly compromise thread safety, determinism, and network replication. 

To build a professional, robust system, we must abstract user input through a prioritized tool stack and route all state mutations through deferred command buffers. Here is the architectural blueprint for achieving this based on the engine's current paradigms.

### 1. Abstracting Input via the `IMapTool` Stack
To prevent multiple gizmos from fighting over input and causing routing chaos, we leverage the State Pattern using the engine's existing `IMapTool` abstraction. 

Instead of polling raw input globally, an interactive gizmo acts as—or delegates to—an `IMapTool`. The `MapCanvas` manages a stack of these tools (`PushTool`, `PopTool`). The active tool at the top of the stack receives exclusive access to strongly-typed input events:
*   `HandleHover(Vector2 worldPos)`
*   `HandleClick(Vector2 worldPos, MouseButton button)`
*   `HandleDrag(Vector2 worldPos, Vector2 delta)`.

When a user interacts with a behavior gizmo (e.g., clicking a waypoint), the gizmo can push a focused interaction tool (like `EntityDragTool` or a custom `WaypointEditTool`) onto the stack. This guarantees the tool captures all subsequent mouse drags and key presses (like `Escape` to cancel) until the interaction completes and the tool pops itself.

### 2. Safe State Manipulation via Command Buffers
You must never let gizmos arbitrarily mutate ECS state directly. Doing so during parallel system execution or from an asynchronous UI thread guarantees race conditions and corrupts chunk versions.

Instead, expose controlled debug actions. All structural and data mutations must be routed through the `IEntityCommandBuffer` (ECB). 
*   **Data Mutation:** If a gizmo adjusts a vehicle's target position, it records a `SetComponent` command to the buffer.
*   **Event Publishing:** If the gizmo triggers a high-level action, it records a `PublishEvent` command to the bus.

Because each thread or module can have its own ECB, the gizmo can record changes lock-free. The kernel then deterministically plays back the command buffer on the main thread during a safe sync point. 

### 3. The Interactive Gizmo Lifecycle
Let’s map out a clean flow for an interactive gizmo (e.g., dragging an attack vector baseline):

1.  **Passive Rendering (Stateless):** The gizmo reads the ECS state (`BrainBlackboard`) and emits backend-neutral shapes (lines, spheres) via `IDebugDrawBuilder`.
2.  **Hover Detection:** The default `StandardInteractionTool` detects the mouse is over the gizmo's hit radius.
3.  **Focus Capture (Stateful):** On mouse down, the system pushes a transient `GizmoDragTool` onto the `MapCanvas` stack.
4.  **Live Preview:** As the user drags, `GizmoDragTool.HandleDrag` updates its internal, memory-only state. It emits updated declarative primitives to the `IDebugDrawBuilder` for a responsive visual preview, but *touches absolutely no ECS data*.
5.  **Commit:** On mouse release (`HandleClick`), the tool takes the final computed values, writes a `SetComponent` instruction to the `IEntityCommandBuffer`, and pops itself from the stack.

### 4. Transaction Semantics (Undo / Redo)
For complex interactions, interactive gizmos need transaction semantics (Begin, Commit, Cancel). Because the gizmo maintains the "draft" state internally while the `MapCanvas` tool has focus, canceling the interaction (e.g., pressing `Escape`) is trivial: the tool just pops itself off the stack without ever writing to the `IEntityCommandBuffer`. 

If you want to support global Undo/Redo later, you can expand the command buffer pattern. Instead of applying changes blindly, the gizmo submits an `IReversibleCommand` that encapsulates both the new state and the previous state snapshot, allowing the engine to walk backward through the debug mutation history.

---------------------

Tying the interactive state of a gizmo directly to the `IMapTool` interface completely breaks the architectural boundary. Because `IMapTool` is deeply coupled to `MapCanvas`, 2D coordinates, and `Raylib_cs` input enums (like `MouseButton` and `KeyboardKey`), embedding our gizmo logic there makes it strictly a local, 2D-only construct. This violates the core requirement that debug visualization instructions remain generic, backend-neutral, and transportable over a network.

To maintain a pristine architecture where gizmos work seamlessly across local 2D maps, future 3D viewports, and remote visualizers, we must completely decouple **input capture** (Presentation Layer) from **interaction logic** (Gizmo Layer). 

Here is the clean architectural pattern to solve this:

### 1. Backend-Neutral Interaction Events
Instead of the gizmo reading Raylib mouse states, the interaction must be abstracted into serializable tool events. We define protocol-neutral DTOs that represent the user's intent. For instance, instead of a 2D screen delta, a drag event should provide a 3D world-space ray or intersection point:
*   `GizmoHoverEvent`
*   `GizmoDragStartEvent`
*   `GizmoDragUpdateEvent`
*   `GizmoDragCommitEvent`

### 2. The Presentation-Tier Adapter (The Proxy Tool)
The `MapCanvas` (or the future 3D renderer) does not host the actual gizmo. Instead, it hosts a generic proxy, such as a `GizmoInteractionAdapterTool` that implements `IMapTool`. 

The sole responsibility of this adapter is to capture local platform input (e.g., Raylib mouse clicks), translate those physical coordinates into the 3D backend-neutral tool events, and route them to the active gizmo. If the operator is using a remote visualizer, the remote client captures the input and serializes these exact same interaction events over the network protocol (e.g., DDS).

### 3. The Pure Gizmo System
The gizmo itself remains a pure, stateless (or ephemerally stateful) processor running securely on the simulation node. Its execution flow becomes:
1. **Receive:** It consumes the backend-neutral `GizmoDragUpdateEvent` (from either the local proxy tool or the network).
2. **Process:** It updates its ephemeral memory state (e.g., calculating the new heading angle).
3. **Present:** It emits declarative, backend-neutral primitives (like lines and text) to the accumulation buffer, which are then routed to the local renderer or serialized to the remote client.
4. **Mutate:** When it receives a `GizmoDragCommitEvent`, it records the final state change (e.g., `SetComponent`) into the deferred `IEntityCommandBuffer`.

### Why this is the correct best practice
By ensuring that the tool events are serialized, you enforce a strict "Remote Authority Model". The actual gizmo logic runs exactly where the heavy ECS data lives (the simulation kernel), while the presentation clients (2D, 3D, or remote) are reduced to "dumb terminals" that only render primitive shapes and forward user interaction events. This ensures perfect determinism and zero duplication of your debug tooling logic.


---------------


Designing a robust picking system for declarative, backend-neutral debug primitives is one of the most intellectually satisfying challenges in engine architecture. Since our gizmos emit pure data (lines, spheres) and run entirely decoupled from the presentation layer, the local `MapCanvas` or remote 3D client has no idea *what* a given sphere represents.

If we were to use a naive approach, we would maintain a stateful, engine-wide dictionary mapping `PrimitiveID -> C# Object`. That violates our zero-allocation requirements, breaks determinism, and fails completely in a distributed network environment.

Instead, we must use a **Stateless Picking Token** architecture. Here is the architectural blueprint for how we can build this into the Fast Data Plane (FDP) engine.

*Please note: The specific structs and events below are an architectural proposal synthesizing your initial idea with the engine's existing ECS and DDS networking patterns. They are not currently implemented in the provided sources.*

### 1. The `PickToken` (Stateless Identity)
We embed a purely blittable identity token into every `DebugPrimitive` that needs to be interactive. A token contains the exact ECS `Entity` handle (which inherently includes the generational safety check) and a domain-specific `SubElementId`.

```csharp
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// A 12-byte stateless identifier embedded in interactive debug primitives.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PickToken
    {
        /// <summary>The ECS entity this primitive belongs to.</summary>
        public Entity Target;

        /// <summary>
        /// Domain-specific identifier (e.g., waypoint index, behavior hash, or bounding-box face).
        /// 0 means the primitive represents the entity as a whole.
        /// </summary>
        public uint SubElementId;

        public bool IsValid => !Target.IsNull;
    }
}
```
We then add this `PickToken` to our `DebugPrimitive` header. If a primitive is purely decorative (e.g., a background grid), its `Target` is `Entity.Null`, meaning it cannot be picked.

### 2. Presentation-Tier Hit Testing (The "Dumb Terminal")
The Presentation Layer (whether it's the 2D `MapCanvas` using Raylib or a remote 3D client) is responsible for the spatial math, completely oblivious to the simulation logic.

When the operator clicks the screen:
1. The client casts a ray (or a 2D point) into its local array of rendered `DebugPrimitive` structs.
2. It performs mathematical intersections (Ray-vs-Sphere, Point-vs-Line) against the primitive payloads.
3. It finds the closest intersecting primitive that has a valid `PickToken`.
4. It extracts the token and fires a backend-neutral tool event.

### 3. The Round-Trip Interaction Event
The presentation proxy tool wraps the extracted token into a serializable event and routes it to the simulation node. 

```csharp
namespace Fdp.Toolkit.Diagnostics.Events
{
    /// <summary>
    /// Emitted by the presentation layer when the user clicks an interactive debug primitive.
    /// Transportable locally via the FdpEventBus, or remotely via CycloneDDS.
    /// </summary>
    public struct GizmoPickedEvent
    {
        public PickToken Token;
        public Vector3 IntersectionWorldPos;
        public MouseButton Button; // Left click, right click, etc.
    }
}
```

### 4. Simulation-Tier Resolution (The Dispatcher)
Back on the authoritative simulation node, a central `GizmoInteractionSystem` (running in the `SystemPhase.Input` phase) reads the `GizmoPickedEvent` off the event bus.

Because the `PickToken` contains the exact ECS `Entity` handle, the resolution is an $O(1)$ operation that requires zero reflection or dictionary lookups:

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    var pickEvents = view.ReadEvents<GizmoPickedEvent>();
    
    foreach (ref readonly var evt in pickEvents)
    {
        var targetEntity = evt.Token.Target;
        
        // 1. Generational safety check - did the entity die while the click was in-flight?
        if (!view.IsAlive(targetEntity)) continue;

        // 2. Are we clicking the Entity itself, or a sub-element?
        if (evt.Token.SubElementId == 0)
        {
            // Standard entity selection: update the SelectionState component
            SelectEntity(view, targetEntity);
        }
        else
        {
            // 3. Sub-element routing
            // e.g., We clicked a specific waypoint (SubElementId = WaypointIndex).
            // We can dispatch this to the IBehaviorGizmo currently active on this entity.
            RouteToBehaviorGizmo(view, targetEntity, evt.Token.SubElementId, evt.IntersectionWorldPos);
        }
    }
}
```

### Architectural Triumphs of this Pattern
1. **Zero Client-Side State:** The remote visualizer doesn't need to download or mirror the TKB (Transient Knowledge Base) or the behavior trees. It just renders colored spheres and reflects the `PickToken` back to the server when clicked.
2. **Generational Safety:** Because the 64-bit `Entity` handle includes the generation counter, if a remote user clicks a waypoint for a vehicle that was destroyed a fraction of a second earlier, `view.IsAlive(targetEntity)` safely catches it. No null reference exceptions.
3. **Sub-Element Granularity:** By utilizing the `SubElementId`, a single behavior like `PlatoonHillAttack` can emit 50 different spheres representing its firing line slots. When the user clicks one, the simulation node knows exactly *which* slot to mutate without doing expensive proximity checks against the component data.

------------------------------

----

we went into behavior-dependent gizmos (which are inherently tied to a concrete entity) shich are instantiated when a concrete behavior on an entity gets active. There could be other gizmos related to an entity, i.e. that should be installed if entity of such type exists. we should monitor entity creation (ConstructionOrder event) and destruction (DestructionOrder event) and install/uninstall gizmos from a global gizmo registry whose (static) filtering function indicates a match for concrete entity just created

------

Extending the event-driven lifecycle pattern to general entities via `ConstructionOrder` and `DestructionOrder` is the exact right move. It completely eliminates polling and perfectly aligns with the engine's `EntityLifecycleModule` (ELM) architecture.

While your instinct to use a "static filtering function" is conceptually sound, invoking arbitrary C# delegates (like `Func<Entity, bool>`) for every spawned entity introduces branch unpredictability and cannot be cleanly SIMD-accelerated. Instead, we can achieve $O(1)$ or SIMD-speed filtering by leveraging the data the engine already guarantees at creation time.

Here is the clean architectural blueprint for entity-bound stateful gizmos:

### 1. High-Performance Filtering Rules

When a new entity enters the world, the `EntityLifecycleModule` publishes a `ConstructionOrder` event. This event explicitly carries the `BlueprintId` (the `TkbType` defining the entity's archetype).

Instead of an opaque filter delegate, our global gizmo registry should map gizmo factories using two high-performance criteria:

1. **Blueprint Match ($O(1)$):** A dictionary mapping a `BlueprintId` directly to a gizmo factory. This is perfect for unit-specific gizmos (e.g., "Install the APC suspension visualizer on all `MilitaryAPC` entities").
2. **Component Mask Match (SIMD):** The registry stores a `BitMask256` representing required component IDs. When an entity is spawned, we do a bitwise AND against its `EntityHeader.ComponentMask`. This is ideal for trait-based gizmos (e.g., "Install the trajectory visualizer on anything with a `NavState` and `SimVelocity`").

### 2. The Stateful Entity Gizmo Contract

We define a contract for ephemeral, memory-only presentation state that persists for the lifetime of the entity.

```
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Visuals;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IStatefulEntityGizmo
    {
        void OnInitialize(ISimulationView view, Entity entity);
        void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder drawBuilder);
        void OnTeardown();
    }
}
```

### 3. The Lifecycle-Driven Manager System

We implement a single system that observes the ELM events and manages the lifecycles of these stateful objects via zero-allocation object pools.

```
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Visuals;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class EntityGizmoManagerSystem : IEcsModuleSystem
    {
        private readonly IDebugDrawBuilder _drawBuilder;
        private readonly EntityGizmoRegistry _registry; // Contains our Blueprint & Mask filters

        // Tracks active gizmos per entity. An entity might match multiple gizmos.
        private readonly Dictionary<Entity, List<IStatefulEntityGizmo>> _activeGizmos = new();

        public EntityGizmoManagerSystem(EntityGizmoRegistry registry, IDebugDrawBuilder drawBuilder)
        {
            _registry = registry;
            _drawBuilder = drawBuilder;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;

            // 1. Teardown: Safely release gizmos when entities are destroyed
            foreach (ref readonly var evt in view.ReadEvents<DestructionOrder>()) //
            {
                if (_activeGizmos.Remove(evt.Entity, out var gizmos))
                {
                    foreach (var gizmo in gizmos)
                    {
                        gizmo.OnTeardown();
                        _registry.ReturnToPool(gizmo);
                    }
                }
            }

            // 2. Startup: Rent and initialize gizmos for new entities
            foreach (ref readonly var evt in view.ReadEvents<ConstructionOrder>()) //
            {
                // Note: ConstructionOrder fires as the entity enters 'Constructing' state.
                // The BlueprintApplicationSystem applies TKB components in the same frame.
                ref var header = ref repo.GetHeader(evt.Entity.Index);

                // $O(1)$ and SIMD matching using the registry
                var matchedFactories = _registry.Match(evt.BlueprintId, header.ComponentMask);

                if (matchedFactories.Count > 0)
                {
                    var instanceList = new List<IStatefulEntityGizmo>(matchedFactories.Count);
                    foreach (var factory in matchedFactories)
                    {
                        var gizmo = factory.Rent();
                        gizmo.OnInitialize(view, evt.Entity);
                        instanceList.Add(gizmo);
                    }
                    _activeGizmos[evt.Entity] = instanceList;
                }
            }

            // 3. Execution: Strictly O(K) where K is entities with active gizmos
            foreach (var kvp in _activeGizmos)
            {
                var entity = kvp.Key;

                // Generational safety check - ensure we don't draw for a recycled slot
                if (!view.IsAlive(entity)) continue;

                foreach (var gizmo in kvp.Value)
                {
                    gizmo.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
                }
            }
        }
    }
}
```

### Architectural Benefits

1. **Zero-Polling Overhead:** By consuming `ConstructionOrder` and `DestructionOrder` from the `ISimulationView` read buffer, the system never iterates over ECS chunks to discover new entities.
2. **Deterministic Garbage Collection:** Using `OnTeardown` tied to `DestructionOrder` guarantees that transient visualization state (like historic path arrays, color pulse timers, or dynamically allocated hitboxes) is cleanly returned to the object pool the exact frame the simulation tears down the entity.
3. **Strict Phase Discipline:** Because the system executes in `PostSimulation`, the `UpdateAndDraw` method is projecting the fully integrated kinematic state of the current frame (after `LinearKinematicsSystem` and `BallisticsSystem` have run), ensuring visual shapes never lag behind the actual physics state.

---

how can we make an interactive gizmo, once picked/focused, activate itself as a map tool to keep receiving events, and how we deactivate the map tool once it "loses focus" ? How can a gizmo loose focus? This must be either that gizmo itself deactivates itself (finished some interactive manipulation based on user input) or the user clicks somewhere else, possibly selecting another entity, picking another gizmo etc.



---

To seamlessly bridge the gap between backend-neutral, remote-authoritative gizmo logic and the local, highly responsive UI, we must leverage the State Pattern using the `MapCanvas`'s existing `IMapTool` stack.

The secret to clean architecture here is the **Proxy Pattern**. The presentation layer does not run the gizmo logic. Instead, when a user clicks a pickable debug primitive, the presentation layer pushes a generic `GizmoInteractionProxyTool` onto the canvas stack. This proxy tool captures local input focus, streams standard interaction events to the simulation kernel, and handles its own deactivation.

Here is the blueprint for how activation, event capturing, and focus-loss are managed gracefully.

### 1. Activation: Gaining Focus

The base map tool (e.g., `StandardInteractionTool` or a dedicated `GizmoHitTool`) is constantly performing hit-tests against the rendered `DebugPrimitive`s. When the user clicks a primitive that contains a valid `PickToken`, the base tool intercepts this and immediately pushes the proxy tool.

```
// Inside StandardInteractionTool or a dedicated hit-tester
public bool HandleClick(Vector2 worldPos, MouseButton button)
{
    if (button == MouseButton.Left)
    {
        // 1. Raycast against local debug primitives array
        if (TryPickGizmoPrimitive(worldPos, out PickToken token))
        {
            // 2. Immediately capture input focus by pushing the Proxy Tool
            _canvas.PushTool(new GizmoInteractionProxyTool(token, _eventBus));

            // 3. Notify the simulation kernel that an interaction began
            _eventBus.Publish(new GizmoInteractionStartedEvent { Token = token, StartPos = worldPos });
            return true; // Click consumed
        }

        // ... fallback to normal entity selection ...
    }
    return false;
}
```

### 2. The Active State: Streaming Events

Once `GizmoInteractionProxyTool` is on top of the `MapCanvas` stack [i], it has exclusive access to `HandleDrag`, `HandleHover`, and `HandleKeyPressed`. It acts as a "dumb terminal," blindly converting local `Raylib` screen coordinates into world-space protocol events and streaming them to the authoritative simulation node.

### 3. Deactivation: Losing Focus

A tool loses focus by calling `_canvas.PopTool()` [i]. There are three distinct ways a gizmo interaction can end, all handled cleanly within the proxy tool's `IMapTool` contract [i]:

1. **Gizmo Finishes (Self-Deactivation):** The most common interaction (like dragging a rotation handle) ends when the user releases the mouse.
2. **User Cancels:** The user presses the `Escape` key or Right-Clicks.
3. **User Clicks Away (Loss of Focus):** For "sticky" gizmos (like drawing a polygon where you click multiple times without holding the mouse), if the user clicks on the empty map or selects another entity entirely, the proxy tool must detect this miss, pop itself, and yield the click back to the canvas.

Here is the concrete implementation of the Proxy Tool managing these exits:

```
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D;
using Raylib_cs;

namespace Fdp.Toolkit.Diagnostics.Tools
{
    /// <summary>
    /// Generic presentation-tier proxy. Captures local input and streams
    /// backend-neutral events to the authoritative simulation gizmo.
    /// </summary>
    public sealed class GizmoInteractionProxyTool : IMapTool
    {
        public string Name => "GizmoInteractionProxy";

        private readonly PickToken _token;
        private readonly FdpEventBus _eventBus; // Or DDS Writer for network
        private MapCanvas _canvas;

        public GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus)
        {
            _token = token;
            _eventBus = eventBus;
        }

        public void OnEnter(MapCanvas canvas) => _canvas = canvas;
        public void OnExit() { _canvas = null; }
        public void Update(float dt) { }
        public void Draw(RenderContext ctx) { /* Local UI hints if necessary */ }

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            // Stream continuous updates to the simulation node
            _eventBus.Publish(new GizmoDragUpdateEvent { Token = _token, WorldPos = worldPos });
            return true; // Consume input so the map doesn't pan
        }

        public bool HandleHover(Vector2 worldPos) => true;

        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            // EXIT CONDITION 1: Self-Deactivation (End of Drag)
            if (button == MouseButton.Left && _canvas.Input.IsMouseButtonReleased(MouseButton.Left))
            {
                _eventBus.Publish(new GizmoInteractionCommitEvent { Token = _token, WorldPos = worldPos });
                _canvas.PopTool();
                return true;
            }

            // EXIT CONDITION 2 & 3: Right-Click Cancel or "Click Away"
            if (button == MouseButton.Right)
            {
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas.PopTool();
                return true;
            }

            // EXIT CONDITION 3 (Sticky Mode): User left-clicked somewhere else.
            // If we are waiting for a discrete click, we check if they clicked OUR primitive again.
            if (button == MouseButton.Left && _canvas.Input.IsMouseButtonPressed(MouseButton.Left))
            {
                if (!IsClickingMyPrimitive(worldPos))
                {
                    // User clicked empty space or another entity.
                    // Pop this tool, notify the server of cancellation, and return FALSE.
                    // Returning false tells the MapCanvas to pass this click down to
                    // the StandardInteractionTool beneath us, seamlessly selecting the new entity.
                    _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                    _canvas.PopTool();
                    return false;
                }
            }

            return true;
        }

        public bool HandleKeyPressed(KeyboardKey key)
        {
            // EXIT CONDITION 2: Explicit Escape
            if (key == KeyboardKey.Escape)
            {
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas.PopTool();
                return true;
            }
            return false;
        }

        private bool IsClickingMyPrimitive(Vector2 worldPos)
        {
            // Perform a local hit-test against the presentation layer's primitive array.
            // Return true ONLY if the primitive under the mouse shares this tool's PickToken.
            return true;
        }
    }
}
```

### Architectural Triumphs of this Pattern

1. **Network Ready:** By packaging the interaction into `GizmoInteractionStartedEvent` and `GizmoDragUpdateEvent`, this proxy runs on the local IG (Image Generator) client, meaning zero network latency for capturing input. The remote authoritative server does all the heavy simulation math and ECS mutations when the events arrive.
2. **Perfect Focus Hand-off:** By returning `false` on the "Click Away" condition, `MapCanvas` seamlessly drops the gizmo proxy and allows the `StandardInteractionTool` to evaluate the click in the exact same frame. This prevents the operator from having to double-click to select a new entity after interacting with a sticky gizmo.

----

Tying the allocation lifecycle of a debug object to a volatile user interaction—like selection state—is a classic architectural trap. Selection changes rapidly as operators click around or drag selection boxes. If we instantiate and destroy C# gizmo objects based on selection, we introduce object pooling churn, cache invalidation, and GC pressure purely for debug visualization, which violates the engine's strict performance mandates.

Instead, we must separate the **lifecycle** of the gizmo from the **execution condition** of the gizmo. Here is how we architect this cleanly, handling both stateless and stateful scenarios.

### 1. The Ideal Path: Stateless Dispatch (Zero Instantiation)

If the gizmo is purely rendering and holds no historical or ephemeral state (e.g., drawing a simple bounding box or a velocity vector), **it should not be instantiated per entity at all**.

We reuse the Dispatcher Pattern. The gizmo is just a stateless struct or static method. A central system queries the ECS for entities that meet the conditions (e.g., have the required components AND are selected) and invokes the drawing logic.

The FDP engine's `EntityQuery` is SIMD-accelerated using `BitMask256`. We can query for `SelectionState` and the target components in $O(1)$ chunk time.

```
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class StatelessGizmoRenderSystem : IEcsModuleSystem
{
    private readonly IDebugDrawBuilder _drawBuilder;
    private readonly DebugSettings _settings; // Contains global toggles

    public void Execute(ISimulationView view, float deltaTime)
    {
        // Chunk-filtered query for entities with our target component (e.g., SimVelocity)
        var query = view.Query()
            .With<SimVelocity>()
            .With<SimTransform>()
            .Build();

        bool isGloballyForced = _settings.ShowVelocityGlobally;

        foreach (var entity in query)
        {
            // Fast rejection based on selection state, bypassing logic if not forced
            if (!isGloballyForced)
            {
                if (!view.HasComponent<SelectionState>(entity)) continue;
                ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
                if (!sel.IsSelected && !sel.IsPrimarySelection) continue; //
            }

            // Execute stateless drawing
            ref readonly var vel = ref view.GetComponentRO<SimVelocity>(entity);
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            DrawVelocity(tf.Position, vel.Linear, _drawBuilder);
        }
    }
}
```

### 2. The Stateful Path: Lifecycle Binding + Query Intercession

If the gizmo *must* hold state (e.g., a fading historical trail, an animation timer, or a cached mesh projection), we fall back to our `EntityGizmoManagerSystem` which binds instances to `ConstructionOrder` and `DestructionOrder`.

To prevent this manager from iterating 10,000 unselected gizmos every frame just to check an `if (isSelected)` flag, we invert the execution loop. Instead of iterating the dictionary of active gizmos, we **iterate the ECS query of selected entities** and look up their corresponding gizmos.

Here is the highly optimized execution phase for the manager:

```
public void Execute(ISimulationView view, float deltaTime)
{
    // ... (Handle ConstructionOrder / DestructionOrder to add/remove from _activeGizmos dictionary) ...

    bool forceAll = _settings.GlobalGizmosEnabled;

    if (forceAll)
    {
        // Global override: O(K) iteration over the dictionary
        foreach (var kvp in _activeGizmos)
        {
            if (view.IsAlive(kvp.Key))
            {
                foreach (var gizmo in kvp.Value)
                    gizmo.UpdateAndDraw(view, kvp.Key, deltaTime, _drawBuilder);
            }
        }
    }
    else
    {
        // Selection only: Use the SIMD-accelerated ECS query to find selected entities first
        var selectedQuery = view.Query()
            .With<SelectionState>()
            .Build();

        foreach (var entity in selectedQuery)
        {
            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
            if (!sel.IsSelected && !sel.IsPrimarySelection) continue; //

            // O(1) dictionary lookup ONLY for entities we know are selected
            if (_activeGizmos.TryGetValue(entity, out var gizmos))
            {
                foreach (var gizmo in gizmos)
                    gizmo.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
            }
        }
    }
}
```

### Why this is the correct architecture:

1. **Memory Stability:** By tying stateful gizmo instances to `ConstructionOrder` / `DestructionOrder`, memory allocation is completely predictable. We avoid thrashing object pools every time the user drag-selects a box of 500 units.
2. **Cache Locality:** Polling `Dictionary<Entity, Gizmo>` and looking up `SelectionState` in the ECS is a cache miss factory. By iterating the `EntityQuery` first, we leverage the ECS's contiguous memory layout. We only pay the dictionary lookup cost for the small handful of entities that are actually selected.
3. **Graceful Degradation:** If the user flips a setting to "Show All", the system seamlessly switches to iterating the dictionary directly, bypassing the selection query entirely.

---

i do not like having any special system that magically knows what component the entity needs to have in order to be eligible for that gizmo. We can have many types of gizmos, and they should not need a specialized system each. They could express their need for some entity components, and there could be  single generic data driven system that decides what to draw for what entity. It might optimize/cache to avoid some heavy checks every frame.

---

Your instinct to move toward a purely data-driven, declarative architecture is exactly how modern, high-performance engines handle subsystem scaling. Hardcoding a manager to magically know about specific `TkbType` IDs or specific behaviors violates the Open-Closed Principle and creates a maintenance bottleneck.

By allowing gizmos to declare their component requirements (e.g., "I need `SimTransform` and `WeaponState`"), we can build a single `DataDrivenGizmoSystem` that acts as a generic orchestrator. It pre-compiles these requirements into SIMD-friendly bitmasks at application startup, caches the instantiations using the engine's lifecycle events, and executes in strictly $O(K)$ time (where $K$ is the number of active gizmo instances).

Here is the clean architectural blueprint for this system using the Fast Data Plane (FDP) engine's existing paradigms.

### 1. The Data-Driven Gizmo Definition

First, we define a declarative contract. A gizmo defines the components it requires to function, and whether it should only be rendered when the entity is selected. It acts as a factory for the ephemeral, stateful instance.

```
using System;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Visuals;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public interface IGizmoDefinition
    {
        /// <summary>The ECS components this gizmo requires to function.</summary>
        Type[] RequiredComponents { get; }

        /// <summary>If true, the generic system skips drawing unless the entity is selected.</summary>
        bool RequiresSelection { get; }

        /// <summary>Factory method to create the stateful instance.</summary>
        IStatefulGizmo CreateInstance();
    }

    public interface IStatefulGizmo
    {
        void OnInitialize(ISimulationView view, Entity entity);
        void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder drawBuilder);
        void OnTeardown();
    }
}
```

### 2. Startup Compilation (The Registry)

When the application boots, we scan for all `IGizmoDefinition` implementations. We convert their `Type[]` requirements into the engine's AVX2-accelerated `BitMask256`. This means evaluating an entity against 100 different gizmo rules takes just a few bitwise instructions, completely allocation-free.

```
    internal struct CompiledGizmoRule
    {
        public IGizmoDefinition Definition;
        public BitMask256 RequiredMask;
    }

    public class GizmoRegistry
    {
        internal readonly List<CompiledGizmoRule> Rules = new();

        public void Register(IGizmoDefinition definition)
        {
            var mask = new BitMask256();
            foreach (var type in definition.RequiredComponents)
            {
                int typeId = ComponentTypeRegistry.GetId(type);
                if (typeId >= 0) mask.SetBit(typeId);
            }
            Rules.Add(new CompiledGizmoRule { Definition = definition, RequiredMask = mask });
        }
    }
```

### 3. The Single Generic System (Cache & Execute)

We write *exactly one* ECS system to manage every entity-bound gizmo in the application.

To avoid evaluating rules every frame, this system leverages the `EntityLifecycleModule` (`ConstructionOrder` / `DestructionOrder`) to populate a cache. To handle components added dynamically *after* spawn, we also leverage the `EntityHeader.LastChangeTick`.

```
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.IG.Components; // SelectionState

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class DataDrivenGizmoSystem : IEcsModuleSystem
    {
        private readonly GizmoRegistry _registry;
        private readonly IDebugDrawBuilder _drawBuilder;

        // Cache: Entity -> Active Gizmo Instances
        private readonly Dictionary<Entity, List<CompiledGizmoInstance>> _activeGizmos = new();
        private uint _lastScanTick = 0;

        private struct CompiledGizmoInstance
        {
            public IStatefulGizmo Instance;
            public bool RequiresSelection;
        }

        public DataDrivenGizmoSystem(GizmoRegistry registry, IDebugDrawBuilder drawBuilder)
        {
            _registry = registry;
            _drawBuilder = drawBuilder;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;

            // 1. TEARDOWN: Clean up destroyed entities
            foreach (ref readonly var evt in view.ReadEvents<DestructionOrder>())
            {
                if (_activeGizmos.Remove(evt.Entity, out var instances))
                {
                    foreach (var gizmo in instances) gizmo.Instance.OnTeardown();
                }
            }

            // 2. SETUP: Evaluate new entities using SIMD bitmasks
            foreach (ref readonly var evt in view.ReadEvents<ConstructionOrder>())
            {
                EvaluateEntity(repo, evt.Entity);
            }

            // Optional 2b: Handle dynamic component additions mid-flight using structural versions
            // (Iterate chunks where LastChangeTick > _lastScanTick and re-evaluate)

            // 3. EXECUTE: Strictly O(K) rendering over the cache
            foreach (var kvp in _activeGizmos)
            {
                var entity = kvp.Key;
                if (!view.IsAlive(entity)) continue;

                // Cache the selection check once per entity, not per gizmo
                bool isSelected = false;
                bool selectionEvaluated = false;

                foreach (var gizmo in kvp.Value)
                {
                    if (gizmo.RequiresSelection)
                    {
                        if (!selectionEvaluated)
                        {
                            isSelected = view.HasComponent<SelectionState>(entity) &&
                                         view.GetComponentRO<SelectionState>(entity).IsSelected;
                            selectionEvaluated = true;
                        }
                        if (!isSelected) continue;
                    }

                    gizmo.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
                }
            }

            _lastScanTick = view.Tick;
        }

        private void EvaluateEntity(EntityRepository repo, Entity entity)
        {
            ref var header = ref repo.GetHeader(entity.Index);
            List<CompiledGizmoInstance>? instances = null;

            foreach (var rule in _registry.Rules)
            {
                // AVX2 SIMD evaluation: Does this entity have all required components?
                if (BitMask256.HasAll(header.ComponentMask, rule.RequiredMask))
                {
                    instances ??= new List<CompiledGizmoInstance>();
                    var gizmo = rule.Definition.CreateInstance();
                    gizmo.OnInitialize(repo, entity);

                    instances.Add(new CompiledGizmoInstance
                    {
                        Instance = gizmo,
                        RequiresSelection = rule.Definition.RequiresSelection
                    });
                }
            }

            if (instances != null)
            {
                _activeGizmos[entity] = instances;
            }
        }
    }
}
```

### Architectural Triumphs of this Pattern

1. **Perfect Decoupling (Open-Closed Principle):** A developer adding a new `LaserGuidanceGizmo` just creates an `IGizmoDefinition` requiring `WeaponState` and `SimTransform`. The core `DataDrivenGizmoSystem` never changes. It automatically discovers the rule and applies it.
2. **Zero Polling Overhead:** By driving instantiation strictly through `ConstructionOrder` events and structural ticks, we never execute heavy `if (HasComponent<T>)` checks during the hot loop.
3. **Data-Driven Visibility:** Moving `RequiresSelection` out of the gizmo's logic and into the definition metadata allows the orchestrator to perform the `SelectionState` ECS lookup exactly *once* per entity, reusing the boolean for multiple attached gizmos, and bypassing the draw logic entirely if the condition fails.
4. **$O(K)$ Execution:** The hot path iteration loops exclusively over `_activeGizmos`. If you have 50,000 entities in the world but only 10 meet the criteria for a specific gizmo, the system only touches those 10.

----

the conditions for gizmo 'unhiding' like 'render only if entity selected' could be generic, gizmo type defined, taking entity repository to get access to entity states including the singletons where we could store some global setting etc,



---

Replacing a hardcoded boolean like `RequiresSelection` with a generic, policy-driven visibility contract is a massive architectural upgrade. It completely adheres to the Open-Closed Principle: the core `DataDrivenGizmoSystem` orchestrator never needs to be modified when a new tool introduces complex visibility requirements, such as checking global engine singletons or evaluating custom hierarchical TKB data.

To implement this cleanly without sacrificing the strict performance requirements of our ECS hot paths, we must abstract the visibility rules using the **Strategy Pattern**. Crucially, we must split the evaluation into two distinct phases to prevent cache-thrashing: global singleton evaluation (executed once per frame) and per-entity evaluation (executed inside the chunk iteration).

Here is the architectural blueprint for generic, type-defined visibility policies using the engine's `ISimulationView` abstraction.

### 1. The Visibility Policy Contract

Instead of the gizmo hardcoding its checks, it provides an `IGizmoVisibilityPolicy`. This policy uses `ISimulationView`, which is the safe, read-only abstraction over `EntityRepository` designed specifically for deterministic module execution.

```
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Defines dynamic visibility rules for a specific type of Gizmo.
    /// </summary>
    public interface IGizmoVisibilityPolicy
    {
        /// <summary>
        /// Evaluated ONCE per frame before entity iteration.
        /// Ideal for checking global ECS singletons (e.g. Master Debug Toggles).
        /// If this returns true, the entity-level check can be bypassed.
        /// </summary>
        bool IsGloballyEnabled(ISimulationView view);

        /// <summary>
        /// Evaluated per-entity.
        /// Ideal for checking SelectionState, Health levels, or specific component data.
        /// </summary>
        bool IsEntityVisible(ISimulationView view, Entity entity);
    }
}
```

### 2. A Concrete Policy Implementation

Let’s implement your exact example: "Render if the entity is selected OR if this specific gizmo type is globally forced on."

We assume a global singleton, `GlobalDebugSettings`, is stored in the ECS via `SetSingleton<T>`.

```
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.IG.Components; // Contains SelectionState

namespace Fdp.Toolkit.Diagnostics.Policies
{
    public sealed class SelectedOrGloballyForcedPolicy : IGizmoVisibilityPolicy
    {
        public bool IsGloballyEnabled(ISimulationView view)
        {
            // O(1) check against a global ECS singleton
            if (view is EntityRepository repo && repo.HasSingleton<GlobalDebugSettings>()) //
            {
                ref readonly var settings = ref repo.GetSingleton<GlobalDebugSettings>(); //
                return settings.ForceAllBehaviorGizmosVisible;
            }
            return false;
        }

        public bool IsEntityVisible(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<SelectionState>(entity)) return false; //

            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity); //

            // Unhide if the entity is part of the current operator selection
            return sel.IsSelected || sel.IsPrimarySelection;
        }
    }
}
```

### 3. Integrating with the Gizmo Definition

The `IGizmoDefinition` now simply exposes this policy:

```
    public interface IGizmoDefinition
    {
        Type[] RequiredComponents { get; }

        // The orchestrator queries this policy to determine visibility
        IGizmoVisibilityPolicy VisibilityPolicy { get; }

        IStatefulGizmo CreateInstance();
    }
```

### 4. The High-Performance Orchestrator Execution

Inside our `DataDrivenGizmoSystem`, we update the `Execute` loop to leverage this two-tier evaluation. Notice how we hoist the `IsGloballyEnabled` check outside the inner loop to guarantee cache locality and avoid repetitive singleton lookups.

```
        public void Execute(ISimulationView view, float deltaTime)
        {
            // ... (Setup and teardown logic omitted for brevity) ...

            // Pre-evaluate global rules for all registered gizmo definitions ONCE
            bool[] globalVisibilityCache = new bool[_registry.Rules.Count];
            for (int i = 0; i < _registry.Rules.Count; i++)
            {
                globalVisibilityCache[i] = _registry.Rules[i].Definition.VisibilityPolicy.IsGloballyEnabled(view);
            }

            // Execute: Strictly O(K) rendering over the active cache
            foreach (var kvp in _activeGizmos)
            {
                var entity = kvp.Key;
                if (!view.IsAlive(entity)) continue; //

                foreach (var compiledGizmo in kvp.Value)
                {
                    var policy = compiledGizmo.Definition.VisibilityPolicy;
                    bool isVisible = globalVisibilityCache[compiledGizmo.RuleIndex];

                    // Only perform the per-entity ECS component checks if not already globally forced
                    if (!isVisible)
                    {
                        isVisible = policy.IsEntityVisible(view, entity);
                    }

                    if (isVisible)
                    {
                        compiledGizmo.Instance.UpdateAndDraw(view, entity, deltaTime, _drawBuilder);
                    }
                }
            }
        }
```

### Architectural Triumphs of this Pattern

1. **Elimination of Global Pollutants:** The `DataDrivenGizmoSystem` no longer references `Hrot.IG.Components.SelectionState` or any specific singleton type directly. It is completely isolated in the generic `Fdp.Toolkit.Diagnostics` tier.
2. **Branch Prediction and Cache Locality:** Hoisting the `IsGloballyEnabled` evaluation into an array before the entity iteration prevents hundreds of redundant `HasSingleton<T>` lookups per frame.
3. **Infinite Extensibility:** A developer can write a `ShowWhenHealthCriticalPolicy` that queries `view.GetComponentRO<Health>` or a `ShowWhenUnderAttackPolicy` that queries `TargetMemory` without ever modifying the core rendering orchestrator. The condition is entirely encapsulated by the gizmo type.

------

Based on the architectural foundation we have laid down in our discussion, we have cleanly separated the concerns of lifecycle, presentation, and input routing. Because of this decoupled design, "gizmo types" are no longer monolithic, hardcoded scripts. Instead, they are defined along several orthogonal architectural axes: **Statefulness**, **Interactivity**, **Scope**, and **Visibility**.

Here is the recap of the gizmo types and execution patterns we have established:

### 1. By Statefulness & Lifecycle (Memory Management)

How the gizmo manages memory and integrates with the engine's execution loop.

- **Stateless Gizmos (Rendering Only):**
  - These are pure, data-driven projectors. They hold no memory and are not instantiated as objects.
  - They run in a generic, SIMD-accelerated dispatcher system that executes $O(1)$ ECS queries (e.g., querying for entities with `SimVelocity`).
  - They read the data and immediately emit backend-neutral `DebugPrimitive` structs to the accumulation buffer.
- **Stateful (Ephemeral) Gizmos:**
  - These are instantiated C# objects (rented from a zero-allocation pool) that persist across frames to hold transient presentation state (e.g., historic trails, animation timers for pulsing colours) without polluting the ECS chunks.
  - Their lifecycle is strictly event-driven: they are created upon `ConstructionOrder` or `AssignBehaviorEvent` and safely returned to the pool upon `DestructionOrder` or `ClearBehaviorEvent`.
  - They conform to contracts like `IStatefulGizmo` (`OnInitialize`, `UpdateAndDraw`, `OnTeardown`).

### 2. By Interactivity & Input Capture

How the gizmo interacts with the operator and the presentation layer.

- **Passive Gizmos:**
  - Purely decorative visualizers. They emit primitives with a `PickToken` target set to `Entity.Null`, meaning the presentation layer's hit-testing will ignore them.
- **Interactive (Focusable) Gizmos:**
  - These emit primitives embedded with a valid stateless `PickToken` (containing the exact `Entity` handle and a `SubElementId` for granular picking, like a specific waypoint).
  - When picked by the user, they utilize the **Proxy Pattern**. The local or remote presentation client pushes a generic `GizmoInteractionProxyTool` onto its `MapCanvas` tool stack to capture exclusive input focus.
  - The proxy streams backend-neutral tool events (e.g., `GizmoDragUpdateEvent`, `GizmoInteractionCommitEvent`) back to the simulation node.
  - The authoritative simulation kernel processes these events and safely executes structural mutations via the deferred `IEntityCommandBuffer`.

### 3. By Attachment Scope

What triggers the gizmo's evaluation.

- **Global Gizmos:**
  - Entity-independent tools that render world-level debug data (e.g., spatial hash grids, navigation meshes, or global bounding volumes).
- **Entity-Bound Gizmos:**
  - Registered against specific `BlueprintId` values or ECS `ComponentMask` signatures. Evaluated for all entities that match the blueprint or possess the required components (e.g., a "suspension visualizer" for all vehicles).
- **Behavior-Bound Gizmos:**
  - Strictly tied to the active AI behavior. Evaluated using the Dispatcher Pattern mapped to the `ActiveBehaviorHash`, projecting heavy cognitive components (like `Blackboard1024`) into domain-specific tactical overlays (e.g., the `PlatoonHillAttack` firing lines).

### 4. By Visibility Policy

When the gizmo is allowed to emit primitives to the renderer.

- **Policy-Driven Gizmos:**
  - Rather than hardcoding `if (isSelected)`, gizmos declare an `IGizmoVisibilityPolicy`.
  - The central orchestrator evaluates a two-tiered check: first checking a global singleton (e.g., "Force All Velocity Vectors On") to maximize branch prediction, and if false, checking the per-entity condition (e.g., "Show only if `SelectionState` is true" or "Show only if `Health.Current` < 20%").

By structuring the toolkit this way, we have achieved a highly modular, $O(K)$ scaling architecture where a new debug tool simply declares its scope, its required components, its statefulness, and its visibility rules, while the generic pipeline handles all the heavy lifting of ECS querying, input capture, and network transport safely.



------

gizmost should be able to add colored text lines (control characters to switch text color) to the entity label



----

To implement colored debug text lines appended to an entity's label, we must establish a strict architectural boundary. You must not allow debug gizmos to mutate the simulation's actual presentation components (such as `EntityInfo.Name` or `ResolvedStyle`).

The `ResolvedStyle` component is heavily optimized using fixed-byte buffers (`_labelText`) and is explicitly padded to remain strictly under the `MaxStyleBytes = 64` cache-line ceiling to guarantee high-performance iteration. Injecting dynamically formatted, multi-line debug strings packed with control characters into this component would instantly rupture the cache layout and corrupt the presentation tier.

Instead, we treat these colored lines as **Entity Badges**—ephemeral, declarative primitives emitted by the gizmo, transported over the network, and aggregated visually by the presentation layer (Image Generator) below the entity's true label.

Here is the clean architecture approach to implementing control-character rich text for entity labels.

### 1. The Declarative Emission API

We extend our `IDebugDrawBuilder` contract so gizmos can emit text specifically bound to an entity, rather than a raw world coordinate. To remain allocation-free on the hot path and network-ready, we utilize the engine's existing `FixedString64` struct.

We define a simple control-character protocol. For example, using the `\f` (form feed) byte followed by an RGB hex char, or a simple enum mapping (e.g., `\x01` = Red, `\x02` = Green).

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    public static class RichTextColors
    {
        public const char Red = '\x01';
        public const char Green = '\x02';
        public const char Yellow = '\x03';
        public const char Default = '\x04';
    }

    public interface IDebugDrawBuilder
    {
        // ... existing methods ...

        /// <summary>
        /// Appends a rich-text debug badge below the target entity's main label.
        /// Evaluated and stacked by the presentation layer.
        /// </summary>
        void DrawEntityBadge(Entity target, FixedString64 richText);
    }
}
```

A behavior gizmo can now construct a rich string completely on the stack:

```
// Inside a gizmo's UpdateAndDraw method:
var text = new FixedString64($"{RichTextColors.Yellow}State:{RichTextColors.Default} Flanking {RichTextColors.Red}HP Low");
drawBuilder.DrawEntityBadge(entity, text);
```

### 2. Presentation-Tier Aggregation

Because the actual map label drawing occurs inside the presentation tier (e.g., `NedVisualizerAdapter.Render` evaluating `ResolvedStyle` and drawing at `position.Y + LabelOffsetPx`), the presentation proxy must aggregate these badge primitives.

When the local `MapCanvas` or remote 3D client receives the `DebugPrimitive` array, it groups any badge primitives by their `PickToken.Target` entity.

### 3. Zero-Allocation Rich Text Parsing

Raylib's default `DrawText` does not natively support inline color switching. We must build a custom `DrawRichText` method in the presentation tier.

To maintain our zero-allocation mandates (no `string.Split` or substring allocations), we apply the exact same span-slicing pattern used by the engine's `LogSyntaxHighlighter`. We iterate the bytes of the `FixedString64`, break them into chunks when a control character is encountered, and advance the X-coordinate cursor.

```
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Rendering
{
    public static class RichTextRenderer
    {
        public static unsafe void DrawRichTextBadge(
            ref FixedString64 text,
            Vector2 startPos,
            int fontSize)
        {
            // Extract the raw byte span for zero-allocation parsing
            ref byte start = ref Unsafe.As<FixedString64, byte>(ref text);
            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpan(ref start, text.Length); //

            int chunkStart = 0;
            Vector2 cursor = startPos;
            Color currentColor = Color.White;

            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];
                if (b <= 0x04) // It's a control character
                {
                    // 1. Flush the current chunk to the screen
                    if (i > chunkStart)
                    {
                        cursor.X += DrawSpan(span.Slice(chunkStart, i - chunkStart), cursor, fontSize, currentColor);
                    }

                    // 2. Switch the active color
                    currentColor = b switch
                    {
                        0x01 => Color.Red,
                        0x02 => Color.Green,
                        0x03 => Color.Yellow,
                        _    => Color.White
                    };

                    chunkStart = i + 1;
                }
            }

            // Flush the final chunk
            if (chunkStart < span.Length)
            {
                DrawSpan(span.Slice(chunkStart), cursor, fontSize, currentColor);
            }
        }

        private static unsafe float DrawSpan(ReadOnlySpan<byte> text, Vector2 pos, int fontSize, Color color)
        {
            // In a real implementation, you would use Raylib.DrawTextCodepoints or
            // stackalloc a null-terminated buffer to pass to Raylib.DrawTextEx
            string chunk = System.Text.Encoding.UTF8.GetString(text);
            Raylib.DrawText(chunk, (int)pos.X, (int)pos.Y, fontSize, color);

            return Raylib.MeasureText(chunk, fontSize);
        }
    }
}
```

### Architectural Triumphs of this Pattern

1. **Pristine Simulation State:** By utilizing an out-of-band `DrawEntityBadge` command, the gizmo does not corrupt the strict 64-byte layout of the `ResolvedStyle` component, preserving cache locality for the core rendering pipeline.
2. **Network Decoupling:** The heavy lifting of converting simulation state into formatted text runs entirely on the authoritative backend node. The `FixedString64` bytes are serialized flawlessly over CycloneDDS.
3. **Deterministic Stacking:** The Image Generator (IG) receives all badge primitives, renders the core entity label from the `ResolvedStyle`, and then loops through the received badges, applying a `Y` offset per line to stack the colored debug text neatly below the unit.

---

Gizmos should work in 2d as well as 3d. Depends to what pipeline (what kind of of api instance - 2d or 3d) the instructions are added . One gizmo can handle both at the same time.

Designing the system so a single gizmo seamlessly handles both 2D and 3D presentation pipelines simultaneously is the holy grail of decoupled visualization. To do this correctly—especially in a distributed, network-ready engine like Fast Data Plane—we must adhere to the principle of **Evaluate Once, Present Anywhere**.

If we were to pass separate `I2DDebugBuilder` and `I3DDebugBuilder` API instances to the gizmo, we would force the simulation node to serialize and transport two separate streams of data over the network, duplicating memory and bandwidth.

Instead, the cleanest architecture is to maintain a single `IDebugDrawBuilder` that accumulates a unified, dimensionally-agnostic stream of primitives, but we introduce a **Pipeline Discriminator** into the primitive header.

Here is the architectural blueprint for handling 2D and 3D concurrently from a single gizmo execution.

1\. The Pipeline Target Mask

We update our flat, blittable `DebugPrimitive` struct to include a bitmask indicating which presentation pipelines should consume the instruction.

```
using System;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    [Flags]
    public enum PipelineTarget : byte
    {
        None       = 0,
        Map2D      = 1 << 0, // Tactical orthographic/perspective map
        Viewport3D = 1 << 1, // Full 3D camera viewport
        All        = Map2D | Viewport3D
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DebugPrimitive
    {
        [FieldOffset(0)] public DebugPrimitiveShape Shape;
        [FieldOffset(1)] public CoordinateSpace Space;
        [FieldOffset(2)] public PipelineTarget TargetView; // <-- The discriminator
        // ... color, thickness, and union payload ...
    }
}
```

2\. The Targeted Emission API

The `IDebugDrawBuilder` is extended so that the gizmo can easily target a specific pipeline or emit universally. By defaulting to `PipelineTarget.All`, a single method call instantly serves both renderers.

```
public interface IDebugDrawBuilder
{
    void DrawSphere(
        Vector3 center, 
        float radius, 
        Color32 color, 
        PipelineTarget target = PipelineTarget.All); // Default applies to both
        
    void DrawEntityBadge(
        Entity targetEntity, 
        FixedString64 richText, 
        PipelineTarget target = PipelineTarget.All);
}
```

Now, a single behavior gizmo can intelligently split its output without running two separate execution loops:

```
public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder builder)
{
    ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

    // 1. Emitted to BOTH pipelines automatically:
    builder.DrawSphere(tf.Position, radius: 50f, Colors.Red); 
    
    // 2. Emitted ONLY to the 2D Tactical Map:
    // (e.g., a flat tactical boundary that looks bad floating in 3D space)
    builder.DrawLine(posA, posB, Colors.Blue, target: PipelineTarget.Map2D);
    
    // 3. Emitted ONLY to the 3D Viewport:
    // (e.g., a volumetric sight-line cone)
    builder.DrawCone(tf.Position, fov, Colors.Yellow, target: PipelineTarget.Viewport3D);
}
```

3\. Pipeline Interpretation (The "Dumb Terminals")

Because the gizmo logic runs exactly once on the authoritative node, it produces a single array of `DebugPrimitive` structs. This array is serialized over CycloneDDS to the Image Generator (IG) node.

The IG node might have multiple windows open: a `MapCanvas` (2D) and a Stride engine viewport (3D). Each presentation layer simply filters the stream and interprets the backend-neutral primitives according to its own dimensional rules.

The 2D Pipeline (`MapCanvas`)

The 2D map iterates the array, completely ignoring any primitive where `(TargetView & PipelineTarget.Map2D) == 0`.

When it encounters a `DrawSphere` instruction, it projects the 3D world coordinates down to the 2D plane. The engine already has a highly optimized `PerspectiveShapeRenderer` which uses the entity's Z-altitude to apply an exaggerated perspective distortion (scaling the 2D footprint up or down based on elevation). The 3D sphere is cleanly interpreted as a flat, parallax-shifted 2D circle on the map.

The 3D Pipeline (`Viewport3D`)

Simultaneously, the 3D renderer iterates the exact same array, ignoring anything without `PipelineTarget.Viewport3D`.

When it encounters the exact same `DrawSphere` instruction, it passes the Cartesian `Vector3` payload directly to the 3D graphics API (e.g., drawing a volumetric wireframe sphere using Stride's debug renderer).

Architectural Triumphs of this Design

-   **CPU Efficiency:** If you have 5,000 active AI behaviors, their visualization logic evaluates the `BrainBlackboard` and ECS state exactly once per frame, completely oblivious to how many UI windows or remote clients are observing them.-   **Network Efficiency:** By bundling 2D and 3D instructions into a single `DebugPrimitivesBatch` DDS topic, we cut network transmission overhead in half.-   **Perfect Consistency:** Because both the 2D map and the 3D viewport derive their graphics from the exact same structural payload processed in the exact same kernel tick, it is impossible for the 2D tactical view to show a unit's path desynchronized from its 3D avatar.

I need to support alpha blended primitives with gradient, like a line with different transparency on one end and other end, interpolating smoothly along the line.

To support gradient alpha-blending across primitives without breaking our strict cache-locality and zero-allocation mandates, we must enhance the payload of our 64-byte tagged union rather than creating entirely new primitive types.

By embedding the secondary color directly into the specific primitive's payload space, we preserve the backend-neutrality of the simulation tier. The simulation simply declares the intent of a gradient, and the presentation tier handles the hardware-specific vertex generation.

Here is the architectural implementation.

1\. Extending the Blittable Union Payload

We already established a 16-byte header for `DebugPrimitive` that includes a primary `Color32`. For a gradient line, this primary color acts as the `StartColor`. We then utilize the remaining 48 bytes of the union payload to store the `EndColor`.

```
using System.Numerics;
using System.Runtime.InteropServices;
using Hrot.IG.Components; // Color32 [1]

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DebugPrimitive
    {
        // ── Header (16 bytes) ──────────────────────────────────────────────────
        [FieldOffset(0)] public DebugPrimitiveShape Shape;
        [FieldOffset(1)] public CoordinateSpace Space;
        [FieldOffset(2)] public Color32 Color; // Acts as StartColor for gradients [1]
        [FieldOffset(6)] public PipelineTarget TargetView; 
        [FieldOffset(7)] public byte Padding; // Explicit alignment padding
        [FieldOffset(8)] public float Thickness;
        [FieldOffset(12)] public float LifetimeSeconds;

        // ── Payload Union (48 bytes max) ───────────────────────────────────────
    
        // LINE PAYLOAD
        [FieldOffset(16)] public Vector3 LineStart;
        [FieldOffset(28)] public Vector3 LineEnd;
        [FieldOffset(40)] public Color32 LineEndColor; // Gradient target [1]
        
        // ... (other payloads remain aligned at offset 16)
    }
}
```

This payload fits perfectly within our 64-byte cache line ceiling, meaning it remains completely safe for `NativeChunkTable` storage and transport over CycloneDDS.

2\. The Accumulation API

We expand the `IDebugDrawBuilder` contract with a specific gradient overload. If a developer uses the standard `DrawLine`, the builder simply sets `LineEndColor` equal to the primary `Color`, indicating a solid fill.

```
public interface IDebugDrawBuilder
{
    // Existing solid line
    void DrawLine(Vector3 start, Vector3 end, Color32 color, float thickness = 1f, PipelineTarget target = PipelineTarget.All);

    // New gradient line
    void DrawLineGradient(
        Vector3 start, 
        Vector3 end, 
        Color32 startColor, 
        Color32 endColor, 
        float thickness = 1f, 
        PipelineTarget target = PipelineTarget.All);
}
```

3\. Presentation-Tier Synthesis (The "Dumb Terminal")

This is where the separation of concerns shines. The authoritative simulation node does no geometry math. It serializes the `DebugPrimitive` and broadcasts it to the Image Generator (IG) or 2D Map Canvas.

When the presentation adapter iterates the primitive array, it inspects the colors. Standard functions like `Raylib.DrawLineEx` or `Raylib.DrawTriangle` accept a single solid color. To render the gradient, the presentation tier drops down to the immediate-mode graphics API (e.g., `rlgl` in Raylib, or a dynamic mesh buffer in Stride) to emit a generated quad (two triangles) with explicit vertex colors.

```
// Inside the presentation tier's DebugRenderAdapter:
if (primitive.Color.Equals(primitive.LineEndColor))
{
    // Fast path: solid line using the standard Raylib API [2]
    Raylib.DrawLineEx(start2D, end2D, primitive.Thickness, primitive.Color);
}
else
{
    // Gradient path: synthesize a textured quad using rlgl
    // 1. Calculate the 2D normal of the line
    // 2. Extrude start2D and end2D by (Thickness / 2) along the normal
    // 3. Push 4 vertices. The two start vertices use primitive.Color; 
    //    the two end vertices use primitive.LineEndColor.
    DrawThickLineGradient(start2D, end2D, primitive.Thickness, primitive.Color, primitive.LineEndColor);
}
```

Architectural Triumphs

-   **No New Primitive Types:** We did not add a `DebugPrimitiveShape.LineGradient` enum. A line is just a line. The renderer intrinsically handles the interpolation if the colors mismatch, keeping the switch-case dispatch logic small.-   **Zero Allocation Fading:** A behavior gizmo can easily draw a "fading" historical trajectory line simply by pushing a `DrawLineGradient` where `startColor.A = 255` and `endColor.A = 0`.-   **Decoupled Graphics Limitations:** We isolated the fact that Raylib lacks a native thick-gradient-line function exclusively to the presentation client. The simulation ECS remains pristine, purely expressing the debug _intent_.

How to approach global settings? Any gizmo should be able to read it or write it. It must be generic, not a fixed struct witk concrete fields. Key value. Fast read access (via the hash of the key). Each gizmo could register its own settings into a global shared dictionary. Saved to user settings.

To architect a generic, globally shared key-value configuration system that remains blazingly fast on the ECS hot path, we must ruthlessly avoid managed allocations (like `string` keys or boxed `object` values) during per-frame reads.

Using a hashed-key dictionary paired with a strongly-typed, unmanaged value union is the perfect approach. It gives gizmo developers the flexibility of a schema-less key-value store while maintaining the zero-allocation, cache-friendly constraints of the engine.

Here is the clean architectural blueprint for this subsystem.

1\. The Zero-Allocation Value Primitive (Tagged Union)

If we store values as `object` in our dictionary, every read will require unboxing, which generates garbage collection (GC) pressure and ruins cache locality. Instead, we mirror the engine's existing `AttributeValueUnion` by defining a purely blittable tagged union.

```
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    public enum SettingType : byte
    {
        Bool = 0,
        Int32 = 1,
        Float32 = 2
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct GizmoSettingValue
    {
        [FieldOffset(0)] public SettingType Type;
        
        [FieldOffset(4)] public bool BoolValue;
        [FieldOffset(4)] public int IntValue;
        [FieldOffset(4)] public float FloatValue;
    
        // Type-safe factories
        public static GizmoSettingValue Create(bool value) => new() { Type = SettingType.Bool, BoolValue = value };
        public static GizmoSettingValue Create(int value) => new() { Type = SettingType.Int32, IntValue = value };
        public static GizmoSettingValue Create(float value) => new() { Type = SettingType.Float32, FloatValue = value };
    }
}
```

2\. The Hashed Key-Value Registry

We create a `GizmoSettingsRegistry` that acts as the single source of truth. It stores settings in a `Dictionary<uint, GizmoSettingValue>` where the key is a 32-bit FNV-1a hash of the setting's string name (an algorithm already heavily utilized in the engine's hot paths).

We keep the original string names in a separate dictionary _only_ for serializing back to the user's JSON file.

```
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    /// <summary>
    /// Global registry for all ephemeral and persistent gizmo settings.
    /// Injected into the ECS world as a managed singleton.
    /// </summary>
    public sealed class GizmoSettingsRegistry
    {
        // Hot-path dictionary: strictly unmanaged keys and values
        private readonly Dictionary<uint, GizmoSettingValue> _activeSettings = new();
        
        // Tracking data for defaults and serialization (cold path)
        private readonly Dictionary<uint, GizmoSettingValue> _defaultSettings = new();
        private readonly Dictionary<uint, string> _hashToName = new();
    
        public void RegisterSetting(string keyName, GizmoSettingValue defaultValue)
        {
            uint hash = ComputeHash(keyName);
            _hashToName[hash] = keyName;
            _defaultSettings[hash] = defaultValue;
    
            // Only apply default if the user hasn't already loaded a saved override
            if (!_activeSettings.ContainsKey(hash))
            {
                _activeSettings[hash] = defaultValue;
            }
        }
    
        // ── Hot Path Accessors ──
    
        public GizmoSettingValue Read(uint keyHash)
        {
            return _activeSettings.TryGetValue(keyHash, out var val) ? val : default;
        }
    
        public void Write(uint keyHash, GizmoSettingValue value)
        {
            _activeSettings[keyHash] = value;
        }
    
        // ── Persistence (Cold Path) ──
    
        public void SaveToDisk(string filePath)
        {
            var overridesToSave = new Dictionary<string, object>();
    
            foreach (var kvp in _activeSettings)
            {
                // Only save values that deviate from the registered defaults
                if (!kvp.Value.Equals(_defaultSettings[kvp.Key]))
                {
                    string name = _hashToName[kvp.Key];
                    overridesToSave[name] = kvp.Value.Type switch
                    {
                        SettingType.Bool => kvp.Value.BoolValue,
                        SettingType.Int32 => kvp.Value.IntValue,
                        SettingType.Float32 => kvp.Value.FloatValue,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                }
            }
    
            var json = JsonSerializer.Serialize(overridesToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json); [3]
        }
    
        /// <summary>FNV-1a 32-bit hash</summary>
        public static uint ComputeHash(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name) { hash ^= c; hash *= 16777619; }
            return hash; [2]
        }
    }
}
```

3\. ECS Integration and Best Practices

At startup, this registry is created, loaded from the user's config file (e.g., `gizmo_settings.json`), and then injected into the ECS world as a managed singleton.

```
var registry = new GizmoSettingsRegistry();
// ... load JSON logic ...
repo.SetSingletonManaged(registry); [4]
```

**The Hoisting Rule (Critical Architecture Standard):** Even though reading from a `Dictionary<uint, GizmoSettingValue>` is extremely fast, doing it 10,000 times inside a tight chunk-iteration loop will still cause L1/L2 cache misses.

When a gizmo system executes, it must **hoist** its settings reads outside the entity iteration loop.

```
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class VehicleVelocityGizmoSystem : IEcsModuleSystem
    {
        private readonly IDebugDrawBuilder _drawBuilder;
        
        // Pre-compute the hash at construction time (zero runtime cost)
        private static readonly uint ShowVelocityHash = GizmoSettingsRegistry.ComputeHash("VehicleGizmo.ShowVelocity");
        private static readonly uint LineLengthHash = GizmoSettingsRegistry.ComputeHash("VehicleGizmo.LineLength");
    
        public void Execute(ISimulationView view, float deltaTime)
        {
            var registry = ((EntityRepository)view).GetSingleton<GizmoSettingsRegistry>(); [5]
    
            // 1. HOIST THE READS: Evaluate configuration ONCE per frame
            if (!registry.Read(ShowVelocityHash).BoolValue) 
                return; // Fast rejection
    
            float lengthMult = registry.Read(LineLengthHash).FloatValue;
    
            // 2. TIGHT LOOP: Iterate entities with zero dictionary lookups
            var query = view.Query().With<SimVelocity>().With<SimTransform>().Build();
            foreach (var entity in query)
            {
                ref readonly var vel = ref view.GetComponentRO<SimVelocity>(entity);
                ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
                
                var endPos = tf.Position + (vel.Linear * lengthMult);
                _drawBuilder.DrawLine(tf.Position, endPos, Colors.Green);
            }
        }
    }
```

Architectural Triumphs

-   **Zero-Allocation Hot Path:** Because the keys are hashed to `uint` at construction and the values are purely blittable structs (`GizmoSettingValue`), the registry allows fast configuration checks without touching the managed heap.-   **Infinite Extensibility:** A newly installed gizmo plugin can simply call `registry.RegisterSetting("MyNewTool.Thickness", GizmoSettingValue.Create(2.5f))` without requiring any hardcoded schema updates in the core engine.-   **Clean Workspaces:** By tracking `_defaultSettings` alongside `_activeSettings`, the `SaveToDisk` routine only serializes what the user actually modified, keeping the `gizmo_settings.json` file small and immune to configuration drift.

Settings change need to be communicated (event callback?) To gizmos soon change the gizmos know tto reread and reapply

In a strict ECS data-oriented engine, using traditional C# object-oriented callbacks (like `event Action OnSettingsChanged`) inside simulation or presentation systems is an architectural anti-pattern. It introduces hidden control flows, breaks deterministic execution phases, and risks cross-thread race conditions if a background module modifies a setting while a render system is iterating.

To handle setting changes cleanly, we must divide our approach based on **what the gizmo actually does with the setting**. We solve this using two patterns: **Hoisted Polling** for the 95% of standard cases, and **Deferred Event Invalidation** for the 5% that require heavy cache re-baking.

1\. The 95% Case: Hoisted Polling (No Events Needed)

If a setting merely controls line thickness, color, or a simple visibility toggle (`bool`), **you should not use events at all**.

Because we designed the `GizmoSettingsRegistry` to use O(1) unmanaged lookups via a pre-computed 32-bit `uint` hash, reading the setting is virtually free. You simply read the setting once at the top of your system's `Execute` method or your stateful gizmo's `UpdateAndDraw` method.

```
public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder builder)
{
    var registry = ((EntityRepository)view).GetSingletonManaged<GizmoSettingsRegistry>();
    
    // O(1) Hoisted read - practically zero CPU cost. No callbacks required.
    float thickness = registry.Read(ThicknessHash).FloatValue;
    
    // ... proceed to draw ...
}
```

This is the essence of immediate-mode, data-driven design. The gizmo naturally reacts to the setting change on the very next frame without any complex state-synchronization logic.

2\. The 5% Case: Deferred Event Invalidation (Heavy State)

If a stateful gizmo performs an expensive operation—like generating a complex polygon mesh, baking a text layout, or allocating a spatial grid—it _does_ need to know when a setting changes so it can invalidate its cache.

For this, we use the engine's existing `FdpEventBus`. When a setting is modified (e.g., via the operator's UI panel or an ExCon command), the mutation logic publishes a blittable struct event. The stateful gizmo then reads this event during its normal execution phase.

Step A: Define the Unmanaged Event

We define a purely blittable event struct to represent the setting mutation.

```
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    /// <summary>
    /// Published to the FdpEventBus whenever a gizmo setting is modified.
    /// </summary>
    [EventId(8050)] // Pick an available ID block for Diagnostics
    public struct GizmoSettingChangedEvent
    {
        public uint KeyHash;
    }
}
```

Step B: Publish on Mutation

Whenever the UI or the console updates the `GizmoSettingsRegistry`, it publishes the event to the bus.

```
// Inside your Configuration UI panel or console command handler:
registry.Write(MyHeavySettingHash, GizmoSettingValue.Create(newResolution));

// Notify the rest of the engine safely
var cmd = view.GetCommandBuffer();
cmd.PublishEvent(new GizmoSettingChangedEvent { KeyHash = MyHeavySettingHash }); 
```

Step C: The Gizmo Consumes the Event

During the `PostSimulation` phase, when the `DataDrivenGizmoSystem` iterates our stateful gizmos, the gizmo checks the event stream via `ISimulationView.ReadEvents<T>()`.

```
public sealed class ComplexNavMeshGizmo : IStatefulGizmo
{
    private static readonly uint ResolutionHash = GizmoSettingsRegistry.ComputeHash("NavMesh.Resolution");
    private bool _needsRebake = true;

    public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder builder)
    {
        // 1. Check for invalidation events without allocating
        var changeEvents = view.ReadEvents<GizmoSettingChangedEvent>(); //
        foreach (ref readonly var evt in changeEvents)
        {
            if (evt.KeyHash == ResolutionHash)
            {
                _needsRebake = true;
                break;
            }
        }
    
        // 2. Perform the heavy operation only if needed
        if (_needsRebake)
        {
            var registry = ((EntityRepository)view).GetSingletonManaged<GizmoSettingsRegistry>();
            int res = registry.Read(ResolutionHash).IntValue;
            BakeHeavyMesh(res);
            _needsRebake = false;
        }
    
        // 3. Emit primitives...
    }
}
```

Architectural Triumphs of this Pattern

-   **Thread Safety & Determinism:** We avoid injecting asynchronous UI-thread callbacks directly into the execution loops of gizmo objects. The event bus naturally buffers the change and exposes it cleanly during the correct ECS phase.-   **Perfect Cache Locality:** `view.ReadEvents<GizmoSettingChangedEvent>()` returns a contiguous memory span. Iterating it is blazing fast and generates zero GC allocations.-   **Decoupled Architecture:** The `GizmoSettingsRegistry` and the UI panels do not need to maintain a list of `IObserver` instances or weak references to ephemeral gizmo objects. The system remains strictly data-in, data-out.

Gizmos shpuld render into layers (1..15) to be easily filterrable.

Implementing categorized layers directly fulfills the "Debug layers/categories" requirement from our original architectural spec. It allows operators to toggle specific visualizations (e.g., AI paths, physics colliders, sensor cones) without having to disable the entire gizmo, while preserving our backend-neutral and zero-allocation principles.

To integrate this cleanly, we will utilize the exact same bitmask-filtering pattern that the engine's `MapCanvas` already uses for its `ActiveLayerMask`, but tailored for our declarative debug primitives.

Here is the architectural blueprint for adding 1..15 debug layers into the pipeline.

1\. Packing the Primitive Header

If you look at our `DebugPrimitive` memory layout from the previous design, we had exactly 1 byte of explicit alignment padding in the 16-byte header. We can replace that padding with a `byte DebugLayer` field, meaning this feature costs absolutely zero additional memory or network bandwidth.

```
using System.Runtime.InteropServices;
using Hrot.IG.Components; // Color32

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DebugPrimitive
    {
        // ── Header (16 bytes) ──────────────────────────────────────────────────
        [FieldOffset(0)] public DebugPrimitiveShape Shape;
        [FieldOffset(1)] public CoordinateSpace Space;
        [FieldOffset(2)] public Color32 Color; 
        [FieldOffset(6)] public PipelineTarget TargetView; 
        
        // REPLACED PADDING: Supports layers 0-255 (we will enforce 0-15 via API)
        [FieldOffset(7)] public byte DebugLayer; 
        
        [FieldOffset(8)] public float Thickness;
        [FieldOffset(12)] public float LifetimeSeconds;
    
        // ── Payload Union (48 bytes max) ───────────────────────────────────────
        [FieldOffset(16)] public Vector3 LineStart;
        // ...
    }
}
```

2\. Updating the Accumulation Contract

We update the `IDebugDrawBuilder` to accept a layer parameter. By defaulting to `0` (the base/uncategorized layer), existing gizmos do not need to be refactored, while advanced gizmos can route different primitives to different conceptual layers.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    public interface IDebugDrawBuilder
    {
        void DrawLine(
            Vector3 start, 
            Vector3 end, 
            Color32 color, 
            float thickness = 1f, 
            PipelineTarget target = PipelineTarget.All,
            byte debugLayer = 0); // 0 = Default, 1..15 = Categorized
    }
}
```

A behavior gizmo can now easily categorize its output:

```
// Inside PlatoonHillAttackGizmo:
_drawBuilder.DrawLine(baseStart, baseEnd, Colors.Blue, debugLayer: 1); // Layer 1: Tactical Lines
_drawBuilder.DrawSphere(slotPos, 2.0f, Colors.Green, debugLayer: 2);   // Layer 2: Slot Allocations
```

3\. High-Performance Filtering at the Presentation Tier

The filtering must happen on the "dumb terminal" (the `MapCanvas` or remote 3D client), completely decoupled from the simulation logic.

We add a `uint ActiveDebugLayerMask` to the client's local state or `RenderContext`, functioning identically to how `MapDisplayComponent.LayerMask` masks out standard game entities.

When the presentation adapter iterates the incoming batch of `DebugPrimitives`, it performs a blazingly fast SIMD or bitwise-AND rejection check:

```
// Inside the presentation tier's DebugRenderAdapter:
public void RenderPrimitives(ReadOnlySpan<DebugPrimitive> primitives, uint activeDebugLayerMask)
{
    foreach (ref readonly var prim in primitives)
    {
        // 1. Pipeline check (2D vs 3D)
        if ((prim.TargetView & PipelineTarget.Map2D) == 0) continue;

        // 2. Debug Layer check (O(1) bitwise culling)
        // Bit shift the layer index and check against the active mask.
        // A layer of 0 is always visible (1 << 0 = 1).
        if ((activeDebugLayerMask & (1U << prim.DebugLayer)) == 0) 
            continue; 
    
        // 3. Render
        DispatchShapeRender(in prim);
    }
}
```

Architectural Triumphs of this Pattern

-   **Network Bandwidth Conservation:** By adding the layer ID to the primitive rather than creating separate DDS topics for each layer, we keep the network transmission bundled into a single dense packet.-   **Immediate Client-Side Toggling:** Because the filter evaluates during the local render loop, the operator can toggle Layer 3 off in the UI, and the primitives instantly disappear from the screen on the exact same frame. There is no round-trip network latency to ask the server to stop sending Layer 3 data.-   **Pristine Cache Alignment:** By repurposing the existing 1-byte padding gap in the `DebugPrimitive` header, we maintain the strict 64-byte structural size required for zero-allocation `NativeChunkTable` insertion and cache-line perfection.

Gizmo framework could not be at all just a debug feature. Interactive gizmos can be used as native map tools. As specialized entity symbol renderers, as map grids etc. Gizmos should be aware of screen space, map zoom and pan etc.

Elevating the Gizmo framework from a mere debugging utility to the primary production presentation tier is a massive architectural paradigm shift, but it is the exactly correct evolution for a data-driven engine. By formalizing interactive gizmos as first-class map tools, entity visualizers, and dynamic grids, we completely unify the rendering and interaction pipelines.

However, achieving this while maintaining our strict backend-neutrality and network-transportability mandates that gizmos must become spatially aware of the operator's view without tightly coupling to the local `Raylib_cs` viewport.

Here is the architectural design for making gizmos viewport, zoom, and pan aware.

1\. Viewport Awareness via ECS State

If a remote authoritative node executes the gizmo logic, it cannot query the window size directly. Instead, we leverage the engine's existing `MapCameraViewport` concept.

The presentation client continuously updates a `MapCameraView` state—containing `Target`, `Offset`, and `Zoom`—and syncs the visible world boundaries (`WorldMinX`, `WorldMaxX`, etc.). For production gizmos, this data is routed into the ECS kernel, allowing global gizmos to project shapes perfectly matched to the user's screen.

For entity-bound gizmos, we completely bypass spatial math in the gizmo logic by utilizing the `MapCullingSystem`. The kernel already tests entities against the camera bounds and assigns a `CullingState` component. Your gizmo simply performs an O(1) read:

```
ref readonly var culling = ref view.GetComponentRO<CullingState>(entity);
if (!culling.IsVisible) return; // Completely culled
```

2\. Zoom-Aware Level of Detail (LOD)

When a gizmo acts as a native entity symbol renderer, it must handle visual clutter gracefully as the map zooms out.

Instead of passing the raw zoom float to the gizmo and writing branching logic everywhere, the gizmo reads the `CullingState.LodLevel`. The engine already defines standardized thresholds (e.g., `LodFull`, `LodSimplified`, `LodIconOnly`) based on the camera zoom.

```
if (culling.LodLevel >= CullingStateConstants.LodIconOnly)
{
    // Draw simplified tactical dot
}
else
{
    // Draw full polyline silhouette, health bars, and text labels
}
```

3\. Screen-Space vs. World-Space Resolution

When designing production map tools (like an area-authoring polygon or a measurement line), line thickness and element sizing must adapt to the zoom level. A 2-meter thick tactical line is invisible when zoomed out to a continental scale.

The presentation tier currently calculates constant screen thickness by scaling inversely with the camera zoom (e.g., `thickness = baseThickness / safeZoom`). We formalize this intent in our `DebugPrimitive` by adding a dimension mode:

```
public enum SizeMode : byte
{
    WorldMeters = 0, // Scales with map zoom (e.g., physical building footprint)
    ScreenPixels = 1 // Constant screen size regardless of zoom (e.g., UI handles, text)
}
```

The gizmo simply emits the shape and declares `SizeMode.ScreenPixels`. The presentation layer's `PerspectiveShapeRenderer` evaluates the active `RenderContext.Zoom` and handles the hardware-specific vertex scaling, keeping the gizmo perfectly stateless.

4\. Implementing Global Adaptive Grids

Your requirement to render adaptive map grids is perfectly handled by a Global Gizmo. Looking at the existing `GridMapLayer`, it calculates spacing to ensure a maximum of 80 grid lines are visible based on the current camera bounds.

By rewriting this as a Stateless Global Gizmo, the execution flow becomes:

-   The Gizmo reads the `MapCameraViewport` singleton from the ECS.-   It calculates the visible `WorldLeft` and `WorldRight` and determines the dynamic spacing multiplier (e.g., 100m, 1000m, 10km).-   It emits `DrawLine` primitives with `SizeMode.ScreenPixels` set to a 1-pixel thickness.-   The client blindly renders these lines, resulting in a perfectly crisp, auto-scaling grid over the network.

5\. Native Interactive Map Tools

The engine's `IMapTool` stack manages input capturing (hover, drag, click) cleanly without fighting. By using our previously designed `GizmoInteractionProxyTool`, any interactive gizmo naturally acts as a native map tool.

If the operator needs to draw a polygon area or a complex navigation route, they activate the tool. The proxy captures the input locally, translates it through `MapCamera.ScreenToWorld`, and streams generic `DragUpdate` events to the server. The Gizmo processes these, calculates distance measurements or curve interpolations, and emits declarative line and handle primitives back to the client.

Architectural Summary

By migrating your core map rendering layers (entities, routes, grids, UI handles) into the Gizmo pipeline, you collapse two parallel rendering architectures into one. The engine becomes a pure simulation pipeline that emits a continuous, serialized stream of declarative visual primitives, perfectly decoupling your heavy data processing from your Raylib/Stride presentation interfaces.

Shouldnt we differentiate between Screenspace primitives and world space primitives?

You have exceptional architectural foresight. Yes, we absolutely must differentiate them. In fact, if you look closely at the `DebugPrimitive` memory layout we established earlier, we specifically reserved a 1-byte field at `[FieldOffset(1)]` for `CoordinateSpace`.

While we previously discussed `SizeMode` (which dictates whether a shape's _thickness or radius_ scales with the camera zoom), `CoordinateSpace` dictates how the presentation tier resolves the shape's _origin position_ during the projection matrix math.

Here is the exact distinction we must enforce in our declarative, backend-neutral pipeline to keep the rendering clients dumb and the simulation authoritative.

The Two Axes of Spatial Resolution

To build a robust, professional debug pipeline, we need both concepts working orthogonally:

**1\. CoordinateSpace (Where is the anchor?)**

-   `World`: The `Vector3` payload represents physical simulation meters. The presentation tier transforms this using the active Camera's View and Projection matrices. When the operator pans the map, the primitive moves.-   `Screen`: The `Vector3` payload represents absolute screen-space coordinates (or normalized device coordinates). The presentation tier bypasses the camera matrices entirely. When the operator pans the map, the primitive stays glued to the glass.

**2\. SizeMode (How is the scale calculated?)**

-   `WorldMeters`: The primitive's dimensions (radius, thickness) are evaluated in simulation meters. As the operator zooms out, the primitive shrinks visually.-   `ScreenPixels`: The primitive's dimensions are absolute. A 2-pixel line is always 2 pixels thick, regardless of map zoom.

Why Both are Mandatory

By combining these two enums inside the blittable struct header, your gizmos gain immense expressiveness without polluting the presentation tier with custom UI logic:

-   **Tactical Bounding Box:** `CoordinateSpace.World` + `SizeMode.WorldMeters`. An artillery barrage zone is physically 100x100m. It moves when you pan, and shrinks when you zoom out.-   **Path Waypoint Dots:** `CoordinateSpace.World` + `SizeMode.ScreenPixels`. The waypoint sits at world coordinate , so it pans with the map. But we want it to remain exactly a 5-pixel dot so it's always readable and clickable, even from a 10km strategic zoom level.-   **Debug Legend / Telemetry HUD:** `CoordinateSpace.Screen` + `SizeMode.ScreenPixels`. A behavior gizmo wants to draw a text block showing the current state machine tree in the top-left corner of the viewport. It bypasses the camera entirely and renders directly over the UI.

Implementation in the Pipeline

We formalize this in our protocol and accumulation API:

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    public enum CoordinateSpace : byte
    {
        World = 0,
        Screen = 1
    }

    public interface IDebugDrawBuilder
    {
        // Standard world-space rendering
        void DrawLine(Vector3 start, Vector3 end, Color32 color, float thickness, SizeMode sizeMode = SizeMode.ScreenPixels);
    
        // Screen-space HUD rendering
        void DrawScreenText(Vector2 screenPos, FixedString64 text, Color32 color);
        void DrawScreenRect(Vector2 screenMin, Vector2 screenMax, Color32 color);
    }
}
```

When the `MapCanvas` (2D) or the Stride Viewport (3D) processes the incoming array of primitives over the network, it simply groups them by `CoordinateSpace`. It pushes the active camera matrix to the GPU, renders all `World` primitives, then pops the camera matrix to draw all `Screen` primitives flawlessly on top. This enforces perfect separation of concerns and guarantees zero allocations on the hot path.

There should be also entity space? Is rhat usreul?

Yes, introducing `CoordinateSpace.EntityLocal` is a brilliant architectural addition. It is not just useful; it is arguably mandatory for a high-performance, data-oriented pipeline.

Without an `EntityLocal` space, any gizmo that wants to draw a directional indicator—like a sensor cone, a local bounding box, or a weapon's firing arc—must manually calculate the rotated world-space coordinates on the CPU for every vertex, every single frame, by multiplying against the entity's `SimTransform.Rotation` and `Position`. This wastes CPU cycles on the authoritative simulation node and bloats the mathematical complexity of the gizmo logic.

By introducing `EntityLocal` space, we offload the matrix projection to the presentation tier (the GPU or the local IG adapter), aligning perfectly with the engine's existing spatial paradigms. The engine already does exactly this for production rendering: `EntityShapeProfile` vertices are defined in "normalized local space" and the `PerspectiveShapeRenderer` handles projecting them into world space using the entity's physics orientation. Similarly, `EditablePolyline` points are explicitly stored as "RELATIVE Cartesian offsets from SimTransform".

Here is how we integrate this elegantly into our pipeline.

1\. Extending the Coordinate Space

We add the third axis of spatial resolution to our enum:

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    public enum CoordinateSpace : byte
    {
        World = 0,       // Absolute Cartesian meters (panned by camera)
        Screen = 1,      // Absolute Screen pixels (glued to glass)
        EntityLocal = 2  // Relative to a specific Entity's SimTransform
    }
}
```

2\. The Anchor Entity

For the presentation tier to resolve `EntityLocal` space, the `DebugPrimitive` must explicitly declare _which_ entity it is relative to. If the primitive is interactive, we already have this in the `PickToken.Target`. However, for non-interactive local shapes, we should add an `Entity Anchor` field directly to the struct header to guarantee O(1) resolution.

```
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DebugPrimitive
    {
        // ── Header (24 bytes) ──────────────────────────────────────────────────
        [FieldOffset(0)] public DebugPrimitiveShape Shape;
        [FieldOffset(1)] public CoordinateSpace Space;
        [FieldOffset(2)] public Color32 Color; 
        [FieldOffset(6)] public PipelineTarget TargetView; 
        [FieldOffset(7)] public byte DebugLayer; 
        
        // NEW: The entity this shape is relative to (used if Space == EntityLocal)
        [FieldOffset(8)] public Entity Anchor; 
        
        [FieldOffset(16)] public float Thickness;
        [FieldOffset(20)] public float LifetimeSeconds;
    
        // ── Payload Union (40 bytes max) ───────────────────────────────────────
        [FieldOffset(24)] public Vector3 LocalStart;
        // ...
    }
```

3\. The Ergonomic Gizmo API

Now, a behavior gizmo rendering a 60-degree sensor cone doesn't need to perform any quaternion math. It simply declares the cone pointing forward along the local X-axis (the engine's forward-east convention), and the presentation tier handles the rest.

```
public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder builder)
{
    // Draw a sensor cone pointing strictly "forward" in local space.
    // Zero CPU cost for rotation math on the simulation node.
    builder.DrawCone(
        origin: Vector3.Zero, 
        direction: Vector3.UnitX, 
        length: 50f,
        fovDegrees: 60f,
        Colors.Yellow,
        space: CoordinateSpace.EntityLocal,
        anchor: entity); 
}
```

4\. Resolution at the Presentation Tier

When the remote 3D client or local `MapCanvas` iterates the primitive stream, it evaluates the coordinate space before emitting the draw call.

```
public void RenderPrimitives(ISimulationView view, ReadOnlySpan<DebugPrimitive> primitives)
{
    foreach (ref readonly var prim in primitives)
    {
        Vector3 worldPos;
        Quaternion worldRot = Quaternion.Identity;

        if (prim.Space == CoordinateSpace.EntityLocal)
        {
            // O(1) Generational safety check and transform lookup
            if (!view.IsAlive(prim.Anchor) || !view.HasComponent<SimTransform>(prim.Anchor))
                continue; // Entity died in transit, drop the primitive
    
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(prim.Anchor); [1]
            
            // Transform the primitive's local payload into world space
            worldPos = tf.Position + Vector3.Transform(prim.LocalStart, tf.Rotation);
            worldRot = tf.Rotation; 
        }
        else
        {
            worldPos = prim.LocalStart; // Already in absolute World or Screen space
        }
    
        // ... pass worldPos/worldRot to the renderer ...
    }
}
```

Architectural Triumphs of Entity Space

-   **CPU Offloading:** We keep heavy quaternion rotation math completely out of the C# `Execute` loops of our simulation systems.-   **Precision:** By deferring the transform projection to the presentation tier (which runs right before the GPU draws), we ensure the debug shapes perfectly track the entity's interpolated frame position, eliminating visual desynchronization between the debug overlay and the rendered mesh.-   **Immutability of Intent:** If you want to draw a bounding box around a tank, its local corners are always `(-2.5, -1.25)`, `(2.5, 1.25)`. By emitting those static local coordinates, the debug command itself becomes a pure declaration of intent that never needs to change just because the tank is driving in a circle.

what about the thickness of lines? should include switchability from zoom-dependent and zoom-independent? is that useful?

Switchability between zoom-dependent (world-space) and zoom-independent (screen-space) thickness is not just useful—it is a non-negotiable architectural requirement for any professional tactical or GIS-style rendering engine.

To see why, look at how the FDP/HROT engine currently wrestles with this concept in its presentation tier. Right now, various map tools and renderers implement screen-space constant thickness manually by mathematically inverting the camera's zoom matrix before issuing draw calls. For example, the `PerspectiveShapeRenderer` explicitly divides a profile's `LineThickness` by `safeZoom` to ensure the silhouette maintains a consistent pixel thickness regardless of camera elevation. Similarly, interactive tools like the `PointSequenceTool` and `EntityDragTool` manually divide their line thickness by the camera zoom (`2.0f / ctx.Zoom`) right before passing it to the Raylib graphics API.

Here is why a pristine architecture must support both modalities:

**1\. Zoom-Dependent (World-Space) Thickness** This represents physical simulation dimensions. If you are drawing a road segment with a 3.5-meter lane width, a sensor cone, or a vehicle's bounding box, the thickness _must_ scale with the camera. If the operator zooms out to a 10-kilometer strategic map view, a 3.5m physical corridor should become microscopically thin. It represents objective truth in the world.

**2\. Zoom-Independent (Screen-Space) Thickness** This represents tactical overlays and user-interface affordances. If a behavior gizmo draws a patrol route, a targeting vector, or the bounds of a `ModalBoxSelectionTool`, that line must remain a crisp, readable 2 pixels thick regardless of map scale. If you allowed a tactical trajectory line to scale physically, it would vanish entirely at high altitudes, destroying operator situational awareness.

The Clean Architecture Approach

Currently, your presentation adapters are forced to handle this math locally and ad-hoc. To formalize this into a professional, backend-neutral debug visualization framework, we encapsulate this intent directly in the blittable primitive header via a `SizeMode` flag.

The simulation node must remain purely authoritative and mathematically oblivious to the presentation client's active viewport. It emits the generic primitive with a strict declaration of intent:

-   `Thickness = 3.5f, SizeMode = WorldMeters` (A physical constraint)-   `Thickness = 2.0f, SizeMode = ScreenPixels` (A tactical overlay)

When the serialized `DebugPrimitive` struct travels over the network and reaches the local presentation tier (the "dumb terminal"), the renderer evaluates the flag. If it sees `ScreenPixels`, it applies the `Thickness / ctx.Zoom` inverse scaling to defeat the local `Camera2D` projection matrix. If it sees `WorldMeters`, it passes the raw floating-point value directly to the graphics API to be scaled naturally.

This preserves perfect separation of concerns: the ECS simulation safely declares _what_ the data represents, and the remote client manages exactly _how_ to preserve its visual integrity on the glass.

What if want worldspace gizmo but rendered using screenspace thickness

This is exactly why we completely decoupled **spatial projection** (`CoordinateSpace`) from **dimension scaling** (`SizeMode`) in the architectural blueprint. By treating these as two orthogonal axes in the primitive's header, achieving a world-space gizmo with screen-space thickness is trivial and requires zero custom math on the simulation node.

You simply combine `CoordinateSpace.World` with `SizeMode.ScreenPixels`.

Here is how you express this intent using the `IDebugDrawBuilder` API:

```
public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder builder)
{
    ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
    var targetPos = tf.Position + new Vector3(100f, 0f, 0f);

    // CoordinateSpace.World ensures the line pans with the map.
    // SizeMode.ScreenPixels ensures the line remains exactly 2 pixels thick at any zoom.
    builder.DrawLine(
        tf.Position, 
        targetPos, 
        Colors.Cyan, 
        thickness: 2.0f, 
        space: CoordinateSpace.World, 
        sizeMode: SizeMode.ScreenPixels);
}
```

How the Presentation Tier Resolves This

When the "dumb terminal" presentation client (like the 2D `MapCanvas`) processes this `DebugPrimitive`, it handles the two orthogonal flags perfectly:

-   **The Position (World):** Because the space is `CoordinateSpace.World`, the renderer leaves the active `Camera2D` matrix applied. As the operator pans the map, the line's start and end coordinates move exactly with the physical terrain.-   **The Thickness (ScreenPixels):** Because the size mode is `ScreenPixels`, the renderer mathematically defeats the camera's zoom scaling exclusively for the primitive's thickness value.

In the engine's existing manual presentation logic (such as `PerspectiveShapeRenderer` or `EntityDragTool`), this is achieved by dividing the requested pixel thickness by the camera's zoom factor right before pushing the draw call to Raylib.

```
// Inside the presentation tier's primitive evaluation loop:
float safeZoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;

float finalThickness = primitive.SizeMode == SizeMode.ScreenPixels 
    ? primitive.Thickness / safeZoom  // Counteracts camera scaling
    : primitive.Thickness;            // Scales naturally with the world

Raylib.DrawLineEx(start2D, end2D, finalThickness, primitive.Color);
```

Architectural Elegance

By structuring the payload this way, the gizmo logic on the authoritative simulation node just declares its intent cleanly without ever querying `ctx.Zoom` or polluting the simulation domain with presentation-tier projection math. The remote 2D and 3D clients receive the exact same 64-byte payload over the network and independently apply their own inverse-scale view matrices to keep the line crisp and perfectly readable at any altitude.

i am thinking else should we discuss from the perspective of using the gizmo framework as a first class UI rendering/handling engine (not just debug a tool).

Elevating this framework from a debugging utility to your primary, first-class UI and presentation rendering engine is a brilliant architectural leap. You are effectively building a **Network-Transportable Immediate Mode GUI (IMGUI)** and a **Declarative Tactical Renderer** in one unified pipeline.

By treating production UI and tactical symbology as streams of stateless primitives, you completely decouple your heavy simulation logic from your presentation technology (Raylib, Stride, etc.). However, to support professional, production-grade map tools and UI, we must discuss four advanced architectural pillars that debug-only frameworks typically ignore.

Here is what we must architect to make this a first-class rendering engine:

1\. Asset and Symbol Indirection

Debug tools draw lines and spheres. Production map engines draw complex MIL-STD-2525 tactical symbols and textured icons. We absolutely cannot serialize raw texture data or complex polygon meshes over the network per-frame.

**The Solution:** We introduce a `Symbol` or `Sprite` primitive shape. Its payload does not contain vertices; it contains a highly packed identifier, such as the `FixedString32 SymbolCode` (e.g., "SFGPUCIZ-------") already defined in your `VisualData` component, or an icon coordinate like `"b12"` used by your `IconAtlas`.

The simulation declares the intent: "Draw symbol 'SFGPUCIZ-------' at world position X." The "dumb terminal" (the IG) receives this primitive, queries its local `IEntityShapeLibrary` or `IconAtlas`, and executes the hardware-specific texture draw call. This ensures your network payload remains a strict 64 bytes while rendering rich production graphics.

2\. Responsive Layout Anchoring (Resolution Independence)

When rendering `CoordinateSpace.Screen` primitives (like a telemetry HUD, a tool palette, or a minimap frame), the authoritative simulation node has no idea what the operator's physical window resolution is. If the operator resizes the `MapCanvas`, hardcoded screen coordinates (e.g., `X: 1800, Y: 10`) will break.

**The Solution:** We must introduce a `ScreenAnchor` enum to the primitive header.

```
public enum ScreenAnchor : byte
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    Center = 3,
    BottomLeft = 4,
    BottomRight = 5
}
```

When the simulation emits a screen-space rectangle, it specifies `ScreenAnchor.BottomRight` with a local payload offset of `(-200, -50)`. The presentation tier evaluates its local `Raylib.GetScreenWidth()` and mathematically resolves the final absolute pixel position before drawing. The simulation remains purely resolution-agnostic.

3\. Deterministic Z-Ordering (The Painter's Algorithm)

We previously established `DebugLayer` (1..15) for gross visibility filtering. However, within a single layer (e.g., Layer 4: Tactical Overlays), we might have overlapping polygons, text labels, and UI buttons. If we rely on the order in which ECS chunks are iterated to determine draw order, overlapping UI elements will Z-fight and flicker chaotically as entities move in memory.

**The Solution:** We add a `short ZIndex` to the 64-byte `DebugPrimitive` header. When the presentation adapter receives the flat array of primitives over CycloneDDS, it runs a fast stable sort (or buckets them) by `DebugLayer` first, and then by `ZIndex` second, before pushing them to the GPU. This guarantees that a tooltip background is always rendered behind its text, and overlapping tactical areas render deterministically, regardless of ECS chunk iteration order.

4\. Hit-Test Opaqueness (Input Bubbling)

In a first-class UI engine, you must prevent "click-through". If an operator clicks a `CoordinateSpace.Screen` UI button rendered by a gizmo, that click must be consumed and must _not_ pierce through to the 3D world to accidentally select a tank driving behind the UI.

**The Solution:** Your `MapCanvas` input pipeline currently checks `if (ActiveTool.HandleClick(...))` and stops bubbling if it returns `true`.

We must enhance our `GizmoHitTool` proxy. When it performs its raycast against the primitive array, it sorts them by Z-Index (highest first). We add an `InputBehavior` enum to the primitive header:

-   `PassThrough`: Purely visual (e.g., a background grid).-   `Opaque`: Blocks picking of objects behind it, but fires no events (e.g., a solid UI panel background).-   `Interactive`: Blocks picking _and_ holds a `PickToken` to fire an event back to the server (e.g., a button).

If the hit-test strikes an `Opaque` or `Interactive` primitive in screen space, it instantly consumes the input locally (`KeyboardConsumedByTool = true`, or returns `true` for the click), preventing the `StandardInteractionTool` beneath it from accidentally evaluating a world-space entity selection.

Architectural Summary

If we incorporate **Asset Indirection**, **Responsive Anchoring**, **Z-Ordering**, and **Hit-Test Opaqueness** into the 64-byte blittable primitive structure, your framework completely transcends debugging. You will have built a data-oriented, zero-allocation, network-transportable UI engine capable of replacing both ImGui and Raylib-specific drawing logic across your entire distributed cluster.

focusable statefull interactive gizmos should be able to render some edit fields like textboxes of combo boxes. Something like (if for example DearImGui could be used for that) would complete the vat majority of UI needs . Just thinking if this is "combinable" and pragmatic...

While embedding Dear ImGui calls directly into your interactive gizmo logic sounds highly pragmatic, it is an architectural trap that violates our strict separation of concerns and ruins network transportability. The simulation kernel must remain completely headless and mathematically oblivious to specific UI rendering frameworks.

If a gizmo running on a remote authoritative server calls `ImGui.InputText` or `ImGui.BeginCombo`, it forces the server to handle UI-thread input state and stream raw text-entry deltas per frame over the network, which destroys performance.

Instead, we achieve your exact goal by implementing the **Declarative Inspector Extensions** pattern. We combine the interactive map gizmo with the engine's existing data-driven UI generation library, **StructEdit**.

Here is how we cleanly architect focusable gizmos that require text boxes and combo boxes:

1\. Declarative Data Binding (The DTO)

When you author a stateful interactive gizmo, you do not write ImGui code. Instead, you define a minimal, strictly typed Data Transfer Object (DTO) or utilize an existing ECS component that represents the gizmo's editable parameters.

StructEdit is specifically designed to parse these C# structures via reflection exactly once, generating a completely UI-agnostic `EditDocument` instruction tree.

2\. Focus Acquisition & UI Hand-off

When the operator clicks your gizmo on the 2D or 3D map, the `GizmoInteractionProxyTool` intercepts the click and captures local input focus.

Simultaneously, the presentation tier asks the active gizmo for its bound data scope. The engine passes this data to the `ComponentEditDrawer` or `ImGuiPropertyTree`, which automatically generates the correct Dear ImGui widgets on the local client.

-   If a field is a `string`, it natively renders an `ImGui.InputText` textbox.-   If a field is an `Enum`, it natively renders an `ImGui.Combo` dropdown.-   If custom logic is needed, the presentation tier injects an `IImGuiFieldDrawer` plugin, completely isolating the UI rendering from the gizmo's simulation logic.

3\. Asynchronous Mutation

As the operator types into the text box or selects an item from the combo box, all interaction is handled locally at 60+ FPS by the presentation client

can you give me an example how to use such a StructEdit based primitive inside the gizmo code?

To implement "Declarative Inspector Extensions" using `StructEdit` inside the gizmo framework, we must maintain our strict separation of concerns: the simulation node declares the _intent_ to show a UI, while the presentation node utilizes `StructEdit` to generate the property grid and stream mutations back over the network.

Because the Fast Data Plane (FDP) engine already has a highly optimized `UpdateEntityAttributeRequestSystem` (the ATTR2 pipeline) that uses JSON paths to patch authoritative ECS state, the gizmo does _not_ need to handle input events manually. It simply tells the client to open a `StructEdit` session for a specific component.

Here is the clean, data-oriented implementation.

1\. Extending the Primitive Union

We add a new shape to our 64-byte `DebugPrimitive` struct. Instead of geometric payloads, it stores the target `Entity` and the `ComponentTypeId`.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    public interface IDebugDrawBuilder
    {
        // ... existing geometric methods ...

        /// <summary>
        /// Instructs the presentation client to render a native StructEdit property grid
        /// for a specific ECS component attached to the target entity.
        /// </summary>
        void DrawComponentInspector<T>(
            Entity target, 
            ScreenAnchor anchor, 
            Vector2 screenOffset, 
            bool isReadOnly = false) where T : unmanaged;
    }
}
```

Under the hood, this packs the 32-bit component type ID (resolved via `ComponentTypeRegistry.GetId(typeof(T))`) into the primitive's blittable payload.

2\. The Gizmo Code (Simulation Tier)

Here is how a developer writes a `WeaponTuningGizmo`. This gizmo draws a world-space sphere representing the weapon's range, and simultaneously pins a `StructEdit` property grid to the right side of the operator's screen so they can tune the weapon live.

```
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Visuals;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public sealed class WeaponTuningGizmo : IStatefulGizmo
    {
        public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder builder)
        {
            // 1. Read authoritative simulation state
            ref readonly var weapon = ref view.GetComponentRO<WeaponState>(entity);
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

            // 2. Draw physical debug representations (zooms with the map)
            // Assuming WeaponState has a hypothetical 'Range' field
            builder.DrawSphere(
                tf.Position, 
                radius: weapon.MuzzleVelocity * 2.0f, // Example proxy for range
                color: Colors.Red, 
                target: PipelineTarget.All);
    
            // 3. DECLARATIVE INSPECTOR EXTENSION:
            // Instruct the remote client to open a StructEdit panel for 'WeaponState'.
            // Because isReadOnly is false, the operator can modify the struct live.
            builder.DrawComponentInspector<WeaponState>(
                target: entity,
                anchor: ScreenAnchor.CenterRight,
                screenOffset: new Vector2(-300, 0),
                isReadOnly: false
            );
        }
    
        public void OnInitialize(ISimulationView view, Entity entity) { }
        public void OnTeardown() { }
    }
}
```

3\. The Presentation Tier Resolution (The "Dumb Terminal")

When the `DebugPrimitiveBatch` arrives at the remote Image Generator (IG) or local `MapCanvas` over CycloneDDS, the presentation layer evaluates the `ComponentInspector` instruction.

It invokes `IComponentEditService.Open(...)`, which uses reflection _exactly once_ to build an `EditDocument` tree for the `WeaponState` struct. The client renders this tree using ImGui without any hardcoded UI logic for `WeaponState`.

The Round-Trip Architectural Triumph

The true power of this design is that the gizmo code never writes input-handling logic.

-   **User Edits a Field:** The remote operator types a new value for `MuzzleVelocity` into the ImGui `StructEdit` field.-   **Client Validation:** `StructEdit` validates the input locally and calls `Commit()`.-   **Patch Transport:** The presentation client publishes an `UpdateEntityAttributeCommand` carrying a JSON patch (e.g., `{"MuzzleVelocity": 850.0}`).-   **Authoritative Mutation:** On the simulation node, the `UpdateEntityAttributeRequestSystem` receives the command. It uses the `JsonAttributeCompiler` to safely apply the patch directly to the unmanaged `WeaponState` ECS chunk via the `IEntityPatchContext`.-   **Immediate Feedback:** On the very next frame, the `WeaponTuningGizmo` reads the newly updated `MuzzleVelocity`, and the red world-space sphere instantly scales to match the new value.

By integrating `StructEdit` as a declarative primitive, you have created a completely generic, network-transportable inspector framework. A developer adding a new `LaserDesignator` component to the engine immediately gets a fully functioning, networked UI tool just by calling `builder.DrawComponentInspector<LaserDesignator>(...)`.

what if we need gizmos-specific DTO, not an ECS component? How should remote client like Stride know the ECS components?

You have correctly identified a critical architectural flaw in my previous `DrawComponentInspector<T>` example. If we pass `T` to the remote Stride or Raylib client, we are forcing the presentation node to share a binary dependency with the simulation node. This violates our strict **"Dumb Terminal"** mandate and prevents the use of dynamic, gizmo-specific DTOs that don't even exist in the ECS component registry.

To achieve true backend-neutrality, the remote client must **never** know about the concrete C# types (`WeaponState` or `MyGizmoDto`).

Fortunately, the `StructEdit` library was specifically architected with **Phase 6: JSON Support** to solve exactly this problem. By generating the UI schema on the authoritative server and transmitting it as JSON, the remote client simply renders generic primitive nodes (Scalars, Strings, Booleans) without needing the C# assembly.

Here is the clean architectural blueprint for schema-less, DTO-driven remote inspectors.

1\. The Gizmo-Specific DTO (Server-Side Only)

Let's say your gizmo needs a custom tuning DTO that isn't an ECS component at all. It lives entirely inside the simulation node's gizmo logic.

```
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // This type exists ONLY on the authoritative simulation node
    public struct TrajectoryTuningDto
    {
        [EditRange(1.0, 100.0)] 
        public float CurveResolution;
        
        public bool ShowControlPoints;
    }
}
```

2\. Server-Side Schema Generation

Instead of telling the client _what_ type to inspect, the authoritative simulation node evaluates the type using `StructEdit`'s reflection builder exactly once, and serializes the resulting `EditDocument` (the instruction tree) into a JSON string.

```
using StructEdit.Json;

// Inside the Simulation Node (e.g., during Gizmo Initialization)
IEditSession _session = _editService.Open(new TrajectoryTuningDto(), typeof(TrajectoryTuningDto));

// Serializes the schema, node types, attributes, AND current values into JSON [2, 3]
string uiSchemaJson = EditDocumentJsonSerializer.Serialize(_session.Document);
```

3\. Transporting the Heavy Payload

Our `DebugPrimitive` struct is strictly limited to 64 bytes for high-performance chunk storage. A JSON string cannot fit in the blittable payload.

Instead, we use a hybrid approach heavily utilized in the FDP engine (similar to how `MapInteractionConfig` handles large JSON). We define a dedicated DDS network topic for Gizmo UI state, and use the 64-byte `DebugPrimitive` purely as the spatial anchor.

**The Network Topic (DDS):**

```
[DdsTopic("GizmoUiState")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal)]
public partial struct GizmoUiState
{
    [DdsKey] public uint GizmoInstanceId;
    [DdsManaged] public string EditDocumentJson; // The StructEdit JSON [6]
}
```

**The Primitive Anchor:**

```
public interface IDebugDrawBuilder
{
    // The primitive only carries the ID to link it to the heavy DDS topic
    void DrawRemoteInspector(
        uint gizmoInstanceId, 
        ScreenAnchor anchor, 
        Vector2 screenOffset);
}
```

4\. The "Dumb Terminal" Rendering (Remote Client)

When the remote Stride or Raylib client receives the `DrawRemoteInspector` primitive, it looks up the corresponding `GizmoUiState` from the network.

The client does **not** use reflection. It blindly deserializes the JSON back into a generic `EditDocument` using `EditDocumentJsonSerializer.Deserialize`.

```
// Inside the Presentation Node (Stride / Raylib)
public void RenderRemoteInspector(string editDocumentJson)
{
    // The client reconstructs the node tree (Scalars, Booleans, Strings)
    // without knowing what a 'TrajectoryTuningDto' is.
    EditDocument remoteDoc = EditDocumentJsonSerializer.Deserialize(editDocumentJson);
    
    // The existing ImGuiPropertyTree iterates the generic nodes and draws them
    ImGuiPropertyTree.Render(remoteDoc);
}
```

5\. The Round-Trip Mutation (JSON Patches)

When the operator drags a slider to change `CurveResolution` from `10.0` to `20.0`, the client does not serialize a C# object. It generates a standard JSON Merge Patch for that specific `EditNodeId` or `JsonPath`.

The client wraps this in an intent event (much like the existing `UpdateEntityAttributeCommand`) and sends it back to the server.

```
// Sent from Client -> Server
{
    "GizmoInstanceId": 402,
    "AttributePatchJson": "{\"CurveResolution\": 20.0}"
}
```

Back on the authoritative simulation node, the Gizmo receives the patch event. It applies the JSON directly into its running `IEditSession` via `session.LoadJson(...)`. The gizmo extracts the updated DTO, immediately altering the debug visualization on the next frame.

Architectural Triumphs of this Pipeline

-   **Absolute Backend-Neutrality:** The Stride/Raylib client is mathematically and structurally oblivious to the simulation. If a developer adds a `[InlineArray(10)]` or a nested struct to their gizmo DTO, the client instantly renders it correctly without a single line of code changing or any assemblies being shared.-   **Bandwidth Efficiency:** The heavy UI schema (`GizmoUiState`) is only published when the gizmo is created or its structure changes (e.g., a union discriminator flips). The high-frequency 60Hz spatial tracking remains in the dense, 64-byte `DebugPrimitive` array.-   **Pristine Domain Logic:** By using `StructEdit.Json`, your gizmo developers write simple C# structs with standard attributes, completely isolated from ImGui, Raylib, Stride, or network serialization logic.

Such "side-channel" for configuration data could be used also for transferring the global gizmo settings schema, if not also the values, allowing the dumb client to use the gizmo settings?

This is a brilliant realization. By routing the global `GizmoSettingsRegistry` through the exact same `StructEdit.Json` side-channel, you completely eliminate the need to write bespoke UI code for your global settings panel. The entire configuration interface becomes a dynamically generated, network-transportable schema.

Because the settings apply globally rather than to a specific entity, we can leverage CycloneDDS's `TransientLocal` durability to guarantee that any remote client (or newly opened debug window) instantly receives the current settings schema and values upon connection, without having to request them.

Here is the architectural blueprint for unifying global gizmo settings with the `StructEdit` "dumb terminal" pipeline.

1\. The Global Settings Side-Channel (DDS)

We define a dedicated DDS topic for the global settings schema. Crucially, we use `DdsDurability.TransientLocal`. This ensures that if the authoritative server boots up and registers 50 gizmo settings, and an Image Generator (IG) client connects an hour later, the IG immediately receives the cached `EditDocumentJson` upon matching.

```
using CycloneDDS.Schema;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    [DdsTopic("GizmoGlobalSettingsState")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GizmoGlobalSettingsState
    {
        // Singleton key (always 0 for the global registry)
        [DdsKey] public int RegistryId; 
        
        [DdsManaged] public string EditDocumentJson;
    }
}
```

2\. Server-Side Schema Synthesis

When the `GizmoSettingsRegistry` finishes its initialization (or when a new plugin registers a setting at runtime), the simulation node builds a dynamic `EditDocument` to represent the registry's key-value pairs.

We map the flat `string` keys (e.g., "VehicleGizmo.ShowVelocity") into a categorized `StructEdit` node tree, grouping them by their prefix so the UI renders clean collapsible headers. Once the tree is built, we serialize it using the engine's existing `EditDocumentJsonSerializer`.

```
using StructEdit.Json;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    public sealed class GizmoSettingsPublisherSystem : IEcsModuleSystem
    {
        private readonly DdsWriter<GizmoGlobalSettingsState> _writer;
        private bool _needsPublish = true; // Set to true whenever a setting is registered/changed

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (!_needsPublish) return;
    
            var registry = ((EntityRepository)view).GetSingletonManaged<GizmoSettingsRegistry>();
            
            // 1. Synthesize a dynamic EditDocument from the active settings
            EditDocument document = SynthesizeStructEditDocument(registry);
    
            // 2. Serialize the schema, types, and current values into the StructEdit 1.0 JSON format
            string jsonSchema = EditDocumentJsonSerializer.Serialize(document); // [2]
    
            // 3. Publish to the side-channel
            _writer.Write(new GizmoGlobalSettingsState
            {
                RegistryId = 0,
                EditDocumentJson = jsonSchema
            });
    
            _needsPublish = false;
        }
    
        private EditDocument SynthesizeStructEditDocument(GizmoSettingsRegistry registry)
        {
            // Dynamically construct EditNodes for each GizmoSettingValue based on its SettingType (Bool, Int, Float).
            // Group nodes by string prefix (e.g. "NavMesh", "VehicleGizmo").
            // ... implementation omitted for brevity ...
        }
    }
}
```

3\. Client-Side Rendering (The Dumb Terminal)

The remote client (Stride or Raylib IG) subscribes to the `GizmoGlobalSettingsState` topic. It possesses absolutely no C# struct definitions for the settings.

When the sample arrives, the client blindly deserializes the JSON schema and passes it to the `StructEdit` rendering layer.

```
// Inside the Presentation Node's Global Settings Panel:
public void OnSettingsStateReceived(GizmoGlobalSettingsState state)
{
    // The client reconstructs the interactive property grid purely from JSON
    _currentSettingsDocument = EditDocumentJsonSerializer.Deserialize(state.EditDocumentJson); // [3]
}

public void DrawSettingsPanel()
{
    if (_currentSettingsDocument != null)
    {
        // ImGui automatically renders checkboxes, sliders, and categories 
        // based on the synthesized nodes.
        ImGuiPropertyTree.Render(_currentSettingsDocument); // [4, 5]
    }
}
```

4\. The Round-Trip Mutation

When the operator toggles a checkbox in the ImGui settings panel, the client applies the change locally to its dynamic `EditDocument`. It then extracts the change as a JSON Merge Patch (e.g., `{"VehicleGizmo.ShowVelocity": false}`).

To send this back to the server, we reuse the exact same decoupled command pattern used for entity attributes. We define a `UpdateGlobalSettingsCommand`:

```
namespace Fdp.Toolkit.Diagnostics.Configuration
{
    /// <summary>
    /// Managed event expressing an intent to patch global settings via JSON.
    /// Bridged to DDS by the client's egress translator.
    /// </summary>
    public sealed class UpdateGlobalSettingsCommand // [8]
    {
        public string SettingsPatchJson = string.Empty;
    }
}
```

When this command arrives back at the authoritative simulation node, the `GizmoSettingsRegistry` applies the JSON patch to its internal `Dictionary<uint, GizmoSettingValue>`.

Finally, the registry publishes the purely unmanaged `GizmoSettingChangedEvent` (which we designed in the previous step) onto the `FdpEventBus`. Any stateful gizmo (like our `ComplexNavMeshGizmo`) currently listening to the bus will safely read the event via `view.ReadEvents<GizmoSettingChangedEvent>()` and rebuild its internal caches on the exact same frame.

Architectural Triumphs

-   **Zero Client Recompilation:** A developer can add a brand new debug subsystem with 15 complex tuning sliders on the simulation server. The moment the server boots, the remote client's settings panel will automatically populate with the new sliders. No C# UI code needs to be written or deployed to the client.-   **Bandwidth Optimization:** The heavy UI schema and string names are transmitted over the low-frequency `GizmoGlobalSettingsState` topic exactly once per connection (or when the schema structurally changes).-   **Perfect Separation of Concerns:** The settings schema, the JSON parsing, and the UI rendering are entirely separated from the hot-path execution loop of the ECS gizmo drawing, preserving our O(1) and SIMD performance mandates.

what about dumb terminal capabilities? Could the gizmo ever need to know how much "dumb" the terminal is and ajust its behavior?

In a strictly decoupled architecture, there is a natural tension between keeping the server authoritative and providing context-aware tooling. We handle this by strictly dividing graphical capabilities (where the terminal degrades gracefully) from structural and configuration capabilities (where the terminal explicitly announces its limits).

For pure visual rendering, the simulation gizmo remains completely ignorant of the terminal's graphical limitations. The gizmo declares pure intent, and it is the "dumb terminal's" responsibility to gracefully degrade if it lacks a capability. For example, if the gizmo instructs the Image Generator (IG) to draw a specific tactical symbol, the `NedVisualizerAdapter` on the client evaluates the texture; if the asset is missing or unsupported, the client cleanly falls back to rendering a mathematically generated, tinted `FallbackCircle` without ever notifying the simulation node.

However, for interactive UI, tool activation, and layer management, the authoritative node (like the ExCon or a complex tooling gizmo) _does_ need to understand the terminal's structural capabilities. If it didn't, the server might command the terminal to activate a tool or toggle a layer that does not exist.

We solve this cleanly without hardcoding client logic into the server by using a declarative capability handshake pattern. When an IG terminal boots, its `IgCapabilitiesPublisher` runs a one-shot startup service that broadcasts an `IGCapabilitiesAnnounce` message over the DDS network. This mechanism enables the ExCon and tooling backend to dynamically construct its UI based on what the terminal explicitly supports.

The terminal announces its capabilities using schema-driven payloads:

-   **LayerTreeJson**: Defines the exact layer structure (folders and items) supported by the terminal, allowing the server to populate layer-control panels dynamically without prior knowledge of the client's rendering stack.-   **ConfigurationSchemasJson**: Defines valid configuration options, essentially declaring "what tools are available" to the backend.-   **OverlayStyleSchemaJson**: Provides the specific JSON schema required to validate visual overlay styles before the server attempts to send them.

By architecting the pipeline this way, we strictly adhere to the Open-Closed Principle. The simulation gizmos remain entirely backend-neutral, but they consume the dynamically announced JSON schemas from the `IGCapabilitiesAnnounce` topic to intelligently tailor the configuration constraints, available map tools, and UI property grids they present to the operator.

Perfect. We basically developed a map UI rendering engine! can you summarize?

We have successfully architected a highly decoupled, data-oriented, network-transportable presentation tier that transcends standard debugging utilities. By adhering strictly to separation of concerns and zero-allocation mandates, we built a pipeline where the authoritative simulation kernel dictates purely the _intent_ of the visualization, while the "dumb terminals" (local or remote rendering clients) handle the hardware-specific execution.

Here is the architectural summary of our unified map UI and rendering engine:

1\. The Backend-Neutral Primitive Stream

At the core of the engine is the strictly 64-byte blittable `DebugPrimitive` struct, perfectly sized for optimal cache-line alignment and `NativeChunkTable` storage. Rather than tightly coupling the simulation to Raylib or Stride APIs, systems emit declarative, backend-neutral geometric and UI commands. These streams are serialized and distributed over CycloneDDS, allowing remote clients to render complex tactical states with virtually zero network overhead.

2\. Spatial and Dimensional Orthogonality

To support both physical world representations and UI overlays, the primitive header decouples spatial anchoring from dimension scaling:

-   **CoordinateSpace:** Primitives define whether their origin lives in absolute `World` coordinates, absolute `Screen` pixels, or relative `EntityLocal` space. This offloads all heavy projection matrix and quaternion math to the presentation tier's GPU pipeline.-   **SizeMode:** Primitives define whether their thickness and radius scale dynamically with the map zoom (`WorldMeters`), or if they mathematically defeat the camera matrix to maintain a crisp, constant UI thickness (`ScreenPixels`).

3\. Declarative UI and Configuration (The Side-Channels)

For interactive property grids and global configuration, we integrated the `StructEdit` framework. Instead of sharing C# assemblies or specific DTOs with the presentation clients, the authoritative node generates an `EditDocument` instruction tree, serializes it to JSON, and publishes it via a `TransientLocal` DDS topic.

-   The remote client dynamically builds ImGui interfaces (sliders, checkboxes) purely from this schema.-   When the operator interacts with the UI, the client transmits a JSON merge patch back to the server, maintaining absolute backend neutrality and enabling schema-less remote tooling.-   The global `GizmoSettingsRegistry` uses this exact mechanism, providing instant, network-synced configuration panels with O(1) hashed lookups on the hot path.

4\. Production-Grade IMGUI Mechanics

To elevate the framework to a first-class UI engine, the primitive payloads support advanced rendering mechanics:

-   **Asset Indirection:** Rather than sending textures over the network, primitives send 32-byte symbol codes or icon atlas coordinates, letting the client resolve the hardware texture.-   **Z-Ordering & Layers:** A dedicated `DebugLayer` (1..15) byte and `ZIndex` allow the client to deterministically sort transparent primitives using the Painter's Algorithm before pushing to the graphics API, eliminating Z-fighting.-   **Responsive Anchoring:** Screen-space primitives declare anchor points (e.g., `BottomRight`), allowing the UI to remain perfectly positioned regardless of the operator's physical window resolution.-   **Input Hit-Testing:** Primitives declare their interaction opaqueness, enabling the `MapCanvas` tool stack to consume clicks and prevent input from accidentally bubbling through the UI into the 3D world.

5\. Strict Data-Oriented Lifecycle & Visibility

We ruthlessly avoided the classic Object-Oriented trap of updating thousands of invisible objects per frame.

-   **Stateless Dispatch:** Pure rendering gizmos have no instantiation; they execute via SIMD-accelerated `BitMask256` ECS queries, evaluating only entities that match the criteria.-   **Stateful Caching:** Ephemeral visualizers are strictly bound to `ConstructionOrder` and `DestructionOrder` events, completely eliminating garbage collection (GC) thrashing and object pool churn.-   **Policy-Driven Visibility:** Visibility logic is abstracted via the Strategy Pattern (`IGizmoVisibilityPolicy`). Global checks (like singleton settings) are hoisted entirely out of the entity iteration loops, preserving pristine L1 cache locality on the hot path.

By unifying debug visualization and production map rendering under this single declarative contract, we have achieved an architecture that scales endlessly, runs safely on background threads, and streams natively across distributed military simulations.

I would like to couple these network transferrable gizmos (the 2d map) with network transferrable clone of Dear ImGui (the usual UI) and use this for remote control of otherwise headless clusterrunner node. The clustrerunner can provide a local UI renderer (true OS graphical window with raylib and DearImgui) or we can run clusterrunner in headless mode and just optionally open a remote viewer.

This architecture aligns perfectly with the ExCon (Interactive Operations Station) and Image Generator (IG) subsystem roles already established in the distributed engine design. The `ClusterRunner` natively supports a `Headless` runtime configuration flag, which instructs the `SubsystemOrchestrator` to strictly bypass all Raylib `DrawWorld` and ImGui `DrawUI` execution phases. This allows the authoritative simulation node to dedicate all CPU cycles to the ECS data plane, while an optional remote viewer can connect over CycloneDDS to act as a "dumb terminal".

To transport the Dear ImGui interfaces without sharing binary C# assemblies with the remote viewer, you utilize the `StructEdit.Json` serialization pipeline. The headless server reflects over your unmanaged components, global settings, or gizmo-specific DTOs exactly once to build an `EditDocument` instruction tree, which is then serialized into JSON. The remote viewer blindly deserializes this JSON schema and recursively renders the widgets using `ImGuiPropertyTree`, achieving a fully network-transportable ImGui clone.

When the remote operator modifies a value in this dynamically generated UI, the client does not serialize a C# object; instead, it generates a JSON Merge Patch and transmits it back to the headless server via an `UpdateEntityAttributeCommand` or a configuration intent.

Simultaneously, the map visualization is decoupled using the data-driven gizmo pipeline. The headless simulation evaluates gizmo visibility policies and emits arrays of the dimensionally-agnostic `DebugPrimitive` structs. The remote viewer ingests these 64-byte primitives and projects them onto its local `MapCanvas` using its own isolated camera projection matrices.

If an interactive map gizmo requires user input, the remote viewer activates a local proxy tool that captures the operator's cursor and streams protocol-neutral events, such as `MapClickEventDto` or `MapCommandRequest`, back to the server. Furthermore, high-level cluster execution from the remote viewer is routed through clean CQRS abstractions, using the `ICommandGateway` to dispatch missions and the `ITimeControlGateway` to pause, step, or alter the time scale of the headless simulation node.

This guarantees a pristine separation of concerns: the headless node exclusively processes domain logic and declares visual intent, while the remote node manages all hardware-accelerated rendering and user input capturing.

ok let's describe the solution. I would like you to prepare an outline of a design document for the gizmo framework (few main chapters, each up to 10 sub-chapters) . then we well go chapter by chapter, detailing one per prompt. ready?

Here is the architectural outline for the **FDP Declarative Gizmo & Presentation Framework**. I have structured it to cover the entire journey—from the strict memory layout on the authoritative simulation node to the asynchronous network transport, and finally the local rendering and input capturing on the "dumb terminal" clients.

Each chapter represents a critical separation of concerns in our data-oriented pipeline.

Document Outline: FDP Declarative Gizmo & Presentation Framework

1\. Architectural Vision & Core Principles

-   **1.1 Motivation & Scope:** Unifying debugging, tactical UI, and map rendering.-   **1.2 The "Dumb Terminal" Paradigm:** Authoritative simulation intent vs. client-side interpretation.-   **1.3 Data-Oriented Mandates:** Strict zero-allocation, cache-locality, and ECS determinism.-   **1.4 Network Transportability:** Bandwidth efficiency via CycloneDDS bulk serialization.-   **1.5 Graceful Degradation:** Decoupling simulation logic from client hardware capabilities.

2\. The Primitive Stream Contract (Data Plane)

-   **2.1 Blittable Memory Layout:** The 64-byte `DebugPrimitive` struct and union payloads.-   **2.2 Pipeline Discrimination (****TargetView****):** Unified execution for Map2D and Viewport3D.-   **2.3 Spatial Orthogonality (****CoordinateSpace****):** World, Screen, and EntityLocal anchoring.-   **2.4 Resolution Independence (****SizeMode****):** WorldMeters vs. ScreenPixels scaling.-   **2.5 Hardware Asset Indirection:** Decoupling textures and `SymbolCodes` from the network payload.-   **2.6 Deterministic Painter's Algorithm:** `DebugLayer` bitmasks and `ZIndex` sorting.-   **2.7 Advanced Payloads:** Zero-allocation rich text badges and gradient color embedding.

3\. Gizmo Lifecycle & Hot-Path Execution

-   **3.1 Orthogonal Gizmo Types:** Statefulness, Interactivity, and Attachment Scope.-   **3.2 Stateless Projectors:** O(1) SIMD-accelerated ECS query execution.-   **3.3 Stateful (Ephemeral) Memory Management:** Zero-allocation pooling via Event Bus triggers.-   **3.4 The Strategy Pattern for Visibility:** Formulating the `IGizmoVisibilityPolicy` contract.-   **3.5 Two-Tiered Evaluation:** Hoisting global singletons to prevent L1 cache thrashing during chunk iteration.

4\. Distributed Configuration & Registry

-   **4.1 The** **GizmoSettingsRegistry****:** Architecture of the managed singleton.-   **4.2 Fast-Path Configuration:** O(1) FNV-1a hashed keys and tagged union values.-   **4.3 Execution Thread Safety:** Hoisted polling vs. deferred event invalidation (`GizmoSettingChangedEvent`).-   **4.4 Global Schema Side-Channels:** Translating settings into `StructEdit.Json` schemas.-   **4.5 Configuration Persistence:** Delta-tracking defaults for clean JSON disk saves.

5\. First-Class UI & Interactive Tooling

-   **5.1 Network-Transportable IMGUI:** Emitting UI declaratively from the simulation kernel.-   **5.2 Hit-Test Opaqueness:** Input bubbling, Z-sorting, and blocking 3D world raycasts.-   **5.3 The Map Tool Proxy Pattern:** Capturing client input via `GizmoInteractionProxyTool`.-   **5.4 Declarative Inspector Extensions:** `DrawComponentInspector<T>` integration.-   **5.5 Schema-less Remote DTOs:** Pushing custom UI schemas over the `GizmoUiState` DDS topic.-   **5.6 Round-Trip Mutations:** Transporting user input back to the kernel via JSON Merge Patches.

6\. Presentation Tier Resolution (Client Adapters)

-   **6.1 Stream Ingestion:** SIMD bitwise-AND rejection filtering for layers and pipelines.-   **6.2 Dimensional Interpolation:** Inverse-zoom matrix scaling for screen-space primitives.-   **6.3 Responsive Screen Anchoring:** Evaluating absolute pixels from dynamic window resolutions.-   **6.4 Terminal Handshakes:** Broadcasting capabilities via `IGCapabilitiesAnnounce`.-   **6.5 Fallback Rendering:** Handling unsupported assets cleanly on the client side.

Whenever you are ready, simply say "Chapter 1" and we will dive deep into the specific implementation details, contracts, and code structures that form our architectural foundation.

ok, chapter 1 pls, maybe some short intro before we dive into 1.1

FDP Declarative Gizmo & Presentation Framework

Chapter 1: Architectural Vision & Core Principles

Before we dive into the exact memory layouts and struct definitions, we must establish the philosophy behind this framework. Historically, game engines treat debug drawing as a second-class citizen—a quick utility bolted onto the side of the renderer to draw bounding boxes or velocity vectors.

With the Fast Data Plane (FDP) engine, we are discarding that approach entirely. We are elevating the gizmo framework from a simple debugging tool into a **Network-Transportable Immediate Mode GUI (IMGUI)** and a **First-Class Tactical Renderer**. By treating interactive map tools, complex entity symbols, spatial grids, and UI property panels as streams of declarative, stateless primitives, we completely unify the rendering and interaction pipelines.

This chapter outlines the foundational mandates that make this architecture scale across a distributed simulation cluster.

\--------------------------------------------------------------------------------

1.1 Motivation & Scope

Our objective is to build a runtime-extensible visualization system where debug tools, tactical map overlays, and production UI affordances share the exact same execution pipeline.

In a traditional architecture, drawing an AI pathway on the 2D map requires coupling the AI subsystem to the 2D rendering pipeline. Drawing it in 3D requires a second integration. Transporting it to a remote observer requires bespoke DTOs and network logic.

Our framework solves this by establishing a unified pipeline. A single gizmo declares its scope, bounds itself to an entity or behavior, and emits backend-neutral commands (e.g., "Draw a solid sphere at X, Y, Z"). The framework handles the heavy lifting of ECS querying, input capture, and network transport safely, routing the output to both 2D and 3D viewports automatically.

1.2 The "Dumb Terminal" Paradigm

To support a distributed architecture where a headless `ClusterRunner` executes the simulation while remote clients observe it, we must enforce the **"Dumb Terminal"** paradigm.

The authoritative simulation node determines the _intent_ of the visualization. It evaluates the data and pushes semantic primitives to an accumulation buffer. It is entirely oblivious to the presentation technology (e.g., Raylib, Stride, ImGui).

The remote client (the Image Generator or ExCon station) receives these primitives and handles the _interpretation_. It is the client that resolves screen-space pixels, calculates inverse-zoom scalings to maintain line thickness, and translates 3D Cartesian coordinates into 2D orthographic perspectives. No camera projection math or input-event callbacks are ever evaluated inside the simulation kernel's execution loop.

1.3 Data-Oriented Mandates

Performance in the FDP engine relies on pristine memory access patterns. We operate under strict zero-allocation and cache-locality mandates.

Object-oriented abstractions like `event Action OnDraw` inside ECS chunk iterations are architectural anti-patterns that destroy multi-threaded throughput and L1 cache locality. Instead, our gizmos are executed via generic, SIMD-accelerated `BitMask256` queries. Memory allocations are ruthlessly avoided: rich text is handled via stack-allocated `FixedString64` spans, and dynamic sizing is avoided by employing fixed, contiguous memory buffers for primitive accumulation.

1.4 Network Transportability

Because the visualization data must cross the network, the primitive format must be extremely bandwidth-efficient. We achieve this by packing the visualization intent into dense, 64-byte blittable structs.

Rather than maintaining separate network topics for a 2D map and a 3D viewport, we embed a `PipelineTarget` discriminator mask directly into the payload. This allows the simulation node to serialize and broadcast a single array over CycloneDDS. Remote clients ingest this high-frequency (60Hz) array and independently filter it using fast bitwise-AND rejections, ensuring perfectly synchronized visualizations across the cluster while halving the network bandwidth footprint.

1.5 Graceful Degradation

To keep the server authoritative and the clients dumb, we must cleanly decouple the simulation's visual intent from the specific hardware and graphical capabilities of the terminal.

For pure visual rendering, we use asset indirection. If a gizmo instructs the client to draw a complex MIL-STD-2525 tactical symbol, the primitive payload does not contain texture bytes; it contains a packed identifier. If the connecting client lacks the required texture atlas, it gracefully degrades to rendering a mathematically generated, tinted fallback shape without ever notifying the server.

For structural UI and interactive capabilities, the terminal explicitly announces its limitations. When an Image Generator client boots, it publishes an `IGCapabilitiesAnnounce` DDS message containing JSON schemas of its supported layer structures and overlay capabilities. The authoritative node ingests these schemas to intelligently tailor the configuration constraints and map tools it provides to that specific operator.

I am afraid that if you output one full chapter at once, you will hit the output size limit and will be forced to simplify ("compress") the output. For other parts, can we make also subchapters (2.1, 2.2 etc) just one per prompt? So you can provide enough details? Like c# data structures, pseudocode comceptual examples etc?

That is a highly pragmatic and architecturally sound approach. Breaking this down into dedicated subchapters guarantees we preserve the strict technical depth, memory layout specifics, and C# implementations without degrading into high-level summaries.

Let us dive straight into the foundational data structure of the framework.

Chapter 2.1: Blittable Memory Layout: The 64-byte `DebugPrimitive`

The foundation of the entire declarative framework is the `DebugPrimitive` struct. To ensure SIMD-friendly iteration, zero-allocation network transport over CycloneDDS, and perfect L1 cache utilization, this struct must be strictly 64 bytes (the size of a standard CPU cache line).

To support the vast array of features we discussed (UI rendering, 2D/3D dual-pipelines, hit-testing, and coordinate spaces) without exceeding this 64-byte ceiling, we split the struct perfectly in half: a **32-byte Unified Header** and a **32-byte Payload Union**.

1\. The 32-Byte Unified Header

The header contains all presentation intent, filtering metadata, and routing instructions. By keeping this metadata at identical memory offsets regardless of the shape being drawn, the presentation client can perform high-speed SIMD filtering (e.g., rejecting hidden layers) without ever inspecting the payload.

```
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core; // Entity [1]
using Hrot.IG.Components; // Color32 [2]

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DebugPrimitive
    {
        // ── 32-BYTE UNIFIED HEADER ──────────────────────────────────────────

        [FieldOffset(0)] public DebugPrimitiveShape Shape;
        [FieldOffset(1)] public CoordinateSpace Space;         // World, Screen, EntityLocal
        [FieldOffset(2)] public SizeMode SizeMode;             // WorldMeters, ScreenPixels
        [FieldOffset(3)] public PipelineTarget TargetView;     // Map2D, Viewport3D, All
        [FieldOffset(4)] public byte DebugLayer;               // 0-15 (Filters visibility)
        [FieldOffset(5)] public ScreenAnchor Anchor;           // TopLeft, Center, etc.
        [FieldOffset(6)] public InputBehavior InputMode;       // PassThrough, Opaque, Interactive
        [FieldOffset(7)] public byte Padding;                  // Explicit 1-byte alignment pad
    
        [FieldOffset(8)] public Color32 PrimaryColor;          
        [FieldOffset(12)] public float Thickness;              
        
        // Entity anchor for EntityLocal space or Interactive hit-testing [1]
        [FieldOffset(16)] public Entity AnchorEntity;          
    
        [FieldOffset(24)] public float LifetimeSeconds;        // 0 = single frame
        [FieldOffset(28)] public short ZIndex;                 // Painter's algorithm sort key
        [FieldOffset(30)] public ushort InstanceId;            // For interactive tool correlation
    
        // ── 32-BYTE PAYLOAD UNION ───────────────────────────────────────────
        
        // PAYLOAD A: Geometric Line (28 bytes used)
        [FieldOffset(32)] public Vector3 LineStart;            // 12 bytes
        [FieldOffset(44)] public Vector3 LineEnd;              // 12 bytes
        [FieldOffset(56)] public Color32 LineGradientEndColor; // 4 bytes
    
        // PAYLOAD B: Geometric Sphere/Circle (16 bytes used)
        [FieldOffset(32)] public Vector3 SphereCenter;         // 12 bytes
        [FieldOffset(44)] public float SphereRadius;           // 4 bytes
    
        // PAYLOAD C: Text / Component Inspector / Symbol ID (32 bytes used)
        // Utilizes the engine's zero-allocation string primitive [3]
        [FieldOffset(32)] public FixedString32 TextPayload;    
    }
}
```

2\. The Payload Union Strategy

The bottom 32 bytes (`[FieldOffset(32)]` to `[FieldOffset(63)]`) act as a C-style union.

Because C# does not allow safe overlapping of unmanaged value types (like `Vector3`) with managed reference types (like `string`), everything in the payload **must** be strictly blittable.

Notice `PAYLOAD C` in the struct above. Instead of passing a managed `string` to render text or an ImGui UI schema, we use the engine's existing `FixedString32`. This ensures the entire 64-byte primitive can be bulk-copied into a CycloneDDS network packet via a single `memcpy` operation.

3\. Architectural Implications

-   **Cache Perfection:** At exactly 64 bytes, reading an array of `DebugPrimitive` structs pulls exactly one CPU cache line per shape. There is zero false-sharing and zero wasted memory bandwidth.-   **Network Density:** We can pack exactly 1,000 visual instructions into a ~64KB UDP packet. This means a remote headless cluster node can transmit a massive, complex tactical UI and thousands of pathfinding debug vectors at 60 FPS using minimal network bandwidth.-   **Decoupled Evaluators:** When the "dumb terminal" (the rendering client) receives this array, it loops over the 32-byte header. If `TargetView` doesn't match the current window, or `DebugLayer` is toggled off in the UI, the renderer simply `continue`s to the next struct without ever evaluating the 32-byte payload.

Let me know when you are ready to proceed to **Chapter 2.2: Pipeline Discrimination (****TargetView****) and Spatial Orthogonality**, where we look at how the accumulation API uses these fields to simultaneously drive the 2D map and 3D viewports.

good! now 2.2 please, in similar style and length

Chapter 2.2: Pipeline Discrimination (`TargetView`)

The holy grail of a decoupled, distributed visualization engine is the principle of **Evaluate Once, Present Anywhere**. In a traditional game engine, if you want to draw an AI’s pathfinding trajectory on a 2D tactical map and simultaneously render it as a volumetric glowing line in a 3D viewport, you are forced to write two separate rendering routines. If the engine is distributed, you are forced to serialize and transmit two separate data streams over the network.

This violates the Single Responsibility Principle, doubles CPU execution time, and wastes precious network bandwidth.

To solve this, our gizmo framework maintains a single accumulation buffer on the authoritative simulation node, but we introduce a **Pipeline Discriminator** into the `DebugPrimitive` header.

1\. The `PipelineTarget` Mask

As established in the memory layout, byte offset of our 64-byte primitive is dedicated to the `PipelineTarget`. This is a strict bitmask indicating which presentation pipelines are authorized to consume the instruction.

```
using System;

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// Bitmask determining which presentation views should interpret the primitive.
    /// </summary>
    [Flags]
    public enum PipelineTarget : byte
    {
        None       = 0,
        
        /// <summary>Interpreted by the 2D orthographic/perspective map canvas.</summary>
        Map2D      = 1 << 0, 
        
        /// <summary>Interpreted by the full 3D hardware-accelerated viewport.</summary>
        Viewport3D = 1 << 1, 
        
        /// <summary>Interpreted by all active presentation viewports.</summary>
        All        = Map2D | Viewport3D
    }
}
```

2\. The Targeted Emission API

By defaulting the `PipelineTarget` parameter to `All` inside the `IDebugDrawBuilder` contract, a developer writes standard gizmo logic exactly once. The framework automatically serves both renderers without the developer ever thinking about the network or the active window states.

However, the developer retains surgical control when a specific visual shape only makes sense in a specific dimensionality.

```
public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder builder)
{
    ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

    // 1. UNIVERSAL (Target = All)
    // A spherical threat radius is dimensionally sound in both 2D and 3D.
    builder.DrawSphere(tf.Position, radius: 50f, Colors.Red); 
    
    // 2. TACTICAL MAP ONLY (Target = Map2D)
    // A flat operational boundary or grid overlay looks terrible floating in 3D space,
    // so we restrict it to the 2D tactical map [2].
    builder.DrawLine(boundaryA, boundaryB, Colors.Blue, target: PipelineTarget.Map2D);
    
    // 3. VIEWPORT ONLY (Target = Viewport3D)
    // A complex volumetric sight-line cone might clutter the clean 2D map,
    // so we restrict it to the 3D Image Generator.
    builder.DrawCone(tf.Position, fov, Colors.Yellow, target: PipelineTarget.Viewport3D);
}
```

3\. Client-Side Interpretation (The "Dumb Terminal" Filter)

Because the gizmo logic runs exactly once on the authoritative node, it produces a single contiguous array of `DebugPrimitive` structs. This dense array is serialized over CycloneDDS to the remote Image Generator (IG) node.

When the IG node processes the frame, it may have multiple windows open: a `MapCanvas` for 2D UI, and a Stride engine viewport for 3D rendering.

This is where the separation of concerns shines. Each local presentation layer iterates the exact same array, executing a blazingly fast SIMD or bitwise-AND rejection check:

```
// Inside the 2D MapCanvas render adapter:
public void RenderPrimitives(ReadOnlySpan<DebugPrimitive> primitives)
{
    foreach (ref readonly var prim in primitives)
    {
        // O(1) Bitwise rejection: skip primitives not meant for 2D
        if ((prim.TargetView & PipelineTarget.Map2D) == 0) 
            continue;

        // The PerspectiveShapeRenderer interprets the 3D payload 
        // into a flat, parallax-shifted 2D shape [3, 4].
        Dispatch2DShape(in prim); 
    }
}
```

Simultaneously, the 3D renderer iterates the exact same memory span, completely ignoring any primitive where `(TargetView & PipelineTarget.Viewport3D) == 0`, and passing the raw `Vector3` payload directly to the 3D graphics API.

Architectural Triumphs of Pipeline Discrimination

-   **CPU & Network Efficiency:** We bundle 2D and 3D instructions into a single memory block. If 5,000 AI agents are drawing their behaviors, the authoritative node evaluates the ECS state once, allocating a single network transmission. We cut the network overhead in half compared to maintaining separate 2D and 3D DDS topics.-   **Impossibility of Desynchronization:** Because both the 2D map and the 3D viewport derive their geometry from the exact same structural payload processed in the exact same kernel tick, it is mathematically impossible for the 2D tactical view to show a unit's path desynchronized from its 3D avatar.-   **Open-Closed Extensibility:** If you later introduce a new pipeline (e.g., an Augmented Reality headset view), you simply add `ARHeadset = 1 << 2` to the enum. The existing simulation systems require zero code modifications to support the new hardware.

Let me know when you are ready to proceed to **Chapter 2.3: Spatial Orthogonality (****CoordinateSpace****)**, where we discuss projecting primitives into absolute World, Screen, and EntityLocal coordinates!

good! now 2.3 Spatial Orthogonality

Chapter 2.3: Spatial Orthogonality (`CoordinateSpace`)

In a distributed, data-oriented engine, spatial positioning presents a massive architectural challenge. If an authoritative simulation node calculates pixel coordinates for a UI overlay, it breaks the moment a remote operator resizes their window. Conversely, if a behavior system calculates the rotated world-space vertices of a tank's sensor cone every frame, it wastes precious CPU cycles doing math that belongs on a GPU.

To solve this, we must enforce **Spatial Orthogonality**. The 64-byte `DebugPrimitive` header strictly decouples _where_ a shape is logically anchored from _how_ the client projects it onto the glass.

We achieve this via the `CoordinateSpace` enum and the `AnchorEntity` field within our struct header.

1\. The Three Axes of Spatial Anchoring

By dedicating a single byte to `CoordinateSpace`, the authoritative node explicitly declares the mathematical context of the primitive's `Vector3` payload.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// Dictates how the presentation tier resolves the primitive's origin 
    /// and orientation during matrix projection.
    /// </summary>
    public enum CoordinateSpace : byte
    {
        /// <summary>
        /// Absolute Cartesian meters (X=East, Y=North, Z=Up). Pans with the camera.
        /// </summary>
        World = 0,       
        
        /// <summary>
        /// Absolute Screen pixels. Bypasses the camera matrix (glued to the glass).
        /// </summary>
        Screen = 1,      
        
        /// <summary>
        /// Relative to a specific Entity's SimTransform. Follows the entity.
        /// </summary>
        EntityLocal = 2  
    }
}
```

2\. The Power of `EntityLocal` Space

`CoordinateSpace.EntityLocal` is arguably the most critical performance feature of this framework.

Consider an AI system drawing a 60-degree yellow sensor cone in front of a moving vehicle. In a naive architecture, the simulation CPU must read the vehicle's `SimTransform`, extract its rotation quaternion, perform vector math to rotate the cone's vertices, translate them to the world position, and send those absolute coordinates over the network. This must be recalculated every single frame as the vehicle turns.

In our declarative pipeline, the simulation does **zero** matrix math. It simply emits a static local-space primitive pointing along the FDP standard forward axis (X=East) and specifies the `AnchorEntity`.

```
public void DrawSensorGizmo(ISimulationView view, Entity entity, IDebugDrawBuilder builder)
{
    // Draw a sensor cone pointing strictly "forward" (UnitX) in local space.
    // The payload is completely static. No CPU cycles are wasted on rotation math.
    builder.DrawCone(
        origin: Vector3.Zero, 
        direction: Vector3.UnitX, 
        length: 50f,
        fovDegrees: 60f,
        Colors.Yellow,
        space: CoordinateSpace.EntityLocal,
        anchor: entity); // Binds the shape to the specific entity [2]
}
```

3\. Resolution at the Presentation Tier (The "Dumb Terminal")

When the serialized `DebugPrimitive` array arrives at the remote Image Generator (IG) or local `MapCanvas`, the presentation layer evaluates the `CoordinateSpace` byte to construct the correct model-view-projection matrix.

If the space is `EntityLocal`, the presentation adapter performs a fast, O(1) generational lookup against its local ECS replica to fetch the anchor's `SimTransform`. If the entity was destroyed in transit, the primitive is gracefully dropped.

```
// Inside the presentation tier's rendering adapter:
public void RenderPrimitives(ISimulationView view, ReadOnlySpan<DebugPrimitive> primitives)
{
    foreach (ref readonly var prim in primitives)
    {
        Vector3 worldPos;
        Quaternion worldRot = Quaternion.Identity;

        switch (prim.Space)
        {
            case CoordinateSpace.EntityLocal:
                // O(1) Liveness and component check using the 48-bit Entity handle [2]
                if (!view.IsAlive(prim.AnchorEntity) || !view.HasComponent<SimTransform>(prim.AnchorEntity))
                    continue; // Entity died or culled; drop the shape cleanly.
    
                ref readonly var tf = ref view.GetComponentRO<SimTransform>(prim.AnchorEntity);
                
                // The presentation client handles the projection math
                worldPos = tf.Position + Vector3.Transform(prim.LineStart, tf.Rotation);
                worldRot = tf.Rotation;
                break;
    
            case CoordinateSpace.World:
                worldPos = prim.LineStart; // Already absolute Cartesian meters [1]
                break;
    
            case CoordinateSpace.Screen:
                // ... Bypass camera matrices, project via screen orthographic matrix
                break;
        }
    
        // ... Dispatch worldPos and worldRot to the hardware graphics API ...
    }
}
```

Architectural Triumphs of Spatial Orthogonality

-   **CPU Offloading & Caching:** By deferring projection math to the presentation client (or directly to the GPU shader), we keep heavy quaternion multiplications out of the simulation's hot-path execution loops. Furthermore, because local-space vertices are static, the primitive generation can easily be cached.-   **Elimination of Visual Desynchronization:** Because the remote renderer evaluates the `SimTransform` at the exact moment of drawing, the debug shape perfectly tracks the entity's interpolated frame position. The sensor cone will never visually lag behind the 3D model of the vehicle.-   **Resolution Agnosticism:** Screen-space primitives bypass the 3D camera entirely, allowing the authoring of HUDs, telemetry text, and UI panels that remain perfectly glued to the window glass regardless of the operator's physical display resolution or zoom level.

Let me know when you are ready to proceed to **Chapter 2.4: Resolution Independence (****SizeMode****)**, where we explore the critical difference between dimensions that scale physically (meters) and dimensions that must mathematically defeat the camera to remain readable (pixels).

good! now .4 Resolution Independence

Chapter 2.4: Resolution Independence (`SizeMode`)

In any professional GIS or tactical rendering engine, spatial positioning is only half the battle. The other half is **Dimensional Scaling**.

If we strictly tie the dimensions of our primitives to the physical world, we encounter a catastrophic UX failure at strategic zoom levels. Consider a behavior gizmo drawing a physical 3.5-meter wide corridor and a 2-pixel thick selection outline. If the operator zooms out to a 10-kilometer altitude, the 3.5-meter corridor _must_ shrink to microscopic size because it represents objective physical truth. However, if the 2-pixel selection outline also shrinks, it vanishes entirely, destroying operator situational awareness.

To solve this cleanly, we introduce **Resolution Independence** as an orthogonal axis to `CoordinateSpace`. We encode this intent into the `DebugPrimitive` header using the `SizeMode` enum.

1\. The `SizeMode` Contract

By dedicating a byte to `SizeMode`, the authoritative simulation safely declares _how_ the shape's dimensions (radius, thickness) should react to the camera's projection matrix.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// Dictates how the presentation tier scales the primitive's dimensions.
    /// </summary>
    public enum SizeMode : byte
    {
        /// <summary>
        /// Physical simulation dimension. Shrinks visually as the camera zooms out.
        /// </summary>
        WorldMeters = 0,

        /// <summary>
        /// Absolute screen dimension. Maintains constant thickness regardless of zoom.
        /// </summary>
        ScreenPixels = 1
    }
}
```

2\. Declaring Intent on the Simulation Node

Because the simulation node is entirely decoupled from the active viewport's resolution or zoom level, it never queries the camera. It simply emits the `DebugPrimitive` with the correct semantic intent:

```
public void DrawPathGizmo(ISimulationView view, Entity entity, IDebugDrawBuilder builder)
{
    // A physical road lane constraint. Scales naturally with the map.
    builder.DrawLine(pointA, pointB, Colors.Gray, thickness: 3.5f, sizeMode: SizeMode.WorldMeters);

    // A tactical AI trajectory overlay. Always exactly 2 pixels thick on the glass.
    builder.DrawLine(pointA, pointB, Colors.Cyan, thickness: 2.0f, sizeMode: SizeMode.ScreenPixels);
}
```

3\. Resolution at the Presentation Tier

When the "dumb terminal" presentation client receives the primitive, it resolves the scaling mathematically right before pushing vertices to the GPU.

For `SizeMode.WorldMeters`, the client passes the raw floating-point value to the graphics API, allowing the camera's view matrix to scale it naturally.

For `SizeMode.ScreenPixels`, the client must mathematically counteract the camera's zoom scaling. If we look at how the engine's existing `PerspectiveShapeRenderer` maintains constant pixel thickness across zoom scales, it explicitly divides the requested line thickness by the active camera's zoom factor. We formalize this exact architectural pattern for our gizmo primitive evaluation:

```
// Inside the presentation tier's rendering adapter:
public void RenderPrimitives(RenderContext ctx, ReadOnlySpan<DebugPrimitive> primitives)
{
    // Safe zoom fallback to prevent divide-by-zero
    float safeZoom = ctx.Zoom > 0f ? ctx.Zoom : 1f; // [1]

    foreach (ref readonly var prim in primitives)
    {
        // Evaluate scale intent without branching into separate draw calls.
        // ScreenPixels inversely scales with zoom (which the MapCamera applies to projected coordinates) [2].
        float finalThickness = prim.SizeMode == SizeMode.ScreenPixels
            ? prim.Thickness / safeZoom  // Counteracts camera scaling to stay crisp
            : prim.Thickness;            // Scales naturally with the world
    
        // ... push finalThickness to hardware graphics API ...
    }
}
```

Architectural Triumphs of Resolution Independence

-   **Pristine Simulation Logic:** The ECS simulation node never reads `ctx.Zoom` or pollutes the unmanaged domain with inverse-matrix presentation math. The gizmo code remains purely declarative.-   **Graceful Multi-View Scaling:** Because the scaling math is deferred to the client, a single `DebugPrimitive` array broadcast over DDS can be rendered simultaneously by two different UI windows operating at entirely different zoom levels. Each local `MapCamera` applies its own inverse zoom, ensuring the tactical line is exactly 2 pixels thick on _both_ screens simultaneously.-   **Orthogonal Flexibility:** By mathematically decoupling `CoordinateSpace` from `SizeMode`, developers unlock immense expressiveness. `CoordinateSpace.World` + `SizeMode.ScreenPixels` creates a line that perfectly tracks physical terrain coordinates during a pan, but never shrinks into pixel-dust when the operator zooms out to a continental view.

Let me know when you are ready to proceed to **Chapter 2.5: Hardware Asset Indirection**, where we tackle how to cleanly render complex MIL-STD-2525 symbols and high-res textures without serializing a single image byte over the network!

now 2.5 Hardware Asset Indirection

Chapter 2.5: Hardware Asset Indirection

To graduate from a simple debug-line drawer to a production-grade tactical map engine, we must support rendering rich visual assets: complex MIL-STD-2525 tactical symbols, 3D meshes, and stylized UI icons.

However, we face a strict architectural constraint: **we absolutely cannot serialize textures, vertex buffers, or heavy mesh data over the network per-frame.** Doing so would instantly destroy our bandwidth budget and violate the 64-byte limit of our `DebugPrimitive` struct.

To solve this, we employ **Hardware Asset Indirection**. The simulation node declares _what_ semantic asset to draw using a tiny, blittable string identifier, and the presentation node resolves that identifier against its local, hardware-specific asset libraries.

1\. The Indirection Contract

In the Fast Data Plane (FDP) engine, we avoid heap-allocated `string` objects to maintain zero-allocation pipelines. The engine already utilizes `FixedString32` and `FixedString64` to store inline, null-terminated UTF-8 byte sequences safely within unmanaged components.

For our `DebugPrimitive` struct, we utilize the 32-byte payload union to store this identifier. If the primitive's `Shape` is `DebugPrimitiveShape.Symbol`, the payload is interpreted as a `FixedString32`.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    public interface IDebugDrawBuilder
    {
        /// <summary>
        /// Instructs the client to render a predefined asset (like a MIL-STD symbol or UI icon).
        /// </summary>
        void DrawSymbol(
            Vector3 position, 
            FixedString32 symbolCode, 
            Color32 tint,
            float scale,
            CoordinateSpace space = CoordinateSpace.World,
            SizeMode sizeMode = SizeMode.ScreenPixels);
    }
}
```

The authoritative simulation node just declares its intent using semantic tokens. For example, a tactical AI system drawing an infantry platoon symbol or a behavior gizmo drawing an atlas icon at a waypoint:

```
// "SFGPUCI-------" is the standard MIL-STD-2525 code for Friendly Infantry [3].
builder.DrawSymbol(tf.Position, "SFGPUCI-------", Colors.Blue, 1.5f);

// "b12" represents a specific grid coordinate in the UI IconAtlas [4].
builder.DrawSymbol(waypointPos, "b12", Colors.Yellow, 1.0f);
```

2\. Resolution at the Presentation Tier (The "Dumb Terminal")

When the serialized `DebugPrimitive` reaches the remote Image Generator (IG) or ExCon node, the "dumb terminal" must translate this 32-byte string into hardware rendering calls (e.g., Raylib textures or Stride 3D meshes).

The presentation client queries its local `IEntityShapeLibrary` or `IconAtlas`. Because the `DebugPrimitive` header carries `Color32 Tint`, the client can apply dynamic coloring to grayscale assets via the GPU shader.

```
// Inside the presentation tier's rendering adapter:
public void RenderPrimitives(RenderContext ctx, ReadOnlySpan<DebugPrimitive> primitives)
{
    foreach (ref readonly var prim in primitives)
    {
        if (prim.Shape == DebugPrimitiveShape.Symbol)
        {
            string symbolCode = prim.TextPayload.ToString();

            // Hardware Indirection: The client looks up the texture/mesh locally.
            if (_textureCache.TryGetValue(symbolCode, out var texture))
            {
                // Draw hardware-specific texture (e.g., Raylib)
                Raylib.DrawTextureEx(texture, projectedPos, 0f, prim.Thickness, prim.PrimaryColor);
            }
            else
            {
                // Graceful Degradation: Asset is missing on this client hardware
                DrawFallbackCircle(projectedPos, prim.PrimaryColor); // [7]
            }
        }
    }
}
```

3\. Graceful Degradation

What happens if the authoritative server tells a remote client to draw a highly specific drone symbol (`"SFAPMFQ--------"`), but the remote client's installation is missing that specific PNG file?

Because of our decoupled design, the system **gracefully degrades**. The presentation client detects the cache miss and cleanly falls back to rendering a mathematically generated shape (like a `DrawFallbackCircle`) using the primitive's `PrimaryColor`.

The simulation server never knows, never cares, and never crashes. The operator still sees a red dot indicating a hostile entity, preserving core situational awareness even when graphical fidelity degrades.

Architectural Triumphs of Asset Indirection

-   **Network Bandwidth Conservation:** We transmit complex, highly-detailed military overlays across the cluster using just 64 bytes per instruction. The dense texture data stays perfectly isolated on the client's GPU VRAM.-   **Hardware Agnosticism:** The simulation node has zero knowledge of `Texture2D`, UV coordinates, or DirectX/OpenGL boundaries. It only knows about strings and math.-   **Pristine Domain Logic:** A developer writing an AI Behavior Gizmo doesn't need to write resource-loading logic or manage asset paths. They simply emit `"SFGPUCI-------"` and trust the pipeline.

Let me know when you are ready to proceed to **Chapter 2.6: Deterministic Painter's Algorithm (****DebugLayer** **&** **ZIndex****)**, where we solve the chaotic Z-fighting that occurs when rendering transparent tactical overlays and UI panels from parallel, multi-threaded ECS iterations.

good! now 2.6 Deterministic Painter's Algorithm

Chapter 2.6: Deterministic Painter's Algorithm (`DebugLayer` & `ZIndex`)

In a high-performance, data-oriented engine, concurrency introduces a severe visual challenge. The FDP kernel is designed to process heavy workloads using parallel chunk iteration, aggressively distributing entity queries across CPU cores via mechanisms like `ForEachParallel`.

Because multiple threads emit debug and UI primitives concurrently into a shared, lock-free accumulation buffer, the exact sequential ordering of the `DebugPrimitive` array is entirely non-deterministic. If a remote presentation client blindly draws these primitives in the order they arrive over the network, overlapping elements—such as transparent tactical polygons, entity bounding boxes, or UI panels—will Z-fight and flicker chaotically every single frame based on thread scheduling.

To achieve a professional, stable visualization without sacrificing parallel CPU throughput, we must decouple the _rendering order_ from the _ECS memory layout_ and _thread execution order_. We solve this by encoding a strict, declarative **Painter's Algorithm** intent directly into the primitive header.

1\. The Two-Tiered Sorting Keys

As defined in our 64-byte memory layout, we reserved two specific fields to govern rendering order:

-   **Macro-Ordering (****byte DebugLayer****):** This serves as the primary sort key. It categorizes the primitive into broad conceptual buckets (e.g., Layer 1: Background Grids, Layer 4: Tactical Overlays, Layer 10: UI Tools).-   **Micro-Ordering (****short ZIndex****):** This provides fine-grained, intra-layer sorting. It allows a gizmo to guarantee that a tooltip's text label always renders strictly _after_ (on top of) its solid background panel, even if they share the exact same `DebugLayer`.

By explicitly declaring this intent on the authoritative server, the gizmo logic remains completely immune to the chaos of multi-threaded execution.

```
public void DrawComplexTooltip(ISimulationView view, Vector2 screenPos, IDebugDrawBuilder builder)
{
    // Emitted concurrently from Thread A.
    // The background panel is pushed to ZIndex 0.
    builder.DrawScreenRect(
        screenPos, size, Colors.DarkGray, 
        debugLayer: 10, zIndex: 0);

    // Emitted concurrently from Thread B.
    // The text is pushed to ZIndex 1, guaranteeing it renders on top.
    builder.DrawScreenText(
        screenPos, "Unit Data", Colors.White, 
        debugLayer: 10, zIndex: 1); 
}
```

2\. Resolution at the Presentation Tier

When the serialized array of primitives arrives at the "dumb terminal" (the Image Generator or local map window), the presentation adapter must establish determinism before pushing vertices to the graphics API.

Because the array is already tightly packed in contiguous memory, we can perform an extremely fast stable sort (or radix sort) directly on the 32-byte headers. We combine the `DebugLayer` and `ZIndex` into a single 32-bit integer key.

```
// Inside the presentation tier's rendering adapter:
public void RenderPrimitives(RenderContext ctx, Span<DebugPrimitive> primitives)
{
    // 1. In-place sort using a combined 32-bit key for the Painter's Algorithm.
    // Key layout: [ 16 empty bits | 8 bits DebugLayer | 8 bits ZIndex (shifted to unsigned) ]
    primitives.Sort((ref readonly DebugPrimitive a, ref readonly DebugPrimitive b) =>
    {
        int keyA = (a.DebugLayer << 16) | (ushort)a.ZIndex;
        int keyB = (b.DebugLayer << 16) | (ushort)b.ZIndex;
        return keyA.CompareTo(keyB);
    });

    // 2. Deterministic Rendering
    foreach (ref readonly var prim in primitives)
    {
        // 2a. Fast rejection of user-hidden layers
        if ((ctx.VisibleLayersMask & (1u << prim.DebugLayer)) == 0)
            continue;
    
        // 2b. Draw to hardware API (Z-fighting is now mathematically impossible)
        DispatchHardwareDrawCall(in prim);
    }
}
```

Architectural Triumphs of the Deterministic Painter's Algorithm

-   **Thread-Safe UI Generation:** Developers can safely use `ForEachParallel` to generate highly complex UI layouts and map overlays across hundreds of CPU cores. The final visual composition will always be perfectly stable, frame after frame.-   **Elimination of Depth Buffers for UI:** By sorting strictly on the CPU before drawing, the presentation client does not need to rely on hardware Z-buffers for 2D map elements or screen-space UI. This prevents visual artifacts with alpha-blended transparency (like sensor cones or map polygons) which notoriously fail when relying purely on hardware depth testing.-   **Pristine Separation of Concerns:** The authoritative simulation node dictates the visual hierarchy logically ("Text belongs above Panel"), while the presentation node pays the minimal computational cost to sort the arrays right before pushing pixels to the glass.

Let me know when you are ready to proceed to **Chapter 2.7: Advanced Payloads (Zero-allocation rich text and gradients)**, where we look at how to pack complex data safely into the remaining bytes of our union structure without triggering the Garbage Collector!

now 2.7 Advanced Payloads

Chapter 2.7: Advanced Payloads (Zero-Allocation Rich Text and Gradients)

In a managed runtime like .NET, the Garbage Collector (GC) is the enemy of deterministic simulation performance. If our gizmo framework allowed developers to emit managed `string` objects for map labels or allocate new arrays for gradient color stops every frame, we would induce massive Gen0 heap thrashing, immediately destroying our multi-threaded throughput and causing micro-stutters during execution.

Because our `DebugPrimitive` must traverse the network and remain strictly 64 bytes to perfectly fit a CPU cache line, we must be exceptionally clever with how we utilize the 32-byte Payload Union space for advanced visual features without relying on heap references.

1\. Zero-Allocation Rich Text Badges

Tactical maps require text: unit designations, telemetry readouts, and waypoint names. We cannot place a standard C# `string` reference inside our blittable struct, as it would cause memory corruption when serialized directly into a CycloneDDS packet.

Instead, we leverage the engine's natively implemented `FixedString32` struct. This unmanaged struct acts as a fixed-size 32-byte buffer that stores up to 31 UTF-8 bytes plus a null terminator directly inline, making it perfectly safe for components and network messages.

```
// Inside the 32-byte Payload Union of our DebugPrimitive
[FieldOffset(32)] public FixedString32 TextPayload;
```

When a developer calls `builder.DrawScreenText(pos, "Target Locked", Colors.Red)`, the framework writes the UTF-8 bytes directly into the primitive's stack-allocated memory. The string travels safely over the network without a single heap allocation, and the remote presentation client decodes the span back into hardware-accelerated text rendering (like Raylib text drawing).

2\. Gradient Color Embedding

Visualizing vectors, such as an entity's velocity or a weapon's tracer path, is vastly improved by using color gradients (e.g., a solid yellow tracer fading to transparent red at its origin).

A geometric line only requires a `Vector3 LineStart` (12 bytes) and a `Vector3 LineEnd` (12 bytes). This consumes 24 bytes of our 32-byte payload union, leaving exactly 8 bytes of unused memory. We pack a secondary `Color32` (4 bytes) directly into this remaining space.

```
// Inside the 32-byte Payload Union of our DebugPrimitive
[FieldOffset(32)] public Vector3 LineStart;            // 12 bytes
[FieldOffset(44)] public Vector3 LineEnd;              // 12 bytes
[FieldOffset(56)] public Color32 LineGradientEndColor; // 4 bytes (Offset 56)
```

By adding a `DrawGradientLine` method to our `IDebugDrawBuilder` API, the simulation safely expresses complex visual shading intent.

```
public void DrawVelocityVector(ISimulationView view, Entity entity, IDebugDrawBuilder builder)
{
    ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
    ref readonly var vel = ref view.GetComponentRO<SimVelocity>(entity);

    // A line that fades from solid Green at the entity to transparent Green at the vector tip.
    builder.DrawGradientLine(
        start: tf.Position,
        end: tf.Position + vel.Linear,
        startColor: Colors.Green,
        endColor: new Color32(0, 255, 0, 0), // 0 Alpha
        thickness: 2.0f);
}
```

3\. Resolution at the Presentation Tier

When the "dumb terminal" processes these advanced payloads, it maps them directly to the graphics API primitives. For gradients, Stride or Raylib easily accept different vertex colors for the start and end points of a line. For text, the presentation adapter reads the `FixedString32` byte span and passes it to the font renderer without ever allocating a managed string on the client either.

Architectural Triumphs of Packed Payloads

-   **Strict Zero-Allocation:** We achieve high-fidelity UI rendering (text, gradients, symbols) at 60Hz without ever invoking the .NET Garbage Collector.-   **Memory Density:** By meticulously packing our unions, we squeeze complex presentation semantics into unused padding bytes, guaranteeing that our primitive arrays stream across the DDS network at maximum bandwidth efficiency.-   **Pristine Domain Isolation:** The simulation kernel handles raw logic and math, safely stuffing data into blittable memory blocks without worrying about vertex buffers, font atlases, or shader parameters.

Let me know when you are ready to proceed to **Chapter 3: Gizmo Lifecycle & Hot-Path Execution (3.1 Orthogonal Gizmo Types)**, where we define how the framework actually executes these gizmos inside the ECS without destroying CPU cache locality!

now \*\*3.1 Orthogonal Gizmo Types

Chapter 3: Gizmo Lifecycle & Hot-Path Execution

3.1: Orthogonal Gizmo Types

Having established the strictly 64-byte, zero-allocation primitive payload in Chapter 2, we must now address a fundamental tension in game engine architecture: how do we execute highly heterogeneous visualization logic without destroying the pristine L1 cache locality of our Entity Component System (ECS)?

In a naive Object-Oriented architecture, developers create a `class MyGizmo` and attach it to an entity, resulting in thousands of scattered heap allocations and virtual method dispatch calls (`Update()`, `Draw()`) every frame. This destroys multi-threaded throughput and introduces GC pressure.

To maintain our data-oriented mandates while satisfying the requirement for dynamically installable visualizations, we must decompose gizmos along three orthogonal axes: **Attachment Scope**, **Statefulness**, and **Interactivity**. By enforcing these classifications via strict interface contracts, the FDP kernel can optimize their execution pathways perfectly.

\--------------------------------------------------------------------------------

Axis 1: Attachment Scope (What does the gizmo observe?)

A gizmo's scope dictates how the framework queries the simulation world to feed it data.

-   **Global (System-Scoped):** The gizmo visualizes macro-structures that are independent of any single entity. Examples include rendering the `SpatialHashGrid` boundaries, nav-mesh polygons, or cluster network topology. The framework evaluates these exactly once per frame.-   **Entity-Bound:** The gizmo visualizes properties of a specific entity (e.g., rendering a bounding box around a `SimTransform`, or velocity vectors). The framework evaluates these inside highly parallelized `BitMask256` chunk iterators.-   **Behavior-Bound:** The gizmo only exists while a specific cognitive state or mission phase is active on an entity. For example, a `SweepAndClearGizmo` should only be evaluated if the entity's `BehaviorState.ActiveBehaviorHash` matches the Sweep-and-Clear identifier. This prevents us from iterating thousands of entities that are just idling.

Axis 2: Statefulness (How does the gizmo manage memory?)

This is the most critical architectural distinction for performance. We divide gizmos strictly into Stateless Projectors and Stateful Instances.

**1\. The Stateless Projector (****IStatelessEntityGizmo****)** 90% of tactical visualizations are pure mathematical projections of current ECS state (e.g., drawing a line from A to B based on velocity). These gizmos must have **zero instantiation**. They are effectively static functions that the kernel invokes inside AVX2-accelerated `EntityQuery` loops.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// A pure, allocation-free data projector. The framework requires no instantiation
    /// and can execute this safely across hundreds of parallel threads.
    /// </summary>
    public interface IStatelessEntityGizmo
    {
        // Declares the exact ECS component mask required (e.g., SimTransform + SimVelocity)
        BitMask256 RequiredComponents { get; }
        
        void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder builder);
    }
}
```

**2\. The Stateful Instance (****IStatefulEntityGizmo****)** Some visualizations inherently require memory. If an AI debugging gizmo needs to draw a fading trail of the last 50 pathfinding requests, it must maintain a circular buffer. These require explicit initialization and teardown hooks, tied directly to the entity's ECS lifecycle.

```
namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// A stateful gizmo instance. Handled via zero-allocation object pools by the framework.
    /// </summary>
    public interface IStatefulEntityGizmo
    {
        void OnAttached(ISimulationView view, Entity entity);
        void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder builder);
        void OnDetached(ISimulationView view, Entity entity);
    }
}
```

Axis 3: Interactivity (Does the gizmo mutate the world?)

A visualization framework becomes a true mapping engine when gizmos can become interactive tools.

-   **Passive Gizmos:** Emit primitives into the `IDebugDrawBuilder` and never read input.-   **Interactive Gizmos (Tools):** When activated, they push an `IMapTool` proxy onto the client's `MapCanvas` stack. They specify `InputBehavior.Interactive` in their primitive headers, allowing them to intercept mouse clicks and drags. Crucially, they do not mutate the ECS directly. When dragged, they publish controlled intents (e.g., `UpdateEntityAttributeCommand` or `MapCommandRequest`) back onto the `FdpEventBus` to preserve strict CQRS and deterministic simulation rules.

Architectural Triumphs of the Orthogonal Design

By forcing developers to declare their gizmos along these three axes, the orchestration kernel achieves massive optimizations:

-   O(1) **SIMD Filtering:** If a developer registers an `IStatelessEntityGizmo` that watches for `WeaponState`, the kernel simply adds the gizmo's `Execute` pointer to the `SystemPhase.PostSimulation` loop, guarded by an `EntityQuery`. It costs absolutely nothing when no weapons are active.-   **L1 Cache Preservation:** Because stateless gizmos are executed directly inline during chunk iteration, the `SimTransform` data they read is already hot in the L1 CPU cache from the physics systems that just ran.-   **Deterministic GC:** By isolating stateful tracking into explicitly declared `IStatefulEntityGizmo` contracts, we can pool their allocations. The framework intercepts `DestructionOrder` events on the bus to call `OnDetached`, returning the gizmo's arrays to the pool without the Garbage Collector ever getting involved.

Let me know when you are ready to proceed to **Chapter 3.2: Stateless Projectors (**O(1) **SIMD-accelerated ECS query execution)**, where we will examine the exact hot-path execution loop that drives these interfaces.

3.1. please, it was truncated

Chapter 3: Gizmo Lifecycle & Hot-Path Execution

Chapter 3.1: Orthogonal Gizmo Types (Statefulness, Interactivity, and Attachment Scope)

Now that we have established our strictly 64-byte, network-transportable primitive stream in Chapter 2, we must design the execution model. In a data-oriented engine like FDP, we cannot rely on classic Object-Oriented paradigms—like allocating a new `DebugVisualizer` object per entity and calling a `virtual Draw()` method thousands of times a frame. That approach shatters L1 cache locality and triggers Garbage Collection (GC) thrashing.

Instead, we treat gizmos as **stateless systems** or **pooled processors** that operate over contiguous arrays of ECS data. To make the framework robust and extensible, we categorize every gizmo along three strictly orthogonal architectural axes: **Attachment Scope**, **Statefulness**, and **Interactivity**.

Here is how we formally define these axes in C#.

1\. Attachment Scope (What does it bind to?)

A gizmo must explicitly declare the data domain it represents. This determines how the framework queries the ECS world and feeds data into the gizmo's execution loop.

-   **Global Gizmos:** Entity-independent overlays. Examples include drawing the physical NavMesh, a coordinate grid, or a global diagnostic UI panel. The framework executes these exactly once per frame, passing the entire `ISimulationView` to let the gizmo query singletons.-   **Entity-Bound Gizmos:** Tied to the existence of a specific ECS component (e.g., `VehicleState` or `RadarSensor`). The framework uses SIMD-accelerated `BitMask256` queries to find matching entities and executes the gizmo for each match.-   **Behavior-Bound Gizmos:** Tied to a concretely running AI behavior (e.g., a specific FastBTree or FastHSM instance). The framework filters entities not just by the `BehaviorState` component, but by checking if the `ActiveBehaviorHash` matches the gizmo's target domain.

2\. Statefulness (Ephemeral vs. Persistent)

The hot path of a 60Hz simulation requires extreme care with memory. We divide gizmos into two lifecycle contracts:

-   **Stateless (Ephemeral):** The vast majority of gizmos. They hold zero instance data. The framework evaluates the ECS chunk, the gizmo emits declarative `DebugPrimitive` shapes directly from the mathematical state of the components, and then it is done.-   **Stateful (Persistent):** Required for heavy debug visualizations that cannot be recalculated every frame (e.g., a complex A\* pathfinding corridor or a convex hull projection). Stateful gizmos receive `OnInitialize` and `OnTeardown` callbacks. The gizmo allocates its native unmanaged buffers (or reuses a fixed pool) during initialization and simply draws from that cached state during the `UpdateAndDraw` tick.

3\. Interactivity (Passive vs. Interactive)

Gizmos must seamlessly bridge the gap between simple line-drawing and first-class map tools.

-   **Passive Visualizers:** They only consume data and emit visual primitives to the `IDebugDrawBuilder`.-   **Interactive Tools:** They declare hit-test boundaries (`InputMode = Interactive`) and implement interaction interfaces. If the operator clicks the gizmo's primitive on the remote terminal, the framework routes a `MapClickEvent` back to the gizmo. Crucially, the gizmo does _not_ mutate the ECS state directly inside the rendering loop. Instead, it emits standard FDP commands (e.g., `UpdateEntityAttributeCommand`) to the event bus, maintaining strict CQRS separation of concerns.

The C# Interface Contracts

To enforce this clean architecture, we provide highly specific interfaces. A developer simply implements the interface that matches their required orthogonality, and the framework's reflection/source-generation layer handles the boilerplate ECS query generation.

```
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // ── 1. Stateless Entity Gizmo ─────────────────────────────────────────
    // Perfect for 90% of use cases. Zero allocations, executes inside a fast 
    // chunk-iteration loop.
    public interface IEntityGizmo<T> where T : unmanaged
    {
        void Draw(
            ISimulationView view, 
            Entity entity, 
            ref readonly T component, 
            IDebugDrawBuilder builder);
    }

    // ── 2. Stateful Global Gizmo ──────────────────────────────────────────
    // Perfect for heavy, entity-independent systems like a NavMesh visualizer.
    public interface IStatefulGlobalGizmo : IDisposable
    {
        // Called once when the gizmo is enabled
        void OnInitialize(ISimulationView view); 
        
        // Called every frame
        void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder builder);
        
        // Called when disabled or during engine teardown
        void OnTeardown(); 
    }
    
    // ── 3. Interactive Behavior Gizmo ─────────────────────────────────────
    // Perfect for tuning an active AI node. Emits both visual primitives and 
    // handles remote tool inputs via the CQRS pattern.
    public interface IInteractiveBehaviorGizmo
    {
        // Associates this gizmo with a specific behavior ID
        uint TargetBehaviorHash { get; }
    
        void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder builder);
        
        // Receives input payloads routed back from the remote terminal
        void OnRemoteInput(
            Entity entity, 
            GizmoInputEvent inputEvent, 
            IEntityCommandBuffer cmd);
    }
}
```

Architectural Elegance

By strictly categorizing gizmos along these three axes, we completely decouple the _authoring_ of a debug tool from the _execution_ of it. The developer writes pure logic ("Draw a sphere if the tank is moving"). The overarching `GizmoExecutionSystem` evaluates visibility policies, manages the native memory pools for stateful caches, and routes network input back to the interactive tools safely via the `IEntityCommandBuffer` without ever disrupting the parallel simulation threads.

Let me know when you are ready for **Chapter 3.2: Stateless Projectors (**O(1) **SIMD-accelerated ECS query execution)**, where we explore exactly how the kernel executes thousands of `IEntityGizmo` instances without trashing the CPU cache!

3.2 Stateless Projectors

Chapter 3: Gizmo Lifecycle & Hot-Path Execution

Chapter 3.2: Stateless Projectors (O(1) SIMD-Accelerated ECS Query Execution)

The vast majority of debug and tactical visualizations—drawing velocity vectors, sensor cones, physical bounding boxes, or pathfinding splines—are simply geometric projections of current component state. They do not require historical memory, circular buffers, or object pooling.

In the Fast Data Plane (FDP) engine, we treat these as **Stateless Projectors** (`IEntityGizmo<T>`). By enforcing absolute statelessness, we allow the FDP kernel to execute these gizmos directly inside the engine's core execution loops, leveraging pristine CPU cache locality and hardware-accelerated SIMD instructions.

Here is the architectural breakdown of how the FDP kernel dispatches thousands of stateless gizmos in under a millisecond without allocating a single byte on the managed heap.

1\. The Power of AVX2 Component Filtering

Before the gizmo even executes, the framework must find the relevant entities. In a traditional Object-Oriented engine, you might iterate a list of "Gizmo Components" attached to game objects. This creates massive memory fragmentation.

In FDP, entities are tightly packed into 64KB `NativeChunkTable` blocks. Each entity has a 256-bit component mask (`BitMask256`) defining its layout. When the `GizmoExecutionSystem` evaluates which entities should be drawn by `VelocityGizmo : IEntityGizmo<SimVelocity>`, it does not check entities individually.

Instead, the engine uses **SIMD (Single Instruction, Multiple Data)**. The `BitMask256.Matches` function utilizes `Avx2` intrinsics to compare 32-byte masks in a single CPU cycle. Furthermore, FDP employs **Chunk Skipping**. If the engine determines that an entire 64KB chunk of entities does not contain the `SimVelocity` component, it skips the entire memory block instantly, making the query O(populated chunks) rather than O(total entities).

2\. The Internal Dispatch System

The framework automatically generates an internal wrapper system for every `IEntityGizmo<T>` registered by developers. This wrapper acts as the bridge between the parallelized ECS query and the specific gizmo logic.

```
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Visuals;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Internal
{
    /// <summary>
    /// Auto-generated system that drives a stateless gizmo across the ECS world.
    /// Executes in the PostSimulation phase.
    /// </summary>
    public sealed class StatelessGizmoDispatchSystem<TGizmo, TComponent> : IEcsModuleSystem
        where TGizmo : struct, IEntityGizmo<TComponent> // Struct guarantees no heap allocs
        where TComponent : unmanaged
    {
        private EntityQuery? _query;
        private readonly IGizmoVisibilityPolicy _visibilityPolicy;

        public StatelessGizmoDispatchSystem(IGizmoVisibilityPolicy visibilityPolicy)
        {
            _visibilityPolicy = visibilityPolicy;
        }
    
        public void Execute(ISimulationView view, float deltaTime)
        {
            // 1. Global Visibility Gate (Hoisted O(1) Check)
            if (!_visibilityPolicy.ShouldEvaluate(view))
                return;
    
            var repo = (EntityRepository)view;
            _query ??= repo.Query().With<TComponent>().Build();
    
            // Fetch the thread-local debug draw builder to accumulate 64-byte primitives
            var builder = GizmoContext.GetThreadLocalBuilder();
    
            // 2. Parallel, chunk-aware iteration utilizing SIMD rejection
            _query.ForEachParallel(entity =>
            {
                // 3. Per-Entity Visibility Gate (e.g. "Is selected?", "Is in culling frustum?")
                if (!_visibilityPolicy.IsVisible(view, entity))
                    return;
    
                // 4. Retrieve data (L1 cache hot) and execute purely stateless logic
                ref readonly var component = ref repo.GetComponentRO<TComponent>(entity);
                
                var gizmo = new TGizmo(); 
                gizmo.Draw(view, entity, in component, builder);
                
            }, ParallelHint.Light); // Adaptive batching for tiny payloads
        }
    }
}
```

3\. The Developer Experience

Because the heavy lifting, multithreading, and memory safety are completely encapsulated in the dispatcher, the developer writing the actual gizmo writes pure, declarative domain logic.

Here is an example of a developer authoring a velocity vector visualizer:

```
namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // The struct itself is stateless. No fields, no allocations.
    public readonly struct VelocityGizmo : IEntityGizmo<SimVelocity>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Draw(
            ISimulationView view, 
            Entity entity, 
            ref readonly SimVelocity velocity, 
            IDebugDrawBuilder builder)
        {
            // The velocity component is passed by ref. 
            // If we need the transform, we fetch it (it's likely in the L1 cache already)
            if (!view.HasComponent<SimTransform>(entity)) return;
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);

            // Emit the 64-byte blittable primitive into the contiguous accumulation buffer.
            builder.DrawGradientLine(
                start: tf.Position,
                end: tf.Position + velocity.Linear,
                startColor: Colors.Green,
                endColor: Colors.Transparent,
                thickness: 2.0f,
                space: CoordinateSpace.World,
                sizeMode: SizeMode.ScreenPixels);
        }
    }
}
```

Architectural Triumphs of the Stateless Projector

-   **Perfect L1 Cache Coherency:** Because this dispatcher runs in the `PostSimulation` phase (immediately after physics and AI have updated the `SimTransform` and `SimVelocity` arrays), the ECS chunks are already hot in the CPU's L1/L2 caches. We extract the visualization data while the processor is already looking at it.-   **Infinite Scaling via** **ForEachParallel****:** FDP's adaptive batching slices the populated chunks into optimal workloads. The stateless gizmo evaluates 10,000 entities across 16 cores concurrently. The output `DebugPrimitive` arrays are accumulated into thread-local contiguous buffers, completely eliminating lock contention.-   **Zero Garbage Collection:** The gizmo instance is a C# `readonly struct`. It is allocated on the stack and instantly discarded. The text payloads use `FixedString32`, and the vectors are passed by `ref`. The .NET Garbage Collector remains entirely dormant during the rendering pass.

Let me know when you are ready for **Chapter 3.3: Stateful (Ephemeral) Memory Management**, where we tackle the hard problem: how to efficiently render historical trails (like a pathfinding corridor or a history of AI evaluations) without violating our zero-allocation, pool-driven mandates!

3.3 Stateful (Ephemeral) Memory Management

Chapter 3: Gizmo Lifecycle & Hot-Path Execution

Chapter 3.3: Stateful (Ephemeral) Memory Management

While stateless projectors gracefully handle 90% of tactical rendering, certain visualizations fundamentally require historical context. Consider an AI debugging tool that draws a fading trail of an entity's last 60 pathfinding requests, or a radar visualizer maintaining a circular buffer of recently dropped sensor contacts.

In a traditional Object-Oriented engine, developers solve this by calling `new List<Vector3>()` or instantiating historical buffers per entity. In a high-frequency simulation, this creates catastrophic Gen0 Garbage Collection (GC) pressure, destroying frame pacing and causing micro-stutters.

To support complex historical tooling without violating our strict zero-allocation mandates, we introduce **Stateful (Ephemeral) Memory Management** driven by the engine's native lifecycle event bus.

1\. The Stateful Contract (`IStatefulEntityGizmo`)

Instead of executing as a static SIMD query, stateful gizmos are state-holding objects managed entirely by the framework's internal object pools. The framework guarantees that a developer will receive explicit `OnAttached` and `OnDetached` callbacks, allowing them to safely borrow and return unmanaged memory or pooled arrays.

```
using System.Buffers;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Visuals;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A stateful gizmo that tracks an entity's historical positions.
    /// </summary>
    public sealed class HistoricalTrailGizmo : IStatefulEntityGizmo
    {
        // Rented from a shared pool to prevent GC allocations
        private Vector3[]? _positionHistory; 
        private int _headIndex;
        private int _count;
        private const int MaxHistory = 60;

        public void OnAttached(ISimulationView view, Entity entity)
        {
            // Zero-allocation memory leasing [1]
            _positionHistory = ArrayPool<Vector3>.Shared.Rent(MaxHistory);
            _headIndex = 0;
            _count = 0;
        }
    
        public void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime, IDebugDrawBuilder builder)
        {
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            
            // 1. Update circular buffer
            _positionHistory![_headIndex] = tf.Position;
            _headIndex = (_headIndex + 1) % MaxHistory;
            if (_count < MaxHistory) _count++;
    
            // 2. Draw the historical trail
            for (int i = 0; i < _count - 1; i++)
            {
                int curr = (_headIndex - 1 - i + MaxHistory) % MaxHistory;
                int prev = (curr - 1 + MaxHistory) % MaxHistory;
    
                // Fades out based on age
                byte alpha = (byte)(255 * (1.0f - (i / (float)_count)));
                builder.DrawLine(
                    _positionHistory[curr], 
                    _positionHistory[prev], 
                    new Color32(0, 255, 255, alpha));
            }
        }
    
        public void OnDetached(ISimulationView view, Entity entity)
        {
            // Return memory deterministically; no GC involvement [1]
            if (_positionHistory != null)
            {
                ArrayPool<Vector3>.Shared.Return(_positionHistory);
                _positionHistory = null;
            }
        }
    }
}
```

2\. Event-Driven Deterministic Teardown

The hardest problem in ECS memory management is knowing _when_ to clean up ephemeral state. If we poll every frame to see if `view.IsAlive(entity)` returns false, we waste CPU cycles iterating over thousands of orphaned objects just to clean them up.

Because the FDP kernel relies on strict, event-driven orchestration via the `FdpEventBus`, the `GizmoExecutionSystem` does not poll. Instead, it hooks directly into the engine's `EntityLifecycleEvent` and `DestructionOrder` streams.

When an entity is destroyed or transitioning into `EntityLifecycle.TearDown`, the framework reads the `DestructionOrder` from the bus during the `BeforeSync` or `PostSimulation` phase. It immediately locates any stateful gizmos bound to that entity index and invokes `OnDetached`.

3\. The Framework Instance Pool

The `HistoricalTrailGizmo` class instances themselves must not be allocated per-entity. The framework maintains a generic `ConcurrentStack<T>` pool for the gizmo wrapper objects themselves, identical to how it pools `EntityRepository` snapshots.

When a visibility policy dictates that a new entity should be visualized by a stateful gizmo:

-   The framework pops an unused `HistoricalTrailGizmo` instance from its internal stack.-   It calls `OnAttached`, passing the entity handle.-   The gizmo rents its `Vector3[]` from `ArrayPool<Vector3>.Shared`.-   When the entity dies (caught via `DestructionOrder`), the framework calls `OnDetached`.-   The gizmo returns its array to the `ArrayPool`.-   The framework pushes the `HistoricalTrailGizmo` instance back onto the `ConcurrentStack`.

Architectural Triumphs of Ephemeral Pooling

-   **Absolute GC Immunity:** By pooling both the gizmo objects and their internal buffers, you can create and destroy tens of thousands of complex historical visualization tools per minute without the Garbage Collector ever waking up.-   O(1) **Teardown Routing:** We completely bypass the need to iterate through tracking dictionaries to find dead entities. `DestructionOrder` events provide explicit, deterministic teardown signals.-   **Encapsulated Complexity:** The author of the debug visualizer writes clean, encapsulated setup and teardown logic without knowing anything about ECS chunk lifecycles, memory pinning, or network state.

Let me know when you are ready to shift into **Chapter 3.4: The Strategy Pattern for Visibility**, where we discuss the `IGizmoVisibilityPolicy` and how to prevent the engine from executing 10,000 gizmos when the operator only wants to look at a single selected tank!

3.4 The Strategy Pattern for Visibility

Chapter 3: Gizmo Lifecycle & Hot-Path Execution

Chapter 3.4: The Strategy Pattern for Visibility

In a data-oriented Entity Component System (ECS), branching is the enemy of throughput. If we naively hardcode visibility checks—like `if (GlobalSettings.ShowPaths && entity.IsSelected)`—directly inside the `IEntityGizmo<T>.Draw` method, we commit two grave architectural sins. First, we violate the Single Responsibility Principle by mixing domain projection logic with tooling state. Second, we destroy L1 cache locality and CPU branch prediction by repeatedly polling global singletons from inside a highly parallelized chunk iteration loop.

To build a professional debug visualization framework, visibility must be policy-driven, not hardcoded. The simulation engine should evaluate a dedicated visibility policy, and if it passes, execute the gizmo.

We achieve this strict separation of concerns by introducing the Strategy Pattern via the `IGizmoVisibilityPolicy` contract. This contract guarantees **Two-Tiered Evaluation**, completely removing branching from the hot-path payload.

1\. The `IGizmoVisibilityPolicy` Contract

To keep the engine running at 60Hz, we split visibility into two orthogonal checks: a global hoist (Tier 1) and a per-entity gate (Tier 2).

```
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Visuals
{
    /// <summary>
    /// Defines the execution gating strategy for a gizmo.
    /// </summary>
    public interface IGizmoVisibilityPolicy
    {
        /// <summary>
        /// TIER 1 (Hoisted): Evaluated exactly once per frame, before any ECS chunk 
        /// iteration begins. If this returns false, the entire system execution is aborted.
        /// </summary>
        bool ShouldEvaluate(ISimulationView view);

        /// <summary>
        /// TIER 2 (Per-Entity): Evaluated during SIMD chunk iteration for matching entities.
        /// Evaluates local spatial or selection constraints.
        /// </summary>
        bool IsVisible(ISimulationView view, Entity entity);
    }
}
```

2\. Concrete Visibility Strategies

By implementing this interface, we can provide standard, reusable visibility strategies that cover the most useful predicates in a game engine, such as global settings toggles, entity selection state, and frustum culling.

Strategy A: Selection-Only Gating

A very common requirement is to only draw a complex gizmo (like a weapon threat dome or pathfinding corridor) when the operator explicitly clicks on the entity.

Using the engine's `SelectionState` component, we can write a policy that evaluates purely off unmanaged ECS data without ever touching the UI layer.

```
using Fdp.Toolkit.Diagnostics.Visuals;
using Hrot.IG.Components; // SelectionState

namespace Fdp.Toolkit.Diagnostics.Policies
{
    public sealed class SelectionOnlyPolicy : IGizmoVisibilityPolicy
    {
        public bool ShouldEvaluate(ISimulationView view) 
        {
            // Global fast-pass: The system always evaluates, 
            // relying on the tight entity filter below.
            return true; 
        }

        public bool IsVisible(ISimulationView view, Entity entity)
        {
            // TIER 2: Fast L1 cache read from the same chunk being iterated.
            if (!view.HasComponent<SelectionState>(entity)) return false;
            
            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
            return sel.IsSelected; //
        }
    }
}
```

Strategy B: Global Setting and Frustum Culling

If we are rendering thousands of AI trajectory lines, we want them hidden by default to save screen clutter and network bandwidth. Furthermore, if they _are_ enabled, we only want to serialize the primitives for entities currently visible inside the camera's viewport.

We can combine our `GizmoSettingsRegistry` with the engine's pre-computed `CullingState`.

```
namespace Fdp.Toolkit.Diagnostics.Policies
{
    public sealed class GlobalSettingAndCullingPolicy : IGizmoVisibilityPolicy
    {
        private readonly uint _settingHash;

        public GlobalSettingAndCullingPolicy(string settingKey)
        {
            // Precompute the FNV-1a hash at startup for O(1) lookups
            _settingHash = Fnv1a.Hash(settingKey); 
        }
    
        public bool ShouldEvaluate(ISimulationView view)
        {
            // TIER 1: Hoisted check.
            // If the user has toggled "Show Trajectories" off in the global ImGui settings, 
            // this returns false. The execution system aborts in O(1) time without 
            // allocating threads or iterating a single chunk.
            var registry = view.GetManagedComponentRO<GizmoSettingsRegistry>(RepositoryAdapter.SingletonEntity);
            return registry.GetBool(_settingHash, defaultValue: false);
        }
    
        public bool IsVisible(ISimulationView view, Entity entity)
        {
            // TIER 2: Entity is only drawn if the camera viewport encompasses its position.
            if (!view.HasComponent<CullingState>(entity)) return true; // Default visible
            
            ref readonly var culling = ref view.GetComponentRO<CullingState>(entity);
            return culling.IsVisible; //
        }
    }
}
```

3\. Execution inside the Kernel (The Triumphant Result)

Look back at the `StatelessGizmoDispatchSystem` from Chapter 3.2. Because we pass the `IGizmoVisibilityPolicy` into the dispatcher, the kernel effortlessly orchestrates the strategy pattern:

-   **The** O(1) **Abort:** Before requesting threads from the thread pool, the kernel calls `_visibilityPolicy.ShouldEvaluate(view)`. If a global setting is disabled, the system instantly returns. Zero CPU cycles wasted.-   **The** O(N) **Parallel Filter:** The kernel spins up 16 threads to evaluate chunks using `ForEachParallel`. Inside the tight loop, it calls `_visibilityPolicy.IsVisible(view, entity)`. Because `SelectionState` and `CullingState` are unmanaged structs packed linearly in the same chunk tables, this check operates flawlessly inside the L1 CPU cache.-   **Pristine Gizmos:** The actual developer writing the `TrajectoryGizmo` writes pure, unconditional mathematics. They never have to write an `if (selected)` statement again.

By enforcing a strict Strategy Pattern for visibility, your engine dynamically limits network bandwidth and CPU cycles to _only_ what the operator explicitly wants to see, scaling seamlessly across millions of entities.

Let me know when you are ready to shift into **Chapter 4: Distributed Configuration & Registry (4.1 The** **GizmoSettingsRegistry****)**, where we architect the system that holds these global debug toggles and distributes their JSON schema to remote dumb terminals!

4.1 The \`GizmoSettingsRegistry

Chapter 4: Distributed Configuration & Registry

Chapter 4.1: The `GizmoSettingsRegistry` (Architecture of the Managed Singleton)

In a traditional game engine, debug settings and tool toggles are often implemented as globally mutable `static` flags (e.g., `public static bool ShowNavMesh = false;`). From a clean architecture perspective, this is a catastrophic anti-pattern. Global static state destroys the ability to run multiple simulation worlds in parallel within the same process (a strict requirement for our Fast Data Plane engine when running predictive rollouts, background AI simulations, or concurrent unit tests).

To ensure our configuration is safely sandboxed, network-transportable, and perfectly integrated with the ECS lifecycle, we model the global gizmo configuration as a **Managed ECS Singleton**.

Here is the architectural blueprint for the `GizmoSettingsRegistry`.

1\. The Singleton Contract

Because the configuration payload contains dynamic collections (dictionaries) and strings (descriptions, JSON schemas), it cannot be a pure unmanaged struct. We define it as a managed class component (`Tier 2` storage in FDP).

Crucially, we decorate it with `[DataPolicy(DataPolicy.Transient)]`. This guarantees that while the registry is globally accessible within the running simulation, the engine's serialization systems will never attempt to save these temporary debug toggles into binary game saves or flight recorder snapshots.

```
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    /// <summary>
    /// A managed ECS singleton storing global tuning values and toggles for all gizmos.
    /// Safely isolated per-world to prevent cross-contamination in multi-world clusters.
    /// </summary>
    [ComponentId(150)] // Example reserved ID for the toolkit
    [DataPolicy(DataPolicy.Transient)] 
    public sealed class GizmoSettingsRegistry
    {
        // Maps a 32-bit FNV-1a hash to the actual setting payload
        private readonly Dictionary<uint, GizmoSettingValue> _settings = new();
        private readonly Dictionary<uint, string> _hashToName = new();

        // Used to track deltas for remote terminals
        public uint Version { get; private set; } = 1;
    
        // ... API omitted for brevity ...
    }
}
```

2\. The Configuration Payload (`GizmoSettingValue`)

To avoid polymorphic object allocations (which trigger the Garbage Collector) for every single configuration value, we represent the values using a custom Tagged Union struct. This struct perfectly packs a boolean, integer, or floating-point value into a safe memory footprint.

```
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    public enum SettingType : byte { Bool, Int, Float }

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct GizmoSettingValue
    {
        [FieldOffset(0)] public readonly SettingType Type;
    
        // Tagged Union payloads
        [FieldOffset(1)] public readonly bool BoolValue;
        [FieldOffset(1)] public readonly int IntValue;
        [FieldOffset(1)] public readonly float FloatValue;
    
        // Constructors enforce type safety
        public GizmoSettingValue(bool value) : this() { Type = SettingType.Bool; BoolValue = value; }
        public GizmoSettingValue(float value) : this() { Type = SettingType.Float; FloatValue = value; }
    }
}
```

3\. Initialization and Injection (The Cold Path)

When a module boots up (e.g., the `AutonomousPerceptionModule` or `GroundKinematicsModule`), it registers its required debug settings directly into the registry during the initialization phase.

Developers register settings using readable, dot-separated strings (e.g., `"Perception.ShowSensorCones"`). Under the hood, the registry immediately hashes this string using the FNV-1a algorithm to generate a stable `uint` key.

```
public void RegisterSetting(string key, GizmoSettingValue defaultValue, string description = "")
{
    uint hash = Fnv1a.Hash(key); // O(N) over the string length, but only runs once at startup
    
    // We only insert if it doesn't exist, preserving values if they were loaded from disk
    if (_settings.TryAdd(hash, defaultValue))
    {
        _hashToName[hash] = key;
        Version++;
    }
}
```

4\. Retrieval during Execution (The Hot Path)

As demonstrated in Chapter 3.4 with the `IGizmoVisibilityPolicy`, the execution pipeline queries these settings thousands of times per frame.

Because we hashed the keys at startup, the hot-path lookup is an O(1) integer-keyed dictionary read. The execution system never allocates strings, never hashes strings, and never deals with string comparison overhead during the simulation loop.

```
// Inside a Visibility Policy or an interactive Map Tool
public bool GetBool(uint hashedKey, bool fallback = false)
{
    if (_settings.TryGetValue(hashedKey, out GizmoSettingValue val) && val.Type == SettingType.Bool)
    {
        return val.BoolValue;
    }
    return fallback;
}
```

The system accesses the registry using the FDP engine's highly optimized singleton API:

```
var registry = view.GetSingletonManaged<GizmoSettingsRegistry>(); //
bool isVisible = registry.GetBool(_precomputedHash);
```

Architectural Triumphs of the Registry

-   **World Isolation:** By storing the registry as an ECS component via `SetSingletonManaged<T>`, if the engine runs a background predictive rollout (a clone of the world), the clone receives a safe, deep copy of the `GizmoSettingsRegistry`. Debug tools toggled in the main view will not accidentally disrupt the predictive simulation.-   **Zero-Allocation Hot Path:** The translation of human-readable strings into FNV-1a integer hashes completely eliminates `string` allocations and string-equality overhead in the `Update` loop.-   **Reactive Binding:** The internal `Version` integer automatically increments when settings are registered or mutated. A separate system monitors this integer, and when it changes, it triggers the schema serialization pipeline to push the updated `StructEdit` JSON out to the remote DDS topics, keeping the "dumb terminals" perfectly in sync.

Let me know when you are ready to proceed to **Chapter 4.2: Fast-Path Configuration (**O(1) **FNV-1a hashed keys and tagged union values)**, where we will dive deeper into how we enforce type safety and memory efficiency inside this dictionary without boxing!

4.2 Fast-Path Configuration

Chapter 4: Distributed Configuration & Registry

Chapter 4.2: Fast-Path Configuration (O(1) FNV-1a Hashed Keys and Tagged Union Values)

In high-performance, data-oriented engines, querying configuration state inside a tight loop is a notorious performance killer. If a stateless projector evaluating 10,000 entities checks a global setting using a string key (`GetSetting("VehicleGizmo.ShowVelocity")`), it forces the CPU to compute a string hash and execute a character-by-character memory comparison on every iteration. This destroys instruction pipelining and guarantees L1 cache misses.

Furthermore, if the configuration registry returns a polymorphic `object` or an interface, retrieving the value forces an unboxing operation, which immediately pollutes the Gen0 heap and awakens the Garbage Collector.

To maintain our strict zero-allocation mandates, we resolve configuration via a combination of **AOT (Ahead-Of-Time) FNV-1a Hashing** and **Blittable Tagged Unions**.

1\. Deterministic FNV-1a Hashing

Instead of passing strings into the execution hot path, the `GizmoSettingsRegistry` translates all human-readable keys into 32-bit integers during the initialization phase. We utilize the Fowler–Noll–Vo (FNV-1a) hash algorithm, standard across the FDP engine for generating deterministic, collision-resistant fingerprints (similar to how `ComponentLayoutHasher` generates memory layout hashes).

When a developer authors a visibility policy or a gizmo, they precompute the hash once:

```
public sealed class VehicleVelocityVisibilityPolicy : IGizmoVisibilityPolicy
{
    private readonly uint _settingHash;

    public VehicleVelocityVisibilityPolicy()
    {
        // O(N) string hashing done exactly once during construction
        _settingHash = Fnv1a.Hash("VehicleGizmo.ShowVelocity");
    }
    
    public bool ShouldEvaluate(ISimulationView view)
    {
        // O(1) integer-keyed dictionary lookup on the hot path
        var registry = view.GetSingletonManaged<GizmoSettingsRegistry>();
        return registry.GetBool(_settingHash);
    }
    // ...
}
```

2\. Defeating the Boxing Trap (Tagged Unions)

A configuration registry must hold heterogeneous data: booleans, integers, and floating-point tuning values. A naive implementation uses `Dictionary<uint, object>`, which requires boxing value types upon insertion and unboxing upon retrieval, violating our zero-allocation mandate.

To solve this elegantly in C#, we use explicit struct layouts to create a C-style Tagged Union. By forcing the fields to overlap at the exact same memory address (`[FieldOffset(1)]`), we pack the value into a dense, strictly blittable footprint.

```
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    public enum SettingType : byte { Bool, Int, Float }

    /// <summary>
    /// A blittable tagged union preventing GC allocations during configuration lookups.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct GizmoSettingValue
    {
        // Discriminator tag at byte 0
        [FieldOffset(0)] public readonly SettingType Type;
    
        // Overlapping payloads at byte 1. The struct occupies exactly 5 bytes.
        [FieldOffset(1)] public readonly bool BoolValue;
        [FieldOffset(1)] public readonly int IntValue;
        [FieldOffset(1)] public readonly float FloatValue;
    
        public GizmoSettingValue(bool value) : this() { Type = SettingType.Bool; BoolValue = value; }
        public GizmoSettingValue(float value) : this() { Type = SettingType.Float; FloatValue = value; }
    }
}
```

3\. Execution Safety and Throughput

When the registry executes `TryGetValue(_settingHash, out GizmoSettingValue val)`, it returns a struct by value. The method internally checks the `val.Type` discriminator before returning the specific overlapping field.

If a remote "dumb terminal" terminal attempts to patch a boolean setting with a float value via JSON, the registry simply rejects the mismatched type enum, ensuring absolute type safety.

Architectural Triumphs of the Fast-Path

-   **Zero Garbage Collection:** Reading a setting returns a small struct entirely on the stack. The .NET Garbage Collector is completely blind to this operation, preserving pristine execution pacing.-   **Branch Predictability:** By resolving the string to an integer hash ahead of time, the hot path reduces configuration checks to a highly predictable O(1) integer hash map lookup.-   **Pristine Cache Locality:** The internal buckets of the `Dictionary<uint, GizmoSettingValue>` contain only dense, unmanaged primitives rather than sparse heap pointers.

Let me know when you are ready to proceed to **Chapter 4.3: Execution Thread Safety (Hoisted polling vs. deferred event invalidation)**, where we tackle how to safely rebuild heavy gizmo caches without race conditions!

4.3 Execution Thread Safety

Chapter 4: Distributed Configuration & Registry

Chapter 4.3: Execution Thread Safety (Hoisted Polling vs. Deferred Event Invalidation)

When building a high-performance engine, mutating global configuration state is incredibly dangerous. Imagine a scenario where a remote operator drags a "Sensor Range" slider in their ImGui terminal. The network translator receives this command and overwrites the value in the `GizmoSettingsRegistry`. If this overwrite occurs at the exact microsecond that 16 worker cores are iterating through 10,000 entities via `ForEachParallel`, we suffer a catastrophic data race: half the entities evaluate using the old range, and half evaluate using the new range.

Worse, if we wrap the dictionary in a `lock()`, we immediately destroy our multi-threaded throughput and stall the entire simulation pipeline.

To maintain perfect thread safety and determinism, we handle configuration reads via **Hoisted Polling**, and configuration mutations via **Deferred Event Invalidation** over the engine's double-buffered event bus.

1\. Hoisted Polling for Stateless Projectors

For our `IEntityGizmo<T>` stateless projectors (which execute across hundreds of chunks in parallel), we explicitly forbid reading from the `GizmoSettingsRegistry` _inside_ the `ForEachParallel` loop.

Reading a dictionary inside a parallel loop—even a lock-free, read-only one—wastes CPU cycles and pollutes the L1 cache. Instead, we **hoist** the setting evaluation to Tier 1 of our `IGizmoVisibilityPolicy`. The kernel's `StatelessGizmoDispatchSystem` reads the configuration exactly once on the main thread, stores the boolean or float in a local stack variable, and passes that immutable state into the parallel worker threads.

Because the registry's internal `Dictionary<uint, GizmoSettingValue>` is never structurally mutated during the `PostSimulation` phase, the parallel reads are 100% thread-safe and lock-free.

2\. Deferred Event Invalidation for Stateful Caches

While stateless gizmos evaluate the UI configuration purely on the fly, **Stateful Gizmos** (like a complex `NavMeshGizmo` that builds a vertex array of the map) cannot afford to poll a setting every frame to see if they need to rebuild their heavy caches.

To solve this, we rely on the FDP engine's `FdpEventBus`. The event bus is strictly double-buffered: events published in Frame N are only visible for consumption in Frame N+1. This guarantees that mid-frame configuration changes are deferred to safe boundaries.

We define an unmanaged event struct:

```
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    [EventId(1500)] 
    public struct GizmoSettingChangedEvent
    {
        public uint HashedKey;
        public GizmoSettingValue NewValue;
    }
}
```

When the network egress system receives a JSON patch from the remote terminal, it applies the change to the `GizmoSettingsRegistry` and publishes the event:

```
// Inside GizmoSettingsRegistry (called during Input phase):
public void SetValue(uint hash, GizmoSettingValue newValue)
{
    if (_settings.TryGetValue(hash, out var current) && current.Equals(newValue))
        return; // No change

    _settings[hash] = newValue;
    Version++;
    
    // Publish to the double-buffered event bus
    _eventBus.Publish(new GizmoSettingChangedEvent 
    { 
        HashedKey = hash, 
        NewValue = newValue 
    });
}
```

3\. Consuming the Invalidation Event

Now, the heavy stateful gizmo simply checks the event stream during its `UpdateAndDraw` loop. Because it uses `view.ReadEvents<T>()`, it consumes a contiguous, thread-safe memory span of all settings changed in the _previous_ frame.

```
public sealed class ComplexNavMeshGizmo : IStatefulGlobalGizmo
{
    private readonly uint _showNavMeshHash = Fnv1a.Hash("NavMesh.ShowPolygons");
    private bool _needsRebuild = true;
    private Vector3[]? _cachedVertices;

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder builder)
    {
        // 1. O(E) Event Iteration (Extremely fast, usually 0 events)
        var events = view.ReadEvents<GizmoSettingChangedEvent>(); //
        foreach (ref readonly var evt in events)
        {
            if (evt.HashedKey == _showNavMeshHash)
            {
                _needsRebuild = true; // The user toggled the setting!
            }
        }
    
        // 2. Safely rebuild heavy caches only when explicitly invalidated
        if (_needsRebuild)
        {
            RebuildNavMeshCache(view);
            _needsRebuild = false;
        }
    
        // 3. Emit primitives from cache...
        DispatchCachedPrimitives(builder);
    }
}
```

Architectural Triumphs of Thread Safety

-   **Zero Mid-Frame Tearing:** Because network mutations are routed through the `FdpEventBus`, a setting cannot change its value halfway through a `ForEachParallel` loop. The visualization remains perfectly cohesive for the entire 16.6ms frame.-   **Elimination of Locks:** The hot-path execution systems never encounter a `lock()` keyword when checking debug visibility.-   **Reactive Rebuilding:** Heavy stateful gizmos remain dormant, costing virtually zero CPU cycles, until they read an explicit `GizmoSettingChangedEvent` span, preventing redundant computations and perfectly isolating configuration UI from visualization logic.

Let me know when you are ready to proceed to **Chapter 4.4: Global Schema Side-Channels**, where we unveil how we reflect over this very registry to dynamically build the `StructEdit.Json` schemas that power the remote UI panels!

4.4: Global Schema Side-Channels

Chapter 4: Distributed Configuration & Registry

Chapter 4.4: Global Schema Side-Channels (Translating Settings into `StructEdit.Json` Schemas)

In a distributed cluster, the fundamental challenge of remote debugging is the UI boundary. If a developer creates a new `AutonomousDroneGizmo` and adds a "Show Sensor Radius" toggle to the `GizmoSettingsRegistry`, how does the remote Image Generator (IG) or ExCon terminal know to render a checkbox for it?

Historically, engines solve this by sharing compiled C# DTO assemblies between the server and the client, or by hardcoding specific debug UI panels on the client side. Both approaches violate our core architecture: sharing assemblies breaks the "dumb terminal" isolation, and hardcoding UI forces presentation updates every time a backend developer adds a debug feature.

To solve this, we rely on **Global Schema Side-Channels** powered by the `StructEdit` framework. We translate the active configuration state into a dynamically serialized JSON instruction tree and broadcast it over DDS.

1\. The `StructEdit` Instruction Tree

The `StructEdit` library was specifically designed to reflect over C# ECS components (or our `GizmoSettingsRegistry`) exactly once, producing a tree of `EditNode` objects. Each node represents an editable unit—such as a `Scalar`, `Boolean`, or `DynamicArray`—complete with its metadata (`EditRange`, `EditUnit`) and a memory-safe `IValueBinding`.

Instead of the remote terminal querying the server's memory, the simulation server periodically dumps this instruction tree into a standardized JSON schema.

```
{
  "structedit_version": "1.0",
  "rootTypeName": "Fdp.Toolkit.Diagnostics.Configuration.GizmoSettingsRegistry",
  "scope": "$",
  "nodes": [
    {
      "path": "$.DroneGizmo.ShowSensorRadius",
      "kind": "Boolean",
      "value": true
    },
    {
      "path": "$.NavMesh.Opacity",
      "kind": "Scalar",
      "value": 0.75
    }
  ]
}
```

2\. The Schema Publisher System

We do not want to serialize this schema every frame, as reflection and string building violate our hot-path performance mandates. Instead, we utilize the `Version` integer built into the `GizmoSettingsRegistry` (as discussed in Chapter 4.1).

We create a dedicated, low-frequency `SystemPhase.Export` system. It monitors the `Version` integer. If the version has incremented (meaning a new gizmo registered a setting, or an existing setting was changed), it triggers the `StructEdit.Json` serialization pipeline.

```
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using StructEdit.Core;
using StructEdit.Json;

namespace Fdp.Toolkit.Diagnostics.Configuration
{
    [UpdateInPhase(SystemPhase.Export)]
    public sealed class ConfigurationSchemaPublisherSystem : IEcsModuleSystem
    {
        private readonly IComponentEditService _editService;
        private readonly IDdsWriter<GizmoUiStateTopic> _uiStateWriter; // Emits to CycloneDDS
        private uint _lastPublishedVersion = 0;

        public ConfigurationSchemaPublisherSystem(IComponentEditService editService, IDdsWriter<GizmoUiStateTopic> writer)
        {
            _editService = editService;
            _uiStateWriter = writer;
        }
    
        public void Execute(ISimulationView view, float deltaTime)
        {
            var registry = view.GetSingletonManaged<GizmoSettingsRegistry>();
            
            // O(1) integer check. Bypasses all serialization if nothing changed.
            if (registry.Version == _lastPublishedVersion) 
                return;
    
            // 1. Open an edit session over the registry
            using IEditSession session = _editService.Open(registry, typeof(GizmoSettingsRegistry));
    
            // 2. Generate the StructEdit JSON schema
            string schemaJson = session.ToJson();
    
            // 3. Publish the schema side-channel over DDS (Transient-Local durability)
            _uiStateWriter.Write(new GizmoUiStateTopic 
            { 
                SchemaJson = schemaJson 
            });
    
            _lastPublishedVersion = registry.Version;
        }
    }
}
```

3\. Client-Side UI Generation (The Dumb Terminal)

Because the DDS topic is configured with `TransientLocal` durability, any remote terminal that connects to the cluster immediately receives the latest `GizmoUiStateTopic` sample.

The terminal takes the JSON payload and feeds it into the `ImGuiPropertyTree` renderer. The presentation tier loops over the `nodes` array in the JSON and emits the corresponding Dear ImGui widgets: `ImGui.Checkbox` for `Boolean` nodes, and `ImGui.SliderFloat` for `Scalar` nodes.

The remote operator sees a fully populated, hierarchical properties panel of debug toggles, despite the client application having absolutely zero compile-time knowledge of the backend simulation components.

Architectural Triumphs of Schema Side-Channels

-   **Absolute Client/Server Decoupling:** We completely eliminate the need to distribute C# DTO assemblies to presentation clients. The server declares its tool parameters via strings and JSON; the client blindly renders them.-   **Cold-Path Serialization:** By gating the JSON generation behind the `registry.Version` integer, the expensive serialization pipeline executes only when necessary, maintaining pristine 60Hz loop times during normal execution.-   **Data-Driven Tooling Extensibility:** A backend AI programmer can add a tuning slider for an A\* heuristic simply by calling `registry.RegisterSetting("AI.HeuristicWeight", 1.5f)`. The slider will automatically appear on all connected ExCon and IG debug panels in the cluster without a single line of UI code being written.

Let me know when you are ready to proceed to **Chapter 4.5: Configuration Persistence (Delta-tracking defaults for clean JSON disk saves)**, where we look at how to persist these tuned debug values across application restarts!

4.5: Configuration Persistenc

Chapter 4: Distributed Configuration & Registry

Chapter 4.5: Configuration Persistence (Delta-Tracking Defaults for Clean JSON Disk Saves)

From a clean architecture perspective, persisting user configuration to disk is notoriously prone to a specific maintenance nightmare: **The Default-Value Trap**.

If a framework blindly serializes the entire `GizmoSettingsRegistry` to a JSON file, it explicitly writes down the developer-authored default values alongside the user's manual tweaks. Six months later, if a gameplay programmer decides that the default "Pathfinding Corridor Opacity" should be `0.8f` instead of `0.5f`, operators across the studio will never see the update. Their local JSON files will stubbornly override the new optimal default with their serialized `0.5f` ghost value.

To guarantee forward-compatibility and keep our configuration files perfectly clean and source-control friendly, we implement **Delta-Tracking Persistence**. The registry strictly segregates default intentions from user mutations, serializing only the deltas.

1\. Upgrading the Internal Storage

We modify the internal dictionary of our `GizmoSettingsRegistry` (introduced in Chapter 4.1). Instead of simply mapping a hashed key to a value, we map it to a tracking entry that preserves the origin state.

```
namespace Fdp.Toolkit.Diagnostics.Configuration
{
    internal struct RegistryEntry
    {
        public GizmoSettingValue DefaultValue;
        public GizmoSettingValue CurrentValue;

        // O(1) blittable equivalence check to determine if the user mutated this setting
        public readonly bool HasUserOverride => !CurrentValue.Equals(DefaultValue);
    }
    
    public sealed class GizmoSettingsRegistry
    {
        // Replaces the basic Dictionary<uint, GizmoSettingValue>
        private readonly Dictionary<uint, RegistryEntry> _settings = new();
        private readonly Dictionary<uint, string> _hashToName = new();
    
        public void RegisterSetting(string key, GizmoSettingValue defaultValue)
        {
            uint hash = Fnv1a.Hash(key);
            
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_settings, hash, out bool exists);
            if (!exists)
            {
                entry.DefaultValue = defaultValue;
                entry.CurrentValue = defaultValue;
                _hashToName[hash] = key;
            }
            else
            {
                // If the setting already exists (e.g., loaded from disk before registration),
                // we ONLY update the default value backing field, preserving their override.
                entry.DefaultValue = defaultValue;
            }
        }
    }
}
```

2\. The Serialization Cold-Path

Because the registry is marked with `[DataPolicy(DataPolicy.Transient)]`, it is safely ignored by the engine's binary Flight Recorder and binary network checkpointing systems. Instead, we persist it explicitly as a human-readable file (e.g., `gizmo_user_prefs.json`) during the engine shutdown sequence or via an explicit "Save Layout" command.

When saving to disk, the registry iterates its entries and builds a dictionary containing _only_ the settings where `HasUserOverride` is true. We utilize the engine's canonical `FdpJsonOptionsRegistry.Indented` alongside the `JsonAestheticFormatter` to produce pristine, readable JSON.

```
using System.Text.Json;
using System.IO;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Serialization;

public void SaveToDisk(string filePath)
{
    var deltaPayload = new Dictionary<string, object>();

    foreach (var kvp in _settings)
    {
        if (kvp.Value.HasUserOverride)
        {
            // Resolve the human-readable string key for the JSON
            string stringKey = _hashToName[kvp.Key];
            deltaPayload[stringKey] = ExtractBoxedValue(kvp.Value.CurrentValue);
        }
    }
    
    // Utilize FDP's strict, indented JSON registry
    string rawJson = JsonSerializer.Serialize(deltaPayload, FdpJsonOptionsRegistry.Indented);
    
    // Apply aesthetic formatting to keep numeric arrays on single lines
    string cleanJson = JsonAestheticFormatter.FlattenNumericArrays(rawJson);
    
    File.WriteAllText(filePath, cleanJson);
}
```

3\. The Graceful Boot Sequence

When the `ClusterRunner` or a local client boots, it loads this JSON file _before_ the domain modules call `RegisterSetting`.

```
public void LoadFromDisk(string filePath)
{
    if (!File.Exists(filePath)) return;

    string json = File.ReadAllText(filePath);
    var overrides = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
        json, FdpJsonOptionsRegistry.DefaultRelaxed);
    
    if (overrides == null) return;
    
    foreach (var kvp in overrides)
    {
        uint hash = Fnv1a.Hash(kvp.Key);
        
        // We write the parsed value directly into the CurrentValue slot.
        // When the domain systems later call RegisterSetting(), they will populate 
        // the DefaultValue slot, seamlessly activating the delta-tracking.
        _settings[hash] = new RegistryEntry 
        { 
            CurrentValue = ParseJsonElement(kvp.Value) 
        };
    }
}
```

Architectural Triumphs of Delta-Tracking

-   **Source-Control Friendly:** By saving only what the user explicitly changed, your generated JSON files drop from 5,000 lines of configuration to just 5 or 6 lines. This eliminates noisy merge conflicts in team environments.-   **Absolute Forward Compatibility:** If a backend engineer adds 50 new diagnostic gizmos to the AI module, they will instantly appear with their correct defaults on all operator machines, because the operators' local JSON files will not contain stale override keys for them.-   **Immutability of Intent:** The codebase remains the absolute authoritative source of truth for _how things should look by default_, while the `GizmoSettingsRegistry` seamlessly injects the operator's personal preferences exactly where required.

This concludes the data plane and configuration pipelines. Let me know when you are ready to transition into **Chapter 5: First-Class UI & Interactive Tooling (5.1 Network-Transportable IMGUI)**, where we bridge this robust backend data directly into the operator's mouse clicks!

5: First-Class UI & Interactive Tooling

Chapter 5: First-Class UI & Interactive Tooling

To elevate a visualization framework from a passive observation window to a professional-grade tactical toolset, we must bridge the gap between simulation data and operator input. In legacy game engines, debug UIs and map tools are tightly coupled to the local application loop, relying on direct memory pointers and hardcoded `ImGui` draw calls. This architecture fundamentally breaks in a distributed cluster where the simulation is headless.

We resolve this by enforcing a strict Command Query Responsibility Segregation (CQRS) loop combined with schema-less remote UI rendering. The headless server dictates the available interactions, the "dumb terminal" client provides the hardware input capture, and all mutations are routed safely back to the authoritative kernel.

5.1 Network-Transportable IMGUI (Schema-less DTOs)

To render property inspectors and debug panels on a remote terminal without distributing binary C# assemblies, we utilize the `StructEdit` framework.

Instead of hardcoding `ImGui.SliderFloat()` calls on the server, the authoritative node uses reflection exactly once to classify a component's memory layout and generate an `EditDocument` instruction tree. This tree maps editable units (Scalars, Booleans, DynamicArrays) to stable `EditNodeId` integers. The document is then serialized into a strictly defined JSON schema (version 1.0) and broadcasted over the network.

When the remote terminal receives this JSON, it passes the payload into a recursive immediate-mode renderer, such as the `ComponentEditDrawer` or `ImGuiPropertyTree`. The presentation tier iterates the node tree, rendering `ImGui.Checkbox` for booleans or `ImGui.SliderFloat` for scalars, completely oblivious to the underlying simulation logic. The terminal blindly renders the UI, achieving a decoupled, network-transportable ImGui implementation.

5.2 The Map Tool Proxy Pattern

For spatial interactions—such as drawing routes, placing units, or rotating entities—we cannot rely on UI panels alone. The operator must interact directly with the 2D or 3D viewport. We handle this via the **Map Tool Proxy Pattern**.

Interactive gizmos do not run on the server; instead, the authoritative node sends a declarative command (e.g., `CMD_PLACE_ENTITY` or `CMD_START_AUTHORING`) over the `MapCommandRequest` DDS topic. The remote client's `MapCommandController` intercepts this request and pushes a stateful proxy implementation of `IMapTool` (such as the `CreationTool` or `PointSequenceTool`) onto the local `MapCanvas` tool stack.

The proxy tool lives entirely on the client, providing zero-latency visual feedback (like a ghost preview of an entity following the cursor) without requiring a network round-trip for every mouse movement.

5.3 Hit-Test Opaqueness & Input Bubbling

To prevent interactive tools from conflicting with camera panning or background map layers, the `MapCanvas` enforces a strict input bubbling hierarchy.

Every frame, hardware inputs (clicks, drags, hovers) are routed to the `ActiveTool` first. The `IMapTool` interface defines contracts like `HandleClick(Vector2 worldPos, MouseButton button)` and `HandleDrag`. If the active proxy tool recognizes the interaction, it returns `true`. This immediately marks the input as consumed, providing "hit-test opaqueness" and preventing the event from bubbling down to the `MapCamera` or the underlying `IMapLayer` collection.

5.4 Round-Trip Mutations (Strict CQRS)

The most critical architectural mandate of this framework is that **gizmos and map tools must never directly mutate ECS state**. Allowing a remote UI tool to overwrite simulation memory directly introduces race conditions and shatters determinism.

When the operator completes an interaction—for example, releasing the mouse button after dragging a waypoint—the local proxy tool simply pops itself from the stack and fires a completion callback. This callback publishes a highly constrained intent event, such as a `SpawnEntityCommand` or an `UpdateEntityAttributeCommand`, onto the client's local event bus.

A dedicated egress translator (e.g., `SpawnEntityCommandEgressTranslator` or `UpdateEntityAttributeCommandEgressTranslator`) intercepts this local event, serializes the command parameters into JSON, and transmits a formal request back over CycloneDDS to the authoritative server. The headless server receives the request, validates the intent against domain logic, and safely applies the structural mutation to the ECS world via an `IEntityCommandBuffer`.

Let me know when you are ready to proceed to **Chapter 6: Presentation Tier Resolution (Client Adapters)**, where we finalize the architecture by examining how the "dumb terminal" efficiently filters, interpolates, and renders these raw primitive streams onto the glass!

we went too quickly, pls detail 5.1 Network-Transportable IMGUI

Let us dissect the Network-Transportable IMGUI pipeline. In traditional engine architectures, exposing a new backend simulation variable to a remote debug tool requires modifying a C# DTO, updating network serialization logic, distributing the compiled assemblies to the client, and finally writing hardcoded `ImGui` draw calls on the presentation tier. This tight coupling is an architectural nightmare for distributed simulation clusters.

To achieve a true "dumb terminal" isolation, we implemented a schema-less, network-transportable UI pipeline powered by our internal `StructEdit` framework.

Here is the technical anatomy of how we decoupled the UI rendering from the authoritative domain logic.

1\. Server-Side Reflection & Document Building (The Cold Path)

When the authoritative server wants to expose ECS component data or global settings to a remote operator, it uses the `StructEdit` library. This library is completely independent of Dear ImGui or any specific UI framework.

When a session is opened, the `IEditDocumentBuilder` uses C# reflection exactly once to scan the target component's memory layout. It builds an abstract `EditDocument` consisting of a hierarchical tree of `EditNode` objects.

To prevent string allocations on the hot path, every node is assigned a stable integer identity (`EditNodeId`) at build time. The builder classifies each field into an `EditNodeKind` (such as `Scalar`, `Boolean`, `Enum`, `FixedBuffer`, or `DynamicArray`) and extracts editor-hint attributes (like `[EditRange]` or `[EditUnit]`) into `EditNodeMetadata`.

2\. Schema-less JSON Serialization

Because we refuse to share binary C# assemblies with the remote presentation clients, the server serializes this `EditDocument` instruction tree into a standardized JSON payload.

The resulting JSON schema (version 1.0) describes the editable surface of the data purely through abstract node kinds and metadata, entirely stripping away the underlying C# implementation details.

```
{
  "structedit_version": "1.0",
  "rootTypeName": "Fdp.Toolkit.Diagnostics.Configuration.GizmoSettingsRegistry",
  "scope": "$",
  "nodes": [
    {
      "path": "$.DroneGizmo.ShowSensorRadius",
      "kind": "Boolean",
      "value": true
    }
  ]
}
```

3\. Client-Side Rendering (`ComponentEditDrawer`)

When the remote Image Generator or ExCon terminal receives this JSON payload over the DDS network, it passes the data into a recursive immediate-mode renderer, specifically the `ComponentEditDrawer` or the read-only `ImGuiPropertyTree`.

The `ComponentEditDrawer` acts as the pure presentation layer. It iterates through the `EditNode` tree and maps the abstract `EditNodeKind` to specific hardware UI calls:

-   **Container Nodes:** `Struct`, `Class`, and `DynamicArray` nodes generate collapsible ImGui tree nodes.-   **Leaf Nodes:** `EditNodeKind.Boolean` maps to `ImGui.Checkbox`.-   **Metadata Integration:** For an `EditNodeKind.Scalar` (like a float), the drawer checks the `EditNodeMetadata`. If a minimum and maximum range were provided via `[EditRange]`, it gracefully emits an `ImGui.SliderFloat`. If no range is present, it falls back to an unbounded `ImGui.InputFloat`.

```
// Inside the presentation client's ComponentEditDrawer
if (type == typeof(float))
{
    float v = value is float f ? f : 0f;
    bool ok = (meta.Min.HasValue && meta.Max.HasValue)
        ? ImGui.SliderFloat("##v", ref v, (float)meta.Min.Value, (float)meta.Max.Value)
        : ImGui.InputFloat("##v", ref v, 0f, 0f);
    if (ok) value = v;
    return ok;
}
```

4\. Zero-Allocation Map Picking

The framework also bridges spatial map interactions directly into the generic property tree. If the backend developer decorated a component field with `[MapPickableEntity]`, the `StructEdit` metadata captures this intent.

The `ComponentEditDrawer` detects this attribute and automatically appends a "Pick Entity" or "Pick Map" button next to the standard input field. Clicking this button pushes an interactive tool onto the local `MapCanvas` stack, allowing the operator to select an entity in the 3D world. The resolved entity ID is then fed back into the abstract UI node without the underlying rendering loop knowing anything about the game's targeting logic.

Architectural Triumphs

This separation of concerns guarantees absolute client/server decoupling. A backend AI engineer can add a tuning parameter or a new debug array to an ECS component, and the remote operator terminal will immediately render the correct sliders and collapsible headers. The UI automatically adapts to the simulation data structure, preserving the pristine, stateless nature of the remote clients.

5.2 Hit-Test Opaqueness

Chapter 5.2: Hit-Test Opaqueness & Input Bubbling

In any professional interactive visualization framework, input resolution is just as critical as rendering. If an operator clicks and drags an interactive 3D rotation gizmo, but the map camera underneath simultaneously interprets that exact same mouse drag as a "pan" command, the user experience immediately degrades into chaos. Similarly, if the operator clicks a Dear ImGui button overlaying the map, we must guarantee that an entity standing behind that button in world space is not accidentally selected.

To solve this, the FDP engine enforces strict **Hit-Test Opaqueness** through a top-down **Input Bubbling Pipeline**. We treat mouse and keyboard events as consumable resources. Once a layer or tool successfully handles an input, it becomes "opaque," absorbing the event and preventing it from bubbling down to lower systems.

1\. The `IMapTool` Input Contract

At the core of this architecture is the `IMapTool` interface. Rather than using event-driven C# `Action` callbacks that execute non-deterministically, interactive tools explicitly evaluate spatial input each frame and return a boolean indicating whether they consumed the interaction.

```
namespace Fdp.Toolkit.Vis2D.Abstractions
{
    /// <summary>
    /// Map tool interface for different interaction modes.
    /// Uses the State Pattern for tool switching.
    /// </summary>
    public interface IMapTool
    {
        // ... Lifecycle and Draw methods ...
        
        // Input evaluation. Returning 'true' consumes the input.
        bool HandleClick(Vector2 worldPos, MouseButton button);
        bool HandleDrag(Vector2 worldPos, Vector2 delta);
        bool HandleHover(Vector2 worldPos);
        
        /// <summary>
        /// Return true to mark the key as consumed so it does not bubble
        /// to other handlers (camera, main loop, etc.).
        /// </summary>
        bool HandleKeyPressed(KeyboardKey key) => false;
    }
}
```

2\. The Input Bubbling Pipeline (`MapCanvas`)

The orchestration of this opaqueness is handled entirely by the `MapCanvas`, which isolates the hardware input state (via `IInputProvider`) from the underlying map layers and tools.

Every frame, the canvas executes `ProcessInputPipeline()`, enforcing a strict hierarchy of event consumption:

-   **Hardware UI (ImGui) Gate:** First, the engine checks if the native UI layer (like a Dear ImGui property panel) wants to capture the mouse (`_input.IsMouseCaptured`). If true, the entire map input pipeline aborts immediately.-   **Active Tool Interception:** The top-most `IMapTool` on the stack gets the first chance to evaluate world-space inputs.-   **Map Layer Fallback:** If the tool ignores the input, the canvas iterates through all active `IMapLayer` implementations (from top to bottom), allowing them to consume the event.

Here is the architectural implementation of the `MapCanvas` input router:

```
protected virtual void ProcessInputPipeline()
{
    KeyboardConsumedByTool = false;

    // 1. HARDWARE UI GATE
    // If the operator is interacting with an ImGui panel, abort map interaction.
    if (_input.IsMouseCaptured) return; 
    
    Vector2 mouseScreen = _input.MousePosition;
    Vector2 mouseWorld = Camera.ScreenToWorld(mouseScreen);
    Vector2 deltaWorld = _input.MouseDelta * (1.0f / Camera.Zoom);
    
    bool leftDown = _input.IsMouseButtonDown(MouseButton.Left);
    bool leftReleased = _input.IsMouseButtonReleased(MouseButton.Left);
    bool consumed = false;
    
    if (ActiveTool != null)
    {
        // 2. ACTIVE TOOL INTERCEPTION
        // Tools handle drags first. If HandleDrag returns true, it sets the consumed flag.
        if (leftDown)
        {
            if (ActiveTool.HandleDrag(mouseWorld, deltaWorld))
            {
                consumed = true;
                _isDraggingInteraction = true;
            }
        }
    
        // Handle Click only if we aren't dragging, to prevent accidental selections on drop
        if (!_isDraggingInteraction && leftReleased)
        {
            if (ActiveTool.HandleClick(mouseWorld, MouseButton.Left)) 
                consumed = true;
        }
    }
    
    // 3. MAP LAYER FALLBACK
    // If the ActiveTool didn't consume the click, let map layers try.
    if (!consumed && leftReleased)
    {
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (!IsLayerVisible(layer)) continue;
    
            // If a layer consumes the input (e.g., clicking a specific overlay), stop bubbling.
            if (layer.HandleInput(mouseWorld, MouseButton.Left, false)) 
                break; 
        }
    }
}
```

3\. Spatial Resolution & Intent

Crucially, standard rendering layers do _not_ mutate selection state or dispatch commands during this phase. A pure map layer (like the `EntityRenderLayer`) simply exposes spatial data via `PickEntity(worldPos)`.

It is the active `IMapTool` (such as the `StandardInteractionTool` or an `EntityPickerTool`) that calls `PickTopmostEntity` and decides what to do with the result, ensuring that selection logic remains perfectly decoupled from the rendering pipeline. When an entity is successfully hit by the tool, it fires an intent event (like an `OnEntitySelectRequest`), cleanly bridging the "dumb terminal" input back to the simulation domain without breaking architectural boundaries.

Architectural Triumphs of Hit-Test Opaqueness

-   **Zero-UI Leakage:** By gating the input pipeline behind `_input.IsMouseCaptured`, we guarantee that operators never accidentally issue movement commands to units hiding beneath a configuration menu.-   **Deterministic State Mechanics:** The separation between `_isDraggingInteraction` and `HandleClick` mathematically prevents an operator from accidentally registering a selection click at the end of a long camera pan or tool drag operation.-   **Decoupled Composability:** Map layers provide structural depth, but tools hold the behavioral intent. You can seamlessly push an `EntityPickerTool` onto the canvas to resolve an AI behavior target, and it will effortlessly consume inputs and pop itself without requiring any modifications to the underlying renderer or camera systems.

5.3 The Map Tool Proxy Pattern

Chapter 5.3: The Map Tool Proxy Pattern

In a headless, distributed simulation cluster, interactive spatial tooling presents a massive architectural bottleneck. If an operator wants to place a new tank on the map, draw a complex multi-point patrol route, or drag an area overlay, we cannot simply send every mouse `Hover` and `Drag` event over the network to the authoritative server. Doing so would saturate the network bandwidth and introduce intolerable input latency, destroying the user experience.

Conversely, we cannot allow the remote "dumb terminal" (the Image Generator or ExCon UI) to mutate the authoritative simulation state directly. That would violate our strict Command Query Responsibility Segregation (CQRS) boundaries and introduce race conditions.

To achieve zero-latency UX while maintaining pristine domain isolation, we rely on the **Map Tool Proxy Pattern**. The authoritative node dispatches a declarative _intent_ to the client, and the client pushes a stateful, local proxy object onto its `MapCanvas` to handle the high-frequency hardware inputs.

Here is the architectural anatomy of this pattern.

1\. The Remote Trigger (`MapCommandRequest`)

When an operator clicks "Place Unit" or "Draw Route" in the ExCon UI, the ExCon does not spawn an entity. Instead, it generates a unique correlation ID and broadcasts a `MapCommandRequest` over CycloneDDS to the target IG windows.

This request contains a strictly defined `CommandType` (like `CMD_PLACE_ENTITY`, `CMD_START_AUTHORING`, or `CMD_PICK_LOCATION`) and a JSON payload containing the required arguments (such as the requested entity type).

```
namespace Hrot.NED.Messages
{
    [DdsTopic("MapCommandRequest")]
    public partial struct MapCommandRequest
    {
        public Guid RequestId;           // Correlation ID
        public int MapId;                // Target window (0 = broadcast)
        public CommandType Type;         // e.g., CMD_PLACE_ENTITY
        public string CommandArgsJson;   // e.g., { "contextId": "...", "entityType": 8001 }
    }
}
```

2\. The Local Orchestrator (`MapCommandController`)

When the remote "dumb terminal" (the IG) receives this request, it hits the `MapCommandController`. This class acts as the orchestrator for tool activation. It decouples the UI interaction mechanics from the network protocol.

The controller parses the incoming JSON, instantiates the appropriate local proxy tool (which implements the `IMapTool` interface), and pushes it onto the `MapCanvas` tool stack.

```
// Inside MapCommandController
public void ActivatePlacementCommand(
    Guid requestId,
    Guid contextId,
    long tkbType,
    string? initialPropertiesJson)
{
    // Clean up any stale sessions
    ClearSession(); 
    
    _sessionRequestId = requestId;
    _sessionContextId = contextId;
    
    // Instantiate the local proxy tool
    var tool = new CreationTool(
        onEntityCreated: OnEntityCreatedByTool, // Local callback
        tkbType: tkbType,
        initialPropertiesJson: initialPropertiesJson,
        autoPopOnPlace: true);
    
    // Push it to the top of the canvas input stack
    _canvas.PushTool(tool); 
}
```

3\. The Local Proxy Execution (`CreationTool`)

Once pushed, the `CreationTool` takes over. Because it executes entirely on the local IG client, it intercepts the 60Hz `HandleHover`, `HandleDrag`, and `HandleClick` callbacks with absolutely zero network latency.

If the operator moves the mouse, the proxy tool handles `Draw()` to render a translucent "ghost" preview of the tank snapping to the cursor. The server is completely unaware that this preview is happening.

When the operator finally commits the action (e.g., via a left-click), the tool constructs a highly specific domain command and fires its completion delegate before popping itself off the canvas.

```
public class CreationTool : IMapTool
{
    public void Draw(RenderContext ctx)
    {
        // Zero-latency local preview rendering
        Raylib.DrawCircle((int)_currentMouseWorld.X, (int)_currentMouseWorld.Y, Radius, GhostColor);
    }

    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            // Build the discrete domain intent
            var cmd = new SpawnEntityCommand
            {
                TkbType = _tkbType,
                InitialTransform = new SimTransform { Position = new Vector3(worldPos, 0) }
            };
            
            _onEntityCreated?.Invoke(cmd); // Route back to the Controller
            _canvas.PopTool();             // Exit the proxy session
            return true;                   // Consume the input
        }
        
        if (button == MouseButton.Right)
        {
            _canvas.PopTool(); // Cancel without committing
            return true;
        }
        return false;
    }
}
```

4\. The Asynchronous Closure (`MapCommandAck`)

When the proxy tool invokes the `OnEntityCreatedByTool` delegate, the `MapCommandController` serializes the `SpawnEntityCommand` into a DDS `CreateEntityRequest` and transmits it to the authoritative SimHost.

Simultaneously, the controller must inform the originating ExCon UI about the lifecycle of the session. It publishes a `MapCommandAck` over DDS. To gracefully support tools that allow multiple placements (e.g., placing a sequence of waypoints or multiple units), the framework utilizes specific `StatusCode` semantics:

-   **Status 1 (Intermediate):** An entity was confirmed, but the tool proxy is still active on the stack.-   **Status 0 (Finished):** The interaction is complete, the proxy tool has popped, and the ExCon UI can safely drop its pending state.-   **Status 2 (Cancelled):** The operator right-clicked or pressed ESC; the proxy popped without mutating anything.

Architectural Triumphs of the Proxy Pattern

-   **Uncompromised Latency:** The operator enjoys a 60 FPS, buttery-smooth UX with instant visual feedback (like ghost entities, line-drawing, or measurement reticles) because the interaction loop runs entirely in local memory.-   **Bandwidth Conservation:** The network is completely shielded from high-frequency input noise. For a complex 50-point route drawing session, the server only receives a single compressed command payload at the very end of the gesture.-   **Pristine CQRS:** The "dumb terminal" IG client never writes to ECS component arrays. It evaluates spatial math natively, but only emits intent-based events (e.g., `SpawnEntityCommand` or `UpdateEntityAttributeCommand`) to the backend.

Let me know when you are ready to wrap up Chapter 5 with **5.4 Round-Trip Mutations**, where we trace exactly how that `SpawnEntityCommand` travels from the local bus, across CycloneDDS, and safely into the authoritative ECS kernel!

5.4 Declarative Inspector Extensions

Chapter 5.4: Declarative Inspector Extensions

A notorious anti-pattern in game engine development is hardcoding UI layouts for every new component or debug gizmo. If a gameplay engineer adds a new `RadarSensor` component, they should not have to open `EntityInspectorPanel.cs` and manually write `ImGui.SliderFloat` calls to expose its range. To ensure our architecture adheres to the Open-Closed Principle, gizmos and components must contribute to the inspector UI automatically and declaratively, without the backend domain logic ever referencing ImGui directly.

We achieve this highly decoupled, data-driven UI through the engine's `StructEdit` memory classifier and a robust plugin architecture for custom renderers and context menus.

1\. The Schema-less Property Tree (`StructEdit`)

At the foundation, we completely eliminate manual UI authoring for standard ECS components. When an operator selects an entity, the `IEditDocumentBuilder` uses C# reflection exactly once to scan the component's memory layout and build an abstract `EditDocument`.

The presentation layer's `ImGuiPropertyTree` recursively renders this document. It automatically translates scalar fields into sliders, booleans into checkboxes, and collections into collapsible arrays, all without any hardcoded UI logic.

2\. The Custom Renderer Plugin System (`IImGuiRenderer`)

When standard hierarchical rendering is insufficient (for example, rendering a raw `Quaternion` as human-readable Euler angles), developers implement the `IImGuiRenderer` interface.

Instead of registering these manually, developers simply decorate the class with `[ImGuiRenderer(typeof(TargetType))]`. At startup, the `ImGuiRendererRegistry` automatically discovers these plugins across all loaded assemblies using reflection. When the `ImGuiPropertyTree` encounters a component field of that type, it cleanly intercepts the draw call and yields rendering authority to the plugin.

3\. Entity-Aware Projections (`IEntityAwareImGuiRenderer`)

Standard rendering plugins work well for isolated structs like `Vector3`, but tactical engine data is often highly contextual. Consider a 1024-byte `Blackboard1024` memory buffer used by AI behaviors. A standard renderer would just dump 1024 raw hexadecimal bytes to the screen.

To solve this, we extend the plugin system with `IEntityAwareImGuiRenderer`, which supplies the `ISimulationView` and the `Entity` handle to the draw call. The `Blackboard1024Renderer` uses this context to query the entity's current `BehaviorState`. It looks up the active behavior hash, retrieves the specific unmanaged DTO type for that behavior (e.g., `PatrolParameters`), and uses `Unsafe.As` to project the raw bytes into a strongly-typed struct before passing it back to the property tree for rendering. The operator sees neatly labelled fields, while the ECS engine continues operating on contiguous, type-erased memory blocks.

4\. Attribute-Driven Map Interactions

Inspector extensions must also seamlessly bridge the gap back into the 3D map. We provide attributes like `[MapPickableEntity]` and `[MapPickableWorldLocation]` to decorate domain components.

When the `ComponentEditDrawer` encounters a field with this attribute, it dynamically injects a "Pick Entity" or "Pick Map" button into the inspector row. Clicking this button pushes an asynchronous interaction context into the `IMapPickService` and activates the corresponding map tool proxy (as discussed in Chapter 5.3). Once the operator clicks a target in the 3D viewport, the result resolves back into the declarative UI field automatically.

5\. Declarative Context Menus

Finally, debug tools must be able to inject spatial actions. Rather than hardcoding every possible map action, the framework utilizes `IEntityContextMenuHandler` and `IDerContextMenuHandler`.

Developers implement this interface to inject items into the operator's right-click menu (e.g., "Edit Overlay", "Rotate Entity"). The engine evaluates these handlers sequentially. Because these handlers are evaluated dynamically, an AI plugin can inject a "Disembark" command into the context menu strictly if the clicked entity currently possesses a passenger status.

Through these mechanisms, your visualization ecosystem remains endlessly extensible. Backend engineers define data and mathematical intent, while the "dumb terminal" client automatically synthesizes a professional, interactive UI panel without a single line of cross-domain coupling.

\*\*5.5 Schema-less Remote DTOs

Chapter 5.5: Schema-less Remote DTOs (Data Transfer Objects)

In a distributed simulation, sharing compiled C# assemblies (DLLs) between the authoritative kernel and remote "dumb terminals" (like the ExCon UI or Image Generators) is a severe architectural anti-pattern. If a backend gameplay engineer adds a new `SensorRange` field to a radar component, we cannot force a recompilation and redeployment of the client UI tools just to update a network DTO.

To enforce absolute decoupling, remote mutations must be processed via **Schema-less Remote DTOs**. The client dispatches untyped intent payloads (JSON or binary key-value pairs), and the authoritative server routes them directly into the ECS memory layout using zero-allocation parsers and Ahead-Of-Time (AOT) compiled delegates.

1\. The Schema-less Patch Payload (`UpdateEntityAttributeRequest`)

When an operator interacts with a remote map tool (such as rotating a unit via the `EntityRotationTool`), the tool does not send a strongly-typed `SimTransform` object over the network. Instead, it broadcasts an `UpdateEntityAttributeRequest` over CycloneDDS.

This request carries a schema-less `AttributePatchJson` string (e.g., `{"Heading": 340.7}`). This allows the remote tool to express a targeted, hierarchical mutation without knowing anything about the engine's internal 256-bit component masks or chunk memory layouts.

2\. The Zero-Allocation `JsonAttributeCompiler`

Parsing JSON on the hot path of a 60Hz simulation is traditionally a massive source of Garbage Collection (GC) pressure. To solve this, the FDP kernel uses a custom `JsonAttributeCompiler`.

During module initialization, the server pre-registers expected JSON paths to specific ECS struct modifiers. The compiler hashes these string paths (e.g., `"GeoPosition.Latitude"`) using the FNV-1a algorithm to generate deterministic, 64-bit integer routing keys.

At runtime, the compiler processes the incoming UTF-8 payload using a stack-allocated state machine and `Utf8JsonReader`. It computes the FNV-1a hash of the JSON keys on the fly. If a matching route is found, it dispatches execution to an `IRoutingEntryInvoker` (like `ValueInvoker<T>`) which directly mutates the ECS struct by `ref`.

Crucially, this pipeline incorporates strict authority checks at the component level. If the local node does not own the targeted ECS component, the invoker simply calls `reader.Skip()`. This leaps over the entire unowned JSON sub-tree in O(1) time using simple bracket-matching, guaranteeing zero allocations and zero memory corruption.

3\. The ATTR2 Binary Fallback

While zero-allocation JSON is highly efficient, parsing string keys still consumes CPU cycles. For high-frequency schema-less updates, the engine implements the **ATTR2 Binary Contract**.

The `UpdateEntityAttributeRequest` can optionally carry a list of `AttributeRecord` structs. Each record encodes a mutation using a well-known 16-bit `ushort AttributeId` (e.g., `GeoLat = 11`) and an `AttributeValueUnion`. Because the `AttributeValueUnion` is a tagged union explicitly mapped to physical memory layouts via `[FieldOffset]`, it travels over DDS without any serialization overhead.

On the receiving server, a `BinaryInterpreter` dispatches these records using a flat array of handler delegates indexed by the `AttributeId`. This achieves O(1) routing without any string parsing or hashing.

Architectural Triumphs of Schema-less DTOs

-   **Absolute Forward Compatibility:** The UI clients can send payloads containing new, unrecognized fields. The `JsonAttributeCompiler` will naturally skip them, preventing crashes when different nodes run slightly mismatched application versions.-   **Zero-Allocation Routing:** By translating string keys to FNV-1a integer hashes and leveraging `ref` semantics, the engine unpacks and routes dynamic remote instructions directly into L1-cached ECS memory without ever waking the .NET Garbage Collector.-   **Pristine Domain Decoupling:** Remote tools declare _what_ they want to change (e.g., `"Name": "Bravo-2"`) while the backend `JsonAttributeCompiler` defines exactly _how_ that maps to physical ECS components, perfectly preserving the boundaries of Command Query Responsibility Segregation (CQRS).

\*\*5.6 Round-Trip Mutations

Chapter 5.6: Round-Trip Mutations (Strict CQRS in a Distributed Kernel)

To finalize our interactive tooling architecture, we must enforce our strictest rule: **Remote UI tools must never mutate ECS state directly.** Allowing a "dumb terminal" client to write into remote memory bypasses simulation authority, destroys determinism, and guarantees catastrophic race conditions.

Instead, every operator interaction completes a strictly segregated Command Query Responsibility Segregation (CQRS) round-trip. The client issues a declarative intent, the authoritative server executes it in a deferred, thread-safe manner, and the resulting state change naturally replicates back to the glass.

Here is the precise architectural pipeline of a network mutation round-trip.

1\. Local Intent Emission

When an operator interacts with a local map tool proxy (such as rotating an entity or dropping a spawn preview), the interaction loop runs entirely in local client memory to guarantee zero-latency feedback. When the operator commits the action (e.g., releasing the mouse button), the local tool proxy does not mutate any ECS components. Instead, it constructs a highly specific domain intent, such as an `UpdateEntityAttributeCommand` or `SpawnEntityCommand`, and publishes it to the client's local event bus.

2\. Egress Translation & Transport

The client's local event bus is monitored by egress translators, such as the `UpdateEntityAttributeCommandEgressTranslator`. This system intercepts the command, extracts the untyped intent (e.g., the JSON attribute patch or component list), and translates it into a standard CycloneDDS wire message, such as an `UpdateEntityAttributeRequest`. The payload is broadcast to the cluster, and the client-side tool enters a pending state awaiting an acknowledgment.

3\. Authoritative Ingress & Validation

When the request reaches the authoritative simulation kernel (SimHost), it hits an ingress system like the `UpdateEntityAttributeRequestSystem`. Before any bytes are touched, the system enforces strict component-level authority guards. It checks the target entity's `EntityHeader.AuthorityMask` to verify that this specific server node actually owns the ECS component being targeted by the schema-less patch. If the node does not have authority, the payload is skipped instantly via a zero-allocation `Utf8JsonReader.Skip()` operation, preventing unauthorized memory corruption.

4\. Deferred Structural Mutation (`IEntityCommandBuffer`)

If the command requires structural changes to the ECS world—such as creating an entity, destroying one, or adding a new component—the kernel absolutely cannot apply the change immediately while worker threads are executing `ForEachParallel` over ECS chunks. Doing so would invalidate memory pointers and trigger race conditions.

Instead, the mutation is recorded into a thread-safe `EntityCommandBuffer`. This buffer acts as a lock-free byte stream, logging operations as simple instructions like `OpCode.CreateEntity`, `OpCode.AddUnmanagedComponent`, or `OpCode.DestroyEntity`. At the end of the simulation frame, safely back on the main thread, the kernel executes `Playback()` to apply these structural mutations atomically to the underlying `EntityRepository`.

5\. Explicit Egress Invalidation & ACK

Once the mutation is safely applied to the ECS layout, the authoritative server immediately calls `SmartEgressUtil.MarkDirty()` for the affected component. This explicitly flags the data for replication, bypassing coarse chunk-level version ticks and guaranteeing that the server's egress translators will broadcast the newly mutated state to all connected clients on the very next network tick.

Simultaneously, the kernel writes a formal confirmation back to DDS, such as a `CreateUpdateDeleteEntityAck`. For attribute updates, this ACK includes a 32-byte opaque bitmask confirming exactly which ECS component IDs were successfully mutated.

6\. Client-Side Closure

Back on the remote "dumb terminal", a controller like the `MapCommandController` receives the incoming ACK. It correlates the `RequestId` to the pending tool session, pops the proxy tool cleanly off the `MapCanvas` tool stack, and clears the pending operation state. At exactly the same time, the updated ECS state (originating from step 5) arrives via the client's ingress translators, causing the UI and map rendering layers to effortlessly update to the new, authoritative simulation truth.

Architectural Triumphs of the Round-Trip Pipeline

-   **Thread-Safe Determinism:** Because all structural changes are isolated into the `EntityCommandBuffer` and played back on the main thread, worker cores can run visualization, AI, and physics in parallel without a single mutex lock.-   **Pristine Domain Decoupling:** Remote tools declare _what_ they want to change using simple intents, while the backend kernel defines exactly _how_ that maps to physical ECS components, perfectly preserving the boundaries of Command Query Responsibility Segregation (CQRS).-   **Lock-Step Replicability:** The client UI state naturally self-heals. It relies entirely on the authoritative data streaming back from the server rather than attempting to predict or hardcode the consequences of its own UI interactions.

6.  Presentation Tier Resolution (Client Adapters)

Chapter 6: Presentation Tier Resolution (Client Adapters)

To complete our visualization architecture, we must resolve how the raw stream of 64-byte `DebugPrimitive` structs, schema-less JSON payloads, and raw domain states are actually rendered onto the glass of a remote "dumb terminal" (such as our Image Generator or ExCon UI).

As clean-architecture practitioners, we absolutely cannot allow the core simulation engine to depend on rendering frameworks like Raylib, Stride, or Unity. The simulation kernel defines the _mathematical truth_, while the client application interprets that truth via the **Adapter Pattern**.

By decoupling presentation via `IVisualizerAdapter` and `IMapLayer`, we achieve a pristine separation of concerns, allowing our engine to scale to tens of thousands of entities at 60 FPS without ever touching the GPU from the backend.

1\. The `IVisualizerAdapter` Contract

Instead of having an entity draw itself, the presentation client implements stateless visualizer adapters. This contract dictates how a physical ECS entity translates to screen pixels, isolating hit-testing and rendering logic entirely on the client.

```
namespace Fdp.Toolkit.Vis2D.Abstractions
{
    /// <summary>
    /// Adapter interface for rendering entities.
    /// Decouples map rendering from specific component types.
    /// </summary>
    public interface IVisualizerAdapter
    {
        // Extracts the world position. Returning null culls the entity.
        Vector2? GetPosition(ISimulationView view, Entity entity);

        // Dispatches hardware-specific rendering (e.g., Raylib calls).
        void Render(ISimulationView view, Entity entity, Vector2 position, RenderContext ctx, bool isSelected, bool isHovered);
    
        // Resolves pick radius for hardware inputs without coupling to physics.
        float GetHitRadius(ISimulationView view, Entity entity);
        
        string? GetHoverLabel(ISimulationView view, Entity entity);
    }
}
```

A production implementation like `NedVisualizerAdapter` queries the entity's `ResolvedStyle` component to determine tactical affiliations (e.g., coloring hostile units red and friendly units blue), loads local textures dynamically based on MIL-STD symbol codes, and draws health bars.

2\. Strict Culling and Level of Detail (LOD)

Because the presentation client might receive state for thousands of entities, we cannot afford to push unoptimized draw calls to the graphics API. We solve this by introducing `CullingState` logic at the ECS level.

Before rendering, a `MapCullingSystem` evaluates the camera frustum and populates a `CullingState` struct for every entity. When the visualizer adapter executes, it reads this state for O(1) fast-rejection:

```
public virtual Vector2? GetPosition(ISimulationView view, Entity entity)
{
    // 1. O(1) Visibility Check
    if (!view.HasComponent<CullingState>(entity)) return null;
    ref readonly var culling = ref view.GetComponentRO<CullingState>(entity);
    if (!culling.IsVisible) return null; // Completely abort rendering
    
    // 2. Resolve projection
    ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
    return new Vector2(tf.Position.X, tf.Position.Y);
}
```

If the entity is visible, the adapter checks `culling.LodLevel`. If the user is zoomed far out (`LodIconOnly`), it gracefully degrades the rendering by suppressing text labels, tactical overlays, and damage bars, saving massive amounts of GPU bandwidth.

3\. The Map Layer Compositor (`EntityRenderLayer`)

To orchestrate thousands of visualizers, the client relies on the `MapCanvas`, which maintains a collection of composable `IMapLayer` instances. Layers represent broad categories of visuals, such as "Vehicles", "Debug Gizmos", or "Visual Effects".

The `EntityRenderLayer` ties the `EntityQuery`, the `IVisualizerAdapter`, and the operator's UI toggles together:

```
public void Draw(RenderContext ctx)
{
    // 1. Skip entire layer if operator unchecked it in the Config UI
    if (LayerBitIndex >= 0)
    {
        uint maskBit = 1u << LayerBitIndex;
        if ((ctx.VisibleLayersMask & maskBit) == 0) return;
    }

    // 2. Iterate entities matching this layer's domain
    foreach (var entity in _query)
    {
        // 3. Fast layer-mask filtering per entity
        uint entityMask = _view.GetComponentRO<MapDisplayComponent>(entity).LayerMask;
        if ((entityMask & ctx.VisibleLayersMask) == 0) continue;
    
        // 4. Delegate to the Visualizer Adapter (Hardware specifics)
        var pos = _adapter.GetPosition(_view, entity);
        if (!pos.HasValue) continue; // Culled by GetPosition
    
        _adapter.Render(_view, entity, pos.Value, ctx, _selection.IsSelected(entity), false);
    }
}
```

By hashing entity layers into a `MapDisplayComponent.LayerMask`, the render loop uses a simple bitwise `&` operator to filter entities in O(1) time per entity, entirely avoiding expensive LINQ queries or type-checks on the hot path.

Architectural Triumphs of the Client Adapters

-   **Absolute Frontend Ignorance:** The simulation server (FDP) runs blazingly fast in a headless environment. It knows absolutely nothing about `Texture2D`, screen coordinates, UI scale, or Raylib pointers.-   **GPU Optimization via ECS:** Culling logic (`CullingState`) and layer bitmasks (`MapDisplayComponent`) are managed as raw memory structs. The graphics tier merely obeys these pre-calculated states, eliminating render-thread bottlenecks.-   **Pluggable Render Pipelines:** Because `IVisualizerAdapter` returns generic `Vector2` projections and uses a decoupled `Render` call, you can swap out the 2D Raylib engine for a 3D Stride/Unity engine tomorrow by simply injecting a new implementation of the adapter.

This clean separation perfectly encapsulates the design mandate of our entire debugging and visualization framework: **Author logic declaratively, transport payloads minimally, and resolve visuals natively.**

6.1 Stream Ingestion

Chapter 6: Presentation Tier Resolution (Client Adapters)

Chapter 6.1: Stream Ingestion (Zero-Allocation Polling and Ghost Resolution)

Because the remote presentation client (the Image Generator or ExCon UI) is designed as a strict "dumb terminal," it holds zero authoritative simulation logic. Its entire view of the world is dictated by the incoming stream of CycloneDDS network messages. If we mishandle the ingestion of this firehose—receiving position updates for 10,000 entities at 60Hz—we will instantly drown the .NET Garbage Collector and destroy the client's frame pacing.

To maintain pristine architectural isolation and hardware-level performance, the client presentation tier ingests the network stream using **Zero-Allocation Data Loans**, **Unified Ingress Packs**, and **Just-In-Time Ghost Resolution**.

1\. The Unified `EntityStatesIngressPack`

Instead of scattering network listeners throughout the UI code, the presentation client registers a single, composable ECS module called the `EntityStatesIngressPack`.

This module groups all inbound data translators—such as the `EntityMasterIngressTranslator` (birth/death), `GeoSpatialIngressTranslator` (kinematics), and `MapVisualOverlayIngressTranslator` (geometry)—into a single `CycloneNetworkIngressSystem`. By executing this system strictly in `SystemPhase.Input`, we guarantee that all network data is fully synchronized into the ECS memory layout _before_ any rendering or UI logic observes the world.

2\. Zero-Allocation Data Loans

On the hot path of network ingestion, instantiating new C# objects for incoming packets is an architectural sin. Instead, our ingress translators use the CycloneDDS `Take()` API to borrow a span of unmanaged memory directly from the network socket's receive buffer.

```
public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
{
    if (_reader is null) return;

    // Zero-allocation memory loan directly from the CycloneDDS native buffer
    using var loan = _reader.Take(); 
    
    foreach (var sample in loan)
    {
        if (!sample.IsValid) continue;
        
        // Data is an unmanaged struct, passed by value/ref without boxing
        var data = sample.Data;
        ProcessSample(in data, cmd, view);
    }
}
```

When the `using` scope closes, the memory is instantly returned to the native pool. The .NET Garbage Collector remains entirely dormant.

3\. Just-In-Time Ghost Resolution (`NetworkEntityMap`)

The incoming DDS messages identify entities using a globally unique 64-bit integer (`NetworkId`). However, the local presentation client's rendering loop relies on contiguous, generational 32-bit `Entity` handles to preserve L1 cache locality during ECS chunk iteration.

To bridge this gap efficiently, every ingress translator utilizes the `NetworkEntityMap`. This service provides an O(1) lookup from the global network ID to the local ECS handle.

Crucially, if a translator receives a position update or a visual overlay for an entity it does not recognize, it does not drop the packet. It dynamically invokes the `GhostCreationSystem`.

```
internal void ProcessSample(in MapEntitySymbol data, IEntityCommandBuffer cmd, EntityRepository? repo)
{
    long netId = data.EntityId;

    // 1. Fast O(1) translation from Network ID to Local ECS Entity
    if (!_entityMap.TryGetEntity(netId, out var entity))
    {
        // 2. Just-In-Time Ghost Creation: The entity just entered our scope.
        // We create a local, non-authoritative "Ghost" shell to hold the incoming data.
        entity = _ghostCreationSystem.CreateGhost(repo, netId); [9]
    }
    
    // 3. Write the incoming payload into the local ECS chunk safely
    cmd.SetManagedComponent(entity, new IgSymbolOverride
    {
        StyleSetId = string.IsNullOrEmpty(data.StyleSetId) ? null : data.StyleSetId,
        TextureOverride = null
    }); [10]
}
```

Architectural Triumphs of Stream Ingestion

-   **Pristine Client State:** The "dumb terminal" possesses no simulation rules. It simply reads structurally-typed memory from the network and writes it directly into its local ECS chunks, achieving maximum synchronization speed.-   **Deterministic Lifecycle:** By evaluating `sample.Info.InstanceState != DdsInstanceState.Alive`, translators can instantly detect when the authoritative server has destroyed an entity, safely triggering a local teardown cascade without manual "delete" packets.-   **Infinite Scaling:** Because the `using var loan = _reader.Take()` pipeline is fundamentally lock-free and avoids the Gen0 heap, the presentation client can ingest tens of thousands of state updates per frame while maintaining a rock-solid 60 FPS.

Let me know when you are ready to proceed to **Chapter 6.2: Kinematic Interpolation & Dead Reckoning**, where we solve the visual stuttering caused by network packet jitter using mathematical projection!

**6.1 Stream Ingestion:** SIMD bitwise-AND rejection filtering for layers and pipelines.

In a distributed simulation, the presentation client acts as a "dumb terminal" ingesting a firehose of network state. To process and route thousands of entity updates to the correct visual layers without obliterating the CPU pipeline, we rely heavily on SIMD-accelerated bitwise rejection.

Instead of evaluating rendering policies using polymorphic objects or branching logic, visibility and layer membership are reduced to raw mathematical operations.

1\. AVX2-Accelerated Component Filtering

At the core of the engine, entities are defined by a 256-bit component mask (`BitMask256`) that is strictly 32-byte aligned. When the presentation tier queries for entities relevant to a specific visual pipeline, the engine does not evaluate entities one by one. Instead, it utilizes `Avx2` intrinsics to execute the inclusion and exclusion logic (`(target & include == include) AND (target & exclude == 0)`) in a single CPU cycle. By vectorizing the bitwise-AND operations and collapsing the 256-bit result into a single 32-bit integer using `MoveMask`, the kernel can instantly reject non-matching entities without branching.

2\. Chunk-Level Skip Operations

This SIMD bitwise rejection is applied hierarchically. Before iterating over individual entities, the framework checks the memory at the 64KB chunk boundary. If the engine determines that a required component is completely absent from a chunk, or if no component in that block has changed since the last network tick, it skips the entire memory block instantly. This elevates the stream filtering performance from O(total entities) to O(populated chunks), preserving pristine L1 cache integrity.

3\. O(1) Map Layer Resolution

Once entities pass the structural query, they must be routed to specific rendering compositors (e.g., "Ground Units" or "Tactical Graphics"). We attach a `MapDisplayComponent` to each entity, which stores its layer affiliations as a simple 32-bit `LayerMask`. During the rendering loop, the `EntityRenderLayer` performs a single, inline bitwise-AND operation against the active camera's `VisibleLayersMask`. If the result is zero (`(entityMask & ctx.VisibleLayersMask) == 0`), the entity is instantly skipped. This completely eliminates the need for expensive LINQ queries, string comparisons, or type-checking on the hot path.

Architectural Triumphs of Bitwise Rejection

By reducing visibility and pipeline routing to strictly aligned memory structures and SIMD bitwise operations, we eliminate branch mispredictions and Garbage Collection overhead. The CPU never stalls waiting to unbox managed objects, allowing the presentation client to seamlessly ingest and filter tens of thousands of telemetry packets at a flawless 60 FPS.

Let me know when you are ready to proceed to **Chapter 6.2: Kinematic Interpolation & Dead Reckoning**, where we solve the visual stuttering caused by network packet jitter using mathematical projection!

**6.2 Dimensional Interpolation:** Inverse-zoom matrix scaling for screen-space primitives.

Chapter 6: Presentation Tier Resolution (Client Adapters)

Chapter 6.2: Dimensional Interpolation (Inverse-Zoom Matrix Scaling for Screen-Space Primitives)

A fundamental tension in any tactical mapping engine is the discrepancy between world-space geometry and screen-space UI. When an operator zooms the map camera from a 10-kilometer strategic theater view down to a 10-meter street view, physical entities (like tanks or buildings) must scale proportionally. However, interactive gizmos—such as selection rings, vertex drag handles, crosshairs, and tracer lines—must maintain a crisp, fixed pixel thickness on the glass.

If we draw these primitives _after_ the camera pass in pure screen space, we destroy depth-sorting and introduce complex coordinate projection overhead. If we draw them _inside_ the world-space camera projection, a 2-pixel-thick line becomes 20 pixels thick when zoomed in, or vanishes entirely when zoomed out.

To resolve this elegantly, the presentation tier employs **Inverse-Zoom Matrix Scaling**. We render the gizmos natively inside the `MapCamera.BeginMode()` world-space matrix, but we dynamically invert the camera's scale factor against the primitive's dimensions just before emission.

1\. The `RenderContext.Zoom` Inversion

Every rendering layer and interactive tool receives a `RenderContext` containing the current `MapCamera` evaluation. Rather than applying expensive CPU-side matrix multiplications, we neutralize the GPU's scaling matrix by dividing the target pixel dimensions by the current zoom factor.

Consider the `EffectRenderLayer`, which draws ephemeral tracer lines. The tracer's length represents physical world-space distance, but its stroke thickness must remain a consistent number of screen pixels regardless of zoom level:

```
// Inside EffectRenderLayer.Draw
// Safe zoom fallback to prevent divide-by-zero
float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;

// ... iterate entities ...

// Convert pixel line thickness to world-space thickness so it stays
// consistent visually regardless of zoom level.
float thickness = VisualEffectStateConstants.EffectLineWidthPx / zoom;

Raylib.DrawLineEx(worldPos, targetWorldPos, thickness, color);
```

_As seen in the_ _EffectRenderLayer_ _pipeline__._

2\. Interactive Tool Handles & Crosshairs

This mathematical inversion is heavily utilized by interactive tools to maintain hit-test and visual consistency. When an operator uses the `LocationPickerTool` or the `EntityPickerTool`, the engine draws a precision crosshair at the cursor.

By applying the inverse-zoom scalar to every geometric component—half-size, thickness, and gap radius—the tool projects a mathematically perfect screen-space reticle while executing strictly inside the world-space pipeline:

```
public void Draw(RenderContext ctx)
{
    // Apply camera zoom to keep the crosshair screen-size consistent
    float zoom  = ctx.Zoom > 0 ? ctx.Zoom : 1f;
    
    // Scale fixed pixel dimensions inversely against the camera matrix
    float size  = CrosshairHalfSize  / zoom;
    float thick = CrosshairThickness / zoom;
    float gap   = CrosshairGapRadius / zoom;
    
    Color color = Color.SkyBlue;
    var   pos   = _mouseWorldPos;
    
    // Draw world-space lines that will result in exact pixel dimensions on screen
    Raylib.DrawLineEx(new Vector2(pos.X - size, pos.Y), new Vector2(pos.X - gap, pos.Y), thick, color);
    Raylib.DrawLineEx(new Vector2(pos.X + gap,  pos.Y), new Vector2(pos.X + size, pos.Y), thick, color);
    // ...
}
```

_As implemented in the_ _LocationPickerTool__._

3\. Selection Boxes and Bounding Rings

The same principle applies to standard area authoring and selection boxes. When dragging a marquee selection via the `BoxSelectionTool`, the filled rectangle is drawn using pure world coordinates to properly highlight the map area, but the bounding box outline uses inverse-zoom thickness to keep the border razor-sharp:

```
// Draw semi-transparent fill in world space
Raylib.DrawRectangleV(min, size, new Color(0, 120, 255, 50));

// Draw border with consistent screen thickness (2.0f pixels)
Raylib.DrawRectangleLinesEx(
    new Rectangle(min.X, min.Y, size.X, size.Y), 
    2.0f / ctx.Zoom, 
    new Color(0, 120, 255, 200));
```

_As utilized in the_ _BoxSelectionTool.Draw_ _method__._

Architectural Triumphs of Inverse-Zoom Scaling

-   **Unified Compositing Pipeline:** By keeping all draw calls inside the single `Camera2D` projection pass, we eliminate the need for a secondary screen-space rendering pass, completely bypassing coordinate transformation overhead and Z-order fighting.-   **Zero-Allocation Geometry:** We compute the inverse scale dynamically as a local stack variable. The CPU does not need to allocate, cache, or recalculate complex projection matrices for UI primitives.-   **Pristine Visual Contract:** Tools feel native and tactile. A vertex drag handle will always appear as an exact 5-pixel radius circle, and a selection line will always be exactly 2 pixels wide, guaranteeing a professional-grade user experience whether tracking intercontinental ballistic trajectories or individual infantrymen.

Let me know if you would like to delve into **Chapter 6.3: Dead-Reckoning Interpolation**, where we tackle how to apply spatial smoothing to high-frequency network packets!

**6.3 Responsive Screen Anchoring:** Evaluating absolute pixels from dynamic window resolutions.

To maintain a resilient, resolution-independent presentation tier, the engine evaluates absolute pixel anchors dynamically each frame rather than caching hardcoded screen coordinates. This architectural practice ensures the UI automatically adapts when operators resize their "dumb terminal" windows.

For floating diagnostics, the `PerformanceOverlay` guarantees it remains pinned to the top-right corner of the display by recalculating its position dynamically using `Raylib.GetScreenWidth() - OverlayWidth - OverlayMarginX`. This same responsive mathematical approach drives the `StatusBarManager`, which positions the global status bar exactly at the bottom of the screen by querying the ImGui main viewport and computing the absolute Y position as `viewport.WorkPos.Y + viewport.WorkSize.Y - height`. Consequently, the central dockspace layout automatically adjusts its own height dynamically to gracefully leave room for this responsive status bar.

When constructing custom interactive title bar controls, such as the `ManagedWindow` pin button, the framework computes the widget's absolute screen coordinates relative to the dynamic window width via `windowPos.X + windowWidth - 2f * fh`. By utilizing these absolute coordinates alongside the `ImGui.GetWindowDrawList()`, the engine renders hit-testable, pixel-perfect interactive elements that consume zero client-area layout space.

Crucially, this dynamic screen resolution logic is bridged flawlessly into the simulation's spatial awareness. The `MapCameraViewport` continuously evaluates the exact world-space boundaries visible to the operator by taking the absolute screen corners—from `Vector2.Zero` to the dynamic `Raylib.GetScreenWidth()` and `Raylib.GetScreenHeight()`—and projecting them through the `Camera.ScreenToWorld` matrix. This cleanly decouples the `MapCullingSystem` from the physical hardware display, ensuring that the engine's data-oriented entity culling performs perfectly whether the application is running in a 720p window or dynamically resized across a 4K monitor.

**6.4 Terminal Handshakes:** Broadcasting capabilities via `IGCapabilitiesAnnounce`.

Chapter 6: Presentation Tier Resolution (Client Adapters)

Chapter 6.4: Terminal Handshakes (Broadcasting Capabilities via `IGCapabilitiesAnnounce`)

In a distributed simulation governed by strict Command Query Responsibility Segregation (CQRS) and isolated "dumb terminals," we face a classic UI coupling problem: How does the remote operator's UI (the ExCon) know which map layers it can toggle, or which interaction tools the Image Generator (IG) actually supports?

Historically, developers solve this by hardcoding a "Layers" panel in the UI client (e.g., `ImGui.Checkbox("Show Ground Units")`). This creates a disastrous architectural dependency. If an IG graphics programmer adds a new "Weather Effects" layer, they must also modify, recompile, and redeploy the remote ExCon UI application just to add the new checkbox.

To achieve absolute client/server decoupling, we implement a **Terminal Handshake** using dynamic JSON schemas. The presentation client tells the control terminal exactly what it is capable of rendering the moment it boots.

1\. The `IGCapabilitiesAnnounce` Contract

When an IG instance starts up, it announces its presence and readiness to the cluster. We facilitate this via the `IGCapabilitiesAnnounce` DDS topic.

Rather than sending C# enums or bitmasks, the IG serializes its capabilities into abstract JSON schemas.

```
namespace Hrot.NED.Descriptors
{
    // Announcement message sent by an IG instance when it starts up.
    // Enables the ExCon to dynamically build its UI based on what the IG supports.
    [DdsTopic("IGCapabilitiesAnnounce")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct IGCapabilitiesAnnounce
    {
        // The specific IG Instance.
        [DdsKey]
        public int MapId;

        // Defines the layer structure (folders/items) for the ExCon "Layers" panel.
        public string LayerTreeJson;
    
        // JSON Schemas defining valid configuration options (e.g., "What tools are available?").
        public string ConfigurationSchemasJson;
    
        // JSON Schema validating the 'styleOverrideJson' field in overlays.
        public string OverlayStyleSchemaJson;
    
        // JSON Manifest of TKB types that this IG specifically supports.
        public string TkbManifestJson;
    }
}
```

Notice the Quality of Service (QoS) attributes: `Durability = DdsDurability.TransientLocal`. This is a critical architectural decision. Because capabilities are static for the lifetime of the session, the IG only needs to publish this message exactly once at startup. If a remote ExCon terminal connects to the cluster three hours later, the DDS middleware's `TransientLocal` durability guarantees that the late-joining ExCon instantly receives the cached handshake packet, preventing temporal race conditions.

2\. The One-Shot `IgCapabilitiesPublisher`

On the IG side, the handshake is assembled by a dedicated, one-shot startup service called `IgCapabilitiesPublisher`. It is invoked immediately after the DDS participant is created and all modules are registered.

Instead of hardcoding the JSON string, the publisher dynamically reflects over the IG's internal rendering registries. For example, to build the `LayerTreeJson`, it iterates over `MapLayerRegistry.All`. This registry defines the standardized JSON keys (like `"units_ground"` or `"tactical_graphics"`) and their rendering bitmasks.

```
namespace Hrot.IG.Services
{
    public static class IgCapabilitiesPublisher
    {
        public static void Publish(IIgNetworkAdapter? adapter, int mapId)
        {
            if (adapter == null) return;

            try
            {
                string layerTreeJson  = BuildLayerTreeJson(); // Reflects over MapLayerRegistry.All
                string configSchemas  = BuildConfigSchemasJson();
                
                // Dispatches to the protocol-neutral network adapter
                adapter.PublishCapabilities(mapId, layerTreeJson, configSchemas); [7]
            }
            catch (Exception ex)
            {
                FdpLog<Log>.Warn("[Node-{0}] Failed to publish IGCapabilitiesAnnounce: {1}", mapId, ex.Message); [7]
            }
        }
    }
}
```

The protocol-neutral `IIgNetworkAdapter` receives this payload and routes it through to the underlying DDS writer (`_participant.Write`).

3\. Data-Driven Remote UI Generation

When the ExCon application receives the `IGCapabilitiesAnnounce` message, it feeds the `LayerTreeJson` directly into its ImGui layout engine. The ExCon has no compile-time knowledge of what `"units_ground"` means. It simply sees a JSON node declaring a boolean toggle layer and blindly renders an `ImGui.Checkbox` for it.

If the operator checks the box, the ExCon sends a `MapInteractionConfig` JSON Merge Patch back to the IG. The IG parses the patch and flips the internal `MapCanvas.ActiveLayerMask` bit.

Architectural Triumphs of the Terminal Handshake

-   **Absolute Front-End Decoupling:** The ExCon UI acts as a pure presentation layer for the IG's capabilities. An IG programmer can add new map layers, drawing tools, or visual settings to the `MapLayerRegistry` in the IG codebase, and those controls will instantly and automatically appear on all connected operator terminals without a single line of UI code being rewritten.-   **Late-Joiner Safety:** By utilizing `TransientLocal` DDS durability, we eliminate the need for complex request/response handshakes to discover state. The architecture remains entirely push-based and stateless, yet perfectly synchronizes UI nodes regardless of their boot order.-   **Pristine Domain Isolation:** The `IGCapabilitiesAnnounce` packet creates an air-gap between visual rendering intent and hardware input configuration, ensuring that our "dumb terminals" remain truly dumb, merely echoing the configuration schemas demanded by the rendering engine.

**6.5 Fallback Rendering:** Handling unsupported assets cleanly on the client side.

Chapter 6: Presentation Tier Resolution (Client Adapters)

Chapter 6.5: Fallback Rendering (Handling Unsupported Assets Cleanly)

In a highly decoupled distributed simulation, it is a statistical guarantee that a "dumb terminal" client (like an Image Generator or an ExCon UI) will eventually encounter an entity it does not know how to draw. A backend engineer might push a new scenario featuring a `Drone_Recon` blueprint, but the operator's local machine might be missing the `SFGPUCIZ-------.png` texture file from its asset directory.

If the presentation tier blindly attempts to load a missing asset, it will throw a `FileNotFoundException` and crash the rendering loop. If it suppresses the error but renders nothing, we introduce a catastrophic "invisible enemy" bug into the tactical simulation.

To maintain a pristine, fault-tolerant user experience, our rendering pipeline enforces **Graceful Degradation via Fallback Rendering**. If an asset is missing, the engine gracefully drops into a mathematically generated stub primitive, preserving critical tactical context (position, affiliation, and identity) without a single allocation or hard crash.

1\. Lazy Asset Resolution

Visual assets are never pre-loaded or strongly bound to ECS components. The authoritative backend pushes declarative string identifiers (like MIL-STD-2525 symbol codes via `VisualData` or `IgSymbolOverride` components). The presentation client relies on a lazy-loaded texture cache to resolve these strings into GPU handles exactly when they enter the camera's culling frustum.

```
/// <summary>
/// Lazily loads and caches the texture for <paramref name="name"/>.
/// Returns null when the file does not exist so callers can draw the fallback circle.
/// </summary>
private Texture2D? TryGetTexture(string name)
{
    if (_textureCache.TryGetValue(name, out var cached))
        return cached;

    string path = NedVisualizerAdapterConstants.AssetBasePath + name + ".png";
    if (!File.Exists(path))
        return null;
    
    var tex = Raylib.LoadTexture(path);
    _textureCache[name] = tex;
    return tex;
}
```

_Note: Because this happens directly inside the_ _IVisualizerAdapter__, the backend simulation server has absolutely zero knowledge of disk paths or_ _.png_ _extensions__._

2\. The Defensive Render Path

When the `NedVisualizerAdapter` executes its `Render` method, it attempts to resolve the texture. If `TryGetTexture` returns `null` (or if the entity never had a valid texture name assigned), the renderer falls back to a dynamically generated stub circle.

Crucially, the adapter still evaluates the entity's `ForceId` (Friend, Hostile, Neutral, Unknown) to derive the correct tactical tint.

```
// ── Icon / fallback circle ──────────────────────────────────────────────────
if (!string.IsNullOrEmpty(textureName))
{
    var tex = TryGetTexture(textureName);
    if (tex.HasValue)
    {
        // ... Projection logic and standard texture rendering ...
        Raylib.DrawTextureEx(tex.Value, position - origin, 0f, scale, drawTint);
    }
    else
    {
        // Graceful degradation: Asset is missing from disk!
        DrawFallbackCircle(position, drawTint);
    }
}
else
{
    // Graceful degradation: Entity has no declared texture.
    DrawFallbackCircle(position, drawTint);
}
```

3\. Semantic Preservation in the Stub

The `DrawFallbackCircle` is not just an error indicator; it is a fully functional semantic stand-in. It renders a mathematically perfect primitive using the dynamically resolved affiliation tint (e.g., a solid red circle for hostile units, solid blue for friendly).

To ensure the operator can still identify and command the unit, the renderer supplements the circle with the entity's human-readable name or its fallback `NetworkIdentity` integer.

```
private static void DrawFallbackCircle(Vector2 pos, Color tint)
    => Raylib.DrawCircle(
        (int)pos.X,
        (int)pos.Y,
        NedVisualizerAdapterConstants.FallbackCircleRadiusPx,
        tint);
```

During Phase 1 of our development, before the TKB-driven symbol engine was complete, this fallback mechanism served as our `StubVisualizerAdapter`. It rendered a 10-pixel red circle and overlaid the `NetworkIdentity` ID as a text label beneath it, proving that we could build and test the entire distributed data plane without requiring a single piece of 2D or 3D art.

Architectural Triumphs of Fallback Rendering

-   **Zero-Crash Resilience:** Missing assets are treated as routine logic branches rather than exceptions. The application will never crash due to a missing PNG file.-   **Preserved Tactical Awareness:** By projecting the entity's `ResolvedStyle.Tint` onto the fallback primitive, the operator instantly knows the location and affiliation of the unit, allowing them to continue interacting with the simulation seamlessly.-   **Pristine Client Decoupling:** The "dumb terminal" accepts whatever data the server pushes. If it encounters a brand-new component or blueprint type it does not recognize, it natively degrades the visual presentation while maintaining perfect simulation synchronization.



Sample implementations necessary



As an example usage of the gizmo framework, i would like to implement and integrate few gizmos like

- map measure tool
  - same functionality as the current map tool, just implemented as gizmo
  - global settings
    - measurement units
- entity health bar - purely rendering gizmo, entity bound
  - same functionality as the one rendered in the IG subsystem
  - global settings
    - height of the health bar
    - width of the health bar
- platoon hill attack behavior bound gizmo
  - shows
    - green base line
    - blue fire line
    - base line slots (little numbered circles on the base line)
    - fire line slots (little numbered circles on the fire line)
  - global settings
    - whether to show slots
- spatial grid global gizmo
  - shows
    - the grid tiles
    - in the upper left corner of each tile little number of entities inside
  - global settings
    - show tiles (otherwise just outer bounds)
    - show number of entities per tile
- entity rotation - entity bound interactive gizmo
  - same functionality as the current implementation (rotating line with heading angle indicator)
- visibility cones - entity bound non interactive, entity local space gizmo
  - shows the visibility cone, as a sector

