# BATCH-33 — Phase 7: IRefactorService + AtomicMultiFileWriter

## Tasks to implement
- **TASK-S7-01**: `IRefactorService` core (find/preview/apply pipeline)
- **TASK-S7-02**: `AtomicMultiFileWriter` (temp-file + rename batch write)

## Existing infrastructure to read first

Before writing any code, read these files to understand the context:

1. `Hrot/Editor/Hrot.Editor.AiShared/References/IReferenceCatalog.cs` — FindReferences API
2. `Hrot/Editor/Hrot.Editor.AiShared/References/AssetReference.cs` — reference record
3. `Hrot/Editor/Hrot.Editor.AiShared/References/IAssetSubElement.cs` — sub-element interface
4. `Hrot/Editor/Hrot.Editor.AiShared/References/SubElementKind.cs` — enum
5. `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` — FindByAssetId
6. `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` — SourceFilePath
7. `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` — project structure
8. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` — test project structure
9. `Hrot/Editor/Hrot.Editor.AiShared.Tests/References/ReferenceCatalogTests.cs` — test pattern

## Files to create

### 1. `Hrot/Editor/Hrot.Editor.AiShared/Refactor/IRefactorService.cs`

The shared layer's single entry point for refactor operations.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Editor.AiShared.Refactor;

public interface IRefactorService
{
    // ---- Read-only queries ----
    IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey);
    IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId);

    // ---- Mutation: previewed, then applied ----
    RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options);
    RefactorResult ApplyRename(RefactorPreview preview);

    DeletePreview PreviewDelete(Guid assetId, DeleteOptions options);
    RefactorResult ApplyDelete(DeletePreview preview);

    // ---- Async variants ----
    Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default);
    Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default);
}

public sealed record RefactorOptions(
    bool IncludeBlueprint = true,
    bool IncludeBTree = true,
    bool IncludeHsm = true,
    bool DryRunOnly = false);

public sealed record DeleteOptions(
    bool AllowDanglingReferences = false);

public sealed record RefactorPreview(
    string FromKey,
    string ToKey,
    IReadOnlyList<RefactorFileEdit> Edits,
    IReadOnlyList<RefactorIssue> Issues);

public sealed record DeletePreview(
    Guid AssetId,
    IReadOnlyList<AssetReferenceInfo> DanglingReferences,
    IReadOnlyList<RefactorIssue> Issues);

public sealed record RefactorFileEdit(
    string FilePath,
    Guid HostAssetId,
    IReadOnlyList<RefactorLineEdit> LineEdits);

public sealed record RefactorLineEdit(
    int LineNumber,
    string OriginalText,
    string ReplacementText,
    string ContextDescription);

public sealed record RefactorIssue(
    RefactorIssueSeverity Severity,
    string Description,
    Guid? RelatedAssetId);

public enum RefactorIssueSeverity { Info, Warning, Error }

public sealed record RefactorResult(
    bool Success,
    IReadOnlyList<string> WrittenFiles,
    string? FailureReason);

// Thin wrapper over AssetReference that includes the resolved SourceFilePath.
public sealed record AssetReferenceInfo(
    Guid HostAssetId,
    AssetKind HostKind,
    Guid HostElementId,
    string HostDisplayPath,
    string TargetKey,
    SubElementKind TargetKind,
    string SourceFilePath);
```

Notes:
- Import `Hrot.Editor.AiShared.References` for `AssetKind`, `SubElementKind`
- Import `Hrot.Editor.AiShared` for `AssetKind`
- `AssetReferenceInfo` is a richer version of `AssetReference` that adds `SourceFilePath`

### 2. `Hrot/Editor/Hrot.Editor.AiShared/Refactor/AtomicMultiFileWriter.cs`

Temp-file + rename batch writer.

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Hrot.Editor.AiShared.Refactor;

