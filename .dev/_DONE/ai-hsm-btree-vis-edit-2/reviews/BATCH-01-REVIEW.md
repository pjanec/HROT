# BATCH-01 Review — TASK-BT-01 Live action/condition palette

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Summary
Dynamic Action/Condition palette entries from `IActionSchemaExporter`, wired through `BTreeDocumentFactory` → composition root; placement bakes `MethodFqn`. Verified independently (diffs + tests re-run), not trusted from report.

## Verification (independent)
- `dotnet build IOS-IG-SimHost.sln` → **Build succeeded, 0 errors**, 29 warnings (within pre-existing band; touched-project diffs warning-clean on inspection).
- `dotnet test Hrot.BTree.Editor.Tests` → **449 passed / 0 failed** (incl. T2–T8, 14 new).
- `dotnet test Hrot.Editor.AiShared.Tests` → **1059 passed / 0 failed** (T1 + BB1 picker tests intact → additive `ActionSchemaEntry` change is BB1-safe).
- Read all 7 impl diffs + both test files. Impl matches D-01/D-02. Tests assert **actual values** (exact encoded `Kind.Id`, `MethodFqn` baked through the real `BTreeCommandSink`, before/after `Changed`) — not string-presence shams; would catch a broken impl.

## Issues
- **Report inaccuracy (no code impact):** report claims "36 pre-existing errors in `Fdp.Presentation.Tests`." My clean build shows **0 errors**. Confabulated narration — flagged per the trust-diffs-not-report rule; nothing to fix in code.
- Test stub `StubGraphModel` uses `#pragma warning disable CS0067` on its unused interface event — benign standard idiom for an unused `event` on a test stub; acceptable (not masking a production diagnostic).

## Beyond-spec decisions (all sound, accepted)
1. Added the missing `[SharedAiHeavyCondition]` branch in `ProcessMethod` (was absent → those methods were previously skipped entirely). Correct + additive.
2. `BTreeKinds.IsLeaf` extended for encoded ids — necessary (link validator relies on `IsLeaf`).
3. `BTreeNodeCatalog` split into `_staticEntries`/`_dynamicEntries` for rebuild — clean.

## Verdict
APPROVED. Specific actions/conditions list in the palette; placement bakes identity; generic fallback preserved; BB1 unaffected.

**Visual confirmation (deferred to REVIEW-BT, non-blocking):** that the populated palette searches/reads well in the running editor.

## Commit message
```
feat(btree-editor): live action/condition palette from ActionSchemaExporter (BATCH-01 / TASK-BT-01)

- ActionSchemaEntry gains IsCondition (appended, default false; BB1-safe)
- ActionSchemaExporter.ProcessMethod sets IsCondition from condition attrs
  (+ adds the missing SharedAiHeavyCondition branch)
- BTreeKinds: Action/Condition prefixes, TryParseLeafActionKind, encoded KindIdToNodeType/IsLeaf
- BTreeNodeCatalog: dynamic Action/Condition entries from IActionSchemaExporter, rebuild on Changed
- BTreeCommandSink.ApplyAddNode: bake MethodFqn for encoded kinds
- Wire sharedSchemaExporter through BTreeDocumentFactory → EditorSubsystem
- Tests T1-T8 (16): exporter discriminator, catalog query/filter/re-query, kinds parse, placement baking, generic fallback

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
