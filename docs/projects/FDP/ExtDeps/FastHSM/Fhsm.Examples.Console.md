# Fhsm.Examples.Console

**Project Path**: `FDP/ExtDeps/FastHSM/examples/Fhsm.Examples.Console/Fhsm.Examples.Console.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Executable

---

## README Validation

**Status: Missing.**

No `README.md` exists in the `examples/Fhsm.Examples.Console/` folder or in the `examples/` parent. The example is self-documenting through its extensive console output, but a README describing the prerequisite source generator setup and what the expected output looks like would be helpful.

---

## Executive Overview

`Fhsm.Examples.Console` is the minimal reference implementation for the FastHSM library. It contains exactly one non-trivial class: `TrafficLightExample`, which builds and runs a classic three-state cyclic state machine (Red -> Green -> Yellow -> Red) entirely in console output, with no graphical dependencies.

The example covers the complete FastHSM workflow:
1. Define action methods tagged with `[HsmAction]` and register them using the source-generated `HsmActionRegistrar.RegisterAll()`.
2. Build the machine programmatically with `HsmBuilder`, registering events and actions by name.
3. Normalize, validate, flatten, and emit the `HsmDefinitionBlob`.
4. Allocate and initialize an `HsmInstance64`.
5. Trigger the initial state and run a tick loop, posting `TimerExpired` events every 3 ticks to drive transitions.
6. Observe entry/exit/activity actions firing and state transitions occurring.

This is the recommended starting point for developers new to FastHSM who want to understand the library before adding Raylib or ImGui.

---

## Architecture

```
+---[ Fhsm.Examples.Console ]--------------------------------+
|                                                           |
|  Program.Main()                                           |
|    TrafficLightExample.Run()                              |
|                                                           |
|  TrafficLightExample.Run()                                |
|    1. HsmActionRegistrar.RegisterAll()                    |
|         Registers all [HsmAction] methods from this       |
|         assembly into HsmActionDispatcher                 |
|                                                           |
|    2. Build via HsmBuilder                                |
|         .Event("TimerExpired", 1)                         |
|         .RegisterAction("OnEnterRed") x5                  |
|         .State("Red").OnEntry(...).OnExit(...).Activity(.).|
|         .State("Green").OnEntry(...)                      |
|         .State("Yellow").OnEntry(...)                     |
|         red.On(1).GoTo(green)                             |
|         green.On(1).GoTo(yellow)                          |
|         yellow.On(1).GoTo(red)                            |
|         red.Initial()                                     |
|                                                           |
|    3. Normalize + Validate + Flatten + Emit               |
|         -> HsmDefinitionBlob blob                         |
|                                                           |
|    4. Initialize + Trigger                                |
|         var instance = new HsmInstance64()                |
|         HsmInstanceManager.Initialize(&instance, blob)    |
|         HsmKernel.Trigger(ref instance)                   |
|                                                           |
|    5. Tick loop (10 ticks, dt=0.016f)                     |
|         every 3 ticks: TryEnqueue TimerExpired event      |
|         HsmKernel.Update(blob, ref instance, ctx, dt)     |
|         (actions print state transitions to console)      |
+-----------------------------------------------------------+
```

```
+---[ State Machine Diagram: TrafficLight ]------------------+
|                                                           |
|     +------+   TimerExpired    +-------+                  |
|     |  Red |-----------------> | Green |                  |
|     |      |                   |       |                  |
|     | Entry: OnEnterRed        | Entry: OnEnterGreen      |
|     | Exit:  OnExitRed         |                          |
|     | Activ: RedActivity       +-------+                  |
|     +------+                       |                      |
|         ^                TimerExpired                     |
|         |                          v                      |
|  TimerExpired           +--------+                        |
|         |               | Yellow |                        |
|         +-------------- | Entry: OnEnterYellow            |
|                         +--------+                        |
|                                                           |
|  Initial state: Red                                       |
+-----------------------------------------------------------+
```

---

## Source Structure

```
Fhsm.Examples.Console/
+-- Program.cs                  Entry point: calls TrafficLightExample.Run()
+-- TrafficLightExample.cs      The complete example in one static class:
|                               - TrafficLightContext struct
|                               - [HsmAction] methods: OnEnterRed, OnExitRed,
|                                 RedActivity, OnEnterGreen, OnEnterYellow
|                               - Run() static method: full build-compile-run pipeline
+-- Fhsm.Examples.Console.csproj
                                References: Fhsm.Kernel, Fhsm.Compiler
                                Analyzer: Fhsm.SourceGen (no output ref)
