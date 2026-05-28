# BATCH-04: IMigrationStorage + InMemoryMigrationStorage + FileSystemMigrationStorage

**Batch Number:** BATCH-04
**Tasks:** Corrective D-011/D-012/D-013 (P3 debt), JM-P1-009, JM-P1-010
**Phase:** Phase 1 — Core infrastructure
**Estimated Effort:** 12-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-03 completed and committed

---

## Developer Role

Your role is described in `.github\skills\developer\SKILL.md`. Read it before starting.

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/json-migration/ONBOARDING.md`
2. **Previous review:** `.dev/json-migration/reviews/BATCH-03-REVIEW.md` — Corrective Task 0 below fixes D-011/D-012/D-013.
3. **Task Details:** `.dev/json-migration/TASK-DETAILS.md` — sections JM-P1-009 and JM-P1-010.
4. **Design — Wire formats doc 02 §3** (`Migration-system.md`): Sidecar directory layout (`.migration-snapshots/`).
5. **Design — Wire formats doc 02 §4** (`Migration-system.md`): Snapshot filename convention.
6. **Design — Wire formats doc 02 §5** (`Migration-system.md`): Journal filename convention (already read for JM-P1-008, review §5.2 sidecar path).
7. **Design — Interfaces doc 03 §5.1** (`Migration-system.md`): `IMigrationStorage` interface — read all method contracts and their remarks.
8. **Design — Interfaces doc 03 §5.2** (`Migration-system.md`): `FileSystemMigrationStorage` — atomic write protocol.
9. **Design — Interfaces doc 03 §5.3** (`Migration-system.md`): `InMemoryMigrationStorage` — helper methods for tests.
10. **Design — Test plan doc 06 §3.11** (`Migration-system.md`): T1-310..T1-335.
11. **Design — Test plan doc 06 §5** (`Migration-system.md`): T3-001..T3-008.

### Source Code Locations

- **Implementation:** `FDP/Engine/Fdp.Core/Serialization/Migrations/`
- **Test project:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/`
- **Foundation types (already implemented):** `SnapshotEntry.cs`, `SidecarFileInfo.cs`, `SidecarKind.cs` (read these to understand the types)

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
`.dev/json-migration/reports/BATCH-04-REPORT.md`

---

## Context

BATCH-03 delivered DiffToJournalConverter, UnknownsJournal, and HashUtilities. BATCH-04 builds the
storage layer: the interface that abstracts sidecar I/O, an in-memory implementation for unit tests,
and the real filesystem implementation. These are required before the adapters (JM-P1-011, JM-P1-012).

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

0. **Corrective Task 0 (D-011/D-012/D-013):** Fix 3 minor issues → **ALL existing tests pass** ✅
1. **JM-P1-009:** Define IMigrationStorage + implement InMemoryMigrationStorage → Write T1-310..T1-335 → **all pass** ✅
2. **JM-P1-010:** Implement FileSystemMigrationStorage → Write T3-001..T3-008 → **all pass** ✅

**DO NOT** move to the next step until current step's tests are all green.

---

## ✅ Tasks

---

### Corrective Task 0: Fix D-011, D-012, D-013

#### D-011 (P3): Pin exact expected hash in T1-293

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/Internal/HashUtilitiesTests.cs`
**Fix:** In `ComputeContentHash_Utf8Bytes_NotPlatformDependent`, add an assertion for the known expected hash:
- Input: `"\u00e9"` (U+00E9 = e with acute, UTF-8 = {0xC3, 0xA9})
- SHA-256({0xC3, 0xA9}) first 16 hex (lowercase) = `"4a99557e4033c353"` (verified at runtime)
- Add: `Assert.Equal("4a99557e4033c353", HashUtilities.ComputeContentHash("\u00e9"));`

Replace the existing weaker `Assert.Equal(hash, HashUtilities.ComputeContentHash("\u00e9"))` stability check with this pinned value.

#### D-012 (P3): Verify round-tripped Value in T1-264

**File:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/UnknownsJournalTests.cs`
**Fix:** In `Serialize_RoundTripsThroughDeserialize`, after the existing assertions, add:
```csharp
Assert.Equal(99, restored.Operations[0].Value!.GetValue<int>());
```
The test uses `MakeLossyPair()` which has `"c":99` in pre and not in post, so the journal has `Set("$.c", 99)`. The value must survive the roundtrip.

