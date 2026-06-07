# TASK-DETAIL — EQS v1.3

Detailed per-task specifications for implementing the Environment Query System as described in
[EQS_Design_v1.3_final.md](./EQS_Design_v1.3_final.md) and expanded in
[IMPLEM_DETAILS.md](./IMPLEM_DETAILS.md).

**DESIGN reference shorthand used throughout this file:**
- "Design §N" = section N of [EQS_Design_v1.3_final.md](./EQS_Design_v1.3_final.md)
- "Impl L:N" = line N of [IMPLEM_DETAILS.md](./IMPLEM_DETAILS.md)

---

## Phase 1: Foundations

**Goal:** Establish all core data types, DDS wire protocol, stubbed solver, and BTree integration
to prove the full Brain-to-Muscle-and-back round-trip over CycloneDDS before any real evaluation
logic is written.

---

### TASK-EQS-001 — Core component layouts

**Design reference:** Design §2, §8, §8.1; Impl L:3–170

**Scope (IN):**
- `EqsResult` struct (24-byte, StructLayout.Sequential) — `EntityId`, `PositionX`, `PositionY`,
  `Score`, `Flags`, `_pad` as shown in Impl L:16–31
- `EqsResultArray` — C# 12 `[InlineArray(16)]` wrapper over `EqsResult` (Impl L:33–40)
- `EqsCognitiveBuffer` component — `Count`, `LastUpdateTick`, `EqsResultArray Results`, plus
  `GetSpanRW()` / `GetSpanRO()` / `IsReady` / `GetTop()` accessors using
  `MemoryMarshal.CreateSpan` / `CreateReadOnlySpan` to bypass the `ldobj` defensive-copy trap
  (Impl L:45–100; Design §8.1)
- `EqsSensor` component — `BlueprintId`, `Epoch`, `SearchRadius`, `FactionFilter`,
  `ThreatThreshold`, `PublishPolicy`, `Priority` (Impl L:103–135)
- Register component IDs `EqsSensor`, `EqsCognitiveBuffer` as the next available slots after
  `BlueprintBlackboard16384 = 206` in `GlobalComponentIds.cs`

**Scope (OUT):** No solver logic, no DDS translators, no test for these in isolation.

**Constraints:**
- `EqsResult` must be exactly 24 bytes (verify with `Marshal.SizeOf`).
- `EqsCognitiveBuffer.GetSpanRW()` must use `MemoryMarshal.CreateSpan(ref Unsafe.As<...>(...), 16)` — direct `[InlineArray]` indexing assignment is forbidden (Design §8.1).
- Component IDs must be added to `GlobalComponentIds.cs` before any component struct references them (see existing pattern: `EqsTargetPool = 203`).
- `EqsSensor` and `EqsCognitiveBuffer` live in `Fdp.Toolkit.Spatial.Eqs` namespace.

**Success conditions:**
1. `Marshal.SizeOf<EqsResult>()` returns `24` in a unit test.
2. A unit test calls `GetSpanRW()`, assigns to `span[0]`, re-reads via `GetSpanRO()`, and asserts the value was retained (proves the span-cast path bypasses the defensive-copy).
3. `GlobalComponentIds.EqsSensor` and `GlobalComponentIds.EqsCognitiveBuffer` constants exist and are unique in the range 207–255.
4. `dotnet build FDP\FDP.sln` succeeds without errors.

---

### TASK-EQS-002 — EqsResultPool singleton and EqsResultEvent

**Design reference:** Design §3.1 (Muscle→Brain results); Impl L:185–285

**Scope (IN):**
- `EqsResultPool` singleton component — `MaxConcurrentInFlightResults = 1024`, `MaxTopK = 16`,
  `PoolCapacity = 16384`, `NextFreeIndex`, `NativeArray<EqsResult> Results` (Impl L:200–228)
- `EqsResultEvent` unmanaged struct — `SensorNetworkId`, `Epoch`, `RefreshTick`, `ResultHandle`,
  `EntryCount` (Impl L:230–255); decorated with `[EventId(N)]` using the next available event ID
- Ring-buffer write-and-wrap logic as shown in Impl L:257–285
- Register `EqsResultPool` component ID in `GlobalComponentIds.cs`

**Scope (OUT):** No DDS topics, no translators.

**Constraints:**
- `EqsResultEvent` must satisfy `where T : unmanaged` — no reference types permitted.
- Pool wrap logic: `if (handle + count > PoolCapacity) handle = 0` before bulk-copy.
- The egress translator (TASK-EQS-004) consumes these events in the same frame; pool acts as a
  same-frame staging area.

**Success conditions:**
1. `EqsResultEvent` passes `Unsafe.SizeOf<EqsResultEvent>() > 0` and all fields are value types.
2. Unit test: write 3 results starting at `NextFreeIndex = 16382`, assert `NextFreeIndex` wraps to
   `3` (not `16385`), and the first result is at index `0`.
3. `dotnet build` succeeds.

---

### TASK-EQS-003 — DDS wire topics and translator contracts

**Design reference:** Design §3.1; Impl L:305–460

**Scope (IN):**
- `EqsSensorConfigTopic` DDS struct (`[DdsTopic("EqsSensorConfig")]`, `[DdsKey] EntityId`, all
  sensor fields — see Impl L:315–335)
- `EqsResultEntry` DDS struct (Impl L:337–345)
- `EqsResultTopic` DDS struct (`[DdsTopic("EqsResult")]`, `[DdsManaged] List<EqsResultEntry>`
  — Impl L:346–358)
- Translator class stubs (compile-only, no logic yet):
  - `EqsSensorConfigEgressTranslator` (Brain-side, implements `IDescriptorTranslator`)
  - `EqsSensorConfigIngressTranslator` (Muscle-side)
  - `EqsResultEventEgressTranslator` (Muscle-side)
  - `EqsResultIngressTranslator` (Brain-side)

**Scope (OUT):** Full translator logic (TASK-EQS-007), QoS configuration at DDS layer.

**Constraints:**
- `EqsSensorConfigTopic` uses `Reliability = Reliable, Durability = TransientLocal,
  HistoryKind = KeepLast, HistoryDepth = 1` (Design §3.1).
- `EqsResultTopic` same QoS; `List<EqsResultEntry>` is the managed payload — only the egress
  translator side creates these lists, never the ECS event bus side.
- Topic names must be exactly `"EqsSensorConfig"` and `"EqsResult"` (wire stability).
- `DescriptorOrdinal` values must be unique within the NED descriptor type registry; confirm
  with the NED descriptor type list before reserving.

**Success conditions:**
1. All four translator stubs compile without error against `IDescriptorTranslator` interface.
2. `EqsSensorConfigTopic` and `EqsResultTopic` compile with their `[DdsTopic]`/`[DdsKey]`/
   `[DdsManaged]` attributes without errors.
3. `dotnet build IOS-IG-SimHost.sln` succeeds.

---

### TASK-EQS-004 — EqsResultUpdateSystem (Brain side)

**Design reference:** Design §3.1 step 6–7; Impl L:595–680

**Scope (IN):**
- `EqsResultUpdateEvent` managed event class — `Observer`, `Epoch`, `RefreshTick`,
  `List<EqsResultEntry> Results`
- `EqsResultUpdateSystem` (`[UpdateInPhase(SystemPhase.Simulation)]`):
  - Reads `EqsResultUpdateEvent` from managed bus
  - Guards: entity must be alive and have `EqsSensor` component
  - Epoch staleness check: `if (evt.Epoch != sensor.Epoch) continue;` (Design §3.1 — epoch
    mismatch discards the payload; **not** a tick comparison — see bug correction in Impl
    L:2403–2415)
  - Lazy-adds `EqsCognitiveBuffer` if missing
  - Writes results using `GetSpanRW()` to avoid the `[InlineArray]` trap (Impl L:640–665)

**Scope (OUT):** DDS ingress translator (TASK-EQS-007), offline direct-bus path (handled by
same system transparently).

**Constraints:**
- Staleness check is `evt.Epoch != sensor.Epoch`, NOT `evt.Epoch < buffer.LastUpdateTick`
  (the latter compares sensor version against simulation tick — wrong).
- System must tolerate the entity having no `EqsSensor` (sensor removed mid-flight); skip
  those events silently.
- `GetSpanRW()` must be used for all writes — see Design §8.1 and Impl L:121–133.

**Success conditions:**
1. Unit test: create entity with `EqsSensor { Epoch=2 }`, publish `EqsResultUpdateEvent` with
   `Epoch=1` (stale), pump system — assert `EqsCognitiveBuffer` is NOT created.
2. Unit test: same entity, publish event with `Epoch=2`, pump — assert `EqsCognitiveBuffer`
   has `Count > 0` and `IsReady == true`, and first result X/Y matches the published entry.
3. Unit test: verify that writing to the buffer via `GetSpanRW()` persists (not silently
   discarded by defensive copy).

---

### TASK-EQS-005 — Stubbed EqsSolverSystem (Phase 1 stub)

**Design reference:** Design §14 Phase 1; Impl L:543–680

**Scope (IN):**
- `EqsSolverSystem` (Phase 1 stub) — queries `EqsSensor + NetworkIdentity`, emits
  `EqsResultEvent` with `EntryCount=0` every tick (Impl L:546–600)
- `EqsModule` (`IEcsModule`, `SlowBackground(10)`) wrapping the solver (Impl L:600–630)
- Wire `EqsModule` into `SimHostCoreLogicPack` (it already exists as a stub in
  `Hrot.SimHost.Modules.EqsModule` but currently delegates to `AreaQuerySolverSystem` —
  replace the delegation with the new `EqsSolverSystem`)
- Wire `EqsResultPool` singleton initialization in `SimHostCoreLogicPack`

**Scope (OUT):** Real multi-phase evaluation (Phase 2+), time-slicing.

**Constraints:**
- The stub must emit a valid `EqsResultEvent` (not throw or silently skip) so the DDS
  round-trip test passes with `EntryCount=0`.
- `EqsModule` uses `ExecutionPolicy.SlowBackground(10)` — do not use `Default` policy.
- The existing `EqsModule` in `Hrot.SimHost.Modules` currently calls
  `AreaQuerySolverSystem`. Replace it without breaking `AreaQuerySolverSystem`; the area
  query solver belongs in `CognitiveSpatialModule` (it already is), so the `EqsModule` no
  longer needs it.

**Success conditions:**
1. Integration test (EditorHarness): spawn entity, attach `EqsSensor`, pump 3 solver ticks
   (300 ms simulated), assert `EqsCognitiveBuffer.IsReady == true` and `Count == 0` (stub
   emits empty result).
2. No pre-existing tests regress (area query tests still pass).
3. Build succeeds.

---

### TASK-EQS-006 — BTree lifecycle nodes (WaitForSensor + MaintainEqsSensor)

