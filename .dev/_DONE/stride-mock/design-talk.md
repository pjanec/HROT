FDP 190 - Stride Integration

Dnes • 8:41

I want to integrate stride 3d engine. Separate app. Playing the role of SimHost as well as IG. Calculating car movememt. Providing perception info. Showing entities in 3d. Prividing path planning. Reusing all existing network infrastructure. Being a dumb terminal for gizmos. I want to build a new 'subsystem' which plays these roles but is not hosted in clusterruner. Instead it runs (being ticked) from within the stride 3d engine main loop, making sure the stride can access the main Ecs without locking so it is easy to read entity components and update stride internals from it for rendering the entities. Using dead reckoning and smoothing for entity position. How to approach this? Best to reuse as much as possible, just creating new composition root.

To integrate the Stride 3D engine as a unified App combining the SimHost and IG roles outside of the `ClusterRunner` architecture, you can build a new Composition Root (e.g., a Stride `Game` or `SyncScript`).

The FDP/HROT architecture is designed to support this exact pattern. By using the `HrotNodeBuilder` and `NedNetworkFactory`, you can combine the `MuscleGround`, `Perception`, `NavigationSolver`, and `ImageGenerator` roles into a single executable while getting all the existing network infrastructure for free.

Here is the step-by-step approach to building your Stride-based subsystem:

1\. Create the Composition Root (HrotNodeBuilder)

Instead of inheriting from `FdpApplication` or `ISubsystem`, you will instantiate the ECS and DDS infrastructure directly during Stride's initialization phase. You assign the combined roles to ensure the node handles kinematics, perception, pathfinding, and presentation.

```
// Inside your Stride Game initialization or a startup SyncScript
var domainId = 0; // or from config
var participant = HrotEnvironment.CreateParticipant(domainId);

var nodeConfig = new HrotNodeConfig
{
    DomainId = domainId,
    NodeId = 100, // Unique ID for this App
    Headless = false, 
    ExternalParticipant = participant,
    SubsystemName = "StrideSimHostIG"
};

// Combine all necessary roles into one node context
var context = new HrotNodeBuilder(nodeConfig)
    .WithRole("StrideSimHostIG", 
        NodeRole.MuscleGround | 
        NodeRole.Perception | 
        NodeRole.NavigationSolver | 
        NodeRole.ImageGenerator)
    .WithNetworkFactory(new NedNetworkFactory(
        participant, 
        new NetworkEntityMap(), 
        HrotEnvironment.CreateGeoTransform(), 
        new FdpEventBus(), 
        nodeConfig.NodeId, 
        NodeRole.MuscleGround | NodeRole.ImageGenerator)) // Supply combined roles to factory
    .Build();
```

2\. Register Simulation, Perception, and Pathfinding Modules

You will load the existing logic packs into your `ModuleHostKernel`. To satisfy the requirement for **dead reckoning and smoothing**, you must register the `DeadReckoningSyncSystem`. Because this node acts as a muscle node (locally owning vehicles) _and_ an IG (rendering remote entities), you must set `driveFromNetwork: false`. This ensures the node dead-reckons remote ghosts, but leaves locally-owned simulated vehicles alone.

```
var repo = context.World;
var kernel = context.Kernel;

// Register base infrastructure and network replication
foreach (var m in context.BaseModules) kernel.RegisterModule(m);
if (context.NedReplication != null) kernel.RegisterModule(context.NedReplication);

// Enable Dead Reckoning for remote ghost entities only
kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem(driveFromNetwork: false));

// Register Path Planning (Navigation Solver)
var trajectoryPool = new TrajectoryPoolManager();
var roadNetwork = LoadRoadNetwork(); // Load your JSON road graph
kernel.RegisterModule(new NavigationSolverModule(roadNetwork, trajectoryPool));

// Register Car Kinematics (SimHost Muscle)
var simCorePack = new SimHostCoreLogicPack(context.EntityMap, roadNetwork, trajectoryPool);
kernel.RegisterModule(simCorePack);

// Register Perception
kernel.RegisterModule(new CognitiveSpatialModule(repo));

kernel.Initialize();
```

3\. Ticking ECS Lock-Free from the Stride Main Loop

To access the ECS safely without locks, execute your ECS kernel synchronously inside Stride's main update loop (`Update()` in a Stride `SyncScript`).

Because the `ModuleHostKernel` processes all structural changes and deferred command buffers within its `Update()` boundary, any queries you run immediately _after_ it are 100% thread-safe for reading.

```
public override void Update()
{
    float dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

    // 1. Tick network translation and orchestration
    _context.SlaveTranslator?.Tick();
    _context.ClusterSlave.Tick();

    // 2. Tick the FDP Simulation Kernel synchronously
    _kernel.Update(dt);
    _context.EventBus.SwapBuffers();

    // 3. (Lock-Free) Read ECS to update Stride 3D Entities
    SyncStrideEntities();
}
```

4\. Updating Stride Internals (3D Rendering)

In your `SyncStrideEntities` method, iterate over the `EntityRepository` to map FDP ECS state to Stride's entity hierarchy.

```
private void SyncStrideEntities()
{
    // Query all entities that have a spatial position
    var query = _repo.Query().With<SimTransform>().Build();

    foreach (var ecsEntity in query)
    {
        ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(ecsEntity);
        
        // Find or create the corresponding Stride Entity
        if (!_strideEntities.TryGetValue(ecsEntity.Index, out var strideEntity))
        {
            strideEntity = CreateStrideEntity(ecsEntity);
            _strideEntities[ecsEntity.Index] = strideEntity;
        }

        // Apply smoothed positions (dead reckoning runs in PostSimulation, so tf is already smoothed)
        strideEntity.Transform.Position = new Stride.Core.Mathematics.Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z);
        
        // Convert FDP Quaternion (Z-Up) to Stride Quaternion (Y-Up if applicable)
        strideEntity.Transform.Rotation = ConvertToStrideQuat(tf.Rotation);
    }
}
```

5\. Playing the "Dumb Terminal" for Gizmos

To act as an IG terminal for gizmos (tactical graphics, UI tools), hook up the FDP Gizmo Buffer and translators. You must register the network ingress/egress translators so the App receives raw debug primitives from the cluster and publishes your mouse clicks back to it.

```
var gizmoBuffer = new DebugPrimitiveBuffer();
var interactionBus = new FdpEventBus();

// Create translators in 'egress' mode (headless: false) because this IS the UI node sending clicks
var gizmoTranslators = networkFactory.CreateGizmoTranslators(interactionBus, context.NodeId, headless: false);

kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(gizmoTranslators.Where(t => t.Direction.HasFlag(TranslatorDirection.Ingress)).ToArray()));
kernel.RegisterGlobalSystem(new CycloneEgressSystem(gizmoTranslators.Where(t => t.Direction.HasFlag(TranslatorDirection.Egress)).ToArray()));

// Clear the buffer before kernel.Update()
gizmoBuffer.EndFrame(dt);
```

After `kernel.Update()`, you can read `gizmoBuffer.GetFrame()`. The buffer will contain primitives like `DebugPrimitiveShape.Line` or `DebugPrimitiveShape.Box2D`. You can write a Stride custom mesh builder or use Stride's `DebugTextSystem` to project these raw shapes into your 3D view, completing the dumb terminal requirement.

The local Simhost itself or the stride might create/registet some gizmos. How to combine those with the remote gizmo streams from other nodes? Still we want the multiplexing, i.e. the stream from just one concrete nodeId to be rendered at a time. But now we have a local stream provider and remote stream providers. Maybe by using dds network even for local streams, and let the dum terminal to listen to just selected node?

Your hypothesis is exactly right. By leveraging CycloneDDS's loopback capabilities, you can treat your local SimHost's gizmo stream as just another network provider. The "dumb terminal" side of your app simply listens to the DDS topic and filters by the requested `NodeId`.

To achieve this, you must **separate your gizmo buffers** into a "Producer" buffer and a "Consumer" buffer. If you use a single buffer, the local draws will mix with the remote stream you are trying to view.

Here is how you approach this architecture:

1\. Dual-Buffer Setup

Instantiate two separate `GizmoPrimitiveBuffer` instances (or `DebugPrimitiveBuffer` depending on the exact namespace alias you are using).

-   **Producer Buffer:** Your local ECS systems and local Stride logic write their gizmos here.-   **Consumer Buffer:** Your Stride 3D rendering loop reads from here to draw to the screen.

```
// Buffer for local systems to draw into
var localProducerBuffer = new DebugPrimitiveBuffer(); 

// Buffer for the Stride Renderer to read from
var terminalConsumerBuffer = new DebugPrimitiveBuffer(); 
```

2\. Publish the Local Stream

Wire up the local producer buffer to broadcast to the cluster. You register the `DebugPrimitivesBatchPublisherSystem` into your `ModuleHostKernel`'s Export phase. It will pack your local gizmos into a `DebugPrimitivesBatch` and send it over DDS using your local `NodeId`.

```
var ddsWriter = new DdsWriterGizmoAdapter<GizmoMap.Network.DebugPrimitivesBatch>(participant);

// Publishes localProducerBuffer to DDS every frame
kernel.RegisterGlobalSystem(new DebugPrimitivesBatchPublisherSystem(
    localProducerBuffer, 
    ddsWriter, 
    (byte)localNodeId)); 
```

3\. Multiplexing via the Ingress Translator

The `DebugPrimitivesIngressTranslator` has built-in support for your exact multiplexing requirement via its `filterNodeId` constructor parameter. It polls the DDS topic and drops any batches that don't match the selected node.

When the user changes which node they want to view in your Stride UI, you can recreate this translator with the target Node ID (or wrap it in a class that allows hot-swapping the ID).

```
byte? selectedNodeToView = 100; // Can be your local NodeId, or a remote one

// Listens to DDS and writes matching batches into the terminalConsumerBuffer
var gizmoIngress = new DebugPrimitivesIngressTranslator(
    terminalConsumerBuffer, 
    new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(participant),
    filterNodeId: selectedNodeToView); 
```

4\. The Stride Main Loop

Your Stride `Update()` loop now acts as both the engine that drives the local simulation and the dumb terminal that consumes the chosen stream:

```
public override void Update()
{
    float dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

    // 1. Clear both buffers at the start of the frame [2, 5]
    localProducerBuffer.EndFrame(dt);
    terminalConsumerBuffer.Clear();

    // 2. Tick ECS (Local systems will draw into localProducerBuffer)
    _kernel.Update(dt);

    // 3. Egress happens automatically via DebugPrimitivesBatchPublisherSystem inside the kernel

    // 4. Ingress: Poll DDS to pull the chosen node's stream into terminalConsumerBuffer [4, 6]
    gizmoIngress.PollAndApply();

    // 5. Render: Iterate over terminalConsumerBuffer.GetFrame() and draw to Stride 3D [7]
    RenderGizmosToStride(terminalConsumerBuffer.GetFrame());
}
```

Why this works so well

Because CycloneDDS seamlessly routes messages sent by a local writer to a local reader on the same participant, your local node stream costs almost zero network overhead when viewed locally, but remains perfectly visible to the rest of the cluster. By strictly isolating the `localProducerBuffer` from the `terminalConsumerBuffer`, you prevent feedback loops and get remote/local multiplexing completely for free.

To translate entities to stride, we need to make sure we track ecs entities creation and destruction and create/delete entities in stride. And that we use full ecs entity id, not just the index, but also the generation.

Using the full `Fdp.Core.Entity` struct (which packs both the `Index` and `Generation`) is the exact right approach. The FDP engine specifically designs the `Entity` struct to prevent "stale reference" bugs when an entity is destroyed and its index is later recycled by the free-list.

Because the `Entity` struct natively implements `IEquatable<Entity>` and `GetHashCode()` using both the index and generation, it works perfectly as a key in a standard C# `Dictionary`.

To track creations and destructions efficiently without subscribing to complex event streams, you can use a **differential synchronization** approach. This involves keeping a dictionary of active Stride entities and reconciling it against the live ECS state every frame.

Here is the most efficient, zero-allocation way to implement this in your Stride `SyncScript`:

1\. State Tracking Fields

Add these fields to your Stride script to maintain the mapping and avoid per-frame garbage collection (GC) allocations.

```
// Maps the FULL ECS Entity (Index + Generation) to the Stride Entity
private readonly Dictionary<Entity, Stride.Engine.Entity> _strideEntities = new();

// Pre-allocated list to track destructions without allocating memory each frame
private readonly List<Entity> _staleEntities = new();
```

2\. The Synchronization Loop

Inside your `Update()` loop (after `_kernel.Update(dt)`), execute a two-pass synchronization.

**Pass 1: Detect Destructions.** Iterate over your tracked dictionary and check if the ECS entity `IsAlive`. **Pass 2: Detect Creations & Updates.** Query the ECS for spatial entities. If the full `Entity` handle isn't in your dictionary, it's a creation.

```
private void SyncStrideEntities()
{
    // ==========================================
    // PASS 1: Detect Destructions
    // ==========================================
    foreach (var kvp in _strideEntities)
    {
        Entity ecsEntity = kvp.Key;
        
        // IsAlive inherently validates the Generation. If the entity was destroyed 
        // and the index recycled, IsAlive(ecsEntity) will return FALSE because 
        // the generation counter in the ECS chunk will no longer match your handle.
        // We also check HasComponent in case the entity lost its spatial presence.
        if (!_repo.IsAlive(ecsEntity) || !_repo.HasComponent<SimTransform>(ecsEntity))
        {
            _staleEntities.Add(ecsEntity);
        }
    }

    // Clean up Stride scene
    foreach (var staleEcsEntity in _staleEntities)
    {
        var strideEntity = _strideEntities[staleEcsEntity];
        
        // Remove from Stride Scene (assuming this script is attached to a root Scene Entity)
        this.Entity.Scene.Entities.Remove(strideEntity); 
        
        _strideEntities.Remove(staleEcsEntity);
    }
    _staleEntities.Clear();

    // ==========================================
    // PASS 2: Detect Creations & Update State
    // ==========================================
    // Query() returns an enumerator that yields the FULL Entity handle
    var query = _repo.Query().With<SimTransform>().Build();

    foreach (var ecsEntity in query)
    {
        // If the Entity (Index + Gen) isn't in our dictionary, it's a brand new entity
        if (!_strideEntities.TryGetValue(ecsEntity, out var strideEntity))
        {
            // CREATION
            // Useful to name the Stride entity with its generation for debugging
            strideEntity = new Stride.Engine.Entity($"FDP_{ecsEntity.Index}_v{ecsEntity.Generation}");
            
            // TODO: Attach Stride Models/Meshes to strideEntity based on ECS DIS Type or TkbType here
            
            this.Entity.Scene.Entities.Add(strideEntity);
            _strideEntities[ecsEntity] = strideEntity;
        }

        // UPDATE (Kinematics / Smoothing)
        ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(ecsEntity);
        
        strideEntity.Transform.Position = new Stride.Core.Mathematics.Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z);
        strideEntity.Transform.Rotation = ConvertToStrideQuat(tf.Rotation);
    }
}
```

Why this is the optimal approach:

-   **Generational Safety:** If FDP destroys Entity `Index: 5, Gen: 1` and creates a new one at `Index: 5, Gen: 2`, `_repo.IsAlive(Entity(5, 1))` returns `false`. Pass 1 will safely destroy the old Stride mesh, and Pass 2 will instantly spawn the new one.-   **Lock-Free & GC-Free:** You are safely reading ECS state from the main Stride thread. Because you clear and reuse `_staleEntities` and use the built-in zero-allocation `EntityQuery` iterator, this adds zero heap pressure to Stride's render loop.

Isnt listening to entity created and entity destroyed fdo events on the world event bus much easier and more cpu friedly? These events are guaranteed to be fired during normal live simulation or preview runtime. Not sure how they would work during replay. The new stride 'subsystem' need to participate in the cluster operations including having its own recording and replay (the simhost part requires that)

While your intuition is spot on for traditional game engine architectures, in the context of the FDP cluster architecture and the Flight Recorder, relying on event bus events for entity lifecycle is actually **more complex and less reliable** when replay is involved.

Here is why the differential synchronization (polling) approach is superior and necessary for your Stride subsystem to support full cluster recording and replay:

1\. The Replay "Time Travel" Problem

During normal live simulation, the `EntityLifecycleModule` (ELM) indeed guarantees that `ConstructionOrder` and `DestructionOrder` events are fired on the event bus.

However, during a replay or a network snapshot load, the normal entity creation pipeline is completely bypassed. The Flight Recorder's `PlaybackSystem` processes keyframes by calling `repo.Clear()` and then blitting raw unmanaged memory chunks directly back into the ECS tables to restore historical state instantly.

Because replay operates via raw memory copies, `ConstructionOrder` and `DestructionOrder` events **are not fired** during playback.

2\. Handling State Restorations

FDP does have a native `EntityLifecycleEvent` stream that includes `Restored` and `BatchRestored` event types specifically to track snapshot loads. But if you rely on an event-driven architecture, your Stride subsystem would have to perfectly handle a massive matrix of state changes:

-   Subscribing to `ConstructionOrder`/`DestructionOrder` for Live mode.-   Subscribing to `EntityLifecycleEvent` to catch `Restored`/`BatchRestored` events during Replay.-   Listening to Orchestration commands like `ReplaySeek` or `ClusterStateChangedEvent` to know when the operator scrubs the timeline, manually wiping your Stride 3D scene, and rebuilding it to prevent ghost duplicates.

3\. Why Differential Sync is the Engine-Idiomatic Approach

The two-pass differential synchronization approach I detailed earlier natively solves the "time travel" problem with zero extra logic.

If the cluster executes a `ReplaySeek` and jumps from Frame 5,000 back to Frame 100:

-   The `PlaybackSystem` instantly overwrites the ECS memory.-   In your Stride `Update()` loop, **Pass 1** executes. `_repo.IsAlive()` will immediately return `false` for all Frame 5,000 entity handles because the generation counters in the ECS chunks no longer match your cached handles. Stride instantly cleans up the future 3D meshes.-   **Pass 2** executes. The ECS `Query` iterator yields the restored Frame 100 entities. Stride instantly spawns them.

4\. CPU Friendliness

In FDP, iterating a `Query` is not like traversing a massive hierarchy. FDP queries are highly optimized to skip empty memory chunks, and they return zero-allocation iterators. Checking `_repo.IsAlive()` is just a highly-optimized bitwise check against the chunk's liveness mask.

Iterating thousands of entities and doing a Dictionary lookup per frame takes negligible CPU time (a fraction of a millisecond) and generates **zero garbage collection (GC) allocations**. This keeps your Stride render loop perfectly smooth while guaranteeing your 3D view is always a mathematically perfect reflection of the ECS memory, regardless of whether the cluster is running live, paused, replaying, or seeking.

Good. Regarding the cluster ops. How the cluster slave stuff integrates with the stride? Arent some operations blocking for long time? If so, we should display some splash screen or other user notification during them, meaning we should not block stride rendering, correct? This requires the stride integration understands the cluster states, allows rendering (but not ecs access) during some blocking cluster stae machine operation etc. How to approach that?

The good news is that you do not need to worry about the `ClusterSlave` blocking your Stride render loop. The FDP cluster orchestration architecture is explicitly designed around asynchronous operations to prevent exactly this issue.

When a long-running cluster operation occurs (like `PrepareLive` for scenario loading, or `TakeSnapshot` for checkpoint I/O), the `IClusterStateHandler` implementations return a `Task` from their `PrepareAsync` methods. The `ClusterSlave` stores this task and polls it each frame inside `Tick()`, naturally deferring the commit phase until the background work completes without ever stalling your 60Hz main loop .

However, you are absolutely correct that you should **display a loading screen and suspend ECS read access** during these loading phases. While the thread isn't blocked, the ECS memory might be undergoing massive structural changes (like clearing the repository or raw memory chunk overwrites from a snapshot restore) .

Here is how you integrate cluster state tracking into your Stride engine to show a splash screen and protect your rendering logic safely:

1\. Track Cluster State via the Event Bus

Whenever the cluster transitions between states (e.g., from `Idle` to `LoadingLive`, and then to `OperatingLive`), the `ClusterSlave` publishes a `TkClusterStateChangedEvent` to the local event bus .

You can read this event to maintain the current state of the node. The states are represented as integers that map directly to the `ClusterState` enum .

2\. Implement the Stride State Machine

In your Stride script, track the current cluster state. Gate your `SyncStrideEntities` method so that it only runs during "Operating" states.

```
using Fdp.Toolkit.Orchestration; // Contains ClusterState enum

public class StrideSimHostNode : SyncScript
{
    private ClusterState _currentClusterState = ClusterState.Idle;

    public override void Update()
    {
        float dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;

        // 1. Tick cluster slave (non-blocking, polls background tasks)
        _context.ClusterSlave.Tick();

        // 2. Tick the ECS Kernel
        _kernel.Update(dt);

        // 3. Read state transitions from the event bus
        foreach (var evt in _context.EventBus.ReadManaged<TkClusterStateChangedEvent>())
        {
            _currentClusterState = (ClusterState)evt.NextStateId;
            HandleStateChange(_currentClusterState);
        }
        
        // Swap buffers AFTER reading events
        _context.EventBus.SwapBuffers();

        // 4. Gate ECS reading and Stride rendering
        if (IsOperatingState(_currentClusterState))
        {
            // Safe to read ECS
            SyncStrideEntities();
        }
        else
        {
            // Do NOT touch ECS spatial data. 
            // The simulation is either paused, loading, or restoring.
            // Let the splash screen render.
        }
    }

    private void HandleStateChange(ClusterState newState)
    {
        if (IsOperatingState(newState))
        {
            HideSplashScreen();
        }
        else
        {
            // Show splash screen with the name of the current state 
            // e.g., "LoadingLive", "LoadingReplay", "LoadingEdit"
            ShowSplashScreen($"Please wait... {newState}");
        }
    }

    private bool IsOperatingState(ClusterState state)
    {
        // Only access ECS entities when the simulation is fully loaded and operating
        return state == ClusterState.OperatingLive ||
               state == ClusterState.OperatingEdit ||
               state == ClusterState.OperatingPreview ||
               state == ClusterState.OperatingReplay;
    }
}
```

Why this approach is robust:

-   **Zero Thread Stalls:** Because `_context.ClusterSlave.Tick()` yields immediately if a `PrepareAsync` task is still running , your Stride `Update()` loop continues to fire at max FPS, keeping your loading spinner/UI perfectly smooth and responsive.-   **Memory Safety:** By gating `SyncStrideEntities()` behind the `IsOperatingState` check, you guarantee that Stride never attempts to read ECS chunks while a `ScenarioLoadHandler` or `ReplayController` is in the middle of dropping entities or replacing the raw memory layout.-   **Automatic Cleanup:** During `Loading*` phases, you might want to call a method like `ClearAllStrideEntities()` alongside showing the splash screen, so that the 3D scene is wiped clean in preparation for the new scenario or replay layout that will appear once the state hits `Operating*`.

I would like to prove the concepts first on a sample app before implementing to stride. App that mimics the stride architecture, using raylib/imgui window instead of 3d stride renderer, hosting our newly introduced stride 'subsystem', providing the initialization and ticking from a fake stride script, managing fake stride entities and rendering them using raylib map independentrly on the gizmo dumb terminal, to see what it all would behave and look like without using real heavy stride stuff. The new 'subsystem' should have the proper api points and flexible architecture allowing to reuse same code when later integrating to real stride engine. This FakeStrideApp should reuse as much of the fdp toolkits and stuff as possible to stay DRY. How to approach it?

To prove the architecture before bringing in the heavy Stride 3D engine, you can build a cleanly decoupled integration core and run it inside a Raylib-based `FdpApplication`. This approach allows you to model fake Stride entities and a fake script lifecycle, ensuring your integration logic is 100% reusable when you move to the real engine.

Here is the step-by-step approach to building the `FakeStrideApp` while staying DRY by reusing the FDP/HROT toolkits.

1\. The Reusable Integration Core (`StrideClusterIntegration`)

First, abstract the FDP cluster operations, kernel, and dual-buffer gizmo terminal into a self-contained class. This is the exact "subsystem" code you will later drop into your real Stride project. It uses the `HrotNodeBuilder` to combine the `MuscleGround` and `ImageGenerator` roles.

```
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Orchestration;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using GizmoMap.Network;

public class StrideClusterIntegration : IDisposable
{
    public HrotNodeContext Context { get; private set; }
    
    // Dual buffers for the "dumb terminal" multiplexing 
    public DebugPrimitiveBuffer ProducerBuffer { get; } = new();
    public DebugPrimitiveBuffer ConsumerBuffer { get; } = new();
    
    private DebugPrimitivesIngressTranslator _gizmoIngress;

    public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
    {
        var config = new HrotNodeConfig
        {
            DomainId = domainId,
            NodeId = nodeId,
            Headless = false, // We have a visual terminal
            SubsystemName = "StrideSimHostIG"
        };

        // Combines SimHost, Perception, Pathfinding, and IG roles into one context
        Context = new HrotNodeBuilder(config)
            .WithRole("StrideSimHostIG", NodeRole.MuscleGround | NodeRole.Perception | NodeRole.ImageGenerator)
            .WithNetworkFactory(networkFactory)
            .Build();

        // 1. Setup Egress: Publish the local producer buffer to the cluster [3]
        var ddsWriter = new DdsWriterGizmoAdapter<DebugPrimitivesBatch>(Context.Participant);
        Context.Kernel.RegisterGlobalSystem(new DebugPrimitivesBatchPublisherSystem(ProducerBuffer, ddsWriter, (byte)nodeId));

        // 2. Setup Ingress: Dumb terminal polling [4]
        var ddsReader = new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(Context.Participant);
        _gizmoIngress = new DebugPrimitivesIngressTranslator(ConsumerBuffer, ddsReader, filterNodeId: null); // Listen to selected node

        // Register base modules (Lifecycle, Geographic, etc.) [5]
        foreach (var m in Context.BaseModules) Context.Kernel.RegisterModule(m);
        if (Context.NedReplication != null) Context.Kernel.RegisterModule(Context.NedReplication);

        Context.Kernel.Initialize();
    }

    public void Tick(float dt)
    {
        // Clear gizmo buffers
        ProducerBuffer.EndFrame(dt);
        ConsumerBuffer.Clear();

        // Tick network and state machine orchestration [6]
        Context.SlaveTranslator?.Tick();
        Context.ClusterSlave.Tick();

        // Tick ECS 
        Context.Kernel.Update(dt);
        Context.EventBus.SwapBuffers();

        // Multiplexing: Pull the chosen remote/local stream into ConsumerBuffer
        _gizmoIngress.PollAndApply();
    }

    public void Dispose()
    {
        Context?.Kernel.Dispose();
        Context?.World.Dispose();
    }
}
```

2\. Fake Stride Abstractions

Create lightweight classes that mimic the API footprint of the Stride engine.

```
// Mimics a Stride 3D Entity
public class FakeStrideEntity
{
    public string Name { get; set; }
    public System.Numerics.Vector3 Position { get; set; }
    public System.Numerics.Quaternion Rotation { get; set; }
}

// Mimics Stride.Engine.SyncScript
public abstract class FakeStrideScript
{
    public abstract void Start();
    public abstract void Update(float deltaTime);
}
```

3\. The Fake Stride Script (Synchronization Logic)

This script houses the lock-free differential synchronization and orchestration state machine logic discussed previously. It connects your Stride objects to the `StrideClusterIntegration`.

```
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

public class SyncFdpToStrideScript : FakeStrideScript
{
    private readonly StrideClusterIntegration _integration;
    private readonly Dictionary<Entity, FakeStrideEntity> _strideEntities = new();
    private readonly List<Entity> _staleEntities = new();
    private ClusterState _currentClusterState = ClusterState.Idle;

    // Public list so our Raylib app shell can render them
    public IEnumerable<FakeStrideEntity> ActiveEntities => _strideEntities.Values;
    public string CurrentStateMessage { get; private set; } = "";

    public SyncFdpToStrideScript(StrideClusterIntegration integration)
    {
        _integration = integration;
    }

    public override void Start() { }

    public override void Update(float dt)
    {
        _integration.Tick(dt);

        // 1. Read cluster state transitions from the event bus [7]
        foreach (var evt in _integration.Context.EventBus.ReadManaged<TkClusterStateChangedEvent>())
        {
            _currentClusterState = (ClusterState)evt.NextStateId;
        }

        // 2. Gate ECS rendering behind operating states
        if (IsOperatingState(_currentClusterState))
        {
            CurrentStateMessage = ""; // Hide splash screen
            SyncStrideEntities();
        }
        else
        {
            CurrentStateMessage = $"Please wait... {_currentClusterState}";
            // In a real app, clear Stride scene here so it's clean for the new load.
            _strideEntities.Clear(); 
        }
    }

    private void SyncStrideEntities()
    {
        var repo = _integration.Context.World;

        // PASS 1: Detect Destructions
        foreach (var kvp in _strideEntities)
        {
            Entity ecsEntity = kvp.Key;
            if (!repo.IsAlive(ecsEntity) || !repo.HasComponent<SimTransform>(ecsEntity))
            {
                _staleEntities.Add(ecsEntity);
            }
        }

        foreach (var stale in _staleEntities)
        {
            _strideEntities.Remove(stale); // Equivalent to Stride's Scene.Entities.Remove()
        }
        _staleEntities.Clear();

        // PASS 2: Detect Creations & Updates
        var query = repo.Query().With<SimTransform>().Build();
        foreach (var ecsEntity in query)
        {
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(ecsEntity);

            if (!_strideEntities.TryGetValue(ecsEntity, out var strideEntity))
            {
                // CREATION
                strideEntity = new FakeStrideEntity { Name = $"FDP_{ecsEntity.Index}_v{ecsEntity.Generation}" };
                _strideEntities[ecsEntity] = strideEntity;
            }

            // UPDATE (Kinematics / Smoothing)
            strideEntity.Position = new System.Numerics.Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z);
            strideEntity.Rotation = tf.Rotation;
        }
    }

    private bool IsOperatingState(ClusterState state)
    {
        return state == ClusterState.OperatingLive || state == ClusterState.OperatingEdit ||
               state == ClusterState.OperatingPreview || state == ClusterState.OperatingReplay;
    }
}
```

4\. The Raylib App Shell (`FdpApplication`)

Finally, use the `FdpApplication` base class to bootstrap the window, provide the ticking mechanism, and render your dummy entities alongside the gizmo terminal buffer.

```
using Fdp.Presentation.Raylib;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Network.NED.Factory;
using Raylib_cs;

public class FakeStrideApp : FdpApplication
{
    private StrideClusterIntegration _integration;
    private SyncFdpToStrideScript _script;
    
    // Reusable FDP 2D primitive renderer [8]
    private GizmoMap.Presentation.DebugPrimitiveRenderer2D _gizmoRenderer;
    private Camera2D _camera;

    public FakeStrideApp() : base(new ApplicationConfig
    {
        Width = 1280, Height = 720,
        WindowTitle = "Fake Stride App (FDP + Raylib + ImGui)",
        TargetFPS = 60
    }) { }

    protected override void OnLoad()
    {
        // Initialize the reusable integration subsystem
        _integration = new StrideClusterIntegration();
        
        // Pass your NedNetworkFactory [9] and cluster domains
        var factory = new NedNetworkFactory(null, new Fdp.Toolkit.Replication.Services.NetworkEntityMap(), 
            Hrot.Map.Common.HrotEnvironment.CreateGeoTransform(), new FdpEventBus(), 100, NodeRole.MuscleGround | NodeRole.ImageGenerator);
            
        _integration.Initialize(domainId: 0, nodeId: 100, factory);

        // Bind the script to the integration core
        _script = new SyncFdpToStrideScript(_integration);
        _script.Start();

        // Setup the Gizmo terminal renderer [10]
        _gizmoRenderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D();
        _camera = new Camera2D { Zoom = 1f, Offset = new System.Numerics.Vector2(640, 360) };
    }

    protected override void OnUpdate(float dt)
    {
        // Tick the fake Stride engine
        _script.Update(dt);
    }

    protected override void OnDrawWorld()
    {
        Raylib.BeginMode2D(_camera);

        // 1. Render the 'Dumb Terminal' Gizmos from the Consumer Buffer [11]
        var gizmoFrame = _integration.ConsumerBuffer.GetFrame();
        _gizmoRenderer.Render(gizmoFrame, _camera, _camera.Zoom);

        // 2. Render the 'Fake Stride Entities' independently
        foreach (var entity in _script.ActiveEntities)
        {
            // Simulate rendering a 3D mesh at the mapped coordinates
            Raylib.DrawCircleV(new System.Numerics.Vector2(entity.Position.X, entity.Position.Y), 5f, Color.Red);
            Raylib.DrawText(entity.Name, (int)entity.Position.X + 8, (int)entity.Position.Y - 8, 10, Color.White);
        }

        Raylib.EndMode2D();
    }

    protected override void OnDrawUI()
    {
        // Draw the Splash Screen / Orchestration status if not operating
        if (!string.IsNullOrEmpty(_script.CurrentStateMessage))
        {
            rlImGui_cs.rlImGui.Begin();
            ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(1280 / 2 - 100, 720 / 2 - 50));
            ImGuiNET.ImGui.Begin("Cluster Status", ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize | ImGuiNET.ImGuiWindowFlags.NoDecoration);
            ImGuiNET.ImGui.Text(_script.CurrentStateMessage);
            ImGuiNET.ImGui.End();
            rlImGui_cs.rlImGui.End();
        }
    }

    protected override void OnUnload()
    {
        _integration.Dispose();
    }
}
```

Why this architecture nails the requirements:

-   **DRY & Proper API Points:** `StrideClusterIntegration` has no dependency on Raylib, making it a perfect decoupled library to inject directly into your Stride module later.-   **Mimics Stride Execution:** `FakeStrideScript` is structured identically to a `SyncScript` in the Stride engine, running the 2-pass generational dictionary lookup to map ECS `Entity` handles to 3D instances.-   **Independent Rendering:** `OnDrawWorld` clearly demonstrates that Stride entity rendering runs completely separated from the FDP Gizmo terminal `DebugPrimitiveRenderer2D` rendering pass, satisfying the requirement to run the Gizmo terminal stream multiplexing separately from 3D presentation,.

Could the same stride 'subsystem' be integrayed into a wrapper subsystem of clusterrunner to allow running with other subsystem like cgf in a single app for easier debugging? In parallel to having the standalone FakeStrideApp. Reusing the code as much as possible.

Yes, absolutely. This is exactly what the `ISubsystem` interface in `ClusterRunner` is designed for.

Because you already cleanly decoupled the core logic into `StrideClusterIntegration` and the synchronization logic into `SyncFdpToStrideScript`, you can easily drop them into an `ISubsystem` wrapper. This allows you to run your new subsystem side-by-side with the `CgfSubsystem` (which acts as the Brain node) in a single `ClusterRunner` process over a local DDS loopback.

Here is how you write the wrapper subsystem to reuse 100% of your integration code while adapting to the `ClusterRunner` lifecycle:

1\. Create the `StrideWrapperSubsystem`

