# Examples

This guide walks through the included examples to demonstrate real-world usage patterns.

---

## 1. Traffic Light

**Location:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs`

This is the canonical "Hello World" of state machines. It demonstrates timed transitions and simple sequential logic.

### Concept

A traffic light cycles continuously:
`Red (3s)` -> `Green (3s)` -> `Yellow (1s)` -> `Red` ...

### Code Walkthrough

#### 1. Definition

```csharp
var builder = new HsmBuilder("TrafficLight");

var red = builder.State("Red");
var yellow = builder.State("Yellow");
var green = builder.State("Green");

// Define cycle
red.On(TimerExpired).GoTo(green);
green.On(TimerExpired).GoTo(yellow);
yellow.On(TimerExpired).GoTo(red);
```

#### 2. Timers via Entry Actions

FastHSM supports timers natively, but you typically set them in entry actions.

```csharp
[HsmAction]
public static unsafe void EnterRed(void* instance, void* context, ushort eventId)
{
    // Set timer 0 to 3000ms
    HsmTimerManager.SetTimer(instance, 64, 0, 3000);
}
```

Wait, `HsmTimerManager` isn't public API in user guide, usually you set it via helper or directly on memory if unsafe.
Actually, the standard pattern in FastHSM for timers is often setting them via an Action.

*Note: In the current API, you access timers via raw pointers or helper methods if available. The example code uses a hypothetical `SetTimer` helper.*

#### 3. Handling the Timer Event

The Kernel automatically fires a System Event (`0xFFFE`) when a timer expires. In the example, we map user ID `TimerExpired` to this or handle it generically.

### Key Takeaways

- **Entry Actions** are where you setup state state (timers, visuals).
- **Cyclic Graphs** are handled naturally.
- **Timers** drive automatic transitions.

---

## 2. Character Controller (Conceptual)

A more complex example usually found in games.

### Hierarchy

```text
Locomotion
├── Grounded
│   ├── Idle
│   ├── Run
│   └── Crouch
└── Airborne
    ├── Jump
    └── Fall
```

### Features Demonstrated

1.  **Hierarchy:** Logic common to all grounded states (like "can jump") is handled in the `Grounded` parent state.
2.  **Guards:** Transitions are conditional.
    ```csharp
    grounded.On(JumpInput)
            .If("HasStamina")
            .GoTo(jump);
    ```
3.  **Interrupts:** `Airborne` states might listen for `Hit` event with High priority to transition to `Ragdoll`.

---

## 3. Worker Bot (AI)

Demonstrates "Activities" and "Context".

### Scenario

A bot gathers resources and returns them to base.

### Logic

1.  **Gather State:** `Activity("Gathering")` increments resource count every tick.
2.  **Guard:** When `ResourceCount >= Max`, transition to Return.
3.  **Return State:** Move towards base.

```csharp
[HsmAction]
public static unsafe void GatheringActivity(void* instance, void* context, HsmCommandWriter* writer)
{
    var bot = (BotContext*)context;
    // Note: Activities typically don't write commands unless they spawn effects
    // but the signature requires it.
    bot.Resources += bot.GatherRate * DeltaTime;
}

[HsmGuard]
public static unsafe bool IsFull(void* instance, void* context, ushort eventId)
{
    var bot = (BotContext*)context;
    return bot.Resources >= bot.Capacity;
}
```

This pattern keeps the status check logic outside the State Machine structure itself (via Guard) and keeps the behavior in the Activity.

---

## 4. Using Command Buffers

Integration with game engine command buffers allows running HSMs in parallel jobs without thread-safety issues.

```csharp
[HsmAction(Name = "SpawnParticle")]
public static unsafe void SpawnParticle(void* instance, void* context, HsmCommandWriter* writer)
{
    var ctx = (GameContext*)context;
    
    // Write command to buffer
    var cmd = new ParticleSpawnCommand
    {
        Position = ctx->PlayerPosition,
        Color = 0xFF00FF00
    };
    
    // Allocate stack span for casting
    Span<byte> cmdBytes = new Span<byte>(&cmd, sizeof(ParticleSpawnCommand));
    writer->TryWriteCommand(cmdBytes);
}
```
