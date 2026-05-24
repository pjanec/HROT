the UI evnets are cluttering the world event bus. Can we move them elsewhere?

Mixing ephemeral UI interaction state with pure domain simulation events violates boundary separation and thrashes the world event bus with high-frequency noise. We must quarantine the presentation tier's event traffic.

We have already established a precedent for this exact architectural constraint in the `AutonomousPerceptionModule`, which isolates its high-volume inter-stage events to avoid global bus corruption. We can apply the same module-private bus strategy to the UI and Gizmo pipelines.

Here is the implementation strategy to seamlessly segregate UI events without modifying the target ECS systems:

**1. Allocate a Dedicated UI Event Bus**
Instantiate an isolated `FdpEventBus` at the composition root specifically for interaction traffic, completely separate from the world bus. 

**2. Re-route Frontend Emission**
Inject this dedicated bus into the presentation layer components, such as `DebugGizmoLayer` and the context menu adapters, so they publish `GizmoDragUpdateEvent`, `GizmoMouseEvent`, and `GlobalActionRequestedEvent` exclusively to the isolated bus rather than the world bus.

**3. Implement a Scoped View Decorator**
Introduce a wrapper around `ISimulationView`, mirroring the `PerceptionScopedView` design. The decorator must intercept `ReadEvents<T>()` and `GetCommandBuffer().PublishEvent<T>()` to redirect designated UI event types to the isolated bus, while transparently delegating all structural ECS queries and component mutations to the underlying repository. You should enforce a strict whitelist of event types that are permitted to flow through this scoped bus to keep the isolation contract explicit.

**4. Execute Interaction Systems in an Isolated Module**
Group the `DataDrivenGizmoSystem`, `GlobalGizmoManager`, and `GlobalActionDispatchSystem` into a dedicated interaction module. During its `Tick` execution, wrap the global view in the scoped view decorator and pass it to these systems. This guarantees the systems cleanly consume interactions from the UI bus and commit valid domain mutations to the real entity command buffer. You must explicitly swap the isolated bus buffers via `SwapBuffers()` prior to evaluating these systems so the pipeline correctly advances the event state for the read buffer.

This architecture completely severs the presentation-tier event storm from the simulation domain. The world bus remains pristine, the interaction systems process inputs without modification, and we maintain strict unidirectional data flow.


pls give me more details; note we need to do this for all subsystems (so it works in any selected clusterrunner perspective)


To successfully segregate presentation-tier interaction noise from the global simulation event bus across all subsystems (`Editor`, `SimHost`, `IG`, and `CGF`), we must replicate the exact boundary-enforcement pattern established in the `AutonomousPerceptionModule`. 

Since `ModuleHostKernel.RegisterGlobalSystem` always injects the raw, unfiltered `EntityRepository`, we cannot decorate the view for globally registered systems. Instead, we must strip the gizmo systems out of the global execution pipeline and encapsulate them into a dedicated `IEcsModule`. This allows us to intercept the execution context, inject a scoped view, and advance the isolated event bus safely.

Here is the precise mechanical implementation to enforce this boundary universally.

### 1. Implement the Interaction Decorators
We introduce a targeted anti-corruption layer. This view intercepts ECS event queries and routes them to the dedicated UI bus, while delegating all structural reads and component mutations to the underlying simulation world.

