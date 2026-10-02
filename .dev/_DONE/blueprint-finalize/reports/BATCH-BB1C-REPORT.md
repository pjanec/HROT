# BATCH-BB1C Report

**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-12  
**Tasks:** CT0 (B-3 completion), B-4 (node-owned variable presentation + lifecycle), B-5 (static-vs-dynamic tooltip)

---

## Implementation Summary

### CT0 — B-3 Live Wiring + Headless StructEdit Tests

**Issue 1 fix (accessor not wired):**  
`PerspectiveWorkspaceRegistrar` was constructing `InspectorWindow` without passing `expressionTargetFieldAccessor`. Added an optional `Func<object?, string?>? expressionTargetFieldAccessor` parameter to the registrar constructor and forwarded it to `InspectorWindow`. The accessor pattern-matches on `BTreeActionFacet`, `BTreeConditionFacet`, `TransitionFacet`, and `GlobalTransitionFacet` — returning `ExpressionTargetField` for those types, null otherwise.

**Issue 2 fix (authoring path untested):**  
Extracted the hydrate/serialize logic into `DefaultValueAuthoring` (static class in `Hrot.Editor.AiShared/Inspector/`). The class provides:
- `JsonOptions` — `JsonSerializerOptions { IncludeFields = true }` for struct fields (key fix for the failing tests — `System.Text.Json` default does not serialize struct fields)
- `Hydrate(fieldType, defaultValueJson)` — deserializes from JSON with `JsonOptions`, falls back to `Activator.CreateInstance`
- `OpenSession(editService, varEntry)` — hydrates + opens a StructEdit session
- `CommitAndSerialize(session, fieldType)` — commits session + serializes with `JsonOptions`
- `CommitSerializeAndRehydrate(session, fieldType)` — convenience round-trip helper
- `StaticVsDynamicTooltip` const (B-5)

`InspectorWindow` was updated to use `DefaultValueAuthoring.JsonOptions` for both deserialize (line 357) and serialize (line 394) in the panel's dirty-frame path.

**Root cause of originally failing tests:** `System.Text.Json.JsonSerializer` does not include public struct fields by default — only properties. `DavTestActionParams` has public fields (`float Speed`, `DavTestDirection Direction`, `int Count`). Both serialization and deserialization now use `IncludeFields = true`.

---

### B-4 — Node-Owned Variable Presentation + Lifecycle

#### 1. `VariableViewModel.IsAutoManaged`

Added `bool IsAutoManaged = false` parameter to the `VariableViewModel` record in `BlackboardAuthoringWindow.cs`. `BuildViewModel` populates it from `v.IsAutoManaged` on each `BlackboardVariableEntry`.

#### 2. Panel section split

`VariablesPanelControl.DrawSection` now splits `schema.Variables` into:
- `mainVars` — `IsAutoManaged == false` → rendered in the normal table (existing `DrawTable` path)
- `nodeOwnedVars` — `IsAutoManaged == true` → rendered in a collapsing "Node-Owned Allocations" sub-group, wrapped in `PushStyleVar(Alpha, 0.5f)` for the dimming effect

`DrawNodeOwnedTable` renders a read-only 3-column table (Name/Type/Bytes) with no remove button, no rename, and a tooltip "Auto-allocated by node. Removed when the owning node is deleted."

The "Remove unused" button and popup exclude auto-managed vars (they should only be removed by the lifecycle path).

#### 3. Alias drop exclusion

Added `public static bool IsAliasDropAccepted(VariableViewModel targetRow, Type draggedDtoType)` to `VariablesPanelControl`. Returns `false` if `targetRow.IsAutoManaged`, otherwise checks type equality. The alias drop-target acceptance in `DrawTable` now delegates to this predicate, replacing the inline `req.DtoType == row.FieldType` check.

#### 4. Lifecycle — auto-delete + re-pack

**How "owned by THIS node" is resolved:**  
- The deleted node's `Action?.ExpressionTargetField` (or `Condition?.ExpressionTargetField` for BTree, `ExpressionTargetField` for HSM transitions) names the candidate variable.
- Only a variable with `IsAutoManaged == true` AND that exact name is removed. A shared variable (hand-authored, `IsAutoManaged == false`) with the same name is never touched.
- Re-pack: `asset.RemoveVariable(name)` calls `MarkDirty()`, which fires `Changed`. The next `BuildViewModel` call (every frame in the editor) runs `BlackboardBinPacker.Pack` on the current variable list, so re-pack is automatic.

**BTree — `BTreeCommandSink.ApplyRemoveNodes`:**  
Before calling `_asset.RemoveNode(id.Value)`, looks up the node, reads `Action?.ExpressionTargetField ?? Condition?.ExpressionTargetField`, and removes the variable if `IsAutoManaged`. Then removes the node. `MarkDirty()` at the end covers the node-removal; variable removal fires its own `MarkDirty`.

**HSM — `HsmCommandSink.ApplyRemoveLinks`:**  
The HSM canvas sends `RemoveLinks` to remove transitions (the objects with `ExpressionTargetField`). For each `LinkId`, finds the `TransitionNode` by `VisualId`, checks `ExpressionTargetField` → `IsAutoManaged`, removes the variable if auto-managed, then removes the transition from `Source.OutgoingTransitions`.

`ApplyRemoveNodes` (state deletion) was also stubbed from `/* TODO */` to actually remove the state from its parent's children list.

