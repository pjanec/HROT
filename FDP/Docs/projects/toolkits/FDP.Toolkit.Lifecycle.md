# FDP.Toolkit.Lifecycle - Distributed Entity Lifecycle Coordination

## Overview

**FDP.Toolkit.Lifecycle** provides distributed coordination for entity construction and destruction across asynchronous modules in the FDP simulation engine. It implements an **ACK-based protocol** that ensures entities are fully initialized by all participating modules before becoming active in the simulation, and properly cleaned up during teardown.

The toolkit solves the critical challenge of **coordinated initialization** in distributed module architectures where:
- Modules run asynchronously (Physics, Network, AI, etc.)
- Entity creation requires multi-module participation
- Initialization failures must abort construction gracefully
- Network-replicated entities need deterministic construction timing
- Teardown must complete before entity destruction

**Key Features:**
- **Event-driven ACK protocol** for construction/destruction coordination
- **Timeout detection** for unresponsive modules (default: 5 seconds at 60 FPS)
- **Blueprint-specific module filtering** (only notify relevant modules)
- **Graceful failure handling** (NACK immediately aborts construction)
- **Zero-allocation hot path** using event consumption
- **Thread-safe** (when used with EntityCommandBuffer)

**Dependencies:**
- `Fdp.Kernel` - Entity/component system, events
- `FDP.Interfaces` - ITkbDatabase for blueprints
- `ModuleHost.Core` - IModule interface, execution policies
- `FDP.Toolkit.Tkb` - Template application

---

## Architecture

### Entity Lifecycle State Machine

Entities progress through distinct lifecycle states managed by the kernel's `EntityLifecycle` enum:

```
┌───────────────┐
│     GHOST     │ ←─── Network: EntityState arrives before Master
└───────┬───────┘
        │ EntityMaster arrives
        ↓
┌───────────────┐
│ CONSTRUCTING  │ ←─── CreateEntity() / BeginConstruction()
└───────┬───────┘
        │ Publish: ConstructionOrder
        │ ┌──────────────────────────────────────┐
        │ │ Module 1: Initialize → ACK           │
        │ │ Module 2: Initialize → ACK           │
        │ │ Module N: Initialize → ACK           │
        │ └──────────────────────────────────────┘
        │ All ACKs received
        ↓
┌───────────────┐
│    ACTIVE     │ ←─── Normal simulation
└───────┬───────┘
        │ BeginDestruction()
        ↓
┌───────────────┐
│   TEARDOWN    │ ←─── Publish: DestructionOrder
└───────┬───────┘
        │ ┌──────────────────────────────────────┐
        │ │ Module 1: Cleanup → ACK              │
        │ │ Module 2: Cleanup → ACK              │
        │ │ Module N: Cleanup → ACK              │
        │ └──────────────────────────────────────┘
        │ All ACKs received
        ↓
    Destroyed
```

**Failure Paths:**
```
CONSTRUCTING ──► Module NACK ──────────────► Destroyed (immediate)
CONSTRUCTING ──► Timeout (300 frames) ─────► Destroyed (logged)
TEARDOWN ─────► Timeout (300 frames) ─────► Force Destroyed
```

### Module Integration Pattern

The EntityLifecycleModule coordinates with other modules via events:

```
┌─────────────────────────────────────────────────────────────────┐
│                    EntityLifecycleModule                        │
│                                                                 │
│  ┌──────────────────┐         ┌─────────────────────────────┐  │
│  │  BeginConstr...  │────────►│ PendingConstruction Map     │  │
│  │  (Entity, BP)    │         │  Entity → RemainingAcks     │  │
│  └──────────────────┘         └─────────────────────────────┘  │
│           │                                                     │
│           ├──► Publish: ConstructionOrder                      │
│           │     {Entity, BlueprintId, Frame, Initiator}        │
│           │                                                     │
│           └──► Set: EntityLifecycle.Constructing               │
└───────────────────────┬────────────────────────────────────────┘
                        │
            ┌───────────┴────────────────────────┐
            │         Event Bus                  │
            └───────────┬────────────────────────┘
                        │
        ┌───────────────┼───────────────┬────────────────┐
        ▼               ▼               ▼                ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ Physics Mod  │ │ Network Mod  │ │   AI Mod     │ │  Audio Mod   │
│              │ │              │ │              │ │              │
│ Listens:     │ │ Listens:     │ │ Listens:     │ │ Listens:     │
│ ConstrOrder  │ │ ConstrOrder  │ │ ConstrOrder  │ │ ConstrOrder  │
│              │ │              │ │              │ │              │
│ → Init Comp  │ │ → Init Comp  │ │ → Init Comp  │ │ → Init Comp  │
│ → Publish    │ │ → Publish    │ │ → Publish    │ │ → Publish    │
│   ACK        │ │   ACK        │ │   ACK        │ │   ACK        │
└──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────┬───────┘
       │                │                │                │
       └───────────────►│◄───────────────┘                │
                        │◄────────────────────────────────┘
                        ▼
        ┌───────────────────────────────────────┐
        │      LifecycleSystem                  │
        │                                       │
        │  ConsumeEvents<ConstructionAck>()    │
        │  → ProcessConstructionAck()          │
        │     ├─► Remove from RemainingAcks    │
        │     └─► Count==0? SetActive()        │
        └───────────────────────────────────────┘
```

**Blueprint Filtering:**
```csharp
// Global participants: All modules that care about all entities
_globalParticipants = { ModuleId.Physics, ModuleId.Network };

// Blueprint-specific: Some entities need additional modules
_blueprintRequirements[TankBlueprint] = { ModuleId.AI, ModuleId.Turret };

// When spawning a Tank:
BeginConstruction(entity, TankBlueprint, ...)
  → Participants = Global ∪ Blueprint-specific
                 = {Physics, Network, AI, Turret}  // All 4 must ACK
```

---

## Core Components

### 1. EntityLifecycleModule

**Purpose:** Central coordinator implementing `IModule` interface.

**Source:** `EntityLifecycleModule.cs` (290 lines)

