# BATCH-01: StructEdit Core Extensions

**Batch Number:** BATCH-01
**Tasks:** TASK-CE01, TASK-CE02, TASK-CE03
**Phase:** Phase 1 — StructEdit Core Extensions
**Estimated Effort:** 6-8 hours
**Priority:** HIGH
**Dependencies:** None (this is the first batch)

---

## Onboarding & Workflow

### Developer Instructions

You are extending the `StructEdit` library with three closely coupled changes:
1. A new binding type (`NestedMemberBinding`) that safely chains member access through a parent binding — critical for struct mutation inside boxed array elements.
2. A `CustomAttributes` property on `EditNodeMetadata` that lets domain attributes flow opaquely to the UI without StructEdit knowing about them.
3. Array element node generation in `ReflectionEditDocumentBuilder` — the builder now produces a complete `EditNode` subtree for every array element so the UI renderer needs zero reflection.

These three tasks are tightly coupled. Implement them in order (CE01 → CE02 → CE03) and run tests after each before moving to the next.

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md`
2. **Code Standards:** `.github/skills/CODE-STANDARDS.md`
3. **Onboarding:** `.dev/comp-edit-1/ONBOARDING.md`
4. **Design (Phase 1):** `.dev/comp-edit-1/DESIGN.md` — read §§ "Phase 1: StructEdit Core Extensions" (all three sub-sections)
5. **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` — read TASK-CE01, TASK-CE02, TASK-CE03 in full

### Source Code Locations

- **Primary work area:** `FDP/ExtDeps/StructEdit/src/`
  - `StructEdit.Core/Bindings/` — add `NestedMemberBinding.cs` here
  - `StructEdit.Core/EditNodeMetadata.cs` — add `CustomAttributes` property
  - `StructEdit.Reflection/ReflectionEditDocumentBuilder.cs` — extend for array elements
- **Test project:** `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/`
  - Existing tests: `Reflection/BindingTests.cs`, `Reflection/DocumentBuilderTests.cs`, `Reflection/AttributeTests.cs`
  - Add your new test classes in `Reflection/` (e.g. `NestedMemberBindingTests.cs`, `ArrayElementNodeTests.cs`)

### Build Commands

```powershell
# Build just FDP (fast)
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/FDP.sln --no-restore

# Run StructEdit tests only
dotnet test FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/StructEdit.Tests.csproj

# Run all tests (do this before submitting report)
dotnet test IOS-IG-SimHost.sln
```

### Report Submission

When done: `.dev/comp-edit-1/reports/BATCH-01-REPORT.md`
If questions arise: `.dev/comp-edit-1/questions/BATCH-01-QUESTIONS.md`

---

## MANDATORY WORKFLOW

**Complete tasks strictly in order. Do NOT skip ahead.**

1. **TASK-CE01** → implement `NestedMemberBinding` → write tests → **ALL tests pass** ✅
2. **TASK-CE02** → add `CustomAttributes` + update `ReadMetadata` → write tests → **ALL tests pass** ✅
3. **TASK-CE03** → extend `ReflectionEditDocumentBuilder` → write tests → **ALL tests pass** ✅

Do not stop and ask for permission to run tests, fix compilation errors, or proceed to the next task. Complete the full batch end-to-end, fix any issues encountered, then submit the report.

---

## Context

This batch is the foundation for the entire `comp-edit-1` workstream. Nothing in Phases 2-4 can proceed until these three tasks are done. No existing files inside `Fdp.Presentation` are touched in this batch — all changes are isolated to the `StructEdit` library under `FDP/ExtDeps/StructEdit/`.

The key insight driving all three tasks: the UI renderer must never call reflection. `ReflectionEditDocumentBuilder` runs its reflection pass once at session open and produces a complete, immutable `EditDocument` tree. The renderer then reads/writes only through `IValueBinding` — no `MemberInfo`, no type inspection at draw time.

---

## Tasks

### TASK-CE01: NestedMemberBinding

