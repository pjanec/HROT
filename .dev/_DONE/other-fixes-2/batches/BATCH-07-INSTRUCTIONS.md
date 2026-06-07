# BATCH-07: Bookkeeping, Test Quality & Debt Tracker Fixes

**Batch Number:** BATCH-07  
**Tasks:** FIX2-015, FIX2-016, FIX2-017, FIX2-018, FIX2-019, FIX2-020, FIX2-021  
**Priority:** LOW–LOW-MEDIUM  
**Dependencies:** None from previous batches

---

## Mandatory Workflow

**Read AGENTS.md at the repo root before writing a single line of code.**

Complete tasks in order. For each task:
1. Define the **success condition** BEFORE touching any code.
2. Implement the fix.
3. Write / update tests where required.
4. Run the relevant test project and confirm all tests pass.
5. Fix any failures before moving to the next task.

Do NOT ask for permission at any step. Do NOT stop early. Finish all seven tasks, make all tests green, then write the report.

---

## Onboarding & Workflow

### Required Reading
1. **Task details:** `.dev/other-fixes-2/TASK-DETAIL.md` -- sections FIX2-015 through FIX2-021
2. **Blueprints-1 debt tracker:** `.dev/blueprints-1/DEBT-TRACKER.md`
3. **Blueprints-2 debt tracker:** `.dev/blueprints-2/DEBT-TRACKER.md`
4. **Breakpoints-1 debt tracker:** `.dev/breakpoints-1/DEBT-TRACKER.md` (if it exists, else search)

### Source Code Areas (vary by task -- search as needed)
- **FIX2-015/016:** Debt tracker markdown files in `.dev/blueprints-1/` and `.dev/blueprints-2/`
- **FIX2-017:** `Hrot/Subsystems/AI/CgfSubsystem/CgfSubsystem.cs` (CgfNoOpTimeController, `_bpPreTickSnapshot`), `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs`
- **FIX2-018:** BTree composite emitter tests -- search for `BTreeFluentEmitterTests` or `CompositeEmitter`
- **FIX2-019:** `AtomicMultiFileWriter` tests -- search for `AtomicMultiFileWriterTests`
- **FIX2-020:** `ChildOrderDeterminismTests` -- search for the file
- **FIX2-021:** `UtilityFluentEmitterTests`, `UtilityAssetLoader` -- search in `Hrot/Subsystems/AI/`

### Build & Test
```
cd d:\WORK\IOS-IG-SimHost-FDP
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --filter "FullyQualifiedName!~AllocationFree" --nologo
```
For utility emitter tests (if in a different project):
```
dotnet build Hrot\Subsystems\AI\Hrot.UtilityDecision.Editor.Tests\Hrot.UtilityDecision.Editor.Tests.csproj --nologo -v q
dotnet test Hrot\Subsystems\AI\Hrot.UtilityDecision.Editor.Tests\Hrot.UtilityDecision.Editor.Tests.csproj --nologo
```
For atomic writer / node editor tests:
```
dotnet test Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Hrot.Blueprints.Tests.csproj --nologo
```

### Report Submission
Submit report to: `.dev/other-fixes-2/reports/BATCH-07-REPORT.md`

---

## Tasks

### Task 1 -- FIX2-015: Address remaining open debt in blueprints-1 DEBT-TRACKER

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-015`

**Success condition:** After this task:
- DEBT-018 and DEBT-022 are either implemented (with tests) or explicitly deferred with a clear comment.
- DEBT-003 has a source comment matching its tracker note.
- All six addressed rows are marked RESOLVED in `.dev/blueprints-1/DEBT-TRACKER.md`.

**What to fix:**
1. Read `.dev/blueprints-1/DEBT-TRACKER.md` to see the current state.
2. For DEBT-018 (debug files folder placement): find where debug output files are written; either move the path or add a `// DEBT-018 deferred: [reason]` comment in the code.
3. For DEBT-022 (`GetNodeHistory(Entity,int)` not on interface): either add the method to `IBlueprintDebugSession` and implement it, or mark as explicitly deferred with a comment.
4. For DEBT-003: find the code referenced by DEBT-003 and add a source comment `// DEBT-003: [description]`.
5. Mark DEBT-003, DEBT-004, DEBT-021, DEBT-023 as RESOLVED in the tracker (they already have code fixes per the task detail).

No test required for pure tracker/comment-only changes. If DEBT-022 is implemented, add a test.

---

### Task 2 -- FIX2-016: Mark blueprints-2 D-03 and D-04 as RESOLVED

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-016`

**Success condition:** `.dev/blueprints-2/DEBT-TRACKER.md` rows D-03 and D-04 are marked RESOLVED. D-01 remains as-is (intentionally deferred).

**What to fix:** Open `.dev/blueprints-2/DEBT-TRACKER.md` and update the rows for D-03 and D-04 to RESOLVED status. No code change needed.

---

### Task 3 -- FIX2-017: Implement breakpoints-1 open debt items

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-017`

