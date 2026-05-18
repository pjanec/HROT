# Getting Started with FastHSM

This guide will walk you through setting up FastHSM and building your first high-performance state machine.

---

## 1. Prerequisites

Before you begin, ensure you have:

- **.NET 8.0 SDK** or later installed.
- A C# IDE (Visual Studio 2022, Rider, or VS Code).
- Basic knowledge of C# and state machine concepts.

---

## 2. Installation

### Add the Package

Add FastHSM to your project via NuGet (once published):

```bash
dotnet add package FastHSM
```

Or reference the project directly if you are building from source:

```bash
dotnet add reference ../src/Fhsm.Kernel/Fhsm.Kernel.csproj
dotnet add reference ../src/Fhsm.Compiler/Fhsm.Compiler.csproj
```

### Verify Installation

Create a simple console application and add the following using statements to `Program.cs` to verify everything is linked:

```csharp
using Fhsm.Kernel;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;

Console.WriteLine("FastHSM Installed Successfully!");
```

---

## 3. Your First State Machine: A Simple Switch

We will build a simple "On/Off" switch.

### Step 3.1: Define Events and Context

First, define your Event IDs and a Context (if needed).

```csharp
// Event IDs (using ushort)
public static class Events
{
    public const ushort Toggle = 1;
}

// Context (data shared with actions)
public struct SwitchContext
{
    public int ToggleCount;
}
```

### Step 3.2: Build the Graph

Use `HsmBuilder` to define the structure.

```csharp
var builder = new HsmBuilder("LightSwitch");

// Define States
var off = builder.State("Off");
var on = builder.State("On");

// Define Transitions
off.On(Events.Toggle).GoTo(on);
on.On(Events.Toggle).GoTo(off);

// Set Initial State
off.Initial();
```

### Step 3.3: Compile the Machine

Compile the builder graph into a `HsmDefinitionBlob`. This blob is immutable and can be shared across thousands of instances.

```csharp
// 1. Build Graph
var graph = builder.Build();

// 2. Normalize & Validate
HsmNormalizer.Normalize(graph);
if (!HsmGraphValidator.Validate(graph, out var errors))
{
    foreach (var error in errors) Console.WriteLine($"Error: {error}");
    return;
}

// 3. Flatten & Emit
var flattened = HsmFlattener.Flatten(graph);
HsmDefinitionBlob blob = HsmEmitter.Emit(flattened);
```

### Step 3.4: Create an Instance

Create a runtime instance. We'll use `HsmInstance64` which fits in 64 bytes (perfect for simple machines).

```csharp
// Allocate instance (on stack or heap)
HsmInstance64 instance = new HsmInstance64();

// Initialize with the blob
HsmInstanceManager.Initialize(&instance, blob);
```

### Step 3.5: Run the Loop

In a real application, you would call `Update` in your game loop. Here we'll simulate a loop.

```csharp
var context = new SwitchContext();

// Run updates
for (int i = 0; i < 5; i++)
{
    // Enqueue an event every other frame
    if (i % 2 == 0)
    {
        var evt = new HsmEvent { EventId = Events.Toggle };
        HsmEventQueue.TryEnqueue(&instance, 64, evt);
        Console.WriteLine($"[Frame {i}] Toggled switch!");
    }

    // Update the state machine
    HsmKernel.Update(blob, ref instance, context, 0.016f); // 16ms delta time
}
```

---

## 4. Adding Actions

State machines are useful because they *do* things. Let's add Entry and Exit actions.

### Step 4.1: Annotate Action Methods

Create a static class with methods annotated with `[HsmAction]`. Note: Use `unsafe` pointers for performance.

```csharp
using Fhsm.Kernel.Attributes;

public static unsafe class SwitchActions
{
    [HsmAction(Name = "TurnLightOn")]
    public static void TurnLightOn(void* instance, void* context, HsmCommandWriter* writer)
    {
        Console.WriteLine("💡 Light is ON");
        
        // Cast context to access data
        ref var ctx = ref *(SwitchContext*)context;
        ctx.ToggleCount++;
    }

    [HsmAction(Name = "TurnLightOff")]
    public static void TurnLightOff(void* instance, void* context, HsmCommandWriter* writer)
    {
        Console.WriteLine("🌑 Light is OFF");
    }
}
```

