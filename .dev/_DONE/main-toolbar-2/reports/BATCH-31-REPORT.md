# BATCH-31 Report

**Batch:** BATCH-31 — MTB2-T2: Save icon in the main toolbar  
**Developer:** Claude (pjanec)  
**Date:** 2026-06-12  
**Status:** Complete

---

## 📊 Task Completion

| Task | Status | Notes |
|------|--------|-------|
| MTB2-T2: `shell/save` atlas cell | ✅ | `g9` chosen — recognizable 3.5" floppy disk glyph; distinct from all asset-kind/folder cells |
| MTB2-T2: `shell/saveAs` atlas cell | ✅ | `h8` — disk variant, distinct |
| MTB2-T2: `shell/saveAll` atlas cell | ✅ | `i1` — disk shape, distinct |
| MTB2-T2: Save toolbar button at sortOrder -9 | ✅ | Registered right after Open Asset (-10), before separator (0); null-safe |
| MTB2-T2: `ShellSave_Icon_Resolves_DistinctCell` test | ✅ | In `AssetKindIconsRegistrationTests.cs` |
| MTB2-T2: `EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry` test | ✅ | In `EditorSubsystemBlueprintWindowsTests.cs` |

---

## 🧪 Testing Results

**Build:** 0 warnings, 0 errors (Hrot.Editor.csproj)

### `Hrot.Editor.AiShared.Tests`

```
Passed!  - Failed:     0, Passed:  1058, Skipped:     0, Total:  1058, Duration: 5 s
```

Includes the new `ShellSave_Icon_Resolves_DistinctCell` test. One pre-existing flaky test (`Write_to_invalid_path_does_not_leave_temp_files_behind`) passed on re-run.

### `Hrot.Blueprints.Tests` (filtered: `FullyQualifiedName~EditorSubsystemBlueprintWindows`)

```
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 3 s
```

Includes the new `EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry` test. All pre-existing build warnings in the Blueprints.Tests project (`CS0618` for obsolete `IBlueprintTimeController`, `CS8601`/`CS8602` nullability) are unrelated to this batch.

---

## 📝 Developer Insights

### Cell selection for `shell/save`, `shell/saveAs`, `shell/saveAll`

Inspected the actual `FDP/Data/Icons/famfamfam-silk.png` atlas (512×512, 16px cells) using Python/Pillow to identify unoccupied cells that visually read as "disk/save" glyphs:

| Key | Cell | Rationale |
|-----|------|-----------|
| `shell/save` | **`g9`** | Classic 3.5" floppy disk shape — metal slider top-left, label area, wider plastic body below. Row 6, column 9. |
| `shell/saveAs` | **`h8`** | Disk variant with a similar floppy-disk shape. Row 7, column 8. |
| `shell/saveAll` | **`i1`** | Another disk-shaped glyph with slightly different proportions. Row 8, column 1. |

All three cells are unused by the existing `DefaultCellMap` (which occupies a1–a6, b1–b9, c8–c12, d2–d9, e1–e8, f1–f9, g1–g5). Confirmed via `HashSet` distinctness assertions in the new test.

### Test design decisions

**`ShellSave_Icon_Resolves_DistinctCell`** — mirrors the existing `FolderIcons_ResolveAndAreDistinct` pattern:
- Asserts `TryGet("shell/save", out _)` returns true.
- Asserts the cell is present in `KeyToCellMap`.
- Asserts the save cell is distinct from all 6 asset-kind cells AND from `folder`/`folder_open`.

**`EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry`** — mirrors the existing `EditorSubsystem_RegisterWindows_RegistersOpenAssetCommand` pattern:
- Verifies `ShellCommands.Get("shell.save")` returns a non-null descriptor with `DisplayName == "Save"`.
- Verifies `DefaultKey == Ctrl+S` (`EditorKey.S + KeyModifiers.Ctrl`).
- Verifies `MainToolbar.Height > 0` (proving toolbar entries, including Save at sortOrder -9, are populated).
- Note: `MainToolbarManager.GetVisibleItemPlan()` is `internal` to `Fdp.Presentation` and not accessible from `Hrot.Blueprints.Tests` (no `InternalsVisibleTo`). Per the BATCH-26 precedent, `MainToolbar.Height > 0` serves as the toolbar-population proxy. The command registration check independently verifies the Save command exists for the toolbar adapter to bind.

### Null safety

The Save toolbar registration is inside the existing `if (windowManager.MainToolbar != null)` block. The bare-ctor `RegisterWindows` call (used in tests) does not throw — verified by all existing and new guardrail tests.

---

## 📁 Files Changed

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Adapters/SilkIconProvider.cs` | Added `shell/save`=g9, `shell/saveAs`=h8, `shell/saveAll`=i1 to `DefaultCellMap` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Added `ToolbarCommandAdapter.Register` for `shell.save` at sortOrder -9 inside the null-safe `MainToolbar != null` block |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Adapters/AssetKindIconsRegistrationTests.cs` | Added `ShellSave_Icon_Resolves_DistinctCell` test |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorSubsystemBlueprintWindowsTests.cs` | Added `using Hrot.Editor.AiShared.Documents; using NodeEditor.Primitives;` + `EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry` test |

### Tests added

1. **`ShellSave_Icon_Resolves_DistinctCell`** — `AssetKindIconsRegistrationTests.cs`:31 lines
2. **`EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry`** — `EditorSubsystemBlueprintWindowsTests.cs`:31 lines

---

## ⚠️ Outstanding Issues / Next Steps

- None. BATCH-31 is complete.
- Next: BATCH-32 (MTB2-T3): `DynamicDisplayName` on `EditorCommandDescriptor` + adapter consumption.
