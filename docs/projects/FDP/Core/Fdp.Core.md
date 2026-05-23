# Fdp.Core

**Project file**: `FDP/Engine/Fdp.Core/Fdp.Core.csproj`
**Date**: 2026-05-23
**Target framework**: .NET 8.0, C# 12.0
**Unsafe blocks**: Enabled

---

## README Validation

**Status: Present at parent folder level (`FDP/Engine/README.md`) — Diverged from project scope**

A `README.md` exists in the parent `Engine/` folder. It accurately describes the high-level
philosophy of the entire FDP engine: the two-tier data model, zero-allocation hot path,
deterministic execution, and Flight Recorder. The document is generally consistent with what
the source code implements.

However, it is a **conceptual overview of the whole engine** and does not describe any
`Fdp.Core`-specific internal details: the chunk table layout, the AVX2-accelerated
`BitMask256`, the phase system, the delta query algorithm, or the event bus internals.
The README also does not mention the `[ComponentId]` enforcement regime or the
`GlobalComponentIds` catalog — both of which are critically important engineering decisions
implemented in `Fdp.Core`.

Verdict: useful as background reading, but **not a substitute for this document**.

---

## Executive Overview

### What This Project Does

`Fdp.Core` is the foundational ECS (Entity-Component-System) kernel of the Fast Data Plane
(FDP) simulation engine. It provides:

- The **entity lifecycle** — creation, destruction, generation-based stale-reference detection
- The **component storage** layer — native (unmanaged) and managed tiers, backed by
  lazily-committed 64 KB memory chunks
- The **query engine** — bitmask-based entity filtering with optional AVX2 acceleration and
  two-level delta iteration
- The **event bus** — double-buffered, lock-free publish/subscribe for both unmanaged and
  managed events
- The **phase system** — validated phase transitions with per-phase read/write permissions
- The **Flight Recorder** — asynchronous LZ4-compressed binary snapshots and deterministic
  playback
- The **time system** — frame clock, variable-step and fixed-step advancement, `GlobalTime`
  singleton injection
- A set of **foundational component types**: `SimTransform`, `SimVelocity`, `GlobalTime`,
  `HierarchyNode`, `EntityInfo`, `PartDescriptor`, `EpisodeTag`, and `DISEntityType`
- **Serialization helpers**, **logging facades**, and **diagnostic services** used
  throughout the solution

### Role in the Larger Solution

`Fdp.Core` is the **bottom of the dependency graph** in the FDP engine stack. It has
**zero project references** to other FDP assemblies. Every other FDP library — `Fdp.ModuleHost`,
`FDP.Toolkit.DER`, `Fdp.Network.Cyclone`, `Fdp.Examples.*`, the `Hrot` engine, and all
application entry points — depends on `Fdp.Core` directly or transitively.

```
+-------------------------------------------------------+
|         Applications (SimHost, IG, ExCon)             |
+-------------------------------------------------------+
         |             |              |
+--------v----+  +-----v-----+  +----v--------+
| Fdp.ModuleHost | FDP.Toolkit.*  | Hrot.Engine  |
+-------------+  +-----------+  +-------------+
         |             |              |
         +-------> Fdp.Core <---------+
                    (this project)
```

### Architectural Layer

`Fdp.Core` occupies the **Infrastructure / Core** layer. It exposes stable public contracts
(interfaces and abstract types) to higher layers while keeping all high-performance
implementation details internal.

### Key Features

| Feature | Implementation highlight |
|---|---|
| Entity handle | 48-bit: 32-bit index + 16-bit generation |
| Component bitmask | `BitMask256`: 256-bit, AVX2-accelerated |
| Memory strategy | Windows `VirtualAlloc` reserve/commit, 64 KB chunks |
| Max entities | 1,000,000 (compile-time constant) |
| Max component types | 256 (limited by bitmask width) |
| Event streaming | Lock-free double-buffered `NativeEventStream<T>` |
| Recording | Async LZ4-compressed delta snapshots, `.fdp` format |
| Component IDs | Explicit `[ComponentId]` attribute, collision detection |
| Iteration | Zero-allocation `ref struct` enumerators |
| Thread safety | Partial: entity create/destroy locked; component access single-threaded by design |

---

## Architecture

### High-Level Design Decisions

**1. Component-centric storage (not archetype-based)**
Each component type gets its own flat `NativeChunkTable<T>`. Entity `i` always lives at index `i`
in every component table it is registered in. This trades some spatial locality across
components for O(1) random access, simpler migration on structural changes, and straightforward
delta recording.

**2. Explicit component IDs via `[ComponentId]`**
All component types must declare a globally unique integer ID via `[ComponentIdAttribute]`.
IDs are registered in `GlobalComponentIds.cs` which partitions the 256-slot space into named
blocks. This guarantees deterministic IDs when multiple binaries merge into a single process
(a hard requirement for the multi-process Runner architecture).

**3. Windows VirtualAlloc for component memory**
`NativeMemoryAllocator` uses `VirtualAlloc` to reserve the entire address space for up to
1,000,000 entities of each component type upfront, then commits individual 64 KB pages on
demand. The physical RAM cost is near-zero until entities actually use the component.

**4. Double-buffered event streams**
`NativeEventStream<T>` and `ManagedEventStream<T>` separate writing (current frame) from
reading (previous frame). A `Swap()` call at end-of-frame makes the just-written events
visible to consumers next frame and recycles the old read buffer. This eliminates read/write
contention entirely during simulation.

**5. Zero-allocation iteration**
All entity enumeration uses C# `ref struct` enumerators. The `foreach` pattern is supported
via `GetEnumerator()` returning a stack-allocated value type — no `IEnumerable<T>`, no
`IEnumerator<T>`, no heap objects.

**6. Phase-gated access control**
`EntityRepository` tracks a `Phase` object that represents the current engine execution
stage. `PhaseConfig` defines valid transitions and per-phase write permissions
(`ReadOnly`, `ReadWriteAll`, `OwnedOnly`, `UnownedOnly`). This prevents accidental writes
during read-only passes and catches ordering bugs at runtime.

**7. Debug build paranoia (`FDP_PARANOID_MODE`)**
The `Debug` configuration defines `FDP_PARANOID_MODE`. Under this flag, range checks are
inserted into every hot-path method (`BitMask256`, `NativeChunkTable`, `NativeMemoryAllocator`)
that are stripped entirely in Release.

### Key Abstractions and Patterns

| Abstraction | Type | Purpose |
|---|---|---|
| `Entity` | `readonly struct` | Lightweight handle (index + generation) |
| `EntityHeader` | `struct` (96 bytes) | Per-entity metadata in `EntityIndex` |
| `BitMask256` | `struct` (32 bytes) | 256-bit presence/query mask |
| `EntityRepository` | `sealed partial class` | Central ECS world; owns all component tables |
| `EntityQuery` | `sealed class` | Compiled query; immutable, reusable |
| `NativeChunkTable<T>` | `sealed class` | Per-component paged array |
| `NativeEventStream<T>` | `class` | Double-buffered unmanaged event buffer |
| `FdpEventBus` | `class` | Typed publish/subscribe hub |
| `Phase` / `PhaseConfig` | `class` | Engine phase definition + transition rules |
| `AsyncRecorder` | `class` | Async LZ4 delta snapshot writer |
| `PlaybackSystem` | `class` | Frame-by-frame state restoration |
| `TimeSystem` | `class` | Game-loop clock, pushes `GlobalTime` singleton |

### Constraints

- **No heap allocation on the hot path** (simulation loop). GC collections would violate
  real-time latency requirements.
- **Component access is single-threaded** by design. Multi-threaded writes would require
  expensive synchronization on every component read.
- **Entity IDs are dense integers**, enabling O(1) array-indexed access but requiring a
  fixed maximum cap of 1,000,000.
