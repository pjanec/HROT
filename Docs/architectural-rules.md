# 🤖 FDP Engine: Rules & Guidelines

**Core Philosophy:** The FDP (Fast Data Plane) is a high-performance, zero-allocation, deterministic Entity Component System (ECS) designed for distributed simulation. Performance, memory safety, and strict network decoupling are non-negotiable.

---

## 🔴 TIER 1: CRITICAL ARCHITECTURE (Never Violate)

### 1. The "Three Domains" Rule: TKB vs. Network vs. ECS
You must strictly differentiate between the three distinct data domains. They share the word "Descriptor" but serve completely different purposes:
*   **Network Descriptors (DDS DTOs):** e.g., `EntityMaster' or 'GeoSpatial`. Used *only* on the wire. Must **never** be registered as ECS components (`[ComponentId]` is forbidden on these).
*   **TKB Descriptors (Static Templates):** Static blueprint definitions stored in `TkbDatabase`. They are *not* ECS components. They are read-only definitions used to populate a new entity at spawn time. Some of them are just static paremeter structs to be used by various entity models (movement models, behavior control models etc.)
*   **ECS Components (Internal State):** e.g., `SimTransform`, `NetworkIdentity`. The actual simulation state. Dynamic.
*   **The Rule:** You must use explicit `IDescriptorTranslator` classes to convert Network Descriptors ↔ ECS Components. You must use `TkbTemplate.ApplyTo()` to convert TKB Descriptors → ECS Components on entity creation.

### 2. Entity IDs vs. Network IDs
Never confuse a local ECS handle with a global network identity.
*   **Network ID (`int`):** A globally unique ID allocated by `INetworkIdAllocator` (or `DdsIdAllocator`). Stored in the ECS as `NetworkIdentity.Value`. Transmitted over the network.
*   **ECS Entity (`Entity` struct):** A purely local handle containing a 32-bit `Index` and a 16-bit `Generation`. Valid only on the local machine for accessing the `EntityRepository`.
*   **The Rule:** Always use `NetworkEntityMap` to translate between a received Network ID and the local ECS `Entity` handle. 

### 3. Zero Heap Allocation on the Hot Path
Systems running in the `Update` loop must generate **zero** heap allocations to prevent GC stalling at 60Hz+.
*   **DON'T:** Use LINQ (`.Where`, `.Select`, `.ToList()`), allocate new objects (`new MyClass()`), or box structs inside `OnUpdate()`.
*   **DO:** Iterate ECS queries using the standard `foreach (var entity in query)`.
*   **DO:** Use `stackalloc`, `Span<T>`, or pre-allocated `NativeArray<T>` for temporary buffers.

### 4. Generational Entity Safety (Anti-Zombie Rule)
ECS entity slots are aggressively recycled. Holding onto a raw integer index will result in memory corruption if the original entity is destroyed and replaced.
*   **DON'T:** Store `int EntityIndex` to remember a target, shooter, or parent.
*   **DO:** Always store the full `Entity` struct (`Index` + `Generation`).
*   **DO:** ALWAYS check `World.IsAlive(entity)` before acting on an entity handle stored from a previous frame.

---

## 🟠 TIER 2: ECS MECHANICS & SCHEDULING

### 5. Efficient Component Access (`TryGetComponent` & `RW` vs `RO`)
Do not waste CPU cycles on redundant lookups or unnecessary data copying.
*   **Use `TryGetComponent`:** Instead of calling `HasComponent<T>` followed immediately by `GetComponentRO<T>`, use `if (view.TryGetComponent<T>(entity, out var comp))`.
*   **Unmanaged Mutations (In-Place):** For Tier 1 structs, use `ref var comp = ref view.GetComponentRW<T>(entity); comp.Value = 1;`. Do *not* read it (`RO`), mutate the copy, and write it back (`SetComponent`), as `RW` gives you a direct, in-place pointer and handles dirty tracking automatically.
*   **Managed Mutations (Strictly Replace):** For Tier 2 managed classes, **never mutate in-place**. Background Snapshot-on-Demand (SoD) modules might hold a shallow reference copy. Always instantiate/clone and use `view.SetManagedComponent<T>(entity, newInstance)`.

