# BATCH-05 Report

**Batch:** BATCH-05  
**Developer:** AI Developer  
**Date:** 2025-05-29  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| D-014 | Complete | Added `MakeLossyPair()` helper + journal write/find/delete + DeleteSidecar parity assertions to T3-008 |
| D-015 | Complete | Replaced early return with `[SkippableFact]` + `Skip.IfNot(...)` via `Xunit.SkippableFact` 1.4.13 |
| D-016 | Complete | Corrected hash from `2db7e52e4d32d0c5` to `4a99557e4033c353` in BATCH-04-INSTRUCTIONS.md |
| JM-P1-011 | Complete | `ReadOnlyLoadOutcome`, `ReadOnlyMigrationAdapter`, T2-001..T2-010 all pass |

---

## Testing Results

**Migration Tests (filter `FullyQualifiedName~Migrations`):** 198 / 198 passed, 0 skipped  
**Full suite:** 986 passed, 2 skipped, 3 failed (all 3 failures are pre-existing benchmark/performance tests unrelated to this batch — see notes below)

**Tests added this batch:**
- T2-001..T2-010 in `ReadOnlyMigrationAdapterTests` — all 10 pass
- T3-007 converted from silent early-return to `[SkippableFact]` — passes on Windows (ran on Windows)
- T3-008 extended with journal + DeleteSidecar parity — passes

**Pre-existing failures (not caused by this batch):**

| Test | Failure reason |
|------|---------------|
| `Benchmark_HotPathOptimization` | "Expected significant speedup, got 1.37x" — environment-dependent threshold |
| `Benchmark_SetRawObject_Performance` | Performance threshold, environment-dependent |
| `RealisticMilitarySimulation_CompleteScenario_MeasuresPerformance` | Performance scenario, not migration-related |

These three failures are present on the baseline commit before this batch's changes (confirmed by `git stash` + rebuild).

---

## Files Changed

### New files
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs`
- `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyMigrationAdapter.cs`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/ReadOnlyMigrationAdapterTests.cs`

### Modified files
- `FDP/Engine/Fdp.Core/Serialization/Migrations/MigrationPipeline.cs` — added `internal int GetCurrentVersion(string docType)` delegating to `_registry`
- `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs` — D-014 + D-015 fixes
- `FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj` — added `Xunit.SkippableFact` 1.4.13
- `.dev/json-migration/batches/BATCH-04-INSTRUCTIONS.md` — D-016 hash correction

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`Assert.Skip` not available in xUnit 2.9.3.** The batch instructions suggested `Assert.Skip` for D-015. This method exists in xUnit v3 but NOT v2.x. Since the project uses xUnit 2.9.3, `Assert.Skip` compiled but resolved to nothing — resulting in CS1061 at build time. Resolved by adding `Xunit.SkippableFact` 1.4.13 to the test project (as the instructions suggested as a fallback) and replacing `[Fact]` with `[SkippableFact]` / `Skip.IfNot(...)`.

2. **`MigrationPipeline.GetCurrentVersion` not exposed.** The design called for `ReadOnlyMigrationAdapter` to call `_pipeline.GetCurrentVersion(docType)` to determine whether migration is needed on the fast path. This method did not exist on `MigrationPipeline` in the existing codebase. Added it as `internal int GetCurrentVersion(string docType)` delegating to `_registry.GetCurrentVersion(docType)`, placed before the "Private helpers" region.

3. **Slow-path `Meta` must reflect post-migration version.** On the slow path (migration runs), the `ReadOnlyLoadOutcome.Meta` must carry the updated `SchemaVersion` (= currentVersion), not the pre-migration value from `JsonEnvelope.Peek`. Solution: after `MigrateToCurrent` runs on the DOM, call `JsonEnvelope.Read(dom)` to read the meta back from the mutated DOM. This satisfies T2-002's assertion `Meta.SchemaVersion == currentVersion`.

4. **Windows `tail` not available.** The first build verification attempt piped through `tail -20`. PowerShell on Windows does not have `tail`. Switched to `Select-Object -Last 20` for all subsequent commands.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **`MigrationPipeline.GetCurrentVersion` needed addition.** This should have been part of `MigrationPipeline`'s internal API from the start (BATCH-01/02). The fact that it wasn't caused a compile error during this batch. It's a trivial delegation but ideally should have been included when the registry was wired up.

2. **Pre-existing benchmark tests are flaky.** Three performance/benchmark tests fail consistently on the dev machine due to environment-dependent timing thresholds. These should either be decorated with `[Fact(Skip = "...")]` or have their thresholds relaxed, otherwise they produce spurious CI noise.

3. **`ReadOnlyMigrationAdapter.ProcessBytes` does two passes on the slow path.** The slow path calls `JsonEnvelope.Peek` (stream read), then re-parses as DOM for `JsonNode.Parse`. For very large documents this means two allocations. A single `JsonNode.Parse` + `JsonEnvelope.Read(dom)` on all documents (forgoing the fast path's allocation savings) would be simpler but slower on the common case. The current design is correct per spec.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **`ProcessBytes` helper.** Both `LoadAndMigrateAsync` overloads share a `ProcessBytes(byte[] utf8, string sourceId)` private method. This avoids duplicating the Peek/migrate logic. Alternative: inline in each overload; rejected because it would duplicate ~30 lines.

2. **Non-seekable stream handling.** For the stream overload, the implementation reads all bytes into a `byte[]` first (via `ReadToEnd`/`CopyTo` to MemoryStream), then processes the bytes. This handles non-seekable streams naturally since the buffer doesn't require seeking. T2-007 confirms this with a `NonSeekableStream` wrapper that overrides `CanSeek` to return `false`.

3. **File-not-found checked before read.** `File.Exists(path)` is called before `File.ReadAllBytesAsync` and throws `MigrationException` with a descriptive message if absent. Alternative: let `FileNotFoundException` propagate and catch it inside the try block alongside `IOException`. The explicit check gives a better message.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

1. **Stream overload with file-backed streams.** T2-006 tests that `LoadAndMigrateAsync(stream, sourceId)` works identically to the path overload when given a `FileStream`. This was explicitly in the spec (T2-006), but the implementation needed to ensure `sourceId` was used only for error messages, not for any file lookup.

2. **`AsJsonString()` on slow path serializes the DOM.** When `WasMigrated=true`, `RawContent` is null, and `AsJsonString()` must serialize `MigratedDom`. The implementation uses `MigratedDom.ToJsonString()`. This allocates a new string on each call; if the caller needs the string multiple times they should cache it. No spec requirement to cache, so left as-is.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

1. **`File.ReadAllBytesAsync` for large files.** On the fast path, the file is read to a byte array, then `Encoding.UTF8.GetString(utf8)` allocates a new string. For very large documents, a streaming approach could avoid the intermediate array allocation. Not worth optimizing at this stage (the fast path avoids DOM allocation which is the expensive part).

2. **`JsonEnvelope.Peek` on the fast path reads only the `$meta` object.** This is efficient — it uses `Utf8JsonReader` with no DOM. The fast path returns the raw string without any further JSON processing. Good design.

---

## Outstanding Issues / Next Steps

- [ ] Pre-existing benchmark test failures should be triaged (separate from this batch)
- [ ] `PersistentMigrationAdapter` (BATCH-06) will be the more complex adapter with storage dependency; `ReadOnlyMigrationAdapter` now serves as the reference design for the fast-path pattern
