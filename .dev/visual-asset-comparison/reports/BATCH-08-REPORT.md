# BATCH-08 Report: Remaining Debt Resolution (D-05, D-06, D-07, D-16)

**Date:** 2026-05-29
**Status:** COMPLETED

---

## Summary Table

| Task | Description | Files Modified | Status |
|------|-------------|----------------|--------|
| D-05 | HSM stableId + visualId same state, no confusion | `HsmComparisonSanitizerTests.cs` | Done |
| D-06 | Blackboard `AssetId:` header form test | `BlackboardComparisonSanitizerTests.cs` | Done |
| D-07 | HSM 3-level nested Child test | `HsmComparisonSanitizerTests.cs` | Done |
| D-16 | Per-row comparison decoration in VariablesPanelControl | `VariablesPanelControl.cs`, `BlackboardAuthoringWindow.cs` | Done |

---

## Test Counts

| Test Suite | Before | After |
|------------|--------|-------|
| `Hrot.Hsm.Editor.Tests` | 251 | 253 (+2: D-05, D-07) |
| `Hrot.Editor.AiShared.Tests` | 536 | 537 (+1: D-06) |
| **Total** | **787** | **790** |

---

## Developer Insights

### Q1 (D-05): HSM stableId + visualId fixture format

The HSM C# format uses `stableId: new Guid("aa050000-...")` as a named parameter on `builder.State(...)` and `visualId: new Guid("cc050000-...")` as a named parameter on `.GoTo(...)`. Comments are sourced from the Layout method's `.State(guid, pos, comment: "...")` and `.Transition(guid, points, comment: "...")` entries, not from the builder code itself. The sanitizer hoists those comments above the corresponding builder lines and then strips the entire `[HsmLayout]` method.

Fixture snippet:
```csharp
var patrol = builder.State("Patrol", stableId: new Guid("aa050000-0000-0000-0000-000000000001"));
patrol.On("EnemySpotted").GoTo("Chase", visualId: new Guid("cc050000-0000-0000-0000-000000000001"));
// Layout:
.State("aa050000-...", new Vector2(100f, 100f), comment: "patrol area")
.Transition("cc050000-...", new Vector2[] { ... }, comment: "enemy spotted")
```

Assertions verified: `// patrol area` appears exactly once; `.GoTo("Chase"` appears exactly once; two runs are byte-identical.

### Q2 (D-06): How BlackboardComparisonSanitizer treats `AssetId:` vs `OwningAssetId:`

The sanitizer does NOT strip either header form from the file content. It wraps the full raw text with `// === Inline blackboard ===` and leaves the header lines verbatim. Both `AssetId:` and `OwningAssetId:` are handled equally in `ExtractAssetId()` which scans lines for either prefix to populate metadata. The test confirms that when `AssetId:` is the header, the GUID is correctly extracted into `result.Metadata.AssetId` and the header line is preserved in `result.SanitizedText`.

### Q3 (D-07): 3-level nested Child scanner behavior

The sanitizer handled 3 levels without confusion on the first attempt. The brace-depth scanner in `HsmComparisonSanitizer` correctly tracked nested lambdas (3 levels of `stateX.Child("...", sbN => { ... }, stableId: ...)`) and emitted all four state names in the output. No assertions failed during development. All four names (`"StateA"`, `"StateB"`, `"StateC"`, `"StateD"`) were present in the sanitized text, and the two-run determinism check passed.

### Q4 (D-16): Callers of DrawSingle / DrawDual

Two callers found:
- `BlackboardAuthoringWindow.cs` -- `_variablesControl.DrawSingle(section)` -- updated to pass `rowDec` callback.
- `BlueprintVariablesWindow.cs` -- `_variablesControl.DrawDual(paramsSection, stateSection)` -- no change needed; the new `rowDecoration` parameter defaults to `null`, so the existing call compiles without modification.

The `BlueprintVariablesWindow` does not have a comparison session registry, so passing no decoration callback is correct behavior for that window.

### Q5: New debt items discovered

None significant. One observation:

- **D-17 (P3):** `BlueprintVariablesWindow` could benefit from the same per-row decoration callback if blueprint comparison sessions are ever introduced for blackboard variables in that context. No action needed now.

---

## Build + Test Output

```
Build succeeded.   (IOS-IG-SimHost.sln, Debug, --no-restore)

Hrot.Hsm.Editor.Tests:
  Passed!  - Failed: 0, Passed: 253, Skipped: 0, Total: 253, Duration: 232 ms

Hrot.Editor.AiShared.Tests:
  Passed!  - Failed: 0, Passed: 537, Skipped: 0, Total: 537, Duration: 11 s
```

---

## Changes by File

### `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/HsmComparisonSanitizerTests.cs`
- Added `StableId_And_VisualId_SameState_NeitherConfused` (D-05): verifies no comment duplication and determinism when a state has both a Layout comment and a visualId transition.
- Added `ThreeLevelNestedChild_AllLevelsExtracted` (D-07): verifies all 4 state names survive 3-level Child nesting.
- Added `CountOccurrences` private static helper used by D-05.

### `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/BlackboardComparisonSanitizerTests.cs`
- Added `AssetIdHeader_Form_SanitizesCorrectly` (D-06): verifies the `AssetId:` header is handled correctly (non-empty output, section label present, GUID extracted into metadata, no warnings).

### `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`
- Added `using Hrot.Editor.AiShared.Comparison;`.
- Added optional `Func<string, FieldDecoration?>? rowDecoration = null` parameter to `DrawSingle`, `DrawDual`, `DrawSection`, `DrawTable`.
- In `DrawTable` row loop: after `ImGui.TableNextRow()`, invoke the callback and apply `ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, rowColor)` with a tint color matching the decoration kind (added=green, removed=red, retyped=blue, renamed=yellow).

### `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
- Replaced the old separate `// Comparison decorations` section (separate text list below the table) with a `Func<string, FieldDecoration?>?` callback built from the session registry.
- Passed the callback as `rowDec` to `_variablesControl.DrawSingle(section, rowDec)`.
- Per-row `TableSetBgColor` in `VariablesPanelControl` now replaces the old post-table text annotation block.
