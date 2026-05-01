# BATCH-03 Review

**Reviewer:** Dev Lead
**Status:** APPROVED WITH MINOR FIXES

---

## Verification Results

| Suite | Before BATCH-03 | After BATCH-03 | Delta |
|-------|-----------------|----------------|-------|
| Fhsm.Tests | 251 | 257 | +6 |
| Fdp.Toolkits.Tests | 776 (789 total, 13 pre-existing fail) | 776 | 0 |
| BhuIntegrationTests (A+B) | — | 7/7 | +7 |
| ClusterRunner.Integration.Tests | — | 2/2 (E1+E2) | +2 |
| Solution build | clean | clean | 0 errors |

---

## Post-Review Fix Applied (by Dev Lead)

**Issue:** `HsmActionDispatcher` static state race between test classes.

After the subagent delivered the batch, the following tests were missing the `[Collection("HsmActionDispatcher")]` collection attribute:
- `Fhsm.Tests.Kernel.CommandBufferIntegrationTests`
- `Fhsm.Tests.SourceGen.ActionDispatchTests`
- `Fhsm.Tests.Examples.IntegrationTests`

Without the attribute, running tests in parallel caused `ClearAll()` from integration tests to wipe the dispatcher tables out from under these existing test classes, producing intermittent failures. Fix: added `[Xunit.Collection("HsmActionDispatcher")]` to all three classes.

**After fix:** `Fhsm.Tests` 257/257 deterministically.

---

## Spot Checks

**Group A (IT-BHU-A1 through A4):** IT-BHU-A3 adapted to use the same blob for both doctrines (since `ResetHsmComponents` does not update `MachineId`). This is correct per spec — the spec only requires `Terminated` cleared and `Phase == Idle` before the first tick, not a different machine.

**Group B (IT-BHU-B1 through B3):** `CognitiveInterruptSystem` → `HsmTickSystem` → `CognitiveCleanupSystem` pipeline proven. Edge detection verified (no re-trigger on second frame).

**Group C (IT-BHU-C1 through C3):** SharedAi adapters exercised end-to-end. Hash cross-check verifies both generators compute identical FNV-1a values for the same compound key.

**Group D (IT-BHU-D1 through D3):** `ClearAll()` semantics verified. `RegisterAll()` round-trip confirmed. `IsFinal` → `Terminated` chain driven through `HsmKernel.Update()`.

**Group E (IT-BHU-E1 and E2):** System order (6 systems, correct types, no `HsmDamageBridgeSystem`) verified. Full 2-frame integration run (mobility-lost → final state → `DoctrineFinishedEvent`) passes.

---

## Decision

**APPROVED — commit.**

Commit message: `feat: BHU-017 integration tests + Fhsm.Tests dispatcher isolation fix`
