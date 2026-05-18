# BATCH-04 Review

**Batch:** BATCH-04  
**Reviewer:** Development Lead  
**Date:** 2026-02-24  
**Status:** ✅ APPROVED (two P2 issues noted; one P1 structural risk tracked for BATCH-05)

---

## Test Quality Assessment

**Applying CODE-STANDARDS.md §0 checklist.**

### BTreeTickSystem tests

**Test 1 — `BTreeTick_DoesNotThrow_WhenBlobNotRegistered`:** Correctly asserts `RunningNodeIndex` unchanged. ✅

**Test 2 — `BTreeTick_DoesNotTick_WhenBrainTierIsNotBTree`:** Uses a real `tickCount` closure variable inside the action delegate — the assertion is on the actual invocation counter, not an approximation. This is the right pattern (not just checking for no exception). ✅

**Test 3 — `BTreeTick_WritesActionToChannel_ForRegisteredTree`:** Asserts both `channel.ActiveAction == 1` and `channel.ActionInstanceId == 1` as specific field values. ✅ This is the core contract test.

**One weak point in Test 3:** The action node inside the BTree directly calls `ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self)` and writes hardcoded `1` for both fields. This means the test is also implicitly testing that `BTreeContext.World` is populated correctly (which is important), but the test name doesn't reflect that. Minor — not a failing issue.

### HsmTickSystem tests

**Test 1 — `HsmTick_TransitionsState_OnRegisteredEvent`:** Asserts `ActiveLeafIds[0] == 1` after running — a concrete state-ID check. ✅

**Concern (P1):** Line 82 writes `brain.State.Reserved1 = 10` to inject the event, with the comment `// HsmKernelCore.CurrentEventId_Offset_128 == 58, which is HsmInstance128.Reserved1`. This is a **fragile magic-offset dependency** — the test is reaching into FastHSM internals by knowing that `Reserved1` maps to the current event at offset 58. If FastHSM's internal layout changes in a minor version bump, this test will silently become wrong (the event won't fire, the state won't transition, the assertion `ActiveLeafIds[0] == 1` will fail with a confusing error). 

**Fix in BATCH-05:** Check whether `HsmKernel` or `HsmInstance128` expose a proper `PushEvent(eventId)` or equivalent API. If so, use it. If not, document the offset dependency explicitly with a `const int` named `HsmInstance128CurrentEventOffset = 58` so at least it's not a magic number, and add a static assert or comment tying it to a specific FastHSM version.

**Test 2 — `HsmTick64_And_HsmTick128_AreIndependent`:** Correctly verifies that each system only processes its own component type without cross-contaminating the other entity. ✅ The empty-registry shortcut (both entities get skipped by name lookup) is a reasonable simplification — the independence property being tested is ECS query isolation, not tick logic.

### BehaviorIngress tests

**Test 1 — `BehaviorIngress_ParsesFleeBlackboard_FromJson`:** Reads back via `*(FleeBlackboard*)bbPtr->Memory` and asserts `SafeDistance == 50.0f`. Specific value check. ✅

**Test 2 — `BehaviorIngress_IncrementsInstanceId_MonotonicallyAcrossMultipleAssignments`:** Two assignments, captures `instanceId1` and `instanceId2`, asserts both `> 0` and `instanceId2 > instanceId1`. Exactly what was prescribed. ✅

**Test 3 — `BehaviorIngress_ResetsBTreeState_OnNewBehavior`:** Asserts `RunningNodeIndex == 0` after assignment of entity with `RunningNodeIndex = 5`. ✅

**Test 4 — `BehaviorIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction`:** Full two-system chain. Asserts `newInstanceId > 1` after ingress, then `channel.ActiveAction == 0` after arbitration. ✅ This is the strongest test in the batch.

**One gap in Test 4:** The test asserts `channel.ActiveAction == 0` but does not assert `channel.BehaviorInstanceId == 0` (the full `channel = default` reset). This is the same gap that was fixed in `Arbitration_ClearsStaleChannel` — the fix was applied to the unit test but not replicated here. Carry to BATCH-05 (minor, P2).

### Corrective test fixes (from BATCH-03)

All three fixes verified in the report checklist. ✅ The `WritingSpyExecutor<TChannel>` addition to `TestHelpers.cs` is clean.

---

## Code Quality Assessment

**`BTreeTickSystem.cs`:** Uses `GetComponentRW` for `btState` and `blackboard`. Zero allocation context construction. `[UpdateAfter(ChannelArbitrationSystem)]` present. Debug-only diagnostic via `#if DEBUG`. Clean. ✅

