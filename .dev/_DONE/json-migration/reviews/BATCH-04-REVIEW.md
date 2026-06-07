# BATCH-04 Review

**Batch:** BATCH-04
**Reviewer:** Dev Lead
**Date:** 2025-07-25
**Decision:** APPROVED with noted weaknesses (2 P3 debt items)

---

## Summary

BATCH-04 is approved. All 188 migration tests pass; build is clean (0 warnings, 0 errors).
JM-P1-009 and JM-P1-010 are complete. Three P3 corrective debt items (D-011/D-012/D-013)
have been resolved. Two new minor debt items are registered below.

---

## Corrective Debt Review

### D-011 - RESOLVED (with correction)
The developer correctly caught that the pre-verified hash in the batch instructions was
wrong. The actual SHA-256 first-16-hex of UTF-8 {0xC3, 0xA9} is `"4a99557e4033c353"`,
not `"2db7e52e4d32d0c5"` as stated in the instructions. The pinned assertion in T1-293
now uses the correct runtime-verified value.

**Verdict:** Correct implementation. Instruction error was from the batch instructions (now noted
in D-016 debt item for correction).

### D-012 - RESOLVED
`Assert.Equal(99, restored.Operations[0].Value!.GetValue<int>())` added to T1-264. Round-trip
value assertion is now present and meaningful.

### D-013 - RESOLVED
Comment block added at the array branch in `DomDiffer.Diff`. Explains the monolithic-leaf
treatment, why `[N]` paths are not naturally produced, and notes the limitation is acceptable
for the entity-dictionary use case.

---

## JM-P1-009 Review

### IMigrationStorage

- All 9 methods correctly defined ✅
- `internal` accessibility decision is correct and well-documented (UnknownsJournal is
  internal; a public interface referencing internal types would be a compile error) ✅
- All method contracts (return null vs throw, atomic vs direct) match the design spec ✅

### SidecarFileHelper

Good factoring. Extracting the filename helpers to a shared static class avoids coupling
FileSystemMigrationStorage to InMemoryMigrationStorage and is cleaner than the spec's implied
approach. The parsing logic is correct:
- Strips suffix, splits on last `.`, validates `v{N}` prefix, parses int correctly ✅
- `OrdinalIgnoreCase` comparison for suffixes is appropriate ✅

### InMemoryMigrationStorage

- Hash verification in `FindBestSnapshotAsync` is correctly placed and throws `MigrationException` ✅
- `FindJournalAsync`: correctly chains Deserialize (validates docType via UnknownsJournal) then
  body-hash vs filename-hash check ✅
- `WriteJournalAsync` guard (ArgumentException on empty ops) ✅
- `ListSidecarsAsync` enumerates by filename only (no content reading) ✅
- `DeleteSidecarAsync` is idempotent (silent remove) ✅
- Test helpers (`Seed`, `SeedSnapshot`, `SeedRawSidecar`, `HasSnapshot`, `HasJournal`,
  `ReadCurrent`) are well-designed and cover all test scenarios ✅

### T1-310..T1-335 Test Quality

All 26 tests are present, match spec IDs, and are properly written:

| Test | Quality assessment |
|------|--------------------|
| T1-310..T1-313 | Basic I/O paths: correct arrange/act/assert ✅ |
| T1-314..T1-315 | Filename convention verified via ListSidecars - good ✅ |
| T1-316..T1-320 | FindBest logic well covered including multiple-snapshot case ✅ |
| T1-320 | Asserts `Version == 3`, not just non-null ✅ |
| T1-321 | Uses `SeedRawSidecar` with fake hash, asserts `MigrationException` ✅ |
| T1-322 | Uses `Compute` on identical DOMs to get empty journal, correct ✅ |
| T1-323 | Journal filename convention verified via ListSidecars ✅ |
| T1-324 | Asserts `SourceDocType` AND `Operations.Count` (substantive) ✅ |
| T1-325 | Correct: non-matching hash returns null, not throw ✅ |
| T1-326 | Uses corrupt docType JSON, asserts `MigrationException` ✅ |
| T1-327 | Uses mismatched body/filename hash, asserts `MigrationException` ✅ |
| T1-328..T1-329 | Delete: before/after state assertions ✅ |
| T1-330..T1-333 | ListSidecars: empty, count, field accuracy, base-name filter ✅ |
| T1-334..T1-335 | DeleteSidecar: before/after, no-op idempotency ✅ |

