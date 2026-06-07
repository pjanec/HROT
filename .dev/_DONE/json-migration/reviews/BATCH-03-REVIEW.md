# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-05-28
**Status:** APPROVED

---

## Summary

All corrective tasks (D-005..D-010) correctly applied. JM-P1-008 fully implemented: HashUtilities,
JournalOpKind, JournalOperation, DiffToJournalConverter, UnknownsJournal all delivered. 154/154
migration tests pass; 0 build warnings.

---

## What Was Verified as Correct

### Corrective fixes (D-005..D-010)

- D-005/D-006/D-007: T1-123/T1-124/T1-125 added. Each test builds a v1 doc with all three diagnostic
  fields (engineVersion, createdUtc, createdBy) in $meta, runs MigrateToCurrent, and asserts the
  field is still present with its original value. Migration actually runs (v1 doc reaches current v3)
  so the invariant is genuinely exercised. ✅
- D-008: T1-129 added. Uses a 4-version registry where step 2 (v2->v3, ThrowingMigratorV2ToV3)
  always throws MigrationException. Step 3 (v3->v4) is a StubMigrator. The test asserts
  Assert.Throws<MigrationException> and then checks step3up.ApplyCallCount == 0. Confirms chain
  truly halts at the failing step. ✅
- D-009: T1-136 added. Calls MigrateTo(doc, 99) on a registry that has "Test.Doc" registered with
  currentVersion: 3. v99 is out of range. Assert.Throws<MigrationException> confirms the pipeline
  throws for an unreachable target version. ✅
- D-010: T1-138 assertion changed from `>= TimeSpan.Zero` to `> TimeSpan.Zero`. ✅
- Existing misidentified tests (formerly at T1-123/124/125/129/136 comment tags) relabeled to
  "unlabeled extra test" to prevent ID confusion. ✅

### JM-P1-008 — HashUtilities

- Implementation uses `SHA256.HashData(bytes)` (no allocation), converts to lowercase hex via
  `Convert.ToHexString(hash, 0, 8).ToLowerInvariant()`. Outputs exactly 16 chars. ✅
- T1-290 uses the pre-computed known hash `"2cf24dba5fb0a30e"` for input "hello". ✅
- T1-293 tests non-ASCII input (`"\u00e9"`) and verifies: 16 lowercase hex chars, stable across
  calls, different from ASCII 'e' hash. Confirms UTF-8 encoding is used. ✅

### JM-P1-008 — DiffToJournalConverter

- Walk algorithm correctly iterates DiffObject children (skipping unmodified nodes), maintains a
  pathStack of string/int segments, and at DiffValue leaves emits Set (preValue != null) or Remove
  (preValue == null). ✅
- Path canonicalization via `JsonPathParser.Build(pathStack)` correctly emits dotted form for
  identifier-valid keys and bracketed form for GUID/special-char keys. ✅
- T1-241 verifies the Set op's Kind, Path ("$.b"), and Value (42). ✅
- T1-242 verifies Remove for a field only in post (absent in pre). ✅
- T1-243 verifies Set emits pre's value (not post's) for differing values. ✅
- T1-245 (GUID key) correctly emits "$.entities['3702ba5f-...'].TkbIdentity". ✅
- T1-246 (array index): DomDiffer treats arrays as monolithic leaf DiffValues (by design), so the
  array-index path in DiffToJournalConverter is exercised via a manually constructed DiffNode tree.
  The test is honest about this with an explanatory comment and still verifies the segment type
  determination and path form. Acceptable. ✅

### JM-P1-008 — UnknownsJournal

- Compute: calls DomDiffer.Diff then DiffToJournalConverter.Convert; populates JournalMeta with
  docType "Fdp.MigrationJournal" and schemaVersion 1. ✅
- Serialize: emits $meta first, uses WriteIndented=true (2-space indentation), normalizes \r\n to
  \n on Windows. All required fields present. Set operations include "value"; Remove omits it. ✅
- Deserialize: validates docType, schemaVersion, all required body fields. Throws MigrationException
  with clear message for each failure mode. ✅
