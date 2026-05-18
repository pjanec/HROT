# Module System Architecture

## Overview

The **Module System** is FDP's plugin architecture for extending simulation capabilities. Modules encapsulate related functionality (replication, time coordination, lifecycle management) and expose it through a uniform interface. This pattern enables:
- Compositional simulation design (mix-and-match modules)
- Hot-swappable functionality (add/remove modules at runtime)
- Clear dependency management (module initialization order)
- Centralized lifecycle coordination

**Key Projects**:
- [ModuleHost.Core](../modulehost/ModuleHost.Core.md): `IModule`, `IModuleSystem`, `ModuleHost` orchestrator
- All Toolkits: `ReplicationLogicModule`, `TimeCoordinationModule`, `LifecycleModule`, etc.
- [NetworkDemo](../examples/Fdp.Examples.NetworkDemo.md): Multi-module composition example

---

## Conceptual Model

### Problem Space

Distributed simulations require cross-cutting functionality:
- **Network Replication**: Synchronize entities across nodes
- **Time Coordination**: Align simulation clocks
- **Entity Lifecycle**: Spawn/despawn coordination
- **Recording/Replay**: Capture and playback sessions

**Challenge**: How do we organize this functionality without tight coupling?

**Solution**: Module System - plugins that register systems, events, and snapshot providers.

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                       ModuleHost                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │         Module Lifecycle Orchestrator                 │   │
│  │  Initialize → Register Systems/Events → Start         │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  Registered Modules:                                         │
│  ┌──────────────────────┐  ┌──────────────────────┐        │
│  │ NetworkModule        │  │ TimeModule           │        │
│  │ ├─ GhostSystem       │  │ ├─ SlaveTimeCtrl     │        │
│  │ ├─ SmartEgressSystem │  │ ├─ TimeSyncSystem    │        │
│  │ └─ TransportSystem   │  │ └─ TimeEventPublisher│        │
│  └──────────────────────┘  └──────────────────────┘        │
│                                                              │
│  ┌──────────────────────┐  ┌──────────────────────┐        │
│  │ ReplicationModule    │  │ LifecycleModule      │        │
│  │ ├─ ReplicaStateSync  │  │ ├─ SpawnSystem       │        │
│  │ ├─ OwnershipTracker  │  │ ├─ DespawnSystem     │        │
│  │ └─ DeltaCompressor   │  │ └─ LifecycleEvents   │        │
│  └──────────────────────┘  └──────────────────────┘        │
└─────────────────────────────────────────────────────────────┘
        │
        │ Systems registered to global scheduler
        ▼
┌─────────────────────────────────────────────────────────────┐
│                  Global System Scheduler                     │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ PreSimulation Phase (Phase 5)                        │   │
│  │  → TimeSyncSystem                                     │   │
│  │  → GhostCreationSystem                               │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ Simulation Phase (Phase 10) - NEVER RUN              │   │
│  └──────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ PostSimulation Phase (Phase 15)                      │   │
│  │  → SmartEgressSystem                                 │   │
│  │  → LifecycleSyncSystem                               │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## Core Interfaces

### IModule

```csharp
namespace ModuleHost.Core
{
    /// <summary>
    /// Base interface for all modules.
    /// Modules encapsulate related functionality and register it with the host.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Unique module identifier (e.g., "ReplicationModule")
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Initialize module. Called once before systems are registered.
        /// Use this to: 1) Validate configuration
        ///              2) Allocate resources
        ///              3) Register dependencies
        /// </summary>
        void Initialize(IModuleContext context);
        
        /// <summary>
        /// Register systems/events/snapshot providers.
        /// Called after Initialize, before Start.
        /// </summary>
        void RegisterComponents(IComponentRegistry registry);
        
        /// <summary>
        /// Start module. Called after all modules initialized.
        /// Use this to: 1) Start background threads
        ///              2) Open network connections
        ///              3) Subscribe to events
        /// </summary>
        void Start();
        
        /// <summary>
        /// Stop module. Called during shutdown.
        /// Use this to: 1) Close connections
        ///              2) Flush pending data
        ///              3) Dispose resources
        /// </summary>
        void Stop();
        
        /// <summary>
        /// Dispose unmanaged resources.
        /// </summary>
        void Dispose();
    }
}
```

