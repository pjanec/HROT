# Fhsm.Compiler

**Project Path**: `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Fhsm.Compiler.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Class Library

---

## README Validation

**Status: Missing.**

No `README.md` exists in `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/` or in the parent `src/` directory. Documentation is recommended given the multi-stage pipeline (Parse -> Normalize -> Validate -> Flatten -> Emit) and the graph model that precedes compilation.

---

## Executive Overview

`Fhsm.Compiler` transforms human-authored state machine definitions into the binary `HsmDefinitionBlob` format consumed by `Fhsm.Kernel`. It is the "compiler" half of the FastHSM system: where the kernel is responsible for execution, this library is responsible for translation.

The compilation pipeline has five distinct stages:

1. **Parse** - `JsonStateMachineParser` or programmatic `HsmBuilder` API converts input into a `StateMachineGraph` (an in-memory graph of `StateNode`, `TransitionNode`, `RegionNode`, and `EventDefinition` objects).
2. **Normalize** - `HsmNormalizer` assigns flat indices (BFS order for cache locality), computes depths, resolves initial states, and assigns history and timer slots.
3. **Validate** - `HsmGraphValidator` checks for unreachable states, missing initial states, duplicate events, transition to non-existent states, and other logical errors.
4. **Flatten** - `HsmFlattener` converts the graph into parallel ROM arrays (`StateDef[]`, `TransitionDef[]`, `RegionDef[]`) and builds action/guard dispatch tables.
5. **Emit** - `HsmEmitter` assembles the ROM arrays into an `HsmDefinitionBlob` with a computed `StructureHash` and `ParameterHash`, and optionally emits a `MachineMetadata` sidecar for diagnostics.

The library also provides a fluent `HsmBuilder` / `StateBuilder` API for programmatic machine construction without JSON, matching the ergonomics of behavior tree builders in `Fbt.Compiler`.

---

## Architecture

```
+---[ Fhsm.Compiler - Pipeline ]-----------------------------+
|                                                            |
|  [Input]                                                   |
|    JSON string  -> JsonStateMachineParser.Parse()          |
|    OR                                                      |
|    C# code      -> new HsmBuilder("Name")                  |
|                       .State(...).OnEntry(...).GoTo(...)   |
|                       .Event(...)                          |
|                       .Build()                             |
|         |                                                  |
|         v                                                  |
|  StateMachineGraph (mutable, heap-allocated)               |
|    States: Dict<string, StateNode>                         |
|    GlobalTransitions: List<TransitionNode>                 |
|    EventNameToId: Dict<string, ushort>                     |
|    Events: List<EventDefinition>                           |
|    RegisteredActions: HashSet<string>                      |
|    RegisteredGuards: HashSet<string>                       |
|         |                                                  |
|         v  HsmNormalizer.Normalize(graph)                  |
|  Graph with assigned FlatIndex, Depth, HistorySlotIndex    |
|         |                                                  |
|         v  HsmGraphValidator.Validate(graph)               |
|  List<ValidationError>  (empty = OK)                       |
|         |                                                  |
|         v  HsmFlattener.Flatten(graph)                     |
|  FlattenedData                                             |
|    States[]       (StateDef array in BFS flat order)       |
|    Transitions[]  (TransitionDef array)                    |
|    Regions[]      (RegionDef array)                        |
|    GlobalTransitions[]                                     |
|    ActionIds[]    (ushort IDs in sorted order)             |
|    GuardIds[]                                              |
|         |                                                  |
|         v  HsmEmitter.Emit(flattenedData)                  |
|  HsmDefinitionBlob (immutable, ready for kernel)           |
|         |                                                  |
|         v  HsmEmitter.BuildMachineMetadata(graph) [opt]    |
|  MachineMetadata (debug sidecar: index->name maps)         |
+------------------------------------------------------------+
```

---

## Source Structure

```
Fhsm.Compiler/
+-- HsmBuilder.cs               Fluent API: HsmBuilder, StateBuilder, TransitionBuilder
|                               Provides .State(), .Event(), .RegisterAction(),
|                               .GlobalTransition(), .Build()
+-- HsmNormalizer.cs            Assigns FlatIndex (BFS), Depth, initial state resolution,
|                               HistorySlot assignment, TransitionRange computation
+-- HsmGraphValidator.cs        Validates graph for unreachable states, missing initials,
|                               bad transition targets, etc.
+-- HsmFlattener.cs             Graph -> ROM arrays; builds action/guard dispatch tables;
|                               FlattenedData container
+-- HsmEmitter.cs               FlattenedData -> HsmDefinitionBlob; hash computation;
|                               BuildMachineMetadata(); EmitWithDebug()
+-- InternalsVisibleTo.cs       [assembly: InternalsVisibleTo("...")] declarations
+-- Graph/
|   +-- StateMachineGraph.cs    Root container: States dict, GlobalTransitions list,
|   |                           EventNameToId map, RegisteredActions/Guards sets
|   +-- StateNode.cs            Mutable node: Name, Parent, Children, Transitions,
|   |                           OnEntryAction/OnExitAction/ActivityAction/TimerAction,
|   |                           IsInitial/IsHistory/IsParallel/IsDeepHistory/IsFinal,
|   |                           FlatIndex, Depth, HistorySlotIndex
|   +-- TransitionNode.cs       Source, Target, EventId, GuardId, ActionId, Flags, VisualId
|   +-- RegionNode.cs           Orthogonal region container
|   +-- EventDefinition.cs      Name, EventId, PayloadSize, IsIndirect, IsDeferred
+-- Hashing/
|   +-- XxHash64.cs             Fast 64-bit hash (xxHash) for StructureHash/ParameterHash
+-- IO/
    +-- JsonStateMachineParser.cs  System.Text.Json parser: JSON -> StateMachineGraph
