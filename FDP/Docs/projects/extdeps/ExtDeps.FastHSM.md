# ExtDeps.FastHSM

## Overview

**FastHSM** is a high-performance hierarchical state machine library for C# designed for real-time systems. It provides zero-allocation runtime execution, cache-friendly data structures, and deterministic event-driven state transitions. Integrated into FDP for entity lifecycle management, network protocol state tracking, and complex behavior modeling.

**Sub-Projects**:
- **Fhsm.Kernel** (`src/Fhsm.Kernel`): Core state machine engine, event queues, transition logic
- **Fhsm.Compiler** (`src/Fhsm.Compiler`): Builder API, state machine validation, code generation
- **Fhsm.Utilities** (`src/Fhsm.Utilities`): Visualization, debugging, serialization tools
- **Examples** (`examples/`): Traffic light, TCP connection, game character states
- **Demos** (`demos/`): Interactive demos, performance benchmarks
- **Tests** (`tests/`): Unit tests, integration tests, determinism validation

**Primary Use in FDP**:
- Entity lifecycle state tracking (spawning, active, despawning, destroyed)
- Network connection state machines (connecting, connected, disconnecting)
- Module lifecycle management (initializing, running, stopping, stopped)

**License**: MIT

---

## Key Features

**Zero-Allocation Runtime**:
- Fixed-size structs (64B/128B/256B for state machine instance)
- Flat array storage (no pointer chasing)
- Pre-allocated event queues (ring buffer)
- Delegate caching (no reflection in hot path)

**Hierarchical States**:
- Nested state support (e.g., `Alive.Moving.Running`)
- Entry/Exit actions fire on state changes
- Transition inheritance (child states inherit parent transitions)

**Event-Driven Design**:
- Priority-based event queues
- Immediate vs. deferred event processing
- Guard conditions (conditional transitions)

**Deterministic Execution**:
- Fixed execution order (priority-sorted events)
- Reproducible state transitions (for testing/replay)
- No hidden state (entire machine state in 64-256 bytes)

**ECS Integration**:
- Designed for Entity Component Systems
- One state machine instance per entity (stored in component)
- Shared state machine definition (immutable, singleton)

---

## Architecture

### State Machine Structure

```
┌─────────────────────────────────────────────────────────┐
│             State Machine Definition                     │
│  (Immutable, shared across all instances)               │
│                                                          │
│  ┌────────┐     ┌────────┐     ┌────────┐              │
│  │ State0 │────▶│ State1 │────▶│ State2 │              │
│  └────┬───┘     └────┬───┘     └────┬───┘              │
│       │              │              │                   │
│       │ OnEntry      │ OnExit       │ Transitions       │
│       │ OnExit       │ OnEntry      │ Guards            │
│       │ Transitions  │ Transitions  │                   │
│       │              │              │                   │
│       └──────────────┴──────────────┘                   │
│                                                          │
│  Event Definitions:                                      │
│  ┌─────────────────────────────────────┐                │
│  │ Event0: Priority=10                 │                │
│  │ Event1: Priority=5                  │                │
│  │ Event2: Priority=1                  │                │
│  └─────────────────────────────────────┘                │
└─────────────────────────────────────────────────────────┘
        │
        │  Instances use shared definition
        ▼
┌─────────────────────────────────────────────────────────┐
│           State Machine Instance (64 bytes)              │
│  ┌────────────────────────────────────────────┐         │
│  │ CurrentStateId:  ushort   (2 bytes)        │         │
│  │ PreviousStateId: ushort   (2 bytes)        │         │
│  │ EventQueue:      RingBuffer<Event> (32B)   │         │
│  │ UserData:        void* (8 bytes)           │         │
│  │ Reserved:        (20 bytes padding)        │         │
│  └────────────────────────────────────────────┘         │
└─────────────────────────────────────────────────────────┘
```

### State Transition Flow

```
1. Post Event
   ├─ Add to EventQueue (priority-sorted)
   └─ Deferred processing

2. Process Events (Tick)
   ├─ Pop highest priority event
   ├─ Evaluate guards (conditions)
   │  ├─ Guard passes? Continue
   │  └─ Guard fails? Try next transition
   ├─ Execute Exit Actions (current state)
   ├─ Change CurrentStateId
   ├─ Execute Entry Actions (new state)
   └─ Repeat until queue empty

3. Hierarchical Transitions
   ├─ Exit child state (OnExit)
   ├─ Exit parent state (OnExit)
   ├─ Enter new parent state (OnEntry)
   └─ Enter new child state (OnEntry)
```

