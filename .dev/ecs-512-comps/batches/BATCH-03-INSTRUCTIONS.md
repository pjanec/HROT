# BATCH-03: Phase 4+5 — EntityQuery Hot-First Traversal + EntityRepository Split Access

**Batch Number:** BATCH-03
**Tasks:** TASK-E006 (EntityQuery/QueryBuilder hot-first), TASK-E007 (EntityRepository split access)
**Phase:** Phase 4 (Query Engine) + Phase 5 (Repository Layer)
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-02 (completed, committed)

---

## Onboarding & Workflow

### Developer Instructions

BATCH-02 completed the `EntityIndex` rewrite. `EntityQuery` and `EntityRepository` were updated
in BATCH-02 only to the extent required for compilation. This batch completes the proper Phase 4
and Phase 5 upgrades:

- **Phase 4:** `EntityQuery.MoveNext()` is rewritten to the hot-first two-stage check using the
  new split API. `QueryBuilder` internal masks are already `BitMask512`; verify no regressions.
- **Phase 5:** `EntityRepository.cs` and `EntityRepository.Sync.cs` properly split all
  `GetHeader` usages into `GetComponentMask` and `GetMetadata`; `GetRecordableMask`,
  `GetSnapshotableMask`, and `GetSaveableMask` return `BitMask512`.

Also fix two debt items from the BATCH-02 review:
- **D003:** Add the missing "Mask Independence" EntityIndex test.
- **D005:** Replace `Unsafe.As<BitMask512, BitMask256>` usages in `ScenarioSerializer.cs` and
  `ImGui/EntityInspectorPanel.cs` with proper `BitMask512` API.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Onboarding:** `.dev/ecs-512-comps/ONBOARDING.md`
3. **Design:** `.dev/ecs-512-comps/DESIGN.md` — "Phase 4: Query Engine" and "Phase 5: Repository Layer" sections.
4. **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E006 and TASK-E007 sections.
5. **BATCH-02 Review:** `.dev/ecs-512-comps/reviews/BATCH-02-REVIEW.md` — understand debt items.
6. **Debt Tracker:** `.dev/ecs-512-comps/DEBT-TRACKER.md`
7. **Code Standards:** `.github/skills/CODE-STANDARDS.md`

### Source Code Location

- **Primary Work Area:** `FDP/Engine/Fdp.Core/`
- **Test Project:** `FDP/Engine/Fdp.Core.Tests/`
- **Solution:** `FDP/FDP.sln`

### Build and Test Commands

```
cd FDP
dotnet build FDP.sln -c Debug

cd FDP/Engine/Fdp.Core.Tests
dotnet test
```

### Report Submission

**When done, submit your report to:**
`.dev/ecs-512-comps/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/ecs-512-comps/questions/BATCH-03-QUESTIONS.md`

---

## Context

After BATCH-02, the hot/cold split is fully in place in `EntityIndex`. However:

- `EntityQuery.MoveNext()` may still not follow the exact hot-first two-stage pattern from
  DESIGN.md Phase 4 (it was only partially updated in BATCH-02 to compile).
- `EntityRepository.Sync.cs` still returns `BitMask256` from `GetRecordableMask`,
  `GetSnapshotableMask`, and `GetSaveableMask` — these must return `BitMask512`.
- Some callers still use `Unsafe.As<BitMask512, BitMask256>` projections that should be proper API.

---

## Corrective Task 0a (D003): Add Mask Independence test

**File:** `FDP/Engine/Fdp.Core.Tests/EntityIndexHotColdTests.cs`

Add a test verifying that setting a component bit on entity A does NOT set that bit on entity B:

```csharp
[Fact]
public void HotMasks_AreIndependentPerEntity()
{
    using var index = new EntityIndex();
    var a = index.CreateEntity();
    var b = index.CreateEntity();

    index.GetComponentMask(a.Index).SetBit(400);

    Assert.True(index.GetComponentMask(a.Index).IsSet(400));
    Assert.False(index.GetComponentMask(b.Index).IsSet(400),
        "Setting bit 400 on entity A must not affect entity B's mask");
}
```

---

## Corrective Task 0b (D005): Replace `Unsafe.As<BitMask512, BitMask256>` projections

**Scope:** `ScenarioSerializer.cs` and `ImGui/EntityInspectorPanel.cs` (and any other file in the
solution found to use this pattern via grep).