**Key Responsibilities:**
1. Maintain participant registrations (global + blueprint-specific)
2. Track pending constructions/destructions
3. Publish lifecycle orders (ConstructionOrder, DestructionOrder)
4. Process ACKs and update tracking state
5. Detect timeouts and force cleanup
6. Provide diagnostic statistics

**Configuration:**
```csharp
public class EntityLifecycleModule : IModule
{
    // Module identity
    public string Name => "EntityLifecycleManager";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
    
    // Reactive event listening
    public IReadOnlyList<Type>? WatchEvents => new[]
    {
        typeof(ConstructionAck),
        typeof(DestructionAck)
    };
    
    // Constructor
    public EntityLifecycleModule(
        ITkbDatabase tkb,                   // Blueprint registry
        IEnumerable<int> participatingModuleIds,  // Global participants
        int timeoutFrames = 300)            // 5 seconds at 60 FPS
    {
        _tkb = tkb;
        _globalParticipants = new HashSet<int>(participatingModuleIds);
        _timeoutFrames = timeoutFrames;
    }
}
```

**Internal State:**
```csharp
// Tracking dictionaries
private readonly Dictionary<Entity, PendingConstruction> _pendingConstruction;
private readonly Dictionary<Entity, PendingDestruction> _pendingDestruction;

// Participant configuration
private readonly HashSet<int> _globalParticipants;
private readonly Dictionary<long, HashSet<int>> _blueprintRequirements;

// Statistics
private int _totalConstructed;
private int _totalDestructed;
private int _timeouts;

// Tracking structures
internal class PendingConstruction
{
    public Entity Entity;
    public long BlueprintId;
    public uint StartFrame;
    public HashSet<int> RemainingAcks = new();
}

internal class PendingDestruction
{
    public Entity Entity;
    public uint StartFrame;
    public HashSet<int> RemainingAcks = new();
    public FixedString64 Reason;
}
```

### 2. Lifecycle Events

**Source:** `Events/LifecycleEvents.cs` (106 lines)

**Event Flow:** Order → Process → ACK → Activate/Destroy

#### ConstructionOrder [EventId: 9001]
```csharp
public struct ConstructionOrder
{
    public Entity Entity;           // Entity being constructed
    public long BlueprintId;        // Template identifier
    public uint FrameNumber;        // Construction start frame
    public int InitiatorModuleId;   // Who spawned it (optional)
}
```

**Published by:** EntityLifecycleModule.BeginConstruction()  
**Consumed by:** Modules that need to initialize entity

#### ConstructionAck [EventId: 9002]
```csharp
public struct ConstructionAck
{
    public Entity Entity;               // Entity initialized
    public int ModuleId;                // Responding module
    public bool Success;                // true = OK, false = abort
    public FixedString64 ErrorMessage;  // Optional failure reason
}
```

**Published by:** Individual modules after initialization  
**Consumed by:** LifecycleSystem → ProcessConstructionAck()

#### DestructionOrder [EventId: 9003]
```csharp
public struct DestructionOrder
{
    public Entity Entity;           // Entity being destroyed
    public uint FrameNumber;        // Destruction start frame
    public FixedString64 Reason;    // Debug info (e.g., "Player killed")
}
```

**Published by:** EntityLifecycleModule.BeginDestruction()  
**Consumed by:** Modules that need cleanup notification

#### DestructionAck [EventId: 9004]
```csharp
public struct DestructionAck
{
    public Entity Entity;       // Entity cleaned up
    public int ModuleId;        // Responding module
}
```

**Published by:** Individual modules after cleanup  
**Consumed by:** LifecycleSystem → ProcessDestructionAck()

### 3. Systems

#### LifecycleSystem

**Source:** `Systems/LifecycleSystem.cs` (45 lines)

**Phase:** `SystemPhase.BeforeSync` (ensures state changes visible to all modules)

**Purpose:** Event processor that drives the ACK state machine.

```csharp
[UpdateInPhase(SystemPhase.BeforeSync)]
public class LifecycleSystem : IModuleSystem
{
    private readonly EntityLifecycleModule _manager;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        uint currentFrame = view.Tick;
        
        // Process construction ACKs
        var constructionAcks = view.ConsumeEvents<ConstructionAck>();
        foreach (var ack in constructionAcks)
        {
            _manager.ProcessConstructionAck(ack, currentFrame, cmd);
        }
        
        // Process destruction ACKs
        var destructionAcks = view.ConsumeEvents<DestructionAck>();
        foreach (var ack in destructionAcks)
        {
            _manager.ProcessDestructionAck(ack, currentFrame, cmd);
        }
        
        // Check for timeouts
        _manager.CheckTimeouts(currentFrame, cmd);
    }
}
```

**Key Operations:**
1. **ConsumeEvents**: Zero-allocation event iteration
2. **ProcessConstructionAck**: Decrement RemainingAcks → Activate on 0
3. **ProcessDestructionAck**: Decrement RemainingAcks → Destroy on 0
4. **CheckTimeouts**: Detect stalled constructions/destructions

#### BlueprintApplicationSystem

**Source:** `Systems/BlueprintApplicationSystem.cs` (38 lines)

**Phase:** `SystemPhase.BeforeSync`

**Purpose:** Apply TKB templates immediately upon receiving ConstructionOrder.

```csharp
[UpdateInPhase(SystemPhase.BeforeSync)]
public class BlueprintApplicationSystem : IModuleSystem
{
    private readonly ITkbDatabase _tkb;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        
        // Consume ConstructionOrder events
        var orders = view.ConsumeEvents<ConstructionOrder>();
        foreach (ref readonly var order in orders)
        {
            if (_tkb.TryGetByType(order.BlueprintId, out var template))
            {
                // Apply template (preserves existing components)
                template.ApplyTo(repo, order.Entity, preserveExisting: true);
            }
        }
    }
}
```

**Why this matters:**
- Runs **before** modules receive ConstructionOrder
- Ensures entity has baseline components from blueprint
- Modules then customize/override as needed
- `preserveExisting: true` allows pre-initialization

#### LifecycleCleanupSystem