#### D-013 (P3): Document DomDiffer array granularity limitation

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/Internal/Diff/DomDiffer.cs`
**Fix:** Add a comment to the `Diff` method documenting the array-as-leaf limitation:

```csharp
// NOTE: Arrays are compared as monolithic leaf DiffValues (by JSON serialization),
// not element-by-element. This means DiffToJournalConverter will not produce
// array-indexed [N] paths from natural DomDiffer output. The [N] path form is
// supported by DiffToJournalConverter and JsonPathParser, but is only exercisable
// via manually-constructed DiffNode trees (see T1-246). In the current use case
// (entity dictionaries keyed by GUID), this is not a limitation.
```

Place this comment on or immediately above the array handling block:
```csharp
// Arrays: if both are arrays, compare their JSON serializations as a unit.
if (a is JsonArray aArr && b is JsonArray bArr)
```

---

### Task 1: JM-P1-009 — IMigrationStorage + InMemoryMigrationStorage

**Design ref:** `TASK-DETAILS.md` section `JM-P1-009`. Design doc 03 §5.1 and §5.3.

#### 1A: Define IMigrationStorage

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/IMigrationStorage.cs`

**IMPORTANT accessibility note:** The design spec shows `public interface IMigrationStorage`, but
`UnknownsJournal` (used in `WriteJournalAsync` / `FindJournalAsync`) is `internal sealed`. This
combination would cause a compiler error ("inconsistent accessibility"). Resolve by making the
interface **`internal`** — all implementations (`InMemoryMigrationStorage`,
`FileSystemMigrationStorage`) live in the same assembly, and `MigrationBootstrap` (JM-P1-013, a
future task) will also be in the same assembly. This is the correct design resolution.

```csharp
internal interface IMigrationStorage
{
    Task<string?> ReadOriginalAsync(string originalPath, CancellationToken ct = default);

    Task WriteOriginalAsync(string originalPath, string content,
        CancellationToken ct = default);

    Task WriteSnapshotAsync(string originalPath, int sourceVersion,
        string contentHash, string content, CancellationToken ct = default);

    Task<SnapshotEntry?> FindBestSnapshotAsync(string originalPath, int maxVersion,
        CancellationToken ct = default);

    Task WriteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default);

    Task<UnknownsJournal?> FindJournalAsync(string originalPath,
        string sourceContentHash, CancellationToken ct = default);

    Task DeleteJournalAsync(string originalPath, UnknownsJournal journal,
        CancellationToken ct = default);

    Task<IReadOnlyList<SidecarFileInfo>> ListSidecarsAsync(string originalPath,
        CancellationToken ct = default);

    Task DeleteSidecarAsync(string originalPath, string sidecarFileName,
        CancellationToken ct = default);
}
```

Contracts per design doc 03 §5.1:
- `ReadOriginalAsync`: returns null if the file does not exist (do NOT throw).
- `WriteOriginalAsync`: atomic write (temp-and-move for FileSystem; dict update for InMemory).
- `WriteSnapshotAsync`: creates sidecar directory if needed. Filename: `{base}.v{N}.{hash16}.snapshot.json`.
- `FindBestSnapshotAsync`: picks highest version ≤ maxVersion. MUST verify hash (hash the content, compare to filename hash). Hash mismatch → throw `MigrationException`. Returns null if none found.
- `WriteJournalAsync`: rejects empty operation lists (`ArgumentException` as defense-in-depth). Filename: `{base}.v{N}.{hash16}.unknowns.json` where N = SourceFileVersion and hash = SourceContentHash.
- `FindJournalAsync`: returns null if no matching hash found. On mismatch between journal body's `sourceContentHash` and the filename → throw `MigrationException`.
- `DeleteJournalAsync`: no-op if the file does not exist (idempotent).
- `ListSidecarsAsync`: enumerate by filename only — no content reading. Filter by `originalBaseName`. Returns `IReadOnlyList<SidecarFileInfo>`.
- `DeleteSidecarAsync`: takes the sidecar filename (not the full path), deletes from the sidecar directory of `originalPath`. No-op if missing.

#### 1B: Sidecar directory and filename conventions

From design doc 02 §3:
- Sidecar directory: `{directory-of-original}/.migration-snapshots/`
- Snapshot filename: `{originalBaseName}.v{N}.{hash16}.snapshot.json`
  - `originalBaseName` = `Path.GetFileNameWithoutExtension(originalPath)`
  - N = `sourceVersion`
  - hash16 = the `contentHash` param (first 16 hex of SHA-256 of original content)
