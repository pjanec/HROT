# Onboarding — EQS v1.3 (Environment Query System)

Welcome to the `eqs-2` workstream. This guide gives a new developer enough context to start
contributing.

---

## What is being built

The existing `AreaQuerySolverSystem` provides a minimal standing area query (target pool
lookups). This workstream upgrades it to a full-featured **Environment Query System (EQS)**
capable of evaluating entity-shaped queries, cover-point positional queries, navmesh-sampled
queries, and accurate-LOS queries against 10 000 concurrently active agents at 10 Hz refresh.

The key design goals are:
- **Zero GC allocation** on the hot path (all solver work uses `stackalloc`, fixed arrays, and
  `NativeArray`).
- **Non-blocking** accurate LOS: the solver submits `RaycastRequestEvent`s and resumes on
  the next tick when results are available (minimum ~3 ticks latency at 10 Hz = ~300 ms).
- **Distributed Brain/Muscle topology**: Brain owns query intent + result consumer;
  Muscle runs the solver via CycloneDDS translators (same pattern as autonomous perception).
- **Template-driven composition**: queries are assembled from composable generator and test
  objects defined in `[EqsTemplate]`-annotated classes; a Roslyn generator registers them
  via FNV-1a hash at compile time.

---

## Planning artifacts

| Artifact | Location |
|---|---|
| Design reference (WHAT and WHY) | [EQS_Design_v1.3_final.md](./EQS_Design_v1.3_final.md) |
| Implementation details + code samples | [IMPLEM_DETAILS.md](./IMPLEM_DETAILS.md) |
| Task specifications | [TASK-DETAIL.md](./TASK-DETAIL.md) |
| Progress checklist | [TASK-TRACKER.md](./TASK-TRACKER.md) |
| Technical debt | [DEBT-TRACKER.md](./DEBT-TRACKER.md) |

---

## Folder layout

New files are placed in the following areas:

| Area | Path | What changes |
|---|---|---|
| EQS component + solver | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/` | All new EQS types: components, events, generators, tests, template registry, solver. The old `AreaQuery*` files remain unchanged. |
| Solver module (Muscle) | `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` | Replace delegation to `AreaQuerySolverSystem` with new `EqsSolverSystem`. |
| DDS translators | `Hrot/Network/NED/SimHost/` | `EqsSensorConfigEgressTranslator.cs`, `EqsSensorConfigIngressTranslator.cs`, `EqsResultEventEgressTranslator.cs`, `EqsResultIngressTranslator.cs`. |
| BTree behavior nodes | `Hrot/Subsystems/Hrot.AI.Behaviors/` | `EqsLifecycleNodes.cs`, `CombatNodes.Action_MoveToOptimalCover`. |
| Integration tests | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/` | EQS integration test classes + mock classes. |
| Component ID catalog | `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Add IDs 207–211 for new EQS components. |
| Roslyn generator | `FDP/Toolkits/Fdp.Toolkits.Analyzers/` | `EqsTemplateGenerator.cs` — `IIncrementalGenerator` for `[EqsTemplate]`. |

---

## Key existing types to understand

**`AreaQuerySolverSystem`** (`Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs`)
Current minimal area-query solver. The new `EqsSolverSystem` is added alongside it; they
coexist. Study the time-sliced pattern and `EntityRepository.QueryTimeSliced` usage here.

**`EqsTargetPool` (component ID 203)** (`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchData.cs`)
Existing result pool for the old area query system. The new `EqsResultPool` (ID 209) is a
separate component with a different ring-buffer structure — do not confuse them.

**`HrotRunnerHarness`** (`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`)
Distributed domain-isolated test harness. Exposes `.Cgf` and `.SimHost` sub-worlds over a real
CycloneDDS bus. DDS domain IDs start at `100`; EQS integration tests must use IDs at `300+`.

**`EditorHarness`** (`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`)
Offline (single-world, no DDS) test harness. Use for all unit-style integration tests that do
not require the distributed topology.

**`SmartEgressUtil`** (`Hrot/Network/NED/`)
Dirty-tracking helper used in all egress translators. Call `ShouldPublish`, then
`MarkPublished` after writing — mirrors the perception translator pattern.

**`SpatialGridData` / `SpatialHashGrid`** (`FDP/Toolkits/Fdp.Toolkits/Spatial/`)
Singleton component holding the spatial hash grid. `EntitiesInRadiusGenerator` reads this via
`repo.GetSingletonUnmanaged<SpatialGridData>()`.

**`TargetMemory`** (Hrot CGF subsystem)
Tracks threats sorted in descending `ThreatScore` order. Index 0 is always the highest-scoring
threat. `CheapLineOfSightTest` reads `ThreatScores[0]` for the threshold comparison.

**`[BTreeDeactivator]`** (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/`)
Implemented in `ai-btree-deactivator-1` workstream. Used by `Deactivate_MaintainEqsSensor`.
The 3-param bridge form requires the `@0` suffix in the `TargetAction` string.