- **Recording format version** (`FdpConfig.FORMAT_VERSION = 4`) must be incremented on any
  binary layout change. Recordings are **not** backwards-compatible.
- **Windows-only native memory**: `NativeMemoryAllocator` uses `VirtualAlloc` / `VirtualFree`
  from `kernel32.dll`. Non-Windows platforms are not currently supported.

### Extension Points

- Register custom component types via `ComponentType<T>.ID` and `[ComponentId(...)]`
- Register custom event types via `[EventId(...)]` and `FdpEventBus.Register<T>()`
- Add custom phases via `new Phase("MyPhase")` and extend `PhaseConfig`
- Subscribe to entity lifecycle via `NativeEventStream<EntityLifecycleEvent>`
- Filter recording via `AsyncRecorder.EntityFilter` predicate
- Control parallel workload via `FdpConfig.MaxDegreeOfParallelism`

---

## ASCII Block Diagrams

### Diagram 1 — Internal Component Architecture

```
Fdp.Core Internal Components
=============================================================================

  +---------------------+       +-----------------------------+
  |    EntityRepository  |       |       EntityIndex            |
  |  (Partial class)     |       |  NativeChunkTable<Header>   |
  |                      |------>|  Free-list allocator         |
  |  Bus: FdpEventBus    |       |  MaxIssuedIndex, ActiveCount |
  |  PhaseConfig         |       +-----------------------------+
  |  GlobalVersion       |
  |  _tableCache[]       |       +-----------------------------+
  |  _singletons[]       |       |   NativeChunkTable<T>        |
  +---------------------+       |  (per component type T)      |
           |                    |  64KB chunks, VirtualAlloc    |
           +------ create ------>|  PaddedVersion per chunk     |
                                |  population counts            |
                                +-----------------------------+

  +---------------------+       +-----------------------------+
  |   EntityQuery        |       |        BitMask256            |
  |  includeMask         |------>|  4 x ulong = 256 bits        |
  |  excludeMask         |       |  AVX2 accelerated Matches()  |
  |  authorityMasks      |       |  SetBit/ClearBit/IsSet       |
  |  DIS filter          |       +-----------------------------+
  |  lifecycle filter    |
  |  Zero-alloc enum     |
  +---------------------+

  +---------------------+       +-----------------------------+
  |     FdpEventBus      |       |    NativeEventStream<T>     |
  |  _nativeStreams       |------>|  Double buffer (read/write) |
  |  _managedStreams      |       |  Lock-free Write()          |
  |  Publish<T>/Read<T>  |       |  Swap() at end of frame     |
  |  SwapBuffers()        |       +-----------------------------+
  +---------------------+
                                 +-----------------------------+
                                 |     ManagedEventStream<T>   |
                                 |  List<T> front/back         |
                                 |  Lock for thread safety     |
                                 +-----------------------------+
```

### Diagram 2 — Data Flow Through a Simulation Frame

```
Simulation Frame Lifecycle
=============================================================================

  FRAME START
      |
      v
  [TimeSystem.Update()]
      | Reads wall clock, applies TimeScale
      | Pushes GlobalTime singleton into EntityRepository
      v
  [EntityRepository.Tick()]
      | Increments GlobalVersion (atomic)
      v
  [EntityRepository.SetPhase(NetworkReceive)]
      |
      v
  [Network / Input Systems]
      | Read from NativeEventStream<T> (read buffer = prev frame)
      | Write new events to NativeEventStream<T> (write buffer)
      | Issue creates/destroys/component sets via EntityCommandBuffer
      v
  [EntityCommandBuffer.Playback()]
      | Execute deferred structural changes on main thread
      v
  [EntityRepository.SetPhase(Simulation)]
      |
      v
  [Simulation Systems]
      | Query entities via EntityQuery (zero-alloc foreach)
      | Read component refs via GetRefRO<T>()
      | Write components via GetRefRW<T>() -> bumps chunk version
      | Publish events via FdpEventBus.Publish<T>()
      v
  [EntityRepository.SetPhase(NetworkSend)]
      |
      v
  [Delta Query (QueryDelta)]
      | Level 1: Skip chunks whose version <= sinceVersion
      | Level 2: Check individual entity structural changes
      | Export changed components to network
      v
  [FdpEventBus.SwapBuffers()]
      | Swaps read/write buffers in all event streams
      | Write buffer cleared, read buffer populated from prev writes
      v
  [AsyncRecorder.CaptureFrame()] (if recording)
      | Copies changed chunk data into front buffer
      | Background thread: LZ4-compresses and writes to .fdp file
      v
  FRAME END
```

### Diagram 3 — Project Dependency Diagram

```
Fdp.Core Dependency Tree
=============================================================================

  Fdp.Core (this project)
  |
  +-- NuGet: K4os.Compression.LZ4 v1.3.8
  |         Used by: AsyncRecorder (frame compression)
  |
  +-- NuGet: NLog v5.2.8
            Used by: FdpLog<T> (structured logging facade)

  InternalsVisibleTo:
  +-- Fdp.Core.Tests        (unit tests)
  +-- Fdp.ModuleHost        (kernel orchestration)
  +-- Fdp.ModuleHost.Tests  (integration tests)
  +-- Fdp.Tests             (legacy test project)

  Who depends on Fdp.Core (consumers, partial list):
  +-- Fdp.ModuleHost
  +-- Fdp.Presentation
  +-- FDP.Toolkit.DER
  +-- Fdp.Toolkits
  +-- Fdp.Network.Cyclone
  +-- Fdp.Engine  (Toolkit)
  +-- Hrot.Engine
  +-- Hrot.Subsystems.*
  +-- All example and runner projects
```

---

## Source Structure Analysis

### Namespaces

| Namespace | Contents |
|---|---|
| `Fdp.Core` | Main ECS primitives, entity types, event types, config |
| `Fdp.Core.Collections` | `NativeArray<T>` |
| `Fdp.Core.CommandHierarchy` | Tactical hierarchy components and events |
| `Fdp.Core.Diagnostics` | `DiagnosticEventHistoryService` |
| `Fdp.Core.FlightRecorder` | Recording and playback infrastructure |
| `Fdp.Core.FlightRecorder.Metadata` | Recording metadata (`RecordingMetadata`) |
| `Fdp.Core.Internal` | `PaddedVersion`, `BatchListPool`, `UnsafeShim` |
| `Fdp.Core.Logging` | `FdpLog<T>`, `MessageLog`, `AiBehaviorLogTarget` |
| `Fdp.Core.Orchestration` | `IRecordReplayController`, `CheckpointIOWorker` |
| `Fdp.Core.Serialization` | JSON options registry and converters |
| `Fdp.ModuleHost.Abstractions` | `ISimulationView` |
| `Fdp.Interfaces` | `INetworkMaster`, `ITkbDatabase`, `PackedKey`, `TkbTemplate` |
| `Fdp.Toolkit.Replication` | `INetworkTopology` |

### Key Files and Classes

**Root level — Entity Identity**

- **`Entity.cs`** — `Entity` struct. 48-bit handle: 32-bit index + 16-bit generation.
  `IsNull` checks both index < 0 and generation == 0. `PackedValue` serializes to `ulong`.
  Generation starts at 1 (never 0) to prevent stale-handle collisions with
  `default(Entity)`.

- **`EntityHeader.cs`** — `EntityHeader` struct (96 bytes, `[StructLayout(Explicit)]`).
  Stored in `NativeChunkTable<EntityHeader>` as the master record for every entity slot.
  Fields: `ComponentMask` (BitMask256 @ offset 0), `AuthorityMask` (BitMask256 @ offset 32),
  `Generation` (ushort @ 64), `Flags` (ushort @ 66), `LastChangeTick` (ulong @ 68),
  `DisType` (DISEntityType @ 76), `LifecycleState` (EntityLifecycle @ 84). The 96-byte
  size is a multiple of both 32 (AVX2 register) and 64 (cache line).