```csharp
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Hrot.Common.Events;

namespace Hrot.Common.Interactions
{
    public sealed class InteractionScopedView : ISimulationView
    {
        private readonly ISimulationView _inner;
        private readonly FdpEventBus _interactionBus;
        private readonly InteractionScopedCommandBuffer _cmdBuf;

        public InteractionScopedView(ISimulationView inner, FdpEventBus interactionBus)
        {
            _inner = inner;
            _interactionBus = interactionBus;
            _cmdBuf = new InteractionScopedCommandBuffer(inner.GetCommandBuffer(), interactionBus);
        }

        public uint Tick => _inner.Tick;
        public float Time => _inner.Time;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged => ref _inner.GetComponentRO<T>(e);
        public T GetManagedComponentRO<T>(Entity e) where T : class => _inner.GetManagedComponentRO<T>(e);
        public bool IsAlive(Entity e) => _inner.IsAlive(e);
        public bool HasComponent<T>(Entity e) where T : unmanaged => _inner.HasComponent<T>(e);
        public bool HasManagedComponent<T>(Entity e) where T : class => _inner.HasManagedComponent<T>(e);
        public QueryBuilder Query() => _inner.Query();
        public IEntityCommandBuffer GetCommandBuffer() => _cmdBuf;

        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged
        {
            // Whitelist unmanaged interaction events
            if (typeof(T) == typeof(GizmoDragUpdateEvent) ||
                typeof(T) == typeof(GizmoMouseEvent) ||
                typeof(T) == typeof(GizmoKeyEvent) ||
                typeof(T) == typeof(GizmoInteractionStartedEvent) ||
                typeof(T) == typeof(GizmoInteractionCommitEvent) ||
                typeof(T) == typeof(GizmoInteractionCancelEvent) ||
                typeof(T) == typeof(GlobalActionRequestedEvent))
            {
                return _interactionBus.Read<T>();
            }
            return _inner.ReadEvents<T>();
        }

        public IReadOnlyList<T> ReadManagedEvents<T>()
        {
            // Whitelist managed interaction events
            if (typeof(T) == typeof(ContextActionTriggered))
            {
                return _interactionBus.ReadManaged<T>();
            }
            return _inner.ReadManagedEvents<T>();
        }
    }

    private sealed class InteractionScopedCommandBuffer : IEntityCommandBuffer
    {
        private readonly IEntityCommandBuffer _realEcb;
        private readonly FdpEventBus _interactionBus;

        public InteractionScopedCommandBuffer(IEntityCommandBuffer realEcb, FdpEventBus interactionBus)
        {
            _realEcb = realEcb;
            _interactionBus = interactionBus;
        }

        public void PublishEvent<T>(in T evt) where T : unmanaged
        {
            if (typeof(T) == typeof(GlobalActionRequestedEvent))
            {
                _interactionBus.Publish(evt);
                return;
            }
            _realEcb.PublishEvent(evt);
        }

        public Entity CreateEntity() => _realEcb.CreateEntity();
        public void DestroyEntity(Entity entity) => _realEcb.DestroyEntity(entity);
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged => _realEcb.AddComponent(entity, component);
        public void SetComponent<T>(Entity entity, in T component) where T : unmanaged => _realEcb.SetComponent(entity, component);
        public void RemoveComponent<T>(Entity entity) where T : unmanaged => _realEcb.RemoveComponent<T>(entity);
        public void AddManagedComponent<T>(Entity entity, T? component) where T : class => _realEcb.AddManagedComponent(entity, component);
        public void SetManagedComponent<T>(Entity entity, T? component) where T : class => _realEcb.SetManagedComponent(entity, component);
        public void RemoveManagedComponent<T>(Entity entity) where T : class => _realEcb.RemoveManagedComponent<T>(entity);
        public void SetLifecycleState(Entity entity, Fdp.Toolkit.Lifecycle.Events.EntityLifecycle state) => _realEcb.SetLifecycleState(entity, state);
    }
}
```

### 2. Encapsulate the Interaction Pipeline
By packaging the gizmo systems into a standard `IEcsModule`, the kernel will automatically invoke `Tick()` *after* all simulation phases (Input, Simulation, PostSimulation) have completed. This is the mathematically correct execution slot: physics and kinematics have finalized the `SimTransform` positions, allowing the gizmos to overlay precisely on top of the updated geometry.