**Source:** `Systems/LifecycleCleanupSystem.cs` (89 lines)

**Phase:** `SystemPhase.Simulation`

**Purpose:** Remove transient components from newly-activated entities.

**Transient Definition:** Components marked as NOT Snapshotable, Recordable, or Saveable.

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class LifecycleCleanupSystem : IModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        InitializeTransientTypes(); // Build list on first run
        
        var cmd = view.GetCommandBuffer();
        
        // Remove transient components from newly-activated entities
        foreach (var typeId in _transientTypes)
        {
            // Use reflection to call RemoveTransientUnmanaged<T>() or
            // RemoveTransientManaged<T>() based on component type
        }
    }
}
```

**Why this matters:**
- Initialization metadata shouldn't persist (e.g., SpawnRequest component)
- Reduces memory footprint for active entities
- Prevents stale data from leaking into snapshots

---

## Public API

### Registration API

#### RegisterModule
```csharp
public void RegisterModule(int moduleId)
```

Adds a module to global participant list (cares about all entities).

**Example:**
```csharp
elm.RegisterModule(ModuleId.Physics);   // Physics must ACK all entities
elm.RegisterModule(ModuleId.Network);   // Network must ACK all entities
```

#### UnregisterModule
```csharp
public void UnregisterModule(int moduleId)
```

Removes a module from participation (useful for runtime plugin unloading).

#### RegisterRequirement
```csharp
public void RegisterRequirement(long blueprintId, int moduleId)
```

Declares blueprint-specific participant (only for entities of this type).

**Example:**
```csharp
// Tanks need AI module (but civilian cars don't)
elm.RegisterRequirement(TankBlueprint, ModuleId.AI);
elm.RegisterRequirement(TankBlueprint, ModuleId.Turret);

// When spawning Tank, participants = Global ∪ {AI, Turret}
```

### Construction API

#### BeginConstruction
```csharp
public void BeginConstruction(
    Entity entity, 
    long blueprintId, 
    uint currentFrame, 
    IEntityCommandBuffer cmd, 
    int initiator = 0)
```

Starts coordinated entity construction.

**Flow:**
1. Calculate participants (global + blueprint-specific)
2. Create `PendingConstruction` tracker
3. Publish `ConstructionOrder` event
4. Wait for ACKs...

**Exceptions:**
- `InvalidOperationException` if entity already in construction

**Example:**
```csharp
var entity = repo.CreateEntity();
repo.SetLifecycleState(entity, EntityLifecycle.Constructing);

elm.BeginConstruction(
    entity, 
    blueprintId: VehicleBlueprints.Tank, 
    currentFrame: view.Tick, 
    cmd: view.GetCommandBuffer(),
    initiator: ModuleId.Spawner
);
```

#### AcknowledgeConstruction (Helper)
```csharp
public void AcknowledgeConstruction(
    Entity entity, 
    int moduleId, 
    uint frame, 
    IEntityCommandBuffer cmd)
```

Convenience wrapper to publish ConstructionAck with `Success = true`.

**Module Implementation Pattern:**
```csharp
public class PhysicsModule : IModule
{
    public void Execute(ISimulationView view, float dt)
    {
        var orders = view.ConsumeEvents<ConstructionOrder>();
        foreach (var order in orders)
        {
            // Initialize physics components
            view.AddComponent(order.Entity, new RigidBody { Mass = 1000 });
            
            // ACK construction
            _elm.AcknowledgeConstruction(
                order.Entity, 
                ModuleId.Physics, 
                view.Tick, 
                view.GetCommandBuffer()
            );
        }
    }
}
```

### Destruction API

#### BeginDestruction
```csharp
public void BeginDestruction(
    Entity entity, 
    uint currentFrame, 
    FixedString64 reason, 
    IEntityCommandBuffer cmd)

// Overload with string reason
public void BeginDestruction(
    Entity entity, 
    uint currentFrame, 
    string reason, 
    IEntityCommandBuffer cmd)
```

Starts coordinated entity teardown.

**Flow:**
1. Create `PendingDestruction` tracker (global participants only)
2. Publish `DestructionOrder` event
3. Set entity state to `EntityLifecycle.TearDown`
4. Wait for ACKs...

**Idempotent:** Returns early if entity already in teardown.

**Example:**
```csharp
elm.BeginDestruction(
    entity, 
    currentFrame: view.Tick, 
    reason: "Player killed by enemy", 
    cmd: view.GetCommandBuffer()
);
```

### Processing API (Internal)

#### ProcessConstructionAck
```csharp
public void ProcessConstructionAck(
    ConstructionAck ack, 
    uint currentFrame, 
    IEntityCommandBuffer cmd)
```

**Called by:** LifecycleSystem

**Logic:**
1. Find `PendingConstruction` entry
2. **If `Success == false`:** Log error, destroy entity immediately
3. Remove `ModuleId` from `RemainingAcks`
4. **If `RemainingAcks.Count == 0`:** Set entity to Active, increment stats

#### ProcessDestructionAck
```csharp
public void ProcessDestructionAck(
    DestructionAck ack, 
    uint currentFrame, 
    IEntityCommandBuffer cmd)
```

**Called by:** LifecycleSystem

**Logic:**
1. Find `PendingDestruction` entry
2. Remove `ModuleId` from `RemainingAcks`
3. **If `RemainingAcks.Count == 0`:** Destroy entity, increment stats

#### CheckTimeouts
```csharp
public void CheckTimeouts(uint currentFrame, IEntityCommandBuffer cmd)
```

**Called by:** LifecycleSystem every frame

**Logic:**
1. Check all pending constructions: `currentFrame - StartFrame > _timeoutFrames`
2. **On timeout:** Log missing modules, destroy entity, increment `_timeouts`
3. Check all pending destructions: `currentFrame - StartFrame > _timeoutFrames`
4. **On timeout:** Force destroy entity (modules failed to cleanup)

**Example Output:**
```
[ELM] Construction timeout for Entity 1234. Missing ACKs from modules: 3, 7
[ELM] Destruction timeout for Entity 5678. Forcing deletion.
```

### Statistics API

#### GetStatistics
```csharp
public (int constructed, int destructed, int timeouts, int pending) GetStatistics()
```

Returns diagnostic counters:
- `constructed`: Total entities successfully activated
- `destructed`: Total entities fully destroyed
- `timeouts`: Total timeout failures
- `pending`: Current entities awaiting ACKs

**Example:**
```csharp
var stats = elm.GetStatistics();
Console.WriteLine($"Lifecycle Stats:");
Console.WriteLine($"  Constructed: {stats.constructed}");
Console.WriteLine($"  Destructed: {stats.destructed}");
Console.WriteLine($"  Timeouts: {stats.timeouts}");
Console.WriteLine($"  Pending: {stats.pending}");
```

---

## Code Examples

### Example 1: Basic Module Setup

```csharp
using FDP.Toolkit.Lifecycle;
using ModuleHost.Core;

