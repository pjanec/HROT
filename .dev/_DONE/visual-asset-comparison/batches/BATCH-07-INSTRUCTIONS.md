# BATCH-07: Polish, Robustness, and Debt Fixes (Slice C-7 + debt)

**Batch Number:** BATCH-07
**Tasks:** TASK-C-29 (verify), TASK-C-30 (verify), TASK-C-31 (verify), TASK-C-32, TASK-C-33, TASK-C-34 + D-09, D-10, D-12 fixes
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** All prior batches

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Design §10.1 (fixture corpus spec):** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
3. **Design §7.3 + §5.3 (error handling):** same file
4. **Design §1.1 + §2 (use cases, workflow):** same file
5. **Task Details C-29 through C-34:** `.dev\visual-asset-comparison\TASK-DETAILS.md`
6. **Current implementation files:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ExportDeliveryModal.cs` — already implements C-29 + C-30
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/AssetSelectionDialog.cs` — already implements C-31
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/PasteResponseModal.cs` — D-12 fix target
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs` — D-09 fix target
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs` — TruncationWarning constant lives here
7. **Existing fixture folders (for C-32 scope):**
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/`
   - `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/`
   - `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/`
   - Blackboard fixture folder (check what exists)

### Test Execution

```powershell
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit to: `.dev\visual-asset-comparison\reports\BATCH-07-REPORT.md`

---

## Pre-Task Verification: C-29, C-30, C-31

**READ BEFORE CODING:** These three tasks are already implemented. Your first job is to verify them.

1. Open `ExportDeliveryModal.cs`. Confirm:
   - `_copyDisabled = _state.GetClipboardText() == null;` exists (C-29 done)
   - `ImGui.Checkbox("Show full export", ref _showFull);` exists (C-30 done)
   - `ImGui.BeginDisabled()` / `ImGui.EndDisabled()` wraps the Copy button (C-29 done)

2. Open `AssetSelectionDialog.cs`. Confirm:
   - `ImGui.Button("Reverse A<->B")` button exists (C-31 done)
   - `_state.Reverse()` is called when clicked (C-31 done)

3. Open existing tests in `ExportDeliveryModalTests.cs`. Confirm these tests exist:
   - `GetClipboardText_UnderLimit_ReturnsText` (C-29)
   - `GetClipboardText_OverLimit_ReturnsNull` (C-29)
   - Tests for `GetPreviewText` (C-30)
   - Tests for `AssetSelectionDialogState.Reverse()` (C-31)

If any of these are missing (implementation or test), add them now. Otherwise, all three tasks are done.

**IMPORTANT:** Do NOT rewrite these implementations. Only add missing tests if needed.

---

## D-12 FIX — PasteResponseModalState.Apply: 0-change response policy

**Target:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/PasteResponseModal.cs`

**Current bug:**
```csharp
// A response with warnings but no changes is treated as a parse error in the UI.
if (response.Warnings.Count > 0 && response.Changes.Count == 0)
{
    ParseError = response.Warnings[0];
    return false;
}
```

**Problem:** A valid "nothing changed" response from the LLM may have:
- 0 changes + 0 warnings (valid — accepted, no change needed)
- 0 changes + a warning that is the TruncationWarning constant (structural parse error — should reject)
- 0 changes + some other informational warning (valid — should accept)

The current code rejects ALL non-empty-warning 0-change responses. But only the TruncationWarning indicates a structural parse failure.

**Fix:**
```csharp
// Only reject when the warning specifically indicates a structural parse failure
// (truncated JSON). Informational notes do not invalidate a "nothing changed" result.
const string TruncationText = "LLM response appears truncated";
var hasTruncationError = response.Warnings.Any(
    w => w.Contains(TruncationText, StringComparison.OrdinalIgnoreCase));

if (hasTruncationError && response.Changes.Count == 0)
{
    ParseError = response.Warnings.First(
        w => w.Contains(TruncationText, StringComparison.OrdinalIgnoreCase));
    return false;
}
```

Note: Do NOT hardcode the full TruncationWarning string. Use `"LLM response appears truncated"` as the prefix check — see `LlmResponseParser.TruncationWarning` for the full string.

If `LlmResponseParser.TruncationWarning` is `internal`, change its access modifier to `internal` (it probably already is — just reference it from the `PasteResponseModal` class which is in the same assembly).

**New tests to add to `PasteResponseModalTests.cs`:**

