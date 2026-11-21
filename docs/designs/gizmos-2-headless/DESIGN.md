# Gizmos-2 Headless — Design Document

> **Codebase baseline:** v189  
> **Scope:** Dynamic headless/interactive transport, zero-CPU idle gizmos, StructInspector live-sync, ClusterRunner dynamic window, input isolation.

---

## 0. Context and Motivation

The v189 codebase has a complete composite-key routing architecture and terminal-side state machines
(GZH session prior art). This design phase adds the *operational* layer on top of that:

- **Zero-CPU headless** — SimHost/CGF/ClusterRunner processes should burn no gizmo CPU when no
  terminal is watching.
- **Dynamic terminal attach/detach** — local Raylib windows and remote DDS terminals can be added
  and removed at runtime without restarting the process.
- **Live StructInspector sync** — backend gizmos can push live DTO state to any connected terminal
  transparently, regardless of whether the terminal is local or remote.
- **Clean ClusterRunner** — the ClusterRunner's Raylib window becomes optional and dynamically
  openable from the console; per-subsystem perspective switching controls which subsystem's gizmos
  run.

---

## 1. Dual-Channel Architecture (Visual + UI-State)

Two data streams serve completely different purposes and must remain separate:

| Stream | Direction | Frequency | QoS | Mechanism |
|---|---|---|---|---|
| `DebugPrimitive` batch | Host → Terminal | 60 Hz | BestEffort | `DebugPrimitiveBuffer` / DDS |
| `GizmoUiState` JSON | Host → Terminal | on-change | TransientLocal / KeepLast(1) | `IGizmoUiStatePublisher` |

Merging them is architecturally impossible: the 64-byte primitive struct cannot carry arbitrary
JSON, and sending static UI state 60 Hz wastes bandwidth and CPU.

The `StructInspectorProjector<T>` (see §2.1) hides this duality from the gizmo author. The gizmo
calls one method per frame; the projector decides whether to publish JSON based on whether the DTO
has changed.

---

## 2. New Types — `Fdp.Toolkits` Assembly

### 2.1 `StructInspectorProjector<T>`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/UI/StructInspectorProjector.cs`

Generic per-gizmo helper. Injected into any gizmo that wants to render a `StructInspector` panel.

```
public sealed class StructInspectorProjector<T> where T : class
{
    public StructInspectorProjector(
        IComponentEditService editService,
        IGizmoUiStatePublisher? uiPublisher) { … }

    // Called every frame; emits primitive + conditionally publishes JSON.
    public void EmitAndSync(
        IDebugDrawBuilder draw,
        long networkId, uint schemaHash, T dto,
        ScreenAnchor anchor = ScreenAnchor.TopLeft,
        SizeMode sizeMode = SizeMode.ScreenPixels) { … }

    // Called from OnStructUpdate; deserialises JSON back into dto,
    // updates the cache so it won't echo the update back.
    public void ApplyUpdate(string payloadJson, ref T dto) { … }
}
```

**Key invariants:**
- Caches the last-serialised JSON string. `EmitAndSync` only calls `uiPublisher.Publish` when the
  JSON differs from the cache — even if `UpdateAndDraw` fires 60 times per second.
- `uiPublisher` is nullable; when null (headless, no publisher wired) the projector only emits the
  primitive, never allocates JSON.
- `ApplyUpdate` refreshes the cache after applying the incoming payload, preventing an immediate
  echo back to the terminal.

**Dependencies:** `StructEdit.Core`, `StructEdit.Json`, `IGizmoUiStatePublisher`, `IDebugDrawBuilder`.  
All already referenced by `Fdp.Toolkits.csproj`.

---

### 2.2 `GizmoUiStateHub` (Multiplexer)

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/GizmoUiStateHub.cs`

Permanent `IGizmoUiStatePublisher` injected into all backend gizmos at startup. When no terminal
is connected the hub silently discards updates (zero memory growth, zero CPU).

```
public sealed class GizmoUiStateHub : IGizmoUiStatePublisher
{
    public void AddEndpoint(IGizmoUiStatePublisher endpoint) { … }
    public void RemoveEndpoint(IGizmoUiStatePublisher endpoint) { … }
    void IGizmoUiStatePublisher.Publish(GizmoUiState state) { … } // routes to all endpoints
}
```

Thread-safe via `lock`. Multiple concurrent terminals are fully supported: the hub broadcasts the
same JSON update to every registered endpoint (local queue + DDS writer simultaneously).

---

### 2.3 `LocalGizmoUiStateTransport`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Hub/LocalGizmoUiStateTransport.cs`

