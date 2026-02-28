# 🤖 FDP Engine: Rules & Guidelines

**Core Philosophy:** The FDP (Fast Data Plane) is a high-performance, zero-allocation, deterministic Entity Component System (ECS) designed for distributed simulation. Performance, memory safety, and strict network decoupling are non-negotiable.

## 🔴 TIER 1: CRITICAL ARCHITECTURE (Never Violate)

### 1. Strict DDS-to-ECS Separation (The Golden Rule)
Network Data Transfer Objects (DDS structs) and internal Simulation State (ECS components) must **never** be mixed.
*   **DON'T:** Add `[ComponentId]` to a DDS data model struct (e.g., `Bagira.BDC.SSTD`).
*   **DON'T:** Pass raw DDS structs into `SpawnEntityCommand.InitialComponents` or `cmd.SetComponent()`.
*   **DON'T:** Use `AutoCycloneTranslator` for complex network messages.
*   **DO:** Write explicit `IDescriptorTranslator` classes that read DDS structs, perform necessary math/logic, and output pure, internal ECS components (e.g., convert `GeoSpatial` Lat/Lon to `SimTransform` Cartesian).

### 2. Zero Heap Allocation on the Hot Path
The simulation must run at 60Hz+ without triggering the Garbage Collector. Systems running in the `Update` loop must generate **zero** heap allocations.
*   **DON'T:** Use LINQ (`.Where`, `.Select`, `.ToList()`) inside `OnUpdate()`.
*   **DON'T:** Use `foreach` over standard managed collections if it boxes enumerators.
*   **DON'T:** Allocate new objects (`new MyClass()`) or arrays inside the update loop.
*   **DO:** Iterate ECS queries using the standard `foreach (var entity in query)`.
*   **DO:** Use `stackalloc`, `Span<T>`, or pre-allocated `NativeArray<T>` / singletons (e.g., `RaycastBatchData`) for temporary buffers.
*   **DO:** Use `FixedString32` or `FixedString64` for strings in ECS components instead of managed `string`.

### 3. Generational Entity Safety (Anti-Zombie Rule)
ECS entity slots are aggressively recycled. Holding onto a raw integer ID will result in cross-wiring and memory corruption if the original entity is destroyed and replaced.
*   **DON'T:** Store `int EntityIndex` to remember a target, shooter, or parent.
*   **DO:** Always store the full `Entity` struct (`Index` + `Generation`).
*   **DO:** ALWAYS check `World.IsAlive(entity)` before accessing components on an entity passed via an event or stored in a buffer.

---

## 🟠 TIER 2: ECS & SYSTEM MECHANICS

### 4. Structural Changes Require Command Buffers
You cannot add/remove components or destroy entities while iterating over an `EntityQuery` because it invalidates the underlying memory chunks.
*   **DON'T:** Call `World.AddComponent<T>()` or `World.DestroyEntity()` inside a `foreach (var entity in query)` loop.
*   **DO:** Acquire the buffer via `var cmd = view.GetCommandBuffer();` and call `cmd.AddComponent<T>()` or `cmd.DestroyEntity()`. The kernel will safely apply these at the end of the phase.
*   *Exception:* You *can* use `World.GetComponentRW<T>()` to modify the *values* of existing components during a query.

### 5. Strict Execution Phasing
Systems must be assigned to the correct phase using `[UpdateInPhase(SystemPhase.X)]` to prevent race conditions and ensure 1-frame data flow.
*   `Input`: Read external hardware, resolve physics raycasts, ingest network packets.
*   `BeforeSync`: Apply structural lifecycles (Spawning, ELM templates).
*   `Simulation`: AI Brains (BTree/HSM), weapon dispatchers, mission directors.
*   `PostSimulation`: Physics kinematics (move positions by velocity), spatial hash rebuilds, dead reckoning.
*   `Export`: Network egress translators, telemetry writing.

### 6. Event-Driven Cross-System Communication
Systems should not directly invoke methods on other systems. Use the `FdpEventBus`.
*   **DON'T:** Pass direct object references between systems.
*   **DO:** Use `World.Bus.Publish(new MyEvent { ... })`.
*   **DO:** Use `World.Bus.Consume<MyEvent>()` in the receiving system.
*   *Note:* The event bus is double-buffered. Events published in Phase A are typically consumed in the next frame (or after `SwapBuffers()` is explicitly called between phases).

---

## 🟡 TIER 3: DISTRIBUTED SIMULATION & NETWORKING

### 7. Explicit Network Ownership
In a distributed environment, a node must not mutate the simulation state of an entity it does not own, unless it is explicitly applying Dead Reckoning.
*   **DON'T:** Blindly update `SimTransform` for all entities in a query.
*   **DO:** Check `if (view.GetComponentRO<NetworkOwnership>(entity).PrimaryOwnerId != _localNodeId) continue;` before applying local physics or AI logic.
*   **DO:** Use `NetworkPosition` and `NetworkVelocity` as anchor targets for Ghost entities, and smoothly Lerp `SimTransform` toward them in the `PostSimulation` phase.

### 8. Transient Event Translation
Single-frame occurrences (like firing a weapon or an explosion) do not persist in ECS state. They must be translated explicitly to cross the network boundary.
*   **DO:** Use a `CycloneNativeEventTranslator<TEcs, TDds>` to listen to the `FdpEventBus` and push events to the DDS wire (and vice versa).

---

## 🟢 TIER 4: CODE STANDARDS & MAINTAINABILITY

### 9. No Magic Numbers
All IDs, threshold values, and capacities must be centralized constants.
*   **DON'T:** Use arbitrary numbers like `[ComponentId(162)]` or `[EventId(5002)]` randomly in files.
*   **DO:** Add them to central registries like `GlobalComponentIds.cs`, `PerceptionConstants.cs`, or `CombatConstants.cs`.

### 10. Simulation Determinism
The simulation must be able to run in Stepped/Lockstep modes or Replay modes seamlessly.
*   **DON'T:** Use `DateTime.UtcNow`, `TimeSpan`, or `Stopwatch` inside `OnUpdate()` logic.
*   **DO:** Use `DeltaTime` (provided by `ComponentSystem`) or read the `GlobalTime` singleton (`World.GetSingletonUnmanaged<GlobalTime>()`) for elapsed time and frame counts.

### 11. Unmanaged over Managed Components
To utilize the CPU L1/L2 caches efficiently, data must be kept contiguous.
*   **DO:** Define components as `unmanaged struct` whenever possible (Tier 1 components).
*   **DON'T:** Use `class` components (`RegisterManagedComponent`) unless absolutely necessary (e.g., holding a `List<T>`, a complex TKB template, or integration with a 3rd-party library).