```csharp
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.Common.Systems;

namespace Hrot.Common.Interactions
{
    public sealed class GizmoInteractionModule : IEcsModule
    {
        public string Name => "GizmoInteraction";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly FdpEventBus _interactionBus;
        private readonly IEcsModuleSystem[] _systems;

        public GizmoInteractionModule(
            FdpEventBus interactionBus,
            ContextActionIngressSystem ingressSystem,
            GlobalActionDispatchSystem dispatchSystem,
            DataDrivenGizmoSystem dataDrivenSystem,
            GlobalGizmoManager globalManager,
            StatelessGizmoSystem statelessSystem)
        {
            _interactionBus = interactionBus;
            _systems = new IEcsModuleSystem[]
            {
                ingressSystem,
                dispatchSystem,
                dataDrivenSystem,
                globalManager,
                statelessSystem
            };
        }

        public void RegisterSystems(ISystemRegistry registry) { }

        public void Tick(ISimulationView view, float deltaTime)
        {
            // Advance the isolated bus state before evaluating interactions
            _interactionBus.SwapBuffers();
            
            var scopedView = new InteractionScopedView(view, _interactionBus);

            foreach (var sys in _systems)
            {
                sys.Execute(scopedView, deltaTime);
            }
        }
    }
}
```

### 3. Subsystem Composition Root Wiring
You must apply this exact substitution across `EditorSubsystem.cs`, `SimHostApp.cs`, `IgApplication.cs`, and `CgfSubsystem.cs` to ensure the behavior is uniform across all selected ClusterRunner perspectives.

Locate the block where `DataDrivenGizmoSystem` and `GlobalGizmoManager` are instantiated in the respective composition roots. 

**Replace the global registrations with the new module:**
```csharp
// 1. Create the dedicated bus
var interactionBus = new FdpEventBus();

// 2. Wire the presentation layer explicitly to the new bus
// (e.g., in EditorSubsystem.cs)
_canvas!.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer!, interactionBus, _world));
_contextMenuHandler = new JsonEntityContextMenuHandler(_world, interactionBus);

// 3. Remove Kernel.RegisterGlobalSystem calls for the interaction pipeline
// _kernel.RegisterGlobalSystem(_editorDataDrivenGizmoSystem); // DELETE
// _kernel.RegisterGlobalSystem(_globalGizmoManager);          // DELETE
// _kernel.RegisterGlobalSystem(new StatelessGizmoSystem(...));// DELETE

// 4. Inject the encapsulated module
_kernel.RegisterModule(new GizmoInteractionModule(
    interactionBus,
    new ContextActionIngressSystem(_entityMap),
    new GlobalActionDispatchSystem(actionRegistry),
    _editorDataDrivenGizmoSystem,
    _globalGizmoManager,
    new StatelessGizmoSystem(editorStatelessGizmoRegistry, _gizmoBuffer)
));
```

*(Note: Ensure `EntityDragGizmoDefinition` is registered into the `GizmoRegistry` during this step for `EditorSubsystem` and `CgfSubsystem` just like it currently is for `SimHostApp` and `IgApplication`.)*

### 4. Close the Event Storm Gating (Required across all perspectives)
Because `DebugGizmoLayer.cs` is shared infrastructure, modifying it immediately fixes the event flood for all subsystems. 

In `DebugGizmoLayer.HandleHover`, you must mandate a spatial delta threshold so static cursor frames do not publish redundant DDS traffic or ECS events:

```csharp
private Vector2 _lastHoverPos = new Vector2(float.NaN, float.NaN);

public void HandleHover(Vector2 mouseWorldPos)
{
    if (_eventBus == null) return;

    if (Vector2.DistanceSquared(_lastHoverPos, mouseWorldPos) < 0.0001f)
        return;

    _lastHoverPos = mouseWorldPos;

    if (_captureActive || _interactionToken.IsValid)
    {
        _eventBus.Publish(new GizmoDragUpdateEvent
        {
            // ...
        });
    }
}
```

This enforces perfect isolation. High-frequency UI interactions now circle efficiently within the `GizmoInteractionModule` pipeline on `_interactionBus`, allowing the FSMs to mutate domain state in the `EntityCommandBuffer` without polluting the global `_world.Bus` with raw coordinate streams.


My goal is also to stram the gizmos over the network  when clusterrunner is running in headless mode. how to do that, what is missing?


The structural pipeline is mostly intact. The architecture correctly populates the `_gizmoBuffer` in headless mode because both `DataDrivenGizmoSystem` and `StatelessGizmoSystem` are registered unconditionally in `SimHostApp.cs`, bypassing the `if (!_headless)` check. 

However, the network egress component is entirely absent. The file `DebugPrimitivesBatchPublisherSystem.cs` exists but is empty, and `SimHostApp` lacks any system registration to push the populated primitive buffer out over the DDS network.

