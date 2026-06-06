# Architect briefing — Blueprint debugging UX (blueprint-dbg-1), state correction + direction

**To the architect:** Before I ask new questions, here are code-verified findings from branch `blueprint-integ-1`.
A few of them correct the mental model from the last exchange — please recalibrate to these so we stay aligned.
Each is backed by `file:line` you can confirm in the sources. The goal of this thread: make **Slice-1 blueprint
debugging work end-to-end in the live editor UX** (place node breakpoints, pause the live tick, step Over/Into/Out,
watch values), then move toward Slice-2.

---

## Corrections to the previous picture (verified against code)

1. **The real blueprint canvas EXISTS and is the shared `AiGraphCanvasWindow`, not `GraphEditorWindow`.**
   - Production canvas: `Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`, registered for the
     Blueprint perspective at `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:2221-2234`
     (`new AiGraphCanvasWindow("Blueprint", …)` → `_blueprintRegistrar.RegisterExtraWindow(...)`).
   - Host integration is live: `Hrot.Blueprints.Editor/Host/{BlueprintDocumentFactory,BlueprintGraphModel,
     BlueprintNodeModel,BlueprintEditorHostServices,BlueprintCommandSink,NodePinSchema}.cs`, plus the
     `NodeDrawers/*` (e.g. `ChannelCommandNodeDrawer`, `FunctionCallNodeDrawer`, `WhenNodeDrawer`) — these are the
     inline pin editors / config drawers delivered in recent batches and smoke-tested by the user.
   - **`GraphEditorWindow.cs` is DEAD legacy.** It only draws `ImGui.TextDisabled($"Graph: {Name}")` behind a
     `// -- Canvas placeholder --` / `TODO(D-BP-04)` (`GraphEditorWindow.cs:106-112`). Its registrar
     `BlueprintWindowRegistrar` is already retired in production (`EditorSubsystem.cs:441-444` returns `null`,
     AIE-015). **We are going to DELETE `GraphEditorWindow`** (see deletion plan below). Please do not reason from
     it again — it is not the integration point and was the source of the earlier "no canvas / build the canvas"
     conclusion. (The `Hrot.Blueprints.Editor.GraphEditor` *namespace* — `CommandHistory`, `GraphCommands`,
     `SelectionState` — is NOT dead; it is used by the live host services and stays.)

2. **The editor already compiles blueprints in `Debug` mode with embedded PDB — NOT Release.**
   - `Hrot.Blueprints.Editor/Reload/QuickReloadService.cs:64`: `Mode: CompilerMode.Debug`, with
     `EmitPdbWithEmbeddedSource: true` (line 71).
   - Consequence: `DebugProbe.NodeEnter` probes **are emitted**, so **node breakpoints can hit today**. The earlier
     "defaults to Release, all probes elided, that's the root blocker" is not correct.
   - Caveat (still true): per Debug DD §1.4, **Debug mode emits `NodeEnter` only, not `PinValueChanged`**. So *pin
     watches* are inert until an asset is compiled in **Trace** mode. That is a real gap for watches — but it does
     NOT block breakpoints or stepping.

3. **The backend pause/step pipeline IS fully wired (this part was right).**
   - `EditorSubsystem.cs:870` `new MasterSyncTimeControllerAdapter(_timeController!)`, `:887`
     `DebugProbe.Sink = bpBlueprintSession`.
   - `BlueprintDebugSession.OnNodeEnter` resolves the breakpoint and calls `_timeController.RequestPause()`; step
     methods call `RequestStepOneTick()`. The adapter does a soft-pause on `MasterSyncController`. So setting a
     breakpoint **directly on the session** already halts the live tick, and `StepOver()` already advances one tick.
   - **The only reason the user can't debug is the missing canvas UX wiring**, not the backend.

