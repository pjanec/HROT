# BATCH-01: Blueprint Compiler Critical Fixes

**Batch Number:** BATCH-01  
**Tasks:** BPF-014, BPF-015, BPF-016, BPF-019, BPF-020, BPF-039, BPF-040, BPF-041, BPF-050  
**Source:** `.dev/blueprint-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/blueprint-fixes-1/TASK-TRACKER.md`  
**Estimated Effort:** 12-16 hours  
**Priority:** CRITICAL -- three of these (BPF-014/015/016) produce uncompilable or silently broken generated C#  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch fixes critical and high-severity defects in the **Blueprint compiler** -- the component that takes Blueprint IR graphs and emits compilable C# code. Three bugs (BPF-014, BPF-015, BPF-016) produce either a CS1501 compile error or silently dead runtime behavior (breakpoints, step debugger, custom events all broken). Two more (BPF-019, BPF-020) introduce use-before-define bugs and drop custom-event dispatch. The remaining four (BPF-039, BPF-040, BPF-041, BPF-050) fix compiler non-determinism and test coverage gaps.

All work is in the compiler subsystem. Stay in the compiler area; do not refactor unrelated code.

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- read BPF-014, BPF-015, BPF-016, BPF-019, BPF-020, BPF-039, BPF-040, BPF-041, BPF-050 sections in full before touching any code
2. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
3. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture` to understand the codebase before editing files. Use `mcp_codebase-memo_get_code_snippet` to read exact implementations before editing. Use `mcp_codebase-memo_search_graph` to find symbols by name.

### Source Code Location
- **Primary compiler area:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/`
- **Compiler emit stage:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/`
- **Compiler IR ops:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/IR/` (or similar -- verify via graph)
- **Compiler tests:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/` and/or `Hrot.Blueprints.Compiler.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev/blueprint-fixes-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/blueprint-fixes-1/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in strict sequence:**

For **each task**:
1. **Define the success condition** -- read the TASK-DETAIL entry, state exactly what a correct implementation looks like and what tests will prove it
2. **Implement the fix** -- make the minimal correct change
3. **Write / fix tests** -- tests must exercise the actual behavior, not string presence
4. **Run all tests** -- `dotnet test` on the compiler test project; ALL must pass
5. **Fix any discrepancies** -- if tests fail, fix the root cause; do not skip or comment out
6. Only when ALL tests pass: move to the next task

**DO NOT** move to the next task until all tests are green. Do not finish the batch and write the report until every task is done and all tests pass.

**No stopping to ask permission for obvious steps like running tests or fixing compilation errors.**

---

## Context

The Blueprint compiler reads IR graph nodes and emits C# source. Several emit operations are implemented as no-op comments or read from the wrong state field, making the emitted code either uncompilable or silently wrong. This batch focuses exclusively on fixing those emit defects plus closing two compiler determinism gaps.

---

## ✅ Tasks

### Task 1: BPF-014 -- LatentDelay resume reads wrong state field (CRITICAL)