Search for `Unsafe.As<BitMask512, BitMask256>` across the codebase. For each occurrence:
- Check what the downstream API expects (`BitMask256` or `BitMask512`).
- If the downstream API can accept `BitMask512` (after Phase 5 upgrade), pass `BitMask512` directly.
- If the downstream API truly requires `BitMask256` (legacy, not being upgraded in this phase),
  leave the `Unsafe.As` projection with a `// TODO(ecs-512): remove when [API] upgraded to BitMask512` comment.

The `ScenarioSerializer.SerializeEntity(BitMask256)` method's signature should be checked:
if it's only used internally, upgrade it to `BitMask512`. The `ImGui` panel similarly.

---

## Task 1: EntityQuery Hot-First Traversal (TASK-E006)

**Files:**
- `FDP/Engine/Fdp.Core/EntityQuery.cs` (UPDATE)
- `FDP/Engine/Fdp.Core/QueryBuilder.cs` (VERIFY — already upgraded in BATCH-01)

**Task Definition:** See [TASK-E006 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e006--entityquery-and-querybuilder-hot-first-traversal).

Follow **DESIGN.md Phase 4 — MoveNext() rewrite** precisely. The canonical hot-first order is:

```
1. GetComponentMaskUnsafe(i)       -- hot memory only, 1 cache line
2. BitMask512.HasAll(compMask, _includeMask) -- false -> continue (skip entity)
3. BitMask512.HasAny(compMask, _excludeMask) -- true  -> continue (skip entity)
---- only entities passing both checks reach cold memory ----
4. GetMetadataUnsafe(i)            -- cold memory, 2 cache lines
5. meta.IsActive check             -- false -> continue
6. lifecycle filter                -- check if needed
7. authority mask checks           -- if needed
8. DIS filter checks               -- if needed
```

**Key constraint:** Steps 1-3 must happen BEFORE step 4. Cold data must not be accessed for
entities that fail the include/exclude mask checks.

`Entity.Current` must read `Generation` from cold metadata (not hard-coded).

`Matches(in BitMask512 mask, in EntityMetadataCold meta)` replaces the old overload that
accepted `EntityHeader`.

**Tests Required (add to `EntityQueryEnumeratorTests.cs` or a new `EntityQueryHotFirstTests.cs`):**

Cover all success conditions from TASK-E006 in TASK-DETAIL.md:

1. **Include filter (upper-range bits):**
   - Create entity with bit 400 set; query with `ComponentType<T>.ID == 400`.
   - Assert entity appears in query result.
   - Create entity without bit 400; assert it does NOT appear.

2. **Exclude filter:**
   - Create entity with bit 300 set; query with `.Without<T>()` where ID == 300.
   - Assert entity does NOT appear.

3. **Dead entity short-circuit:**
   - A destroyed entity (hot mask == 0) must never appear in query results even if the
     query has no required components (empty include mask).
   - Verify: create entity, add component, destroy entity; query for that component; assert 0 results.

4. **Parallel iteration equals serial:**
   - Create 100 entities; add a specific component to 30 of them.
   - `ForEachParallel` result set matches `ForEach` result set.

5. **Count / Any correctness:**
   - Empty world: `Count() == 0`, `Any() == false`.
   - 3 matching entities: `Count() == 3`, `Any() == true`.

6. **All existing `QueryTests.cs` and `EntityQueryEnumeratorTests.cs` tests pass unchanged.**

---

## Task 2: EntityRepository Split Header Access (TASK-E007)

**Files:**
- `FDP/Engine/Fdp.Core/EntityRepository.cs` (UPDATE)
- `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs` (UPDATE — masks return BitMask512)
- `FDP/Engine/Fdp.Core/EntityRepository.DeltaQuery.cs` (UPDATE if it has GetHeader calls)
- `FDP/Engine/Fdp.Core/EntityRepository.View.cs` (UPDATE if it has GetHeader calls)
- Any toolkit files still using `Unsafe.As<BitMask512, BitMask256>` for `GetRecordableMask` usage

**Task Definition:** See [TASK-E007 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e007--entityrepository-split-header-access).

**Key rules:**
- Every operation that **sets or clears a component bit** uses `GetComponentMask(entity.Index)`.
- Every operation that **reads or writes `LastChangeTick`, `Generation`, `DisType`, `IsActive`,
  `LifecycleState`, or `AuthorityMask`** uses `GetMetadata(entity.Index)`.
- `GetRecordableMask()`, `GetSnapshotableMask(bool)`, `GetSaveableMask()` must return `BitMask512`.
  They iterate `ComponentTypeRegistry.GetRecordableTypeIds()` and set bits in a `BitMask512`.

**Tests Required (add to `EntityRepositoryTests.cs`):**

