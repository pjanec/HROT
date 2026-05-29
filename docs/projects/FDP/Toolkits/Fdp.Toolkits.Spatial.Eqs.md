# Fdp.Toolkit.Spatial.Eqs -- Environment Query System (EQS) v1.3

**Source folder**: `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/`
**Primary namespace**: `Fdp.Toolkit.Spatial.Eqs`
**Companion namespace**: `FDP.Eqs` (for `EqsSensorHandle`)
**DDS topics namespace**: `Fdp.Toolkit.Spatial.Eqs.Topics`
**Design reference**: `.dev/eqs-2/EQS_Design_v1.3_final.md`
**Date**: 2026-05-30

---

## Overview

EQS is the AI's standing-query system for understanding the spatial and tactical state of
the world. It upgrades the engine's earlier `AreaQuerySolverSystem` (polygon containment only)
into a full system supporting:

- **Entity queries** -- nearest enemies matching predicates, threat lists, ally queries.
- **Positional queries** -- best cover position, flanking point, retreat routes.
- **Path-aware queries** -- navmesh reachability and path-cost scoring.
- **LOS-filtered queries** -- cheap (single-ray) and accurate (multi-ray) line-of-sight tests.

Target scale is 10k agents (final C++ stage); hundreds at current Stride3D stage.

---

## Architecture

### Four-Layer Model

```
+-------------------------------------------+   Brain Node
|  Layer 1: Sensor declarations             |
|  EqsSensor component, runtime params,     |
|  refresh policy, priority                 |
+-------------------------------------------+
|  Layer 2: Cognitive buffer + reader       |
|  EqsCognitiveBuffer, synchronous reader   |
|  GetTop / GetSpanRO for BTree nodes       |
+-------------------------------------------+
                     | DDS (EqsSensorConfig / EqsResult topics)
+-------------------------------------------+   Muscle Node
|  Layer 3: Solver service                  |
|  EqsSolverSystem -- time-sliced, 10 Hz    |
|  EqsModule drives it on background thread |
+-------------------------------------------+
|  Layer 4: Tactical world providers        |
|  ICoverProvider, INavmeshProvider,        |
|  ILosService, SpatialHashGrid             |
+-------------------------------------------+
```

### Brain/Muscle Boundary Protocol

EQS follows the engine's autonomous-perception replication pattern.

**Brain -> Muscle (configuration):**

The Brain creates an `EqsSensor` component on the agent entity. An egress translator
(`EqsSensorConfigEgressTranslator`) monitors changes via `SmartEgressUtil` and publishes
to the `EqsSensorConfig` DDS topic. On the Muscle, `EqsSensorConfigIngressTranslator`
applies the component to the ghost entity. Component presence is the subscription
mechanism -- no bespoke subscribe message exists.

**Muscle -> Brain (results via discrete events):**

1. The Muscle solver maintains per-sensor state (`SensorEvalState`) locally.
2. On completion the solver writes ranked candidates into `EqsResultPool` (native ring buffer)
   and obtains an integer `ResultHandle`.
3. An unmanaged `EqsResultEvent { ParentNetworkId, LocalChildIndex, Epoch, RefreshTick,
   ResultHandle, EntryCount }` is published on the Muscle event bus.
4. `EqsResultEventEgressTranslator` dereferences the pool and publishes an `EqsResult` DDS
   topic sample with a `[DdsManaged] List<EqsResultEntry>` payload.
5. On the Brain, `EqsResultIngressTranslator` reads the DDS sample and publishes a managed
   `EqsResultUpdateEvent` onto the Brain event bus.
6. `EqsResultUpdateSystem` (Brain-tier, `SystemPhase.Simulation`) writes results into the
   entity's `EqsCognitiveBuffer` component.
7. BTree/HSM nodes read from the buffer synchronously via `GetTop` / `GetSpanRO`.

**Offline/editor path:** When running as a single process (no DDS), the solver publishes
`EqsResultEvent` directly on the shared event bus. `EqsResultUpdateSystem` handles both
paths identically via Path A (DDS-bridged managed event) and Path B (local unmanaged event).

---

## ECS Components

### `EqsSensor`

Standing query configuration. Lives on the Brain entity. Replicated to Muscle.

