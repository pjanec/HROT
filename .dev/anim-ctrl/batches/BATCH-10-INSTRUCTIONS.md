# BATCH-10 INSTRUCTIONS — Phase 5 Final: AiPrimitive Registration + Cross-Subsystem Reuse

**Batch ID:** BATCH-10  
**Status:** Ready for Implementation  
**Developer:** @developer-subagent (Claude Sonnet 4.6)  
**Build Target:** `IOS-IG-SimHost.sln`  
**Test Target:** All animation control tests (baseline 180 + new tests from this batch)

---

## Overview

This batch **completes Phase 5** by implementing the final task: **ANC-P5-07 — AiPrimitive registration and cross-subsystem reuse**. This brings all Phase 0–5 tasks to 100% completion, unlocking Phase 7 (integration tests) on the stage-1 critical path.

**Scope:**
- **ANC-P5-07:** Register all 11 Phase 5 animation nodes as AiPrimitives for reuse in BTree, HSM, and Blueprint contexts

**Exit Criteria:**
- All 11 animation nodes registered and verified working in 3 subsystem contexts
- Cross-context reuse tests pass (same node instance dispatches correctly in BTree, HSM, Blueprint)
- No regressions: baseline 180 tests + new tests all passing
- Build clean (0 errors)
- Phase 5 marked 100% complete

---

## Onboarding

### Recent Context
- **BATCH-09 (Phase 5 Part 1+2 + Phase 7 Infra):** Completed ANC-P7-02 helpers + verified ANC-P7-01. Deferred ANC-P5-07 and ANC-P5-08. See [BATCH-09-REPORT.md](../reports/BATCH-09-REPORT.md) and [BATCH-09-REVIEW.md](../reviews/BATCH-09-REVIEW.md).
- **BATCH-09 Insights:** Blueprint infrastructure complexity noted. This batch provides focused design session needed.
- **Test Baseline:** 180 passing (169 original + 11 from BATCH-09)

### Design References
- **DD-5 §11, §1:** AiPrimitive hosting pattern, registration, cross-context dispatch
- **DD-1 §8–10:** Animation system dispatcher patterns and phase ordering
- **Codebase Examples:** Search for `[BlueprintRegistrar]` attribute + `AiBehaviorFactory.cs` for registration patterns