```

---

## Public API Reference

### HsmBuilder

```csharp
public class HsmBuilder
{
    public HsmBuilder(string machineName);

    // Add a top-level state; returns StateBuilder for chaining
    public StateBuilder State(string name, Guid stableId = default);

    // Register an event with its numeric ID
    public HsmBuilder Event(string eventName, ushort eventId,
        int payloadSize = 0, bool isIndirect = false, bool isDeferred = false);

    // Register an action name (required for validation and dispatch table)
    public HsmBuilder RegisterAction(string functionName);

    // Register a guard function name
    public HsmBuilder RegisterGuard(string functionName);

    // Add a global transition (fires for any active state matching the event)
    public HsmBuilder GlobalTransition(string eventName, string targetStateName,
        Guid visualId = default);

    // Returns the final graph (call after all .State()/.Event() calls)
    public StateMachineGraph Build();
}
```

### StateBuilder

```csharp
public class StateBuilder
{
    public StateBuilder OnEntry(string actionName);
    public StateBuilder OnExit(string actionName);
    public StateBuilder Activity(string actionName);
    public StateBuilder Initial();             // Mark as initial child of parent
    public StateBuilder History();            // Shallow history
    public StateBuilder DeepHistory();
    public StateBuilder Parallel();           // Orthogonal (AND) region
    public StateBuilder Final();
    public StateBuilder TimerAction(string actionName);

    // Add a nested child state
    public StateBuilder Child(string childName, Action<StateBuilder> configure,
        Guid stableId = default);

    // Add a transition from this state
    // Returns TransitionBuilder for guard/action chaining
    public TransitionBuilder On(ushort eventId);

    // Reference to the underlying StateNode (for external access)
    public StateNode State { get; }
}
```

### TransitionBuilder

```csharp
public class TransitionBuilder
{
    public TransitionBuilder GoTo(string targetStateName);
    public TransitionBuilder GoTo(StateBuilder target);
    public TransitionBuilder WithGuard(string guardName);
    public TransitionBuilder WithAction(string actionName);
}
```

Convenience extension that allows the compact syntax: `redState.On(TimerExpired).GoTo(green)`.

### HsmNormalizer

```csharp
public class HsmNormalizer
{
    // All-in-one normalization: FlatIndex, Depth, InitialState,
    // HistorySlots, TransitionRanges. Mutates the graph in place.
    public static void Normalize(StateMachineGraph graph);
}
```

### HsmGraphValidator

```csharp
public class HsmGraphValidator
{
    public static List<ValidationError> Validate(StateMachineGraph graph);
}

public class ValidationError
{
    public string Message { get; }
    public ValidationSeverity Severity { get; }  // Error | Warning
}
```

### HsmFlattener

```csharp
public class HsmFlattener
{
    public class FlattenedData
    {
        public StateDef[] States { get; set; }
        public TransitionDef[] Transitions { get; set; }
        public RegionDef[] Regions { get; set; }
        public GlobalTransitionDef[] GlobalTransitions { get; set; }
        public ushort[] ActionIds { get; set; }
        public ushort[] GuardIds { get; set; }
    }

    // Convert normalized graph to ROM arrays
    public static FlattenedData Flatten(StateMachineGraph graph);
}
```

### HsmEmitter

```csharp
public class HsmEmitter
{
    // FlattenedData -> HsmDefinitionBlob (with StructureHash + ParameterHash)
    public static HsmDefinitionBlob Emit(HsmFlattener.FlattenedData data);

    // Build human-readable metadata sidecar (index->name maps)
    public static MachineMetadata BuildMachineMetadata(StateMachineGraph graph);