```csharp
[ComponentId(GlobalComponentIds.EqsSensor)]
public struct EqsSensor
{
    public uint   BlueprintId;           // FNV-1a hash of template AssetId GUID
    public uint   Epoch;                 // incremented on any parameter change
    public float  SearchRadius;          // world-space units
    public uint   FactionFilter;         // bitmask of target factions
    public float  ThreatThreshold;       // minimum threat score for cheap LOS filter
    public byte   PublishPolicy;         // see EqsPublishPolicy enum
    public byte   Priority;              // solver band: Critical=0, Normal=1, Low=2
    public float  ScoreDeltaThreshold;   // used by ScoreDelta publish policy
    public Entity ContextSlot0;          // Self/Observer position source
    public Entity ContextSlot1;          // Target (primary LOS context)
    public Entity ContextSlot2;          // Leader/Squad-mate (secondary LOS context)
}
```

When any field changes the `Epoch` counter increments, causing the solver to discard
in-flight results and restart evaluation.

### `EqsCognitiveBuffer`

Brain-tier result cache. Written by `EqsResultUpdateSystem`; read by BTree nodes.

```csharp
[ComponentId(GlobalComponentIds.EqsCognitiveBuffer)]
[DataPolicy(DataPolicy.NoSave)]
public struct EqsCognitiveBuffer
{
    public int            Count;                 // valid entries (0-16)
    public uint           LastUpdateTick;        // simulation tick of last write
    public float          LastUpdateTimeSeconds; // simulation time (seconds) of last write
    public EqsResultArray Results;               // [InlineArray(16)] of EqsResult

    public Span<EqsResult>         GetSpanRW();  // for EqsResultUpdateSystem writes
    public ReadOnlySpan<EqsResult> GetSpanRO();  // for BTree reads
    public bool                    IsReady => LastUpdateTick > 0;
    public ref readonly EqsResult  GetTop();     // top-ranked entry (index 0)
}
```

`LastUpdateTimeSeconds` was added in Phase 10 (TASK-EQS-033) to support the `WhenNode`
`EqsResult.BecomesStale` trigger (see Blueprint integration section).

**Critical:** writes must go through `GetSpanRW()` to bypass the C# 12 `[InlineArray]`
`ldobj` defensive-copy trap that silently discards mutations when writing through a direct
index assignment.

### `EqsResult`

Single ranked candidate (32 bytes). Handles both entity-shaped and positional queries.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct EqsResult
{
    public long   EntityId;         // packed entity value; 0=positional; -1=rejected sentinel
    public float  PositionX;        // world-space X (positional queries)
    public float  PositionY;        // world-space Y (positional queries)
    public float  PositionZ;        // world-space altitude, Sim Z-up (P3D-201)
    public float  Score;            // final score [0..1] for Top-K ranking
    public short  Flags;            // standard flag bits (see table below)
    public short  FlagsMeaningful;  // parallel bitset: which flag bits were populated
}
```

`FlagsMeaningful` was added in Phase 10 (TASK-EQS-032). A bit not set in `FlagsMeaningful`
must not be read by consumers, regardless of the corresponding bit in `Flags`.

**Standard flag bits (16-bit field):**

| Bit | Meaning | Set by |
|---|---|---|
| 0 | `HasLOSToContext0` | `LineOfSight` test (context 0) |
| 1 | `HasLOSToContext1` | `LineOfSight` test (context 1) |
| 2 | `HasLOSToContext2` | `LineOfSight` test (context 2) |
| 3 | `NavmeshReachable` | `NavmeshReachable` test |
| 4 | `IsInCover` | `CoverQuality` test |
| 5 | `IsExposedFromKnownThreat` | `ThreatExposure` test |
| 6 | `IsPreferredSide` | `DotProduct` test |
| 7 | `WasFreshThisRefresh` | Solver lifecycle |
| 8-15 | Reserved | -- |

Context slots 0/1/2 map to `EqsSensor.ContextSlot0/1/2` respectively. Up to 3 LOS contexts
can be queried simultaneously per template.

### `EqsResultArray`

```csharp
[InlineArray(16)]
public struct EqsResultArray { private EqsResult _element; }
```

Fixed 16-slot inline array. Never write via direct indexing; use `EqsCognitiveBuffer.GetSpanRW()`.

### `EqsPublishPolicy`

```csharp
public enum EqsPublishPolicy : byte
{
    AlwaysPush  = 0,  // emit after every evaluation
    TopChanged  = 1,  // emit when top-ranked identity changes
    _Reserved2  = 2,
    ScoreDelta  = 3,  // emit when any top-K score delta exceeds ScoreDeltaThreshold
}
```

`ScoreDeltaThreshold` was added to `EqsSensor` and the DDS topic in Phase 10 (TASK-EQS-034).

---

## Per-Sensor Solver State

### `SensorEvalState`

Muscle-side component tracking per-sensor cross-tick evaluation state.

```csharp
[ComponentId(GlobalComponentIds.SensorEvalState)]
public struct SensorEvalState
{
    public EqsEvalPhase Phase;
    public int          PendingRaycastCount;
    public uint         AwaitingSinceTick;
    public uint         CurrentEpoch;        // snapshot of sensor.Epoch at eval start
    public ulong        CurrentStructureHash; // for hot-reload hard-reset detection
    public TopKScoreCache LastPublishedTopK; // [InlineArray(16)] of float scores
}