Cover all success conditions from TASK-E007 in TASK-DETAIL.md:

1. **AddComponent sets hot mask bit:**
   - Create entity, add component of type ID 350.
   - `repo.GetEntityIndex().GetComponentMask(entity.Index).IsSet(350)` is true.

2. **RemoveComponent clears hot mask bit:**
   - Add then remove component of type ID 350.
   - Hot mask bit 350 is false after removal.

3. **GetRecordableMask returns BitMask512:**
   - Register a component with `record: true`.
   - `repo.GetRecordableMask()` returns a `BitMask512`.
   - The bit for that component's ID is set in the returned mask.

4. **All existing `EntityRepositoryTests.cs` tests pass.**

---

## Testing Requirements

- All existing `Fdp.Core.Tests` tests must pass after every task.
- All existing toolkit tests (`Fdp.Toolkits.Tests`) must pass.
- New tests must verify actual behavior (bit states, method return types, population counts) — not
  just that objects compile.

---

## Quality Standards

**Test Quality:**
- NOT ACCEPTABLE: Tests that only verify `GetRecordableMask()` is non-null.
- REQUIRED: Tests that call `mask.IsSet(componentId)` and assert the specific bit is set.
- REQUIRED: The include-filter-with-upper-range-bits test (bit 400) is mandatory — it directly
  proves the 512-component expansion works end-to-end in queries.

**Code Quality:**
- Follow `.github/skills/CODE-STANDARDS.md`.
- No compiler warnings introduced.
- The `Matches(in BitMask512, in EntityMetadataCold)` overload must replace any overload
  accepting `EntityHeader` — verify no `EntityHeader` reference remains in `EntityQuery.cs`.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective 0a (D003):** Add mask independence test → **ALL tests pass** ✅
2. **Corrective 0b (D005):** Clean up Unsafe.As projections → **ALL tests pass** ✅
3. **Task 1 (TASK-E006):** Complete EntityQuery hot-first MoveNext → **ALL tests pass** ✅
4. **Task 2 (TASK-E007):** EntityRepository split + BitMask512 sync masks → **ALL tests pass** ✅

**DO NOT** stop to ask for permission. Work autonomously until everything is green, then submit the report.

---

## Success Criteria

This batch is DONE when:

- [ ] **D003 corrective**: `HotMasks_AreIndependentPerEntity` test exists and passes.
- [ ] **D005 corrective**: `Unsafe.As<BitMask512, BitMask256>` projections resolved (either removed or annotated as intentional with TODO comment).
- [ ] **TASK-E006**: `EntityQuery.MoveNext()` follows exact hot-first order from DESIGN.md Phase 4; entity with bit 400 appears in queries; dead entities never appear; parallel equals serial.
- [ ] **TASK-E007**: `GetRecordableMask()`, `GetSnapshotableMask()`, `GetSaveableMask()` return `BitMask512`; AddComponent/RemoveComponent correctly set/clear hot mask bits.
- [ ] All existing `QueryTests.cs`, `EntityQueryEnumeratorTests.cs`, `EntityRepositoryTests.cs` tests pass.
- [ ] Full solution `dotnet build FDP/FDP.sln -c Debug` — 0 errors, 0 new warnings.
- [ ] Report submitted to `.dev/ecs-512-comps/reports/BATCH-03-REPORT.md`.

---

## Developer Insights (Required in Report)

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Did you find any other call sites that still referenced `EntityHeader` or the old `GetHeader` API? What did you find?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q4:** What edge cases did you discover during the `MoveNext()` rewrite?

**Q5:** Are there any concerns about the hot-first ordering that the design lead should know about?

**Q6:** Suggested commit message for this batch.

---

## Reference Materials

- **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E006, TASK-E007
- **Design:** `.dev/ecs-512-comps/DESIGN.md` — Phase 4 and Phase 5 sections
- **Previous Review:** `.dev/ecs-512-comps/reviews/BATCH-02-REVIEW.md`
- **Debt Tracker:** `.dev/ecs-512-comps/DEBT-TRACKER.md`
- **Code Standards:** `.github/skills/CODE-STANDARDS.md`
- **EntityQuery source:** `FDP/Engine/Fdp.Core/EntityQuery.cs`
- **EntityRepository.Sync source:** `FDP/Engine/Fdp.Core/EntityRepository.Sync.cs`
- **Existing query tests:** `FDP/Engine/Fdp.Core.Tests/QueryTests.cs`, `EntityQueryEnumeratorTests.cs`
- **Existing repo tests:** `FDP/Engine/Fdp.Core.Tests/EntityRepositoryTests.cs`
