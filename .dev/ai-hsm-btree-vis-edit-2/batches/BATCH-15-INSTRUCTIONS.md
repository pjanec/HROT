# BATCH-15 — Single-parent + no-cycle enforcement on wire (CRITICAL)

**Task:** TASK-BT-15 (Fix-A2 #2). **One objective:** wiring always yields a valid tree.

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating**; finish without asking until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-15-REPORT.md`.
- Context: `BTreeCommandSink.ApplyAddLink` ([BTreeCommandSink.cs:129-149](../../Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs#L129)) adds `childId` to the new parent's `ChildVisualIds` but **never removes it from its previous parent** → a node gets TWO parents. Consequences: (a) **cycles** can form (and the validator's single-chain `FindParent` misses them); (b) **"disappearing links"** — `BTreeGraphModel` keys each link by the child's VisualId (one link per child), so a child with two parents produces two same-id links and one overwrites the other in the cache. Host doc §5.3: *"A node can have at most one parent. Adding a second incoming edge replaces the existing edge."*

## 🎯 Objective
`ApplyAddLink` must keep the model a valid single-parent, acyclic tree:
1. **Single-parent (replace):** before adding `childId` to the new parent, **remove `childId` from every other node's `ChildVisualIds`** (detach from its previous parent). After this, exactly one node lists `childId`.
2. **No-cycle (reject):** if attaching `childId` under `parentId` would create a cycle — i.e. `parentId` is `childId` itself, or `parentId` is within `childId`'s subtree (reachable by following `ChildVisualIds` from `childId`) — **do NOT add the link** (leave the model unchanged) and do not throw. (This is the backstop; the canvas validator should also reject, but the model must never hold a cycle.)

## File (exact)
`Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs` — rewrite the body of `ApplyAddLink` (keep the reversed-pin resolution: `from` = child output pin, `to` = parent input pin → `childId`, `parentId`):
```
resolve childId, parentId from pins (as now)
parent = _asset.FindNode(parentId); if null return
if (childId == parentId) return                      // self-parent
if (SubtreeContains(childId, parentId)) return       // would create a cycle
// single-parent: detach child from any current parent
foreach node in _asset.Nodes: if node.ChildVisualIds contains childId and node != parent → remove childId
if (!parent.ChildVisualIds.Contains(childId)) parent.ChildVisualIds.Add(childId)
_links[linkId] = (childId, parentId)
_asset.MarkDirty()
```
Add a private helper `bool SubtreeContains(Guid rootId, Guid targetId)` that walks `ChildVisualIds` from `rootId` (with a visited-set to be safe) and returns true if `targetId` is found in the subtree (including rootId). Use `_asset.FindNode`/`_asset.Nodes` for traversal.

*(Optional hardening, only if trivial: `BTreeLinkValidator` already has cycle detection; you may leave it — the command-sink backstop is the guarantee. Do NOT rework the validator's pin logic in this batch.)*

## 🧪 Tests (new file `Host/BTreeCommandSinkLinkTests.cs`, or extend an existing command-sink test)
Build a `BehaviorTreeAsset` + `BTreeGraphModel` + `BTreeCommandSink`; issue `GraphCommand.AddLink` with the correct pins (use the reversed convention — `from` = child's output pin, `to` = parent's input pin; get pin ids via the graph model / `BTreeEditorNode.OutputPinId`/`InputPinId`). Assert on the model:
- `AddLink_MovesChildToNewParent`: child C under parent P1; AddLink C→P2 → P1.ChildVisualIds no longer contains C, P2.ChildVisualIds contains C (single-parent replace).
- `AddLink_NoDuplicateParents`: after the above, **exactly one** node has C in its ChildVisualIds.
- `AddLink_WouldCreateCycle_IsRejected`: tree P→A→B; AddLink P-as-child-of-B (i.e. wire so B becomes parent of P, where P is B's ancestor) → model unchanged (B.ChildVisualIds does NOT contain P); no exception.
- `AddLink_SelfParent_IsRejected`: AddLink N→N → no change.
- `AddLink_NormalAttach`: a parentless node attached to a parent → added.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `Failed: 0` in `Hrot.BTree.Editor.Tests` (incl. new tests).
- [ ] Re-wiring a node MOVES it (old parent detached); no node ever has two parents; cycles/self-parent are rejected (model unchanged).
- [ ] Report written. (Visual confirmation that links no longer "disappear" and cycles can't be drawn → REVIEW-BT-2.)

## Notes
- This complements BATCH-14 (emit cycle guard): BT-15 stops cycles being created; BT-14 is the codegen safety net if one slips in.
- Do NOT touch `BTreeGraphModel`'s link cache — single-parent enforcement makes the one-link-per-child keying correct (no collision).