public enum EqsEvalPhase : byte
{
    Idle              = 0,
    Evaluating        = 1,
    _AwaitingRaycasts = 2,  // raycasts submitted; waiting for results
    Finalizing        = 3,  // reserved
}
```

### `EqsSolverGlobalState`

Muscle-side singleton tracking the per-tick accurate-raycast budget.

```csharp
[ComponentId(GlobalComponentIds.EqsSolverGlobalState)]
public struct EqsSolverGlobalState
{
    public int MaxAccurateRaycastsPerSolverTick;  // default 2048
    public int AccurateRaysSubmittedThisTick;      // reset at start of each EqsModule.Tick
}
```

---

## Query Templates

### `EqsQueryTemplate`

Compiled representation of an EQS query blueprint.

```csharp
public struct EqsQueryTemplate
{
    public uint          BlueprintId;      // FNV-1a hash of AssetId GUID
    public IEqsGenerator Generator;        // candidate producer
    public IEqsTest[]?   FilterCheap;      // fast reject tests
    public IEqsTest[]?   FilterExpensive;  // slow reject tests (before top-K reduction)
    public IEqsTest[]?   ScoreCheap;       // fast scoring tests
    public IEqsTest[]?   ScoreExpensive;   // slow scoring tests
    public int           MaxCandidates;    // generator cap
    public ulong         StructureHash;    // FNV-1a hash of generator + test type names
}
```

### `IEqsGenerator`

```csharp
public interface IEqsGenerator
{
    // Fills candidates span; returns valid count.
    // Entity queries store entity.PackedValue in EntityId.
    // Positional queries set EntityId = 0.
    int Generate(Entity observer, ref EqsSensor sensor,
                 ISimulationView view, Span<EqsResult> candidates);
}
```

### `IEqsTest`

```csharp
public interface IEqsTest
{
    EqsTestPhase Phase { get; }
    // Filters: set EntityId = -1L to reject.
    // Scorers: add to EqsResult.Score (additive, zero-allocation).
    void ExecuteBatch(Entity observer, ref EqsSensor sensor,
                      ISimulationView view, Span<EqsResult> candidates);
}
```

### `EqsTestPhase`

```csharp
public enum EqsTestPhase : byte
{
    FilterCheap      = 0,  // fast data-driven filters; run first
    FilterExpensive  = 1,  // slow filters (navmesh reachability); before top-K reduction
    ScoreCheap       = 2,  // fast scoring (distance falloff); after top-K reduction
    ScoreExpensive   = 3,  // slow scoring (cover quality, path cost, accurate LOS)
}
```

Top-K reduction runs between `FilterExpensive` and `ScoreCheap` when the candidate count
exceeds a threshold, so expensive scoring phases only operate on viable candidates.

### Template Authoring: `[EqsTemplate]` attribute

```csharp
// Hand-authored C# template registered by the Roslyn source generator:
[EqsTemplate("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]
public static class FindCoverFromTarget
{
    public const uint BlueprintId = 0x7F3A2B1Cu;

    public static EqsQueryTemplate Build(ILosService los) => new EqsQueryTemplate
    {
        BlueprintId   = BlueprintId,
        Generator     = new CoverPointsGenerator(),
        FilterCheap   = new IEqsTest[] { new CheapLineOfSightTest(los) },
        ScoreCheap    = new IEqsTest[] { new DistanceScoreTest() },
        MaxCandidates = 32,
    };

    // Overload for the source generator (no runtime dependencies).
    public static EqsQueryTemplate Build(IEqsTemplateBuilder b)
        => Build(new BlockedLosService());
}
```

Rules enforced by the `Fdp.Toolkits.Analyzers` purity analyser (TASK-EQS-020):
- `Build()` must be `static`, taking only `IEqsTemplateBuilder`.
- Must not read runtime state, singletons, or non-deterministic APIs.
- Runtime variation is expressed via `EqsSensor` parameters, not inside templates.

### Template Registry

```csharp
[ComponentId(GlobalComponentIds.IEqsTemplateRegistry)]
public interface IEqsTemplateRegistry
{
    bool TryGetTemplate(uint blueprintId, out EqsQueryTemplate template);
}
```

Stored as a managed singleton on the repo (`SetSingletonManaged<IEqsTemplateRegistry>`).
The source generator emits a `[BlueprintRegistrar]` class with a `RegisterAll()` method
that populates the registry at startup. Hot-reload replaces the singleton atomically via
`AiHotReloadCoordinator`.

---

## Generators

| Class | Query type | Description |
|---|---|---|
| `EntitiesInRadiusGenerator` | Entity | Entities within `SearchRadius` via spatial hash; writes full 3D `SimTransform` position into the candidate (P3D-203) |
| `CoverPointsGenerator` | Positional | Cover points from `ICoverProvider` in radius; streams 3D `CoverPoint.PositionZ` (P3D-204) |
| `NavmeshSamplesGenerator` | Positional | Navmesh sample points via `INavmeshProvider`; retains sampled Z (Recast Y-up altitude mapped to EQS Z-up, P3D-203) |

All generators must operate on the provided span with zero heap allocation.

---

## Tests (Filters and Scorers)

| Class | Phase | Kind | Description |
|---|---|---|---|
| `FactionFilterTest` | `FilterCheap` | Filter | Rejects candidates outside `FactionFilter` bitmask |
| `CheapLineOfSightTest` | `FilterCheap` | Filter | Cheap LOS via `ILosService.HasCheapLineOfSight` |
| `NavmeshReachableTest` | `FilterExpensive` | Filter | Navmesh reachability via `INavmeshProvider` |
| `AccurateLineOfSightTest` | `FilterExpensive` or `ScoreExpensive` | Filter/Score | Accurate LOS; submits `RaycastRequestEvent`; polls ring buffer cross-tick |
| `DistanceScoreTest` | `ScoreCheap` | Score | Inverse-linear distance falloff from observer; uses `Vector3.Distance` (true 3D, P3D-205) |
| `PathCostScoreTest` | `ScoreExpensive` | Score | Path-cost scoring via `INavmeshProvider`; uses candidate `PositionZ` for Sim-to-Recast axis mapping (P3D-205) |

All test classes implement `IEqsTest` and must be zero-allocation. Filters reject by
setting `EqsResult.EntityId = -1L` (the rejection sentinel). Scorers add to
`EqsResult.Score` additively.

---

## Service Interfaces

### `ICoverProvider`

```csharp
[ComponentId(GlobalComponentIds.ICoverProvider)]
public interface ICoverProvider
{
    int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results);
}
```

Implementations: `ManualCoverProvider` (designer-authored cover nodes placed in the scene).
Auto-generated navmesh-edge cover is a future stage.

### `CoverPoint`

28-byte unmanaged struct carrying cover node position (X, Y, Z), facing direction,
quality multiplier, and stance height (Prone/Crouch/Stand). The `PositionZ` field
was added via the 3D Cognitive Spatial Awareness promotion (P3D-204).

### `ILosService`

```csharp
public interface ILosService
{
    bool HasCheapLineOfSight(Vector2 observer, Vector2 target);
}
```

`BlockedLosService` -- stub that always returns `false` (LOS always blocked; cover always
valid). Used in Phase 3 and for `StructureHash` computation via `Build(IEqsTemplateBuilder)`.

---

## Result Pool

### `EqsResultPool`

Muscle-side ECS singleton: a native ring-buffer holding packed results.

```
Capacity: MaxConcurrentInFlightResults (1024) x MaxTopK (16) = 16384 EqsResult entries
Total native memory: 16384 x 32 bytes = ~512 KB
```

The solver calls `WriteAndWrap(ReadOnlySpan<EqsResult>)` which returns the base index
(the `ResultHandle`). The pool wraps at capacity. The egress translator dereferences the
handle in the same frame, so the ring-buffer write cursor is the only synchronization
needed.

### `EqsResultEvent`

28-byte strictly unmanaged event published by `EqsSolverSystem` on the Muscle bus:

```csharp
[EventId(2050)]
public struct EqsResultEvent
{
    public long ParentNetworkId;
    public int  LocalChildIndex;
    public uint Epoch;
    public uint RefreshTick;
    public int  ResultHandle;
    public int  EntryCount;
}
```

`ParentNetworkId == 0` indicates a local-only (offline/editor) sensor; in that case
`LocalChildIndex` holds the sensor entity's `Index`.

---

## DDS Topics

Both topics are defined in `Fdp.Toolkit.Spatial.Eqs.Topics`.

### `EqsSensorConfigTopic`

Direction: Brain -> Muscle. QoS: Reliable/TransientLocal/KeepLast(1).
Compound key: `(ParentNetworkId, LocalChildIndex)`.

Fields mirror `EqsSensor` fields plus the 3 context-slot network IDs (added in Phase 11,
TASK-EQS-035/036 to replace hardcoded `TargetMemory[0]` reads).

### `EqsResultEntry` / `EqsResultTopic`

Direction: Muscle -> Brain. QoS: Reliable/TransientLocal.
`EqsResultEntry` mirrors `EqsResult` without internal padding. `EqsResultTopic` carries
`[DdsManaged] List<EqsResultEntry>` as the ranked result payload, keyed by
`(ParentNetworkId, LocalChildIndex, Epoch)`.

---

## Solver

### `EqsModule` (in `Hrot.SimHost.Modules`)

Drives `EqsSolverSystem` at 10 Hz on a background thread (SoD pattern).

```csharp
public sealed class EqsModule : IEcsModule
{
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);
}
```

On each `Tick`:
1. Lazy-initialises `EqsSolverGlobalState` singleton with `MaxAccurateRaycastsPerSolverTick = 2048`.
2. Resets `AccurateRaysSubmittedThisTick = 0`.
3. Calls `EqsSolverSystem.Execute`.

### `EqsSolverSystem` (in `Hrot.SimHost.Systems`)

Phase 2 time-sliced solver. Runs against a SoD snapshot via `QueryTimeSliced`.

**Wall-clock budget:** `EqsBudgetMs = 4.0 ms` (configurable).

**Per-sensor evaluation flow:**

```
EvaluateSensor(entity):
  1. Resolve compound identity (PartMetadata / NetworkIdentity / local-only).
  2. Read or initialize SensorEvalState.
  3. Detect epoch change or structural hot-reload -> reset SensorEvalState.
  4. Resolve EqsQueryTemplate from IEqsTemplateRegistry (fallback: stub empty event).
  5. Generate candidates (IEqsGenerator).
  6. Run FilterCheap tests (reject by setting EntityId = -1L).
  7. Run FilterExpensive tests.
  8. Top-K reduction if candidate count > MaxCandidates.
  9. Run ScoreCheap tests (additive score accumulation).
  10. Run ScoreExpensive tests (may submit raycasts; enter _AwaitingRaycasts phase).
  11. Sort by Score descending; clamp to TopK = 16.
  12. Apply publish policy (AlwaysPush / TopChanged / ScoreDelta).
  13. Write results to EqsResultPool; emit EqsResultEvent.
