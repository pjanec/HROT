# FIX1-BATCH-03 — Phases 2, 3, & 4: NodeEditor Extensions (Interaction & Hit-Testing)

## Tasks Covered
- **TASK-NEA-03** — Expand `GraphChangeNotification` record to include `IReadOnlySet<AttachmentId>? AffectedAttachments`.
- **TASK-NEA-06, TASK-NEC-05, TASK-NER-04** — Rewrite Z-Layer priorities in `HitTester.cs` to strictly adhere to the 15-step hierarchy.
- **TASK-NEC-06** — Update `CanvasInput.cs` drag-and-drop to evaluate drop coordinates against spatial index for container node reparenting, emitting `GraphCommand.ChangeParent`.
- **TASK-NER-07** — Hook `HoverKind.CustomElement` in right-click handler to query `ICustomElementContextMenuProvider` and display host-provided context menus.

## Onboarding

You are working on the `IOS-IG-SimHost-FDP-2` project. This batch fixes the canvas interaction
layer for the three NodeEditor extensions (NodeAttachments, ContainerNodes, and CustomCanvasRenderer).
The rendering pipeline is in place; what is missing is the interaction side: hit-testing and event routing.

**Mandatory read before coding:**
- `.dev/blueprints-2/FIX1-TASK-DETAIL.md` — "ACTION PACKET 3: Phases 2, 3, & 4" section.
- `.dev/blueprints-2/FIX1-TASK-EXTRA-DETAILS.md` — "ACTION PACKET 3: Phases 2, 3, & 4 — NodeEditor Extensions (Interaction & Hit-Testing)" with the 15-step z-order list and detailed reparenting steps.
- `.dev/blueprints-2/NodeEditor_Extension_NodeAttachments.md` — §4.4 (GraphChangeNotification).
- `.dev/blueprints-2/NodeEditor_Extension_ContainerNodes.md` — §10.1 (drag reparenting).
- `.dev/blueprints-2/NodeEditor_Extension_CustomCanvasRenderer.md` — §8.1 (z-order), §9.4 (context menus).
- `.dev/blueprints-2/ACCEPTANCE-CRITERIA.md` — F2-02, F3-03, F4-03, F4-05.
- `FDP/ExtDeps/NodeEdit/src/` — NodeEditor Core and UI source code.

## Developer Insights Section

After implementing, answer the following in your report:
1. What issues were encountered (unexpected APIs, missing interfaces, etc.)?
2. What weak points did you spot in the existing codebase?
3. What design decisions were made beyond the spec?

---

## Tasks

### TASK-NEA-03: Missing Attachment Change Notifications

