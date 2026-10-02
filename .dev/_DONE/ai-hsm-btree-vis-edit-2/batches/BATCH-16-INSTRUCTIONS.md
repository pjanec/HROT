# BATCH-16 — Break-link works for projected (JSON-loaded) links

**Task:** TASK-BT-16 (Fix-A2 #3). **One objective.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating**; finish without asking until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-16-REPORT.md`.
- Context: `BTreeCommandSink.ApplyRemoveLinks` ([BTreeCommandSink.cs:151-163](../../Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs#L151)) resolves the link via a private `_links` dict that is populated **only by `ApplyAddLink` during this session**. Links loaded from JSON and projected by `BTreeGraphModel` are NOT in `_links`, so "Break link" (context menu) is a **no-op for any pre-existing wire**. The user can't delete existing links.

## 🎯 Objective
`ApplyRemoveLinks` must delete the link by resolving it through the **graph model** (works for both projected and session-added links), not the session-only `_links` dict.

## File (exact)
`Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs` — rewrite `ApplyRemoveLinks` to, for each `LinkId`:
1. `var link = _graph.FindLink(linkId);` — the `BTreeParentChildLink` (reversed convention: `FromPin` = child output pin, `ToPin` = parent input pin).
2. If found: `var childId = _graph.FindPin(link.FromPin)?.OwnerNodeId.Value;` and `var parentId = _graph.FindPin(link.ToPin)?.OwnerNodeId.Value;`. Then `_asset.FindNode(parentId)?.ChildVisualIds.Remove(childId)`.
3. Also remove from `_links` if present (keep it consistent; harmless if absent).
4. If `_graph.FindLink` returns null, fall back to the existing `_links` lookup (defensive).
5. `_asset.MarkDirty()` once after processing.

> Verify the actual member names on `BTreeParentChildLink`/`IPinModel` (`FromPin`, `ToPin`, `OwnerNodeId`) and `IGraphModel.FindLink`/`FindPin` against the real code — match them exactly. Do not invent members.

## 🧪 Tests (extend `Hrot.BTree.Editor.Tests/.../BTreeCommandSinkTests.cs`)
- `RemoveLinks_ProjectedLink_DeletesIt`: build a `BehaviorTreeAsset` where Root has a child C **without** going through `ApplyAddLink` (set `root.ChildVisualIds.Add(C)` directly, then construct `BTreeGraphModel` so the link is *projected*). Get the projected link's `LinkId` from `graph.Links` (the one whose pins resolve to child C → parent Root). `sink.Apply(new GraphCommand.RemoveLinks(new[]{ linkId }))` → assert `root.ChildVisualIds` no longer contains C.
- `RemoveLinks_SessionAddedLink_DeletesIt`: add a link via `ApplyAddLink` first, then remove it via its `LinkId` → child detached. (Regression — the existing path still works.)
- `RemoveLinks_UnknownLink_NoThrow`: removing a random/non-existent `LinkId` → no exception, model unchanged.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `Failed: 0` in `Hrot.BTree.Editor.Tests` (incl. new tests).
- [ ] Break-link deletes BOTH projected (JSON-loaded) and session-added links.
- [ ] Report written. (Visual confirm of the "Break link" context menu → REVIEW-BT-2.)

## Notes
- The fix is to resolve via `_graph` (authoritative for all rendered links), not the session `_links` dict.
- Don't change `ApplyAddLink` (BATCH-15) or the link-id derivation.