```

**Accurate LOS cross-tick polling:**

When an `AccurateLineOfSightTest` needs raycasts, it submits `RaycastRequestEvent`s
(bounded by `EqsSolverGlobalState.MaxAccurateRaycastsPerSolverTick`) and the sensor enters
`EqsEvalPhase._AwaitingRaycasts`. On the next solver tick the system polls the raycast
result ring buffer. Consequence: a fully-accurate LOS query has a minimum latency of ~3
solver ticks (~300 ms at 10 Hz).

**Fallback path:** If `IEqsTemplateRegistry` is absent or the `BlueprintId` is not found,
the solver emits a zero-entry `EqsResultEvent` (Phase 1 stub behaviour).

### `EqsResultUpdateSystem` (in `Hrot.SimHost.Systems`)

Brain-tier system (`SystemPhase.Simulation`). Handles both paths:

- **Path A (Online/DDS):** reads `EqsResultUpdateEvent` (managed) published by
  `EqsResultIngressTranslator`.
- **Path B (Offline/Local):** reads `EqsResultEvent` (unmanaged) published directly
  by `EqsSolverSystem`.

Both paths:
1. Find the observer entity by compound key.
2. Verify `evt.Epoch == sensor.Epoch`; discard silently if stale.
3. Lazy-add `EqsCognitiveBuffer` if absent.
4. Write `Count`, `LastUpdateTick`, `LastUpdateTimeSeconds`, and result entries through
   `GetSpanRW()`.

---

## EqsSensorHandle

```csharp
// Namespace: FDP.Eqs
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct EqsSensorHandle : IEquatable<EqsSensorHandle>
{
    public readonly Entity ChildId;
    public bool IsValid => !ChildId.IsNull;
}
```

Typed wrapper around `Entity` that identifies a child entity carrying an `EqsSensor`
component. Used as a blackboard field so Blueprint variable pickers can filter to "sensor
handles" rather than presenting all Entity variables.

---

## BTree Lifecycle Nodes

Defined in `Hrot.AI.Behaviors.Brains.EqsLifecycleNodes` (assembly `Hrot.AI.Behaviors`).

### Blackboard Parameter Structs

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct EqsParams
{
    public uint   BlueprintId;
    public float  SearchRadius;
    public float  ThreatThreshold;
    public uint   FactionFilter;
    public float  ScoreDeltaThreshold;
    public Entity ContextSlot0;  // Self/Observer
    public Entity ContextSlot1;  // Target
    public Entity ContextSlot2;  // Leader/Squad-mate
}

[StructLayout(LayoutKind.Sequential)]
public struct EqsSpawnParams
{
    public EqsParams      SensorConfig;
    public byte           ChildSlotIndex;   // 0..254; 255 reserved
    public EqsSensorHandle SpawnedHandle;   // output: child entity handle
}
```