- Journal filename: `{originalBaseName}.v{N}.{hash16}.unknowns.json`
  - N = `journal.SourceFileVersion`
  - hash16 = `journal.SourceContentHash`

Helper function (can be a private/internal static):
```csharp
static string GetSidecarDirectory(string originalPath)
    => Path.Combine(Path.GetDirectoryName(originalPath)!, ".migration-snapshots");

static string GetSnapshotFileName(string originalPath, int version, string hash16)
    => $"{Path.GetFileNameWithoutExtension(originalPath)}.v{version}.{hash16}.snapshot.json";

static string GetJournalFileName(string originalPath, int version, string hash16)
    => $"{Path.GetFileNameWithoutExtension(originalPath)}.v{version}.{hash16}.unknowns.json";
```

#### 1C: Implement InMemoryMigrationStorage

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/InMemoryMigrationStorage.cs`

The in-memory implementation stores everything in dictionaries. It does NOT do real filesystem I/O.

Required internal state:
```csharp
private readonly Dictionary<string, string> _originals = new();
private readonly Dictionary<string, string> _sidecars = new();
// _sidecars is keyed by the full sidecar path (or a synthetic key like
// GetSidecarDirectory(orig) + "/" + filename). Use the same helper as
// FileSystemMigrationStorage to compute paths so the two implementations
// use the same filenames.
```

Required test-helper methods (these are `internal` and visible to the test project via `InternalsVisibleTo`):
```csharp
public void Seed(string originalPath, string content);
public void SeedSnapshot(string originalPath, int sourceVersion, string content);
public bool HasSnapshot(string originalPath, int sourceVersion);
public bool HasJournal(string originalPath, string sourceContentHash);
public string? ReadCurrent(string originalPath);
```

`SeedSnapshot` must compute the contentHash from `content` (call `HashUtilities.ComputeContentHash(content)`) so the filename is correct and `FindBestSnapshotAsync` can find it.

`FindBestSnapshotAsync` must verify the hash: hash the stored content and compare to the hash embedded in the filename. If mismatch, throw `MigrationException`.

`FindJournalAsync` must:
1. Find a sidecar file with the matching `sourceContentHash` in its filename (journal extension).
2. Call `UnknownsJournal.Deserialize` on the content (this validates docType/schemaVersion).
3. Verify the journal body's `sourceContentHash` matches the hash embedded in the filename. If mismatch, throw `MigrationException`.

`ListSidecarsAsync` must parse filenames to populate `SidecarFileInfo`:
- Parse `{base}.v{N}.{hash16}.snapshot.json` or `{base}.v{N}.{hash16}.unknowns.json`
- Only return entries where base matches the `originalBaseName` of the given `originalPath`
- `SidecarFileInfo` constructor: `(FileName, Kind, Version, ContentHash)`

`WriteJournalAsync` must throw `ArgumentException` if `journal.Operations.Count == 0`.

#### 1D: Tests T1-310..T1-335

**Test file:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/InMemoryMigrationStorageTests.cs`

Create tests using `InMemoryMigrationStorage` directly. Each test uses standard Arrange-Act-Assert.
Use the `Seed`, `HasSnapshot`, `HasJournal`, `ReadCurrent` helpers in assertions.

Test-specific notes:

- **T1-314** (`WriteSnapshotAsync_CreatesSidecarDirectory`): InMemory doesn't have real directories. Test that after `WriteSnapshotAsync`, `HasSnapshot` returns true. The "sidecar directory" is virtual.
- **T1-315** (`WriteSnapshotAsync_FilenameFollowsConvention`): After `WriteSnapshotAsync`, call `ListSidecarsAsync` and verify the returned `SidecarFileInfo.FileName` follows the `{base}.v{N}.{hash16}.snapshot.json` pattern.
- **T1-320** (`FindBestSnapshotAsync_MultipleSnapshots_ReturnsHighest`): Seed 3 snapshots at v1, v2, v3. Call `FindBestSnapshotAsync(maxVersion: 3)` → returns v3 entry.
- **T1-321** (`FindBestSnapshotAsync_HashMismatch_Throws`): For InMemoryMigrationStorage, you need to inject a corrupted snapshot. Add a `SeedCorruptSnapshot(string originalPath, int version, string fakeHash, string content)` method to the test helper, or use reflection/internal access to corrupt the stored sidecar directly.
- **T1-322** (`WriteJournalAsync_EmptyOperations_ThrowsArgumentException`): Call `WriteJournalAsync` with a journal that has 0 operations. Assert `ArgumentException`.
- **T1-326** (`FindJournalAsync_CorruptJournalEnvelope_Throws`): Seed a sidecar that has the right filename pattern but its JSON content has the wrong `$meta.docType`. `FindJournalAsync` should throw `MigrationException`.
- **T1-327** (`FindJournalAsync_InconsistentHashInsideJournal_Throws`): Seed a sidecar where the filename hash (`{hash16}`) does not match the `sourceContentHash` inside the journal JSON body. `FindJournalAsync` should throw `MigrationException`.

