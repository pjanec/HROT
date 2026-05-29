# Fhsm.Kernel

**Project Path**: `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Fhsm.Kernel.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Class Library
**Authors**: Antigravity
**Description**: High-performance, cache-friendly hierarchical state machine library for .NET

---

## README Validation

**Status: Missing.**

No `README.md` exists in `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` or in the `FastHSM/` root. Given the complexity and importance of this library (used by HROT AI), a README is strongly recommended. The closest thing is the csproj description field: "High-performance, cache-friendly hierarchical state machine library for .NET".

---

## Executive Overview

`Fhsm.Kernel` is the runtime engine for FastHSM, a high-performance Hierarchical State Machine (HSM) library targeting game AI workloads. It implements a UML-compliant HSM execution model with zero heap allocations per tick, fixed-size instance memory in three tiers, and a type-erased void-pointer core that avoids generic code expansion for batch processing.

The library is used by the HROT AI system to drive unit behavior. Each AI unit is represented by a `HsmInstance64`, `HsmInstance128`, or `HsmInstance256` struct - a fixed-size block of memory that holds all execution state (active state IDs, event queue, timers, history slots). The kernel processes batches of these instances per frame, firing entry/exit/activity/transition actions and advancing the machine's phase.

Key design properties:
- **Zero-allocation tick loop.** All instance memory is pre-allocated in fixed-size structs. The kernel uses `unsafe` code and pointer arithmetic.
- **Three memory tiers.** 64B (crowd AI), 128B (standard enemies), 256B (complex units with history and parallel regions). Tier selection is based on state count, depth, and history slot requirements.
- **Phase-based execution.** Each instance transitions through phases: `Idle -> Entry -> RTC (Run-To-Completion) -> Activity` each tick. This ensures UML-correct RTC semantics.
- **Struct-based event queue.** Events are 24-byte structs with inline payloads, queued inside the instance memory. No heap allocation for event posting.
- **Hot-reload support.** The `HotReloadManager` compares structure and parameter hashes between old and new definition blobs to decide between soft reload (preserve state) and hard reset.

---

## Architecture

```
+---[ Fhsm.Kernel - Component Map ]---------------------------+
|                                                             |
|  HsmKernel (public static API)                              |
|    UpdateBatch<TInstance, TContext>(def, span, ctx, dt)     |
|    Update<TInstance, TContext>(def, ref inst, ctx, dt)      |
|    Trigger(ref instance)                                    |
|    All overloads -> thin shim, AggressiveInlining           |
|         |                                                   |
|         v  pins spans, calls void* core                     |
|  HsmKernelCore (internal static unsafe)                     |
|    UpdateBatchCore(def, void*, count, size, ctx, dt, cmd)   |
|    ProcessInstancePhase(...)                                |
|      -> Idle:    ProcessTimerPhase()                        |
|      -> Entry:   InitializeMachine() | ProcessEventPhase()  |
|      -> RTC:     ProcessRTCPhase()                          |
|      -> Activity:ProcessActivityPhase()                     |
|                                                             |
|  HsmInstanceManager (public static)                         |
|    Initialize<T>(T* instance, blob)                         |
|    Reset<T>(T* instance)                                    |
|    SelectTier(blob) -> 64 | 128 | 256                       |
|                                                             |
|  HsmEventQueue (public static)                              |
|    TryEnqueue(void*, size, HsmEvent) -> bool                |
|    TryDequeue(void*, size, out HsmEvent) -> bool            |
|    GetCount(void*, size) -> int                             |
|    TryEnqueue<T>(T*, HsmEvent)  -- generic overload         |
|    TryDequeue<T>(T*, out HsmEvent)                          |
|                                                             |
|  HsmDefinitionRegistry (class)                              |
|    Register(blob), TryGet(id), Get(id), Unregister, Clear   |
|                                                             |
|  HsmActionDispatcher (static unsafe)                        |
|    RegisterAction(id, IntPtr)                               |
|    RegisterGuard(id, IntPtr)                                |
|    ExecuteAction(id, instance*, context*, writer*)          |
|    EvaluateGuard(id, instance*, context*, eventId) -> bool  |
|                                                             |
|  HsmValidator (static)                                      |
|    ValidateDefinition(blob, out error) -> bool              |
|    ValidateInstance<T>(T*, blob, out error) -> bool         |
|                                                             |
|  HotReloadManager (class)                                   |
|    TryReload(machineId, newBlob, instances) -> ReloadResult |
+-------------------------------------------------------------+
```

---

## Memory Tier Architecture

```
+---[ Instance Memory Tiers ]--------------------------------+
|                                                           |
|  HsmInstance64  (64 bytes)   - "Crowd AI"                 |
|  +----------------------------------------------+         |
|  | Header            (16B)  InstanceHeader       |         |
|  | ActiveLeafIds     ( 4B)  fixed ushort[2]      |         |
|  | TimerDeadlines    ( 8B)  fixed uint[2]        |         |
|  | HistorySlots      ( 4B)  fixed ushort[2]      |         |
|  | EventQueue        (28B)  1 event + metadata   |         |
|  +----------------------------------------------+         |
|                                                           |
|  HsmInstance128 (128 bytes)  - "Standard Enemy"           |
|  +----------------------------------------------+         |
|  | Header            (16B)  InstanceHeader       |         |
|  | ActiveLeafIds     ( 8B)  fixed ushort[4]      |         |
|  | TimerDeadlines    (16B)  fixed uint[4]        |         |
|  | HistorySlots      (16B)  fixed ushort[8]      |         |
|  | EventQueue        (72B)  Hybrid (1 interrupt  |         |
|  |                          + 2 normal events)   |         |
|  +----------------------------------------------+         |
|                                                           |
|  HsmInstance256 (256 bytes)  - "Complex Unit"             |
|  +----------------------------------------------+         |
|  | Header            (16B)  InstanceHeader       |         |
|  | ActiveLeafIds     (16B)  fixed ushort[8]      |         |
|  | TimerDeadlines    (32B)  fixed uint[8]        |         |
|  | HistorySlots      (32B)  fixed ushort[16]     |         |
|  | EventQueue       (160B)  1 interrupt +        |         |
|  |                          5 normal events      |         |
|  +----------------------------------------------+         |
+-----------------------------------------------------------+
```

---

## Phase Execution Model

```
+---[ Instance Phase State Machine ]-------------------------+
|                                                           |
|  Entry (uninitialized: ActiveLeafIds[0] == 0xFFFF)        |
|    -> InitializeMachine()                                 |
|       -> fire OnEntry for initial state hierarchy         |
|    -> advance to Activity                                 |
|                                                           |
|  Entry (initialized: events present)                      |
|    -> ProcessEventPhase()                                 |
|       -> dequeue next event                               |
|       -> advance to RTC                                   |
|                                                           |
|  RTC (Run-to-Completion)                                  |
|    -> ProcessRTCPhase(eventId)                            |
|       -> evaluate transitions from active states          |
|       -> find matching (eventId, guard passes)            |
|       -> compute LCA path (exit count, entry count)       |
|       -> fire exit actions (leaf to LCA)                  |
|       -> fire transition action                           |
|       -> fire entry actions (LCA to target)               |
|       -> update ActiveLeafIds                             |
|    -> advance to Activity                                 |
|                                                           |
|  Activity                                                 |
|    -> ProcessActivityPhase()                              |
|       -> for each active leaf: fire ActivityAction        |
|       -> advance to Idle                                  |
|                                                           |
|  Idle                                                     |
|    -> ProcessTimerPhase(dt)                               |
|       -> for each TimerSlot: tick deadline counter        |
|       -> if deadline reached: enqueue TimerFiredEvent     |
|    -> if queue non-empty: advance to Entry                |
+-----------------------------------------------------------+
```

---

## Source Structure

```
Fhsm.Kernel/
+-- HsmKernel.cs                Public API: UpdateBatch/Update overloads (all
|                               AggressiveInlining shims calling HsmKernelCore)
+-- HsmKernelCore.cs            Internal unsafe core: UpdateBatchCore, phase dispatch,
|                               InitializeMachine, LCA computation, transition execution
+-- HsmInstanceManager.cs       Initialize/Reset typed instances; SelectTier()
+-- HsmEventQueue.cs            Tier-aware enqueue/dequeue with generic and void* overloads
+-- HsmDefinitionRegistry.cs    Thread-safe ConcurrentDictionary of blobs by StructureHash
+-- HsmActionDispatcher.cs      Static action/guard dispatch table (IntPtr -> function ptr)
+-- HsmValidator.cs             Structural validation of blobs and instances
+-- HotReloadManager.cs         Hot-reload with hard/soft reset logic
+-- HsmCommandAllocator.cs      Allocates slots in the command buffer page
+-- HsmRng.cs                   Fast inline RNG for probabilistic transitions
+-- TraceSymbolicator.cs        Translates raw trace records to human-readable strings
+-- InternalsVisibleTo.cs       [assembly: InternalsVisibleTo("...")] declarations
+-- Attributes/
|   +-- HsmActionAttribute.cs   [HsmAction(Name="...")] marks action methods
|   +-- HsmActionRegistrarAttribute.cs  [HsmActionRegistrar] marks registrar classes
|   +-- HsmDefinitionAttribute.cs  [HsmDefinition("...")] marks definition factories
|   +-- HsmGuardAttribute.cs    [HsmGuard(Name="...")] marks guard methods
|   +-- HsmLayoutAttribute.cs   [HsmLayout(Tier=64|128|256)] marks instance structs
+-- Data/
    +-- HsmDefinitionBlob.cs    Immutable ROM container: States[], Transitions[], Regions[],
    |                           GlobalTransitions[], ActionTable[], GuardTable[]
    +-- HsmDefinitionHeader.cs  Magic, version, counts, StructureHash, ParameterHash
    +-- StateDef.cs             32-byte ROM: topology, actions, flags, timer/history slots
    +-- TransitionDef.cs        16-byte ROM: source/target, eventId, guardId, actionId, flags
    +-- RegionDef.cs            Orthogonal region definition
    +-- GlobalTransitionDef.cs  Global transitions (any active state -> target)
    +-- HsmEvent.cs             24-byte event: EventId, Priority, Flags, Timestamp, Payload[16]
    +-- HsmInstance64.cs        64-byte Tier 1 instance struct
    +-- HsmInstance128.cs       128-byte Tier 2 instance struct
    +-- HsmInstance256.cs       256-byte Tier 3 instance struct (see Data/ list)
    +-- InstanceHeader.cs       Common header: MachineId, Generation, Phase, Flags, RngState
    +-- CommandPage.cs          Fixed-size command buffer page
    +-- HsmCommandWriter.cs     Writes commands into a CommandPage
    +-- Enums.cs                InstancePhase, InstanceFlags, EventPriority, EventFlags,
    |                           StateFlags, TransitionFlags, ReloadResult
    +-- HsmTraceContext.cs      Trace context pointer passed to UpdateBatch for diagnostics
    +-- TraceRecord.cs          Single trace entry: nodeIndex, status, timestamp
    +-- LinkerTableEntry.cs     FunctionId lookup entry in action/guard tables
    +-- MachineMetadata.cs      Debug sidecar: StateNames[], EventNames[], ActionNames[]