### 6. Strict Execution Phasing & Ordering
The FDP Kernel enforces strict phasing. Understand the difference between Main World Systems and Background Modules.
*   **Global Systems (Main Thread):** Must be registered via `RegisterGlobalSystem`. They can **only** use `SystemPhase.Input`, `BeforeSync`, `PostSimulation`, and `Export`. They use `[UpdateBefore(typeof(...))]` and `[UpdateAfter]` to define strict topological execution order within their phase.
*   **Module Systems (Background Thread):** Registered inside an `IModule` and mapped to `SystemPhase.Simulation`. These systems run asynchronously/parallelized in the background. Do *not* assign Global Systems to the `Simulation` phase.

### 7. Structural Changes Require Command Buffers
You cannot add/remove components or destroy entities while iterating over an `EntityQuery` because it invalidates the underlying memory chunks.
*   **DO:** Acquire the buffer via `var cmd = view.GetCommandBuffer();` and call `cmd.AddComponent<T>()` or `cmd.DestroyEntity()`. The kernel safely applies these at the end of the phase.

---

## 🟡 TIER 3: DISTRIBUTED SIMULATION & NETWORKING

### 8. Granular Component Authority
FDP supports *split authority* (e.g., Node A owns the chassis/movement, Node B owns the turret). Checking the entity's primary owner is not enough.
*   **DON'T:** Just check `if (view.GetComponentRO<NetworkOwnership>(entity).PrimaryOwnerId != _localNodeId)`.
*   **DO:** Check `if (!view.HasAuthority<T>(entity))` before a system mutates component `T`. 
*   **DO:** Remember that if you don't own it, you must send an `UpdateEntityDescriptorRequest` via DDS rather than modifying it locally.

### 9. Transient Event Translation
Single-frame occurrences (firing a weapon, explosions) do not persist in ECS state. They must be explicitly bridged across the network.
*   **DO:** Use `FdpEventBus` for local cross-system signaling (`World.Bus.Publish` / `Consume<T>`).
*   **DO:** Use a `CycloneNativeEventTranslator<TEcs, TDds>` to automatically ferry ECS events to the DDS wire and vice versa.

---

## 🟢 TIER 4: CODE STANDARDS & PERFORMANCE

### 10. High-Performance Logging (`FdpLog`)
String interpolation (`$"..."`) allocates memory. This is fatal on the hot path.
*   **DON'T:** Use `Console.WriteLine` or standard logging directly in `OnUpdate()`.
*   **DO:** Use the strongly-typed `FDP.Kernel.Logging.FdpLog<MySystem>.Info()`.
*   **DO:** Guard expensive logging (multiple parameters or string interpolation) with `if (FdpLog<MySystem>.IsDebugEnabled)` to ensure the JIT compiler completely elides the allocation in production.

### 11. No Magic Numbers
All IDs, thresholds, and capacities must be centralized constants.
*   **DON'T:** Hardcode `[ComponentId(162)]`, `[EventId(5002)]`, or `long tkbType = 101;` inside logic files.
*   **DO:** Reference them from central registries like `GlobalComponentIds.cs`, `CombatConstants.cs`, or `TkbEntityTypes.cs`.

### 12. Simulation Determinism
The simulation must be able to run in Stepped/Lockstep modes or Replay modes seamlessly.
*   **DON'T:** Use `DateTime.UtcNow`, `TimeSpan`, or `Stopwatch` inside `OnUpdate()` logic.
*   **DO:** Use `DeltaTime` (provided by `ComponentSystem`) or read the `GlobalTime` singleton (`World.GetSingletonUnmanaged<GlobalTime>()`) for elapsed time and frame counts.