// Configure EntityLifecycleModule at ModuleHost startup
var tkb = new TkbDatabase();
var elm = new EntityLifecycleModule(
    tkb: tkb,
    participatingModuleIds: new[] { ModuleId.Physics, ModuleId.Network },
    timeoutFrames: 300  // 5 seconds at 60 FPS
);

// Register it with ModuleHost
kernel.RegisterModule(elm);

// Done! Module will now coordinate lifecycle for all entities
```

### Example 2: Spawning an Entity (Spawner Module)

```csharp
public class VehicleSpawnerModule : IModule
{
    private readonly EntityLifecycleModule _elm;
    private readonly ITkbDatabase _tkb;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Consume spawn requests (from user input, AI, etc.)
        var requests = view.ConsumeEvents<SpawnVehicleRequest>();
        
        foreach (var request in requests)
        {
            // 1. Create entity in Constructing state
            var entity = view.CreateEntity();
            view.SetLifecycleState(entity, EntityLifecycle.Constructing);
            
            // 2. Optionally add initial components (before template)
            view.AddComponent(entity, new Position { 
                X = request.SpawnX, 
                Y = request.SpawnY 
            });
            
            // 3. Trigger lifecycle coordination
            _elm.BeginConstruction(
                entity, 
                blueprintId: request.VehicleType,  // e.g., TankBlueprint
                currentFrame: view.Tick,
                cmd: view.GetCommandBuffer(),
                initiator: ModuleId.Spawner
            );
            
            // Entity now in CONSTRUCTING state, waiting for ACKs
        }
    }
}
```

### Example 3: Module Responding to ConstructionOrder

```csharp
public class PhysicsModule : IModule
{
    private readonly EntityLifecycleModule _elm;
    
    public IReadOnlyList<Type> WatchEvents => new[] { typeof(ConstructionOrder) };
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Listen for construction orders
        var orders = view.ConsumeEvents<ConstructionOrder>();
        foreach (var order in orders)
        {
            // Check if we care about this blueprint
            bool needsPhysics = _tkb.GetByType(order.BlueprintId)
                .HasComponent<RigidBody>();
            
            if (needsPhysics)
            {
                // Initialize physics state
                var rb = new RigidBody { Mass = 1000, Drag = 0.1f };
                view.AddComponent(order.Entity, rb);
                
                // Compute initial physics setup
                var collider = new BoxCollider { Width = 2, Height = 1 };
                view.AddComponent(order.Entity, collider);
            }
            
            // ACK construction (even if we didn't do anything)
            _elm.AcknowledgeConstruction(
                order.Entity, 
                ModuleId.Physics, 
                view.Tick, 
                cmd
            );
        }
    }
}
```

### Example 4: Handling Construction Failures

```csharp
public class NetworkModule : IModule
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var orders = view.ConsumeEvents<ConstructionOrder>();
        foreach (var order in orders)
        {
            // Try to allocate network ID
            if (!_idPool.TryAllocate(out var netId))
            {
                // NACK construction (ID pool exhausted)
                view.PublishEvent(new ConstructionAck
                {
                    Entity = order.Entity,
                    ModuleId = ModuleId.Network,
                    Success = false,
                    ErrorMessage = new FixedString64("Network ID pool exhausted")
                });
                continue;
            }
            
            // Success path
            view.AddComponent(order.Entity, new NetworkIdentity { Id = netId });
            view.PublishEvent(new ConstructionAck
            {
                Entity = order.Entity,
                ModuleId = ModuleId.Network,
                Success = true
            });
        }
    }
}
```

**Result:** If ANY module sends `Success = false`, the entity is immediately destroyed and logged:
```
[ELM] Construction failed for 1234: Network ID pool exhausted
```

### Example 5: Blueprint-Specific Requirements

```csharp
// At initialization, declare special requirements
elm.RegisterRequirement(TankBlueprint, ModuleId.AI);         // Tanks need AI
elm.RegisterRequirement(TankBlueprint, ModuleId.Turret);     // Tanks need turret
elm.RegisterRequirement(HelicopterBlueprint, ModuleId.Flight); // Helicopters need flight

// Now when spawning:
elm.BeginConstruction(entity, TankBlueprint, ...);
// → Participants = Global{Physics, Network} ∪ Blueprint{AI, Turret}
//                = {Physics, Network, AI, Turret}  ← All 4 must ACK

elm.BeginConstruction(entity, CivilianCarBlueprint, ...);
// → Participants = Global{Physics, Network} only
//                = {Physics, Network}  ← Only 2 must ACK
```

### Example 6: Coordinated Destruction

```csharp
public class HealthSystem : IModuleSystem
{
    private readonly EntityLifecycleModule _elm;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Query all active entities with Health component
        var query = view.Query()
            .WithLifecycle(EntityLifecycle.Active)
            .WithComponent<Health>()
            .Build();
        
