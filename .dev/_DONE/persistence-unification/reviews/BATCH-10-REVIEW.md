# BATCH-10 Review — editor smoke-test fixes
**Status:** ✅ APPROVED   **Date:** 2026-06-05

## Summary
Three post-migration editor regressions surfaced by the user's manual smoke test, root-caused + fixed.

## Verified (read source + assertions, ran suites myself)
- **#1 (migrated assets opened flat):** TWO coupled fixes, both correct.
  - 1a — `EditorSubsystem.Initialize` JSON contributors `Discover(...)` → `Refresh(...)` (Discover only scanned
    headers; LoadAll never ran → catalog had only the layout-less assembly asset). + dir-missing warning.
  - 1b — `AssetCatalog.Rebuild` now dedups `_cache` by AssetId, last-writer (JSON, added last) wins the VALUE
    (first-occurrence keeps the slot for stable order). So the browser lists SampleScout ONCE, as the
    layout-bearing JSON instance; `FindByName` no longer returns the layout-less assembly copy. ✅
- **#2 (Save-All didn't persist layout):** `doc.MarkDirty()` added to the `asset.Changed` handler
  (`EditorSubsystem.cs:2204`) — Save-All skipped non-dirty docs. Lead pre-verified the model→DTO sync already
  works (`BTreeCommandSink.ApplyNodeMoves` sets `node.Position`; `ToDto` reads it), so MarkDirty is the whole fix. ✅
- **#3 (HSM open didn't switch perspective):** root cause `AssetKind.Hsm.ToString()`="Hsm" ≠ registrar "HSM".
  New `AssetKindExtensions.ToPerspectiveName()` canonical map (BTree→"BTree", Hsm→"HSM", Blueprint→"Blueprint")
  used in BOTH directions (`AiDocumentManager.Activate` forward `:162`; `WindowManagerPerspectiveSwitcher` reverse
  `:70`). Display name stays "HSM"; AssetKind enum unchanged. ✅
- **Tests:** +4 AssetCatalog dedup (same-id from two contributors → one entry, the later/JSON instance with
  non-zero positions; FindByAssetId/FindByName agree); +4 Kind→perspective mapping; 2 existing
  AiDocumentManagerTests updated from `"Hsm"`→`"HSM"` — those had codified the BUG (asserted the callback emits
  "Hsm", which never matched the "HSM" perspective); now assert the fix. Legit, not changed-to-go-green. ✅
- **Ran myself:** build 0/0; AiShared 832/832 (+12); BTree 391; HSM 339; boot 10/10; Blueprints 7 pre-existing/0 new.

## Scope honesty (manual-verify carried to the user re-smoke)
- #1b + #3 are headlessly tested. #1a (`Refresh` + path resolution) and #2 (`MarkDirty` in the EditorSubsystem
  handler) are correct by inspection but only reachable via full editor boot — the **end-to-end** proof (open
  SampleScout/SampleGuard and see their saved layout; drag a node, Save-All, restart, layout restored; open an
  HSM and the perspective switches) needs the **user's re-smoke**. The boot test (10/10) + SaveAllAndFlushTests
  cover the wiring around these.
- Not ours (reported, untouched): `[x]` close (code correct — live ImGui artifact); "Blueprint not registered"
  (pre-existing — needs Compile/Reload first); browser friendliness (Phase 7).

## Verdict
APPROVED. Addresses all three regressions with minimal, well-targeted fixes + headless tests for the two
verifiable ones. Hand back to the user for the editor re-smoke.

## Commit Message
```
fix(editor): post-migration smoke fixes — JSON layout load, Save-All dirty, HSM perspective (BATCH-10)

Three regressions surfaced by manual smoke after the BATCH-09 JSON migration:
- #1 migrated assets opened with no layout: (1a) EditorSubsystem loaded the JSON contributors with
  Discover() (headers only, LoadAll never ran) -> changed to Refresh(); (1b) AssetCatalog.Rebuild now
  dedups _cache by AssetId with last-writer (JSON) wins, so the browser lists/open-resolves the single
  layout-bearing JSON instance instead of the layout-less assembly copy.
- #2 Save-All didn't persist layout: the asset.Changed handler scheduled a flush but never marked the
  DOCUMENT dirty, so SaveAllAiDocumentsCommand skipped it. Added doc.MarkDirty(). (Model->DTO position
  sync already worked.)
- #3 opening an HSM didn't switch perspective: AssetKind.Hsm.ToString()="Hsm" != registrar "HSM". New
  AssetKindExtensions.ToPerspectiveName() canonical map used in both Activate (forward) and the perspective
  switcher (reverse); display name stays "HSM".
Tests: +4 AssetCatalog dedup, +4 Kind->perspective mapping; 2 existing tests updated (had codified the bug).
Build 0/0; AiShared 832/832; BTree 391; HSM 339; boot 10/10; Blueprints 7 pre-existing/0 new.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```
