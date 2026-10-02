# BATCH-11 REPORT — FlowControl composite node color (gray → orange)

**Date:** 2026-06-12 | **Branch:** `blueprint-integ-1`

## Result

`NodeCategory.FlowControl` header color changed from gray `(0.20, 0.20, 0.20)` to amber-orange `(0.85, 0.45, 0.12)`. No other categories affected.

## Seam changed

| File | Line | Change |
|------|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Adapters/EngineEditorTheme.cs` | 60 | `NodeCategory.FlowControl => new Vector4(0.20f, 0.20f, 0.20f, 1f)` → `new Vector4(0.85f, 0.45f, 0.12f, 1f)` |

**Why this is the right spot:** `EngineEditorTheme` is the only `IEditorTheme` implementation injected via `AiEditorAdapterBundle` into all three AI editors (BTree, HSM, Blueprint). No BTree-specific theme exists. `HsmEditorTheme` exists but delegates non-Custom categories to `DefaultTheme`, which already had orange for `FlowControl` since before BATCH-11.

**Least-invasive:** One literal changed. Same format as the surrounding category colors. No new constants, no new types.

## Tests

**3 new headless tests added** in `Hrot/Editor/Hrot.Editor.AiShared.Tests/Adapters/AIE004_EngineEditorThemeTests.cs`:

| Test | What it asserts |
|------|----------------|
| `EngineEditorTheme_GetCategoryHeaderColor_FlowControl_IsOrange` | Exact value: `(0.85, 0.45, 0.12, 1.0)` |
| `EngineEditorTheme_GetCategoryHeaderColor_FlowControl_DiffersFromComment` | FlowControl ≠ Comment (the catch-all gray) |
| `EngineEditorTheme_GetCategoryHeaderColor_Function_Unchanged` | Guard: `Function` still `(0.07, 0.30, 0.60)` |

All 18 AIE004 tests pass (`Failed: 0`). The wider AiShared.Tests project (1062 tests) has 1 pre-existing flaky failure in `Batch14RefactorTests` (unrelated).

## Build

```
dotnet build Hrot/Editor/Hrot.Editor.AiShared.Tests
→ 0 errors, 0 warnings
```

## Blueprint/HSM impact

- **Blueprint**: Uses `EngineEditorTheme` (shared). `FlowControl` nodes in Blueprint are exec/flow nodes — orange is the expected color and matches Unreal conventions. The `DefaultTheme` (from `NodeEditor.Core`) already maps `FlowControl` to orange (`Rgb(0xD3, 0x54, 0x00)`).
- **HSM**: Historically `HsmEditorTheme` delegated non-Custom categories to `DefaultTheme` (which already had orange). If HSM now uses `EngineEditorTheme` from the adapter bundle, it gets the same orange. Either way, no regression — both themes now agree on FlowControl = orange.
- **BTree**: The primary beneficiary. Composite nodes (Root/Sequence/Selector/ObserverSelector/Parallel) mapped to `NodeCategory.FlowControl` now render with a distinct orange header instead of blending into the gray background.

## Visual gate note

The final hue `(0.85, 0.45, 0.12)` is a warm amber-orange. The lead will confirm the exact shade visually at REVIEW-BT-2; this value can be tuned trivially.