        foreach (var entity in query)
        {
            ref var health = ref view.GetComponent<Health>(entity);
            
            if (health.Current <= 0)
            {
                // Trigger coordinated teardown
                _elm.BeginDestruction(
                    entity,
                    currentFrame: view.Tick,
                    reason: $"Health depleted ({health.LastDamageSource})",
                    cmd
                );
                
                // Entity now in TEARDOWN state
                // - Network module will send final deletion packet
                // - Audio module will play death sound
                // - VFX module will spawn explosion
                // - All modules ACK, then entity destroyed
            }
        }
    }
}
```

### Example 7: Network Module Responding to Destruction

```csharp
public class NetworkModule : IModule
{
    public IReadOnlyList<Type> WatchEvents => new[] 
    { 
        typeof(ConstructionOrder),
        typeof(DestructionOrder) 
    };
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Handle destruction cleanup
        var destructionOrders = view.ConsumeEvents<DestructionOrder>();
        foreach (var order in destructionOrders)
        {
            // Send final state update to network
            if (view.HasComponent<NetworkIdentity>(order.Entity))
            {
                var netId = view.GetComponent<NetworkIdentity>(order.Entity);
                _cyclone.PublishDelete(netId.Id);
            }
            
            // ACK destruction
            cmd.PublishEvent(new DestructionAck
            {
                Entity = order.Entity,
                ModuleId = ModuleId.Network
            });
        }
    }
}
```

### Example 8: Integration Testing Pattern

```csharp
[Fact]
public async Task EntityConstruction_AllModulesACK_ActivatesEntity()
{
    // Setup
    var kernel = new ModuleHostKernel();
    var repo = new EntityRepository();
    var tkb = new TkbDatabase();
    
    var elm = new EntityLifecycleModule(
        tkb, 
        participatingModuleIds: new[] { 1, 2, 3 },
        timeoutFrames: 60
    );
    
    // Register mock modules
    kernel.RegisterModule(elm);
    kernel.RegisterModule(new MockPhysicsModule(1, elm));
    kernel.RegisterModule(new MockNetworkModule(2, elm));
    kernel.RegisterModule(new MockAudioModule(3, elm));
    
    // Execute
    var entity = repo.CreateEntity();
    repo.SetLifecycleState(entity, EntityLifecycle.Constructing);
    
    var cmd = new EntityCommandBuffer();
    elm.BeginConstruction(entity, blueprintId: 100, currentFrame: 0, cmd);
    cmd.Playback(repo);
    
    // Run simulation until activation
    for (int frame = 0; frame < 10; frame++)
    {
        kernel.Update(0.016f);
        
        if (repo.GetLifecycleState(entity) == EntityLifecycle.Active)
            break;
    }
    
    // Verify
    Assert.Equal(EntityLifecycle.Active, repo.GetLifecycleState(entity));
    var stats = elm.GetStatistics();
    Assert.Equal(1, stats.constructed);
    Assert.Equal(0, stats.timeouts);
}
```

### Example 9: Timeout Handling

```csharp
[Fact]
public void ConstructionTimeout_DestroysPendingEntity()
{
    var elm = new EntityLifecycleModule(
        null, 
        new[] { ModuleId.Physics, ModuleId.Network },
        timeoutFrames: 10  // Very short timeout for testing
    );
    
    var cmd = new MockCommandBuffer();
    var entity = new Entity(100, 1);
    
    // Begin construction at frame 0
    elm.BeginConstruction(entity, blueprintId: 1, currentFrame: 0, cmd);
    
    // Only Physics ACKs (Network doesn't respond)
    elm.ProcessConstructionAck(new ConstructionAck
    {
        Entity = entity,
        ModuleId = ModuleId.Physics,
        Success = true
    }, currentFrame: 5, cmd);
    
    // Check timeout at frame 15 (> 10 frame timeout)
    elm.CheckTimeouts(currentFrame: 15, cmd);
    
    // Verify entity destroyed due to timeout
    var stats = elm.GetStatistics();
    Assert.Equal(1, stats.timeouts);
    Assert.Contains(cmd.DestroyedEntities, e => e == entity);
}
```

**Console Output:**
```
[ELM] Construction timeout for Entity 100. Missing ACKs from modules: 2
```

### Example 10: Multi-Entity Concurrent Construction

```csharp
public void StressTest_100SimultaneousConstructions()
{
    var elm = new EntityLifecycleModule(
        null,
        new[] { 1, 2, 3 },
        timeoutFrames: 300
    );
    
    var entities = new List<Entity>();
    var cmd = new MockCommandBuffer();
    
    // Spawn 100 entities simultaneously
    for (int i = 0; i < 100; i++)
    {
        var entity = new Entity((uint)i, 1);
        entities.Add(entity);
        elm.BeginConstruction(entity, blueprintId: 1, currentFrame: 0, cmd);
    }
    
    // Simulate ACKs arriving over time
    for (uint frame = 1; frame <= 5; frame++)
    {
        foreach (var entity in entities)
        {
            // Module 1 ACKs at frame 1
            if (frame == 1)
                elm.ProcessConstructionAck(
                    new ConstructionAck { Entity = entity, ModuleId = 1, Success = true },
                    frame, cmd);
            
            // Module 2 ACKs at frame 3
            if (frame == 3)
                elm.ProcessConstructionAck(
                    new ConstructionAck { Entity = entity, ModuleId = 2, Success = true },
                    frame, cmd);
            
            // Module 3 ACKs at frame 5
            if (frame == 5)
                elm.ProcessConstructionAck(
                    new ConstructionAck { Entity = entity, ModuleId = 3, Success = true },
                    frame, cmd);
        }
    }
    
    // Verify all entities constructed
    var stats = elm.GetStatistics();
    Assert.Equal(100, stats.constructed);
    Assert.Equal(0, stats.timeouts);
    Assert.Equal(0, stats.pending);
}
```

---

## Integration Patterns

### Pattern 1: Centralized Registration

**Use Case:** All modules register at kernel initialization.

```csharp
public class SimulationSetup
{
    public static ModuleHostKernel CreateKernel()
    {
        var kernel = new ModuleHostKernel();
        var tkb = new TkbDatabase();
        
        // Create ELM with all participants upfront
        var elm = new EntityLifecycleModule(
            tkb,
            participatingModuleIds: new[] 
            {
                ModuleId.Physics,
                ModuleId.Network,
                ModuleId.AI,
                ModuleId.Audio
            },
            timeoutFrames: 300
        );
        
        // Register blueprint-specific requirements
        elm.RegisterRequirement(VehicleBlueprints.Tank, ModuleId.Turret);
        elm.RegisterRequirement(VehicleBlueprints.Aircraft, ModuleId.Flight);
        
        kernel.RegisterModule(elm);
        
        // Register other modules...
        kernel.RegisterModule(new PhysicsModule(elm));
        kernel.RegisterModule(new NetworkModule(elm));
        // ...
        
        return kernel;
    }
}
```

### Pattern 2: Lazy Blueprint Requirements

**Use Case:** Modules register their interests dynamically.

```csharp
public class TurretModule : IModule
{
    private readonly EntityLifecycleModule _elm;
    