- **`EntityIndex.cs`** — `EntityIndex`. Manages the `NativeChunkTable<EntityHeader>`.
  Implements a free-list allocator: destroyed entity IDs are pushed onto `_freeList` and
  reused first. `MaxIssuedIndex` tracks iteration bounds. Thread-safe create/destroy via
  `_createLock`.

- **`EntityLifecycle.cs`** / **`EntityLifecycleState.cs`** — `EntityLifecycle` enum
  (`Constructing`, `Active`, `TearDown`, `Ghost`, `All`) and `EntityLifecycleEvent` struct
  (written to `NativeEventStream<EntityLifecycleEvent>` on creation/destruction).

**Root level — Component System**

- **`ComponentType.cs`** — Two static generic types:
  - `ComponentType<T>` (where `T : unmanaged`) — provides `.ID` and `.Size`; calls
    `ComponentTypeRegistry.GetOrRegister<T>()`.
  - `ManagedComponentType<T>` (where `T : class`) — same ID space, used for Tier-2
    components.
  - `ComponentTypeRegistry` (static) — central thread-safe registry; enforces
    `[ComponentId]` on all types; detects ID collisions.

- **`ComponentIdAttribute.cs`** — `[ComponentId(byte id)]`. Must be placed on every
  component struct, class, or interface. Stores a `byte` ID in range [0, 255].

- **`GlobalComponentIds.cs`** — `static class GlobalComponentIds`. Centralizes all
  component ID constants in named allocation blocks:
  - IDs 0–19: `Fdp.Core` core components
  - IDs 20–49: FDP toolkit expansion
  - IDs 50–79: `FDP.Toolkit.Replication`
  - IDs 80–109: IG components
  - IDs 110–139: `Fdp.ModuleHost` network components
  - IDs 140–159: Application-level descriptors
  - IDs 160–199: Application-level descriptor components
  - IDs 200–255: Reserved

- **`BitMask256.cs`** — `BitMask256` struct (32 bytes, `Pack = 32`). Four `ulong` fields
  covering 256 bits. `SetBit`/`ClearBit`/`IsSet` use integer shift math. `Matches()`
  dispatches to `Avx2Matches()` on supported CPUs using `vmovdqu` + three AVX2
  instructions to check include AND exclude masks in one pass.

- **`IComponentTable.cs`** — `IComponentTable` interface used by `EntityRepository` to
  store heterogeneous component tables in `_tableCache[]` and `_componentTables`.
  Methods: `HasComponent(int)`, `RemoveComponent(int)`, `GetVersionForEntity(int)`.

- **`ComponentTable.cs`** / **`NativeChunkTable.cs`** — `NativeChunkTable<T>` stores
  unmanaged components in 64 KB pages lazily committed from reserved address space.
  `GetRefRW(entityId, currentVersion)` is the primary write accessor; it bumps the
  chunk version via `PaddedVersion`. `GetRefRO(entityId)` does not bump. `HasChanges(
  sinceVersion)` scans chunk versions in a tight loop (L1-cache friendly).

- **`ManagedComponentTable.cs`** — Stores managed (class) components as `T?[]` arrays,
  one element per entity slot. Analogous to `NativeChunkTable<T>` but on the managed heap.

- **`ComponentMetadataTable.cs`** — Stores `PartDescriptor` per (entity, component-type)
  pair, used for network partial-update synchronization.

**Root level — Entity Repository**

- **`EntityRepository.cs`** — Core `partial class`. Owns `EntityIndex`, all component
  tables, the event bus, phase tracking, singletons storage, and the destruction log.
  Key public surface:
  - `EntityCount`, `MaxEntityIndex`, `GlobalVersion`
  - `Tick()` — increments global version each frame
  - `SetPhase(Phase)` — validated phase transition
  - `CreateEntity()`, `DestroyEntity(Entity)`, `IsAlive(Entity)`
  - `AddComponent<T>()`, `SetComponent<T>()`, `RemoveComponent<T>()`, `GetRefRW<T>()`,
    `GetRefRO<T>()`, `HasComponent<T>()`
  - `Query()` — returns `QueryBuilder`
  - `SetSingletonUnmanaged<T>()`, `GetSingletonUnmanaged<T>()`
  - `UnmanagedHandle` — GCHandle IntPtr for HSM bridge

- **`EntityRepository.DeltaQuery.cs`** — `QueryDelta(query, sinceVersion)` returning
  `DeltaQueryEnumerable`. Implements two-level chunk-skip: Level 1 skips whole 64 KB
  blocks if no component-table chunk version exceeds `sinceVersion`; Level 2 walks
  individual entities in hot chunks.

- **`EntityRepository.View.cs`** — Implements `ISimulationView` (thread-safe read-only
  view used by background modules). Provides per-thread `EntityCommandBuffer` via
  `ThreadLocal<EntityCommandBuffer>`.

- **`EntityRepository.Sync.cs`** — Synchronization helpers for network replication.

**Root level — Query**

- **`EntityQuery.cs`** — `EntityQuery` class. Immutable post-construction (thread-safe).
  `EntityEnumerator` is a `ref struct` with a tight `MoveNext()` loop. Supports lifecycle
  filter, DIS type filter, component include/exclude, and authority include/exclude.

- **`QueryBuilder.cs`** — `QueryBuilder`. Fluent API: `With<T>()`, `Without<T>()`,
  `WithManaged<T>()`, `WithOwned<T>()`, `WithDisType()`, `WithLifecycle()`,
  `IncludeConstructing()`, `Build()`. `WithComponentId(int)` allows filtering by raw ID
  without a direct type reference.

**Root level — Events**

- **`NativeEventStream.cs`** — `NativeEventStream<T>` (where `T : unmanaged`).
  True double-buffering: atomic `Interlocked.Increment` for lock-free reservation on the
  write path. Auto-expands via a locked resize slow path. `Swap()` rotates buffers.
  `GetPendingBytes()` / `InjectIntoCurrent()` are used by the Flight Recorder.

- **`UntypedNativeEventStream.cs`** — Type-erased wrapper for events received before
  their typed stream is registered (e.g. during replay bootstrap).

- **`ManagedEventStream.cs`** — Double-buffered `List<T>` for managed events. All writes
  lock `_lock`. `Swap()` atomically rotates lists.

- **`FdpEventBus.cs`** — Central event hub. Maintains `ConcurrentDictionary` maps for
  native (`int -> INativeEventStream`) and managed (`int -> ManagedEventStream<T>`) streams.
  `Publish<T>(evt)` — fast path via `GetOrAdd` then `Write`. `Read<T>()` returns a
  `ReadOnlySpan<T>` from the read buffer. `SwapBuffers()` iterates all streams and calls
  `Swap()` on each. `HasEvent<T>()` uses a pre-built `_activeEventIds` set to avoid
  lock acquisition.

- **`EventAccumulator.cs`** — Rolling history buffer (default 60 frames). Captures event
  snapshots and replays them into a replica bus for slow/background modules that missed
  frames.

- **`EventType.cs`** — `EventType<T>` static class — exposes `.Id` from the `[EventId]`
  attribute. `EventTypeRegistry` validates uniqueness at startup.

- **`EventIdAttribute.cs`** — `[EventId(int id)]` applied to event structs or classes.

**Root level — Phase System**

- **`Phase.cs`** — `Phase` class with `Name` and integer `Id` (from `PhaseRegistry`).
  Static well-known instances: `Initialization`, `NetworkReceive`, `Simulation`,
  `NetworkSend`, `Presentation`. Equality is integer comparison (O(1)).
  `PhaseConfig` — string-based transition map and permission map, internally caches
  to integer dictionaries for hot-path O(1) lookup.
  `WrongPhaseException` — thrown by `EntityRepository.AssertPhase()`.
  `PhasePermission` enum — `ReadOnly`, `ReadWriteAll`, `OwnedOnly`, `UnownedOnly`.

