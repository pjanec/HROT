# HROT / FDP Engine — Programmer's Guide: Rules, Limits & Don'ts

**Audience:** engineers writing or modifying code in the FDP framework or the HROT
application layer.
**Scope:** this is a *cross-cutting constraints* guide — the architectural invariants
you must not violate, the engine's hard limits, and the traps that bite people. It does
**not** re-explain how the subsystems work.

> For the **how** (mechanisms, data flow, rationale), read these first; this guide layers
> on top of them and links back to them:
> - [docs/HROT architecture.md](HROT%20architecture.md) — the deep narrative (ECS, event bus, Brain-Muscle, replication, perception/EQS, channels, time sync, recorder, TKB, 2PC).
> - [docs/00-SOLUTION-OVERVIEW.md](00-SOLUTION-OVERVIEW.md) — project index, node topology, the 10 key architecture decisions.
> - [.dev/.guides/CODE-STANDARDS.md](../.dev/.guides/CODE-STANDARDS.md) — the standing review rules (magic numbers, SimMath, RW/RO, zero-alloc, component design).
> - [docs/AI_DEV_GUIDE.md](AI_DEV_GUIDE.md) — AI behavior authoring.

### How to read this

Every rule carries a severity and (where it exists in code) a `file:line` citation you can
click through to verify. Citations are repo-relative.

| Icon | Severity | Meaning |
|---|---|---|
| 🔴 | **hard-invariant** | Violating it crashes, corrupts memory, desyncs the cluster, or silently drops data. Non-negotiable. |
| 🟡 | **strong-guideline** | Violating it produces wrong behavior or breaks a contract; allowed only with a documented reason. |
| 🔵 | **advisory** | A sharp edge / performance note to be aware of. |

A recurring word in this guide is **"silently"**. The engine is built for throughput, so
most capacity limits and authority checks **drop data without throwing**. *No exception is
not the same as no problem* — assume a cap was hit and check the counter.

---

## Part 0 — The global invariants (apply everywhere)

These hold across every subsystem. The rest of the guide assumes them.

1. 🔴 **Zero allocation on the hot path.** No `new`, no LINQ (`.Where()/.Select()/.Any()`)
   in `OnUpdate`/`Tick`/draw loops. Pre-allocate in `OnCreate`; use `stackalloc` for small
   scratch spans. `foreach` over a query is fine. `.dev/.guides/CODE-STANDARDS.md:80-85`

2. 🔴 **All ECS components are unmanaged value types** (`struct`, no reference fields),
   `[StructLayout(LayoutKind.Sequential)]`, with `fixed`/inline buffers sized by **named
   constants** (never magic numbers). `.dev/.guides/CODE-STANDARDS.md:91-94`

3. 🔴 **Every component type needs `[ComponentId(GlobalComponentIds.X)]`.** Auto-increment
   IDs were removed; registration throws if the attribute is missing, the ID collides, or
   the ID is not also a named constant in `GlobalComponentIds.cs`. `FDP/Engine/Fdp.Core/ComponentType.cs:140-147`

4. 🔴 **Never read the OS clock in simulation code.** No `DateTime.UtcNow` / `Stopwatch` in
   any system or the recorder. Read `GlobalTime.TotalWallTicks` — the single per-frame
   source of truth — so every system in a frame sees the same timestamp. `docs/HROT architecture.md:48,322`

5. 🔴 **Use `SimMath`, never raw `System.Numerics` rotation APIs.** The world is
   right-handed, **X=East, Y=North, Z=Up**, yaw about Z (0 = East).
   `System.Numerics.Quaternion.CreateFromYawPitchRoll` (yaw about Y) is **banned in
   production**. Use `SimMath.FromYaw` / `FromYawPitchRoll` / `Facing*`. `.dev/.guides/CODE-STANDARDS.md:46-56`

6. 🔴 **Single-writer authority.** Each component has exactly one authoritative node
   (`EntityHeader` authority mask). `GetComponentRW` validates write access; egress checks
   `HasAuthority` before publishing; ingress must drop loopback. AI never writes physical
   components directly — it writes **Channels** (see Part D). `docs/HROT architecture.md:432,258`

7. 🔴 **Background ≠ main thread.** Background/async modules run on a **read-only snapshot**
   (`ISimulationView`). They never touch the live `EntityRepository`; structural changes go
   through an `IEntityCommandBuffer`, played back on the main thread. `GetComponentRW` is
   intentionally absent from `ISimulationView`. `.dev/.guides/CODE-STANDARDS.md:74-76`

---

## Part 1 — Cross-cutting traps (the ones that catch everyone)

These patterns recur in many subsystems. Learn them once.

### 1.1 🔴 The C# 12 `[InlineArray]` defensive-copy trap
Indexing an `[InlineArray]` field through a `GetComponentRW` ref (or any struct value)
makes the JIT emit a defensive `ldobj` **copy** — your write hits a temporary and is
**silently discarded**. Always write through a `Span<T>` obtained from a `GetSpanRW()`
helper (`MemoryMarshal.CreateSpan`), or use the Get-mutate-`SetComponent` pattern.
Confirmed hot spots: `MissionPlanQueue.Phases` `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/MissionComponents.cs:78-110`,
`EqsCognitiveBuffer` `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs:41`,
`DangerAreaCognitiveBuffer` `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs:11`.

### 1.2 🔴 "Requires a live `EntityRepository`, not a snapshot"
Many mutating systems cast `ISimulationView` to `EntityRepository` and **throw** if it's a
snapshot. They cannot run in a background/snapshot context. Includes all combat systems
(`BallisticsSystem`, `FireProcessingSystem`, `DamageCalculationSystem`,
`HealthApplicationSystem`) `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/FireProcessingSystem.cs:46-49`,
vehicle/nav systems (`CarKinematicsSystem`, `NavigationIntentBridgeSystem`, …)
`FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/CarKinematicsSystem.cs:48-51`,
`MissionControlExecutionSystem` `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs:87-90`,
and `BlueprintEventIngressSystem` `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintEventIngressSystem.cs:36-39`.

### 1.3 🔴 Entity handles: index 0 is valid; generation guards staleness
`default(Entity)` (`{0,0}`) is permanently invalid (slot 0 bumps to generation 1 on first
use) `FDP/Engine/Fdp.Core/EntityIndex.cs:87-93`. But a live entity **can** have `Index == 0`,
so **never use `Index == 0` / `AnchorId == 0` as a "no entity" sentinel** — test the
generation instead. `FDP/Engine/Fdp.Core/.../DebugPrimitive.cs:32-37`,
`FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs:229`. And **never store a
generational handle across frames** in node/AI state — resolve by `NetworkId` each tick.
`Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs:211`

### 1.4 🟡 Capacities drop silently — there is no overflow exception
Almost every fixed buffer **truncates or evicts without error**. If a count "feels low,"
suspect a cap. Examples surface throughout Part D/E and the table below.

### 1.5 🔴 DDS loopback & re-announce guards (combined Brain+Muscle nodes)
On `--mode all`, a node receives its own published samples back. Ingress translators must
**guard against overwriting locally-owned state**: don't write `SimTransform` from `WorldPos`
if you own it `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/GeoSpatialIngressTranslator.cs:77-85`;
don't reset `NetworkAuthority.PrimaryOwnerId` on re-announced `EntityMaster`
`.../EntityMasterIngressTranslator.cs:142-146`; check DDS **instance state before `IsValid`**
(dispose samples have `IsValid==false`) `.../EntityMasterIngressTranslator.cs:73-78`.