To enable headless gizmo streaming, you must implement the `DebugPrimitivesBatchPublisherSystem` to run in the `Export` phase and wire it into the `SimHost` kernel.

### 1. Implement the Egress System
The system must grab the current frame from the buffer, perform a zero-allocation reinterpret cast to a byte span, and allocate the necessary heap array for the CycloneDDS `DebugPrimitivesBatch` struct. 

Open `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DebugPrimitivesBatchPublisherSystem.cs` and implement the missing logic:

```csharp
using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using GizmoMap.Network;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public sealed class DebugPrimitivesBatchPublisherSystem : IEcsModuleSystem
    {
        private readonly DebugPrimitiveBuffer _buffer;
        private readonly IDdsWriter<DebugPrimitivesBatch> _writer;
        private readonly byte _nodeId;

        public DebugPrimitivesBatchPublisherSystem(
            DebugPrimitiveBuffer buffer,
            IDdsWriter<DebugPrimitivesBatch> writer,
            byte nodeId)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _nodeId = nodeId;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var frame = _buffer.GetFrame();
            
            // Skip publishing if the buffer is empty to avoid flooding the network
            if (frame.Length == 0) return;

            var batch = new DebugPrimitivesBatch
            {
                FrameNumber    = view.Tick,
                NodeId         = _nodeId,
                PrimitivesData = MemoryMarshal.AsBytes(frame).ToArray()
            };

            _writer.Write(batch);
        }
    }
}
```

### 2. Wire the System in SimHostApp
Open `SimHostApp.cs`. Locate the block where the Gizmo systems are registered (around the `GZ032` comment block). You need to inject the new publisher system directly after the `StatelessGizmoSystem` registration, ensuring it only runs if a live DDS participant is present.

```csharp
_kernel.RegisterGlobalSystem(new StatelessGizmoSystem(
    _statelessGizmoRegistry,
    _gizmoBuffer,
    isSelectedPredicate: static (view, entity) =>
        view.HasComponent<SelectionState>(entity) &&
        view.GetComponentRO<SelectionState>(entity).IsSelected));

// INJECT THIS BLOCK
if (ddsParticipant != null)
{
    var batchWriter = new DdsWriterGizmoAdapter<DebugPrimitivesBatch>(ddsParticipant);
    _kernel.RegisterGlobalSystem(new DebugPrimitivesBatchPublisherSystem(
        _gizmoBuffer,
        batchWriter,
        (byte)localNodeId));
}
```

The IG side is already correctly architected to handle this data. It unconditionally creates a `DebugPrimitivesIngressTranslator` which polls the `DebugPrimitivesBatch` topic and writes the primitives into its local `GizmoPrimitiveBuffer`. Once you register the publisher on the SimHost, the network boundary is closed and headless gizmo streaming will function.


we of course need to receive the UI interactions from a remote viewer back to gizmos


The networking stack is already receiving the UI interactions. The `NedReplicationModule` internally wires the `GizmoInteractionIngressTranslator`, which successfully decodes the `GizmoInteractionBatch` DDS topic into typed ECS events (`GizmoDragUpdateEvent`, `GizmoMouseEvent`, `ContextActionTriggered`, etc.) during the `Input` phase.

However, there is an architectural disconnect. Because we successfully quarantined the interaction pipeline onto a private `_interactionBus` to prevent the UI event storm, the `DataDrivenGizmoSystem` and `GlobalActionDispatchSystem` are now deaf to the global world bus. The network ingress translator is publishing remote UI events to the global bus, where they are ignored.

To close the loop without breaking our quarantine boundary, we must implement a **Global-to-Scoped Event Bridge** directly inside the `GizmoInteractionModule`. We pull the network-originated interaction events off the global read buffer and inject them into our isolated bus just before evaluating the local interaction systems.

Here is the exact modification required in `GizmoInteractionModule.cs`:

