# BATCH-01: Phase 1 Prerequisites + Phase 2 New Data Structures

**Batch Number:** BATCH-01
**Tasks:** TASK-E001, TASK-E002, TASK-E003, TASK-E004
**Phase:** Phase 1 (Prerequisites) + Phase 2 (New Data Structures)
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** None (this is the first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the foundation for the ECS 512-component expansion. You will:
1. Widen the component ID type from `byte` to `int` (pure type promotion, no behavior change).
2. Update engine capacity constants and a validation guard.
3. Create `BitMask512` — a new 512-bit, 64-byte bitmask with AVX2-accelerated logic.
4. Create `EntityMetadataCold` — a new 128-byte cold metadata struct.

Phases 1 and 2 are self-contained: nothing outside `Fdp.Core` changes at the source level, and
no new types are wired into `EntityIndex` or any other system yet.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Onboarding:** `.dev/ecs-512-comps/ONBOARDING.md`
3. **Design:** `.dev/ecs-512-comps/DESIGN.md` — read "Phase 1: Prerequisites" and "Phase 2: New Data Structures" sections thoroughly.
4. **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — read TASK-E001, TASK-E002, TASK-E003, TASK-E004 sections thoroughly.
5. **Code Standards:** `.github/skills/CODE-STANDARDS.md`

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
`.dev/ecs-512-comps/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/ecs-512-comps/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch is the first of four. It establishes the type system and data structures that all
subsequent batches build on. No existing behavior changes — this is purely additive/widening.

**Related Tasks:**
- [TASK-E001](./../TASK-DETAIL.md#task-e001--widen-component-id-type-attribute-and-constants) — byte→int type promotion
- [TASK-E002](./../TASK-DETAIL.md#task-e002--configuration-update-capacity-and-format-version) — config constants + QueryBuilder guard
- [TASK-E003](./../TASK-DETAIL.md#task-e003--new-data-structure-bitmask512) — new BitMask512 struct
- [TASK-E004](./../TASK-DETAIL.md#task-e004--new-data-structure-entitymetadatacold) — new EntityMetadataCold struct

---

## Batch Objectives

1. Make component IDs compile-time safe for values 0-511.
2. Raise the capacity constant and format version in the engine config.
3. Deliver a complete, fully-tested `BitMask512` implementation ready for use in Phase 3.
4. Deliver a complete, fully-tested `EntityMetadataCold` implementation ready for use in Phase 3.

---

## Tasks

### Task 1: Widen Component ID Type (TASK-E001)

**Files:**
- `FDP/Engine/Fdp.Core/ComponentIdAttribute.cs` (UPDATE)
- `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` (UPDATE)

**Task Definition:** See [TASK-E001 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e001--widen-component-id-type-attribute-and-constants) for the full scope, constraints, and success conditions.

**Key points:**
- `ComponentIdAttribute.Id`: `byte` → `int`
- Constructor parameter: `byte id` → `int id`
- XML doc: update stated range from `[0, 255]` to `[0, 511]`
- `GlobalComponentIds`: all `public const byte` → `public const int`
- Add `// ID block 256-511: Reserved for expansion` comment at the bottom of `GlobalComponentIds`
- No numeric values change; do not renumber or remove any constants

**Tests Required (add to `ComponentIdAttributeTests.cs` and `ComponentTypeRegistryTests.cs`):**
- All existing tests must still pass unchanged.
- New: reflection test — `[ComponentId(300)]` attribute; read `.Id`; assert value is `300` (not `44`).
- New: registry collision test — register two components with IDs 300 and 301; then attempt to register a third with ID 300; assert `InvalidOperationException`.

---

### Task 2: Configuration Update (TASK-E002)

**Files:**
- `FDP/Engine/Fdp.Core/FdpConfig.cs` (UPDATE)
- `FDP/Engine/Fdp.Core/QueryBuilder.cs` (UPDATE)

**Task Definition:** See [TASK-E002 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e002--configuration-update-capacity-and-format-version) for the full scope, constraints, and success conditions.

**Key points:**
- `MAX_COMPONENT_TYPES`: `256` → `512`
- `FORMAT_VERSION`: `4` → `5` (must be exactly 5, not a larger increment)
- Update XML doc on `MAX_COMPONENT_TYPES` to say "Limited by `BitMask512` capacity."
- `QueryBuilder.WithComponentId(int)`: change guard `componentId < 256` → `componentId < 512`

**Tests Required (in `QueryTests.cs` — add new, existing must pass):**
- `WithComponentId(255)` still sets the correct bit (backward compatibility).
- `WithComponentId(400)` now correctly sets bit 400 in the include mask (was silently ignored before).
- `WithComponentId(512)` is still silently ignored (upper bound guard intact).
- `FdpConfig.MAX_COMPONENT_TYPES == 512`
- `FdpConfig.FORMAT_VERSION == 5`

---

### Task 3: BitMask512 (TASK-E003)

**File:**
- `FDP/Engine/Fdp.Core/BitMask512.cs` (NEW FILE)

**Task Definition:** See [TASK-E003 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e003--new-data-structure-bitmask512) for the full scope, constraints, and success conditions.

Follow the DESIGN.md "Phase 2 — BitMask512" section strictly for the memory layout, field
names, and AVX2 algorithm structure. Key implementation constraints:

- `[StructLayout(LayoutKind.Explicit, Size = 64)]`
- 8 `ulong` private fields `_q0`–`_q7` at offsets 0, 8, 16, 24, 32, 40, 48, 56
- `SetBit`/`ClearBit`/`IsSet` use `switch` on `bitIndex >> 6`
- `Matches`, `HasAll`, `HasAny`: AVX2 path lower 256 bits first (`Avx.TestC`/`Avx.TestZ`), then upper 256 bits; scalar fallback interleaves include/exclude checks, lower-half first
- `#if FDP_PARANOID_MODE` guards: `bitIndex >= 0 && bitIndex < 512`
- `IEquatable<BitMask512>` with `Equals`, `GetHashCode`, `==`, `!=`

**Tests Required (create `BitMask512Tests.cs` in `Fdp.Core.Tests`):**

Cover all success conditions from TASK-E003 in TASK-DETAIL.md:

1. **Size test** — `Unsafe.SizeOf<BitMask512>() == 64`
2. **SetBit/IsSet round-trip** — for each boundary bit: 0, 63, 64, 127, 255, 256, 383, 511:
   - Set the bit, assert `IsSet` is true.
   - Assert all other tested bits are still false (no bit bleed across quads).
3. **ClearBit** — Set bit 400, clear it, assert `IsSet(400) == false` and `IsEmpty() == true`.
4. **HasAll** — true when all required bits set; false when any is missing. Cover: all bits in lower half, all in upper half, bits straddling quad 3/4 boundary.
5. **HasAny** — true when at least one bit overlaps; false when no overlap.
6. **Matches** — verify all four combinations of (include-present, include-missing) x (exclude-present, exclude-absent). Cover lower-half only, upper-half only, and mixed.
7. **Equality** — identical masks equal; any single differing bit makes them unequal; `GetHashCode` consistent with equality.
8. **Paranoid mode** — `SetBit(-1)` and `SetBit(512)` each throw `ArgumentOutOfRangeException` (compile with `FDP_PARANOID_MODE` defined for this test).

Tests must use actual value assertions (bit set/not set, equality, exceptions), not string checks.
Minimum: 20 distinct test methods.

---

### Task 4: EntityMetadataCold (TASK-E004)

**File:**
- `FDP/Engine/Fdp.Core/EntityMetadataCold.cs` (NEW FILE)

**Task Definition:** See [TASK-E004 in TASK-DETAIL.md](./../TASK-DETAIL.md#task-e004--new-data-structure-entitymetadatacold) for the full scope, constraints, and success conditions.

Follow DESIGN.md "Phase 2 — EntityMetadataCold" section for the field layout. Key constraints:

- `[StructLayout(LayoutKind.Explicit, Size = 128)]`
- Field offsets exactly as specified: `AuthorityMask` at 0, `Generation` at 64, `Flags` at 66, `LastChangeTick` at 68, `DisType` at 76, `LifecycleState` at 84
- `IsActive`: computed property — `(Flags & 0x0001) != 0`
- `SetActive(bool)`: sets/clears bit 0 of `Flags` without touching any other bits
- Must satisfy `where T : unmanaged`

**Tests Required (create `EntityMetadataColdTests.cs` in `Fdp.Core.Tests`):**

Cover all success conditions from TASK-E004 in TASK-DETAIL.md:

1. **Size test** — `Unsafe.SizeOf<EntityMetadataCold>() == 128`
2. **IsActive/SetActive round-trip** — default is false; `SetActive(true)` → true; `SetActive(false)` → false.
3. **SetActive does not touch other bits** — set `Flags = 0xFFFE`, call `SetActive(true)`, assert `Flags == 0xFFFF`; call `SetActive(false)`, assert `Flags == 0xFFFE`.
4. **AuthorityMask field** — `meta.AuthorityMask.SetBit(300)` succeeds; `meta.AuthorityMask.IsSet(300) == true`.
5. **Unmanaged constraint** — the test file must contain a line that verifies: `_ = new NativeChunkTable<EntityMetadataCold>(1)` (or equivalent usage of a generic unmanaged constraint); this must compile without error, proving the struct is `unmanaged`.

---

## Testing Requirements

- All existing `Fdp.Core.Tests` tests must continue to pass after every task.
- New test files: `BitMask512Tests.cs`, `EntityMetadataColdTests.cs`.
- Updated test files: `ComponentIdAttributeTests.cs`, `ComponentTypeRegistryTests.cs`, `QueryTests.cs`.
- Minimum test method count: 20 for BitMask512Tests, 5 for EntityMetadataColdTests.
- All assertions must verify actual behavior (values, sizes, bit states, exceptions) — not just
  that objects compile or exist.

---

## Quality Standards

**Test Quality:**
- NOT ACCEPTABLE: Tests that only check "can I create this struct" or "does it compile."
- REQUIRED: Tests that verify actual layout (byte sizes via `Unsafe.SizeOf`), actual bit values,
  actual exception types and messages.
- REQUIRED: For `BitMask512`, test boundary bits explicitly (0, 63, 64, 127, 255, 256, 383, 511).
- REQUIRED: The quad-boundary tests (e.g., bit 63 vs bit 64) are the most important; they catch
  off-by-one errors in the `switch` dispatch.

**Code Quality:**
- Follow `.github/skills/CODE-STANDARDS.md` — read it before writing code.
- No compiler warnings introduced.
- XML doc on all public members of new types.
- Do NOT introduce managed fields in `EntityMetadataCold` (it must satisfy `unmanaged`).

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (TASK-E001):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (TASK-E002):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (TASK-E003):** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4 (TASK-E004):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including all previous tasks' tests)

**DO NOT** ask for permission to run tests, fix compilation errors, or continue to the next task.
Work autonomously until all success criteria are met, then write your report.

---

## Success Criteria

This batch is DONE when:

- [ ] TASK-E001: `ComponentIdAttribute.Id` is `int`; all existing component structs compile unchanged; reflection test for ID=300 passes; collision test passes.
- [ ] TASK-E002: `MAX_COMPONENT_TYPES == 512`; `FORMAT_VERSION == 5`; `WithComponentId(400)` sets bit 400; `WithComponentId(512)` is still ignored.
- [ ] TASK-E003: `BitMask512` exists; `Unsafe.SizeOf<BitMask512>() == 64`; all bit boundary tests pass; `Matches`/`HasAll`/`HasAny` tests pass; equality tests pass; paranoid-mode bounds tests pass.
- [ ] TASK-E004: `EntityMetadataCold` exists; `Unsafe.SizeOf<EntityMetadataCold>() == 128`; `IsActive`/`SetActive` tests pass; `AuthorityMask.SetBit(300)` works; unmanaged constraint verified.
- [ ] All existing `Fdp.Core.Tests` tests still pass.
- [ ] `dotnet build FDP/FDP.sln -c Debug` completes with zero errors and zero new warnings.
- [ ] Report submitted to `.dev/ecs-512-comps/reports/BATCH-01-REPORT.md`.

---

## Developer Insights (Required in Report)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase while reading through it? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover during the BitMask512 or EntityMetadataCold implementation that weren't explicitly mentioned?

**Q5:** Are there any concerns about the AVX2 path or the scalar fallback that the design lead should know about?

**Q6:** Suggested commit message for this batch.

---

## Reference Materials

- **Task Details:** `.dev/ecs-512-comps/TASK-DETAIL.md` — TASK-E001 through TASK-E004
- **Design:** `.dev/ecs-512-comps/DESIGN.md` — Phase 1 and Phase 2 sections
- **Onboarding:** `.dev/ecs-512-comps/ONBOARDING.md`
- **Code Standards:** `.github/skills/CODE-STANDARDS.md`
- **Existing BitMask reference:** `FDP/Engine/Fdp.Core/BitMask256.cs` (study this as a reference pattern)
- **Existing tests to study:** `FDP/Engine/Fdp.Core.Tests/BitMask256Tests.cs`, `ComponentIdAttributeTests.cs`, `ComponentTypeRegistryTests.cs`