**Design reference:** Design §8 (reader API), §14 Phase 1; Impl L:685–810, L:3390–3550

**Scope (IN):**
- `EqsParams` unmanaged struct — `BlueprintId`, `SearchRadius`, `ThreatThreshold`,
  `FactionFilter` (Impl L:3395–3408)
- `EqsLifecycleNodes` static class:
  - `Action_MaintainEqsSensor` — adds/updates `EqsSensor` component, increments `Epoch` on
    parameter change, returns `NodeStatus.Running` indefinitely (Impl L:3412–3447)
  - `Deactivate_MaintainEqsSensor` — companion `[BTreeDeactivator]` that removes `EqsSensor`
    and `EqsCognitiveBuffer` on exit (Impl L:3448–3465)
  - `Action_WaitForSensor` — returns `Running` until `EqsCognitiveBuffer.IsReady == true`
    (Impl L:3470–3495)
- Canonical `Parallel`+`Sequence` composition shown in Impl L:3498–3525

**Scope (OUT):** `Action_MoveToOptimalCover` (separate task), full `HideInCover_BT`
definition.

**Constraints:**
- `[BTreeDeactivator]` `TargetAction` string must use the `@0` compound-key suffix convention
  for 3-param bridge actions (see DEBT-TRACKER D-03 in `ai-btree-deactivator-1`).
- `Action_MaintainEqsSensor` must call `sensor.Epoch++` only when a parameter actually
  changed — not on every tick — to avoid constant solver resets.
- Deactivator must remove both `EqsSensor` and `EqsCognitiveBuffer` to prevent stale reads on
  re-activation (Design §11 lifecycle rules).

**Success conditions:**
1. Unit test (EditorHarness, offline): tree with `Parallel(MaintainEqsSensor, WaitForSensor)`,
   pump until `WaitForSensor` returns `Success` (requires TASK-EQS-005 stub to emit empty
   result).
2. Unit test: force branch abort (set `BehaviorTreeState.RunningNodeIndex` to a different
   leaf), pump one tick — assert `EqsSensor` component no longer exists on entity.
3. Unit test: mutate `EqsParams.SearchRadius`, re-tick `Action_MaintainEqsSensor` — assert
   `EqsSensor.Epoch` incremented exactly once.

---

## Phase 2: Entity-Shaped Queries with Cheap Tests

**Goal:** Replace the Phase 1 stub with a real time-sliced multi-phase evaluation loop capable
of evaluating entity-shaped queries using `EntitiesInRadius` generator and `Faction` + `Distance`
tests.

---

### TASK-EQS-007 — Full DDS translator implementations

**Design reference:** Design §3.1; Impl L:305–460, L:2960–3390

**Scope (IN):** Complete implementation of all four translators stub-created in TASK-EQS-003:
- `EqsSensorConfigEgressTranslator.ScanAndPublish` — uses `SmartEgressUtil` dirty-tracking,
  writes `EqsSensorConfigTopic` (Impl L:3030–3075)
- `EqsSensorConfigIngressTranslator.PollIngress` — applies `EqsSensor` component to ghost
  entity, handles `NotAliveDisposed` to remove component (Impl L:3080–3140)
- `EqsResultEventEgressTranslator.ScanAndPublish` — reads `EqsResultEvent`, dereferences
  `EqsResultPool`, builds `List<EqsResultEntry>`, writes `EqsResultTopic` (Impl L:3145–3215);
  translates local `EntityId` to `NetworkId` for entity-shaped results
- `EqsResultIngressTranslator.PollIngress` — reads `EqsResultTopic`, maps `SensorNetworkId`
  back to local entity, bridges to `EqsResultUpdateEvent` on Brain bus (Impl L:3220–3290);
  maps `TargetNetworkId` back to local entity `PackedValue`
- Register all four translators in `SimHostAuxiliaryTranslatorPack`

**Scope (OUT):** Offline/editor path (handled without translators by design).

**Constraints:**
- Egress translator creates `List<EqsResultEntry>` only during translation — never on the ECS
  event bus side (Design §3.1, "unmanaged-to-managed transition cleanly at the translator").
- Ingress translator uses `_bus.PublishManaged(...)` (not `cmd.PublishEvent`) because
  `EqsResultUpdateEvent` is managed (Design §3.1 step 5).
- `DescriptorOrdinal` values must match between egress and ingress translators of the same pair.
- `SmartEgressUtil` dirty-tracking: call `ShouldPublish`, then after writing call
  `MarkPublished` (mirror the perception translator pattern).

**Success conditions:**
1. Integration test (HrotRunnerHarness, "simhost,cgf"): Brain attaches `EqsSensor`, pump — assert
   Muscle ghost entity gains `EqsSensor` component within 10 s.
2. Integration test: Muscle stub emits `EqsResultEvent` (empty), pump — assert Brain entity gains
   `EqsCognitiveBuffer` with `IsReady == true` within 10 s.
3. Integration test: Brain removes `EqsSensor` — assert Muscle ghost entity loses `EqsSensor`
   within 10 s (`NotAliveDisposed` path).
4. All pre-existing translator tests pass.

---

### TASK-EQS-008 — Core interfaces: IEqsGenerator, IEqsTest, EqsQueryTemplate

**Design reference:** Design §5; Impl L:815–880

**Scope (IN):**
- `EqsTestPhase` enum — `FilterCheap=0`, `FilterExpensive=1`, `ScoreCheap=2`,
  `ScoreExpensive=3` (Impl L:815–830)
- `IEqsGenerator` interface — `int Generate(Entity observer, ref EqsSensor sensor,
  ISimulationView view, Span<EqsResult> candidates)` (Impl L:840–850)
- `IEqsTest` interface — `EqsTestPhase Phase { get; }` + `void ExecuteBatch(Entity observer,
  ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)` (Impl L:851–865)
- `EqsQueryTemplate` struct — `BlueprintId`, `IEqsGenerator Generator`, four `IEqsTest[]`
  arrays keyed by phase, `MaxCandidates` (Impl L:867–885)
- `IEqsTemplateRegistry` interface — `bool TryGetTemplate(uint blueprintId, out
  EqsQueryTemplate)` (Impl L:1140–1145)
- `[EqsTemplate(AssetId="...")]` attribute declaration (Impl L:1610–1615)
- `EqsTemplateBase` abstract base (Impl L:885–900)

**Scope (OUT):** Roslyn source generator (TASK-EQS-014), concrete templates (TASK-EQS-015),
runtime registry implementation.

**Constraints:**
- `IEqsGenerator.Generate` must operate on a `Span<EqsResult>` — no `List<T>` allocation.
- `EqsQueryTemplate` is a struct (not a class) to allow stack allocation in the solver.
- Template `Build()` must be static and pure (Design §5.6) — enforced in Phase 6 by the
  analyzer.

**Success conditions:**
1. Compile-time: all interfaces and struct compile without errors.
2. A trivial unit-test generator that hardcodes 3 entries into a `Span<EqsResult>` and a
   trivial filter test that zeroes out index 1 can be composed into an `EqsQueryTemplate`
   and called without allocation.

---

### TASK-EQS-009 — EntitiesInRadius generator

**Design reference:** Design §5.5 (`EntitiesInRadius`); Impl L:900–970

**Scope (IN):**
- `EntitiesInRadiusGenerator` — queries `SpatialGridData` singleton (existing
  `SpatialHashGrid`), stacks allocates intermediate buffer, packs results into
  `Span<EqsResult>` with `EntityId = (long)entity.PackedValue`, excludes observer self,
  caps at `candidates.Length` (Impl L:905–965)

**Scope (OUT):** Any other generator kind.

**Constraints:**
- Must use `stackalloc` for intermediate `(Entity, Vector2)` buffer — no heap allocation.
- Observer entity must be excluded from results.
- Returns the valid count, not `candidates.Length` (generator may find fewer than capacity).
- Read `SpatialGridData` via `repo.GetSingletonUnmanaged<SpatialGridData>()` (same pattern as
  `AreaQuerySolverSystem`).

**Success conditions:**
1. Unit test: world with 5 entities at known positions, 1 observer at origin, radius 10.
   Assert generator returns exactly the 5 entities within radius (excluding self).
2. Unit test: observer at origin, radius 0 — assert returns 0.
3. No heap allocations visible in profiler (use `Assert.Equal(0, GC.CollectionCount(0))` pattern
   after generator call in a tight loop).

---

### TASK-EQS-010 — FactionFilterTest and DistanceScoreTest

**Design reference:** Design §5.4; Impl L:975–1100

**Scope (IN):**
- `FactionFilterTest` (`FilterCheap`) — reads `EntityInfo.ForceId`, matches against
  `sensor.FactionFilter` bitmask, marks rejected candidates with `EntityId = -1L`
  (Impl L:975–1025)
- `DistanceScoreTest` (`ScoreCheap`) — reads already-packed `PositionX/Y` from `EqsResult`
  (no ECS lookup needed), applies linear falloff `score = 1 - clamp(dist / maxDist, 0, 1)`,
  accumulates into `candidate.Score` (Impl L:1030–1080)

**Scope (OUT):** LOS tests, cover tests, navmesh tests.

**Constraints:**
- Rejection sentinel is `EntityId = -1L` (NOT `0`) because `0` is reserved for valid
  positional candidates (Design §5; bug correction in Impl L:1518–1530).
- `DistanceScoreTest` must skip candidates with `EntityId == -1L`.
- Both tests must operate on the span in-place without any heap allocation.

**Success conditions:**
1. Unit test: 4 entity candidates, faction mask excludes 2 — after `FactionFilterTest` those 2
   have `EntityId == -1L`, the other 2 retain their original IDs.
2. Unit test: 2 surviving candidates at distances 5 and 10 with `SearchRadius=20` — after
   `DistanceScoreTest`, closer candidate has higher `Score`.
3. Verify `DistanceScoreTest` does not modify candidates where `EntityId == -1L`.

---

### TASK-EQS-011 — Time-sliced EqsSolverSystem (Phase 2 full)

**Design reference:** Design §7; Impl L:1105–1380

**Scope (IN):**
- Replace Phase 1 stub in `EqsSolverSystem` with full multi-phase solver:
  - `_sensorQuery` built once with `With<EqsSensor>().With<NetworkIdentity>()`
  - `QueryTimeSliced` with `IteratorState`, `EqsBudgetMs=4.0`, `TimeSliceMetric.WallClockTime`
    (Impl L:1155–1185; Design §7.6)
  - `EvaluateSensor` private method: Generation → FilterCheap → FilterExpensive → `ReduceTopK`
    → ScoreCheap → ScoreExpensive → sort → `WriteResultsToPoolAndPublish` (Impl L:1190–1290)
  - `ReduceTopK` — compacts span by removing `-1L` entries, pre-sorts if over `MaxTopK`,
    returns truncated span (Impl L:1295–1320)
  - `WriteResultsToPoolAndPublish` — ring-buffer write + emit `EqsResultEvent`
    (Impl L:1325–1365)
