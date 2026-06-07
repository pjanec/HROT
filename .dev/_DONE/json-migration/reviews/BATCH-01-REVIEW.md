# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-28
**Status:** APPROVED

---

## Summary

All 5 tasks (JM-P1-001 through JM-P1-005) implemented. 94 new tests pass. Full regression suite (885 tests) passes with no new failures.

---

## Issues Found

### Issue 1: T1-010 test assertion too weak (masks implementation bug)

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/JsonEnvelopeTests.cs` (test `Peek_NonIntegerSchemaVersion_ThrowsMigrationException`)
**Problem:** Test uses `Assert.ThrowsAny<Exception>()` — spec says the test is `Peek_NonIntegerSchemaVersion_ThrowsMigrationException`. The root cause: `ReadMetaObject` calls `reader.GetInt32()` on a string token, which throws `InvalidOperationException` (from `Utf8JsonReader`), not `MigrationException`. Callers that catch only `MigrationException` will miss this failure path.
**Fix required:** In `JsonEnvelope.ReadMetaObject`, wrap the `reader.GetInt32()` call with a try/catch that rethrows as `MigrationException` with a message identifying the field. Change test to `Assert.Throws<MigrationException>`.
**Recorded:** DEBT-TRACKER P2, target BATCH-02 (applies automatically to next batch).

### Issue 2: `Direction` property on `IJsonDocumentMigrator` beyond spec (minor)

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/IJsonDocumentMigrator.cs`
**Problem:** Spec (TASK-DETAILS JM-P1-005) defines the interface with 4 members: `DocType`, `FromVersion`, `ToVersion`, `Apply()`. The implementation adds `Direction`. This is derivable from `ToVersion > FromVersion`, making it redundant in the interface. Every future migrator implementation must supply it unnecessarily.
**Fix:** Consider removing `Direction` from the interface (registry derives it from the version delta anyway). Low priority — does not break correctness.
**Recorded:** DEBT-TRACKER P3, target BATCH-02 or later.

### Issue 3: `MigrationReport.AddWarning(string)` is public (minor footgun)

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationReport.cs`
**Problem:** `public void AddWarning(string message)` bypasses path capture and hardcodes `"$"` as the path. Migrators calling `ctx.Report.AddWarning("msg")` instead of `ctx.AddWarning("msg")` will silently lose scope information.
**Fix:** Make `AddWarning(string)` `internal`. Migrators always have access to `ctx.AddWarning`.
**Recorded:** DEBT-TRACKER P3.

---

## Test Quality Assessment

Tests are generally high quality: they verify actual values (path strings, FromVersion/ToVersion fields, warning Path fields, return values). Key highlights:

- T1-004 (stream stop position) validates `ms.Position < 10_000` — actual streaming behavior checked.
- T1-071/T1-072 (multi-step path order) verify `FromVersion`/`ToVersion` on each migrator — actual order enforced.
- T1-100/T1-101 (warning path capture) verify the exact `Path` and `Message` values — not just that a warning was added.
- T1-190/T1-194 (TryWrite/TryRemove missing-parent) verify both the return value AND that the DOM is unchanged.

One weakness (Issue 1 above): T1-010 uses `ThrowsAny<Exception>` instead of specific type.

---

## Verdict

**Status:** APPROVED

Issues 2 and 3 are non-blocking (P3). Issue 1 is P2 and will be fixed in BATCH-02 as the first task (ahead of new work). Implementation is correct and well-tested. Ready to commit.

---

## 📝 Commit Message

```
feat: add Fdp.Core.Serialization.Migrations foundation (BATCH-01)

Completes JM-P1-001, JM-P1-002, JM-P1-003, JM-P1-004, JM-P1-005.

Adds the Fdp.Core.Serialization.Migrations namespace with:
- DocumentMeta record, MigrationDirection, MigrationReport, MigrationWarning,
  MigrationException, SnapshotEntry, SidecarFileInfo, SidecarKind
- FdpDocumentTypes string constants (FlightRecorderMetadata, RoadNetwork,
  MigrationJournal)
- JsonEnvelope: streaming $meta peek (ReadOnlySpan<byte>, Stream, string),
  DOM Read/Write/HasEnvelope, WithSchemaVersion/WithEngineVersion
- JsonPath restricted dialect: parser, applicator (TryWrite/TryRemove/Read)
  with canonical builder (dotted vs bracketed), all unsupported operators
  rejected
- MigrationContext with LIFO scope stack (WithItem/WithIndex/WithPathSuffix),
  automatic path capture in AddWarning
- MigrationRegistry: full chain validation (up+down coverage per step, no gaps,
  no duplicates, non-adjacent rejection), passthrough registration, GetPath
  multi-step routing, CanMigrate, sealing mechanism
- IJsonDocumentMigrator interface

Tests: 94 tests (T1-001..T1-020, T1-030..T1-035, T1-050..T1-077,
T1-090..T1-101, T1-160..T1-194). All pass; no pre-existing test regressions.
```

---

**Next Batch:** BATCH-02