- **ZeroChanges_WithTruncationWarning_Rejected:** Build a `ComparisonResponse` with 0 changes and a warning containing "LLM response appears truncated". `Apply()` returns false and `ParseError != null`.
- **ZeroChanges_WithInformationalWarning_Accepted:** Build a `ComparisonResponse` with 0 changes and a warning like "No structural changes detected". `Apply()` returns true and a session is stored.
- **ZeroChanges_NoWarnings_Accepted:** `ComparisonResponse` with 0 changes, 0 warnings. `Apply()` returns true.

---

## D-09 FIX — CompanionFileDiscovery.DiscoverFromFolder ranking

**Target:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs`

**Current issue:** `DiscoverFromFolder` scans the folder for `.cs` files and returns the first match for the asset ID. A `.Blackboard.cs` file with `OwningAssetId:` matching the asset may be selected as the "main file" before the actual `.BTree.cs` or `.Hsm.cs` main asset file (which has `AssetId:`).

**Fix:** When multiple files contain the asset ID, prefer files matching `AssetId:` over `OwningAssetId:`. The main asset file uses `AssetId:` as the header. Companions use `OwningAssetId:`.

Scan all `.cs` files in the folder. Score them:
- 2 points if the file contains `AssetId: {guidHex}` (direct format) 
- 1 point if the file contains `OwningAssetId: {guidHex}` or `AssetId: {guidHex}` in other forms

Return the highest-scored file as the main file. If there is a tie or no `AssetId:` match, fall back to the first-found.

Keep the implementation simple: read first line of each file (where the header should be), score, pick the best.

**New test to add (look for existing `CompanionFileDiscoveryTests.cs`):**

- **DiscoverFromFolder_PreferAssetIdOverOwningAssetId:** Create a temp folder with:
  - `MyAsset.Blackboard.cs` containing `// OwningAssetId: {guid}` on the first line
  - `MyAsset.BTree.cs` containing `// AssetId: {guid}` on the first line
  
  Both share the same GUID. `DiscoverFromFolder(guid, AssetKind.BTree, folderPath)` should return `MyAsset.BTree.cs` as the main file, not the Blackboard companion.

---

## D-10 FIX — ComparisonExportBuilder disk fixture round-trip test

**Target:** Add test to `Hrot.Editor.AiShared.Tests/Comparison/ComparisonExportBuilderTests.cs` (or a new `ComparisonExportBuilderRoundTripTests.cs`).

