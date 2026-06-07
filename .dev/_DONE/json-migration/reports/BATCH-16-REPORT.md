# BATCH-16 Report — JM-P2-011: Phase 2 CI Regression Run (GATE)

**Date:** 2026-05-29
**Status:** COMPLETE — all deliverables done, all new tests pass

---

## 1. Files Created / Modified

| Action  | File |
|---------|------|
| Created | `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase2ConventionTests.cs` |
| Created | `.dev/json-migration/reports/BATCH-16-REPORT.md` (this file) |
| Created | `.dev/json-migration/reports/PHASE-2-GATE-REPORT.md` |

No existing files were modified.

---

## 2. New Tests: T_Conv_01 through T_Conv_04

All four tests are in `Hrot.Common.Tests.Scenario.Migrations.Phase2ConventionTests`.

| Test ID    | Method Name                                                      | Result | Duration |
|------------|------------------------------------------------------------------|--------|----------|
| T_Conv_01  | `AllCommittedFixtures_HaveValidMetaEnvelope`                     | PASS   | 352 ms   |
| T_Conv_02  | `AllScenarioFixtures_HaveCorrectDocTypeAndVersion`               | PASS   | 833 ms   |
| T_Conv_03  | `AllBlueprintFixtures_HaveCorrectDocTypeAndVersion`              | PASS   | 347 ms   |
| T_Conv_04  | `LoadScenario_ViaReadOnlyAdapter_DomHasValidMetaAndRoundTrips`   | PASS   | 217 ms   |

### T_Conv_01 verified:
- Walked workspace root for `*.json` files
- Applied same exclusion logic as `FixtureStamper.ShouldSkipPath`
- Detected known fixtures by `header.subsystemType` / `Header.SubsystemType` / `nodes+segments`
- Confirmed >= 10 known fixtures found (sanity guard)
- Called `JsonEnvelope.Peek(path)` on each — zero failures

### T_Conv_04 verified:
- Loaded `scenarios/hill-attack/scenario.json` via `ReadOnlyMigrationAdapter.LoadAndMigrateAsync`
- DOM first property is `"$meta"` with `docType="Hrot.Scenario"`, `schemaVersion=1`
- Serialized DOM to JSON string via `ToJsonString()`, re-parsed
- Re-parsed DOM also has `"$meta"` first with correct values
- Legacy `"header"` field preserved

---

## 3. Full Test Suite Summary

| Test Suite                          | Passed | Failed | Skipped | Total | Notes |
|-------------------------------------|--------|--------|---------|-------|-------|
| `Fdp.Core.Tests`                    | 1141   | 0      | 2       | 1143  | 2 benchmarks intentionally skipped |
| `Hrot.Common.Tests`                 | 15     | 0      | 0       | 15    | +4 new T_Conv tests |
| `Fdp.Tools.EnvelopeStamper.Tests`   | 10     | 0      | 0       | 10    | |
| `Hrot.SimHost.Tests`                | 573    | 38     | 3       | 614   | 38 failures are pre-existing (non-migration) |
|   — NodeBootstrapperMigrationTests  | 12     | 0      | 0       | 12    | subset of above; migration wiring verified |

**Total passing (new tests): 4 of 4**
**Pre-existing failures in Hrot.SimHost.Tests:** 38 — in HillAttack, Gizmos, CreateEntityRequest, PathfindingBatch, EpisodeLoad, FullBranchPipeline areas. None in migration-related tests.

---

## 4. Phase 2 Gate Report

See `.dev/json-migration/reports/PHASE-2-GATE-REPORT.md`.

---

## Implementation Notes

- `Phase2ConventionTests` uses the same exclusion logic as `FixtureStamper.ShouldSkipPath` (duplicated inline — no cross-project reference needed).
- `T_Conv_04` uses `services.ReadOnly.LoadAndMigrateAsync(path)` (async overload), which is the correct API on `ReadOnlyMigrationAdapter`.
- `ReadOnlyLoadOutcome.AsJsonObject()` handles both fast-path (`RawContent`) and slow-path (`MigratedDom`) cases.
- Workspace root discovery via `IOS-IG-SimHost.sln` marker file is safe for CI.
- `TreatWarningsAsErrors` is not set on this test project (consistent with existing tests); no new warnings introduced.
