# Fhsm.Demo.Visual

**Project Path**: `FDP/ExtDeps/FastHSM/demos/Fhsm.Demo.Visual/Fhsm.Demo.Visual.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Executable

---

## README Validation

**Status: Up-to-date.**

A `README.md` exists in the project folder (`demos/Fhsm.Demo.Visual/README.md`). It accurately describes:
- The three state machines (Patrol, Gather, Combat with history states)
- Controls (mouse click selection, zoom, inject events)
- The architecture overview
- Performance characteristics (60 FPS, 20+ agents, zero-allocation runtime)
- The state machine viewer features

No divergence detected between the README and the source code. The README mentions "Manual Events: Buttons to inject events for testing" which is confirmed by the `StateMachineVisualizer` UI class.

---

## Executive Overview

`Fhsm.Demo.Visual` is the interactive showcase for the FastHSM library. It mirrors the structure of `Fbt.Demo.Visual` but demonstrates hierarchical state machines (HSMs) instead of behavior trees. Multiple 2D agents simultaneously run one of three compiled state machines (Patrol, Gather, Combat), and the developer can select any agent to inspect its full state hierarchy, active states, event history, and internal context in real time via ImGui panels.

The demo teaches:
1. **Full compilation pipeline**: The three state machine definitions are built at application startup using `HsmBuilder`, compiled through the full `Normalize -> Validate -> Flatten -> Emit` pipeline, and stored as `HsmDefinitionBlob` objects.
2. **Source-generated action registration**: Actions are tagged with `[HsmAction]` and registered by calling the generated `HsmActionRegistrar.RegisterAll()` at startup - no manual dispatch table population.
3. **HsmInstance64 in practice**: Every agent carries a 64-byte `HsmInstance64` struct as part of its entity state. The demo shows that even complex hierarchical machines (Combat has a 2-level hierarchy with history states and interrupt transitions) fit comfortably in 64 bytes.
4. **Event injection from UI**: The `StateMachineVisualizer` provides buttons to inject events (e.g., `EnemyDetected`, `EnemyLost`) directly into a selected agent's event queue, enabling interactive testing of transitions.
5. **MachineMetadata for visualization**: The sidecar `MachineMetadata` (produced by `HsmEmitter.BuildMachineMetadata()`) provides the `StateNames`, `EventNames`, and `ActionNames` maps that allow the visualizer to display human-readable labels instead of raw ushort IDs.

---

## Architecture

```
+---[ Fhsm.Demo.Visual - Component Map ]----------------------+
|                                                             |
|  DemoApp                                                    |
|    _agents: List<Agent>                                     |
|    _machines: Dict<string, HsmDefinitionBlob>               |
|    _machineMetadata: Dict<string, MachineMetadata>          |
|    _behaviorSystem: BehaviorSystem                          |
|    _renderSystem: RenderSystem                              |
|    _smVisualizer: StateMachineVisualizer                    |
|    _camera: Camera2D                                        |
|                                                             |
|  BehaviorSystem                                             |
|    Builds all three HsmDefinitionBlob at startup            |
|    InitializeAgent(agent)                                   |
|    Update(agents, dt)                                       |
|      -> HsmKernel.UpdateBatch(blob, instances, ctx, dt)     |
|         per machine type                                    |
|                                                             |
|  RenderSystem                                               |
|    RenderAgents(agents, selectedAgent, metadata, time)      |
|      -> draws circles, direction, status labels             |
|                                                             |
|  StateMachineVisualizer (ImGui)                             |
|    Render(agent, blob, metadata, time)                      |
|      -> state hierarchy panel                               |
|      -> context panel (blackboard)                          |
|      -> event injection buttons                             |
+-------------------------------------------------------------+
```

```
+---[ Initialization Sequence ]-------------------------------+
|                                                             |
|  DemoApp.Initialize()                                       |
|    Generated.HsmActionRegistrar.RegisterAll()               |
|      -> calls HsmActionDispatcher.RegisterAction() for each |
|         [HsmAction]-tagged method in Actions.cs             |
|                                                             |
|    BehaviorSystem ctor:                                     |
|      MachineDefinitions.CreatePatrolMachine()               |
|        -> HsmBuilder -> Normalize -> Validate -> Flatten    |
|        -> HsmEmitter.Emit()     -> HsmDefinitionBlob        |
|        -> HsmEmitter.BuildMachineMetadata() -> sidecar      |
|      CreateGatherMachine() [same pipeline]                  |
|      CreateCombatMachine() [same pipeline]                  |
|                                                             |
|    SpawnPatrolAgents(5), SpawnGatherAgents(3),              |
|    SpawnCombatAgents(2)                                     |
|      -> new Agent(id, pos, "patrol", role)                  |
|      -> BehaviorSystem.InitializeAgent(agent)               |
|           HsmInstanceManager.Initialize(&inst, blob)        |
|           HsmKernel.Trigger(ref inst)                       |
+-------------------------------------------------------------+
```

---

## Source Structure

```
Fhsm.Demo.Visual/
+-- Program.cs                  Entry point; creates DemoApp and calls Run()
+-- DemoApp.cs                  Main application: Raylib window, camera, game loop,
|                               agent spawning, system orchestration, ImGui UI
+-- MachineDefinitions.cs       Static factory for the three HsmDefinitionBlob objects
|                               (Patrol, Gather, Combat) using HsmBuilder pipeline
+-- MachineMetadata.cs          Simple sidecar container: StateNames, EventNames,
|                               ActionNames dictionaries (local copy of kernel type)
+-- Actions.cs                  All [HsmAction]-tagged static action methods;
|                               uses AgentContext* for per-agent state access
+-- Entities/
|   +-- Agent.cs                Agent entity: HsmInstance64 inline, AgentContext struct,
|                               Position, TargetPosition, Role, visual state
+-- Systems/
|   +-- BehaviorSystem.cs       HSM update loop; per-machine blob management;
|   |                           agent initialization; event posting from actions
|   +-- RenderSystem.cs         Raylib-based agent rendering with status labels
+-- UI/
|   +-- StateMachineVisualizer.cs  ImGui panel: active state tree, context data,
|                                  event injection buttons, transition history
+-- Data/
    +-- combat.json             JSON definition of the Combat state machine
    +-- gather.json             JSON definition of the Gather state machine
    +-- patrol.json             JSON definition of the Patrol state machine