**Approach:**
1. Use an existing sanitizer fixture file (pick the smallest one from `BTree.Editor.Tests/Comparison/Fixtures/`).
2. Sanitize it to get a `SanitizationResult`.
3. Create a second `SanitizationResult` with the same content (simulating "same asset, two versions").
4. Call `ComparisonExportBuilder.Build(resultA, resultB)`.
5. Assert the output contains the expected header markers:
   - `=== VERSION A ===`
   - `=== VERSION B ===`
   - `--- METADATA ---`
   - `--- COMPANION FILES ---` (or assert it's present if companions exist)
6. Assert output is byte-identical on a second call with the same inputs (determinism check).

**No golden file needed.** Just assert structural presence and determinism.

---

## TASK-C-32 — Comprehensive Sanitization Fixture Corpus

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-32`
**Design refs:** §10.1

Add the following missing fixture files and tests. For each fixture, look at the existing fixture structure in:
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/`

Do NOT create a new test project. Add fixtures and tests to the existing test files.

### 1. BTree — Asset With No Comments

**Fixture file:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/Fixtures/NoCommentsBTree.cs`

Content: A minimal BTree C# file with no `//` comments at all. Just the class structure and method bodies.

**Test (add to existing `BTreeComparisonSanitizerTests.cs`):**
```
NoComments_Asset_SanitizesWithoutError
```
- Sanitize the fixture. Assert `result.SanitizedText` does not contain `//`.
- Assert `SanitizationResult.Warnings` is empty.

### 2. Blackboard — Only Read-Only Passthrough Fields

**Fixture file:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/ReadOnlyBlackboard.cs`

Content: A Blackboard C# file where all fields are `[ReadOnly]` passthrough fields (or whatever attribute marks them read-only in the existing codebase — check `BlackboardComparisonSanitizer.cs` to see how it handles read-only fields). No writable fields.

**Test (add to existing `BlackboardComparisonSanitizerTests.cs`):**
```
ReadOnly_AllFields_SanitizesSuccessfully
```
- Sanitize the fixture. Assert result is not null.
- Assert sanitized output contains the field names (they should be included — sanitizer doesn't strip fields, just normalizes).

### 3. Blueprint — Deeply Nested Graphs

**Fixture file:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Fixtures/DeepNestedBlueprint.bp.json`

Content: A Blueprint JSON file with 2-3 nested subgraph references. Each nested graph has its own node list. The root graph references 2 inner graphs; each inner graph has 2 nodes.

**Test (add to existing `BlueprintComparisonSanitizerTests.cs`):**
```
DeepNested_Blueprint_SanitizesAllGraphsInOrder
```
- Sanitize the fixture. Assert `SanitizedText` mentions all node IDs from all nested graphs.
- Assert nodes appear in the same order across two runs (determinism).

### 4. BTree — Malformed File (Graceful Failure)

**Fixture file:** No new fixture file needed — construct the content inline.

**Test (add to existing `BTreeComparisonSanitizerTests.cs`):**
```
MalformedFile_NoCSharpClass_ReturnsFallback
```
- Pass a string that is not a valid C# BTree file (e.g., `"this is not valid csharp"`) as the asset content.
- Assert `Sanitize()` does NOT throw.
- Assert the result contains an empty or single-line sanitized text, or that `Warnings.Count > 0`.

To test with inline content: create a temp file, write the malformed content to it, pass it to `Sanitize()` via an `AssetExportRequest` pointing at the temp file. Clean up after the test.

### 5. HSM — Parallel Regions + Global Transitions + Sub-BTree Sync Bindings

**Fixture file:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/Fixtures/ParallelHsm.cs`

Content: An HSM C# file with:
- A parallel state (two concurrent child states).
- A global transition (not scoped to a specific state).
- A sub-BTree binding with sync bindings (check existing HSM fixture for the pattern).

**Test (add to existing `HsmComparisonSanitizerTests.cs`):**
```
ParallelRegions_WithGlobalTransitions_SanitizesCorrectly
```
- Sanitize the fixture. Assert `SanitizedText` is non-empty and does not contain layout coordinates.
- Assert determinism (same output across two calls).

---

## TASK-C-33 — Error Handling Polish

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-33`
**Design refs:** §7.3, §5.3

### Step 1: Create ComparisonErrorMessages.cs

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonErrorMessages.cs`

```csharp
namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Centralizes all user-facing error and warning strings for the comparison feature.
/// Use these constants in UI and validators rather than inline strings.
/// See design section 5.3 and 7.3 for the full message catalogue.
/// </summary>
public static class ComparisonErrorMessages
{
    // Asset selection / validation
    public const string FileNotFound            = "File not found: {0}";
    public const string FileNotReadable         = "File cannot be read: {0}";
    public const string AssetKindMismatch       = "Asset kinds do not match: Version A is {0}, Version B is {1}.";
    public const string AssetIdMismatch         = "Asset IDs do not match. These files may represent different assets.";
    public const string SameFileTwice           = "Version A and Version B are the same file.";

    // Export / export builder
    public const string ExportBuilderNoSanitizer = "No sanitizer registered for asset kind: {0}";
    public const string ExportTooLargeForClipboard = "Export is {0:N0} bytes, which exceeds the 8 MB clipboard limit. Use Save to File instead.";

    // Companion discovery
    public const string NoMainFileFound         = "Could not find a main asset file matching the given asset ID in folder: {0}";
    public const string MultipleMainFilesFound  = "Multiple asset files with the same AssetId found in folder: {0}. Using the first match.";

    // LLM response parsing
    public const string TruncatedResponse       = "LLM response appears truncated. Re-run with a more capable model or smaller asset.";
    public const string UnrecoverableResponse   = "LLM response could not be parsed. Expected JSON with a 'changes' array.";
    public const string NoChangesInResponse     = "LLM reported no changes between the two versions.";
}
```

### Step 2: Wire into existing code

Replace inline message strings in these files with references to `ComparisonErrorMessages`:
- `AssetSelectionValidator.cs` — any hardcoded message strings
- `LlmResponseParser.cs` — the `TruncationWarning` constant (replace its content with `ComparisonErrorMessages.TruncatedResponse`)
- `PasteResponseModal.cs` — the TruncationText check (now uses `ComparisonErrorMessages.TruncatedResponse`)
- `ExportDeliveryModal.cs` — any error messages

Note: `ComparisonErrorMessages.FileNotFound` uses `{0}` placeholder (intended for `string.Format`). Do not change existing behavior — just consolidate strings.

### Step 3: Tests

**New file:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/ComparisonErrorMessagesTests.cs`

Tests:
- **AllPublicConstantsAreNonEmpty:** Use reflection to check all `public const string` fields in `ComparisonErrorMessages` are non-null and non-empty.
- **AssetIdMismatch_Validator_UsesCorrectMessage:** Validate two assets with different AssetIds. The `ValidationIssue` message equals (or contains) `ComparisonErrorMessages.AssetIdMismatch`.
- **TruncatedResponse_Parser_UsesCorrectMessage:** Parse a truncated response fixture. The `ComparisonResponse.Warnings` contains `ComparisonErrorMessages.TruncatedResponse`.

---

## TASK-C-34 — User-Facing Documentation

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-34`

Create the user guide. No code, just documentation.

**New file:** `.dev/visual-asset-comparison/USER-GUIDE.md`

The guide must cover all four use cases from §1.1:
1. **PR Review** — Reviewing an AI-generated edit before merging: export the two asset versions, paste into LLM, review structured output.
2. **AI-Agent Edit Audit** — Auditing what an AI agent changed in a BTree/HSM: export after the agent's edit, compare with the version before.
3. **Refactor Verification** — Confirming a manual refactor preserved intended behavior: export before/after, check "behavior" and "intent_shift" changes.
4. **Regression Hunt** — Investigating a behavioral regression between two git revisions: export both revisions' assets, look for "removal" and "behavior" changes.

Format:
- Title: `# Visual Asset Comparison — User Guide`
- One section per use case (## Use Case 1: PR Review, etc.)
- Each section: 1-2 sentence description, step-by-step workflow, example LLM prompt snippet.
- Section 5: `## Export → LLM → Paste Workflow` — generic end-to-end instructions
- Section 6: `## Severity Reference` — brief table of all 5 severities with descriptions

The guide does NOT require code tests. It is the manual gate deliverable for C-34.

---

## Mandatory Workflow

1. **Verify C-29, C-30, C-31:** Check implementation + confirm tests exist. Add missing tests if needed. ✅
2. **D-12 fix:** Update `PasteResponseModalState.Apply` + 3 new tests ✅
3. **D-09 fix:** Update `CompanionFileDiscovery.DiscoverFromFolder` + 1 new test ✅
4. **D-10 fix:** Add `ComparisonExportBuilder` disk fixture round-trip test ✅
5. **C-32:** Add 5 fixture files + 5 tests ✅
6. **C-33:** Create `ComparisonErrorMessages.cs` + wire + 3 new tests ✅
7. **C-34:** Write `USER-GUIDE.md` ✅
8. Full solution build: 0 errors ✅
9. All tests pass ✅

---

## Developer Insights (Answer in Report)

**Q1:** Did C-29, C-30, and C-31 already have tests, or did you need to add tests for any of them? If tests existed, cite the test method names.

**Q2:** For D-09 (`DiscoverFromFolder` ranking), did you find the `DiscoverFromFolder` method in `CompanionFileDiscovery.cs`? How many lines did the fix require?

**Q3:** For C-32 (fixture corpus), what was the BTree fixture format — actual `.cs` files with C# syntax, or synthetic test strings? How did you construct the "malformed file" test?

**Q4:** For C-33, did the existing code already use inline strings, or were some already defined as constants? How many inline string replacements were needed?

**Q5:** List any edge cases encountered and new debt items discovered (if any).

---

## Success Criteria

- [ ] C-29, C-30, C-31 verified + tests confirmed (no new code unless tests were missing)
- [ ] D-12: `PasteResponseModalState.Apply` updated, 3 new tests
- [ ] D-09: `DiscoverFromFolder` ranking fixed, 1 new test
- [ ] D-10: ExportBuilder disk fixture round-trip test added (2 assertions minimum)
- [ ] C-32: 5 new fixture files, 5 new tests
- [ ] C-33: `ComparisonErrorMessages.cs` created, strings centralized, 3 new tests
- [ ] C-34: `USER-GUIDE.md` with all 4 use cases + 2 additional sections
- [ ] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/..."` passes
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-07-REPORT.md`

---

## Reference Materials

- **Design §10.1 (test strategy):** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
- **Task details C-29 to C-34:** `.dev\visual-asset-comparison\TASK-DETAILS.md`
- **Existing sanitizer tests:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/`
- **Existing modal tests:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/`
- **LlmResponseParser constants:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs`
- **CompanionFileDiscovery:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs`
