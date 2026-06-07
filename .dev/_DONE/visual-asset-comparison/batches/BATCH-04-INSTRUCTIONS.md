# BATCH-04: Export Logic + LLM Response Parsing (Slices C-4 backend + C-5 backend)

**Batch Number:** BATCH-04
**Tasks:** TASK-C-11, TASK-C-12, TASK-C-14, TASK-C-16, TASK-C-17, TASK-C-20
**Slices:** C-4 (export logic, no UI) + C-5 (response parsing, no UI)
**Estimated Effort:** 14-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (framework interfaces), BATCH-02/03 (sanitizers in place)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Design Document — §3.6, §4.1–4.6, §5.1–5.4:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
3. **Task Details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-11, C-12, C-14, C-16, C-17, C-20
4. **Existing comparison interfaces (study):**
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs` — types used throughout
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs` — skeleton to replace
5. **Design §3.6 companion discovery table** (in the design doc) — the naming conventions for each asset kind

### Source Code Locations

| What | Path |
|------|------|
| CompanionFileDiscovery (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs` |
| AssetSelectionValidator (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/AssetSelectionValidator.cs` |
| ComparisonExportBuilder (REPLACE skeleton) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs` |
| LlmResponseParser (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs` |
| ComparisonResponse + Change types (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonResponse.cs` |
| ComparisonSessionState + Registry (NEW) | `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs` |
| Companion discovery tests (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/CompanionFileDiscoveryTests.cs` |
| Validator tests (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/AssetSelectionValidatorTests.cs` |
| Export builder tests (EXTEND existing) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs` |
| Parser tests (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/LlmResponseParserTests.cs` |
| Session state tests (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonSessionStateTests.cs` |
| Response fixtures (NEW) | `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/Responses/` |

### Test Execution

```powershell
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit to: `.dev\visual-asset-comparison\reports\BATCH-04-REPORT.md`

---

## Tasks

### TASK-C-11 — `CompanionFileDiscovery`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-11`
**Design refs:** §3.6

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs`

**Companion naming conventions (from design §3.6):**

| Asset Kind | Main File Pattern | Companion Files |
|---|---|---|
| BTree | `{Name}_BT.cs` | `{Name}_BT.Blackboard.cs`, `{Name}_BT.HeavyBlackboard.cs`, `{Name}_BT.Orchestrators.g.cs` |
| HSM | `{Name}_HSM.cs` | `{Name}_HSM.Blackboard.cs`, `{Name}_HSM.HeavyBlackboard.cs`, `{Name}_HSM.Orchestrators.g.cs` |
| Blackboard | `{Name}.Blackboard.cs` | `{Name}.HeavyBlackboard.cs` |
| Blueprint | `{Name}.bp.json` | (none) |

**API to implement:**

```csharp
public static class CompanionFileDiscovery
{
    // Returns a DiscoveredAsset with main file + companion paths (found/not-found).
    public static DiscoveredAsset DiscoverFromMainFile(
        string mainFilePath, AssetKind expectedKind);

    // Scans a folder for a file whose AssetId matches targetAssetId; then resolves companions.
    // Skips directories whose name starts with '.'.
    public static DiscoveredAsset? DiscoverFromFolder(
        string folderPath, Guid targetAssetId, AssetKind expectedKind);
}

public sealed record DiscoveredAsset(
    string MainFilePath,
    IReadOnlyList<DiscoveredCompanion> Companions);

public sealed record DiscoveredCompanion(
    string Path,
    bool IsPresent);
```

**Implementation notes:**
- `DiscoverFromMainFile`: determine base name using the asset kind's suffix (e.g., strip `_BT.cs` to get `{Name}` for BTree), construct companion paths in the same directory, check `File.Exists()` for each.
- `DiscoverFromFolder`: recursively scan `folderPath` for `*.cs` and `*.bp.json` files. Skip directories whose name starts with `.` (e.g., `.migration-snapshots`, `.git`). Parse each candidate file's header to extract AssetId (`// AssetId:` or `// OwningAssetId:` for Blackboard files; `"AssetId"` JSON field for Blueprint). Return the first match's `DiscoveredAsset`. If none found, return null.
- AssetId extraction must not throw; skip files that error on read or parse.

**Tests required (`CompanionFileDiscoveryTests.cs`):**
- **BTree single-file:** Create a temp directory with `Foo_BT.cs` + `Foo_BT.Blackboard.cs` (no heavy, no orchestrators). `DiscoverFromMainFile` returns main file + 1 present companion + 2 not-present companions.
- **Blackboard companions:** `{Name}.Blackboard.cs` present; `{Name}.HeavyBlackboard.cs` present. Both in the result.
- **Blueprint:** No companions expected.
- **Folder mode:** Create a temp folder with `Foo_BT.cs` (containing `// AssetId: <guid>`) and a `.migration-snapshots/Foo_BT.cs` with the same AssetId. `DiscoverFromFolder` finds the one NOT in the dot-prefixed dir.
- **Folder mode — dot-prefix exclusion is general:** Create a `.git/Bar.cs` with the target AssetId; verify it is NOT returned.
- **Folder mode — not found:** Empty folder returns null.

---

### TASK-C-12 — `AssetSelectionValidator`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-12`
**Design refs:** §3.7, §7.3

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/AssetSelectionValidator.cs`

**Validation result types:**

```csharp
public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message);

public enum ValidationSeverity { Error, Warning }
```

**API:**

```csharp
public static class AssetSelectionValidator
{
    // Returns ValidationResult. IsValid=false means "do not proceed with comparison".
    // IsValid=true with Warnings means "proceed with caution".
    public static ValidationResult Validate(
        DiscoveredAsset versionA,
        DiscoveredAsset versionB,
        AssetKind expectedKind);
}
```

**Rules (from §3.7):**
1. Both main files must exist. Error: `"File not found: {path}"`.
2. Both files must be readable. Error: `"Cannot read file: {path}"`.
3. Both files must be parseable enough to extract AssetId. Error: `"Cannot parse Version A's metadata: {path}"`.
4. The two files must have the same `AssetKind`. Error: `"Cannot compare across asset kinds — Version A is {kindA} but Version B is {kindB}."`.
5. If AssetIds differ: Warning: `"The two assets have different AssetIds ({idA} vs {idB}). Phase 1 comparison treats both as the same asset for visualId correlation..."`.

**Tests required (`AssetSelectionValidatorTests.cs`):**
- **Kind mismatch:** BTree + Blueprint → `IsValid=false`, error message contains "asset kinds".
- **Same kind, same AssetId:** `IsValid=true`, no issues.
- **Same kind, different AssetIds:** `IsValid=true`, one Warning with AssetId values in message.
- **Missing file:** `IsValid=false`, error mentions "File not found".
- **Unreadable file:** Write a file, then lock it (or just write an unreadable name — on Windows this may not be easy; simulate by wrapping file read in a try/catch and testing with a file whose content can't be parsed as any AssetKind's header format — use the third rule).
- **Unparseable file:** Write a file containing garbage that doesn't parse as C# or JSON. `IsValid=false`, error mentions "Cannot parse".

**Note on asset ID extraction:** Extract AssetId from files the same way each sanitizer does: scan for `// AssetId:` header line (C# files) or `"AssetId"` JSON field (Blueprint). If extraction fails or returns `Guid.Empty`, treat as unparseable.

---

### TASK-C-14 — `ComparisonExportBuilder` Full Implementation

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-14`
**Design refs:** §4.1, §4.2, §4.3, §4.4, §4.6

**File to replace:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs`

The current skeleton returns `"<not implemented>"`. Replace it with the full implementation.

**Method signature (existing — do NOT change the signature, only the implementation):**

```csharp
public string Build(
    IAssetComparisonSanitizer sanitizer,
    AssetExportRequest versionA,
    AssetExportRequest versionB)
```

**What to produce (§4.1 structure):**

```
{INSTRUCTION_BLOCK}

================================================================================
VERSION A (OLD)
================================================================================
{VERSION_A_METADATA_BLOCK}

--- COMPANION FILES ---
{VERSION_A_SANITIZED_CONTENT}

================================================================================
VERSION B (NEW)
================================================================================
{VERSION_B_METADATA_BLOCK}

--- COMPANION FILES ---
{VERSION_B_SANITIZED_CONTENT}

================================================================================
END OF COMPARISON INPUT
================================================================================
```

- Separator lines: `=` × 80 chars.
- The instruction block is the complete text from §4.2 of the design (the "You are comparing two versions..." text starting at the first paragraph through `Begin your response now with the HUMAN SUMMARY section.`). Store it as a `private const string` in the class.
- Metadata block format (§4.3):
  ```
  ASSET NAME:       {AssetName}
  ASSET KIND:       {Kind}
  ASSET ID:         {AssetId:D}
  SOURCE PATH:      {SourceFilePath}
  LAST MODIFIED:    {Timestamp:yyyy-MM-dd HH:mm:ss} UTC   (or "(unknown)")
  COMPANION FILES:  {CompanionFile1} (present)
                    {CompanionFile2} (not present)
  ```
  If no companion files, omit the COMPANION FILES line.
- Sanitized content per file (§4.4): each file preceded by `// === FILE: {filename} ===\n` header. Main file first, companions alphabetically by filename.
- Migration notice: when `SanitizationResult.Metadata.MigrationNotice` is non-null, insert `// MIGRATION NOTICE: {notice}\n` before that version's content section.
- All line endings normalized to `\n` (§4.6).
- The `Build` method invokes `sanitizer.Sanitize()` for both versionA and versionB. Companion files are NOT sanitized (they are emitted verbatim since we have separate sanitizers for the Blackboard companion — they were already processed when the Blackboard sanitizer ran). Wait, actually: per the design §4.4, the main file content is the `SanitizationResult.SanitizedText`. Companion files are listed in `SanitizationResult.Metadata.CompanionFiles`; for the builder, just read them verbatim (they were already sanitized by the BlackboardComparisonSanitizer in the result). Actually re-read the design: the `CompanionFiles` list in `AssetMetadataBlock` is just a list of paths. For TASK-C-14, just include the companion file paths in the metadata block labeled `(present)` or `(not present)` based on `File.Exists`. The content of companion files appears in the sanitized text of the MAIN sanitizer output (e.g., the Blackboard sanitizer concatenates inline + heavy into one `SanitizedText`). So the `Build` method only needs to emit `SanitizationResult.SanitizedText` with the `// === FILE: {mainFileName} ===` header.

**Tests required (extend `ComparisonExportBuilderTests.cs`):**

The current test in that file asserts `Build(...) == "<not implemented>"`. Replace or extend it with proper tests:

- **Structural test:** Given two mock sanitizers (stub), assert the output contains all four separator lines, VERSION A/B headers, `--- COMPANION FILES ---` markers, and `END OF COMPARISON INPUT`.
- **Instruction block test:** The output starts with the instruction block text (first 3 words: "You are comparing").
- **Metadata test:** Given a known `AssetMetadataBlock`, the output contains correctly-formatted ASSET NAME, ASSET KIND, ASSET ID, SOURCE PATH, LAST MODIFIED lines.
- **File header test:** The sanitized text is preceded by `// === FILE: {filename} ===`.
- **Migration notice test:** When `Metadata.MigrationNotice` is populated, the output contains `// MIGRATION NOTICE: ` before the version content.
- **Line endings test:** Output contains no `\r\n` — all line endings are `\n`.
- **Self-comparison test:** Same `SanitizationResult` for A and B (identical sanitized text, same metadata except path) — the export is structurally valid and the two content sections are byte-identical modulo the file paths in the metadata.

**Note:** The mock for `IAssetComparisonSanitizer` should return a `SanitizationResult` with fixed content so tests are deterministic. Use a `FakeSanitizer` nested class.

---

### TASK-C-16 — `LlmResponseParser`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-16`
**Design refs:** §5.1, §5.2, §5.3, §5.4

**New files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonResponse.cs`

**Types to define in `ComparisonResponse.cs`:**

```csharp
public sealed record ComparisonResponse(
    string? HumanSummary,
    string TopLevelSummary,
    IReadOnlyList<ComparisonChange> Changes,
    IReadOnlyList<string> Warnings);

public sealed record ComparisonChange(
    string Kind,
    string? ElementId,
    string ElementDescription,
    string? Field,
    string? OldValue,
    string? NewValue,
    string Severity,
    string Description);
```

The `Kind` and `Severity` are stored as strings (not enums) so unknown values don't cause parse failures. Consumers map to enums using `ComparisonStyleMap` (TASK-C-22, next batch).

**LlmResponseParser API:**

```csharp
public static class LlmResponseParser
{
    // Returns a ComparisonResponse on success, or a ComparisonResponse with a single
    // warning "LLM response appears truncated. Re-run..." when unrecoverable.
    // Never throws.
    public static ComparisonResponse Parse(string responseText);
}
```

**Parsing algorithm (per §5.3):**

1. Strip markdown fences (` ```json ... ``` ` patterns).
2. Locate section boundaries:
   - Find `----- HUMAN SUMMARY -----` marker (five dashes, exact text).
   - Find `----- STRUCTURED CHANGES (JSON) -----` marker.
   - If both found: human summary = text between them; JSON section = text after JSON marker.
   - If markers absent: fallback — find the first `{` in the text; everything before it is the human summary; JSON is from `{` to the last `}`.
3. Parse the JSON section with `System.Text.Json`.
4. If parse fails (truncated JSON): attempt recovery — find the last complete `}` before EOF that closes a JSON object in the `changes` array. Build a partial result from what was parsed.
5. If recovery also fails: return `ComparisonResponse("LLM response appears truncated...", ...)` with the truncation warning.
6. Validate each `Change`:
   - Unknown `kind`: add warning `"LLM produced unknown kind '{kind}' — treated as 'node_modified'"`, keep the entry with `Kind = "node_modified"`.
   - Unknown `severity`: add warning `"LLM produced unknown severity '{severity}' — treated as 'tuning'"`, keep the entry with `Severity = "tuning"`.
   - Missing required `description`: fill with empty string, add warning.
   - `elementId` null or absent: keep as null (no warning needed).

**Known kind values:** `node_added`, `node_removed`, `node_modified`, `variable_added`, `variable_removed`, `variable_renamed`, `variable_retyped`, `connection_changed`, `comment_changed`, `intent_shift`.

**Known severity values:** `cosmetic`, `tuning`, `feature`, `removal`, `behavior`.

**Tests required (`LlmResponseParserTests.cs`):**
- **Well-formed response (§5.4 example):** All fields populated correctly. Human summary text captured. Both changes parsed. `Warnings` empty.
- **Markdown-fenced JSON:** Input wraps JSON in ` ```json ... ``` `. Result is identical to the non-fenced version.
- **Leading prose before HUMAN SUMMARY marker:** Extra text before the marker is tolerated; summary correctly extracted.
- **Marker absent:** Fallback — first `{` to last `}` is the JSON section; everything before is the summary.
- **Truncated JSON (recoverable):** Input is the §5.4 example JSON with the closing `]` and `}` cut off after the first `}` of `changes`. Parser returns the first change, with a warning.
- **Truncated JSON (unrecoverable):** Input is just `{ "summary": "abc", "changes": [{ "kind":` (mid-key cut). Parser returns truncation warning.
- **Unknown `kind`:** Change with `"kind": "totally_unknown_thing"`. Warning added; kind set to `node_modified`.
- **Unknown `severity`:** Change with `"severity": "catastrophic"`. Warning added; severity set to `tuning`.
- **Missing `description`:** Change object without `description` key. Warning added; description set to empty string.
- **Null `elementId`:** Change with `"elementId": null`. Parsed normally; no warning.
- **Empty changes array:** `"changes": []`. Returns empty `Changes`, no warnings.

---

### TASK-C-17 — `ComparisonSessionState` and `ComparisonSessionRegistry`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-17`
**Design refs:** §6.2, §6.3, §6.9

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs`

**Types:**

```csharp
public sealed class ComparisonSessionState
{
    public Guid AssetId { get; }
    public ComparisonResponse Response { get; }
    public string? MigrationNotice { get; }
    public bool IsStale { get; private set; }
    public IReadOnlySet<string> EnabledSeverities { get; }  // backed by HashSet<string>

    // Default: behavior, feature, removal, tuning enabled; cosmetic disabled.
    public ComparisonSessionState(Guid assetId, ComparisonResponse response, string? migrationNotice = null);

    public void ToggleSeverity(string severity);
    public void MarkStale();
}

// Singleton registry keyed by AssetId.
public sealed class ComparisonSessionRegistry
{
    public ComparisonSessionState? GetSession(Guid assetId);
    public void SetSession(ComparisonSessionState session);
    public void ClearSession(Guid assetId);
}
```

**Default enabled severities:** `behavior`, `feature`, `removal`, `tuning` (cosmetic disabled by default per §6.3).

**DI registration:** Register `ComparisonSessionRegistry` as a singleton in `SharedAiEditorServiceCollectionExtensions.AddSharedAiEditor()`.

**Tests required (`ComparisonSessionStateTests.cs`):**
- **Default enabled severities:** `behavior`, `feature`, `removal`, `tuning` in set; `cosmetic` NOT in set.
- **ToggleSeverity — add:** Cosmetic disabled by default; call `ToggleSeverity("cosmetic")` → now enabled.
- **ToggleSeverity — remove:** `ToggleSeverity("behavior")` → behavior disabled.
- **MarkStale:** `IsStale = false` initially; `MarkStale()` → `IsStale = true`.
- **Registry — set and get:** `SetSession(state)`, then `GetSession(assetId)` returns same instance.
- **Registry — overwrite:** `SetSession` twice for same AssetId → `GetSession` returns second.
- **Registry — clear:** `ClearSession` → `GetSession` returns null.

---

### TASK-C-20 — LLM Response Parsing Fixture Suite

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-20`
**Design refs:** §10.1

**New files (fixture texts):**

Location: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/Responses/`

| File | Content |
|------|---------|
| `well_formed.txt` | The complete §5.4 example response (human summary + structured JSON) |
| `markdown_fenced.txt` | Same content but JSON wrapped in ` ```json ... ``` ` |
| `extra_leading_prose.txt` | Well-formed response preceded by 3 lines of random prose before `----- HUMAN SUMMARY -----` |
| `truncated_recoverable.txt` | §5.4 example with only the first `changes` entry present (closing `]` and `}` missing) |
| `truncated_unrecoverable.txt` | JSON cut mid-key: `{ "summary": "abc", "changes": [{ "kind":` |
| `unknown_kind.txt` | Well-formed but with `"kind": "something_new"` on one change |
| `unknown_severity.txt` | Well-formed but with `"severity": "catastrophic"` on one change |
| `missing_required_field.txt` | Well-formed but `description` key absent from one change |
| `unresolvable_element_ids.txt` | Well-formed, all changes have `elementId: "nonexistent-id"` |

**One parametrized test per fixture:**

```csharp
[Theory]
[InlineData("well_formed.txt",              2, 0)]  // 2 changes, 0 warnings
[InlineData("markdown_fenced.txt",          2, 0)]
[InlineData("extra_leading_prose.txt",      2, 0)]
[InlineData("truncated_recoverable.txt",    1, 1)]  // 1 change recovered, 1 warning
[InlineData("truncated_unrecoverable.txt",  0, 1)]  // 0 changes, 1 truncation warning
[InlineData("unknown_kind.txt",             2, 1)]  // 1 warning for unknown kind
[InlineData("unknown_severity.txt",         2, 1)]  // 1 warning for unknown severity
[InlineData("missing_required_field.txt",   2, 1)]  // 1 warning for missing description
[InlineData("unresolvable_element_ids.txt", 2, 0)]  // elementId not validated here
public void Parse_Fixture_ProducesExpectedCounts(string fixture, int expectedChanges, int expectedWarnings)
```

**Note on test project fixture embedding:** Add `<Content Include="Comparison/Fixtures/Responses/**" CopyToOutputDirectory="PreserveNewest" />` to the `.csproj` if not already there. Reference files via `Path.Combine(AppContext.BaseDirectory, "Comparison", "Fixtures", "Responses", fixtureName)`.

---

## Mandatory Workflow

1. **TASK-C-11:** Implement + test CompanionFileDiscovery → all tests pass ✅
2. **TASK-C-12:** Implement + test AssetSelectionValidator → all tests pass ✅
3. **TASK-C-14:** Replace export builder skeleton with full implementation + tests → all tests pass ✅
4. **TASK-C-16:** Implement + test LlmResponseParser → all tests pass ✅
5. **TASK-C-17:** Implement + test ComparisonSessionState + Registry → all tests pass ✅
6. **TASK-C-20:** Create fixture files + parametrized tests → all tests pass ✅
7. Full solution build: 0 errors ✅

---

## Developer Insights (Answer in Report)

**Q1:** The `ComparisonExportBuilder` skeleton existed from BATCH-01. Was the method signature compatible with the full implementation, or did you need to change it?

**Q2:** Truncated JSON recovery is the trickiest part of `LlmResponseParser`. What strategy did you implement, and did you need to handle any edge cases (e.g., truncation in the middle of a string value vs. between objects)?

**Q3:** The `CompanionFileDiscovery.DiscoverFromFolder` must parse multiple file types to extract AssetIds. Did you find any cases in the real test assets where the parsing heuristics would have picked the wrong file?

**Q4:** The `AssetSelectionValidator` extracts AssetIds from files to check for mismatches. How did you handle the case where a file is valid but reports `Guid.Empty` as the AssetId (e.g., a Blackboard file without the `// OwningAssetId:` header)?

**Q5:** List any edge cases or scenarios you were unable to test with the specified test suite. Suggest them as P3 debt items.

---

## Success Criteria

- [ ] TASK-C-11: `CompanionFileDiscovery` with 6 tests (single-file BTree, Blackboard companions, Blueprint no-companions, folder mode hit, folder mode dot-prefix exclusion, folder mode not found)
- [ ] TASK-C-12: `AssetSelectionValidator` with 6 tests (kind mismatch, same kind same id, same kind different id, missing file, unreadable/unparseable)
- [ ] TASK-C-14: `ComparisonExportBuilder` full implementation with 7 tests (structural, instruction block, metadata, file header, migration notice, line endings, self-comparison)
- [ ] TASK-C-16: `LlmResponseParser` with 10 tests (well-formed, markdown fenced, extra prose, marker absent, truncated-recoverable, truncated-unrecoverable, unknown kind, unknown severity, missing description, null elementId)
- [ ] TASK-C-17: `ComparisonSessionState` + `ComparisonSessionRegistry` with 7 tests
- [ ] TASK-C-20: 9 fixture files created + 1 parametrized test with 9 cases
- [ ] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/..."` passes
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-04-REPORT.md`

---

## Reference Materials

- **Design §3.6 (companion file table):** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
- **Design §4.1–4.6 (export format):** Same file
- **Design §5.1–5.4 (LLM contract + parser):** Same file
- **Design §6.2–6.3 (session state):** Same file
- **Task details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-11 through C-17, C-20
- **Existing type reference:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs`