```

---

## The Three State Machines

### Patrol (3 flat states)

```
+-- SelectingPoint (initial)
|     OnEntry: FindPatrolPoint -> posts PointSelected event
|
+-- Moving
|     Activity: MoveToTarget -> posts Arrived when close enough
|
+-- Waiting
      (no actions; TimerExpired fired by kernel timer)

Transitions:
  SelectingPoint --[PointSelected]--> Moving
  Moving         --[Arrived]-------> Waiting
  Waiting        --[TimerExpired]--> SelectingPoint
```

### Gather (5 flat states)

```
+-- Searching (initial)
|     OnEntry: FindResource -> posts ResourceFound
+-- MovingToResource
|     Activity: MoveToResource -> posts Arrived
+-- Harvesting
|     OnEntry: Gather -> posts ResourceCollected
+-- MovingToBase
|     Activity: MoveToBase -> posts Arrived
+-- Depositing
      OnEntry: DepositResources -> posts ResourcesDeposited

Transitions:
  Searching       --[ResourceFound]------> MovingToResource
  MovingToResource--[Arrived]-----------> Harvesting
  Harvesting      --[ResourceCollected]--> MovingToBase
  MovingToBase    --[Arrived]-----------> Depositing
  Depositing      --[ResourcesDeposited]-> Searching
```

### Combat (2-level hierarchy with history and interrupt transitions)

```
+-- Patrolling (initial, composite)
|     +-- Wandering (initial leaf)
|     +-- Scanning  (leaf)
|     Transitions (internal):
|       Wandering --[UpdateEvent]--> Scanning
|       Scanning  --[UpdateEvent]--> Wandering
|
+-- Engaging (composite, HISTORY state)
      +-- Chasing  (initial leaf)
      |     OnEntry: StartChase
      +-- Attacking (leaf)
            Activity: Attack

Transitions (top-level):
  Patrolling --[EnemyDetected, INTERRUPT]--> Engaging
  Engaging   --[EnemyLost]----------------> Patrolling
  Chasing    --[Arrived]-------------------> Attacking
  Attacking  --[UpdateEvent]-------------->  Chasing
