# FIX1-BATCH-03 Review

**Batch:** FIX1-BATCH-03 — Phases 2, 3, & 4: NodeEditor Extensions (Interaction & Hit-Testing)  
**Tasks:** TASK-NEA-03, TASK-NEA-06/NEC-05/NER-04, TASK-NEC-06, TASK-NER-07  
**Status:** APPROVED

---

## Verification Summary

### TASK-NEA-03 — GraphChangeNotification.AffectedAttachments
- Property already existed in codebase; correctly identified as no-op. ✅
- 5 new tests verify default null and explicit set behavior. ✅
- F2-02 (partial): SATISFIED

### TASK-NEA-06 / TASK-NEC-05 / TASK-NER-04 — HitTester Z-Order
- 14 named `internal const int` constants replace all hardcoded z-layer integers. ✅
- All 15-step spec priorities verified: Reroute(15) > Pin(14) > Wire(13) > TopMost(12) > Attachment(11) > AfterNodes(10) > ContainerChevron(9) > ContainerHeader(8) > CommentHeader(7) > AfterWires(6) > NodeBody(5) > BeforeContent(4) > ContainerInterior(3) > CommentBody(2) > Canvas(1). ✅
- Key bug fixed: Pins now correctly beat wires (14 > 13). ✅
- Key bug fixed: Attachment StackIndex now used instead of raw enumerate index. ✅
- 14 new tests in HitTesterZOrderTests (9 constant ordering assertions + 5 behavioral). ✅
- F2-02, F3-03, F4-03: SATISFIED

### TASK-NEC-06 — Container Reparenting via Drag-and-Drop
- Already implemented (CommitNodeDrop / UpdateContainerDropTarget / cycle detection). ✅
- Tests added to verify existing behavior. ✅
- F3-03: SATISFIED

### TASK-NER-07 — Custom Element Context Menus
- `CanvasRenderer.DrawContextMenu` handles `HoverKind.CustomElement` correctly. ✅
- Provider RendererId matched before calling `GetItemsFor`. ✅
- Null-safe fallback to empty popup when no provider registered. ✅
- 4 new routing tests. ✅
- F4-05: SATISFIED

## Test Results Verified
- NodeEditor.Core.Tests: 181/181 pass ✅
- NodeEditor.UI.Tests: 26/26 pass ✅

## No new debt items.

## Suggested Git Commit Message
Already committed: `fix(node-editor): FIX1-BATCH-03 NodeEditor extensions interaction layer` (84abef4f)
