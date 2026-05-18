# BATCH-16: Global Transition Checking (P0 Critical)

**Batch Number:** BATCH-16  
**Tasks:** TASK-G01 (Global Transition Checking)  
**Phase:** Gap Implementation - P0 Critical  
**Estimated Effort:** 6-8 hours (1 day)  
**Priority:** HIGH (Critical Path)  
**Dependencies:** BATCH-01 through BATCH-15 (Core Runtime)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is a **critical gap implementation batch**. You are implementing missing functionality from the design specification that was identified during gap analysis. This feature is critical for the HSM to behave as designed by the architect.

**Background:** Global transitions (e.g., Death, Stun interrupts) are checked in a separate table and must **preempt** all local transitions. Currently, the kernel only checks per-state transitions, meaning global interrupts don't work as designed.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Gap Analysis:** `.dev-workstream/GAP-ANALYSIS.md` - Section 3.2 (Global Transition Checking)
3. **Task Definition:** `.dev-workstream/GAP-TASKS.md` - TASK-G01 (full specification)
4. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Section 3.4 (Transition Resolution, Step A)
5. **Architect Decision:** `docs/design/HSM-Implementation-Design.md` - Section 6.3 (Q7: Separate Global Transition Table)

### Source Code Location

- **Primary Work Area:** `src/Fhsm.Kernel/HsmKernelCore.cs`
- **Test Project:** `tests/Fhsm.Tests/Kernel/TransitionExecutionTests.cs` (or new file)
- **Supporting Files:** 
  - `src/Fhsm.Kernel/Data/GlobalTransitionDef.cs` (already exists ✅)
  - `src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs` (GlobalTransitions accessor exists ✅)

### Report Submission

**❗ MANDATORY REPORT REQUIRED**

**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-16-REPORT.md`

**Use the template:**  
`.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