---

## Core API

### 1. Define State Machine

```csharp
using Fhsm.Compiler;
using Fhsm.Kernel;

// Event IDs (constants)
const ushort TimerExpiredEvent = 1;
const ushort InterruptEvent = 2;

// Build state machine definition
var builder = new HsmBuilder("TrafficLight");

// Define states
var red = builder.State("Red")
    .OnEntry("SetLightRed")
    .OnExit("ClearLight");
    
var yellow = builder.State("Yellow")
    .OnEntry("SetLightYellow");
    
var green = builder.State("Green")
    .OnEntry("SetLightGreen");

// Define transitions
red.On(TimerExpiredEvent).GoTo(green);
green.On(TimerExpiredEvent).GoTo(yellow);
yellow.On(TimerExpiredEvent).GoTo(red);

// All states transition to red on interrupt
red.On(InterruptEvent).GoTo(red);
green.On(InterruptEvent).GoTo(red);
yellow.On(InterruptEvent).GoTo(red);

// Set initial state
red.Initial();

// Compile to immutable definition
StateMachineDef definition = builder.Build();
```

### 2. Define Actions

```csharp
using Fhsm.Kernel.Attributes;

[HsmAction(Name = "SetLightRed")]
public static unsafe void SetLightRed(void* instance, void* context, ushort eventId)
{
    var light = (TrafficLight*)context;
    light->Color = LightColor.Red;
    light->Timer = 30.0f; // Red for 30 seconds
}

[HsmAction(Name = "ClearLight")]
public static unsafe void ClearLight(void* instance, void* context, ushort eventId)
{
    var light = (TrafficLight*)context;
    light->Intensity = 0.0f;
}
```

### 3. Runtime Execution

```csharp
// Create instance (per entity)
var instance = new HsmInstance64(); // 64-byte instance
instance.Initialize(definition);

// User data (e.g., traffic light state)
TrafficLight lightData = new TrafficLight();

unsafe
{
    fixed (TrafficLight* lightPtr = &lightData)
    {
        // Post events
        instance.PostEvent(TimerExpiredEvent);
        instance.PostEvent(InterruptEvent); // Higher priority if defined
        
        // Process events (typically per-frame)
        instance.Tick((void*)&instance, (void*)lightPtr);
        
        // Check current state
        ushort currentState = instance.CurrentStateId;
        string stateName = definition.GetStateName(currentState);
    }
}
```

---

## Integration with FDP

### Use Case 1: Entity Lifecycle

```csharp
// Define lifecycle states
var builder = new HsmBuilder("EntityLifecycle");

var spawning = builder.State("Spawning")
    .OnEntry("OnBeginSpawn")
    .OnExit("OnCompleteSpawn");
    
var active = builder.State("Active")
    .OnEntry("OnActivate");
    
var despawning = builder.State("Despawning")
    .OnEntry("OnBeginDespawn")
    .OnExit("OnCompleteDespawn");
    
var destroyed = builder.State("Destroyed")
    .OnEntry("OnDestroy");

// Transitions
spawning.On(SpawnCompleteEvent).GoTo(active);
active.On(DespawnRequestEvent).GoTo(despawning);
despawning.On(DespawnCompleteEvent).GoTo(destroyed);

spawning.Initial();

var lifecycleDef = builder.Build();

// Usage in ECS
public struct EntityLifecycleComponent
{
    public HsmInstance64 StateMachine;
}

public class EntityLifecycleSystem : IModuleSystem
{
    private StateMachineDef _lifecycleDef;
    
    public void Execute(ISimulationView view, float deltaTime)
    {
        var query = view.Query().With<EntityLifecycleComponent>().Build();
        
        foreach (var entity in query)
        {
            ref var lifecycle = ref entity.Get<EntityLifecycleComponent>();
            
            // Post time-based events
            if (ShouldCompleteSpawn(entity))
                lifecycle.StateMachine.PostEvent(SpawnCompleteEvent);
                
            if (ShouldDespawn(entity))
                lifecycle.StateMachine.PostEvent(DespawnRequestEvent);
            
            // Process state machine
            unsafe
            {
                var entityData = entity; // Capture for pointer
                lifecycle.StateMachine.Tick(
                    (void*)&lifecycle.StateMachine,
                    (void*)&entityData
                );
            }
        }
    }
}
```

### Use Case 2: Network Connection State