```

The `History()` flag on `Engaging` means that when `EnemyLost` fires and then `EnemyDetected` re-fires, the agent resumes from whichever sub-state of `Engaging` it was in last (Chasing or Attacking), rather than always starting from Chasing.

---

## Key Types

### Agent

```csharp
public class Agent
{
    public int Id { get; }
    public Vector2 Position { get; set; }
    public Vector2 TargetPosition { get; set; }
    public float Speed;
    public AgentRole Role;
    public Color Color;

    // The HSM instance - embedded inline (64B struct, no heap alloc)
    public HsmInstance64 Instance;

    // Context passed to every action call
    public AgentContext Context;

    // Visual state
    public float AttackFlashTimer;
    public string MachineName;

    public void UpdateMovement(float dt);
}

public enum AgentRole { Patrol, Gather, Combat }
```

### AgentContext

```csharp
// Passed as void* context to every [HsmAction] method
public struct AgentContext
{
    public int AgentId;
    public Vector2 TargetPosition;
    public float DistanceToTarget;
    public int PatrolPointIndex;
    public float WorldTime;
    public float DeltaTime;
}
```

### MachineMetadata (local)

```csharp
public class MachineMetadata
{
    public Dictionary<ushort, string> StateNames { get; set; }
    public Dictionary<ushort, string> EventNames { get; set; }
    public Dictionary<ushort, string> ActionNames { get; set; }
}
```

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2D rendering, window, input, camera |
| `ImGui.NET` | 1.91.6.1 | Immediate-mode debug UI panels |
| `rlImgui-cs` | 3.2.0 | Bridge between Raylib and ImGui.NET |
| `Fhsm.Kernel` | (project ref) | `HsmInstance64`, `HsmKernel`, `HsmInstanceManager`, `HsmEventQueue`, `HsmDefinitionBlob` |
| `Fhsm.Compiler` | (project ref) | `HsmBuilder`, `HsmNormalizer`, `HsmGraphValidator`, `HsmFlattener`, `HsmEmitter` |
| `Fhsm.SourceGen` | (analyzer) | Generates `HsmActionRegistrar.RegisterAll()` from [HsmAction] attributes |

Data JSON files in `Data/` are configured as `CopyToOutputDirectory: PreserveNewest`.

---

## Usage Examples

### Example 1: Running the Demo

```bash
cd FDP/ExtDeps/FastHSM/demos/Fhsm.Demo.Visual
dotnet run
```

A 1280x720 Raylib window opens showing:
- 5 blue patrol agents wandering between random waypoints
- 3 green gather agents cycling between resource sites and base
- 2 red combat agents patrolling with interrupt transitions to "Engaging" mode

Left-click an agent to open the state machine inspector. The active states are highlighted green in the hierarchy view. Use the "Inject Event" buttons to manually trigger transitions and observe the history state behavior on the Combat agents.

### Example 2: Building a State Machine Programmatically

From `MachineDefinitions.CreatePatrolMachine()`:

```csharp
var builder = new HsmBuilder("Patrol");

// Declare events
builder.Event("PointSelected", MachineDefinitions.PointSelected);
builder.Event("Arrived",       MachineDefinitions.Arrived);
builder.Event("TimerExpired",  MachineDefinitions.TimerExpired);

// Declare actions (must match [HsmAction] names in Actions.cs)
builder.RegisterAction("FindPatrolPoint");
builder.RegisterAction("MoveToTarget");

// Define states
var selecting = builder.State("SelectingPoint")
    .OnEntry("FindPatrolPoint")
    .Initial();

var moving = builder.State("Moving")
    .Activity("MoveToTarget");

var waiting = builder.State("Waiting");

// Wire transitions
selecting.On(MachineDefinitions.PointSelected).GoTo(moving);
moving.On(MachineDefinitions.Arrived).GoTo(waiting);
waiting.On(MachineDefinitions.TimerExpired).GoTo(selecting);

// Compile
var graph    = builder.Build();
HsmNormalizer.Normalize(graph);

var errors = HsmGraphValidator.Validate(graph);
// assert errors.Count == 0

