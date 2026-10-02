# BATCH-BB1D Review
**Status:** ✅ APPROVED   **Date:** 2026-06-12

## Summary
Fixes the P1 live-wiring gap: BB1 (B-1/B-2/B-3) was headlessly complete but the composition root
(`EditorSubsystem.cs`) never passed the exporter / shared `*FacetFqnContext` / `expressionTargetFieldAccessor`,
so the feature was invisible in the running editor. Now wired for both BTree and HSM. Verified green: BTree 434,
HSM 382, Editor (incl. EditorSubsystemBoot 10) 178; EditorSubsystem builds 0/0.

## Verified
- `EditorSubsystem.cs`: both registrars now pass `expressionTargetFieldAccessor: ResolveExpressionTargetField`
  (4-type switch); the `ActiveChanged` handler creates ONE `BTreeFacetFqnContext`/`HsmFacetFqnContext` per
  activation and passes the SAME instance to both `BuildDrawers(..., sharedSchemaExporter, ctx)` and
  `BuildFacetDispatcher(asset, ctx)`. Correct.
- `HsmSelectionBridgeHelper.BuildFacetDispatcher(asset, ctx)` overload added (mirrors BTree).
- Integration test `BB1DSharedContextIntegrationTests` drives the real bridge-helper + factory seam: dispatcher
  writes the FQN to the shared context, drawer then filters to only the compatible var; a `NoContext` test
  documents the old all-vars behavior. This test fails on the pre-BB1D wiring (the gap that escaped BB1A–C
  review). Strong.

## Lead self-note
My BB1B/BB1C reviews checked `PerspectiveWorkspaceRegistrar` but not `EditorSubsystem.cs` — the actual
composition root. The ONBOARDING explicitly flags editor live-wiring as the #1 recurring trap; future editor
batches must verify the composition root, not just the registrar.

## Verdict
APPROVED. B-1/B-2/B-3 are now live-wired. Remaining: REVIEW-BB1 (running-editor visual smoke — user) and
DEBT-BF-04 (HSM state-action picker — design call).

## Commit Message
```
fix(ai-editor): live-wire BB1 in EditorSubsystem composition root (BATCH-BB1D)

BB1A-C were headlessly complete but invisible in the running editor. Wire it:
- EditorSubsystem: pass expressionTargetFieldAccessor (ResolveExpressionTargetField, 4-facet
  switch) to both BTree/HSM registrars; share one BTreeFacetFqnContext/HsmFacetFqnContext per
  ActiveChanged between BuildDrawers (+ sharedSchemaExporter) and BuildFacetDispatcher.
- HsmSelectionBridgeHelper: add BuildFacetDispatcher(asset, fqnContext) overload.
- 14 integration tests through the real bridge-helper+factory seam (fail on the old wiring).
Suites green: BTree 434, HSM 382, Editor 178 (boot 10); 0 failed, 0 new.
```
