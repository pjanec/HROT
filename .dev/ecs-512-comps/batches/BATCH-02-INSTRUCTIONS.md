# BATCH-02: Phase 3 EntityIndex Rewrite + BATCH-01 Corrective Tasks

**Batch Number:** BATCH-02
**Tasks:** TASK-E005 (primary), plus Corrective Task 0a (D001) and Corrective Task 0b (D002)
**Phase:** Phase 3 — Core Rewrite
**Estimated Effort:** 14-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (completed, committed)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the major structural rewrite at the heart of the 512-component expansion:
`EntityIndex` is split from a single `NativeChunkTable<EntityHeader>` into two parallel tables:
- **Hot**: `NativeChunkTable<BitMask512>` — the component mask only (64 bytes per entity)
- **Cold**: `NativeChunkTable<EntityMetadataCold>` — everything else (128 bytes per entity)

Additionally, fix two issues flagged in the BATCH-01 review (D001 and D002).

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Onboarding:** `.dev/ecs-512-comps/ONBOARDING.md`
3. **Design:** `.dev/ecs-512-comps/DESIGN.md` — "Phase 3: Core Rewrite" section thoroughly.
4. **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E005 section thoroughly.
5. **BATCH-01 Review:** `.dev/ecs-512-comps/reviews/BATCH-01-REVIEW.md` — understand corrective tasks.
6. **Debt Tracker:** `.dev/ecs-512-comps/DEBT-TRACKER.md`
7. **Code Standards:** `.github/skills/CODE-STANDARDS.md`

### Source Code Location

- **Primary Work Area:** `FDP/Engine/Fdp.Core/`
- **Test Project:** `FDP/Engine/Fdp.Core.Tests/`
- **Solution:** `FDP/FDP.sln`

### Build Command

```
cd FDP
dotnet build FDP.sln -c Debug
```

### Test Command

```
cd FDP/Engine/Fdp.Core.Tests
dotnet test
```

### Report Submission