### 1.6 🔴 One-frame latencies are structural, not bugs
The double-buffered event bus delivers events in frame **N+1** `FDP/Engine/Fdp.Core/FdpEventBus.cs:13`.
Behavior transitions, nav status, lifecycle promotion, and pathfinding results all carry a
documented ≥1-tick latency. Tests that expect same-frame effects must manually tick the
downstream system. See Parts D and C.

### 1.7 🔴 Stale Roslyn source-generator cache ("my fix didn't take")
Incremental generators cache `.g.cs` keyed on inputs; changing **generator logic** without
touching a `.bp.json`/source file replays stale output. Clean-rebuild
(`dotnet build --no-incremental`, clear `obj/`, or restart VS) to force regeneration.
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/BlueprintIncrementalGenerator.cs:16-57`

### 1.8 🟡 Layering bans (enforced, some by reflection tests)
- ECS systems / UI panels must not import CycloneDDS or JSON types — that's the translator
  (anti-corruption) layer's job. `docs/00-SOLUTION-OVERVIEW.md:424`
- `Fdp.*` must not reference or implement `IClusterOpHandler` (Hrot-only). `Hrot/Network/Hrot.Network.Orchestration/IClusterOpHandler.cs:13-17`
- `Fdp.Toolkit.Orchestration` enum files must not reference `Hrot.NED`. `FDP/Toolkits/Fdp.Toolkits/Orchestration/Enums/ClusterOpType.cs:6`
- `Fdp.Toolkits` must not reference `Hrot.Stride.Core` (dependency is one-way down). `FDP/Toolkits/Fdp.Toolkits/Physics/IRaycastBackend.cs:18-22`
- `GizmoMap.Contracts` / `.Network` / `.Presentation` must not reference `Fdp.*`/`Hrot.*` (verified by build tests). `FDP/ExtDeps/GizmoMap/GizmoMap.Network.Tests/GizmoNetworkTests.cs:21-23`
- The combat `HitEvent` migration: **no game-domain types in the `Fdp.Core` kernel.** `FDP/Engine/Fdp.Core/Events/HitEvent.cs:1-11`

---

## Part 2 — Hard numeric limits (quick reference)

Every value below is a fixed cap. Exceeding it truncates/drops/evicts **silently** unless
noted. All are named constants in code (cite shown).

| Limit | Value | Over-limit behavior | Source |
|---|---|---|---|
| Registered component types (`BitMask512`) | **511** max ID | out-of-range, guarded only in `FDP_PARANOID_MODE` | `FDP/Engine/Fdp.Core/ComponentIdAttribute.cs:20` |
| ECB unmanaged component payload | **1024 B** | throws `ArgumentException` at record | `FDP/Engine/Fdp.Core/EntityCommandBuffer.cs:35` |
| `BehaviorParameters` DTO | **100 B** | compile error FDP_001 / startup throw | `FDP/Toolkits/Fdp.Toolkits.Analyzers/BehaviorParameterSizeAnalyzer.cs:26` |
| Channel `Params` / `State` buffers | **32 B each** (≤96 B struct) | corrupts adjacent state | `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorConstants.cs:10-16` |
| Action types per dispatcher channel | **64** (0 = none) | — | `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorConstants.cs:31` |
| Mission plan phases | **8** | excess tasks dropped + Warn | `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/MissionComponents.cs:143` |
| Tracked targets (`TargetMemory`/`SensorContactList`) | **16** | lowest-score evicted / dropped | `FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs:11` |
| Perception broadphase candidates / observer / tick | **256** | dropped from LOS this tick | `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/VisionBroadphaseSystem.cs:46` |
| Perception grid footprint | **1000 m × 1000 m, ≤50 000 entities** | not perceived | `FDP/Toolkits/Fdp.Toolkits/Perception/PerceptionConstants.cs:44,57` |
| Sensor track-lost debounce | **20 perception ticks (~2 s @10 Hz)** | not configurable | `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/SensorTrackDebounceSystem.cs:40` |
| EQS Top-K per result | **16** | — | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs:17` |
| EQS in-flight result pool | **1024** | ring overwrite before egress | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs:18` |
| EQS accurate-LOS raycasts / solver tick | **2048** (global, runtime-tunable) | deferred to later tick | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsEvalState.cs:53` |
| Navmesh path waypoints / request | **128** | truncated | `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs:44` |
| Raycast batch / frame | **4096** | dropped | `FDP/Toolkits/Fdp.Toolkits/Physics/PhysicsConstants.cs:14-24` |
| Raycast broadphase candidates / ray | **64** | missed hits in dense areas | `FDP/Toolkits/Fdp.Toolkits/Physics/PhysicsConstants.cs:14-24` |
| Bullet lifetime | **120 ticks (~2 s)**, no per-weapon override | culled | `FDP/Toolkits/Fdp.Toolkits/Combat/CombatConstants.cs:58-60` |
| Weapon mounts enumerated / entity | **16** | truncated | `FDP/Toolkits/Fdp.Toolkits/Combat/WeaponMountQuery.cs:37` |
| `UnitRoster` subordinates / commander | **16** | assignment rejected + event | `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs:32` |
| Squad contact pool / role-slot members | **16** | lowest-threat evicted / OOB if exceeded | `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs:128-134` |
| Blueprint AiPrimitive Params / WorkingState | **100 B / 1016 B** | compile error BP1200/BP1201 | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs:348-357` |
| Blueprint Instance variable tiers | **928 / 3936 / 16096 B** | compile error BP1210 | `.../Stage2_Validate.cs:361-382` |
| Tuning piecewise curve control points | **64** | truncated + warn | `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs:19` |
| `DebugPrimitive` struct | **64 B** (one cache line) | overflow / payload aliasing | `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs:16` |
| Debug-draw buffer / persistent | **4096 / 256 slots** | `DroppedCount++`, discarded | `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs:13-15` |
| Sub-tick debug ring | **256 node entries** | oldest dropped | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs:49` |
| `BinaryInterpreter` flusher subsystems | **32** (uint32 mask) | throws | `FDP/Toolkits/Fdp.Toolkits/Replication/Patching/BinaryInterpreterBuilder.cs:97-98` |
| Recorder double buffer / frame | **32 MB** | overflow corrupts | `FDP/Engine/Fdp.Core/FlightRecorder/AsyncRecorder.cs:19-26` |
| Ghost timeout (no `EntityMaster`) | **3600 frames (~60 s)** | ghost destroyed | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostTimeoutSystem.cs:19` |
| DDS ID-allocator discovery wait | **3 s** (client) / **30 s** (node start) | throws | `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocator.cs:36-38` |
| Module target frequency | **0–60 Hz** (0 ⇒ 60) | throws | `FDP/Engine/Fdp.ModuleHost/Abstractions/ExecutionPolicy.cs:160` |
| Storage gateway parallel copies | **8** (SMB inbound ~20 cap) | — | `Hrot/Subsystems/Hrot.Orchestrator/StorageGatewayModule.cs:54` |
| `ImGuiPropertyTree` nesting depth | **8** | truncated | `FDP/Engine/Fdp.Presentation/ImGui/Utils/ImGuiPropertyTree.cs:34` |
| Mission-control entity-wait retries | **10 frames** then NAK `EntityNotFound` | request dropped | `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs:42-46` |

> ⚠️ **Doc-vs-code drift to be aware of:**
> - The component-type cap is **511 / `BitMask512`** in code (`ComponentIdAttribute.cs:20`),
>   while `CODE-STANDARDS.md:94` and the architecture narrative still say **256 / `BitMask256`**.
>   `BitMask256` exists and is the 32-byte query/header mask, but the registrable-ID ceiling
>   the runtime enforces is 511. Treat 511 as authoritative; fix the docs.
> - The behavior-parameter cap is **100 B** in code (FDP_001 + `BehaviorConstants.cs:27`),
>   while `AI_DEV_GUIDE.md:859` describes a **60-byte** parameter region in a 128-byte
>   blackboard. Treat **100 B** as authoritative.

---

## Part 3 — Foundation

### 3.1 ECS kernel (`Fdp.Core`)
- 🔴 **`ECB.Playback` is main-thread-only.** Recording is per-thread-buffer safe; playback
  must run on the main thread after parallel work. `FDP/Engine/Fdp.Core/EntityCommandBuffer.cs:9-10,291`
- 🔴 **Parallel query actions must not modify shared state** — collect changes into an ECB.
  `FDP/Engine/Fdp.Core/EntityQuery.cs:331-332`
- 🔴 **`bool` fields in components need `[MarshalAs(UnmanagedType.I1)]`.** Interop sizes
  `bool` as 4 bytes; without the attribute every field after it gets the wrong
  `Marshal.OffsetOf` and serialization/StructEdit corrupt. Registration throws. `FDP/Engine/Fdp.Core/ComponentType.cs:437-447`
- 🔴 **`BitMask256` must stay 32-byte aligned** in `EntityHeader` (AVX2). `FDP/Engine/Fdp.Core/BitMask256.cs:12-14`
- 🔴 **Never `ComponentTypeRegistry.Clear()` in production** — it's test-only; clearing
  mid-session invalidates all component tables. `FDP/Engine/Fdp.Core/ComponentType.cs:367-368`
- 🔴 **ECB managed components are stored by reference, not copied.** Never pass one instance
  to two `AddManagedComponent` calls, and never mutate the object between record and
  playback. `FDP/Engine/Fdp.Core/EntityCommandBuffer.cs:151-156`
- 🟡 **Phase permissions gate writes:** `NetworkReceive`=unowned-only, `Simulation`=owned-only,
  `NetworkSend`/`Presentation`=read-only. Wrong-phase writes throw. `FDP/Engine/Fdp.Core/Phase.cs:155-159`
- 🟡 **Mutable class components must declare a `[DataPolicy]`.** The default is an error;
  unspecified mutable classes become `Snapshotable` (reference-copied to background snapshots
  → data race). `FDP/Engine/Fdp.Core/DataPolicyAttribute.cs:12-17`
- 🟡 **SoD snapshots sync only a 7-singleton allowlist** (`SpatialGridData`, `EqsResultPool`,
  `IEqsTemplateRegistry`, `ICoverProvider`, `INavmeshProvider`, `RaycastBatchData`,
  `EqsSolverGlobalState`). A background solver reading any other singleton gets `null`. `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs:103-122`
- 🔵 **`DeltaQuery` is conservative** — it compares chunk-level versions (not per-entity), so
  unmodified entities in a changed chunk may be returned. `FDP/Engine/Fdp.Core/EntityRepository.DeltaQuery.cs:119-124`

### 3.2 Event bus (`FdpEventBus`)
- 🔴 **One-frame latency is inherent** (publish N → read N+1). Not configurable. `FDP/Engine/Fdp.Core/FdpEventBus.cs:13,30-33`
- 🔴 **`SwapBuffers` exactly once per frame** (PostSimulation). Skipping hides events; double
  calling discards the read buffer. `FDP/Engine/Fdp.Core/FdpEventBus.cs:246-282`
- 🔴 **Unmanaged event types need `[EventId(uniqueId)]`** (globally unique; registration
  throws otherwise). **Managed** event IDs are derived from `Type.FullName` hash — *renaming
  a managed event class breaks replay/serialization.* `FDP/Engine/Fdp.Core/EventType.cs:51-68`, `FdpEventBus.cs:448-453`
- 🟡 **Native (`Publish<T>`) for high-frequency; `PublishManaged` only for low-volume
  (<100/frame)** — managed streams lock + GC. `FDP/Engine/Fdp.Core/ManagedEventStream.cs:22-23`
- 🟡 **For replay, pre-register typed streams** via `PrepareForNativeEventReplay<T>()` and
  `ClearCurrentBuffers()` before injecting; the no-size `InjectIntoCurrent` overload silently
  drops events for unregistered types. `FDP/Engine/Fdp.Core/FdpEventBus.cs:51-60,363-384`
- 🔵 **`ManagedEventStream.Count` returns the *write* buffer count** (historical), not the
  readable count. `FDP/Engine/Fdp.Core/ManagedEventStream.cs:39-41`
- 🟡 **`EventAccumulator`** (for slow/replica modules) holds ≤`maxHistoryFrames` (60) and
  flushes by `lastSeenTick`; both capture and flush are main-thread-only. `FDP/Engine/Fdp.Core/EventAccumulator.cs:1-57`

### 3.3 Module host (`Fdp.ModuleHost`)
- 🔴 **Execution policy is constrained:** `Synchronous` ⇒ `Direct` strategy and vice-versa;
  no other combination is valid. Background modes need a snapshot strategy. `FDP/Engine/Fdp.ModuleHost/Abstractions/ExecutionPolicy.cs:148,154`
- 🔴 **Every system needs `[UpdateInPhase]`** (else registration throws). `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs:146`
- 🔴 **Global systems may not use `SystemPhase.Simulation`** (only Input / BeforeSync /
  PostSimulation / Export). `Simulation` is for module systems on background threads. `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:158-165`
- 🔴 **`RegisterModule`/`RegisterGlobalSystem` before `Initialize()`; `SetTimeController`
  before `Initialize()`; `Initialize()` before `Update()`.** Runtime additions use
  `InstallModuleAsync`. The legacy `Update(float)` overload is `[Obsolete]` — it **causes
  deterministic desync.** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:121,153,452`
