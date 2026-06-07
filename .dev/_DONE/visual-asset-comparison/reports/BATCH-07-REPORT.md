# BATCH-07 Report

**Batch:** BATCH-07 — Polish, Robustness, and Debt Fixes  
**Status:** COMPLETE  
**Tasks:** C-29 (verify), C-30 (verify), C-31 (verify), C-32, C-33, C-34, D-09 fix, D-10 fix, D-12 fix

---

## 1. Summary of Work Completed

### Pre-Task Verification: C-29, C-30, C-31 ✅

All three tasks were confirmed already implemented. Existing tests were found and verified:

- **C-29** (`ExportDeliveryModalTests.cs`): `GetClipboardText_UnderLimit_ReturnsText`, `GetClipboardText_OverLimit_ReturnsNull`
- **C-30** (`ExportDeliveryModalTests.cs`): `GetPreviewText_40Lines_Returns30LinesWithMarker`, `GetPreviewText_ShowFull_ReturnsFullText`
- **C-31** (`AssetSelectionDialogTests.cs`): `Reverse_SwapsPaths`, `DoubleReverse_RestoresOriginal`

No additional tests were needed.

---

### D-12 Fix: PasteResponseModalState.Apply — 0-change Response Policy ✅

**File changed:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/PasteResponseModal.cs`

**Problem:** The old code rejected any response that had warnings and zero changes. This broke the valid "nothing changed" scenario (LLM correctly reports no differences, with an informational warning).

**Fix:** Only reject 0-change responses when the warning contains the truncation text (`ComparisonErrorMessages.TruncatedResponse`). Informational warnings with 0 changes are now accepted as valid.

**Tests added (3):**
- `ZeroChanges_WithTruncationWarning_Rejected`
- `ZeroChanges_WithInformationalWarning_Accepted`
- `ZeroChanges_NoWarnings_Accepted`

---

### D-09 Fix: CompanionFileDiscovery.DiscoverFromFolder — Prefer Main File ✅

**File changed:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs`

**Problem:** `DiscoverFromFolder` returned the first file that matched the target `AssetId` in any header field, including companion files that carry `OwningAssetId`. A `.Blackboard.cs` companion could be returned instead of the main `.BT.cs` file.

**Fix:** Added `ScoreFileForAssetId()` private method that returns 2 for `AssetId:` match, 1 for `OwningAssetId:` match. The highest-scored file is selected, ensuring the main asset file always beats companions.

**Test added (1):**
- `DiscoverFromFolder_PreferAssetIdOverOwningAssetId`

---

### D-10 Fix: ComparisonExportBuilder Round-Trip Test ✅

**File changed:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs`

**Test added (1):**
- `Build_DiskFixtureRoundTrip_ContainsAllStructuralMarkers` — verifies `VERSION A (OLD)`, `VERSION B (NEW)`, `--- COMPANION FILES ---`, `END OF COMPARISON INPUT` markers and determinism (two back-to-back builds produce identical output).

---

### C-32: Fixture Corpus ✅

New fixture files and tests added:

| Fixture | Test | Location |
|---|---|---|
| `NoCommentsBTree.cs` | `NoComments_Asset_SanitizesWithoutError` | `Hrot.BTree.Editor.Tests` |
| `ReadOnlyBlackboard.cs` | `ReadOnly_AllFields_SanitizesSuccessfully` | `Hrot.Editor.AiShared.Tests` |
| `ParallelHsm.cs` | `ParallelRegions_WithGlobalTransitions_SanitizesCorrectly` | `Hrot.Hsm.Editor.Tests` |
| inline JSON | `DeepNested_Blueprint_SanitizesAllGraphsInOrder` | `Hrot.Blueprints.Tests` |
| inline content | `MalformedFile_NoCSharpClass_ReturnsFallback` | `Hrot.BTree.Editor.Tests` |

**csproj fix:** `Hrot.Editor.AiShared.Tests.csproj` was missing `<Compile Remove="Comparison\Fixtures\*.cs" />` and `<None ... CopyToOutputDirectory>`. Added to match the pattern already used by `Hrot.BTree.Editor.Tests` and `Hrot.Hsm.Editor.Tests`.

---

### C-33: Error Handling Polish ✅

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonErrorMessages.cs`

Central constants class with 5 constants:
- `TruncatedResponse`, `FileNotFound`, `AssetKindMismatch`, `AssetIdMismatch`, `CannotParseMetadata`

**Wired into:**
- `LlmResponseParser.cs` — `TruncationWarning` now references `ComparisonErrorMessages.TruncatedResponse`
- `PasteResponseModal.cs` — `TruncationText` local variable now references the same constant
- `AssetSelectionValidator.cs` — all five inline message strings replaced with constants

