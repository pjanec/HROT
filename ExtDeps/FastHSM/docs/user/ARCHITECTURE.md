# Architecture Overview

FastHSM is built on a **Data-Oriented** philosophy, distinct from traditional Object-Oriented State Machine implementations.

---

## 1. Core Concepts

### ROM vs RAM Separation

Most C# State Machines involve classes for States (`class WalkState : State`). This creates:
- Lots of small objects (GC pressure).
- Scattered memory (Cache misses).
- Virtual call overhead.

FastHSM splits the data into two parts:

1.  **HsmDefinitionBlob (ROM):** Read-only, immutable definition of the state machine structure. It contains flat arrays of states, transitions, actions IDs. It is created once and shared by all instances.
2.  **HsmInstance (RAM):** A fixed-size struct (64, 128, or 256 bytes) containing only the *mutable* state:
    - Current Active State ID(s)
    - Event Queue
    - Timer Values
    - History Slots

### The Kernel

The `HsmKernel` is a static interpreter. It takes a **ROM** and a **RAM** and advances the state by one tick. It creates 0 garbage.

```csharp
// The entire runtime logic
HsmKernel.Update(definition, ref instance, context, dt);
```

---

## 2. Compilation Model

FastHSM uses a compiler approach.

1.  **Define:** You write C# code using the Fluent Builder API.
2.  **Compile:** At runtime startup (or theoretically build time), this graph is compiled into the `HsmDefinitionBlob`.
3.  **Optimization:** The compiler flattens the hierarchy, calculates standard ancestors (LCA) for transitions, and resolves string names to integer IDs.

This means complex hierarchy lookups (like finding the Least Common Ancestor states to exit/enter) are done *once* at compile time, not every frame at runtime.

---

## 3. Runtime Execution Model

The Kernel processes an update in four phases:

### Phase 1: Idle / Timer
- Checks if any active timers have expired.
- If so, fires a Timer Event.

### Phase 2: Entry
- If the instance was just initialized, it enters the Initial State.
- Executes `OnEntry` actions.

### Phase 3: RTC (Run-To-Completion)
- Checks the Event Queue.
- If an event exists, it attempts to find a valid Transition.
- If a transition is found:
    1.  Calculates path from Current State to Target State.
    2.  Executes **Exit Actions** (up to LCA).
    3.  Executes **Transition Action**.
    4.  Executes **Entry Actions** (down to Target).
    5.  Updates Current State.
    6.  Repeats (handles "stateless" transitions or instant chains).

### Phase 4: Activity
- If the machine settles (no more transitions), it runs the **Activity** for the current active state(s).
- This is where continuous logic (movement, animation updates) lives.

---

## 4. Memory Model

### Instance Tiers

To ensure fixed memory usage, FastHSM provides tiered structs.

- **HsmInstance64:**
    - Size: 64 bytes
    - Max Active Regions: 4
    - Max Timers: 4
    - Queue Capacity: Small
- **HsmInstance128:**
    - Size: 128 bytes
    - Max Active Regions: 8
    - Max Timers: 8
- **HsmInstance256:**
    - Size: 256 bytes
    - Max Active Regions: 16
    - Max Timers: 16

This allows you to densely pack thousands of AI agents into a single managed array (`HsmInstance64[]`), ensuring perfectly linear memory access for the CPU cache prefetcher.

---

## 5. Persistence & Determinism

Because the `HsmInstance` is a simple struct of primitive types (`ushort`, `byte`, `uint`), it is trivially serializable.

- **Save Game:** copying the bytes of the instance struct is sufficient to save the entire state of the machine (excluding external Context).
- **Network Sync:** You can send the struct over the network to sync state.
- **Rewind:** You can keep a ring buffer of previous instance states for rewind/replay functionality.

---

## 6. Integration

### Source Generator

FastHSM uses a C# Source Generator to bridge the gap between "String Names" in the builder and "Function Pointers" in the runtime.

- You write `OnEntry("MyMethod")`.
- The Builder hashes "MyMethod" to `0xA1B2`.
- The Source Generator scans your code for `[HsmAction(Name="MyMethod")]` and generates a switch statement:
  ```csharp
  case 0xA1B2: MyClass.MyMethod(...) break;
  ```
- This avoids `MethodInfo.Invoke` reflection overhead completely.
