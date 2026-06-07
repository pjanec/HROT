# FIX1-BATCH-04 — Phases 5 & 6: BTree & HSM Authoring Hosts

## Tasks Covered
- **TASK-BT-S1-11** — Implement BTree observer guard badge renderer (`btree.observer_guard_badges` custom canvas pass) to draw `👁 OBSERVES` pills on wires from `ObserverSelector` to Guard children.
- **TASK-HS-S1-08, TASK-HS-S1-10** — Replace `/* TODO */` stubs in `HsmCommandSink.cs` for `ApplyAddRegion`, `ApplyRemoveRegion`, `ApplyReorderRegions`, `ApplyAddAttachment`, `ApplyRemoveAttachments`.
- **TASK-HS-S1-14** — Update the HSM transition renderer to draw `TransitionKind.Internal` transitions as dashed loops strictly inside the source state bounding box.

## Onboarding

You are working on the `IOS-IG-SimHost-FDP-2` project. This batch fixes the authoring hosts
for BTree and HSM editors. The projection/layout models are correct, but several command sinks
are stubbed and specific visual requirements from the specs were skipped.

**Mandatory read before coding:**
- `.dev/blueprints-2/FIX1-TASK-DETAIL.md` — "ACTION PACKET 4: Phases 5 & 6 — BTree & HSM Authoring Hosts".
- `.dev/blueprints-2/FIX1-TASK-EXTRA-DETAILS.md` — "ACTION PACKET 4: Phases 5 & 6" with detailed steps.
- `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md` — BTree host design (observer badges).
- `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md` — HSM host design (§7.4 internal transitions).
- `.dev/blueprints-2/NodeEditor_Extension_NodeAttachments.md` — §4.4 (AffectedAttachments notifications).
- `.dev/blueprints-2/ACCEPTANCE-CRITERIA.md` — F5-13, F6-05, F6-18, F6-19.

## Developer Insights Section

After implementing, answer the following in your report:
1. What issues were encountered?
2. What weak points did you spot in the existing codebase?
3. What design decisions were made beyond the spec?

---

## Tasks

### TASK-BT-S1-11: BTree Observer Guard Badges

**Target files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/ObserverGuardBadgeRenderer.cs`
  (or the file registered for `btree.observer_guard_badges` custom canvas pass)

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-BT-S1-11: BTree Observer Guard Badges".

**Summary:**
1. Locate the renderer registered for the `btree.observer_guard_badges` custom canvas pass.
2. Inside its `Render(ICanvasRenderContext ctx)` method, iterate over `ctx.Graph.Links`.
3. For each link: resolve `FromNode` and `ToNode`.
4. If `FromNode.Kind.Id == "bt.composite.observerSelector"` AND `ToNode.Kind.Id` is `"bt.leaf.condition"` or `"bt.leaf.observer"`:
   - Calculate the bezier midpoint biased toward the parent: use `t = 0.3f` on the link bezier curve.
   - Render a small filled ImGui rect/pill containing the text `👁 OBSERVES` at that coordinate (using `ImGui.GetWindowDrawList().AddRectFilled` + `ImGui.GetWindowDrawList().AddText` or equivalent).
5. If the renderer class body is empty/stub, implement the loop.

**Acceptance criteria:** F5-13.

**Tests required:**
- Add a test that:
  1. Creates a mock `ICanvasRenderContext` with a graph containing:
     - An `ObserverSelector` node linked to a `Condition` child.
     - A plain `Selector` node linked to an `Action` child (no badge expected).
  2. Calls `Render(ctx)`.
  3. Asserts exactly one badge was emitted (e.g., by verifying the draw list calls or a test-observable tracking mechanism).

---

### TASK-HS-S1-08 & TASK-HS-S1-10: Implement `HsmCommandSink` Stubs

**Target files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-HS-S1-08 & TASK-HS-S1-10: Implement `HsmCommandSink` Stubs".

**Summary:** Replace the five `/* TODO */` stub methods with real implementations:
1. **`ApplyAddRegion(cmd)`**: Resolve `cmd.ContainerId` to the parent `StateNode`. Insert a new `RegionDescriptor` at `cmd.Index` into `stateNode.Regions`. Call `_asset.MarkDirty()`. Fire `IGraphModel.Changed` with `AffectedNodes = { cmd.ContainerId }`.
2. **`ApplyRemoveRegion(cmd)`**: Resolve container `StateNode`. Remove the region at `cmd.RegionIndex`. Move children of the removed region to region 0 (or the canonical merge region). Mark dirty, fire Changed.
3. **`ApplyReorderRegions(cmd)`**: Resolve container `StateNode`. Apply the `cmd.NewOrder` permutation to `Regions`. Mark dirty, fire Changed.
4. **`ApplyAddAttachment(cmd)`**: Resolve `cmd.HostNodeId` to the state node. Add the attachment record to `HsmAsset.Attachments` (or the state's attachment list). Mark dirty. Fire `IGraphModel.Changed` with `AffectedAttachments = { cmd.AttachmentId }`.
5. **`ApplyRemoveAttachments(cmd)`**: Resolve host nodes, remove attachment records. Mark dirty. Fire Changed with `AffectedAttachments = cmd.AttachmentIds`.

**Acceptance criteria:** F6-05.

**Tests required:**
- Add tests for each method that:
  1. Set up an `HsmAsset` with appropriate initial state.
  2. Call the command sink method with a valid command.
  3. Assert the asset was mutated correctly (e.g., region count increased, attachment added).
  4. Assert `MarkDirty()` was called (mock or flag-based).
  5. Assert `IGraphModel.Changed` was fired with the correct `AffectedNodes` / `AffectedAttachments`.

---

### TASK-HS-S1-14: Internal Transition Rendering

**Target files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/` — Transition renderer or custom canvas renderer.

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-HS-S1-14: Internal Transition Rendering".

**Summary:**
1. Locate where HSM transitions are rendered (likely a custom canvas renderer hooked to `hsm.transition_labels` or a dedicated internal transition renderer).
2. For transitions where `Kind == TransitionKind.Internal`:
   a. Set the underlying NodeEditor `ILinkModel.Style` to hidden/transparent so the default wire renderer skips it.
   b. In the custom renderer `Render` pass, draw a dashed curved path (or small looping arrow) that is entirely within the `NodeInteriorBounds` of the source state.
   c. Render the event/action label directly next to the inner loop.
3. External transitions continue to use the standard bezier wire rendering.

**Acceptance criteria:** F6-18, F6-19.

**Tests required:**
- Add a test that:
  1. Creates an `HsmAsset` with an internal transition on state "A".
  2. Calls the transition renderer.
  3. Asserts that the internal transition was rendered inside `NodeInteriorBounds` (not as a bezier arc exiting the node).
  4. Asserts that the external transitions still use the standard wire rendering path.

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
# Build and test the Hrot subsystems
cd "d:\Work\IOS-IG-SimHost-FDP-2"
dotnet build Hrot/
dotnet test Hrot/
```

---

## Report Format

Write your report to: `.dev/blueprints-2/reports/FIX1-BATCH-04-REPORT.md`

**Required sections:**
1. **Summary** — What was implemented.
2. **Task Status** — Per-task: Implemented / Partial / Blocked.
3. **Tests** — List of test methods added, and pass/fail status.
4. **Developer Insights**:
   - Issues encountered
   - Weak points spotted
   - Design decisions beyond spec
5. **Build Output** — Paste relevant `dotnet build` / `dotnet test` output (last 30 lines minimum).
