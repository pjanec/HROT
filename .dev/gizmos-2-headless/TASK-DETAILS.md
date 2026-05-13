# Gizmos-2 Headless — Task Details

> **Design reference:** [DESIGN.md](./DESIGN.md)  
> **Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## Phase 1: Core Infrastructure — Zero-CPU Headless

### GZH-001 — `TerminalConnectedEvent` / `TerminalDisconnectedEvent`

**Design ref:** DESIGN.md §2.5

**Location:**  
`FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/TerminalLifecycleEvents.cs`

**Description:**  
Add two simple managed event classes to the FDP event bus contract:

```csharp
public sealed class TerminalConnectedEvent  { public long TerminalId { get; init; } }
public sealed class TerminalDisconnectedEvent { public long TerminalId { get; init; } }
```

These are plain managed events published on `FdpEventBus`. They are the single source of truth
for terminal lifecycle regardless of whether the terminal is local (Raylib) or remote (DDS).

**Success conditions:**
- Both classes exist in `Fdp.Toolkit.Diagnostics.Gizmos.Events` namespace.
- Both are visible from `Fdp.Toolkits` and `Hrot.*` assemblies.
- A unit test (`GZH001_1`) publishes `TerminalConnectedEvent` on an `FdpEventBus`, advances one
  frame, and reads it back via `bus.ReadManaged<TerminalConnectedEvent>()`.

---

### GZH-002 — `GizmoExecutionController`

**Design ref:** DESIGN.md §2.4, §3

**Location:**  
`FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs`

**Description:**  
Reference-counted toggle for a `TogglablePostSimulationGroup`. Calls synchronous teardown on both
gizmo managers when the listener count reaches zero — without involving the event bus.

Constructor signature:
```csharp
public GizmoExecutionController(
    TogglablePostSimulationGroup group,
    GlobalGizmoManager globalManager,
    DataDrivenGizmoSystem dataDrivenSystem)
```

`AddListener()`: `Interlocked.Increment`; sets `group.Enabled = true` when count goes from 0→1.  
`RemoveListener()`: `Interlocked.Decrement`; when count reaches 0:
  1. Calls `_globalManager.CancelInteractiveTools()` synchronously.
  2. Calls `_dataDrivenSystem.CancelInteractiveTools()` synchronously.
  3. Sets `_group.Enabled = false` immediately.

No `FdpEventBus` is involved. No `_pendingDisable` flag. No "one final frame" delay.
The synchronous call is safe because teardown only disposes gizmo objects — it does not re-enter
the ECS scheduler and does not interact with the event bus double-buffer.

**Success conditions:**
- Unit test `GZH002_1`: start with 0 listeners → group disabled. Call `AddListener()` → enabled.
  Call `AddListener()` again → still enabled. Call `RemoveListener()` → still enabled (count=1).
  Call `RemoveListener()` → disabled (count=0).
- Unit test `GZH002_2`: register an exclusive-focus gizmo in `GlobalGizmoManager`. Call
  `RemoveListener()` to 0. Verify the gizmo's `OnCancel()` was called immediately (no tick needed)
  and `controller.ListenerCount == 0` with `group.Enabled == false`.

---

### GZH-003 — Wire Gizmo Systems into `TogglablePostSimulationGroup`

**Design ref:** DESIGN.md §3

**Location:** Each subsystem's composition root:
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Also: a helper `GizmoSystemModule` wrapper may be introduced so registration can be done
with a single `kernel.RegisterModule(new GizmoSystemModule(gizmoGroup))` call.

**Description:**  
In each subsystem that uses the gizmo systems, wrap `GlobalGizmoManager`, `DataDrivenGizmoSystem`,
and `StatelessGizmoSystem` into a `TogglablePostSimulationGroup`. Set `Enabled = false` by default.
Create a `GizmoExecutionController` for each subsystem.

For each subsystem that runs in interactive mode by default (IG, Editor), keep `Enabled = true` at
startup — they already open a window. For headless-first subsystems (SimHost, CGF), start `Enabled
= false`.

