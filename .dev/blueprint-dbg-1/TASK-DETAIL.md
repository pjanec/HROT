# TASK-DETAIL — Blueprint debugging UX (blueprint-dbg-1)

Per-batch executable instructions for `sonnet` sub-agents. Each batch: read the named **template** files first
(the sibling BTree/HSM implementation is authoritative — mirror it, don't invent), then make the edits, then run
the gates. The **lead** reviews and commits.

Conventions: paths are repo-relative. "Template" = read-as-reference. Baseline = `Hrot.Blueprints.Tests` has
**7 pre-existing failures**; new failures must be **0**.

---

## Batch 0 — Cleanup: delete the dead `GraphEditorWindow`

**Why:** `GraphEditorWindow` is a placeholder (`ImGui.TextDisabled` + `TODO(D-BP-04)`), never registered in
production (its registrar `BlueprintWindowRegistrar` returns `null` at `EditorSubsystem.cs:441-444`, AIE-015). It
caused a false "no canvas exists" read. Removing it eliminates the trap.

**Delete:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` (whole file).
- The 4 `GraphEditorWindow_*` tests in
  `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorWindowTests.cs` (constructor/title/selection/
  null-arg). If the file has only those, delete the file; else delete just those methods.

**Edit:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintWindowRegistrar.cs:60` — remove the
  `() => new GraphEditorWindow(...)` registration. Adjust any count/array.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/BlueprintWindowRegistrarTests.cs` — the
  `RegistersAllSevenWindows` test: drop GraphEditorWindow from the expected set (→ six) and rename if it asserts a
  count.
- `.dev/breakpoints-1/DEBT-TRACKER.md` — mark **D-BP-04 SUPERSEDED**: the real canvas (`AiGraphCanvasWindow`) uses
  the context-menu-provider pattern (Batch A), not `GraphEditorWindow`; the old `TODO(D-BP-04)` is removed with the
  file.

**Keep (do NOT touch):** the `Hrot.Blueprints.Editor.GraphEditor` namespace (`CommandHistory`, `GraphCommands`,
`IGraphCommand`, `SelectionState`) — used by live host services; `BlueprintEditorWindowBase` — base of all live
windows.

**Verify nothing else references it:** `grep -r GraphEditorWindow` returns only the deletions above.
**Note (flag, do not act):** `BlueprintWindowRegistrar` itself is retired in production but still DI-registered
(`BlueprintEditorServiceCollectionExtensions.cs:19-21`) + unit-tested — a larger separate orphan; out of scope here.

**Gate:** build 0/0; Blueprints tests 7/0-new; AiShared tests; boot 10/10.

---

## Batch A — Breakpoint set + render (KEYSTONE)

**Goal:** right-click a node on the live blueprint canvas → toggle a breakpoint that **actually pauses the live
tick**, and show a **red gutter bullet** on breakpointed nodes.

**Templates (read first):**
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs` — the debug params + `BuildRenderers` +
  `SetBreakpointManager` shape to mirror (see lines 79-127, 152-196).
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs:71-106` — `SetBreakpointManager()` +
  context-menu provider install.
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs` — the gutter renderer.
- the BTree breakpoint **context-menu provider** (`BTreeBreakpointContextMenuProvider`, an
  `ICustomElementContextMenuProvider`) — find via grep; mirror it.

**Q1 RESOLVED (verified in code) — the breakpoint store is already fully wired; do NOT rebuild it.**
`BlueprintDebugSession.SetBreakpoint` (`:251`) already: records `AssetStructureHashAtSetTime`, allocates its own
`BreakpointId`, and (when a manager is set) forwards to `_dataBreakpointManager.AddBreakpoint(new
ExternalHitTagPredicateDto { Tag = nodeIdStr })` (`:267-272`), tracking `_mgrBpIds`; `ClearBreakpoint`/`ClearAll`
remove from both; `OnNodeEnter` calls `_dataBreakpointManager.OnExternalHit`. The manager is wired in production at
`EditorSubsystem.cs:886` (`bpBlueprintSession.SetDataBreakpointManager(_bpManager)`). **So the context menu only
calls `session.SetBreakpoint`/`ClearBreakpoint`/`IsBreakpointSet` — dual-registration is automatic.** Reference
test: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintContextMenuTests.cs`.

**Current blueprint code to extend:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs:86-197` — `Build(...)`. Add
  optional param mirroring BTree: `IBlueprintDebugSession? debugSession = null` (and the NodeEdit `IDebugSession?`
  if the overlay needs it — check `BlueprintEditorHostServices` ctor vs BTree's which takes `debug:`). Thread it
  into a new `BuildRenderers(...)` overload that `SetSession`s the gutter (and, in Batch B, the overlay) renderer.
  Note: the *manager* does not need threading here for the store (already wired at the session level); pass it only
  if the gutter renderer wants to also draw manager-only breakpoints like BTree's does.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintEditorHostServices.cs` — add the context-menu-
  provider plumbing mirroring `BTreeEditorHostServices` (and a `debug:` ctor param if BTree has one and blueprint
  lacks it). The blueprint provider needs the **debug session** (to call `SetBreakpoint`), not the manager.
- **Caller:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — find the `BlueprintDocumentFactory.Build(...)`
  call (~`:2295`) and pass the existing `bpBlueprintSession` (created ~`:887`, already manager-wired at `:886`).
  This injection is what currently makes the blueprint canvas debug-unaware.

**Create:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/BlueprintBreakpointGutterRenderer.cs` — mirror
  `BTreeBreakpointGutterRenderer`: `ICustomCanvasRenderer`, AfterNodes pass; `SetSession(IBlueprintDebugSession)`;
  draws a red bullet for nodes in `session.GetBreakpoints()`. `IsActive` false when session null (no per-frame
  cost).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintBreakpointContextMenuProvider.cs` — mirror
  `BTreeBreakpointContextMenuProvider`: `ICustomElementContextMenuProvider`; on a node, add "Toggle Breakpoint"
  that calls **`session.SetBreakpoint(assetId, graphId, nodeId)` / `ClearBreakpoint`** (the DebugProbe path that
  pauses — verified `OnNodeEnter → RequestPause`; the manager forward is automatic per Q1).
- **Retire** the orphaned static `BlueprintBreakpointMenuPopulator` (only the deleted `GraphEditorWindow` used it).
  Confirm no other refs; delete it, or fold its predicate logic into the new provider if it carries anything the
  session path lacks.

**Tests:**
- `BlueprintBreakpointGutterRenderer` reports `IsActive==false` with null session; draws for a registered bp
  (headless: assert the renderer queries the session / produces a draw command for the bp node).
- `BlueprintBreakpointContextMenuProvider` toggling calls `session.SetBreakpoint`/`ClearBreakpoint` with the right
  ids (use `CapturingDebugSession` or a mock).
- Factory test: `Build(... debugSession, breakpointManager)` returns a context whose renderer list includes the
  gutter renderer and whose host services have the manager set (mirror `BlueprintDocumentFactoryTests`).

**User smoke (PENDING after commit):** open a blueprint, attach to a ticking entity, right-click a node → Toggle
Breakpoint → the sim halts when that node executes; the node shows a red bullet; clear it → resumes.

---

## Batch B — Runtime overlay (executing-node highlight)

**Goal:** while ticking, the currently-executing node gets a gold pulse; recently-executed nodes get
status glyphs — the "what's running now" feedback.

**Template:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeRuntimeOverlayRenderer.cs` (reads
`_session.GetCurrentStateSnapshot()` → `RunningElementId` gold pulse, `StackElementIds` dimmed outlines;
`GetRecentNodeHistory()` → OK/X/~ glyphs; `GetRecentAsyncHistory()` → async badges). Also see how it's added in
`BTreeDocumentFactory.BuildRenderers` (AfterNodes, last).

**VERIFY FIRST (possible gap):** confirm `IBlueprintDebugSession`/`BlueprintDebugSession` exposes the
equivalent of `GetCurrentStateSnapshot()` with a **currently-executing node id** and node history. The Debug DD has
`CallFrame`/call-frame stack and `GetCurrentStateSnapshot` (§8.4) but check whether it surfaces an *executing node
id* and recent history like the BTree session does. **If missing, adding that read-side surface to
`BlueprintDebugSession` is part of this batch** (it's tracked internally during `OnNodeEnter` — expose it). Report
the exact gap before building the renderer.

**Create:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Renderers/BlueprintRuntimeOverlayRenderer.cs`
mirroring the BTree one; wire it in the factory's `BuildRenderers` (AfterNodes, after the gutter renderer) with
`SetSession(debugSession)`.

**Tests:** overlay `IsActive==false` with null session; given a session reporting an executing node, the renderer
targets that node (headless assertion on the draw target / queried id).

**User smoke (PENDING):** run a blueprint → the live node pulses gold; on breakpoint pause the paused node is
clearly marked; recent nodes show OK/fail glyphs.

---

## Batch C — Step controls UI

**Goal:** when paused, the user can **Continue / Step Over / Step Into / Step Out** from the UI (the session +
time-controller backend already implements these).

**Templates:** find the sibling step-control surface (Explore noted siblings host step controls in a
`RuntimeInspectorPane`-style class, not the Debug panel) — grep `StepOver`/`Continue`/`RequestStepOneTick` across
`Hrot.BTree.Editor` / `Hrot.Hsm.Editor` and mirror the button row. Also the demo's canvas pause overlay
(`NodeEditor.Demo` has a primitive Continue button) for the floating-overlay placement.

**Edit / create:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs:21-62` — add the Continue / Step
  Over / Step Into / Step Out buttons (enabled only when `IsPaused`), wired to the `IBlueprintDebugSession`
  step/continue methods. Keep the existing PAUSED banner + breakpoint table.
- Optional: a floating canvas pause overlay (top-right of `AiGraphCanvasWindow`) with the same controls — mirror
  the sibling/demo overlay. Scope this as a sub-step if the panel buttons land first.

**Note:** Slice-1 step-into across *peer blueprint calls* is out of scope (Debug DD §1.3); Step Into within a
graph is in scope. Don't promise cross-peer step.

**Tests:** headless — pressing each button invokes the matching session method (mock/capturing session); buttons
disabled when not paused.

**User smoke (PENDING):** hit a breakpoint → press Step Over → sim advances exactly one tick and re-pauses; Continue
resumes.

---

## Batch D — Watches (Trace mode + add-watch + live values)

**Goal:** the user can watch a pin's live value. Requires the asset compiled in **Trace** mode (Debug emits no
`PinValueChanged`).

**Q2 RESOLVED — UX = per-asset Debug/Trace dropdown (default Debug) → write `EditorMetadata.CompilerMode` → user
runs Quick Reload to re-emit. Two corrections from code: the toggle goes in the REAL production toolbar (NOT the
dead `GraphEditorWindow`), and `EditorMetadata.CompilerMode` does NOT exist yet — it must be added.** Sibling
HSM/BTree use a runtime trace-buffer flag, not a compile mode — not reusable here.

**Sub-parts:**
1. **Add `CompilerMode` to the asset-level editor metadata.** Locate the asset-level metadata class (the one
   exposing `asset.EditorMetadata.Recipe`; note `NodeMetadata` is the *node*-level X/Y/Comment — different). Add a
   `CompilerMode` (enum `Hrot.Blueprints.Core.Compiler.CompilerMode`, default `Debug`) property, serialized
   **`JsonIgnore`-when-default** so existing `.bp.json` assets stay byte-stable (projection-only invariant — see
   how `Node.PinDefaults` did it).
2. **Per-asset Debug/Trace dropdown** in the **real production toolbar** (the one hosting the existing Quick
   Reload / Full Rebuild buttons — locate it; it is NOT `GraphEditorWindow`. Candidates: the blueprint perspective
   toolbar wired in `EditorSubsystem`/`BlueprintEditorModule`, or `AiGraphCanvasWindow`'s toolbar). Writing the
   dropdown sets `asset.EditorMetadata.CompilerMode` + marks dirty; it does not recompile by itself.
3. **Make Quick Reload honor the mode.** `QuickReloadService.cs:64` currently hardcodes `CompilerMode.Debug` —
   read `asset.EditorMetadata.CompilerMode` instead (same for the Full Rebuild path). Trace then emits
   `PinValueChanged<T>` probes.
4. **Add-watch gesture.** Right-click an **output data pin** → "Add Watch" → `session.AddWatch(assetId, graphId,
   pinId)` (Debug DD §8.2). Extend the Batch-A context-menu provider to handle pins (not just nodes).
5. **Watch panel live values.** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/WatchPanelWindow.cs` is
   already wired to `OnPinValueChangedEvent` + `GetWatches()` — verify it renders updates once Trace probes flow.
   Extend `MarshalFromBytes` only if a watched type isn't covered (Debug DD §8.5 — primitives/small structs only).

**Tests:** add-watch via provider calls `session.AddWatch` with right ids; watch panel renders a value after a
simulated `PinValueChanged`; Trace toggle causes `QuickReloadService` to pass `CompilerMode.Trace`.

**User smoke (PENDING):** switch asset to Trace, right-click a pin → Add Watch → the Watch panel shows the value
updating each tick.

---

## After D — Slice-2 on-ramp (not scheduled yet)
True **break-on-pin-write** data breakpoints: add a compare-and-`RequestPause` in
`BlueprintDebugSession.OnPinValueChanged` (already invoked in Trace mode). Cheap once D's Trace path + pin context
menu exist. Also: conditional breakpoints, value editing at pause, cross-peer step-into (all Slice-2 per Debug DD
§1.3).
