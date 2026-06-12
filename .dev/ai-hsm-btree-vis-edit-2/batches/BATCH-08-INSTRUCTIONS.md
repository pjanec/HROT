# BATCH-08 — Wire the Add-Node picker for the BTree canvas (REVIEW-BT F2)

**Task:** TASK-BT-08 (REVIEW-BT finding F2). **One objective.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-08-REPORT.md`.
- **Exact template to mirror:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintPickerSources.cs` (the `BlueprintNodePickerSource : IPickerSource<NodeCatalogEntry>` + `Register(...)`), and its call site in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` (`BlueprintPickerSources.Register(bundle.PickerRegistry, nodeCatalog, …)`).

## 🎯 Objective
On the BTree canvas, pressing Tab or right-click → "Add Node…" calls `view.Host.Pickers.Open("nodes.all", …)`, but **no `"nodes.all"` picker source is registered** for BTree, so the picker silently cancels (the user sees nothing). Register a BTree node picker source backed by `BTreeNodeCatalog` so the palette opens and lists the catalog entries; choosing one places that node.

## Files (exact)
1. **NEW** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreePickerSources.cs`:
   - `internal sealed class BTreeNodePickerSource : IPickerSource<NodeCatalogEntry>` mirroring `BlueprintNodePickerSource` (same interface members; implement `Query(text, context)` by calling `_catalog.Query(new NodeSearchQuery(text))`; `GetItemKey` = `item.Kind.Id`; `GetSearchableText`/display = `item.DisplayName`; render the display name; no drag-in/out; not async; cheap).
   - `public static class BTreePickerSources { public static void Register(IPickerRegistry registry, BTreeNodeCatalog catalog) { … registry.Register("nodes.all", src); registry.Register("nodes.by-pin", src); } }`
   - Match the REAL `IPickerSource<T>` interface from NodeEditor (read it; implement every member). Do NOT invent members.
2. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` — after the GraphView/commands are built (near the `BuiltinCommandHandlers.RegisterAll(...)` call), add:
   `BTreePickerSources.Register(bundle.PickerRegistry, nodeCatalog);`

> The picker→place flow (picker returns a `NodeCatalogEntry` → canvas issues `GraphCommand.AddNode` with `entry.Kind`) is already handled generically by NodeEditor's canvas (same as Blueprint) and by `BTreeCommandSink.ApplyAddNode` (BT-01). You only need to register the source. If the place-flow needs the kind threaded a specific way, mirror exactly what Blueprint does.

## 🧪 Tests (new file `Host/BTreePickerSourceTests.cs`)
- `Register_AddsNodesAllSource`: a fake/real `IPickerRegistry` after `BTreePickerSources.Register(reg, new BTreeNodeCatalog())` has a source under `"nodes.all"`. (If `IPickerRegistry` has no public "contains" API, assert via opening/querying — read the interface and use what's available; otherwise test the source directly per below.)
- `PickerSource_Query_ReturnsCatalogEntries`: `new BTreeNodePickerSource(new BTreeNodeCatalog()).Query("Sequence", null)` contains the Sequence entry (`Kind.Id == "bt.composite.sequence"`).
- `PickerSource_Query_Empty_ReturnsManyStatics`: `Query("", null)` returns the static composite/leaf/decorator entries (count ≥ 10).
- `PickerSource_GetItemKey_IsKindId`: for a Sequence entry, `GetItemKey(entry) == entry.Kind.Id`.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings in `Hrot.BTree.Editor`.
- [ ] `dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests` — **Failed: 0** (incl. new tests).
- [ ] `"nodes.all"` (and `"nodes.by-pin"`) registered for BTree; source returns catalog entries.
- [ ] Report written. Note: the actual "picker opens visually + places node" is confirmed at the next visual review (it's ImGui UI) — your tests prove the source is registered + queryable.

## Notes
- Do NOT modify the generic NodeEditor canvas/picker code — only register the BTree source (the canvas already calls `Pickers.Open("nodes.all", …)`).
- If `IPickerSource<T>` has members whose exact semantics are unclear, copy the Blueprint implementation's behavior verbatim.
