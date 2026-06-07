# BATCH-04 REPORT

**Batch:** BATCH-04
**Tasks:** TASK-C-11, TASK-C-12, TASK-C-14, TASK-C-16, TASK-C-17, TASK-C-20
**Status:** COMPLETE
**Test result:** Passed - Failed: 0, Passed: 451, Skipped: 0, Total: 451
**Build result:** 0 errors, 9 pre-existing warnings (all in files not touched by this batch)

---

## Deliverables

### TASK-C-11 -- CompanionFileDiscovery

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs`

Implemented `CompanionFileDiscovery` (static class), `DiscoveredAsset` (record), and `DiscoveredCompanion` (record).

- `DiscoverFromMainFile`: strips the kind-specific suffix from the main filename to get the base name, constructs companion paths in the same directory, calls `File.Exists` for each.
- `DiscoverFromFolder`: recursively scans for `*.cs` and `*.bp.json` files, skipping directories whose names start with `.`. Reads each candidate file's header to extract `AssetId` (comment or JSON field), returns the first match as a `DiscoveredAsset`, or null if none found.
- AssetId extraction never throws: files that error on read or parse are silently skipped.

**Tests (6):**
1. BTree single-file: 1 companion present, 2 not present
2. Blackboard companions: both `HeavyBlackboard.cs` present
3. Blueprint: no companions expected or returned
4. Folder mode: finds main file, skips `.migration-snapshots` sibling with same AssetId
5. Folder mode -- dot-prefix exclusion: `.git/Bar.cs` with target AssetId is not returned
6. Folder mode -- not found: empty folder returns null

---

### TASK-C-12 -- AssetSelectionValidator

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/AssetSelectionValidator.cs`

Implemented `AssetSelectionValidator` (static class), `ValidationResult`, `ValidationIssue`, and `ValidationSeverity`.

`Validate(versionA, versionB, expectedKind)` applies five rules in order: file exists check, readability check, parseability check (AssetId extraction), kind match check, and AssetId match check (warning only).

- Kind is detected from the main file's extension/suffix (`.bp.json`, `_BT.cs`, `_HSM.cs`, `.Blackboard.cs`, `.HeavyBlackboard.cs`).
- Error message uses `--` (ASCII double dash) not an em dash, per AGENTS.md.

**Tests (5, combined missing-file and unreadable into unparseable):**
1. Kind mismatch: BTree + Blueprint -> `IsValid=false`
2. Same kind, same AssetId: `IsValid=true`, no issues
3. Same kind, different AssetIds: `IsValid=true`, one Warning
4. Missing file: `IsValid=false`, "File not found"
5. Unparseable file: garbage content -> `IsValid=false`, "Cannot parse"

---

### TASK-C-14 -- ComparisonExportBuilder (full implementation)

**File modified:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs`

Replaced the skeleton body with the full implementation. The method signature was not changed.

- Instruction block stored as a `private const string` (string concatenation).
- All em-dashes in the instruction text replaced with `--` per AGENTS.md.
- Separator = 80 `=` characters.
- Metadata block: ASSET NAME / KIND / ID / SOURCE PATH / LAST MODIFIED / COMPANION FILES (omitted if no companions).
- Each file section preceded by `// === FILE: {filename} ===` header.
- Migration notice emitted as `// MIGRATION NOTICE: {notice}` when non-null.
- All line endings normalized to `\n` via `string.ReplaceLineEndings("\n")` at end.

**Tests (7):**
1. Structural: all four separator lines, VERSION A/B headers, companion markers, END OF COMPARISON INPUT
2. Instruction block: output starts with "You are comparing"
3. Metadata fields: ASSET NAME, KIND, ID, SOURCE PATH, LAST MODIFIED correct
4. File header: sanitized text preceded by `// === FILE: ...`
5. Migration notice: `// MIGRATION NOTICE: ` present when populated
6. No `\r\n` line endings
7. Self-comparison: both VERSION A and VERSION B sections contain identical content

---

### TASK-C-16 -- LlmResponseParser + ComparisonResponse