For T1-321, T1-326, T1-327 to work, `InMemoryMigrationStorage` needs a way to seed corrupted data. Add internal test-only helpers:
```csharp
// Seeds a raw sidecar directly by filename (for corruption tests)
internal void SeedRawSidecar(string originalPath, string fileName, string rawContent);
```

For T1-322, build a journal via `UnknownsJournal.Compute` on identical DOMs (returns empty operations), then call WriteJournalAsync — but wait, the test could also build the journal via Deserialize of JSON with an empty operations array.

Actually, the cleanest approach for T1-322: create a journal with 0 operations by calling `UnknownsJournal.Deserialize` with a JSON that has `"operations":[]`. This avoids depending on `Compute`.

---

### Task 2: JM-P1-010 — FileSystemMigrationStorage

**Design ref:** `TASK-DETAILS.md` section `JM-P1-010`. Design doc 02 §3-§5, doc 03 §5.2.

**File:** `FDP/Engine/Fdp.Core/Serialization/Migrations/FileSystemMigrationStorage.cs`

`FileSystemMigrationStorage` is an `internal sealed` class (same accessibility as the interface) that implements `IMigrationStorage` using real filesystem I/O.

Key implementation points:

**Atomic write protocol** (for WriteOriginalAsync, WriteSnapshotAsync, WriteJournalAsync):
```csharp
var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 8);
try
{
    await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, ct);
    File.Move(tempPath, targetPath, overwrite: true);
}
catch
{
    try { File.Delete(tempPath); } catch { /* best effort */ }
    throw;
}
```

**WriteSnapshotAsync**: create the sidecar directory with `Directory.CreateDirectory` (idempotent). Compute the snapshot filename from `originalPath`, `sourceVersion`, `contentHash`.

**FindBestSnapshotAsync**: scan the sidecar directory for files matching `{base}.v*.*.snapshot.json`. Parse version from filename. Select highest version ≤ maxVersion. Read content. Verify hash: `HashUtilities.ComputeContentHash(content) == parsedHash`. If mismatch, throw `MigrationException`. Return `SnapshotEntry`.

**FindJournalAsync**: scan for `{base}.v*.{sourceContentHash}.unknowns.json`. Read content. Deserialize. Verify body `sourceContentHash` matches filename hash. Throw `MigrationException` on mismatch.

**ListSidecarsAsync**: enumerate `.migration-snapshots/` directory. Filter by base name. Parse filename. No content reading. Return `IReadOnlyList<SidecarFileInfo>`.

**DeleteSidecarAsync**: delete the file at `{sidecarDir}/{fileName}`. No-op if missing (catch `FileNotFoundException`).

**Error handling**: Wrap `IOException` in `MigrationException` for operations that should not fail on a healthy filesystem (e.g., write failures). For "file not found" in reads, return null per the interface contract (do NOT throw).

#### T3 tests (FileSystemMigrationStorage)

**Test file:** `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs`

Use `Path.GetTempPath()` + `Guid.NewGuid().ToString("N")` for isolated directories. Clean up in `Dispose` (implement `IDisposable` in a test fixture class).

