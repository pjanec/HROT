# BATCH-05 REVIEW

**Reviewer:** Dev Lead
**Status:** APPROVED
**Commit:** (pending)

---

## Test Results (Verified)

| Suite | Command | Result |
|-------|---------|--------|
| Integration (EQS filter) | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs"` | 15/15 PASS |
| Unit (EQS filter) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs"` | 27/27 PASS |

---

## TASK-EQS-012 — CoverPoint, ICoverProvider, ManualCoverProvider

**PASS.**

- `CoverPoint` struct: 24 bytes via `[StructLayout(LayoutKind.Sequential)]` with explicit `_pad0` (byte) and `_pad1` (ushort) padding. Verified by T-CP1.
- `ICoverProvider` interface correctly decorated with `[ComponentId(211)]` — required for `SetSingletonManaged<ICoverProvider>()` to locate the managed singleton slot. Pattern matches `IEqsTemplateRegistry`.
- `ManualCoverProvider`: linear scan using squared-distance comparison (`dx*dx + dy*dy <= radiusSq`) — correct, no sqrt needed.
- `GlobalComponentIds.ICoverProvider = 211` added correctly after `IEqsTemplateRegistry = 210`.

Tests: T-CP1 (size), T-CP2 (radius filter). Both pass and cover the success conditions from TASK-EQS-012.

---

## TASK-EQS-013 — CoverPointsGenerator, ILosService, CheapLineOfSightTest

**PASS.**

- `BlockedLosService` always returns `false` (all positions occluded) — correct Phase 3 stub.
- `CoverPointsGenerator` sets `EntityId = 0` for all positional candidates — correct.
- `CoverPointsGenerator` seeds `Score = rawPoints[i].Quality` — sensible baseline weighting.
- `CheapLineOfSightTest.ExecuteBatch` is `unsafe` to access `TargetMemory` fixed arrays — correct pattern.
- Bypass conditions: `mem.Count == 0` and `mem.ThreatScores[0] < sensor.ThreatThreshold` — both implemented correctly, placed before the candidate loop.
- Rejection sentinel is `-1L` — correct.
- `EntityId == 0` positional candidates are processed (no skip for 0) — correct, they're the main case.
- `Flags |= 1` set on covered candidates — correct.
- Skip `EntityId == -1L` (already rejected) — correct.

Tests: T-LOS1 (no threats bypass), T-LOS2 (below-threshold bypass), T-LOS3 (exposed reject), T-LOS4 (occluded kept + flag). All four cases explicitly tested with both bypass paths verified.

---

## TASK-EQS-015 — FindCoverFromTarget Template

**PASS.**

- `[EqsTemplate("f8a3c1d2-4e5b-4f6a-8c9d-2b1e3f4a5c6d")]` positional constructor — correct.
- `BlueprintId = 0x7F3A2B1Cu` — constant, unique, documented.
- `Build(ILosService los)` is static and pure. LOS service injected by caller for testability.
- Composition: `CoverPointsGenerator` + `CheapLineOfSightTest(los)` + `DistanceScoreTest`. Correct.
- `MaxCandidates = 32` — reasonable for cover-point queries.

T-FCT1: 3 cover points (1 exposed, 2 occluded), `MockLosService` that returns `true` for `x > 2`. After pump: `Count == 2`, both `EntityId == 0`, top result has higher score. All assertions correct.

T-FCT2: no threats (`Count=0`), `MockLosService` that always returns `true`. After pump: `Count == 3` (bypass triggered, all points survive). Correct verification of bypass path.

---

## Cross-Cutting Changes

### ICoverProvider Singleton Sync (EntityRepository.Sync.cs)

`SyncSingletonById(source, GlobalComponentIds.ICoverProvider)` added alongside `IEqsTemplateRegistry`. Required for background SoD snapshot to see the cover database. Pattern is identical to the BATCH-04 fix. Accepted.

### `[Collection("EqsIntegrationTests")]` on all EQS integration test classes

Pre-existing latent flaw: without a shared collection, all 15 EQS integration tests ran in parallel. Each creates an `EditorHarness` with a background 10 Hz solver thread, causing thread-pool saturation. Fix prevents timeout regressions.

This is a minimal, legitimate change. The `AGENTS.md` "minimize textual diffs" constraint applies to functional changes — fixing a test infrastructure fragility is explicitly necessary to maintain the health of the test suite. Accepted.

### Phase1Stub timeout 2000 → 5000 ms

Corrected to match all other EQS integration tests. Accepted.

---

## Issues

### P3 — ICoverProvider ComponentId in GlobalComponentIds couples core to toolkit

Same architectural concern as BATCH-04 `IEqsTemplateRegistry = 210`. `GlobalComponentIds.ICoverProvider = 211` in the core framework references an EQS toolkit concept. The pragmatic workaround is acceptable for now.

**Action:** Log to technical debt tracker alongside BATCH-04 P3 debt item.

---

## Summary

- All 15 integration tests pass (including both new T-FCT1, T-FCT2).
- All 27 unit tests pass (including new T-CP1, T-CP2, T-CG1, T-LOS1 through T-LOS4).
- No P1 or P2 issues.
- One P3 debt item added to existing tracker.
- BATCH-05 approved for commit.

**Suggested commit message:** `feat(eqs): cover points, LOS filter, FindCoverFromTarget template (BATCH-05)`
