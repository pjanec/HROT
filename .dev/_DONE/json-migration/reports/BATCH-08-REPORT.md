# BATCH-08 Report — Coverage Closure

## Objective

Close ALL coverage gaps so every migration-namespace class reaches:
- **Line coverage >= 90%**
- **Branch coverage >= 85%**

## Final Results

All 350 migration tests pass (0 failed, 0 skipped). All classes meet thresholds.

### Coverage Table (after BATCH-08)

| Class | Line | Branch | Status |
|---|---|---|---|
| `FileSystemMigrationStorage` (class body) | 0.9534 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<ReadOriginalAsync>d__0` | 1.0000 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<FindBestSnapshotAsync>d__3` | 0.9714 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<FindJournalAsync>d__5` | 0.9677 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<WriteSnapshotAsync>d__2` | 1.0000 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<WriteJournalAsync>d__4` | 1.0000 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<WriteOriginalAsync>d__1` | 1.0000 | 1.0000 | PASS |
| `FileSystemMigrationStorage/<AtomicWriteAsync>d__9` | 1.0000 | 1.0000 | PASS |
| `MigrationBootstrap` | 1.0000 | 1.0000 | PASS |
| `InMemoryMigrationStorage` | 0.9324 | 0.8846 | PASS |
| `UnknownsJournal` | 0.9931 | 0.8589 | PASS |
| `JsonEnvelope` | 0.9788 | 0.9014 | PASS |
| `JsonPathApplicator` | 0.9519 | 0.8673 | PASS |
| `JsonPathParser` | 0.9738 | 0.8900 | PASS |
| `MigrationRegistry` | 0.9032 | 0.8709 | PASS |
| `MigrationRegistry/DocTypeEntry` | 0.9090 | 1.0000 | PASS |
| `MigrationContext` | 0.9523 | 1.0000 | PASS |
| `MigrationReport` | 0.9473 | 1.0000 | PASS |
| `PersistentMigrationAdapter/<LoadAndMigrateAsync>d__5` | 0.9797 | 0.8571 | PASS |
| `PersistentMigrationAdapter/<SaveAsync>d__6` | 1.0000 | 0.9166 | PASS |
| `ReadOnlyMigrationAdapter` | 1.0000 | 1.0000 | PASS |
| `ReadOnlyLoadOutcome` | 1.0000 | 1.0000 | PASS |
| `DiffToJournalConverter` | 0.9555 | 0.9642 | PASS |
| `DomDiffer` | 1.0000 | 0.9318 | PASS |
| `MigrationPipeline` | 1.0000 | 0.9333 | PASS |
| All other migration classes | >= 0.9000 | >= 0.8500 | PASS |

## Tests Added

Starting count: ~332 tests (session start). Final count: **350 tests**.

| ID | Test | Covers |
|---|---|---|
| T1-281 | `UnknownsJournal` optional-fields FALSE branches | `UnknownsJournal` branch gap |
| T2-018..T2-021 | `ReadOnlyMigrationAdapter` paths | `ReadOnlyMigrationAdapter` coverage |
| T2-108 | `ReadEngineVersion_AssemblyWithAttribute_ReturnsVersionString` | `MigrationBootstrap.ReadEngineVersion` happy path |
| T2-109 | `ReadEngineVersion_AssemblyWithoutAttribute_ReturnsUnknown` | `MigrationBootstrap.ReadEngineVersion` `?? "unknown"` fallback |
| T3-016 | `FindBestSnapshotAsync` multiple snapshots / `version <= bestVersion` | `FindBestSnapshotAsync` skip-lower-version branch |
| T3-017 | `ReadOriginalAsync_LockedFile_ThrowsMigrationException` (Windows) | `ReadOriginalAsync` IOException catch |
| T3-018 | `FindBestSnapshotAsync_NoSidecarDirectory_ReturnsNull` | Early-exit `return null` when sidecar dir absent |
| T3-019 | `FindJournalAsync_NoSidecarDirectory_ReturnsNull` | Early-exit `return null` when sidecar dir absent |
| T3-020 | `FindBestSnapshotAsync_HashMismatch_ThrowsMigrationException` | Hash-mismatch throw in snapshot loop |
| T3-021 | `FindJournalAsync_HashMismatch_ThrowsMigrationException` | Hash-mismatch throw in journal loop |
| T3-022 | `ListSidecarsAsync_WithSnapshotAndJournal_ReturnsBoth` | `else if TryParseJournalFileName` branch in `ListSidecarsAsync` |
| T3-023 | `FindBestSnapshotAsync_LockedSnapshotFile_ThrowsMigrationException` (Windows) | IOException catch in snapshot read loop |
| T3-024 | `FindJournalAsync_LockedJournalFile_ThrowsMigrationException` (Windows) | IOException catch in journal read loop |
| T3-025 | `DeleteJournalAsync_LockedFile_ThrowsMigrationException` (Windows) | IOException catch in `DeleteJournalAsync` |
| T3-026 | `DeleteSidecarAsync_LockedFile_ThrowsMigrationException` (Windows) | IOException catch in `DeleteSidecarAsync` |