In-memory bridge from backend publisher to local terminal consumer.

```
public sealed class LocalGizmoUiStateTransport : IGizmoUiStatePublisher
{
    // Producer (called by hub / gizmo)
    void IGizmoUiStatePublisher.Publish(GizmoUiState state);   // overwrite by GizmoInstanceId

    // Consumer (called by local render loop)
    // callback receives each queued state; caller invokes adapter.ReceiveUiState(state)
    public void PollAndApply(Action<GizmoUiState> handler);
}
```

**Bounded memory design:** uses `ConcurrentDictionary<uint, GizmoUiState>` keyed by
`GizmoInstanceId`. The producer simply overwrites the entry; any backlog is at most one state per
active schema. The consumer iterates, fires the callback, then clears the dictionary.

> **Why `Action<GizmoUiState>` not `ImGuiPropertyTreeAdapter`?**  
> `ImGuiPropertyTreeAdapter` lives in `GizmoMap.Presentation`, which is above `Fdp.Toolkits` in
> the dependency graph. The callback keeps `LocalGizmoUiStateTransport` in the Toolkits layer;
> callers in the presentation layer close over the adapter reference.

---

### 2.4 `GizmoExecutionController`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoExecutionController.cs`

Reference-counted gate around the `TogglablePostSimulationGroup` that wraps the three core gizmo
systems. When the count drops to zero, all interactive gizmos are synchronously cancelled before
the group is toggled off.

```
public sealed class GizmoExecutionController
{
    public GizmoExecutionController(
        TogglablePostSimulationGroup group,
        GlobalGizmoManager globalManager,
        DataDrivenGizmoSystem dataDrivenSystem) { … }

    public void AddListener() { … }    // Interlocked.Increment; enables group at 1
    public void RemoveListener() { … } // Interlocked.Decrement; triggers cleanup at 0
}
```

**Teardown sequence when count → 0 (Synchronous Direct Teardown):**
1. Call `_globalManager.CancelInteractiveTools()` synchronously.
2. Call `_dataDrivenSystem.CancelInteractiveTools()` synchronously.
3. Set `_group.Enabled = false` immediately.

**Why not use the event bus for teardown?**  
`FdpEventBus` is strictly double-buffered: `Publish()` writes to the back-buffer; systems
reading the front-buffer in the same frame cannot see it. Forcing a `SwapBuffers()` mid-frame
would silently destroy all unprocessed events meant for other systems. Trying to force an
extra `Execute()` pass would require holding an `ISimulationView`, which violates the controller's
POJO design. The synchronous call approach sidesteps all of these hazards completely.

`TerminalDisconnectedEvent` is still published to the bus (by the DDS ingress translator or the
local module) as a notification for other components, but the manager cleanup does **not** depend
on it.

---

### 2.5 `TerminalConnectedEvent` / `TerminalDisconnectedEvent`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/TerminalLifecycleEvents.cs`

Network-agnostic FDP bus events that decouple the core ECS from DDS or Raylib specifics.

```csharp
public sealed class TerminalConnectedEvent
{
    public long TerminalId { get; init; }
}

public sealed class TerminalDisconnectedEvent
{
    public long TerminalId { get; init; }
}
```

**Routing rules:**
- **Local terminal (Raylib/ImGui):** `LocalTerminalModule` directly calls
  `controller.AddListener()` — no event needed, avoids bus timing hazards.
- **Remote terminal (DDS):** the `IGCapabilitiesAnnounce` ingress translator inside
  `GizmoNetworkTransportModule` tracks each announcing terminal. It calls
  `controller.AddListener()` when a new terminal ID appears and `controller.RemoveListener()`
  when that terminal's DDS instance goes non-alive. It also publishes `TerminalConnectedEvent`
  / `TerminalDisconnectedEvent` on the FDP bus as informational notifications for any other
  consumers — but those events do **not** drive the controller directly.

> **Note on `OnStructUpdate`:** The `IGizmoInteractionHandler` interface already contains
> `void OnStructUpdate(string payloadJson) { }` (default no-op, v189). `GlobalGizmoManager`
> already routes `GizmoStructUpdateEvent` payloads to `OnStructUpdate` by anchor ID. No
> interface or routing work is needed as part of this sprint.

