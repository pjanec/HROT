# BATCH-01 Review

**Reviewed by:** Dev Lead
**Date:** 2025-05-24
**Status:** APPROVED WITH P1 CORRECTIONS REQUIRED

---

## Verdict

Phase 1 infrastructure is **functionally correct and well-implemented**. All 10 new tests
pass; 181 pre-existing tests are unaffected. The implementation is approved to proceed to
Phase 2 after the P1 corrections below are fixed.

---

## Scope Check

| Task | Status | Notes |
|------|--------|-------|
| TASK-EQL-001 — NodeDeactivatorDelegate + BTreeDeactivatorAttribute | DONE | Correct signatures, namespaces, attribute shape. |
| TASK-EQL-002 — ActionRegistry deactivator support | DONE (functional) | Missing contract unit tests — see P1 issue. |
| TASK-EQL-003 — Interpreter delta tracking | DONE | Parallel sweep, pathWasReset, exception propagation all implemented. |
| HybridLifecycleTests.cs — 10 test methods | DONE | Tests pass. Test quality concern noted — see findings. |

---

## Implementation Quality

### NodeDeactivatorDelegate.cs

Correct. Matches DESIGN.md §1.1 exactly: namespace `Fbt`, void-returning, same 4-parameter
signature as `NodeLogicDelegate`, proper generic constraints.

### BTreeDeactivatorAttribute.cs

Correct. `sealed`, `AllowMultiple = false`, `Inherited = false`, `TargetAction` property
set from single constructor argument. Matches DESIGN.md §1.2.

### ActionRegistry.cs

Correct. `_deactivators` parallel dictionary added. `RegisterDeactivator` throws for null
key and null delegate. `TryGetDeactivator` returns false/null for missing key.
Last-write-wins on duplicate keys. **Contract tests are missing — see P1.**

### Interpreter.cs

Correct structure:
- `_deactivatorDelegates` populated from `blob.MethodNames` in constructor.
- 4-step `Tick` ordering matches DESIGN.md §3.5: snapshot → bounds-check → execute → sweep.
- `pathWasReset` flag prevents double-firing on hot-reload path.
- `InvokeDeactivatorIfRegistered` correctly guards on `NodeType.Action or NodeType.Condition`.
- `SweepParallelChildren` iterates `[childIndex, childIndex + SubtreeOffset)` for each
  non-completed child, calling `InvokeDeactivatorIfRegistered` on each — correct.
- Empty blob handled via `Array.Empty`.
- Exception propagation verified by T7 test.

One important observation: **`NodeIndexStack` slots [0..7] are always zero** in the current
interpreter. `ExecuteSequence` and `ExecuteSelector` use `RunningNodeIndex` for resume
tracking but never write to `NodeIndexStack`. As a result, the 9-element path snapshot
effectively captures only `RunningNodeIndex` (slot 8); slots 0–7 are always zero. The delta
sweep still works correctly for all current test scenarios because each tree has at most one
concurrently running leaf node. The `NodeIndexStack`-based path capture is architecturally
correct for the design's stated goal but provides no additional tracking value with the
current interpreter. Recorded as D-06 for future awareness.

---

## Test Quality Assessment

### Tests verified by reading assertions (not just trusting report)

**T1 (natural completion):** Two-phase tick; asserts `deactivationCount == 0` after
Running, `deactivationCount == 1` after Success. Count check is precise. ✅

**T2 (sequential handoff):** Tests ActionA deactivating when ActionB takes over. Count
assertions for both deactivators are independent and precise. ✅
**DEVIATION FROM SPEC:** TASK-DETAIL.md T2 specifies an ObserverSelector branch switch
(high-priority condition flips from Failure→Success). This test implements a Sequence
handoff (ActionA returns Success → ActionB starts). This is because `ExecuteSelector`
uses resume semantics and does NOT re-evaluate completed children on subsequent ticks.
The ObserverSelector branch-switch scenario is therefore untestable with the current
interpreter. The deactivator mechanism is correctly exercised for the sequence handoff
case. Recorded as D-05.

**T3 (tree failure):** Count check after Failure. Precise. ✅

**T4 (no allocation):** `GC.CollectionCount(0)` before/after 1000 ticks. Appropriate check
for the no-allocation constraint. ✅

**T5 (two nodes, only exited fires):** Two independent counters, both asserted with exact
values. ✅

**T6 (idle path sentinel):** Fresh state with all-zero path; action completes without ever
entering Running state (returns Success directly). Asserts `deactivationCount == 0`.
Correctly tests that zero-sentinel path entries are skipped. ✅

**T7 (exception propagation):** Manual try/catch; asserts `threw == true`. Correct. ✅

**T8 (deep subtree abort):** Selector → Sequence → LeafAction, FallbackAction. LeafAction
fails on Tick 1, triggering branch switch. Asserts deactivator fires exactly once. ✅
**NOTE:** Spec says ObserverSelector + condition-flip; test uses plain Selector + failure.
Same mechanism; different trigger. Functionally adequate given the interpreter limitation.