    public void Initialize()
    {
        // Self-register for all tank types
        foreach (var tankBlueprint in GetTankBlueprints())
        {
            _elm.RegisterRequirement(tankBlueprint, ModuleId.Turret);
        }
    }
}
```

### Pattern 3: Network-Synchronized Spawning

**Use Case:** Client receives entity from network and coordinates local initialization.

```csharp
public class NetworkModule : IModule
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        // Receive EntityMaster from DDS
        var newEntities = _cyclone.ReceiveEntityMasters();
        
        foreach (var master in newEntities)
        {
            // Create local replica entity
            var entity = view.CreateEntity();
            view.SetLifecycleState(entity, EntityLifecycle.Constructing);
            
            // Add network identity
            view.AddComponent(entity, new NetworkIdentity { Id = master.Id });
            
            // Trigger local construction (Physics, Audio, etc. initialize)
            _elm.BeginConstruction(
                entity,
                blueprintId: master.TypeId,
                currentFrame: view.Tick,
                cmd: view.GetCommandBuffer(),
                initiator: ModuleId.Network
            );
        }
    }
}
```

### Pattern 4: Graceful Shutdown

**Use Case:** Destroy all entities during simulation teardown.

```csharp
public void ShutdownSimulation()
{
    var cmd = new EntityCommandBuffer();
    
    // Query all active entities
    var all = _repo.Query()
        .WithLifecycle(EntityLifecycle.Active)
        .Build();
    
    foreach (var entity in all)
    {
        _elm.BeginDestruction(
            entity,
            currentFrame: _repo.GlobalVersion,
            reason: "Simulation shutdown",
            cmd
        );
    }
    
    cmd.Playback(_repo);
    
    // Run frames until all destroyed
    while (_elm.GetStatistics().pending > 0)
    {
        _kernel.Update(0.016f);
    }
}
```

---

## Performance Characteristics

### Time Complexity

| Operation | Complexity | Notes |
|-----------|------------|-------|
| BeginConstruction | O(P) | P = participant count (global + blueprint) |
| ProcessConstructionAck | O(1) | HashSet removal |
| CheckTimeouts (per frame) | O(N) | N = pending entities |
| GetStatistics | O(1) | Simple field access |

### Memory Overhead

**Per pending entity:**
- `PendingConstruction`: ~72 bytes (Entity + BlueprintId + StartFrame + HashSet overhead)
- HashSet<int> (participants): ~32 bytes + (8 bytes × P entries)

**Example:** 100 pending constructions with 4 participants each:
- Memory: 100 × (72 + 32 + 32) = **13.6 KB**

### Network Traffic

**Per entity lifecycle:**
- ConstructionOrder: ~24 bytes
- ConstructionAck × N modules: 16 bytes × N
- **Total Construction:** 24 + (16 × N) bytes

**Example:** 4 modules = 24 + 64 = **88 bytes per entity spawn**

### Event Processing

The event-driven architecture ensures **zero polling overhead**:
- Modules only wake when events arrive
- `ConsumeEvents<T>()` uses iterator-based traversal (no allocations)
- ACK processing is O(1) per event

---

## Thread Safety

### Module Execution

EntityLifecycleModule executes with **Synchronous policy**, ensuring:
- Single-threaded access to internal dictionaries
- No race conditions on `_pendingConstruction`/`_pendingDestruction`

### Command Buffer Pattern

All modifications use `IEntityCommandBuffer`:
```csharp
cmd.PublishEvent(new ConstructionOrder { ... });  // Deferred
cmd.SetLifecycleState(entity, Active);             // Deferred
cmd.DestroyEntity(entity);                         // Deferred
```

**Thread Safety:** Commands are batched and played back at safe points.

### Concurrent Module ACKs

Multiple modules publishing ACKs simultaneously is **safe** because:
1. Events are queued independently per module
2. LifecycleSystem consumes all events in a single-threaded context
3. ProcessConstructionAck() executes serially

---

## Error Handling

### Construction Failures

**Scenario:** Module cannot initialize entity (resource exhaustion, validation failure).

**Response:**
```csharp
cmd.PublishEvent(new ConstructionAck
{
    Entity = entity,
    ModuleId = myModuleId,
    Success = false,
    ErrorMessage = new FixedString64("Validation failed: Invalid position")
});
```

**Result:**
- Entity **immediately destroyed** (no waiting for other ACKs)
- Error logged to `Console.Error`
- Other modules' ACKs ignored (entity already removed)

### Timeout Handling

**Scenario:** Module crashes or hangs without ACKing.

**Detection:**
```csharp
if (currentFrame - pending.StartFrame > _timeoutFrames)
{
    Console.Error.WriteLine($"Timeout for {entity}. Missing: {string.Join(", ", pending.RemainingAcks)}");
    cmd.DestroyEntity(entity);
    _timeouts++;
}
```

**Configuration:**
```csharp
// Production: 5 seconds at 60 FPS
var elm = new EntityLifecycleModule(tkb, modules, timeoutFrames: 300);

