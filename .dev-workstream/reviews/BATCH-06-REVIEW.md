# BATCH-06 Review

**Batch:** BATCH-06  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED — clean batch; no new P1 issues; two P2 items for BATCH-07

---

## Test Quality Assessment (CODE-STANDARDS.md §0)

### SpatialHashGrid / SpatialHashSystem tests (CarKinem.Tests)

**`SpatialHashGrid_QueryNeighbors_ReturnsFullEntity_NotRawIndex`:** Verifies both `entity.Index == 5` and `entity.Generation == 3` — i.e., the full generational identity, not just the index. ✅ This is exactly the right test design: without the generation check, a raw-int implementation with trivial `Entity(index, 0)` construction would still pass.

**`SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState`:** Resolves DEBT-001. Verifies the entity is returned from `QueryNeighbors` at the correct position. ✅

### VisionBroadphaseSystemTests (Perception.Tests) — 5 tests (was 4)

**`VisionBroadphase_UsesLocalGrid_DoesNotBruteForce`:** Setup design is rigorous — only `targetA` is in the local grid; `targetB` is live in the world with identical faction/range/FOV. The assertion `count == 1` with `TargetEntityIndex == targetA.Index` cannot pass by coincidence. ✅ This is the strongest isolation test in the whole codebase.

`0.866f` literals replaced with `MathF.Cos(MathF.PI / 6f)` throughout. ✅

### ThreatEvaluationSystemTests — 3 tests (was 1)

**`ThreatEvaluation_BoostsScore_OnTargetVisibleEvent`:** Asserts score ≥ 50 after event — checks the right branch. ✅

**`ThreatEvaluation_ZeroScoreEntry_IsRetained`:** Documents the retention policy explicitly. The fact that the test is named `_IsRetained` (not `_IsEvicted`) means the policy was deliberately chosen — entities with zero score stay in the memory until overwritten by higher-threat targets. This should be verified against the DESIGN.md intent; if the design intends zero-score entries to be garbage-collected, the policy is wrong. Flag as **P2 — verify retention policy matches design intent**.

### NavigationActionTests — 7 tests

**Size tests:** Each uses `sizeof(T) <= 32` with a descriptive failure message quoting the actual size. ✅ Using `sizeof` (compile-time) rather than `Unsafe.SizeOf<T>()` is correct for `unmanaged` structs.

**`AllNavigationActionIds_AreDistinct`:** Uses a `HashSet` to test distinctness — correctly handles the case where IDs happen to be consecutive integers but one was accidentally duplicated. ✅

**`FleeParams_ContainsFullEntityStruct_NotRawIndex`:** Uses `typeof(FleeParams).GetField(...).FieldType == typeof(Entity)` via reflection AND asserts `sizeof(Entity) == 8`. The double check (type AND size) catches both a wrong-type bug and a future ABI change where Entity's layout shifts. ✅ Excellent test design.

**One gap (P2):** There is no test asserting that `NavigationConstants.FrustrationTickThreshold == 120` is used correctly downstream. The constant exists and is documented, but since `MoveToExecutor` doesn't exist yet, there's nothing to tie the constant to. When BCS-P3-T2 is implemented, add a test that reads `NavigationConstants.FrustrationTickThreshold` in the frustration-guard assertion — not a hardcoded `120`. This prevents the "constant changed but test uses old value" drift.

---

## Code Quality Assessment

**`NavigationActions.cs`:** All structs are `[StructLayout(LayoutKind.Sequential)]`, all unmanaged. The `FleeParams` XML doc explicitly calls out the `IsAlive(Threat)` guard requirement — this is proactive documentation that will prevent future bugs in `FleeExecutor`. ✅

**`NavigationConstants.cs`:** All five constants named with XML doc. The frustration-guard constants include a real-world anchor (`120 ticks ≈ 2 seconds at 60 Hz`) — good. ✅

**Q1 answer (DEBT-010 verification):** Only 2 compiler errors after making `GetEntity(int)` internal — both in toolkit code, both fixed cleanly by using the entity already in the tuple. Zero workarounds. ✅ The architectural refactor worked exactly as intended.

**Q4 answer (stale FleeParams.Threat):** The answer correctly identifies `view.IsAlive(params.Threat)` as the guard, places it at the start of each `Execute` tick (not just `OnEnter`), and explains the generational check mechanism. The explicit statement "this check must be made every tick, not just on entry" is the correct design. ✅ — This must be enforced in the `FleeExecutor` implementation test for BCS-P3-T3.