public sealed class AtomicMultiFileWriter
{
    public AtomicWriteResult Write(IReadOnlyDictionary<string, string> filePathToContent)
    {
        // 1. Write each file to a temp path in the same directory.
        var tempFiles = new List<(string TempPath, string FinalPath)>();
        foreach (var (finalPath, content) in filePathToContent)
        {
            var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, System.Text.Encoding.UTF8);
                tempFiles.Add((tempPath, finalPath));
            }
            catch (Exception ex)
            {
                // Roll back all temp files written so far.
                foreach (var (t, _) in tempFiles)
                    TryDelete(t);
                TryDelete(tempPath);
                return new AtomicWriteResult(false, Array.Empty<string>(), ex.Message);
            }
        }

        // 2. Move all temp files to their final paths (overwrite).
        var written = new List<string>();
        foreach (var (tempPath, finalPath) in tempFiles)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                written.Add(finalPath);
            }
            catch (Exception ex)
            {
                // Partial failure: log but do not roll back already-moved files.
                TryDelete(tempPath);
                return new AtomicWriteResult(false, written.AsReadOnly(), ex.Message);
            }
        }
        return new AtomicWriteResult(true, written.AsReadOnly(), null);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }
}

public sealed record AtomicWriteResult(
    bool Success,
    IReadOnlyList<string> SuccessfullyWritten,
    string? FailureReason);
```

### 3. `Hrot/Editor/Hrot.Editor.AiShared/Refactor/RefactorService.cs`

Concrete implementation of `IRefactorService`.

**Constructor signature:**
```csharp
public sealed class RefactorService : IRefactorService
{
    public RefactorService(
        IReferenceCatalog referenceCatalog,
        IAssetCatalog assetCatalog,
        AtomicMultiFileWriter writer)
    { ... }
}
```

**`FindReferences(targetKey)`:**
1. Call `_referenceCatalog.FindReferences(targetKey)` → list of `AssetReference`
2. For each, resolve `SourceFilePath` via `_assetCatalog.FindByAssetId(ref.HostAssetId)?.SourceFilePath ?? string.Empty`
3. Return as `IReadOnlyList<AssetReferenceInfo>`

**`FindReferencesInAsset(hostAssetId)`:**
1. Call `_referenceCatalog.AllReferencesIn(hostAssetId)` → list of `AssetReference`
2. Resolve `SourceFilePath` the same way
3. Return as `IReadOnlyList<AssetReferenceInfo>`

**`PreviewRename(fromKey, toKey, options)`:**
1. `var references = _referenceCatalog.FindReferences(fromKey)` 
2. Filter references by `options` (skip HostKind == Blueprint if `!options.IncludeBlueprint`, etc.)
3. Group by unique `(HostAssetId, SourceFilePath)` pairs
4. For each group:
   a. Get `SourceFilePath` from catalog; if empty, add a Warning issue and skip
   b. If file doesn't exist, add a Warning issue and skip
   c. Read all lines with `File.ReadAllLines(path)`
   d. For each line (1-based index), check if it contains `fromKey` (use `string.Contains`)
   e. Create `RefactorLineEdit(lineNum, origLine, origLine.Replace(fromKey, toKey), contextDesc)`
   f. Collect into `RefactorFileEdit(path, hostAssetId, lineEdits)`
5. Check if `toKey` already exists in the catalog (collision warning)
6. Return `RefactorPreview(fromKey, toKey, fileEdits, issues)`

Notes on filtering by options:
- `AssetKind.Blueprint` → check `options.IncludeBlueprint`
- `AssetKind.BTree` → check `options.IncludeBTree`
- `AssetKind.Hsm` → check `options.IncludeHsm`

Context description for line edit: use `$"Reference in {hostAssetKind} asset at line {lineNum}"`

**`ApplyRename(preview)`:**
1. If `preview.Issues` has any Error-level issue, return failure
2. For each `RefactorFileEdit`:
   a. Read the current file content as lines (or start from the lines in `RefactorLineEdit.OriginalText`)
   b. For each `RefactorLineEdit`, replace the line at `LineNumber - 1` (0-based) with `ReplacementText`
   c. Join lines back to string (use `string.Join(Environment.NewLine, lines)`)
3. Call `_writer.Write(filePathToContent dict)`
4. If write fails, return failure
5. Return success

**`PreviewDelete(assetId, options)`:**
1. Get all references from `FindReferencesInAsset` for all assets that reference `assetId`
   - Actually: find all references where `TargetKey` matches the asset's key or AssetId string
   - Simplification: use `_referenceCatalog.AllElements` to find the sub-element with `SourceAssetId == assetId`
   - For each such sub-element, call `FindReferences(element.Key)` to get dangling refs
2. Classify as auto-resolvable vs critical (for Slice 1, all are auto-resolvable)
3. Return `DeletePreview(assetId, danglingRefs, issues)`

**`ApplyDelete(preview)`:**
1. If preview has Error issues and not `AllowDanglingReferences`, return failure (get options from DeletePreview)
   - Actually, the `DeleteOptions` is not stored in `DeletePreview`; use a simpler approach: if there are Error-level issues in `preview.Issues`, refuse
2. Find the asset by `preview.AssetId` in the catalog
3. Delete the file with `File.Delete(asset.SourceFilePath)` (if it exists)
4. Return success

**Async variants:**
```csharp
public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default)
    => Task.Run(() => PreviewRename(fromKey, toKey, options), ct);

