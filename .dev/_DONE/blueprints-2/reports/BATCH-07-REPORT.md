# BATCH-07 Report

## Tasks Completed
- TASK-NEA-05: Attachment rendering (pills, glyphs, state outlines)
- TASK-NEA-06: Hit-testing for attachments (HoverKind.Attachment + HitTester)
- TASK-NEA-10: Low-zoom bar rendering (below zoom 0.5)
- Demo supplement: S34_NodeAttachments scenario (bonus, NEA-11 theme part was already done)

## Files Created
1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/AttachmentRenderer.cs` (NEW)
   - `AttachmentRenderer` sealed class with `DrawAll(...)` method
   - Normal-zoom: per-pill rendering with category color, state outlines, glyph + label text
   - Low-zoom (< 0.5): single 3 px colored bar above host node
2. `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/View/AttachmentHitTestTests.cs` (NEW)
   - 3 tests: HoverInfo stores AttachmentId, HoverInfo.None has empty attachment, HoverKind has Attachment
3. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeAttachmentModel.cs` (NEW)
   - Mutable `FakeAttachmentModel` implementing `IAttachmentModel` for demo use
4. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/Scenarios/S34_NodeAttachments.cs` (NEW)
   - Demo scenario with 4 nodes covering Decorator/Flag/Pure/Custom categories and row wrapping

## Files Modified
5. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/HoverInfo.cs`
   - Added `AttachmentId Attachment { get; init; }` field
   - Added `Attachment` to `HoverKind` enum
6. `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasLayout.cs`
   - Added `AttachmentLayouts` and `AttachmentScreenRects` dictionaries to `CanvasLayout`
   - Updated `Clear()` to clear both new dictionaries
   - Added per-node attachment layout computation in `CanvasLayoutBuilder.Build`
7. `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/HitTester.cs`
   - Updated `UpdateHover` signature to accept `Dictionary<AttachmentId, RectF> attachmentScreenRects`
   - Added section 2b: attachment pill hit-testing between wires (zLayer 1) and nodes (zLayer 2)
8. `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs`
   - Added `AttachmentRenderer _attachments` field
   - Updated `_hitTester.UpdateHover(...)` call to pass `_layout.AttachmentScreenRects`
   - Added `_attachments.DrawAll(...)` call after node rendering (step 8b)
9. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs`
   - Added `using System.Collections.Generic` and `using System.Linq`
   - Added `_attachments` dictionary backing store
   - Added `Attachments`, `FindAttachment`, `GetAttachmentsForNode` members (shadowing default interface members)
   - Added `AddAttachment(...)` and `RemoveAttachment(...)` mutable helpers
10. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/DemoShell.cs`
    - Registered `S34_NodeAttachments` scenario

## Test Counts
- Before: 82 (Core) + 10 (UI) = 92 total
- After:  85 (Core) + 10 (UI) = 95 total
- 3 new tests added in `AttachmentHitTestTests`

## Build
- 0 errors, 0 warnings