```csharp
public void Tick(ISimulationView view, float deltaTime)
{
    // 1. Bridge network-originated UI events from the global bus into the isolated UI bus
    BridgeNetworkIngressEvents(view);

    // 2. Advance the isolated bus state before evaluating interactions
    _interactionBus.SwapBuffers();
    
    var scopedView = new InteractionScopedView(view, _interactionBus);

    foreach (var sys in _systems)
    {
        sys.Execute(scopedView, deltaTime);
    }
}

private void BridgeNetworkIngressEvents(ISimulationView view)
{
    // The GizmoInteractionIngressTranslator runs in the Input phase and publishes
    // these decoded network events to the global world bus. We mirror them into our private bus.
    
    // Spatial interactions
    foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionStartedEvent>()) 
        _interactionBus.Publish(evt);
    foreach (ref readonly var evt in view.ReadEvents<GizmoDragUpdateEvent>()) 
        _interactionBus.Publish(evt);
    foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionCommitEvent>()) 
        _interactionBus.Publish(evt);
    foreach (ref readonly var evt in view.ReadEvents<GizmoInteractionCancelEvent>()) 
        _interactionBus.Publish(evt);
        
    // Hardware interactions (Exclusive focus)
    foreach (ref readonly var evt in view.ReadEvents<GizmoMouseEvent>()) 
        _interactionBus.Publish(evt);
    foreach (ref readonly var evt in view.ReadEvents<GizmoKeyEvent>()) 
        _interactionBus.Publish(evt);

    // Semantic actions (Context Menus)
    foreach (var evt in view.ReadManagedEvents<ContextActionTriggered>()) 
        _interactionBus.PublishManaged(evt);
}
```

This preserves strict unidirectional data flow. The raw remote inputs enter through DDS, land on the global bus, cross the bridge into the private UI bus, and perfectly simulate a local operator action. The local `DataDrivenGizmoSystem` will process the remote drag exactly as if the local mouse moved, executing the FSM and safely mutating domain state inside its `EntityCommandBuffer`.


pure briding would leave the ui interaction events in the mail world bus which is we are trying to avoid


You are completely right. Bridging the events after the fact means the `GizmoInteractionIngressTranslator` has already polluted the global world bus during the `Input` phase. 

If we look closely at `GizmoInteractionIngressTranslator.cs`, there is an underlying architectural smell causing this: the translator explicitly downcasts `ISimulationView` to `EntityRepository` to reach the global `repo.Bus.PublishManaged` method. This hidden dependency violates the Liskov Substitution Principle and completely circumvents our `InteractionScopedView` decorator.

To achieve true quarantine, we must inject the isolated `_interactionBus` directly into the presentation-tier translators and execute them inside our encapsulated module. 

Here is how we enforce the strict network boundary.

### 1. Eradicate the Hidden Dependency in the Ingress Translator
Modify `GizmoInteractionIngressTranslator` to accept an `FdpEventBus` via constructor injection. Remove the `IEntityCommandBuffer` and the `view as EntityRepository` cast entirely.

```csharp
public sealed class GizmoInteractionIngressTranslator : INetworkTranslator
{
    private readonly IDdsReader<GizmoInteractionBatch>? _reader;
    private readonly FdpEventBus _interactionBus;

    public GizmoInteractionIngressTranslator(
        IDdsReader<GizmoInteractionBatch>? reader, 
        FdpEventBus interactionBus)
    {
        _reader = reader;
        _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (_reader == null) return;

        while (_reader.TryRead(out var batch))
        {
            ReceivedSampleCount++;
            Translate(batch); // No ECS view/cmd needed anymore
        }
    }

    private void Translate(in GizmoInteractionBatch batch)
    {
        // ... (token and pos resolution remains the same) ...

        switch (batch.Kind)
        {
            case GizmoInteractionEventKind.Started:
                _interactionBus.Publish(new GizmoInteractionStartedEvent { Token = token, WorldPos = worldPos });
                break;
            case GizmoInteractionEventKind.MenuAction:
                _interactionBus.PublishManaged(new ContextActionTriggered
                {
                    EntityNetworkId = (int)batch.PickAnchorId,
                    ActionName      = batch.ActionId.ToString(),
                });
                break;
            // ... (apply identical _interactionBus.Publish routing for the other cases) ...
        }
    }
}
```

