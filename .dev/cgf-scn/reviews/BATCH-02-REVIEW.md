# BATCH-02 Review

**Batch:** BATCH-02
**Tasks:** DEBT-D002, TASK-C013, TASK-C005 (a–d)
**Reviewer:** Dev Lead
**Date:** 2026-04-21
**Decision:** ✅ APPROVED

---

## Summary

All tasks delivered correctly. The 3 stale test assertions are fixed. The DTO
extension and gateway logic align with the design. The expression-tree compiler
correctly avoids reflection on the hot path. Build clean, all relevant tests pass.

---

## Implementation Review

### DEBT-D002 — Stale System-Count Assertions ✅

Simple fix, correct approach — updated to actual counts rather than deleting
assertions.

### TASK-C013 — EntityCreationRequest Extension ✅

- Both new properties are `init`-only, preserve DTO immutability
- Sentinel `!= 0` gateway is correct (ID `0` is never returned by the allocator)
- `ProcessPendingRequest` child loop uses `AddRange` on the existing list: correct,
  no new allocation

### TASK-C005 ✅

- `RemapNetworkIdAttribute` — minimal marker attribute, correct `AttributeUsage`
- DTOs — `[JsonPropertyName]` applied; properties match camelCase JSON keys
- `BehaviorParamRemapperCompiler` — reflection only in `BuildDelegate<T>()`, not
  in the returned lambda; `ConcurrentDictionary` cache is thread-safe; `int` 
  narrowing-cast branch handled
- `ScenarioBehaviorRemapper` — `InvalidOperationException` on duplicate; unknown
  behaviorId passes through unchanged

---

## Test Quality Assessment

C013 tests cover all 6 success conditions including the "fall-through" edge cases.
C005c delegate-caching test uses `CompileCallCount` internal counter — acceptable
for verifying caching without coupling to implementation internals excessively.

---

## Debt Items Identified

| ID | Priority | Description |
|----|----------|-------------|
| D-003 | P3 | `BehaviorParamRemapperCompiler` silently skips `[RemapNetworkId]`-annotated properties that have no setter (read-only). Should at minimum log a warning at compile time. |

---

## Git Commit

```
feat(cgf-scn): Phase 2 infrastructure - EntityCreationRequest extension + remapping (TASK-C013, C005, DEBT-D002)
```

---

## TASK-TRACKER Update

- [x] TASK-C013 — done
- [x] TASK-C005 — done
- DEBT-D002 — resolved ✅