### `Action_MaintainEqsSensor`

Persistent action (always returns `Running`). On first tick adds `EqsSensor` with
`Epoch = 1`; on subsequent ticks updates changed fields and increments `Epoch`.
Deactivator removes both `EqsSensor` and `EqsCognitiveBuffer`.

**Typical usage pattern:**

```csharp
Parallel(
    builder.Action<EqsParams>(EqsLifecycleNodes.Action_MaintainEqsSensor),
    builder.Action<EqsParams>(EqsLifecycleNodes.Action_WaitForSensor))
```

### `Action_WaitForSensor`

Polling action. Returns `Running` until `EqsCognitiveBuffer.IsReady` is `true`, then
returns `Success`. Gating behaviors behind this node avoids acting on an empty buffer
before the first solver result arrives.

### `Action_SpawnEqsSensorChild`

Creates a child entity via the deferred command buffer (ECB). The child carries
`PartMetadata` (parent + `localChildIndex`) and `EqsSensor`. Idempotency is ensured:
on re-entry (e.g. BTree restart) it scans for an existing child before spawning.
Returns `Success` once the child is alive. Deactivator destroys the child via ECB.

`localChildIndex` is computed deterministically as `(entity.Index << 8) | ChildSlotIndex`,
giving stable keys across ticks for the same (parent, slot) pair.