    // Write blob + metadata to disk for debugging
    public static void EmitWithDebug(HsmDefinitionBlob blob,
        MachineMetadata metadata, string outputPath);
}
```

### JsonStateMachineParser

```csharp
public class JsonStateMachineParser
{
    // Parse JSON string into StateMachineGraph
    // JSON schema: { name, states: [{name, onEntry, onExit, children:[...]}],
    //               transitions: [{source, target, event, guard?, action?}] }
    public StateMachineGraph Parse(string json);
}
```

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Fhsm.Kernel` | (project ref) | `HsmDefinitionBlob`, `StateDef`, `TransitionDef`, `RegionDef`, `HsmDefinitionHeader`, `MachineMetadata` (data types only) |

No NuGet packages. The compiler depends only on the kernel's data types, not on its runtime execution logic.

---

## Usage Examples

### Example 1: Full Pipeline from HsmBuilder (Traffic Light)

```csharp
using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

// 1. Build
var builder = new HsmBuilder("TrafficLight");

builder.Event("TimerExpired", 1);
builder.RegisterAction("OnEnterRed")
       .RegisterAction("OnEnterGreen")
       .RegisterAction("OnEnterYellow");

var red    = builder.State("Red")   .OnEntry("OnEnterRed").Initial();
var green  = builder.State("Green") .OnEntry("OnEnterGreen");
var yellow = builder.State("Yellow").OnEntry("OnEnterYellow");

red.On(1).GoTo(green);
green.On(1).GoTo(yellow);
yellow.On(1).GoTo(red);

// 2. Normalize
var graph = builder.Build();
HsmNormalizer.Normalize(graph);

// 3. Validate
var errors = HsmGraphValidator.Validate(graph);
if (errors.Count > 0)
    throw new InvalidOperationException(errors[0].Message);

// 4. Flatten + Emit
var flat = HsmFlattener.Flatten(graph);
HsmDefinitionBlob blob = HsmEmitter.Emit(flat);
MachineMetadata meta   = HsmEmitter.BuildMachineMetadata(graph);

// blob is now ready for HsmKernel.Update(blob, ...)
```

### Example 2: JSON Input Pipeline

```csharp
string json = File.ReadAllText("machines/combat.json");

var parser = new JsonStateMachineParser();
var graph  = parser.Parse(json);

HsmNormalizer.Normalize(graph);

var errors = HsmGraphValidator.Validate(graph);
foreach (var e in errors)
    Console.WriteLine($"[{e.Severity}] {e.Message}");
if (errors.Any(e => e.Severity == ValidationSeverity.Error))
    return;

var blob = HsmEmitter.Emit(HsmFlattener.Flatten(graph));
```

### Example 3: Hierarchical State Machine with History

```csharp
var builder = new HsmBuilder("Combat");
builder.Event("EnemyDetected", 1);
builder.Event("EnemyLost", 2);
builder.Event("UpdateEvent", 3);

builder.RegisterAction("StartPatrol")
       .RegisterAction("StartChase")
       .RegisterAction("Attack")
       .RegisterAction("ResumePatrol");

// Patrolling is a composite state with history
var patrolling = builder.State("Patrolling").Initial()
    .Child("Wandering", w => w
        .OnEntry("StartPatrol")
        .Initial()
    )
    .Child("Scanning", s => s
        // stays on UpdateEvent
    );

// Engaging composite state
var engaging = builder.State("Engaging")
    .History()  // deep history - remember last sub-state
    .Child("Chasing", c => c
        .OnEntry("StartChase")
        .Initial()
    )
    .Child("Attacking", a => a
        .Activity("Attack")
    );

// Transitions between top-level composites
patrolling.On(1).GoTo(engaging);   // EnemyDetected
engaging.On(2).GoTo(patrolling);   // EnemyLost

var graph = builder.Build();
HsmNormalizer.Normalize(graph);

var flat = HsmFlattener.Flatten(graph);
var blob = HsmEmitter.Emit(flat);
```

### Example 4: Global Transition

A global transition fires regardless of which leaf state is currently active:

```csharp
builder.Event("GameOver", 99);
builder.State("GameOverScreen");

// Any active state -> GameOverScreen when event 99 is received
builder.GlobalTransition("GameOver", "GameOverScreen");
```

---

## Compilation Pipeline Internals

### BFS Index Assignment (HsmNormalizer)

