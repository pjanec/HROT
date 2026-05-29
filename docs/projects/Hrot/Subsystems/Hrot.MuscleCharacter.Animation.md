# Hrot.MuscleCharacter.Animation

**Project path:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Hrot.MuscleCharacter.Animation.csproj`
**Assembly:** `Hrot.MuscleCharacter.Animation`
**Target framework:** net8.0
**Date:** 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary
architectural reference.

---

## Executive Overview

`Hrot.MuscleCharacter.Animation` is the core animation runtime library for humanoid
character animation in the HROT military simulation. It is a pure C# class library
(no executable, no entry point) that:

1. **Defines the `IAnimationBackend` abstraction** — the engine-agnostic interface
   that decouples ECS systems from any specific animation engine (Stride, Fake, etc.).
2. **Provides ECS components** for replicated animation state (channels, queue, stance,
   look-at) and Muscle-internal executor state.
3. **Implements eight ECS systems** that collectively form the Muscle-side animation
   pipeline, orchestrated by `AnimationMuscleModule` in a strict phase order.
4. **Translates TKB character animation descriptors** into runtime components via
   `AnimationTkbTranslator`, with per-class baked caching and hot-reload support.
5. **Provides Blueprint AiPrimitive nodes** (11 nodes) for authoring montage, stance,
   and look-at commands in Blueprints, behavior trees, and HSM contexts.
6. **Defines eight typed engine events** for animation lifecycle and notifies.
7. **Validates animation descriptors and Blueprint references** via ANIM001–ANIM011.

The design is governed by:
- `DD-1_MuscleCharacterRuntime_v1_2.md` — ECS systems and channel pipeline
- `DD-3_EventCatalog_AnimationNotify_v1_3.md` — event types and event IDs
- `DD-4_TKB_AnimationDescriptor_v1_2.md` — TKB descriptor and baking
- `DD-5_BlueprintPrimitives_v1_1.md` — AiPrimitive node contracts

---

## Architecture

### Brain–Muscle Split

The animation pipeline spans two simulation nodes:

- **Brain** node: authors animation intent by writing to `AnimationChannel`,
  `LookAtChannel`, `StanceIntent`, and `AnimationMontageQueue` components.
  Brain runs behavior trees and Blueprints that invoke AiPrimitive nodes
  (see [Blueprint Primitives](#blueprint-aiprimitive-nodes)).
- **Muscle** node: owns the `IAnimationBackend` instance, runs the eight ECS
  systems in `AnimationMuscleModule`, and translates component state into
  backend calls each tick.

Components replicated cross-node (Brain → Muscle) are defined in
`ReplicatedComponents.cs`; non-replicated Muscle-internal state lives in
`InternalComponents.cs`.

### ECS Channel Pattern

Animation commands follow the same channel pattern as `LocomotionChannel` and
`WeaponChannel` in Fdp.Toolkits. Each channel component carries:

- `ActiveAction` (ushort) — the current action ID (0 = idle)
- `BehaviorInstanceId` / `ActionInstanceId` — for dispatcher routing and preemption
- `DispatchedInstanceId` — synchronization between dispatcher ticks
- `Status` (NodeStatus) — Idle / Running / Success / Failure
- `Params[32]` — action parameter payload (fixed-size struct)
- `State[32]` — executor state payload (progress, blend weights)

### System Execution Order

`AnimationMuscleModule` registers eight systems in a mandatory phase order
defined in DD-1 §17:

```
SystemPhase.Simulation  (executed in registration order within phase)
  1. AnimationCapabilityChangeReactorSystem  -- capability loss fast-path
  2. AnimationDispatcherSystem               -- routes AnimationChannel commands
  3. LookAtDispatcherSystem                  -- routes LookAtChannel commands
  4. StanceTransitionSystem                  -- drives stance intent -> backend
  5. MontageQueueAdvanceSystem               -- advances queue before bridge
  6. AnimationRuntimeBridgeSystem            -- registers entities; calls backend.Tick

SystemPhase.PostSimulation  (executed in registration order within phase)
  7. NotifyEventEmitterSystem                -- drains backend notifies after Tick
  8. AnimationStateReporterSystem            -- synthesizes completion events
  9. AnimationBackendCleanupSystem           -- unregisters destroyed entities (late)