- 🔴 **RCU hot-plug is serialized (one in-flight swap)** and the live topology is **immutable
  once published** — clone-and-swap, never mutate in place. `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:84`, `KernelExecutionTopology.cs:21-28`
- 🔴 **Module `Tick()` runs *after* all phases.** `[UpdateAfter]`/`[UpdateBefore]` only order
  systems **within the same phase** — cross-phase dependencies are silently ignored.
  Circular deps within a phase throw `CircularDependencyException` at init. `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs:167-188`
- 🟡 **Physics integrators in PostSimulation must be in a `TogglablePostSimulationGroup`** and
  disabled during replay, or they overwrite restored positions. `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs:10-22`
- 🟡 **Don't `SwapBuffers` a snapshot's bus after `FlushToReplica`** — flush writes into the
  read buffer; swapping then clears it. `FDP/Engine/Fdp.ModuleHost/Providers/SharedSnapshotProvider.cs:46`
- 🔵 **Timed-out async modules become zombies** — the kernel abandons the `Task` but it keeps
  running and holds its snapshot lease. The kernel does **not** dispose modules; callers own
  lifetime. `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:842,382`
- 🔵 Diagnostics-only APIs (`GetRegisteredModuleNames`) allocate — not for the hot path. `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:208`

---

## Part 4 — Networking & cluster

### 4.1 Network core (`Fdp.Network.Cyclone`, Replication)
- 🔴 **Don't use `SmartEgressUtil` for high-frequency physics (`GeoSpatial`/`GeoSpatialDR`)** —
  its dictionary/hashset lookups cost ~600k/s at scale. Use **shadow-state comparison** vs
  `NetworkTransform`. `FDP/Toolkits/Fdp.Toolkits/Replication/Utilities/SmartEgressUtil.cs:63`