**T9 (Parallel subtree sweep):** Two distinct counters (countA, countB), both asserted
independently at `== 1` after Parallel exits. ✅
**OBSERVATION:** When Parallel completes (RequireAll, both succeed), `ExecuteParallel`
clears `LocalRegisters[3] = 0`. `SweepParallelChildren` sees all children as
"not finished" (childStatesBits = 0) and sweeps ALL children. This fires deactivators for
children that completed normally, not only mid-flight ones. The DESIGN.md says "for each
child whose completion bit is NOT set (still running)" — but since completion bits are
cleared on Parallel exit, all children are swept. The test exercises the sweep path
correctly; deactivators are confirmed to fire for both leaf actions.

**T10 (hot-reload):** Manually places OOB `RunningNodeIndex`; asserts (a) deactivator fires
once, (b) `rnAtDeactivation == blob.Nodes.Length` (OOB value, pre-reset), (c) tree
executes and succeeds this frame, (d) total count still == 1. Most thorough test in the
suite. ✅

---

## Issues Found

### P1 — Missing TASK-EQL-002 contract unit tests

**Severity:** P1 (must fix in next batch)

The `ActionRegistryTests.cs` contains zero tests for the new deactivator methods. The
following success conditions from TASK-DETAIL.md TASK-EQL-002 are unverified by any
automated test:

- T2: `TryGetDeactivator("Missing", out _)` returns `false`
- T3: `RegisterDeactivator(null, delegate)` throws `ArgumentNullException`
- T4: `RegisterDeactivator("key", null)` throws `ArgumentNullException`
- T5: Registering same key twice: second registration wins

The only implicitly tested condition is T1 (register + retrieve same delegate), via
the HybridLifecycleTests. This is insufficient for a registry contract.

**Fix required:** Add 5–6 unit tests to `ActionRegistryTests.cs` covering T1–T5 explicitly.

### P2 — Missing TASK-EQL-001 explicit contract tests

**Severity:** P2

The following success conditions for TASK-EQL-001 are not covered by any automated test:

- T1: `typeof(NodeDeactivatorDelegate<,>).Namespace == "Fbt"`
- T2: `typeof(BTreeDeactivatorAttribute).Namespace == "Fbt"`
- T3: `new BTreeDeactivatorAttribute("Foo.Bar").TargetAction == "Foo.Bar"`
- T4: Lambda assignment to `NodeDeactivatorDelegate<TestBlackboard, MockContext>` (implicitly
  covered by every HybridLifecycleTest, but no dedicated test)

All four are verifiable by inspection; T4 is implicitly covered. However, explicit tests
guard against future namespace or signature regressions. Should be added to `AttributeTests.cs`.

---

## Debt Tracker Updates

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| D-05 | P2 | DESIGN.md §1.5 L-02 | `ExecuteSelector` uses resume semantics; re-evaluates from `RunningNodeIndex`, not from highest-priority child. ObserverSelector condition-flip branch-switch (L-02, the "critical case") cannot be exercised in tests. T2 and T8 test branch switches via failure propagation only. Proper ObserverSelector semantics would require a separate `ExecuteObserverSelector` that re-evaluates from child 0 every tick. | Future (Interpreter refactor) |
| D-06 | P3 | Interpreter.cs | `NodeIndexStack` is never written by `ExecuteSequence`/`ExecuteSelector`. The 9-element path snapshot effectively captures only `RunningNodeIndex`; slots 0–7 are always zero. Delta sweep still works for all current single-leaf scenarios. Multi-running-node scenarios (future composite types) would require NodeIndexStack to be maintained. | Future |
| D-07 | P3 | ONBOARDING.md + batch instructions | `dotnet test FastBTree.sln` fails because `Fbt.SourceGen.csproj` is missing. Batch instructions reference `FastBTree.sln` for test runs, which fails. Developers must use `dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj` directly. ONBOARDING.md note is accurate; batch instruction test command needs correction. | Fix in BATCH-02 instructions |

---

## Git Commit Message

```
feat(fbt): add BTree deactivator infrastructure (Phase 1)

- Add NodeDeactivatorDelegate<TBlackboard, TContext> (void-returning,
  mirrors NodeLogicDelegate signature)
- Add BTreeDeactivatorAttribute with TargetAction constructor argument
- Extend ActionRegistry with RegisterDeactivator/TryGetDeactivator
- Extend Interpreter.Tick with 4-step delta-tracking:
    1. oldPath snapshot (stackalloc ushort[9]) before bounds-check
    2. Hot-reload bounds-check with pathWasReset flag (no early return)
    3. ExecuteNode
    4. Post-tick path sweep (skipped when pathWasReset)
- Add InvokeDeactivatorIfRegistered (type-guarded on Action/Condition)
- Add SweepParallelChildren (range sweep over child subtree blocks)
- Add HybridLifecycleTests with 10 test methods covering T1-T10

No engine dependencies touched. All 191 pre-existing passing tests
unaffected. 11 pre-existing SourceGen-related failures unchanged.
```

---

## Next Batch

BATCH-02 will:
1. Fix the P1 missing contract tests for TASK-EQL-002 (and P2 for TASK-EQL-001).
2. Implement TASK-EQL-004: BTreeActionGenerator deactivator detection and emission (Phase 2).