**`BehaviorIngressSystem.cs`:** `GetComponentRW` throughout. `unchecked { behavior.InstanceId++; }` with doc comment explaining intentional wrap. `HasComponent` guards before accessing optional components (`BrainBTreeState`, `BrainBlackboard`). Unsafe pointer pattern matches Q3 explanation precisely. Clean. ✅

**`BehaviorRegistry.cs`:** `BehaviorDefinition` uses `required` + `init` for immutability after construction. Clear null-documentation of optional fields. `Thread-safe for reads` noted in XML doc. Clean. ✅

**Magic numbers:** `BrainTierBTree` and `BrainTierHsm` constants appear throughout. `BehaviorConstants` fully used. No raw literals spotted. ✅

**One structural concern (P2):** `BehaviorRegistry` keys on `name.GetHashCode()`. .NET `string.GetHashCode()` is randomised per process (DoS protection). In the same process this is self-consistent (ingress also calls `GetHashCode()`), but serialised behavior hashes (e.g., persisted scenarios or network packets) would be non-reproducible across runs. This is not a current bug but it's a landmine for Phase 5/6 when network synchronisation enters. Track for the Phase 5 batch — a stable `BehaviorId` (e.g., CRC32 of the name, or a manually assigned `int`) would be safer.

---

## Q4 Observation — cross-group ordering

The Q4 note that `InputSystemGroup` runs before `SimulationSystemGroup` only by registration convention is a genuine gap. Track as a BATCH-05 item: document the required registration order in `StandardSystemGroups.cs` with a comment, and consider whether the kernel supports `[UpdateBefore(typeof(SimulationSystemGroup))]` at group level (if so, add it).

---

## Issues Found

| # | Severity | Description | Batch |
|---|---|---|---|
| 1 | P1 | `HsmTick_TransitionsState_OnRegisteredEvent` injects event via magic field `Reserved1` — fragile internal-layout dependency; needs named constant or proper API usage | BATCH-05 |
| 2 | P2 | `BehaviorIngress_StaleSets...` Test 4 missing `Assert.Equal(0u, channel.BehaviorInstanceId)` | BATCH-05 |
| 3 | P2 | `BehaviorRegistry.GetHashCode()` key — cross-process non-reproducibility; note for Phase 5 | Phase 5 batch |
| 4 | P2 | `InputSystemGroup` cross-group ordering enforced only by convention — document or enforce | BATCH-05 |

---

## Verdict

**Status: APPROVED. Phase 1 complete.**

All Phase 1 tasks delivered. Full solution green. Test quality is substantially better than previous batches — the prescribed assertion-level specs resulted in tests that actually catch regressions. Issue 1 (HSM event injection via internal offset) is the most important to fix before HSM usage grows in Phase 2/3.

---

## 📝 Commit Message

```
feat: BTreeTickSystem + HsmTickSystem + BehaviorRegistry/Ingress (BATCH-04)

Completes BCS-P1-T5, BCS-P1-T6, BCS-P1-T7 — Phase 1 done

FDP.Toolkit.Behavior:
- BTreeContext: IAIContext impl; stack-allocated per entity; zero heap alloc
- BTreeTickSystem: steps FastBTree interpreter; [UpdateAfter(ChannelArbitration)]
- FdpHsmContext: minimal unmanaged struct (Entity Self) for HsmKernel constraint
- HsmTickSystem<T>: generic; registered twice for BrainHsm64 + BrainHsm128
- BehaviorDefinition + BehaviorRegistry: startup-time name-hash → definition map
- AssignBehaviorEvent: managed event class (carries string JsonParams)
- BehaviorIngressSystem: consumes event, bumps InstanceId (unchecked wrap),
  resets BTreeState, writes blackboard via ParseParams unsafe delegate;
  runs in InputSystemGroup (before SimulationSystemGroup)

Fdp.Kernel:
- StandardSystemGroups.cs: added InputSystemGroup

BehaviorConstants: added BrainTierHsm = 1, BrainTierBTree = 2

Correctives:
- Ordering attributes on ChannelArbitrationSystem + all 3 dispatchers
- WritingSpyExecutor<TChannel> in TestHelpers.cs
- 3 existing tests strengthened with additional assertions

Tests: 25 total in Behavior.Tests; full solution green (0 build errors)

Related: FDP/Docs/projects/behavior-control/DESIGN.md §3.2-3.3
```

---

**Next Batch:** BATCH-05 (Phase 2 — Perception Toolkit, with HSM event injection fix + minor correctives)