- ApplyTo: two-pass (all Sets first in journal order, then all Removes in journal order). T1-272 and
  T1-273 both verify the Set-first ordering produces the correct final DOM state. ✅
- T1-264 round-trip: verifies SourceDocType, SourceFileVersion, DownMigratedToVersion,
  SourceContentHash, Operations.Count, Operations[0].Kind, Operations[0].Path. ✅

---

## Issues Found

### Issue 1 (P3 — DEBT): T1-293 weak hash assertion for non-ASCII input

**File:** `Fdp.Core.Tests/Serialization/Migrations/Internal/HashUtilitiesTests.cs`
**Problem:** T1-293 verifies length, format, stability, and distinctness from ASCII 'e', but does
NOT verify the actual expected hash value for `"\u00e9"`. T1-290 correctly pins the expected hash
for "hello". T1-293 should do the same:
- UTF-8 bytes of `"\u00e9"` = `{0xC3, 0xA9}`
- SHA-256({0xC3, 0xA9}) first 16 hex = `"2db7e52e4d32d0c5"` (pre-compute and pin)
**Priority:** P3 — Useful for catching encoding changes; not critical since T1-290 covers the basic
correctness case.

### Issue 2 (P3 — DEBT): T1-264 round-trip does not verify Set operation value

**File:** `Fdp.Core.Tests/Serialization/Migrations/UnknownsJournalTests.cs`
**Problem:** T1-264 verifies Operations[0].Kind and Operations[0].Path but not Operations[0].Value.
The round-trip should also assert that the Value of the Set operation is preserved through
Serialize/Deserialize (e.g., `ops[0].Value!.GetValue<int>() == 99`).
**Priority:** P3 — Minor coverage gap; Serialize/Deserialize of values is implicitly tested by
the ApplyTo tests, but a direct assertion in T1-264 would make it self-contained.

### Issue 3 (P3 — DEBT): DomDiffer array granularity limitation undocumented

**File:** `Fdp.Core/Serialization/Migrations/Internal/Diff/DomDiffer.cs`
**Problem:** DomDiffer treats arrays as monolithic leaf DiffValues (compares JSON serialization as
a string), so DiffToJournalConverter cannot produce `[N]` indexed paths from natural DomDiffer
output. The design implies per-element array diffing is possible. In practice this is acceptable
(entity dictionaries are the real use case, not arrays), but the limitation should be noted in a
code comment on DomDiffer.Diff for future maintainers.
**Priority:** P3 — No user impact in current usage patterns.

---

## Suggested Commit Message

```
feat: add pipeline spec tests, DiffToJournalConverter, UnknownsJournal, HashUtilities (BATCH-03)

Fixes debt items D-005..D-010 (MigrationPipeline spec test gaps).
Implements JM-P1-008 (DiffToJournalConverter, UnknownsJournal, HashUtilities).

Corrective fixes:
- D-005: T1-123 MigrateToCurrent_PreservesEngineVersionField added
- D-006: T1-124 MigrateToCurrent_PreservesCreatedUtcField added
- D-007: T1-125 MigrateToCurrent_PreservesCreatedByField added
- D-008: T1-129 MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3 added
- D-009: T1-136 MigrateTo_NoPathExists_Throws added
- D-010: T1-138 duration assertion changed from >= to > TimeSpan.Zero

JM-P1-008:
- HashUtilities.ComputeContentHash: SHA-256 first 16 hex chars (lowercase)
- JournalOpKind (Set/Remove) + JournalOperation (record)
- DiffToJournalConverter: walks DiffNode tree, emits flat JournalOperation list
  with canonical JSONPaths; array-index segment supported
- UnknownsJournal: Compute/Serialize/Deserialize/ApplyTo; two-pass apply order
  (all Set first, then all Remove, per design 02 S7)
- JsonPathParser.Build(IEnumerable<object>) overload added

Tests: 154/154 migration tests pass; 0 build warnings.
Known P3 debt: D-011 (T1-293 weak hash), D-012 (T1-264 value round-trip), D-013 (DomDiffer array comment).
```