**Report must include:**
- ✅ All tasks completed with evidence
- ✅ Full test output (not just "tests pass")
- ✅ Developer insights documented (see Report Requirements section)
- ✅ Any deviations documented with rationale
- ✅ Pre-submission checklist completed

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-16-QUESTIONS.md`

---

## Context

### Why This Matters

Global transitions are a **critical architectural feature** for handling system-wide interrupts. Examples:
- **Death:** Entity dies → transition to Dead state from ANY state
- **Stun:** Entity stunned → transition to Stunned state, interrupting current action
- **Panic:** Global alarm → all agents transition to Alert state

**Current Problem:**
- Global transitions are defined in a separate table (`GlobalTransitionDef[]`)
- The table is stored in `HsmDefinitionBlob.GlobalTransitions` ✅
- **BUT:** The kernel never checks it! Only per-state transitions are checked.

**Impact:** Global interrupts don't work. A "Death" event won't transition the entity to Dead if it's in a state that doesn't have a local transition for it.

### Design Specification

**From Section 3.4 (Transition Resolution), Step A:**

> **Step A: Check global interrupts FIRST**  
> Global transitions are checked before per-state transitions. If a global transition's guard passes, it preempts all local transitions with priority 255 (highest).

**Architect Decision Q7:**

> **Decision:** Separate Global Transition Table  
> **Reasoning:** Global interrupts (Death, Stun) are checked every tick. An O(G) scan of a tiny separate table is much faster than filtering the main transition list.

### Related Tasks

- **[TASK-G01](../GAP-TASKS.md#task-g01-global-transition-checking)** - Primary task for this batch

---

## 🎯 Batch Objectives

By the end of this batch:

1. ✅ Global transitions checked **before** local transitions in `SelectTransition`
2. ✅ Global transitions with passing guards return immediately with priority 255
3. ✅ Local transitions only checked if no global transition found
4. ✅ Tests validate global transitions preempt local transitions
5. ✅ Integration with existing kernel phases (no breaking changes)

**Success Metric:** A "Death" event (defined as global transition) will transition entity to Dead state even if current state has no local "Death" transition.

---

## ✅ Tasks

### Task 1: Implement Global Transition Checking (TASK-G01)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)  
**Task Definition:** See [TASK-G01 in GAP-TASKS.md](../GAP-TASKS.md#task-g01-global-transition-checking)

#### Current State Analysis

**Current Implementation:**
```csharp
private static unsafe TransitionDef? SelectTransition(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    ushort* activeLeafIds,
    int regionCount,
    ushort eventId,
    void* contextPtr)
{
    // Currently ONLY checks per-state transitions
    // NO global transition checking!
    
    // Loops through regions, bubbles up from leaf to root
    // Returns first enabled transition found
}
```

**What's Missing:** No "Step A" (global transition checking)

#### Design Code Reference

**From `docs/design/HSM-Implementation-Design.md` Section 3.4:**

```csharp
// Step A: Check global interrupts
var globalTransitions = definition.GlobalTransitions; // ReadOnlySpan<GlobalTransitionDef>
foreach (ref readonly var trans in globalTransitions)
{
    if (trans.TriggerEventId == evt.EventId)
    {
        if (EvaluateGuard(definition, trans.GuardId, instancePtr, contextPtr))
        {
            candidate = new TransitionCandidate
            {
                TransitionIndex = trans.Index, // ⚠️ GlobalTransitionDef doesn't have Index field!
                Priority = 255, // Highest
            };
            return true; // Global interrupt takes priority
        }
    }
}
```

**⚠️ Important Discovery:** `GlobalTransitionDef` doesn't have an `Index` field. You'll need to track the index in the loop.

#### Implementation Requirements

**1. Add Global Transition Checking Loop**

Location: `HsmKernelCore.SelectTransition`, **before** the existing per-state transition loop

```csharp
// NEW CODE - Add this FIRST (Step A: Global Transitions)
var globalTransitions = definition.GlobalTransitions;
for (int g = 0; g < globalTransitions.Length; g++)
{
    ref readonly var globalTrans = ref globalTransitions[g];
    
    if (globalTrans.EventId == eventId) // Match event
    {
        // Evaluate guard (0 = no guard = always true)
        if (globalTrans.GuardId == 0 || 
            EvaluateGuard(globalTrans.GuardId, instancePtr, contextPtr, eventId))
        {
            // Found enabled global transition - create special TransitionDef
            return CreateGlobalTransition(ref globalTrans);
        }
    }
}