## Source Changes

### `FileSystemMigrationStorage.cs` — `ReadOriginalAsync` TOCTOU fix

Removed `if (!File.Exists(originalPath)) return null;` pre-check. The method now
relies solely on exception handling:

```csharp
// BEFORE:
if (!File.Exists(originalPath))
    return null;
try { ... }
catch (FileNotFoundException) { return null; }   // unreachable in tests (no race)

// AFTER (pre-check removed):
try { ... }
catch (FileNotFoundException) { return null; }   // now testable — non-existent path throws FNFE
```

**Rationale:** The pre-check introduced a TOCTOU window (file could be deleted between the
`Exists` check and `ReadAllTextAsync`). Removing it makes the `FileNotFoundException` catch
testable with any non-existent path, while preserving identical observable behavior.

### `MigrationBootstrap.cs` — `ReadEngineVersion` extraction

Extracted the assembly version lookup from `BuildForProduction` into a new
`internal static string ReadEngineVersion(System.Reflection.Assembly assembly)` method.
The `?? "unknown"` fallback was previously unreachable because the production assembly
always carries `AssemblyInformationalVersionAttribute`.

The extracted method can be tested independently using `AssemblyBuilder.DefineDynamicAssembly`
to create a bare assembly with no attributes, exercising the `?? "unknown"` branch.

## Key Techniques

### Windows-only locked-file tests (`[SkippableFact]`)

Exception-catch blocks in `ReadAllTextAsync` and `File.Delete` calls cannot be triggered
in a portable way. Tests lock the target file with `FileShare.None` before calling the
method, forcing an `IOException`. These tests use `Xunit.SkippableFact` v1.4.13 with
`Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))`.

### Hash-mismatch tests

`FindBestSnapshotAsync` and `FindJournalAsync` validate that the content hash matches
the hash embedded in the filename. Tests create sidecar files manually with `"0000000000000000"`
in the filename while the body contains a different hash, triggering the integrity-check throw.

For journals the body is produced by writing a real journal (body hash = `"aabbccdd11223344"`),
copying its bytes to a file named with a different hash, then searching with that fake hash.

### `AssemblyBuilder` for untestable `?? "unknown"` branch

`System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly` creates a synthetic assembly
with no attributes. Passing it to `ReadEngineVersion` exercises the `?.InformationalVersion ?? "unknown"`
null-coalesce branch, which is never reachable with the real production assembly.

## Files Changed

| File | Change |
|---|---|
| `FDP/Engine/Fdp.Core/Serialization/Migrations/FileSystemMigrationStorage.cs` | Removed `File.Exists` pre-check from `ReadOriginalAsync` |
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Bootstrap/MigrationBootstrap.cs` | Extracted `internal static ReadEngineVersion(Assembly)` method |
| `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/FileSystemMigrationStorageTests.cs` | Added T3-016..T3-026 (11 tests); added `using System.Linq` |
| `FDP/Engine/Fdp.Core.Tests/Serialization/Migrations/MigrationBootstrapTests.cs` | Added T2-108, T2-109; added `using System.Reflection`, `using System.Reflection.Emit` |
| Various test files | T1-281, T2-018..T2-021 added earlier in batch |
