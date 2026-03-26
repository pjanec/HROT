# BS-1-BATCH-01 Review

**Batch:** BS-1-BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-03-26
**Status:** ✅ APPROVED

---

## Summary

Implemented BS-1 proof-of-concept combat contracts (WeaponFire + Detonation/Damage) and enforced distributed authority correctness for damage (BS1-T001..BS1-T003). Refactored `AimAndFireExecutor` to emit `WeaponFireIntent` instead of `FireRequestEvent` (BS1-T004), with new unit tests covering struct layout, DDS topic attributes, event publication, and authority gating.

---

## Issues Found

### Issue 1: Intermediate runtime regression until T007 lands

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs`
**Problem:** `AimAndFireExecutor` now emits `WeaponFireIntent`, but `FireProcessingSystem` still consumes `FireRequestEvent` (T007 scope). The integration test suite had to narrow/defer milestones that depend on bullets being spawned.
**Fix:** Ensure the next batch includes BS1-T007 (or an equivalent fix) and restore the bullet-dependent assertions once the CQRS chain is end-to-end.

---

### Issue 2: Authority guard implementation differs from the spec’s suggested API

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`
**Problem:** BS1-T003 requested using the project’s “existing HasAuthority API”. The implementation uses `World.HasComponent<NetworkAuthority>` + `NetworkAuthority.HasAuthority`, with a fallback to “authoritative when component missing”. This matches the unit tests but should be validated against real distributed authority wiring.
**Fix:** In the next batch, include at least one higher-level/harness check covering a remote-owned entity with `NetworkAuthority` present (so the guard path is exercised in a realistic setup).

---

### Issue 3: Minor test coverage gaps in BS1-T004

**File:** `FDP/Toolkits/FDP.Toolkit.Combat.Tests/AimAndFireExecutorTests.cs`
**Problem:** The happy-path test asserts shooter/target IDs but does not assert `WeaponIndex` value, and it doesn’t assert `channel.Status` remains `NodeStatus.Running` on the firing tick.
**Fix:** Extend `AimAndFire_EmitsWeaponFireIntent_WhenConditionsAreMet` to validate `WeaponIndex == 0` and `channel.Status == NodeStatus.Running` after firing.

---

## Test Quality Assessment

Tests are high quality for this batch: they validate meaningful behavior (health changes gated by `NetworkAuthority`, correct event emission/absence, and struct layout + DDS topic attribute values). They would catch correctness regressions such as “wrong event type emitted”, “wrong event payload IDs”, and “wrong struct sizes/topic names”.

Residual risk is mainly around distributed authority wiring (Issue 2) and the known intermediate state until T007 (Issue 1).

---

## Verdict

**Status:** ✅ APPROVED

Follow-up required: include BS1-T007 and restore bullet-dependent integration milestones in BS-1 Batch 2.

---

## 📝 Commit Message

```
feat: BS-1 combat CQRS POC contracts + authority gating (BS-1-BATCH-01)

Completes BS1-T001, BS1-T002, BS1-T003, BS1-T004.

Adds unmanaged ECS event structs and CycloneDDS `DdsTopic`-annotated DDS messages with layout/topic unit tests. Refactors the Brain-tier `AimAndFireExecutor` to publish `WeaponFireIntent` and prevents non-authority nodes from applying damage.

Tests: dotnet test FDP/Toolkits/FDP.Toolkit.Combat.Tests/FDP.Toolkit.Combat.Tests.csproj and dotnet test Bagira.DDS.DataModel.Tests/Bagira.DDS.DataModel.Tests.csproj
Related: docs/brain-split/BS-1-DESIGN.md, docs/brain-split/BS-1-TASK-DETAIL.md
```

---

**Next Batch:** BS-1-BATCH-02