```

---

## Public API Reference

### HsmKernel (primary entry point)

```csharp
public static class HsmKernel
{
    // Batch update - preferred for large populations
    [MethodImpl(AggressiveInlining)]
    public static unsafe void UpdateBatch<TInstance, TContext>(
        HsmDefinitionBlob definition,
        Span<TInstance> instances,
        in TContext context,
        float deltaTime)
        where TInstance : unmanaged
        where TContext : unmanaged;

    // Batch update with command buffer
    public static unsafe void UpdateBatch<TInstance, TContext>(
        HsmDefinitionBlob definition,
        Span<TInstance> instances,
        in TContext context,
        float deltaTime,
        ref CommandPage commandPage);

    // Batch update with command buffer + trace context
    public static unsafe void UpdateBatch<TInstance, TContext>(
        HsmDefinitionBlob definition,
        Span<TInstance> instances,
        in TContext context,
        float deltaTime,
        ref CommandPage commandPage,
        HsmTraceContext* traceCtx);

    // Single instance update (delegates to UpdateBatch with count=1)
    public static unsafe void Update<TInstance, TContext>(
        HsmDefinitionBlob definition,
        ref TInstance instance,
        in TContext context,
        float deltaTime);

    // Single instance update with command buffer + trace
    public static unsafe void Update<TInstance, TContext>(
        HsmDefinitionBlob definition,
        ref TInstance instance,
        in TContext context,
        float deltaTime,
        ref CommandPage commandPage,
        HsmTraceContext* traceCtx);

