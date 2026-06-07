# BATCH-06 Review

**Batch:** BATCH-06
**Reviewer:** Development Lead
**Date:** 2025-07-14
**Status:** ⚠️ CHANGES APPLIED BY REVIEWER

---

## Summary

JM-P1-012 implementation (PersistentMigrationAdapter, MigrationLoadResult, Round-Trip Diff) is
functionally solid. Two issues found — both applied directly by reviewer rather than sending back
to developer (changes are small and targeted).

---

## Issues Found

### Issue 1: FindBestSnapshotAsync hash validation incorrectly removed (regression bug)

**Files:**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/InMemoryMigrationStorage.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/InMemoryMigrationStorageTests.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/PersistentMigrationAdapterTests.cs`

**Problem:**
The original `FindBestSnapshotAsync` validated that `hash(snapshot_content) == parsedHash`,
acting as a data-integrity guard. The sub-agent wrote T2-038 with `v5Hash` as the snapshot's
`contentHash` when the snapshot actually contained v2 content. This caused the integrity check to
throw (correctly), so the developer removed the validation entirely and rewrote T1-321 from
"hash mismatch throws MigrationException" to "hash mismatch is OK".

The snapshot naming convention is `{base}.v{version}.{hash}.snapshot.json` where `{hash}` is
always `HashUtilities.ComputeContentHash(snapshotContent)`. The validation was correct and
semantically meaningful. T2-038 had wrong test setup.

**Fix applied:**
- Restored hash validation in `FindBestSnapshotAsync` (throws `MigrationException` on mismatch)
- Restored T1-321 to its original "hash mismatch throws" form
- Fixed T2-038 to compute hash from v2 snapshot content (not from v5 file content)

### Issue 2: PersistentMigrationAdapter constructor visibility — no fix possible

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/PersistentMigrationAdapter.cs` (line 23)

**Analysis:**
Design spec §7.2 shows `public PersistentMigrationAdapter(...)`, but `IMigrationStorage` is `internal`.
Making the constructor `public` produces CS0051 (inconsistent accessibility). The constructor must stay
`internal`. This is not a developer error — the design intent will be satisfied when `MigrationBootstrap`
(JM-P1-013) provides the external `public` construction API. No fix needed.

---

## Test Quality Assessment

No quality issues with the test logic itself. The tests verify actual behavior with correct
assertions. T2-080 (gate test) is a genuine end-to-end round-trip verifying real data preservation
semantics.

---

## Verdict

**Status:** APPROVED (after reviewer-applied fixes)

---

## Commit Message

```
feat: add PersistentMigrationAdapter, MigrationLoadResult, Round-Trip Diff (BATCH-06)

Completes JM-P1-012

- New: MigrationLoadResult (Dom, OriginalMeta, CurrentMeta, WasMigrated,
  HasUnknownsJournal, IsDegraded, SourceContentHash, Journal, Report)
- New: PersistentMigrationAdapter (LoadAndMigrateAsync + SaveAsync,
  Cases A/B/C/D, dual-hash PruneStaleAsync)
- DomDiffer: opt-in compareArraysElementWise param (default false, T1
  tests unaffected); UnknownsJournal.Compute now uses element-wise diff
- DiffToJournalConverter: added $meta exclusion at root level
- MigrationPipeline: added CanMigrateTo() helper
- InMemoryMigrationStorage: restored FindBestSnapshotAsync hash integrity check
- 30 new tests: T2-030..T2-041, T2-050..T2-066, T2-080

Tests: 228/228 passing
```

---

**Next Batch:** BATCH-07 — MigrationServices + MigrationBootstrap (JM-P1-013)