```

---

## ASCII Block Diagrams

### Diagram 1: Assembly Dependency Graph

```
+----------------------------------------------+
|  Hrot.MuscleCharacter.Animation              |  net8.0 class library
+----------------------------------------------+
   |        |
   |        +------- Fdp.Core
   |                    (Entity, EntityRepository, GlobalComponentIds,
   |                     ITkbEntityTranslator, ITkbHotReloadEvents)
   |
   +------- Fdp.Toolkits
               (IAnimationBackend dispatch infra, BehaviorConstants,
                DispatcherSystemBase, NodeStatus, ActorCapabilities,
                BlueprintRegistryStaging, BlueprintDefinition)
```

### Diagram 2: Muscle Animation Pipeline (per tick)

```
Brain node writes:
  AnimationChannel.ActiveAction = PlayMontage (action=1)
  AnimationChannel.Params = PlayMontageParams { MontageId, BlendIn, ... }
  AnimationMontageQueue (optional queue entries)
  StanceIntent.TargetStance = Crouched
  LookAtChannel.ActiveAction = LookAtPoint (action=10)

                    |  replicated via AnimationReplication  |
                    v                                        v
Muscle tick (Simulation phase):
  1. CapabilityReactor   -- if capability lost -> force-stop, set Failure
  2. AnimationDispatcher -- if new ActionInstanceId -> OnEnter(PlayMontageExecutor)
  3. LookAtDispatcher    -- if new ActionInstanceId -> OnEnter(LookAtPointExecutor)
  4. StanceTransitionSystem  -- stance version mismatch -> backend.RequestStanceChange
  5. MontageQueueAdvance -- if queue running -> pop next entry, stage on channel
  6. RuntimeBridgeSystem -- first tick: RegisterEntity; every tick: backend.Tick(dt)

Muscle tick (PostSimulation phase):
  7. NotifyEmitter       -- backend.DrainNotifies -> publish AnimNotifyEvent
  8. StateReporter       -- detect slot completion -> emit MontageStarted/Ended
                         -- detect stance change   -> emit StanceChangedEvent
                         -- write channel.Status = Success/Failure
  9. BackendCleanup      -- on DestructionOrder -> backend.UnregisterEntity