---

## Blueprint Node Integration

Blueprint-layer nodes defined in `Hrot.Blueprints.Compiler` (AST types) and implemented
in the Blueprint compiler's code-generation pipeline:

| Node | Description |
|---|---|
| `SpawnEqsSensorNode` | Spawns a child sensor entity. `TemplateAssetId` (Guid) resolves to `BlueprintId` at compile time. |
| `ReadEqsResultNode` | Reads the top (or rank-i) result from the entity's `EqsCognitiveBuffer`. Uses `GetSpanRO()` to avoid the [InlineArray] defensive-copy trap. |
| `ScoreDecisionNode` | Evaluates a `UtilityDecisionDef` asset and writes the winning option ID to an output variable. |
| `ReadRankedResultNode` | Reads rank-i entry from a utility result buffer (0 = top-ranked). |
| `WhenNode` (EqsResult mode) | Reactive trigger. Fires on `EqsTrigger` conditions: `FirstReady`, `TopChanged`, `ScoreCrossed`, `BecomesStale`. Requires `LastUpdateTimeSeconds` for staleness checks. |

Node drawers for the visual editor:
- `SpawnEqsSensorNodeDrawer` / `SpawnEqsSensorNodeSession` (in `Hrot.Blueprints.Editor`)
- `ReadEqsResultNodeDrawer` / `ReadEqsResultNodeSession` (in `Hrot.Blueprints.Editor`)