**Success condition:**
- D-BP-01: `CgfNoOpTimeController.IsPausedByDebugger` returns a real value (not hardcoded false).
- D-BP-02: `_bpPreTickSnapshot` in `CgfSubsystem` mirrors more than just `CgfComponentRegistry`.
- D-BP-04: `GraphEditorWindow.DrawUI()` has a right-click popup that reaches `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` through the UI path.

**What to fix:** Implement the three items per design, or if deferring, explicitly mark them deferred in the debt tracker with a technical reason. For D-BP-04 specifically (the user-facing breakpoint menu), at minimum wire the right-click handler even if the ImGui context is headless (use `LastClickedNode` observable or similar).

**Tests required (if implementing D-BP-04):**
- A test that constructs `GraphEditorWindow`, sets a right-click on a node, calls `DrawUI()`, and asserts `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` was called.

---

### Task 4 -- FIX2-018: BTree composite emitter -- add Roslyn compile assertion

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-018`

**Success condition:** A test compiles a complex BTree (composite with decorators/pills) using `BTreeFluentEmitter.EmitComposite`, feeds the output to `Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText`, and asserts no parse diagnostics. If the emitter produces `;,` or other invalid C#, the test fails.

**What to fix:** Add a Roslyn parse/compile assertion to the existing BTree composite emitter tests. The test must invoke `EmitComposite` with a representative complex tree and then call `CSharpSyntaxTree.ParseText(emittedCode).GetDiagnostics()` and assert it is empty.

**Test required:** Add to the existing test file (do not create a new file if avoidable).

---

### Task 5 -- FIX2-019: AtomicMultiFileWriter -- add two-file partial-batch test

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-019`

**Success condition:** A test uses a two-file dict where file-1 moves successfully and file-2 fails during the move phase. The test asserts `result.SuccessfullyWritten` contains file-1 and `result.Failed` contains file-2.

**What to fix:** Add a new test case to `AtomicMultiFileWriterTests.cs` exercising the two-file partial-batch scenario. Use a mock file system or a temp directory with a read-only file to force the second move to fail.

**Test required:** Two-file scenario asserting partial `SuccessfullyWritten` accumulation.

---

### Task 6 -- FIX2-020: ChildOrderDeterminismTests -- use real production model

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-020`

**Success condition:** `ChildOrderDeterminismTests` exercises the real production `IContainerNodeModel` implementations (`StateNode` or `Demo.FakeContainerModel`). If a Dictionary/HashSet-backed production impl were introduced, it would NOT pass the tests.

**What to fix:** Replace the private test-local `FakeContainerModel` with the real production `IContainerNodeModel` type (e.g. `StateNode` from the HSM model or the `NodeEditor.Demo` type referenced in the task detail). Assert child ordering on the real type.

**Test required:** Same assertions, but exercising the real production type.

---

### Task 7 -- FIX2-021: Utility emitter round-trip -- implement reflect step and full structural assertion

**Full details:** `.dev/other-fixes-2/TASK-DETAIL.md#fix2-021`

**Success condition:** The utility emitter round-trip test:
1. Emits a `UtilityDecisionAsset` to C#.
2. Parses it with Roslyn (already done).
3. Loads it back via `UtilityAssetLoader.Load` (reflect step).
4. Asserts full structural equality (weights, contexts, curves, consideration order) against the original model -- NO alphabetical sorting.

If the emitter inverts an ordering or zeros a weight, the test fails.

**What to fix:**
1. Implement `UtilityAssetLoader` consideration/option parsing (the task detail says "defers options/considerations parsing -- the reflect path doesn't exist yet").
2. Update `UtilityFluentEmitterTests` round-trip test to remove the alphabetical sort and compare full structural equality.

**Tests required:** Updated round-trip test that verifies weights, contexts, curves, and ordering.

---

## Quality Standards

**PRODUCTION PATH for FIX2-018 through FIX2-021:** Tests must drive production emitter/loader/writer paths. Test-local stubs used to satisfy interface contracts are acceptable only if the code under test is the real production class.

**ALL EXISTING TESTS MUST STAY GREEN.**

---

## Developer Insights (Report Questions)

1. For FIX2-017: did you implement D-BP-01/02/04 fully or defer them? If deferred, what is the technical reason?
2. For FIX2-021: what was the structure of `UtilityAssetLoader.Load` -- did it need a new parsing path or was it already partially implemented?
3. Any edge cases discovered during the bookkeeping tasks (e.g., tracker rows missing from the files)?
4. **Suggested commit message** for this batch.