    // Trigger initial state entry (call once after Initialize)
    public static unsafe void Trigger<TInstance>(ref TInstance instance)
        where TInstance : unmanaged;
}
```

### HsmInstanceManager

```csharp
public static class HsmInstanceManager
{
    // Zero-fill and set header; mark as uninitialized (ActiveLeafIds = 0xFFFF)
    public static unsafe void Initialize<T>(T* instance, HsmDefinitionBlob definition)
        where T : unmanaged;

    // Reset to initial state; increment generation; preserve MachineId + RngState
    public static unsafe void Reset<T>(T* instance)
        where T : unmanaged;

    // Determine tier (64, 128, or 256) from blob's state count/depth/history
    public static int SelectTier(HsmDefinitionBlob definition);
}
```

### HsmEventQueue

```csharp
public static class HsmEventQueue
{
    // void* overloads (for kernel core, size = sizeof(TInstance))
    public static unsafe bool TryEnqueue(void* instance, int size, in HsmEvent evt);
    public static unsafe bool TryDequeue(void* instance, int size, out HsmEvent evt);
    public static unsafe int GetCount(void* instance, int size);

    // Generic overloads (for application code)
    public static unsafe bool TryEnqueue<T>(T* instance, in HsmEvent evt)
        where T : unmanaged;
    public static unsafe bool TryDequeue<T>(T* instance, out HsmEvent evt)
        where T : unmanaged;
}
```

### HsmDefinitionRegistry

```csharp
public class HsmDefinitionRegistry
{
    public void Register(HsmDefinitionBlob blob);
    public bool TryGet(uint machineId, out HsmDefinitionBlob? blob);
    public HsmDefinitionBlob Get(uint machineId);   // throws if not found
    public bool Unregister(uint machineId);
    public void Clear();
}
```

### HsmValidator

```csharp
public static class HsmValidator
{
    // Validates state count, root parent sentinel, transition index bounds
    public static bool ValidateDefinition(HsmDefinitionBlob blob, out string? error);