**File:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Bindings/NestedMemberBinding.cs` (NEW FILE)
**Task Detail:** See [TASK-DETAIL.md §TASK-CE01](../TASK-DETAIL.md#task-ce01-nestedmemberbinding)
**Design Reference:** [DESIGN.md §1.1](../DESIGN.md#11-nestedmemberbinding)

Study the existing bindings in `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Bindings/` — especially `ManagedFieldBinding.cs` and `DynamicArrayBinding.cs` — before writing this class.

**Critical constraint (read carefully):** When `SetBoxed` is called on a `NestedMemberBinding` whose parent holds a value type (`_parent.ValueType.IsValueType == true`), mutating the member is not sufficient on its own. The boxed parent struct must be written back via `_parent.SetBoxed(parentObj)` after the mutation, otherwise the mutation is lost (C# value-type copy semantics).

**Tests to write** (add to `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/NestedMemberBindingTests.cs`):
- `T-CE01a`: struct mutation propagated through parent (see TASK-DETAIL §Success Conditions)
- `T-CE01b`: class mutation (no parent re-push needed, but `GetBoxed` still returns correct value)
- `T-CE01c`: null parent returns null without throwing

### TASK-CE02: EditNodeMetadata.CustomAttributes

**Files:**
- `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditNodeMetadata.cs` (MODIFY)
- `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ReflectionEditDocumentBuilder.cs` (MODIFY — `ReadMetadata` only)

**Task Detail:** See [TASK-DETAIL.md §TASK-CE02](../TASK-DETAIL.md#task-ce02-editnodemetadatacustomattributes)
**Design Reference:** [DESIGN.md §1.2](../DESIGN.md#12-editnodemetadatacustomattributes)

The `EditNodeMetadata.Empty` singleton must remain valid and `CustomAttributes` must default to `Array.Empty<Attribute>()` — do not allocate a new list for the common (no-custom-attrs) case.

In `ReadMetadata`, after collecting the known attributes (EditRange, EditUnit, EditDisplayName, InlineArrayHint, FixedBufferHint), collect all remaining attributes via `GetCustomAttributes(false)`, filter out the ones already handled, and store the rest in `CustomAttributes`. If the only result would be an empty list, keep the `Array.Empty<Attribute>()` default (no new allocation).

**Tests to write** (add to `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/AttributeTests.cs` or a new `MetadataTests.cs`):
- `T-CE02a` through `T-CE02d`: see TASK-DETAIL §Success Conditions

### TASK-CE03: Array Element Node Generation

**File:** `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ReflectionEditDocumentBuilder.cs` (MODIFY)

**Task Detail:** See [TASK-DETAIL.md §TASK-CE03](../TASK-DETAIL.md#task-ce03-array-element-node-generation) — read in full, including all constraints.
**Design Reference:** [DESIGN.md §1.3](../DESIGN.md#13-array-element-node-generation)

This is the most complex task in the batch. Study the existing `BuildNode` and `BuildChildren` methods carefully before modifying them.

**What to add / change:**
- Add optional parameters `IValueBinding? explicitBinding = null` and `IValueBinding? parentBinding = null` to `BuildNode`.
- Add a private static `BuildArrayElements` method that loops over `IContainerBinding.Count`, calls `cb.GetElementBinding(i)` for each, and passes the result as `explicitBinding` into a `BuildNode` call with `fi: null, pi: null, nativeOffset: -1`.
- In the `DynamicArray`, `InlineArray`, and `FixedBuffer` switch cases, call `BuildArrayElements` after creating the container binding and store the returned list as `children`.
- In `BuildChildren`, propagate `parentBinding` downward so that when `CreateLeafBinding` would be called with `fi == null && pi == null`, it instead wraps the member binding in a `NestedMemberBinding` using `parentBinding`.
- Update `CreateLeafBinding` to accept and use `parentBinding`.

**Key contract:** After `MarkStructuralChange()` + `RebuildDocument()`, the rebuilt `EditDocument` must reflect the updated element count and still be correct.

**Tests to write** (add to `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/ArrayElementNodeTests.cs` — new file):
- `T-CE03a` through `T-CE03f`: see TASK-DETAIL §Success Conditions
- Pay special attention to `T-CE03b` (struct mutation end-to-end) and `T-CE03c` (List<T> resize + rebuild) — these are the most likely to reveal integration bugs

---

## Testing Requirements

- **Minimum new tests:** 13 (3 CE01 + 4 CE02 + 6 CE03)
- All existing `StructEdit.Tests` tests must continue to pass (do not break the reflection test suite)
- Tests must verify **actual behavior** (values returned by `GetBoxed`, element counts, mutation correctness) — not just "no exception thrown" or "object is not null"
- Every spec success condition (`T-CE01a` through `T-CE03f`) must have a corresponding test

---

## Quality Standards

**Code:**
- `NestedMemberBinding` must be `internal sealed` in namespace `StructEdit.Core.Bindings`
- No magic numbers — use named locals or constants for array lengths, capacities
- No new public API surface beyond what the spec requires
- Follow the style of existing bindings (see `ManagedFieldBinding.cs`)

**Tests:**
- Tests that only check "no exception" are NOT acceptable
- Must assert specific values: `Assert.Equal(9f, ...)`, `Assert.Equal(3, children.Count)`, etc.
- The struct mutation test (`T-CE01a`) must read back the value from the parent binding, not from the `NestedMemberBinding` directly, to prove the write propagated

---

## Report Requirements

Submit `.dev/comp-edit-1/reports/BATCH-01-REPORT.md` covering:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Were there any cases where the spec was ambiguous or required interpretation? What did you decide?

**Q3:** Did you discover edge cases not mentioned in the spec? What are they?

**Q4:** Are there any weak points or design smells in the StructEdit codebase you noticed while working?

**Q5:** Suggested commit message (a few lines covering what changed and tests added).

---

## Success Criteria

This batch is DONE when:
- [ ] `NestedMemberBinding.cs` created and all CE01 tests pass
- [ ] `EditNodeMetadata.CustomAttributes` added and all CE02 tests pass
- [ ] `ReflectionEditDocumentBuilder` extended and all CE03 tests pass
- [ ] All pre-existing `StructEdit.Tests` tests still pass
- [ ] `dotnet test IOS-IG-SimHost.sln` exits with 0 failures
- [ ] Report submitted

---

## Reference

- **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §§ CE01, CE02, CE03
- **Design:** `.dev/comp-edit-1/DESIGN.md` §§ 1.1, 1.2, 1.3
- **Existing bindings to study:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Bindings/ManagedFieldBinding.cs`, `DynamicArrayBinding.cs`
- **Existing builder to study:** `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ReflectionEditDocumentBuilder.cs`
- **Existing tests to study:** `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/BindingTests.cs`, `DocumentBuilderTests.cs`