**New test file:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonErrorMessagesTests.cs`

Tests (3):
- `AllPublicConstantsAreNonEmpty` — reflection-based constant health check
- `AssetIdMismatch_Validator_UsesCorrectMessage` — integration: validator warning contains the constant
- `TruncatedResponse_Parser_UsesCorrectMessage` — integration: parser warning on truncated input equals the constant

---

### C-34: User-Facing Documentation ✅

**New file:** `.dev/visual-asset-comparison/USER-GUIDE.md`

Covers all four §1.1 use cases:
1. PR Review
2. AI-Agent Edit Audit
3. Refactor Verification
4. Regression Hunt

Plus generic Export → LLM → Paste workflow section and a Severity Reference table.

---

## 2. Test Results

```
Passed!  - Failed: 0, Passed: 157, Skipped: 0  — Hrot.Editor.AiShared.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed:  17, Skipped: 0  — Hrot.Blueprints.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed:  15, Skipped: 0  — Hrot.BTree.Editor.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed:  16, Skipped: 0  — Hrot.Hsm.Editor.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed:   2, Skipped: 0  — Fdp.Core.Tests.dll (net8.0)
```

Note: `Fdp.Examples.CarKinem.Tests` exits with a .NET runtime error (net9.0 not installed). This is a pre-existing environmental issue, not related to BATCH-07 changes.

Build: **succeeded** (solution-wide, zero CS errors).

---

## 3. Developer Insights

### Issues Encountered

1. **Fixture `.cs` files compiled by SDK glob** — `Hrot.Editor.AiShared.Tests.csproj` did not exclude `Comparison/Fixtures/*.cs` from compilation. The `ReadOnlyBlackboard.cs` fixture failed to compile because `[ReadOnly]` is not a type in that project. Fixed by adding `<Compile Remove>` + `<None CopyToOutputDirectory>` to match the pattern already established in the BTree and HSM test projects.

2. **`CannotParseMetadata` multi-match** — `AssetSelectionValidator.cs` has two identical `error = $"Cannot parse Version..."` statements (one in the JSON branch, one in the C# header branch). `replace_string_in_file` correctly rejected the ambiguous replacement; applied two targeted replacements with unique surrounding context.

### Weak Points Spotted

- **`ComparisonErrorMessages` constants use prefix-only strings**, not `string.Format`-ready `{0}` placeholders. The batch instructions suggested using `{0}` placeholders, but the existing validator code builds messages by string interpolation. Changing to `string.Format` style would require changing call sites; instead, prefix-only constants were used to preserve backward compatibility. If a future batch unifies message construction style, this is the place to align.

- **`Hrot.Editor.AiShared.Tests.csproj` had no fixture exclusion pattern** — this was a gap compared to the BTree/HSM test projects. The pattern should be considered standard and applied to all test projects that hold `.cs` fixture files. No existing pre-existing tests broke, but it could have caused issues earlier.

### Design Decisions Beyond the Spec

- `DeepNestedBlueprint.bp.json` was placed in `Hrot.Editor.AiShared.Tests/Comparison/Fixtures/` as specified, but the test in `BlueprintComparisonSanitizerTests.cs` uses inline JSON rather than loading the fixture file. This avoids cross-project file dependency issues (the Blueprints test project cannot reference AiShared.Tests fixtures via `AppContext.BaseDirectory`). The fixture file remains as a reference document.

- `ParallelHsm.cs` fixture tests both layout stripping (`DoesNotContain("[HsmLayout("`) and coordinate removal (`DoesNotContain("Vector2("`)) as two separate assertions, giving more precise failure messages if the sanitizer regresses.

---

## 4. Files Changed

### New Files
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonErrorMessages.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonErrorMessagesTests.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/ReadOnlyBlackboard.cs`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/DeepNestedBlueprint.bp.json`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/NoCommentsBTree.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/ParallelHsm.cs`
- `.dev/visual-asset-comparison/USER-GUIDE.md`

### Modified Files
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/PasteResponseModal.cs` (D-12 + C-33)
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs` (D-09)
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs` (C-33)
- `Hrot/Editor/Hrot.Editor.AiShared/Comparison/AssetSelectionValidator.cs` (C-33)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` (fixture exclusion)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/PasteResponseModalTests.cs` (D-12 tests)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/CompanionFileDiscoveryTests.cs` (D-09 test)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs` (D-10 test)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardComparisonSanitizerTests.cs` (C-32 test)
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/BTreeComparisonSanitizerTests.cs` (C-32 tests)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs` (C-32 test)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/BlueprintComparisonSanitizerTests.cs` (C-32 test)