**Minor production code note (P3):** `NavigationActions.cs` has the comment `// 3 bytes of implicit Sequential padding (aligns struct to 4-byte boundary)` on `FollowRouteParams`. This is good for documentation but is actually wrong — `StructLayout(LayoutKind.Sequential)` with a `byte` after an `int` produces 3 bytes of padding to bring the total to 8 bytes for 4-byte alignment. The comment says "aligns struct to 4-byte boundary" but the struct total is 8 bytes, not 4. Trivially harmless but confusing to future maintainers. Fix when touching this file.

---

## Issues Table

| # | Sev | Description | Target |
|---|---|---|---|
| 1 | P2 | `ThreatEvaluation_ZeroScoreEntry_IsRetained` — verify documented retention policy matches DESIGN.md §4.3 intent; if design intends zero-score eviction, the test name and implementation are both wrong | BATCH-07 |
| 2 | P2 | NavigationActionTests missing frustration-guard constant linkage test — add when `MoveToExecutor` is implemented (BCS-P3-T2) | BATCH-07 (MoveToExecutor batch) |
| 3 | P3 | `FollowRouteParams` padding comment is misleading ("aligns to 4-byte boundary" but struct is 8 bytes) | Any batch touching NavigationActions.cs |

---

## Verdict

**Status: APPROVED. Phase 3 started (T1 done).**

This is the cleanest batch since BATCH-01. The P1 architectural correctives were executed exactly as the architect specified, with zero workarounds and zero residual raw-index usage. The grid isolation test is the most sophisticated in the codebase — it's a model for future async-boundary tests. Carry three minor items to BATCH-07.

---

## 📝 Commit Message

```
fix+feat: Entity-safe SpatialHashGrid + local rebuild pattern + Navigation stubs (BATCH-06)

P1 Correctives (DEBT-009, DEBT-010, DEBT-011):
- SpatialHashGrid.GridValues: NativeArray<int> → NativeArray<Entity>
- SpatialHashGrid.Add: (int,Vector2) → (Entity,Vector2)
- SpatialHashGrid.QueryNeighbors: Span<(int,Vector2)> → Span<(Entity,Vector2)>
- SpatialHashSystem, CarKinematicsSystem, AudioPerceptionSystem: updated to
  use entity directly from tuple — zero GetEntity(int) calls remain in toolkits
- EntityRepository.GetEntity(int): public → internal + XML doc
  (2 compiler errors fixed; both were correct elimination of raw-index reconstruction)
- LocalGridBuilderSystem: new async IModuleSystem; rebuilds private grid from snapshot
- PerceptionModule: owns private SpatialHashGrid (Persistent allocator); IDisposable
- VisionBroadphaseSystem: constructor-injected grid; brute-force eliminated

Test additions:
- SpatialHashGrid_QueryNeighbors_ReturnsFullEntity_NotRawIndex (generational check)
- SpatialHashSystem_IndexesEntity_WithSimTransformButNoVehicleState (DEBT-001)
- VisionBroadphase_UsesLocalGrid_DoesNotBruteForce (isolation proof)
- ThreatEvaluation_BoostsScore_OnTargetVisibleEvent (DEBT-013)
- ThreatEvaluation_ZeroScoreEntry_IsRetained (DEBT-013, retention policy)
- AudioPerceptionSystemTests Test 2: real entity index (DEBT-014)
- VisionBroadphaseSystemTests: MathF.Cos(PI/6) replaces 0.866f (DEBT-012)

BCS-P3-T1 — FDP.Toolkit.Navigation:
- NavigationConstants: ActionId{MoveTo,Flee,FollowRoute,FollowRoadGraph},
  FrustrationTickThreshold, FrustrationSpeedThreshold
- NavigationActions: MoveToParams(16B), FleeParams(16B,Entity Threat),
  FleeState(4B), FollowRouteParams(8B), FollowRoadGraphParams(8B)
- NavigationActionTests: 7 tests (size, distinctness, FleeParams.Threat type guard)

Full solution: 0 build errors, 1269 passed, 3 skipped
```

---

**Next Batch:** BATCH-07 (BCS-P3-T2 MoveToExecutor, BCS-P3-T3 FleeExecutor, BCS-P3-T4 FollowRoadGraphExecutor, BCS-P3-T5 FollowRouteExecutor)
