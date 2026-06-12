# BATCH-16 Review — TASK-BT-16 Break-link for projected links

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent)
- `ApplyRemoveLinks` now resolves via `_graph.FindLink(id)` → `FindPin(FromPin/ToPin)` → child/parent → `parent.ChildVisualIds.Remove(child)`; falls back to the session `_links` dict only if the graph doesn't know the link; always `_links.Remove` + `MarkDirty`. Works for projected (JSON-loaded) AND session-added links.
- Key test `RemoveLinks_ProjectedLink_DeletesIt` uses the **real `BTreeGraphModel`** (not a stub): builds Root→child via `ChildVisualIds` (simulating JSON load), projects, finds the link by pin-owner resolution, removes it → child detached. Genuinely exercises the real projection path. Plus session-added regression + unknown-link no-throw (stub-based, appropriate).
- `Hrot.BTree.Editor.Tests` **501/0**.

## Issues
None.

## Verdict
APPROVED. Completes Fix-A2: BTree editing no longer crashes (BT-14), wiring moves nodes / can't cycle (BT-15), and existing links are deletable (BT-16).

## Commit message
```
fix(btree-editor): break-link works for projected (JSON-loaded) links (BATCH-16 / TASK-BT-16)

ApplyRemoveLinks resolved links only via a session-only _links dict, so
"Break link" was a no-op for any wire loaded from JSON. Now it resolves via the
graph model (FindLink → FindPin → child/parent), deleting both projected and
session-added links; _links is a defensive fallback. +3 tests (projected-link
removal via real BTreeGraphModel, session regression, unknown-link no-throw).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