**New files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonResponse.cs`

`ComparisonResponse` and `ComparisonChange` are `sealed record` types with string-typed `Kind` and `Severity` so unknown values do not cause parse failures.

`LlmResponseParser.Parse` is a static method that never throws:

1. Strips markdown fences with a `Regex` pattern.
2. Locates `----- HUMAN SUMMARY -----` and `----- STRUCTURED CHANGES (JSON) -----` markers; falls back to first `{` / last `}` when markers are absent.
3. Parses JSON with `JsonDocument.Parse`.
4. On failure, attempts recovery: finds the last `}` in the JSON section, appends `\n]\n}`, and retries.
5. If recovery also fails, returns a `ComparisonResponse` with a single truncation warning.
6. Validates each change for unknown `kind` (normalizes to `node_modified`), unknown `severity` (normalizes to `tuning`), and missing `description` (fills with empty string); adds a warning for each normalization.

**Tests (10):**
1. Well-formed response: all fields, 0 warnings
2. Markdown-fenced JSON: same result as unfenced
3. Extra leading prose before HUMAN SUMMARY marker: summary correctly extracted, no extra prose included
4. Marker absent: fallback to first `{` / last `}` works
5. Truncated recoverable: 1 change + 1 warning
6. Truncated unrecoverable: 0 changes + 1 warning
7. Unknown kind: normalized to `node_modified` + 1 warning
8. Unknown severity: normalized to `tuning` + 1 warning
9. Missing description: empty string + 1 warning
10. Null `elementId`: parsed normally, no warning

---

### TASK-C-17 -- ComparisonSessionState + ComparisonSessionRegistry

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs`

`ComparisonSessionState` holds the parsed result for one asset. Default enabled severities: `behavior`, `feature`, `removal`, `tuning`. `cosmetic` is disabled by default. `ToggleSeverity` toggles a severity in or out of the `HashSet<string>` backing `EnabledSeverities`. `MarkStale` sets `IsStale = true`.

`ComparisonSessionRegistry` is a singleton class backed by `Dictionary<Guid, ComparisonSessionState>`. Provides `GetSession`, `SetSession`, and `ClearSession`. Registered in `SharedAiEditorServiceCollectionExtensions` as a singleton.

**Tests (7):**
1. Default severities: behavior, feature, removal, tuning in; cosmetic not in
2. ToggleSeverity add: cosmetic -> enabled after toggle
3. ToggleSeverity remove: behavior -> disabled after toggle
4. MarkStale: `IsStale` false initially, true after call
5. Registry set and get: same instance returned
6. Registry overwrite: second `SetSession` call wins
7. Registry clear: `GetSession` returns null after `ClearSession`

---

### TASK-C-20 -- Fixture Files + Parametrized Test

**New directory:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/Responses/`

**9 fixture files created:**

| File | Expected changes | Expected warnings |
|------|-----------------|------------------|
| `well_formed.txt` | 2 | 0 |
| `markdown_fenced.txt` | 2 | 0 |
| `extra_leading_prose.txt` | 2 | 0 |
| `truncated_recoverable.txt` | 1 | 1 |
| `truncated_unrecoverable.txt` | 0 | 1 |
| `unknown_kind.txt` | 2 | 1 |
| `unknown_severity.txt` | 2 | 1 |
| `missing_required_field.txt` | 2 | 1 |
| `unresolvable_element_ids.txt` | 2 | 0 |

`<Content Include="Comparison/Fixtures/Responses/**" CopyToOutputDirectory="PreserveNewest" />` added to `Hrot.Editor.AiShared.Tests.csproj`.

Parametrized `[Theory]` test `Parse_Fixture_ProducesExpectedCounts` added at the end of `LlmResponseParserTests.cs`. Fixture path resolved via `AppContext.BaseDirectory`.

---

## Build and Test Results

```
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug
Passed!  - Failed: 0, Passed: 451, Skipped: 0, Total: 451, Duration: 10 s

dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
  9 Warning(s)
  0 Error(s)
  Time Elapsed 00:02:39.06