**Root level — Memory**

- **`FdpConfig.cs`** — Global constants: `MAX_ENTITIES = 1_000_000`, `CHUNK_SIZE_BYTES = 65536`,
  `MAX_COMPONENT_TYPES = 256`, `FORMAT_VERSION = 4`, `SYSTEM_ID_RANGE`. Runtime settings:
  `EnforceExplicitComponentIds`, `EnforceExplicitEventRegistration`,
  `MaxDegreeOfParallelism`. `ParallelHint` enum for workload classification.

- **`NativeMemoryAllocator.cs`** — P/Invoke to `kernel32.dll` `VirtualAlloc` /
  `VirtualFree`. `Reserve(long)` allocates address space (zero RAM cost).
  `Commit(void*, long)` backs pages with RAM. `Decommit(void*, long)` releases RAM while
  keeping address reservation. `Free(void*, long)` releases reservation entirely.

- **`NativeChunk.cs`** — Wraps a pointer into a committed 64 KB chunk for one component
  type. Computes `chunkBase + localOffset * sizeof(T)`.

**Root level — Utility Types**

- **`BitMask256.cs`** — (see above)
- **`FixedString32.cs`** — Stack-allocated UTF-8 string, 31 chars max. Used in components
  to avoid heap allocations for short names.
- **`FixedString64.cs`** — Same pattern, 63 chars max.
- **`PartDescriptor.cs`** — Wraps `BitMask256` to track which 64-byte parts of a large
  component are present (for partial network updates). `[ComponentId(7)]`.
- **`MultiPartComponent.cs`** — Static helper: `GetPartCount<T>()`, `GetPartOffset(int)`,
  `GetPartSize<T>(int)`. Part size is 64 bytes (cache line aligned).
- **`HierarchyNode.cs`** — Doubly-linked list component for parent/child relationships.
  Fields: `Parent`, `FirstChild`, `PreviousSibling`, `NextSibling` (all `Entity` handles).
- **`EntityCommandBuffer.cs`** — Byte-stream command recorder. Supports:
  `CreateEntity()`, `DestroyEntity()`, `AddComponent<T>()`, `SetComponent<T>()`,
  `RemoveComponent<T>()`, and managed variants. `Playback(repo)` replays commands in order.
- **`EpisodeTag.cs`** — `[ComponentId(84)]` tag marking entities belonging to a specific
  episode (Guid). `[DataPolicy(DataPolicy.NoSave)]`.
- **`DISEntityType.cs`** — `[StructLayout(Explicit, Size=8)]` overlay struct allowing access
  as both a `ulong` and named DIS fields (Kind, Domain, Country, Category, Subcategory,
  Specific, Extra).

**Root level — Core Components**

- **`CoreComponents/SimComponents.cs`** — `SimTransform` (`[ComponentId(0)]`): `Vector3 Position`
  + `Quaternion Rotation`. `SimVelocity` (`[ComponentId(1)]`): `Vector3 Linear` + `Vector3 Angular`.
- **`GlobalTime.cs`** — `GlobalTime` singleton (`[ComponentId(3)]`). Fields: `TotalTime`,
  `DeltaTime`, `TimeScale`, `FrameNumber`, `StartWallTicks`, `UnscaledDeltaTime`,
  `UnscaledTotalTime`, `TotalWallTicks`.
- **`TimeSystem.cs`** — Manages game-loop clock. `Update(budgetMs)` for variable-step;
  `Step(fixedDt)` for deterministic fixed-step. Pushes `GlobalTime` into ECS world via
  `SetSingletonUnmanaged`.

**FlightRecorder**

- **`RecorderSystem.cs`** — Core serialization. Writes delta frames: destructions list,
  events, singletons, then dirty component chunks (bitwise scan via `HasChanges`). Entity
  index headers are also captured to preserve structural changes. `FillLiveness` + chunk
  sanitization zero out dead-entity slots before writing.
- **`PlaybackSystem.cs`** — Reads binary frames: destructions, events, singletons, chunks.
  Calls `repo.GetEntityIndex().RebuildMetadata()` after applying all data. `ApplyFrame`
  handles both keyframe (full-state reset) and delta types.
- **`AsyncRecorder.cs`** — Wraps `RecorderSystem` with 32 MB double buffers, background
  I/O worker, and LZ4 compression. `MinRecordableId` and `EntityFilter` control scope.
- **`FrameOuterHeader.cs`** — Binary contract: `[CompressedSize:4][UncompressedSize:4]
  [Tick:8][FrameType:1][WallClockTicks:8]` = 25 bytes total.
- **`RecordingGlobalHeader.cs`** — Binary contract: `[Magic:6][FormatVersion:4]
  [Timestamp:8]` = 18 bytes total.
- **`FdpAutoSerializer.cs`** — Compile-time expression-tree serializer for arbitrary
  component types (used for managed component recording).
- **`ComponentLayoutHasher.cs`** / **`SchemaValidator.cs`** — Hash the struct layout of
  all registered component types; validate recording schema matches current binary before
  playback begins.

**Logging**

- **`FdpLog<T>.cs`** — Generic static facade over NLog. Exposes `IsTraceEnabled` etc.
  as fast boolean checks to guard string interpolations. All log methods are
  `[AggressiveInlining]`.
- **`MessageLog.cs`** — In-process log sink. `MessageLogEntry` is an immutable record with
  pre-tokenized `LogChunk` segments for zero-allocation UI rendering.
- **`NLogMessageLogTarget.cs`** — NLog target that routes to `MessageLog`.
- **`AiBehaviorLogTarget.cs`** — Specialized NLog target for AI behavior logging.

**Serialization**

- **`Serialization/FdpJsonOptionsRegistry.cs`** — Centralizes `JsonSerializerOptions`
  configuration for the whole engine.
- **`Serialization/Converters/FixedStringConverters.cs`** — JSON converters for
  `FixedString32` / `FixedString64`.
- **`Serialization/Converters/StrictStringEnumConverter.cs`** — Enum JSON converter that
  rejects unknown string values.
- **`Serialization/Converters/VectorArrayConverters.cs`** — JSON converters for
  `System.Numerics.Vector3`, `Quaternion`, `float[]` arrays.

**Abstractions (`Abstractions/`)**

- **`ISimulationView.cs`** — Read-only interface into the ECS world for background modules.
  Methods: `GetComponentRO<T>()`, `HasComponent<T>()`, `ReadEvents<T>()`, `Query()`,
  `GetCommandBuffer()`. Implemented by `EntityRepository` (partial).
- **`ITkbDatabase.cs`** — Interface for the TKB (Type Knowledge Base) template database.
  Methods: `Register(template)`, `GetByType(long)`, `GetByName(string)`, etc.
- **`INetworkMaster.cs`** — Minimal contract for network-authoritative entity descriptors.
  Properties: `EntityId` (long), `TkbType` (long).
- **`INetworkTopology.cs`** — Network topology discovery: `LocalNodeId`, `GetExpectedPeers(long)`,
  `GetAllNodes()`.
- **`INetworkTranslator.cs`** / **`INetworkEventTranslator.cs`** — Translation contracts
  between ECS state and network protocol messages.
- **`TkbTemplate.cs`** — Blueprint template base type. Contains mandatory components and
  descriptors for entity construction.
- **`PackedKey.cs`** — Utility to pack (ordinal, instanceId) into a single `long`:
  `[High 32 bits: Ordinal][Low 32 bits: InstanceId]`.

**CommandHierarchy (`CommandHierarchy/`)**

- **`TacticalDesignation.cs`** — `enum TacticalDesignation : ushort`
  (`Undefined`, `Commander`, `SquadLeader`, `Wingman`, `Support`).
