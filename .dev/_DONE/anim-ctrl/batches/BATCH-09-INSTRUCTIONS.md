# BATCH-09 INSTRUCTIONS — Phase 5 Completion & Phase 7 Infrastructure

**Batch ID:** BATCH-09  
**Status:** Ready for Implementation  
**Developer:** @developer-subagent  
**Build Target:** `IOS-IG-SimHost.sln`  
**Test Target:** All animation control tests (baseline 169 + new tests from this batch)

---

## Overview

This batch completes **Phase 5** (the last 2 deferred AiPrimitive integration tasks) and establishes the **integration test infrastructure** for Phase 7 (the networkless stage-1 validation suite).

**Scope:**
- **ANC-P5-07:** AiPrimitive registration + cross-subsystem reuse (BTree, HSM, Blueprint)
- **ANC-P5-08:** PlayMontageChainNode custom drawer (editor-side; may defer to tech debt if blocked)
- **ANC-P7-01:** `PumpUntil` + `IPumpableHarness` shared infrastructure
- **ANC-P7-02:** Animation diagnostics + command helpers
- **ANC-P7-03:** Integration fixture + inline TKB test data

**Exit Criteria:**
- All 5 tasks fully implemented
- No regressions: baseline 169 tests + new tests all passing
- Build clean (0 errors)
- Test quality verified per review criteria below

---

## Onboarding

### Recent Context
- **BATCH-08 (Phase 5 Part 2):** Look-at nodes, getter nodes, validators ANIM008–011. All approved. See [BATCH-08-REPORT.md](../reports/BATCH-08-REPORT.md).
- **BATCH-08 Review:** [BATCH-08-REVIEW.md](../reviews/BATCH-08-REVIEW.md) details test quality expectations (behavioral tests, no smoke tests, positive/negative path coverage).
- **DEBT-TRACKER:** [DEBT-TRACKER.md](../DEBT-TRACKER.md) tracks deferred tech debt; none block this batch, but D-03, D-07, D-08 noted for future review.

### Design References
Consult these design documents directly (do NOT restate in report):
- **DD-5 §11, §14.5:** ANC-P5-07 & ANC-P5-08 (AiPrimitive registration, custom drawer)
- **DD-5 §1:** AiPrimitive hosting pattern (BTree, HSM, Blueprint contexts)
- **DD-Tests §5.2, §7.1–4, §8, §11.3:** ANC-P7-01 through ANC-P7-03 (infrastructure, harness, fixture)

