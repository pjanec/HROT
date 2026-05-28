# BATCH-02 Report

**Status:** APPROVED — all tasks complete, all tests pass.

---

## Tasks Completed

### Corrective Task 0 (D-001, D-002, D-003)

| Debt | Description | Fix |
|------|-------------|-----|
| D-001 | `JsonEnvelope` swallows parse exceptions for `schemaVersion` field | Wrapped `reader.GetInt32()` in try-catch; re-throws as `MigrationException` |
| D-002 | `IJsonDocumentMigrator.Direction` redundant property | Removed from interface, registry check, and `StubMigrator` |
| D-003 | `MigrationReport.AddWarning(string)` was `public` | Changed to `internal` |

Tests after corrective fixes: **94/94 pass** (all pre-existing migration tests).

---

### JM-P1-006: MigrationPipeline

**Files created:**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationPipeline.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/TestMigrators.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationPipelineTests.cs`

**Implementation summary:**
- `MigrationPipeline(MigrationRegistry)` constructor
- `MigrateToCurrent(JsonObject root, string? sourcePath)` — resolves current version, delegates to `MigrateTo`
- `MigrateTo(JsonObject root, int targetVersion, string? sourcePath)` — builds migration chain, applies each step, enforces 4 invariants per step:
  1. `$meta` object identity unchanged
  2. `$meta.docType` unchanged
  3. `$meta.schemaVersion` not modified by migrator
  4. `engineVersion`, `createdBy`, `createdUtc` unchanged
- Sets `$meta.schemaVersion` after each step
- Wraps non-`MigrationException` exceptions from migrators

**Tests:** T1-120..T1-139 — **20/20 pass**

---

### JM-P1-007: DomDiffer Extraction

**Approach:** Conversion pattern — Core has internal diff types; ComponentDiffService converts Core → Toolkit types.

**Files created:**
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/DiffNode.cs` — internal mirrors of `DiffNode`, `DiffObject`, `DiffValue`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/DomDiffer.cs` — static `DomDiffer.Diff(JsonNode?, JsonNode?, string, double)`, returns null when trees are identical
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/DomDifferTests.cs` — T1-220..T1-229

**Files modified:**
- `FDP/Engine/Fdp.Core/Fdp.Core.csproj` — added `<InternalsVisibleTo Include="Fdp.Toolkits" />`
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Diff/ComponentDiffService.cs` — rewrote `ComputeDiff` to call `DomDiffer.Diff` and convert via private `ConvertNode` helper; `BuildAllModified*` helpers retained

**Invariants preserved:**
- `IComponentDiffService` interface unchanged
- Toolkit `DiffNode`/`DiffObject`/`DiffValue` classes unchanged
- `ComputeDiff` signature unchanged

**Tests:** T1-220..T1-229 — **10/10 pass**

---

## Test Summary

| Suite | Tests | Pass | Fail |
|-------|-------|------|------|
| Pre-existing migration tests (T1-001..T1-119) | 94 | 94 | 0 |
| MigrationPipeline (T1-120..T1-139) | 20 | 20 | 0 |
| DomDiffer (T1-220..T1-229) | 10 | 10 | 0 |
| **Total** | **124** | **124** | **0** |

---

## Build Verification

| Project | Warnings | Errors |
|---------|----------|--------|
| `Fdp.Core` | 0 | 0 |
| `Fdp.Toolkits` | 0 | 0 |
| `Fdp.Core.Tests` | 0 | 0 |

---

## Quality Checklist

- [x] `T1-010` asserts `Assert.Throws<MigrationException>` (not `ThrowsAny`)
- [x] `IJsonDocumentMigrator` no longer has `Direction` property
- [x] `MigrationReport.AddWarning(string)` is `internal`
- [x] All 4 pipeline invariants enforced (T1-130..T1-133)
- [x] `DomDiffer` in `Fdp.Core.Serialization.Migrations.Internal`
- [x] Toolkits still compiles with 0 warnings/errors
- [x] All T1-220..T1-229 pass
- [x] No unicode characters introduced in new files
- [x] No unnecessary comments added to unchanged code