- Store `EqsBudgetMs` as public property (default 4.0)

**Scope (OUT):** `SensorEvalState` cross-tick persistence (Phase 5), priority bands (§7.5
deferred to Phase 2 follow-up), `EpochSnapshot` check against `SensorEvalState` (Phase 5).

**Constraints:**
- `stackalloc EqsResult[template.MaxCandidates]` inside `EvaluateSensor` — no heap allocation.
- Pre-allocate `_currentCmd`, `_currentTick`, `_currentView` fields to avoid lambda closure
  allocation when passing `EvaluateSensor` to `QueryTimeSliced` (Impl L:1150–1165).
- `ReduceTopK` checks `EntityId != -1L` (not `!= 0`) to preserve valid positional candidates.

**Success conditions:**
1. Integration test (EditorHarness): spawn observer + 5 enemy entities, attach `EqsSensor`
   with `BlueprintId` mapped to a test template (`EntitiesInRadius` + `FactionFilter` +
   `DistanceScore`), pump solver ticks — assert `EqsCognitiveBuffer.Count > 0` and top result
   has enemy entity ID.
2. Integration test: budget set to 0.001 ms — assert solver does NOT evaluate all 5 entities in
   one tick (confirms time-slicing yields).
3. Confirm `ReduceTopK` does not destroy positional candidates (`EntityId=0`) when mixed with
   rejected entity candidates (`EntityId=-1L`).

---

## Phase 3: Positional Queries with Cheap LOS

**Goal:** Add cover-point positional queries, cheap LOS filtering, and the first starter-pack
template (`FindCoverFromTarget`).

---

### TASK-EQS-012 — ICoverProvider interface and CoverPoint struct

**Design reference:** Design §10.3; Impl L:1400–1460

**Scope (IN):**
- `CoverPoint` struct (24-byte, StructLayout.Sequential) — `PositionX/Y`, `DirectionX/Y`,
  `Quality`, `StanceHeight`, padding (Impl L:1405–1435)
- `ICoverProvider` interface — `int GetCoverPointsInRadius(Vector2 center, float radius,
  Span<CoverPoint> results)` (Impl L:1437–1450)
- `ManualCoverProvider` concrete class (Stride3D stage) — stores a hardcoded or
  designer-placed list of `CoverPoint`s, implements the interface using a simple radius loop

**Scope (OUT):** Auto-computed cover from navmesh (final stage).

**Constraints:**
- `CoverPoint` must be unmanaged (no reference fields) so generators can use `stackalloc`.
- `ManualCoverProvider` stores points as a plain array; no spatial index needed at this stage
  (designer-placed maps are small).

**Success conditions:**
1. `Marshal.SizeOf<CoverPoint>()` == 24 in a unit test.
2. Unit test: `ManualCoverProvider` with 3 known points — query with center at origin,
   radius 20 returns the 2 points within radius.

---

### TASK-EQS-013 — CoverPointsGenerator, ILosService, CheapLineOfSightTest

**Design reference:** Design §5.5 (`CoverPoints`), §9.1; Impl L:1460–1600

**Scope (IN):**
- `CoverPointsGenerator` — queries `ICoverProvider` singleton, stacks allocates `CoverPoint[]`,
  sets `EntityId=0` for all positional results (Impl L:1464–1530)
- `ILosService` interface — `bool HasCheapLineOfSight(Vector2 observer, Vector2 target)`
  (Design §10.4); stubbed implementation (always returns `false` = LOS blocked = cover valid)
  for this stage since occluder grid is not yet built
- `CheapLineOfSightTest` (`FilterCheap`) — reads `TargetMemory` for primary threat (index 0),
  bypasses if `ThreatScores[0] < sensor.ThreatThreshold`, calls `HasCheapLineOfSight(candidate,
  threat)` — if clear (exposed), marks `EntityId = -1L`; if blocked (cover), sets flag bit 0
  (Impl L:1545–1595)

**Scope (OUT):** Accurate LOS (Phase 5), fully implemented occluder grid.

**Constraints:**
- `CoverPointsGenerator` uses `stackalloc CoverPoint[candidates.Length]` — no heap allocation.
- Rejection sentinel for positional queries: `EntityId = -1L`, NOT `0` (same correction as
  TASK-EQS-010 — see Impl L:1518–1530).
- `CheapLineOfSightTest` must gracefully bypass if `TargetMemory.Count == 0` or threat score
  is below threshold (Design §11; Impl L:1565–1575 guard clause).
- `TargetMemory.ThreatScores[0]` accesses the primary (highest) threat score as the threshold
  comparison (Impl L:2736 bug note).

**Success conditions:**
1. Unit test: `CoverPointsGenerator` with mock `ICoverProvider` returning 2 points — assert
   both have `EntityId=0` in the result span.
2. Unit test: `CheapLineOfSightTest` with `MockLosService` (always exposed = clear LOS), threat
   score = 100, threshold = 50 — assert all candidates marked `-1L`.
3. Unit test: same setup but threat score = 10 (below threshold 50) — assert all candidates
   pass through unmodified (bypass path).

---

### TASK-EQS-015 — FindCoverFromTarget starter template