- 🔴 **`TypeId` assignment is non-deterministic across sessions** (DDS arrival order) —
  cannot be persisted in saves or compared live-vs-replay; pre-register or hash. `FDP/Network/Fdp.Network.Cyclone/Services/TypeIdMapper.cs:10-16`
- 🔴 **DDS write-before-match:** subscribe to `PublicationMatched` *before* reading
  `CurrentCount`, and defer the first allocator request until the server is matched, or it's
  silently dropped. `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocator.cs:60-79`
- 🔴 **`GhostCreationSystem.CreateGhost` is Input-phase main-thread only.** Ghosts must reach
  **Active** before any system operates on them (partial hydration crashes otherwise). `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostCreationSystem.cs:35`
- 🔴 **`NetworkSpawningSystem`: ELM `BeginConstruction` is the *last* call** — entity +
  `NetworkEntityMap` registration must complete first. `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/NetworkSpawningSystem.cs:149-153`
- 🔴 **`BinaryInterpreter` handler delegates must not capture mutable state** (interpreter is
  shared/immutable); use `ReserveScratchpad`. The `indices` span passed to attribute setters
  is stack-allocated — **never capture it.** `FDP/Toolkits/Fdp.Toolkits/Replication/Patching/BinaryInterpreterBuilder.cs:56-58`, `IEntityPatchContext.cs:10-11`
- 🟡 **Use the `int[]` (component-ID) `RegisterMapping` overload**, not the legacy `Type[]`
  one — ordinals are not component IDs. `FDP/Toolkits/Fdp.Toolkits/Replication/Services/DescriptorOwnershipMap.cs:42-46`
- 🟡 **`CycloneNetworkCleanupSystem` is *not* auto-registered** by `CycloneNetworkModule` —
  register it yourself or entity-disposal cleanup silently won't run. `FDP/Network/Fdp.Network.Cyclone/Modules/CycloneNetworkModule.cs:100`
- 🟡 **During replay set `GhostCreationSystem.BypassLifecycle=true`** so live samples don't
  spawn ghosts that collide with recorded IDs; reset on return to live. `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostCreationSystem.cs:15-26`
- 🔵 `ManagedSerializationProvider` allocates on every `Encode`/`Apply` — a known hot spot for
  managed descriptors. `FDP/Network/Fdp.Network.Cyclone/Providers/ManagedSerializationProvider.cs:11`

### 4.2 HROT protocols (NED / BDC / Orchestration)
- 🔴 **Orchestrator (ID allocator) must be running before any node starts** (30 s wait, then
  throw). `Hrot/Network/Hrot.Network.Orchestration/DdsIdAllocatorHelper.cs:18,51-53`
- 🔴 **`IClusterOpHandler.PrepareAsync` must not mutate ECS** — only `Commit` (main thread)
  may. `Hrot/Network/Hrot.Network.Orchestration/IClusterOpHandler.cs:33-35`
- 🔴 **BDC is position+entity-master only.** Its factory returns null/no-op for command
  gateway, ExCon, mission, pathfinding, perception — those need NED. `Hrot/Network/Hrot.Network.BDC/Factory/BdcNetworkFactory.cs:50-79`
- 🔴 **BDC ordinals start at 1000** to avoid NED collisions (shared `DescriptorOwnershipMap`
  key space). `Hrot/Network/Hrot.Network.BDC/Replication/BdcEntityMasterTranslator.cs:33-34`
- 🔴 **`NedReplicationModule` requires `MuscleGround|ImageGenerator|Brain` in the role** (else
  throws). Pure-IG (`driveFromNetwork`) only when neither Muscle nor Brain. `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs:187-194`
- 🔴 **Gizmo egress & ingress translators must never co-exist on one node** (broadcast loop). `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoTranslatorPack.cs:12-13`
- 🟡 **`EntityMaster` carries no `OwnerId`** — ownership comes from DDS sender metadata (last
  writer wins, per NED SST). `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs:104-107`
- 🟡 **Keep hand-bridged enums in sync:** `TkbType`↔`DisType`, and `eTacticalDesignation`↔
  `Hrot.Core.CommandHierarchy.TacticalDesignation` (separate assemblies, mapper-bridged). `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs:88-96,120`
- 🟡 **Use `OrchestrationJsonOptions.Default`** for all DDS payload round-trips (string enums,
  rejects integer-as-enum). `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs:24-32`
- 🔴 **Listener nodes (IG/ExCon) don't record/replay** but **must still ACK** prepare/finalize
  or they stall the 2PC. `Hrot/Network/Hrot.Network.Orchestration/ListenerRecordReplayController.cs:11-22`

### 4.3 Cluster orchestration & deterministic time
- 🔴 **`ClusterSlave.Tick()` is main-thread-only**; all `Commit()` writes happen there. `FDP/Toolkits/Fdp.Toolkits/Orchestration/ClusterSlave.cs:113`
- 🔴 **Time-control (Pause/Resume/Step/SetTimeScale) bypasses 2PC** — it publishes bus intents
  directly. Don't route it through the transaction path. `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs:317`
- 🔴 **Construction order at boot matters:** `MasterSyncController` (publishes the
  `Continuous` baseline) → `SwapBuffers` → master time translators, so late slaves get the
  t=0 anchor; and `LiveBranchProcessManager.Tick()` must run **before** `ClusterMaster.Tick()`
  (freeze time before PrepareLive fan-out). `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs:143`, `LiveBranchProcessManager.cs:19`
- 🔴 **`SwitchTimeModeEvent` can't go on DDS directly** (carries a C# enum) — register
  `SwitchTimeModeWireDto` (int). `ExecuteNodeOpIntent` (managed payload) must use
  `PublishManaged`/`ConsumeManaged`. `FDP/Toolkits/Fdp.Toolkits/Time/Messages/TimeMessages.cs:77`
- 🔴 **State-machine dead-ends:** `Degraded` has **no outgoing edges** (restart required);
  `RunningEdit→LoadingLive` is unsupported (route via Unload→Idle); `ManageEpisode` requires
  `OperatingLive`. `Hrot/Subsystems/Hrot.Orchestrator/HrotStateGraph.cs:12,18`, `TransitionPlanner.cs:168`
- 🔴 **ExCon is excluded from `FrameAck` lockstep** (no kernel) — including it stalls the
  master forever. `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs:302`
- 🔴 **`SteppingTimeController`: call `Step()` *before* `kernel.Update()`** — otherwise the
  first tick sees `DeltaTime == 0`. `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SteppingTimeController.cs:13-17`
- 🔴 **Slaves anchor time to NTP-adjusted `SyncedWallTicks`, never raw OS ticks** (else a
  permanent boot-time offset). Offset hard-snaps if >1 s diverged, else steers at 10%; RTT
  >200 ms discarded. `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs:65,249`
- 🟡 **Master freezes sim time (`_totalTime`) during `BarrierPending`** — only wall ticks
  advance, or the master would show a different paused time than slaves. `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs:383`
- 🟡 **Drain stale `AdvanceFrameIntent` in Continuous/BarrierPending** or the managed queue
  grows unbounded. `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SlaveSyncController.cs:338`
- 🟡 **`ClusterMaster` rejects all `ClusterOpRequest`s until the bootstrap latch fires** (all
  mandatory nodes in Standby) — early commands silently fail unless you inspect the status
  topic. State tracking is **optimistic** and can diverge if a transaction aborts. `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs:96,122`
- 🟡 Storage gateway = **SMB pull**, ≤8 parallel copies (Windows ~20-connection inbound cap). `Hrot/Subsystems/Hrot.Orchestrator/StorageGatewayModule.cs:54`