var flat = HsmFlattener.Flatten(graph);
var blob = HsmEmitter.Emit(flat);
var meta = HsmEmitter.BuildMachineMetadata(graph);
```

### Example 3: Action Method Pattern

All action methods follow the same `unsafe static void` signature:

```csharp
[HsmAction(Name = "FindPatrolPoint")]
public static void FindPatrolPoint(
    void* instance,          // pointer to HsmInstance64 (or 128/256)
    void* context,           // pointer to AgentContext struct
    HsmCommandWriter* writer) // optional command buffer
{
    var ctx = (AgentContext*)context;
    var agent = GetAgent(ctx);  // lookup from static _agentLookup

    // Compute new patrol target
    var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
    ctx->TargetPosition = target;
    agent.TargetPosition = target;

    // Post event to advance the state machine
    var evt = new HsmEvent { EventId = MachineDefinitions.PointSelected };
    fixed (HsmInstance64* instPtr = &agent.Instance)
    {
        HsmEventQueue.TryEnqueue(instPtr, 64, evt);
    }
}
```

The action posts an event back into its own instance's queue. This is the canonical pattern for "action triggers transition": the action fires, posts an event, and on the next tick the kernel processes the event and executes the matching transition.

---

## Architecture Diagram: Action Event Loop

```
+---[ Action -> Event -> Transition Loop ]-------------------+
|                                                           |
|  Tick N:                                                  |
|    BehaviorSystem.Update(agents, dt)                      |
|      HsmKernel.UpdateBatch(patrolBlob, instances, ctx, dt)|
|        For agent in Patrol "SelectingPoint" state:        |
|          Phase = Activity (no activity action here)       |
|          Phase = Idle -> ProcessTimers                    |
|          Queue empty -> stay Idle                         |
|        (nothing happens until first frame Entry fires)    |
|                                                           |
|  InitializeMachine (first tick after Trigger):            |
|    Agent enters "SelectingPoint"                          |
|    OnEntry "FindPatrolPoint" fires                        |
|    FindPatrolPoint() posts PointSelected event            |
|    Queue: [PointSelected]                                 |
|                                                           |
|  Tick N+1:                                                |
|    Phase = Entry: ProcessEventPhase                       |
|      Dequeue PointSelected -> Phase = RTC                 |
|    Phase = RTC: ProcessRTCPhase(PointSelected)            |
|      Evaluate SelectingPoint transitions                  |
|      Match: [PointSelected] -> Moving                     |
|      Execute: OnExit SelectingPoint (none)                |
|               OnEntry Moving (none)                       |
|      Update ActiveLeafIds -> Moving                       |
|    Phase = Activity: Activity "MoveToTarget" fires        |
|      MoveToTarget() checks distance, posts Arrived if <10 |
|    Phase = Idle                                           |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: Agent Context Lookup

```
+---[ Global Agent Lookup Table ]----------------------------+
|                                                           |
|  Problem: [HsmAction] methods receive void* context       |
|  The context is AgentContext* (a value-type struct with    |
|  AgentId field). There is no direct Agent reference.      |
|                                                           |
|  Solution: static Dictionary<int, Agent> _agentLookup     |
|    Populated by BehaviorSystem when agents are created.   |
|    Actions cast void* context to AgentContext*, read      |
|    AgentId, then call GetAgent(ctx) to get the Agent.     |
|                                                           |
|  Actions.SetAgentLookup(agentDict);  // called at startup |
|                                                           |
|  void* context --> (AgentContext*)context --> ctx->AgentId|
|    --> _agentLookup[id] --> Agent reference               |
|                                                           |
|  Trade-off: static global table is a concurrency concern. |
|  The demo is single-threaded, so this is acceptable.      |
|  Production code would pass context inline or use IDs.    |
+-----------------------------------------------------------+
```

---

## Best Practices Illustrated

1. **Action methods post events - they do not change state directly.** The action `FindPatrolPoint` posts `PointSelected` to the event queue rather than setting `ActiveLeafIds` directly. State transitions are always driven through the event queue and the kernel's RTC phase.

2. **`HsmActionRegistrar.RegisterAll()` must be called before any `Update()`.** The source-generated registrar populates `HsmActionDispatcher`'s static tables. If omitted, all action calls are silently no-ops because the dispatch table is empty.

3. **`MachineMetadata` is essential for the visualizer.** The `StateMachineVisualizer` displays `StateNames[activeLeafId]` rather than raw ushort values. Always build and keep the metadata alongside the blob.

4. **`HsmInstance64` is embedded by value in `Agent`.** No heap allocation per agent. The agent array is a `List<Agent>` but each `Agent.Instance` struct is stored inline in the `Agent` class's field. For maximum cache efficiency, use `Agent[]` or `NativeArray<HsmInstance64>` with separate entity data.