- **`UnitSubordinate.cs`** — Component tracking subordinate membership.
- **`UnitRoster.cs`** — Component or helper for unit command hierarchy roster.
- **`CommandHierarchyEvents.cs`** — Event types for command hierarchy changes.

**Diagnostics (`Diagnostics/`)**

- **`DiagnosticEventHistoryService.cs`** — Thread-safe circular buffer (capacity 500).
  Captures event metadata from all `IEventStreamInspector` instances on the event bus.
  Used by the debug UI to display a scrolling event history.
- **`IDiagnosticEventHistoryService.cs`** — Interface for the above.

**Internal (`Internal/`)**

- **`PaddedVersion.cs`** — `[StructLayout(Explicit, Size=64)]`. A single `uint` at offset 0,
  padded to a full cache line to prevent false sharing when chunk version arrays are
  updated by multiple threads.
- **`BatchListPool.cs`** — Object pool for `List<T>` to reduce allocations during batch
  operations.
- **`UnsafeShim.cs`** — Thin wrappers around `System.Runtime.CompilerServices.Unsafe`
  for cases where generic constraints differ.

**Collections (`Collections/`)**

- **`NativeArray<T>.cs`** — Unsafe unmanaged array backed by `Marshal.AllocHGlobal`.
  `IDisposable`, zero-initializes on construction, bounds-checked indexer.
  `Allocator` enum (`None`, `Temp`, `Persistent`).

**Orchestration (`Orchestration/`)**

- **`IRecordReplayController.cs`** — Application-agnostic async interface for the
  record/replay lifecycle: `PrepareRecordingAsync`, `FinalizeRecordingAsync`,
  `PrepareReplayAsync`, `SeekToTimeAsync`, `ProcessPlaybackTick`, `TeardownReplayAsync`.
  Also exposes `IsReplayActive`, `GetCurrentReplayTime()`, `ActiveReplayDurationSeconds`.
- **`CheckpointIOWorker.cs`** — Background worker for checkpoint I/O operations.

---

## Public API Reference

### Core Types

#### `Entity` (struct)

```csharp
public readonly struct Entity : IEquatable<Entity>
```

Lightweight entity handle. 48 bits total: 32-bit array index + 16-bit generation counter.
The generation number increments each time an entity at a given index is destroyed and
reissued, allowing stale references to be detected in O(1).

| Member | Description |
|---|---|
| `int Index` | Entity slot in the ECS array. |
| `ushort Generation` | Stale-reference guard. Starts at 1, never 0. |
| `ulong PackedValue` | `(Generation << 32) | Index`. For ECB serialization. |
| `bool IsNull` | True when `Index < 0 || Generation == 0`. |
| `static Entity Null` | Null sentinel value. |
| `Entity(int, ushort)` | Explicit constructor. |
| `Entity(ulong packed)` | Deserialization constructor. |
| `bool Equals(Entity)`, `==`, `!=` | Value equality. |
| `string ToString()` | `"Entity(3, v2)"` or `"Entity.Null"`. |

#### `EntityHeader` (struct, 96 bytes)

Internal slot record stored in the entity index. Explicit layout.

| Field | Offset | Type | Description |
|---|---|---|---|
| `ComponentMask` | 0 | `BitMask256` | Which components are present. |
| `AuthorityMask` | 32 | `BitMask256` | Which components this node owns. |
| `Generation` | 64 | `ushort` | Matches `Entity.Generation`. |
| `Flags` | 66 | `ushort` | Bit 0 = `IsActive`. |
| `LastChangeTick` | 68 | `ulong` | Frame tick of last structural change. |
| `DisType` | 76 | `DISEntityType` | Full 8-byte DIS type. |
| `LifecycleState` | 84 | `EntityLifecycle` | `Constructing/Active/TearDown/Ghost`. |

Methods: `IsActive`, `SetActive(bool)`, `Clear()`.

#### `BitMask256` (struct, 32 bytes)

256-bit bitmask optimized for AVX2. Must be 32-byte aligned in heap allocations.

| Member | Description |
|---|---|
| `SetBit(int bitIndex)` | Sets a bit (0–255). |
| `ClearBit(int bitIndex)` | Clears a bit. |
| `bool IsSet(int bitIndex)` | Tests a bit. |
| `Clear()` | Zeros all bits. |
| `SetAll()` | Sets all 256 bits. |
| `bool IsEmpty()` | True if all bits are 0. |
| `BitwiseAnd(in BitMask256)` | In-place AND. |
| `BitwiseOr(in BitMask256)` | In-place OR. |
| `static bool Matches(in target, in include, in exclude)` | AVX2-accelerated: `(target & include) == include && (target & exclude) == 0`. |
| `static bool HasAll(in source, in required)` | All bits in `required` are set in `source`. |
| `static bool HasAny(in source, in test)` | Any bit in `test` is set in `source`. |
| `bool Equals(BitMask256)`, `==`, `!=` | Value equality. |

#### `EntityRepository` (class)

Central ECS world. `IDisposable`, `partial` (split across four files).

**Properties**

| Property | Description |
|---|---|
| `int EntityCount` | Number of active entities. |
| `int MaxEntityIndex` | Highest index ever issued (iteration bound). |
| `uint GlobalVersion` | Current change version, incremented by `Tick()`. |
| `Phase CurrentPhase` | Active execution phase. |
| `PhaseConfig PhaseConfig` | Transition / permission configuration. |
| `FdpEventBus Bus` | Event bus for publish/subscribe. |
| `float SimulationTime` | Current simulation time (seconds). |
| `IntPtr UnmanagedHandle` | GCHandle pointer for HSM bridge. |
| `TimeSliceMetric DefaultTimeSliceMetric` | `WallClockTime` or `EntityCount`. |

**Entity lifecycle**

| Method | Description |
|---|---|
| `Entity CreateEntity()` | Allocates an entity (free-list or new index). |
| `void DestroyEntity(Entity)` | Deactivates entity, recycles index. |
| `bool IsAlive(Entity)` | Checks index + generation match. |
| `void ReserveIdRange(int maxId)` | Reserves low IDs for system entities. |
| `void Clear()` | Destroys all entities and resets state. |

**Component operations (unmanaged Tier 1)**

| Method | Description |
|---|---|
| `void AddComponent<T>(Entity, in T)` | Adds component; sets bitmask bit. |
| `void SetComponent<T>(Entity, in T)` | Adds or updates component. |
| `void RemoveComponent<T>(Entity)` | Removes component; clears bitmask bit. |
| `bool HasComponent<T>(Entity)` | Tests component mask. |
| `ref T GetRefRW<T>(Entity)` | Write reference; bumps chunk version. |
| `ref readonly T GetRefRO<T>(Entity)` | Read-only reference; no version bump. |

**Component operations (managed Tier 2)**

| Method | Description |
|---|---|
| `void AddManagedComponent<T>(Entity, T)` | Stores managed object. |
| `void SetManagedComponent<T>(Entity, T)` | Stores or replaces managed object. |
| `void RemoveManagedComponent<T>(Entity)` | Removes managed object. |
| `bool HasManagedComponent<T>(Entity)` | Tests managed component presence. |
| `T? GetManagedComponent<T>(Entity)` | Returns managed object or null. |

**Singleton operations**

| Method | Description |
|---|---|
| `void SetSingletonUnmanaged<T>(in T)` | Stores singleton component (capacity-1 table). |
| `ref T GetSingletonUnmanaged<T>()` | Read/write reference to singleton. |
| `ref readonly T GetSingletonRO<T>()` | Read-only singleton reference. |

**Query and iteration**

| Method | Description |
|---|---|
| `QueryBuilder Query()` | Returns a fluent query builder. |
| `DeltaQueryEnumerable QueryDelta(query, sinceVersion)` | Two-level chunk-skip delta iterator. |
| `void Tick()` | Increments `GlobalVersion` atomically. |