### Task Details
See [TASK-DETAIL.md](../TASK-DETAIL.md) for:
- [ANC-P5-07](../TASK-DETAIL.md#anc-p5-07--aiprimitive-registration--cross-subsystem-reuse)
- [ANC-P5-08](../TASK-DETAIL.md#anc-p5-08--playmontageechainnode-custom-drawer-editor)
- [ANC-P7-01](../TASK-DETAIL.md#anc-p7-01--pumpuntil--ipumpableharness-shared-infra)
- [ANC-P7-02](../TASK-DETAIL.md#anc-p7-02--animation-diagnostics--command-helpers)
- [ANC-P7-03](../TASK-DETAIL.md#anc-p7-03--integration-fixture--inline-tkb-test-data)

---

## Test-Driven Task Progression

**Mandatory Workflow:** For each task, follow this pattern:

1. **Define the contract** (interfaces, success conditions from TASK-DETAIL)
2. **Write test cases first** (at least 3 tests per task; see Examples below)
3. **Implement** to satisfy the tests
4. **Verify** all tests pass + no regressions
5. **Document findings** in the report (see Report Format section)

### Test Quality Expectations

Based on BATCH-08 review, tests must:
- ✅ **Verify behavior, not just existence.** Not "does the method exist?" but "does it do X correctly?"
- ✅ **Cover positive AND negative paths.** E.g., valid input passes; invalid input throws or warns.
- ✅ **Exercise the full contract.** For fixture tests, spawn entities, tick, drain bus, verify results.
- ✅ **Avoid over-simulation.** Fake backend is deterministic; use it; don't mock it away.
- ❌ **Avoid smoke tests.** Don't just call methods and check for no exceptions.

### Examples

**Example: ANC-P5-07 Test Structure**
```csharp
[Fact]
public void AiPrimitive_PlayMontageNode_RegisteredInBTreeEvaluator() {
    // Setup: Create BTree evaluator + test tree with PlayMontageNode
    var tree = CreateBTreeWithPlayMontageNode();
    var evaluator = new BTreeEvaluator();
    
    // Action: Evaluate tree
    var result = evaluator.Evaluate(tree);
    
    // Verify: Node was dispatched via AnimationDispatcher
    Assert.True(result.Success);
    Assert.True(animationDispatcherWasCalled);
}

[Fact]
public void AiPrimitive_LookAtPointNode_CrossContextReuse() {
    // Setup: Same node struct instantiated in BTree and HSM contexts
    var btreeResult = EvaluateInBTree(lookAtNode);
    var hsmResult = ExecuteInHsm(lookAtNode);
    
    // Verify: Both contexts successfully dispatched the node
    Assert.True(btreeResult.Success);
    Assert.True(hsmResult.Success);
}
```

**Example: ANC-P7-03 Fixture Test**
```csharp
[Fact]
public async Task AnimationIntegrationFixture_BootstrapsAndTicksWithoutError() {
    using var fixture = new AnimationIntegrationFixture();
    var humanoid = fixture.SpawnHumanoid();
    
    // Pump a few frames
    await fixture.PumpFrames(3);
    
    // Entity should exist and have animation components
    Assert.True(fixture.Repo.HasEntity(humanoid));
    Assert.True(fixture.Repo.Has<AnimationChannel>(humanoid));
}
```

---

## Task Breakdown

### Task 1: ANC-P5-07 — AiPrimitive Registration (3–4 hours)

**What to Implement:**
- Register all 11 Phase 5 action/getter nodes as `AiPrimitive` types
- Implement dispatcher entries in:
  - `BTreeEvaluator` (evaluates nodes as BTree actions)
  - `HsmActionExecutor` (executes nodes as HSM action bodies)
  - `BlueprintPrimitiveDispatcher` (existing; verify)
- Write reuse test: same node instance dispatches correctly in BTree, HSM, and Blueprint

**Success Conditions (from TASK-DETAIL):**
- Reuse test passes: primitive compiles/dispatches in BTree-action, HSM-action body, and Blueprint context
- All 11 nodes (9 actions + 2 getters) registered
- No compilation errors; no type-unsafety warnings

**Key Files to Touch:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Nodes/AiPrimitive.cs` (existing; register nodes)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Dispatchers/BTreeEvaluator.cs` (add animation dispatch)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/AiPrimitiveCrossContextTests.cs` (new; 4–6 tests)

---

### Task 2: ANC-P5-08 — PlayMontageChainNode Custom Drawer (2–3 hours or defer)

**What to Implement:**
- Custom drawer for `PlayMontageChainNode` in Blueprint editor
- Renders fixed 8-slot array inline with:
  - Montage picker dropdowns
  - Blend/rate/section per-entry (if time allows)
  - Add/remove/reorder buttons
- Or: If editor integration is not straightforward, mark as deferred in DEBT-TRACKER and create a ticket reference in the report

**Success Conditions (from TASK-DETAIL):**
- Drawer renders 8 slots + mutation buttons
- OR: Documented rationale for deferral + link to editor-team ticket

**Key Files:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Presentation/Drawers/PlayMontageChainNodeDrawer.cs` (new, or skip if deferred)

**Note:** This is **editor-owned**. If UI integration is not documented or straightforward, defer to DEBT-TRACKER with a note. Runtime Phase 5 is unblocked by this task.

---

### Task 3: ANC-P7-01 — PumpUntil + IPumpableHarness (2–3 hours)

**What to Implement:**
- Extract `PumpUntil`/`PumpFrames` from current test helpers into shared infra
- Define `IPumpableHarness` interface
- Implement frame-budgeted loop with `TimeoutException` on failure
- Add diagnostic dump to exception message

**Success Conditions (from TASK-DETAIL):**
- Unit test: condition met returns early (no timeout)
- Unit test: never-true condition throws `TimeoutException` after `maxFrames` with named condition + dump in message
- Methods: `PumpUntil(Func<bool> condition, uint maxFrames)`, `PumpFrames(uint count)`

**Key Files:**
- `Hrot/Tests/Hrot.Integration.Shared/PumpableHarness.cs` (new or existing; check structure)
- `Hrot/Tests/Hrot.Integration.Shared.Tests/PumpableHarnessTests.cs` (new; 3–4 tests)

---

### Task 4: ANC-P7-02 — Animation Diagnostics + Command Helpers (2–3 hours)

**What to Implement:**
- `DumpAnimationDiagnostics(entity, repo)` — dump animation component state (for test debugging)
- `WriteParams<T>(ref T, ComponentRepository)` — write action params to console (throws if `sizeof(T) > 32`)
- `IssuePlayMontage(entity, montageId, repo)` — dispatcher call helper
- Additional helpers as discovered (e.g., `ReadCurrentStance`, `ReadMontageQueueState`)

**Success Conditions (from TASK-DETAIL):**
- Helper unit tests pass
- `WriteParams` throws when `sizeof(T) > 32`
- Helpers used by P7-04+ scenario tests (can verify with dummy calls in this batch)

**Key Files:**
- `Hrot/Tests/Hrot.Animation.Integration/TestHelpers.cs` (new or extend)
- `Hrot/Tests/Hrot.Animation.Integration.Tests/AnimationTestHelpersTests.cs` (new; 3–5 tests)

---

### Task 5: ANC-P7-03 — Integration Fixture + Inline TKB Test Data (3–4 hours)

**What to Implement:**
- `AnimationIntegrationFixture : IPumpableHarness, IDisposable`
- Bootstrap: `SimHostNodeBootstrapper(networkFactory: null)` (no networking)
- Methods:
  - `SpawnHumanoid()` — create test character entity
  - `ResetWorld()` — destroy test entities, drain event bus
  - Properties: `Repo`, `EventBus`, `Pumper`
- Minimal inline test data: `TestData.MinimalCharacterDef()` (via `BakeForTest`)
- Integration smoke test: spawn + tick without error

**Success Conditions (from TASK-DETAIL):**
- Fixture bootstraps once (can be IClassFixture)
- `ResetWorld` destroys entities + drains bus
- Smoke test: spawn humanoid, pump 5 frames, verify no exceptions
- Fixture ready to host 8 integration scenarios (Phase 7 Part 2)

**Key Files:**
- `Hrot/Tests/Hrot.Animation.Integration/AnimationIntegrationFixture.cs` (new)
- `Hrot/Tests/Hrot.Animation.Integration.Tests/AnimationIntegrationFixtureTests.cs` (new; 2–3 tests)

---

## Developer Insights Section

**After implementing, answer these questions in your report:**

1. **Did you encounter any issues registering nodes in BTree/HSM contexts?** (For ANC-P5-07)
   - If so, what was the root cause?
   - Did it require changes to the dispatcher architecture?

2. **For ANC-P5-08 (custom drawer), did you defer or implement?** Why?
   - If deferred: describe the blocking issue + reference to editor-team ticket
   - If implemented: any surprises in the editor integration layer?

3. **What did you learn about the PumpUntil timeout mechanism?** (For ANC-P7-01)
   - How sensitive is the frame budget to GC pauses or slow operations?
   - Should it be configurable per-harness?

4. **For the integration fixture, what was the bootstrap complexity?** (For ANC-P7-03)
   - Did `SimHostNodeBootstrapper(networkFactory: null)` work as expected?
   - Any setup/teardown pitfalls?

5. **What weak points in the animation infrastructure did you spot?**
   - Registry/dispatch patterns difficult to follow?
   - Test setup boilerplate excessive?
   - Anything flagged for future refactoring?

---

## Report Format

**File:** `.dev/anim-ctrl/reports/BATCH-09-REPORT.md`

**Required Sections:**
1. **Executive Summary** — What was completed, test count, build status
2. **Implementation Details** — Per-task breakdown (reference design docs, don't restate)
3. **Test Coverage** — Table of test names + coverage areas (like BATCH-08)
4. **Developer Insights** — Answers to the questions above
5. **Build & Test Results** — Final test run output (test count, pass/fail, timing)
6. **Issues Found / Tech Debt** — Any P2/P3 issues spotted (for DEBT-TRACKER)

**Example structure:**
```markdown
# BATCH-09 REPORT

**Status:** ✅ COMPLETE  
**Tests Passing:** 189 (baseline 169 + new 20)  
**Build:** Clean  

## Executive Summary
Completed Phase 5 + Phase 7 infrastructure...

## Implementation Details
### ANC-P5-07: AiPrimitive Registration
...

## Test Coverage
| Task | Tests | Notes |
| --- | --- | --- |
| ANC-P5-07 | 4 | Cross-context reuse |
| ANC-P5-08 | 0/2 | Deferred; see DEBT-TRACKER |
| ANC-P7-01 | 3 | Timeout + early exit |
| ANC-P7-02 | 4 | Helpers + boundary conditions |
| ANC-P7-03 | 3 | Fixture bootstrap + reset |
| **Total** | **14** | **+5 from deferral** |

## Developer Insights
Q1: ...
Q2: ...

## Build & Test Results
All 189 tests passing in 8 seconds.

## Issues Found
None; all tasks in-scope completed.
```

---

## Checklist Before Handoff

- [ ] All 5 tasks implemented (or explicitly deferred with rationale)
- [ ] Test cases written first (before implementation)
- [ ] All new tests passing (+ baseline 169 tests still green)
- [ ] Build clean (0 errors)
- [ ] Developer Insights section answers all 5 questions
- [ ] Report references design docs (doesn't duplicate specs)
- [ ] No regressions detected
- [ ] Ready for review (no "work in progress" markers)

---

## Execution Start

You are a world-class C# backend engineer with deep knowledge of ECS architectures, animation pipelines, and test infrastructure.

**Begin now:** Implement BATCH-09 fully. Start with test cases (TDD). Reference TASK-DETAIL.md and DD-* design docs as needed (don't ask for clarification unless a design is genuinely contradictory). Write your completion report to `.dev/anim-ctrl/reports/BATCH-09-REPORT.md`. Do not stop until all tests pass, the build is clean, and you are confident in the quality of the implementation.

**You have autonomy:** Make design decisions beyond the spec if they improve correctness or clarity. Document any such decisions in your Developer Insights.

When finished, reply with: **"BATCH-09 COMPLETE"** and include a one-line summary (e.g., "5 tasks done, 189 tests passing, 0 issues").
