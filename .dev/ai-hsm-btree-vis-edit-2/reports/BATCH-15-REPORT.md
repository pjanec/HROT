# BATCH-15 REPORT — Single-parent + no-cycle enforcement on wire

**Date:** 2026-06-12
**Task:** TASK-BT-15 (Fix-A2 #2)
**Branch:** blueprint-integ-1

## Summary

Rewrote `BTreeCommandSink.ApplyAddLink` to enforce a valid tree on every link operation: (1) single-parent — detach the child from any previous parent before attaching to the new one; (2) reject self-parent and cycles with no model change and no exception. Added `SubtreeContains` BFS helper with visited-set guard.

This removes the root cause of double-parented nodes (which led to "disappearing links" in `BTreeGraphModel`'s per-child-id keyed cache) and prevents cycles from being created in the canvas. BATCH-14's emit cycle guard is the codegen safety net if one slips in; this is the front-line prevention.

## Changes

### 1. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs`

**`ApplyAddLink` (rewritten):**
- **Self-parent guard:** `if (childId == parentId) return;` — a node wiring to itself produces no model change.
- **Cycle guard:** `if (SubtreeContains(childId, parentId)) return;` — if the would-be parent is already in the child's subtree, the link is rejected (no model change, no exception).
- **Single-parent detach:** Before adding the child to the new parent, iterates all nodes and removes `childId` from any other node's `ChildVisualIds`. Ensures exactly one parent at all times.
- **Attach:** adds `childId` to the new parent's `ChildVisualIds` if not already present.

**`SubtreeContains(Guid rootId, Guid targetId)` (new private helper):**
- BFS traversal following `ChildVisualIds` from `rootId`.
- Uses `HashSet<Guid> visited` to guard against any pre-existing cycles in the model.
- Returns `true` if `targetId` is found in the subtree (including `rootId` itself).
- Reads from `_asset.FindNode` / `_asset.Nodes` for traversal.

### 2. `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeCommandSinkTests.cs`

Five new tests appended to the existing `BTreeCommandSinkTests` class:

| Test | What it proves |
|------|---------------|
| `AddLink_NormalAttach_adds_parentless_node_to_parent` | A parentless node can be attached normally |
| `AddLink_MovesChildToNewParent` | Re-wiring C from P1 to P2: P1 no longer has C, P2 has C |
| `AddLink_NoDuplicateParents` | After a re-wire, exactly 1 node in the entire model lists C as a child |
| `AddLink_WouldCreateCycle_IsRejected` | P→A→B tree; attempting B→P (making P child of B) leaves model unchanged; no exception |
| `AddLink_SelfParent_IsRejected` | Wiring N→N leaves model unchanged; no exception |

## Test results

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| Hrot.BTree.Editor.Tests | 498 | 0 | +5 new link-enforcement tests; 16 pre-existing BTreeCommandSink tests unchanged |

## Build

- `dotnet build IOS-IG-SimHost.sln` — **0 errors, 0 new warnings** in `Hrot.BTree.Editor`.

## Design notes

- **Single-parent is the invariant that makes `BTreeGraphModel`'s link cache work.** The cache keys links by child VisualId (one link per child). Before this change, a node could have two parents → two links share the same key → one overwrites the other in the cache → "disappearing link" on screen.
- **Cycle rejection is defense-in-depth.** The canvas validator already has cycle detection, but the command sink is the backstop — the model must never hold a cycle regardless of what the UI layer does.
- **No throw on rejection.** The instructions specify returning silently (no model change). This matches the existing pattern for null-pin and null-parent early returns.
- **`SubtreeContains` uses BFS, not DFS**, to avoid stack depth issues on large trees without needing recursion. The visited set handles the unlikely case where a pre-existing cycle already exists in the model.
- This complements BATCH-14: BT-15 stops cycles from being created; BT-14 is the codegen safety net if one slips in.
