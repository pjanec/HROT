# BATCH-37 Review

**Batch:** BATCH-37
**Reviewer:** Dev Lead
**Date:** (current)
**Verdict:** NEEDS FIXES

---

## Summary

Strong implementation overall. The Execute/ExecuteCore split is an excellent find — the closure
allocation root cause was non-obvious and the fix is clean. Structural, spatial, and lifecycle
tracker logic mirrors the reference implementation faithfully. Authority filtering is correct.
Collect-then-fire pattern is properly applied. Destruction-log reading is correct (no clearing).

One P1 issue: the lifecycle test does not cover the `NameSubstring` path as required by the
TASK-DETAIL success condition.

---

## Issues

### Issue 1 — P1: LifecyclePredicate test uses EcsHandle instead of NameSubstring [TASK-DETAIL mismatch]

**Location:** `DataBreakpointSystemStatefulTests.LifecyclePredicate_FiresOnBirth_AndOnDeath`

**Observation:**  
`TASK-DETAIL.md` UBP-P2T3 success condition states:

> `LifecyclePredicateDto(NameSubstring, "EnemyTank")` — spawn matching entity → hit, destroy it → second hit.

The implemented test uses `EntityIdentifierType.EcsHandle` + `entity.Index.ToString()`.
The `ReadEntityName` / `ReadStringField` / `MatchesLifecycleCriteria(NameSubstring)` code path
is not exercised by any test. This violates the TASK-DETAIL requirement.

**Required fix:**

1. Add a lightweight name component to the test file:
```csharp
[ComponentId(212)]
internal struct EntityLabel { public string Name; }
```

2. Rename the existing test to `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByHandle` (keep it,
   it verifies EcsHandle matching correctly).

3. Add a new test `LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring`:
   - Register `EntityLabel` in the repo.
   - Create entity E, add `EntityLabel { Name = "EnemyTank" }`.
   - Add lifecycle breakpoint: `NameSubstring`, `TargetValue = "EnemyTank"`,
     `NameComponentType = typeof(EntityLabel)`, `NamePropertyPath = "Name"`.
   - Execute → assert birth hit (IsPaused = true).
   - RequestContinue.
   - Execute again (no new entity) → assert no second birth hit.
   - DestroyEntity(E) → Execute → assert death hit (IsPaused = true again).

   This test covers: `ReadEntityName`, `ReadStringField`, and the `NameSubstring` branch in
   `MatchesLifecycleCriteria`.

4. Also add a negative variant in the same test or as a separate test:
   - Create entity F, add `EntityLabel { Name = "AlliedTank" }`.
   - Same breakpoint as above (TargetValue = "EnemyTank").
   - Execute → assert F does NOT trigger a hit (name doesn't contain "EnemyTank").
   - Verifies the `Contains` logic is used, not exact match.

**Note on managed string field in struct:**  
`EntityLabel { public string Name; }` is an unmanaged-looking struct but `string` is a reference
type. If `RegisterComponent<EntityLabel>` fails because `string` is not blittable, use a managed
component registration instead (`RegisterManagedComponent<EntityLabel>()`), or define the component
as a class (`internal sealed class EntityLabel { public string? Name; }`) and use
`GetManagedComponentByTypeId` path in `ReadEntityName`.

Alternatively, if the managed component path is complex to set up in tests, verify the fallback
`NameComponentType == null` path by using `TargetValue = entity.Index.ToString()` with
`IdentifierType = NameSubstring` and `NameComponentType = null` — this tests the fallback
`entity.ToString().Contains(...)` branch.

At minimum, ONE test must exercise the `NameSubstring` condition type as specified in TASK-DETAIL.

---

## Positive Observations

**Execute/ExecuteCore split (excellent):**  
The closure-allocation root cause was subtle and correctly diagnosed. The split is minimal and
clean — exactly the right approach. The comment in `Execute` explaining why no lambdas are
allowed there is valuable.

**Structural tracker:**  
`ComputeEffectivePresence` mirrors `RecordingSearchService` exactly. Authority/ghost/any-auth
cases all handled. Destruction log pruning of `knownSet` is correct.

**Spatial tracker:**  
`ReadPosition2D` / `ReadFloatField` via reflection mirrors the reference implementation.
`IsInBounds` is a clean inline bounds check (no `BoundingBox2D.Contains` dependency risk).
Dwelling test correctly calls Execute 3 times with entity inside and asserts hitCount stays at 1.

**Lifecycle tracker:**  
Birth detection via `MaxEntityIndex` iteration. Death detection via `GetDestructionLog()`.
Destruction log is read-only (not cleared). All correct.

**Authority test:**  
`AuthorityRequirement_RequireAuthority_FiltersGhostMutations` correctly verifies:
- Component added without authority → no hit (ghost path)
- `SetAuthority<WeaponState>(entity, true)` → hit fires

**HasMountedDelegates extension:**  
Now includes `HasStatefulTrackers`. The gate correctly prevents any work when no breakpoints
of any kind are mounted.

**Collect-then-fire pattern:**  
All three tracker evaluations build `hits` list first, then call `OnHit` after the loops.
No rewind-mid-iteration risk.

---

## After Fixes

After applying the NameSubstring test fix, there are no other blockers. The batch will be
APPROVED and ready for commit.