---

## 3. `TogglablePostSimulationGroup` for Gizmos

In each subsystem's composition root, the three core gizmo systems are wrapped in a
`TogglablePostSimulationGroup` (already exists in `Fdp.ModuleHost`) and a `GizmoExecutionController`:

```csharp
var gizmoGroup = new TogglablePostSimulationGroup("GizmoExecution",
    globalGizmoManager,
    dataDrivenGizmoSystem,
    statelessGizmoSystem);

gizmoGroup.Enabled = false; // headless by default

kernel.RegisterModule(new GizmoSystemModule(gizmoGroup)); // registers the group as a system
```

The gizmo managers (`GlobalGizmoManager`, `DataDrivenGizmoSystem`) are always instantiated and
backend tools can always call `Register()` on them. Only their `Execute()` bodies are bypassed
when the group is disabled.

---

## 4. Dynamic Transport Modules

### 4.1 `LocalTerminalModule`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/LocalTerminalModule.cs`

Installable `IEcsModule` that bridges a local Raylib/ImGui window to the backend gizmo pipeline.

Lifecycle:
1. **Constructor:** creates `LocalGizmoUiStateTransport`, registers it with the `GizmoUiStateHub`,
   calls `controller.AddListener()`.
2. **`RegisterSystems()`:** empty — the local terminal reads primitives directly from the
   `DebugPrimitiveBuffer` span (zero-copy), not via a transport copy.
3. **`Dispose()`:** removes endpoint from hub, calls `controller.RemoveListener()`.

No `TerminalConnectedEvent` is published for local terminals. The direct `AddListener()` call is
synchronous and avoids event-bus timing issues during bootstrap.

---

### 4.2 `GizmoNetworkTransportModule`

**File:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Modules/GizmoNetworkTransportModule.cs`

Installable `IEcsModule` for DDS transport. Installed **once** for the lifetime of the DDS
infrastructure (not once per remote terminal). The ingress translator inside the module manages
per-terminal listener count tracking.

Lifecycle:
1. **Constructor:** creates DDS writer adapter, registers `DdsGizmoUiStatePublisher` with the hub.
   Does **not** call `controller.AddListener()` — that is done per-terminal by the ingress
   translator when a real remote terminal announces.
2. **`RegisterSystems()`:** registers `DebugPrimitivesBatchPublisherSystem` (60 Hz Export phase)
   and the `IGCapabilitiesAnnounce` ingress translator.
3. **Ingress translator per-frame:** maintains `HashSet<uint> _connectedTerminalIds`.
   - New `IGCapabilitiesAnnounce` sample with unknown node ID → add to set, call
     `controller.AddListener()`, publish `TerminalConnectedEvent` to bus.
   - Sample with `InstanceState != Alive` (disconnect / crash) → remove from set, call
     `controller.RemoveListener()`, publish `TerminalDisconnectedEvent` to bus.
4. **`Dispose()`:** calls `controller.RemoveListener()` once for each ID still in
   `_connectedTerminalIds` (drains any unbalanced counts); removes hub endpoint.

---

### 4.3 Multi-Transport Coexistence

Multiple modules can be installed simultaneously. The `GizmoUiStateHub` broadcasts to all active
endpoints. The `GizmoExecutionController` listener count accommodates multiple concurrent modules.
The `DebugPrimitiveBuffer` is read once per frame by the local terminal directly and once by the
`DebugPrimitivesBatchPublisherSystem` over DDS — both reads are safe because the buffer's
`GetFrame()` returns a `ReadOnlySpan` (zero-copy, no side effects).

---

## 5. Remote Terminal Connect / Disconnect Detection

The `IGCapabilitiesAnnounce` DDS topic uses **TransientLocal durability with KeepLast(1)**.

**Connect:** when a new remote terminal boots and publishes its capabilities, the ingress
translator sees a fresh `IGCapabilitiesAnnounce` sample with an unknown node ID. It adds the ID
to its `HashSet<uint>`, calls `controller.AddListener()`, and publishes `TerminalConnectedEvent`
on the FDP bus. Because `TransientLocal` caches the last sample, a late-joining translator will
also correctly detect already-connected terminals on startup.

**Disconnect:** when a remote terminal crashes or disconnects cleanly, CycloneDDS generates a
disposal lifecycle sample (`InstanceState != Alive`, e.g., `NotAliveNoWriters` or
`NotAliveDisposed`). The ingress translator removes the ID from its set, calls
`controller.RemoveListener()`, and publishes `TerminalDisconnectedEvent` on the FDP bus.

No custom heartbeat or keepalive is required — DDS lifecycle management handles this
automatically.

---

## 6. Gizmo Manager Updates — `CancelInteractiveTools()`

Both gizmo managers expose a synchronous `CancelInteractiveTools()` method called directly by
`GizmoExecutionController.RemoveListener()` when the last terminal disconnects. This avoids any
reliance on the `FdpEventBus` double-buffering cycle for teardown.

### 6.1 `GlobalGizmoManager.CancelInteractiveTools()`

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
    // Remove on-demand tools; keep permanent tools (RequiresExclusiveFocus == false
    // AND WantsRawInput == false) such as LayerControlGizmo.
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

Global "always-on" tools (like `LayerControlGizmo`, which have `RequiresExclusiveFocus = false`
and `WantsRawInput = false`) survive terminal disconnects unchanged.

### 6.2 `DataDrivenGizmoSystem.CancelInteractiveTools()`

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

---

## 7. `LayerControlGizmo` Refactoring

**File:** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`

