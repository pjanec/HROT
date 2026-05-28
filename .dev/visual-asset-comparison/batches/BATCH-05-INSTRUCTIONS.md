# BATCH-05: UI Layer — Dialogs, Toolbar Wiring, and Response Matcher (Slices C-4 UI + C-5 UI)

**Batch Number:** BATCH-05
**Tasks:** TASK-C-19, TASK-C-10, TASK-C-13, TASK-C-18, TASK-C-15
**Slices:** C-4 (UI layer) + C-5 (response matcher + paste UI)
**Estimated Effort:** 16-20 hours
**Priority:** HIGH
**Dependencies:** BATCH-04 (C-11, C-12, C-14, C-16, C-17 done)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Skill:** `.github\skills\developer\SKILL.md`
2. **Design §7.1, §7.2, §7.3, §7.6:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md` — AssetSelectionDialog, ExportDeliveryModal, PasteResponseModal, ResponseAssetMatcher
3. **Task Details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-10, C-13, C-15, C-18, C-19
4. **ImGui patterns to follow:**
   - Modal pattern: `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs` (two-frame open: `bool _openXxxModal = true`, then `ImGui.OpenPopup(...)` + `ImGui.BeginPopupModal(...)`)
   - Toolbar button pattern: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` (`ImGui.Button()` + `ImGui.SameLine()`)
   - Test pattern for renderer-adjacent code: `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Renderers/VariableBindingBadgeRendererTests.cs`
5. **Existing types (study before implementing):**
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/CompanionFileDiscovery.cs` — used in C-10 dialog
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/AssetSelectionValidator.cs` — used in C-10 dialog
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonExportBuilder.cs` — used in C-15 pipeline
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/LlmResponseParser.cs` — used in C-18 paste
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ComparisonSessionState.cs` — populated in C-18
   - `Hrot/Editor/Hrot.Editor.AiShared/Comparison/IAssetComparisonSanitizer.cs` — types used throughout

### Test Execution

```powershell
dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj" -c Debug
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

### Report Submission

Submit to: `.dev\visual-asset-comparison\reports\BATCH-05-REPORT.md`

---

## KEY DESIGN CONSTRAINT: UI Classes Must Be Logic-Layer Testable

Since there is no ImGui rendering available in unit tests, ALL UI classes in this batch MUST follow the same pattern used throughout this codebase:
- Separate the **state + logic** from the **ImGui rendering**
- The state/logic is tested via direct method calls
- The `Render()` / `DrawUI()` method drives the state, but is not tested directly

Each dialog class must expose properties and methods that can be called in tests without ever calling `ImGui.*`. See existing examples in the codebase.

---

## Tasks

---

### TASK-C-19 — `ResponseAssetMatcher`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-19`
**Design refs:** §7.6

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/ResponseAssetMatcher.cs`

**API:**

```csharp
public static class ResponseAssetMatcher
{
    // Returns fraction [0.0, 1.0] of non-null elementIds in the response that resolve
    // against activeNodeIds.
    // Returns 1.0 (no mismatch) when the response has no non-null elementIds.
    public static double MatchScore(ComparisonResponse response, IReadOnlySet<string> activeNodeIds);

    // True when MatchScore < 0.5.
    public static bool IsLikelyMismatch(ComparisonResponse response, IReadOnlySet<string> activeNodeIds);
}
```

**Implementation notes:**
- Filter `response.Changes` to those with non-null `ElementId`.
- If none exist (e.g., all `intent_shift` changes), return 1.0 (no resolvable IDs = no mismatch).
- Otherwise, count how many non-null ElementIds exist in `activeNodeIds`.
- Return `(double)matches / total`.
- `IsLikelyMismatch`: returns `true` when score < 0.5.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ResponseAssetMatcherTests.cs`):**
- **Score = 1.0:** All ElementIds resolve. `IsLikelyMismatch = false`.
- **Score = 0.0:** None resolve. `IsLikelyMismatch = true`.
- **Score = 0.5:** 1 of 2 resolves. `IsLikelyMismatch = false` (0.5 is not < 0.5, so no dialog).
- **Score = 0.49:** 1 resolves out of some number that gives ratio < 0.5. `IsLikelyMismatch = true`.
- **All null elementIds:** Response has only `intent_shift` changes with `ElementId=null`. Score = 1.0. No mismatch.
- **Empty changes:** Response with no changes. Score = 1.0. No mismatch.