---

## Part 5 — Simulation domains

### 5.1 Navigation & motion
- 🔴 **Cancel locomotion via `NavigationIntent` (bump `IntentId`, `Mode=None`), never by
  writing `NavState` directly** (`NavState` is Muscle-owned). `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FleeExecutor.cs:99`
- 🔴 **Ignore `NavigationStatus` when its `IntentId` ≠ current `NavigationIntent.IntentId`**
  (stale on loop-reset / new order / snapshot restore; ≥1-tick latency). `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs:296`
- 🔴 **All nav/raycast positions are FDP Cartesian metres** (Z-up); geo conversion is the
  translator's/adapter's job, never the executor's. Vehicle steering math is **2D (XY)** even
  though Z is carried. `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationComponents.cs:187-190`, `Physics/IRaycastBackend.cs:26-30`
- 🔴 **Pathfinding runs at 10 Hz on a background thread**, ring-buffered, oldest-evict; excess
  requests dropped; results carry ≥1-tick materialization latency. `FDP/Toolkits/Fdp.Toolkits/Navigation/Modules/NavigationSolverModule.cs:15-26`
- 🔴 **`IVolumetricPathProvider.IsFlyable/PathExists/QueryVersion` throw `NotSupportedException`
  by default** — capability-check before calling. `FDP/Toolkits/Fdp.Toolkits/Navigation/IVolumetricPathProvider.cs:31-50`
- 🔴 **`FrustrationTicks` is written only by `NavigationExecutionSystem`** — never externally. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/FrustrationTicks.cs:19-21`
- 🔴 **Muscle route handles use `NavigationHandleAllocator` (≥0x40000000)** to avoid Brain
  handle collisions; **`FleeParams` must store the full `Entity` handle** (index+generation)
  and check `IsAlive`. `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationHandleAllocator.cs:7-10`, `NavigationActions.cs:85-88`
- 🟡 **`CarKinematicsSystem` queries with `.WithOwned<SimTransform>()`** (split authority) and
  needs a live repo. `LinearKinematicsSystem` can't live in `GroundKinematicsModule` (circular
  ref) — register it via the facade. `FDP/Toolkits/Fdp.Toolkits/CarKinem/Modules/GroundKinematicsModule.cs:25-35`
- 🟡 **Infantry must not carry `VehicleState`** — its presence skips DtCrowd registration. Use
  `CollisionShapeKind.Capsule`. Don't cache `ActionInstanceId` if `RegisterAgent` fails
  (navmesh not baked) — retry next tick. `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs:233-242,52-54`

### 5.2 Combat, weapons & effectors
- 🔴 **Combat event IDs 5001–5099 are frozen** (wire/serialization contracts). `FDP/Toolkits/Fdp.Toolkits/Combat/HitEvent.cs:17`
- 🔴 **System order: `BallisticsSystem` → `LinearKinematicsSystem` → `RaycastSolverSystem`**
  (swept segment uses prev→cur position; reorder = missed/mislocated hits). Order is held by
  registration array position, not attributes. `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/BallisticsSystem.cs:17-39`
- 🔴 **Fire/damage only on the authority node.** `FireProcessingSystem`, `DamageSystem`,
  `HealthApplicationSystem` all skip non-owned entities (no duplicate bullets / double damage).
  Set `IsRemote=true` on network-ingressed detonations or the Muscle double-counts. `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/FireProcessingSystem.cs:69-72`
- 🔴 **Bullet is in `TearDown` (still `IsAlive`) for a one-frame window** when `DamageSystem`
  reads `BallisticProjectile`; a second same-tick hit is dropped. `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/DamageSystem.cs:60-64`
- 🟡 **`WeaponFireIntent` uses local `Entity` handles, not network IDs** (PACK-P003).
  `FireRequestEvent` (5002) is deprecated — use `WeaponFireIntent` (5003). `AimAndFireParams`
  must fit `BehaviorConstants.ActionParamsByteSize`. `FDP/Toolkits/Fdp.Toolkits/Combat/Events/WeaponFireEvents.cs:14-19`
- 🟡 **Primary weapon (mount 0) lives on the platform entity; mounts ≥1 are child entities.**
  `CombatTkbTranslator` **OR-s** the combat collision layer onto existing colliders — don't
  unconditionally overwrite `PhysicsCollider`. `FDP/Toolkits/Fdp.Toolkits/Combat/Translators/CombatTkbTranslator.cs:18-21,71-73`
- 🟡 **Don't bypass `AimAndFireExecutor`** — it gates on ammo and cooldown before publishing. `FDP/Toolkits/Fdp.Toolkits/Combat/Executors/AimAndFireExecutor.cs:53-66`
- 🔵 **Damage is flat HP loss (25) — armor/penetration is a deferred POC.** `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/DamageCalculationSystem.cs:63-67`
- 🔵 **Threat ranking, weapon/effector selection, posture, and group-fire are *not* in the
  Combat toolkit** — they live in the **Utility AI decision layer** (see §6.3). The Combat
  toolkit only resolves fire/ballistics/damage.

### 5.3 Perception, sensors & EQS
- 🔴 **Perception runs at 10 Hz on a background SoD snapshot** — write only via ECB, and read
  only the module-private **scoped bus** (`LosCheckRequestEvent`, `TargetVisibleEvent`); other
  event types return empty. Call `_scopedBus.SwapBuffers()` between pipeline stages. `FDP/Toolkits/Fdp.Toolkits/Perception/Modules/AutonomousPerceptionModule.cs:147-231`
- 🔴 **`FieldOfViewCos` stores cos(half-angle)** — not degrees, not full-FOV cosine. Forward
  axis is **`UnitX` (east)**; using `UnitY` produces a yaw-independent north-facing bug. `FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs:24`, `Systems/VisionBroadphaseSystem.cs:38`
- 🔴 **LOS occlusion is 2D (XY) only** — altitude ignored; supply `colliderRadiusReader` or all
  occluders are treated as zero-radius points (≈no occlusion). `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LosRequestBatchingSystem.cs:107,25`
- 🟡 **Spatial grid keys by full `Entity` handle** (index+generation) — keying by index alone
  leaves dead handles in the grid. `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/LocalGridBuilderSystem.cs:37`
- 🔴 **EQS generators/tests must be zero-allocation** (`stackalloc`); `CoverPoint` is kept
  unmanaged (28 B) for this. **EQS generator types available:** `EntitiesInRadiusGenerator`,
  `CoverPointsGenerator`, `NavmeshSamplesGenerator`. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsQueryTemplate.cs:26-39`
- 🟡 **Read an `EqsResult.Flags` bit only if its `FlagsMeaningful` bit is set** — unrun tests
  leave undefined bits. Area-query pool cursor is "last writer wins" per tick. `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs:34`