    // Validates instance size matches tier, header magic, active leaf ids
    public static unsafe bool ValidateInstance<T>(
        T* instance, HsmDefinitionBlob definition, out string? error)
        where T : unmanaged;
}
```

### Attributes (`Attributes/`)

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class HsmActionAttribute : Attribute
{
    public string? Name { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class HsmGuardAttribute : Attribute
{
    public string? Name { get; set; }
}

/// <summary>
/// Marks a static parameterless method that returns HsmDefinitionBlob as a named
/// HSM asset to be catalogued by HsmAssetContributor.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HsmDefinitionAttribute : Attribute
{
    /// <summary>The logical name of the state machine (catalog key).</summary>
    public string MachineName { get; }

    /// <summary>
    /// Optional stable asset GUID. If null, the asset ID is derived from MachineName
    /// via FNV-1a-32 (same as BTree convention).
    /// </summary>
    public string? AssetId { get; set; }

    /// <summary>
    /// When true, signals that this asset uses an editor-managed companion blackboard
    /// file ({AssetName}.Blackboard.cs). The runtime ignores this flag; it is read by
    /// the HROT HSM editor. Default is false -- all existing assets are unaffected.
    /// </summary>
    public bool BlackboardManaged { get; set; }

    /// <summary>
    /// When set, the source generator wires BehaviorIngressSystem to provision a
    /// Blackboard1024 component for this behavior. Null means no heavy component.
    /// </summary>
    public Type? HeavyDtoType { get; set; }

    public HsmDefinitionAttribute(string machineName);
}
```