**`GlobalComponentIds`** (`FDP/Engine/Fdp.Core/GlobalComponentIds.cs`)
Central ID catalog. The last assigned IDs are 203–206. New EQS component IDs go in
207–211 (reservation range 207–255 for toolkit/zone components).

---

## Critical implementation constraints

1. **`[InlineArray]` defensive-copy trap** (Design §8.1 / `IMPLEM_DETAILS.md` L:45–100):
   `EqsCognitiveBuffer` uses `[InlineArray(16)]` for the top-K results. Writing through a
   plain index assignment (`buffer.Results[0] = x`) emits a `ldobj` defensive copy that
   silently discards the write. Always use `buffer.GetSpanRW()` which casts via
   `MemoryMarshal.CreateSpan(ref Unsafe.As<EqsResultArray, EqsResult>(ref Results), 16)`.

2. **Rejection sentinel is `-1L`, not `0`** (`IMPLEM_DETAILS.md` L:1518–1530):
   Entity candidates use the entity's packed value as `EntityId`. Positional candidates
   (cover points, navmesh samples) use `EntityId = 0` — this is a *valid* placeholder.
   Rejected candidates must be marked `EntityId = -1L`. The `ReduceTopK` compaction checks
   `!= -1L` to preserve both entity and positional survivors.

3. **Epoch check in `EqsResultUpdateSystem`** (`IMPLEM_DETAILS.md` L:2403–2415):
   The staleness check must compare `evt.Epoch != sensor.Epoch` (version counter vs.
   version counter). Do NOT compare `evt.Epoch < buffer.LastUpdateTick` (epoch vs. tick).

4. **Non-blocking accurate LOS** (`IMPLEM_DETAILS.md` L:1810–2010):
   `AccurateLineOfSightTest` submits `RaycastRequestEvent`s and sets
   `SensorEvalState.Phase = _AwaitingRaycasts`. On the next solver tick the system polls
   `RaycastBatchData` for resolved hits. If not all resolved: return immediately, do NOT
   block. Minimum realistic latency: 3 solver ticks (~300 ms at 10 Hz).

5. **`EqsResultUpdateSystem` epoch staleness fix** (`IMPLEM_DETAILS.md` L:2403):
   Original prototype used `buffer.LastUpdateTick` for comparison — this is wrong. The
   correct comparison uses the live `sensor.Epoch` field on the entity component.

---

## Build and run

```
# Full solution build
dotnet build IOS-IG-SimHost.sln

# FDP unit tests only
dotnet test FDP\FDP.sln

# Hrot integration tests (requires no DDS firewall block)
dotnet test Hrot\Runner\Hrot.ClusterRunner.Integration.Tests\
```

---

## Developer workflow

See [docs/AI_DEV_GUIDE.md](../../docs/AI_DEV_GUIDE.md) for the batch-based development
workflow, review process, and commit conventions used in this project.