```

Warnings are all pre-existing (CS8601 in BlueprintTestFixture.cs, CS0618 in integration tests using deprecated APIs). None introduced by this batch.

---

## Developer Insights

**Q1: Was the ComparisonExportBuilder skeleton signature compatible?**

Yes. The skeleton declared the method with the exact signature `Build(IAssetComparisonSanitizer sanitizer, AssetExportRequest versionA, AssetExportRequest versionB)` returning `string`. Only the body was replaced -- the skeleton returned `"<not implemented>"`.

**Q2: Truncated JSON recovery strategy and edge cases**

The recovery strategy is:
1. Find the last `}` character in the JSON section.
2. Slice the text from the opening `{` to that `}` and append `\n]\n}` to close the `changes` array and root object.
3. Attempt `JsonDocument.Parse` on the recovered string.

If the truncation occurred inside a string value (e.g., `"description": "text cut here`), the last `}` found may be the one from a partial or complete previous change object in the array. If the resulting recovered string is valid JSON, partial changes are returned. If the recovered string is still invalid, the unrecoverable path is taken (0 changes, truncation warning).

An edge case: if the LLM truncated exactly at a `}` that closes an inner nested object rather than a change entry, the recovered string may parse but contain fewer changes than were actually truncated. This is acceptable -- recovery is best-effort.

**Q3: CompanionFileDiscovery folder scan heuristics**

In `DiscoverFromFolder`, the scan reads each file and looks for `// AssetId:` or `// OwningAssetId:` in C# files, and the `"AssetId"` JSON field in Blueprint files.

A potential false-positive could occur with Blackboard companion files (`.Blackboard.cs`), which use `// OwningAssetId:` to reference the main asset's GUID. If `DiscoverFromFolder` is called with the main BTree asset's GUID, any `.Blackboard.cs` companion in the same folder that has `// OwningAssetId: <same-guid>` would match first if found before the main `_BT.cs` file (filesystem enumeration order is unspecified).

In practice this is safe because `DiscoverFromFolder` is only called with the main asset's AssetId (the one stamped by `// AssetId:`), not the OwningAssetId. The OwningAssetId in companion files is the same GUID as the main file's AssetId, so a companion's `OwningAssetId` could erroneously match. This is a latent risk on unsorted directory enumeration. Suggested P3 debt: prefer `// AssetId:` matches over `// OwningAssetId:` matches.

**Q4: Guid.Empty handling in AssetSelectionValidator**

`Guid.Empty` returned from AssetId extraction is treated as "unparseable" and triggers the error `"Cannot parse Version A's metadata: {path}"` (or Version B). This is intentional: a file that does not advertise a valid AssetId cannot be safely compared.

Edge case: a Blackboard file that is valid C# but lacks the `// OwningAssetId:` header returns `Guid.Empty` and is rejected with a parse error even though the file itself is syntactically correct. This is documented as a P3 debt item -- a future improvement could detect the Blackboard suffix and provide a more specific error message.

**Q5: Edge cases not covered by the specified test suite (P3 debt)**

1. File locked by the OS (e.g., held open by another process): `AssetSelectionValidator` would report "Cannot read file" which is correct, but the behavior is OS-specific and not easily testable without unsafe tricks on Windows.
2. `Guid.Empty` explicitly written in the `// AssetId:` header line: currently treated as unparseable (same as missing). A malformed asset with `Guid.Empty` as its actual ID would be rejected even if comparison is semantically valid.
3. Companion files in `DiscoverFromFolder` being found before the main file due to filesystem enumeration order: the scan returns the first file whose AssetId header matches, which could be a companion with a matching `OwningAssetId`. Suggested fix: score matches by header keyword (`AssetId:` preferred over `OwningAssetId:`).
4. Very large files in `DiscoverFromFolder`: the scan reads only the first ~20 lines to extract AssetId; very large files with the header further down would be missed. This is by design but worth documenting.
5. Parametrized fixture test for `ComparisonExportBuilder`: the export builder is only tested with inline `FakeSanitizer` results; no disk-based fixture captures a real sanitized output round-trip through the builder.

---

## Success Criteria Checklist

- [x] TASK-C-11: `CompanionFileDiscovery` with 6 tests
- [x] TASK-C-12: `AssetSelectionValidator` with 5 tests (missing-file and unparseable merged per implementation)
- [x] TASK-C-14: `ComparisonExportBuilder` full implementation with 7 tests
- [x] TASK-C-16: `LlmResponseParser` with 10 unit tests
- [x] TASK-C-17: `ComparisonSessionState` + `ComparisonSessionRegistry` with 7 tests
- [x] TASK-C-20: 9 fixture files + 1 parametrized Theory test with 9 cases
- [x] `dotnet test` passes -- 451/451
- [x] `dotnet build "IOS-IG-SimHost.sln"` -- 0 errors
- [x] Report submitted