---

### TASK-C-10 — `AssetSelectionDialog`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-10`
**Design refs:** §7.1, §7.2

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/AssetSelectionDialog.cs`

**Dialog state model:**

```csharp
public sealed class AssetSelectionDialogState
{
    public string PathA { get; set; } = "";
    public string PathB { get; set; } = "";
    public bool Reversed { get; private set; }
    public string? ValidationError { get; private set; }
    public string? ValidationWarning { get; private set; }

    // Swaps A and B paths.
    public void Reverse() { ... }

    // Validates paths using AssetSelectionValidator. Returns null on success, error string on failure.
    public string? Validate(AssetKind expectedKind) { ... }

    // Builds the result records once validation passed.
    public AssetSelectionResult BuildResult(AssetKind expectedKind) { ... }
}

public sealed record AssetSelectionResult(
    AssetExportRequest VersionA,
    AssetExportRequest VersionB,
    bool Reversed);
```

**ImGui rendering class (`AssetSelectionDialog`):**

```csharp
public sealed class AssetSelectionDialog
{
    private readonly AssetSelectionDialogState _state = new();
    private bool _openPending;
    private bool _active;

    // Called once to request the dialog open.
    public void RequestOpen() { _openPending = true; }

    // Returns AssetSelectionResult when user confirms, null when still open or cancelled.
    // Must be called every ImGui frame.
    public AssetSelectionResult? Render(AssetKind expectedKind)
    {
        if (_openPending)
        {
            ImGui.OpenPopup("Compare With...##assetsel");
            _openPending = false;
            _active = true;
        }
        if (!_active) return null;

        var modalOpen = true;
        if (ImGui.BeginPopupModal("Compare With...##assetsel", ref modalOpen,
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            // ... render inputs for PathA, PathB, Reverse button, Validate button, Build button
            ImGui.EndPopup();
        }
        if (!modalOpen) _active = false;
        return null;  // or result if Build was clicked and validation passed
    }
}
```

**UI elements (per design §7.2):**
- Active asset chip showing the current asset path (read-only label, pre-populated as Version B)
- `ImGui.InputText` for Path A (Version A / older)
- `ImGui.InputText` for Path B (Version B / newer) — pre-filled with current asset path when opened from an editor
- "Reverse A<->B" button (swaps PathA and PathB)
- Validation error/warning displayed in `ImGui.TextColored` below the inputs (red for error, yellow for warning)
- "Validate" button (runs `AssetSelectionValidator.Validate()` on the two paths)
- "Build Comparison Export" button (disabled until validation passes; triggers the export pipeline)
- "Cancel" button

**Note on file-picking:** There is no native file dialog wrapper in this codebase. Use `ImGui.InputText` for path entry (users type or paste paths). The button layout per §7.2 is fine with text-only path entry for Phase 1.

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/AssetSelectionDialogTests.cs`):**

Test the `AssetSelectionDialogState` class directly (no ImGui rendering needed):

- **Reverse swaps paths:** Set PathA="A.cs", PathB="B.cs", call `Reverse()` → PathA="B.cs", PathB="A.cs".
- **Double reverse restores original:** After two `Reverse()` calls → back to original.
- **Validate with existing files (same kind):** Create two temp BTree `.cs` files in a temp dir. `Validate(AssetKind.BTree)` returns null (no error).
- **Validate with missing file:** Set PathA to a nonexistent path. `Validate(AssetKind.BTree)` returns a non-null error string.
- **BuildResult after successful validate:** Sets up `AssetExportRequest` records with correct `AssetMainFilePath` values.
- **Reversed flag propagated:** After `Reverse()`, `BuildResult(...)` has `Reversed=true`.

---

### TASK-C-13 — `ExportDeliveryModal`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-13`
**Design refs:** §4.5

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ExportDeliveryModal.cs`

**Modal state model:**

```csharp
public sealed class ExportDeliveryModalState
{
    public const int MaxClipboardBytes = 8 * 1024 * 1024;  // 8 MB threshold

