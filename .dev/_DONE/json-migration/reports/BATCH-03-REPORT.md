# BATCH-03 — Completion Report

**Date:** 2026-01-01 (batch complete)
**Branch:** (current working branch)
**Submitted by:** Developer Agent

---

## Summary

All tasks in BATCH-03 are complete. The test suite went from 124 migration tests to 154 migration
tests (30 new tests added), all green. The 2 pre-existing failures
(`MilitarySimulationPerformanceTest`, `DualStream_RecordableMaskFilter_NonRecordableBitIsCleared`)
remain unchanged and are unrelated to this batch.

---

## Task Outcomes

### Corrective Task 0 — D-005 through D-010

| Defect | Status | Detail |
|--------|--------|--------|
| D-005 (missing T1-123) | Fixed | `MigrateToCurrent_PreservesEngineVersionField` added |
| D-005 (missing T1-124) | Fixed | `MigrateToCurrent_PreservesCreatedUtcField` added |
| D-005 (missing T1-125) | Fixed | `MigrateToCurrent_PreservesCreatedByField` added |
| D-005 (missing T1-129) | Fixed | `MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3` added |
| D-005 (missing T1-136) | Fixed | `MigrateTo_NoPathExists_Throws` added |
| D-006 (wrong label pre-T1-123) | Fixed | Header changed to `// Downgrade v2 -> v1 (unlabeled extra test)` |
| D-006 (wrong label pre-T1-124) | Fixed | Header changed to `// Passthrough doc type -> empty report (unlabeled extra test)` |
| D-006 (wrong label pre-T1-125) | Fixed | Header changed to `// Unknown doc type -> throws MigrationException (unlabeled extra test)` |
| D-006 (wrong label pre-T1-129) | Fixed | Header changed to `// Report.Direction is Up for upgrade (unlabeled extra test)` |
| D-006 (wrong label pre-T1-136) | Fixed | Header changed to `// Direction is Down for downgrade (unlabeled extra test)` |
| D-007 (`ThrowingMigratorV2ToV3` missing) | Fixed | Class added to `TestMigrators.cs` |
| D-008 (`StubMigrator` in T1-129) | N/A | Pre-existing `StubMigrator` in file was already sufficient |
| D-009 (missing `TestDocV1ToV2_WarningWithPath`) | N/A | Pre-existing in file |
| D-010 (weak `>=` assertion in T1-138) | Fixed | Changed to `Assert.True(report.Duration > TimeSpan.Zero)` |

### JM-P1-008 — DiffToJournalConverter + UnknownsJournal + HashUtilities

#### Source files created

| File | Description |
|------|-------------|
| `Fdp.Core/Serialization/Migrations/Internal/JournalOpKind.cs` | `Set`/`Remove` enum |
| `Fdp.Core/Serialization/Migrations/Internal/JournalOperation.cs` | Record: Kind + Path + Value |
| `Fdp.Core/Serialization/Migrations/Internal/HashUtilities.cs` | `ComputeContentHash` — SHA-256 first 8 bytes → 16 lower-hex chars |
| `Fdp.Core/Serialization/Migrations/Internal/DiffToJournalConverter.cs` | Walks DiffNode tree → `IReadOnlyList<JournalOperation>` |
| `Fdp.Core/Serialization/Migrations/UnknownsJournal.cs` | `Compute`, `Serialize`, `Deserialize`, `ApplyTo` |

#### Source file modified

| File | Change |
|------|--------|
| `Fdp.Core/Serialization/Migrations/Internal/JsonPathParser.cs` | Added `Build(IEnumerable<object>)` overload that accepts `string`/`int` segments |

#### Test files created

| File | Tests |
|------|-------|
| `Fdp.Core.Tests/Serialization/Migrations/Internal/HashUtilitiesTests.cs` | T1-290..T1-293 (4 tests) |
| `Fdp.Core.Tests/Serialization/Migrations/Internal/DiffToJournalConverterTests.cs` | T1-240..T1-246 (7 tests) |
| `Fdp.Core.Tests/Serialization/Migrations/UnknownsJournalTests.cs` | T1-260..T1-273 (14 tests) |

---

## Test Results

```
Migration tests only:
  Passed: 154   Failed: 0   Skipped: 0

Full suite:
  Passed: 943   Failed: 2 (pre-existing)   Skipped: 2
```

Pre-existing failures (not related to this batch):
- `Fdp.Tests.MilitarySimulationPerformanceTest.RealisticMilitrarySimulation_CompleteScenario_MeasuresPerformance`
  — timing test that flaps on the CI machine (playback not faster than recording).
- `Fdp.Tests.RecorderSystemTests.DualStream_RecordableMaskFilter_NonRecordableBitIsCleared`
  — pre-existing baseline failure unrelated to JSON migration.

---

## Design Decisions / Deviations

1. **`JsonPathParser.Build` added instead of inline segment list** — Enables the converter to
   produce canonical paths from a mixed `string`/`int` stack without coupling the converter to
   the internal `JsonPathSegment` hierarchy.

2. **Array-index detection in `DiffToJournalConverter.DetermineSegment`** — Checks the parent
   node in `preMigrationDom` at the current path stack; only emits an `int` segment when the
   parent is a `JsonArray` and the name is a valid non-negative integer. This matches the
   spec requirement that array children use `[N]` form.

3. **`ApplyTo` two-pass ordering** — All `Set` ops run first (in journal order), then all
   `Remove` ops (in journal order), per design §7. Test T1-273 covers the edge case where
   `Remove` appears before `Set` in the journal and verifies that `Set-first` order is obeyed.

4. **`ComputeContentHash` returns 16 hex chars** — Uses `SHA-256` first 8 bytes (not 16), i.e.,
   `Convert.ToHexString(hash, 0, 8)`. The summary erroneously mentioned 16 bytes; the 16-char
   (8-byte) form matches the expected value `"2cf24dba5fb0a30e"` from the test spec.

5. **`UnknownsJournal` is `internal sealed`** — Matches the project convention for internal
   migration infrastructure. `InternalsVisibleTo` in `Fdp.Core.csproj` makes it visible to
   the test project.

---

## Files Changed

### Modified
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestMigrators.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationPipelineTests.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JsonPathParser.cs`

### Created
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JournalOpKind.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/JournalOperation.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/HashUtilities.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/DiffToJournalConverter.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/UnknownsJournal.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/HashUtilitiesTests.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/DiffToJournalConverterTests.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/UnknownsJournalTests.cs`
