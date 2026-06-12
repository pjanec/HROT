# BATCH-05 REPORT — Validation inline on canvas (TASK-BT-05)

**Date:** 2026-06-12
**Status:** ✅ COMPLETE

## Summary

Projected per-node validation diagnostics from `BTreeValidator` onto `BTreeNodeModel.State` and `BTreeNodeModel.StatusTooltip` in `BuildCaches()`. Previously `State` was hardcoded `NodeState.Normal` and `StatusTooltip` was `null`, so invalid nodes looked fine on the canvas. Now invalid nodes project `NodeState.Error`/`Warning` + a diagnostic tooltip, and the state is recomputed whenever the asset fires `Changed`. Scope: canvas node-state only — inspector banner is deferred per D-04.

## Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs` | Added `using Hrot.BTree.Editor.Validation;`; `BuildCaches()` now runs `new BTreeValidator().Validate(_asset)` and builds a per-VisualId `(NodeState, Tooltip)` map; `BTreeNodeModel` constructor accepts `state`/`statusTooltip` and returns them instead of hardcoded values |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Model/BTreeNodeValidationStateTests.cs` | New file — 4 behavioral tests |

## Implementation Details

### Core change (`BTreeGraphModel.cs`)

In `BuildCaches()`, before constructing node models:

1. Run `new BTreeValidator().Validate(_asset)` — stateless, cheap
2. Build a `Dictionary<Guid, (NodeState State, string Tooltip)>` mapping per-VisualId diagnostic severity:
   - `BTreeDiagnosticSeverity.Error` → `NodeState.Error`
   - `BTreeDiagnosticSeverity.Warning` → `NodeState.Warning`
   - `BTreeDiagnosticSeverity.Info` → skip (no node state)
   - `VisualId == Guid.Empty` → tree-level, skip (no single node)
   - **Error wins over Warning** when a node has multiple diagnostics
3. Pass each node's resolved `(state, tooltip)` (default `Normal`/`null`) into the `BTreeNodeModel` constructor

`BTreeNodeModel` now stores `_state` and `_statusTooltip` fields; `State` and `StatusTooltip` return them instead of the hardcoded constants. No other properties changed (Category, pins, links, attachments untouched).

### Behavioral Tests (`BTreeNodeValidationStateTests.cs`)

Four new `[Fact]` methods, using the `EmptyBlob()`/`MakeAsset()`/`MakeNode()` helpers from the existing test base, plus a `GetNodeModel()` helper that constructs `BTreeGraphModel` and calls `FindNode`:

| Test | What it asserts |
|------|-----------------|
| `NodeState_EmptyComposite_HasWarningFlagAndTooltip` | Root → empty Sequence → the Sequence node's `State & NodeState.Warning != 0` and `StatusTooltip` is non-empty; also confirms the validator produces `EmptyComposite`/`Warning` |
| `NodeState_UnboundAction_HasErrorFlagAndTooltip` | Root → Sequence → Action (empty MethodFqn) → the Action node's `State & NodeState.Error != 0` and `StatusTooltip` is non-empty; also confirms `UnboundActionMethod`/`Error` |
| `NodeState_ValidNode_IsNormalNoTooltip` | Root → Sequence → Action (bound MethodFqn) → the Action node's `State == NodeState.Normal` and `StatusTooltip` is null; also confirms zero diagnostics |
| `NodeState_RecomputesOnChanged` | Root → empty Sequence → initially Warning; then add child to Sequence + `MarkDirty()` → re-read same node from graph → now Normal/null (proves validation re-runs on `Changed` event) |

## Verification

- `dotnet build IOS-IG-SimHost.sln` — **0 errors**, 0 new warnings in `Hrot.BTree.Editor`
- `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0**, Passed: 477 (incl. 4 new tests)
- Only `BTreeGraphModel.cs` changed; inspector banner NOT touched (deferred per D-04)
