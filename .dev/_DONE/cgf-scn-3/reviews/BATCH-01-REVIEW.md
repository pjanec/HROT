# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Dev Lead  
**Verdict:** APPROVED  

---

## Assessment

All four tasks (S301–S304) are correctly implemented:

- **S301** — `SetManagedComponent` and effective managed-component removal are now used
  correctly. The deviation from the spec (`SetManagedComponent(null!)` instead of
  `RemoveManagedComponent`) is appropriate — `RemoveManagedComponent` is `internal` to
  `Fdp.Core` and the null-value overload is the documented public equivalent.
  Tests cover all three success conditions (HasManagedComponent true after replace, value
  correct, HasManagedComponent false after abort).

- **S302** — Span extraction before the for-loop correctly avoids the InlineArray
  defensive-copy trap. Tests verify PhaseCount 3, correct DoctrineId on each phase, and
  zero-task edge case.

- **S303** — `[DataPolicy(DataPolicy.NoSave)]` correctly placed. Old round-trip test
  replaced with DOM-exclusion test; co-present saveable component still appears.

- **S304** — `GetMode()` returns `TimeMode.Deterministic`. Test added and passing.

The 7 pre-existing failures in `Fdp.Toolkits.Tests` are unrelated to this batch and
were present before these changes.

---

## Commit Message

```
fix: Phase 1 core ECS correctness (BATCH-01) -- Completes S301-S304

S301: Fix ActiveMissionPlan to use SetManagedComponent/null-clear instead of
      unmanaged SetComponent/RemoveComponent in MissionControlExecutionSystem.
      Note: EntityRepository.RemoveManagedComponent is internal; use
      SetManagedComponent<T>(entity, null!) as the public equivalent.

S302: Fix InlineArray Span-mutation defensive-copy trap in TryBuildQueue.
      Extract Span<MissionPhase> before the for-loop per C#12 InlineArray rules.

S303: Add [DataPolicy(DataPolicy.NoSave)] to BrainBlackboard struct to exclude
      the 128-byte cognitive scratchpad from scenario JSON serialization.
      Replace round-trip test with DOM-exclusion assertion.

S304: Fix SteppingTimeController.GetMode() to return TimeMode.Deterministic
      (was incorrectly returning TimeMode.Continuous).

Tests: 451/451 pass in Hrot.SimHost.Tests; 2 new tests pass in Fdp.Toolkits.Tests.
```