```

### Diagram 3: TKB Translation at Ghost Promotion

```
+----------------------------+       +----------------------------------+
|  AnimationTkbTranslator    |       |  BakedAnimationCache             |
|  : ITkbEntityTranslator    |       |  ConcurrentDictionary<long,      |
+----------------------------+       |    CharacterAnimationBakedData>  |
| Inject(repo, entity,       |       +----------------------------------+
|   template)                |-----> GetOrBake(classId, dto)
|                            |         BakingUtils.BakeDef(dto)
|  1. GetDescriptor<         |              (montage list, stance map,
|     CharacterAnimationDef  |               marker hashes, slot defs)
|     Dto>()                 |
|  2. AddComponent           |       +----------------------------------+
|     AnimationChannel       |       |  ITkbHotReloadEvents (optional)  |
|     LookAtChannel          |       |  Subscribe -> OnDescriptorChanged|
|     StanceIntent           |       |  -> evict cache entry            |
|     StanceStatus           |       +----------------------------------+
|     AnimationMontageQueue  |
|     AnimationMontageQueue  |
|     State                  |
|     AnimationExecutorState |
|     LookAtExecutorState    |
|     CharacterAnimationDef  |
|     Runtime (handle)       |
+----------------------------+
```

---

## Key Types

### Contracts (`Hrot.MuscleCharacter.Animation.Contracts`)

| Type | Kind | Description |
|------|------|-------------|
| `IAnimationBackend` | interface | Engine-agnostic animation backend contract; all Muscle systems depend only on this |
| `AnimationBackendHandle` | struct | Generation-safe entity handle (Index + Generation) for backend operations |
| `AnimationBackendConfig` | struct | Initialization config (MaxEntities, default blend times, etc.) |
| `AnimationBackendMetrics` | struct | Performance snapshot (active entity count, tick time, pending notifies) |
| `AnimNotifyCategory` | enum (byte) | Canonical notify categories: Generic(0), Footstep(1), HitWindowOpened(2), HitWindowClosed(3) |
| `MontageAssetId` | struct | Stable FNV1a hash-based montage ID (engine-agnostic; computed by `StableIdHasher`) |
| `MontagePlaybackState` | enum (byte) | Slot lifecycle: Inactive / Active / BlendingOut |
| `SlotId` | enum (byte) | Slot indices 0–7 for 8 concurrent playback slots |
| `RawNotifyEvent` | struct | Notify fired by backend: Kind, MarkerHash, TimeSeconds, PayloadFloat, PayloadUint |
| `PlayMontageParams` | struct (32 bytes) | ActionParams for PlayMontage: MontageId, BlendIn/Out, PlayRate, StartSection, LoopCount, Priority, Flags |
| `StopMontageParams` | struct (32 bytes) | ActionParams for StopMontage: BlendOutTime, StopReason |
| `PlayMontageQueueParams` | struct (32 bytes) | ActionParams for PlayMontageQueue: InitialBlendInTime, Priority, Flags |
| `AnimationActionIds` | static class | Action ID constants: PlayMontage=1, StopMontage=2, PlayMontageQueue=3, EnqueueMontage=4, ClearMontageQueue=5 |
| `LookAtActionIds` | static class | Action ID constants: LookAtPoint=10, LookAtEntity=11, ReleaseLook=12 |

### Components (`Hrot.MuscleCharacter.Animation.Components`)

Replicated/contractual components (defined in `ReplicatedComponents.cs`, replicated Brain→Muscle):

| Component | ComponentId | Description |
|-----------|-------------|-------------|
| `AnimationChannel` | 220 | Montage playback intent channel; carries ActiveAction, BehaviorInstanceId, ActionInstanceId, Status, Params[32], State[32] |
| `LookAtChannel` | 221 | Look-at/aim intent channel; same layout as AnimationChannel |
| `StanceIntent` | 222 | Brain-authored stance request: TargetStance + Version counter |
| `StanceStatus` | 223 | Muscle-authored acknowledgment: CurrentStance + AckVersion |
| `AnimationMontageQueue` | 224 | Fixed-size (max 8) queue of chained montage entries (MontageId, BlendIn, BlendOut, PlayRate) |
| `AnimationMontageQueueState` | 225 | Tracking: CurrentEntryIndex, Count, TrackingActive, StartActionInstanceId |

Muscle-internal components (defined in `InternalComponents.cs`, not replicated):

| Component | ComponentId | Description |
|-----------|-------------|-------------|
| `AnimationExecutorState` | 230 | Slot table (8 slots x 28 bytes) + LastActiveMontageId; holds ongoing per-slot ECS state |
| `LookAtExecutorState` | 231 | Aim target position, blend weights, TargetType (none/point/entity) |
| `CharacterAnimationDefRuntime` | 232 | Baked def handle + StanceCount + SlotCount; set by TKB translator |

Enumerations used by components:

| Type | Description |
|------|-------------|
| `StanceId` | Standing(0), Crouched(1), Prone(2) |
| `StanceTransitionPhase` | Idle / Transitioning / Locked |

### ECS Systems (`Hrot.MuscleCharacter.Animation.Systems`)

| System | Phase | Responsibility |
|--------|-------|----------------|
| `AnimationCapabilityChangeReactorSystem` | Simulation (1st) | Detects `CanPlayAnimations` / `CanAim` capability loss; force-stops dispatchers, releases aim, writes Failure status |
| `AnimationDispatcherSystem` | Simulation (2nd) | Extends `DispatcherSystemBase<AnimationChannel>`; capability-gates commands; routes to PlayMontage/StopMontage/Queue executors |
| `LookAtDispatcherSystem` | Simulation (3rd) | Extends `DispatcherSystemBase<LookAtChannel>`; routes LookAtPoint/LookAtEntity/ReleaseLook executors |
| `StanceTransitionSystem` | Simulation (4th) | Compares `StanceIntent.Version` vs `StanceStatus.AckVersion`; calls `backend.RequestStanceChange` on mismatch |
| `MontageQueueAdvanceSystem` | Simulation (5th) | Pops next queue entry when slot becomes free; stages next montage onto `AnimationChannel` |
| `AnimationRuntimeBridgeSystem` | Simulation (6th) | First tick: registers entity with backend; every tick: calls `backend.Tick(deltaTime)` |
| `NotifyEventEmitterSystem` | PostSimulation (7th) | Calls `backend.DrainNotifies(handle, buf)` per entity; publishes `AnimNotifyEvent` to the ECS event bus |
| `AnimationStateReporterSystem` | PostSimulation (8th) | Detects slot completion and stance changes; emits MontageStarted/Ended/SectionAdvanced/StanceChanged events; writes `channel.Status = Success` |
| `AnimationBackendCleanupSystem` | PostSimulation (9th) | Listens for `DestructionOrder` events; calls `backend.UnregisterEntity` before entity is reaped |

### Executors (`Hrot.MuscleCharacter.Animation.Executors`)

Executors are registered with the dispatcher systems and implement `OnEnter / OnTick / OnExit`:

| Executor | Action | Notes |
|----------|--------|-------|
| `PlayMontageExecutor` | PlayMontage (1) | Calls `backend.PlayMontageOnSlot`; records `LastActiveMontageId` in executor state |
| `StopMontageExecutor` | StopMontage (2) | Calls `backend.StopMontageOnSlot`; reads `LastActiveMontageId` to publish interrupted event |
| `PlayMontageQueueExecutor` | PlayMontageQueue (3) | Initiates queue playback; sets up `AnimationMontageQueueState` for `MontageQueueAdvanceSystem` |
| `EnqueueExecutor` | EnqueueMontage (4) | Appends one entry to `AnimationMontageQueue` during active queue playback |
| `ClearQueueExecutor` | ClearMontageQueue (5) | Zeros out the queue component directly |
| `LookAtPointExecutor` | LookAtPoint (10) | Calls `backend.SetAimTargetPoint` |
| `LookAtEntityExecutor` | LookAtEntity (11) | Calls `backend.SetAimTargetEntity` (entity position resolved at bridge time) |
| `ReleaseLookExecutor` | ReleaseLook (12) | Calls `backend.ReleaseAim` |

### TKB Descriptor (`Hrot.MuscleCharacter.Animation.Descriptors`)

| Type | Description |
|------|-------------|
| `CharacterAnimationDefDto` | Root TKB descriptor: Slots[], Montages[], StanceTransitions[], AimConfig, NotifyMarkers[], ClassName |
| `SlotDefDto` | Per-slot definition: SlotId (0–255), Name, BoneMask[], Mode (Override/Additive), Priority |
| `MontagDefDto` | Per-montage definition: Name, DurationSeconds, SectionNames[], Markers[], DefaultPlayRate |
| `StanceTransitionDto` | Stance-to-stance transition: FromStance, ToStance, TransitionMontageName, BlendDurationSeconds |
| `AimConfigDto` | Aim overlay: AimBoneNames[], DefaultBlendInTime, DefaultBlendOutTime |
| `SlotCompositingMode` | Override(0) / Additive(1) |

### Baking (`Hrot.MuscleCharacter.Animation.Baking`)

| Type | Description |
|------|-------------|
| `BakedAnimationCache` | `ConcurrentDictionary<long, CharacterAnimationBakedData>` keyed by class ID; subscribes to `ITkbHotReloadEvents` to evict stale entries |
| `BakedAnimationDef` / `CharacterAnimationBakedData` | Immutable pre-computed lookup tables: MontageId→def, MarkerHash→def, Stance→TransitionMontageId |
| `BakingUtils.BakeDef` | Converts a `CharacterAnimationDefDto` into a `CharacterAnimationBakedData` using `StableIdHasher` |

### Hashing (`Hrot.MuscleCharacter.Animation.Hashing`)

| Type | Description |
|------|-------------|
| `StableIdHasher` | FNV1a-64 based hashing; `ComputeMontageAssetId(name)` → signed 31-bit positive int; deterministic across runs and machines |

### Events (`Hrot.MuscleCharacter.Animation.Events`)

All events are `readonly struct` and registered with the engine event catalog.
Lifecycle events are synthesized by `AnimationStateReporterSystem`; notify events
are drained from the backend by `NotifyEventEmitterSystem`.

| Event | EventId | Source | Description |
|-------|---------|--------|-------------|
| `MontageStartedEvent` | 8201 | StateReporter | Montage began playing; carries Target, MontageId, ActionInstanceId, QueueIndex |
| `MontageEndedEvent` | 8202 | StateReporter | Montage finished (natural, interrupted, blended-out, or failed); carries MontageEndReason |
| `MontageSectionAdvancedEvent` | 8203 | StateReporter | Section index changed within a montage; carries FromSectionIndex, ToSectionIndex |
| `StanceChangedEvent` | 8204 | StateReporter | Character stance changed; carries PreviousStance, NewStance |
| `AnimNotifyEvent` | 8210 | NotifyEmitter | Generic notify fired from backend; carries MontageId, MarkerHash, PayloadFloat |
| `FootstepEvent` | 8211 | NotifyEmitter | Footstep impact; carries FootIndex (0=left, 1=right) — local-only, not replicated |
| `HitWindowOpenedEvent` | 8212 | NotifyEmitter | Melee hit window opened |
| `HitWindowClosedEvent` | 8213 | NotifyEmitter | Melee hit window closed |

`MontageEndReason` values: NaturalEnd(0), Interrupted(1), BlendedOutByNext(2), Failed(3).

### Picker Attributes (`Hrot.MuscleCharacter.Animation.Events`)

| Attribute | Applied to | Purpose |
|-----------|-----------|---------|
| `[MontagePicker]` | `int` fields | Marks a montage ID field for editor picker support (shows dropdown of available montages) |
| `[AnimMarkerPicker]` | `uint` fields | Marks a marker hash field for editor picker support |

### Blueprint AiPrimitive Nodes (`Hrot.MuscleCharacter.Animation.Nodes`)

All 11 nodes are unmanaged `struct`s registered as AiPrimitives by `AnimationNodeRegistrar`
(IDs 5001–5011). They work identically in BTree, HSM, and Blueprint contexts.

Action nodes:

| Node | AiPrimitive ID | Description |
|------|---------------|-------------|
| `PlayMontageNode` | 5001 | Play a single montage: TargetCharacter, `[MontagePicker]` MontageId, SlotIndex |
| `StopMontageNode` | 5002 | Stop current montage with blend-out: TargetCharacter, SlotIndex |
| `PlayMontageChainNode` | 5005 | Play a sequence of up to 8 montages: ChainCount, ChainedMontages[8] |
| `EnqueueMontageNode` | 5003 | Append one montage to active queue: TargetCharacter, MontageId |
| `ClearMontageQueueNode` | 5004 | Clear all pending queue entries |
| `SetStanceNode` | 5006 | Request a stance change: TargetCharacter, TargetStance (StanceId) |
| `LookAtPointNode` | 5007 | Aim at world-space point: TargetCharacter, TargetPointXYZ, BlendInTime, Priority |
| `LookAtEntityNode` | 5008 | Aim at entity: TargetCharacter, TargetEntity, OffsetXYZ, BlendInTime, Priority |
| `ReleaseLookNode` | 5009 | Release aim overlay: TargetCharacter, BlendOutTime |

Getter nodes (read-only; usable as BTree conditions):

| Node | AiPrimitive ID | Description |
|------|---------------|-------------|
| `GetMontageQueueProgressNode` | 5010 | Reads queue index, elapsed time, and active status from target entity |
| `GetCurrentStanceNode` | 5011 | Reads current stance, transition phase, and blend weight |

### Validators (`Hrot.MuscleCharacter.Animation.Validation`)

`AnimationValidators.ValidateDto(dto)` runs DTO-level checks at TKB load time.
Compiler-level checks run when Blueprint nodes reference animation actions.

| Rule ID | Level | Trigger | Description |
|---------|-------|---------|-------------|
| ANIM001 | Error | Compiler | `PlayMontageNode.MontageId` references a montage name not defined in the character's TKB descriptor |
| ANIM002 | Error | Compiler | `StopMontageNode.SlotIndex` out of range for the character's slot count |
| ANIM003 | Error | Compiler | `PlayMontageChainNode.ChainCount` exceeds 8 or is 0 |
| ANIM004 | Error | Compiler | `SetStanceNode.TargetStance` references a stance not defined in the descriptor |
| ANIM005 | Error | Compiler | `PlayMontageChainNode` entry has zero MontageId (unassigned slot) |
| ANIM006 | Error | TKB load | `StanceTransitions` entry references a transition montage name not in `Montages[]` |
| ANIM007 | Warning | TKB load | Notify marker name in a montage is not found in the top-level `NotifyMarkers[]` catalog |
| ANIM008 | Error | Compiler | `LookAtPointNode` target character lacks `CanAim` capability flag in the descriptor |
| ANIM009 | Error | Compiler | `LookAtEntityNode` target character lacks `CanAim` capability flag |
| ANIM010 | Warning | Compiler | `PlayMontageChainNode` used without `[InlineArray]` safe pattern (codegen path check) |
| ANIM011 | Error | Compiler | Conflicting action nodes address the same channel with incompatible priorities in one Blueprint |
| ANIM012 | (deferred) | Compiler | `PlayMontageChainNode` custom drawer validation — not yet implemented |

BP2016 and BP2017 are Blueprint compiler validators (in `Hrot.Blueprints.Compiler`):

| Rule | Level | Description |
|------|-------|-------------|
| BP2016 | Warning | A `WhenNode` is wired to a BestEffort DDS event (may miss events under load) |
| BP2017 | Error | A Brain Blueprint subscribes to a local-only event (e.g., `FootstepEvent`) which is never replicated from Muscle |

---

## Dependencies

```
Hrot.MuscleCharacter.Animation
  --> Fdp.Core                    (Entity, EntityRepository, GlobalComponentIds,
                                   ITkbEntityTranslator, ITkbHotReloadEvents,
                                   ComponentId, DataPolicy attributes)
  --> Fdp.Toolkits                (DispatcherSystemBase, BehaviorConstants,
                                   ActorCapabilities, ActorCapabilityState,
                                   NodeStatus, BlueprintRegistryStaging,
                                   BlueprintDefinition, IEcsModule,
                                   IEcsModuleSystem, ISystemRegistry)