4. **The correct implementation approach is to MIRROR the existing HSM/BTree debug UX, which already runs on this
   same shared canvas — NOT to mutate `NodeState` in the graph model.**
   - Sibling pattern (verified): `Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs:71-106`
     (`SetBreakpointManager()` installs a `BTreeBreakpointContextMenuProvider : ICustomElementContextMenuProvider`);
     `Renderers/BTreeBreakpointGutterRenderer.cs` (an `ICustomCanvasRenderer`, AfterNodes pass, reads
     `_session.GetBreakpoints()` → red gutter bullet); `Renderers/BTreeRuntimeOverlayRenderer.cs` (reads
     `_session.GetCurrentStateSnapshot()` → `RunningElementId` gold pulse, stack outlines, history glyphs). HSM has
     the equivalents.
   - Crucially, the **graph model stays debug-unaware**: both `BTreeNodeModel` and `BlueprintNodeModel` hard-code
     `State => NodeState.Normal`; debug visuals are drawn by **renderers reading the session each frame**. So the
     earlier suggestion to inject `IBlueprintDebugSession` into `BlueprintGraphModel` and project
     `NodeState.Breakpoint/Executing` flags is the wrong layer — we will follow the renderer pattern instead.
   - The blueprint side is simply **missing the blueprint counterparts**: `BlueprintDocumentFactory.Build` does NOT
     inject the debug session / breakpoint manager (unlike `BTreeDocumentFactory`), so there is no breakpoint
     context-menu provider, no gutter renderer, and no runtime-overlay renderer on the blueprint canvas. The static
     `BlueprintBreakpointMenuPopulator` is orphaned (only the dead `GraphEditorWindow` referenced it); we will
     likely replace it with a `BlueprintBreakpointContextMenuProvider` mirroring BTree.

5. **`DebugPanelWindow` is partial:** it shows a "PAUSED" banner + breakpoint table but has **no Step/Continue
   buttons** (`Debug/DebugPanelWindow.cs:21-62`). Siblings host step controls in a `RuntimeInspectorPane`-style
   surface — we'll copy that.

---

## Our chosen direction (so you can tune advice to it)

Land Slice-1 by mirroring the proven HSM/BTree pattern into the blueprint host, in user-verifiable batches:

- **Batch A** — inject debug session + breakpoint manager into `BlueprintDocumentFactory`; add a blueprint
  breakpoint **context-menu provider** (right-click node → `BlueprintDebugSession.SetBreakpoint`, the path that
  actually pauses) + a **breakpoint gutter renderer** (red bullet).
- **Batch B** — blueprint **runtime-overlay renderer** (executing-node gold pulse + recent-history glyphs) from
  `GetCurrentStateSnapshot()`.
- **Batch C** — **Step Over/Into/Out/Continue** UI (copy the sibling pane) + a canvas pause overlay, wired to the
  already-functional session methods.
- **Batch D** — per-asset **Trace-mode toggle** (so `PinValueChanged` is emitted) + right-click-pin "Add Watch" →
  `WatchPanelWindow` live values.
- **Cleanup** — delete the dead `GraphEditorWindow` (+ its tests / registrar entry) so it stops being a false
  integration point.

Then Slice-2 (true data breakpoints) is cheap on top, since `OnPinValueChanged` already runs in Trace mode.

---

## Two questions where your design intent would save us guessing

1. **Breakpoint store — single source of truth?** BTree's gutter renderer reads BOTH
   `IBTreeDebugSession.GetBreakpoints()` AND a universal `IDataBreakpointManager`. For *blueprint node breakpoints*,
   the path that actually pauses is `BlueprintDebugSession.SetBreakpoint` (DebugProbe → `RequestPause`). When the
   blueprint canvas context menu sets a node breakpoint, should it write **only** to `BlueprintDebugSession`, or
   **also** register it in the universal `IDataBreakpointManager` like BTree does? Is there an intended single
   source of truth across the two stores, or are they meant to coexist (session = node-entry breakpoints that
   pause; manager = conditional/data breakpoints)?

2. **Trace-mode toggle UX.** Watches need Trace mode (Debug emits no `PinValueChanged`), but Debug is the right
   default for breakpoints/stepping. What is the intended per-asset Debug↔Trace switch in the editor — a toolbar
   toggle that recompiles via Quick Reload? Does any sibling editor (HSM/BTree) already expose such a mode switch
   we should mirror, or is per-asset Trace a new control we need to design?