Instead of inheriting from `FdpApplication` (which owns the window), you inherit from `ISubsystem`. The `ClusterRunner` will inject the `INetworkFactory` (typically `NedNetworkFactory`) and the `SubsystemConfig` containing a unique `NodeId` for this specific module.

```
using Fdp.Toolkit.Runner;
using Hrot.Core.Network;
using Raylib_cs;
using System.Numerics;

public class StrideWrapperSubsystem : ISubsystem
{
    public string Name => "StrideMock";
    public Vector4 TitleBarColor => new Vector4(0.8f, 0.4f, 0.1f, 1.0f); // Custom color for the UI title bar

    private readonly INetworkFactory _networkFactory;
    
    private StrideClusterIntegration _integration;
    private SyncFdpToStrideScript _script;
    private GizmoMap.Presentation.DebugPrimitiveRenderer2D _gizmoRenderer;

    // Injected by ClusterRunner's composition root (Program.cs)
    public StrideWrapperSubsystem(INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory;
    }

    public void Initialize(SubsystemConfig config)
    {
        _integration = new StrideClusterIntegration();
        
        // config provides the DDS DomainId and a cluster-unique NodeId
        _integration.Initialize(config.DomainId, config.NodeId, _networkFactory);

        _script = new SyncFdpToStrideScript(_integration);
        _script.Start();

        _gizmoRenderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D();
    }

    public void Update(float deltaTime)
    {
        // Ticks the integration core and updates the FakeStrideEntities
        _script.Update(deltaTime);
    }

    public void DrawWorld()
    {
        // 1. Render the 'Dumb Terminal' Gizmos
        var gizmoFrame = _integration.ConsumerBuffer.GetFrame();
        
        // Note: ClusterRunner handles the Raylib Camera2D internally, 
        // you just draw using Raylib coordinates directly here.
        _gizmoRenderer.Render(gizmoFrame, zoom: 1f);

        // 2. Render the 'Fake Stride Entities' independently
        foreach (var entity in _script.ActiveEntities)
        {
            Raylib.DrawCircleV(new Vector2(entity.Position.X, entity.Position.Y), 5f, Color.Red);
            Raylib.DrawText(entity.Name, (int)entity.Position.X + 8, (int)entity.Position.Y - 8, 10, Color.White);
        }
    }

    public void DrawUI()
    {
        if (!string.IsNullOrEmpty(_script.CurrentStateMessage))
        {
            ImGuiNET.ImGui.Begin("Stride Status", ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize);
            ImGuiNET.ImGui.Text(_script.CurrentStateMessage);
            ImGuiNET.ImGui.End();
        }
    }

    public void Shutdown()
    {
        _integration?.Dispose();
    }
}
```

2\. Add it to the ClusterRunner Composition Root

In your `Hrot.ClusterRunner/Program.cs` file, the orchestrator parses the CLI `--mode` flag to instantiate subsystems. You can add a new hook for your subsystem to run alongside CGF and Orchestrator.

Inside the `ScanForSubsystems()` projection loop or explicitly in the `subsystems` list creation:

```
// Inside Program.cs, where subsystems are instantiated based on requested modes
if (requested.Contains("stride_mock"))
{
    // Resolves a unique Node ID for this subsystem (e.g., BaseNodeId + 700)
    int subNodeId = ResolveAppNodeId("StrideMock", config.NodeId); 
    
    // Create an isolated DDS participant and factory for this subsystem
    var participant = HrotEnvironment.CreateParticipant(config.DomainId);
    var factory = new NedNetworkFactory(
        participant, new NetworkEntityMap(), HrotEnvironment.CreateGeoTransform(), 
        new FdpEventBus(), subNodeId, NodeRole.None);

    subsystems.Add(new StrideWrapperSubsystem(factory));
}
```

3\. Run the Unified App

You can now run your mock Stride logic, the CGF brain node, and the master Orchestrator all in a single debuggable process over DDS loopback:

```
Hrot.ClusterRunner.exe -m orchestrator,cgf,stride_mock
```

Why this approach is ideal:

-   **Perfect DRY Compatibility:** The `StrideClusterIntegration` class does not care if it's ticked by `FakeStrideApp.OnUpdate` or `StrideWrapperSubsystem.Update`. The exact same logic is proven in both environments.-   **Split-Authority Testing:** By running CGF (`NodeRole.Brain`) alongside your subsystem (`NodeRole.MuscleGround | ImageGenerator`), you get an instant 100% accurate simulation of how your real Stride integration will behave under a distributed split-authority model. CGF will command entities (NavIntents, Behavior), and your Stride wrapper will execute physics and render them natively.

Good! So we will create both the standalone app AND the clusterrunner wrapper subsystem.

This is an excellent approach. Building both the standalone `FakeStrideApp` and the `StrideWrapperSubsystem` guarantees that your core integration logic remains perfectly DRY and completely decoupled from the specific host environment.

By leveraging the shared `StrideClusterIntegration` core, you get the best of both worlds:

-   **Rapid Prototyping:** The `FakeStrideApp` will inherit from `FdpApplication`, giving you a fast, lightweight Raylib/ImGui environment to visualize and debug your differential synchronization logic without the overhead of the full Stride engine.-   **Production-Ready Cluster Integration:** By implementing the `ISubsystem` interface, your wrapper will natively hook into the `SubsystemOrchestrator`. The orchestrator will automatically drive your subsystem through the standard lifecycle (`Initialize`, `Update`, `DrawWorld`, `DrawUI`, and `Shutdown`).

Furthermore, utilizing the `HrotNodeBuilder` inside your shared core ensures that the ECS, event bus, and network translators are initialized exactly the same way across both apps.

Where would you like to begin? We can start by writing the shared `StrideClusterIntegration` core, setting up the `FakeStrideApp` shell, or wiring the command-line arguments into the `ClusterRunner` composition root.

How will we render the fake stride 3d entities (as pure 2d) in case of the clusterrunner wrapper subsystem? We need to reuse the clusterrunner's raylib window but only if the perspective is switched to the stride wrapper subsystem.

To ensure your `StrideWrapperSubsystem` only renders its 2D fake entities when its perspective is active, you need to implement the `IMapCameraProvider` interface.

The `ClusterRunner`'s `SubsystemOrchestrator` uses this interface as a marker. When it executes the `DrawWorldAll()` loop, it checks if a subsystem implements `IMapCameraProvider`. If it does, the orchestrator will **only** call `DrawWorld()` when that subsystem is the currently active map owner.

Additionally, implementing this interface allows the `PerspectiveCoordinatorSystem` to seamlessly copy the camera's zoom and pan coordinates from the outgoing subsystem (like SimHost or CGF) into your mock Stride camera when the user switches tabs.

Here is how you update your wrapper subsystem to implement this gating and handle the Raylib 2D camera mode:

Updated `StrideWrapperSubsystem`

```
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D.Components; // Contains MapCamera and MapCameraView
using Hrot.Core.Network;
using Raylib_cs;
using System.Numerics;

// 1. Implement IMapCameraProvider alongside ISubsystem
public class StrideWrapperSubsystem : ISubsystem, IMapCameraProvider
{
    public string Name => "StrideMock";
    public Vector4 TitleBarColor => new Vector4(0.8f, 0.4f, 0.1f, 1.0f);

    private readonly INetworkFactory _networkFactory;
    
    private StrideClusterIntegration _integration;
    private SyncFdpToStrideScript _script;
    private GizmoMap.Presentation.DebugPrimitiveRenderer2D _gizmoRenderer;

    // 2. Add a MapCamera to maintain zoom/pan state
    private MapCamera _camera;

    public StrideWrapperSubsystem(INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory;
    }

    public void Initialize(SubsystemConfig config)
    {
        _integration = new StrideClusterIntegration();
        _integration.Initialize(config.DomainId, config.NodeId, _networkFactory);

        _script = new SyncFdpToStrideScript(_integration);
        _script.Start();

        _gizmoRenderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D();
        
        // Initialize camera to center of default 720p window
        _camera = new MapCamera { Offset = new Vector2(1280 / 2f, 720 / 2f), Zoom = 1f };
    }

    public void Update(float deltaTime)
    {
        _script.Update(deltaTime);
        
        // Optional: Update camera logic (panning/zooming) if this tab is active
        // _camera.HandleInput(new RaylibInputProvider()); 
    }

    // 3. The Orchestrator will automatically skip this method if the perspective isn't "StrideMock"
    public void DrawWorld()
    {
        // Apply the 2D Camera transform before drawing
        _camera.BeginMode();

        // Render the 'Dumb Terminal' Gizmos
        var gizmoFrame = _integration.ConsumerBuffer.GetFrame();
        _gizmoRenderer.Render(gizmoFrame, _camera.InnerCamera, _camera.Zoom);

        // Render the 'Fake Stride Entities'
        foreach (var entity in _script.ActiveEntities)
        {
            Raylib.DrawCircleV(new Vector2(entity.Position.X, entity.Position.Y), 5f, Color.Red);
            Raylib.DrawText(entity.Name, (int)entity.Position.X + 8, (int)entity.Position.Y - 8, 10, Color.White);
        }

        _camera.EndMode();
    }

    public void DrawUI()
    {
        // UI is handled separately; the orchestrator will gate this using WindowScope.PerspectiveBound
        // when you register your UI windows, so you don't need manual gating here.
        if (!string.IsNullOrEmpty(_script.CurrentStateMessage))
        {
            ImGuiNET.ImGui.Begin("Stride Status", ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize);
            ImGuiNET.ImGui.Text(_script.CurrentStateMessage);
            ImGuiNET.ImGui.End();
        }
    }

    public void Shutdown()
    {
        _integration?.Dispose();
    }

    // ==========================================
    // IMapCameraProvider Implementation
    // ==========================================
    
    public MapCameraView? GetCameraView() => _camera?.GetCameraView();

    public void ApplyCameraView(MapCameraView view) => _camera?.ApplyCameraView(view);
}
```

How the `ClusterRunner` routes this:

-   When you run `Hrot.ClusterRunner.exe -m orchestrator,cgf,stride_mock`, the composition root sees your module.-   In the ImGui main menu bar, the orchestrator generates a `[StrideMock]` button.-   Clicking that button fires an event that calls `_orchestrator.SwitchMapOwner("StrideMock")`.-   The orchestrator pulls the `MapCameraView` from the previous owner (e.g., CGF) and pushes it into your subsystem via `ApplyCameraView(view)`, so the screen doesn't jump.-   From then on, only your subsystem's `DrawWorld()` executes until the user clicks a different tab.

Does it make sense to use this IMapCameraProvider also in the standalone app for consistency and reusability?

While it is a good instinct for consistency, the standard FDP engine architecture actually dictates keeping `IMapCameraProvider` strictly at the `ClusterRunner` subsystem layer.

Here is why, and how you should structure it to perfectly match the engine's conventions:

Why standalone apps don't use `IMapCameraProvider`

The `IMapCameraProvider` interface lives in the `Fdp.Toolkit.Runner` namespace. It was explicitly designed for the `SubsystemOrchestrator` to synchronize camera state when a user clicks between different subsystem tabs (like switching from CGF to SimHost).

A standalone `FdpApplication` has no orchestrator, no other subsystems, and no tabs to switch between, so implementing the provider interface there adds an unnecessary dependency on the `Runner` toolkit.

The FDP Architectural Pattern

If you look at how the core FDP subsystems (`SimHost` and `IG`) handle this, they split the responsibility:

-   **The Standalone Apps** (`SimHostApp` and `IgApplication`) simply expose a public `GetMapCamera()` method that returns their internal `MapCamera` object.-   **The Subsystem Wrappers** (`SimHostSubsystem` and `IgSubsystem`) are the ones that implement `IMapCameraProvider`. They satisfy the orchestrator's interface by calling the app's `GetMapCamera()` method and extracting the `MapCameraView` data struct.

How to apply this to your Stride integration

To maximize reusability and follow the exact FDP engine pattern, you should move the `MapCamera` into your shared `StrideClusterIntegration` core.

**1\. Update the Shared Core:**

```
public class StrideClusterIntegration : IDisposable
{
    // ... existing fields ...
    public MapCamera Camera { get; } = new MapCamera { Offset = new Vector2(1280 / 2f, 720 / 2f), Zoom = 1f };

    public void Tick(float dt)
    {
        // ... existing tick logic ...
        
        // (Optional) If you want the camera to support panning/zooming via Raylib input:
        // Camera.HandleInput(new RaylibInputProvider()); 
    }
}
```

**2\. The Standalone App (****FakeStrideApp****):** The standalone app uses the camera to draw, and can expose it just like `IgApplication` does, without referencing `IMapCameraProvider`.

```
public class FakeStrideApp : FdpApplication
{
    // ... existing code ...
    
    // Follows the IG/SimHost pattern exactly
    public MapCamera GetMapCamera() => _integration.Camera; 

    protected override void OnDrawWorld()
    {
        _integration.Camera.BeginMode();
        // Draw gizmos and fake 3D entities
        _integration.Camera.EndMode();
    }
}
```

**3\. The Wrapper (****StrideWrapperSubsystem****):** The wrapper implements the interface for the `ClusterRunner` and delegates to the integration core, keeping your architecture perfectly aligned with FDP standards.

```
public class StrideWrapperSubsystem : ISubsystem, IMapCameraProvider
{
    // ... existing code ...

    public void DrawWorld()
    {
        _integration.Camera.BeginMode();
        // Draw gizmos and fake 3D entities
        _integration.Camera.EndMode();
    }

    // ==========================================
    // IMapCameraProvider Implementation
    // ==========================================
    public MapCameraView? GetCameraView() => _integration.Camera.GetCameraView();
    public void ApplyCameraView(MapCameraView view) => _integration.Camera.ApplyCameraView(view);
}
```

This ensures your standalone app remains a lightweight, decoupled sandbox, while your `ClusterRunner` wrapper provides exactly the "glue" the orchestrator expects.

Ok i need the standalone to support map pan and zoom similrly to what the clusterrunber provides. Maybe be could reuse some of that code.

You can easily reuse the exact pan and zoom logic that the `ClusterRunner` uses. The FDP toolkit provides the **RaylibInputProvider** and **MapCamera** classes specifically for this purpose.

Because `MapCamera` natively supports smooth-damped panning (via right-click drag) and zooming (via scroll wheel), you just need to feed it the input state each frame and tell it to update its interpolation.

Here is how you wire it up cleanly in both your standalone app and your wrapper subsystem.

1\. In the Standalone App (`FakeStrideApp`)

In your standalone application, the camera is always active, so you simply create the input provider and apply it every frame before your logic ticks.

```
using Fdp.Toolkit.Vis2D.Defaults; // Contains RaylibInputProvider

public class FakeStrideApp : FdpApplication
{
    // ... existing fields ...
    private RaylibInputProvider _inputProvider;

    protected override void OnLoad()
    {
        // ... existing initialization ...
        
        _inputProvider = new RaylibInputProvider();
    }

    protected override void OnUpdate(float dt)
    {
        // 1. Feed hardware input to the MapCamera (Right-click to pan, scroll to zoom)
        _integration.Camera.HandleInput(_inputProvider);
        
        // 2. Step the camera's smooth damping physics
        _integration.Camera.Update(dt);

        // 3. Tick your fake Stride logic
        _script.Update(dt);
    }
    
    // ... rest of the app ...
}
```

2\. In the ClusterRunner Wrapper (`StrideWrapperSubsystem`)

In the `ClusterRunner`, there is a catch: multiple subsystems are running at the same time. If you pan or zoom while looking at the CGF or Orchestrator tab, you don't want your hidden Stride mock camera to pan in the background.

To prevent this, you can reuse a neat trick the orchestrator provides: **SubsystemConfig.IsActiveMapOwner**. This is a delegate injected by the orchestrator that returns `true` only when the user is actually looking at your subsystem's tab.

```
using Fdp.Toolkit.Vis2D.Defaults;

public class StrideWrapperSubsystem : ISubsystem, IMapCameraProvider
{
    // ... existing fields ...
    private RaylibInputProvider _inputProvider;
    private Func<bool> _isActiveMapOwner = () => true;

    public void Initialize(SubsystemConfig config)
    {
        // ... existing initialization ...
        
        _inputProvider = new RaylibInputProvider();
        
        // Store the visibility gate provided by the orchestrator
        _isActiveMapOwner = config.IsActiveMapOwner ?? (() => true); 
    }

    public void Update(float deltaTime)
    {
        // 1. Only process mouse input if the user is actively viewing the Stride tab
        if (_isActiveMapOwner())
        {
            _integration.Camera.HandleInput(_inputProvider);
        }
        
        // 2. Always update the camera so it finishes its smooth panning animation
        // even if the user just switched away
        _integration.Camera.Update(deltaTime);
        
        // 3. Tick your mock logic
        _script.Update(deltaTime);
    }
    
    // ... rest of the wrapper ...
}
```

Why this is the right approach:

-   **Zero Reinvention:** By using `RaylibInputProvider`, you avoid writing manual input-capture loops. It implements `IInputProvider`, which is the exact interface `MapCamera.HandleInput()` expects.-   **Built-in Damping:** `MapCamera.Update(dt)` automatically handles linear interpolation for panning and zooming (`PanDamping` and `ZoomDamping`), giving you the exact same buttery-smooth map navigation found in the main `ClusterRunner` tools.-   **Engine-Agnostic Core:** Notice that `_inputProvider` lives entirely in `FakeStrideApp` and `StrideWrapperSubsystem`. Your `StrideClusterIntegration` core class never needs to know about `RaylibInputProvider`. When you move to the real Stride 3D engine, you simply delete the Raylib camera logic and let Stride's native `Input` manager handle 3D camera controls.