**Target files:**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs` (or wherever `GraphChangeNotification` is defined)

**Summary:**
1. Locate the `GraphChangeNotification` record (or class).
2. Add an optional property: `IReadOnlySet<AttachmentId>? AffectedAttachments = null`.
3. Verify existing code that creates `GraphChangeNotification` still compiles (the new property has a default, so it is backward compatible).

**Note:** The BTree and HSM command sinks will populate this field in later batches. This task only adds the field to the record.

**Acceptance criteria:** F2-02 (partial — the field must exist; population will be done in batch 04).

**Tests required:**
- Add a test that:
  1. Creates a `GraphChangeNotification` with `AffectedAttachments = new HashSet<AttachmentId> { attachmentId }`.
  2. Asserts the property is non-null and contains the expected ID.
  3. Creates a `GraphChangeNotification` without specifying `AffectedAttachments`.
  4. Asserts the property is null (default).

---

### TASK-NEA-06, TASK-NEC-05, TASK-NER-04: Hit-Testing Z-Order

**Target files:**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/HitTester.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-NEA-06, TASK-NEC-05, TASK-NER-04: Hit-Testing Z-Order Convergence".

**Summary:** Rewrite the hit-test evaluation sequence (the `UpdateHover` method or equivalent) to test in exactly this order (highest priority wins). Do NOT skip any step:

| Priority | Element |
|----------|---------|
| 15 (highest) | Reroutes |
| 14 | Pins |
| 13 | Wires |
| 12 | Custom `TopMost` render pass elements (via `ICustomCanvasHitTester`) |
| 11 | Attachments (highest `StackIndex` first) |
| 10 | Custom `AfterNodes` render pass elements |
| 9 | Container collapse-arrow chevrons |
| 8 | Container header strips |
| 7 | Comment title bars |
| 6 | Custom `AfterWires` render pass elements |
| 5 | Node bodies (regular nodes and container children) |
| 4 | Custom `BeforeContent` render pass elements |
| 3 | Container interiors (empty area not covered by children) |
| 2 | Comment bodies (pass-through) |
| 1 | Empty Canvas |

For custom render pass elements (steps 4, 6, 10, 12): iterate the registered `ICustomCanvasRenderer` instances and call `ICustomCanvasHitTester.TryHitTest(pos, out hit)` if the renderer implements that interface.

For attachments (step 11): iterate `IGraphModel.Attachments` (or the per-node attachment list), sorted by `StackIndex` descending; test bounds intersection.

For container chevrons (step 9) and header strips (step 8): test against `IContainerNodeModel` instances only.

**Acceptance criteria:** F2-02, F3-03, F4-03.

**Tests required:**
- Add unit tests that:
  1. Place a custom `TopMost` renderer element and a node at the same canvas position; assert the `TopMost` element wins the hit test.
  2. Place an attachment and a node body at the same position; assert the attachment wins.
  3. Place a container header strip and a node body at the same position; assert the header wins over node body.
  4. Place nothing at a position; assert `HoverKind.Canvas` (empty) is returned.
  5. (Bonus) Place a wire and an attachment at the same position; assert wire wins over attachment.

---

### TASK-NEC-06: Container Reparenting via Drag-and-Drop

**Target files:**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-NEC-06: Container Reparenting via Drag-and-Drop".

**Summary:**
1. In the `MouseButton.Left` release handler (where `InteractionMode.DraggingNodes` concludes):
   a. Call `UpdateContainerDropTarget` (or equivalent spatial query) to find the innermost valid `IContainerNodeModel` under the drop cursor position.
   b. For each dragged node, compare the resolved target container ID (or `null` for root canvas) with the node's current `ParentContainerId`.
   c. If they differ, emit a `GraphCommand.ChangeParent` command instead of `GraphCommand.MoveNodes`. Calculate `NewLocalPosition` by subtracting the new parent container's interior origin from the dropped canvas position.
   d. Prevent cycles: if the dragged node IS a container, ensure the target is not a descendant of the dragged node.
2. If no container change, emit the normal `GraphCommand.MoveNodes` as before.

**Acceptance criteria:** F3-03.

**Tests required:**
- Add tests that:
  1. Drag a node onto a container's interior → asserts `GraphCommand.ChangeParent` is emitted with correct `NewParentId` and `NewLocalPosition`.
  2. Drag a node within the same container (not crossing boundary) → asserts `GraphCommand.MoveNodes` is emitted (no reparent).
  3. Drag a node from inside a container to the root canvas → asserts `GraphCommand.ChangeParent` with `NewParentId = null`.
  4. Drag a container onto one of its own descendants → asserts NO `ChangeParent` command is emitted (cycle prevention).

---

### TASK-NER-07: Custom Element Context Menus

**Target files:**
- `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasInput.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-NER-07: Custom Element Context Menus".

**Summary:**
1. In the right-click handler:
   a. Check if the active hover state is `HoverKind.CustomElement`.
   b. If so, extract the `CustomElementRef` (containing `RendererId` and `ElementKey`).
   c. Iterate registered `ICustomElementContextMenuProvider` instances (from the host services or renderer registry); find one whose `RendererId` matches.
   d. Call `provider.GetMenuItems(elementKey)` to retrieve `IReadOnlyList<ContextMenuItem>`.
   e. Display them in an ImGui context menu popup.
2. If no provider matches, fall back to the default canvas context menu.

**Acceptance criteria:** F4-05.

**Tests required:**
- Add tests that:
  1. Register a mock `ICustomElementContextMenuProvider` for a given `RendererId`; simulate a right-click on a `HoverKind.CustomElement` with matching `RendererId`; assert `GetMenuItems` was called with the correct `ElementKey`.
  2. Simulate a right-click on a `CustomElement` with no matching provider; assert the default context menu path is taken (no crash).

---

## Mandatory Workflow: Test-Driven Task Progression

For every task:
1. **Read** the spec and acceptance criteria first.
2. **Write or update the test** before or alongside the implementation.
3. **Implement** the feature/fix.
4. **Run the tests** and confirm they pass.
5. **Do not mark a task complete** unless its tests pass.

Do not swallow exceptions silently. Let failures surface loudly.

---

## Build & Test Commands

```powershell
cd "FDP/ExtDeps/NodeEdit"
dotnet build
dotnet test
```

---

## Report Format

Write your report to: `.dev/blueprints-2/reports/FIX1-BATCH-03-REPORT.md`

**Required sections:**
1. **Summary** — What was implemented.
2. **Task Status** — Per-task: Implemented / Partial / Blocked.
3. **Tests** — List of test methods added, and pass/fail status.
4. **Developer Insights**:
   - Issues encountered
   - Weak points spotted
   - Design decisions beyond spec
5. **Build Output** — Paste relevant `dotnet build` / `dotnet test` output (last 30 lines minimum).
