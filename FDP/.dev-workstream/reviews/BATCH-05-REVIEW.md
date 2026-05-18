# BATCH-05 Review

**Batch:** BATCH-05  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED — with two P1 architectural changes mandated by the architect, both become Corrective 0 for BATCH-06

---

## Test Quality Assessment (CODE-STANDARDS.md §0)

### PerceptionComponentTests (5 tests)

All five tests check specific field values, not just absence of exceptions. ✅

**Strength:** `AddOrUpdateTarget_WhenTableFull_EvictsLowestScoringSlot` walks the array to confirm both the evicted entity is gone *and* the new entity is present — correct bilateral assertion. ✅

**One weak assertion:** `AddOrUpdateTarget_SameEntityTwice_AccumulatesScoreCountStaysOne` asserts `ThreatScores[0] == 65f`. After the second add, insertion-sort runs and the single entry stays at index 0 (trivially, because there's only one entry). The test doesn't verify whether sort-stability is preserved with two entries at different scores — this is fine for now but worth noting if sort behaviour becomes more complex.

### AudioPerceptionSystemTests (3 tests)

**Test 1** — asserts `mem.Count == 1` and `mem.EntityIds[0] == (long)source.Index`. Specific entity ID check. ✅

**Test 2** — confirms hearing-range cutoff with count == 0. Correctly uses `Intensity=60` (large enough to include the listener in the fallback radius scan) but `HearingRange=30` (smaller than distance 50). Distinguishes the two filter layers. ✅

**Test 3** — two-listener setup, asserts one updated and one not. Good separation of concerns. ✅

**Issue — raw `SourceEntityIndex = 99` in Test 2 (P3):** The source entity index `99` is a magic number with no corresponding entity in the world. Since `AudioPerceptionSystem` doesn't validate whether the source entity is alive (it only updates the listener's `TargetMemory`), this is functionally harmless, but it means the stored entity ID in `TargetMemory` would point to a dead or non-existent entity. In this test `Count == 0` so it's never observed, but if this pattern were copied to a positive-path test, it would produce an undetectable stale-reference bug in `TargetMemory`. Track as P3, fix alongside DEBT-009.

### VisionBroadphaseSystemTests (4 tests)

**Test 1** — asserts `events.Length == 1`, `events[0].ObserverEntityIndex == observer.Index`, and `events[0].TargetEntityIndex == target.Index`. Both entity indices verified. ✅ Strongest test in the batch.

**Tests 2–4** — three distinct exclusion criteria tested separately: same faction, beyond range, outside FOV cone. Each is a separate test with a clear setup. ✅ This correctly follows the prescribed split from BATCH-05 instructions.

**One concern (P2):** Tests 2–4 emit `events.Length == 0` assertions — correct. But tests 2 and 3 use the magic number `0.866f` for `FieldOfViewCos` instead of `MathF.Cos(MathF.PI / 6f)`. Only Test 1 and Test 4 use the expression form. Inconsistency — should be a named constant or the expression form throughout. Add to DEBT tracker.

### ThreatEvaluationSystemTests (1 test)

**Strength:** The expected value is computed from the constant: `const float expected = 100f * (1f - PerceptionConstants.ThreatScoreDecayPerSecond * 1.0f)`. This means if the constant changes, the assertion automatically tracks. ✅

**Gap (P2):** Only one test. Missing:
- A test that verifies `ThreatEvaluation` adds a `TargetVisibleEvent` boost (score is increased, not only decayed).
- A test that verifies zero-score entries are removed from `TargetMemory` (if scores decay to zero, stale targets should be evicted; or if this is a future feature, document it as such).

Add to DEBT tracker.

### LosRequestBatchingSystemTests (2 tests)

**Test 1** — asserts count == 2, and both `ObserverEntityIndex` and `TargetEntityIndex` for each event. ✅

**Test 2** — production mode correctly asserts zero `TargetVisibleEvent`s. Covers the negative branch without needing a mock. ✅

---

## Code Quality Assessment

**`PerceptionComponents.cs`:** All fixed arrays use `PerceptionConstants.MaxTrackedTargets`. ✅ `AddOrUpdateTarget` is a static method on the struct — correct pattern for zero-allocation mutation. ✅ Insertion sort on 4 elements is fine and annotated.

**`AudioPerceptionSystem.cs`:** `GetComponentRW<TargetMemory>` for in-place mutation. ✅ `MaxQueryResults = 256` named constant. ✅ Two-path design (grid / fallback) is clean and the fallback is correctly documented.

**Critical finding:** Line 66 — `Entity listener = World.GetEntity(candidateIdx)`. This is the exact misuse the architect flagged. `SpatialHashGrid.QueryNeighbors` returns raw `int` indices; the system reconstructs an `Entity` via `World.GetEntity(int)`. This is the `GetEntity(int)` anti-pattern. This means: (1) `SpatialHashGrid` stores raw ints, not full `Entity` structs — stale-reference risk; (2) `EntityRepository.GetEntity(int)` is public — bad API exposure. **Both must be fixed (P1, mandated by architect).** → BATCH-06 corrective.

**`VisionBroadphaseSystem.cs`:** Forward vector uses `Vector3.UnitX` — correct. ✅ FOV check via `Vector2.Dot`. Pure brute-force but correctly justified for Phase 2 scale. ✅ ECB used for all writes. ✅

**`ThreatEvaluationSystem.cs`:** Snapshot copy → local mutation → `ecb.SetComponent` read-modify-write pattern. ✅ Matches Q3 description exactly.

---

