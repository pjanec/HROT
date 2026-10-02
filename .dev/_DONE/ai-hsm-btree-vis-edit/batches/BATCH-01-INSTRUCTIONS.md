# BATCH-01: Kernel / Attribute Prerequisites

**Batch Number:** BATCH-01
**Tasks:** TASK-BB-K-01, TASK-BB-K-02, TASK-BB-K-03, TASK-BB-K-04
**Phase:** Phase 0 — Kernel / attribute prerequisites
**Estimated Effort:** 6–8 hours
**Priority:** HIGH — blocks all subsequent phases
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This is the first batch for the **Blackboard Authoring** feature. You are implementing four purely additive kernel-level attribute changes that have zero behavioral effect on the existing codebase. All existing tests must remain green throughout.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/_DONE/ai-hsm-btree-vis-edit/ONBOARDING.md` — read this fully, especially section 3 (code locations)
3. **Task Details:** `.dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — read §0 (Reconciliation) and Phase 0 section
4. **Design:** `.dev/_DONE/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md` — sections §3.1, §4a.4, §6.3, §9.6, §14.5

### Source Code Locations

- **BTreeDefinition attribute:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs`
- **HsmDefinition attribute:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmDefinitionAttribute.cs`
- **Shared AI attributes (heavy action):** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/SharedAiAttributes.cs`
- **FastHSM kernel attributes folder:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/`
- **FastBTree kernel attributes folder:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/`
- **Tests to add to:** existing test projects under `FDP/ExtDeps/FastBTree/` and `FDP/ExtDeps/FastHSM/` (find them), or create new test files adjacent to the attribute source if no test project exists

### Report Submission

**When done, submit your report to:**
`.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/_DONE/ai-hsm-btree-vis-edit/questions/BATCH-01-QUESTIONS.md`

---

## Context

Phase 0 adds four new properties/attributes to the kernel layer. These are purely opt-in additions — all defaults preserve today's behavior exactly. They unblock every subsequent phase:

- `BlackboardManaged` (K-01): lets the editor know this asset uses an editor-managed companion file
- `HeavyDtoType` (K-02): wires the runtime to provision a `Blackboard1024` heavy component when set
- `[BlackboardDtoStruct]` (K-03): lets user-defined DTO structs opt in to the editor's type picker
- `[BlackboardReadOnly]` / `[BlackboardReadWrite]` (K-04): annotate action parameter access pattern for the editor's schema exporter

---

## Tasks

### TASK-BB-K-01 — `BlackboardManaged` flag on `[BTreeDefinition]` / `[HsmDefinition]`

**Spec:** See TASK-DETAIL.md §TASK-BB-K-01; design BB §3.1, §14.2, §14.3.

Add `bool BlackboardManaged { get; set; }` property (default `false`) to:
- `BTreeDefinitionAttribute` (`FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeDefinitionAttribute.cs`)
- `HsmDefinitionAttribute` (`FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmDefinitionAttribute.cs`)

This is purely an opt-in annotation. The runtime ignores it; the editor reads it. Default `false` means all existing assets are unaffected.

**Tests Required:**
- Confirm `BlackboardManaged` defaults to `false` on a new instance of each attribute
- Confirm an explicit `BlackboardManaged = true` assignment round-trips correctly (set and read back)
- Confirm no existing behavior-tree or HSM tests break

### TASK-BB-K-02 — `HeavyDtoType` argument on `[BTreeDefinition]` / `[HsmDefinition]`

**Spec:** See TASK-DETAIL.md §TASK-BB-K-02; design BB §6.3, §14.5.

Add `Type? HeavyDtoType { get; set; }` property (default `null`) to the same two attribute classes as K-01.

When set, the source generator (in a later phase) will wire `BehaviorIngressSystem` to provision a `Blackboard1024` component. For now, just the property declaration + tests. No generator changes yet.

**Tests Required:**
- `HeavyDtoType` defaults to `null`
- Setting `HeavyDtoType = typeof(SomeStruct)` is readable back as `typeof(SomeStruct)`
- `null` → no heavy component wired (validated with a simple attribute-read test, no runtime integration yet)

### TASK-BB-K-03 — `[BlackboardDtoStruct]` marker attribute

**Spec:** See TASK-DETAIL.md §TASK-BB-K-03; design BB §4a.4, §10, §14.5.

Create a new marker attribute `[BlackboardDtoStruct]` in the shared annotations assembly that is reachable from both kernels and the editor. The attribute:
- `AttributeTargets.Struct`
- `AllowMultiple = false`
- No constructor arguments needed (it is a pure marker)

**Where to place it:** Find the shared attributes assembly (alongside `SharedAiAttributes.cs` in `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/`). Place it in the same namespace/assembly so the editor can discover it via reflection.

**Tests Required:**
- A `[BlackboardDtoStruct]`-decorated struct is discoverable via `Assembly.GetTypes()` + `IsDefined(typeof(BlackboardDtoStructAttribute))` reflection
- A struct *without* the attribute is NOT discovered by the same query
- The attribute can be applied to a struct and read back at runtime

### TASK-BB-K-04 — `[BlackboardReadOnly]` / `[BlackboardReadWrite]` parameter attributes

**Spec:** See TASK-DETAIL.md §TASK-BB-K-04; design BB §9.6, §10.2, §14.5.

Create two new attributes in the same shared annotation assembly:
- `[BlackboardReadOnly]` — marks the first `ref` param of an action method as read-only access
- `[BlackboardReadWrite]` — marks it as read-write access (explicit, as opposed to default)

Both attributes:
- `AttributeTargets.Parameter`
- `AllowMultiple = false`
- No constructor arguments
- The kernel ignores them at runtime; they exist solely for the editor's schema exporter

**Tests Required:**
- Applying `[BlackboardReadOnly]` to a method parameter is readable via `ParameterInfo.GetCustomAttribute<BlackboardReadOnlyAttribute>()`
- Applying `[BlackboardReadWrite]` to a method parameter is readable the same way
- Unannotated parameter has neither attribute (null checks on `GetCustomAttribute` for both)
- Both attributes can coexist in the assembly (compile test)

---

## Mandatory Workflow: Test-Driven Task Progression

Follow this workflow for each task:

1. **Read the task spec** in TASK-DETAIL.md and the referenced design sections
2. **Write the tests first** (failing is fine — they define the contract)
3. **Implement** the minimal code to make tests pass
4. **Verify** nothing else broke: `dotnet build IOS-IG-SimHost.sln` + `dotnet test IOS-IG-SimHost.sln`
5. **Move to the next task** only after all tests for the current task pass

---

## Testing Requirements

- All 4 tasks have unit tests demonstrating the property/attribute is correctly declared and readable
- All existing tests continue to pass after changes (`dotnet test IOS-IG-SimHost.sln`)
- Tests live in the appropriate test project (find the existing test project for FastBTree/FastHSM, or create `*Tests.cs` files there)
- No test should "assert true" or only check for no exceptions — each must assert specific values

---

## Report Requirements

Submit `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-01-REPORT.md` with:

### Report Format

```markdown
# BATCH-01 Report

## Tasks Completed
- [ ] TASK-BB-K-01
- [ ] TASK-BB-K-02
- [ ] TASK-BB-K-03
- [ ] TASK-BB-K-04

## Test Results
[Paste the dotnet test summary output]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points, inconsistencies, or improvement opportunities in the existing kernel/attribute code?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Suggested git commit message for this batch?
```

---

## Success Criteria

This batch is DONE when:
- [ ] TASK-BB-K-01: `BlackboardManaged` property exists on both `[BTreeDefinition]` and `[HsmDefinition]`, defaults `false`, tests pass
- [ ] TASK-BB-K-02: `HeavyDtoType` property exists on both attributes, defaults `null`, tests pass
- [ ] TASK-BB-K-03: `[BlackboardDtoStruct]` marker attribute created and reflection-discoverable, tests pass
- [ ] TASK-BB-K-04: `[BlackboardReadOnly]` and `[BlackboardReadWrite]` parameter attributes created, readable via ParameterInfo, tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with no errors
- [ ] `dotnet test IOS-IG-SimHost.sln` all pass (or no regressions from pre-existing failures)
- [ ] Report submitted to `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-01-REPORT.md`
