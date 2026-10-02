# BATCH-05 — Validation inline on canvas (BTree) **[VISUAL GATE]**

**Task:** TASK-BT-05 (`.dev/_DONE/ai-hsm-btree-vis-edit-2/TASK-DETAIL.md#task-bt-05--validation-inline-on-canvas`)
**Phase:** A · **One objective only.** Depends on BT-04 (validator).
**Scope decision D-04** (`.dev/_DONE/ai-hsm-btree-vis-edit-2/DECISIONS.md`): this batch covers the **canvas node-state** only. The inspector banner is DEFERRED — do NOT implement it here.

## 🔒 Working agreement (MANDATORY)
Same as prior batches: one task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.
**[VISUAL GATE]:** implement + headless tests that assert the projected `NodeState`/`StatusTooltip`. The outline/⚠ pixels are confirmed by the lead later.

## 📋 Onboarding
- Design: `docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md` §5 (EB-D part 2); host `docs/blueprints/BTree_Editor_NodeEditor_Host_Design.md` §11.2.
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-05-REPORT.md`.

## 🎯 Objective
`BTreeNodeModel.State` is hardcoded `NodeState.Normal` and `StatusTooltip => null`, so invalid nodes look fine on the canvas. Drive both from per-node validation diagnostics so NodeEditor draws the error/warning outline + ⚠ glyph and a hover tooltip.

## File (exact)
`Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs`:
1. In `BuildCaches()` (which already runs on construction and on `_asset.Changed`), **before** building node models, run validation and build a per-`VisualId` map:
   ```csharp
   var diags = new BTreeValidator().Validate(_asset);   // stateless; cheap
   // map: VisualId (non-empty) -> (NodeState state, string tooltip)
   ```
   - `BTreeDiagnostic` = `(Guid VisualId, BTreeDiagnosticSeverity Severity, BTreeDiagnosticCode Code, string Message)`; `VisualId == Guid.Empty` ⇒ tree-level, skip (do not map to a node).
   - Severity → `NodeState` (from `NodeEditor.Primitives`): `Error` → `NodeState.Error`, `Warning` → `NodeState.Warning`, `Info` → none. If a node has multiple diagnostics, **Error wins** over Warning (combine via the higher severity); tooltip = the message of the highest-severity diagnostic (or join messages — your call, but it must be non-empty when a diagnostic exists).
2. Pass each node's `(NodeState, tooltip)` (default `Normal`/`null` when absent) into the `BTreeNodeModel` it constructs, and have `BTreeNodeModel.State`/`StatusTooltip` return them (replace the hardcoded `NodeState.Normal`/`null`).

`using Hrot.BTree.Editor.Validation;` is in the same assembly. Touch nothing else (pins, Category, links, attachments unchanged).

## 🧪 Tests (new file `Model/BTreeNodeValidationStateTests.cs`)
Reuse the `EmptyBlob()`/`MakeAsset()`/`AddNode` pattern. Build assets, construct `BTreeGraphModel`, get a node's `INodeModel` via `graph.FindNode(new NodeId(visualId))`, assert `.State`/`.StatusTooltip`. (First run the validator to confirm which `BTreeDiagnosticCode` maps to which severity — assert the flag that matches the real severity.)

- `NodeState_EmptyComposite_HasWarningFlagAndTooltip`: Root → empty Sequence → the **Sequence** node's `State` has the `NodeState.Warning` flag set (`(state & NodeState.Warning) != 0`) and `StatusTooltip` is non-empty. (EmptyComposite severity = Warning.)
- `NodeState_UnboundAction_HasErrorFlagAndTooltip`: Root → unbound Action → the **Action** node's `State` has `NodeState.Error` flag and `StatusTooltip` non-empty.
- `NodeState_ValidNode_IsNormalNoTooltip`: valid tree (Root → Sequence → bound Action) → the bound Action node's `State == NodeState.Normal` and `StatusTooltip` is null.
- `NodeState_RecomputesOnChanged`: build Root → empty Sequence (Sequence has Warning); then add a child to the Sequence and fire the asset's `Changed`; re-read the Sequence node model → it no longer has the Warning flag (empty-composite cleared). (Proves validation re-runs on rebuild.)

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. new tests).
- [ ] Invalid nodes project `NodeState.Error`/`Warning` + tooltip; valid nodes `Normal`/null; recomputed on `Changed`.
- [ ] Only `BTreeGraphModel.cs` changed; inspector banner NOT touched (deferred).
- [ ] Report written.

## Notes
- Validation here is independent of the Diagnostics-window path (BT-04) — both call the cheap stateless `BTreeValidator`. That duplication is acceptable for now.
- Do NOT add a debounce/timer; `BuildCaches` already runs on `_asset.Changed`, which is the right cadence.