    public string ExportText { get; }
    public string AssetName { get; }

    public ExportDeliveryModalState(string exportText, string assetName)
    {
        ExportText = exportText;
        AssetName = assetName;
    }

    // Returns null on success, error string on failure.
    public string? SaveToFile(string filePath)
    {
        try { File.WriteAllText(filePath, ExportText, System.Text.Encoding.UTF8); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    // Returns the text to copy to clipboard, or null if over the 8 MB threshold.
    public string? GetClipboardText()
        => System.Text.Encoding.UTF8.GetByteCount(ExportText) <= MaxClipboardBytes
            ? ExportText : null;

    // First 30 lines of the export text for the preview.
    public string GetPreviewText(bool showFull = false)
    {
        if (showFull) return ExportText;
        var lines = ExportText.Split('\n');
        var preview = string.Join('\n', lines.Take(30));
        return lines.Length > 30 ? preview + "\n[...] (Show full to see remaining lines)" : preview;
    }

    // Generates the default save filename: {AssetName}_comparison_{timestamp:yyyyMMdd_HHmmss}.txt
    public string GetDefaultFileName()
        => $"{AssetName}_comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
}
```

**ImGui rendering class (`ExportDeliveryModal`):**

```csharp
public sealed class ExportDeliveryModal
{
    private ExportDeliveryModalState? _state;
    private bool _openPending;
    private bool _showFull;
    private bool _active;
    private string _savePath = "";
    private string? _lastError;
    private bool _copyDisabled;

    // Called to open with the given export result.
    public void Open(string exportText, string assetName) { ... }

    // Returns true when the modal is still open, false when closed.
    // Must be called every ImGui frame.
    public bool Render() { ... }
}
```

**UI elements (per design §4.5):**
- Read-only scrollable preview of the export text (first 30 lines; "Show full" expander toggle)
- `ImGui.InputText` for save path + "Save to file" button
- "Copy to clipboard" button (`ImGui.SetClipboardText(text)`) — disabled with tooltip "Export exceeds 8 MB clipboard limit" when over threshold
- Save error displayed in red if the file write fails
- "Close" button

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/ExportDeliveryModalTests.cs`):**

Test the `ExportDeliveryModalState` class directly:

- **SaveToFile writes the export text:** Write to a temp file path. Assert the file exists and has the correct content.
- **SaveToFile returns error string on failure:** Pass a path in a non-existent directory (e.g., `Z:\DoesNotExist\file.txt`). Error string is non-null.
- **GetClipboardText returns text when under 8 MB:** Small export text → returns the text.
- **GetClipboardText returns null when over 8 MB:** Construct an 8 MB + 1 byte string → returns null.
- **GetPreviewText first 30 lines:** 40-line export text → preview contains exactly 30 lines + the "[...]" marker.
- **GetPreviewText show full:** `GetPreviewText(showFull: true)` returns the full text.
- **GetDefaultFileName format:** Assert result matches pattern `{AssetName}_comparison_\d{8}_\d{6}.txt`.

---

### TASK-C-18 — `PasteResponseModal`

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-18`
**Design refs:** §6.1

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/PasteResponseModal.cs`

**Modal state model:**

```csharp
public sealed class PasteResponseModalState
{
    public string PastedText { get; set; } = "";
    public string? ParseError { get; private set; }
    public bool SessionWasApplied { get; private set; }

    // Applies the pasted text. Returns true on success, false on parse error.
    // On success, populates the registry for the given assetId.
    public bool Apply(Guid assetId, ComparisonSessionRegistry registry)
    {
        var response = LlmResponseParser.Parse(PastedText);
        // A truncation-only warning (no changes) is treated as a parse error for UI purposes.
        if (response.Warnings.Count > 0 && response.Changes.Count == 0)
        {
            ParseError = response.Warnings[0];
            return false;
        }
        ParseError = null;
        var state = new ComparisonSessionState(assetId, response);
        registry.SetSession(state);
        SessionWasApplied = true;
        return true;
    }

    public void Reset() { PastedText = ""; ParseError = null; SessionWasApplied = false; }
}
```

**ImGui rendering class (`PasteResponseModal`):**

```csharp
public sealed class PasteResponseModal
{
    private readonly PasteResponseModalState _state = new();
    private readonly byte[] _textBuf = new byte[256 * 1024];  // 256 KB max paste
    private bool _openPending;
    private bool _active;

    public void RequestOpen() { _openPending = true; _state.Reset(); }

    // Returns true when the session was applied (modal should close or refresh).
    // Must be called every ImGui frame.
    public bool Render(Guid activeAssetId, ComparisonSessionRegistry registry)
    {
        if (_openPending)
        {
            ImGui.OpenPopup("Paste LLM Response##pastemod");
            _openPending = false;
            _active = true;
        }
        if (!_active) return false;

        var applied = false;
        var modalOpen = true;
        if (ImGui.BeginPopupModal("Paste LLM Response##pastemod", ref modalOpen,
            ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Multiline text input (the response text field)
            ImGui.InputTextMultiline("##lpastetext", _textBuf, (uint)_textBuf.Length,
                new System.Numerics.Vector2(600, 300));
            _state.PastedText = System.Text.Encoding.UTF8.GetString(_textBuf).TrimEnd('\0');

            if (_state.ParseError != null)
                ImGui.TextColored(new System.Numerics.Vector4(1, 0.3f, 0.3f, 1), _state.ParseError);

            if (ImGui.Button("Apply"))
            {
                if (_state.Apply(activeAssetId, registry))
                {
                    applied = true;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
        if (!modalOpen) _active = false;
        return applied;
    }
}
```

**Tests required (`Hrot.Editor.AiShared.Tests/Comparison/PasteResponseModalTests.cs`):**

Test the `PasteResponseModalState` class directly:

- **Apply with well-formed text populates registry:** Set `PastedText` to the §5.4 well-formed example. Call `Apply(assetId, registry)`. `Apply` returns true. `registry.GetSession(assetId)` is non-null. `SessionWasApplied = true`.
- **Apply twice replaces previous session:** Call `Apply` with different text for the same assetId. Registry returns the second session.
- **Apply with unrecoverable text returns false:** Set `PastedText` to `"{ \"summary\": \"abc\", \"changes\": [{ \"kind\":"`. `Apply` returns false. `ParseError` is non-null. Registry unchanged.
- **Apply with well-formed text keeps ParseError null:** Well-formed text → `ParseError` stays null after `Apply`.
- **Reset clears state:** After `Apply`, call `Reset()` → `PastedText = ""`, `ParseError = null`, `SessionWasApplied = false`.

---

### TASK-C-15 — "Compare with..." Toolbar Button in All Four Editors

**Full spec:** `.dev\visual-asset-comparison\TASK-DETAILS.md#task-c-15`
**Design refs:** §7.1

**Scope:** Add an `ImGui.Button("Compare with...")` to the toolbar of each of the four editors. When clicked, it opens the `AssetSelectionDialog`. After the dialog produces a result, it runs the comparison pipeline (`ComparisonExportBuilder.Build(...)`) and opens the `ExportDeliveryModal` with the result.

**Files to modify:**

1. **BTree editor:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/` — find the main canvas window class (look for a class with `DrawUI()` or similar that renders the BTree canvas toolbar). Add "Compare with..." button there.
2. **HSM editor:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` — same pattern.
3. **Blueprint editor:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` — add after the existing "Full Rebuild" button and separator.
4. **Blackboard editor:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — add the comparison button there.

**Before starting C-15:** Read each of the four editor's main window files to understand where the toolbar is rendered and what services are accessible. Use `grep_search` for the editor canvas render methods.

**Pipeline invoked when dialog confirms:**
```csharp
// 1. Dialog produced AssetSelectionResult with VersionA + VersionB requests
// 2. Get sanitizer from SanitizerRegistry
var sanitizer = _sanitizerRegistry.Get(expectedKind);
// 3. Build the export
var exportText = _exportBuilder.Build(sanitizer, result.VersionA, result.VersionB);
// 4. Open ExportDeliveryModal
_exportModal.Open(exportText, assetName);
```

**DI: How to get `SanitizerRegistry` and `ComparisonExportBuilder`:**
- Both are registered as singletons in `SharedAiEditorServiceCollectionExtensions.AddSharedAiEditor()`.
- Inject them into each editor window class via constructor parameter.
- Do NOT add them directly to `IEditorHostServices` — keep them separate.

**New companion classes needed:**
- `ComparisonToolbarAction` — a small coordinator class that holds the dialog + export modal + registry + builder, and orchestrates the pipeline when called from any of the four toolbar buttons. This avoids code duplication across the four editor windows.
  - File: `Hrot/Editor/Hrot.Editor.AiShared/Comparison/UI/ComparisonToolbarAction.cs`
  - Method: `Render(Guid activeAssetId, string activeAssetPath, AssetKind kind)` — call from each editor's toolbar `DrawUI()`.

**Tests required:**

Integration tests in `Hrot.Editor.AiShared.Tests/Comparison/ComparisonToolbarActionTests.cs`:

Test the `ComparisonToolbarAction` pipeline logic directly (no ImGui):

- **Pipeline produces non-empty export:** Set up `AssetSelectionDialogState` with two valid temp BTree asset paths, call `BuildResult()`, pass to `ComparisonExportBuilder.Build()` with a fake sanitizer. Assert the export text is non-empty and contains "VERSION A" and "VERSION B".
- **Export contains instruction block:** Assert the output starts with "You are comparing".
- **Export delivery state shows correct preview:** Construct `ExportDeliveryModalState` with the export text, assert `GetPreviewText()` returns the first 30 lines.

---

## Mandatory Workflow

1. **TASK-C-19:** Implement + test `ResponseAssetMatcher` → all tests pass ✅
2. **TASK-C-10:** Implement `AssetSelectionDialog` + `AssetSelectionDialogState` + tests → all tests pass ✅
3. **TASK-C-13:** Implement `ExportDeliveryModal` + `ExportDeliveryModalState` + tests → all tests pass ✅
4. **TASK-C-18:** Implement `PasteResponseModal` + `PasteResponseModalState` + tests → all tests pass ✅
5. **TASK-C-15:** Implement `ComparisonToolbarAction` + wire button into all 4 editors → integration tests pass ✅
6. Full solution build: 0 errors ✅

---

## Developer Insights (Answer in Report)

**Q1:** Where exactly in each of the 4 editor window files did you add the "Compare with..." button? Give file paths + line numbers.

**Q2:** `ComparisonToolbarAction` is intended to avoid code duplication. What services does it hold, and how is it constructed in each editor (DI vs. direct construction)?

**Q3:** Were there any DI complications when injecting `SanitizerRegistry` and `ComparisonExportBuilder` into the editor windows? How did you resolve them?

**Q4:** The `PasteResponseModalState.Apply` method treats a response with 0 changes + 1 warning as a parse error. Is this the right policy when the LLM returns an empty changes array (no changes detected, which is a valid result)?

**Q5:** List any edge cases or limitations in the UI logic. Suggest them as debt items if appropriate.

---

## Success Criteria

- [ ] TASK-C-19: `ResponseAssetMatcher` with 6 tests
- [ ] TASK-C-10: `AssetSelectionDialog` + state model with 6 tests
- [ ] TASK-C-13: `ExportDeliveryModal` + state model with 7 tests
- [ ] TASK-C-18: `PasteResponseModal` + state model with 5 tests
- [ ] TASK-C-15: "Compare with..." wired in all 4 editors + 3 integration tests
- [ ] `dotnet test "Hrot/Editor/Hrot.Editor.AiShared.Tests/..."` passes
- [ ] `dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4` — 0 errors
- [ ] Report submitted to `.dev\visual-asset-comparison\reports\BATCH-05-REPORT.md`

---

## Reference Materials

- **Design §7.1–7.6:** `.dev\visual-asset-comparison\Visual_Asset_Comparison_Detailed_Design.md`
- **Task details:** `.dev\visual-asset-comparison\TASK-DETAILS.md` — TASK-C-10, C-13, C-15, C-18, C-19
- **ImGui modal pattern:** `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`
- **Toolbar pattern:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs`
- **Existing comparison types:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/` (all files)
- **DI registration:** `Hrot/Editor/Hrot.Editor.AiShared/Di/SharedAiEditorServiceCollectionExtensions.cs`
