# BATCH-10 Review

**Batch:** BATCH-10
**Status:** APPROVED WITH MANDATORY CORRECTIONS
**Reviewer:** Dev Lead
**Date:** Post-sub-agent verification

---

## Corrective Fix: SchemaVersion References in Integration Tests (MANDATORY)

The sub-agent did not search `Hrot.ClusterRunner.Integration.Tests` for `SchemaVersion`
references. Two integration test files still referenced the deleted
`ScenarioHeaderDto.SchemaVersion` property:

- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ZoneScenarioLoadIntegrationTests.cs` (line 45)
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/UrbanCombatFileLifecycleTests.cs` (line 193)

Both were fixed by the dev lead before commit. The failing build was the gating signal.

**Root cause:** Sub-agent searched `Hrot/Engine` but not `Hrot/Runner` for `SchemaVersion`
references. Future batches removing DTO properties must search the ENTIRE solution, not
just the primary project tree.

---

## Build Verification

After applying the integration-test fix:

- All migration-related errors: RESOLVED
- Remaining build failures: Only pre-existing `Hrot.Blueprints.Tests` errors
  (`Hrot.Editor` namespace missing, `IAnimationTkbQueries` not found) — unrelated to this work.

---

## Test Verification

### Hrot.Common.Tests — 11/11 PASSED

New tests: `ScenarioPhase2Tests` T01–T04, updated `ModuleRegistrationTests` T02–T05.
All passing.

### Hrot.Presentation.Tests — PASSES WITH KNOWN RACE CONDITION

Isolation run of `SaveLoad_RoundTrip_PreservesEntitiesAndComponents`: PASSED.
Full suite run: intermittently fails due to pre-existing `ComponentTypeRegistry` static
state shared across parallel test classes. This is NOT a BATCH-10 regression.
Verified pre-existing by git-stash reverting to BATCH-09 state.

### Fdp.Toolkits.Tests — RoundTrip_MissionPlanQueue_PreservesPhaseData

Confirmed pre-existing: test fails in BATCH-09 state too.
`MissionPlanQueue` has `[DataPolicy(DataPolicy.NoSave)]` so it cannot be round-tripped
through save/load. The test expectation is incorrect. Not caused by BATCH-10.

---

## D-017 Review — PASS

Skeleton modules correctly converted to `public static class` with `public static void
RegisterAll`. All 4 files converted. `ModuleRegistrationTests.cs` T02–T05 updated to call
statically (no instantiation).

**Note:** The modules use `RegisterPassthroughDocType` which is the correct Phase 2 approach
for v1 types with no migration chain. This will be upgraded to `RegisterDocType` when actual
migration steps are added in later phases.

---

## D-018 Review — PASS

`ReadOnlyLoadOutcome.Report` has the correct XML doc comment. The "null on fast path"
contract is clearly documented with a note that callers must null-check before accessing
`MigrationReport.Warnings`.

---

## JM-P2-003 Review — PASS WITH MINOR NOTES

### ScenarioSerializer.Serialize — PASS

`JsonEnvelope.Write(root, new DocumentMeta(header.SubsystemType, 1))` is called after
assembling the `Entities` and optional `Header.TkbName` nodes. `$meta` is stamped as the
FIRST property via `Write`'s reorder logic (collect → clear → re-add). No `Header.SchemaVersion`
or root-level `Header.SubsystemType` written.

### ScenarioSerializer.Deserialize — ADAPTED (ACCEPTED)

The adapter uses `$meta.docType` check when `$meta` is present, rather than skipping the
check entirely. This is MORE correct than the spec's suggestion to skip: it ensures the
serializer remains gated to its registered doc type even in direct-call contexts (without
the migration adapter). The pre-existing `SubsystemType_MismatchSkipsDeserialize` test
remains valid and continues to pass.

### ScenarioFileService.SaveScenario — ADAPTED (ACCEPTED)

`PersistentMigrationAdapter.SaveAsync` requires a `priorLoad` parameter that's not available
for fresh saves. The direct write path is used unconditionally. `$meta` is still stamped by
`ScenarioSerializer.Serialize` before the write, so the envelope IS in the output file.
This is the correct approach.

### ScenarioFileService.LoadScenario — ADAPTED (ACCEPTED)

Uses `_migrationServices.ReadOnly.LoadAndMigrateAsync(filePath)` instead of `Persistent`.
`ReadOnlyLoadOutcome.AsJsonObject()` provides the migrated DOM. This is the correct API.

### HrotScenarioLoadHandler — PASS

Optional `ReadOnlyMigrationAdapter?` parameter added. Phase 2 path migrates JSON before
entity extraction.

### ScenarioPhase2Tests — TEST QUALITY: GOOD

T01: Checks `$meta` envelope present, correct docType and schemaVersion.
T02: Checks `Header.TkbName` stored correctly.
T03: Round-trip — 2 entities serialized and deserialized correctly.
T04: Legacy format (no `$meta`, has `Header.SubsystemType`) still deserializes correctly.

Tests verify actual behavior, not just compilation. The round-trip T03 is particularly
valuable as a regression guard.

### Minor Notes

1. The `HrotScenarioEnvelope.PeekSubsystemType` was updated to check `$meta.docType` first.
   This is good — it makes the AcceptedSubsystemTypes validation in `ScenarioFileService`
   work correctly for Phase 2 files.

2. `ValidateSubsystemType` in `ScenarioFileService` now checks both `$meta.docType` (Phase 2)
   and `Header.SubsystemType` (legacy). This is correct for the transition period.

---

## What Was NOT Done

The sub-agent correctly noted: `PersistentMigrationAdapter.SaveAsync` cannot be used for
fresh saves. The Phase 2 save path is therefore direct write only. The `MigrationServices`
injection is wired for LOAD only in Phase 2. Full persistent adapter usage (with migration
round-trip on load then save) is deferred to Phase 4.

---

## Decision

**APPROVED.** The two integration test fixes (applied by dev lead) are included in the commit.
All other task work is complete and correct. The adaptations from the spec are well-reasoned
and improve correctness.
