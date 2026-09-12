# DEC-04 — Pill editing: click-select, Del-remove, inspector properties

**Workstream:** DEC ([../DEC-PLAN.md](../DEC-PLAN.md)). **Layer:** NodeEditor.Core + NodeEditor.UI (generic, additive) + Hrot.BTree.Editor host. **Depends:** DEC-02/03/03b (landed). **Size: medium-large.**

## Why

A decorator pill can be added but not edited or removed: it can't be selected by clicking, Del doesn't remove it, and the inspector shows nothing for it. The editing UI (facets) already exists; this batch wires selection + delete (generic, like nodes) and the inspector facet routing (BTree).

## Verified facts (use these exactly)

- `HoverKind.Attachment` exists (`HoverInfo.cs:32`) and the **HitTester already returns it** (`HitTester.cs:190` submits a `HoverKind.Attachment` hit for pills). So hover/hit-test works; the gap is the click handler.
- `CanvasInput` left-click switch (`CanvasInput.cs` ~137-264) has cases for Pin/Node/Reroute/Container but **NO `HoverKind.Attachment` case** → clicking a pill selects nothing.
- `SelectionEntry.OfAttachment(AttachmentId)` exists (`SelectionEntry.cs:34`); `SelectionState.Attachments` exists.
- **Two Delete paths**, neither handles attachments: `EditCommands.DeleteSelected` (`EditCommands.cs:58`, bound to the Delete command) handles Reroutes/Links/Nodes/Comments; `CanvasInput.DeleteSelected` (`CanvasInput.cs:1228`, fired on Delete/Backspace in idle). Update BOTH (or confirm which actually fires and update that one — but be safe).
- `AttachmentRenderer` (`NodeEditor.UI/Canvas/AttachmentRenderer.cs`) — confirm it draws a selection outline when the attachment is in `SelectionState.Attachments`; if it doesn't, add it (design: 2px accent outline like a selected node).
- BTree inspector: `BTreeFacetMapper : IFacetDispatcher` has `GetFacet(IAssetSubSelection)` and `ApplyFacet(IAssetSubSelection, facet)`. Both currently handle ONLY `BTreeNodeSelection` (map node-type→facet; ApplyFacet mutates the asset node directly + `_asset.MarkDirty()` — NO command sink). Pill facets (`BTreeRepeaterFacet{int Count; string? Comment; string VisualId}`, `BTreeCooldownFacet{float Duration; …}`, `BTreeInverterFacet`/ForceSuccess/ForceFailure/UntilSuccess/UntilFailure `{string? Comment; string VisualId}`) already exist in `BTreeFacets.cs:179-260` with `[Edit*]` attributes — but nothing builds them.
- Selection→inspector bridge: `BTreeSelectionBridgeHelper.MapSelection(SelectionState, asset)` reads only `selection.Nodes` → returns `BTreeNodeSelection(visualId)`. It ignores `selection.Attachments`.
- Pill model: `BTreePillAttachmentModel(pill)` wraps a `BTreeEditorPill`; `_attachmentCache[model.Id] = model`. Confirm `model.Id == new AttachmentId(pill.VisualId)` (so an `AttachmentId.Value` maps to a pill `VisualId`). The asset has `FindPill(Guid)`, `RemovePill`, `AddPill`. `BTreeEditorPill` fields: `VisualId, HostNodeVisualId, DecoratorType (NodeType), IntParam (int?), FloatParam (float?), Comment, StackIndex`.

## Implementation

### A. NodeEditor.Core (additive)
1. `IAttachmentModel`: add `IReadOnlyDictionary<string, object?>? HostProperties => null;` (default-impl null → additive; no other impl breaks). Lets a host carry restore data so a deleted attachment can be faithfully recreated on undo.

### B. NodeEditor.UI (generic — pills behave like nodes)
2. `CanvasInput` left-click switch: add `case HoverKind.Attachment:` mirroring the node-select logic (no drag): plain click → `view.Selection.ReplaceWith(SelectionEntry.OfAttachment(hover.Attachment))`; Ctrl → `Toggle`; Shift → `Add`. (Match the existing modifier handling used for nodes.)
3. Delete handling — in BOTH `EditCommands.DeleteSelected` and `CanvasInput.DeleteSelected`, handle `view.Selection.Attachments`:
   - Forward: `new GraphCommand.RemoveAttachments(attachmentIds)`.
   - Inverse: for each removed attachment, look up `view.Model.FindAttachment(id)` and build `new GraphCommand.AddAttachment(m.Id, m.HostNodeId, m.Category, m.Glyph, m.Label, m.Tooltip, m.StackIndex, m.HostProperties)`. Add to the batch with the existing reverse-order inverse discipline.
   - After delete, clear selection as the existing code does.
4. `AttachmentRenderer`: ensure selected attachments render the selection outline (verify; add if missing).