5. **Combat machine's `History()` flag demonstrates interrupt semantics.** When an agent is interrupted by `EnemyDetected` while `Scanning`, returns to `Patrolling` via `EnemyLost`, then gets `EnemyDetected` again - it resumes `Engaging.Scanning` (the last remembered sub-state), not `Engaging.Chasing` (the initial). This is the UML shallow history behavior.

6. **`TimerExpired` is a kernel-managed event, not manually posted.** The patrol machine's `Waiting` state uses a kernel timer slot (`TimerSlotIndex`). The kernel itself fires `TimerExpiredEvent (0xFFFE)` when the deadline is reached. No manual timer management code is needed in the actions.

---

## Extended: StateMachineVisualizer Panel Layout

The `StateMachineVisualizer` renders the following ImGui panels when an agent is selected:

**Panel 1: Agent Info**
```
Agent ID: 4   Role: Combat
Position: (342, 218)   Target: (780, 450)
Speed: 50.0   Distance: 512.3
Machine: combat
```

**Panel 2: Active State Tree**
```
State Hierarchy:
  [0] __Root
    [1] Patrolling          <- ACTIVE (green)
      [2] Wandering         <- ACTIVE LEAF (bright green)
      [3] Scanning
  [4] Engaging
    [5] Chasing
    [6] Attacking
```

Active states are highlighted green. The current active leaf is highlighted bright green. The hierarchy depth corresponds to the `StateDef.Depth` field.

**Panel 3: Context Data**
```
AgentId:          4
PatrolPointIndex: 2
DistanceToTarget: 512.3
WorldTime:        14.72
```

**Panel 4: Inject Events**
```
[EnemyDetected]  [EnemyLost]  [UpdateEvent]
[PointSelected]  [Arrived]    [TimerExpired]
```

Clicking an event button calls `HsmEventQueue.TryEnqueue` directly, allowing manual testing of transitions in the running simulation.

---

## Extended: Adding a Fourth Machine

To add a "Scout" state machine to the demo:

1. Add event constants to `MachineDefinitions.cs`:

```csharp
public const ushort ScopeAcquired = 10;
public const ushort ScopeTimeout  = 11;
```

2. Add a factory method:

```csharp
public static (HsmDefinitionBlob, MachineMetadata) CreateScoutMachine()
{
    var builder = new HsmBuilder("Scout");

    builder.Event("ScopeAcquired", ScopeAcquired);
    builder.Event("ScopeTimeout",  ScopeTimeout);

    builder.RegisterAction("AdvanceToOverwatch");
    builder.RegisterAction("Observe");

    var advancing = builder.State("Advancing")
        .Activity("AdvanceToOverwatch")
        .Initial();

    var observing = builder.State("Observing")
        .Activity("Observe");

    advancing.On(ScopeAcquired).GoTo(observing);
    observing.On(ScopeTimeout).GoTo(advancing);

    return CompileAndEmit(builder);
}
```

3. Add corresponding `[HsmAction]` methods in `Actions.cs`:

```csharp
[HsmAction(Name = "AdvanceToOverwatch")]
public static void AdvanceToOverwatch(void* instance, void* context, HsmCommandWriter* writer)
{
    var ctx = (AgentContext*)context;
    var agent = GetAgent(ctx);
    agent.TargetPosition = new Vector2(_random.Next(100, 1180), 50); // edge of screen
    // post arrival event when close
}
```

4. Spawn scout agents in `DemoApp.Initialize()`.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fhsm.Kernel` | Runtime: `HsmKernel`, `HsmInstance64`, `HsmInstanceManager`, `HsmEventQueue` |
| `Fhsm.Compiler` | Build-time: `HsmBuilder`, normalization, validation, flatten, emit pipeline |
| `Fhsm.SourceGen` | Analyzer: generates `HsmActionRegistrar.RegisterAll()` from [HsmAction] attributes |
| `Fhsm.Examples.Console` | Simpler sibling demo showing the traffic light example without Raylib |
| `Fbt.Demo.Visual` | Analogous demo for FastBTree; same Raylib+ImGui architecture pattern |
| HROT AI | Production consumer of `Fhsm.Kernel` using the same patterns shown here |