// Testing: Fast timeout
var elm = new EntityLifecycleModule(tkb, modules, timeoutFrames: 10);
```

### Double-Construction Guard

**Scenario:** Accidentally call BeginConstruction twice:

```csharp
if (_pendingConstruction.ContainsKey(entity))
{
    throw new InvalidOperationException($"Entity {entity.Index} already in construction");
}
```

**Prevention:** Ensures state machine integrity.

### Idempotent Destruction

**Scenario:** Multiple systems call BeginDestruction:

```csharp
if (_pendingDestruction.ContainsKey(entity))
{
    return;  // Already in teardown, ignore
}
```

**Behavior:** Safe to call multiple times (first wins).

---

## Testing Strategies

### Unit Testing

**Test Isolation:** EntityLifecycleModule can be tested standalone.

```csharp
[Fact]
public void ProcessConstructionAck_AllAcks_SetsActive()
{
    var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
    var cmd = new MockCommandBuffer();
    var entity = new Entity(100, 1);
    
    elm.BeginConstruction(entity, 1, 0, cmd);
    
    // ACK from module 1
    elm.ProcessConstructionAck(
        new ConstructionAck { Entity = entity, ModuleId = 1, Success = true },
        currentFrame: 1, cmd);
    Assert.Equal(0, cmd.ActivatedEntities.Count);  // Not yet
    
    // ACK from module 2
    elm.ProcessConstructionAck(
        new ConstructionAck { Entity = entity, ModuleId = 2, Success = true },
        currentFrame: 2, cmd);
    Assert.Equal(1, cmd.ActivatedEntities.Count);  // Now activated
}
```

### Integration Testing

**Full Kernel Testing:**

```csharp
[Fact]
public async Task FullKernel_Construction_ActivatesAfterAllModules()
{
    var kernel = new ModuleHostKernel();
    var repo = new EntityRepository();
    var elm = new EntityLifecycleModule(null, new[] { 1, 2, 3 });
    
    kernel.RegisterModule(elm);
    kernel.RegisterModule(new TestModule(1, elm));
    kernel.RegisterModule(new TestModule(2, elm));
    kernel.RegisterModule(new TestModule(3, elm));
    
    var entity = repo.CreateEntity();
    repo.SetLifecycleState(entity, EntityLifecycle.Constructing);
    
    var cmd = new EntityCommandBuffer();
    elm.BeginConstruction(entity, 1, 0, cmd);
    cmd.Playback(repo);
    
    // Run simulation
    for (int i = 0; i < 10; i++)
    {
        kernel.Update(0.016f);
        if (repo.GetLifecycleState(entity) == EntityLifecycle.Active)
            break;
    }
    
    Assert.Equal(EntityLifecycle.Active, repo.GetLifecycleState(entity));
}
```

### Stress Testing

**Concurrent Entity Spawning:**

```csharp
[Fact]
public void StressTest_1000Entities_AllActivate()
{
    var elm = new EntityLifecycleModule(null, new[] { 1, 2 });
    var entities = CreateAndBeginConstruction(1000);
    
    // Simulate delayed ACKs
    foreach (var entity in entities)
    {
        elm.ProcessConstructionAck(...);  // Module 1
    }
    foreach (var entity in entities)
    {
        elm.ProcessConstructionAck(...);  // Module 2
    }
    
    var stats = elm.GetStatistics();
    Assert.Equal(1000, stats.constructed);
}
```

### Network Testing

**Distributed Simulation:**

```csharp
[Fact]
public async Task TwoNodes_ReplicatedSpawn_BothActivate()
{
    var node1 = CreateNode();
    var node2 = CreateNode();
    
    // Node 1 spawns entity
    var entity1 = node1.Spawn(TankBlueprint);
    
    // Wait for network replication
    await Task.Delay(100);
    
    // Node 2 should have replica
    var entity2 = node2.FindByNetworkId(entity1.NetworkId);
    Assert.NotNull(entity2);
    Assert.Equal(EntityLifecycle.Active, node2.GetState(entity2));
}
```

---

## Debugging and Diagnostics

### Logging

Enable detailed lifecycle logging:

```csharp
public class VerboseLifecycleModule : EntityLifecycleModule
{
    public override void BeginConstruction(...)
    {
        Console.WriteLine($"[{Name}] BEGIN Construction: Entity={entity.Index}, Blueprint={blueprintId}, Participants={string.Join(",", participants)}");
        base.BeginConstruction(...);
    }
    
    public override void ProcessConstructionAck(...)
    {
        var remaining = _pendingConstruction[ack.Entity].RemainingAcks.Count;
        Console.WriteLine($"[{Name}] ACK from Module {ack.ModuleId}: Entity={ack.Entity.Index}, Remaining={remaining}");
        base.ProcessConstructionAck(...);
    }
}
```

**Output:**
```
[EntityLifecycleManager] BEGIN Construction: Entity=1234, Blueprint=100, Participants=1,2,3
[EntityLifecycleManager] ACK from Module 1: Entity=1234, Remaining=2
[EntityLifecycleManager] ACK from Module 2: Entity=1234, Remaining=1
[EntityLifecycleManager] ACK from Module 3: Entity=1234, Remaining=0  → ACTIVATED
```

### Statistics Monitoring

```csharp
// Production monitoring
public void MonitorLifecycleHealth()
{
    var stats = _elm.GetStatistics();
    
    if (stats.pending > 100)
        Logger.Warn($"High pending count: {stats.pending}");
    
    if (stats.timeouts > 0)
        Logger.Error($"Construction timeouts detected: {stats.timeouts}");
    
    Metrics.Gauge("lifecycle.pending", stats.pending);
    Metrics.Counter("lifecycle.constructed", stats.constructed);
    Metrics.Counter("lifecycle.timeouts", stats.timeouts);
}
```

### Visualizing State

```csharp
public void DumpLifecycleState()
{
    Console.WriteLine("=== Entity Lifecycle State ===");
    
    var constructing = _repo.Query()
        .WithLifecycle(EntityLifecycle.Constructing)
        .Build();
    Console.WriteLine($"Constructing: {constructing.Count()}");
    
    var active = _repo.Query()
        .WithLifecycle(EntityLifecycle.Active)
        .Build();
    Console.WriteLine($"Active: {active.Count()}");
    
    var teardown = _repo.Query()
        .WithLifecycle(EntityLifecycle.TearDown)
        .Build();
    Console.WriteLine($"TearDown: {teardown.Count()}");
    
    var stats = _elm.GetStatistics();
    Console.WriteLine($"Pending ACKs: {stats.pending}");
    Console.WriteLine($"Total Timeouts: {stats.timeouts}");
}
```

---

## Comparison with Alternatives

### Alternative 1: Polling-Based Validation

**Traditional Approach:**
```csharp
public void ValidateEntities()
{
    foreach (var entity in GetConstructingEntities())
    {
        bool allReady = CheckAllModulesReady(entity);
        if (allReady) Activate(entity);
    }
}
```

**Drawbacks:**
- Wastes CPU cycles every frame checking state
- Polling overhead scales with entity count
- No explicit failure signaling

### Alternative 2: Callback-Based

**Callback Approach:**
```csharp
entity.OnInitialized += (module) => {
    if (AllModulesInitialized()) Activate();
};
```

**Drawbacks:**
- Allocates delegate closures
- Difficult to track timeouts
- Not serializable/deterministic

### EntityLifecycleModule Advantages

✅ **Event-driven:** Zero polling overhead  
✅ **Timeout detection:** Automatic failure recovery  
✅ **Explicit failures:** Modules can NACK construction  
✅ **Blueprint filtering:** Only notify relevant modules  
✅ **Deterministic:** Event ordering preserved in Flight Recorder  
✅ **Network-safe:** Works with distributed simulations

---

## Advanced Topics

### Multi-Stage Construction

**Scenario:** Entity needs 2-phase initialization (stage 1: physics, stage 2: gameplay).

**Solution:** Use multiple lifecycle coordinates:

```csharp
// Stage 1: Physics construction
elm1.BeginConstruction(entity, PhysicsBlueprint, ...);
// Wait for ACKs...

