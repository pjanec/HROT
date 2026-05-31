# BATCH-01: Blueprint Windows Wiring, Breakpoint Menu Decision, and StateNode Coverage

**Batch Number:** BATCH-01
**Tasks:** FIX3-001, FIX3-002, FIX3-003
**Topic:** other-fixes-3 (final round-3 stragglers)
**Estimated Effort:** 4-8 hours
**Priority:** HIGH (FIX3-001), LOW-MEDIUM (FIX3-002), LOW (FIX3-003)
**Dependencies:** None (all round-2 work complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

You are completing the **final 3 items** from the round-3 conformance verification pass. The
round-2 developer did good structural work; what remains is the "last wiring mile" for
`BlueprintWindowRegistrar`, a user-facing decision on the breakpoint right-click menu, and
one missing test on `StateNode`.

Read the task details carefully -- they are precise. Do NOT guess at file paths; use the
codebase-memory MCP tools to locate symbols by name.

### Required Reading (IN ORDER)

1. **Task Details (ALL three):** `.dev/other-fixes-3/TASK-DETAIL.md` -- full lineage, what is
   done, what remains, and the prescribed fix for each item.
2. **Tracker:** `.dev/other-fixes-3/TASK-TRACKER.md` -- status checkboxes to update when done.
3. **Developer guide:** `.dev/.guides/DEV-GUIDE.md` (if present) -- general workflow.

### Source Code Areas

- `BlueprintWindowRegistrar` and related editor subsystem files (Blueprints editor area)
- `GraphEditorWindow.cs` and `BlueprintBreakpointMenuPopulator` (breakpoints area)
- `StateNode` in `HsmAsset.cs` and `ChildOrderDeterminismTests` (HSM/node-editor area)

Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_search_graph` to locate exact
file paths before editing.

### Report Submission

When done, submit your report to: `.dev/other-fixes-3/reports/BATCH-01-REPORT.md`

If you have questions: `.dev/other-fixes-3/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Success-Condition-First, Test-Driven Task Progression

**CRITICAL: For each task, define the success condition FIRST before any implementation.**

For each task:
1. **State the success condition** -- what exact observable change proves this is done?
2. **Implement** the production fix or test.
3. **Run the tests** for the relevant project and confirm they pass.
4. **Fix root causes** of any failures before moving to the next task.
5. Only move to the next task when ALL tests pass.

**DO NOT** stop to ask permission for obvious steps (running tests, fixing compilation
errors, adjusting test assertions to match actual behaviour). Do it all until everything
is green, then write the report.

---

## Context

Full lineage and code references for all three tasks are in
`.dev/other-fixes-3/TASK-DETAIL.md`. The sections below are summaries; the TASK-DETAIL.md
entries are authoritative. Read them first.

---

## Tasks

### Task 1: FIX3-001 -- Wire `BlueprintWindowRegistrar` into the production window-registration pass

**Full details:** `.dev/other-fixes-3/TASK-DETAIL.md` section `FIX3-001`

**Summary of what remains:** `BlueprintWindowRegistrar` is a correct standalone DI service,
but `LocalWindowController.RegisterWindows` only iterates `ISubsystem[]`. Nothing resolves
the DI `IWindowRegistrar` and calls `RegisterWindows(wm)` on it. The 7 blueprint windows
are therefore never registered at runtime.

**Prescribed fix (choose one approach -- (a) is preferred as least invasive):**

- **(a)** In `EditorSubsystem.RegisterWindows` and/or `SimHostSubsystem.RegisterWindows`,
  resolve `BlueprintWindowRegistrar` from the DI container (it is already registered as
  `IWindowRegistrar`) and call `registrar.RegisterWindows(wm)`.
- **(b)** Make `BlueprintWindowRegistrar` implement `ISubsystem` so it appears in the
  `_subsystems` array that `LocalWindowController` already iterates.
- **(c)** Register `BlueprintWindowRegistrar` into the `_subsystems` list at construction.

Whichever approach you choose, document your reasoning in the report.

**Success condition (define before implementing):**
After the fix, the real production `LocalWindowController` window-registration pass must
invoke `BlueprintWindowRegistrar.RegisterWindows`. Verify this with a test.

**Required test:** An integration test (or a focused unit test) that exercises the actual
`LocalWindowController` window-registration pass (or the `EditorSubsystem`/
`SimHostSubsystem.RegisterWindows` call path) and **asserts that the blueprint windows
appear in the `WindowManager`** after the pass. Do not test through `BlueprintWindowRegistrar`
directly -- the production caller must be in the test's execution path.

**Test project to add to:** Use the same test project as `BlueprintWindowRegistrarTests`
(locate it via `mcp_codebase-memo_search_graph` for `BlueprintWindowRegistrarTests`).

**Build command to verify (run from repo root):**
```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

---

### Task 2: FIX3-002 -- D-BP-04 decision: implement blueprint-canvas right-click breakpoint menu or formally defer

**Full details:** `.dev/other-fixes-3/TASK-DETAIL.md` section `FIX3-002`

**Summary of what remains:** `GraphEditorWindow.DrawUI()` is still a canvas placeholder
(`ImGui.TextDisabled`). `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` is never
called from the UI. D-BP-02 is accepted as deferred (documented). D-BP-04 is user-facing.

**Decision to make -- in this order:**

1. **Locate `GraphEditorWindow.cs` and `BlueprintBreakpointMenuPopulator`.** Understand
   what `DrawUI()` currently does and how much canvas infrastructure exists.

2. **If the canvas is a stub/placeholder** (no real node rendering, no hit-testing):
   - Formally defer D-BP-04. Add a clear `// TODO(D-BP-04): wire PopulateNodeMenu into
     the canvas right-click handler once canvas rendering is implemented.` comment in
     `GraphEditorWindow.DrawUI()` at the location where the right-click handler would go.
   - Update `DEBT-TRACKER.md` (if it exists in this topic's folder or in `breakpoints-1`)
     to mark D-BP-04 as DEFERRED with rationale "canvas rendering not yet implemented;
     right-click handler cannot be wired without a rendered node to click on."

3. **If the canvas has real node rendering with hit-testing** (i.e., it could receive
   a right-click on a node):
   - Implement the right-click breakpoint menu by calling
     `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` from the right-click handler.
   - Add a test that exercises the populator being called when a node is right-clicked.

**Success condition (define before implementing):**
D-BP-04 is either implemented (menu reachable via UI) or formally deferred with a clear
code comment + DEBT-TRACKER entry. Either outcome closes FIX3-002.

**Test:** If implementing: add a test. If deferring: no new test needed, but confirm
existing breakpoint tests still pass.

**Build command to verify (run from repo root):**
```
dotnet build Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```

---

### Task 3: FIX3-003 -- Add `StateNode.ChildNodeIds` insertion-order determinism test

**Full details:** `.dev/other-fixes-3/TASK-DETAIL.md` section `FIX3-003`

**Summary of what remains:** `ChildOrderDeterminismTests` now uses the production
`FakeContainerModel`, but `StateNode.ChildNodeIds` (a LINQ projection over
`Children: List<StateNode>`) is a materially different code path that is not yet covered.

**Success condition (define before implementing):**
A new test in `ChildOrderDeterminismTests` (or a new test class in the same project)
instantiates a real `StateNode`, adds child `StateNode` instances in a defined order,
then reads `ChildNodeIds` and asserts the returned sequence matches the insertion order.

**Required test details:**
- Instantiate `StateNode` directly (it implements `IContainerNodeModel`).
- Add at least 3 children in a specific order (e.g., IDs 10, 30, 20).
- Call `ChildNodeIds` and assert the returned list equals `[10, 30, 20]` (insertion order,
  not sorted order -- this exercises the NEC-10 canonical-order invariant from the design).
- Do NOT use `FakeContainerModel` for this test -- use `StateNode` directly.

**Locate `StateNode` using:** `mcp_codebase-memo_search_graph` for `StateNode` in `HsmAsset.cs`.
**Locate the test class using:** `mcp_codebase-memo_search_graph` for `ChildOrderDeterminismTests`.

**Build and test command:**
Locate the test project that contains `ChildOrderDeterminismTests` and run:
```
dotnet test <path-to-test-project>.csproj --filter "FullyQualifiedName~ChildOrderDeterminism" --nologo
```
Then run all non-AllocationFree tests in that project to confirm nothing is broken.

---

## Testing Requirements

- **FIX3-001:** At least 1 integration test that exercises the production caller path
  (not just `BlueprintWindowRegistrar` in isolation).
- **FIX3-002:** At least 1 test if implementing the menu; or confirmation existing tests
  still pass if deferring.
- **FIX3-003:** At least 1 `StateNode` insertion-order test with >= 3 children.
- All previously passing tests must continue to pass.

---

## Quality Standards

**TEST QUALITY:**
- Tests must exercise **production code paths**, not test doubles or stubs standing in for
  the real code.
- Assertions must be on **observable behaviour** (windows registered, order preserved),
  not internal state or "can this compile."
- Do NOT write a test that only calls the method once with a trivial input and asserts
  no exception was thrown. That is a vacuous test.

**CODE QUALITY:**
- Do not add unnecessary abstractions, comments, or refactoring beyond what the task requires.
- Preserve all existing comments exactly.
- Minimize the diff -- only change lines required for the functional fix.

---

## Report Requirements

When done, create `.dev/other-fixes-3/reports/BATCH-01-REPORT.md` covering:

**For each task:**
1. What success condition you defined before implementation.
2. What approach you chose and why.
3. Exact file(s) changed and what changed.
4. Test(s) added and what they verify.
5. Test run output (final line summary, e.g., "Passed: 42, Failed: 0").

**Developer Insights:**
- Q1: What issues did you encounter? How did you resolve them?
- Q2: Did you spot any weak points in the existing codebase?
- Q3: What design decisions did you make beyond the instructions? Alternatives considered?
- Q4: Any edge cases discovered not mentioned in the spec?

---

## Success Criteria

This batch is DONE when:
- [ ] FIX3-001: Blueprint windows are registered via the production caller path; integration test passes.
- [ ] FIX3-002: D-BP-04 is either implemented with a test, or formally deferred with a TODO comment + DEBT-TRACKER update.
- [ ] FIX3-003: `StateNode` insertion-order test added and passing.
- [ ] All non-AllocationFree tests in all affected projects pass.
- [ ] TASK-TRACKER.md updated (mark FIX3-001, FIX3-002, FIX3-003 as [x]).
- [ ] Report submitted to `.dev/other-fixes-3/reports/BATCH-01-REPORT.md`.
