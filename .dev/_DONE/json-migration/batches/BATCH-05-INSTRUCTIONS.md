# BATCH-05: ReadOnlyMigrationAdapter + ReadOnlyLoadOutcome

**Batch Number:** BATCH-05
**Tasks:** Corrective D-014/D-015/D-016 (P3 debt), JM-P1-011
**Phase:** Phase 1 — Core infrastructure
**Estimated Effort:** 6-10 hours
**Priority:** HIGH
**Dependencies:** BATCH-04 completed and committed

---

## Developer Role

Your complete workflow is described in `.github\skills\developer\SKILL.md`. Read it before starting.

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/json-migration/ONBOARDING.md`
2. **Previous review:** `.dev/json-migration/reviews/BATCH-04-REVIEW.md` — Corrective Task 0 below fixes D-014/D-015/D-016.
3. **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — section JM-P1-011.
4. **Design — Interfaces doc 03 §7.1** (`Migration-system.md`): `ReadOnlyMigrationAdapter` and `ReadOnlyLoadOutcome` — read the load sequence and both overloads.
5. **Design — Test plan doc 06 §4.1** (`Migration-system.md`): T2-001..T2-010.
6. **Review context for fast path:** `MigrationPipeline.MigrateToCurrent` and `JsonEnvelope.Peek` already exist (BATCH-01/02). `ReadOnlyMigrationAdapter` wraps them.

### Source Code Locations

- **New files (this batch):**
  - `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs`
  - `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyMigrationAdapter.cs`
- **Tests:**
  - `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/ReadOnlyMigrationAdapterTests.cs` (new)
  - `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs` (corrective edits)

### Existing Types to Understand

Read these before implementing (they are already complete):
- `MigrationPipeline` — public `MigrateToCurrent(JsonObject dom)` (returns MigrationReport), has internal access to `_registry.GetCurrentVersion(docType)`
- `MigrationRegistry` — `GetCurrentVersion(docType)` returns int
- `JsonEnvelope.Peek(Stream)` — reads $meta from stream
- `JsonEnvelope.Peek(string path)` — reads $meta from file path
- `DocumentMeta` — readonly record struct with DocType, SchemaVersion, EngineVersion, CreatedBy, CreatedUtc

### Build & Test Commands

```powershell
# Build
dotnet build FDP/Engine/Fdp.Core/Fdp.Core.csproj

# Run migration tests only
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --filter "FullyQualifiedName~Migrations"

# Full regression
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/json-migration/reports/BATCH-05-REPORT.md`

---

## Context

BATCH-04 delivered the storage layer (`IMigrationStorage`, `InMemoryMigrationStorage`,
`FileSystemMigrationStorage`). BATCH-05 delivers the first adapter: `ReadOnlyMigrationAdapter`,
the cluster's fast-path migration adapter that never writes sidecar files. This is the simpler of
the two adapters (no storage dependency) and is a natural prerequisite for understanding the more
complex `PersistentMigrationAdapter` in BATCH-06.

---

## MANDATORY WORKFLOW

0. **Corrective Task 0 (D-014/D-015/D-016):** Fix 3 minor issues → **ALL existing 188 tests pass** ✅
1. **JM-P1-011:** Implement ReadOnlyLoadOutcome + ReadOnlyMigrationAdapter → Write T2-001..T2-010 → **all pass** ✅

---

## Tasks

---

### Corrective Task 0: Fix D-014, D-015, D-016

#### D-014 (P3): Extend T3-008 parity test to cover journal operations

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs`

T3-008 (`FileSystemStorage_BehaviorMatchesInMemoryStorage`) currently only covers:
WriteOriginal, ReadOriginal, WriteSnapshot, FindBestSnapshot, ListSidecars.

**Add** to the existing T3-008 test body (after the ListSidecars assertions):
1. Build a lossy journal using `UnknownsJournal.Compute` on two different DOM states (the same
   `MakeLossyPair()` helper pattern used in `InMemoryMigrationStorageTests` works here).
2. Call `WriteJournalAsync` on both `_storage` (filesystem) and `memStorage` (in-memory).
3. Call `FindJournalAsync` with the source hash on both → assert both return non-null, same
   `Operations.Count`.
4. Call `DeleteJournalAsync` on both → call `FindJournalAsync` again → assert both return null.
5. Add a snapshot and call `DeleteSidecarAsync` by filename on both → call `ListSidecarsAsync`
   → assert both return the same count.

