# BATCH-32 Report

**Batch:** BATCH-32 (MTB2-T3)  
**Developer:** pjanec (Claude)  
**Date:** 2026-06-12  
**Status:** Complete

---

## 📊 Task Completion

| Task | Status | Notes |
|------|--------|-------|
| MTB2-T3: `DynamicDisplayName` on `EditorCommandDescriptor` | ✅ | Trailing optional `Func<string>? DynamicDisplayName = null` added after `IsChecked` |
| `MenuItemNode.DynamicLabel` + `ResolveLabel()` | ✅ | Property + pure accessor added; falls back to `Name` when null |
| `WindowManager.cs` leaf-render sites use `ResolveLabel()` | ✅ | Both checkable (L537) and plain-action (L548) sites updated |
| `MenuCommandAdapter.ApplyLeafNode` wires `DynamicLabel` | ✅ | Sets `node.DynamicLabel = descriptor.DynamicDisplayName` |
| `ToolbarCommandAdapter.ResolveTooltip` seam | ✅ | `public static string ResolveTooltip(IEditorCommands, string)` added |
| `RenderEntry` tooltip calls `ResolveTooltip` | ✅ | Inline tooltip replaced with `ResolveTooltip(commands, commandId)` |
| L114 checkable-no-icon text fallback uses `DynamicDisplayName` | ✅ | `descriptor.DynamicDisplayName?.Invoke() ?? descriptor.DisplayName` |
| 4 named tests | ✅ | All pass; assert real resolved strings |
| Build 0 warnings | ✅ | `Fdp.Presentation.csproj` builds clean |
| Tests Failed: 0 | ✅ | Both suites green |

---

## 📁 Files Changed

| File | Change |
|------|--------|
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Action/IEditorCommands.cs` | Added `Func<string>? DynamicDisplayName = null` as trailing optional param to `EditorCommandDescriptor` record |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/GlobalMenuRegistry.cs` | Added `DynamicLabel` property + `ResolveLabel()` method to `MenuItemNode` |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` | Two leaf-render sites: `child.Name` → `child.ResolveLabel()` |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MenuCommandAdapter.cs` | `ApplyLeafNode`: wires `node.DynamicLabel = descriptor.DynamicDisplayName` |
| `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ToolbarCommandAdapter.cs` | Added `ResolveTooltip()` public static seam; `RenderEntry` calls it; L114 text fallback uses `DynamicDisplayName` |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/MenuCommandAdapterTests.cs` | Added 3 tests (MTB2-T3) |
| `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/ToolbarCommandAdapterTests.cs` | Added `using NodeEditor.Primitives;` + 1 test (MTB2-T3) |

---

## 🧪 Tests Added

### MenuCommandAdapterTests.cs (3 new)

| # | Test Name | What It Asserts |
|---|-----------|----------------|
| 1 | `Descriptor_DynamicDisplayName_DefaultsNull` | Constructing `EditorCommandDescriptor` without `DynamicDisplayName` → property is `null` |
| 2 | `MenuNode_DynamicLabel_OverridesName_WhenSet` | `MenuItemNode` with `DynamicLabel = () => "Save [x]"` → `ResolveLabel() == "Save [x]"`; with `DynamicLabel == null` → `ResolveLabel() == "Save"` |
| 3 | `MenuAdapter_SetsDynamicLabel_FromDescriptor` | `MenuCommandAdapter.Register` with `DynamicDisplayName = () => "DYN"` → leaf `ResolveLabel() == "DYN"`; null `DynamicDisplayName` → leaf `ResolveLabel() == "Plain"` (the path-leaf `Name`) |

### ToolbarCommandAdapterTests.cs (1 new)

| # | Test Name | What It Asserts |
|---|-----------|----------------|
| 4 | `ToolbarTooltip_UsesDynamicDisplayName_WhenSet` | `ResolveTooltip` with `DynamicDisplayName` returning `"Dynamic Label"` → first line starts with `"Dynamic Label"`, contains description, contains shortcut; null `DynamicDisplayName` → `"Static Label"` (the `DisplayName`) |

All 4 tests assert real resolved strings — no `Assert.True(true)`, no skips, no stub assertions.

---

## 🧪 Testing Results

### Build
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### NodeEditor.Core.Tests
```
Passed!  - Failed:     0, Passed:   181, Skipped:     0, Total:   181, Duration: 39 ms
```

### Fdp.Presentation.Tests (filtered: MenuCommandAdapter|ToolbarCommandAdapter)
```
Passed!  - Failed:     0, Passed:    17, Skipped:     0, Total:    17, Duration: 58 ms
```

**Breakdown:**
- MenuCommandAdapterTests: 9 tests (6 pre-existing + 3 new) — all passed
- ToolbarCommandAdapterTests: 8 tests (7 pre-existing + 1 new) — all passed

**BLUEPRINT_REGENERATE_SNAPSHOTS was NOT set** during any test run.

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

No blocking issues. The existing code structure was clear and the batch instructions were precise. The `MenuCommandAdapterTests.cs` already existed (the batch instructions called it a "new file," but it was already present from MTB-P2-T2). I added the 3 new tests to the existing file, reusing the existing `FakeCommandSet` helper. The `ToolbarCommandAdapterTests` needed an additional `using NodeEditor.Primitives;` for `KeyBinding`/`EditorKey`/`KeyModifiers` used in the new test.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The `ResolveTooltip` method re-looks-up the descriptor via `commands.Get(commandId)`, while `RenderEntry` already has the descriptor as a parameter. This is by design (the batch specifies the signature), and the overhead is trivial (an O(1) dictionary lookup). If performance ever becomes a concern, an overload taking the descriptor directly could be added.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

I set `node.DynamicLabel = descriptor.DynamicDisplayName` unconditionally — even when `DynamicDisplayName` is null. This is cleaner and equivalent to not setting it, since `ResolveLabel()` already falls back to `Name` when `DynamicLabel` is null. No conditional was needed.

The `ResolveTooltip` method returns `string.Empty` for unknown command IDs (when `Get` returns null), rather than throwing. This is consistent with `GetState`'s defensive pattern.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- Unknown command ID in `ResolveTooltip`: returns `string.Empty` (defensive, matches `GetState` pattern).
- `DynamicLabel` set to a delegate that returns null: `DynamicLabel?.Invoke()` returns null, then `?? Name` kicks in → falls back to `Name`. This is safe.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

`ResolveLabel()` is called twice per frame per leaf node (once in the `Gui.MenuItem` call). This is by design — immediate mode re-evaluation. The cost is a delegate invoke + null check per call, which is negligible for the number of menu leaves in practice. Similarly, `ResolveTooltip` is only called when `IsItemHovered()` is true, so the re-lookup cost is rarely incurred.

---

## ⚠️ Outstanding Issues / Next Steps

- **MTB2-T4** (BATCH-33): Active-save-target resolver + dynamic Save label — this batch (T3) provides the exact infrastructure T4 needs to set `DynamicDisplayName` on `shell.save`/`shell.saveAs`.
- No known issues. Existing commands without `DynamicDisplayName` render exactly as before (verified by passing all pre-existing tests + the new default-null test).
