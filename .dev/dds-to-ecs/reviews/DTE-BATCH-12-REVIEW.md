# DTE-BATCH-12 Review

**Batch:** DTE-BATCH-12  
**Reviewer:** Development Lead  
**Date:** 2026-02-28  
**Status:** ? NEEDS FIXES

---

## Summary
Integration troubleshooting wiring is mostly in place: IOS now uses `DdsWriterAdapter`, IG wires map-click egress, and SimHost/IG load the TKB catalog through `BagiraEnvironment`. However, several required success-condition tests from `TASK-DETAILS-Integration-Troubleshooting.md` are missing, so the batch cannot be approved yet.

---

## Code Quality & Design Adherence
- `Bagira.Map.Common.Dds` provides a shared `IDdsWriter` abstraction and `DdsWriterAdapter`, used consistently by IOS and Runner.
- `IgApplication` creates the command gateway and publishes `MapClickEvent` using the shared DDS participant.
- `SimHostApp` and `IgApplication` instantiate TKB via `BagiraEnvironment.CreateTkb(BdcTkbCatalog.RegisterAll)` as required.

---

## Test Quality Assessment (Gaps)
The following success-condition tests are missing or incomplete:

1. **INTS-P1-001:** No test asserts that `SimHostApp` and `IgApplication` successfully resolve TKB types on first spawn/ghost spawn (DDS-based scenario).
2. **INTS-P1-003:** No test verifies `DdsWriterAdapter.Write` actually writes a DDS sample (only interface/dispose checks exist).
3. **INTS-P1-004:** No behavioral test covers dockspace passthrough (map panning vs. ImGui capture).
4. **INTS-P1-005:** No test confirms `MapClickEvent` egress and `MiniIosPanelState` `CreateEntityRequest` publishing with network enabled/disabled cases.

These are P1 issues and must be resolved before approval.

---

## Required Fixes
- Add the missing tests listed above to meet the specified success conditions.
- Update the report with the additional test results.

---

## Suggested Commit Message
`Wire IOS DDS writers and IG map-click egress for integration troubleshooting`

---

## Verdict

**Status:** NEEDS FIXES

---

**Next Batch:** DTE-BATCH-13 (blocked until fixes above are complete)