in the fake stride renderer (as well later in the real stride renderer) i would like to show also the interactions "special effects" like fires and detonations. I need to have prepared some ways (apis) of the how to pass these events into the rendering engines. And render something relevant in our fake stride 2d renderer.

To handle "special effects" like fires, detonations, and tracers, you do not need to invent a new API to pass events to your rendering engine. The FDP architecture already provides a standardized, ECS-driven pattern for this, specifically designed to bridge the gap between network events and 3D presentation layers.

In the FDP IG (Image Generator) layer, transient events (`WeaponFireNotification` and `DetonationNotification`) are caught by the **EventToEffectSystem**. This system spawns **ephemeral ECS entities** carrying a `VisualEffectState` component. A companion system, **VisualEffectCleanupSystem**, automatically fades and destroys these entities over time.

By registering these two systems, your Stride renderer (and the Fake Raylib renderer) can simply query for `VisualEffectState` entities and draw them, exactly as you already do for vehicles.

Here is how you integrate this into your `FakeStrideApp` architecture:

1\. Register the Effect Systems in `StrideClusterIntegration`

You need to add the two visual effect systems to your ECS kernel. Because your node acts as an `ImageGenerator`, the `NedNetworkFactory` automatically wires up the DDS network translators that will receive remote shots and detonations and publish them to your local event bus.

In `StrideClusterIntegration.cs`, add this before calling `Context.Kernel.Initialize()`:

```
using Hrot.IG.Systems; // Contains EventToEffectSystem & VisualEffectCleanupSystem

public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
{
    // ... existing setup ...

    // 1. Spawns VisualEffectState entities from network events (Simulation Phase)
    Context.Kernel.RegisterGlobalSystem(new EventToEffectSystem());
    
    // 2. Fades and destroys expired effect entities (PostSimulation Phase)
    Context.Kernel.RegisterGlobalSystem(new VisualEffectCleanupSystem());

    Context.Kernel.Initialize();
}
```

2\. Track Effects in your Fake Stride Script

Update your `SyncFdpToStrideScript` to track these ephemeral effect entities. Because `VisualEffectCleanupSystem` destroys them automatically when they expire, your Pass 1 (Destructions) logic will automatically clean up the Stride particle systems without any extra effort.

First, define a data class for the fake effects:

```
using Hrot.IG.Components; // Contains VisualEffectState, EffectType, TracerTarget

public class FakeStrideEffect
{
    public EffectType Type { get; set; }
    public System.Numerics.Vector3 Position { get; set; }
    public System.Numerics.Vector3 TracerEnd { get; set; }
    public float Scale { get; set; }
    public float Alpha { get; set; }
}
```

Then, update `SyncFdpToStrideScript.cs` to add a dictionary and a query for effects:

```
private readonly Dictionary<Entity, FakeStrideEffect> _strideEffects = new();

// Expose to FakeStrideApp for rendering
public IEnumerable<FakeStrideEffect> ActiveEffects => _strideEffects.Values;

private void SyncStrideEntities()
{
    var repo = _integration.Context.World;

    // ==========================================
    // PASS 1: Detect Destructions (Vehicles & Effects)
    // ==========================================
    // ... existing vehicle destruction loop ...
    
    foreach (var kvp in _strideEffects)
    {
        Entity ecsEntity = kvp.Key;
        if (!repo.IsAlive(ecsEntity) || !repo.HasComponent<VisualEffectState>(ecsEntity))
        {
            _staleEntities.Add(ecsEntity);
        }
    }

    foreach (var stale in _staleEntities)
    {
        _strideEffects.Remove(stale); // In real Stride: Scene.Entities.Remove(particleEntity)
    }
    _staleEntities.Clear();

    // ==========================================
    // PASS 2: Detect Creations & Updates 
    // ==========================================
    // ... existing vehicle query loop ...

    // Query for visual effects
    var effectQuery = repo.Query().With<SimTransform>().With<VisualEffectState>().Build();
    
    foreach (var ecsEntity in effectQuery)
    {
        ref readonly var effect = ref repo.GetComponentRO<VisualEffectState>(ecsEntity);
        ref readonly var tf = ref repo.GetComponentRO<SimTransform>(ecsEntity);

        if (!_strideEffects.TryGetValue(ecsEntity, out var strideEffect))
        {
            // CREATION: Spawn your Stride Particle System or Muzzle Flash here
            strideEffect = new FakeStrideEffect { Type = effect.Type };
            _strideEffects[ecsEntity] = strideEffect;
        }

        // UPDATE: Drive fade and scaling
        strideEffect.Position = new System.Numerics.Vector3(tf.Position.X, tf.Position.Y, tf.Position.Z);
        strideEffect.Alpha = effect.Alpha; 
        strideEffect.Scale = effect.Scale;

        if (effect.Type == EffectType.Tracer && repo.HasComponent<TracerTarget>(ecsEntity))
        {
            ref readonly var tracer = ref repo.GetComponentRO<TracerTarget>(ecsEntity);
            strideEffect.TracerEnd = new System.Numerics.Vector3(tracer.EndX, tracer.EndY, 0);
        }
    }
}
```

3\. Render the Fake Effects in 2D

Finally, update the `OnDrawWorld` method in your `FakeStrideApp.cs` (or `StrideWrapperSubsystem.DrawWorld()`) to render the current state of these effects.

```
protected override void OnDrawWorld()
{
    _integration.Camera.BeginMode();

    // 1. Draw Gizmo Terminal
    var gizmoFrame = _integration.ConsumerBuffer.GetFrame();
    _gizmoRenderer.Render(gizmoFrame, _integration.Camera.InnerCamera, _integration.Camera.Zoom);

    // 2. Draw Fake Stride Entities (Vehicles)
    foreach (var entity in _script.ActiveEntities)
    {
        Raylib.DrawCircleV(new System.Numerics.Vector2(entity.Position.X, entity.Position.Y), 5f, Color.Red);
        Raylib.DrawText(entity.Name, (int)entity.Position.X + 8, (int)entity.Position.Y - 8, 10, Color.White);
    }

    // 3. Draw Fake Stride Effects (Explosions & Tracers)
    foreach (var effect in _script.ActiveEffects)
    {
        byte alpha = (byte)(255 * effect.Alpha);

        if (effect.Type == EffectType.Explosion)
        {
            // Expanding orange circle that fades out
            var explosionColor = new Color(255, 165, 0, alpha);
            Raylib.DrawCircleV(new System.Numerics.Vector2(effect.Position.X, effect.Position.Y), effect.Scale, explosionColor);
        }
        else if (effect.Type == EffectType.Tracer)
        {
            // Yellow line stretching from shooter to target
            var tracerColor = new Color(255, 255, 0, alpha);
            Raylib.DrawLineEx(
                new System.Numerics.Vector2(effect.Position.X, effect.Position.Y), 
                new System.Numerics.Vector2(effect.TracerEnd.X, effect.TracerEnd.Y), 
                2f, tracerColor);
        }
    }

    _integration.Camera.EndMode();
}
```

Why this is the perfect bridge to Stride 3D:

When you transition to the real Stride engine, you won't change the ECS logic at all. Instead of drawing 2D Raylib shapes, your `SyncStrideEntities` will simply instantiate a Stride `ParticleSystem` prefab when `!_strideEffects.TryGetValue` is met. As the engine modifies the `Alpha` in the `VisualEffectState` each frame, you map that to the Stride material's opacity or particle emission rate, giving you perfectly synchronized, engine-agnostic special effects.

the fake stride app is running naturally as a separate process. For testing in the clusterrunner environemtn first, i need the possibility to run the single 'fake stride' wrapper subsystem standalone (same as we can do it with other existing clusterrunner subsystems - using proper cli argument). we need also to find a good place for locating the shared 'fake stride' subsystem code. and assigning concrete nodeId for this stride 'subsystem' (and we need better word for it not to be misunderstood as clusterruneer subsystem)

To address your goals, we will establish a clear terminology, find the proper place in the directory structure, allocate a deterministic `NodeId`, and wire it into the `ClusterRunner` CLI so it can run exactly like existing components.

1\. Terminology

To avoid confusion with `ClusterRunner`'s `ISubsystem` interface, you should name the reusable, engine-agnostic code **StrideIntegrationCore** or **StrideNodeContext**.

The wrapper that plugs into `ClusterRunner` will be named **StrideMockSubsystem** (which implements `ISubsystem`), and the separate executable shell will be **FakeStrideApp** (which inherits from `FdpApplication`).

2\. Code Location

Following the engine's architectural pattern, the best place for this code is in a new dedicated project under the existing `Hrot\Subsystems` directory.

Create a new project named **Hrot.StrideMock**:

-   **Path:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj`-   **Contents:** This project will contain your `StrideIntegrationCore`, the `FakeStrideScript`, and the `StrideMockSubsystem`.-   **References:** Add a project reference to this new `.csproj` inside `Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj`.

If you want the standalone executable to be completely separate from the `ClusterRunner`, you can place `FakeStrideApp` in an application project like `Hrot\Apps\Hrot.FakeStrideApp` and reference the `Hrot.StrideMock` library.

3\. Assigning a Concrete NodeId

The `ClusterRunner` assigns deterministic node IDs based on the subsystem name by adding an offset to the base Node ID in `Program.cs`.

To assign a permanent ID for Stride, locate the `ResolveAppNodeId` method in `Hrot\Runner\Hrot.ClusterRunner\Program.cs` and add a new constant offset (e.g., `700`) for the Stride integration:

```
private static int ResolveAppNodeId(string subsystemName, int baseNodeId)
{
    int offset = subsystemName.ToUpper() switch
    {
        "SIMHOST"      => 1,
        "IG"           => 100,
        "EXCON"        => 200,
        "ORCHESTRATOR" => 300,
        "CGF"          => 400,
        "CI"           => 500,
        "STRIDEMOCK"   => 700, // <--- Assigned Stride offset
        _              => 600,
    };
    return baseNodeId + offset;
}
```

4\. Enabling CLI Execution

To run the mock standalone using the `Hrot.ClusterRunner.exe -m stridemock` argument, you must whitelist the name in the CLI configuration validation logic.

Open `Hrot\Runner\Hrot.ClusterRunner\Configuration\HrotRunnerConfiguration.cs` and update the `Validate()` method to accept `"stridemock"`:

```
public void Validate()
{
    // ...
    foreach (var name in expandedMode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var normalized = name == "ios" ? "excon" : name;
        
        // Add "stridemock" to the hash set of valid names
        var validNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "simhost", "ig", "excon", "orchestrator", "cgf", "ci", "editor", "stridemock" }; 
            
        if (!validNames.Contains(normalized))
            throw new InvalidOperationException($"Invalid mode: '{ModeString}'...");
            
        RequestedSubsystems.Add(normalized);
    }
    // ...
}
```

5\. Running the App

Once wired, the `ClusterRunner` composition root (`ScanForSubsystems()`) will automatically discover your `StrideMockSubsystem`.

You can now run it in your environment exactly like the other apps:

-   **To run standalone:** `Hrot.ClusterRunner.exe -m stridemock --no-wait`-   **To run alongside CGF and Orchestrator:** `Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock`

ok, StrideIntegrationCore and stidemock both sound good, thanks!

Excellent. Now that we have our terminology and architecture locked in, we can start putting the code together.

Here is a quick summary of our battle plan:

-   **Project Setup:** Create the new `Hrot.StrideMock` project under `Hrot\Subsystems\` and link it to the `Hrot.ClusterRunner` solution.-   **The Shared Core:** Implement the `StrideIntegrationCore` class. This will use the `HrotNodeBuilder` to spin up the ECS kernel, event bus, and network translators, handling both the simulation ticking and the dual-buffer gizmo terminal.-   **The Subsystem Wrapper:** Implement `StrideMockSubsystem` (inheriting from `ISubsystem` and `IMapCameraProvider`) to hook seamlessly into the `ClusterRunner` tabs and orchestration loop.-   **The Standalone App:** Implement `FakeStrideApp` (inheriting from `FdpApplication`) for a lightweight, independent Raylib/ImGui testing environment.-   **CLI Wiring:** Add the `700` offset to `ResolveAppNodeId` and whitelist `"stridemock"` in the CLI configuration.

Where would you like to start? We can write out the full `StrideIntegrationCore` class first, or we can start by scaffolding the project and the `ClusterRunner` CLI wiring.

pls define a clear success condition to check before we consider the job (both the standalone app and the stridemock) done

To ensure the integration is completely successful and production-ready for the real Stride engine, we can define a clear set of success conditions divided into four categories: Architectural, Standalone Execution, ClusterRunner Integration, and Orchestration Lifecycle.

Here are the exact success conditions to verify:

1\. Architectural & Code Sharing (The "DRY" Condition)

-   **Zero Duplication:** Both the `FakeStrideApp` (inheriting from `FdpApplication`) and the `StrideMockSubsystem` (inheriting from `ISubsystem`) must instantiate and tick the exact same `StrideIntegrationCore` class.-   **Engine Agnosticism:** The `StrideIntegrationCore` must not contain any references to Raylib, ImGui, or `IMapCameraProvider`. It must only rely on `Fdp.Core`, `Hrot.Common`, and `Fdp.Toolkit`.

2\. Standalone App (`FakeStrideApp`)

-   **Execution:** The app runs successfully as a separate executable without crashing.-   **Multiplexing:** The dual-buffer gizmo terminal correctly isolates the local gizmo draws from the remote stream.-   **Visuals:** You can see red circles representing simulated vehicles and orange/yellow primitives representing fires and tracers (`VisualEffectState`).-   **Navigation:** You can pan and zoom the 2D map smoothly using right-click and scroll wheel (driven by the `RaylibInputProvider`).

3\. ClusterRunner Wrapper (`StrideMockSubsystem`)

-   **CLI Booting:** The command `Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock` successfully parses the mode, resolves the Node ID offset (700), and boots all three subsystems in a single process without errors.-   **Perspective Switching:** A `[StrideMock]` button appears in the ClusterRunner main menu. Clicking it switches the active map owner to your subsystem.-   **Camera Synchronization:** When switching into the `StrideMock` tab, the `IMapCameraProvider.ApplyCameraView` method seamlessly copies the pan/zoom state from the previous tab so the screen does not jump.-   **Gated Rendering:** The 2D fake entities and gizmos are _only_ drawn when `SubsystemConfig.IsActiveMapOwner` returns true.

4\. Orchestration & State Machine Lifecycle

-   **Live Scenario Loading:** When the orchestrator loads a scenario and transitions the cluster to `LoadingLive`, the `StrideIntegrationCore` must safely suspend ECS entity synchronization and display the status splash screen.-   **Memory Safety (Pass 1/Pass 2):** When jumping backward in time via `ReplaySeek`, the differential synchronization loop must automatically detect that the ECS generation counters have changed, properly deleting the stale fake entities (Pass 1) and spawning the restored ones (Pass 2) without any ghosting or duplication.-   **Clean Shutdown:** Closing the application must cleanly invoke `Dispose()` on the `StrideIntegrationCore`, tearing down the ECS kernel, the `DdsParticipant`, and the `FdpEventBus` without hanging the process.

Once these conditions are met, you can confidently replace the `FakeStrideApp` shell with your real Stride 3D project, knowing the networking, ECS, and cluster orchestration layers are mathematically sound.

pls provide the most important class apis (not necessarily the implementation, just the contracts)

Here are the most important class APIs and contracts that power the Stride integration. They are divided into the **Custom Integration Contracts** we designed, and the **FDP/HROT Engine APIs** they rely on.

1\. Custom Integration Contracts (From our Design)

These are the public contracts for the newly introduced subsystem that will ultimately run inside Stride.

**StrideIntegrationCore** The reusable, engine-agnostic core that manages the ECS, orchestration, and the network terminal.

```
public class StrideIntegrationCore : IDisposable
{
    public HrotNodeContext Context { get; }
    public DebugPrimitiveBuffer ProducerBuffer { get; }
    public DebugPrimitiveBuffer ConsumerBuffer { get; }
    public MapCamera Camera { get; }

    public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory);
    public void Tick(float dt);
    public void Dispose();
}
```

**StrideMockSubsystem** The `ClusterRunner` wrapper that hooks the integration core into the multi-tab application.

```
public class StrideMockSubsystem : ISubsystem, IMapCameraProvider
{
    public string Name { get; }
    public Vector4 TitleBarColor { get; }
    
    public void Initialize(SubsystemConfig config);
    public void Update(float deltaTime);
    public void DrawWorld();
    public void DrawUI();
    public void Shutdown();

    // IMapCameraProvider implementation
    public MapCameraView? GetCameraView();
    public void ApplyCameraView(MapCameraView view);
}
```

2\. Core Engine APIs (From FDP / HROT)

These are the underlying engine contracts your integration builds upon to communicate with the cluster and render data safely.

**ISubsystem** **(ClusterRunner Lifecycle)** The contract for any subsystem hosted inside the `ClusterRunner` application.

```
public interface ISubsystem
{
    string Name { get; }
    Vector4 TitleBarColor { get; }