### 2. Isolate the Egress Translator
Apply the exact same dependency injection to `GizmoInteractionEgressTranslator`. It should read its traffic exclusively from the `_interactionBus` rather than evaluating `view.ReadEvents<T>()`.

```csharp
public sealed class GizmoInteractionEgressTranslator : INetworkTranslator
{
    private readonly byte _nodeId;
    private readonly IDdsWriter<GizmoInteractionBatch>? _writer;
    private readonly FdpEventBus _interactionBus;

    public GizmoInteractionEgressTranslator(
        byte nodeId,
        IDdsWriter<GizmoInteractionBatch>? writer,
        FdpEventBus interactionBus)
    {
        _nodeId = nodeId;
        _writer = writer;
        _interactionBus = interactionBus;
    }

    public void ScanAndPublish(ISimulationView view)
    {
        if (_writer == null) return;

        foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionStartedEvent>())
            WriteRecord(GizmoInteractionEventKind.Started, evt.Token, evt.WorldPos);
            
        // ... (repeat for all other interaction events)
    }
}
```

### 3. Update the Translator Pack
Modify `GizmoTranslatorPack` to accept the isolated bus and pass it to the translators.

```csharp
public static IEnumerable<INetworkTranslator> Create(DdsParticipant participant, long localNodeId, FdpEventBus interactionBus)
{
    yield return new GizmoInteractionIngressTranslator(new DdsReaderGizmoAdapter<GizmoInteractionBatch>(participant), interactionBus);
    yield return new GizmoInteractionEgressTranslator((byte)localNodeId, new DdsWriterGizmoAdapter<GizmoInteractionBatch>(participant), interactionBus);
}
```

### 4. Execute the Network Boundary Inside the Encapsulated Module
In your composition root (`SimHostApp.cs`, `IgApplication.cs`), pull the `GizmoTranslatorPack` out of the global network module registration. Pass the resulting translators directly into `GizmoInteractionModule`.

We can now define the perfect pipeline execution order inside `GizmoInteractionModule.Tick`:

```csharp
public sealed class GizmoInteractionModule : IEcsModule
{
    private readonly FdpEventBus _interactionBus;
    private readonly IEcsModuleSystem[] _systems;
    
    // We run dedicated ingress/egress systems strictly for the UI traffic
    private readonly CycloneNetworkIngressSystem _gizmoIngress;
    private readonly CycloneEgressSystem _gizmoEgress;

    public GizmoInteractionModule(
        FdpEventBus interactionBus,
        IEnumerable<INetworkTranslator> gizmoTranslators,
        // ... other systems)
    {
        _interactionBus = interactionBus;
        _gizmoIngress = new CycloneNetworkIngressSystem(gizmoTranslators.ToArray());
        _gizmoEgress = new CycloneEgressSystem(gizmoTranslators.ToArray());
        // ...
    }

    public void Tick(ISimulationView view, float deltaTime)
    {
        // 1. Network Ingress: Reads from DDS, writes directly into _interactionBus.
        // Bypasses the global ECB entirely.
        _gizmoIngress.Execute(view, deltaTime);

        // 2. Advance the isolated bus state to make the new DDS events readable.
        _interactionBus.SwapBuffers();
        
        var scopedView = new InteractionScopedView(view, _interactionBus);

        // 3. Execution: Gizmo systems consume the UI events and commit ECS mutations.
        foreach (var sys in _systems)
        {
            sys.Execute(scopedView, deltaTime);
        }

        // 4. Network Egress: Reads remaining outbound events from _interactionBus and pushes to DDS.
        _gizmoEgress.Execute(scopedView, deltaTime);
    }
}
```

By aggressively injecting the private bus into the network adapters, the presentation tier DDS ingress natively drops its payloads into the quarantine layer. The main world bus remains 100% free of UI noise, and you no longer need the hacky event-bridge in the `Tick` method.


how we ensure the gizmo network ingress and egress is running in headless mode only while in non-headless mode all is connected to the local raylib/imguie rendering & interaction layer?


We handle this topological shift entirely at the composition root (e.g., inside `SimHostApp` or `IgApplication`) by evaluating the `Headless` flag provided by the `SubsystemConfig` during initialization. This enforces strict boundary control, ensuring the domain interaction systems remain completely ignorant of their I/O sources.