```

---

## Public API Used

### TrafficLightContext

```csharp
private struct TrafficLightContext
{
    public int TickCount;
}
```

The context is passed as `void* context` to every action. Actions cast it to `TrafficLightContext*` to read or write `TickCount`. The struct must have a stable layout since the kernel passes it by pointer.

### Action Methods

```csharp
// Entry/exit actions - fire once on state entry or exit
[HsmAction(Name = "OnEnterRed")]
public static void OnEnterRed(void* instance, void* context, HsmCommandWriter* writer);

[HsmAction(Name = "OnExitRed")]
public static void OnExitRed(void* instance, void* context, HsmCommandWriter* writer);

[HsmAction(Name = "OnEnterGreen")]
public static void OnEnterGreen(void* instance, void* context, HsmCommandWriter* writer);

[HsmAction(Name = "OnEnterYellow")]
public static void OnEnterYellow(void* instance, void* context, HsmCommandWriter* writer);

// Activity action - fires every tick while in Red state
[HsmAction(Name = "RedActivity")]
public static void RedActivity(void* instance, void* context, HsmCommandWriter* writer);
```

All actions are `unsafe static void` methods with the signature `(void* instance, void* context, HsmCommandWriter* writer)`. This is the only supported action signature in FastHSM.

### Source-Generated Registrar

The `Fhsm.SourceGen` analyzer generates (approximately):

```csharp
// Auto-generated - do not edit
namespace Fhsm.Examples.Console.Generated
{
    public static class HsmActionRegistrar
    {
        public static void RegisterAll()
        {
            HsmActionDispatcher.RegisterAction(
                XxHash64.Compute("OnEnterRed"),
                (IntPtr)(delegate*<void*,void*,HsmCommandWriter*,void>)
                    &TrafficLightExample.OnEnterRed);

            HsmActionDispatcher.RegisterAction(
                XxHash64.Compute("OnExitRed"),
                (IntPtr)(delegate*<void*,void*,HsmCommandWriter*,void>)
                    &TrafficLightExample.OnExitRed);
            // ... etc for all [HsmAction] methods in the assembly
        }
    }
}
```

The generated code uses `delegate*` (function pointer) syntax for zero-overhead dispatch. Action IDs are the XxHash64 of the action name string, computed once at registration time.

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Fhsm.Kernel` | (project ref) | `HsmInstance64`, `HsmKernel`, `HsmInstanceManager`, `HsmEventQueue`, `HsmDefinitionBlob`, `HsmCommandWriter`, `HsmActionDispatcher` |
| `Fhsm.Compiler` | (project ref) | `HsmBuilder`, `HsmNormalizer`, `HsmGraphValidator`, `HsmFlattener`, `HsmEmitter` |
| `Fhsm.SourceGen` | (analyzer) | Generates `HsmActionRegistrar.RegisterAll()` |

---

## Usage Examples

### Example 1: Running the Console Demo

```bash
cd FDP/ExtDeps/FastHSM/examples/Fhsm.Examples.Console
dotnet run
```

Expected output (abbreviated):

```
=== Traffic Light State Machine ===

Compiled: 4 states, 3 transitions

Instance initialized (Tier: 64B)

--- Tick 0 ---
RED - Stop!

--- Tick 1 ---
  [Red Activity - Tick 1]

--- Tick 2 ---
  [Red Activity - Tick 2]
Timer expired!
  Exiting Red state...
GREEN - Go!

--- Tick 3 ---

--- Tick 4 ---

--- Tick 5 ---
Timer expired!
YELLOW - Caution!

--- Tick 6 ---

--- Tick 7 ---

--- Tick 8 ---
Timer expired!
RED - Stop!

--- Tick 9 ---
  [Red Activity - Tick 9]

=== Simulation Complete ===
```

### Example 2: Writing a New Example Using the Same Pattern

The traffic light pattern can be adapted for any cyclic state machine. Here is a door lock with three states:

```csharp
[HsmAction(Name = "OnEnterLocked")]
public static void OnEnterLocked(void* instance, void* context, HsmCommandWriter* writer)
    => Console.WriteLine("[LOCKED] Door is locked.");

[HsmAction(Name = "OnEnterUnlocked")]
public static void OnEnterUnlocked(void* instance, void* context, HsmCommandWriter* writer)
    => Console.WriteLine("[UNLOCKED] Door is unlocked.");

[HsmAction(Name = "OnEnterOpen")]
public static void OnEnterOpen(void* instance, void* context, HsmCommandWriter* writer)
    => Console.WriteLine("[OPEN] Door is open.");

// Build machine
const ushort KeyTurned  = 1;
const ushort DoorPushed = 2;
const ushort DoorClosed = 3;

var builder = new HsmBuilder("DoorLock");
builder.Event("KeyTurned",  KeyTurned)
       .Event("DoorPushed", DoorPushed)
       .Event("DoorClosed", DoorClosed);

builder.RegisterAction("OnEnterLocked")
       .RegisterAction("OnEnterUnlocked")
       .RegisterAction("OnEnterOpen");

var locked   = builder.State("Locked")  .OnEntry("OnEnterLocked").Initial();
var unlocked = builder.State("Unlocked").OnEntry("OnEnterUnlocked");
var open     = builder.State("Open")    .OnEntry("OnEnterOpen");

locked.On(KeyTurned).GoTo(unlocked);
unlocked.On(KeyTurned).GoTo(locked);
unlocked.On(DoorPushed).GoTo(open);
open.On(DoorClosed).GoTo(locked);

// Compile, initialize, run
Generated.HsmActionRegistrar.RegisterAll();
var graph = builder.Build();
HsmNormalizer.Normalize(graph);
var blob = HsmEmitter.Emit(HsmFlattener.Flatten(graph));

var instance = new HsmInstance64();
unsafe { fixed (HsmInstance64* p = &instance) HsmInstanceManager.Initialize(p, blob); }
HsmKernel.Trigger(ref instance);

// Simulate: post KeyTurned, then DoorPushed
var ctx = new TrafficLightContext();
HsmKernel.Update(blob, ref instance, ctx, 0.016f); // OnEnterLocked fires

// Post KeyTurned
unsafe
{
    fixed (HsmInstance64* p = &instance)
        HsmEventQueue.TryEnqueue(p, new HsmEvent { EventId = KeyTurned });
}
HsmKernel.Update(blob, ref instance, ctx, 0.016f); // OnEnterUnlocked fires
```

### Example 3: Inspecting the Compiled Blob

After compilation, the blob's header reveals the machine structure:

```csharp
var flat = HsmFlattener.Flatten(graph);
var blob = HsmEmitter.Emit(flat);

Console.WriteLine($"States:      {blob.Header.StateCount}");
Console.WriteLine($"Transitions: {blob.Header.TransitionCount}");
Console.WriteLine($"Actions:     {blob.Header.ActionCount}");
Console.WriteLine($"StructureHash: 0x{blob.Header.StructureHash:X8}");
Console.WriteLine($"ParameterHash: 0x{blob.Header.ParameterHash:X8}");

// Also inspect flat data directly
foreach (var state in flat.States)
{
    Console.WriteLine($"  State[{state.FirstChildIndex}] depth={state.Depth} " +
                      $"entry={state.OnEntryActionId} exit={state.OnExitActionId}");
}
```

### Example 4: Handling Validation Errors

```csharp
var graph = builder.Build();
HsmNormalizer.Normalize(graph);

var errors = HsmGraphValidator.Validate(graph);
foreach (var error in errors)
{
    Console.WriteLine($"[{error.Severity}] {error.Message}");
}

if (errors.Any(e => e.Severity == ValidationSeverity.Error))
{
    Console.WriteLine("Compilation aborted due to validation errors.");
    return;
}

var blob = HsmEmitter.Emit(HsmFlattener.Flatten(graph));
```

---

## Architecture Diagram: Source Generator Integration