### Task Details
See [TASK-DETAIL.md](../TASK-DETAIL.md#anc-p5-07--aiprimitive-registration--cross-subsystem-reuse):
- All 11 Phase 5 nodes must be registered (9 action + 2 getter)
- Cross-context dispatch pattern (BTree → HSM → Blueprint)
- Success condition: same node instance works in all 3 contexts

---

## Test-Driven Task Progression

**Mandatory Workflow:**

1. **Define contract** — Understand AiPrimitive registration API by examining `AiBehaviorFactory.cs` and similar registrars in codebase
2. **Write test cases first** — Create cross-context tests BEFORE implementing registration
3. **Implement registration** — Register each node type with its dispatcher
4. **Verify all tests pass** — Baseline + new tests; no regressions
5. **Document findings** — Answer Developer Insights questions

### Test Quality Expectations

From prior batches:
- ✅ **Verify behavior, not existence.** Don't just check "does registration succeed?" — verify dispatch happens correctly in each context
- ✅ **Cross-context integration.** Same node instance tested in BTree, HSM, Blueprint (not mocked)
- ✅ **Use fake backend.** AnimationTestHelpers from BATCH-09 already provide helpers
- ❌ **Avoid smoke tests.** Don't just call Register() and check for no exceptions

### Test Structure Example

```csharp
[Fact]
public void PlayMontageNode_RegisteredInBTreeContext_DispatchesCorrectly()
{
    // Setup: Create BTree with PlayMontageNode action
    var btreeNode = CreateBTreeActionNode(PlayMontageNode);
    var btreeEvaluator = new BTreeEvaluator();
    
    // Action: Evaluate tree
    var result = btreeEvaluator.Evaluate(btreeNode);
    
    // Verify: Node dispatched to animation system
    Assert.True(result.Success);
    // Verify via AnimationTestHelpers: was IssuePlayMontage called?
    var channelStatus = ReadCurrentStance(...);  // Verify state changed
    Assert.NotEqual(NodeStatus.Idle, channelStatus);
}

[Fact]
public void PlayMontageNode_SameInstanceInHsmContext_DispatchesCorrectly()
{
    // Same node struct; different host context (HSM)
    var hsmExecutor = new HsmActionExecutor();
    var result = hsmExecutor.Execute(PlayMontageNode);
    
    // Verify dispatch happened
    Assert.True(result.Success);
}

[Fact]
public void AllPhase5Nodes_CrossContextReuse_CompatibleInAllThreeContexts()
{
    // Test all 11 nodes can dispatch in BTree, HSM, Blueprint
    var nodes = new[] { PlayMontageNode, StopMontageNode, ..., GetStanceNode };
    foreach (var node in nodes)
    {
        Assert.True(DispatchInBTree(node));
        Assert.True(DispatchInHsm(node));
        Assert.True(DispatchInBlueprint(node));
    }
}
```

---

## Task: ANC-P5-07 — AiPrimitive Registration (6–8 hours)

### What to Implement

Register all 11 Phase 5 animation nodes as `AiPrimitive` types, enabling their use in:
- **BTree context:** Actions dispatched via BTreeEvaluator
- **HSM context:** Action bodies executed via HsmActionExecutor
- **Blueprint context:** Imperative nodes (already supported via BlueprintPrimitiveDispatcher)

### Nodes to Register (11 total)

**Action Nodes (9):**
1. PlayMontageNode
2. StopMontageNode
3. EnqueueMontageNode
4. ClearMontageQueueNode
5. PlayMontageChainNode (note: custom drawer deferred to DEBT-TRACKER)
6. SetStanceNode
7. LookAtPointNode
8. LookAtEntityNode
9. ReleaseLookNode

**Getter Nodes (2):**
10. GetMontageQueueProgressNode
11. GetCurrentStanceNode

### Registration Pattern

Examine existing registrars in codebase for pattern. Expected structure:

```csharp
[BlueprintRegistrar]
public static class AnimationNodeRegistrar
{
    public static void Register(
        BlueprintRegistryStaging staging,
        BehaviorRegistry behaviorReg)
    {
        // Register PlayMontageNode
        var playDef = new AiPrimitiveDef { ... };
        staging.RegisterAiPrimitive(playDef.Id, playDef);
        
        // Register BTree action dispatcher
        behaviorReg.RegisterAction(
            ActionId: playDef.Id,
            thunk: (context) => DispatchPlayMontage(context));
        
        // Register HSM action dispatcher
        hsmRegistry.RegisterAction(
            ActionId: playDef.Id,
            executor: (state) => ExecutePlayMontage(state));
        
        // ... repeat for all 11 nodes
    }
}
```

**Key Files to Touch:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Registration/AnimationNodeRegistrar.cs` (new)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Dispatchers/BTreeAnimationDispatcher.cs` (add integrations)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Dispatchers/HsmAnimationDispatcher.cs` (add integrations)
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/AiPrimitiveCrossContextTests.cs` (tests)

### Success Conditions (from TASK-DETAIL)

✅ **Reuse test passes:** Same primitive compiles/dispatches in BTree-action, HSM-action body, and Blueprint context  
✅ **All 11 nodes registered** with correct IDs and dispatcher entries  
✅ **No compilation errors** or type-unsafety warnings  
✅ **Cross-context dispatch behavior verified** (behavior tests, not just registration)  
✅ **Baseline tests still passing** (180 baseline + new tests)

### Research Required

Before implementing, understand:
1. **AiPrimitive ID allocation:** What ID namespace? Reuse DD-5 IDs or allocate new?
2. **BTreeEvaluator integration:** How does it call animation dispatchers? Ref state pattern?
3. **HsmActionExecutor integration:** Action lifecycle (OnEnter, OnTick, OnExit)?
4. **BlueprintRegistry RCU:** What's the CommitStaging() protocol? Transaction semantics?

**Recommendation:** Spend first 1-2 hours reading existing registrars + dispatcher code. Ask specific questions in Developer Insights if patterns unclear.

---

## Developer Insights Section

**After implementing, answer these questions in your report:**

1. **What was the most complex aspect of cross-context dispatch?**
   - Ref state patterns? Async action semantics? ID collision handling?

2. **Did you encounter any type-unsafety issues?**
   - Did generic dispatch require unsafe casts?
   - How did you handle NodeStatus → Fbt.NodeStatus enum conversion?

3. **What integration points did you need to touch?**
   - Did you need to modify existing dispatchers, or only add new registration?
   - Any breaking changes to dispatcher interfaces?

4. **How did you test cross-context reuse?**
   - Did you use AnimationTestHelpers from BATCH-09?
   - What frame budget was needed for integration tests?

5. **What weak points in the AiPrimitive infrastructure did you discover?**
   - Documentation? API clarity? Patterns?
   - Anything flagged for future refactoring?

---

## Report Format

**File:** `.dev/anim-ctrl/reports/BATCH-10-REPORT.md`

**Required Sections:**
1. **Executive Summary** — All 11 nodes registered; test count; build status
2. **Implementation Details** — Per-node registration (reference DD-5, don't duplicate)
3. **Cross-Context Verification** — Test results for BTree, HSM, Blueprint dispatch
4. **Test Coverage** — Table of new tests (like prior batches)
5. **Developer Insights** — Answers to 5 questions above
6. **Build & Test Results** — Final test run output
7. **Issues Found / Tech Debt** — Any P2/P3 items for future

---

## Checklist Before Handoff

- [ ] All 11 nodes registered (PlayMontage, Stop, Enqueue, Clear, Chain, SetStance, LookAtPoint, LookAtEntity, ReleaseLook, GetQueueProgress, GetStance)
- [ ] Cross-context tests written first, then implementation
- [ ] Tests passing: baseline 180 + new cross-context tests (expect 10-15 new tests)
- [ ] Build clean (0 errors, 0 warnings)
- [ ] Developer Insights section answers all 5 questions
- [ ] Report references DD-5, doesn't duplicate specs
- [ ] No regressions detected
- [ ] Phase 5 marked 100% complete (all tasks done or formally deferred to DEBT-TRACKER)

---

## Execution Start

You are an expert C# backend engineer with deep knowledge of ECS, animation pipelines, and cross-subsystem dispatch patterns.

**Begin now:** Implement ANC-P5-07 fully using TDD. Reference DD-5 §11 and existing AiBehaviorFactory patterns in codebase. Research first if registration patterns unclear (don't guess). Write your completion report to `.dev/anim-ctrl/reports/BATCH-10-REPORT.md`.

**You have autonomy:** Make design decisions on ID allocation, dispatcher patterns if specs allow. Document any such decisions in Developer Insights.

When finished, reply with: **"BATCH-10 COMPLETE"** and include a one-line summary (e.g., "11 nodes registered, cross-context tests passing, Phase 5 complete").

---

## Post-BATCH-10 Work (NOT in scope for this batch)

**Phase 7 Integration Scenarios (BATCH-10B or later):**
- ANC-P7-04 through ANC-P7-11 (8 scenarios)
- Uses ANC-P7-02 helpers (BATCH-09 ✓)
- Uses PumpUntil infrastructure (BATCH-09 ✓)
- Ready to proceed once ANC-P5-07 complete

**Phase 5 Editor Task (DEBT-TRACKER):**
- ANC-P5-08: PlayMontageChainNode custom drawer (defer to editor team)

**Phase 6 (Lower Priority, after stage-1 green):**
- Replication layer (DDS integration)

Stage-1 (Phases 0-5 + 7) will be **100% complete** upon successful BATCH-10.