**Success conditions:**
- Integration test: start `SimHostSubsystem` in headless mode. Confirm that
  `DataDrivenGizmoSystem.Execute` is never called (mock/counter shows 0 calls).
- Integration test: call `controller.AddListener()`. Confirm `DataDrivenGizmoSystem.Execute` is
  now called each frame.

---

### GZH-004 — Add `CancelInteractiveTools()` to `GlobalGizmoManager`

**Design ref:** DESIGN.md §6.1

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs`

**Description:**  
Add a new public synchronous method `CancelInteractiveTools()`. This method is called directly by
`GizmoExecutionController.RemoveListener()` when the listener count reaches zero; it does not
involve the event bus.

```csharp
public void CancelInteractiveTools()
{
    if (_focusedGizmo != null)
    {
        _focusedGizmo.OnCancel();
        _focusedGizmo.SetFocus(false);
        _focusedGizmo.Dispose();
        _focusedGizmo = null;
    }
    var onDemandKeys = _activeGizmos
        .Where(kvp => kvp.Value.RequiresExclusiveFocus || kvp.Value.WantsRawInput)
        .Select(k => k.Key).ToList();
    foreach (var key in onDemandKeys)
    {
        _activeGizmos[key].Dispose();
        _activeGizmos.Remove(key);
    }
}
```

Permanent global tools (those with neither `RequiresExclusiveFocus` nor `WantsRawInput`, such as
`LayerControlGizmo`) are intentionally left intact.

No changes to `Execute()`. No event bus reads.

**Success conditions:**
- Unit test `GZH004_1`: register two gizmos — one permanent (`RequiresExclusiveFocus = false`,
  `WantsRawInput = false`) and one on-demand (`RequiresExclusiveFocus = true`). Grant focus to
  the on-demand one. Call `CancelInteractiveTools()` directly. Verify on-demand gizmo had
  `OnCancel()` called and is removed from the manager; permanent gizmo is still registered.

---

### GZH-005 — Add `CancelInteractiveTools()` to `DataDrivenGizmoSystem`

**Design ref:** DESIGN.md §6.2

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

**Description:**  
Add a new public synchronous method `CancelInteractiveTools()`. This method is called directly by
`GizmoExecutionController.RemoveListener()` when the listener count reaches zero; it does not
involve the event bus.

```csharp
public void CancelInteractiveTools()
{
    foreach (var kvp in _injectedGizmos)
    {
        kvp.Value.OnCancel();
        kvp.Value.Dispose();
    }
    _injectedGizmos.Clear();
}
```

No changes to `Execute()`. No event bus reads.

**Success conditions:**
- Unit test `GZH005_1`: inject a gizmo for an entity. Call `CancelInteractiveTools()` directly.
  Verify the gizmo's `OnCancel()` was called immediately and `_injectedGizmos` is empty.

---

## Phase 2: UI State Infrastructure

### GZH-006 — `StructInspectorProjector<T>`

**Design ref:** DESIGN.md §2.1

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs`

**Description:**  
Create the generic helper class. Key contracts:
- `EmitAndSync(...)`: always emits the `MakeStructInspector` primitive; only calls
  `uiPublisher.Publish` when the serialised JSON differs from the cached value.
- `ApplyUpdate(payloadJson, ref T dto)`: deserialises via `IComponentEditService`, updates cache.
- When `uiPublisher` is `null`, `EmitAndSync` still emits the primitive but never allocates JSON.
- `_lastPublishedJson` cache is updated in `ApplyUpdate` to prevent an echo-back.

**Success conditions:**
- Unit test `GZH006_1`: create a projector with a mock publisher. Call `EmitAndSync` twice with
  the same DTO. Publisher receives exactly one `Publish` call (not two).
- Unit test `GZH006_2`: call `EmitAndSync` with an updated DTO value. Publisher receives a second
  `Publish` call with the updated JSON.
- Unit test `GZH006_3`: call `ApplyUpdate` with valid JSON. Verify the DTO field values are
  updated. Call `EmitAndSync` immediately after with the same DTO state. Verify publisher does
  NOT receive another `Publish` call (cache matches the applied JSON).
