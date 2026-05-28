# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Development Lead
**Date:** 2026-05-28
**Status:** CHANGES REQUIRED (corrective tasks in BATCH-03)

---

## Summary

Debt fixes D-001/D-002/D-003 applied correctly. `MigrationPipeline` implemented and functional. `DomDiffer` extracted and rewired. 124/124 migration tests pass, 0 build warnings. One critical file-encoding corruption in `ComponentDiffService.cs` was found and fixed directly by the reviewer.

---

## Issues Found

### Issue 1 (CRITICAL — FIXED BY REVIEWER): Mojibake encoding in `ComponentDiffService.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs`
**Problem:** The developer wrote the file back using Windows-1252 interpretation of existing UTF-8 bytes, double-encoding all non-ASCII characters in comments (e.g., `—` → `â€"`, `─` → `â"€`). The AGENTS.md rule "Do not introduce mojibake by changing file encoding" was violated.
**Fix applied:** Reviewer restored the original from git and re-applied only the necessary code changes using the tools, preserving the original UTF-8 encoding.

### Issue 2 (CORRECTIVE): Five spec tests replaced with unrelated tests

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationPipelineTests.cs`
**Problem:** The developer reassigned T1-IDs to different scenarios. The following spec-required tests from `Migration-system.md` doc 06 §3.5 are absent:

| Spec ID | Required test name | Status |
|---------|-------------------|--------|
| T1-123 | `MigrateToCurrent_PreservesEngineVersionField` | MISSING |
| T1-124 | `MigrateToCurrent_PreservesCreatedUtcField` | MISSING |
| T1-125 | `MigrateToCurrent_PreservesCreatedByField` | MISSING |
| T1-129 | `MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3` | MISSING |
| T1-136 | `MigrateTo_NoPathExists_Throws` | MISSING — T1-125 (impl) covers unknown-docType not no-path |

These tests verify important behaviors:
- T1-123/124/125: a normal migration run must not silently discard `engineVersion`, `createdUtc`, `createdBy` from `$meta`.
- T1-129: chain must stop at the failing migrator; the pipeline must not call subsequent migrators after a throw.
- T1-136: `MigrateTo(targetVersion)` for an unreachable version (e.g. v99 on a type with only v1-v3) must throw.

**Fix required:** Add these 5 tests. See corrective task in BATCH-03.

### Issue 3 (CORRECTIVE): T1-138 duration assertion too weak

**File:** `MigrationPipelineTests.cs` — `MigrateTo_WithMigratorsRun_DurationIsPositive`
**Problem:** Uses `report.Duration >= TimeSpan.Zero`. Spec says "Duration is positive" (`> TimeSpan.Zero`).
**Fix:** Change to `Assert.True(report.Duration > TimeSpan.Zero)`.

---

## What Was Verified as Correct

### Corrective fixes (D-001/D-002/D-003)

- D-001: `JsonEnvelope.ReadMetaObject` catches `InvalidOperationException`/`FormatException` from `reader.GetInt32()` and re-throws as `MigrationException`. T1-010 updated to `Assert.Throws<MigrationException>`. ✅
- D-002: `IJsonDocumentMigrator.Direction` removed from interface, registry validation, and `StubMigrator`. ✅
- D-003: `MigrationReport.AddWarning(string)` is `internal`. ✅

### MigrationPipeline (JM-P1-006)

- All 4 invariants are checked post-migrator (identity, docType, schemaVersion, diagnostic fields). ✅
- Invariant messages name the violated field (T1-130: `"docType"`, T1-131: `"$meta"`, T1-132: `"schemaVersion"`). ✅
- Non-`MigrationException` from migrators is wrapped with `docType`/path context. ✅
- `schemaVersion` is set by the pipeline (not the migrator) after each step. ✅
- Passthrough returns empty report without running migrators. ✅
- Multi-step up+down migration transforms the DOM correctly (T1-137 impl). ✅
- Warning path capture via `ctx.WithItem` scope works (T1-139). ✅
- `StubMigrator.ApplyCallCount` preserved for chain-stop tests. ✅

### DomDiffer extraction (JM-P1-007)

- `DiffNode`/`DiffObject`/`DiffValue` placed in `Fdp.Core.Serialization.Migrations.Internal` namespace. ✅
- `DomDiffer.Diff` returns null for identical trees (T1-220). ✅
- OldValue/NewValue are actual JSON strings, not just IsModified flags (T1-221..T1-228). ✅
- 50-level recursion test does not stack-overflow (T1-229). ✅
- `ComponentDiffService.ComputeDiff` delegates to `DomDiffer.Diff` and converts via `ConvertNode`. ✅
- Toolkit public API unchanged; `Fdp.Toolkits` builds with 0 warnings. ✅
- `InternalsVisibleTo Fdp.Toolkits` already existed in `Fdp.Core.csproj`. ✅

---

## Suggested Commit Message

```
feat: add MigrationPipeline, extract DomDiffer, fix BATCH-01 debt (BATCH-02)

Completes JM-P1-006 (MigrationPipeline) and JM-P1-007 (DomDiffer extraction).
Fixes debt items D-001/D-002/D-003.

Debt fixes:
- D-001: JsonEnvelope wraps non-integer schemaVersion as MigrationException;
  T1-010 updated to Assert.Throws<MigrationException>
- D-002: IJsonDocumentMigrator.Direction removed (redundant, registry derives it)
- D-003: MigrationReport.AddWarning(string) made internal

MigrationPipeline:
- MigrateToCurrent / MigrateTo(targetVersion)
- Enforces 4 post-migrator invariants ($meta identity, docType, schemaVersion,
  diagnostic fields); throws MigrationException naming the violated field
- Wraps non-MigrationException migrator failures with context
- Sets $meta.schemaVersion after each step; records Duration

DomDiffer extraction:
- DiffNode/DiffObject/DiffValue/DomDiffer moved to
  Fdp.Core.Serialization.Migrations.Internal
- ComponentDiffService.ComputeDiff now delegates to DomDiffer.Diff; all
  existing Toolkit callers compile unchanged
- Unicode characters in ComponentDiffService.cs preserved correctly

Tests: 30 new tests (T1-120..T1-139, T1-220..T1-229); total 124 migration
tests pass. Known gap: 5 spec tests missing — added to BATCH-03 as corrective.
```

---

## Corrective Tasks for BATCH-03

See BATCH-03-INSTRUCTIONS.md (Corrective Task 0) for full spec.

| # | Test to add | Why important |
|---|-------------|----------------|
| 1 | T1-123 `MigrateToCurrent_PreservesEngineVersionField` | Verify diagnostic fields survive migration |
| 2 | T1-124 `MigrateToCurrent_PreservesCreatedUtcField` | Verify diagnostic fields survive migration |
| 3 | T1-125 `MigrateToCurrent_PreservesCreatedByField` | Verify diagnostic fields survive migration |
| 4 | T1-129 `MigrateToCurrent_MigratorThrowsAtStep2of3_DoesNotRunStep3` | Verify chain stops on first failure |
| 5 | T1-136 `MigrateTo_NoPathExists_Throws` | Verify unreachable version throws |
| 6 | Fix T1-138: `>= TimeSpan.Zero` → `> TimeSpan.Zero` | Weak assertion per spec |

**Next Batch:** BATCH-03
