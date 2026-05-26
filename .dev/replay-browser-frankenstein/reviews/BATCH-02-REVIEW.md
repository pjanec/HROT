# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Dev Lead
**Verdict:** APPROVED
**New Debt Items:** D03, D04

---

## Summary

All 18 new tests pass. Build is clean (0 errors). Core implementations are correct.
Test quality is **good** — the critical `RBF_P3T3` suite is properly exercised with a
`CountingResolver` call-count mechanism and real-value assertions. One required test was
silently omitted (see D03). Developer insight answers (Q1-Q5) were not included in the
report (see D04).

---

## Task-by-task Findings

### RBF-P2T3 — Subsystem wiring

**Implementation:** Correct.
- `_manager` and `_activeRepo` are nullable fields, both cleared in `Initialize`.
- `LoadFdpViaManager` disposes old manager, creates new, subscribes `OnTimeChanged`,
  then calls `OnManagerTimeChanged()` immediately to bind `_activeRepo`.
- `OnManagerTimeChanged` looks up `LocalEntitiesProviderNodeId` in `Contexts` (safe
  TryGetValue guard) then calls `RebindActiveRepo`.
- `Shutdown` disposes `_manager` before the old `_context`.

**Tests:**
- `InitialState_ManagerIsNull` — verifies both fields are null post-Initialize. OK.
- `LoadOneFile_BindsActiveRepo` — creates a real `.fdp` file, loads it, then uses
  `Assert.Same` to verify `ActiveRepo` is exactly the `SandboxRepo` of the local
  entities provider node. Strong assertion. OK.
- `SeekAfterLoad_ActiveRepoRemainsCorrect` — calls `SetBaseWallTicks` after load to
  fire `OnTimeChanged`, verifies `ActiveRepo` is unchanged. Correctly tests the
  re-bind path through `OnManagerTimeChanged`. OK.

**No issues.**

---

### RBF-P3T1 — NetworkIdGuid

**Implementation:** Correct.
- `MemoryMarshal.Write(bytes, in value)` — CS9191 fix was applied correctly.
- `ToLong` uses `CreateReadOnlySpan(ref g, 1)` on the by-value parameter; safe.
- Encoding is deterministic: long packed into bytes 0-7, remainder zeroed.

**Tests:**
- `RoundTrips` is a `[Theory]` covering 0, MinValue, MaxValue, and negative values.
  Verifies `ToLong(From(v)) == v`. OK.
- `ProducesValidGuidString` — verifies `From(42).ToString()` is a parseable GUID string.
  Reasonable sanity check. OK.

**No issues.**

---

### RBF-P3T2 — FederatedGuidResolver

**Implementation:** Correct.
- Both maps are `null` initially and populated via hot-swap methods.
- `Resolve(Entity)` returns `"null"` literal on miss, never throws.
- `Resolve(string)` returns `Entity.Null` on miss, never throws.

**Tests:**
- `SaveMap_Hit` / `SaveMap_Miss` — standard hit/miss coverage. OK.
- `LoadMap_Hit` / `LoadMap_Miss` — standard hit/miss coverage. OK.
- `HotSwap_SaveMap` — swaps the map and verifies the new mapping is active and the
  old one is gone. OK.

**No issues.**

---

### RBF-P3T3 — ScenarioSerializer.DeserializeWith

**Implementation:** Correct overall. One design deviation documented as D03.

- SubsystemType header is never checked — OK, this is the whole point.
- Pass-1 (CreateEntity) is skipped; `preAllocated` is the source of entity handles.
- For an entity key in the DOM that is NOT in `preAllocated`: implementation **skips**
  (continues) instead of throwing. The TASK-DETAILS wording is "may throw", so this
  is within spec. The skip behavior is pragmatically correct for the TransientMasterBuilder
  use case where a partial preAllocated map is valid.
- `loadResolver` is forwarded to all `translator.Inject` calls AND to every
  `AutoSerializer.TryInject` call. Both paths are covered.
- Unknown component type names (not in `ComponentTypeRegistry`) **throw**
  `InvalidOperationException`. This is the same behavior as standard `Deserialize`.
  After `RepositoryPriming`, all component types in any valid `.fdp` file should be
  registered. Acceptable.

**Tests — CRITICAL assessments:**

