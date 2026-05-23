# FIX1-BATCH-06 — Phase 10: Stepping & Breakpoints

## Tasks Covered
- **TASK-HS-S3-01** — Map transition breakpoints in `HsmBreakpointGutterRenderer` to render red affordance dots directly on transition labels.
- **TASK-BT-S3-02, TASK-HS-S3-02** — Implement step control state machines (`OnStepOverImpl`, `OnStepIntoImpl`, `OnStepOutImpl`, etc.) in both debug sessions to command `RequestStepOneTick` and `RequestPause`.
- **TASK-BT-S3-03** — Implement `SubtreeBoundaryRenderer` to compute AABB of currently executing nodes within the active BTree stack and draw a dashed boundary box.
- **TASK-HS-S3-03** — Implement `ICustomCanvasHitTester` on `HsmRegionConflictsRenderer` to make conflict overlays clickable and wire the warning glyph to a suppression popup.
- **TASK-HS-S3-04** — Map HSM history and final states to transparent theme category so only circular custom glyphs render.

## Onboarding

You are working on the `IOS-IG-SimHost-FDP-2` project. This final batch completes the
debugging suite for both BTree and HSM editors (Phase 10). Step buttons are currently
no-ops and some advanced visual renderers lack interactive hit-testing.

**Mandatory read before coding:**
- `.dev/blueprints-2/FIX1-TASK-DETAIL.md` — "ACTION PACKET 6: Phase 10 — Stepping & Breakpoints".
- `.dev/blueprints-2/FIX1-TASK-EXTRA-DETAILS.md` — "ACTION PACKET 6: Phase 10 — Stepping & Breakpoints (Detailed Fixes)".
- `.dev/blueprints-2/BTree_Editor_NodeEditor_Host_Design.md` — BTree stepping spec.
- `.dev/blueprints-2/HSM_Editor_NodeEditor_Host_Design.md` — HSM stepping spec (§8.1+).
- `.dev/blueprints-2/ACCEPTANCE-CRITERIA.md` — F10-01 through F10-12.
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/` — BTree debug session.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/` — HSM debug session.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/` — HSM renderers.

## Developer Insights Section

After implementing, answer the following in your report:
1. What issues were encountered?
2. What weak points did you spot in the codebase?
3. What design decisions were made beyond the spec?

---

## Tasks

### TASK-HS-S3-01: Transition Breakpoint Rendering

**Target files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-HS-S3-01: Transition Breakpoint Rendering".

**Summary:**
1. In `Render()`, after handling state node breakpoints, iterate `_session.GetBreakpoints()`.
2. For each breakpoint that does NOT match a state, call `_asset.FindTransitionByVisualId(bp.ElementId)`.
3. If a transition is found, use `LinkBezier.GetPointAt(0.5)` (or your bezier midpoint helper) to locate the transition's midpoint in canvas space.
4. Draw a small red filled circle (affordance dot) next to the transition label at that midpoint.

**Acceptance criteria:** F10-04.

**Tests required:**
- Test that a breakpoint matching a transition ID causes a red dot to be drawn at the transition's midpoint.
- Test that no dot is drawn for a breakpoint that matches only a state (not a transition).

---

### TASK-BT-S3-02 & TASK-HS-S3-02: Step Control State Machines

**Target files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-BT-S3-02 & TASK-HS-S3-02: Implement Step Control State Machines".

**Summary:**
1. Add tracking fields to both sessions:
   - `private StepMode _stepMode;` (enum: None, Over, Into, Out)
   - `private int _stepFromStackDepth;` (BTree) or `private HsmMicroStep _stepFromMicroStep;` (HSM)
2. Implement `OnStepOverImpl`, `OnStepIntoImpl`, `OnStepOutImpl`:
   - Set `_stepMode`, record current depth/phase.
   - Call `Coordinator.TimeController.RequestStepOneTick()` to advance the engine by one tick.
3. Implement `OnContinueImpl`: clear `_stepMode`; call `RequestContinue()`.
4. Implement `OnPauseImpl`: call `RequestPause()`.
5. In the trace buffer polling loop (`Update()`): after processing new records, evaluate the step condition:
   - **StepOver (BTree)**: if `StackPointer` returned to `_stepFromStackDepth`, call `RequestPause()` and reset `_stepMode`.
   - **StepInto (BTree)**: after one node execution record, call `RequestPause()`.
   - **StepOut (BTree)**: if `StackPointer < _stepFromStackDepth`, call `RequestPause()`.
   - **HSM**: use `_stepFromMicroStep` comparison.

**Acceptance criteria:** F10-01, F10-02, F10-03.

**Tests required:**
- BTree: `OnStepOverImpl` calls `RequestStepOneTick`. After trace update where stack depth returns, `RequestPause` is called.
- BTree: `OnStepIntoImpl` calls `RequestStepOneTick`. After first NodeEvaluated, `RequestPause` is called.
- HSM: `OnStepOverImpl` calls `RequestStepOneTick`. After microstep advances past start, `RequestPause` is called.
- Test `OnPauseImpl` calls `RequestPause`. Test `OnContinueImpl` calls `RequestContinue`.

---

### TASK-BT-S3-03: Subtree Boundary AABB Computation

**Target files:**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/SubtreeBoundaryRenderer.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-BT-S3-03: Subtree Boundary AABB Computation".

**Summary:**
1. Read live `BehaviorTreeStateSnapshot` from the session.
2. If the simulation is paused inside a subtree (`StackPointer > 0`), extract the subtree's root entry node using `NodeIndexStack[0..StackPointer]`.
3. Walk all child nodes of that subtree entry node via the `IGraphModel` to compute a combined AABB of their `NodeInteriorBounds`.
4. Render a faint blue dashed rectangle encompassing this combined AABB in the `BeforeContent` pass.

**Acceptance criteria:** F10-07.

**Tests required:**
- Test with a 3-node graph where the snapshot shows `StackPointer = 1`; assert AABB encompasses the subtree child nodes.
- Test with `StackPointer = 0`; assert no AABB is drawn.

---

### TASK-HS-S3-03: Region Conflicts Hit-Testing & Popup

**Target files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs`

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-HS-S3-03: Region Conflicts Hit-Testing & Popup".

