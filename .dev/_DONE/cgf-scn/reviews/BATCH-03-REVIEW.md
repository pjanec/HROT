# BATCH-03 Review

**Batch:** BATCH-03
**Tasks:** TASK-C004
**Reviewer:** Dev Lead
**Date:** 2026-04-21
**Decision:** ✅ APPROVED

---

## Summary

`StagingEntityExtractor` implemented correctly. All 12 success conditions tested.
Build clean, 391/391 SimHost tests pass. A notable technical finding was that
`catch (TargetInvocationException)` was insufficient for the registration loop —
the developer correctly identified and fixed this.

---

## Implementation Review

### TASK-C004 — StagingEntityExtractor ✅

- Static exclusion mask uses `GlobalComponentIds` named constants ✅
- Translator mask ORed into instance mask at construction ✅
- `PartMetadata` (ID 55) used for root-entity detection, NOT `CommanderId` ✅
- `ChildComponentOverrides` harvesting with child-specific mask (excludes `PartMetadata` itself) ✅
- `ActiveMissionPlan` behavior param remapping via `ScenarioBehaviorRemapper` ✅
- `EpisodeTag` appended last ✅
- Staging repo disposed in `finally` block ✅
- `PreAllocatedNetworkId` from Pass 1 lookup ✅

The `ScenarioSerializer.Translators` property addition in FDP is minimal and
non-breaking (exposes existing private array as `IReadOnlyList`).

---

## Test Quality Assessment

13 tests cover all 12 success conditions. The test infrastructure helpers
(inline translators, `DisposableEntityRepository` wrapper, `StubIdAllocator`)
are well-structured and reusable for future tests.

The discovery that `catch (TargetInvocationException)` was too narrow is a
valuable codebase insight — noted in debt tracker.

---

## Debt Items Identified

| ID | Priority | Description |
|----|----------|-------------|
| D-004 | P3 | `StagingEntityExtractor.RegisterAllGlobalTypesInRepo` uses `catch (Exception)` broadly to swallow component type registration failures. Should ideally emit a debug log for each type-registration failure to ease future diagnostics. |

---

## Git Commit

```
feat(cgf-scn): Phase 2 - StagingEntityExtractor + test infrastructure (TASK-C004)
```
(Committed at 89b34ea)

---

## TASK-TRACKER Update

- [x] TASK-C004 — done
