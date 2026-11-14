# BATCH-08 Review — TASK-BT-08 Add-Node picker (REVIEW-BT F2)

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent)
- New `BTreePickerSources.cs`: full `IPickerSource<NodeCatalogEntry>` mirroring `BlueprintNodePickerSource`; `Query` does pin-context filtering (`QueryForPinContext`) when context carries pin info, else `_catalog.Query`. `Register` wires `"nodes.all"` + `"nodes.by-pin"`. `GetItemKey = Kind.Id`.
- `BTreeDocumentFactory.Build` registers it after `BuiltinCommandHandlers.RegisterAll` (mirrors Blueprint). Only addition; nothing else touched.
- Build 0 warn/0 err; `dotnet test Hrot.BTree.Editor.Tests` → **491 passed / 0 failed** (485 + 6).

## Issues
None.

## Verdict
APPROVED. Root cause (unregistered `"nodes.all"` source) fixed. The picker *opening + placing a node visually* is confirmed at REVIEW-BT-2 (ImGui UI); tests prove the source is registered + queryable.

## Commit message
```
fix(btree-editor): register Add-Node picker source for the BTree canvas (BATCH-08 / TASK-BT-08)

BTreeNodeCatalog was never registered with IPickerRegistry, so Tab / "Add Node…"
opened nothing ("nodes.all" lookup silently cancelled). Add BTreePickerSources
(mirrors BlueprintPickerSources) registering "nodes.all"/"nodes.by-pin" backed by
the catalog, wired in BTreeDocumentFactory. +6 tests. Fixes REVIEW-BT F2.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