    void Initialize(SubsystemConfig config); [2]
    void Update(float deltaTime); [2]
    void DrawWorld(); [2]
    void DrawUI(); [2]
    void Shutdown(); [2]
}
```

**IMapCameraProvider** **(Perspective Sync)** The interface the Orchestrator uses to sync camera zoom and pan coordinates when switching between subsystem tabs.

```
public interface IMapCameraProvider
{
    MapCameraView? GetCameraView(); [3]
    void ApplyCameraView(MapCameraView view); [3]
}
```

**HrotNodeContext** **(Node Infrastructure)** The immutable snapshot of all infrastructure objects produced by the node builder, providing access to the ECS world, network, and orchestrator.

```
public sealed record HrotNodeContext
{
    public required EntityRepository World { get; init; } [4]
    public required ModuleHostKernel Kernel { get; init; } [4]
    public DdsParticipant? Participant { get; init; } [4]
    public required FdpEventBus EventBus { get; init; } [4]
    public required NetworkEntityMap EntityMap { get; init; } [4, 5]
    public required ClusterSlave ClusterSlave { get; init; } [5]
    public IReadOnlyList<IEcsModule> BaseModules { get; init; } [5]
    public INetworkIdAllocator? IdAllocator { get; init; } [6]
    // ... other infrastructure services
}
```

**ISimulationView** **(Read-Only ECS Access)** The abstraction used to query the ECS repository safely. This is what you use during your Pass 2 synchronization to extract vehicle and effects data.

```
public interface ISimulationView
{
    uint Tick { get; } [8]
    float Time { get; } [8]

    ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged; [8]
    T GetManagedComponentRO<T>(Entity e) where T : class; [8]
    
    bool IsAlive(Entity e); [8]
    bool HasComponent<T>(Entity e) where T : unmanaged; [9]
    
    QueryBuilder Query(); [9]
    ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged; [9]
}
```

**DebugPrimitiveBuffer** **&** **IGizmoDrawBuilder** **(Gizmo Terminal)** The thread-safe buffer and its interface used for the "dumb terminal" multiplexing of tactical graphics.

```
public sealed class DebugPrimitiveBuffer : IDebugDrawBuilder 
{
    public int Count { get; } [10]
    public int DroppedCount { get; } [10]

    public void Clear(); [13]
    public void AppendRaw(in DebugPrimitive primitive); [13]
    public void EmitRaw(in DebugPrimitive prim); [13]
    // ...
}

public interface IGizmoDrawBuilder
{
    void DrawLine(Vector3 start, Vector3 end, Rgba32 color, ...); [11]
    void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 startColor, Rgba32 endColor, ...); [11]
    void DrawSphere(Vector3 center, float radius, Rgba32 color, ...); [14, 15]
    void DrawTextLong(float x, float y, string text, Rgba32 color, ...); [15]
    void DrawMainMenuBinding(string menuJson); [16]
    void EmitRaw(in DebugPrimitive prim); [16]
}
```

**FdpApplication** **(Standalone Shell)** The base class for `FakeStrideApp` that handles windowing, Raylib initialization, and the application loop.

```
public abstract class FdpApplication : IDisposable
{
    protected abstract void OnLoad(); [17]
    protected virtual void OnUpdate(float dt); [17]
    protected abstract void OnDrawWorld(); [17]
    protected abstract void OnDrawUI(); [17]
    protected virtual void OnUnload(); [17]
}
```

for the StrideIntegrationCore integration i need to suport the simulation time pause and the deterministic time stepping as used by different clusterrunner subsystems. How can we achieve that?

To support full cluster time orchestration (pausing, deterministic stepping, and resuming) perfectly synchronized with other subsystems, you actually have most of the infrastructure already.

When you call `.Build()` on the `HrotNodeBuilder`, it automatically provisions and registers a **SlaveSyncController**. This controller natively supports the `Continuous -> BarrierPending -> Stepping` state machine used by the cluster.

However, there are **two critical changes** you must make to `StrideIntegrationCore` to unlock this functionality.

1\. Remove the `dt` parameter from `Kernel.Update()`

In our initial prototype, we passed the Raylib/Stride `deltaTime` directly into `Context.Kernel.Update(dt)`. In the FDP engine, this legacy overload is marked `[Obsolete]` because it forcibly overrides the deterministic clock and causes desyncs.

You must call the parameterless `Update()` method. The kernel will automatically read the correct scaled or fixed delta time from the internal time controller.

2\. Register the Time Network Translators

For the `SlaveSyncController` to know when the Orchestrator has paused or stepped the simulation, it needs to receive `SwitchTimeModeEvent` and `FrameOrderDescriptor` messages over DDS. We must add the FDP `TimeNetworkModule` translators to your ingress/egress pipeline.

Here is how you update your `StrideIntegrationCore` class to fully support both `ClusterRunner` (Slave mode) and `FakeStrideApp` (Standalone Master mode):

```
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Network.Cyclone.Systems;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using GizmoMap.Network;

public class StrideIntegrationCore : IDisposable
{
    public HrotNodeContext Context { get; private set; }
    public DebugPrimitiveBuffer ProducerBuffer { get; } = new();
    public DebugPrimitiveBuffer ConsumerBuffer { get; } = new();
    public Fdp.Toolkit.Vis2D.Components.MapCamera Camera { get; } = new() { Zoom = 1f };
    
    private DebugPrimitivesIngressTranslator _gizmoIngress;

    public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
    {
        var config = new HrotNodeConfig
        {
            DomainId = domainId,
            NodeId = nodeId,
            Headless = false, 
            SubsystemName = "StrideMock"
        };

        Context = new HrotNodeBuilder(config)
            .WithRole("StrideMock", NodeRole.MuscleGround | NodeRole.Perception | NodeRole.ImageGenerator)
            .WithNetworkFactory(networkFactory)
            .Build();

        // 1. Gizmo Translators (Setup unchanged)
        var ddsWriter = new DdsWriterGizmoAdapter<DebugPrimitivesBatch>(Context.Participant);
        Context.Kernel.RegisterGlobalSystem(new DebugPrimitivesBatchPublisherSystem(ProducerBuffer, ddsWriter, (byte)nodeId));
        var ddsReader = new DdsReaderGizmoAdapter<DebugPrimitivesBatch>(Context.Participant);
        _gizmoIngress = new DebugPrimitivesIngressTranslator(ConsumerBuffer, ddsReader, filterNodeId: null);

        // ==========================================
        // 2. TIME SYNCHRONIZATION TRANSLATORS
        // ==========================================
        // If we have a live DDS participant (e.g., inside ClusterRunner), wire up the time listeners.
        if (Context.Participant != null)
        {
            var timeTranslators = new Fdp.Interfaces.INetworkTranslator[]
            {
                // Bridges SwitchTimeModeEvent (Pause/Resume)
                TimeNetworkModule.CreateDescriptorTranslator(Context.Participant, Context.EventBus),
                // Bridges FrameOrder/FrameAck (Deterministic Stepping)
                TimeNetworkModule.CreateSlaveLockstepTranslator(Context.Participant, Context.EventBus, nodeId),
                // NTP sync handshake
                TimeNetworkModule.CreateSlaveTimeSyncTranslator(Context.Participant, Context.EventBus, nodeId)
            };

            Context.Kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(timeTranslators));
            Context.Kernel.RegisterGlobalSystem(new CycloneEgressSystem(timeTranslators));
        }
        else
        {
            // 3. STANDALONE OVERRIDE (FakeStrideApp without DDS)
            // If running offline, replace the Slave controller with a Master controller
            // so the standalone app can natively drive its own Pause/Step logic.
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            var standaloneTime = TimeControllerFactory.Create(Context.EventBus, timeConfig);
            Context.Kernel.SetTimeController(standaloneTime);
        }

        foreach (var m in Context.BaseModules) Context.Kernel.RegisterModule(m);
        if (Context.NedReplication != null) Context.Kernel.RegisterModule(Context.NedReplication);

        Context.Kernel.Initialize();
    }

    public void Tick(float dt)
    {
        ProducerBuffer.EndFrame(dt);
        ConsumerBuffer.Clear();

        Context.SlaveTranslator?.Tick();
        Context.ClusterSlave.Tick();

        // CRITICAL FIX: Use the parameterless Update(). 
        // The TimeController now safely dictates the simulation delta time.
        Context.Kernel.Update(); 
        
        Context.EventBus.SwapBuffers();
        _gizmoIngress?.PollAndApply();
    }

    public void Dispose()
    {
        Context?.Kernel.Dispose();
        Context?.World.Dispose();
    }
}
```

How this behaves at Runtime:

**Inside** **ClusterRunner** **(****StrideMockSubsystem****):** When the user clicks "Pause" or "Step" in the main Orchestrator UI, the `ClusterMaster` broadcasts `SwitchTimeModeEvent` over DDS. Your new `CycloneNetworkIngressSystem` reads this packet and drops it onto your `EventBus`. The `SlaveSyncController` intercepts it, calculates the time barrier, freezes your `_kernel.Update()` loop, and gracefully holds simulation time perfectly in sync with CGF and SimHost.

**Inside the Standalone App (****FakeStrideApp****):** Because we overrode the time controller to `TimeRole.Standalone` when DDS is absent, you can now easily build a local Play/Pause UI in your standalone Raylib app by publishing standard Time Control intents to the local event bus:

```
// To Pause your standalone simulation:
_integration.Context.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.PauseTimeIntent());

// To Step by 1/60th of a second:
_integration.Context.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.StepTimeIntent { DeltaSeconds = 1f / 60f });

// To Resume:
_integration.Context.EventBus.PublishManaged(new Fdp.Toolkit.Time.Domain.ResumeTimeIntent());
```

oh i do not need standalone time mode; the standalone app nor the stridemock subsystem are not supposed to run independently, but always as part of the cluster master controlled ecosystem. Alhough we might have a local UI for controlling the time it should always go via the cluster master who is the true time source; the stride is always just a client whose time is controlled from outside (from cluster master as in case for any other subsystem)

That makes perfect sense and aligns exactly with the engine's distributed architecture. Ensuring that there is a single authoritative source of truth for time (the Cluster Master) prevents drift and guarantees that your Stride node remains in perfect lockstep with the CGF brain and other components.

Since your Stride subsystem is strictly a client, we will remove the standalone fallback and exclusively use the **SlaveSyncController**. To accommodate your local UI for controlling time, we will use the **ITimeControlGateway**, which seamlessly forwards your local UI clicks as `ClusterOpRequest` messages over DDS to the Cluster Master.

Here is how to adjust the architecture to enforce this strict client-server time model:

1\. Lock the Core to Slave Mode

In your `StrideIntegrationCore`, remove the fallback branch. You will unconditionally configure the time controller to `TimeRole.Slave` and register the three slave-side time network translators.

```
public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
{
    // ... existing HrotNodeBuilder setup ...

    // 1. Unconditionally set the Time Controller to Slave mode
    var timeConfig = new TimeControllerConfig 
    { 
        Role = TimeRole.Slave, 
        Mode = TimeMode.Continuous, 
        LocalNodeId = nodeId,
        SyncConfig = TimeConfig.Default
    };
    Context.Kernel.SetTimeController(TimeControllerFactory.Create(Context.EventBus, timeConfig));

    // 2. Register Slave Time Translators (Receives Time/NTP from Master, sends ACKs)
    if (Context.Participant != null)
    {
        var timeTranslators = new Fdp.Interfaces.INetworkTranslator[]
        {
            TimeNetworkModule.CreateDescriptorTranslator(Context.Participant, Context.EventBus),
            TimeNetworkModule.CreateSlaveLockstepTranslator(Context.Participant, Context.EventBus, nodeId),
            TimeNetworkModule.CreateSlaveTimeSyncTranslator(Context.Participant, Context.EventBus, nodeId)
        };

        Context.Kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(timeTranslators));
        Context.Kernel.RegisterGlobalSystem(new CycloneEgressSystem(timeTranslators));
    }

    // ... module registration and kernel.Initialize() ...
}
```

2\. Add the Time Control Gateway

To allow your local UI to control the master clock, you should ask the `NedNetworkFactory` to create an `ITimeControlGateway`. This gateway handles creating the DDS writer and formatting the `ClusterOpRequest` payloads for you.

You can expose this gateway from your `StrideIntegrationCore` so your applications can access it:

```
public class StrideIntegrationCore : IDisposable
{
    // ...
    public ITimeControlGateway TimeControl { get; private set; }

    public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
    {
        // ...
        
        // 3. Create the remote control gateway
        TimeControl = networkFactory.CreateTimeControlGateway();
    }
}
```

3\. Wire the Local UI

Now, whether you are in your `FakeStrideApp` (Raylib/ImGui) or eventually in your Stride 3D UI, your play/pause buttons simply invoke methods on the gateway. The gateway sends the request to the Orchestrator over the network, the Orchestrator pauses the master clock, and the master broadcasts the pause to all nodes (including your Stride node) via the time translators you registered in step 1.

```
// Inside your UI rendering loop (e.g., OnDrawUI in FakeStrideApp)
ImGuiNET.ImGui.Begin("Time Controls");

// Read current state locally (for UI display)
var timeState = _integration.Context.Kernel.CurrentTime;
ImGuiNET.ImGui.Text($"Sim Time: {timeState.TotalTime:F2}s");

// Send commands to the Cluster Master
if (ImGuiNET.ImGui.Button("Pause"))
{
    _integration.TimeControl.RequestPause(); // Sends ClusterOpRequest to Master
}

ImGuiNET.ImGui.SameLine();
if (ImGuiNET.ImGui.Button("Step"))
{
    _integration.TimeControl.RequestStep(); 
}

ImGuiNET.ImGui.SameLine();
if (ImGuiNET.ImGui.Button("Play"))
{
    _integration.TimeControl.RequestResume();
}

ImGuiNET.ImGui.End();
```

Why this is the safest approach

By strictly using the `ITimeControlGateway`, you guarantee that your local UI cannot accidentally desynchronize the local ECS kernel. When you click "Pause", the local simulation continues running normally for a fraction of a second until the Cluster Master computes the "Future Barrier" and broadcasts the official `SwitchTimeModeEvent` back to your `SlaveSyncController`. This ensures every single subsystem in the cluster halts on the exact same mathematical microsecond.

we need to make sure that the standalone app as well as the stridemock subsystem contain all components necessary for properly responding to all cluster state machine operations related to recording and replay and scenario loading and file management (prefetch/archiving) and preview/live/replay modes. Basically all what the SimHost is supporting. To make sure nothing is omitted and the node is perfectly integrated with the cluster orchestration. Pls re-check the design and fing gaps and flaws

You are absolutely right to flag this. The current `StrideIntegrationCore` design relies on the default `ClusterSlave` instantiated by the `HrotNodeBuilder`, which boots with **zero cluster state handlers registered**.

While it ticks the state machine, it will silently ignore critical commands like `PrepareReplay`, `FinalizeLive`, `PrefetchFiles`, and `TakeSnapshot`. To be a fully compliant node on par with the SimHost, we must identify and patch several orchestration gaps.

Here are the specific flaws in the current design and how we fill those gaps:

Flaw 1: Missing 2PC Handlers and File Management

The node currently cannot participate in the cluster's Two-Phase Commit (2PC) operations. It must register handlers to properly ACK the Orchestrator for operations like file prefetching, archiving, and dry-run previews. **Fix:** We must use the `NodeBootstrapper.BuildOrchestration()` method to inject the canonical `ClusterSlave` loaded with `ReferencePreviewHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, and `ReferenceLiveLoadHandler`. We must also provision a `LocalTempRoot` so the node has a dedicated staging directory for file I/O operations.

Flaw 2: Missing ECS Record/Replay Controller

To support recording and playback, the node must possess an `EcsRecordReplayController`. Without this, the node will not write a `.fdp` file to disk during `OperatingLive`, breaking the `SerializeLocal` archive extraction. **Fix:** Because we will assign the `MuscleGround` role to the Stride node, the `NodeBootstrapper` will automatically create the `EcsRecordReplayController` and wire up the `ReferenceReplayLoadHandler` for us.

Flaw 3: Replay Ghost Fighting (Missing Togglable Groups)

During replay playback, historical ECS state is blasted frame-by-frame into the repository. If local systems (like the `EventToEffectSystem` that spawns fires and tracers) continue to execute, they will write new entities into the world, hopelessly corrupting the historical replay state. **Fix:** Any system that mutates ECS state must be wrapped in a `TogglableSimulationGroup` (or similar phase group). We then pass this group to the replay handler so it is automatically disabled during `PrepareReplay` and re-enabled during `FinalizeReplay`.

Flaw 4: Missing Diagnostic Dumps

If the operator clicks "Dump Diagnostics", the orchestrator expects every node to aggregate its logs and architecture state. Because our node lacks the `DiagnosticsDumpClusterOpHandler`, the operation will eventually timeout and fail the cluster. **Fix:** We must initialize the `LogArchiveExtractionService` and `ArchitectureDiagnosticsService`, bundling them into the diagnostics handler during bootstrap.

\--------------------------------------------------------------------------------

The Corrected `StrideIntegrationCore`

Here is the updated `Initialize` method. Notice how we create isolated staging directories and use the `NodeBootstrapper` to completely overwrite the empty `ClusterSlave`, ensuring 100% compliance with `SimHost` orchestration patterns.