Three targeted changes to the existing implementation:

### 7.1 Remove hardcoded `SchemaHash`

Replace:
```csharp
public const uint SchemaHash = 0x8899AABB;
```
With:
```csharp
public static readonly uint SchemaHash =
    GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!);
```
This guarantees the hash matches what the terminal derives via reflection, without magic numbers.

### 7.2 Add `IGizmoUiStatePublisher?` constructor parameter

The gizmo receives the `GizmoUiStateHub` via its constructor. When null, live DTO sync is
disabled (headless / pre-seeded schema mode still works).

```csharp
public LayerControlGizmo(
    long anchorId,
    FdpEventBus interactionBus,
    IComponentEditService editService,
    IGizmoUiStatePublisher? uiPublisher = null)   // NEW
```

### 7.3 Replace raw `MakeStructInspector` call with `StructInspectorProjector<LayerControlDto>`

Remove the manual `MakeStructInspector` emission and DTO JSON tracking. Delegate to the projector:

```csharp
private readonly StructInspectorProjector<LayerControlDto> _projector;

// In UpdateAndDraw:
if (_isEditing)
    _projector.EmitAndSync(draw, _anchorId, SchemaHash, _dto, ScreenAnchor.Center, SizeMode.ScreenPercent);

// In OnStructUpdate:
_projector.ApplyUpdate(payloadJson, ref _dto);
_activeLayers = _dto.ToMask();
_isEditing = false;
```

**Note:** `_anchorId` is already injected via constructor in v189 — it is not hardcoded.

---

## 8. ClusterRunner Dynamic Window

### 8.1 Motivation

Currently, in `Program.cs`, Raylib/ImGui initialisation is inside a static `if (!config.Headless)`
block. Making the window optional and runtime-openable requires extracting this logic into callable
methods.

### 8.2 `OpenLocalWindow()` and `CloseLocalWindow()`

These methods are added to `SubsystemOrchestrator` (or a dedicated `PresentationShell` wrapper).
Both operate on the main rendering thread.

**`OpenLocalWindow()`:**
1. Guard: return if already open.
2. Call `Raylib.SetConfigFlags(...)`, `Raylib.InitWindow(...)`, `rlImGui.Setup(...)`.
3. Load `IconAtlas` from embedded resources.
4. Instantiate `WindowManager` with the atlas.
5. Call `RegisterWindows(windowManager)` on all `IWindowRegistrar` subsystems.
6. Wire `windowManager.OnPerspectiveChanged` to the `PerspectiveCoordinatorSystem`.
7. Install `LocalTerminalModule` via `kernel.InstallModuleAsync` for the active perspective's
   subsystem (to enable its gizmo CPU group).
8. Set `_isLocalWindowOpen = true`.

**`CloseLocalWindow()`:**
1. Guard: return if already closed.
2. Call `kernel.UninstallModuleAsync(_activeLocalTerminalModule)` (triggers manager cleanup and
   decrements listener count).
