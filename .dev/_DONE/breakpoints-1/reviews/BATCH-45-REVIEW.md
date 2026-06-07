# BATCH-45 Review — UBP-P8T1 + P8T2 + P8T3 + P8T4

**Date:** 2025  
**Status:** APPROVED  
**Prior test count:** 72  
**New test count:** 89 (+17)

---

## Summary

P8T1-P8T4 implemented cleanly. 89/89 tests passing. Zero compiler warnings across all modified projects.

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Fixed `UpdateCondition` to remount compiled delegate after DTO update |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointConditionSummarizer.cs` | NEW — pure-logic static summarizer |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/BreakpointJsonClipboard.cs` | NEW — JSON serialize/deserialize helpers |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/PredicateBuilderState.cs` | NEW — pure-logic predicate mode state |
| `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj` | Added `Hrot.Diagnostics.Breakpoints` reference + `InternalsVisibleTo` for test project |
| `Hrot/Engine/Hrot.Presentation/Panels/Breakpoints/DataBreakpointManagerPanel.cs` | NEW — ImGui panel with data grid, toolbar, banner wiring |
| `Hrot/Engine/Hrot.Presentation/Windows/DataBreakpointManagerWindow.cs` | NEW — `ManagedWindow` subclass with `PerspectiveBound` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj` | Added `Hrot.Presentation` reference |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/ManagerWindowTests.cs` | NEW — 8 tests |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/PredicateBuilderStateTests.cs` | NEW — 5 tests |
| `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/JsonClipboardTests.cs` | NEW — 4 tests |

---

## Test Quality Assessment

### P8T1 — ManagerWindowTests (8 tests) ✓
- **`ManagerWindow_PerspectiveBound_WindowHasCorrectScopeAndPerspective`**: Instantiates real `DataBreakpointManagerWindow`, asserts `Scope == PerspectiveBound`, `OwningPerspective == "SimHost"`, `IsOpen == false`. **Directly validates the design requirement.**
- **`ManagerWindow_AddRow_AppendsBreakpointToManager`**: Calls internal `panel.AddBreakpoint()` seam, asserts `_manager.AllBreakpoints.Count == 1`. **Correct.**
- **`ManagerWindow_EnableCheckbox_TogglesManagerSetEnabled`**: Toggle off → assert `Enabled == false`, toggle on → assert `Enabled == true`. **Covers bidirectional toggle.**
- **`ManagerWindow_EnableAll_EnablesAllBreakpoints`**: 2 BPs disabled, `panel.EnableAll()` → all enabled. **Correct.**
- **`ManagerWindow_DisableAll_DisablesAllBreakpoints`**: 2 BPs enabled, `panel.DisableAll()` → all disabled. **Correct.**
- **Summarizer tests** (3 tests): null → "(none)", PropertyMatch → contains component name, Compound → contains operator+count. **Core format coverage.**

### P8T2 — PredicateBuilderStateTests (5 tests) ✓
- **`PredicateBuilder_SwitchingMode_DiscardsAndOpensNewSession`**: Switch Component→BehaviorParam; assert new DTO instance of `BehaviorParamPredicateDto`. **Verifies mode discard.**
- **`PredicateBuilder_SwitchingToSameMode_IsNoOp`**: Same-mode switch keeps same DTO reference. **Identity check via `Assert.Same`.**
- **`PredicateBuilder_CompileAndApply_RemountsDelegate`**: Registers BP with `PropertyMatchDto`, applies `BehaviorParamPredicateDto`; asserts condition updated. **Validates the `UpdateCondition` fix.**
- **`PredicateBuilder_LoadBreakpoint_InfersMode`**: Load compound BP → inferred mode is `Compound`. **Mode inference coverage.**
- **`PredicateBuilder_AllModes_ProduceExpectedDtoType`**: Iterates all `PredicateMode` enum values, switches, asserts non-null DTO. **Exhaustive enum coverage.**

### P8T3 — JsonClipboardTests (4 tests) ✓
- **`JSON_CopyPaste_RoundTrip_PreservesAllFields`**: Compound with `PropertyMatchDto` (nested `NumericPredicateDto`), `BehaviorParamPredicateDto`, `ExternalHitTagPredicateDto`, `ReadOnlyChildIndices`. Asserts all fields preserved after round-trip. **Full polymorphic type coverage.**
- **`JSON_TryDeserialize_InvalidJson_ReturnsNull`**: Malformed JSON → null. **Error handling.**
- **`JSON_TryDeserialize_UnknownType_ReturnsNull`**: Unknown `$type` discriminator → null. **Graceful failure.**
- **`JSON_Serialize_ExternalHitTag_ProducesCorrectDiscriminator`**: Verifies `ExternalHitTag` discriminator appears in JSON. **Validates polymorphic serialization is working for P7's new DTO.**

### P8T4 — Temporal banner wiring
No new tests needed (P3T3 already covers banner logic). `DataBreakpointManagerPanel.DrawBanner()` wires `TemporalStatusBannerPanel.Draw()` at the bottom of `DrawContent()`. Verified via code inspection.

---

## Key Implementation Notes

1. **`UpdateCondition` fix**: Now calls `UnmountDelegate(id)` + conditionally `TryMountDelegate(id, updated)`. This is required for `PredicateBuilder_CompileAndApply_RemountsDelegate` to work.

2. **Test seams via `internal` methods**: `AddBreakpoint()`, `ToggleEnabled()`, `EnableAll()`, `DisableAll()`, `RemoveSelected()` on `DataBreakpointManagerPanel` are `internal`. `InternalsVisibleTo` in `Hrot.Presentation.csproj` exposes them to the test project. This is the correct pattern (no ImGui needed).

3. **`DataBreakpointManagerWindow` is separate from `DataBreakpointManagerPanel`**: The window delegates `DrawClientArea()` to the panel, following the established pattern from `ArchitectureDiagnosticsWindow` and `FdpEntityInspectorWindow`.

---

## APPROVED — proceed to BATCH-46