### IModuleSystem

```csharp
namespace ModuleHost.Core
{
    /// <summary>
    /// System owned by a module. Executes once per frame.
    /// </summary>
    public interface IModuleSystem
    {
        /// <summary>
        /// System name (for diagnostics)
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Execution phase (PreSimulation, Simulation, PostSimulation)
        /// </summary>
        SystemPhase Phase { get; }
        
        /// <summary>
        /// Priority within phase (lower = earlier execution)
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// Execute system logic.
        /// Called once per frame by global scheduler.
        /// </summary>
        void Execute(ISimulationView view, float deltaTime);
    }
}
```

### IModuleContext

```csharp
namespace ModuleHost.Core
{
    /// <summary>
    /// Context provided to modules during initialization.
    /// Grants access to shared resources and configuration.
    /// </summary>
    public interface IModuleContext
    {
        /// <summary>
        /// Register a system with the global scheduler.
        /// </summary>
        void RegisterSystem(IModuleSystem system);
        
        /// <summary>
        /// Register an event handler.
        /// </summary>
        void RegisterEventHandler<TEvent>(Action<TEvent> handler)
            where TEvent : struct;
        
        /// <summary>
        /// Register a snapshot provider (for recording/replay).
        /// </summary>
        void RegisterSnapshotProvider(ISnapshotProvider provider);
        
        /// <summary>
        /// Get configuration section for this module.
        /// </summary>
        IConfiguration GetConfiguration(string sectionName);
        
        /// <summary>
        /// Access to ECS world (for initialization only, not per-frame).
        /// </summary>
        IWorld World { get; }
    }
}
```

---

## Module Lifecycle

### Initialization Sequence

```
┌────────────────────────────────────────────────────────────┐
│                  ModuleHost Startup                         │
└────────────────────────────────────────────────────────────┘
                           │
                           ▼
        ┌──────────────────────────────────────┐
        │ 1. Create Modules                    │
        │    var modules = new IModule[]       │
        │    {                                 │
        │        new NetworkModule(),          │
        │        new ReplicationModule(),      │
        │        new TimeModule(),             │
        │        new LifecycleModule()         │
        │    };                                │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │ 2. Initialize (Sequential)           │
        │    foreach (var module in modules)   │
        │    {                                 │
        │        module.Initialize(context);   │
        │    }                                 │
        │                                      │
        │  Each module:                        │
        │  - Validates configuration           │
        │  - Allocates resources               │
        │  - Checks dependencies               │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │ 3. Register Components (Sequential)  │
        │    foreach (var module in modules)   │
        │    {                                 │
        │        module.RegisterComponents(    │
        │            componentRegistry);       │
        │    }                                 │
        │                                      │
        │  Each module:                        │
        │  - Registers systems                 │
        │  - Registers events                  │
        │  - Registers snapshot providers      │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │ 4. Sort Systems by Phase/Priority    │
        │    var sortedSystems = systems       │
        │        .OrderBy(s => s.Phase)        │
        │        .ThenBy(s => s.Priority)      │
        │        .ToList();                    │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │ 5. Start (Sequential)                │
        │    foreach (var module in modules)   │
        │    {                                 │
        │        module.Start();               │
        │    }                                 │
        │                                      │
        │  Each module:                        │
        │  - Starts background threads         │
        │  - Opens network connections         │
        │  - Subscribes to events              │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │ 6. Simulation Loop                   │
        │    while (running)                   │
        │    {                                 │
        │        foreach (var sys in sorted)   │
        │        {                             │
        │            sys.Execute(view, dt);    │
        │        }                             │
        │    }                                 │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │ 7. Shutdown (Reverse Order)          │
        │    foreach (var module in reversed)  │
        │    {                                 │
        │        module.Stop();                │
        │        module.Dispose();             │
        │    }                                 │
        └──────────────────────────────────────┘
```

### Dependency Resolution

Modules declare dependencies via initialization order:

**Example: Dependency Graph**
```
TimeModule (no dependencies)
    │
    ├─ Required by: NetworkModule
    └─ Required by: ReplicationModule

NetworkModule (depends on TimeModule)
    │
    └─ Required by: ReplicationModule

ReplicationModule (depends on NetworkModule + TimeModule)

LifecycleModule (depends on NetworkModule)
```