| Test | Verdict |
|------|---------|
| `IgnoresSubsystemFilter` | PASS — verifies standard `Deserialize` returns 0 entities, then `DeserializeWith` injects the component AND checks the actual float value (X = 3f). Real assertion. |
| `InjectsComponentsViaCustomResolver` | PASS — round-trips `DummyPosition {1,2,3}` through the new overload. OK. |
| `AcceptsEntityNullFromResolver` | PASS — only pre-allocates entity A (not B), uses an empty-map resolver, asserts no throw, then checks `GuidedTarget.TargetId == Entity.Null`. Correct verification of the null-propagation path. |
| `ResolverReachesAutoSerializer` | PASS — uses `CountingResolver` that increments `ResolveStringCount` on each `Resolve(string)` call. After deserialization of a `GuidedTarget`-bearing entity, asserts `count > 0`. This is the precise call-tracking mechanism required. |
| `DefaultDeserializeStillThrowsOnMissingGuid` | PASS — tampers the DOM to insert a foreign GUID as TargetId, verifies standard `Deserialize` still throws. Regression guard is correct. |

**Missing test (D03):** `RBF_P3T3_DeserializeWith_InlineArrayHandleResolves` — required by
TASK-DETAILS, conditionally allowed by BATCH-02-INSTRUCTIONS (implement or document as
debt). Developer silently omitted both the test and the debt entry. Added as D03.

---

### RBF-P3T4 — BitMask512.BitwiseAndNot

**Implementation:** Correct.
- `_q0 &= ~other._q0` pattern across all 8 quads. Efficient and correct.
- `[AggressiveInlining]` applied consistently with other BitMask512 methods.

**Tests:**
- `AllBitsCovered` — sets bits 0, 63, 64, 511 (all 8 quads represented); claims all of
  them; asserts all are cleared after `BitwiseAndNot`. Covers all quads. OK.
- `EmptyClaimed_ReturnsCandidate` — empty claimed mask; asserts bits 1, 100, 300 survive.
  Verifies no-op case. OK.

**Minor gap:** No partial-overlap test (candidate has bits A+B, claimed only A, verify
B survives and A is cleared). Not blocking — the operation is a trivial bitmask expression
and the existing tests span all 8 quads. Acceptable.

---

### RBF-P3T6 — Extract RepositoryPriming

**Implementation:** Correct.
- `RepositoryPriming.RegisterDiscoveredComponents` reflects all non-dynamic,
  non-system assemblies.
- Finds `RegisterComponent<T>` via `IsGenericMethodDefinition` + `GetParameters().Length == 1`.
- `ComponentTypeRegistry.GetOrRegisterManaged(type)` is called before the reflection
  invocation — ensures the registry is populated even if the `Invoke` path fails.
- Exception swallowing in the inner loop is acceptable here (priming is best-effort).
- `ReplayBrowserContext` constructor now delegates to `RepositoryPriming`.

**Test:**
- `RegisterDiscoveredComponents_RegistersHarnessPosition` — clears registry,
  re-runs priming, verifies `HarnessPosition` (ComponentId 202) is usable via
  `SetComponent` + `HasComponent` + `GetComponent`. Integration-level test.
  This is sufficient to prove priming works end-to-end. OK.

**No issues.**

---

## Report Quality

**Missing:** Developer did not answer the Q1-Q5 developer insight questions in the report.
The report body ends without an Insights section. While this does not affect production code
quality, it wastes the opportunity to surface architecture observations. Added as D04.

---

## New Debt Items

| ID | Description | Priority | Target Batch |
|----|-------------|----------|--------------|
| D03 | `RBF_P3T3_DeserializeWith_InlineArrayHandleResolves` silently omitted — developer neither wrote the test nor added a DEBT-TRACKER entry as required. Need to determine if `FdpAutoSerializer` handles inline-array `Entity` fields through the supplied resolver; write the test if supported, otherwise document as "not supported". | P3 | BATCH-03 |
| D04 | Developer omitted Q1-Q5 insight answers from BATCH-02-REPORT. Not a production issue but reduces development visibility. | P3 | (process note, no code fix needed) |

---

## Commit Approval

**APPROVED.**

Commit BATCH-02 changes. Tick off RBF-P2T3, RBF-P3T1, RBF-P3T2, RBF-P3T3, RBF-P3T4,
RBF-P3T6 in TASK-TRACKER. Add D03 and D04 to DEBT-TRACKER.