### 5.4 TKB, entity lifecycle, scenario & versioning
- 🔴 **TKB descriptor names must not contain `#`** (runtime instance delimiter, e.g.
  `Foo#2`). TKB JSON files **must have an `Int64 `$guid`**; keys not starting with a letter
  are skipped. `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/TkbDescriptorAttribute.cs:17-41`, `TkbDeserializer.cs:27-30`
- 🔴 **`TkbDescriptorRegistry` is filled once at startup via `[ModuleInitializer]`, read-only
  thereafter** (last-registration-wins, case-insensitive). `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDescriptorRegistry.cs:14-56`
- 🔴 **`ITkbEntityTranslator.Inject` must guard every add with `IsComponentTypeRegistered<T>()`**
  — TKB translation runs on nodes that don't register all component types. `FDP/Toolkits/Fdp.Toolkits/Behavior/Translators/BehaviorTkbTranslator.cs:30-100`
- 🔴 **`ZipTkbProvider` is read-only** (CI artifact) — authoring uses `RawDirectoryTkbProvider`.
  Consume/dispose `TkbEntityFile.JsonStream` before advancing the enumerator. `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/ZipTkbProvider.cs:11-12`
- 🔴 **`EntityLifecycleModule.SetTranslators` before `RegisterSystems`** or blueprints won't
  apply. Lifecycle promotion is **≥1 frame later even with zero participants.** Construction
  **failure** and **timeout** (default 300 frames) destroy the entity **without a
  `DestructionOrder`** — modules listening for it won't be notified. `FDP/Toolkits/Fdp.Toolkits/Lifecycle/EntityLifecycleModule.cs:77-83,234-241,316-355`
- 🔴 **Scenario serialization contract:** `IEntityScenarioTranslator.Extract` may return only
  `JsonNode|string|int|float|double|bool` (null rejected); declare all custom DOM keys in
  `GetOutputDomKeys` (unknown keys throw on load); convert `Entity` handles to/from stable
  GUIDs via `IGuidResolver`. `FdpAutoSerializer` **can't serialize `Entity`-typed
  fixed-buffer/`[InlineArray]` fields** — mark `[ScenarioIgnore]` and handle manually. Call
  `Build()` once before any extract/inject; build via `ScenarioSerializerBuilder`, not the
  internal ctor. `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs:148-163`, `IEntityScenarioTranslator.cs:44-80`, `FdpAutoSerializer.cs:98-118`
- 🟡 **Migration:** register all migration modules **before sealing** `MigrationRegistry`;
  down-migration is lossy (e.g. Scenario v2→v1 discards `Tags`). `Hrot/Engine/Hrot.Common/Scenario/Migrations/...`

---

## Part 6 — AI authoring & behavior

### 6.1 Behavior dispatch, BTrees, HSMs & channels
- 🔴 **AI writes Channels, never physical components.** `LocomotionChannel`/`WeaponChannel`/
  `InteractionChannel` `Params` and `State` are 32 B each (≤96 B struct); `ActionParams` ≤
  the named byte budget — overflow corrupts adjacent channel state. `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorConstants.cs:10-16`
- 🔴 **`BehaviorId`s are stable forever, never reused** (0 = none; civilian 1001–1999,
  military 2001–2999). **Never `string.GetHashCode()`** for an ID (process-randomized) — use
  `BehaviorIds` + `TryGetId`. `BehaviorRegistry` must be fully written before the first frame. `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorIds.cs:14-18`, `BehaviorRegistry.cs:105-112`
- 🔴 **Behavior transition has ≥1-frame latency:** `MissionDirectorSystem` (Simulation)
  publishes the trigger; `BehaviorIngressSystem` (Input, the **sole** `BehaviorState` writer)
  applies it next frame; arbitration preempts one tick later. `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/MissionDirectorSystem.cs:27-41`
- 🔴 **`CognitiveInterruptSystem` is the sole writer of `PreviousCapabilities`**; reactors read
  it and must run `[UpdateBefore]` it. **Interrupt bytes are edge-triggered and cleared
  end-of-frame** — consume them within the same tick. `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveInterruptSystem.cs:38-41`
- 🔴 **Only `BTreeTickSystem` publishes `BehaviorFinishedEvent`** (root-level); dispatchers
  (leaf-level) must not. **`IActionExecutor.OnEnter` must fully initialize state** so the
  same-frame `Execute` is safe. **Behavior-param parse is atomic** — a parse failure leaves
  the entity on its old behavior entirely. `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/BehaviorFinishedEvent.cs:16-20`, `Systems/BehaviorIngressSystem.cs:27-104`
- 🔴 **On reassignment, reset the HSM instance header** (`ResetHsmComponents` re-stamps
  `MachineId` with the new `StructureHash`) or `ValidateInstance` fails on the first tick. `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs:138-244`
- 🟡 **`MissionTrigger.ReachedDestination` is deprecated** (coupled Brain to Muscle nav) — use
  `BehaviorFinished`. `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/MissionComponents.cs:24-28`
- 🟡 **Hot-reload discovery (`LoadAndScan`) on a background thread must not touch staging
  buffers** — defer to `ApplyReload` on the main thread. `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs:439-452`

### 6.2 Blueprint visual scripting
- 🔴 **Compiler output must be deterministic** — no `Guid.NewGuid()`/`DateTime.Now`, sorted
  iteration; node IDs are synthesized via SHA-256. Non-determinism breaks hot-reload diffs and
  snapshot comparison. `docs/blueprints/Blueprint_Subsystem_Architecture_v1.2.md:1269`
- 🔴 **Stage-2 validators are hard caps** (compile errors): Library blueprints can't contain
  latent nodes or event graphs (BP1101/BP1013); AiPrimitive **Conditions** must be synchronous
  (no `Return(Running)`, no latent — BP1100/BP1101); function graphs called via
  `FunctionCallNode` can't be latent (BP1650) and **can't recurse** (cycle = compile error
  BP1654, would stack-overflow at runtime); each exec-out pin drives exactly **one** successor
  — fan-out needs a `SequenceNode` (BP1411); `WhenNode` only in Instance event graphs (BP2001). `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`
- 🔴 **`.bp.json` assets must be `<AdditionalFiles>`** in the csproj or the MSBuild generator
  ignores them (no `.g.cs`, and peer-call validation BP1301 fails). `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/BlueprintIncrementalGenerator.cs:22-24`
- 🔴 **`BlueprintRegistry` snapshot is published via `Interlocked.Exchange`** — never mutate
  the live snapshot; `RegisterDirect` is pre-first-commit only. The **quick-reload merge path
  can't clear a world-singleton marking** — toggling `IsWorldSingleton` off needs a full
  rebuild. `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs:134,147-149`
- 🔴 **Roslyn hot-reload ALC must be collectible and the caller owns `Unload()`** — else the
  old assembly leaks. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Roslyn/InMemoryRoslynCompiler.cs:115`
