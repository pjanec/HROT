# API Reference

This document provides a comprehensive reference for the FastHSM public API.

---

## Namespace: Fhsm.Compiler

The compiler namespace contains tools to define, validate, and compile state machines.

### `HsmBuilder`

The entry point for defining a state machine graph.

```csharp
public class HsmBuilder
{
    public HsmBuilder(string name);
    public StateBuilder State(string name);
    public HsmGraph Build();
}
```

- **State(name)**: Creates or retrieves a state builder for the given name.
- **Build()**: Finalizes the graph and returns an `HsmGraph` object ready for compilation.

### `StateBuilder`

Fluent API for configuring a single state.

```csharp
public class StateBuilder
{
    public StateBuilder OnEntry(string actionName);
    public StateBuilder OnExit(string actionName);
    public StateBuilder Activity(string actionName);
    public StateBuilder AddChild(string name);
    public StateBuilder Initial();
    public StateBuilder InitialChild(string name);
    
    public TransitionBuilder On(ushort eventId);
}
```

- **OnEntry(action)**: Sets the action to run when entering this state.
- **OnExit(action)**: Sets the action to run when exiting this state.
- **Activity(action)**: Sets the action to run every tick while in this state.
- **AddChild(name)**: Creates a sub-state.
- **Initial()**: Marks this state as the initial state of the root.
- **InitialChild(name)**: Marks a child as the initial state for these children.
- **On(eventId)**: Begins defining a transition triggered by `eventId`.

### `TransitionBuilder`

Fluent API for configuring transitions.

```csharp
public class TransitionBuilder
{
    public TransitionBuilder GoTo(string targetState);
    public TransitionBuilder If(string guardName);
    public TransitionBuilder Do(string actionName);
}
```

- **GoTo(target)**: Specifies the destination state.
- **If(guard)**: Adds a guard condition. Transition only occurs if Guard returns true.
- **Do(action)**: Specifies an action to run during the transition.

### `HsmNormalizer`

```csharp
public static class HsmNormalizer
{
    public static void Normalize(HsmGraph graph);
}
```

- **Normalize()**: cleans up the graph structure, resolving implicit parent relationships and ensuring consistency.

### `HsmGraphValidator`

```csharp
public static class HsmGraphValidator
{
    public static List<ValidationError> Validate(HsmGraph graph);
}
```

- **Validate()**: Checks for structural errors. Returns a list of errors (empty if valid).

### `HsmFlattener`

```csharp
public static class HsmFlattener
{
    public static HsmFlattenedGraph Flatten(HsmGraph graph);
}
```

- **Flatten()**: Converts the recursive graph into linear arrays suitable for the Emit phase.

### `HsmEmitter`

```csharp
public static class HsmEmitter
{
    public static HsmDefinitionBlob Emit(HsmFlattenedGraph flattened);
}
```

- **Emit()**: Produces the final, immutable `HsmDefinitionBlob` used by the runtime.

- **Emit()**: Produces the final, immutable `HsmDefinitionBlob` used by the runtime.

### `XxHash64`

High-performance non-cryptographic hash function.

```csharp
public static class XxHash64
{
    public static ulong ComputeHash(ReadOnlySpan<byte> data);
    public static ulong ComputeHash(string text);
}
```

---

## Namespace: Fhsm.Kernel

The runtime engine.

### `HsmKernel`

The static interpreter that runs the state machines.

```csharp
public static unsafe class HsmKernel
{
    public static void Update<T>(HsmDefinitionBlob definition, ref T instance, object context, float deltaTime) 
        where T : unmanaged, IHsmInstance;
        
    public static void UpdateBatch<T, TContext>(HsmDefinitionBlob definition, Span<T> instances, TContext context, float deltaTime)
        where T : unmanaged, IHsmInstance;
        
    public static void Trigger<T>(HsmDefinitionBlob definition, ref T instance, object context)
        where T : unmanaged, IHsmInstance;
}
```

- **Update()**: Runs the state machine for one frame. Processes timers, potential events, and activities.
- **UpdateBatch()**: Processes multiple instances in a tight loop for cache efficiency.
- **Trigger()**: Forces the initial entry sequence. Usually called once after initialization.

### `HsmInstanceManager`

Helper for managing instance memory.

```csharp
public static unsafe class HsmInstanceManager
{
    public static void Initialize<T>(T* instance, HsmDefinitionBlob definition) where T : unmanaged, IHsmInstance;
    public static void Reset<T>(T* instance) where T : unmanaged, IHsmInstance;
}
```

- **Initialize()**: Sets the machine ID and default values in the instance memory.