**Phase management**

| Method | Description |
|---|---|
| `void SetPhase(Phase)` | Validates and sets current phase. |
| `void AssertPhase(Phase)` | Throws `WrongPhaseException` if mismatch. |

#### `EntityQuery` (class)

Immutable compiled query. Constructed via `QueryBuilder.Build()`.

| Member | Description |
|---|---|
| `EntityEnumerator GetEnumerator()` | Zero-allocation `ref struct` enumerator for `foreach`. |
| `bool IsEmpty` | True if no entity currently matches. |
| `[Obsolete] void ForEach(Action<Entity>)` | Allocates closures; use `foreach` instead. |

`EntityEnumerator` is a `ref struct` supporting `Current` (`Entity`) and `MoveNext()`.

#### `QueryBuilder` (class)

Fluent builder returned by `EntityRepository.Query()`.

| Method | Description |
|---|---|
| `QueryBuilder With<T>()` | Require unmanaged component T. |
| `QueryBuilder Without<T>()` | Exclude unmanaged component T. |
| `QueryBuilder WithManaged<T>()` | Require managed component T. |
| `QueryBuilder WithoutManaged<T>()` | Exclude managed component T. |
| `QueryBuilder WithOwned<T>()` | Require T with local authority. |
| `QueryBuilder WithoutOwned<T>()` | Exclude T where local authority is set. |
| `QueryBuilder WithComponentId(int)` | Filter by raw integer ID (no type reference needed). |
| `QueryBuilder WithDisType(DISEntityType, ulong mask)` | Filter by DIS entity type. |
| `QueryBuilder WithLifecycle(EntityLifecycle)` | Filter by lifecycle state. |
| `QueryBuilder IncludeConstructing()` | Include `Constructing` entities. |
| `QueryBuilder IncludeTearDown()` | Include `TearDown` entities. |
| `EntityQuery Build()` | Creates immutable query. |

#### `FdpEventBus` (class)

Double-buffered publish/subscribe hub. `IDisposable`, `IEventBus`.

| Method | Description |
|---|---|
| `void Publish<T>(T evt) where T : unmanaged` | Thread-safe event publish. |
| `void PublishManaged<T>(T evt)` | Publish managed event. |
| `ReadOnlySpan<T> Read<T>() where T : unmanaged` | Read events from previous frame. |
| `IReadOnlyList<T> ReadManaged<T>()` | Read managed events from previous frame. |
| `bool HasEvent<T>()` | Check if any events of type T were published last frame. |
| `bool HasManagedEvent<T>()` | Managed variant of HasEvent. |
| `void Register<T>()` | Pre-register native event stream. |
| `void RegisterManaged<T>()` | Pre-register managed event stream. |
| `void SwapBuffers()` | Rotate all buffers (end of frame). |
| `void PrepareForNativeEventReplay<T>()` | Ensure typed stream for playback injection. |

#### `NativeEventStream<T>` (class)

Lock-free, auto-expanding unmanaged event buffer.

| Method | Description |
|---|---|
| `void Write(in T evt)` | Lock-free write via `Interlocked.Increment`. |
| `ReadOnlySpan<T> Read()` | Read buffer (stable after Swap). |
| `ReadOnlySpan<byte> GetRawBytes()` | Raw bytes of read buffer. |
| `void Swap()` | Rotate buffers. |
| `void Clear()` | Clear both buffers. |
| `ReadOnlySpan<byte> GetPendingBytes()` | Write buffer bytes (Flight Recorder use). |
| `void InjectIntoCurrent(ReadOnlySpan<byte>)` | Inject bytes into read buffer (playback use). |
| `int Count` | Event count in read buffer. |
| `int EventTypeId` | From `[EventId]` attribute. |
| `int ElementSize` | `sizeof(T)`. |

#### `EntityCommandBuffer` (class)

Byte-stream deferred command recorder. Thread-safe per buffer instance.

| Method | Description |
|---|---|
| `Entity CreateEntity()` | Records create; returns placeholder with negative index. |
| `void DestroyEntity(Entity)` | Records destruction. |
| `void AddComponent<T>(Entity, in T)` | Records add. |
| `void AddEmptyComponent<T>(Entity)` | Records add with zero-initialized value. |
| `void SetComponent<T>(Entity, in T)` | Records set (add or update). |
| `void RemoveComponent<T>(Entity)` | Records remove. |
| `void AddManagedComponent<T>(Entity, T)` | Records managed add. |
| `void SetManagedComponent<T>(Entity, T)` | Records managed set. |
| `void PublishEvent<T>(in T)` | Records event publish. |
| `void Playback(EntityRepository)` | Executes all recorded commands. |
| `void Clear()` | Resets buffer for reuse. |

#### `NativeChunkTable<T>` (class)

Per-component virtual memory page table.

| Member | Description |
|---|---|
| `int ChunkCapacity` | Elements per 64 KB chunk. |
| `int TotalChunks` | Total chunk slots. |
| `ref T this[int entityId]` | Lazy-allocating indexed accessor (no version bump). |
| `ref T GetRefRW(int entityId, uint version)` | Write ref; bumps chunk version to `version`. |
| `ref readonly T GetRefRO(int entityId)` | Read-only ref. |
| `bool HasChanges(uint sinceVersion)` | Scans chunk versions in O(chunks). |
| `uint GetChunkVersion(int chunkIndex)` | Version of a chunk. |
| `int GetPopulationCount(int chunkIndex)` | Non-zero entity count in chunk. |

#### `NativeMemoryAllocator` (static class)

Windows `VirtualAlloc` wrapper.

| Method | Description |
|---|---|
| `static void* Reserve(long sizeBytes)` | Reserve address space; zero RAM cost. |
| `static void Commit(void* ptr, long sizeBytes)` | Back with RAM. |
| `static void Decommit(void* ptr, long sizeBytes)` | Release RAM, keep address space. |
| `static void Free(void* ptr, long originalSize)` | Release entire reservation. |
| `static bool Is64KBAligned(void* ptr)` | Validates alignment. |

#### `Phase` / `PhaseConfig` (classes)

| Member | Description |
|---|---|
| `Phase.Name` | String name (e.g. `"Simulation"`). |
| `Phase.Id` | Integer ID for fast hot-path comparison. |
| `Phase.Initialization`, etc. | Well-known static instances. |
| `PhaseConfig.Default` | Strict linear transition: Init -> NetRcv -> Sim -> NetSend -> Presentation -> (loop). |
| `PhaseConfig.ValidTransitions` | String-keyed transition map. |
| `PhaseConfig.Permissions` | Per-phase `PhasePermission`. |
| `PhaseConfig.BuildCache()` | Converts string maps to integer caches. |

#### `TimeSystem` (class)

| Method | Description |
|---|---|
| `TimeSystem(EntityRepository, TimeProvider?)` | Constructor. |
| `void Reset()` | Restarts clock, zeros accumulators. |
| `void Update(double budgetMs)` | Variable-step; reads wall clock, caps delta, pushes `GlobalTime`. |
| `void Step(float fixedDeltaTime)` | Deterministic fixed-step; pushes `GlobalTime`. |
| `float TimeScale` | Speed multiplier (0 = paused). |
| `float MaxDeltaTime` | Delta cap (default 0.1 s). |

#### Core Component Types

| Type | ComponentId | Key Fields |
|---|---|---|
| `SimTransform` | 0 | `Vector3 Position`, `Quaternion Rotation` |
| `SimVelocity` | 1 | `Vector3 Linear`, `Vector3 Angular` |
| `GlobalTime` | 3 | `TotalTime`, `DeltaTime`, `TimeScale`, `FrameNumber`, `TotalWallTicks` |
| `HierarchyNode` | 6 | `Entity Parent`, `FirstChild`, `PreviousSibling`, `NextSibling` |
| `PartDescriptor` | 7 | Wraps `BitMask256`, tracks which 64-byte parts of a component exist |
| `EntityInfo` | — | `FixedString64 Name`, `ForceId` |
| `EpisodeTag` | 84 | `Guid EpisodeId` |
| `DISEntityType` | — | `ulong Value` + byte fields for Kind/Domain/Country/Category/Subcategory/Specific/Extra |