**Initialization Order**:
```csharp
var modules = new IModule[]
{
    new TimeModule(),           // 1. No dependencies
    new NetworkModule(),        // 2. Depends on TimeModule
    new ReplicationModule(),    // 3. Depends on Network + Time
    new LifecycleModule()       // 4. Depends on Network
};

// ModuleHost initializes in array order
foreach (var module in modules)
{
    module.Initialize(context); // Sequential, respects dependencies
}
```

---

## Concrete Examples

### Example 1: ReplicationLogicModule

```csharp
public class ReplicationLogicModule : IModule
{
    private readonly ReplicationConfig _config;
    private GhostCreationSystem _ghostSystem;
    private SmartEgressSystem _egressSystem;
    private OwnershipTrackingSystem _ownershipSystem;
    
    public string Name => "ReplicationModule";
    
    public ReplicationLogicModule(ReplicationConfig config)
    {
        _config = config;
    }
    
    public void Initialize(IModuleContext context)
    {
        // Validate configuration
        if (_config.MaxGhosts <= 0)
            throw new ArgumentException("MaxGhosts must be > 0");
        
        // Create systems (don't register yet)
        _ghostSystem = new GhostCreationSystem(_config);
        _egressSystem = new SmartEgressSystem(_config);
        _ownershipSystem = new OwnershipTrackingSystem();
    }
    
    public void RegisterComponents(IComponentRegistry registry)
    {
        // Register components
        registry.Register<GhostComponent>();
        registry.Register<NetworkIdComponent>();
        registry.Register<OwnershipComponent>();
        
        // Register systems
        registry.RegisterSystem(_ghostSystem);        // PreSimulation, Priority 20
        registry.RegisterSystem(_egressSystem);       // PostSimulation, Priority 10
        registry.RegisterSystem(_ownershipSystem);    // PostSimulation, Priority 5
        
        // Register event handlers
        registry.RegisterEventHandler<OwnershipChangeEvent>(OnOwnershipChange);
        
        // Register snapshot provider (for recording/replay)
        registry.RegisterSnapshotProvider(new ReplicationSnapshotProvider());
    }
    
    public void Start()
    {
        // Initialize networking (if needed)
        _ghostSystem.Start();
        _egressSystem.Start();
    }
    
    public void Stop()
    {
        // Flush pending network messages
        _egressSystem.Flush();
        
        // Close connections
        _ghostSystem.Stop();
    }
    
    public void Dispose()
    {
        _ghostSystem?.Dispose();
        _egressSystem?.Dispose();
        _ownershipSystem?.Dispose();
    }
    
    private void OnOwnershipChange(OwnershipChangeEvent evt)
    {
        // Handle ownership transfer
        _ownershipSystem.HandleTransfer(evt.EntityId, evt.NewOwner);
    }
}
```

### Example 2: TimeCoordinationModule

```csharp
public class TimeCoordinationModule : IModule
{
    private readonly TimeConfig _config;
    private ITimeController _timeController;
    private TimeSyncSystem _syncSystem;
    private TimeEventPublisher _eventPublisher;
    
    public string Name => "TimeModule";
    
    public void Initialize(IModuleContext context)
    {
        // Choose time controller based on role
        if (_config.IsMaster)
        {
            _timeController = new MasterTimeController(_config);
        }
        else
        {
            _timeController = new SlaveTimeController(_config);
        }
        
        _syncSystem = new TimeSyncSystem(_timeController);
        _eventPublisher = new TimeEventPublisher(_timeController);
    }
    
    public void RegisterComponents(IComponentRegistry registry)
    {
        // Register components
        registry.Register<TimeComponent>();
        registry.Register<TimeSyncComponent>();
        
        // Register systems
        registry.RegisterSystem(_syncSystem);          // PreSimulation, Priority 0 (FIRST!)
        registry.RegisterSystem(_eventPublisher);      // PostSimulation, Priority 100
        
        // Register snapshot provider
        registry.RegisterSnapshotProvider(new TimeSnapshotProvider(_timeController));
    }
    
    public void Start()
    {
        _timeController.Start();
    }
    
    public void Stop()
    {
        _timeController.Stop();
    }
    
    public void Dispose()
    {
        _timeController?.Dispose();
    }
}
```

