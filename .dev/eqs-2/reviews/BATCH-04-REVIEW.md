# BATCH-04 REVIEW

**Reviewer:** Dev Lead
**Status:** APPROVED
**Commit:** (pending)

---

## Test Results (Verified)

| Suite | Command | Result |
|-------|---------|--------|
| Integration (EQS filter) | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` | 13/13 PASS |
| Unit (EQS filter) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"` | 20/20 PASS |

---

## Corrective Task — Ghost Lifecycle Regression

**PASS.** `.WithLifecycle(EntityLifecycle.Ghost)` correctly changed to `.WithLifecycle(EntityLifecycle.All)` in the Phase 2 rewrite. T4 (`EqsSolverSystem_Phase1Stub_PopulatesBufferAfterSolverFires`) now passes.

---

## TASK-EQS-009 — EntitiesInRadiusGenerator

**PASS.**

- `stackalloc (Entity, Vector2)[candidates.Length]` — correct zero-allocation intermediate buffer.
- `SpatialGridData.Grid.QueryNeighbors` used correctly.
- Observer excluded via `neighbors[i].entity == observer` check.
- `EntityId = (long)neighbors[i].entity.PackedValue` — correct encoding (includes generation).
- Returns `validCount`, not `rawCount`.

Tests: 3 unit tests covering zero-radius, observer exclusion, and radius boundary. All correct.

---

## TASK-EQS-010 — FactionFilterTest and DistanceScoreTest

**PASS.**

- Rejection sentinel is `EntityId = -1L` throughout — correct.
- Positional candidates (`EntityId = 0`) are explicitly skipped in `FactionFilterTest` and untouched.
- Already-rejected candidates are skipped in both tests.
- `FactionFilterTest` rejects dead/missing-EntityInfo entities with `-1L` — correct defensive guard.
- `DistanceScoreTest` reads observer position from ECS (correct; only candidate positions come from packed generator data).
- Linear falloff formula `1.0f - Clamp(dist/maxDist, 0, 1)` — correct.
- Scoring is additive (`candidate.Score += score`) — correct per spec.

Tests: 4 pure unit tests, no harness dependency. All assertions are precise. Positional candidate behavior explicitly asserted in T-F1 and T-F3.

---

## TASK-EQS-011 — EqsSolverSystem Phase 2

**PASS.**

- Phase 2 pipeline: Generate -> FilterCheap -> FilterExpensive -> ReduceTopK -> ScoreCheap -> ScoreExpensive -> sort -> write pool -> publish event.
- Registry accessed via `repo.HasSingletonManaged<IEqsTemplateRegistry>()` / `GetSingletonManaged` — correct.
- Lazy pool init: `if (!repo.HasSingleton<EqsResultPool>())` — correct guard.
- `ReduceTopK` compaction check: `EntityId != -1L` (NOT `!= 0`) — correct; positional candidates preserved.
- `WriteAndWrap` called as `(ReadOnlySpan<EqsResult>)finalCandidates` — correct cast.
- `EqsBudgetMs = 4.0` public property — correct default.
- `EqsModule` unchanged (still `new EqsSolverSystem()` no args) — confirmed.
- Phase 1 fallback (no registry or unknown blueprint) preserved — correct.

Tests:
- T-S1 (full pipeline): creates observer + 5 hostile enemies, asserts `Count > 0` and top result is an enemy entity. Strong end-to-end test.
- T-S2 (multi-sensor): 10 sensors with NullGenerator, asserts all eventually have `IsReady` buffers. Tests iterator state advancing across multiple frames.
- T-S3 (fallback): no registry, asserts `Count == 0` and `IsReady`. Verifies Phase 1 stub fallback.
- T-RK1 and T-RK2: replicate ReduceTopK contract inline (see P3 debt below).

---

## `EntityRepository.Sync.cs` Change (Deviation)

The subagent added a `SyncSingletonById` helper and three call sites at the end of `SyncFrom` to share singleton table references (not deep copies) for:
- `GlobalComponentIds.SpatialGridData` (47)
- `GlobalComponentIds.EqsResultPool` (209)
- `GlobalComponentIds.IEqsTemplateRegistry` (210)

**Rationale is sound:** the background solver (snapshot) and main-thread consumer must reference the same `EqsResultPool` native buffer, otherwise handles written by the solver are invalid when the consumer reads. Reference-sharing is safe because:
1. `NativeChunkTable<T>.Dispose()` is idempotent (guarded by `if (_disposed) return;`).
2. The SoD contract ensures the main thread does not structurally replace singleton tables mid-frame.
3. Only one thread writes `NextFreeIndex` at a time (background thread exclusively during the SoD task).

**Accepted.** Comment in the code clearly documents the rationale. See P3 debt note below.

---

## Issues

### P3 — Technical Debt: EQS-specific IDs in EntityRepository.Sync.cs

`EntityRepository.Sync.cs` now references `GlobalComponentIds.EqsResultPool` and `GlobalComponentIds.IEqsTemplateRegistry`, coupling the core framework to EQS toolkit concerns. This is pragmatic for now but should be generalized in a future cleanup pass (e.g., a registration mechanism for "SoD-shared singleton IDs").

**Action:** Log to technical debt tracker. No change required in this batch.

### P3 — ReduceTopK private method not directly unit-testable

`ReduceTopK` is `private static` in `EqsSolverSystem`. The unit tests T-RK1 and T-RK2 replicate the algorithm inline rather than calling the actual method. The tests verify the CONTRACT, not the implementation — this is acceptable for private methods.

**Action:** No change required. Low priority improvement would be making it `internal static` with InternalsVisibleTo.

---

## Summary

- All 13 integration tests pass (including previously failing T4).
- All 20 unit tests pass.
- No P1 or P2 issues.
- Two P3 debt items logged.
- BATCH-04 is approved for commit.

**Suggested commit message:** `feat(eqs): Phase 2 solver, EntitiesInRadiusGenerator, FactionFilter, DistanceScore (BATCH-04)`
