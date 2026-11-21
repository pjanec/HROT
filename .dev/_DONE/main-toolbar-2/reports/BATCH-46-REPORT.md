# BATCH-46 Report — BUG-A12: Perspective-scoped Save target

**Status:** Done — build 0 warnings, all tests green.
**Live GUI confirmation:** user's responsibility.

---

## Bug

`shell.save` / `shell.saveAs` read `docManager.Active` (the globally last-activated document)
in the non-scenario branch. Opening an HSM then switching to the Blueprint perspective left
`Active` pointing at the HSM doc, so Save stayed enabled and saved the unrelated HSM.

---

## Resolver design

Added an optional `Func<AiDocument?>? resolveActiveDocument = null` parameter (last on the
signature) to `ShellSaveCommands.Register`. When supplied:

- **`IsEnabled`** for `shell.save` and `shell.saveAs` returns `resolveActiveDocument() != null`
  (instead of `docManager.Active != null`).
- **Handlers** call `resolveActiveDocument()` to obtain the save target; if it returns `null`
  the handler is an early-return no-op.

When `resolveActiveDocument` is `null` the old `docManager.Active` path is used unchanged
(back-compat; all existing tests that omit the seam remain green).

The scenario branch is untouched — `isScenarioContext` is checked first and short-circuits
before the resolver is ever invoked.

---

## Traced perspective → kind → doc path

```
windowManager.CurrentPerspective          // e.g. "Blueprint", "BTree", "HSM", "Editor"
    │
    ▼ (switch in ResolveDocumentForCurrentPerspective)
AssetKind?                                // "Blueprint"→Blueprint, "BTree"→BTree, "HSM"→Hsm
    │                                     // any other (incl. "Editor")→null → return null
    ▼
docManager.OpenDocuments                  // IReadOnlyList<AiDocument>
    │  iterate, match doc.Kind == targetKind, take last match
    ▼
AiDocument? match                         // the open doc for that perspective, or null
```

Key facts verified in source:

- `WindowManager.CurrentPerspective` is the live string queried at every `IsEnabled` /
  handler invocation — no stale capture.
- `AssetKindExtensions.ToPerspectiveName()` defines the canonical forward map
  (`AssetKind.Hsm` → `"HSM"`, etc.). The reverse switch in the helper is the only reverse
  mapping needed and is kept co-located with the resolver.
- `WindowManagerPerspectiveSwitcher.OnPerspectiveChanged` already uses the same
  "iterate OpenDocuments, take last match by kind" pattern — the resolver mirrors it exactly.
- "Editor" perspective and any unknown key → `null` → scenario branch handles it / no-op.

`describeActiveTarget` in `EditorSubsystem` is updated to call the same helper, so the
dynamic label always names the same document that Ctrl+S will save (or shows plain "Save"
when nothing is targetable).

---

## `ShellSaveCommands` changes

File: `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`

- Added `Func<AiDocument?>? resolveActiveDocument = null` optional parameter (after
  `describeActiveTarget`).
- `shell.save` `IsEnabled`: `resolveActiveDocument != null ? resolveActiveDocument() != null : docManager.Active != null`
- `shell.save` handler: `var doc = resolveActiveDocument != null ? resolveActiveDocument() : docManager.Active;`
- `shell.saveAs` `IsEnabled`: same pattern as save.
- `shell.saveAs` handler: same pattern as save.
- `shell.saveAll`, `scenario.save`, `scenario.saveAs`: unchanged.

---

## `EditorSubsystem` changes

File: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

- Added private static `ResolveDocumentForCurrentPerspective(WindowManager, AiDocumentManager?)`
  helper that implements the perspective→kind→doc path.
- `ShellSaveCommands.Register(...)` call: added `resolveActiveDocument:` argument wired to
  the helper.
- `describeActiveTarget` lambda updated to call the same helper (was `_aiDocumentManager?.Active`).

---

## New test

File: `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs`

**`Save_ResolverReturnsNull_DisabledAndNoOp_EvenWhenActiveExists`**

- Opens an HSM doc → `docManager.Active` is non-null.
- Supplies `resolveActiveDocument: () => null` (simulates Blueprint perspective, no Blueprint doc open).
- Asserts `shell.save.IsEnabled() == false`, `shell.saveAs.IsEnabled() == false`.
- Invokes both handlers; asserts HSM save delegate NOT called, `requestSaveAs` NOT called.
- Asserts HSM doc remains dirty.

---

## Test results

```
SaveCommands (filtered):  Failed: 0, Passed: 15, Skipped: 0  (14 existing + 1 new)
Hrot.Editor.Tests:        Failed: 0, Passed: 186, Skipped: 0
Build Hrot.Editor.csproj: 0 Errors, 0 Warnings
```

---

## Files changed

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs` | Add `resolveActiveDocument` seam; use in `shell.save` and `shell.saveAs` IsEnabled + handlers |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | Add `ResolveDocumentForCurrentPerspective` helper; wire `resolveActiveDocument` and fix `describeActiveTarget` |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Documents/SaveCommandsTests.cs` | Add `Save_ResolverReturnsNull_DisabledAndNoOp_EvenWhenActiveExists` test |