---

## JM-P1-010 Review

### FileSystemMigrationStorage

- Atomic write protocol matches spec exactly: `temp + ".tmp." + Guid.NewGuid().ToString("N")[..8]`,
  then `File.Move(temp, target, overwrite: true)`, temp deleted on exception ✅
- `FindBestSnapshotAsync`: reads all `.snapshot.json` files, parses filename, selects highest
  version <= maxVersion, verifies hash. Handles `FileNotFoundException` (sidecar deleted mid-scan)
  with `continue` - good defensive practice ✅
- `FindJournalAsync`: reads all `.unknowns.json` files, filters by filename hash, then
  double-checks body hash consistency ✅
- `ListSidecarsAsync`: no content reading, returns empty list when directory absent ✅
- IOException wrapping into MigrationException is correct (file-not-found returns null, other
  I/O errors are MigrationException) ✅
- Edge cases table in report matches implementation ✅

### T3-001..T3-008 Test Quality

| Test | Quality assessment |
|------|---------------------|
| T3-001 | Full read-write-snapshot round-trip with content equality assertion ✅ |
| T3-002 | Tests ACL-based denial on Windows, non-Windows gets a soft skip (debt D-015) |
| T3-003 | Two parallel reads via Task.WhenAll, both checked ✅ |
| T3-004 | Asserts directory exists AND single `.snapshot.json` file in it ✅ |
| T3-005 | ListSidecars after WriteSnapshot: Kind, Version, ContentHash all asserted ✅ |
| T3-006 | No sidecar dir → empty list (no throw) ✅ |
| T3-007 | Windows-only file lock test, early return on non-Windows (debt D-015) |
| T3-008 | Parity test covers WriteOriginal, ReadOriginal, WriteSnapshot, FindBestSnapshot, ListSidecars. Missing journal and DeleteSidecar coverage (debt D-014) |

---

## Debt Items

### D-014 (P3) — T3-008 parity test missing journal and DeleteSidecar coverage

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs`
**Description:** T3-008 parity test covers write/read/snapshot/list operations but does not
exercise `WriteJournalAsync`, `FindJournalAsync`, `DeleteJournalAsync`, or `DeleteSidecarAsync`
in the InMemory vs FileSystem comparison. The instruction required all 8 storage methods to be
covered in the parity gate.

### D-015 (P3) — T3-007 uses early return rather than xUnit Skip

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs`
**Description:** On non-Windows platforms, T3-007 does an early `return` (test passes vacuously)
instead of using an explicit xUnit `Skip` attribute or OS-conditional `[Fact]`. The test silently
reports as passed without executing anything on Linux/macOS. Use a runtime skip or
`[PlatformFact("windows")]` pattern.

### D-016 (P3) — BATCH-04-INSTRUCTIONS.md contains incorrect pre-computed hash

**File:** `.dev/json-migration/batches/BATCH-04-INSTRUCTIONS.md`
**Description:** The instructions stated the SHA-256 of U+00E9 (UTF-8) first-16-hex is
`"2db7e52e4d32d0c5"`. The correct value is `"4a99557e4033c353"`. Only affects documentation
(the test was corrected by the developer).

---

## Sign-off

All implemented types (`IMigrationStorage`, `InMemoryMigrationStorage`,
`FileSystemMigrationStorage`, `SidecarFileHelper`) are consistent with the design spec,
accessible to the test project via `InternalsVisibleTo`, and covered by tests that exercise
both happy-path and error paths. The storage layer is ready for BATCH-05 (adapters).