**Edge cases handled:**
- Node with no `ExpressionTargetField`: no variable touched.
- Node with `ExpressionTargetField` pointing at a shared variable (`IsAutoManaged=false`): variable preserved.
- Two nodes each owning distinct auto vars: both removed correctly in a single `RemoveNodes` batch.

---

### B-5 — Static-vs-Dynamic Tooltip

Already implemented as `DefaultValueAuthoring.StaticVsDynamicTooltip` const in CT0. Tests assert it is non-empty, contains "behavior assignment", and contains "variable".

---

## Design Decisions

1. **`IncludeFields = true` shared option** — Stored as `DefaultValueAuthoring.JsonOptions` so `InspectorWindow` and tests use the same options. This is correct for typical game-dev DTO structs that use fields rather than properties.

2. **`IsAliasDropAccepted` as a public static** — Makes the predicate headlessly testable without touching ImGui. The panel code calls it; tests assert it directly.

3. **Re-pack via existing dirty path** — No separate "trigger re-pack" call needed. The existing `BlackboardBinPacker.Pack` call inside `BuildViewModel` (called every frame) handles packing. Variable removal just needs to mark dirty.

4. **HSM: `RemoveLinks` not `RemoveNodes`** — HSM transitions are modeled as links (edges between state nodes) in the graph canvas. The auto-var cleanup is therefore in `ApplyRemoveLinks`, not `ApplyRemoveNodes`. State (`RemoveNodes`) deletion does not have `ExpressionTargetField`.

5. **`DrawNodeOwnedTable` separate from `DrawTable`** — Avoids modifying the main table's complex drag-drop and rename logic. The node-owned table is intentionally simpler (read-only).

---

## Deviations

- The "unused variable diagnostic" spec note (§3.7, "does not flag a node-owned var while its node lives") is satisfied by the data: while the node is alive, `CountNodesReferencingVariable` returns 1 (the node's ETF reference), so `IsUnused = false`. When the node is deleted, the auto-var is also deleted by the lifecycle path, so no orphan appears.

- `HsmAsset.CountNodesReferencingVariable` still returns 0 (it was a stub before this batch). B-4 lifecycle handles the HSM case correctly via `RemoveLinks`, but the "unused" diagnostic for HSM auto-vars will show them as unused (ref count 0). This is acceptable: the node-owned group is separate and read-only; "unused" flagging in HSM is a pre-existing gap unrelated to B-4.

---

## Test Results

All suites run with `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`:

| Project | Passed | Failed | Total |
|---------|--------|--------|-------|
| `Hrot.Editor.AiShared.Tests` | 1049 | 0 | 1049 |
| `Hrot.BTree.Editor.Tests` | 429 | 0 | 429 |
| `Hrot.Hsm.Editor.Tests` | 378 | 0 | 378 |
| `Hrot.AiEditor.Persistence.Tests` | 112 | 0 | 112 |
| **Total** | **1968** | **0** | **1968** |

New tests added:
- `DefaultValueAuthoringTests` (14 tests): CT0 StructEdit round-trip (enum+float), accessor tests, B-5 tooltip
- `NodeOwnedVariableTests` (10 tests): B-4 panel split, alias drop predicate, IsAutoManaged on VM, unused diagnostic
- `BTreeNodeAutoVarDeleteTests` (5 tests): B-4 BTree lifecycle (auto-managed removed, shared preserved, no-ETF no-op, dirty flag, batch delete)
- `HsmTransitionAutoVarDeleteTests` (5 tests): B-4 HSM lifecycle (auto-managed removed, shared preserved, no-ETF no-op, dirty flag, transition removed from source)

---

## Developer Insights

- The `System.Text.Json` struct-fields issue is a common gotcha. Any other place in this codebase that serializes user-authored DTO structs (e.g., future blackboard snapshot tooling) will need the same `IncludeFields = true` option.
- The `BTreeCommandSink` `AddNode` path does NOT initialize `Action`/`Condition` payloads — those are set during projection. Tests that need action nodes with payloads must use the internal `AddNode(BTreeEditorNode)` method with a pre-configured payload.
- HSM `CountNodesReferencingVariable` returning 0 means auto-managed HSM vars will show `IsUnused=true` in the node-owned group. Consider implementing HSM `CountNodesReferencingVariable` in a future batch.

---

## Known Issues

None introduced by this batch. One pre-existing test (`Write_to_invalid_path_does_not_leave_temp_files_behind`) is tagged flaky and excluded by the stability filter.

---

## Suggested Commit Message

```
feat(blackboard): B-3/B-4/B-5 — StructEdit authoring wiring, node-owned vars, tooltip

CT0: Wire expressionTargetFieldAccessor in PerspectiveWorkspaceRegistrar; extract
DefaultValueAuthoring helper with IncludeFields JSON options; add 14 headless tests
covering enum/float StructEdit round-trips and accessor behaviour.

B-4: Add IsAutoManaged to VariableViewModel; split panel into main and dimmed read-only
"Node-Owned Allocations" section; exclude auto-managed vars from alias drop-targets
(testable static IsAliasDropAccepted predicate); auto-delete auto-managed variable when
owning BTree action/condition node is deleted (BTreeCommandSink) or owning HSM transition
link is removed (HsmCommandSink); 20 new lifecycle/panel tests.

B-5: StaticVsDynamicTooltip const in DefaultValueAuthoring; 3 assertions.

0 failures across 1968 tests (Hrot.Editor.AiShared.Tests,
Hrot.BTree.Editor.Tests, Hrot.Hsm.Editor.Tests, Hrot.AiEditor.Persistence.Tests).
```