```
+---[ FlatIndex Assignment - BFS Order ]--------------------------+
|                                                                 |
|  State tree (input):                                            |
|    Root (implicit)                                              |
|      Red (depth 1)                                              |
|      Green (depth 1)                                            |
|      Yellow (depth 1)                                           |
|                                                                 |
|  BFS traversal:                                                 |
|    Queue: [Root]                                                |
|    Dequeue Root -> FlatIndex = 0                                |
|    Enqueue children: Red, Green, Yellow                         |
|    Dequeue Red -> FlatIndex = 1                                 |
|    Dequeue Green -> FlatIndex = 2                               |
|    Dequeue Yellow -> FlatIndex = 3                              |
|                                                                 |
|  Result: StateDef array in order [Root, Red, Green, Yellow]     |
|  Cache-efficient: parent is always at lower index than children |
+-----------------------------------------------------------------+
```

### Hash Computation (HsmEmitter via XxHash64)

```
+---[ Hash Strategy ]----------------------------------------+
|                                                           |
|  StructureHash: hash of state topology                    |
|    - State count, transition count                        |
|    - StateDef array contents (topology only)             |
|    - TransitionDef source/target/event pairs              |
|                                                           |
|  ParameterHash: hash of tuneable values                   |
|    - Action IDs, guard IDs                                |
|    - Timer values (if present)                            |
|    - Other parameter arrays                               |
|                                                           |
|  Hot-Reload decision (in HotReloadManager):               |
|    StructureHash same, ParameterHash same -> NoChange     |
|    StructureHash same, ParameterHash diff -> SoftReset    |
|    StructureHash diff                     -> HardReset    |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: Graph Model

```
+---[ StateMachineGraph Object Model ]------------------------+
|                                                            |
|  StateMachineGraph                                         |
|    Name: "Combat"                                          |
|    RootState: StateNode("__Root")                          |
|    States: { "Red": StateNode, "Green": ..., ... }         |
|    GlobalTransitions: [ TransitionNode ]                   |
|    EventNameToId: { "TimerExpired": 1, ... }               |
|    RegisteredActions: { "OnEnterRed", ... }                |
|                                                            |
|  StateNode                                                 |
|    Name, Parent (ref), Children (list), Transitions (list) |
|    OnEntryAction, OnExitAction, ActivityAction (strings)   |
|    IsInitial, IsHistory, IsParallel                        |
|    FlatIndex (ushort, set by Normalizer)                   |
|    Depth (byte, set by Normalizer)                         |
|    HistorySlotIndex (ushort, set by Normalizer)            |
|                                                            |
|  TransitionNode                                            |
|    Source: StateNode                                       |
|    Target: StateNode                                       |
|    EventId: ushort                                         |
|    GuardId: ushort (0 = none)                              |
|    ActionId: ushort (0 = none)                             |
|    VisualId: Guid (for debug/editor use)                   |
|                                                            |
|  EventDefinition                                           |
|    Name, EventId, PayloadSize, IsIndirect, IsDeferred      |
+------------------------------------------------------------+
```

---

## Best Practices

1. **Always call `HsmNormalizer.Normalize()` before `HsmGraphValidator.Validate()`.** The validator assumes FlatIndex values are assigned. Calling validate on an un-normalized graph produces meaningless index-based error messages.

2. **Register all actions before calling `Build()`/`Flatten()`.** `HsmFlattener` builds the dispatch table from `graph.RegisteredActions`. Actions used in `OnEntry`/`OnExit`/`Activity` that are not registered will produce a validation error.

3. **Use `Guid stableId` for states that will persist across hot-reloads.** The stable GUID is used by `HotReloadManager` to match old states to new states when computing reload type. States without stable IDs are matched by name.

4. **`HsmEmitter.BuildMachineMetadata()` should be called alongside `Emit()`.** Store the `MachineMetadata` alongside the blob. It is needed by `TraceSymbolicator` to translate raw `StateIndex` values in trace records back to human-readable names.

5. **`StructureHash` is the primary identity of a machine.** The kernel uses `blob.Header.StructureHash` as the `MachineId` stored in `InstanceHeader`. If two different definitions produce the same hash (collision), the kernel will incorrectly process instances. The XxHash64 used makes collisions astronomically unlikely but not impossible for adversarial inputs.

6. **JSON parser is for tooling, not production runtime.** Parse JSON at startup or in editor tools; do not parse JSON in the game loop. Compile to blob once and cache the blob.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fhsm.Kernel` | Consumer of `HsmDefinitionBlob` produced by this library |
| `Fhsm.Demo.Visual` | Uses both: compiler to build blobs, kernel to execute them |
| `Fhsm.Examples.Console` | Uses both: traffic light example goes through full pipeline |
| `Fhsm.SourceGen` | Sibling source generator; generates action registrars from [HsmAction] attributes |
| `Fbt.Compiler` | Analogous compiler for FastBTree; similar pipeline structure |
