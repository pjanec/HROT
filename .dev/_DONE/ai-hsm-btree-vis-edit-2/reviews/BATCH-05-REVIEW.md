# BATCH-05 Review — TASK-BT-05 Validation inline on canvas

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED (scope: canvas node-state per D-04; inspector banner deferred)

## Verification (independent)
- Diff (`BTreeGraphModel`): `BuildCaches` runs stateless `new BTreeValidator().Validate(_asset)`, builds a per-`VisualId` map (skips `Guid.Empty` tree-level), severity→`NodeState` (Error/Warning), Error-wins on multi-diagnostic; `BTreeNodeModel` ctor now takes `(NodeState, string?)` and returns them. Only `BTreeGraphModel.cs` changed; inspector banner untouched (deferred).
- New test `Model/BTreeNodeValidationStateTests.cs` (4 tests): each first asserts the validator's real output (`Code`+`Severity`+`VisualId`), then the projected `INodeModel.State`/`StatusTooltip`. `(State & flag).NotBe(0)` resolves to `NotBe(NodeState.Normal)` → genuinely fails if the flag is absent. `RecomputesOnChanged` proves re-validation on `Changed`.
- Build **0 warnings/0 errors**; `dotnet test Hrot.BTree.Editor.Tests` → **477 passed / 0 failed** (473 + 4).

## Issues
None.

## Verdict
APPROVED. `[VISUAL GATE]`: outline/⚠ pixels confirmed at REVIEW-BT. Inspector banner is a deferred follow-up (BT-05b / fold into REVIEW-BT).

## Commit message
```
feat(btree-editor): inline validation node-state on canvas (BATCH-05 / TASK-BT-05)

BTreeGraphModel runs the stateless BTreeValidator on each rebuild and projects
per-VisualId diagnostics onto BTreeNodeModel.State/StatusTooltip (Error/Warning
flags + tooltip; Error wins; tree-level skipped) so NodeEditor draws the
outline + warn glyph. Recomputes on asset Changed. +4 tests. Inspector banner
deferred (D-04).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
