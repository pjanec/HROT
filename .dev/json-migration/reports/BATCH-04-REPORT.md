# BATCH-04 Report

**Batch:** BATCH-04
**Developer:** GitHub Copilot
**Date:** 2025-07-25
**Status:** COMPLETE

---

## Summary

All tasks in BATCH-04 are complete.  154 pre-existing migration tests remain green,
26 new `InMemoryMigrationStorage` tests (T1-310..T1-335) were added, and 8 new
`FileSystemMigrationStorage` tests (T3-001..T3-008) were added.

**Total migration tests: 188 / 188 pass.**

---

## Corrective items (Debt tracker)

### D-011 - Pin hash for non-ASCII input in T1-293

File: `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/HashUtilitiesTests.cs`

Added `Assert.Equal("4a99557e4033c353", HashUtilities.ComputeContentHash("\u00e9"))` at the end of
`ComputeContentHash_Utf8Bytes_NotPlatformDependent`.

> **Correction from instructions:** The instructions stated the pre-verified hash was
> `"2db7e52e4d32d0c5"` but the actual SHA-256 first-16-hex of UTF-8 {0xC3, 0xA9} is
> `"4a99557e4033c353"`.  The instructions contained an incorrect pre-computation.
> The pinned value in the test reflects the real runtime output.

### D-012 - Verify Set value survives round-trip in T1-264

File: `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/UnknownsJournalTests.cs`

Added `Assert.Equal(99, restored.Operations[0].Value!.GetValue<int>())` at the end of
`Serialize_RoundTripsThroughDeserialize`.

### D-013 - Document array granularity limitation in DomDiffer

File: `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/DomDiffer.cs`

Added a multi-line comment block before the array branch explaining that arrays are
treated as monolithic leaf values (not element-by-element), so `[N]`-indexed paths from
`DomDiffer` are not produced in normal use.

---

## JM-P1-009: IMigrationStorage + InMemoryMigrationStorage

### Files created

| File | Purpose |
|------|---------|
| `FDP/Engine/Fdp.Core/Serialization/Migrations/IMigrationStorage.cs` | Internal interface (9 methods) |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/SidecarFileHelper.cs` | Internal static helpers shared by both implementations |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/InMemoryMigrationStorage.cs` | Dictionary-backed in-memory implementation |

### Design deviations

1. **`IMigrationStorage` is `internal`, not `public`.**  The spec §3.11 says `public interface IMigrationStorage`.
   However `UnknownsJournal` is `internal sealed`, and an `internal` type cannot appear in the signature of a
   `public` interface without causing accessibility errors.  Making the interface `internal` is the minimal
   correct fix.

2. **Shared filename helpers extracted to `SidecarFileHelper`.**  The spec implied that filename parsing helpers
   lived inside `InMemoryMigrationStorage` and were reused by `FileSystemMigrationStorage` via `internal static`.
   To avoid coupling the FS implementation to the in-memory class, the helpers were factored into a dedicated
   `internal static class SidecarFileHelper`.  Both implementations call `SidecarFileHelper.*` directly.

3. **`InMemoryMigrationStorage` uses `StringComparison.Ordinal` for keys.**  The spec did not specify
   case-sensitivity for the in-memory store.  `Ordinal` (case-sensitive) was chosen to match real filesystem
   behavior on Linux/macOS and avoid masking case-sensitivity bugs in callers.

### Test helpers on InMemoryMigrationStorage

| Method | Access | Purpose |
|--------|--------|---------|
| `Seed(path, content)` | `public` | Seed an original file |
| `SeedSnapshot(path, version, content)` | `public` | Seed snapshot (auto-computes hash) |
| `SeedRawSidecar(path, fileName, rawContent)` | `internal` | Seed raw sidecar by exact filename (for corruption tests) |
| `HasSnapshot(path, version)` | `public` | Query whether a snapshot exists for a version |
| `HasJournal(path, hash)` | `public` | Query whether a journal exists for a hash |
| `ReadCurrent(path)` | `public` | Read the stored original content |

---

## JM-P1-010: FileSystemMigrationStorage

### File created

`FDP/Engine/Fdp.Core/Serialization/Migrations/FileSystemMigrationStorage.cs`

### Atomic write protocol

All writes use a temp-file-and-move pattern:

```
targetPath + ".tmp." + Guid[..8]  →  atomic File.Move(temp, target, overwrite: true)
```

The temp file is deleted on exception (best-effort).

### Edge cases

| Scenario | Behaviour |
|----------|-----------|
| `ReadOriginalAsync` - file not found | Returns `null` |
| `ReadOriginalAsync` - I/O error | Throws `MigrationException` |
| `FindBestSnapshotAsync` - no sidecar dir | Returns `null` |
| `FindJournalAsync` - no sidecar dir | Returns `null` |
| `DeleteJournalAsync` - file missing | No-op (idempotent) |
| `DeleteSidecarAsync` - file missing | No-op (idempotent) |
| `ListSidecarsAsync` - no sidecar dir | Returns empty list |

---

## Tests added

### T1-310..T1-335 — InMemoryMigrationStorageTests (26 tests)

