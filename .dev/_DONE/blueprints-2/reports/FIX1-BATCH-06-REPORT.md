# FIX1-BATCH-06 Report

## 1. Summary

Implemented Phase 10 ("Stepping & Breakpoints") across six tasks:

- **TASK-HS-S3-01** — Extended `HsmBreakpointGutterRenderer` to render transition
  breakpoint dots in addition to state breakpoint dots. The loop over `_session.GetBreakpoints()`
  now checks `_asset.FindTransitionByVisualId(bp.ElementId)` when the element is not a
  state. Added `internal (int StateDots, int TransitionDots) CountBreakpoints()` helper
  and `LastStateDotCount` / `LastTransitionDotCount` fields for test-time introspection
  without calling ImGui.

- **TASK-BT-S3-02** — Implemented a step-control state machine in `BTreeDebugSession`.
  Added `StepMode` enum (`None`, `Into`, `Over`, `Out`), fields `_stepMode`,
  `_stepFromStackDepth`, and `_nodeProcessedSinceStep`. `OnStepOverImpl()` captures the
  current stack pointer and calls `Coordinator.RequestStepOneTick()`. `OnPauseImpl()`
  calls `Coordinator.RequestPause()`. `OnContinueImpl()` clears the step mode and calls
  `Coordinator.RequestContinue()`. Added an injectable constructor
  `BTreeDebugSession(AiTracerCoordinator?)` for test isolation. The three coordinator
  request methods (`RequestStepOneTick`, `RequestPause`, `RequestContinue`) were added as
  `public virtual` no-ops to `AiTracerCoordinator`.

- **TASK-HS-S3-02** — Implemented the HSM step-control state machine in `HsmDebugSession`
  following the same pattern. Uses `_stepFromMicroStep` (byte) so that Step Over and Step
  Out both complete when `MicroStep != _stepFromMicroStep`. Added an injectable constructor
  `HsmDebugSession(AiTracerCoordinator?)` for test isolation.

- **TASK-BT-S3-03** — `SubtreeBoundaryRenderer.IsActive` was already correctly
  implemented (`_session?.IsAttached == true`). No production changes were needed; task
  verified and covered by new tests.

- **TASK-HS-S3-03** — Implemented `ICustomCanvasHitTester` on `HsmRegionConflictsRenderer`.
  `HitTest()` iterates `_glyphPositions`, converts each graph-space position via
  `ctx.Viewport.GraphToScreen(graphPos)`, and returns a `CustomElementHit` with
  `CustomElementKind.Standalone` when the canvas point is within 8 px. Changed
  `_glyphPositions` visibility to `internal` for test access. Added `internal int
  LastGlyphCount;` for test introspection.

- **TASK-HS-S3-04** — Added pseudostate transparency support:
  - `HsmAsset.StateNode.IsPseudostate` computed property (`IsHistory || IsDeepHistory || IsFinal`).
  - `HsmKinds.Pseudostate = "hsm.pseudostate"` constant.
  - `HsmEditorTheme` (new file) implements `IEditorTheme`, overriding
    `GetCategoryHeaderColor(NodeCategory.Custom)` to return `Vector4.Zero` (fully
    transparent) while delegating all other categories to `DefaultTheme`. Static helper
    `IsPseudostateKind(NodeKindKey)` tests whether a kind key maps to a pseudostate node
    kind (Final, History, or DeepHistory).

---

## 2. Task Status

| Task | Status |
|------|--------|
| TASK-HS-S3-01 | Implemented |
| TASK-BT-S3-02 | Implemented |
| TASK-HS-S3-02 | Implemented |
| TASK-BT-S3-03 | Verified (already implemented) |
| TASK-HS-S3-03 | Implemented |
| TASK-HS-S3-04 | Implemented |

---

## 3. Files Changed

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Debug/AiTracerCoordinator.cs` | Added `RequestStepOneTick()`, `RequestPause()`, `RequestContinue()` virtual methods |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeDebugSession.cs` | Added injectable constructor; `StepMode` enum; step state machine |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs` | Added injectable constructor; `_stepFromMicroStep`; step state machine |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs` | Added transition BP rendering; `CountBreakpoints()` internal helper; `LastStateDotCount` / `LastTransitionDotCount` fields |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmHistoryGlyphsRenderer.cs` | Added `CountGlyphs()` internal helper; `LastGlyphCount` field |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmRegionConflictsRenderer.cs` | Implemented `ICustomCanvasHitTester`; `_glyphPositions` changed to `internal`; `LastGlyphCount` field |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` | Added `StateNode.IsPseudostate` computed property |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmKinds.cs` | Added `Pseudostate` constant |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Theme/HsmEditorTheme.cs` | New file: `IEditorTheme` implementation with pseudostate transparency and `IsPseudostateKind` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeStepControlTests.cs` | New file: step control tests for `BTreeDebugSession` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/SubtreeBoundaryRendererTests.cs` | New file: `SubtreeBoundaryRenderer.IsActive` tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/FakeHsmSession.cs` | New file: reusable `IHsmDebugSession` test stub |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmStepControlTests.cs` | New file: step control tests for `HsmDebugSession` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmBreakpointRendererTests.cs` | New file: `HsmBreakpointGutterRenderer.CountBreakpoints()` tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmRegionConflictsRendererTests.cs` | New file: `HsmRegionConflictsRenderer` hit-test tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmPseudostateTests.cs` | New file: `IsPseudostate`, `HsmKinds.Pseudostate`, `HsmEditorTheme` tests |

---

## 4. Build & Test Results

```
Hrot.BTree.Editor:       Build succeeded. 0 Warning(s), 0 Error(s)
Hrot.Hsm.Editor:         Build succeeded. 0 Warning(s), 0 Error(s)
Hrot.BTree.Editor.Tests: Passed! Failed: 0, Passed: 163, Total: 163
Hrot.Hsm.Editor.Tests:   Passed! Failed: 0, Passed: 188, Total: 188
```

---

## 5. Design Decisions

- **`internal` test helpers over ImGui calls**: Renderers like `HsmBreakpointGutterRenderer`
  call `ImGui.GetColorU32()` inside `Render()`, which requires an initialized ImGui context
  and causes `AccessViolationException` in unit tests. Internal counting helpers
  (`CountBreakpoints()`, `CountGlyphs()`) expose the structural logic without touching ImGui,
  allowing full coverage without a display server.

- **`_stepFromMicroStep` for HSM step granularity**: HSM execution advances by micro-steps
  (sub-atomic transitions within a macro-step). Capturing the micro-step counter at
  step-initiation time and comparing on the next `Update()` call correctly handles both
  Step Over (same state, next micro-step) and Step Out (return to parent, which also
  changes the micro-step).

- **`Vector4.Zero` for pseudostate header transparency**: NodeEditor renders category
  header color with alpha; `Vector4.Zero` (RGBA all 0) produces a fully transparent header,
  which is the intended visual treatment for pseudostate nodes (History, DeepHistory, Final)
  that should not display the colored category header bar.

- **`_glyphPositions` promoted to `internal`**: The list is an implementation detail of
  render-pass layout computation. Making it `internal` (rather than exposing a read-only
  wrapper) is the minimal change that enables white-box hit-test verification without
  adding a public API surface.

- **Injected `AiTracerCoordinator?` constructor**: Both `BTreeDebugSession` and
  `HsmDebugSession` inherit from a base class that accepts an optional coordinator.
  Adding a thin public constructor on each subclass enables test injection of a
  `SpyCoordinator` without requiring a DI container or reflection.