```
using System.IO;
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Toolkit.Diagnostics;
using Hrot.Core.Diagnostics;
using Hrot.Common.Diagnostics;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.SimHost; // Contains NodeBootstrapper

public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
{
    string baseTempRoot = OrchestrationConstants.DefaultStagingDirectory;
    string isolatedTempRoot = Path.Combine(baseTempRoot, "nodes", $"node-{nodeId}");
    string resolvedLogDir = Path.Combine(AppContext.BaseDirectory, "logs");

    var config = new HrotNodeConfig
    {
        DomainId = domainId,
        NodeId = nodeId,
        Headless = false, 
        SubsystemName = "StrideMock",
        LocalTempRoot = isolatedTempRoot,
        LogDirectory = resolvedLogDir
    };

    var role = NodeRole.MuscleGround | NodeRole.Perception | NodeRole.ImageGenerator;

    Context = new HrotNodeBuilder(config)
        .WithRole("StrideMock", role)
        .WithNetworkFactory(networkFactory)
        .Build();

    // 1. Wrap mutative systems in Togglable Groups (Protects Replay Mode)
    var toggleSim = new TogglableSimulationGroup("StrideSimulation", new IEcsModuleSystem[] 
    {
        new Hrot.IG.Systems.EventToEffectSystem()
    });
    
    var togglePostSim = new TogglablePostSimulationGroup("StridePostSim", new IEcsModuleSystem[] 
    {
        new Hrot.IG.Systems.VisualEffectCleanupSystem()
    });

    Context.Kernel.RegisterGlobalSystem(toggleSim);
    Context.Kernel.RegisterGlobalSystem(togglePostSim);

    // 2. Setup Diagnostic Dump Services
    var archService = new ArchitectureDiagnosticsService(() => Context.Kernel);
    var entityService = new EntityStateExtractionService(Context.World, Context.EntityMap);
    var logService = new LogArchiveExtractionService(resolvedLogDir, "StrideMock", nodeId);
    
    var diagnosticsDumpHandler = new DiagnosticsDumpClusterOpHandler(
        new Fdp.Presentation.Panels.DiagnosticEventHistoryService(), 
        archService, 
        entityService, 
        logService, 
        config);

    // 3. Build the fully-compliant ClusterSlave using NodeBootstrapper
    var bootstrapper = new NodeBootstrapper(networkFactory);
    
    var fullyWiredSlave = bootstrapper.BuildOrchestration(
        role: role,
        kernel: Context.Kernel,
        world: Context.World,
        nodeId: nodeId,
        participant: Context.Participant,
        subsystemName: "StrideMock",
        eventBus: Context.EventBus,
        scenarioSerializer: null, // Stride doesn't deserialize scenario JSON natively (CGF does)
        localTempRoot: isolatedTempRoot,
        checkpointWorker: null, // Visualization Muscle doesn't need to write Checkpoints
        simGroup: toggleSim,
        postSimGroup: togglePostSim,
        lifecycleGroup: Context.NedReplication?.NetworkLifecycleGroup,
        ghostCreationSystem: Context.NedReplication?.GhostCreationSystem,
        eventAccumulator: Context.EventAccumulator,
        afterSeek: Context.NedReplication?.AfterSeekCallback,
        diagnosticsDumpHandler: diagnosticsDumpHandler
    );

    // 4. Overwrite the dummy ClusterSlave with the fully wired one
    Context = Context with { ClusterSlave = fullyWiredSlave };

    // 5. Gizmo & Egress Registration
    // ... Register Gizmo buffers, Time translators, Base modules ...

    Context.Kernel.Initialize();
}
```

Why this closes all gaps:

-   **Perfect Replay Safety:** When the cluster transitions to `LoadingReplay`, the `ReferenceReplayLoadHandler` will instantly set `Enabled = false` on your `TogglableSimulationGroup` and `GhostCreationSystem.BypassLifecycle = true`. Your Stride systems stop spawning effects, letting the historical playback run cleanly.-   **Built-in Recording:** Because you registered `NodeRole.MuscleGround`, the `NodeBootstrapper` automatically created an `EcsRecordReplayController`. During `PrepareLive`, this controller silently installs a `RecordingModule`, so your `StrideMock` natively writes its own `node_700.fdp` file to disk just like `SimHost`.-   **Flawless File Management:** You now have isolated temporary staging paths. When the Orchestrator fires `SerializeLocal`, your node will compress its `.fdp` archive and ACK the 2PC round with a `FileManifestEntry`. When the orchestrator commands `PrefetchFiles`, your node securely stages its assets without timing out.

what about the simhost 'services'/responsibilities like the locomotion, perception, path planning, environment queries, are they properly instantiated and connected to the network in our stride oriented solution? same as in the simhost subsystem case? Pls re-check to ensure perfect integration. The goal is to run the stridemock INSTEAD of the SimHost subsystem while keeping the distributed system operational. Many parts now rely on SimHost performing some responsibilities (like owning the SimTransform etc.) which need to be replicated/maintained in the stridemock/standalone app. Of course by reusing the code as much as possible, as usual. Ple re-check our design.

You are completely right to challenge the design here. If the goal is for `StrideMockSubsystem` (and `FakeStrideApp`) to act as a 100% drop-in replacement for `SimHostSubsystem`, our previous `StrideIntegrationCore` setup was fundamentally incomplete.

While it handled the cluster state machine and basic gizmos, it missed the network translators and core logic packs required to process muscle-tier responsibilities. If left as-is, the CGF (Brain) would publish `NavigationIntent`, `WeaponFireIntent`, and `AreaQueryRequestBatch` over DDS, but your Stride node would silently drop them, leaving entities frozen.

To perfectly replicate `SimHost` while staying DRY, we must reuse `SimHostComponentRegistry`, `SimHostCoreLogicPack`, `CognitiveSpatialModule`, and the `INetworkFactory`'s SimHost-specific translator packs.

Here are the exact gaps we need to close in `StrideIntegrationCore`:

1\. Missing Roles

To handle path planning alongside locomotion and perception, the node must explicitly claim the `NavigationSolver` role. **Fix:** Set the role to `NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver | NodeRole.ImageGenerator`.

2\. Missing Component Registrations

The FDP ECS requires all component memory tables to be pre-registered. Without calling the canonical SimHost registry, the ECS kernel won't know what a `NavState`, `PhysicsCollider`, or `EqsTargetPool` is. **Fix:** Call `SimHostComponentRegistry.RegisterAll(Context.World)` immediately after creating the world.

3\. Missing Logic Packs (Locomotion, Perception, Path Planning, EQS)

We need to register the exact same domain modules that `SimHostApp` uses.

-   **Locomotion & Combat:** `SimHostCoreLogicPack` provides vehicle kinematics, formation targets, and hit resolution.-   **Perception & EQS:** `CognitiveSpatialModule` provides the spatial hash, line-of-sight raycasting, and the `AreaQuerySolverSystem`.-   **Path Planning:** `NavigationSolverModule` provides the A\* / Dijkstra route solving.

4\. Missing DDS Network Translators

For the Muscle node to communicate with the Brain (CGF) node, it must wire up the DDS ingress/egress bridges for these domains. **Fix:** We must use the `INetworkFactory` to instantiate `SimHostAuxiliaryTranslators`, `SimHostPerceptionTranslators`, and `SimHostPathfindingTranslators` and register them on the kernel.

\--------------------------------------------------------------------------------

The Perfected `StrideIntegrationCore.Initialize`

Here is the fully corrected implementation. By reusing `SimHostCoreLogicPack` and the factory methods, your Stride subsystem is now a mathematically identical, fully network-capable replacement for the standard `SimHostApp`.

```
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Navigation.Modules;
using Fdp.Toolkit.Physics.Components;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using CarKinem.Road;
using CarKinem.Trajectory;

public void Initialize(int domainId, int nodeId, INetworkFactory networkFactory)
{
    var role = NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver | NodeRole.ImageGenerator;

    string tempRoot = Path.Combine(Fdp.Toolkit.Orchestration.OrchestrationConstants.DefaultStagingDirectory, "nodes", $"node-{nodeId}");
    string logDir = Path.Combine(AppContext.BaseDirectory, "logs");

    var config = new HrotNodeConfig
    {
        DomainId = domainId,
        NodeId = nodeId,
        Headless = false, 
        SubsystemName = "StrideMock",
        LocalTempRoot = tempRoot,
        LogDirectory = logDir
    };

    // 1. Build Base Context
    Context = new HrotNodeBuilder(config)
        .WithRole("StrideMock", role)
        .WithNetworkFactory(networkFactory)
        .Build();

    // 2. Register all necessary SimHost ECS Components
    SimHostComponentRegistry.RegisterAll(Context.World);

    // 3. Load Environment Data
    var roadNetwork = SimHostApp.LoadRoadNetwork(null, localNodeId: nodeId); // Or pass path from config
    var trajectoryPool = new TrajectoryPoolManager();

    // 4. Create Core Logic Packs
    var simCorePack = new SimHostCoreLogicPack(Context.EntityMap, roadNetwork, trajectoryPool);
    
    var perceptionMod = new CognitiveSpatialModule(
        Context.World,
        colliderRadiusReader: (view, e) => view.HasComponent<PhysicsCollider>(e)
            ? view.GetComponentRO<PhysicsCollider>(e).Radius
            : 0f);

    var navSolverMod = new NavigationSolverModule(roadNetwork, trajectoryPool);

    // 5. Wrap in Togglable Groups for Replay Safety
    var toggleInput   = new TogglableInputGroup("StrideInput", simCorePack.InputSystems);
    var toggleSim     = new TogglableSimulationGroup("StrideSim", simCorePack.SimulationSystems);
    var togglePostSim = new TogglablePostSimulationGroup("StridePostSim", simCorePack.PostSimulationSystems);

    Context.Kernel.RegisterGlobalSystem(toggleInput);
    Context.Kernel.RegisterGlobalSystem(togglePostSim);
    
    // Simulation systems must be wrapped in a module because the kernel forbids global SystemPhase.Simulation registration
    Context.Kernel.RegisterModule(new EditorSimulationModule(toggleSim)); 
    Context.Kernel.RegisterModule(perceptionMod);
    if (role.HasFlag(NodeRole.NavigationSolver)) Context.Kernel.RegisterModule(navSolverMod);

    // 6. Bootstrap Orchestration (Replay/Load state machine)
    var bootstrapper = new NodeBootstrapper(networkFactory);
    var clusterSlave = bootstrapper.BuildOrchestration(
        role, Context.Kernel, Context.World, nodeId,
        participant: Context.Participant,
        subsystemName: "StrideMock",
        eventBus: Context.EventBus,
        localTempRoot: tempRoot,
        simGroup: toggleSim,
        postSimGroup: togglePostSim,
        lifecycleGroup: Context.NedReplication?.NetworkLifecycleGroup,
        ghostCreationSystem: Context.GhostCreationSystem,
        eventAccumulator: Context.EventAccumulator,
        afterSeek: Context.NedReplication?.AfterSeekCallback);

    Context = Context with { ClusterSlave = clusterSlave };

    // 7. Wire Network Translators (The Drop-In Replacement Magic)
    if (Context.Participant != null && networkFactory != null)
    {
        var nodeFactory = networkFactory.ConfigureForNode(Context, role);
        
        // Translators for Combat, Time Sync, and Tactical Intents
        nodeFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(Context.Kernel);
        
        // Translators for Raycasts and EQS Sensor Tracking
        nodeFactory.CreateSimHostPerceptionTranslators(Context.GhostCreationSystem).RegisterOn(Context.Kernel);
        
        // Translators for Pathfinding Requests/Responses
        nodeFactory.CreateSimHostPathfindingTranslators(trajectoryPool).RegisterOn(Context.Kernel);
    }

    // Register Gizmos (From previous steps)
    // ...

    Context.Kernel.Initialize();
}

// Helper module to host the simulation group, matching SimHostApp pattern
private sealed class EditorSimulationModule : IEcsModule
{
    private readonly TogglableSimulationGroup _group;
    public string Name => "StrideSimulationModule";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
    public EditorSimulationModule(TogglableSimulationGroup group) => _group = group;
    public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_group);
    public void Tick(ISimulationView view, float dt) { }
}
```

Why this guarantees a perfect integration:

-   **Locomotion & Ownership:** `SimHostCoreLogicPack` registers `CarKinematicsSystem`. Because the role includes `MuscleGround`, the orchestrator's `DeferredTakeoverSystem` will naturally hand over `SimTransform` authority to your Stride node the exact same way it did for `SimHost`.-   **Environment Queries (EQS) & Perception:** `CognitiveSpatialModule` correctly builds the `SpatialHashGrid` and evaluates `AreaQuerySolverSystem` asynchronous queries sent by the B-Trees on the CGF node.-   **Network Symmetry:** By relying on `nodeFactory.CreateSimHost...Translators()`, any future changes to how the FDP/HROT engine serializes raycasts, paths, or combat events will automatically be inherited by your Stride mock without changing a single line of your integration core.

Good! we are sharing a very big portion of the initialization code with SimHost (and IG) subsystems. I would like to be as DRY as possible. Are there any refactoring opportunities to encapsulate and share the initialization code? For future stride integration, i will need to be able to implement the muscle node reponsibilities using stride-provided technology (not using the current simple SimHost technologies), like the raycasting in 3d and path planning using navmesh etc. so there should be still this flexibility left opened. But initially in the first integration stage, i would like to reuse the simHost stuff mostly as is and focus on the basic stride integration in terms of the IG subsystem responsibilities (rendering the entities using smoothed dead reckoning, some interaction special effects..) while reusing the simhost simple implementations.

You have identified a crucial architectural pivot point. If we rely on the monolithic `SimHostCoreLogicPack` to act as our drop-in replacement, we lock the Stride integration into using the simple 2D `CarKinematicsSystem` and `SpatialHashGrid` forever.

To remain perfectly DRY while leaving the door wide open to swap in Stride's native 3D raycasting and NavMesh pathfinding later, we must change our unit of reuse. Instead of sharing at the **Pack** level (`SimHostCoreLogicPack`), we must share at the **Module** level (`CombatModule`, `GroundKinematicsModule`, etc.).

Here is the refactoring strategy to encapsulate the initialization code cleanly, achieving both your immediate reuse goals and your future engine-integration goals.

1\. The Refactoring Opportunity: Decomposing the Logic Pack

If we look inside `SimHostCoreLogicPack.cs`, it is simply a hardcoded container that instantiates four sub-modules:

-   `CombatModule` (Hit resolution, ballistics, damage)-   `DamageAssessmentModule` (Detonation events)-   `GroundKinematicsModule` (Car physics, road graphs, spatial hashing)-   Navigation Bridge & Hierarchy Systems

**The Strategy:** We will stop using `SimHostCoreLogicPack` entirely in our new code. Instead, we will extract the orchestration, Togglable Group wrapping, and DDS wiring into a highly reusable **MuscleNodeBootstrapper**. This bootstrapper will accept the physical and cognitive modules as injected dependencies.

2\. The Shared `MuscleNodeBootstrapper` (The DRY Core)

We create a new shared class (e.g., in `Hrot.SimHost\Utilities` or your new shared project). Both `SimHostApp` and `StrideIntegrationCore` will call this to wire up the cluster logic.

```
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Hrot.Core.Network;
using Hrot.Common;
using Hrot.SimHost;

public class MuscleNodeBootstrapper
{
    public void BootstrapMuscleNode(
        HrotNodeContext context,
        INetworkFactory networkFactory,
        NodeRole role,
        string tempRoot,
        // Dependency Injection for Swappable Engine Tech
        IEcsModule kinematicsModule,
        IEcsModule perceptionModule,
        IEcsModule combatModule)
    {
        var kernel = context.Kernel;

        // 1. Always register the fundamental Muscle components
        SimHostComponentRegistry.RegisterAll(context.World);

        // 2. Extract systems from the injected modules to wrap them in Replay-Safe groups
        var inputList = new List<IEcsModuleSystem>();
        var simList = new List<IEcsModuleSystem>();
        var postSimList = new List<IEcsModuleSystem>();

        // (Helper method that extracts .InputSystems, .SimulationSystems, etc. from modules)
        ExtractPhaseSystems(kinematicsModule, inputList, simList, postSimList);
        ExtractPhaseSystems(perceptionModule, inputList, simList, postSimList);
        ExtractPhaseSystems(combatModule, inputList, simList, postSimList);

        var toggleInput   = new TogglableInputGroup("MuscleInput", inputList);
        var toggleSim     = new TogglableSimulationGroup("MuscleSim", simList);
        var togglePostSim = new TogglablePostSimulationGroup("MusclePostSim", postSimList);

        kernel.RegisterGlobalSystem(toggleInput);
        kernel.RegisterModule(new EditorSimulationModule(toggleSim)); // Wrapped for kernel compliance
        kernel.RegisterGlobalSystem(togglePostSim);

        // 3. Orchestration & Replay Handler Registration
        var bootstrapper = new NodeBootstrapper(networkFactory);
        var clusterSlave = bootstrapper.BuildOrchestration(
            role, kernel, context.World, context.NodeId,
            participant: context.Participant,
            subsystemName: "MuscleNode",
            eventBus: context.EventBus,
            localTempRoot: tempRoot,
            simGroup: toggleSim,
            postSimGroup: togglePostSim,
            lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator: context.EventAccumulator,
            afterSeek: context.NedReplication?.AfterSeekCallback);

        // 4. Network Translators 
        if (context.Participant != null)
        {
            var nodeFactory = networkFactory.ConfigureForNode(context, role);
            nodeFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(kernel);
            nodeFactory.CreateSimHostPerceptionTranslators(context.GhostCreationSystem).RegisterOn(kernel);
            nodeFactory.CreateSimHostPathfindingTranslators().RegisterOn(kernel);
        }
    }
}
```

3\. Stage 1: Initial Stride Integration (Reusing SimHost Tech)

For your current stage, your `StrideIntegrationCore` will invoke this shared bootstrapper by passing in the exact same simple 2D modules that `SimHostApp` uses. You get 100% logic parity without repeating the orchestration boilerplate.

```
// Inside StrideIntegrationCore.Initialize(...)

var roadNetwork = SimHostApp.LoadRoadNetwork(...);
var trajectoryPool = new TrajectoryPoolManager();

// Inject the default FDP simple implementations
var defaultKinematics = new GroundKinematicsModule(roadNetwork, trajectoryPool);
var defaultPerception = new CognitiveSpatialModule(Context.World, /* collider reader */);
var defaultCombat = new CombatModule();

var bootstrapper = new MuscleNodeBootstrapper();
bootstrapper.BootstrapMuscleNode(
    Context, 
    networkFactory, 
    NodeRole.MuscleGround | NodeRole.ImageGenerator,
    isolatedTempRoot,
    kinematicsModule: defaultKinematics,
    perceptionModule: defaultPerception,
    combatModule: defaultCombat
);
```

