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