You will need to define a `MakeLossyPair()` helper in `FileSystemMigrationStorageTests.cs` (copy
the pattern from `InMemoryMigrationStorageTests.cs`).

#### D-015 (P3): Replace early return with explicit Skip in T3-007

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs`

In `ReadLockedFile_FailsGracefully`, replace the early return:
```csharp
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    return; // not testable on non-Windows
```
with an explicit xUnit skip:
```csharp
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    throw new Xunit.SkipException("File locking behavior is Windows-only.");
```
Or use the `[Fact(Skip = "...")]` approach if the test does nothing on non-Windows (no runtime condition needed when skipped unconditionally). Since the test only runs on Windows, use a conditional `Skip` attribute:

```csharp
[SkippableFact]
public async Task ReadLockedFile_FailsGracefully()
{
    Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
    ...
}
```

If `SkippableFact` from the `Xunit.SkippableFact` NuGet package is not available in the project, use the simpler approach: make it a Windows-only fact with a direct `Skip` attribute:

```csharp
[Fact(Skip = "File locking test is Windows-only; skipped on this platform")]
```

But that skips on ALL platforms including Windows. A better simple alternative is to use an early-exit with `Assert.Skip`:

```csharp
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    Assert.Skip("File locking test is Windows-only.");
    return;
}
```

Check if `Assert.Skip` is available in the xUnit version used in this project. If not, add a NuGet reference to `Xunit.SkippableFact` or use `[Fact(Skip = "...")]` unconditionally and add a comment explaining it should only be run on Windows CI.

**NOTE:** After making this change, the test count may change (the test may now show as `Skipped` instead of `Passed` on non-Windows). If running on Windows, it should still pass.

#### D-016 (P3): Correct hash value comment in BATCH-04-INSTRUCTIONS.md

**File:** `.dev/json-migration/batches/BATCH-04-INSTRUCTIONS.md`

In the D-011 section, change:
```
- SHA-256({0xC3, 0xA9}) first 16 hex (lowercase) = `"2db7e52e4d32d0c5"` (pre-verified)
- Add: `Assert.Equal("2db7e52e4d32d0c5", HashUtilities.ComputeContentHash("\u00e9"));`
```
to:
```
- SHA-256({0xC3, 0xA9}) first 16 hex (lowercase) = `"4a99557e4033c353"` (verified at runtime)
- Add: `Assert.Equal("4a99557e4033c353", HashUtilities.ComputeContentHash("\u00e9"));`
```

---

### Task 1: JM-P1-011 — ReadOnlyMigrationAdapter

**Design ref:** `TASK-DETAILS.md` section `JM-P1-011`. Design doc 03 §7.1, doc 06 §4.1.

#### 1A: ReadOnlyLoadOutcome

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs`

```csharp
namespace Fdp.Core.Serialization.Migrations.Adapters;

/// <summary>
/// The result of <see cref="ReadOnlyMigrationAdapter.LoadAndMigrateAsync"/>.
/// When <see cref="WasMigrated"/> is false, <see cref="RawContent"/> carries the
/// file content without DOM allocation. When true, <see cref="MigratedDom"/>
/// is the migrated DOM.
/// </summary>
public sealed class ReadOnlyLoadOutcome
{
    // Read-only properties; set by the adapter.
    public DocumentMeta Meta { get; init; }
    public bool WasMigrated { get; init; }
    public string? RawContent { get; init; }
    public JsonObject? MigratedDom { get; init; }
    public MigrationReport? Report { get; init; }

    /// <summary>
    /// Returns a parsed JsonObject regardless of which path was taken.
    /// On the fast path, parses RawContent (allocates a DOM).
    /// On the slow path, returns MigratedDom directly.
    /// </summary>
    public JsonObject AsJsonObject()
    {
        if (MigratedDom is not null)
            return MigratedDom;
        if (RawContent is not null)
            return JsonNode.Parse(RawContent)!.AsObject();
        throw new InvalidOperationException(
            "ReadOnlyLoadOutcome has neither RawContent nor MigratedDom.");
    }

    /// <summary>
    /// Returns the JSON text regardless of which path was taken.
    /// On the slow path, serializes MigratedDom.
    /// </summary>
    public string AsJsonString()
    {
        if (RawContent is not null)
            return RawContent;
        if (MigratedDom is not null)
            return MigratedDom.ToJsonString();
        throw new InvalidOperationException(
            "ReadOnlyLoadOutcome has neither RawContent nor MigratedDom.");
    }
}
```