### C. Hrot.BTree.Editor host
5. New sub-selection type `BTreePillSelection(Guid PillVisualId) : IAssetSubSelection` (sibling of `BTreeNodeSelection` — put it in the same file/namespace as `BTreeNodeSelection`; find it via grep).
6. `BTreeSelectionBridgeHelper.MapSelection`: if `selection.Attachments` has exactly one entry → return `new BTreePillSelection(attachmentId.Value)`; else fall back to the existing node logic. (Update the publish path `MapSelection` + the per-frame `Bridge` method's return type if needed — it currently returns `BTreeNodeSelection?`; widen to `IAssetSubSelection?`.)
7. `BTreeFacetMapper.GetFacet`: add a branch — if `subSelection is BTreePillSelection ps`, `var pill = _asset.FindPill(ps.PillVisualId)`; build the facet by `pill.DecoratorType`:
   - Repeater → `new BTreeRepeaterFacet { Count = pill.IntParam ?? 1, Comment = pill.Comment, VisualId = pill.VisualId.ToString() }`
   - Cooldown → `new BTreeCooldownFacet { Duration = pill.FloatParam ?? 1f, Comment = pill.Comment, VisualId = ... }`
   - Inverter/ForceSuccess/ForceFailure/UntilSuccess/UntilFailure → the matching facet with `Comment` + `VisualId`.
8. `BTreeFacetMapper.ApplyFacet`: add a branch — if `subSelection is BTreePillSelection ps`, `var pill = _asset.FindPill(...)`; switch on the facet type and write back: `BTreeRepeaterFacet → pill.IntParam = rf.Count`; `BTreeCooldownFacet → pill.FloatParam = cf.Duration`; all → `pill.Comment = facet.Comment`; then `_asset.MarkDirty()`.
9. `BTreePillAttachmentModel`: implement `HostProperties` to return the restore dict so DEC-04-A undo works: `{ ["decoratorType"] = pill.DecoratorType, ["intParam"] = pill.IntParam, ["floatParam"] = pill.FloatParam, ["comment"] = pill.Comment }` (omit null entries or leave them — `ApplyAddPill` already guards types). This makes the generic delete-inverse `AddAttachment` restore the pill exactly via the existing `ApplyAddPill`.

## Constraints
- Additive in core (`HostProperties` default-null; new `HoverKind.Attachment` case; attachments added to delete). No behaviour change for nodes. HSM/Blueprint hosts unchanged (they have no attachments today → no-ops).
- Inspector edits mutate the asset directly + `MarkDirty()` (match the existing `ApplyFacet` node pattern; do NOT route through the command sink).
- No `.btree.json`/codegen changes. (But after building, if you hit a Parallel `CS7036`, run `dotnet build-server shutdown` — stale analyzer cache, see DEC-PLAN gotcha; it is NOT your bug.)

## Tests
- **NodeEditor.UI/Core.Tests:** if drivable, a `HoverKind.Attachment` left-click selects the attachment; Delete with an attachment selected emits `RemoveAttachments` + an inverse `AddAttachment` carrying the model's `HostProperties`. If the ImGui-bound paths aren't unit-testable, cover what is (e.g. `DeleteSelected` command construction over a fake model with one attachment) and note the gap honestly.
- **Hrot.BTree.Editor.Tests:**
  - `BTreeSelectionBridgeHelper.MapSelection` with one selected attachment → `BTreePillSelection` with the right VisualId.
  - `BTreeFacetMapper.GetFacet(BTreePillSelection)` for a Repeater pill → `BTreeRepeaterFacet` with `Count == pill.IntParam`; Cooldown → `Duration`.
  - `ApplyFacet(BTreePillSelection, BTreeRepeaterFacet{Count=7})` → `asset.FindPill(...).IntParam == 7`; Cooldown duration likewise; Comment persists.
  - `BTreePillAttachmentModel.HostProperties["decoratorType"]` equals the pill's `DecoratorType` (guards delete-undo).
  - Round-trip safety: editing a pill's count then saving keeps it (mirror existing pill round-trip tests).

## Verification (run + paste RAW output)
1. `dotnet build` NodeEditor.Core, NodeEditor.UI, `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`, `Hrot.Blueprints.Editor` → 0 errors. (If a Parallel CS7036 appears, `dotnet build-server shutdown` then rebuild — stale analyzer, not your change.)
2. `dotnet test` NodeEditor.Core.Tests, NodeEditor.UI.Tests, `Hrot.BTree.Editor.Tests` → counts; no new failures vs baseline (Core 192/0, UI 78/0, BTree.Editor 536/0).

## Report back
Diff summary; how pill selection + Delete were wired (and whether undo restores the pill); how the inspector facet routing works; whether the UI paths were unit-testable + any gap; raw build + test output. **Do NOT commit** — lead reviews & commits.