The `[BlackboardDtoStruct]`, `[BlackboardReadOnly]`, and `[BlackboardReadWrite]` parameter
attributes are defined in `Fbt.Kernel` (namespace `Fbt.Kernel`) and apply equally to HSM
action methods. See the `Fbt.Kernel` documentation for their full API and usage.

---

## Data Structures

### HsmEvent (24 bytes)

```csharp
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct HsmEvent
{
    [FieldOffset(0)]  public ushort EventId;         // User-defined event type
    [FieldOffset(2)]  public EventPriority Priority; // Normal | Interrupt | Low
    [FieldOffset(3)]  public EventFlags Flags;       // Deferred, etc.
    [FieldOffset(4)]  public uint Timestamp;         // Frame/tick when enqueued
    [FieldOffset(8)]  public unsafe fixed byte Payload[16]; // Inline data
}
```

### StateDef (32 bytes, ROM)

```csharp
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct StateDef
{
    [FieldOffset(0)]  public ushort ParentIndex;           // 0xFFFF = root
    [FieldOffset(2)]  public ushort FirstChildIndex;
    [FieldOffset(4)]  public ushort ChildCount;
    [FieldOffset(6)]  public ushort FirstTransitionIndex;
    [FieldOffset(8)]  public ushort TransitionCount;
    [FieldOffset(10)] public byte Depth;                   // 0 = root
    [FieldOffset(11)] public byte RegionCount;
    [FieldOffset(12)] public ushort OnEntryActionId;       // 0 = none
    [FieldOffset(14)] public ushort OnExitActionId;
    [FieldOffset(16)] public ushort ActivityActionId;
    [FieldOffset(18)] public StateFlags Flags;
    [FieldOffset(20)] public ushort HistorySlotIndex;      // 0xFFFF = none
    [FieldOffset(22)] public ushort TimerSlotIndex;        // 0xFFFF = none
    [FieldOffset(24)] public ushort RegionStartIndex;
    [FieldOffset(26)] public ushort InitialChildIndex;     // 0xFFFF = none
    [FieldOffset(28)] public byte OutputLaneMask;
    [FieldOffset(30)] public ushort TimerActionId;
}
```