---

## System Registration & Execution

### System Phases

```csharp
public enum SystemPhase
{
    PreSimulation = 5,   // Before game logic (time sync, ghost creation)
    Simulation = 10,     // Game logic (NEVER EXECUTED BY GLOBAL SCHEDULER)
    PostSimulation = 15  // After game logic (egress, event publishing)
}
```

**Why Simulation Phase is Never Run**:
- Game logic is MODULE-SPECIFIC, not cross-cutting
- Each module runs its own simulation systems internally
- Global scheduler only runs CROSS-CUTTING systems (networking, time, etc.)

### Priority Within Phase

```csharp
// PreSimulation Phase (Phase 5)
TimeSyncSystem:         Priority 0   // FIRST (updates simulation time)
GhostCreationSystem:    Priority 20  // After time sync (needs correct time)
InputSyncSystem:        Priority 30  // After ghost creation

// PostSimulation Phase (Phase 15)
OwnershipTrackingSystem: Priority 5  // Before egress (updates ownership)
SmartEgressSystem:       Priority 10 // After ownership (publishes updates)
LifecycleSyncSystem:     Priority 15 // After egress
TimeEventPublisher:      Priority 100 // LAST (publishes time events)
```

### Registration Example

```csharp
public class ModuleHost
{
    private List<IModuleSystem> _systems = new();
    
    public void RegisterSystem(IModuleSystem system)
    {
        _systems.Add(system);
    }
    
    public void SortSystems()
    {
        // Sort by Phase, then Priority
        _systems = _systems
            .OrderBy(s => (int)s.Phase)
            .ThenBy(s => s.Priority)
            .ToList();
    }
    
    public void ExecuteFrame(float deltaTime)
    {
        var view = _world.CreateView();
        
        foreach (var system in _systems)
        {
            system.Execute(view, deltaTime);
        }
    }
}
```

---

## Snapshot Providers (Recording/Replay)

Modules register **snapshot providers** to enable recording/replay:

```csharp
public interface ISnapshotProvider
{
    /// <summary>
    /// Capture current module state.
    /// Called once per frame during recording.
    /// </summary>
    void CaptureSnapshot(ISnapshotWriter writer);
    
    /// <summary>
    /// Restore module state from snapshot.
    /// Called during replay.
    /// </summary>
    void RestoreSnapshot(ISnapshotReader reader);
    
    /// <summary>
    /// Components this provider serializes.
    /// Used for: 1) Validation
    ///           2) Sanitization (strip non-replayed components)
    /// </summary>
    Type[] GetComponentTypes();
}
```

**Example: Replication Snapshot Provider**
```csharp
public class ReplicationSnapshotProvider : ISnapshotProvider
{
    public void CaptureSnapshot(ISnapshotWriter writer)
    {
        // Write ghost count
        writer.Write(_ghostEntities.Count);
        
        // Write each ghost
        foreach (var(remoteId, localEntity) in _ghostEntities)
        {
            writer.Write(remoteId);
            writer.Write(localEntity.Id);
            
            // Write components
            var ghost = localEntity.Get<GhostComponent>();
            writer.Write(ghost.OwnerNodeId);
            writer.Write(ghost.RemoteEntityId);
        }
    }
    
    public void RestoreSnapshot(ISnapshotReader reader)
    {
        // Clear existing ghosts
        foreach (var entity in _ghostEntities.Values)
        {
            _world.DestroyEntity(entity);
        }
        _ghostEntities.Clear();
        
        // Read ghost count
        int count = reader.ReadInt32();
        
        // Recreate ghosts
        for (int i = 0; i < count; i++)
        {
            uint remoteId = reader.ReadUInt32();
            uint localId = reader.ReadUInt32();
            
            var entity = _world.CreateEntity();
            var ghost = new GhostComponent
            {
                OwnerNodeId = reader.ReadUInt32(),
                RemoteEntityId = reader.ReadUInt32()
            };
            entity.Add(ghost);
            
            _ghostEntities[remoteId] = entity;
        }
    }
    
    public Type[] GetComponentTypes()
    {
        return new[] { typeof(GhostComponent) };
    }
}
```