public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default)
    => Task.Run(() => ApplyRename(preview), ct);
```

### 4. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/AtomicMultiFileWriterTests.cs`

Test the atomic writer with real temp files.

```
Tests:
1. Write_empty_dictionary_succeeds_with_no_written_files
2. Write_single_file_creates_file_with_correct_content
3. Write_multiple_files_creates_all_files_with_correct_content
4. Write_overwrites_existing_file
5. Write_returns_success_true_on_success
6. Write_to_invalid_path_returns_failure
7. Write_to_invalid_path_does_not_leave_temp_files_behind
```

Use `Path.GetTempPath()` + `Path.GetRandomFileName()` for temp file paths. Clean up in `IDisposable` or directly in the test.

### 5. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/RefactorServiceTests.cs`

Test the refactor service with in-memory fakes + temp files.

**Fake implementations needed:**
- `FakeReferenceCatalog : IReferenceCatalog` — holds a list of `AssetReference` items added via `AddReference(AssetReference)`
- `FakeAssetCatalog : IAssetCatalog` — holds a dict of `Guid → IEditableAsset`; simple `FakeAsset` record
- `FakeAsset : IEditableAsset` — minimal: `AssetId`, `Name`, `SourceFilePath`, other members default/stub

**Tests:**
1. `FindReferences_returns_empty_when_no_references`
2. `FindReferences_returns_matching_references_with_source_path`
3. `FindReferencesInAsset_returns_references_for_host_asset`
4. `PreviewRename_empty_catalog_returns_empty_edits`
5. `PreviewRename_finds_key_in_source_file_and_creates_line_edit`
   - Create a temp file with content `var x = "action://Foo";`
   - Add a reference with `TargetKey = "action://Foo"` pointing to that asset
   - Call `PreviewRename("action://Foo", "action://Bar", default)`
   - Verify one `RefactorFileEdit` with one `RefactorLineEdit` where `ReplacementText` contains `"action://Bar"`
6. `PreviewRename_skips_reference_when_source_file_missing`
7. `PreviewRename_respects_IncludeBTree_false_option`
8. `ApplyRename_writes_modified_files`
9. `ApplyRename_returns_failure_when_file_write_fails`
10. `PreviewDelete_returns_dangling_references`

Note: For `ApplyRename` tests, actually write content to temp files, call `ApplyRename`, then verify the file content was updated.

## IMPORTANT RULES

1. No Unicode in comments (ASCII only per AGENTS.md)
2. TreatWarningsAsErrors is active — no CS0067 (event not used), no unused variables
3. All types live in `Hrot.Editor.AiShared.Refactor` namespace
4. Tests live in `Hrot.Editor.AiShared.Tests.Refactor` namespace
5. `AssetReferenceInfo` is a new type — do NOT confuse with `AssetReference` (existing type)
6. `AssetKind` is in `Hrot.Editor.AiShared` (not References sub-namespace)
7. The `SubElementKind` and `AssetReference` types are in `Hrot.Editor.AiShared.References`
8. Build must pass: 0 errors, 0 warnings

## Existing test count
- AiShared.Tests currently has 139 passing tests
- After this batch: should have ~149+ tests

## Build commands
```
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
dotnet build Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
```
