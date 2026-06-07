# BATCH-01: FastBTree Deactivator — Core Library (Phase 1)

**Batch Number:** BATCH-01
**Tasks:** TASK-EQL-001, TASK-EQL-002, TASK-EQL-003
**Phase:** Phase 1 — FastBTree Library (Fbt.Kernel, isolated)
**Estimated Effort:** 12–16 hours
**Priority:** HIGH
**Dependencies:** None (this is the first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch implements the complete Phase 1 deactivator infrastructure inside the
`FastBTree` library (`Fbt.Kernel`), verified entirely by `Fbt.Tests` with no engine
dependencies. You will add the delegate type and attribute, extend `ActionRegistry`, and
add delta-tracking + deactivator invocation to `Interpreter`.

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/ai-btree-deactivator-1/ONBOARDING.md` — project overview, key types,
   build commands.
2. **Design Document:** `.dev/ai-btree-deactivator-1/DESIGN.md` — read §1.1 through §1.5 in full
   before writing any code. The delta-tracking algorithm, `pathWasReset` flag, Parallel subtree
   sweep, and ordering constraint are all specified there.
3. **Task Specifications:** `.dev/ai-btree-deactivator-1/TASK-DETAIL.md` — read the Phase 1
   section (TASK-EQL-001, TASK-EQL-002, TASK-EQL-003) for success conditions and constraints.
4. **Reference test file:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BTreeNewFeaturesTests.cs`
   — this is the canonical example for how to write isolated interpreter tests using
   `TestBlackboard`, `MockContext`, and manually-constructed `BehaviorTreeBlob` objects.
   Read this file fully before writing `HybridLifecycleTests.cs`.
5. **Interpreter source:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` —
   understand `Tick`, `ExecuteNode`, `ExecuteSelector`, `ExecuteParallel` before modifying.
6. **ActionRegistry source:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/ActionRegistry.cs`
   — understand existing `Register`/`TryGetAction` pattern before extending.
7. **Existing attribute:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeActionAttribute.cs`
   — the new `BTreeDeactivatorAttribute` follows the same conventions.

### Source Code Locations

- **Primary work area:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/`
  - New delegate: `NodeDeactivatorDelegate.cs` (alongside `NodeLogicDelegate.cs`)
  - New attribute: `Attributes/BTreeDeactivatorAttribute.cs`
  - Extended: `Runtime/ActionRegistry.cs`
  - Extended: `Runtime/Interpreter.cs`
- **Test project:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/`
  - New: `HybridLifecycleTests.cs`

### Build & Test Commands

Run from the solution root `D:\WORK\IOS-IG-SimHost-FDP`:

```powershell
# Test FastBTree in isolation
dotnet test FDP\ExtDeps\FastBTree\FastBTree.sln --no-restore

# Or for a specific test class
dotnet test FDP\ExtDeps\FastBTree\FastBTree.sln --filter "ClassName=HybridLifecycleTests"
```

### Report Submission

When done, submit your report to:
`.dev/ai-btree-deactivator-1/reports/BATCH-01-REPORT.md`

If you have blocking questions, create:
`.dev/ai-btree-deactivator-1/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch delivers the entire Phase 1 deactivator infrastructure. Phases 2 (Roslyn
generator) and 3 (engine integration) both depend on Phase 1 being complete and green.
No engine code is touched in this batch — all capability is proven by `Fbt.Tests` alone.

---

## Batch Objectives

1. Define `NodeDeactivatorDelegate<TBlackboard, TContext>` and `BTreeDeactivatorAttribute`.
2. Extend `ActionRegistry` with deactivator registration and lookup.
3. Extend `Interpreter.Tick` with pre/post-tick delta tracking, deactivator invocation,
   Parallel subtree sweep, and hot-reload ordering.
4. Write `HybridLifecycleTests.cs` covering all 10 success-condition test scenarios from
   TASK-EQL-003 (mapped from design test IDs L-01 through L-08 plus edge cases).

---

## Tasks

### TASK-EQL-001 — NodeDeactivatorDelegate and BTreeDeactivatorAttribute

**Design reference:** DESIGN.md §1.1, §1.2
**Task specification:** TASK-DETAIL.md — TASK-EQL-001

Add two new files to `Fbt.Kernel`:

1. `NodeDeactivatorDelegate.cs` — delegate type in namespace `Fbt`, mirroring
   `NodeLogicDelegate` signature but returning `void`.
2. `Attributes/BTreeDeactivatorAttribute.cs` — attribute in namespace `Fbt` with a single
   constructor argument `string targetAction`.

Follow the exact signatures from DESIGN.md §1.1 and §1.2. Do not deviate.

**Success conditions:** All T1–T5 from TASK-DETAIL.md TASK-EQL-001.

---

### TASK-EQL-002 — ActionRegistry deactivator support

**Design reference:** DESIGN.md §1.3
**Task specification:** TASK-DETAIL.md — TASK-EQL-002

Extend `ActionRegistry<TBlackboard, TContext>` with:
- A new `Dictionary<string, NodeDeactivatorDelegate<TBlackboard, TContext>>` field
  (`_deactivators`), parallel to `_actions`.
- `RegisterDeactivator(string key, NodeDeactivatorDelegate<TBlackboard, TContext> deactivator)`
  — throw `ArgumentNullException` for null key or null delegate; last-write-wins on duplicate.
- `TryGetDeactivator(string key, out NodeDeactivatorDelegate<TBlackboard, TContext>? deactivator)`
  — returns `false`/null when not found; mirrors `TryGetAction` semantics.

**Success conditions:** All T1–T5 from TASK-DETAIL.md TASK-EQL-002.

---

### TASK-EQL-003 — Interpreter deactivator array and delta tracking

**Design reference:** DESIGN.md §1.4 (full section — read every paragraph), §3.5
**Task specification:** TASK-DETAIL.md — TASK-EQL-003 (read the full success conditions T1–T10)

This is the most complex task. Implement in `Interpreter<TBlackboard, TContext>`:

#### 1. `_deactivatorDelegates` field

```csharp
private readonly NodeDeactivatorDelegate<TBlackboard, TContext>?[] _deactivatorDelegates;
```

Populated in the constructor by iterating `blob.MethodNames` and calling
`registry.TryGetDeactivator` for each entry. Length equals `blob.MethodNames.Length`.
When `blob.MethodNames` is null or empty, use `Array.Empty<...>()`.

#### 2. `InvokeDeactivatorIfRegistered` helper

Guard on `node.Type is NodeType.Action or NodeType.Condition` before using `node.PayloadIndex`.
See exact code in DESIGN.md §1.4.

#### 3. `Tick` delta tracking

Follow the ordering in DESIGN.md §3.5 precisely:

1. Snapshot `oldPath` (9-element `stackalloc ushort[9]`) — BEFORE any structural
   bounds-check.
2. Structural bounds-check with `pathWasReset` flag: if `RunningNodeIndex >= blob.Nodes.Length`
   and `RunningNodeIndex > 0`, sweep `oldPath` against an empty path immediately, reset
   `RunningNodeIndex = 0` and `StackPointer = 0`, increment `state.TreeVersion` (unchecked),
   set `pathWasReset = true`. Do NOT return — continue to `ExecuteNode`.
3. Call `ExecuteNode(0, ...)`.
4. If `!pathWasReset`: snapshot `newPath` (same layout as `oldPath`), sweep `oldPath` for
   entries not in `newPath`, call `InvokeDeactivatorIfRegistered` for each.

The 9-element path layout: slots 0–7 = `state.NodeIndexStack[0..7]`; slot 8 = `state.RunningNodeIndex`.

#### 4. Parallel subtree sweep

When `InvokeDeactivatorIfRegistered` is called on a `NodeType.Parallel` node that exited the
path: for each direct child of the Parallel whose completion bit is NOT set in
`state.LocalRegisters`, iterate every node index in
`[childIndex, childIndex + childNode.SubtreeOffset)` and call `InvokeDeactivatorIfRegistered`
on each. See DESIGN.md §1.4 for the exact description.

**No heap allocation.** Both `oldPath` and `newPath` are `stackalloc` only.

**Success conditions:** All T1–T10 from TASK-DETAIL.md TASK-EQL-003 must pass.

---

## Test Requirements for HybridLifecycleTests.cs

Create `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/HybridLifecycleTests.cs`.

The test class must cover all scenarios from DESIGN.md §1.5 (L-01 through L-08) AND all
success conditions from TASK-DETAIL.md TASK-EQL-003 (T1–T10). Many map to the same test;
write distinct test methods for each success condition. **Minimum 10 test methods, targeting
all 10 success conditions.**

### Quality standards

**REQUIRED — tests must verify behavior, not just compilation:**
- Every test that registers a deactivator must assert a specific **count** of invocations
  (0, 1, or N as appropriate). Do not assert merely that no exception was thrown.
- Tests for "only exited node fires" (L-05/T5) must use two distinct deactivator counters
  and assert both independently.
- Test L-07/T9 (Parallel subtree sweep) must construct a real `Parallel` node with two
  `Sequence` children, each containing a resource-owning `Action`, and assert both deactivators
  fired exactly once. Do not fake this by checking only one child.
- Test L-08/T10 (hot-reload) must assert (a) deactivator fires before reset, (b)
  `RunningNodeIndex == 0` after, (c) execution continues (tree evaluates) on the same frame,
  (d) the post-tick sweep does NOT fire again (count remains 1, not 2).
- Test T7 (deactivator exception propagates): assert with `Assert.Throws` or equivalent that
  the exception is NOT swallowed.

**NOT ACCEPTABLE:**
- Tests that only assert `Assert.DoesNotThrow` for the main logic paths.
- Tests that assert a deactivator "was called" without checking the exact count.
- Tests that skip T9 (Parallel subtree) or T10 (hot-reload).

### Test-Driven Task Progression (MANDATORY WORKFLOW)

```
Task 1 (EQL-001): Implement → Write T1-T5 from EQL-001 → ALL pass
Task 2 (EQL-002): Implement → Write T1-T5 from EQL-002 → ALL pass
Task 3 (EQL-003): Implement → Write T1-T10 tests → ALL pass (including prior tasks)
```

**DO NOT** move to the next task until:
- Current task implementation is complete.
- Current task tests are written and passing.
- ALL tests from previous tasks still pass.

**Why:** Each task builds on the previous. The interpreter depends on the registry which
depends on the delegate type. Building top-down without tests leads to cascading failures.

---

## Developer Insights Section (required in report)

Your report MUST answer:

1. **What issues were encountered?** Describe any compiler errors, unexpected behavior in
   the `BehaviorTreeState` layout, or edge cases in the interpreter that required deviation
   from the spec.
2. **What weak points did you spot in the existing codebase?** E.g., the `NodeIndexStack`
   layout, the `LocalRegisters` encoding used by `Parallel`, any implicit assumptions in
   `Interpreter.Tick`.
3. **What design decisions did you make beyond the spec?** E.g., any helper method names,
   access modifiers, or test infrastructure choices.
4. **Did you find any gaps in the DESIGN.md spec?** Specifically around the Parallel subtree
   sweep or the `pathWasReset` hot-reload path.

---

## Report Format

Submit to `.dev/ai-btree-deactivator-1/reports/BATCH-01-REPORT.md`:

```markdown
# BATCH-01 Report

## Summary
[2-3 sentence summary of what was implemented]

## Tasks Completed
- [x] TASK-EQL-001 — NodeDeactivatorDelegate and BTreeDeactivatorAttribute
- [x] TASK-EQL-002 — ActionRegistry deactivator support
- [x] TASK-EQL-003 — Interpreter deactivator array and delta tracking

## Test Results
[Output of: dotnet test FDP\ExtDeps\FastBTree\FastBTree.sln --no-restore]

## Files Changed
[List every file created or modified with a one-line description]

## Developer Insights

### Issues Encountered
[Required — see prompts above]

### Weak Points Spotted
[Required — see prompts above]

### Design Decisions Beyond Spec
[Required — see prompts above]

### Gaps Found in DESIGN.md
[Required — see prompts above]
```