---

## Hot-Reload

EQS participates in the AI hot-reload pipeline managed by `AiHotReloadCoordinator`:

- **Soft reload** (only `ParamHash` changed): live sensors continue; new parameters
  are picked up on the next tick without disruption.
- **Hard reset** (`StructureHash` changed): the solver detects the hash mismatch in
  `SensorEvalState.CurrentStructureHash`, resets `SensorEvalState`, and begins a fresh
  evaluation on the next tick.

`StructureHash` is computed by `EqsQueryTemplate.ComputeStructureHash()` as a 64-bit
FNV-1a hash over the fully-qualified type names of the generator and all test instances.

---

## Diagnostics and Visualizers

Phase 7 (TASK-EQS-022) added an ImGui inspector panel and a gizmo projector that draws
ranked candidate positions as world-space overlays in the 2D map view. The visualizer
reads `EqsCognitiveBuffer` directly and is registered via the standard `GizmoRegistry`
pattern. Visualizer tests live in `Hrot/Subsystems/Hrot.IG.Tests/Eqs/`.

---

## Starter Template Pack

`FindCoverFromTarget` ships as a hand-authored C# template demonstrating the authoring
pattern. Its `BlueprintId` is the FNV-1a hash of AssetId GUID
`"f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d"` (= `0x7F3A2B1Cu`).

Eight templates are specified in the design's starter pack; `FindCoverFromTarget` is the
one currently implemented.

---

## HideInCover Behavior

`Hrot.AI.Behaviors.Brains.HideInCoverBehavior` is the reference consumer for EQS. It
demonstrates both lifecycle patterns:

- **Inline pattern** (`EqsHideInCoverBlackboard`): sensor lives directly on the parent
  entity; `Action_MaintainEqsSensor` + `Action_WaitForSensor` in a `Parallel` node.
- **Child-entity pattern** (`EqsHideInCoverBlackboard_Child`): sensor lives on a spawned
  child entity; `Action_SpawnEqsSensorChild` as a separate sub-tree resource owner.

---

## Test Infrastructure

| Project | Location | Content |
|---|---|---|
| `Fdp.Toolkits.Tests` | `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/` | 15 unit/component-test files: layout, pool, solver compaction, template generator, purity analyser, cover provider, navmesh, accurate LOS, structure hash |
| `Hrot.ClusterRunner.Integration.Tests` | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/` | 15 integration-test files: round-trip, distributed stale-epoch rejection, Phase 2 full pipeline, score-delta, top-K reduction, path-cost inversion, hot-reload, multi-sensor, multi-template, mid-eval abort, threat threshold, FlagsMeaningful, LastUpdateTimeSeconds, golden terrain fixtures |
| `Hrot.IG.Tests` | `Hrot/Subsystems/Hrot.IG.Tests/Eqs/` | Visualizer tests (`EqsVisualizersTests.cs`) |

Key integration tests (TASK-EQS-023 through TASK-EQS-029):

- **T-RT1** -- offline editor round-trip: `EqsSensor` -> `EqsSolverSystem` ->
  `EqsResultUpdateSystem` -> `EqsCognitiveBuffer`.
- **T-DIS1/T-DIS2** -- distributed Brain/Muscle round-trip; stale epoch rejection.
- **T-TOPK** -- top-K reduction preserves highest-scoring candidates and positional
  sentinels.
- **T-ALI1/2/3** -- raycast budget exhaustion and cross-tick accurate-LOS polling.
- **T-PCI1** -- path cost vs. Euclidean distance inversion.
- **T-STALE** -- stale epoch events discarded across DDS after sensor parameter change.
- **T-ABORT** -- mid-evaluation BTree subtree abort; no leaked sensor components.
- **T-THREAT** -- `TargetMemory` threat-threshold bypassing via `ThreatThreshold` field.

---

## Source Files (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/`)