```csharp
// Connection state machine
var builder = new HsmBuilder("NetworkConnection");

var disconnected = builder.State("Disconnected");
var connecting = builder.State("Connecting")
    .OnEntry("BeginHandshake");
var connected = builder.State("Connected")
    .OnEntry("OnConnectionEstablished")
    .OnExit("OnConnectionLost");
var disconnecting = builder.State("Disconnecting")
    .OnEntry("SendDisconnect");

// Transitions
disconnected.On(ConnectRequestEvent).GoTo(connecting);
connecting.On(HandshakeCompleteEvent).GoTo(connected);
connecting.On(TimeoutEvent).GoTo(disconnected);
connected.On(DisconnectRequestEvent).GoTo(disconnecting);
connected.On(ConnectionLostEvent).GoTo(disconnected);
disconnecting.On(AckReceivedEvent).GoTo(disconnected);

disconnected.Initial();

var connDef = builder.Build();

// Usage
public struct NetworkConnectionComponent
{
    public HsmInstance64 StateMachine;
    public uint RemoteNodeId;
}

[HsmAction(Name = "BeginHandshake")]
public static unsafe void BeginHandshake(void* instance, void* context, ushort eventId)
{
    var conn = (NetworkConnectionComponent*)context;
    // Send SYN packet
    NetworkManager.SendHandshake(conn->RemoteNodeId);
}
```

---

## Performance Characteristics

**State Transition Benchmark** (1M transitions):
```
| Instance Size | Transitions | Time   | Throughput     |
|---------------|-------------|--------|----------------|
| 64 bytes      | 1,000,000   | 12 ms  | 83M trans/s    |
| 128 bytes     | 1,000,000   | 14 ms  | 71M trans/s    |
| 256 bytes     | 1,000,000   | 18 ms  | 55M trans/s    |
```

**Event Processing** (10k instances, 10 events each):
```
| Event Count | Total Events | Time   | Throughput     |
|-------------|--------------|--------|----------------|
| 10          | 100,000      | 8 ms   | 12.5M events/s |
| 100         | 1,000,000    | 78 ms  | 12.8M events/s |
```

**Memory Footprint**:
```
| Component          | Size       |
|--------------------|------------|
| HsmInstance64      | 64 bytes   |
| StateMachineDef    | ~2KB       | (shared, immutable)
| Action Delegates   | ~500 bytes | (cached, shared)
```

---

## Best Practices

**State Design**:
- Keep state count reasonable (< 50 states)
- Use hierarchical states for related substates
- Prefer guard conditions over state explosion

**Event Handling**:
- Assign priorities to critical events (higher = processed first)
- Use deferred events for next-frame processing
- Avoid posting events from within entry/exit actions (use deferred)

**Action Implementation**:
- Keep entry/exit actions fast (< 1ms)
- Use `void*` context to pass entity/component data
- Mark actions with `[HsmAction]` for registration

**Debugging**:
- Use state name queries for logging (`definition.GetStateName(stateId)`)
- Visualize state machines using `Fhsm.Utilities.Visualization`
- Test determinism with fixed event sequences

---

## Comparison with Other FSM Libraries

| Feature                | FastHSM | Stateless | Appccelerate.StateMachine |
|------------------------|---------|-----------|---------------------------|
| Zero-Allocation        | ✅      | ❌        | ❌                        |
| Hierarchical States    | ✅      | ⚠️        | ✅                        |
| Event Queues           | ✅      | ❌        | ✅                        |
| Fixed Memory (64B)     | ✅      | ❌        | ❌                        |
| ECS-Friendly           | ✅      | ⚠️        | ❌                        |
| Deterministic          | ✅      | ⚠️        | ⚠️                        |

---

## Conclusion

**FastHSM** provides production-quality hierarchical state machines with exceptional performance and minimal memory overhead. Its zero-allocation design, cache-friendly structures, and deterministic execution make it ideal for real-time systems and ECS architectures. Integration with FDP enables robust entity lifecycle management, network protocol tracking, and complex behavior modeling.

**Key Strengths**:
- **Performance**: 83M transitions/s, 12M events/s, zero GC pressure
- **Memory Efficiency**: 64-256 byte instances, shared definitions
- **Determinism**: Reproducible execution for testing/replay
- **ECS Integration**: Designed as components with shared definitions

**Recommended for**: Game AI, entity lifecycles, network protocols, UI navigation, robot control systems

---

**Sub-Projects Covered**: 6 (Kernel, Compiler, Utilities, Examples, Demos, Tests)

**Total Lines**: 490