**Summary:**
1. Implement the `ICustomCanvasHitTester` interface on the renderer.
2. In `TryHitTest(pos, out hit)`: return a valid `CustomElementHit` if the mouse intersects the ⚠ glyph's bounds.
3. Wire the selection of this element (in the editor's UI update loop or via `ImGui.BeginPopup`) to render the conflict details:
   - Show which `CommandLane`s are conflicting.
   - List contributing actions.
   - Provide a "Suppress this warning" button.

**Acceptance criteria:** F10-10.

**Tests required:**
- Test that `TryHitTest` returns `true` when cursor is over the ⚠ glyph's bounds.
- Test that it returns `false` when cursor is elsewhere.

---

### TASK-HS-S3-04: History & Final States Rendering Bypass

**Target files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmHistoryGlyphsRenderer.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Theme/HsmEditorTheme.cs` (or equivalent theme file)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/StateNode.cs` (or equivalent node model)

**Detailed instructions:** See `FIX1-TASK-EXTRA-DETAILS.md` → "TASK-HS-S3-04: History & Final States Rendering Bypass".

**Summary:**
1. Assign a specific `Category` string (e.g., `"hsm.pseudostate"`) to `StateNode` instances for history and final states.
2. In `HsmEditorTheme.cs`, map `"hsm.pseudostate"` category to completely transparent background and border: `BackgroundColor = Vector4.Zero`, `BorderColor = Vector4.Zero`.
3. In `HsmHistoryGlyphsRenderer.Render()`, draw a 20px circle with "H", "H*", or "⊙" at the node's center coordinate. Since the underlying NodeEditor theme is transparent, only the glyph renders.

**Acceptance criteria:** F10-12.

**Tests required:**
- Test that a history state `StateNode` has category `"hsm.pseudostate"`.
- Test that the theme maps `"hsm.pseudostate"` to `Vector4.Zero` background.
- Test that `HsmHistoryGlyphsRenderer.Render()` draws a glyph at the node's center (verify via test-observable draw count).

---

## Mandatory Workflow: Test-Driven Task Progression

1. Read spec and acceptance criteria.
2. Write/update tests alongside implementation.
3. Run tests and confirm they pass.
4. Do not mark a task complete unless tests pass.

Do not swallow exceptions silently.

---

## Build & Test Commands

```powershell
cd "d:\Work\IOS-IG-SimHost-FDP-2"
dotnet test "Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/"
dotnet test "Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/"
```

---

## Report Format

Write your report to: `.dev/blueprints-2/reports/FIX1-BATCH-06-REPORT.md`

**Required sections:**
1. **Summary** — What was implemented.
2. **Task Status** — Per-task: Implemented / Partial / Blocked.
3. **Tests** — List of test methods added, and pass/fail status.
4. **Developer Insights**:
   - Issues encountered
   - Weak points spotted
   - Design decisions beyond spec
5. **Build Output** — Paste relevant output (last 30 lines minimum).