#### 1B: ReadOnlyMigrationAdapter

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyMigrationAdapter.cs`

**IMPORTANT:** `ReadOnlyMigrationAdapter` does NOT take `IMigrationStorage` in its constructor.
It never writes sidecar files. Its only dependency is `MigrationPipeline`.

```csharp
public sealed class ReadOnlyMigrationAdapter
{
    private readonly MigrationPipeline _pipeline;

    public ReadOnlyMigrationAdapter(MigrationPipeline pipeline);

    public async Task<ReadOnlyLoadOutcome> LoadAndMigrateAsync(
        string path,
        CancellationToken ct = default);

    public async Task<ReadOnlyLoadOutcome> LoadAndMigrateAsync(
        Stream stream,
        string sourceId,
        CancellationToken ct = default);
}
```

**Load sequence (file path overload):**

1. Read all file bytes: `byte[] utf8 = await File.ReadAllBytesAsync(path, ct)`.
2. `DocumentMeta meta = JsonEnvelope.Peek(utf8.AsSpan())` — streaming peek, no DOM.
3. Check `meta.SchemaVersion == _pipeline.GetCurrentVersion(meta.DocType)`:
   - **Fast path** (equal): Return `ReadOnlyLoadOutcome { Meta=meta, WasMigrated=false, RawContent=System.Text.Encoding.UTF8.GetString(utf8), MigratedDom=null, Report=null }`.
   - **Slow path** (not equal): Parse DOM (`JsonNode.Parse(utf8)!.AsObject()`), call `_pipeline.MigrateToCurrent(dom)`, return `ReadOnlyLoadOutcome { Meta=meta, WasMigrated=true, RawContent=null, MigratedDom=dom, Report=report }`.
4. If `File.Exists(path)` is false before step 1, throw `MigrationException($"File not found: {path}")`.
5. Wrap `IOException` in `MigrationException` as needed.

**Note on `GetCurrentVersion`:** Check if `MigrationPipeline` exposes a method or property to get the current registered version for a docType. If not, call `_pipeline.MigrateTo(dom, int.MaxValue)` does not work. Instead:
- Check `MigrationRegistry.GetCurrentVersion(docType)` — the pipeline may expose the registry. 
- Look at the actual `MigrationPipeline` API to find the right method.
- If `MigrationPipeline` does not expose a `GetCurrentVersion` method, add one as an `internal` method. Do NOT add a `public` method; keep the public API surface minimal.

**Load sequence (stream overload):**

1. If `stream.CanSeek`: seek to position 0. Otherwise: read entire stream into `MemoryStream`.
2. Get a `byte[]` from the seekable stream or MemoryStream.
3. Follow the same steps 2-5 as the file path overload, substituting `sourceId` in exception messages.
4. For non-seekable streams, the buffer read is necessary — no special optimization required.

**Exception cases:**
- File not found → `MigrationException("File not found: {path}")`
- `$meta` missing or malformed → `MigrationException` (already thrown by `JsonEnvelope.Peek`)
- Unknown docType (not registered) → `MigrationException` (thrown by `MigrationPipeline.MigrateToCurrent`)
- Malformed JSON (parse fails) → `MigrationException`

**DO NOT** allocate a `JsonObject` on the fast path. The `MigratedDom` must be null on the fast path, and `RawContent` must be populated.

#### 1C: MigrationPipeline extension (if needed)

If `MigrationPipeline` does not expose a way to check the current version for a docType without actually migrating, add an `internal` method. Read `MigrationPipeline.cs` carefully before deciding. Common pattern: `internal int GetCurrentVersion(string docType)` that delegates to `_registry.GetCurrentVersion(docType)`.

---

### Task 2: Tests T2-001..T2-010

**Test file:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/ReadOnlyMigrationAdapterTests.cs`

#### Test fixture setup