3. Call `windowManager.SaveSettings()`.
4. Call `rlImGui.Shutdown()`, `Raylib.UnloadTexture(...)`, `Raylib.CloseWindow()`.
5. Set `_isLocalWindowOpen = false`.

### 8.3 Run Loop Update

The main run loop is extended to drain console commands on the main thread:

```csharp
while (_running)
{
    while (_pendingConsoleActions.TryDequeue(out var action))
        action(this);

    float dt = GetDeltaTime();
    orchestrator.Update(dt);

    if (_isLocalWindowOpen && !Raylib.WindowShouldClose())
    {
        // ... Raylib draw + ImGui draw ...
    }
    else if (_isLocalWindowOpen && Raylib.WindowShouldClose())
    {
        CloseLocalWindow();
    }
}
```

---

## 9. Console Command Service

**File:** `Hrot/Runner/Hrot.ClusterRunner/Services/ConsoleCommandService.cs`

A background REPL that reads console input and enqueues `Action` delegates into a
`ConcurrentQueue<Action<SubsystemOrchestrator>>`. The main loop drains the queue on the main
thread — required because Raylib window operations must run on the thread that called `InitWindow`.

```csharp
public sealed class ConsoleCommandService : IDisposable
{
    public event Action<Action<SubsystemOrchestrator>>? OnCommandDispatched;

    public void Start(); // launches background thread with IsBackground = true
    public void Dispose();
}
```

Supported commands initially: `open`, `close`, `help`, `exit`.

**Thread safety:** The background `Console.ReadLine()` call is wrapped in a dedicated `Thread`
with `IsBackground = true` so the CLR does not wait for it during process teardown. The queue is
`ConcurrentQueue` (lock-free). Raylib calls are never made on the background thread.

---

## 10. Perspective-Aware CPU Saving

Each subsystem that hosts gizmos receives its own `GizmoExecutionController`. The
`PerspectiveCoordinatorSystem` is updated to call `RemoveListener()` on the outgoing subsystem's
controller and `AddListener()` on the incoming subsystem's controller whenever a perspective
switch occurs.

This ensures that only the subsystem currently visible on screen evaluates gizmo primitives and
ticks its stateful gizmos.

---

## 11. Subsystem Input Isolation

### 11.1 Problem

When multiple subsystems run in the same process, each subsystem's `Update()` may poll
`Raylib.IsMouseButtonPressed()`. Background subsystems steal clicks meant for the active one.
ImGui also intercepts events that the map canvas should see.

### 11.2 Solution: Two-Layer Gate

**Layer 1 — Perspective ownership:**  
In each subsystem's `Update()` (or within the `DebugGizmoLayer` hook), canvas and gizmo input
is only processed when the subsystem is the active map owner:

```csharp
if (_orchestrator.IsActiveMapOwner(this) && !ImGui.GetIO().WantCaptureMouse)
{
    _canvas.Update(deltaTime);
    _gizmoLayer.HandleInput(...);
}
```

**Layer 2 — ImGui capture flags:**  
`RaylibInputProvider` (already exists at
`FDP/Engine/Fdp.Presentation/Vis2D/Defaults/RaylibInputProvider.cs`) must expose:

```csharp
public bool IsMouseCaptured  => ImGui.GetIO().WantCaptureMouse;
public bool IsKeyboardCaptured => ImGui.GetIO().WantCaptureKeyboard;
```

The `MapCanvas` and `DebugGizmoLayer` query these properties and abort their input pipelines when
ImGui has captured the device. This prevents accidental gizmo activations when clicking ImGui
panels.

---

## 12. Composition Root Wiring Example

The complete wiring at startup (per subsystem that uses gizmos):