```

The project does **not** reference Hrot.Core, any Hrot subsystems, Stride, or
CycloneDDS. It has no upward dependencies on any game-side assembly.

---

## Usage Patterns

### Registering the module on the Muscle node

```csharp
// At Muscle node startup:
IAnimationBackend backend = new FakeAnimationBackend(classData);
// Or in production: new StrideAnimationBackend(strideEngine);

var cache = new BakedAnimationCache(hotReloadEvents);
var module = new AnimationMuscleModule(backend, cache);
systemRegistry.RegisterModule(module);

// Register the TKB translator so it fires on ghost promotion:
tkbRegistry.RegisterTranslator(new AnimationTkbTranslator(hotReloadEvents));
```

### Writing an animation command from a BTree action

```csharp
// Inside a BTree action delegate (Brain side):
ref var channel = ref repo.GetComponentRW<AnimationChannel>(entity);
channel.ActiveAction = AnimationActionIds.PlayMontage;
channel.BehaviorInstanceId = ctx.BehaviorInstanceId;
channel.ActionInstanceId++;  // bump to preempt stale

var p = new PlayMontageParams
{
    MontageId = StableIdHasher.ComputeMontageAssetId("Run_Fwd"),
    BlendInTime  = 0.2f,
    BlendOutTime = 0.3f,
    PlayRate = 1.0f,
};
unsafe { *(PlayMontageParams*)channel.Params = p; }
channel.Status = NodeStatus.Running;
```

### Using AiPrimitive nodes in a Blueprint

```csharp
// Blueprint authoring (compile-time struct):
var node = new PlayMontageNode
{
    TargetCharacter = 0,  // self-reference resolved at dispatch time
    MontageId = StableIdHasher.ComputeMontageAssetId("Idle_Ready"),
    SlotIndex = 0,
};
blueprint.Append(AnimationNodeRegistrar.PlayMontage_AiId, node);
```

### Reacting to animation events in a behavior tree

```csharp
// BTree action: wait for montage completion
foreach (var evt in view.ReadEvents<MontageEndedEvent>())
{
    if (evt.Target == myEntity && evt.EndReason == MontageEndReason.NaturalEnd)
        // transition to next behavior
}

// Footstep sound trigger (local Muscle node only):
foreach (var evt in view.ReadEvents<FootstepEvent>())
    audioSystem.PlayFootstep(evt.Target, evt.FootIndex);
```

### Validating a TKB descriptor

```csharp
var messages = AnimationValidators.ValidateDto(dto);
foreach (var msg in messages)
{
    if (msg.Severity == ValidationSeverity.Error)
        throw new InvalidDataException($"{msg.RuleId}: {msg.Message} [{msg.Context}]");
}
```

---

## Test Projects

| Project | Description |
|---------|-------------|
| `Hrot.MuscleCharacter.Animation.Tests` | Layer-1 (unit) and Layer-2 (system) tests. Covers TKB translator, baked cache, all 8 ECS systems, executor state machines, and Blueprint nodes |
| `Hrot.Animation.Integration.Tests` | Networkless stage-1 integration suite (8 scenarios) exercising the full Muscle pipeline with `FakeAnimationBackend` and `PumpUntil` harness |
| `Hrot.Animation.Network.Integration.Tests` | Networked stage-2 integration suite exercising full Brain→Muscle replication round-trips |
| `Hrot.MuscleCharacter.Animation.Fake.Tests` | Smoke tests for the `FakeAnimationBackend` in isolation |