Use `InMemoryMigrationStorage` for storage (the ReadOnlyMigrationAdapter doesn't use storage, but the test helpers need it to create test documents).

Define a fixed test registry with `"Test.Doc"` registered at currentVersion=2, with a v1→v2 migrator that adds a field. Use the existing `StubMigrator` and `MigratorFactory` helpers from the test assembly:

```csharp
// Test helper: build a registry with Test.Doc at version N
private static MigrationPipeline BuildPipeline(int currentVersion)
{
    var registry = new MigrationRegistry();
    // Register "Test.Doc" migrators up to currentVersion
    registry.RegisterDocType("Test.Doc", currentVersion, ...);
    return new MigrationPipeline(registry);
}
```

Use `JsonEnvelope.Write` to build test JSON documents with the correct `$meta` envelope, or build raw JSON strings manually. The key is that each test document must have:
```json
{
  "$meta": { "docType": "Test.Doc", "schemaVersion": N },
  "field1": "value"
}
```

#### Test implementation notes

| ID | Test | Key assertions |
|----|------|----------------|
| T2-001 | `LoadAndMigrate_AtCurrentVersion_FastPath_NoMigration` | `WasMigrated==false`, `RawContent!=null`, `MigratedDom==null`, content matches original |
| T2-002 | `LoadAndMigrate_OlderVersion_SlowPath_Migrates` | `WasMigrated==true`, `MigratedDom!=null`, `RawContent==null`, `Meta.SchemaVersion==currentVersion` |
| T2-003 | `LoadAndMigrate_NoSidecarWritten` | After LoadAndMigrate, no sidecar write operations occur. This is guaranteed by the design (no storage dep), but test it by verifying `InMemoryMigrationStorage.HasSnapshot` and `HasJournal` return false if you pass a storage instance. Since the adapter has no storage dep, simply verify that the test does not create any files — this is trivially true. The test can be a smoke test: call LoadAndMigrate twice (same file), verify results are consistent. |
| T2-004 | `LoadAndMigrate_AsJsonObject_FastPath_AllocatesOnDemand` | On fast path, call `outcome.AsJsonObject()` → returns a parsed `JsonObject`. Verify the field values are correct. |
| T2-005 | `LoadAndMigrate_AsJsonString_SlowPath_SerializesDom` | On slow path (WasMigrated=true), call `outcome.AsJsonString()` → returns a JSON string. Parse it and verify correct schema version. |
| T2-006 | `LoadAndMigrate_StreamInput_WorksIdentically` | Build a `MemoryStream` from the same test document. Call the stream overload. Verify same `WasMigrated` value and same content as file overload. |
| T2-007 | `LoadAndMigrate_NonSeekableStream_BuffersAndProcesses` | Wrap a `MemoryStream` in a non-seekable wrapper. Call stream overload. Verify `WasMigrated` and content. |
| T2-008 | `LoadAndMigrate_FileNotFound_Throws` | Pass a non-existent path → Assert `MigrationException`. |
| T2-009 | `LoadAndMigrate_UnknownDocType_Throws` | Build a document with `docType: "Unknown.Doc"` (not registered). Call LoadAndMigrate → Assert `MigrationException`. |
| T2-010 | `LoadAndMigrate_MalformedEnvelope_Throws` | Build a document without `$meta`. Call LoadAndMigrate → Assert `MigrationException`. |

**For T2-003:** Since the adapter has no storage parameter, the "no sidecar written" guarantee is structural (by design). The test should document this with a comment and verify the fast-path invariant: call on a file at current version twice, both return `WasMigrated=false` with identical `RawContent`.

**For T2-006 / T2-007:** To test the stream overload without a real file, write the test JSON to a `MemoryStream`. For T2-007's non-seekable stream, create a wrapper class:

```csharp
private sealed class NonSeekableStream(Stream inner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

**For T2-009:** Write a document with a known good `$meta` shape but `docType: "Unknown.Doc"`. The adapter peeks the envelope (succeeds), then checks the current version (throws because doc type is not registered). The test expects `MigrationException`.

---

## Testing Requirements

**Corrective Task 0**: All existing 188 tests must still pass after the D-014/D-015 changes. D-015's change might cause T3-007 to show as "Skipped" on non-Windows — this is acceptable and the total count (Passed+Skipped) should add up correctly.

**JM-P1-011** (T2-001..T2-010 — 10 tests):
- T2-001: **MUST** assert `MigratedDom==null` (not just `WasMigrated==false`). The fast path must not allocate a DOM.
- T2-002: **MUST** assert `RawContent==null` (slow path has no raw content).
- T2-004: **MUST** verify the `AsJsonObject()` return value contains expected field data (not just non-null).
- T2-005: **MUST** verify that `AsJsonString()` returns valid JSON at the current schema version.
- T2-009: **MUST** throw `MigrationException` (not just any exception).

---

## Report Requirements

**Submit to:** `.dev/json-migration/reports/BATCH-05-REPORT.md`

Include:
1. Completion status for each task.
2. Test results (`dotnet test --filter`) — exact pass/fail/skip counts.
3. Whether `GetCurrentVersion` needed to be added to `MigrationPipeline` and what you added.
4. Any design deviations or ambiguities resolved.
5. Weak points noticed.