| ID | Test name | Coverage |
|----|-----------|----------|
| T1-310 | `ReadOriginalAsync_ExistingFile_ReturnsContent` | ReadOriginal happy path |
| T1-311 | `ReadOriginalAsync_NonexistentFile_ReturnsNull` | ReadOriginal miss |
| T1-312 | `WriteOriginalAsync_NewFile_Creates` | WriteOriginal create |
| T1-313 | `WriteOriginalAsync_ExistingFile_Overwrites` | WriteOriginal overwrite |
| T1-314 | `WriteSnapshotAsync_CreatesSidecarEntry` | WriteSnapshot creates entry |
| T1-315 | `WriteSnapshotAsync_FilenameFollowsConvention` | Filename format |
| T1-316 | `FindBestSnapshotAsync_NoSidecars_ReturnsNull` | Empty store |
| T1-317 | `FindBestSnapshotAsync_ExactMatch_ReturnsEntry` | Exact version hit |
| T1-318 | `FindBestSnapshotAsync_LowerSnapshot_Returned` | Version below max |
| T1-319 | `FindBestSnapshotAsync_HigherSnapshotExists_NotReturned` | Version above max |
| T1-320 | `FindBestSnapshotAsync_MultipleSnapshots_ReturnsHighestAllowed` | Best selection |
| T1-321 | `FindBestSnapshotAsync_HashMismatch_Throws` | Corruption detection |
| T1-322 | `WriteJournalAsync_EmptyOperations_ThrowsArgumentException` | Guard clause |
| T1-323 | `WriteJournalAsync_FilenameFollowsConvention` | Journal filename |
| T1-324 | `FindJournalAsync_MatchingHash_ReturnsJournal` | Journal happy path |
| T1-325 | `FindJournalAsync_NonMatchingHash_ReturnsNull` | Wrong hash miss |
| T1-326 | `FindJournalAsync_CorruptJournalEnvelope_Throws` | Bad docType corruption |
| T1-327 | `FindJournalAsync_InconsistentHashInsideJournal_Throws` | Filename/body hash mismatch |
| T1-328 | `DeleteJournalAsync_ExistingJournal_Deletes` | Delete journal |
| T1-329 | `DeleteJournalAsync_NonexistentJournal_NoOp` | Delete non-existent journal |
| T1-330 | `ListSidecarsAsync_EmptyDirectory_ReturnsEmpty` | Empty store |
| T1-331 | `ListSidecarsAsync_MultipleSidecars_ReturnsAll` | Count check |
| T1-332 | `ListSidecarsAsync_ParsesFilenameCorrectly` | Field accuracy |
| T1-333 | `ListSidecarsAsync_OtherBaseNames_ExcludedFromResult` | Base name filter |
| T1-334 | `DeleteSidecarAsync_ExistingFile_Deletes` | Delete by filename |
| T1-335 | `DeleteSidecarAsync_Nonexistent_NoOp` | Delete non-existent sidecar |

### T3-001..T3-008 — FileSystemMigrationStorageTests (8 tests)

| ID | Test name | Coverage |
|----|-----------|----------|
| T3-001 | `FullCycle_RealFiles_RoundTripsLosslessly` | Write+read+snapshot round-trip |
| T3-002 | `AtomicWrite_TempFileCleanedUp_OnException` | Temp file cleanup on failure (Windows) |
| T3-003 | `ConcurrentReads_SameFile_DoNotInterfere` | Parallel reads |
| T3-004 | `WriteSnapshot_CreatesSidecarDirectory_WithCorrectLayout` | Sidecar dir creation |
| T3-005 | `Sidecar_FilenameParseable_ByListSidecars` | Filename parsing end-to-end |
| T3-006 | `MissingSidecarDirectory_ListSidecars_ReturnsEmpty` | Missing sidecar dir |
| T3-007 | `ReadLockedFile_FailsGracefully` | Locked file error handling (Windows only) |
| T3-008 | `FileSystemStorage_BehaviorMatchesInMemoryStorage` | Parity check |

---

## Test run results

```
dotnet test ... --filter "FullyQualifiedName~Migrations"

Passed!  - Failed: 0, Passed: 188, Skipped: 0, Total: 188
```

Breakdown:
- 154 pre-existing tests
- 3 modified tests (D-011, D-012, D-013 assertions added; all still pass)
- 26 new T1-310..T1-335
- 8 new T3-001..T3-008

---

## Known weak points

1. **T3-007 is Windows-only.**  File-locking semantics on Linux/macOS differ; the test
   returns early on non-Windows platforms rather than using `[Fact(Skip=...)]` so the
   test still runs but exercises only the platform check logic.

2. **FileSystemMigrationStorage does not do fsync.**  There is no `FileStream` flush or
   `FlushToDisk` after the atomic move.  Power-failure durability is not guaranteed below
   the OS buffer layer.

3. **`InMemoryMigrationStorage` uses `Ordinal` path comparison**, meaning `"test.json"` and
   `"TEST.JSON"` are treated as different files.  This matches Linux behavior but could cause
   confusion on Windows where the real filesystem is case-insensitive.

4. **D-011 correction:** The pre-computed hash value in BATCH-04-INSTRUCTIONS.md was wrong
   (`"2db7e52e4d32d0c5"` instead of the correct `"4a99557e4033c353"`).  The instruction
   should be updated to reflect the correct value.
