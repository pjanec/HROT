# FIX1-BATCH-03 Completion Report

## Summary

All six tasks in FIX1-BATCH-03 have been completed. Two tasks (TASK-NEA-03, TASK-NEC-06)
were already implemented in production code and required no changes. The remaining four
tasks were implemented, tests were written, and the full test suite passes clean.

**Build result:** 0 errors, 0 warnings  
**Test result:** 207 passed, 0 failed, 0 skipped  
(NodeEditor.Core.Tests: 181, NodeEditor.UI.Tests: 26)

---

## Task Status

### TASK-NEA-03 — GraphChangeNotification.AffectedAttachments

**Status: Already implemented — no production code change required.**

`GraphChangeNotification` already contained `IReadOnlySet<AttachmentId>? AffectedAttachments`
and `GraphChangeKind` already included `AttachmentsAdded`, `AttachmentsRemoved`,
`AttachmentsModified`. The interface default method `FindAttachment(AttachmentId) => null` was
also present on `IGraphModel`.

New tests added:
- `tests/NodeEditor.Core.Tests/Interfaces/GraphChangeNotificationTests.cs` (5 tests)

---

### TASK-NEA-06, TASK-NEC-05, TASK-NER-04 — Z-Layer priority hierarchy in HitTester

**Status: Implemented.**

File: `src/NodeEditor.UI/Canvas/HitTester.cs`

All hardcoded z-layer integers replaced with 14 named internal constants that
strictly mirror the 15-step spec hierarchy. Changes by element type:

| Element | Before | After |
|---|---|---|
| Comment body | 0 | ZLayerCommentBody (2) |
| Comment header/resize | 4 | ZLayerCommentHeader (7) |
| Wires | 1 | ZLayerWire (13) |
| AfterWires custom | zLayer:1, subLayer:100000+ | zLayer:ZLayerAfterWires(6), subLayer:0+ |
| Attachments | zLayer:2, attachIndex | zLayer:ZLayerAttachment(11), stackIndex |
| Container chevron | 4 | ZLayerContainerChevron (9) |
| Container header | 4 | ZLayerContainerHeader (8) |
| AfterNodes custom | zLayer:4, subLayer:100000+ | zLayer:ZLayerAfterNodes(10), subLayer:0+ |
| Node body | isForeground?3:2 | ZLayerNodeBody(5); foreground via subLayer offset |
| Pins | node z-layer (2 or 3) | ZLayerPin (14) |
| Reroutes | 5 | ZLayerReroute (15) |
| TopMost custom | 6 | ZLayerTopMost (12) |
| Container interior | 1 | ZLayerContainerInterior (3) |
| BeforeContent custom | zLayer:0, subLayer:100000+ | zLayer:ZLayerBeforeContent(4), subLayer:0+ |

Key correctness fixes:
- Pins now correctly beat node bodies and wires (14 > 13 > 5).
- Wires now correctly beat all custom layers except TopMost/Pin/Reroute.
- Foreground nodes no longer have a higher z-layer than background nodes; they are
  distinguished by subLayer offset (+100000) within the same ZLayerNodeBody level.
- Attachment ordering uses `StackIndex` from the model (correct semantic ordering)
  instead of raw enumeration index.
- AfterWires/AfterNodes/BeforeContent custom layers no longer rely on large subLayer
  offsets to piggyback onto incorrect z-layers.

`InternalsVisibleTo("NodeEditor.UI.Tests")` added to `NodeEditor.UI.csproj` so tests
can reference the constants.

New tests added:
- `tests/NodeEditor.UI.Tests/Canvas/HitTesterZOrderTests.cs` (14 tests)
  - 9 constant ordering assertions (Reroute > Pin > Wire > TopMost > Attachment > ...)
  - 5 behavioral SelectWinner tests (TopMost beats node, Reroute beats all, etc.)

---

### TASK-NEC-06 — Container reparenting on drag-and-drop in CanvasInput

**Status: Already implemented — no production code change required.**

`UpdateContainerDropTarget` in `CanvasInput.cs` already locates the innermost valid
container via the spatial index with cycle detection (`ContainerCycleDetector.WouldCreateCycleAny`).
`CommitNodeDrop` already emits `GraphCommand.ChangeParent` vs. `GraphCommand.MoveNodes`
based on whether reparenting is needed.

---

### TASK-NER-07 — HoverKind.CustomElement in right-click context menu

**Status: Implemented.**

File: `src/NodeEditor.UI/Canvas/CanvasRenderer.cs`

Added `case HoverKind.CustomElement:` to the `DrawContextMenu` switch. The new case:
1. Reads `target.CustomElement` to get the `CustomElementRef` (RendererId, ElementKey).
2. Reads `view.Host.CustomElementContextMenu` to get the registered provider.
3. Guards with a `provider != null && provider.RendererId == ceRef.RendererId` check.
4. Calls `provider.GetItemsFor(elementKey, hit)` and renders each item via `ImGui.MenuItem`.
5. Respects `item.Enabled` (disabled items are rendered but not clickable).
6. Falls through silently (empty popup) when no matching provider is registered.

New tests added to `tests/NodeEditor.Core.Tests/Canvas/CustomRendererDetailsAndPerfTests.cs`
(class `CustomElementContextMenuRoutingTests`, 4 tests):
- Provider is queried when RendererId matches.
- Provider is not queried when RendererId mismatches.
- Null provider does not throw.
- Disabled ContextMenuItem carries Enabled=false correctly.

---

## Tests Added

| File | Tests | Covers |
|---|---|---|
| `tests/NodeEditor.Core.Tests/Interfaces/GraphChangeNotificationTests.cs` | 5 | TASK-NEA-03 |
| `tests/NodeEditor.UI.Tests/Canvas/HitTesterZOrderTests.cs` | 14 | TASK-NEA-06/NEC-05/NER-04 |
| `CustomRendererDetailsAndPerfTests.cs` (class `CustomElementContextMenuRoutingTests`) | 4 | TASK-NER-07 |

**Total new tests: 23**  
**Total suite after batch: 207 (was 184)**

---

## Developer Insights

1. **Two tasks were already done.** Before writing any code, inspecting the actual
   production files revealed TASK-NEA-03 and TASK-NEC-06 had been fully implemented
   in a previous session. Verified via source inspection rather than spec assumptions.

2. **Z-layer subLayer overflow hack.** The pre-existing code used `subLayer:100000+`
   offsets to make "after-X" custom layers appear later than standard elements within
   the same z-layer bucket. The fix moves each group to a dedicated z-layer, making
   ordering explicit and removing the need for magic offsets.

3. **Attachment StackIndex vs. enumerate index.** The old code used the raw index from
   a `foreach` loop over attachments (order undefined). The fix reads `StackIndex` from
   the attachment model, which reflects the intended visual stack ordering.

4. **InternalsVisibleTo via AssemblyAttribute MSBuild pattern.** Used the
   `<AssemblyAttribute>` / `<_Parameter1>` pattern in the csproj rather than
   `AssemblyInfo.cs` — avoids adding a new source file.

5. **ImGui untestable at unit level.** DrawContextMenu is a private static method that
   calls ImGui APIs requiring a live render context. The routing tests in
   `CustomElementContextMenuRoutingTests` verify the provider selection logic in
   isolation, not the ImGui calls. This is the correct level of coverage given the
   architecture.