| File | Key types |
|---|---|
| `EqsComponents.cs` | `EqsResult`, `EqsResultArray`, `EqsCognitiveBuffer`, `EqsPublishPolicy`, `EqsSensor` |
| `EqsEvalState.cs` | `SensorEvalState`, `EqsEvalPhase`, `TopKScoreCache`, `EqsSolverGlobalState` |
| `EqsQueryTemplate.cs` | `EqsTestPhase`, `IEqsGenerator`, `IEqsTest`, `EqsQueryTemplate`, `IEqsTemplateRegistry`, `EqsTemplateAttribute`, `IEqsTemplateBuilder`, `EqsTemplateBuilder` |
| `EqsResultPool.cs` | `EqsResultPool`, `EqsResultEvent` |
| `EqsResultUpdateEvent.cs` | `EqsResultUpdateEvent` (managed DDS-bridged event) |
| `EqsDdsTopics.cs` | `EqsSensorConfigTopic`, `EqsResultEntry`, `EqsResultTopic` (DDS wire types) |
| `EqsSensorHandle.cs` | `EqsSensorHandle` (namespace `FDP.Eqs`) |
| `ICoverProvider.cs` | `ICoverProvider` |
| `CoverPoint.cs` | `CoverPoint` |
| `ManualCoverProvider.cs` | `ManualCoverProvider` |
| `ILosService.cs` | `ILosService`, `BlockedLosService` |
| `INavmeshProvider.cs` | (relocated to `Navigation/INavmeshProvider.cs`) |
| `EntitiesInRadiusGenerator.cs` | `EntitiesInRadiusGenerator` |
| `CoverPointsGenerator.cs` | `CoverPointsGenerator` |
| `NavmeshSamplesGenerator.cs` | `NavmeshSamplesGenerator` |
| `FactionFilterTest.cs` | `FactionFilterTest` |
| `DistanceScoreTest.cs` | `DistanceScoreTest` |
| `CheapLineOfSightTest.cs` | `CheapLineOfSightTest` |
| `AccurateLineOfSightTest.cs` | `AccurateLineOfSightTest` |
| `NavmeshReachableTest.cs` | `NavmeshReachableTest` |
| `PathCostScoreTest.cs` | `PathCostScoreTest` |
| `FindCoverFromTarget.cs` | `FindCoverFromTarget` (starter template) |
| `AreaQueryBatchData.cs` | `AreaQueryBatchData` (legacy area-query types, kept for backward compat) |
| `AreaQueryBatchHelper.cs` | `AreaQueryBatchHelper` |
| `AreaQueryEvents.cs` | Area-query event types |
| `StubNavmeshProvider.cs` | `StubNavmeshProvider` (test/Phase 4 stub) |

Solver and module live in `Hrot.SimHost`:

| File | Location | Key types |
|---|---|---|
| `EqsSolverSystem.cs` | `Hrot/Subsystems/Hrot.SimHost/Systems/` | `EqsSolverSystem` |
| `EqsResultUpdateSystem.cs` | `Hrot/Subsystems/Hrot.SimHost/Systems/` | `EqsResultUpdateSystem` |
| `EqsModule.cs` | `Hrot/Subsystems/Hrot.SimHost/Modules/` | `EqsModule` |

---

## Dependencies

The EQS core types in `Fdp.Toolkits` have no additional project dependencies beyond
`Fdp.Core` and `Fdp.ModuleHost.Abstractions`. The solver and result-update system live
in `Hrot.SimHost` and also depend on `Fdp.Toolkit.Replication` (for `NetworkIdentity`,
`PartMetadata`) and `CycloneDDS.NET` (via the DDS translator layer).