### TransitionDef (16 bytes, ROM)

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct TransitionDef
{
    [FieldOffset(0)]  public ushort SourceStateIndex;
    [FieldOffset(2)]  public ushort TargetStateIndex;
    [FieldOffset(4)]  public ushort EventId;           // 0 = completion transition
    [FieldOffset(6)]  public ushort SyncGroupId;       // 0 = none
    [FieldOffset(8)]  public ushort GuardId;           // 0 = no guard (always passes)
    [FieldOffset(10)] public ushort ActionId;          // 0 = no effect action
    [FieldOffset(12)] public TransitionFlags Flags;
    [FieldOffset(14)] public ushort Cost;              // LCA steps: up + down
}
```

---

## Known Limitations / Deferred Items

### 1. Orthogonal Region Output Arbitration -- Conflict Detection Only (P4)

`HsmKernelCore.ArbitrateOutputLanes` detects when multiple parallel regions write to
the same actuator lane and suppresses the conflicting region's output by logging the
conflict to `HsmTraceContext`. The first-wins rule is applied silently (no error is
raised). Full priority-based arbitration -- where each region carries an explicit
priority value and the highest-priority region wins -- is explicitly marked as a P4
(future) enhancement in the source code. Until then, state machine authors must ensure
that parallel regions do not compete for the same output lane.

### 2. Deep History Restoration into Parallel States -- Tentative Break

`HsmKernelCore.DrillDownToInitial` stops drilling when it encounters a state whose
`StateFlags.IsParallel` flag is set, with the comment:

```csharp
// If we hit a Parallel state, we stop?
// Usually History restores into Parallel means entering Parallel.
if ((state.Flags & StateFlags.IsParallel) != 0) break;
```

This means that deep history restoration into a composite parallel (orthogonal) state
returns the parallel state itself rather than its active sub-region leaf states. The
correct UML behaviour requires entering all child regions and restoring their individual
history. This hardening has not been implemented and is a known gap for complex
concurrent sub-state configurations.

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| None (NuGet) | - | No external package dependencies |

The csproj has no `<PackageReference>` or `<ProjectReference>` elements. `Fhsm.Kernel` is a self-contained library with only .NET 8 BCL dependencies.

---

## Usage Examples

### Example 1: Basic Single-Instance Update (Traffic Light)

```csharp
// 1. Obtain a compiled definition blob (from Fhsm.Compiler)
HsmDefinitionBlob blob = ...; // compiled by HsmEmitter.Emit(...)

// 2. Register action implementations
HsmActionDispatcher.RegisterAction(1, (IntPtr)(delegate*<void*, void*, HsmCommandWriter*, void>)
    &Actions.OnEnterRed);

// 3. Allocate and initialize instance
var instance = new HsmInstance64();
unsafe
{
    fixed (HsmInstance64* ptr = &instance)
        HsmInstanceManager.Initialize(ptr, blob);
}

// 4. Trigger initial state entry
HsmKernel.Trigger(ref instance);

// 5. Post an event
var evt = new HsmEvent
{
    EventId = TimerExpiredEvent,
    Priority = EventPriority.Normal,
    Timestamp = (uint)frameCount
};
unsafe
{
    fixed (HsmInstance64* ptr = &instance)
        HsmEventQueue.TryEnqueue(ptr, evt);
}

// 6. Update (call once per frame)
var ctx = new MyContext { DeltaTime = 0.016f };
HsmKernel.Update(blob, ref instance, ctx, 0.016f);
```

### Example 2: Batch Update for Multiple Agents

```csharp
// Pool of 1000 patrol agents using the same blob
var instances = new HsmInstance64[1000];
unsafe
{
    for (int i = 0; i < instances.Length; i++)
        fixed (HsmInstance64* ptr = &instances[i])
            HsmInstanceManager.Initialize(ptr, patrolBlob);
}

// Trigger all instances
for (int i = 0; i < instances.Length; i++)
    HsmKernel.Trigger(ref instances[i]);

// Per-frame batch update - single call processes all 1000 instances
var ctx = new AgentContext { DeltaTime = dt, WorldTime = worldTime };
HsmKernel.UpdateBatch(patrolBlob, instances.AsSpan(), ctx, dt);
```

### Example 3: Posting a High-Priority Interrupt

```csharp
// Enemy detected - interrupt normal patrol flow
unsafe
{
    fixed (HsmInstance64* ptr = &agent.Instance)
    {
        var evt = new HsmEvent
        {
            EventId = EnemyDetectedEvent,
            Priority = EventPriority.Interrupt,  // preempts Normal events
            Timestamp = (uint)currentFrame
        };
        bool ok = HsmEventQueue.TryEnqueue(ptr, evt);
        if (!ok)
        {
            // Tier 1 queue full (only 1 slot); the interrupt evicts the oldest normal event
        }
    }
}
```

### Example 4: Validating Before Use

```csharp
if (!HsmValidator.ValidateDefinition(blob, out string? err))
    throw new InvalidOperationException($"Invalid HSM definition: {err}");