## Architect Decisions — Integration into Work Items

Three architectural decisions from the post-batch discussion with the architect:

### Architect Decision 1: Local Rebuild Pattern for Async Perception (P1)

**What:** `PerceptionModule` must own a private `SpatialHashGrid`, rebuilt each tick by a new `LocalGridBuilderSystem` that runs first inside the module. `VisionBroadphaseSystem` then queries this private grid.

**Why:** The main-thread's `SpatialHashGrid` contains `NativeArray<T>` fields (pointers). Sharing this across the async boundary = shallow copy = data race on the same physical memory. The local rebuild is fast (< 0.2ms for 10k entities) and fully thread-safe.

**Status:** P1 → BATCH-06 corrective.

### Architect Decision 2: `SpatialHashGrid` stores full `Entity` structs, not raw ints (P1)

**What:** Change `GridValues: NativeArray<int>` → `GridValues: NativeArray<Entity>`. `Add(Entity, Vector2)` instead of `Add(int, Vector2)`. `QueryNeighbors` returns `Span<(Entity entity, Vector2 pos)>` instead of `Span<(int, Vector2)>`.

**Why:** Raw entity indices bypass generational safety — the entire point of `Entity.Generation`. Callers cannot detect stale references. Every system currently calling `World.GetEntity(int)` after a grid query is a latent bug. The 4-byte cost per grid entry (int → Entity/8 bytes) is negligible.

**Impact:** Breaking change to `SpatialHashGrid`. All callers must be updated: `SpatialHashSystem`, `AudioPerceptionSystem`, `QueryFallback` (returns raw int tuples — must change to Entity tuples), and tests.

**Status:** P1 → BATCH-06 corrective, BEFORE any new code is added that stores raw indices.

### Architect Decision 3: `EntityRepository.GetEntity(int)` made `internal` (P1)

**What:** Change `public Entity GetEntity(int index)` to `internal Entity GetEntity(int index)` in `EntityRepository.cs`. Add an XML doc comment explaining the only valid use cases (interop with C++ libs, low-level kernel scanning).

**Why:** A public `GetEntity(int)` is a "loaded gun" — it implicitly encourages storing raw indices. Making it internal forces gameplay and toolkit code to always use full `Entity` structs. Engine-internal uses (physics interop, `EntityQuery` bit scanning) are in the same assembly and retain access.

**Status:** P1 → BATCH-06 corrective. This change will produce compiler errors in any production code currently calling `World.GetEntity(int)` — AudioPerceptionSystem must be fixed at the same time (part of Decision 2 ripple).

---

## Issues Table

| # | Sev | Description | Target |
|---|---|---|---|
| 1 | P1 | `SpatialHashGrid` stores raw `int` indices — stale-reference risk; must store full `Entity` structs | BATCH-06 |
| 2 | P1 | `EntityRepository.GetEntity(int)` is public — must be `internal` | BATCH-06 |
| 3 | P1 | `VisionBroadphaseSystem` brute-force O(N×M); needs local rebuild pattern with private grid | BATCH-06 |
| 4 | P2 | `VisionBroadphaseTests` uses raw `0.866f` literal in tests 2 & 3 instead of named constant or expression | BATCH-06 |
| 5 | P2 | `ThreatEvaluationSystemTests` has only 1 test; missing boost-path test and zero-score eviction | BATCH-06 |
| 6 | P3 | `AudioPerceptionSystemTests` Test 2 uses `SourceEntityIndex = 99` (non-existent entity); harmless but bad pattern | BATCH-06 |

---

## Verdict

**Status: APPROVED with mandatory P1 correctives in BATCH-06.**

Phase 2 is functionally complete for Phase 2 entity counts. The three P1 issues are not correctness bugs at current scale — the brute-force works; the raw-int grid works when entities don't die. But building Phase 3 navigation executors on top of a stale-reference-prone grid, or on a public `GetEntity(int)`, would entrench the problem. Fix now, before Phase 3 begins.

---

## 📝 Commit Message

```
feat: FDP.Toolkit.Perception — Phase 2 complete (BATCH-05)

BCS-P2-T1 — T4 implemented

New: PerceptionConstants, Faction, PerceptionReceptor, TargetMemory
     (AddOrUpdateTarget: accumulate/add/evict-lowest/insertion-sort)
New: AudioStimulusEvent, LosCheckRequestEvent, TargetVisibleEvent
New: AudioPerceptionSystem (main-thread, GetComponentRW, grid+fallback)
New: VisionBroadphaseSystem (async IModuleSystem, brute-force O(N×M),
     UnitX forward, ECB events; grid path deferred to BATCH-06)
New: ThreatEvaluationSystem (snapshot RO + ECB, score decay by constant)
New: PerceptionModule (SlowBackground 10Hz)
New: LosRequestBatchingSystem (mock/production mode via constructor flag)
New: FDP.Toolkit.Perception.Tests (15 tests)

Correctives from BATCH-04:
- HsmTickSystemTests: named EventXId const + nameof anchor
- BehaviorIngressSystemTests Test 4: added BehaviorInstanceId == 0 assertion
- StandardSystemGroups.cs: InputSystemGroup ordering doc comment + TODO

Full solution: 0 build errors; all tests green

Known: SpatialHashGrid stores int indices (not Entity); GetEntity(int) is
public; local rebuild pattern pending — all addressed in BATCH-06.
```

---

**Next Batch:** BATCH-06 (P1 architectural correctives: Entity grid, internal GetEntity, local rebuild pattern + Phase 2 test gaps)