- Unit test `GZH006_4`: create projector with `uiPublisher = null`. Call `EmitAndSync`. No
  exception. The draw builder receives the `StructInspector` primitive.

---

### GZH-007 — `GizmoUiStateHub`

**Design ref:** DESIGN.md §2.2

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/GizmoUiStateHub.cs`

**Description:**  
Multiplexer implementing `IGizmoUiStatePublisher`. Maintains a `List<IGizmoUiStatePublisher>`
protected by a lock. `Publish()` iterates the list; if empty, does nothing.

**Success conditions:**
- Unit test `GZH007_1`: create hub with 0 endpoints. Call `Publish()`. No exception, no side effects.
- Unit test `GZH007_2`: add two mock endpoints. Call `Publish()`. Both endpoints receive the state.
- Unit test `GZH007_3`: add an endpoint, then `RemoveEndpoint()` it. Call `Publish()`. Endpoint
  receives no calls.
- Unit test `GZH007_4`: `AddEndpoint` / `RemoveEndpoint` from a different thread while `Publish`
  is in progress. No `InvalidOperationException` (collection modified during iteration).

---

### GZH-008 — `LocalGizmoUiStateTransport`

**Design ref:** DESIGN.md §2.3

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/LocalGizmoUiStateTransport.cs`

**Description:**  
In-memory `IGizmoUiStatePublisher` using `ConcurrentDictionary<uint, GizmoUiState>`.

- `Publish(state)`: `_dict[state.GizmoInstanceId] = state;` (overwrite; bounded by schema count).
- `PollAndApply(Action<GizmoUiState> handler)`: iterate all entries, call `handler` for each,
  then clear the dictionary.

**Success conditions:**
- Unit test `GZH008_1`: publish the same `GizmoInstanceId` ten times with different JSON values.
  `PollAndApply` delivers exactly one state (the last one) to the handler.
- Unit test `GZH008_2`: publish two distinct `GizmoInstanceId` values. `PollAndApply` delivers
  both, one call each.
- Unit test `GZH008_3`: after `PollAndApply`, the dictionary is empty (no double-delivery).

---

## Phase 3: Dynamic Terminal Modules

### GZH-009 — `LocalTerminalModule`

**Design ref:** DESIGN.md §4.1

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/LocalTerminalModule.cs`

**Description:**  
Installable `IEcsModule`. Constructor receives `GizmoExecutionController` and `GizmoUiStateHub`.

```csharp
public LocalTerminalModule(GizmoExecutionController controller, GizmoUiStateHub uiHub)
{
    _localUiTransport = new LocalGizmoUiStateTransport();
    uiHub.AddEndpoint(_localUiTransport);
    controller.AddListener();
}

public LocalGizmoUiStateTransport LocalUiTransport { get; }

public void Dispose()
{
    _uiHub.RemoveEndpoint(_localUiTransport);
    _controller.RemoveListener();
}
```

`RegisterSystems()` is empty. The local terminal reads the `DebugPrimitiveBuffer` directly
(zero-copy); no primitive transport system needed.

`Name` = `"LocalTerminal"`, `Policy` = `ExecutionPolicy.Synchronous()`.

**Success conditions:**
- Unit test `GZH009_1`: instantiate `LocalTerminalModule`. Verify `controller.ListenerCount == 1`.
  Dispose the module. Verify `controller.ListenerCount == 0`.
- Unit test `GZH009_2`: publish a `GizmoUiState` via the hub. Verify `LocalUiTransport` receives
  it. Dispose module. Publish another state via hub. Verify `LocalUiTransport` does NOT receive it
  (endpoint was removed).

---

### GZH-010 — `GizmoNetworkTransportModule`

**Design ref:** DESIGN.md §4.2

**Location:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/GizmoNetworkTransportModule.cs`

**Description:**  
Installable `IEcsModule` for DDS transport.

Constructor:
```csharp
public GizmoNetworkTransportModule(
    GizmoExecutionController controller,
    GizmoUiStateHub uiHub,
    INetworkFactory networkFactory,
    DebugPrimitiveBuffer gizmoBuffer,
    long localNodeId,
    FdpEventBus interactionBus)
```