### Using Deterministic RNG

FastHSM provides a built-in deterministic Random Number Generator (RNG) for replays and simulations.

```csharp
[HsmAction(Name = "RandomMove")]
public static unsafe void RandomMove(void* instance, void* context, HsmCommandWriter* writer)
{
    var inst = (HsmInstance64*)instance;
    var rng = new HsmRng(&inst->Header.RngState);
    
    float angle = rng.NextFloat() * MathF.PI * 2f;
    // Use angle for random movement
}
```
```

### Step 4.2: Bind Actions in Builder

Update your builder code to reference these actions by name.

```csharp
var off = builder.State("Off")
    .OnEntry("TurnLightOff"); // Run when entering Off

var on = builder.State("On")
    .OnEntry("TurnLightOn");  // Run when entering On
```

*Note: The FastHSM Source Generator will automatically wire up the string names to the methods at compile time.*

---

## 5. Working with Events

Events drive the state machine.

### Event Structure

```csharp
public struct HsmEvent
{
    public ushort EventId;      // What happened?
    public EventPriority Priority; // Low, Normal, Interrupt
    public ushort Payload;      // Small data (optional)
    public EventFlags Flags;    // Internal flags
}
```

### Enqueuing Events

Events are processed during the `Update` call.

```csharp
// Enqueue a Normal priority event
var evt = new HsmEvent { EventId = Events.Toggle };
HsmEventQueue.TryEnqueue(&instance, 64, evt);
```

### Event Priorities

- **Interrupt:** Processed immediately, even if machine is busy.
- **Normal:** Standard processing order.
- **Low:** Processed only if time permits.

---

## 6. Context Data

Because `HsmInstance` is just raw state data, your business logic (health, position, inventory) lives in **Context**.

1. Define a struct or class for your Context.
2. Pass it to `HsmKernel.Update`.
3. Cast `void* context` inside your Actions.

**Best Practice:** Use a `struct` or `ref struct` for context to avoid allocations if you pass it by reference, but typically `void*` implies pinned or unmanaged memory if you want to be safe, though C# allows passing reference to managed objects as pointers if pinned. 

*Ideally for FastHSM, your Context is treated as an unsafe pointer or you simply ensure it's alive during the Update.*

```csharp
// Inside Action
ref var player = ref *(PlayerComponent*)context;
player.Health -= 10;
```

---

## 7. Hierarchical States

FastHSM supports nested states. This organizes complex logic.

```csharp
var locomotion = builder.State("Locomotion");
var idle = locomotion.AddChild("Idle");
var walk = locomotion.AddChild("Walk");

// Transitions can potentially be handled by parent
locomotion.On(Events.Stunned).GoTo(stunnedState); // Handles functionality for all children

// Set initial child
locomotion.InitialChild("Idle");
```

---

## 8. Guards

Guards are conditions that must be true for a transition to happen.

### Step 8.1: Define Guard Method

```csharp
[HsmGuard(Name = "CanWalk")]
public static unsafe bool CanWalk(void* instance, void* context, ushort eventId)
{
    ref var ctx = ref *(MyContext*)context;
    return ctx.Stamina > 0;
}
```

### Step 8.2: Use in Builder

```csharp
idle.On(Events.Move)
    .If("CanWalk")
    .GoTo(walk);
```

---

## 9. Activities

Activities are actions that run **every tick** while the state is active.

```csharp
var walk = builder.State("Walk")
    .Activity("UpdatePosition"); // Runs every Update() while in Walk state
```

---

## 10. Next Steps

You've built a functional hierarchical state machine!

- Check out **[Examples](EXAMPLES.md)** for more complex patterns.
- Read the **[API Reference](API-REFERENCE.md)** for deep dives.
- Review **[Performance Guide](PERFORMANCE.md)** if you are building for high scale.