**Task Definition:** See [TASK-DETAIL.md BPF-014](../TASK-DETAIL.md#bpf-014----instance-latentdelay-resume-reads-workingstate-field-instead-of-the-cursor-compiler)

**Success Condition (define before implementing):**  
The emitted C# for a `LatentDelay` node must read `s.Cursor.WaitUntilTime` (the per-slot cursor field) and not `ws.__waitUntilTime` (the working-state scratch field). A unit test must compile the emitted code and verify the correct field reference.

**What to do:**
1. Use `mcp_codebase-memo_search_graph` to find the emit method responsible for `LatentDelay` / resume check (look for symbols containing `LatentDelay` or `WaitUntil` in the compiler emit area).
2. Read the exact code via `get_code_snippet`.
3. Fix the field reference from `ws.__waitUntilTime` to `s.Cursor.WaitUntilTime` (verify exact names in context).
4. Write a test that emits a blueprint containing a LatentDelay node and asserts the emitted text contains `s.Cursor.WaitUntilTime` and does NOT contain `ws.__waitUntilTime` (or whatever the wrong field is). Prefer a test that compiles the emitted code if the test infrastructure supports it.

**Tests Required:**
- A test verifying the correct field is referenced in emitted code for a LatentDelay node
- Run the full compiler test suite after

---

### Task 2: BPF-015 -- DebugProbe.NodeEnter/PinValue emitted as a C# comment (CRITICAL)

**Task Definition:** See [TASK-DETAIL.md BPF-015](../TASK-DETAIL.md#bpf-015----debugprobenodeenterpinvalue-emitted-as-a-c-comment-not-a-call-compiler-found-by-2-clusters)

**Success Condition (define before implementing):**  
`DebugProbe.NodeEnter(...)` and `DebugProbe.PinValue(...)` must be emitted as actual C# method calls, not commented-out code. A test must verify the emitted output contains callable (non-commented) invocations of these methods.

**What to do:**
1. Find the emit path for `NodeEnter` and `PinValue` probe calls in the compiler.
2. Identify where `//` is prepended or where the call is emitted as a comment string.
3. Fix to emit as a real call.
4. Write a test: emit a blueprint with at least one node entry; assert the emitted code contains a non-commented `DebugProbe.NodeEnter` call (use regex or substring check that verifies the call is NOT inside a comment).

**Tests Required:**
- Test verifying NodeEnter emitted as real call, not comment
- Test verifying PinValue emitted as real call (if applicable)

---

### Task 3: BPF-016 -- Event-poll call site omits payload args -- CS1501 uncompilable (CRITICAL)

**Task Definition:** See [TASK-DETAIL.md BPF-016](../TASK-DETAIL.md#bpf-016----event-poll-call-site-omits-payload-args---uncompilable-generated-c-compiler)

**Success Condition (define before implementing):**  
The emitted event-poll call must include all payload arguments that the method signature requires; the emitted C# must compile without CS1501. Additionally, any stray `deltaTime` argument that does not belong to the event-poll signature must be removed.

**What to do:**
1. Find the event-poll emit logic in the compiler.
2. Read the event-poll method signature being called (on the generated class or the engine API).
3. Fix the argument list emitted at the call site.
4. Write a test that emits a blueprint with an event-poll node and compiles the emitted code; assert compilation succeeds (no CS1501).

**Tests Required:**
- Emit a blueprint with event-poll; compile the output; assert no compile error
- If the test harness can't run Roslyn compilation, at minimum assert the emitted argument list matches the method signature

---

### Task 4: BPF-019 -- BuildReturnTerminator resolves into last-allocated block, not current (HIGH)

**Task Definition:** See [TASK-DETAIL.md BPF-019](../TASK-DETAIL.md#bpf-019----buildreturnterminator-resolves-return-value-into-the-last-allocated-block-not-the-current-block-compiler)

**Success Condition (define before implementing):**  
`BuildReturnTerminator` must resolve the return value into the **current** IR block, not the last-allocated block. A test must verify that a blueprint with a return node in a non-final block produces a return from the correct block.

**What to do:**
1. Find `BuildReturnTerminator` in the compiler IR/emit code.
2. Identify the block-resolution logic (which block it resolves into).
3. Fix to use the current block context.
4. Write a test: build a blueprint with at least two blocks where the return is in a non-final block; verify the IR / emitted code places the return in the correct block.

**Tests Required:**
- Test verifying return is placed in correct block when multiple blocks exist

---

### Task 5: BPF-020 -- IrOp_RaiseCustomEvent emitted as a C# comment (HIGH)

**Task Definition:** See [TASK-DETAIL.md BPF-020](../TASK-DETAIL.md#bpf-020----irop_raisecustomevent-emitted-as-a-comment---custom-event-dispatch-silently-dropped-compiler)

**Success Condition (define before implementing):**  
`IrOp_RaiseCustomEvent` must emit a real C# method call that dispatches the custom event. Custom event dispatch cannot be silently dropped.

**What to do:**
1. Find the `IrOp_RaiseCustomEvent` emit handler.
2. Fix to emit a real call (not a comment).
3. Write a test verifying a blueprint with a RaiseCustomEvent node emits a callable dispatch statement.

**Tests Required:**
- Test verifying RaiseCustomEvent emits a real (non-comment) dispatch call

---

### Task 6: BPF-039 -- GetOrdered appends residual fields via dict.Values (non-deterministic) (MEDIUM)

**Task Definition:** See [TASK-DETAIL.md BPF-039](../TASK-DETAIL.md#bpf-039----getordered-appends-residual-fields-via-dictvalues-non-deterministic-compiler)

**Success Condition (define before implementing):**  
`GetOrdered` must produce a stable, deterministic field ordering across runs. Residual (unordered) fields must be appended in a stable order (e.g. sorted by name or stable key). A test must verify the same set of fields always produces the same ordered list regardless of dictionary insertion order.

**What to do:**
1. Find `GetOrdered` in the compiler.
2. Fix the residual-field append to use a sorted enumeration.
3. Write a test: provide a set of fields with deliberate out-of-insertion-order keys; verify the output ordering is stable (run with different insertion orders or assert a specific deterministic order).

**Tests Required:**
- Test verifying GetOrdered output is stable across multiple calls with same input

---

### Task 7: BPF-040 -- MetadataReferenceResolver does not sort references (MEDIUM)

**Task Definition:** See [TASK-DETAIL.md BPF-040](../TASK-DETAIL.md#bpf-040----metadatareferenceresolver-does-not-sort-references-determinism-m-9-compiler)

**Success Condition (define before implementing):**  
The metadata reference list produced must be sorted by a stable key (e.g. assembly name or path) so that repeated compilations with the same input produce identical reference lists.

**What to do:**
1. Find `MetadataReferenceResolver` in the compiler.
2. Add a sort by a stable key before returning the reference list.
3. Write a test verifying the resolved references are in sorted order.

**Tests Required:**
- Test verifying reference list is sorted

---

### Task 8: BPF-041 -- Stage8 PDB embedded-source test is a size heuristic, not content verification (MEDIUM)

**Task Definition:** See [TASK-DETAIL.md BPF-041](../TASK-DETAIL.md#bpf-041----stage8-pdb-embedded-source-test-is-a-size-heuristic-not-content-verification-compiler)

**Success Condition (define before implementing):**  
The Stage8 PDB test must verify that the PDB actually contains the expected source content (e.g. extract embedded source from the PDB and compare to the original), not just check the PDB byte size.

**What to do:**
1. Find the Stage8 PDB test in the compiler tests.
2. Replace the size-heuristic assertion with a content check: read the embedded source from the PDB and verify it matches expected content.
3. Ensure the test would catch a regression where the source is not embedded.

**Tests Required:**
- Updated Stage8 test with content verification instead of size heuristic

---

### Task 9: BPF-050 -- Parallel-determinism compiler test not implemented (LOW)

**Task Definition:** See [TASK-DETAIL.md BPF-050](../TASK-DETAIL.md#bpf-050----parallel-determinism-compiler-test-178-not-implemented-compiler)

**Success Condition (define before implementing):**  
A test must compile the same blueprint in parallel (N concurrent compilations) and verify that all outputs are byte-identical. The test must reference the design's §17.8 requirement.

**What to do:**
1. Find where the design §17.8 is (check the compiler design doc, likely `Compiler_Detailed_Design.md` under `.dev/blueprints-1/`).
2. Implement a parallel-determinism test: run N=4+ concurrent compilations of the same blueprint; assert all emitted outputs are identical.

**Tests Required:**
- Parallel-determinism test per §17.8

---

## 🧪 Testing Requirements

- Run the full compiler test project after each task: `dotnet test [path to Hrot.Blueprints.Compiler.Tests or Hrot.Blueprints.Tests]` -- find the exact project path via the codebase graph before running
- All existing tests must continue to pass after your changes
- New tests must verify **actual behavior** (correct field references, compilable output, stable ordering) not just string presence or object existence
- Do not add comments or docstrings to code you did not change

## ⚠️ Quality Standards

**TEST QUALITY EXPECTATIONS:**
- **NOT ACCEPTABLE:** `Assert.Contains("WaitUntilTime", emittedCode)` with no structural verification -- a comment containing the string would still pass
- **REQUIRED:** Either compile the emitted code and run it, OR assert the string is in a non-comment position (e.g. use a regex that excludes comment lines)
- **NOT ACCEPTABLE:** Tests that pass even when the fix is reverted
- **REQUIRED:** Tests that would FAIL if the bug was reintroduced

## 📊 Report Requirements

Submit `.dev/blueprint-fixes-1/reports/BATCH-01-REPORT.md` using the template at `.dev/.guides/BATCH-REPORT-TEMPLATE.md`.

**Required sections:**
- Tasks completed (BPF-014 through BPF-050 as listed above)
- Test results: test count and pass/fail
- Issues encountered during implementation and how you resolved them
- Design decisions you made beyond the spec
- Weak points or improvement opportunities spotted in the codebase
- Edge cases discovered not mentioned in the instructions
- Suggested commit message

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BPF-014 fixed: LatentDelay emits `s.Cursor.WaitUntilTime`; test passes
- [ ] BPF-015 fixed: DebugProbe calls emitted as real calls; test passes
- [ ] BPF-016 fixed: Event-poll emits correct arguments; test passes (or compilation verified)
- [ ] BPF-019 fixed: BuildReturnTerminator uses current block; test passes
- [ ] BPF-020 fixed: RaiseCustomEvent emits real dispatch; test passes
- [ ] BPF-039 fixed: GetOrdered is deterministic; test passes
- [ ] BPF-040 fixed: MetadataReferenceResolver sorts references; test passes
- [ ] BPF-041 fixed: Stage8 PDB test verifies content; test passes
- [ ] BPF-050 done: Parallel-determinism test implemented and passes
- [ ] All pre-existing compiler tests still pass
- [ ] Report submitted

---

## 📚 Reference Materials
- **Task Details:** `.dev/blueprint-fixes-1/TASK-DETAIL.md` -- sections BPF-014..BPF-050 (as listed)
- **Compiler Design:** `.dev/blueprints-1/Blueprint_Subsystem_Compiler_Detailed_Design.md` (and InlinePatches)
- **Debug Protocol Design:** `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` (for DebugProbe context)
- **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`