unsafe
{
    fixed (HsmInstance64* ptr = &instance)
    {
        if (!HsmValidator.ValidateInstance(ptr, blob, out err))
            throw new InvalidOperationException($"Invalid instance: {err}");
    }
}
```

---

## Architecture Diagram: Type-Erasure Core Pattern

```
+---[ Type-Erasure for Generic Elimination ]-----------------+
|                                                           |
|  HsmKernel.UpdateBatch<TInstance, TContext>(...)          |
|    [only one instantiation regardless of TInstance count] |
|    pins spans with fixed()                                |
|    passes void* instPtr, int instanceSize                 |
|         |                                                 |
|         v                                                 |
|  HsmKernelCore.UpdateBatchCore(                           |
|    def, void* instancePtr, int count, int size,           |
|    void* contextPtr, float dt, void* cmdPage, trace*)     |
|                                                           |
|  Core iterates:                                           |
|    byte* instPtr = base + i * size                        |
|    InstanceHeader* header = (InstanceHeader*)instPtr      |
|                                                           |
|  Result: ONE compiled version of UpdateBatchCore handles  |
|  all three tier sizes. Generic expansion = 3 thin shims.  |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: LCA Computation

```
+---[ Lowest Common Ancestor (LCA) for Transitions ]---------+
|                                                           |
|  Transition: StateA -> StateC (different subtrees)        |
|                                                           |
|  State hierarchy:                                         |
|    Root                                                   |
|      Top (depth 1)                                        |
|        A (depth 2)  <- active leaf                        |
|        B (depth 2)                                        |
|          C (depth 3) <- target                            |
|                                                           |
|  LCA computation:                                         |
|    Walk up from A: A, Top, Root                           |
|    Walk up from C: C, B, Top, Root                        |
|    First common ancestor: Top                             |
|                                                           |
|  Execution:                                               |
|    Exit: A (leaf to LCA, exclusive)                       |
|    Transition effect action                               |
|    Entry: B, C (LCA to target, inclusive of target)       |
|                                                           |
|  TransitionDef.Cost = exit steps + entry steps            |
|  (precomputed by HsmFlattener, stored in ROM)             |
+-----------------------------------------------------------+
```

---

## Best Practices

1. **Always call `HsmKernel.Trigger()` after `Initialize()`.** `Initialize()` marks the instance as uninitialized (ActiveLeafIds = 0xFFFF). `Trigger()` advances the phase to `Entry`, which on the next `Update()` call fires `InitializeMachine()` and executes the OnEntry chain for the initial state hierarchy.

2. **Use `SelectTier()` to choose the correct instance type.** Do not hard-code `HsmInstance64` unless you know the machine's complexity. Call `HsmInstanceManager.SelectTier(blob)` and allocate accordingly.

3. **Event queue capacity is finite and tier-dependent.** Tier 1 holds 1 event. Tier 2 holds 3 (1 interrupt + 2 normal). Tier 3 holds 6. Design state machines to not require more events than the tier supports, or use `Interrupt` priority for critical events that must not be dropped.

4. **`UpdateBatch()` is preferred over per-instance `Update()` for populations.** The batch overload avoids repeated function call overhead and keeps the hot loop in a single tight iteration.

5. **`HsmActionDispatcher` is global state.** Register actions once at startup, not per-instance. Action IDs are ushort values assigned at compile time by `HsmFlattener`.

6. **Validate definitions in debug builds.** `HsmValidator.ValidateDefinition()` catches structural errors from the compiler. Wrap in `#if DEBUG` for production builds to avoid the overhead.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fhsm.Compiler` | Builds `HsmDefinitionBlob` objects consumed by this kernel |
| `Fhsm.Demo.Visual` | Visual demo using this kernel with Raylib agents |
| `Fhsm.Examples.Console` | Minimal console demo; shows traffic light state machine |
| HROT AI system | Production consumer; drives unit behavior using `HsmInstance64` |
| `Fbt.Kernel` | Sister library: behavior tree runtime (different paradigm) |