// EXISTING CODE - Per-state transitions (Step B)
// ... (keep existing logic)
```

**2. Return Type Consideration**

Current `SelectTransition` returns `TransitionDef?`. You have two options:

**Option A (Recommended):** Convert `GlobalTransitionDef` → `TransitionDef` on the fly

```csharp
private static TransitionDef CreateGlobalTransition(ref GlobalTransitionDef globalTrans)
{
    return new TransitionDef
    {
        SourceStateIndex = 0xFFFF, // Special marker for global
        TargetStateIndex = globalTrans.TargetStateIndex,
        EventId = globalTrans.EventId,
        GuardId = globalTrans.GuardId,
        ActionId = globalTrans.ActionId,
        Flags = globalTrans.Flags | TransitionFlags.IsInterrupt, // Mark as interrupt
        Cost = 0, // Global transitions don't compute LCA cost
        SyncGroupId = 0,
    };
}
```

**Option B (Alternative):** Change return type to `(bool isGlobal, TransitionDef? transition)` tuple

Only use Option B if Option A causes issues. Prefer minimal API changes.

**3. Integration with ProcessRTCPhase**

Verify that `ProcessRTCPhase` → `ExecuteTransition` handles global transitions correctly:
- Global transitions have `SourceStateIndex = 0xFFFF`
- `ExecuteTransition` must handle this (it computes LCA from source/target)

**If `ExecuteTransition` breaks with `0xFFFF` source:**
- Add special case: `if (transition.SourceStateIndex == 0xFFFF) { /* Global transition logic */ }`
- For global transitions: Don't exit any states, just execute effect + enter target path

**4. Guard Evaluation**

Ensure `EvaluateGuard` signature matches:
```csharp
private static bool EvaluateGuard(ushort guardId, byte* instancePtr, void* contextPtr, ushort eventId)
```

If signature differs, adjust your call accordingly.

#### Edge Cases to Handle

1. **Multiple Global Transitions for Same Event:**
   - Check all global transitions for the event
   - Return the **first** one with passing guard (deterministic)

2. **Global Transition with Guard Fails:**
   - Continue checking other global transitions
   - Fall through to local transitions if none pass

3. **No Global Transitions Defined:**
   - `definition.GlobalTransitions.Length == 0`
   - Skip loop entirely (performance)

4. **Global Transition with SourceStateIndex = 0xFFFF:**
   - Make sure LCA computation doesn't crash
   - Special case in `ExecuteTransition` if needed

#### Performance Considerations

**Design Justification (from Architect Q7):**
> An O(G) scan of a tiny separate table is much faster than filtering the main transition list.

**Expected Performance:**
- Global transitions: typically 1-5 entries (Death, Stun, Panic)
- Linear scan is < 100 CPU cycles
- Much faster than scanning all transitions and checking a flag

**Your responsibility:** Don't optimize prematurely. Implement correctly first.

#### Tests Required

**Minimum: 5 tests covering:**

1. ✅ **Global transition preempts local transition**
   - State A has local transition for event "Attack" → State B
   - Global transition for event "Attack" → State Dead
   - Send "Attack" event
   - **Assert:** Entity transitioned to Dead (global), not B (local)

2. ✅ **Global transition with guard check**
   - Global transition for "Damage" → Dead, guard: "IsDead()"
   - Guard returns false
   - **Assert:** Global transition NOT taken, local transition evaluated

3. ✅ **Global transition with passing guard**
   - Global transition for "Damage" → Dead, guard: "IsDead()"
   - Guard returns true
   - **Assert:** Entity transitions to Dead

4. ✅ **Multiple global transitions - first enabled wins**
   - Global transition 1: "Alert" → State X, guard: false
   - Global transition 2: "Alert" → State Y, guard: true
   - Send "Alert" event
   - **Assert:** Entity transitions to State Y (second one)

5. ✅ **No global transition - falls through to local**
   - No global transition for event "Jump"
   - State A has local transition for "Jump" → State B
   - Send "Jump" event
   - **Assert:** Entity transitions to State B (local transition works)

**Optional (Nice to Have):**
- Global transition from deep nested state
- Global transition with effect action
- Global transition to composite state (drills down to initial leaf)

---

## 🧪 Testing Requirements

### Minimum Standards

- **Minimum Test Count:** 5 tests (as specified above)
- **Test Location:** `tests/Fhsm.Tests/Kernel/TransitionExecutionTests.cs` (add to existing) OR create new file `tests/Fhsm.Tests/Kernel/GlobalTransitionTests.cs`
- **Test Quality:** Must verify **behavior**, not just "does it compile"

### Test Quality Expectations

**❗ NOT ACCEPTABLE:**
- Tests that only check "can I call SelectTransition"
- Tests that don't verify the global-vs-local priority
- Tests without assertions on final state

**✅ REQUIRED:**
- Tests that verify global transitions preempt local transitions
- Tests that verify guard evaluation for global transitions
- Tests that verify fallback to local transitions when no global matches
- Tests that verify deterministic behavior (first-enabled-wins)

### Test Setup Helpers

You may need to:
1. Create a test `HsmDefinitionBlob` with global transitions
2. Use `HsmBuilder` to define a machine with global transitions (check if API supports it)
3. Or manually construct a blob with global transitions for testing

**Hint:** Check if `HsmFlattener` already creates global transitions. If so, use the builder API. If not, you may need to manually construct for tests.

### Test Execution

After implementing:
```bash
cd tests/Fhsm.Tests
dotnet test --filter "FullyQualifiedName~GlobalTransition"
```

**Expected Result:** All tests pass, 0 failures

---

## 📊 Report Requirements

### Mandatory Report Sections

Your report **MUST** include:

1. **Implementation Summary**
   - How did you implement global transition checking?
   - Did you use Option A (convert to TransitionDef) or Option B (tuple)?
   - Any deviations from instructions?

2. **Integration with ExecuteTransition**
   - Did `ExecuteTransition` work with `SourceStateIndex = 0xFFFF`?
   - Did you need to add special case handling?
   - If yes, what changes did you make?

3. **Test Results (FULL OUTPUT)**
   ```
   Test run for Fhsm.Tests.dll (.NETCoreApp,Version=v8.0)
   [Paste COMPLETE test output, not just "5 passed"]
   ```

4. **Design Decisions Made**
   - What choices did you make beyond the instructions?
   - What alternatives did you consider? Why did you choose your approach?

5. **Code Quality & Improvements**
   - Did you spot any weak points in the existing code?
   - What would you improve or refactor if you could?

6. **Edge Cases & Challenges**
   - What edge cases did you discover?
   - What integration challenges did you face?

### Developer Insights Required

**❗ Share your professional insights in your report:**

**Q1: Issues Encountered**  
What problems or obstacles did you encounter during implementation? How did you resolve them?

**Q2: Design Decisions Made**  
What design decisions did you make beyond the instructions? (e.g., how to handle the missing `Index` field, whether to modify `ExecuteTransition`, etc.) What alternatives did you consider?

**Q3: Code Quality Observations**  
Did you spot any weak points in the existing `SelectTransition` or `ExecuteTransition` implementations? What would you improve if you could refactor?

**Q4: Edge Cases Discovered**  
What edge cases or scenarios did you discover that weren't mentioned in the instructions? How did you handle them?

**Q5: Performance Considerations**  
Did you notice any performance concerns with the global transition checking? Any optimization opportunities?

**Q6: Integration Challenges**  
How did the global transition feature integrate with the existing kernel? Were there any architectural friction points?

---

## 🎯 Success Criteria

This batch is DONE when:

- ✅ `SelectTransition` checks global transitions **before** local transitions
- ✅ Global transitions with passing guards return immediately with priority 255
- ✅ Local transitions only checked if no global transition found
- ✅ All existing tests still pass (no regressions)
- ✅ 5+ new tests covering global transitions (all passing)
- ✅ Code follows existing kernel patterns (no architectural violations)
- ✅ Report submitted with developer insights documented
- ✅ No compiler warnings introduced

---

## ⚠️ Common Pitfalls to Avoid

### 1. Breaking Existing Functionality
**Problem:** Modifying `SelectTransition` might break existing transition logic  
**Solution:** Run ALL existing tests after implementation. Zero regressions allowed.

### 2. Incorrect Priority Handling
**Problem:** Global transitions should have priority 255, but `TransitionDef` uses `Flags` field (upper 4 bits) for priority  
**Solution:** When creating `TransitionDef` from global transition, set priority correctly:
```csharp
Flags = globalTrans.Flags | (TransitionFlags)((15 << 12)) // Priority 15 in upper 4 bits
```

### 3. Guard Signature Mismatch
**Problem:** `EvaluateGuard` might have different signature  
**Solution:** Check actual signature in `HsmKernelCore.cs` and match it

### 4. Not Handling Composite Target States
**Problem:** Global transition to composite state might not drill down to initial leaf  
**Solution:** `ExecuteTransition` should already handle this (via `ExecuteEntryPath`). Verify in tests.

### 5. Forgetting to Test Fallback
**Problem:** Only testing global transitions, not verifying local transitions still work  
**Solution:** Test #5 (no global transition - falls through to local) is MANDATORY

---

## 📚 Reference Materials

### Primary References
- **Task Definition:** [GAP-TASKS.md - TASK-G01](../GAP-TASKS.md#task-g01-global-transition-checking)
- **Gap Analysis:** [GAP-ANALYSIS.md - Section 3.2](../GAP-ANALYSIS.md)
- **Design Document:** `docs/design/HSM-Implementation-Design.md` - Section 3.4 (Transition Resolution)
- **Architect Decision:** `docs/design/HSM-Implementation-Design.md` - Section 6.3, Q7 (Separate Global Transition Table)

### Code References
- **Current Implementation:** `src/Fhsm.Kernel/HsmKernelCore.cs` - `SelectTransition` method (line ~300-400)
- **Global Transition Struct:** `src/Fhsm.Kernel/Data/GlobalTransitionDef.cs`
- **Blob Accessor:** `src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs` - `GlobalTransitions` property
- **Existing Tests:** `tests/Fhsm.Tests/Kernel/TransitionExecutionTests.cs`

### Supporting Documents
- **Dev Workflow:** [README.md](../README.md)
- **Dev Lead Guide:** [DEV-LEAD-GUIDE.md](../DEV-LEAD-GUIDE.md)
- **Task Tracker:** [TASK-TRACKER.md](../TASK-TRACKER.md)

---

## ⚠️ Quality Standards

### Code Quality
- ✅ Follow existing kernel patterns (void* core, unsafe pointers)
- ✅ Add XML doc comments for any new helper methods
- ✅ No compiler warnings
- ✅ Performance-conscious (but correctness first)

### Test Quality
- ✅ Tests must verify **behavior** (what happens), not **structure** (how it's coded)
- ✅ Tests must be **deterministic** (no flaky tests)
- ✅ Tests must be **readable** (clear Arrange/Act/Assert)
- ✅ Tests must **fail if implementation breaks**

### Report Quality
- ✅ All sections completed (no "TODO" or "N/A" without explanation)
- ✅ Developer insights shared thoroughly (2-3 sentences minimum per area)
- ✅ Full test output included (copy-paste from console)
- ✅ Deviations documented with clear rationale
- ✅ Pre-submission checklist checked

---

## 🚀 Getting Started

### Recommended Implementation Order

1. **Read all references** (2 hours)
   - Gap analysis section 3.2
   - Design document section 3.4
   - Existing `SelectTransition` implementation
   - Existing `ExecuteTransition` implementation

2. **Write failing tests first** (1 hour)
   - Test #1: Global preempts local (should fail)
   - Test #5: Fallback to local (should pass - regression check)

3. **Implement global transition loop** (2 hours)
   - Add global transition checking before existing logic
   - Implement `CreateGlobalTransition` helper
   - Run tests, verify Test #1 now passes

4. **Handle edge cases** (1 hour)
   - Multiple global transitions
   - Guard evaluation
   - No global transitions (empty array)

5. **Verify ExecuteTransition integration** (1 hour)
   - Check if `SourceStateIndex = 0xFFFF` works
   - Add special case if needed
   - Run all tests (existing + new)

6. **Write remaining tests** (1 hour)
   - Tests #2, #3, #4
   - Any additional edge cases discovered

7. **Write report** (1 hour)
   - Document developer insights
   - Document deviations (if any)
   - Include full test output
   - Complete pre-submission checklist

**Total Estimated Time:** 6-8 hours

---

## 📝 Pre-Submission Checklist

Before submitting your report, verify:

- [ ] All 5 required tests implemented and passing
- [ ] All existing tests still passing (zero regressions)
- [ ] No compiler warnings introduced
- [ ] Code follows existing kernel patterns
- [ ] Report includes full test output (not just "5 passed")
- [ ] All 6 insight areas documented thoroughly
- [ ] Any deviations documented with rationale
- [ ] Pre-submission checklist completed (this one!)

---

## 🎯 Final Notes

**This is a critical feature.** Global transitions are architectural, not optional. Take your time to implement correctly.

**Don't guess.** If something is unclear, ask questions in `.dev-workstream/questions/BATCH-16-QUESTIONS.md`.

**Test thoroughly.** This feature affects the core transition resolution logic. Regressions would be catastrophic.

**Document well.** The next developer needs to understand global vs local transition priority.

---

**Good luck! This is an important milestone toward 100% design compliance.** 🚀

**When complete, submit report to:** `.dev-workstream/reports/BATCH-16-REPORT.md`