#### `ComponentIdAttribute` (class)

```csharp
[ComponentId(byte id)]
```

Required on every component struct/class/interface. `Id` must be unique across the solution
and registered in `GlobalComponentIds`. Collision detected at registration time.

#### `DataPolicyAttribute` (class)

```csharp
[DataPolicy(DataPolicy.NoSave)]
```

Controls how the engine pipeline handles a component type: `NoSnapshot`, `SnapshotViaClone`,
`NoRecord`, `NoSave`, or `Transient` (all three exclusions).

#### `EventIdAttribute` (class)

```csharp
[EventId(int id)]
```

Required on all event types used with `FdpEventBus`. `EventTypeRegistry` validates
uniqueness at registration time.

#### `FixedString32` / `FixedString64` (structs)

Zero-allocation UTF-8 strings for use inside ECS components.

| Member | Description |
|---|---|
| `MaxLength` | 31 / 63 bytes. |
| `FixedString32(string)` | Constructs from managed string; truncates if needed. |
| `string ToString()` | Converts back to managed string. |
| `int Length` | Byte length (not character count). |
| `bool IsEmpty` | True if zero-length. |
| `void Clear()` | Zeros all bytes. |
| `bool Equals(FixedString32)` | Byte-by-byte comparison. |

#### `EntityLifecycle` (enum)

| Value | Description |
|---|---|
| `Constructing = 0` | Allocated, not yet acknowledged by all modules. |
| `Active = 1` | Fully initialized, in simulation. |
| `TearDown = 2` | Scheduled for destruction, cleanup in progress. |
| `Ghost = 4` | Created from network state, awaiting `EntityMaster`. |
| `All = 255` | Query wildcard. |

#### `AsyncRecorder` (class)

| Member | Description |
|---|---|
| `AsyncRecorder(string filePath, RecordingMetadata?)` | Creates recorder; opens file stream. |
| `void CaptureFrame(EntityRepository, uint prevTick, FdpEventBus?, bool)` | Non-blocking frame capture. |
| `int MinRecordableId` | Entities with index below this are skipped. |
| `Predicate<Entity>? EntityFilter` | Optional fine-grained entity filter. |
| `long MaxNetworkId` | Highest network entity ID (written to metadata on dispose). |
| `int RecordedFrames` | Successful frame count. |
| `int DroppedFrames` | Frames dropped due to buffer full. |
| `Exception? LastError` | Background thread error for tests. |

#### `IRecordReplayController` (interface)

Application-level async contract for record/replay lifecycle. All methods are `async Task`.
See `Orchestration/IRecordReplayController.cs` for full signatures.

Key methods: `PrepareRecordingAsync`, `FinalizeRecordingAsync`, `PrepareReplayAsync`,
`SeekToTimeAsync`, `ProcessPlaybackTick`, `TeardownReplayAsync`.

#### `ISimulationView` (interface, in `Fdp.ModuleHost.Abstractions`)

Read-only contract over `EntityRepository` used by background modules.
`EntityRepository` implements this via `EntityRepository.View.cs`.

| Method | Description |
|---|---|
| `uint Tick` | Current global version. |
| `float Time` | Simulation time seconds. |
| `ref readonly T GetComponentRO<T>(Entity)` | Read-only unmanaged component. |
| `T GetManagedComponentRO<T>(Entity)` | Read-only managed component. |
| `bool IsAlive(Entity)` | Entity validity check. |
| `bool HasComponent<T>(Entity)` | Presence check. |
| `ReadOnlySpan<T> ReadEvents<T>()` | Event buffer read. |
| `QueryBuilder Query()` | Query builder. |
| `IEntityCommandBuffer GetCommandBuffer()` | Per-thread command buffer. |

#### `FdpLog<T>` (static class)

| Member | Description |
|---|---|
| `bool IsTraceEnabled` etc. | Guard flags for string interpolation. |
| `void Trace(string)`, `Debug`, `Info`, `Warn`, `Error`, `Fatal` | Log at level. |
| All methods | `[AggressiveInlining]`, no allocation on disabled levels. |

---

## Internal Dependencies

### NuGet Packages

| Package | Version | Use |
|---|---|---|
| `K4os.Compression.LZ4` | 1.3.8 | Frame compression in `AsyncRecorder` |
| `NLog` | 5.2.8 | Logging backend for `FdpLog<T>` |

### Project References

`Fdp.Core` has **no `<ProjectReference>` elements**. It is a leaf node in the project
dependency graph.

### InternalsVisibleTo

`Fdp.Core.Tests`, `Fdp.Tests`, `Fdp.ModuleHost`, `Fdp.ModuleHost.Tests` can access
`internal` members. This is intentional to allow the module host to call low-level
methods (`GetEntityIndex()`, `GetDestructionLog()`, etc.) without exposing them publicly.

---

## Usage Examples

### Example 1 — Basic Initialization and Entity Creation

```csharp
using Fdp.Core;

// 1. Create the ECS world
var world = new EntityRepository();

// 2. Configure phase transitions (optional; defaults to strict linear)
world.PhaseConfig = PhaseConfig.Default;

// 3. Create a TimeSystem (pushes GlobalTime singleton each frame)
var clock = new TimeSystem(world);

// 4. Pre-register events the simulation will publish
world.Bus.Register<EntityLifecycleEvent>();

// 5. Advance to the Initialization phase
world.SetPhase(Phase.Initialization);

// 6. Create entities and add components
var tank = world.CreateEntity();
world.AddComponent(tank, new SimTransform
{
    Position = new System.Numerics.Vector3(100f, 200f, 0f),
    Rotation = System.Numerics.Quaternion.Identity
});
world.AddComponent(tank, new SimVelocity
{
    Linear = new System.Numerics.Vector3(5f, 0f, 0f)
});
world.AddComponent(tank, new EntityInfo
{
    Name = new FixedString64("Alpha-1"),
    ForceId = ForceId.Friend
});

Console.WriteLine($"Created {world.EntityCount} entity: {tank}");
// Output: Created 1 entity: Entity(0, v1)
```

### Example 2 — Frame Loop with Query and Component Update

```csharp
// Build queries once (cache them - they are immutable and reusable)
var movingQuery = world.Query()
    .With<SimTransform>()
    .With<SimVelocity>()
    .Build();

// Game loop
while (running)
{
    // Advance the frame clock
    world.SetPhase(Phase.NetworkReceive);
    clock.Update();

    world.SetPhase(Phase.Simulation);
    world.Tick(); // Increment global version

    // Iterate matching entities with zero heap allocation
    foreach (var entity in movingQuery)
    {
        ref var transform = ref world.GetRefRW<SimTransform>(entity);
        ref readonly var velocity = ref world.GetRefRO<SimVelocity>(entity);

        // Read singleton time
        ref readonly var gt = ref world.GetSingletonRO<GlobalTime>();

        // Physics integration
        transform.Position += velocity.Linear * gt.DeltaTime;
    }

    // Swap event buffers so this frame's events become readable next frame
    world.Bus.SwapBuffers();
}
```

### Example 3 — Command Buffer for Thread-Safe Deferred Changes