### `HsmEventQueue`

Manages the event queue within an instance.

```csharp
public static unsafe class HsmEventQueue
{
    public static bool TryEnqueue<T>(T* instance, int sizeBytes, HsmEvent evt) where T : unmanaged;
    public static bool TryDequeue<T>(T* instance, int sizeBytes, out HsmEvent evt) where T : unmanaged;
    public static void Clear<T>(T* instance, int sizeBytes) where T : unmanaged;
    public static int GetCount<T>(T* instance, int sizeBytes) where T : unmanaged;
}
```

### `HsmTraceBuffer` (Debugging)

Zero-allocation tracing system.

```csharp
public class HsmTraceBuffer
{
    public HsmTraceBuffer(int capacityBytes = 65536);
    public TraceLevel FilterLevel { get; set; }
    public void Clear();
    public ReadOnlySpan<byte> GetTraceData();
}
```

- **FilterLevel**: `Transitions`, `Events`, `States`, `Actions`, `Guards`, `Tier1` (default), `All`.

To use, set the static buffer:
`HsmKernelCore.SetTraceBuffer(myBuffer);`

### `TraceSymbolicator`

Utility to convert binary trace records into human-readable strings using the `HsmDefinitionBlob`.

```csharp
public class TraceSymbolicator
{
    public TraceSymbolicator(HsmDefinitionBlob definition);
    public string Symbolicate(ReadOnlySpan<byte> traceRecord);
}
```

### `HsmDefinitionRegistry`

Thread-safe registry to store and retrieve machine definitions by ID.

```csharp
public static class HsmDefinitionRegistry
{
    public static void Register(HsmDefinitionBlob blob);
    public static HsmDefinitionBlob Get(uint structureHash);
}
```

### `HsmCommandAllocator`

Helper for allocating command pages.

```csharp
public static class HsmCommandAllocator
{
    public static unsafe CommandPage* AllocatePage();
    public static unsafe void FreePage(CommandPage* page);
}
```

### `HsmRng`

Deterministic Random Number Generator.

```csharp
public unsafe ref struct HsmRng
{
    public HsmRng(uint* seedPtr);
    public float NextFloat();
    public int NextInt(int min, int max);
    public bool NextBool();
}
```

- **Usage**: Initialize with `&instance->Header.RngState`. Guaranteed deterministic sequence for replays.

---

## Namespace: Fhsm.Kernel.Data

Data structures used at runtime.

### `HsmDefinitionBlob`

The immutable "ROM" of the state machine. Contains all states, transitions, and hierarchy info. Typically stored as a static singleton per machine type.

### `HsmInstance64` / `HsmInstance128` / `HsmInstance256`

The mutable "RAM".

- **HsmInstance64**: 4 regions, 4 timers, small queue. (Fits in 1 cache line)
- **HsmInstance128**: 8 regions, 8 timers, medium queue. (Fits in 2 cache lines)
- **HsmInstance256**: 16 regions, 16 timers, large queue. (Fits in 4 cache lines)

### `HsmEvent`

```csharp
public struct HsmEvent
{
    public ushort EventId;
    public EventPriority Priority;
    public ushort Payload;
    public EventFlags Flags;
}
```

### `EventPriority`

enum: `Low`, `Normal`, `Interrupt`.

### Enums

- **InstanceFlags**: `DebugTrace`, `Error`, `Terminated`, etc.
- **StateFlags**: `IsComposite`, `IsHistory`, `IsFinal`, etc.

---

## Namespace: Fhsm.Kernel.Attributes

Source Generator markers.

### `[HsmAction]`

Marks a static method as an Action.

```csharp
[HsmAction(Name = "MyAction")]
public static unsafe void MyAction(void* instance, void* context, HsmCommandWriter* writer) { ... }
```

- **Name**: Optional override for the reference string used in the Builder.
- **UsesRNG**: Set to `true` (`[HsmAction(UsesRNG=true)]`) if the action uses the deterministic RNG.

### `[HsmGuard]`

Marks a static method as a Guard.

```csharp
[HsmGuard(Name = "MyGuard", UsesRNG = false)]
public static unsafe bool MyGuard(void* instance, void* context, ushort eventId) { ... }
```

- **UsesRNG**: Set to `true` if the guard uses the deterministic RNG.

---

## Namespace: Fhsm.SourceGen

### `HsmActionGenerator`

An incremental source generator that scans for `[HsmAction]` and `[HsmGuard]` attributes and generates the `HsmActionDispatcher` class. This connects the string IDs in your blob to the actual C# methods without reflection.
