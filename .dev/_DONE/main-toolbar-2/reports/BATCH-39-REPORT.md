# BATCH-39 REPORT — MTB2-T8 (model layer): `INameFolderDialog` + `KnownSubfolders` helper

**Date:** 2026-06-12  
**Branch:** blueprint-integ-1  
**Task:** MTB2-T8 part 1 (DBT-A3)  
**Model:** pro (prescribed)

---

## Summary

Added the pure/headless model layer for the New-Asset/Save-As name+folder modal:

1. **`INameFolderDialog`** — new interface defining the common surface (`Title`, `Name`, `FolderPicker`, `CanConfirm`, `Confirm`) for the generic name+folder modal.
2. **`NewAssetDialog`** — now implements `INameFolderDialog` with `Title => $"New {Kind}"`.
3. **`SaveAsDialog`** — now implements `INameFolderDialog` with `Title => "Save As"`.
4. **`AssetFolderDerivation.KnownSubfolders`** — new static helper that derives distinct logical subfolder relpaths from catalog assets' `AssetRelPath` directory parts (NOT the filesystem). Always includes `""` (root). Ordered, case-insensitive-distinct.
5. **6 exact named tests** — all pass; no behavior change to existing code.

No ImGui in this batch (the modal renderer is BATCH-40).

---

## Files touched (exactly as prescribed)

### NEW: `Hrot/Editor/Hrot.Editor.AiShared/Recipes/INameFolderDialog.cs`

```csharp
public interface INameFolderDialog
{
    string Title { get; }
    string Name { get; set; }
    FolderPickerState FolderPicker { get; }
    bool CanConfirm();
    ConfirmResult Confirm(Action<IEditableAsset>? onCreated = null);
}
```

### EDIT: `Hrot/Editor/Hrot.Editor.AiShared/Recipes/NewAssetDialog.cs`

| Change | Detail |
|--------|--------|
| Class declaration | `: INameFolderDialog` added |
| `Title` property | `public string Title => $"New {Kind}";` — added after `Kind` |

All existing members (`Name`, `FolderPicker`, `CanConfirm`, `Confirm`) already matched the interface. No rename, no behavior change.

### EDIT: `Hrot/Editor/Hrot.Editor.AiShared/Recipes/SaveAsDialog.cs`

| Change | Detail |
|--------|--------|
| Class declaration | `: INameFolderDialog` added |
| `Title` property | `public string Title => "Save As";` — added after `Kind` |

All existing members already matched. No explicit interface members needed — no member name differed.

### NEW: `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetFolderDerivation.cs`

```csharp
public static class AssetFolderDerivation
{
    public static IReadOnlyList<string> KnownSubfolders(
        IReadOnlyList<IEditableAsset> assets,
        AssetKind kind,
        Func<AssetKind, string?> baseFolderResolver);
}
```

For each asset of `kind`: `rel = AssetRelPath.RelPath(asset, baseFolderResolver(kind))`; take the directory part (everything before the last `/`, else `""`); collect distinct (`OrdinalIgnoreCase`), include `""` (root). Mirrors the subfolder extraction in `AssetPickerSource.ToEntry`. Sorted by `OrdinalIgnoreCase`.

### NEW: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetFolderDerivationTests.cs`

| Test | Description |
|------|-------------|
| `KnownSubfolders_ReturnsDistinctDirsForKind` | Blueprint assets at relpaths `AI/Foo`, `AI/Bar`, `Root` ⇒ result contains `"AI"` and `""` (root), `"AI"` appears once (distinct). Sorted `""` before `"AI"`. |
| `KnownSubfolders_FiltersByKind` | Catalog with Blueprint + HSM assets, `kind = Blueprint` ⇒ only `"AI"` and `""` present, `"Combat"` (HSM) absent. |
| `KnownSubfolders_IncludesRoot_WhenAssetsAtRoot` | Asset with no subfolder ⇒ single result `[""]`. |
| `KnownSubfolders_EmptyKind_YieldsRootOnly` | No assets of the kind ⇒ `[""]` (just root). |

### NEW: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Recipes/NameFolderDialogTests.cs`

| Test | Description |
|------|-------------|
| `NewAssetDialog_ImplementsINameFolderDialog` | Assignable to `INameFolderDialog`; `Title == "New Blueprint"`; `Name` round-trips via interface; `FolderPicker` is the same instance. |
| `SaveAsDialog_ImplementsINameFolderDialog` | Assignable; `Title == "Save As"`; `Name == "SourceAsset"`; `FolderPicker` exposed. |

---

## Build result

```
dotnet build Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj
Build succeeded. 0 Warning(s) 0 Error(s)

dotnet build Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj
Build succeeded. 0 Warning(s) 0 Error(s)
```

---

## Test results (BLUEPRINT_REGENERATE_SNAPSHOTS not set)

### Filtered run

```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "FullyQualifiedName~AssetFolderDerivation|FullyQualifiedName~NameFolderDialog"
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 11 ms
```

All 6 named tests pass:
- `AssetFolderDerivationTests.KnownSubfolders_ReturnsDistinctDirsForKind`
- `AssetFolderDerivationTests.KnownSubfolders_FiltersByKind`
- `AssetFolderDerivationTests.KnownSubfolders_IncludesRoot_WhenAssetsAtRoot`
- `AssetFolderDerivationTests.KnownSubfolders_EmptyKind_YieldsRootOnly`
- `NameFolderDialogTests.NewAssetDialog_ImplementsINameFolderDialog`
- `NameFolderDialogTests.SaveAsDialog_ImplementsINameFolderDialog`

### Full suite (stability-filtered)

```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed!  - Failed:     0, Passed:  1078, Skipped:     0, Total:  1078, Duration: 5 s
```

Full suite stays green — no regressions.

---

## Design decisions

- **`AssetFolderDerivation.KnownSubfolders` always returns `""` (root):** per the batch spec "returns `[""]`" for empty kind. This mirrors how `FolderPickerState` treats root as always present.
- **Sort: `OrdinalIgnoreCase`**: matches the batch spec "Ordered, case-insensitive-distinct" and the `AssetRelPath` normalization (already uses `/` separators).
- **Null guards on `assets` and `baseFolderResolver`**: consistent with existing patterns in the codebase (e.g., `AssetRelPath.RelPath` guards `asset`). Prevents NRE in callers that pass unvalidated data.
- **No explicit interface members needed for `SaveAsDialog`**: all required members already matched exactly by name and signature.

---

## Deviations

None. All changes exactly as prescribed.

---

## Known issues

None.

---

## Integration notes

- `INameFolderDialog` is consumed by the ImGui modal renderer in BATCH-40 — it provides the `Title` for the window header.
- `AssetFolderDerivation.KnownSubfolders` feeds `FolderPickerState` construction with catalog-derived subfolders rather than filesystem scanning, keeping the modal headless-testable.
- The `baseFolderResolver` parameter follows the same pattern as `AssetPickerSource` constructor, allowing deterministic test injection.