**When done, submit your report to:**
`.dev/ecs-512-comps/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/ecs-512-comps/questions/BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 completed Phases 1 and 2. `BitMask512` and `EntityMetadataCold` exist but are not
wired into any system yet. `EntityIndex` still uses the old `NativeChunkTable<EntityHeader>`.
`EntityHeader` itself still exists. This batch replaces the monolithic header with the two
parallel tables and deletes `EntityHeader`.

**Downstream systems** (EntityQuery, EntityRepository, Flight Recorder) still reference the old
`GetHeader`/`GetHeaderUnsafe` API — those are updated in Phases 4, 5, 6. Your task is to
rewrite `EntityIndex` and provide the new API that phases 4/5/6 will call.

**Important:** `EntityQuery.cs` currently uses `BitMask512` for its masks but still calls
`entityIndex.GetHeader(i).ComponentMask` (a `BitMask256`). After this batch those calls must
compile via the new `GetComponentMask(i)` which returns `BitMask512`. Read `EntityQuery.cs`
and `EntityRepository.cs` before you start — you will need to update their call sites too
because the old `GetHeader`/`GetHeaderUnsafe` methods are being removed.

---

## Corrective Task 0a (D001): Fix vacuous `GlobalComponentIds_NoToolkitBlockDuplicates`

**File:** `FDP/Engine/Fdp.Core.Tests/ComponentIdAttributeTests.cs`

**Problem (from BATCH-01-REVIEW.md):**
The test filters fields with `f.FieldType == typeof(byte)`. After TASK-E001 widened all
`GlobalComponentIds` constants from `const byte` to `const int`, this filter matches zero
fields. The test passes vacuously — duplicate detection is silently disabled.

**Fix:**
- Change `f.FieldType == typeof(byte)` → `f.FieldType == typeof(int)`
- Change `var value = (byte)field.GetRawConstantValue()!` → `var value = (int)field.GetRawConstantValue()!`
- Change `Dictionary<byte, string>` → `Dictionary<int, string>`

**After fix:** Run the test and verify it finds more than zero fields (should be ~200+ constants).
Add an assertion at the start of the test: `Assert.NotEmpty(fields)`.

---

## Corrective Task 0b (D002): Add `Pack = 64` to `BitMask512` layout

**File:** `FDP/Engine/Fdp.Core/BitMask512.cs`

**Problem (from BATCH-01-REVIEW.md):**
`BitMask256` uses `[StructLayout(LayoutKind.Explicit, Size = 32, Pack = 32)]` to guarantee
32-byte aligned AVX2 vector loads. `BitMask512` is missing `Pack = 64`. Before Phase 3 wires
`BitMask512` into the `NativeChunkTable` hot array, alignment must be enforced.

**Fix:**
```csharp
// Change:
[StructLayout(LayoutKind.Explicit, Size = 64)]
// To:
[StructLayout(LayoutKind.Explicit, Size = 64, Pack = 64)]
```

Verify `Unsafe.SizeOf<BitMask512>() == 64` still holds after the change (it should — `Pack`
affects field alignment not struct size when using `Explicit` layout).

---

## Task 1: EntityIndex Rewrite (TASK-E005)

**Files:**
- `FDP/Engine/Fdp.Core/EntityIndex.cs` (FULL REWRITE)
- `FDP/Engine/Fdp.Core/EntityHeader.cs` (DELETE)
- `FDP/Engine/Fdp.Core/EntityQuery.cs` (UPDATE call sites)
- `FDP/Engine/Fdp.Core/EntityRepository.cs` (UPDATE call sites only — full rewrite is Phase 5, but remove compile errors now)
- `FDP/Engine/Fdp.Core/EntityRepository.DeltaQuery.cs` (UPDATE call sites if needed)
- `FDP/Engine/Fdp.Core/EntityRepository.View.cs` (UPDATE call sites if needed)

**Task Definition:** See [TASK-E005 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e005--entityindex-rewrite-hotcold-parallel-tables) for the full scope, constraints, and success conditions.

Follow **DESIGN.md Phase 3** section for the exact API and invariants.

### Implementation Notes

**New internal layout:**
```csharp
private readonly NativeChunkTable<BitMask512>         _hotMasks;
private readonly NativeChunkTable<EntityMetadataCold> _coldMeta;
```

**API changes per DESIGN.md:**

| Old | New |
|-----|-----|
| `GetHeader(int)` | `GetComponentMask(int)` + `GetMetadata(int)` |
| `GetHeaderUnsafe(int)` | `GetComponentMaskUnsafe(int)` + `GetMetadataUnsafe(int)` |
| `CopyChunkToBuffer(c, buf)` | `CopyHotChunkToBuffer(c, buf)` + `CopyColdChunkToBuffer(c, buf)` |
| `RestoreChunkFromBuffer(c, data)` | `RestoreHotChunkFromBuffer(c, data)` + `RestoreColdChunkFromBuffer(c, data)` |
| `SanitizeChunk(c, liveness)` | `SanitizeHotChunk(c, liveness)` + `SanitizeColdChunk(c, liveness)` |
| `ApplyComponentFilter(BitMask256)` | `ApplyComponentFilter(BitMask512)` |
| `ForceRestoreEntity(int, bool, int, BitMask256)` | `ForceRestoreEntity(int, bool, int, BitMask512)` |

**CreateEntity invariants:**
- Clear hot mask (`_hotMasks[index].Clear()`)
- Clear `AuthorityMask` in cold, set `IsActive(true)` in cold
- Increment population on **both** tables (same chunk index, same timing)
- Generation logic preserved (skip 0 generation)

**DestroyEntity invariants:**
- Clear hot mask immediately (guarantees fast-fail in query's `HasAll`)
- Increment generation in cold meta
- Decrement population on **both** tables
- `SetActive(false)` in cold meta

**SyncFrom invariants:**
- Call `SyncDirtyChunks` on **both** `_hotMasks` and `_coldMeta`
- Sync counters (`_activeCount`, `_maxIssuedIndex`)

**RebuildMetadata:** derive liveness from cold data (`IsActive` from `_coldMeta[id]`).

**GetChunkLiveness:** reads `IsActive` from cold meta (not hot mask).

**All existing call sites in EntityQuery, EntityRepository, etc.** that call `GetHeader` or
`GetHeaderUnsafe` must be updated to use the new split API. For Phase 4/5 files, do the
minimal change needed to compile: replace `GetHeader(i).ComponentMask` with
`GetComponentMask(i)`, `GetHeader(i).Generation` with `GetMetadata(i).Generation`, etc.

**Sanitize methods:**
- `SanitizeHotChunk(chunkIndex, liveness)`: for dead entities (liveness[i] == false), set the
  `BitMask512` slot to all-zero in the hot array.
- `SanitizeColdChunk(chunkIndex, liveness)`: for dead entities, no-op (cold sanitization just
  zeroes out sensitive data not required by design). Implement per DESIGN.md spec.

### Tests Required

All tests in the task detail are required. See [TASK-E005 Success Conditions](../TASK-DETAIL.md#task-e005--entityindex-rewrite-hotcold-parallel-tables).

Update the following existing test files to use the new API:
- `FDP/Engine/Fdp.Core.Tests/EntityIndexLivenessTests.cs`
- `FDP/Engine/Fdp.Core.Tests/EntityIndexSyncTests.cs`
- `FDP/Engine/Fdp.Core.Tests/ChunkIterationTests.cs`

Any test that called `GetHeader(...)`, `GetHeaderUnsafe(...)`, `CopyChunkToBuffer`, 
`RestoreChunkFromBuffer`, or `SanitizeChunk` must be updated for the new split API.

**New tests to add** (add to `EntityIndexLivenessTests.cs` or a new `EntityIndexHotColdTests.cs`):

1. **Create/Destroy round-trip** — Create entity, assert `IsAlive == true`. Destroy, assert `IsAlive == false` and `GetComponentMask(idx).IsEmpty() == true`.

2. **Mask independence** — Create entity A and entity B. Set bit 400 on A's hot mask. Assert B's hot mask does NOT have bit 400 set.

3. **Population counters in sync** — Create 10 entities, destroy 3. Assert `ActiveCount == 7`. Assert `GetChunkPopulation(0) == 7`.

4. **SyncFrom** — Source has entity 5 with bit 300 in hot mask. After `dest.SyncFrom(source)`, `dest.GetComponentMask(5).IsSet(300) == true`.

5. **GetChunkLiveness** — Create entities 0, 1, 2. Destroy entity 1. Call `GetChunkLiveness(0, span)`. Assert `span[0]==true`, `span[1]==false`, `span[2]==true`.

6. **ForceRestoreEntity** — Call `ForceRestoreEntity(10, true, 3, someMask512)`. Assert `GetComponentMask(10)` equals `someMask512`. Assert `GetMetadata(10).IsActive == true`. Assert `GetMetadata(10).Generation == 3`.

7. **Dead entity hot mask zeroed** — Create entity, destroy entity. Assert `GetComponentMask(idx).IsEmpty() == true`. (Ensures fast-fail in query traversal.)

---

## Testing Requirements

- All existing `Fdp.Core.Tests` tests must pass after every task in this batch.
- Corrective task tests: `GlobalComponentIds_NoToolkitBlockDuplicates` must assert `fields.Count > 0` and correctly detect any introduced duplicate.
- `BitMask512_SizeIs64Bytes` must still pass after `Pack=64` change.
- EntityIndex tests must verify actual values (mask bits, generation numbers, active counts) — not just that objects compile.
- Minimum 7 new EntityIndex test methods testing the hot/cold split specifically.

---

## Quality Standards

**Test Quality:**
- NOT ACCEPTABLE: Tests that only check "entity was created" without verifying hot mask state.
- REQUIRED: Tests that set specific bit indices and verify those exact bits are set/clear.
- REQUIRED: Tests that verify both hot AND cold state after operations (not just one table).
- REQUIRED: The `ForceRestoreEntity` test must check BOTH `GetComponentMask` (hot) AND `GetMetadata` (cold) results.

**Code Quality:**
- Follow `.github/skills/CODE-STANDARDS.md`.
- No compiler warnings introduced.
- XML doc on all public members of updated API.
- `EntityHeader.cs` must be deleted (not just emptied).
- Both `_hotMasks` and `_coldMeta` must be disposed in `EntityIndex.Dispose()`.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective Task 0a (D001):** Fix `GlobalComponentIds_NoToolkitBlockDuplicates` → **ALL tests pass** ✅
2. **Corrective Task 0b (D002):** Add `Pack=64` to BitMask512 → **ALL tests pass** ✅
3. **Task 1 (TASK-E005):** Rewrite EntityIndex → Update all call sites → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written/updated
- ✅ **ALL tests passing** (including all previous batch tests)

**DO NOT** ask for permission to run tests, fix compilation errors, or continue to the next task.
Work autonomously until all success criteria are met. If the build fails, fix it. If tests fail,
fix the root cause. Write the report only when everything is green.

---

## Success Criteria

This batch is DONE when:

- [ ] **D001 corrective**: `GlobalComponentIds_NoToolkitBlockDuplicates` filters `typeof(int)` fields; `Assert.NotEmpty(fields)` passes; test detects duplicates correctly.
- [ ] **D002 corrective**: `BitMask512` has `Pack=64`; `Unsafe.SizeOf<BitMask512>() == 64` still passes.
- [ ] **TASK-E005**: `EntityHeader.cs` deleted; `EntityIndex` has `_hotMasks` (BitMask512 table) and `_coldMeta` (EntityMetadataCold table); old `GetHeader`/`GetHeaderUnsafe` removed; new split accessors present.
- [ ] All 7 new hot/cold EntityIndex tests pass with actual value assertions.
- [ ] All existing updated tests (`EntityIndexLivenessTests.cs`, `EntityIndexSyncTests.cs`, `ChunkIterationTests.cs`) pass with new API.
- [ ] All other `Fdp.Core.Tests` tests pass (EntityQuery, EntityRepository, Flight Recorder tests).
- [ ] `dotnet build FDP/FDP.sln -c Debug` completes with zero errors and zero new warnings.
- [ ] Report submitted to `.dev/ecs-512-comps/reports/BATCH-02-REPORT.md`.

---

## Developer Insights (Required in Report)

**Q1:** What issues did you encounter during the EntityIndex rewrite? How did you resolve them?

**Q2:** Were there unexpected call sites (beyond EntityQuery and EntityRepository) that depended on `GetHeader`? How many files did you touch?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover during implementation that weren't mentioned in the spec?

**Q5:** Are there any concerns about the hot/cold split invariants (e.g., population counter synchronization) that the design lead should know about?

**Q6:** Suggested commit message for this batch.

---

## Reference Materials

- **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E005
- **Design:** `.dev/ecs-512-comps/DESIGN.md` — Phase 3 section
- **Previous Review:** `.dev/ecs-512-comps/reviews/BATCH-01-REVIEW.md`
- **Debt Tracker:** `.dev/ecs-512-comps/DEBT-TRACKER.md`
- **Code Standards:** `.github/skills/CODE-STANDARDS.md`
- **Existing EntityIndex:** `FDP/Engine/Fdp.Core/EntityIndex.cs` (study before rewriting)
- **Existing EntityHeader:** `FDP/Engine/Fdp.Core/EntityHeader.cs` (understand what moves where)
- **NativeChunkTable API:** `FDP/Engine/Fdp.Core/NativeChunkTable.cs`
- **Existing EntityIndex tests:** `FDP/Engine/Fdp.Core.Tests/EntityIndexLivenessTests.cs`, `EntityIndexSyncTests.cs`, `ChunkIterationTests.cs`