```csharp
// In a background system or parallel worker:
var cmdBuf = new EntityCommandBuffer();

// Queue changes without touching the world directly
cmdBuf.DestroyEntity(deadEnemy);

var newProjectile = cmdBuf.CreateEntity(); // Returns placeholder Entity
cmdBuf.AddComponent(newProjectile, new SimTransform { Position = firingPos });
cmdBuf.AddComponent(newProjectile, new SimVelocity  { Linear  = direction * speed });

// Back on the main thread, after all parallel work completes:
world.SetPhase(Phase.Simulation); // Ensure we're in a write phase
cmdBuf.Playback(world);
cmdBuf.Clear(); // Ready for reuse
```

### Example 4 — Delta Query for Network Replication

```csharp
uint lastSentVersion = 0;
var replicatedQuery = world.Query()
    .With<SimTransform>()
    .With<SimVelocity>()
    .Build();

// Called every frame in NetworkSend phase
void SendNetworkUpdates()
{
    world.SetPhase(Phase.NetworkSend);

    // Only yields entities whose component chunks changed since lastSentVersion
    foreach (var entity in world.QueryDelta(replicatedQuery, lastSentVersion))
    {
        ref readonly var transform = ref world.GetRefRO<SimTransform>(entity);
        ref readonly var velocity  = ref world.GetRefRO<SimVelocity>(entity);
        network.Send(entity, transform, velocity);
    }

    lastSentVersion = world.GlobalVersion;
}
```

### Example 5 — Recording and Playback

```csharp
// RECORDING
using var recorder = new AsyncRecorder("exercise_001.fdp");

for (int frame = 0; frame < 3600; frame++)
{
    clock.Update();
    RunSimulationFrame(world);

    // Non-blocking capture: serializes delta into front buffer,
    // background thread compresses and writes to disk
    recorder.CaptureFrame(world, prevVersion, world.Bus);
    prevVersion = world.GlobalVersion;

    world.Bus.SwapBuffers();
}
// Dispose flushes remaining buffers and writes .meta.json manifest

// PLAYBACK
var playback = new PlaybackSystem();
using var reader = new BinaryReader(File.OpenRead("exercise_001.fdp"));

// Validate schema before starting
var validator = new SchemaValidator();
validator.ValidateOrThrow(reader); // Throws if FORMAT_VERSION or layout changed

while (reader.BaseStream.Position < reader.BaseStream.Length)
{
    playback.ApplyFrame(world, reader, world.Bus);
    RenderFrame(world);
}
```

---

## Best Practices

### Thread Safety

- **Entity create/destroy** is thread-safe (locked). Prefer doing it on the main thread
  anyway to avoid surprises.
- **Component reads and writes** are **not** thread-safe. Never write to a component while
  another thread reads it. Use `EntityCommandBuffer` to defer writes from worker threads
  and replay on the main thread.
- **Event publishing** (`NativeEventStream.Write`) is thread-safe via `Interlocked`.
- **`FdpEventBus.SwapBuffers()`** must be called on the main thread when no other thread
  is writing events.
- Each background thread should have its own `EntityCommandBuffer` instance (do not share).

### Performance Tips

- **Cache queries.** `QueryBuilder.Build()` allocates once. Store the `EntityQuery` as a
  field and reuse it each frame — it is immutable and safe to share.
- **Use `GetRefRO<T>()` for reads.** The `GetRefRW<T>()` overload bumps the chunk version,
  generating false positives in delta queries. Only call `GetRefRW` when you actually
  intend to write.
- **Guard log calls.** Use `FdpLog<T>.IsDebugEnabled` before constructing interpolated
  strings on hot paths.
- **Use `FixedString32/64` in components** instead of `string`. Managed strings in
  components force the managed component tier and add GC pressure.
- **Batch structural changes.** Creating and destroying many entities one at a time
  acquires/releases the create lock repeatedly. Batch via `EntityCommandBuffer.Playback()`
  in one pass.
- **Set `MinRecordableId`** on `AsyncRecorder` to skip system entities (IDs below
  `FdpConfig.SYSTEM_ID_RANGE`) and reduce recording file size.
- **`FdpConfig.EnforceExplicitComponentIds = true`** should be set in all production entry
  points before constructing any `EntityRepository`. This prevents silent ID collisions
  when multiple binaries share a process.
- **Avoid the `[Obsolete] ForEach(Action<Entity>)`** on `EntityQuery`. It allocates a
  closure on every call. Use `foreach` with the zero-allocation enumerator.

### Common Mistakes to Avoid

1. **Holding a `ref T` across a structural change** (add/remove component, destroy entity).
   Component tables can reallocate. Obtain refs as late as possible and discard immediately
   after use.

2. **Comparing `Phase` objects with `string.Equals`**. Use `==` which calls `Id == Id`
   (integer comparison). String comparison on every frame would be 10–100x slower.

3. **Publishing events after `SwapBuffers()`**. Events published after the swap go to the
   new write buffer and will not be visible until the frame after next.

4. **Registering a component type after entities exist**. Call `ComponentType<T>.ID` (which
   triggers registration) before creating any entities that use it.

5. **Missing `[ComponentId]` attribute**. With `FdpConfig.EnforceExplicitComponentIds = true`,
   this throws at startup. Add the attribute and register the ID in `GlobalComponentIds.cs`.

6. **Forgetting `[EventId]`**. `EventType<T>.Id` throws `InvalidOperationException` if the
   attribute is missing. Every event struct must declare a unique ID.

7. **Not calling `Tick()` or `SwapBuffers()`**. Without `Tick()`, `GlobalVersion` never
   advances and delta queries return nothing. Without `SwapBuffers()`, events are never
   promoted from write buffer to read buffer.

8. **Recycling entity IDs without generation check**. Always compare `entity.Generation`
   against the stored generation in `EntityHeader`. Use `IsAlive(entity)` for safety.

### Recommended Patterns

- Define all component IDs in `GlobalComponentIds.cs` before writing any component struct.
  This prevents accidental collisions across teams.
- Use `Phase.Initialization` → `Phase.NetworkReceive` → `Phase.Simulation` →
  `Phase.NetworkSend` → `Phase.Presentation` as the canonical frame order.
- Test systems with `world.ResetGlobalVersion(1)` in test setup to get deterministic
  version numbers regardless of initialization-time component registrations.
- Use `world.RegisterLifecycleStream(lifecycleStream)` to observe entity creation and
  destruction events in a single place rather than polling.

---

## Related Projects

### Direct Consumers (depend on `Fdp.Core`)

| Project | Relationship |
|---|---|
| `Fdp.ModuleHost` | Module orchestration kernel; has `InternalsVisibleTo` access |
| `Fdp.ModuleHost.Tests` | Integration tests; `InternalsVisibleTo` access |
| `Fdp.Presentation` | UI rendering layer over ECS state |
| `FDP.Toolkit.DER` | Domain-specific behavior toolkit |
| `Fdp.Toolkits` | Shared toolkit utilities |
| `Fdp.Network.Cyclone` | DDS/network integration |
| `Fdp.Engine` (Toolkit) | Higher-level simulation engine |
| `Hrot.Engine` | Second simulation engine in the solution |
| `Hrot.Subsystems.*` | All subsystems of Hrot engine |
| All `Fdp.Examples.*` | Example projects |

### Test Projects

| Project | Description |
|---|---|
| `Fdp.Core.Tests` | Unit tests for `Fdp.Core` (direct `InternalsVisibleTo` access) |
| `Fdp.ModuleHost.Tests` | Integration tests exercising entity/component/event lifecycle |

### Cross-Cutting Concerns

- **Global component ID catalog** (`GlobalComponentIds.cs`) is the single authoritative
  source of truth for all component type IDs across the entire solution. Any new component
  in any project must register here.
- **`FdpConfig.FORMAT_VERSION`** must be bumped whenever any binary serialization format
  changes (component layouts, event schemas, frame header structure). All projects that
  read `.fdp` files must be recompiled against the same version.
- **`ISimulationView`** is the stable boundary between `Fdp.Core`'s internal implementation
  and higher-layer modules. Background modules should never receive a direct
  `EntityRepository` reference.