In **non-headless mode**, we instantiate the local `MapCanvas` and attach the `DebugGizmoLayer`. We inject our isolated `_interactionBus` and the `_gizmoBuffer` directly into the layer. Raw hardware input flows from Raylib into `MapCanvas.Update()`, routing down to `DebugGizmoLayer.HandleInput()`, which translates mouse and keyboard actions into typed interaction events on the local bus. Rendering is handled locally by reading the buffer via `DebugPrimitiveRenderer2D` during the canvas draw phase. In this branch, we intentionally omit the registration of the DDS gizmo translators.

In **headless mode**, we completely bypass the instantiation of `MapCanvas`, `DebugGizmoLayer`, and the local Raylib render loops. Instead, we instantiate the network adapters—specifically the `GizmoTranslatorPack` (which yields `GizmoInteractionIngressTranslator` and `GizmoInteractionEgressTranslator`) and the `DebugPrimitivesBatchPublisherSystem`. We inject the identical isolated `_interactionBus` and `_gizmoBuffer` into these translators, then wrap them in `CycloneNetworkIngressSystem` and `CycloneEgressSystem` inside our encapsulated `GizmoInteractionModule`. 

Because the core execution pipeline (`DataDrivenGizmoSystem`, `GlobalActionDispatchSystem`, and `GlobalGizmoManager`) reads from the scoped interaction view and writes to the injected primitive buffer, it remains 100% oblivious to whether the events originated from a local hardware click or a remote DDS packet. The data-oriented state machines execute identically across both topologies, perfectly satisfying the Open/Closed Principle.



we need to wire the new dedicated ui event bus to the event browser UI panel for different subsystems


To expose the isolated UI event bus to the `EventBrowserPanel` across all perspectives, we must leverage the existing `EventHistoryCaptureSystem`. 

The `EventBrowserPanel` does not read buses directly; it reads from an injected `IDiagnosticEventHistoryService`. The data is fed into this service by `EventHistoryCaptureSystem` instances, which are registered in the ECS kernel to run in the `PostSimulation` phase. 

Currently, the composition roots inject two capture systems: one for `"World"` and one for `"Orchestration"`. To integrate the new interaction bus without breaking isolation, we simply register a third capture system dedicated to it. This naturally tags the UI events with a new provider name, allowing operators to filter them in the browser GUI.

Here are the required updates for each subsystem's composition root:

**1. EditorSubsystem (`Hrot.Editor/EditorSubsystem.cs`)**
Locate the `_fdpEventBrowser` initialization and append the new capture system using the local `interactionBus` you created previously:
```csharp
_fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
_kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _fdpEventHistory, _world.Bus));
if (_orchestrationBus != null)
    _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _orchestrationBus));

// INJECT:
_kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, interactionBus));
```

**2. SimHostApp (`Hrot.SimHost/SimHostApp.cs`)**
Locate the initialization block where `_fdpEventBrowser` is constructed and append the interaction bus:
```csharp
_fdpEventBrowser = new FdpEventBrowserPanel(_eventHistoryService);
_kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _eventHistoryService, _world.Bus));
_kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _eventHistoryService, _eventBus));

// INJECT:
_kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _eventHistoryService, interactionBus));
```

**3. CgfSubsystem (`Hrot.CGF/CgfSubsystem.cs`)**
Apply the identical registration to the CGF composition root:
```csharp
_fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
_context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _fdpEventHistory, _context.World.Bus));
_context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _context.EventBus));

// INJECT:
_context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, interactionBus));
```

**4. IgApplication (`Hrot.IG/IgApplication.cs`)**
Apply the registration to the IG standalone shell:
```csharp
_fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
_context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _fdpEventHistory, _context.World.Bus));
_context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _context.EventBus));

// INJECT:
_context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, interactionBus));
```

By keeping this registration within the global ECS kernel execution, you guarantee that `EventHistoryCaptureSystem` strictly reads the events post-simulation (when the `InteractionScopedView` mutations have settled) without requiring any modifications to the `EventBrowserPanel` or `IDiagnosticEventHistoryService` implementations. The UI dropdown for providers will automatically discover `"Interaction"` because the panel dynamically populates its filter list from the incoming snapshot data.



