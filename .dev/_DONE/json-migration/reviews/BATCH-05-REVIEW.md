# BATCH-05 Review

**Batch:** BATCH-05  
**Reviewer:** Dev Lead  
**Date:** 2025-05-29  
**Verdict:** APPROVED

---

## Summary

BATCH-05 delivered:
- **JM-P1-011**: `ReadOnlyLoadOutcome` + `ReadOnlyMigrationAdapter` (new files in `Adapters/` subfolder)
- **D-014**: Extended T3-008 to cover WriteJournal, FindJournal, DeleteJournal, DeleteSidecar parity
- **D-015**: Replaced T3-007 early-return with `[SkippableFact]` / `Skip.IfNot` via `Xunit.SkippableFact` 1.4.13
- **D-016**: Corrected hash in BATCH-04-INSTRUCTIONS.md

Build: **0 errors, 7 pre-existing xUnit2013 warnings** (all from prior batches).  
Tests: **198/198 migration tests pass** (0 skipped, 0 failed).

---

## Code Review

### ReadOnlyLoadOutcome.cs

Design-aligned. The two-branch model (`RawContent` on fast path, `MigratedDom` on slow path) is correct and matches design doc 03 §7.1.

`AsJsonObject()` and `AsJsonString()` are correct dual-dispatch helpers — both branches handled, `InvalidOperationException` thrown if both are null (unreachable in practice but correct defensively). `init` accessors match the record-like pattern mandated by the design.

### ReadOnlyMigrationAdapter.cs

Implementation matches the design specification:
- **Fast path**: `JsonEnvelope.Peek(utf8.AsSpan())` → version comparison → returns `RawContent` without DOM allocation.
- **Slow path**: `JsonNode.Parse` + `MigrateToCurrent` + `JsonEnvelope.Read(dom)` to capture the updated `SchemaVersion` in `Meta`.
- **Error handling**: `MigrationException` propagated on file-not-found, IO error, JSON parse failure, and unknown docType (via `GetCurrentVersion` → `GetEntry` → throw).
- **No storage dependency**: constructor takes only `MigrationPipeline`. Structurally guarantees no sidecar writes.

One minor observation: the seekable-stream branch and the non-seekable-stream branch in `LoadAndMigrateAsync(Stream, ...)` both do the same thing (`CopyToAsync` to a `MemoryStream`). The `CanSeek` check and `Seek(0, Begin)` for seekable streams is a pre-caution to reset position — acceptable, but the `using var ms = new MemoryStream()` could be unified. This is a non-blocking stylistic note, not a defect.

### MigrationPipeline.cs — GetCurrentVersion addition

`internal int GetCurrentVersion(string docType)` at line 149 correctly delegates to `_registry.GetCurrentVersion(docType)`. Clean minimal addition. The `internal` visibility is appropriate — `ReadOnlyMigrationAdapter` is in the same assembly.

### D-014: T3-008 parity extension

T3-008 now covers all 9 `IMigrationStorage` methods in the `FileSystemStorage`/`InMemoryStorage` behavioral parity comparison:
1. WriteOriginalAsync / ReadOriginalAsync
2. WriteSnapshotAsync / FindBestSnapshotAsync
3. ListSidecarsAsync
4. **WriteJournalAsync / FindJournalAsync** (new)
5. **DeleteJournalAsync** (new)
6. **DeleteSidecarAsync** (new)

The `MakeLossyPair()` helper builds a pre/post `JsonObject` pair with at least one removed property so `UnknownsJournal.Compute` produces a non-empty journal. This correctly exercises the journal write/find/delete round-trip.

The `DeleteSidecarAsync` sub-section is correct: it reads the sidecar list before deletion, asserts it is non-empty, deletes, then re-asserts that both stores now have matching (empty) lists.

### D-015: T3-007 SkippableFact

`[SkippableFact]` + `Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "...")` is the correct pattern for the Xunit.SkippableFact package. On Linux/macOS the test is reported as Skipped rather than silently passing. On Windows (where the test ran in this batch) it runs and passes.

### D-016: BATCH-04-INSTRUCTIONS.md hash correction

Hash `2db7e52e4d32d0c5` → `4a99557e4033c353`. Documentation-only fix. Consistent with D-011 (the same correct hash was established in BATCH-03 review).

---

## Test Quality Assessment

| Test | Coverage quality |
|------|-----------------|
| T2-001 | Fast path (already at current version) — asserts WasMigrated=false, RawContent=full content, MigratedDom=null, Report=null, content byte-identical. **Good.** |
| T2-002 | Slow path (older version) — asserts WasMigrated=true, MigratedDom!=null, RawContent=null, Meta.SchemaVersion==currentVersion. **Good.** |
| T2-003 | No-sidecar structural guarantee via idempotency smoke test. Acceptable approach given no storage ref; structurally provable. |
| T2-004 | AsJsonObject on fast path — verifies on-demand DOM allocation + correct field values. **Good.** |
| T2-005 | AsJsonString on slow path — verifies serialized DOM re-parses to correct schemaVersion. **Good.** |
| T2-006 | Stream overload parity with file overload. **Good.** |
| T2-007 | Non-seekable stream — custom `NonSeekableStream` wrapper correctly blocks `CanSeek`. **Good.** |
| T2-008 | File not found → MigrationException. **Good.** |
| T2-009 | Unknown docType → MigrationException. **Good.** |
| T2-010 | Malformed envelope (no `$meta`) → MigrationException. **Good.** |

Tests are aligned with the design. No fake tests, no trivially-passing assertions.

---

## Issues Found

**None blocking.** One P3 observation:

| ID | Description | Action |
|----|-------------|--------|
| — | The seekable/non-seekable branches in `LoadAndMigrateAsync(Stream)` are functionally identical (both do `CopyToAsync`). The `Seek(0, Begin)` for seekable streams is the only difference. Minor code smell, no defect. | No debt item; can be cleaned up opportunistically. |

---

## Debt Resolution

| ID | Status |
|----|--------|
| D-014 | RESOLVED — T3-008 now covers all 9 IMigrationStorage methods |
| D-015 | RESOLVED — T3-007 uses [SkippableFact] / Skip.IfNot |
| D-016 | RESOLVED — BATCH-04-INSTRUCTIONS.md hash corrected |

---

## Decision

**APPROVED.** JM-P1-011 (ReadOnlyMigrationAdapter GATE) is complete.  
Proceed to commit and then create BATCH-06 for JM-P1-012 (PersistentMigrationAdapter + Round-Trip Diff GATE).
