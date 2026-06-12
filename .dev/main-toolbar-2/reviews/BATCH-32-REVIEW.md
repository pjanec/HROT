# BATCH-32 Review — MTB2-T3

**Status:** ✅ APPROVED · **Date:** 2026-06-12 · Reviewer: Dev Lead

## Verified (independent)
- `EditorCommandDescriptor`: `Func<string>? DynamicDisplayName = null` added trailing — additive, existing
  constructions compile (build green proves it).
- `MenuItemNode.DynamicLabel` + `ResolveLabel() => DynamicLabel?.Invoke() ?? Name`; both WindowManager leaf-render
  sites (checkable + plain) now call `child.ResolveLabel()`. `MenuCommandAdapter.ApplyLeafNode` sets
  `node.DynamicLabel = descriptor.DynamicDisplayName`.
- `ToolbarCommandAdapter.ResolveTooltip(commands, id)` pure seam → first line `DynamicDisplayName ?? DisplayName`
  (+ description + shortcut); `RenderEntry` + the L114 text-fallback use it.
- **Backward-compatible:** all null → `Name`/`DisplayName`; existing commands render exactly as before.
- Tests assert real resolved strings (`"DYN"`, `"Plain"`, tooltip startswith `"Dynamic Label"` + `"A description"` +
  `"Ctrl+S"`, null→`"Static Label"`). No skips/tautologies.
- Build 0 warnings; `Fdp.Presentation.Tests` (Menu/Toolbar filter) 17/17; `NodeEditor.Core.Tests` 181/181. (Descriptor
  change is additive; NodeEditor.UI consumers unaffected.)

## Issues
None.

## Commit
`feat(main-toolbar2): dynamic command labels — DynamicDisplayName + menu/tooltip plumbing (MTB2-T3)`