// Stage 2: Gameplay construction
elm2.BeginConstruction(entity, GameplayBlueprint, ...);
```

**Alternative:** Custom state components:

```csharp
public struct InitializationPhase
{
    public byte Phase;  // 0=Physics, 1=Rendering, 2=Gameplay
}
```

### Conditional Participation

**Scenario:** Module participates only if entity has specific component.

```csharp
public class AIModule : IModule
{
    public void Execute(ISimulationView view, float dt)
    {
        var orders = view.ConsumeEvents<ConstructionOrder>();
        foreach (var order in orders)
        {
            // Only ACK if entity needs AI
            if (view.HasComponent<AIBehavior>(order.Entity))
            {
                InitializeAI(order.Entity);
                _elm.AcknowledgeConstruction(...);
            }
            // Else: Don't ACK (not a participant for this entity)
        }
    }
}
```

**Consideration:** Must not be in `_globalParticipants` or will timeout!

### Ghost Entities (Network Replicas)

**Scenario:** Network receives `EntityState` before `EntityMaster`.

**Solution:** Create in Ghost state:

```csharp
var entity = repo.CreateEntity();
repo.SetLifecycleState(entity, EntityLifecycle.Ghost);
repo.AddComponent(entity, new NetworkIdentity { Id = netId });

// Later, when EntityMaster arrives:
_elm.BeginConstruction(entity, master.Blueprint, ...);
// Transitions: Ghost → Constructing → Active
```

---

## Future Enhancements

### Planned Features

1. **Priority-based ACKing:**
   - Critical modules ACK first (Physics before Audio)
   - Allows early activation for rendering while AI initializes

2. **Partial activation:**
   - Entity becomes "partially active" after core modules ACK
   - Non-critical modules (audio, VFX) can ACK later

3. **Rollback support:**
   - Integration with Flight Recorder for deterministic replay
   - Rewind construction/destruction during rollback

4. **Dynamic timeout adjustment:**
   - Per-module timeout configuration
   - Adaptive timeouts based on system load

5. **Diagnostic web UI:**
   - Real-time visualization of pending constructions
   - Identify slow-ACKing modules

### Research Areas

- **Hierarchical construction:** Parent-child entity dependencies
- **Transaction semantics:** All-or-nothing multi-entity spawning
- **Prewarm pools:** Reuse constructed entities for performance

---

## Dependencies

### Required Packages

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Kernel\Fdp.Kernel\Fdp.Kernel.csproj" />
  <ProjectReference Include="..\..\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
  <ProjectReference Include="..\..\Common\FDP.Interfaces\FDP.Interfaces.csproj" />
  <ProjectReference Include="..\FDP.Toolkit.Tkb\FDP.Toolkit.Tkb.csproj" />
</ItemGroup>
```

### Component Dependencies

- **Fdp.Kernel:**
  - `Entity`, `EntityRepository`
  - `EntityLifecycle` enum
  - `IEntityCommandBuffer`
  - `EventIds`, event consumption

- **FDP.Interfaces:**
  - `ITkbDatabase`, `TkbTemplate`

- **ModuleHost.Core:**
  - `IModule`, `IModuleSystem`
  - `ExecutionPolicy`
  - `ISimulationView`
  - `SystemPhase`

- **FDP.Toolkit.Tkb:**
  - `TkbDatabase` (blueprint application)

---

## Summary

**FDP.Toolkit.Lifecycle** is a battle-tested distributed coordination system that ensures robust entity construction/destruction across asynchronous modules. Its event-driven ACK protocol provides:

1. **Reliability:** Timeout detection prevents stuck entities
2. **Performance:** Zero polling overhead, O(1) ACK processing
3. **Flexibility:** Blueprint-specific filtering reduces overhead
4. **Debuggability:** Explicit failure signals and statistics
5. **Network-safe:** Works with DDS-replicated simulations

Key statistics:
- **290 lines** of core implementation
- **4 lifecycle events** (Order/ACK × Construction/Destruction)
- **3 systems** (Lifecycle, BlueprintApplication, Cleanup)
- **O(1) ACK processing** per event
- **~88 bytes** network overhead per entity spawn (4 modules)

The module is production-ready and forms the **foundation for coordinated entity management** in the FDP simulation engine. Without it, modules would race during initialization leading to undefined behavior, missing components, and crashes. With it, entity construction is **deterministic, timeout-safe, and network-compatible**.