Steps in constructor:
1. Create `DdsWriterGizmoAdapter<GizmoUiState>` from the factory's participant.
2. Wrap it as `DdsGizmoUiStatePublisher` implementing `IGizmoUiStatePublisher`.
3. Call `uiHub.AddEndpoint(_ddsUiPublisher)`.
4. Create `_primitivePublisherSystem` via `networkFactory.CreateGizmoPublisherSystem(...)`.
5. Create ingress/egress translators via `networkFactory.CreateGizmoTranslators(...)`.
   - The ingress translator maintains a `HashSet<uint> _connectedTerminalIds`.
   - When a new `IGCapabilitiesAnnounce` sample arrives and its node ID is **not** in the set:
     add it, then call `controller.AddListener()`.
   - When a sample arrives with `InstanceState != Alive` and its node ID **is** in the set:
     remove it, then call `controller.RemoveListener()`.
6. Do NOT call `controller.AddListener()` in the constructor — listener count stays 0
   until an actual remote terminal announces.

`RegisterSystems()`: registers the publisher system and translator systems.

`Dispose()`: removes the hub endpoint. Drains `_connectedTerminalIds`:
```csharp
foreach (var _ in _connectedTerminalIds) _controller.RemoveListener();
_connectedTerminalIds.Clear();
```

**Success conditions:**
- Unit test `GZH010_1` (using fake factories): construct the module. Verify `controller.ListenerCount == 0` (no terminal has announced yet). Dispose. Verify `controller.ListenerCount == 0`.
- Unit test `GZH010_2`: simulate one `IGCapabilitiesAnnounce` sample (new node ID). Verify `controller.ListenerCount == 1`. Simulate a second sample (different node ID). Verify `controller.ListenerCount == 2`. Simulate lifecycle-dead sample for the first node ID. Verify `controller.ListenerCount == 1`.
- Integration test `GZH010_3`: with a real DDS participant in a test domain, install the module.
  Publish a `GizmoUiState` via the hub. Verify it is written to the DDS topic.

---

## Phase 4: `LayerControlGizmo` Upgrade

### GZH-011 — Refactor `LayerControlGizmo`

**Design ref:** DESIGN.md §7

**Location:** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`

**Description:**

**Change 1** — Dynamic schema hash:
```csharp
// BEFORE:
public const uint SchemaHash = 0x8899AABB;

// AFTER:
public static readonly uint SchemaHash =
    GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!);
```

**Change 2** — Constructor adds optional `IGizmoUiStatePublisher?`:
```csharp
public LayerControlGizmo(
    long anchorId,
    FdpEventBus interactionBus,
    IComponentEditService editService,
    IGizmoUiStatePublisher? uiPublisher = null)
```

**Change 3** — Replace `MakeStructInspector` emission with `StructInspectorProjector<LayerControlDto>`:
- Add `private readonly StructInspectorProjector<LayerControlDto> _projector;`
- Initialise in constructor with `editService` and `uiPublisher`.
- In `UpdateAndDraw`: replace the raw `MakeStructInspector` call with `_projector.EmitAndSync(...)`.
- In `OnStructUpdate`: replace manual JSON deserialisation with `_projector.ApplyUpdate(payloadJson, ref _dto)`.

Update all composition roots that instantiate `LayerControlGizmo` (`SimHostApp.cs`,
`IgApplication.cs`, `EditorSubsystem.cs`) to pass the `GizmoUiStateHub` as the `uiPublisher`.

**Change 4** — Update terminal-side schema registry pre-seeding.  
Anywhere that the terminal (IG, Editor) pre-seeds a `LayerControlDto` schema under the old hash
`0x8899AABB`, replace that hash with:
```csharp
GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!)
```
Both sides must use the same deterministic computation. After this change, the pre-seeded schema
hash and the `StructInspector` primitive's `SchemaHash` field will agree automatically.

**Success conditions:**
- Unit test `GZH011_1`: compute `SchemaHash`. Verify it equals
  `GizmoSettingsRegistry.ComputeHash("Hrot.Common.Diagnostics.Gizmos.LayerControlDto")`.
- Unit test `GZH011_2`: construct `LayerControlGizmo` with a mock publisher. Call `UpdateAndDraw`
  with `_isEditing = true`. Verify publisher receives the DTO JSON. Call it a second time with the
  same DTO. Verify publisher receives exactly one total call.
- Unit test `GZH011_3` (regression): existing `SC_GZ067` through `SC_GZ070` unit tests still pass.

---

## Phase 5: ClusterRunner Dynamic Window

### GZH-012 — `OpenLocalWindow()` and `CloseLocalWindow()`

**Design ref:** DESIGN.md §8.2, §8.3

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Program.cs` and/or a new `PresentationShell` class

