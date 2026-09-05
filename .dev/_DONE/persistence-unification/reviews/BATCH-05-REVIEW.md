# BATCH-05 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
PU-301 (JSON dual-load contributors + ownership + SourceFilePath) + PU-302 (Kind-guarded post-reload stitch)
+ PU-303 (tests, incl. the reopen-when-C#-won't-compile acceptance). **Phase 3 complete** — the thread's core
promise is delivered + proven. Mechanism is dormant in the live editor (zero `.json` until migration PU-401);
exercised by synthesized `.json`.

## Verified (read source + assertions, ran suites)
- **Dual-load:** `BTreeJsonAssetContributor`/`HsmJsonAssetContributor` — header-lazy discover (skip malformed),
  lazy `LoadFull` → `Deserialize` → new `Mapper.ToModel(dto, sourceFilePath, isEditorOwned)` (FromDto delegates
  to it) → `IsEditorOwned=true`, `SourceFilePath=.json`; JSON wins AssetId collision (added after assembly
  contributor; last-write-wins). `ClearDirty()` on load. ✅
- **Acceptance (the crux):** `PU301_Acceptance_*_ReopenWhenCSharpBroken` — real `.btree.json`/`.hsm.json` on
  disk, NO assembly contribution, `Open` succeeds, `ReconcileFromCatalog(empty)` does not throw, doc stays open,
  topology intact (Nodes==2 / States==1), indices sentinel (-1), `IsDirty==false`. Genuine, not a stub. ✅
- **Kind-guard (Blueprint non-regression):** `ReconcileFromCatalog` branch is
  `if (Kind is BTree or Hsm && IsEditorOwned) StitchRuntimeIndices else ReconcileAsset`. Tests prove a
  Blueprint doc AND a hand-authored BTree doc take full-replace (Asset becomes the fresh ref), NOT stitch.
  Blueprint reconcile path (separate `BlueprintEditorModule`) untouched — Blueprints 7 pre-existing/0 new. ✅
- **Stitch:** `StitchKernelIndices` maps VisualId→KernelBlobIndex (BTree) / StableId→FlatIndex (HSM) from the
  fresh blob, updates `Blob`(+`Metadata` via new `HsmAsset.UpdateBlob`), re-wires `BTreeDebugSession.
  SetDebugMetadata`, unmatched→sentinel(-1)+non-Clean diagnostic. **No `MarkDirty` on load/stitch** (asserted). ✅
- **Changed pre-existing test (legitimate):** `Reload_ReconcileFromCatalog_UpdatesMatchingOpenDoc` switched its
  `_FakeEditableAsset` to `Kind=Blueprint` so it tests the (unchanged) full-replace path — the new
  `ReconcileStitchTests` cover the BTree/HSM stitch path. Not masking a regression. ✅
- **Ran myself:** build 0 errors/0 warnings; AiShared 769/769; EditorSubsystemBoot 10/10; Blueprints 7
  pre-existing/0 new. (Coder also reports BTree 392, HSM 341, persistence 88, generators 37 green.)

## Issues / Debt
- **PU-D09 (P3):** stitch uses `BlackboardLoadState.StructParseFailed` as the unmatched-node diagnostic —
  semantically stretched; consider a dedicated stitch/diagnostic state.
- **PU-D07-bis (P3):** `InternalsVisibleTo` added to `Hrot.BTree.Editor`/`Hrot.Hsm.Editor` for
  `Hrot.Editor.AiShared.Tests` (to call the projectors). Acceptable; flagged.
- **Model change (noted, not debt):** `HsmAsset.Blob`/`Metadata` converted from getter-only auto-props to
  backing fields for `UpdateBlob`. HSM suite 341/341 green → no consumer broke.
- JSON-root paths relative to BaseDirectory (proper roots = PU-501); JSON file-watcher deferred (PU-401/501) —
  known phase items.

## Verdict
APPROVED. Completes PU-301, PU-302, PU-303. Phase 3 done; the "assets reopen even when C# won't compile"
promise is implemented + proven. (Activated for real assets at PU-401, blocked on PU-D06.)

## Commit Message
```
feat(persistence): editor JSON dual-load + post-reload stitch (BATCH-05, Phase 3)

Completes PU-301, PU-302, PU-303 — the thread's core promise: editor-owned BTree/HSM assets
load from .btree.json/.hsm.json and reopen even when the C# won't compile.
- BTreeJsonAssetContributor/HsmJsonAssetContributor: header-lazy discover (skip malformed),
  lazy LoadFull -> Deserialize -> Mapper.ToModel(dto, sourceFilePath, isEditorOwned=true);
  JSON wins AssetId collision; ClearDirty on load. Wired into the AI catalog (dormant: zero
  .json until migration PU-401). Mappers gain ToModel(dto, sourceFilePath, isEditorOwned).
- ReconcileFromCatalog is Kind-guarded: editor-owned BTree/HSM -> StitchRuntimeIndices
  (keep JSON topology authoritative; map VisualId/StableId -> KernelBlobIndex/FlatIndex from
  the recompiled blob; update Blob(+Metadata via HsmAsset.UpdateBlob); re-wire debug overlay;
  unmatched -> sentinel + diagnostic; never MarkDirty). Blueprint + hand-authored keep
  full-replace (ReconcileAsset) — Blueprint path untouched.
Tests (19): reopen-when-C#-broken acceptance (BTree+HSM); stitch index-by-VisualId/StableId +
Blob update + unmatched sentinel/diagnostic; Kind-guard (Blueprint + hand-authored full-replace);
no-dirty on load/stitch. AiShared 769/769; boot 10/10; Blueprints 7 pre-existing/0 new; build 0 warnings.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
```