are you sure the ordering of the Ecs module and different systems allws for processing the gizmos in the right way? i.e. gizmo projectors run after the main simulation tick, incoming ui interaction put to event bus before the gizmos are run, the entity repository modification fired via command buffers at proper time, and all that working well in both headless and non-headless mode?

Yes, the execution order is mathematically sound and adheres perfectly to strict ECS unidirectional data flow. By encapsulating the pipeline inside `GizmoInteractionModule` and relying on the `Tick` method, we leverage the kernel's native lifecycle to guarantee correct processing in both topologies.

Here is exactly how the pipeline guarantees safe execution, step-by-step:

**1. Projection After Geometry Finalization**
In standard system-based modules, systems annotated with `[UpdateInPhase(SystemPhase.PostSimulation)]` run during the post-simulation phase. However, by intentionally omitting `RegisterSystems` and manually executing `StatelessGizmoSystem`, `GlobalGizmoManager`, and `DataDrivenGizmoSystem` inside `GizmoInteractionModule.Tick(view, dt)`, we shift their execution to the very end of the kernel frame. 
The `ModuleHostKernel` explicitly calls `module.Tick()` *after* all system phases (`Input`, `Simulation`, `PostSimulation`, `Export`) have completed. This guarantees that physics (`CarKinematicsSystem`) and kinematics (`LinearKinematicsSystem`) have fully finalized the `SimTransform` positions for the frame. The gizmos project onto the absolute final geometry.

**2. Unified UI Interaction Ingress**
The event bus flow perfectly synchronizes local and remote inputs before the gizmos evaluate them:
*   **Non-Headless (Local):** `_canvas?.Update(deltaTime)` is called *before* `_kernel.Update()`. The local `DebugGizmoLayer` parses Raylib hardware inputs and writes `GizmoMouseEvent` and `GizmoDragUpdateEvent` directly into the write buffer of the isolated `_interactionBus`.
*   **Headless (Remote):** Once `GizmoInteractionModule.Tick` begins, the very first operation is `_gizmoIngress.Execute(view, dt)`. This translator pulls incoming `GizmoInteractionBatch` payloads from DDS and writes the exact same typed events into the `_interactionBus` write buffer.
*   **The Swap:** Immediately after ingress, `_interactionBus.SwapBuffers()` is invoked. This cleanly advances the state, merging both local and remote interactions into the read buffer simultaneously for the upcoming system evaluations.

**3. Deterministic Command Buffer Playback**
During execution, systems like `DataDrivenGizmoSystem` and `GlobalActionDispatchSystem` evaluate the interaction events and queue domain mutations (e.g., adding components, spawning entities) into the `IEntityCommandBuffer` provided by the `InteractionScopedView`. 
Because the `GizmoInteractionModule` is configured with `ExecutionPolicy.Synchronous()`, the `ModuleHostKernel` handles the mutation lifecycle natively. Immediately after `entry.Module.Tick(view, dt)` completes, the kernel synchronously invokes `PlaybackCommands(entry)`. This automatically flushes the command buffer, safely mutating the `EntityRepository` and stamping the structural changes before the next frame begins.

**4. Topological Parity (Headless vs. Non-Headless)**
This design seamlessly handles both environments without branching the domain logic:
*   In **non-headless mode**, the composition root wires the `_interactionBus` directly into `MapCanvas` and omits the DDS translators. The canvas feeds the bus, the gizmo systems consume it, and the `_gizmoBuffer` is rendered directly to the screen via `Raylib`.
*   In **headless mode**, the `MapCanvas` is bypassed. The composition root injects the DDS translators into `GizmoInteractionModule`. Remote interactions feed the bus, the gizmo systems consume it (ignorant of the source), and the resulting `_gizmoBuffer` is blindly serialized out to the network by `DebugPrimitivesBatchPublisherSystem` running in the `Export` phase.

By leveraging the post-phase `Tick` execution slot and an isolated interaction bus, we decouple presentation from simulation while preserving deterministic, frame-perfect structural mutation.