**Design reference:** Design §6.5, §6.6 (template #4); Impl L:1603–1680

**Scope (IN):**
- `FindCoverFromTarget` class with `[EqsTemplate(AssetId="...")]` and static `Build()` method
  composing `CoverPointsGenerator` + `CheapLineOfSightTest` + `DistanceScoreTest`
  (Impl L:1610–1640)
- Manual registration in `EqsTemplateRegistry` (until Roslyn generator in Phase 6)
  using a hardcoded `BlueprintId` constant

**Scope (OUT):** Remaining 7 starter-pack templates (deferred — see Design §6.6).

**Constraints:**
- `Build()` must be `static` and must not read runtime state (Design §5.6).
- `AssetId` GUID must be unique — generate with a GUID tool; do not reuse any existing GUID.

**Success conditions:**
1. Integration test (EditorHarness): spawn observer with `TargetMemory` containing one threat at
   position (20, 0), inject `MockCoverProvider` with 3 cover points (one exposed to the threat,
   two occluded), inject `ExposedAtPosition(20,0)` `MockLosService`, attach `EqsSensor`
   mapped to `FindCoverFromTarget`, pump solver ticks — assert `EqsCognitiveBuffer` contains
   exactly 2 results (the occluded ones) and both have `EntityId=0`.
2. Top-ranked result has higher score than second (closer to observer).

---

## Phase 4: Navmesh Integration via DotRecast

**Goal:** Wire `INavmeshProvider`, `NavmeshReachable` filter, and `PathCost` scorer; add
`NavmeshSamplesGenerator`.

---

### TASK-EQS-016 — INavmeshProvider interface

**Design reference:** Design §10.2; Impl L:1685–1720

**Scope (IN):**
- `INavmeshProvider` interface — `bool IsReachable(Vector2, Vector2)`, `bool
  TryGetPathDistance(Vector2, Vector2, out float)`, `int GetRandomPointsInRadius(Vector2,
  float, Span<Vector2>)` (Impl L:1690–1715)
- `StubNavmeshProvider` concrete class — all points reachable, path distance = Euclidean
  distance (for Stride3D stage until DotRecast is integrated)

**Scope (OUT):** DotRecast integration (separate workstream).

**Success conditions:**
1. `StubNavmeshProvider.IsReachable` always returns `true`.
2. `TryGetPathDistance` returns Euclidean distance.
3. Compiles without error.

---

### TASK-EQS-017 — NavmeshSamplesGenerator, NavmeshReachableTest, PathCostScoreTest

**Design reference:** Design §5.5 (`NavmeshSamples`), §5.4; Impl L:1720–1870

**Scope (IN):**
- `NavmeshSamplesGenerator` — queries `INavmeshProvider.GetRandomPointsInRadius`, packs into
  `EqsResult` with `EntityId=0` (Impl L:1725–1775)
- `NavmeshReachableTest` (`FilterExpensive`) — calls `INavmeshProvider.IsReachable`, marks
  reachable candidates with flag bit 3, marks unreachable with `-1L` (Impl L:1780–1825)
- `PathCostScoreTest` (`ScoreExpensive`) — calls `TryGetPathDistance`, applies inverse-linear
  falloff, marks candidate `-1L` if no path exists (Impl L:1830–1870)

**Constraints:**
- All three use `stackalloc` or existing candidate span — no heap allocation.
- `NavmeshReachableTest` must skip candidates already marked `-1L`.

**Success conditions:**
1. Integration test (path-cost inversion scenario, EditorHarness):
   - Observer at origin; three targets: A at (0,5) euclidean=5 path=50, B at (0,10) euclidean=10
     path=10, C at (0,2) unreachable.
   - Template: `EntitiesInRadiusGenerator` + `DistanceScoreTest` (ScoreCheap) +
     `NavmeshReachableTest` (FilterExpensive) + `PathCostScoreTest` (ScoreExpensive).
   - Assert: buffer has exactly 2 entries (C rejected).
   - Assert: B is ranked #1 (B total score > A total score despite A being closer).
   - See Impl L:2385–2410 for expected score math.

---

## Phase 5: Accurate LOS and the State Machine

**Goal:** Implement cross-tick raycast polling, `_AwaitingRaycasts` phases, `SensorEvalState`
persistence, and raycast cap enforcement.

---

### TASK-EQS-018 — SensorEvalState component and EqsSolverGlobalState singleton

**Design reference:** Design §7.4; Impl L:1880–1960

**Scope (IN):**
- `EqsEvalPhase` enum — `Idle`, `Evaluating`, `_AwaitingRaycasts`, `Finalizing`
  (Impl L:1883–1893)
- `SensorEvalState` component — `Phase`, `PendingRaycastCount`, `AwaitingSinceTick`,
  `CurrentStructureHash` (for TASK-EQS-021 hot-reload; Impl L:1895–1918)
- `EqsSolverGlobalState` singleton — `MaxAccurateRaycastsPerSolverTick` (default 2048),
  `AccurateRaysSubmittedThisTick` (Impl L:1955–1970)
- Register `SensorEvalState` component ID in `GlobalComponentIds.cs`

**Success conditions:**
1. `SensorEvalState` compiles as unmanaged struct.
2. `EqsSolverGlobalState.AccurateRaysSubmittedThisTick` can be set/read correctly in a unit
   test.

---

### TASK-EQS-019 — AccurateLineOfSightTest and cross-tick polling in EqsSolverSystem

**Design reference:** Design §7.4, §9.2; Impl L:1965–2100

**Scope (IN):**
- `AccurateLineOfSightTest` (`ScoreExpensive`) — reads `TargetMemory`, submits
  `RaycastRequestEvent` events via `cmd.PublishEvent`, marks candidates with
  `FlagPendingRay = 1 << 15`, respects `MaxAccurateRaycastsPerSolverTick` cap
  (Impl L:1970–2045)
- Cross-tick polling state machine in `EqsSolverSystem.EvaluateSensor` — checks
  `SensorEvalState.Phase == _AwaitingRaycasts`, polls `RaycastBatchData` ring buffer
  by `RayId`, resolves hits: clear LOS → set flag bit 0, blocked → mark `-1L`; if not
  all resolved, `return` early (yield) (Impl L:2050–2105)
- `EpochSnapshot` check: reset `SensorEvalState` when `sensor.Epoch` differs from
  snapshot stored at evaluation start

**Scope (OUT):** Priority bands (§7.5), `QueryTimeSliced` continuation per-sensor (the
time-sliced enumerator handles sensor-level interruption; within-sensor candidate-level
continuation is deferred to debt).

**Constraints:**
- Solver must NEVER block: when `!allResolved`, return immediately (Design §7.4 rule).
- `RayId` encoding: `((long)entity.Index << 32) | (uint)candidateIndex` (Impl L:2010).
- `AccurateRaysSubmittedThisTick` must be reset to 0 at the start of each `EqsModule.Tick`.
- Minimum accurate-LOS query latency: 3 solver ticks (~300 ms at 10 Hz) — test must verify
  this, not try to get results faster.

**Success conditions:**
1. Integration test (EditorHarness with `MockRaycastSolverSystem`):
   - Budget set to 2 rays/tick, template generates 5 candidates needing `AccurateLOS`.
   - Tick 1: assert `SensorEvalState.Phase == _AwaitingRaycasts`, `CognitiveBuffer.IsReady == false`.
   - Tick 4: assert `CognitiveBuffer.IsReady == true` (all rays resolved across 3+ ticks).
2. Integration test: budget = 2, 5 candidates — assert exactly 2 `RaycastRequestEvent`s in tick 1.
3. Assert: solver does not call `cmd.PublishEvent(new EqsResultEvent(...))` while in
   `_AwaitingRaycasts` phase.

---

## Phase 6: Hot-Reload + Authoring

**Goal:** Roslyn source generator for `[EqsTemplate]` blueprint registration, purity analyzer,
and hot-reload classification (soft/hard) via `AiHotReloadCoordinator`.

---

### TASK-EQS-020 — [EqsTemplate] Roslyn source generator and purity analyzer

**Design reference:** Design §5.6, §6.1, §6.2; Impl L:3560–3640

**Scope (IN):**
- `EqsTemplateGenerator` (`IIncrementalGenerator`) — scans for `[EqsTemplate(AssetId=...)]`,
  computes FNV-1a `BlueprintId`, emits `EqsRegistrar_{Assembly}.g.cs` with
  `[BlueprintRegistrar]` class and `Register(BlueprintRegistryStaging)` method
  (Impl L:3565–3635)
- `EqsTemplatePurityAnalyzer` (Roslyn `DiagnosticAnalyzer`) — flags any `[EqsTemplate]`
  `Build()` method that reads non-constant state (instance fields, global singletons, non-pure
  APIs); and enforces that `Build()` is `static` and its only parameter is `IEqsTemplateBuilder`
  (Design §5.6)
- Add both to `Fdp.Toolkits.Analyzers` project

**Scope (OUT):** Hot-reload classification (TASK-EQS-021).

**Constraints:**
- Generator must be `IIncrementalGenerator` (not `ISourceGenerator`) for performance.
- FNV-1a hash algorithm must match the runtime registration (same formula as in Impl L:3600–3612).
- GUID collisions at runtime detected by `BlueprintRegistryStaging.Register` throwing
  `InvalidOperationException` (Design §6.3).
- Analyzer must run incrementally alongside the generator (both in the same analyzer assembly).

**Success conditions:**
1. Annotate `FindCoverFromTarget` with `[EqsTemplate(AssetId="...")]` — build generates
   `EqsRegistrar_...g.cs` with the registration entry.
2. Verify the generated `BlueprintId` matches `FNV1a32(assetId)` computed manually.
3. Unit test (generator test): feed a class with the attribute through the generator, assert the
   output file contains `staging.Add(...)` with the correct ID.
4. Analyzer test: a `Build()` method that reads a static field emits the EQS purity diagnostic;
   a clean `Build(IEqsTemplateBuilder b)` compiles with no diagnostic.

---

### TASK-EQS-021 — Hot-reload: StructureHash, SensorEvalState, hard/soft reset

**Design reference:** Design §6.4; Impl L:3640–3720

**Scope (IN):**
- `SensorEvalState.CurrentStructureHash` field (see TASK-EQS-018)
- Hard-reset logic in `EqsSolverSystem.EvaluateSensor`: compare live `def.StructureHash`
  against `evalState.CurrentStructureHash`; on mismatch reset phase to `Idle`, zero
  `PendingRaycastCount`, mark `CognitiveBuffer.IsReady = false` (Impl L:3650–3680)
- Soft-reset logic: `EpochSnapshot` mismatch (Epoch changed) resets iterator state without
  touching structure hash (Impl L:3685–3695)
- Wire `AiHotReloadCoordinator` to call `RegisterAll` on the new `[BlueprintRegistrar]`

**Scope (OUT):** Purity analyzer (TASK-EQS-020), full ALC hot-swap mechanism (inherits from existing AI coordinator).

**Success conditions:**
1. Unit test: `EqsSolverSystem` with two `EqsQueryTemplate` instances sharing `BlueprintId`
   but with different `StructureHash` values — on first tick, template A evaluates; manually
   swap registry to template B; next tick, assert `SensorEvalState.Phase == Idle` (hard reset)
   and `CognitiveBuffer.IsReady == false`.
2. Unit test: Epoch increments without structure change — assert `Phase` resets but
   `CurrentStructureHash` is preserved.

---

## Phase 7: Diagnostics

**Goal:** ImGui component inspector and zero-allocation gizmo projector for `EqsSensor` /
`EqsCognitiveBuffer`.

---

### TASK-EQS-022 — ImGui inspector and gizmo projector

**Design reference:** Design §14 (final phase); Impl L:2808–3000

**Scope (IN):**
- `EqsCognitiveBufferRenderer` (`[ImGuiRenderer(typeof(EqsCognitiveBuffer))]`) — renders
  summary string + `ImGui.BeginTable` with Rank/EntityId/Position/Score columns
  (Impl L:2815–2870)
- `EqsGizmoSettings` static class — registers `ShowRadius`, `ShowCandidates`, `ShowScores`
  booleans (Impl L:2875–2895)
- `EqsSensorGizmo` (`[GizmoProjector(typeof(SimTransform), typeof(EqsSensor))]`) —
  draws dashed search-radius sphere in cyan, lines to each top-K candidate (green for
  positional, yellow for entity), score text if `ShowScores` enabled (Impl L:2900–2990)

**Scope (OUT):** Solver-side timing visualizers (deferred to debt).

**Constraints:**
- `EqsSensorGizmo.Draw` must be zero-allocation; pre-compute FNV-1a hashes for settings
  keys in constructor (Impl L:2912–2920).
- Use `ReadOnlySpan<EqsResult>` path when reading buffer in gizmo (safe read, no mutation).

**Success conditions:**
1. In a running editor build, selecting an entity with `EqsSensor` shows the component
   inspector table with correct result data.
2. The gizmo draws a circle at the expected radius when `EqsSensor.SearchRadius` is changed.
3. `ShowCandidates = false` hides candidate lines without crashing.

---

## Phase 8: Integration Tests

**Goal:** Headless end-to-end integration tests covering edge cases with deterministic mock
data providers. All tests reside in `Hrot.ClusterRunner.Integration.Tests` or
`Hrot.SimHost.Integration.Tests`.

---

### TASK-EQS-023 — Basic round-trip tests (Editor + Distributed)

**Design reference:** Design §14 Phase 1; Impl L:2060–2195

**Scope (IN):**
- `Eqs_OfflineEditor_PopulatesCognitiveBuffer` — EditorHarness, `MockCoverProvider` +
  `MockNavmeshProvider`, spawn entity, attach `EqsSensor`, pump until `IsReady`, assert
  `Count > 0` and top result `EntityId==0` (Impl L:2070–2130)
- `Eqs_DistributedTopology_EvaluatesOnMuscleAndPopulatesBrain` — HrotRunnerHarness
  `"simhost,cgf"`, inject mocks into SimHost world, Brain spawns entity, attaches sensor,
  wait for Brain buffer ready (Impl L:2135–2185)

**Scope (OUT):** Edge case tests (TASK-EQS-024 through EQS-029).

**Constraints:**
- DDS domain IDs for EQS tests start at 300 (distinct from existing harness ranges at 100/200).
- `MockCoverProvider` and `MockNavmeshProvider` must be in
  `Hrot.SimHost.Integration.Tests.Mocks` namespace (Impl L:2192).
- Use `[Collection("HeavyE2ETests")]` to prevent CI thread starvation.

**Success conditions:**
1. `Eqs_OfflineEditor_PopulatesCognitiveBuffer` passes with `buffer.Count > 0`.
2. `Eqs_DistributedTopology_EvaluatesOnMuscleAndPopulatesBrain` passes — Brain buffer has
   `IsReady == true` within 10 s.

---

### TASK-EQS-024 — Test: Top-K reduction and positional sentinel preservation

**Design reference:** Impl L:2208–2285

**Scope (IN):**
- `DeterministicPositionalGenerator` — yields 5 positional candidates with `PositionX` values
  10, 20, 30, 40, 50 and `EntityId=0`
- `SentinelRejectionFilterTest` — `FilterCheap`, rejects indices 1 and 3 (`EntityId = -1L`)
- `DummyScoreTest` — `ScoreCheap`, asserts internally that it receives `Length == 3` and no
  `-1L` entries (proves `ReduceTopK` compacted before scoring)
- Test `Eqs_TopKReduction_PreservesPositionalSentinels` (EditorHarness) — asserts buffer
  contains exactly 3 entries, all with `EntityId==0`, with X-coords matching 10, 30, 50

**Success conditions:**
1. Buffer has exactly 3 entries.
2. All 3 have `EntityId == 0`.
3. X-coordinates are 10, 30, 50 (correct compaction).
4. `DummyScoreTest.ExecuteBatch` is called with span of length 3 containing no `-1L` entries.

---

### TASK-EQS-025 — Test: Raycast budget exhaustion and cross-tick polling

**Design reference:** Impl L:2290–2380

**Scope (IN):**
- `MockRaycastSolverSystem` — runs in `SystemPhase.Input`, reads `RaycastRequestEvent`s,
  writes `RaycastHit` entries into `RaycastBatchData` ring buffer matching `RayId`; supports
  optional `delayTicks` parameter (Impl L:2340–2360)
- Test `Eqs_RaycastBudgetExhaustion_YieldsAcrossMultipleTicks` (EditorHarness):
  - Budget = 2 rays/tick, template generates 5 candidates, all need accurate LOS.
  - Tick 1: assert `_AwaitingRaycasts`, buffer not ready.
  - Tick 4: assert buffer ready with 5 results (Impl L:2362–2380).

**Success conditions (per Impl L:2296–2310):**
1. After tick 1: `SensorEvalState.Phase == _AwaitingRaycasts`.
2. After tick 1: `CognitiveBuffer.IsReady == false`.
3. After tick 4 (all rays resolved): `CognitiveBuffer.IsReady == true`.
4. `AccurateRaysSubmittedThisTick` never exceeds 2 in any single tick.

---

### TASK-EQS-026 — Test: Path cost vs. Euclidean distance inversion

**Design reference:** Impl L:2384–2410

**Scope (IN):**
- `DeterministicPathingMock` — implements `INavmeshProvider`; A at (0,5) euclidean=5 path=50;
  B at (0,10) euclidean=10 path=10; C at (0,2) unreachable (Impl L:2387–2400)
- Test `Eqs_PathCost_InvertsEuclideanDistance` (EditorHarness)

**Success conditions:**
1. Buffer has exactly 2 entries (C rejected).
2. B is at index 0 (top rank).
3. Score math verified as in Impl L:2403–2410: B total ~1.666 > A total ~1.082.

---

### TASK-EQS-027 — Test: Stale epoch rejection across DDS

**Design reference:** Impl L:2415–2530; includes bug fix to `EqsResultUpdateSystem`

**Scope (IN):**
- `DynamicRadiusGeneratorMock` — yields 1 result for `SearchRadius=10`, 2 for `SearchRadius=20`
  (Impl L:2470–2480)
- Test `Eqs_DistributedTopology_RejectsStaleEpochResults` (HrotRunnerHarness `"simhost,cgf"`):
  - Epoch 1 result lands, assert `Count == 1`.
  - Mutate sensor to Epoch 2.
  - Inject fake `EqsResultUpdateEvent` with Epoch 1 and 99 entries.
  - Pump 1 frame — assert buffer NOT equal to 99 entries.
  - Pump until Epoch 2 result arrives — assert `Count == 2` (Impl L:2477–2525).

**Constraints:**
- Requires the `EqsResultUpdateSystem` epoch-check bug to be fixed (TASK-EQS-004): must
  compare `evt.Epoch != sensor.Epoch`, NOT against `LastUpdateTick`.

**Success conditions:**
1. After injecting stale event: `buffer.Count != 99`.
2. After genuine Epoch 2 arrives: `buffer.Count == 2`.

---

### TASK-EQS-028 — Test: Mid-evaluation BTree subtree abort

**Design reference:** Impl L:2533–2640

**Scope (IN):**
- Reuse `MockRaycastSolverSystem` with `delayTicks=5`.
- Test `Eqs_MidEvaluationAbort_SilentlyDropsQueryWithoutLeaking`
  (HrotRunnerHarness `"simhost,cgf"`):
  - Budget = 1 ray/tick, solver enters `_AwaitingRaycasts`.
  - Trigger Brain BTree abort (Impl L:2604–2614).
  - Assert `EqsSensor` removed from Brain.
  - Pump until replication completes.
  - Assert Muscle ghost no longer has `EqsSensor`.
  - Assert `AccurateRaysSubmittedThisTick == 0` (solver stopped).

**Success conditions:**
1. `EqsSensor` removed from Brain entity after deactivator fires.
2. `EqsSensor` removed from Muscle ghost entity after DDS propagation.
3. No crash or exception in solver on next tick.
4. `AccurateRaysSubmittedThisTick == 0` (no rays submitted for dead query).

---

### TASK-EQS-029 — Test: TargetMemory threat threshold bypassing

**Design reference:** Impl L:2643–2740

**Scope (IN):**
- `ExposedLosServiceMock` — `HasCheapLineOfSight` always returns `true` (all cover exposed)
  (Impl L:2702–2710)
- Test `Eqs_ThreatThreshold_BypassesContextFilters` (EditorHarness):
  - Scenario A: threat score 100, threshold 50 — assert buffer `Count == 0` (all rejected).
  - Scenario B: drop threat score to 10 — assert buffer `Count == 1` (filter bypassed, point
    survives) (Impl L:2720–2740).

**Success conditions:**
1. Scenario A: `buffer.Count == 0` (exposed cover rejected when threat active).
2. Scenario B: `buffer.Count == 1` (filter bypassed when threat score below threshold).

---

## Phase 9: HideInCover BTree Behavior

**Goal:** Demonstrate the complete EQS pipeline through a production-usable behavior tree.

---

### TASK-EQS-030 — HideInCoverBlackboard and Action_MoveToOptimalCover

**Design reference:** Impl L:3565–3580, L:3680–3800

**Scope (IN):**
- `HideInCoverBlackboard` unmanaged struct — `EqsParams EqsConfig`, `MoveToOptimalCoverParams
  MoveConfig` (Impl L:3752–3765)
- `MoveToOptimalCoverParams` — `Speed`, `ArrivalRadius` (Impl L:3685–3695)
- `Action_MoveToOptimalCover` BTree action — reads `EqsCognitiveBuffer.GetTop()`, writes to
  `LocomotionChannel` via `ActionIdMoveTo`, returns `Running` while moving, forwards
  `Success`/`Failure` from channel status (Impl L:3700–3800)

**Scope (OUT):** `Action_HoldPosition`, full `HideInCover_BT` integration scenario test.

**Constraints:**
- Must check `buffer.IsReady && buffer.Count > 0` before reading — return `Failure` if not
  ready (Impl L:3720).
- Uses `fixed (byte* dst = channel.Params) { *(MoveToParams*)dst = moveToParams; }` — no
  allocation (Impl L:3780–3790).
- Must set `channel.BehaviorInstanceId` to prevent channel arbitration from stomping
  (Impl L:3745).

**Success conditions:**
1. Unit test: entity with pre-populated `EqsCognitiveBuffer` (top result at (10, 20)),
   `Action_MoveToOptimalCover` — assert `LocomotionChannel.ActiveAction == ActionIdMoveTo`
   and `MoveToParams.Destination == (10, 20)`.
2. Unit test: `EqsCognitiveBuffer` not ready — assert action returns `NodeStatus.Failure`.
3. Unit test: `LocomotionChannel.Status == Success` — assert action returns `NodeStatus.Success`.

---

### TASK-EQS-031 — HideInCover_BT full behavior definition

**Design reference:** Impl L:3802–3880

**Scope (IN):**
- `[BTreeDefinition("HideInCover_BT")]` static method in `TacticsNodes` composing:
  - `ObserverSelector` with two branches (Impl L:3820–3870)
  - High priority: `Condition_HasTarget` → `Parallel(MaintainEqsSensor, Sequence(WaitForSensor,
    MoveToOptimalCover, HoldPosition))`
  - Low priority: `Action_Wander`

**Scope (OUT):** Actual integration scenario test (can be exercised manually in Editor).

**Success conditions:**
1. Build succeeds (tree compiles without validation errors from `TreeValidator`).
2. `EditorHarness` quick-smoke: spawn entity, assign `HideInCover_BT`, inject mock threat into
   `TargetMemory`, pump 500 ms — assert `LocomotionChannel` has an active `MoveTo` intent.
3. Remove threat from `TargetMemory` — assert `EqsSensor` removed by deactivator within 1
   pump frame and `LocomotionChannel` becomes idle.

---

## Deferred: Design Phases 7 and 8

The following items from [Design §14](./EQS_Design_v1.3_final.md) are intentionally out of
scope for this workstream and tracked as future debt.

**Design phase 7 — Per-template diagnostics:**
- Cost tracking per sensor (evaluation time, candidate count after each phase).
- Refresh-rate observability (actual Hz delivered vs. configured Hz).
- Dropped-by-budget counters and diagnostic sink.
- These are plumbed into the existing diagnostic system; no design detail in `IMPLEM_DETAILS.md`.
- Log as debt items when the time-sliced solver data is available.

**Design phase 8 — v2 optimizations:**
- Identical-evaluation sharing: sensors with the same `(BlueprintId, parameters hash)` share a
  single solver invocation.
- Leased subscriptions for distributed deployment (avoid re-evaluating when consumer is absent).
- No tasks created; record in DEBT-TRACKER when Phase 2+ work is complete.

**Starter-pack templates (remaining 7):**
TASK-EQS-015 covers only `FindCoverFromTarget`. The following templates are listed in Design §6.6
and deferred:
- `FindNearestEnemy`, `FindNearestAlly`, `FindThreatsInView`, `FindFlankingPosition`,
  `FindSafeRetreatPoint`, `FindAllyForFormation`, `FindOpenFiringPosition`.
- Each is straightforward to add once TASK-EQS-020 (Roslyn generator) is complete.

---

## Phase 10 — Corrective: Schema additions (architect findings #1, #2, #3)

**Goal:** Land three additive struct/topic field changes that the EQS v1.3 design explicitly
required but the initial implementation omitted. Identified during the When-node iteration
scoping conversation with the engine architect.

**Cross-iteration coordination:** The new `EqsCognitiveBuffer.LastUpdateTimeSeconds` field
(TASK-EQS-033) is also listed as the When-node iteration's engine-side dependency
([When_Reactivity_Iteration_Design_v2_2.md](../blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md)
§1.10 note 4, §6.8, §11). It is landed here, in EQS-2, as the natural data owner. When-node
iteration consumes it without shipping its own copy.

Expanding `EqsSensor` with `ScoreDeltaThreshold` (TASK-EQS-034) and 3 context-slot fields
(Phase 11) deliberately overrides When-node design v2.2 §1.10 note 9, which fixed the seven
existing `EqsSensor` fields as "the known field set" exposed by `SpawnEqsSensorNode`. After
this phase lands, that note must be revised to include the additional fields and the spawn
node's pin layout (§2.8) expanded accordingly.

---

### TASK-EQS-032 — Add `FlagsMeaningful` to `EqsResult`

**Design reference:** Design §4.1 ("a parallel `FlagsMeaningful` bitset indicating which bits
were actually computed by the template's tests"), §4.2 ("A bit not in `FlagsMeaningful` must
not be read"); architect response item #1.

**Scope (IN):**
- Add `public short FlagsMeaningful` to `EqsResult`, replacing the existing `short _pad`
  field. Struct stays at exactly 24 bytes.
- Update `EqsResultUpdateSystem` (both online and offline paths) to copy
  `FlagsMeaningful` alongside `Flags`.
- Update `EqsResultTopic.EqsResultEntry` DDS struct to carry the field, then update
  `EqsResultEventEgressTranslator` and `EqsResultIngressTranslator` to thread it through.
- Update `IEqsTest` implementations whose `Flag` bit setting is currently unconditional to
  set the matching bit in `FlagsMeaningful` whenever the test actually evaluates the bit
  (`CheapLineOfSightTest`, `AccurateLineOfSightTest`, `NavmeshReachableTest`).
- For tests that bypass evaluation (e.g. threat-below-threshold path), `FlagsMeaningful`
  for the corresponding bit must remain 0 on those candidates.

**Scope (OUT):** Refactoring readers (BTree nodes, gizmo) to honour `FlagsMeaningful` — those
become tasks within Phase 11 once context slots also need it.

**Constraints:**
- `Marshal.SizeOf<EqsResult>()` must remain `24` after the change.
- Both flag-write sites in each test must update both `Flags` and `FlagsMeaningful` together
  via a small helper or matching assignment pair.

**Success conditions:**
1. `Marshal.SizeOf<EqsResult>()` returns `24` unchanged.
2. Unit test: register a template whose only LOS test is bypassed (threat score below
   threshold) — assert all candidates in the resulting buffer have
   `FlagsMeaningful & (1<<0) == 0` even though `Flags` may be 0 or carry other bits.
3. Unit test: register a template that exercises `CheapLineOfSightTest` with threats above
   threshold — assert `FlagsMeaningful & (1<<0) != 0` on every surviving candidate.
4. DDS round-trip test: `FlagsMeaningful` survives a Brain → Muscle → Brain cycle and lands
   in the `EqsCognitiveBuffer` unchanged.

---

### TASK-EQS-033 — Add `LastUpdateTimeSeconds` to `EqsCognitiveBuffer`

**Design reference:** Design §8 ("the most recent top-K result and the simulation time of last
update"); architect response item #2; When-node design v2.2 §1.10 note 4, §6.8
(`BecomesStale` trigger uses simtime, not ticks).

**Scope (IN):**
- Add `public float LastUpdateTimeSeconds` to `EqsCognitiveBuffer`. Insertion point: after
  `LastUpdateTick`. Struct alignment review required (insert padding if needed so the
  inline-array offset stays a multiple of `EqsResult.Stride`).
- `EqsResultUpdateSystem` stamps the field from `view.Time` (the consumer's simulation time)
  on every successful write of the buffer. Both online and offline paths.
- Document that `LastUpdateTick` remains the determinism-friendly timestamp (publish-side,
  carried through `EqsResultEvent`) while `LastUpdateTimeSeconds` is the consumer-side
  wall-of-simtime stamp used by recency queries.
- No change to `EqsResultEvent` (it does not carry seconds; the consumer time-stamps).
- No change to DDS wire format.

**Scope (OUT):** A `BecomesStale` reader helper or BTree node — that's a When-node iteration
or future EQS feature.

**Constraints:**
- `LastUpdateTimeSeconds` must be set every time `Count` and `LastUpdateTick` are written,
  including the "empty result event" path (where `Count = 0` but `IsReady` flips to true).
- `EqsCognitiveBuffer.IsReady` semantics must remain `LastUpdateTick > 0` — do NOT switch
  to checking `LastUpdateTimeSeconds`.

**Success conditions:**
1. Unit test: spawn entity at `view.Time = 5.0f`, publish a result event, assert
   `buffer.LastUpdateTimeSeconds == 5.0f`.
2. Unit test: advance to `view.Time = 5.5f` and publish a second event with empty
   `EntryCount`, assert `buffer.LastUpdateTimeSeconds == 5.5f` (stamps even on empty
   updates).
3. `Marshal.SizeOf<EqsCognitiveBuffer>()` remains a multiple of `EqsResult.Stride`; no
   regression in the existing `EqsCognitiveBuffer_GetSpanRW_NoDefensiveCopy` test.

---

### TASK-EQS-034 — Add `ScoreDeltaThreshold` to `EqsSensor` and DDS topic

**Design reference:** Design §3.2 ("`ScoreDelta(threshold)` — send when any top-K score has
shifted by more than the threshold"), §3.1 (publish policies overridable per-sensor);
architect response item #3.

**Scope (IN):**
- Add `public float ScoreDeltaThreshold` to `EqsSensor`. Insertion point: alongside
  `PublishPolicy` / `Priority`.
- Add the same field to `EqsSensorConfigTopic` DDS struct.
- Thread the field through `EqsSensorConfigEgressTranslator` and `EqsSensorConfigIngressTranslator`.
- Update `EqsLifecycleNodes.Action_MaintainEqsSensor`: when the threshold changes, increment
  `sensor.Epoch` (same pattern as the existing four-field comparison).
- Wire the threshold into the solver's publish-policy decision (currently the policy byte is
  carried but no `ScoreDelta` path exists). Add a per-sensor `LastPublishedTopK` cache
  (small array of 16 floats) on `SensorEvalState` so the next publish can diff the
  current top-K against the last-published top-K and decide whether to emit
  `EqsResultEvent`.
- Default value: `0.0f` (every change publishes, equivalent to `AlwaysPush` behavior for
  the `ScoreDelta` policy when the threshold is unset).

**Scope (OUT):** Authoring helpers (`SpawnEqsSensorNode` pin addition) — handled by the
When-node iteration after this lands.

**Constraints:**
- `PublishPolicy` byte enum must gain a `ScoreDelta = 3` discriminator (or whatever value
  preserves the existing enum's ordinals) — confirm with Design §3.2's policy list.
- The diff cache lives on `SensorEvalState`, not `EqsSensor` — it is solver-local state, not
  Brain-replicated parameters.

**Success conditions:**
1. Unit test: sensor with `PublishPolicy = ScoreDelta`, threshold 0.1. First evaluation
   publishes. Second evaluation with all top-K scores ≤ 0.05 change does NOT publish.
   Third evaluation with one top-K score changed by 0.2 publishes.
2. Unit test: `Action_MaintainEqsSensor` mutates `ScoreDeltaThreshold` only — assert
   `sensor.Epoch` incremented exactly once.
3. DDS round-trip: `ScoreDeltaThreshold` survives Brain → Muscle replication via
   `EqsSensorConfigTopic`.

---

## Phase 11 — Corrective: Context-slot generalization (architect finding #4)

**Goal:** Replace the hardcoded `TargetMemory.PositionsX/Y[0]` reads in `CheapLineOfSightTest`
and `AccurateLineOfSightTest` with a generalized 3-context-slot mechanism declared on
`EqsSensor` per Design §4.2.

---

### TASK-EQS-035 — Add context slots to `EqsSensor` and DDS topic

**Design reference:** Design §4.2 ("Context slots are query-template-defined runtime
references (typically self, target, leader/squad-mate). Up to 3 LOS contexts simultaneously");
architect response item #4.

**Scope (IN):**
- Add three context-slot fields to `EqsSensor`:
  ```csharp
  public Entity ContextSlot0;   // by convention: Self (filled by the spawn helper)
  public Entity ContextSlot1;   // by convention: Target
  public Entity ContextSlot2;   // by convention: Leader / Squad-mate
  ```
- Add the same three fields to `EqsSensorConfigTopic`. DDS carries them as
  `(uint Index, uint Generation)` pairs (or whatever the existing entity-wire encoding is).
- Thread through both DDS translators. On Muscle side, the ingress translator must map the
  incoming `Entity` (which is the *Brain*'s entity handle) to the local *Muscle*'s ghost
  entity via the existing `NetworkEntityMap`. If the lookup fails (ghost not yet promoted),
  the slot remains `Entity.Null` until the next config sample.
- Update `EqsLifecycleNodes.Action_MaintainEqsSensor` to accept three additional
  `EqsParams.ContextSlot0/1/2` fields (additive — existing callers pass `Entity.Null`).
- Update `EqsParams` blackboard struct to include the three slot fields (additive).

**Constraints:**
- Slots are filled with arbitrary `Entity` handles by the caller; the EQS subsystem assigns
  no semantic meaning to slot indices. The convention "Slot 0 = Self, Slot 1 = Target,
  Slot 2 = Leader" is documented only in the spawn helper, not enforced by the solver.
- Tests read whichever slots they need (e.g. an LOS test reading `ContextSlot1` will fail
  gracefully if the caller left it as `Entity.Null`).
- For the cross-network handle mapping: a slot whose Brain-side entity has no corresponding
  Muscle-side ghost yet (e.g. spawning order) must remain `Entity.Null` on the Muscle side
  until the next replication tick; do not block evaluation.

**Success conditions:**
1. Unit test: spawn `EqsSensor` with `ContextSlot1 = targetEntity`; assert DDS round-trip
   preserves the slot value and Muscle-side ghost resolves correctly.
2. Unit test: spawn `EqsSensor` with `ContextSlot1` pointing at an entity that has no Muscle
   ghost yet — assert solver does not throw; slot stays `Entity.Null` on Muscle.
3. Unit test: `Action_MaintainEqsSensor` mutates `ContextSlot1` — assert `sensor.Epoch`
   incremented.

---

### TASK-EQS-036 — Generalize LOS tests to read from context slots

**Design reference:** Design §4.2; architect response item #4.

**Scope (IN):**
- Replace `CheapLineOfSightTest`'s hardcoded `TargetMemory.PositionsX[0]/PositionsY[0]`
  read with a configurable context-slot index (default 1, matching "Target" by convention).
  Add `public byte ContextSlotIndex { get; set; } = 1;` to the test's parameter struct.
- Same change in `AccurateLineOfSightTest`. Same default.
- Both tests must:
  - Read `sensor.ContextSlotN` per their configured index.
  - If the slot is `Entity.Null`, bypass the test (treat as "no threat configured") — do not
    fall through to old `TargetMemory[0]` logic. Set `FlagsMeaningful` bit 0 to 0 on every
    candidate (TASK-EQS-032 dependency).
  - If the slot points to a live entity that has `SimTransform`, read position from
    `view.GetComponentRO<SimTransform>(slotEntity).Position`. The dependency on
    `TargetMemory` is removed entirely.
  - On success (LOS evaluated), set both `Flags` bit and `FlagsMeaningful` bit per
    TASK-EQS-032.

**Scope (OUT):** A multi-context-bit fan-out (e.g. setting all three `HasLOSToContext0/1/2`
bits in a single test pass) — the test is still single-context per invocation; a template
that needs three LOS readings declares three test instances each pointing at a different
slot index.

**Constraints:**
- `TargetMemory` consumption is permitted (e.g. as a fallback to derive a slot) but must not
  be hardcoded; the existing tests that depend on `TargetMemory.ThreatScores[0]` for the
  threat-threshold bypass should continue to work using the **observer's** `TargetMemory`,
  not a slot lookup. The slot is the *position source*; the observer's threat list is still
  read for the threshold gate.

**Success conditions:**
1. Unit test: observer with no `TargetMemory`, sensor with `ContextSlot1` set to a live
   target entity — assert `CheapLineOfSightTest` reads the target's `SimTransform.Position`
   and runs (not bypassed for missing TargetMemory).
2. Unit test: sensor with `ContextSlot1 = Entity.Null` — assert test bypasses cleanly;
   no candidate has `FlagsMeaningful` bit 0 set.
3. Existing tests in `CoverGeneratorAndLosTests.cs` and `AccurateLosTests.cs` continue to
   pass after migration to the new slot-based API (test fixtures may need to set
   `sensor.ContextSlot1 = target` instead of relying on `TargetMemory[0]`).

---

## Phase 12 — Corrective: Multi-sensor child-entity support (architect findings #A, #5)

**Goal:** Implement the engine-confirmed pattern of hosting multiple concurrent EQS queries
per agent via dynamically-spawned child entities, each carrying its own `EqsSensor` +
`EqsCognitiveBuffer`. Pattern uses the existing `PartMetadata` + `SubEntityCleanupSystem`
cleanup infrastructure (verified extant at
[FDP/Toolkits/Fdp.Toolkits/Replication/Systems/SubEntityCleanupSystem.cs](../../FDP/Toolkits/Fdp.Toolkits/Replication/Systems/SubEntityCleanupSystem.cs)
and [PartMetadata.cs](../../FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs)).

**Cross-iteration coordination:** The `EqsSensorHandle` wrapper struct is listed in
[When_Reactivity_Iteration_Design_v2_2.md](../blueprints-3-when-node/When_Reactivity_Iteration_Design_v2_2.md)
§2.1 as a When-node iteration deliverable. It is landed here, in EQS-2, as the natural
data owner. When-node iteration consumes it.

---

### TASK-EQS-037 — Declare `EqsSensorHandle` wrapper struct

**Design reference:** Architect response item #A ("a lightweight wrapper struct like
`EqsSensorHandle { public Entity ChildId; }`"); When-node design v2.2 §2.1.

**Scope (IN):**
- New file `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs` (namespace `FDP.Eqs`,
  matching the When-node design's import).
- Struct definition exactly per When-node design v2.2 §2.1:
  ```csharp
  namespace FDP.Eqs;
  [StructLayout(LayoutKind.Sequential, Pack = 4)]
  public readonly struct EqsSensorHandle : IEquatable<EqsSensorHandle>
  {
      public readonly Entity ChildId;
      public EqsSensorHandle(Entity childId) => ChildId = childId;
      public bool Equals(EqsSensorHandle other) => ChildId.Equals(other.ChildId);
      public override bool Equals(object? obj) => obj is EqsSensorHandle other && Equals(other);
      public override int GetHashCode() => ChildId.GetHashCode();
      public static bool operator ==(EqsSensorHandle a, EqsSensorHandle b) => a.Equals(b);
      public static bool operator !=(EqsSensorHandle a, EqsSensorHandle b) => !a.Equals(b);
      public bool IsValid => ChildId.Id != 0;
  }
  ```
- No runtime behavior; purely a type-system wrapper so Blueprint variable pickers can
  filter to "EQS sensor handles" rather than presenting all `Entity` variables.

**Success conditions:**
1. Compile: struct compiles standalone.
2. Unit test: `EqsSensorHandle(entity).ChildId == entity`.
3. Unit test: two handles with the same `Entity` are `Equals` and produce equal
   hash codes.
4. Unit test: `default(EqsSensorHandle).IsValid == false`.

---

### TASK-EQS-038 — Relax `EqsSolverSystem` query and rekey sensor replication

**Design reference:** Architect response — child entities created via the deferred command
buffer on Brain, attached `PartMetadata + EqsSensor`; replicated to Muscle via existing
`EqsSensorConfigEgressTranslator`. Q3 resolved: solver drops `NetworkIdentity` requirement.
Architect implementation-guidance addendum: identity-read branching, dictionary-cached
ingress, invisible-ghost spawning.

**Scope (IN):**

**A. Solver query and identity-read rewrite (`EqsSolverSystem`):**
- `EqsSolverSystem._sensorQuery` becomes
  `repo.Query().With<EqsSensor>().WithLifecycle(EntityLifecycle.All).Build()`.
  No `.With<NetworkIdentity>()`.
- **Rewrite `EvaluateSensor`'s ID-resolution step.** Today it unconditionally reads
  `NetworkIdentity` from the sensor entity to populate `EqsResultEvent.SensorNetworkId`;
  dropping the query requirement without rewriting this read will throw an ECS
  `KeyNotFoundException`/component-missing exception on the first child-entity sensor.
  The new branch is:
  ```
  if (repo.HasComponent<PartMetadata>(sensorEntity)) {
      var parent     = repo.GetComponentRO<PartMetadata>(sensorEntity).ParentEntity;
      var instanceId = repo.GetComponentRO<PartMetadata>(sensorEntity).InstanceId;
      if (!repo.IsAlive(parent) || !repo.HasComponent<NetworkIdentity>(parent))
          return; // parent gone or local-only; for local-only see local-path below
      parentNetworkId = repo.GetComponentRO<NetworkIdentity>(parent).Value;
      localChildIndex = instanceId;
  } else if (repo.HasComponent<NetworkIdentity>(sensorEntity)) {
      // Legacy single-sensor path (Action_MaintainEqsSensor on ctx.Self).
      parentNetworkId = repo.GetComponentRO<NetworkIdentity>(sensorEntity).Value;
      localChildIndex = 0;
  } else {
      // Purely local sensor (no NetworkIdentity anywhere in the chain).
      parentNetworkId = 0;
      localChildIndex = sensorEntity.Index; // any locally-unique value works
  }
  ```
- `EqsResultEvent` carries `(long ParentNetworkId, int LocalChildIndex)` instead of the
  single `SensorNetworkId`. Rename the field accordingly. Update both publish sites in
  `EqsSolverSystem` (the stub-fallback empty-result path and the real
  `WriteResultsToPoolAndPublish` path).

**B. Wire-format change (`EqsSensorConfigTopic` and translators):**
- Change `EqsSensorConfigTopic`'s `[DdsKey]` from the sensor entity's own `NetworkId`
  (which child entities lack) to the compound:
  - `[DdsKey] long ParentNetworkId` — the parent agent's `NetworkIdentity.Value`.
  - `[DdsKey] int LocalChildIndex` — `PartMetadata.InstanceId` for the child case,
    `0` for the legacy single-sensor case.
- `EqsSensorConfigEgressTranslator.ScanAndPublish`: apply the same identity-resolution
  branch shown above to derive the key from the sensor entity.

**C. Muscle-side ingress with dictionary cache (no per-packet ECS query):**
- `EqsSensorConfigIngressTranslator` maintains a managed dictionary as private state:
  ```csharp
  private readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childGhostCache = new();
  ```
- On each `PollIngress` sample:
  1. Look up `parentGhost = _entityMap.Resolve(ParentNetworkId)`. If parent ghost not yet
     promoted (returns `Entity.Null`), skip the sample; the Brain's
     Reliable/TransientLocal QoS will redeliver after the parent lands.
  2. Look up the child ghost in `_childGhostCache` by key.
  3. **Cache miss** → spawn an invisible carrier ghost via the egress command buffer
     (see §D below). Insert into cache.
  4. **Cache hit** → reuse the existing ghost.
  5. Apply / update the `EqsSensor` component (and `EqsCognitiveBuffer` if missing) on
     the resolved child ghost.
- **Forbidden:** scanning the ECS via `repo.Query().With<PartMetadata>()...` inside the
  ingress loop. That iterates 64KB chunks per packet and destroys the CPU budget under
  load. **Reference implementation pattern:** `MultiInstanceCycloneTranslator<T>` in the
  codebase solves the same problem.
- On `NotAliveDisposed` (Brain removed the sensor): look up the child in the cache,
  `ecb.RemoveComponent<EqsSensor>(child)` (or destroy the child entity), and remove the
  cache entry.

**D. "Invisible ghost" spawning rules (Muscle side, child carrier only):**
- The Muscle-side carrier ghost is created purely via
  `var ecb = view.GetCommandBuffer(); var child = ecb.CreateEntity();`. Do **not** route
  the spawn through `GhostCreationSystem` or any other DDS-aware lifecycle machinery.
- **Only** these components may be attached to the carrier:
  - `PartMetadata { ParentEntity = parentGhost, InstanceId = LocalChildIndex, DescriptorOrdinal = 0 }`
  - `EqsSensor { ... }` (populated from the DDS sample)
  - `EqsCognitiveBuffer` (the solver will write into it; lazy-add via
    `EqsResultUpdateSystem` is also acceptable but local-attach saves a round-trip)
- **Forbidden:** attaching `NetworkIdentity`, `TkbIdentity`, `GhostStateTracker`, or any
  other replication-lifecycle component on the carrier. It is an invisible local entity
  whose entire purpose is to host the sensor without participating in the
  `EntityMaster`/`EntityLifecycleModule` DDS handshake.

**E. Result-event reverse lookup (Brain side):**
- `EqsResultIngressTranslator` symmetrically maintains
  `Dictionary<(long ParentNetId, int ChildIndex), Entity>` on the Brain side, populated by
  the egress side of the same sensor's lifecycle (or rebuilt lazily on first miss by
  scanning for the corresponding `PartMetadata` only once and caching).
- Translates inbound `EqsResultEvent`'s `(ParentNetworkId, LocalChildIndex)` back to the
  local Brain-side child entity and publishes the bridged `EqsResultUpdateEvent`.

**F. Purely local (single-node editor) path:**
- For sensors with no parent network identity at all (single-process editor mode), the
  solver still iterates them; no DDS replication occurs. The `EqsResultEvent`'s reverse
  lookup is short-circuited because publisher and consumer share the same
  `EntityRepository`: `EqsResultUpdateSystem` matches by the sensor entity directly,
  not by `(ParentNetworkId, LocalChildIndex)`. Add a fast-path: if
  `evt.ParentNetworkId == 0`, treat `LocalChildIndex` as the sensor entity's local index
  and resolve directly.

**Scope (OUT):** Migrating the existing `Action_MaintainEqsSensor` (which attaches directly
to `ctx.Self`, a parent-shaped entity) — that BTree action keeps working for single-sensor
agents because `ctx.Self` typically has `NetworkIdentity`. Decision: legacy single-sensor
path continues; new pattern is additive.

**Constraints:**
- **Identity branch:** The solver must branch its identity read on `HasComponent<PartMetadata>`.
  If true, derive `ParentNetworkId` from `PartMetadata.ParentEntity`'s `NetworkIdentity`
  and use `PartMetadata.InstanceId` as `LocalChildIndex`. Else read `NetworkIdentity` from
  the sensor entity itself and use `LocalChildIndex = 0`. Else (no network identity
  anywhere) use the local-path fast-key (`ParentNetworkId = 0`,
  `LocalChildIndex = sensor.Index`). Skipping this branch causes a runtime exception on
  the first child-entity sensor.
- **Ingress cache mandatory:** `EqsSensorConfigIngressTranslator` must cache child `Entity`
  handles in a private `Dictionary<(long, int), Entity>` to avoid O(N-entities) ECS
  queries during network polling. Do **not** call `repo.Query().With<PartMetadata>()` or
  any equivalent inside the polling loop. Mirror `MultiInstanceCycloneTranslator<T>`.
- **Invisible-ghost rules:** Muscle-side carrier ghosts must be spawned via
  `ecb.CreateEntity()` (never through `GhostCreationSystem`) and must carry exactly
  `PartMetadata`, `EqsSensor`, and `EqsCognitiveBuffer` — **no** `NetworkIdentity`,
  `TkbIdentity`, or `GhostStateTracker`. Adding any of those activates the standard DDS
  entity-lifecycle handshake and will cause spurious destroy commands when the Brain
  releases the sensor.
- **Backwards compat:** if an `EqsSensor` is attached to an entity that has
  `NetworkIdentity` but no `PartMetadata` (the legacy single-sensor case from
  `Action_MaintainEqsSensor`), the egress translator must still publish, keying as
  `ParentNetworkId = sensor entity's own NetworkId`, `LocalChildIndex = 0`. Muscle ingress
  finds the corresponding ghost directly via `_entityMap.Resolve(ParentNetworkId)` (since
  the parent IS the sensor host) and attaches `EqsSensor` on it. The dictionary cache
  stores `(parentNetId, 0) → parentGhost` so subsequent updates are O(1). This preserves
  the single-sensor `HideInCover_BT` behavior unchanged.
- `EqsResultUpdateSystem` must handle three shapes: (a) result event whose key resolves
  to a separate child-carrier entity (multi-sensor path), (b) one that resolves to the
  legacy parent-host entity (single-sensor path), (c) one with
  `ParentNetworkId == 0` resolving by local sensor index (offline path).

**Success conditions:**
1. Unit test: spawn a sensor on a non-networked entity (no `NetworkIdentity`, no
   `PartMetadata`) — assert solver iterates it, emits `EqsResultEvent` with
   `ParentNetworkId == 0`, and does NOT throw any "component missing" exception.
2. Unit test: spawn a sensor on a child entity with `PartMetadata{ParentEntity=parent,
   InstanceId=42}` where parent has `NetworkIdentity.Value=12345` — assert
   `EqsResultEvent.ParentNetworkId == 12345` and `LocalChildIndex == 42`.
3. Integration test (distributed): Brain spawns a parent entity and a child sensor
   entity; assert Muscle's `EqsSensorConfigIngressTranslator` spawns exactly one carrier
   ghost (via `ecb.CreateEntity()`) carrying only `PartMetadata`, `EqsSensor`, and
   `EqsCognitiveBuffer` — assert no `NetworkIdentity` / `TkbIdentity` /
   `GhostStateTracker` on the carrier.
4. Performance: with 1000 dynamic child sensors and 10 Hz config updates, ingress
   translator's `PollIngress` allocates 0 bytes per call after warmup (dictionary
   pre-grown), and per-packet work is O(1) lookup, not O(entities).
5. Backwards compat: existing `HideInCover_BT` test (legacy single-sensor on `ctx.Self`)
   continues to pass with no test-side changes.
6. Local-only path: editor harness without DDS — spawn 3 child sensors on one parent,
   pump, assert 3 separate `EqsCognitiveBuffer` components are populated (one per child).

---

### TASK-EQS-039 — BTree spawning / destroying child-sensor actions

**Design reference:** Architect response item #5; When-node design v2.2 §1.8 (`SpawnEqsSensorNode`
semantics — equivalent for the BTree side).

**Scope (IN):**
- New action `Action_SpawnEqsSensorChild` in `EqsLifecycleNodes`:
  - Reads its `EqsParams` blackboard params plus a new
    `EqsSpawnParams { byte ChildSlotIndex; }`.
  - Allocates a deterministic `LocalChildIndex` per parent + slot index pair so re-running
    the action targets the same child. Recommended formula:
    `(int)((uint)ctx.Self.Index << 8) | childSlotIndex`. Stable across ticks for the same
    parent/slot pair.
  - **Spawns the child via the deferred command buffer**, NOT via direct repo mutation:
    ```csharp
    var ecb   = ctx.World.GetCommandBuffer();
    var child = ecb.CreateEntity();
    ecb.AddComponent(child, new PartMetadata {
        ParentEntity      = ctx.Self,
        InstanceId        = computedIndex,
        DescriptorOrdinal = 0,
    });
    ecb.AddComponent(child, new EqsSensor { /* populated from EqsParams + context slots */ });
    ecb.AddComponent(child, default(EqsCognitiveBuffer));
    ```
    BTree actions execute during `SystemPhase.Simulation` while the kernel is iterating
    ECS chunks. Calling `ctx.World.CreateEntity()` or `ctx.World.AddComponent(...)`
    directly during this phase mutates the chunk arrays mid-iteration and corrupts memory.
    The ECB defers the structural change to the next safe playback point.
  - Writes the resulting `EqsSensorHandle { ChildId = newChild }` into a blackboard slot
    (the action's output param, exposed as a blackboard-field write). The reserved
    child `Entity` handle returned by `ecb.CreateEntity()` is valid to store immediately
    and resolves to a stable slot at playback time.
  - Returns `NodeStatus.Success` after recording the spawn; subsequent invocations with
    the same `(parent, slot)` reuse the existing child (idempotent — see Constraints).
- New deactivator `Deactivate_SpawnEqsSensorChild` paired with the `@0` compound-key
  convention. Destroys the child entity referenced by the blackboard handle, also via
  the command buffer:
  ```csharp
  var ecb = ctx.World.GetCommandBuffer();
  if (handle.IsValid && ctx.World.IsAlive(handle.ChildId))
      ecb.DestroyEntity(handle.ChildId);
  ```
- Update `EqsParams` (or create `EqsSpawnParams`) to include the slot-index discriminator.

**Scope (OUT):** A Blueprint `SpawnEqsSensorNode` — that's a When-node iteration deliverable.

**Constraints:**
- **Deferred structural mutation only.** The BTree action must use the ECB
  (`ctx.World.GetCommandBuffer()`) for `CreateEntity` and every `AddComponent`. Direct
  `ctx.World.CreateEntity()` / `ctx.World.AddComponent(...)` during the Simulation phase
  causes chunk-array corruption — the kernel is iterating chunks when the BTree runs.
  Same constraint applies to the deactivator's `DestroyEntity`.
- **Idempotency without per-tick scans.** The action must not double-spawn: if a child
  already exists matching `(ctx.Self, ChildSlotIndex)`, reuse it. The deterministic
  `LocalChildIndex` formula above lets the action store the previously-spawned
  `EqsSensorHandle` in its blackboard output slot and reuse it on re-entry — no ECS scan
  in the steady-state path. On first entry only (handle slot empty or stale), fall back
  to a one-shot guarded scan: a pre-built `EntityQuery` on `PartMetadata` (cached at
  module level, not rebuilt per tick) filtered by `ParentEntity == ctx.Self &&
  InstanceId == computedIndex` to find a pre-existing matching child that may have
  survived across a BTree restart. Cache the result back into the blackboard immediately.
- **Cascading cleanup is automatic.** When the deactivator's `ecb.DestroyEntity(child)`
  plays back, the child's `EqsSensor` and `EqsCognitiveBuffer` components vanish along
  with the entity through normal ECS destruction.
- **Parent death is automatic.** `SubEntityCleanupSystem` already runs in `PostSimulation`
  and destroys any entity whose `PartMetadata.ParentEntity` is no longer alive. No extra
  cleanup code needed for the agent-death path.

**Success conditions:**
1. Unit test (EditorHarness): parent entity, run `Action_SpawnEqsSensorChild` with slot 1 —
   assert one child exists with matching `PartMetadata`; assert the blackboard
   `EqsSensorHandle` field points at that child.
2. Unit test: run the action twice with same slot — assert exactly one child still exists
   (idempotent).
3. Unit test: run the action twice with different slot indices — assert two children exist.
4. Unit test: deactivate the action — assert child entity destroyed; the `EqsSensor` and
   `EqsCognitiveBuffer` components vanish with it.
5. Unit test: destroy the parent entity — assert `SubEntityCleanupSystem` cleans up the
   child on the next PostSimulation tick (no manual deactivator needed).

---

### TASK-EQS-040 — Multi-sensor integration test + HideInCover_BT child-entity recipe

**Design reference:** Design §11 (sensor lifecycle), §6.6 (starter templates);
architect response items #A + #5.

**Scope (IN):**
- New integration test `Eqs_MultiSensor_OneAgentTwoConcurrentQueries` (EditorHarness):
  - Spawn observer + 5 enemy entities + 3 cover points.
  - Spawn two child sensors on the observer via `Action_SpawnEqsSensorChild` with
    different `(template, slot)` pairs: one running `FindNearestEnemy`, one running
    `FindCoverFromTarget`.
  - Pump solver ticks.
  - Assert both children's `EqsCognitiveBuffer` are populated with different result
    counts and shapes (entity-shaped vs positional).
  - Assert results are read from the children via `handle.ChildId`, not the parent.
- New "child-entity" alternative `HideInCover_BT_v2` BTree definition in
  `HideInCoverBehavior.cs` that uses `Action_SpawnEqsSensorChild` +
  `Action_DestroyEqsSensorChild` instead of `Action_MaintainEqsSensor`. The existing
  `HideInCover_BT` stays in place as a single-sensor reference; v2 is the canonical
  multi-sensor example for documentation / future starter-pack recipes.
- `Action_MoveToOptimalCover` (TASK-EQS-030) gains an `EqsSensorHandle` blackboard input
  so it can read from a child sensor's buffer rather than the agent's. Backwards compat:
  if the handle is `Entity.Null`, fall back to reading from `ctx.Self`.

**Scope (OUT):** Distributed multi-sensor (depends on TASK-EQS-038's replication rekey
landing). Recommend a separate `Eqs_DistributedMultiSensor_ReplicatesChildren` test as
a follow-up once TASK-EQS-038 is green.

**Success conditions:**
1. `Eqs_MultiSensor_OneAgentTwoConcurrentQueries` passes (offline editor).
2. `HideInCover_BT_v2` smoke test: spawn agent, inject threat, pump 500 ms, assert
   `LocomotionChannel.ActiveAction == ActionIdMoveTo` and the destination matches the
   child sensor's top result.
3. Existing `HideInCover_BT` test continues to pass (no regression).
4. `Action_MoveToOptimalCover` unit test with `EqsSensorHandle.IsValid == false` falls
   back to the parent-buffer path correctly.

