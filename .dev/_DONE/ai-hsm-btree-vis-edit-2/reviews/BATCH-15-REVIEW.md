# BATCH-15 Review — TASK-BT-15 Single-parent + no-cycle on wire (CRITICAL)

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent)
- `ApplyAddLink` now: reject self-parent; reject if `SubtreeContains(childId, parentId)` (would create a cycle); **detach child from every other parent** before attaching (single-parent replace, host §5.3); add to new parent. `SubtreeContains` = BFS over `ChildVisualIds` with visited-set (cycle-safe). Correct.
- Fixes both reported wiring bugs: re-wire MOVES the node (no second parent) → no more cycles and no more "disappearing links" (the one-link-per-child cache no longer collides). Cycles/self-parent leave the model unchanged.
- 5 new tests (normal attach, re-wire moves child, exactly-one-parent, cycle rejected, self-parent rejected). `Hrot.BTree.Editor.Tests` **498/0**.

## Issues
None. (`BTreeLinkValidator` left as-is per scope; the command-sink backstop is the guarantee, and BT-14 covers emit.)

## Verdict
APPROVED. Together with BT-14, the model can't form a cycle and the emitter can't overflow.

## Commit message
```
fix(btree-editor): single-parent + no-cycle enforcement on wire (BATCH-15 / TASK-BT-15)

ApplyAddLink now detaches the child from its previous parent before attaching
to the new one (single-parent, host §5.3), and rejects self-parent / would-be
cycles (SubtreeContains BFS) leaving the model unchanged. Fixes "re-wire adds a
second parent" → no more cycles (which crashed codegen) and no more
"disappearing links" (one-link-per-child cache no longer collides). +5 tests.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