| ID | Test | Implementation hint |
|---|---|---|
| T3-001 | `FullCycle_RealFiles_RoundTripsLosslessly` | Write an original file, write snapshot, read snapshot back, verify content matches original. Use `WriteOriginalAsync` + `WriteSnapshotAsync` + `FindBestSnapshotAsync`. |
| T3-002 | `AtomicWrite_PowerFailure_DoesNotCorruptOriginal` | Write a file, then simulate an interrupted write by leaving a `.tmp.*` file next to it, then verify the original is still readable and correct. (Full process-kill simulation is impractical in a unit test; test the "no orphaned temp files after exception" path instead: make WriteOriginalAsync fail by supplying a read-only directory, verify temp file is cleaned up.) |
| T3-003 | `ConcurrentReads_SameFile_DoNotInterfere` | Two parallel `ReadOriginalAsync` calls on the same file path must both return the correct content. |
| T3-004 | `WriteSnapshot_CreatesSidecarDirectory_WithCorrectLayout` | After `WriteSnapshotAsync`, verify `.migration-snapshots/` directory exists alongside the original. |
| T3-005 | `Sidecar_FilenameParseable_ByListSidecars` | After `WriteSnapshotAsync`, call `ListSidecarsAsync` and verify the returned `SidecarFileInfo` has correct `Kind`, `Version`, `ContentHash`. |
| T3-006 | `MissingSidecarDirectory_ListSidecars_ReturnsEmpty` | Call `ListSidecarsAsync` when no sidecar directory exists → returns empty list (no throw). |
| T3-007 | `ReadLockedFile_FailsGracefully` | Skip with `Skip` attribute on non-Windows (`[FactAttribute(Skip = "Windows-only")]`). On Windows, lock a file with `FileShare.None`, call `ReadOriginalAsync`, assert it throws `MigrationException` (wrapping IOException). |
| T3-008 | `FileSystemStorage_BehaviorMatchesInMemoryStorage` | Parity test: Run the same set of operations on both `FileSystemMigrationStorage` and `InMemoryMigrationStorage`. Verify that readable outputs (FindBestSnapshot, ReadOriginal, FindJournal, HasSnapshot/HasJournal) are equivalent. |

**For T3-008 specifically**: Create a helper method that runs a fixed sequence:
1. Seed/WriteOriginal with content "original v1"
2. WriteSnapshot (version=1)
3. WriteJournal (with non-empty ops)
4. FindBestSnapshot (maxVersion=1) → verify content matches
5. FindJournal (contentHash) → verify operations count matches
6. DeleteJournal → verify HasJournal returns false
7. ListSidecars → verify only snapshot entry remains

Run this sequence against both storage implementations and assert equivalent results.

**Also** run the InMemory tests T1-310..T1-335 against `FileSystemMigrationStorage` by creating a parameterized test base class or a separate test class that repeats the same tests with `FileSystemMigrationStorage` instead:

```csharp
// Option: inherit test class
public sealed class FileSystemMigrationStorageParityTests : InMemoryMigrationStorageTests
{
    protected override IMigrationStorage CreateStorage() => new FileSystemMigrationStorage();
    // ...cleanup temp dir
}
```

---

## 🧪 Testing Requirements

**Corrective Task 0**: 3 minor changes; all existing 154 tests must still pass after each fix.

**JM-P1-009** (T1-310..T1-335 — 26 tests):
- Test quality: each test must assert on specific values, not just "does not throw".
- T1-320: verify the returned `SnapshotEntry.Version == 3` (not just non-null).
- T1-321: verify a `MigrationException` is thrown (not just any exception).
- T1-324: verify returned journal's `SourceDocType` and `Operations.Count`.
- T1-327: verify `MigrationException` thrown (not null return).
- T1-332: verify all three `SidecarFileInfo` fields (`Kind`, `Version`, `ContentHash`).

**JM-P1-010** (T3-001..T3-008 — 8 tests):
- T3-002: test the "temp file is cleaned up on exception" behavior. Do NOT rely on process kill simulation.
- T3-007: must be skipped on non-Windows (use xUnit's `[Fact(Skip = "Windows-only")]` or an OS check).
- T3-008: parity test must cover at least: Read, WriteSnapshot, FindBestSnapshot, WriteJournal, FindJournal, DeleteJournal, ListSidecars, DeleteSidecar.

---

## 📊 Report Requirements

**Submit to:** `.dev/json-migration/reports/BATCH-04-REPORT.md`

Include:

1. **Completion status** — each corrective fix ✅/❌, JM-P1-009 ✅/❌, JM-P1-010 ✅/❌.
2. **Test results** — exact counts from `dotnet test` output (total, passed, failed, skipped).
3. **Design deviation noted** — `IMigrationStorage` made `internal` due to `UnknownsJournal` being `internal`; note this here.
4. **Design decisions made beyond the spec** — particularly how corruption scenarios (T1-321, T1-326, T1-327) are handled in InMemory vs FileSystem.
5. **Issues encountered** — anything unclear in the design, ambiguities resolved.
6. **Weak points spotted** — anything fragile in the new or existing code.