---

## Multi-Module Composition (NetworkDemo)

```csharp
public class NetworkDemoApp
{
    private ModuleHost _moduleHost;
    
    public void Initialize()
    {
        // Create modules in dependency order
        var timeModule = new TimeCoordinationModule(timeConfig);
        var networkModule = new NetworkModule(networkConfig);
        var replicationModule = new ReplicationLogicModule(replicationConfig);
        var lifecycleModule = new LifecycleModule(lifecycleConfig);
        
        // Create module host
        _moduleHost = new ModuleHost(world);
        
        // Register modules (order matters!)
        _moduleHost.RegisterModule(timeModule);         // 1. No dependencies
        _moduleHost.RegisterModule(networkModule);      // 2. Depends on TimeModule
        _moduleHost.RegisterModule(replicationModule);  // 3. Depends on Network + Time
        _moduleHost.RegisterModule(lifecycleModule);    // 4. Depends on Network
        
        // Initialize all modules
        _moduleHost.InitializeModules();
        
        // Sort systems by phase/priority
        _moduleHost.SortSystems();
        
        // Start all modules
        _moduleHost.StartModules();
    }
    
    public void Update(float deltaTime)
    {
        // ModuleHost executes all systems in sorted order
        _moduleHost.ExecuteFrame(deltaTime);
    }
    
    public void Shutdown()
    {
        // Stop modules in reverse order
        _moduleHost.StopModules();
        _moduleHost.Dispose();
    }
}
```

**System Execution Order**:
```
Frame N:
  [PreSimulation Phase]
    1. TimeSyncSystem (Priority 0)          ← TimeModule
    2. GhostCreationSystem (Priority 20)    ← ReplicationModule
    3. LifecycleIngressSystem (Priority 30) ← LifecycleModule
  
  [Simulation Phase] - SKIPPED (no systems registered)
  
  [PostSimulation Phase]
    1. OwnershipTrackingSystem (Priority 5)  ← ReplicationModule
    2. SmartEgressSystem (Priority 10)       ← ReplicationModule
    3. LifecycleEgressSystem (Priority 15)   ← LifecycleModule
    4. TimeEventPublisher (Priority 100)     ← TimeModule
```

---

## Best Practices

**Module Design**:
1. **Single Responsibility**: One module per concern (time, network, lifecycle)
2. **Declare Dependencies**: Initialize modules in dependency order
3. **Stateless Systems**: Store state in components, not systems
4. **Idempotent Start/Stop**: Safe to call multiple times

**System Design**:
1. **Choose Correct Phase**: PreSim (inputs), PostSim (outputs)
2. **Set Priority Carefully**: Lower = earlier execution
3. **Minimize Per-Frame Work**: Cache queries, avoid allocations
4. **Use SystemPhase.Simulation for Internal Logic**: Don't register with global scheduler

**Registration**:
1. **Register Components First**: Before systems that use them
2. **Register Events Before Handlers**: Avoid missing events
3. **Validate Configuration Early**: In `Initialize()`, not `Start()`

**Testing**:
1. **Unit Test Modules in Isolation**: Mock IModuleContext
2. **Integration Test Module Composition**: Verify dependency order
3. **Test Snapshot Providers**: Verify roundtrip (capture → restore)

---

## Conclusion

The **Module System** provides a flexible, compositional architecture for building distributed simulations. By encapsulating functionality into modules, FDP achieves clean separation of concerns, testability, and runtime flexibility (hot-swap modules). The system/event/snapshot provider registration pattern ensures deterministic execution order and enables advanced features like recording/replay.

**Key Strengths**:
- **Composability**: Mix-and-match modules for different scenarios
- **Testability**: Mock modules/systems for unit tests
- **Lifecycle Management**: Centralized initialization/shutdown
- **Recording/Replay**: Snapshot providers enable deterministic replay

**Used By**:
- ModuleHost.Core (orchestration, lifecycle management)
- All FDP.Toolkit.* projects (module implementations)
- NetworkDemo (multi-module composition example)

**Total Lines**: 1010