**Description:**  
Extract the Raylib/ImGui bootstrap from the static `if (!config.Headless)` block into callable
methods. Add a `ConcurrentQueue<Action<SubsystemOrchestrator>> _pendingConsoleActions` field.

`OpenLocalWindow()`:
- Guards against double-open.
- Calls Raylib `SetConfigFlags`, `InitWindow`, `rlImGui.Setup`.
- Loads `IconAtlas`, creates `WindowManager`.
- Calls `subsystem.RegisterWindows(windowManager)` for all `IWindowRegistrar` subsystems.
- Wires `windowManager.OnPerspectiveChanged` → `PerspectiveCoordinatorSystem`.
- Calls `controller.AddListener()` for the currently-active map owner's controller.
- Sets `_isLocalWindowOpen = true`.

`CloseLocalWindow()`:
- Guards against double-close.
- Triggers RCU uninstall of `LocalTerminalModule` (calls `RemoveListener()` via dispose).
- Calls `windowManager.SaveSettings()`, `rlImGui.Shutdown()`, `Raylib.CloseWindow()`.
- Sets `_isLocalWindowOpen = false`.

The existing static startup path remains: `if (!config.Headless) OpenLocalWindow()` is called
during `Initialize()` so the default interactive behaviour is preserved.

**Success conditions:**
- Manual test: start ClusterRunner with `--headless`. No Raylib window opens. Issue `open` command.
  Window appears. Issue `close` command. Window disappears. Process is still running.
- Automated: unit test verifies `_isLocalWindowOpen` transitions correctly and that
  `controller.ListenerCount` is 1 after open and 0 after close (using mock Raylib stubs or
  compile-time seams).

---

### GZH-013 — `ConsoleCommandService`

**Design ref:** DESIGN.md §9

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Services/ConsoleCommandService.cs`

**Description:**  
Background REPL. Reads stdin on a dedicated thread (`IsBackground = true`). Registered commands
dispatch via an event to the main thread's `ConcurrentQueue`.

```csharp
public sealed class ConsoleCommandService : IDisposable
{
    private readonly Dictionary<string, Action> _commands;
    public event Action<Action<SubsystemOrchestrator>>? OnCommandDispatched;
    public void Start();   // spawns IsBackground thread
    public void Dispose(); // CancellationTokenSource.Cancel()
}
```

Initial commands: `open`, `close`, `help`, `exit`.

Wiring in `Program.cs`:
```csharp
using var consoleSvc = new ConsoleCommandService();
consoleSvc.OnCommandDispatched += orchestrator.EnqueueConsoleAction;
consoleSvc.Start();
```

**Success conditions:**
- Unit test `GZH013_1`: create `ConsoleCommandService` with a fake stdin (piped string). Call
  `Start()`. Verify `OnCommandDispatched` fires with an action that calls
  `orchestrator.OpenLocalWindow()` in response to the `open` command.
- Process teardown test: `Dispose()` cancels the background thread within 500 ms. No blocking.

---

### GZH-014 — Perspective-Aware `GizmoExecutionController` Switching

**Design ref:** DESIGN.md §10

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs`

**Description:**  
When `SwitchMapOwner(newOwner)` is called, the `PerspectiveCoordinatorSystem` must:
1. Call `_outgoingSubsystem.GizmoController.RemoveListener()`.
2. Call `_incomingSubsystem.GizmoController.AddListener()`.

Each subsystem exposes its `GizmoExecutionController` via an interface or property so the
coordinator can access it without a direct type dependency. A simple option: extend
`ISubsystem` (or add `IGizmoControllable`) to expose a nullable `GizmoExecutionController?`.