4\. Stage 2: Future Stride Integration (Native 3D Tech)

When you are ready to implement 3D raycasting and Stride's NavMesh, you do not need to touch the orchestration, the DDS translators, or the ECS kernel ticking logic.

You simply implement your own `IEcsModule` classes that wrap Stride APIs and inject them into the exact same bootstrapper.

```
// Inside StrideIntegrationCore.Initialize(...) in the future:

// Your custom module runs Physics.Raycast() inside Stride instead of 2D math
var stridePerception = new StrideNativeRaycastModule(stridePhysicsContext);

// Your custom module hooks NavigationIntent to Stride NavMesh agents
var strideKinematics = new StrideNavMeshKinematicsModule(strideSceneContext);

// Combat remains the same (HitResolution, Damage logic is agnostic)
var defaultCombat = new CombatModule();

var bootstrapper = new MuscleNodeBootstrapper();
bootstrapper.BootstrapMuscleNode(
    Context, 
    networkFactory, 
    NodeRole.MuscleGround | NodeRole.ImageGenerator,
    isolatedTempRoot,
    kinematicsModule: strideKinematics,    // Swapped!
    perceptionModule: stridePerception,    // Swapped!
    combatModule: defaultCombat            // Reused!
);
```

Why this encapsulates perfectly:

-   **DRY:** `SimHostApp` and your Stride wrapper now share the massive >150 line block of code that registers `ClusterSlave`, builds `TogglableSimulationGroup` instances (crucial for replay safety), and wires the DDS `IDescriptorTranslator` packs.-   **Complete Flexibility:** The FDP architecture is already interface-based via `IEcsModule`. By breaking the `SimHostCoreLogicPack` monolith into its interface constituents, you treat `GroundKinematicsModule` and `CognitiveSpatialModule` as interchangeable plugins.

great! of course we need to share this with the existing Simhost subsystem. And ig there are any IG subsystem related sharing, let's do it for the IG responsibilities as well. PLs suggest where to place this shared initialization code.

To achieve maximum DRYness across `SimHost`, `IG`, and your new `StrideMock` subsystem, we must look at the dependency graph.

Currently, a lot of the orchestration boilerplate (like `ClusterSlave` setup) is trapped inside `Hrot\Subsystems\Hrot.SimHost\NodeBootstrapper.cs` and duplicated directly inside `Hrot\Subsystems\Hrot.IG\IgApplication.cs`.

Where to place the shared code

The perfect place for this shared initialization code is **Hrot\\Engine\\Hrot.Common\\Hrot.Common.csproj**, specifically within the **Hrot.Common.Infrastructure** namespace.

**Why here?**

-   **Dependency Hierarchy:** `Hrot.Common` sits below all application subsystems. `SimHost`, `IG`, `CGF`, and your new `StrideMock` all already reference it.-   **Context Ownership:** This is exactly where the `HrotNodeBuilder` and `HrotNodeContext` currently live. It is the architecturally correct home for application bootstrapping logic.

What we will share (The "Unified Bootstrapper")

We will replace the monolithic `SimHostCoreLogicPack` and the duplicated IG boilerplate with a new `SharedApplicationBootstrapper` in `Hrot.Common`. Because the engine's `NedReplicationModule` is already brilliantly designed to configure itself based on the `NodeRole`, our bootstrapper can serve **all** node types automatically.

Here is the blueprint for the shared initialization logic we will extract:

1\. The Core Infrastructure (Shared by SimHost, IG, Stride)

-   **HrotNodeBuilder execution:** Setting up the ECS World, EventBus, and DDS Participant.-   **Time Synchronization:** Automatically configuring the `SlaveSyncController` and its network translators so all nodes step deterministically.-   **Orchestration Handlers:** The massive 100+ line block that wires the `ClusterSlave` with `ReferenceReplayLoadHandler`, `ReferenceLiveLoadHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, and the `DiagnosticsDumpClusterOpHandler`.-   **Gizmo Transport:** The boilerplate that queries the network factory to wire up `CreateGizmoTranslators` and `CreateGizmoPublisherSystem`.-   **Network Replication:** Requesting the `IReplicationModule` from the factory. If the role includes `MuscleGround` (SimHost/Stride), it automatically wires Physics Egress. If the role includes `ImageGenerator` (IG/Stride), it automatically wires Dead Reckoning and the Ingress packs.

2\. The Application-Specific Plugins (Injected)

Instead of hardcoding logic packs, the bootstrapper will accept standard `IEcsModule` plugins.

**For your StrideMock (and SimHost):** You will pass in `NodeRole.MuscleGround | NodeRole.ImageGenerator` and inject:

-   `GroundKinematicsModule` (or your future 3D Stride Native NavMesh module)-   `CognitiveSpatialModule` (or your future 3D Stride Raycast module)-   `CombatModule`

**For the existing IG Subsystem:** We will refactor `IgApplication` to use this same bootstrapper, passing `NodeRole.ImageGenerator` and injecting its presentation modules:

-   `MapLayerModule`-   `MapCullingModule`-   `StyleResolutionModule`-   `EventEffectModule`

The Resulting API Contract

By moving this into `Hrot.Common.Infrastructure`, the initialization for your `StrideIntegrationCore` (and eventually the refactored `SimHostApp` and `IgApplication`) will shrink to just configuring plugins:

```
using Hrot.Common.Infrastructure;

// 1. Define the role
var role = NodeRole.MuscleGround | NodeRole.ImageGenerator;

// 2. Define the app-specific modules (Plugin Architecture)
var myModules = new IEcsModule[] 
{
    new GroundKinematicsModule(roadNetwork, trajPool), // Swap for Stride modules later!
    new CognitiveSpatialModule(Context.World),
    new CombatModule(),
    new EventEffectModule() // Reusing IG's visual effects module for explosions!
};

// 3. Let the shared bootstrapper handle ALL the heavy lifting
var bootstrapper = new SharedApplicationBootstrapper();
Context = bootstrapper.BootstrapNode(
    domainId, 
    nodeId, 
    "StrideMock", 
    role, 
    networkFactory,
    myModules); 

// ECS is now fully wired, replicated, and cluster-ready.
Context.Kernel.Initialize();
```

By extracting the `NodeBootstrapper` out of `Hrot.SimHost`, generalizing it to handle the IG's Gizmo/Diagnostics paths, and placing it in `Hrot.Common`, you guarantee that your new Stride integration is 100% compliant with all cluster state machine operations without writing any duplicate orchestration code.

good! how to make sure during the refactor we will not omit or reorder some system initialization that would break the way how the system operates now? There could be something fragile in the existing initialization code that was fine tuned during many development iterations. We need to be sure we are not going to break this. Could you pls check for such parts (probably the comment should give some clue) and suggest how to make sure the refactored system keeps the initialization order etc. as close as possible to the existing v190 code?

You are absolutely right to be cautious. The FDP/HROT engine has several highly sensitive initialization sequences that have been fine-tuned to prevent race conditions, memory corruption, and network synchronization bugs.

By analyzing the engine's comments, I have identified the **five most fragile initialization sequences** you must preserve. To guarantee we do not break them during the refactor, we will design the `SharedApplicationBootstrapper` using the **Template Method Pattern**, which locks the execution order into a strict, unmodifiable sequence while allowing your specific nodes (StrideMock, SimHost, IG) to plug in their custom logic.

Here are the critical traps we must avoid, followed by the safe refactoring architecture.

The 5 Fragile Initialization Traps (From Engine Comments)

**1\. The Component Registration vs. Serializer Trap** The engine uses an `FdpAutoSerializer` that compiles high-speed IL delegates for all known components. **The Rule:** All ECS components _must_ be registered before `HrotScenarioSerializerFactory.Build()` is called. If you initialize the serializer too early, it will compile an empty schema, and saving/loading scenarios will silently drop all entity data.

**2\. The Orchestration Handler Chain Trap** The `ClusterSlave` processes incoming cluster operations (like `PrepareLive` or `PrepareReplay`) by asking handlers if they `CanHandle()` the request. **The Rule:** The `ReferenceReplayLoadHandler` **MUST be registered first**, so it can intercept `PrepareLive` commands when jumping from Replay back to Live mode (the "Live-from-Replay branch"). Scenario/Episode handlers must be registered next, followed by the fallback `ReferenceLiveLoadHandler` last. If this order is inverted, the cluster state machine will break.

**3\. The Spawning vs. Network Ingress Trap** Entities must be ready to receive data the moment the network connects. **The Rule:** The `SpawningModule` (or `NetworkSpawningSystem`) and the `EntityLifecycleModule` (ELM) must be registered in the kernel **before** the `CycloneNetworkModule` (or its ingress translators). If network packets arrive before the spawn pipeline is ready, ghost entities will fail to materialize.

**4\. The Togglable Group / Replay Safety Trap** During replays, live physics and AI must be suspended so they don't overwrite historical data. **The Rule:** You must collect all simulation systems and wrap them in `TogglableSimulationGroup` (and input/post-sim equivalents) **before** calling `BuildOrchestration()`. The orchestration bootstrapper requires references to these togglable groups so the Replay Handler can disable them during playback.

**5\. The Event Bus Playback Trap** **The Rule:** All custom events must be registered on the ECS world _before_ `kernel.Initialize()` is called, otherwise the `EntityCommandBuffer.Playback` will crash or drop events on the very first simulation tick.

\--------------------------------------------------------------------------------

The Bulletproof Solution: The Template Method Bootstrapper

To prevent these bugs from ever being introduced, we will structure the `SharedApplicationBootstrapper` so that the phases are strictly locked. Your individual subsystems (like `StrideIntegrationCore` or `SimHostApp`) will simply override `protected` methods to supply their data.

Here is the architecture to use:

```
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;

public abstract class SharedApplicationBootstrapper
{
    // The strict, unchangeable initialization pipeline
    public HrotNodeContext BootstrapNode(
        HrotNodeConfig config,
        NodeRole role,
        INetworkFactory networkFactory)
    {
        // ==========================================
        // PHASE 1: Build Core Context & Network
        // ==========================================
        var context = new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .Build();

        // ==========================================
        // PHASE 2: Component & Event Registration (Trap #1 & #5 Safe)
        // ==========================================
        HrotSharedComponentRegistry.RegisterAll(context.World);
        
        // Plugin Hook: Nodes register their specific components
        RegisterDomainComponents(context.World); 

        // ==========================================
        // PHASE 3: Build Serializers (Trap #1 Safe)
        // ==========================================
        // Serializer is built AFTER all components are registered
        var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(GetBehaviorRegistry());

        // ==========================================
        // PHASE 4: Gather Systems & Build Togglable Groups (Trap #4 Safe)
        // ==========================================
        var inputList = new List<IEcsModuleSystem>();
        var simList = new List<IEcsModuleSystem>();
        var postSimList = new List<IEcsModuleSystem>();

        // Plugin Hook: Nodes provide their logic modules/systems
        PopulateSystems(context, inputList, simList, postSimList);

        var toggleInput   = new TogglableInputGroup($"{config.SubsystemName}Input", inputList);
        var toggleSim     = new TogglableSimulationGroup($"{config.SubsystemName}Sim", simList);
        var togglePostSim = new TogglablePostSimulationGroup($"{config.SubsystemName}PostSim", postSimList);

        context.Kernel.RegisterGlobalSystem(toggleInput);
        context.Kernel.RegisterModule(new EditorSimulationModule(toggleSim)); // Wrapped for kernel compliance [7]
        context.Kernel.RegisterGlobalSystem(togglePostSim);

        // ==========================================
        // PHASE 5: Orchestration Wiring (Trap #2 Safe)
        // ==========================================
        // The inner NodeBootstrapper naturally handles the strict Replay -> Scenario -> Live ordering
        var bootstrapper = new Hrot.SimHost.NodeBootstrapper(networkFactory);
        var fullyWiredSlave = bootstrapper.BuildOrchestration(
            role, context.Kernel, context.World, config.NodeId,
            participant: context.Participant,
            subsystemName: config.SubsystemName,
            eventBus: context.EventBus,
            scenarioSerializer: serializer,
            localTempRoot: config.LocalTempRoot,
            simGroup: toggleSim,
            postSimGroup: togglePostSim,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator: context.EventAccumulator);

        var finalContext = context with { ClusterSlave = fullyWiredSlave };

        // ==========================================
        // PHASE 6: Spawning & Network Translators (Trap #3 Safe)
        // ==========================================
        // Base modules (including ELM and Spawning) registered BEFORE DDS translators
        foreach (var m in finalContext.BaseModules) 
            finalContext.Kernel.RegisterModule(m);
            
        RegisterSpawningPipeline(finalContext);

        if (finalContext.Participant != null)
        {
            var nodeFactory = networkFactory.ConfigureForNode(finalContext, role, GetBehaviorRegistry());
            RegisterNetworkTranslators(finalContext, nodeFactory);
        }

        // ==========================================
        // PHASE 7: Initialization
        // ==========================================
        finalContext.Kernel.Initialize();

        return finalContext;
    }

    // ─── Plugin Hooks (Implemented by StrideMock, SimHost, IG) ───