- 🟡 **Blueprint `SetVar` writes directly into the blackboard span** (bypassing `GetComponentRW`,
  so chunk versions don't advance) — which is why sub-tick debug snapshots are **full
  keyframes, not deltas**. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs:14-18`
- 🔵 Warnings worth heeding: `WhenNode` on a **BestEffort-QoS** event may miss firings (UDP);
  `WhenNode(EventFired)` + `FallingEdge` never fires. `.../Stage2_Validate.cs:829-838,813-818`

### 6.3 Roslyn generators & analyzers (compile-time invariants)
- 🔴 **FDP_001** errors if any `[SharedAiAction]`/`[SharedAiCondition]` DTO > **100 B** (would
  overrun `BrainBlackboard`). Keep that analyzer in the FDP Behavior domain — never in generic
  FastBTree/FastHSM. `FDP/Toolkits/Fdp.Toolkits.Analyzers/BehaviorParameterSizeAnalyzer.cs:26`
- 🔴 **Never add/remove ECS components inside HSM/BTree `SharedAi` thunks** — they write
  directly during chunk iteration; structural mutation corrupts the chunk arrays. `FDP/Toolkits/Fdp.Toolkits.Analyzers/HsmActionGenerator.cs:695`
- 🔴 **Generator-discovered methods must be `static` and public.** Private/protected
  `[SharedAiAction]` are silently skipped; non-static `[SharedAi*]`/`[UtilityInput]` trigger
  warnings and are dropped from the dispatch table. `[UtilityInput]` must be
  `static float (in UtilityInputCtx)`. `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedBhuDiagnostics.cs:19-23`, `BTreeActionGenerator.cs:83-88`
- 🔴 **`[UtilityInput]` names must be unique and not collide under 16-bit FNV-1a truncation**
  (UT0102/UT0103). The hash must stay byte-identical across the BTree/HSM/Utility generators. `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityInputGenerator.cs:243-256`
- 🟡 **Utility AI is where threat/posture/maneuver decisions live.** `[UtilityDecision].Build()`
  must be structurally pure (UT0130); `ManeuverSelect` decisions may bind only squad-leader
  self-inputs, not Candidate/Target (UT0151); avoid all-`WeightedProduct`+gated options with
  no `WeightedSum` fallback (UT0144 → possible no-winner). Runtime weight tweaking is via the
  Tuning registry (§7.2). `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs:98-176`
- 🔴 **`[GizmoProjector]` must implement `IStatelessGizmo`/`IGlobalStatelessGizmo`** or it's
  silently unregistered (FDP_002). **`[TkbDescriptor]` names must be unique per assembly**
  (case-insensitive, TKB001). `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs:16-30`

### 6.4 Mission plan, unit hierarchy & squad
- 🔴 **`MissionControlExecutionSystem`** needs a live repo and must contain **zero DDS / JSON /
  `EntityMission`-writer references** (PACK-P001 layering). Plans are truncated to 8 phases. `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs:31-90`
- 🔴 **`UnitHierarchySystem` order each tick:** destruction-cascade → removals → assignments.
  Roster cap is 16 (over-cap rejected + event). `Hrot/Engine/Hrot.Common/Systems/UnitHierarchySystem.cs:13-17,119-123`
- 🔴 **Genesis intent DTOs are `DataPolicy.Transient`** — materialized in genesis Phase 4,
  must never be persisted/relied-upon post-load. `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs:133-142`
- 🔴 **`DdsCommandClient.SendAsync` requires a non-empty `RequestId`** (else throws). `FDP/Toolkits/Fdp.Toolkits/Commands/DdsCommandClient.cs:66`
- 🟡 **Squad primitives:** `PhaseSequencer` — `VetoDetected` always preempts; dwell uses strict
  `>` (exit one tick late). `SlotRotation.BurnSlot` is **permanent** (survives release, no
  unburn). Context-action `ActionName` must be an integer string; menu JSON must not be parsed
  on the hot path; the context bus is **isolated** from the world bus. `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/PhaseSequencer.cs:77-85`, `SlotRotation.cs:47-56`, `Hrot/Engine/Hrot.Common/Systems/ContextActionIngressSystem.cs:13-44`
- 🟡 **Mission triggers:** an empty/null trigger list resolves to `TimerElapsed(float.MaxValue)`
  (holds forever); only the **first** trigger is examined; `ReachedDestination` is a no-op
  alias for `BehaviorFinished`. `Hrot/Engine/Hrot.Core/Mission/MissionTriggerHelper.cs:18-34`

---

## Part 7 — Recording, replay & diagnostics

### 7.1 Flight recorder, replay & checkpoints
- 🔴 **The recorder raw-copies unmanaged memory** — *any* struct-layout change between record
  and playback silently corrupts. `ComponentLayoutHasher` (deterministic FNV-1a; never use its
  `GetHashCode`) writes a schema manifest and `SchemaValidator` aborts playback on drift —
  **but legacy recordings without a `.meta.json` skip validation and corrupt silently.** `FDP/Engine/Fdp.Core/FlightRecorder/SchemaValidator.cs:48-55`, `ComponentLayoutHasher.cs:20-22`
- 🔴 **Pass `wallClockTicks` from `GlobalTime.TotalWallTicks`** into `CaptureFrame`/`Keyframe`
  — never sample a clock inside the async path. `WallClockTicks` must be **monotonic
  non-decreasing** (binary-search seek). `FDP/Engine/Fdp.Core/FlightRecorder/AsyncRecorder.cs:113-116`, `PlaybackController.cs:242-244`
- 🔴 **Replay is main-thread-only:** `RegisterSystems` before any seek; seeks run synchronously
  on the main thread (off-thread = corruption); `GetCurrentReplayTime` **before**
  `TeardownReplayAsync`. `FDP/Toolkits/Fdp.Toolkits/Replay/ReplayModule.cs:93-133`
- 🔴 **Don't publish `NodeOpStatus(Success)` until `RecordingModule.Dispose` completes** (LZ4
  flush + file-handle release), or replay-open races the writer (D9 crash). After
  `CheckpointIOWorker.Enqueue`, the snapshot is owned by the worker — don't mutate it. `FDP/Toolkits/Fdp.Toolkits/Replay/RecordingModule.cs:79-106`, `FDP/Engine/Fdp.Core/Orchestration/CheckpointIOWorker.cs:108`
- 🔴 **Managed recordable classes need a public parameterless ctor**, ≥1 public serializable
  member (else warmup throws), and **no circular references** (stack-overflow) and **no
  interface-typed fields** (shallow-copied only). `FDP/Engine/Fdp.Core/FlightRecorder/FdpAutoSerializer.cs:56-300`
- 🟡 **`.fdp` format is version-locked** — no migration; mismatched `FORMAT_VERSION` throws. `DataPolicy.NoRecord` events never appear in replay. Don't set `RecordingConfiguration.Blocking=true` in production (stalls the main thread). `FDP/Engine/Fdp.Core/FlightRecorder/PlaybackController.cs:92-98`
- 🔵 **Delta gap:** cold-chunk field changes via direct `GetMetadata()` ref-access don't stamp
  `LastChangeTick` and are dropped from delta frames (DEBT D004). After a dropped frame the
  recorder auto-forces the next keyframe — don't also request one. `FDP/Engine/Fdp.Core/FlightRecorder/RecorderSystem.cs:117-122`
- 🔵 **Note:** "dry-run" is not a distinct subsystem in code; AAR diagnostics are the
  ReplayBrowser search/export tooling (its incremental/DiffNode deserializers throw
  `NotSupportedException`). `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs:672`

### 7.2 Gizmos, debug-draw, data breakpoints & tuning
- 🔴 **`DebugPrimitive` is a 64-byte blittable tagged union** (DDS reinterpret-casts it). Don't
  exceed the payload; **don't call `StampGizmoTypeId` on `SemanticShape`/`SpatialAnchor`**
  (offset-60 aliases orientation data); emit `DrawSpatialAnchor` **before** the matching
  `DrawSemanticShape` in the same frame; test `AnchorGeneration`, not `AnchorIndex`, for
  validity. `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Primitives/DebugPrimitive.cs:16,148-150`
- 🔴 **Debug-draw buffers drop on overflow** (`DroppedCount`) — size them or check the counter.
  `DrawTextLong` allocates on first unique string then interns. `IDebugDrawBuilder` deliberately
  does **not** inherit `IGizmoDrawBuilder` (separate `FixedString32` CLR types bridged by
  `Unsafe.As`). `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs:13-15`
- 🔴 **Tuning:** call `TuningRegistry.BeginFrame` at frame-top before any system reads a
  tunable (Apply enqueues, BeginFrame commits; out-of-range silently clamped). Piecewise
  curves cap at 64 points. `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs:7,19`
- 🔴 **Data breakpoints:** `DataBreakpointSystem` runs in PostSimulation **after**
  `RecorderTickSystem` (record natural state before rewind); never call `OnHit` inside a
  `QueryDelta` iteration (collect then dispatch); re-entrant same-tick hits are dropped (first
  wins); **never persist or carry compiled predicate delegates across a hot-reload** (stale
  unmanaged pointers) — persist only the `SearchPredicateDto`. NetworkId-typed lifecycle
  breakpoints throw `NotSupportedException` (use EcsHandle/NameSubstring). `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointSystem.cs:22-23,83`, `DataBreakpointManager.cs:408,1006`
- 🔵 **MCP server:** searches for an MCP host/server across `FDP/Diagnostics`,
  `FDP/ExtDeps/GizmoMap`, `Hrot/Diagnostics`, and the orchestration paths found **none**. The
  "AI assistance/control/diagnostics via MCP" capability is **not present in the scanned
  codebase** — it appears to be external tooling or planned. (No constraints to document yet.)

---

## Part 8 — Presentation, UI & Stride

### 8.1 Presentation / UI / editors
- 🔴 **All ImGui/Raylib draw calls on the main thread** between `NewFrame`/`Render` (not
  thread-safe). Cross-thread notification only via `volatile` flags. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs:423`
- 🔴 **NodeEdit is read-only for editors** — mutate `IGraphModel` only through
  `IGraphCommandSink` (preserves undo/validation); never retain `ICanvasRenderContext` across
  frames; custom renderers must not mutate `SelectionState`. Transient drag updates don't push
  undo — only the committed move does. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs:7`, `ICanvasRenderContext.cs:12-38`, `Commands/UndoStack.cs:15`
- 🔴 **No allocating LINQ in panel/pick hot paths** (rebuild filtered lists on change; `IsMatch`
  is O(1)). Don't close the component-edit window on validation failure. `Hrot/Engine/Hrot.Presentation/Panels/SpawnerPanel.cs:22-23`, `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs:108`
- 🔴 **ImGui layout footguns:** open popups **outside** `BeginMainMenuBar`; context-menu popups
  in the **same child** as `OpenPopup`; `ImGuiPropertyTree`/`ComponentEditDrawer` only inside
  their own/established `BeginTable`; add fonts **before** the atlas bake. In the picker wide
  layout use `DrawList.AddText` (not `SetCursorPos`/text widgets) so strings containing `%`
  don't crash native `vsnprintf`. `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs:480`, `.../NodeEditor.UI/Picker/Layouts/WideLayout.cs:134`
- 🟡 **`BehaviorUiRegistry` is one-time startup**; reflection happens at compile time, draw is
  allocation-free. `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiCompiler.cs:37-44`
- 🔵 **Adapter limits:** `CanvasMapPickAdapter` has no area-pick (point only);
  `SimulationViewAdapter.GetEntities()` is empty (`ISimulationView` has no enumeration). Zones
  are **not** ECS entities and bypass the genesis pipeline — applied synchronously in `Commit`.
  `GridMapLayer` ignores the visibility bitmask. `MapCanvas` resets the right-drag flag
  unconditionally each frame. `FDP/Engine/Fdp.Presentation/ImGui/Adapters/SimulationViewAdapter.cs:41-43`, `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Handlers/HrotEditLoadHandler.cs:29-31`

### 8.2 Stride 3D integration
- 🔴 **All FDP↔Stride conversion goes through `FdpStrideTransform`** — Stride is **left-handed
  Y-up**; conversion needs both an axis swizzle (FDP.Z→Stride.Y, FDP.Y→Stride.Z) **and**
  negation of the quaternion's imaginary parts. A pure relabel produces wrong rotations.
  Navmesh/DotRecast input is in Stride space — swizzle FDP positions first. `Stride/Hrot.Stride.Core/FdpStrideTransform.cs:10-26`, `DotRecastNavmeshProvider.cs:20`
- 🔴 **The seam services (`IPhysicsBodyService`, `IStrideVisualFactory`, `IStrideRaycastService`)
  are single-threaded** — Stride's `Physics.Simulation`/`Content.Load` aren't thread-safe;
  all calls on the Stride host thread. `Stride/Hrot.Stride.Core/IPhysicsBodyService.cs:40`
- 🔴 **Stride-only fixes must not leak into shared toolkits.** Infantry-vs-vehicle routing,
  `VehicleState` stripping, frustration skips belong in the **Stride muscle**
  (`InfantryVehicleStateStripTkbTranslator`), never in shared `NavigationExecutionSystem`
  (commit `8b8cc439` did this and broke non-Stride tank routing — reverted). Reference-guard
  tests enforce `Hrot.Stride.Core` has no Raylib/rlImGui/StrideMock deps. `Stride/Hrot.Stride.Core/InfantryVehicleStateStripTkbTranslator.cs:16`, `Stride/Hrot.Stride.Core.Tests/ReferenceGuardTests.cs:32`
- 🔴 **`BulletReverseSyncSystem` must be `TogglablePostSimulationGroup`-wrapped (off during
  replay) and only process `.WithOwned<SimTransform>()`** — else it overwrites restored/ghost
  positions. Capsule (character) velocity is derived from **pose delta**, not
  `PostCollisionLinearVelocityFdp` (which gives the commanded, not actual, value). `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs:21-51`
- 🔴 **`StrideKinematicsModule` must not register FDP integrators** (`CarKinematicsSystem`/
  `LinearKinematicsSystem`) — Bullet integrates; double-integration corrupts `SimTransform`.
  `PhysicsBodyLifecycleSystem` reads shape from `StrideVisualReference`, never re-resolving the
  TKB descriptor. `Stride/HrotStrideApp.Game.Tests/StrideKinematicsModuleIntegrationTests.cs:106`, `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs:211`
- 🔴 **Bullet runtime properties** (`AngularFactor`, `LinearFactor`, `CanSleep`,
  `LinearDamping`, `Friction`) must be set **after** the entity is added to the scene & the
  `PhysicsProcessor` ran — defer via `PendingDynamicConfig`. (`ColliderShape`, `IsKinematic`,
  `Mass` are safe in the initializer.) `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs:534`
- 🟡 **Don't silently swallow missing model assets** — `StrideVisualFactory` rethrows naming
  the URL; no placeholder fallback. `Stride/HrotStrideApp.Game/StrideVisualFactory.cs:117`
- 🔵 **Environment limits:** this Stride version (4.2.1.2487) has **no immediate-mode debug-shape
  API** (3D shapes use a pooled-entity workaround); kinematic vehicles **block-or-slide** (no
  curved-surface slides); full `StrideHrotGame` **requires a GPU/display** — CI must use the
  seam fakes. `Stride/Hrot.Stride.Core/PooledEntityDebugDrawSink3D.cs:19`, `Stride/HrotStrideApp.Game/StrideHrotGame.cs:79`

---

## Appendix — Provenance & gaps

- **Method:** this guide was assembled by mining source comments, XML-doc `<remarks>`, and the
  design docs across 19 subsystem areas (349 grounded findings, all with `file:line`
  citations). Raw per-area findings are saved under `.dev/_mining/*.json` if you want the
  full set or to regenerate.
- **Topics with no grounded material found in the scanned code** (so absent from this guide):
  a runtime **MCP server** (not present anywhere scanned); a named **dry-run** subsystem;
  per-bone/IK **human-animation** constraints; explicit per-topic **QoS** override tables
  (CycloneDDS defaults are used); and dedicated **group-maneuver/ORBAT** classes beyond the
  squad HSM primitives.
- **When you change a limit or invariant**, update both the code's named constant **and** this
  guide's table row + the relevant `docs/` narrative. The doc-drift box in Part 2 exists
  because that didn't happen for the component-count and behavior-param caps.