```
+---[ Source Generator Pipeline ]----------------------------+
|                                                           |
|  Build time (Fhsm.SourceGen analyzer runs):               |
|                                                           |
|  TrafficLightExample.cs:                                  |
|    [HsmAction(Name = "OnEnterRed")]                       |
|    public static void OnEnterRed(...)                     |
|    [HsmAction(Name = "OnExitRed")]                        |
|    public static void OnExitRed(...)                      |
|    ...                                                    |
|           |                                               |
|           v  Fhsm.SourceGen processes [HsmAction] attrs   |
|           |                                               |
|  Generated/HsmActionRegistrar.g.cs:                       |
|    public static class HsmActionRegistrar                 |
|    {                                                      |
|        public static void RegisterAll()                   |
|        {                                                  |
|            HsmActionDispatcher.RegisterAction(            |
|                id_OnEnterRed, &OnEnterRed);               |
|            HsmActionDispatcher.RegisterAction(            |
|                id_OnExitRed, &OnExitRed);                 |
|            ... (all [HsmAction] methods)                  |
|        }                                                  |
|    }                                                      |
|                                                           |
|  Runtime:                                                 |
|    HsmActionRegistrar.RegisterAll()  <- called once       |
|    HsmActionDispatcher               <- global table      |
|    HsmKernelCore.ProcessRTCPhase()   <- dispatches by ID  |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: Event-Driven Tick Sequencing

```
+---[ Tick Sequencing with Manual Events ]-------------------+
|                                                           |
|  Tick 0: HsmKernel.Trigger(ref instance)                  |
|    Phase: Entry -> InitializeMachine                      |
|    OnEntry "OnEnterRed" fires: prints "RED - Stop!"       |
|    Phase: Activity -> "RedActivity" fires: Tick 0         |
|    Phase: Idle -> no timer expiry yet                     |
|                                                           |
|  Tick 1: HsmKernel.Update(...)                            |
|    Phase: Idle -> ProcessTimers (no deadline reached)     |
|    Queue empty -> stay Idle                               |
|    Phase: Activity -> "RedActivity" fires: Tick 1         |
|                                                           |
|  Tick 2: i%3 == 2 -> post TimerExpired event              |
|    HsmEventQueue.TryEnqueue(&instance, 64, event)         |
|    Phase: Entry -> ProcessEventPhase                      |
|      Dequeue TimerExpired                                 |
|    Phase: RTC -> find transition from Red: [1]->Green     |
|      OnExit "OnExitRed" fires                             |
|      OnEntry "OnEnterGreen" fires                         |
|      ActiveLeafIds[0] = FlatIndex(Green)                  |
|    Phase: Activity -> Green has no activity action        |
|    Phase: Idle                                            |
|                                                           |
|  Key: event is manually posted every 3 ticks.             |
|  In a real machine, timer slots handle this automatically. |
+-----------------------------------------------------------+
```

---

## Best Practices Illustrated

1. **Always call `RegisterAll()` before the first `Update()`.** The source-generated registrar populates `HsmActionDispatcher` tables. If called after `Update()`, the first tick may fire actions before they are registered.

2. **`HsmKernel.Trigger()` is a separate step from `Initialize()`.** `Initialize()` sets up the memory layout and marks the instance as uninitialized. `Trigger()` sets the phase to `Entry`, which causes `InitializeMachine()` to fire on the first `Update()` call (firing OnEntry for the initial state). This separation allows batch initialization before the first tick.

3. **`unsafe` context in action methods.** All action methods must be `unsafe` and `static`. The `void* context` parameter must be cast to the concrete context type. Ensure the context struct's lifetime exceeds the `Update()` call.

4. **The builder's `RegisterAction()` call uses string names matching `[HsmAction(Name = "...")]`.** The string name is hashed at compile time to produce the action ID. The name in the builder call and the attribute must be identical. A mismatch produces an action that dispatches to nothing (silent no-op at runtime).

5. **Use `HsmValidator.ValidateDefinition()` in tests.** Add an assertion after `HsmEmitter.Emit()` in unit tests to catch structural issues early.

6. **`HsmInstance64` can be declared as a local variable.** Unlike heap-allocated objects, these structs can live on the stack or inside larger structs. The `unsafe fixed` pattern shown in the example is necessary for pointer operations.

---

## Extended Usage: Validating Instance After Initialization

In debug builds, validate the instance after `Initialize()` to catch tier mismatches:

```csharp
var instance = new HsmInstance64();
unsafe
{
    fixed (HsmInstance64* p = &instance)
    {
        HsmInstanceManager.Initialize(p, blob);

        string? err;
        if (!HsmValidator.ValidateInstance(p, blob, out err))
            throw new InvalidOperationException($"Instance invalid: {err}");
    }
}
```

---

## Extended Usage: Reading Active State Index

After each `Update()`, read which leaf state is currently active:

```csharp
unsafe
{
    fixed (HsmInstance64* ptr = &instance)
    {
        ushort activeLeafId = ptr->ActiveLeafIds[0];
        // Use MachineMetadata to resolve the name
        if (meta.StateNames.TryGetValue(activeLeafId, out string? name))
            Console.WriteLine($"Active state: {name} (index {activeLeafId})");
        else
            Console.WriteLine($"Active state index: {activeLeafId}");
    }
}
```

Build `MachineMetadata` alongside the blob:

```csharp
var graph = builder.Build();
HsmNormalizer.Normalize(graph);
var flat = HsmFlattener.Flatten(graph);
var blob = HsmEmitter.Emit(flat);
var meta = HsmEmitter.BuildMachineMetadata(graph);  // state/event name maps
```

---

## Diagram: HsmBuilder Registration Rules

```
+---[ What Must Be Registered With HsmBuilder ]-------------+
|                                                           |
|  builder.Event("TimerExpired", 1)                         |
|    Required for any transition using eventId=1            |
|    Enables: HsmGraphValidator to check event references   |
|             HsmFlattener to map names to IDs              |
|                                                           |
|  builder.RegisterAction("OnEnterRed")                     |
|    Required for any state that names this in:             |
|      .OnEntry("OnEnterRed")                               |
|      .OnExit("OnEnterRed")                                |
|      .Activity("OnEnterRed")                              |
|      .TimerAction("OnEnterRed")                           |
|    Unregistered action references -> validation error     |
|                                                           |
|  Action name must EXACTLY match:                          |
|    [HsmAction(Name = "OnEnterRed")] on the method         |
|    The HsmActionRegistrar.RegisterAll() uses XxHash64     |
|    of the name string as the dispatch table key           |
|                                                           |
|  If name mismatch: action silently does nothing at runtime|
+-----------------------------------------------------------+
```

---

## Diagram: HsmInstance64 Region in Memory

```
+---[ HsmInstance64 Memory Walkthrough ]---------------------+
|                                                           |
|  byte offset  field                                       |
|  -----------  -----                                       |
|  0-15         InstanceHeader                              |
|                 MachineId   (uint, 4B) at offset 0        |
|                 Generation  (ushort)   at offset 4        |
|                 Phase       (byte)     at offset 6        |
|                 Flags       (byte)     at offset 7        |
|                 RngState    (uint)     at offset 8        |
|                 (reserved)             at offset 12       |
|  16-19        ActiveLeafIds[2] (fixed ushort[2])          |
|                 [0] = current active leaf state index     |
|                 [1] = second region (if orthogonal)       |
|  24-31        TimerDeadlines[2] (fixed uint[2])           |
|                 Each is a tick-count deadline             |
|                 0 = timer inactive                        |
|  32-35        HistorySlots[2] (fixed ushort[2])           |
|                 Remembers which child was last active      |
|                 in a History state                        |
|  36           EventCount (byte) - 0 or 1                  |
|  37-39        Reserved (3 bytes)                          |
|  40-63        EventBuffer[24] - one HsmEvent (24 bytes)   |
|  -----------  -----                                       |
|  Total: 64 bytes                                          |
+-----------------------------------------------------------+
```

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fhsm.Kernel` | Runtime: `HsmKernel`, `HsmInstance64`, event queue, dispatch |
| `Fhsm.Compiler` | Build: `HsmBuilder`, normalizer, validator, flattener, emitter |
| `Fhsm.SourceGen` | Generates `HsmActionRegistrar.RegisterAll()` from [HsmAction] tags |
| `Fhsm.Demo.Visual` | Full-featured Raylib demo showing multi-agent HSM execution |
| `Fbt.Examples.Console` | Analogous console example for FastBTree (same minimal philosophy) |