**Success conditions:**
- Unit test `GZH014_1`: two mock subsystems, each with its own controller (both starting at 0
  listeners, groups disabled). Simulate window open (adds listener to subsystem A). Switch
  perspective to subsystem B. Verify subsystem A's controller is at 0, subsystem B's is at 1.

---

## Phase 6: Remote Terminal Lifecycle

### GZH-015 — DDS Lifecycle Connect and Disconnect Detection

**Design ref:** DESIGN.md §5

**Location:**  
`FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/GizmoNetworkTransportModule.cs`
(ingress translator component)

**Description:**  
The ingress translator that reads `IGCapabilitiesAnnounce` must track per-terminal lifecycle using
a `HashSet<uint> _connectedTerminalIds`. On each sample read:

- If `sample.Info.InstanceState == DdsInstanceState.Alive` **and** the node ID is **not** in the
  set: add it, publish `TerminalConnectedEvent` on `FdpEventBus`, call
  `_controller.AddListener()`.
- If `sample.Info.InstanceState != DdsInstanceState.Alive` (`NotAliveDisposed` or
  `NotAliveNoWriters`) **and** the node ID **is** in the set: remove it, publish
  `TerminalDisconnectedEvent` on `FdpEventBus`, call `_controller.RemoveListener()`.

This is part of `GZH-010` scope but listed separately for tracking since it requires specific
CycloneDDS lifecycle handling expertise.

**Success conditions:**
- Unit test `GZH015_1` (fake DDS reader): deliver a sample with `InstanceState = Alive` and a
  fresh node ID. Verify `TerminalConnectedEvent` appears on the interaction bus and
  `controller.ListenerCount == 1`.
- Unit test `GZH015_2` (fake DDS reader): after a connect, deliver a sample for the same node ID
  with `InstanceState = NotAliveNoWriters`. Verify `TerminalDisconnectedEvent` appears on the bus
  and `controller.ListenerCount == 0`.
- Unit test `GZH015_3`: deliver a dead-instance sample for a node ID that was never in the set.
  Verify `TerminalDisconnectedEvent` is NOT emitted and `ListenerCount` remains unchanged.
- Unit test `GZH015_4`: deliver two alive samples for the **same** node ID. Verify
  `TerminalConnectedEvent` and `AddListener` are each called exactly once (idempotent).

---

## Phase 7: Input Isolation

### GZH-016 — Subsystem Input Collision Fix

**Design ref:** DESIGN.md §11

**Location:**  
- `FDP/Engine/Fdp.Presentation/Vis2D/Defaults/RaylibInputProvider.cs`
- Each subsystem's `Update()` method (IG, SimHost, CGF, Editor)

**Description:**

**Step 1** — Add capture-flag properties to `RaylibInputProvider`:
```csharp
public bool IsMouseCaptured    => ImGui.GetIO().WantCaptureMouse;
public bool IsKeyboardCaptured => ImGui.GetIO().WantCaptureKeyboard;
```
These must also be added to `IInputProvider`.

**Step 2** — Gate canvas and gizmo input in each subsystem's `Update()`:
```csharp
if (_orchestrator.IsActiveMapOwner(this) && !_inputProvider.IsMouseCaptured)
{
    _canvas.Update(deltaTime);
    _gizmoLayer.HandleInput(...);
}
```

**Step 3** — Verify `DebugGizmoLayer.HandleInput` also checks `isMouseCaptured` before processing
left-clicks, right-clicks, and raw-input events (this check may already partially exist; verify
and fill any gaps).

**Success conditions:**
- Unit test `GZH016_1` (mock input provider): set `WantCaptureMouse = true`. Call
  `_canvas.Update()` and `_gizmoLayer.HandleInput()` via the subsystem's `Update`. Verify
  neither the canvas nor the gizmo layer processes any mouse events.
- Unit test `GZH016_2`: set `IsActiveMapOwner = false` for subsystem B. Call subsystem B's
  `Update()`. Verify neither canvas nor gizmo layer receives input calls.