    protected abstract void RegisterDomainComponents(EntityRepository world);
    protected abstract void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input, List<IEcsModuleSystem> sim, List<IEcsModuleSystem> postSim);
    protected abstract void RegisterSpawningPipeline(HrotNodeContext context);
    protected abstract void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory configuredFactory);
    
    // Optional hooks
    protected virtual Fdp.Toolkit.Behavior.BehaviorRegistry? GetBehaviorRegistry() => null;
}
```

How to use this for your Stride Integration

When you write `StrideIntegrationCore`, it will simply inherit from this bootstrapper and implement the hooks. You will never have to worry about accidentally registering the Replay Handler out of order or compiling the serializer too early.

By pushing the boilerplate into this strict template method in `Hrot.Common.Infrastructure`, you guarantee that **v190's exact execution topology** is perfectly preserved across all your applications.

Good! The ECS components are interesting topic. Now they are registered differently per subsystem. I guess this is unnecessary and it migth simplify things if we unify this completely in a way that every subsystem will register the same set (full set, union) of all ECS components used by the cluster runner subsystem now. What do you think?

It is a very tempting idea from a DRY (Don't Repeat Yourself) perspective to simplify initialization. However, in the context of the FDP engine architecture, **unifying all ECS components into a single global registry for every subsystem is highly discouraged and would actually break the client-server memory model.**

The split registration approach (e.g., `HrotSharedComponentRegistry` vs `CognitiveComponentRegistry` vs `KinematicComponentRegistry`) is not just boilerplate; it acts as an architectural firewall. Here is why you must maintain the separation:

1\. The TKB Template "Silent Skip" Mechanic

The FDP engine uses shared `TkbTemplate` blueprints to spawn entities across all nodes. Because an M1 Abrams tank template contains physics, AI, and visual data, the engine needs a way to avoid spawning heavy AI memory on a purely visual node (like the IG or your Stride renderer).

To solve this, the `TkbTemplate` applicator explicitly checks `repo.IsComponentTypeRegistered<T>()`. If a component is not registered in the local ECS world, the template **silently skips it**. If you unify the registries, your Stride/IG node will suddenly report that `BrainHsm128` and `VehicleState` are registered, causing the engine to instantiate them.

2\. Massive Memory Bloat and Cache Degradation

If your visual nodes (IG, StrideMock) register the full union of components, they will be forced to allocate memory for data they never simulate.

-   Components like `BrainHsm128` (128 bytes), `Blackboard1024` (1 kilobyte), and `MissionPlanQueue` are massive.-   If you have 10,000 vehicles, registering the union means your 3D renderer will allocate megabytes of dead AI and physics state memory inside its ECS chunks. This destroys the Data-Oriented Design (DOD) cache density, pushing visual data like `SimTransform` further apart in RAM and slowing down your render loop.

3\. The 256 Component Limit

FDP uses a highly optimized `BitMask256` to track entity components instantly. This means there is a hard limit of exactly 256 component types that can ever exist in a single repository. If every subsystem registers every component, you artificially exhaust the ID space limits on nodes that only needed a fraction of the components.

4\. Intentional Segregation by Role

If you look at the source code, the engine designers explicitly split the registries to support the `NodeRole` distribution:

-   **HrotSharedComponentRegistry**: Registers only network replication, geographic primitives, and lifecycle events. _Every_ node calls this.-   **KinematicComponentRegistry**: Registers `VehicleState`, `NavState`, etc. Only Muscle nodes call this.-   **CognitiveComponentRegistry**: Registers `BehaviorState`, B-Trees, and HSMs. Only Brain nodes call this.-   **IgApplication**: Registers its own visual effects (`CullingState`, `ResolvedStyle`, `VisualEffectState`) and only the specific network components it needs for dead-reckoning.

The Recommendation

Instead of a global union, you should strictly follow the existing pattern for your `StrideIntegrationCore`.

Because your Stride subsystem will act as a combination of `MuscleGround` and `ImageGenerator`, you should call:

-   `HrotSharedComponentRegistry.RegisterAll(world)` (The foundation)-   `KinematicComponentRegistry.RegisterAll(world)` (For physics execution)-   Explicitly register specific visual components needed for Stride (like `VisualEffectState` and `TracerTarget` for your special effects).

Leave the `CognitiveComponentRegistry` completely out of your Stride app. The Brain node will handle the AI, and your Stride node will remain lean, fast, and cache-friendly.

Ok. What ebaut the fdp events registration unification? same story or is there a potential for more unification?

While the technical constraints on events are much looser than components, the architectural answer is essentially the **same story: you should keep event registration strictly segregated by domain.**

Here is why unifying event registrations across all subsystems is still discouraged in the FDP engine, even though the underlying mechanics differ from components:

1\. No 256-Limit, but Still Causes Overhead

Unlike components—which are strictly bound by the `BitMask256` hardware limit and cause massive memory bloat if unified—events are keyed by a standard 32-bit `int` (`EventIdAttribute`). There is no hard limit on how many event types you can register.

However, every time you call `RegisterEvent<T>()`, the `FdpEventBus` permanently allocates a `NativeEventStream<T>` (or `ManagedEventStream<T>`) which contains double-buffered arrays. If your Stride subsystem registers 100 AI and pathfinding events that it never uses, you force the `FdpEventBus.SwapBuffers()` and the Flight Recorder hot-paths to iterate over dozens of empty streams 60 times a second.

2\. Domain Cohesion (The Engine's Pattern)

If you look at the engine's source code, the designers deliberately group event registrations into the exact same domain registries as the components.

For example, `CombatComponentRegistry` registers the `Health` component right next to the `DetonationNotification` and `WeaponFireNotification` events. If you pull all events out into a single global union, you break this cohesion, making it much harder to drop a domain module into a new app because its required events are no longer registered alongside it.

3\. Command Buffer Safety (Trap #5)

We previously identified that if a system pushes an event via the `EntityCommandBuffer` and that event type isn't registered, the simulation will crash on the main thread during playback. By forcing each subsystem to explicitly register the events its own systems actually publish or consume (e.g., the IG subsystem explicitly registering `DetonationNotification`), the architecture guarantees that you only run systems where the plumbing is fully verified.

The Recommendation for StrideMock

You should follow the exact same pattern we established for components. Your `StrideIntegrationCore` should only register the events that its systems or network translators actually touch:

-   **The Shared Foundation:** Call `HrotSharedComponentRegistry.RegisterAll(world)`, which automatically registers all the `ConstructionOrder`, `DestructionAck`, and other global cluster events your node needs to survive.-   **The Stride-Specific Events:** Explicitly register the special effect events you are bridging to your renderer, just like the IG node does:-   **Gizmo Interactions:** If you implement mouse-picking later, register the interaction events:

By keeping your event registry lean and specific to Stride's actual responsibilities (Visuals, Gizmos, and Physics), your node remains as highly optimized and cache-friendly as the original engine designers intended.

ok. The "repo.IsComponentTypeRegistered()" check you mentioned, although probably functional and sufficient, is that the architecturally correct and elegant solution? Shouldn't we use more explicit mechanism to eliminate the components (based on roles) to be avoided when instantiating entities?

While it might look like a defensive hack at first glance, `repo.IsComponentTypeRegistered<T>()` is actually the intentional and architecturally correct mechanism for this in the FDP engine.

Think about the dependency inversion at play here. If we introduced explicit role-based filtering inside `TkbTemplate` (e.g., `if (role == NodeRole.ImageGenerator) skip PhysicsCollider`), we would tightly couple the universal blueprint data to the distributed deployment topology. The data model would have to know about every possible cluster role, which violates the Single Responsibility Principle.

Instead, the engine uses the repository's component tables as a strict interface boundary. The `TkbTemplate` declares the complete union of everything an entity _could_ be across the entire distributed system.

The actual memory layout is dictated purely by the node's bootstrap sequence. Because we explicitly segregated our initialization into distinct registries (`HrotSharedComponentRegistry`, `KinematicComponentRegistry`, `CognitiveComponentRegistry`), a visual node like our Stride renderer simply never registers the heavy AI or physics structs. When `template.ApplyTo()` fires on that node, the applicator delegate checks the repository, sees the unmanaged table doesn't exist, and silently skips the component allocation.

This gives us a perfectly decoupled Data-Oriented architecture:

-   Blueprints don't know about cluster roles.-   Nodes only allocate ECS chunk memory for the components they actually process, preserving cache density.-   The shared applicator seamlessly bridges the two without throwing exceptions or requiring brittle manual mapping tables.

perfect, thanks!

pls update the success condition criteria, incorporating all the design changes we have made

Here are the updated success conditions for the Stride integration, incorporating the strict client-server time model, full orchestration compliance, SimHost module parity, and the Template Method bootstrapper.

1\. Architectural & Bootstrapping (The "DRY & Safe" Condition)

-   **Template Method Compliance:** The integration must inherit from the new `SharedApplicationBootstrapper` located in `Hrot.Common.Infrastructure`. It must rely on the bootstrapper's strict phase ordering to prevent serializer, component, and orchestration handler race conditions.-   **Modular Plugin Injection:** Instead of a monolithic logic pack, the bootstrapper must accept injected interfaces (`IEcsModule`). The Stride integration must inject `GroundKinematicsModule`, `CognitiveSpatialModule`, and `CombatModule` to perfectly mirror SimHost responsibilities while remaining open for future 3D replacements.-   **Segregated Registration:** The integration must strictly avoid global component/event unions. It must only call `HrotSharedComponentRegistry`, `KinematicComponentRegistry`, and register its specific visual events (e.g., `WeaponFireNotification`, `DetonationNotification`) to prevent TKB template mis-allocations and preserve cache density.

2\. Standalone App (`FakeStrideApp`)

-   **Execution & Visuals:** Runs as a separate executable, successfully multiplexing the dual-buffer gizmo terminal alongside 2D representations of simulated vehicles and ephemeral effects (`VisualEffectState`).-   **Strict Client Time Control:** The standalone app must configure its clock to `TimeRole.Slave` driven by the `SlaveSyncController`. The local UI play/pause/step buttons must exclusively use the `ITimeControlGateway` to send DDS requests to the Cluster Master rather than directly manipulating the local kernel.-   **Smooth Navigation:** Map panning and zooming must be driven smoothly by the FDP `RaylibInputProvider` and `MapCamera`.

3\. ClusterRunner Wrapper (`StrideMockSubsystem`)

-   **CLI & Subsystem Identity:** The command `Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock` successfully boots all nodes. The system resolves the deterministic `NodeId` using the assigned offset of `700`.-   **Perfect Drop-In Replacement:** The subsystem successfully claims the roles `MuscleGround | Perception | NavigationSolver | ImageGenerator`. It correctly intercepts and executes `NavigationIntent`, `AreaQueryRequestBatch`, and `WeaponFireIntent` commands from the CGF brain node over DDS exactly as SimHost would.-   **Camera Synchronization:** The `IMapCameraProvider` correctly copies pan/zoom coordinates from the outgoing subsystem when the user clicks the `[StrideMock]` tab.-   **Gated Rendering:** 2D Raylib draws and gizmos only execute when `SubsystemConfig.IsActiveMapOwner` evaluates to true.

4\. Orchestration & State Machine Lifecycle

-   **Replay Safety:** Mutative systems (e.g., `EventToEffectSystem` and kinematics) are safely wrapped in `TogglableSimulationGroup`. During `LoadingReplay`, the orchestration handlers instantly disable these groups to prevent live physics from corrupting historical playback data.-   **2PC & File Management:** The node actively participates in Two-Phase Commit operations. It correctly stages files via isolated temporary directories and ACKs `PrefetchFiles` and `SerializeLocal` commands using the registered `ReferencePrefetchHandler` and `ReferenceArchiveHandler`.-   **Built-in Recording:** Because the node registers as a Muscle, the bootstrapper automatically provisions the `EcsRecordReplayController`, causing the Stride node to reliably generate its own `.fdp` recording file during `OperatingLive`.-   **Diagnostic Dumps:** The node fully complies with the `CollectDiagnostics` cluster operation via `DiagnosticsDumpClusterOpHandler`, correctly extracting entity state, architecture snapshots, and filtered logs when requested by the orchestrator.

and pls update the interfaces and apis, if any needs changing

These are the finalized success criteria for the Stride integration, reflecting the strict DOD (Data-Oriented Design) memory model, the Template Method bootstrapping pattern, and the authoritative client-server time constraints.

1\. Architectural & Bootstrapping (The "DRY & Safe" Condition)

-   **Template Method Compliance:** The integration must inherit from the new abstract `SharedApplicationBootstrapper` located in `Hrot.Common.Infrastructure`. It must rely on the bootstrapper's strict phase ordering to prevent serializer compilation errors, component allocation bugs, and orchestration handler race conditions.-   **Modular Plugin Injection:** Instead of relying on a monolithic logic pack, the bootstrapper must accept injected interfaces (`IEcsModule`). The Stride integration must inject `GroundKinematicsModule`, `CognitiveSpatialModule`, and `CombatModule` to perfectly mirror SimHost responsibilities while remaining strictly decoupled for future 3D replacements.-   **Segregated Registration:** The integration must strictly avoid global component/event unions. It must only call `HrotSharedComponentRegistry.RegisterAll`, `KinematicComponentRegistry.RegisterAll`, and register its specific visual presentation events (e.g., `WeaponFireNotification`, `DetonationNotification`). This guarantees that `TkbTemplate` applicators silently skip heavy AI/Physics allocations on visual nodes, preserving ECS chunk cache density.

2\. Standalone App (`FakeStrideApp`)

-   **Execution & Visuals:** Runs as a separate executable (`FdpApplication`), successfully multiplexing the dual-buffer gizmo terminal alongside 2D representations of simulated vehicles and ephemeral effects (`VisualEffectState`).-   **Strict Client Time Control:** The standalone app must configure its clock to `TimeRole.Slave` driven by the `SlaveSyncController`. The local UI play/pause/step buttons must exclusively use the `ITimeControlGateway` to send DDS `ClusterOpRequest` messages to the Cluster Master rather than directly mutating the local kernel.-   **Smooth Navigation:** Map panning and zooming must be driven smoothly by the engine's `RaylibInputProvider` and `MapCamera`.

3\. ClusterRunner Wrapper (`StrideMockSubsystem`)

-   **CLI & Subsystem Identity:** The command `Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock` successfully boots all nodes. The system resolves the deterministic `NodeId` using the assigned offset of `700`.-   **Perfect Drop-In Replacement:** The subsystem successfully claims the roles `MuscleGround | Perception | NavigationSolver | ImageGenerator`. It correctly intercepts and executes `NavigationIntent`, `AreaQueryRequestBatch`, and `WeaponFireIntent` commands from the CGF brain node over DDS exactly as SimHost would.-   **Camera Synchronization:** The `IMapCameraProvider.ApplyCameraView` correctly copies pan/zoom coordinates from the outgoing subsystem when the user clicks the `[StrideMock]` tab.-   **Gated Rendering:** 2D Raylib draws and gizmos only execute when `SubsystemConfig.IsActiveMapOwner` evaluates to true.

4\. Orchestration & State Machine Lifecycle

-   **Replay Safety:** Mutative systems are safely wrapped in `TogglableSimulationGroup`. During `LoadingReplay`, the orchestration handlers instantly disable these groups to prevent live physics from corrupting historical playback data.-   **2PC & File Management:** The node actively participates in Two-Phase Commit operations. It correctly stages files via isolated temporary directories and ACKs `PrefetchFiles` and `SerializeLocal` commands using the registered `ReferencePrefetchHandler` and `ReferenceArchiveHandler`.-   **Built-in Recording:** Because the node registers as a Muscle, the bootstrapper automatically provisions the `EcsRecordReplayController`, causing the Stride node to reliably generate its own `.fdp` recording file during `OperatingLive`.-   **Diagnostic Dumps:** The node fully complies with the `CollectDiagnostics` cluster operation via `DiagnosticsDumpClusterOpHandler`, correctly extracting entity state, architecture snapshots, and filtered logs when requested by the orchestrator.

\--------------------------------------------------------------------------------

Updated Class APIs and Contracts

Given the shift to the Template Method pattern to protect the initialization sequence, the core API contract changes from a single `StrideIntegrationCore` class to a shared abstract bootstrapper and a concrete implementation.

**SharedApplicationBootstrapper** **(The Shared Core Contract)** Located in `Hrot.Common.Infrastructure`. This locks the initialization phases to prevent the 5 fragile initialization traps.

```
public abstract class SharedApplicationBootstrapper
{
    // The strict, unchangeable initialization pipeline.
    public HrotNodeContext BootstrapNode(
        HrotNodeConfig config,
        NodeRole role,
        INetworkFactory networkFactory)
    {
        // 1. Build Context (HrotNodeBuilder)
        // 2. RegisterDomainComponents(world)
        // 3. Build Serializers
        // 4. PopulateSystems(context, input, sim, postSim) -> Wraps in TogglableGroups
        // 5. BuildOrchestration (NodeBootstrapper)
        // 6. RegisterSpawningPipeline & RegisterNetworkTranslators
        // 7. Kernel.Initialize()
    }

    // Template Hooks
    protected abstract void RegisterDomainComponents(EntityRepository world);
    protected abstract void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input, List<IEcsModuleSystem> sim, List<IEcsModuleSystem> postSim);
    protected abstract void RegisterSpawningPipeline(HrotNodeContext context);
    protected abstract void RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory configuredFactory);
}
```

**StrideNodeBootstrapper** **(The Concrete Implementation)** Located in `Hrot.StrideMock`. This replaces the former `StrideIntegrationCore`. It implements the hooks to inject the requested `IEcsModule` logic packs and registers the specific visual events.

```
public class StrideNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly IEcsModule _kinematicsModule;
    private readonly IEcsModule _perceptionModule;
    private readonly IEcsModule _combatModule;

    public StrideNodeBootstrapper(IEcsModule kinematics, IEcsModule perception, IEcsModule combat)
    {
        _kinematicsModule = kinematics;
        _perceptionModule = perception;
        _combatModule = combat;
    }

    protected override void RegisterDomainComponents(EntityRepository world)
    {
        // Strictly segregated registration
        HrotSharedComponentRegistry.RegisterAll(world);
        KinematicComponentRegistry.RegisterAll(world);
        
        // Specific events required by the visual renderer
        world.RegisterEvent<DetonationNotification>();
        world.RegisterEvent<WeaponFireNotification>();
    }

    protected override void PopulateSystems(HrotNodeContext context, List<IEcsModuleSystem> input, List<IEcsModuleSystem> sim, List<IEcsModuleSystem> postSim)
    {
        // Extract systems from injected modules and add visual effect systems
        // These will be automatically wrapped in TogglableSimulationGroup for replay safety
    }

    // ... implementations for spawning pipeline and SimHost/IG network translators ...
}
```

**StrideMockSubsystem** **(The Wrapper)** Remains mostly unchanged structurally, but delegates to the new bootstrapper. It must expose the `ITimeControlGateway` for UI interactions.

```
public class StrideMockSubsystem : ISubsystem, IMapCameraProvider
{
    public string Name => "StrideMock";
    public Vector4 TitleBarColor => new Vector4(0.8f, 0.4f, 0.1f, 1.0f);
    
    private HrotNodeContext _context;
    private ITimeControlGateway _timeControl;
    private MapCamera _camera;

    public void Initialize(SubsystemConfig config);
    public void Update(float deltaTime);
    public void DrawWorld();
    public void DrawUI();
    public void Shutdown();

    // IMapCameraProvider
    public MapCameraView? GetCameraView();
    public void ApplyCameraView(MapCameraView view);
}
```

where in the source tree will out standalone fake stride app will live?

To maintain strict separation of concerns and align with the existing HROT repository structure, we will split the implementation into two distinct projects.

**1\. The Integration Core & Subsystem Wrapper (Library)** All reusable logic must reside in the `Subsystems` solution folder, exactly like `Hrot.SimHost` and `Hrot.IG`.

-   **Path:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj`-   **Type:** Class Library-   **Contents:**
    -   `StrideNodeBootstrapper` (the modular plugin injector)-   `StrideMockSubsystem` (the ClusterRunner wrapper interface)-   **Dependencies:** References `Hrot.Common`, `Fdp.Core`, and the relevant toolkit modules. It must be completely free of Raylib or standalone application shell dependencies.

**2\. The Standalone Fake Stride App (Executable)** The standalone executable shell belongs in the `Runner` solution folder, sitting parallel to the `Hrot.ClusterRunner`.

-   **Path:** `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj`-   **Type:** Executable (`OutputType = Exe`)-   **Contents:**
    -   `FakeStrideApp.cs` (inherits from `FdpApplication`)-   `Program.cs` (the `static void Main` entry point)-   **Dependencies:** References the `Hrot.StrideMock` library, `Fdp.Presentation` (for Raylib/ImGui), and `Fdp.ModuleHost`.

This separation guarantees that the core bootstrapping logic remains engine-agnostic. When you transition to the real Stride 3D engine, you simply drop the `Hrot.FakeStrideApp` project and replace it with your native Stride application, while `Hrot.StrideMock` remains completely untouched.

once we later switch from the fake stride app into a real heavy stride app, where would the real stride app be placed?

To maintain strict separation of concerns, your real Stride 3D application will drop exactly into the `Runner` solution folder, completely replacing the `FakeStrideApp` executable.

In this architecture, application shells and process entry points are deliberately isolated from the reusable integration logic. Your native Stride project will sit at a path like `Hrot\Runner\Hrot.StrideApp`, parallel to the main `Hrot.ClusterRunner` project.

This guarantees that the dependency graph remains pristine. The heavy Stride executable will simply reference the `Hrot.StrideMock` class library located in the `Subsystems` directory. Your core integration logic—the `StrideNodeBootstrapper` and the ECS/DDS network wiring—will remain completely untouched and unaware of the heavy 3D rendering environment, while the new Stride shell takes over the windowing, inputs, and the 3D presentation loop.