```csharp
// 1. Hub (permanent, lives until subsystem shutdown)
var uiHub = new GizmoUiStateHub();

// 2. Gizmo systems + group
var dataDrivenSystem = new DataDrivenGizmoSystem(...);
var globalManager    = new GlobalGizmoManager(drawBuilder, interactionBus);
var statelessSystem  = new StatelessGizmoSystem(gizmoRegistry, drawBuilder);

var gizmoGroup = new TogglablePostSimulationGroup("GizmoExecution",
    globalManager, dataDrivenSystem, statelessSystem);
gizmoGroup.Enabled = false;
kernel.RegisterModule(new GizmoSystemModule(gizmoGroup));

// 3. Controller (no FdpEventBus needed — teardown is synchronous via CancelInteractiveTools)
var controller = new GizmoExecutionController(gizmoGroup, globalManager, dataDrivenSystem);

// 4. Backend tools (always instantiated, even headless)
long layerControlId = GlobalGizmoManager.NewId();
var layerControl = new LayerControlGizmo(
    layerControlId, interactionBus, editService, uiHub);
globalManager.Register(layerControlId, layerControl);

// --- At startup (if DDS is available), install the DDS transport module once: ---
// It starts with 0 listeners; ingress translator will call AddListener per remote terminal.
var netModule = new GizmoNetworkTransportModule(controller, uiHub, networkFactory, gizmoBuffer);
await kernel.InstallModuleAsync(netModule);
// → listener count stays 0 until a remote terminal announces via IGCapabilitiesAnnounce

// --- At runtime, when local window is opened: ---
var localModule = new LocalTerminalModule(controller, uiHub);
await kernel.InstallModuleAsync(localModule);
// → controller.AddListener() → gizmoGroup.Enabled = true (count = 1)

// When remote terminal announces (handled internally by netModule ingress translator):
// → controller.AddListener() → count = 2

// --- On local window close: ---
await kernel.UninstallModuleAsync(localModule);
// → Dispose() → controller.RemoveListener() → (if count drops to 0)
//   CancelInteractiveTools() + group.Enabled = false

// --- On remote terminal disconnect (handled internally by netModule ingress translator): ---
// → controller.RemoveListener() → (if count drops to 0)
//   CancelInteractiveTools() + group.Enabled = false
```

---

## 13. Dependency Analysis and Project Placement

| New type | Target assembly | Key dependencies already present |
|---|---|---|
| `StructInspectorProjector<T>` | `Fdp.Toolkits` | `StructEdit.Core`, `StructEdit.Json`, `IGizmoUiStatePublisher` |
| `GizmoUiStateHub` | `Fdp.Toolkits` | `IGizmoUiStatePublisher`, `GizmoUiState` (global alias) |
| `LocalGizmoUiStateTransport` | `Fdp.Toolkits` | `IGizmoUiStatePublisher`, `GizmoUiState` |
| `GizmoExecutionController` | `Fdp.Toolkits` | `TogglablePostSimulationGroup` (Fdp.ModuleHost), `GlobalGizmoManager`, `DataDrivenGizmoSystem` |
| `TerminalLifecycleEvents` | `Fdp.Toolkits` | (plain classes, no deps) |
| `LocalTerminalModule` | `Fdp.Toolkits` | `IEcsModule`, `LocalGizmoUiStateTransport`, `GizmoUiStateHub` |
| `GizmoNetworkTransportModule` | `Fdp.Toolkits` | `IEcsModule`, `DdsWriterGizmoAdapter`, `GizmoUiStateHub` |
| `ConsoleCommandService` | `Hrot.ClusterRunner` | `SubsystemOrchestrator` |

No new `<ProjectReference>` entries are needed in `Fdp.Toolkits.csproj`.  
`Hrot.ClusterRunner` already references `Fdp.Toolkits` transitively.

---

## 14. Issues Acknowledged and Accepted Policies

| # | Issue | Policy |
|---|---|---|
| 1 | Two terminals edit same DTO simultaneously | **Last-write-wins.** No OCC versioning. Acceptable for diagnostic tools. |
| 2 | Remote terminal crash detection | Handled by `IGCapabilitiesAnnounce` TransientLocal QoS lifecycle events. |
| 3 | `LocalGizmoTransport` allocates arrays via `ToArray()` | **Postponed.** Only used in examples and unit tests, not production hot path. |
| 4 | `LocalGizmoUiStateTransport` unbounded queue | **Fixed.** ConcurrentDictionary with overwrite semantics (bounded by schema count). |
| 5 | Console REPL blocks process exit | **Fixed.** Background thread (`IsBackground = true`). |
| 6 | Background subsystem input collision | **Fixed.** Two-layer gate: `IsActiveMapOwner` + ImGui capture flags. |
| 7 | `DdsGizmoUiStatePublisher` remains in hub after remote disconnect; DDS module is installed for DDS lifetime, not per terminal | **Accepted.** CycloneDDS drops samples silently at the C-core boundary when no matched readers exist. No memory leak; negligible CPU on no-reader write path. |
