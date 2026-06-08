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

---
---

# CORRECTIVE BATCHES (CF) — node-identity bug: breakpoints never pause

> **Added 2026-06-08 after live diagnosis.** Batches A–E shipped the UX, but breakpoints **still do not pause**.
> The earlier "probe coverage" theory in STATUS.md was built on a **wrong node-ID table** and is superseded.
> See the corrected diagnosis below. These CF batches fix the actual compiler bug.

## The confirmed bug (read this before touching anything)

**Symptom:** user sets a breakpoint on a node (red marker appears), runs the sim, it never pauses.

**Root cause — the node ID the editor sets a breakpoint with ≠ the node ID the runtime fires `OnNodeEnter` with.**
Verified against ground truth (`Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count4.bp.json` + the live
`bp-diag.log`). For the `Count4` asset (`AssetId 47fe9c55-c6ca-4c69-9c5a-d46de25745de`,
`GraphId 10000006-0000-0000-0000-000000000001`):

| Authored node (from `.bp.json`) | Authored Id | In compiled DebugMap? | Runtime probe id |
|---|---|---|---|
| EventEntry | `20000006-…0001` | (verify in CF-1) | — |
| SetVariable | `20000006-…0002` | yes | (verify) |
| FunctionCall ("Add") | `20000006-…0003` | yes | not firing as `…0003` |
| GetVariable ("Get Count") | `20000006-…0004` | yes | **fires `…0004`** ← data node, wrong attribution |
| Sequence | `da9a9c0b-25f8-4a81-9a52-75c715456f18` | **NO** — replaced by `0ec3b253-3c5a-1024-…` |
| Delay (latent) | `0b561966-b00b-4c84-a1a0-87042220ba9f` | **NO** — replaced by `976ef338-34f2-1469-…` |
| Return | `7b6da53f-4e11-4bc9-9d0c-bad0e22c7f5c` | (verify) |

Two independent identity breaks:
1. **Provenance loss through lowering.** `Stage3_Normalize.SynthesizedGuid` (SHA-256 deterministic) and the
   Stage-6 wait/instance lowering create replacement/synthesized statements that **drop the authored `NodeId`**
   (`IrDebugAnnotation.Synthesized = "stage6-wait-lower-inst"`, `NodeId = null` — see
   `Compiler/Lowering/WaitLowering_Instance.cs`). The authored Delay/Sequence ids never reach the DebugMap.
2. **Probe mis-attribution.** `Compiler/Lowering/DebugProbeInsertion.cs:24` keys each block's `NodeEnter` probe to
   `block.Statements[0].Debug.NodeId`. The first statement of an exec node's block is frequently an inlined
   **data-input read** (e.g. GetVariable `…0004`) — so the probe fires as the data node, not the exec node.

**What is NOT the bug (do not "fix" these — they work, proven by the synthetic tests in `BreakpointTests`):**
`BlueprintDebugSession.OnNodeEnter → HandleBreakpointHit → DataBreakpointManager.OnExternalHit → RequestPause`,
the `StringComparer.Ordinal` dictionary, the F9/context-menu wiring, the adapter. Compiling first registers the
DebugMap correctly (in-memory, no file) — but cannot fix an identity-space mismatch.

**Verified reference points (do not assume — re-read these files):**
- `Compiler/Lowering/DebugProbeInsertion.cs:19-62` — per-block probe insertion; line 24 early-returns when first
  statement has no NodeId.
- `Compiler/Stages/Stage5_Schedule.cs:305-356` (`ScheduleLatentNode`, uses `DebugOf(node)`) and `:453-506`
  (`ScheduleSequenceNode`, terminator carries `DebugOf(seq)` but the block emits no NodeId-bearing statement).
- `Compiler/Lowering/WaitLowering_Instance.cs` — `Synth()` annotation drops NodeId on emitted statements.
- `Compiler/Emit/DebugMapBuilder.cs:87-106` (`RecordNodeStart/End`) + `Compiler/Emit/CSharpEmitter.cs:43-54`
  (`EmitNodeStart` is gated on `debug?.NodeId != null`) — DebugMap entries are driven by statements with a NodeId.
- `Compiler/Ir/IrDebugAnnotation.cs` — fields `NodeId`, `PinId`, `GraphId`, `Synthesized`, `NodeKind`, `DisplayName`.
- `Compiler/Assets/Nodes.cs:35-48` — `Node` base has `Id`, `Pins`, `EditorMetadata`, `PinDefaults`; **no provenance
  field exists yet**.
- Editor compile path: `Reload/QuickReloadService.cs:73` (`_compiler.Compile`) → `:161`
  (`_session?.RegisterDebugMap(result.DebugMap)`), in-memory.

## Zoo operating rules for ALL CF batches (Zoo = the external coder)

- **Do NOT** delete, skip, weaken, or change the assertions of any existing test to make the suite pass. If a test
  legitimately must change because behavior changed, change ONLY the expected value, and **list every such test by
  name with old→new expectation** in the report.
- **Do NOT** set `BLUEPRINT_REGENERATE_SNAPSHOTS` or regenerate golden snapshots. If a golden changes, report the
  exact diff and STOP for lead review.
- The report must include the **full failing-test set by name** before and after, and the exact `dotnet test`
  command lines run. The lead (Petr) hard-reviews the **diff**, not the report, and commits.
- Editor must be CLOSED during build (DLL locks). Gate: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors.

---

## Batch CF-1 — Ground-truth diagnostic (NO production code changes)

**Why:** remove all remaining ambiguity about what `976ef338` / `0ec3b253` are and exactly where the authored
Delay/Sequence ids are lost, before changing compiler code.

**Do:** Write a single xUnit test in a new file
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF1_NodeIdentityDiagnosticsTests.cs` that:
1. Loads `Count4.bp.json` (find how other compiler tests load a `.bp.json` asset — e.g. via the test fixture used
   in `Compiler/Stage7_EmitTests`), compiles it with `CompilerMode.Debug`.
2. From the compile result, emits to the **test output** (`ITestOutputHelper`) and to a file
   `.dev/blueprint-dbg-1/reports/CF1-NODE-IDENTITY-REPORT.md`:
   - Every `DebugMap.Entries` row: `NodeId`, `NodeKind`, `DisplayName`, `StartLine`.
   - Every authored node from the asset graph: `Id`, `Kind` — and a column "DebugMap entry keyed by this exact
     authored Id? yes/no".
   - Every `DebugProbe.NodeEnter(self, "<id>")` literal found in the generated C# source (regex the emitted source).
   - For each authored exec node with **no** matching DebugMap entry / no matching probe literal, the synthesized
     id (if any) that appears instead, and the `Synthesized` tag of the statements in its block.
3. The test asserts nothing about correctness yet — it is a **reporting** test. It must **pass** (so it stays green
   in CI as a living map), but its body writes the report file.

**SUCCESS CONDITION (CF-1):** `CF1-NODE-IDENTITY-REPORT.md` exists and definitively answers, for `Count4`:
(a) which authored node ids have a DebugMap entry keyed by that exact id; (b) for Delay `0b561966` and Sequence
`da9a9c0b`, the synthesized id that replaced them and the lowering stage/tag responsible; (c) the complete list of
`DebugProbe.NodeEnter` ids actually emitted. Build 0 errors; new test passes; **no other test's result changes**.

---

## Batch CF-2 — Preserve authored node identity end-to-end (the fix)

**Depends on:** CF-1 (its report tells you the exact lowering sites to touch).

**Goal:** every authored **exec-flow** node emits its `NodeEnter` probe and DebugMap entry keyed to **its own
authored `Node.Id`** — including Delay (latent) and Sequence — and pure data-only nodes do **not** get probes.

**Design (follow this; do not invent a parallel id space):**
1. **Carry provenance, never synthesize identity for breakpointable nodes.** Add a nullable `Guid? OriginNodeId`
   to `IrDebugAnnotation` (`Compiler/Ir/IrDebugAnnotation.cs`). Anywhere a lowering/normalization step emits a
   statement that stands in for an authored node (the `Synth()` path in `WaitLowering_Instance.cs`; any
   `SynthesizedGuid`-replacement of an authored Delay/Sequence found in CF-1), set `OriginNodeId = <authored
   node id>` instead of leaving NodeId null. The authored id must be threaded in from the source `Node.Id` at the
   call site (Stage 5 knows it — `DebugOf(node)`).
2. **Attribute each block's probe to the exec node it represents, not `Statements[0]`.** Change
   `DebugProbeInsertion.InsertProbes` so the probe id is chosen as: the block's owning exec-node id. Concretely:
   prefer the first statement whose `Debug.NodeId` (or new `OriginNodeId`) corresponds to the **exec node** that
   opened the block, rather than `Statements[0]` unconditionally. If Stage 5 is the only place that knows the
   owning exec node, record it on the block (add an `OwningNodeId`/`Guid? SourceNodeId` to `IrBlock`, set in
   `ScheduleBlock`/`ScheduleLatentNode`/`ScheduleSequenceNode`) and have `DebugProbeInsertion` read it. This is the
   cleaner fix and is preferred.
3. **DebugMap entries follow the same key.** Ensure `CSharpEmitter.EmitNodeStart` / `DebugMapBuilder.RecordNodeStart`
   record the authored/owning node id (use `NodeId ?? OriginNodeId`), so the map contains an entry for every
   breakpointable exec node keyed by its authored id.
4. **Data-only (pure) nodes get NO probe** (matches standard visual-scripting semantics). If CF-1 shows the "Add"
   FunctionCall (`…0003`) is a **pure** node, it is intentionally not breakable — that is correct; do not force a
   probe onto it. (Editor gating for this is CF-3.)

**SUCCESS CONDITION (CF-2)** — add these as assertions in a new test
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF2_AuthoredIdProbeTests.cs` (compile `Count4` in Debug):
1. `DebugMap.Entries` contains an entry whose `NodeId == Guid 0b561966-b00b-4c84-a1a0-87042220ba9f` (Delay).
2. `DebugMap.Entries` contains an entry whose `NodeId == Guid da9a9c0b-25f8-4a81-9a52-75c715456f18` (Sequence).
3. The generated C# source contains the literal `DebugProbe.NodeEnter(self, "0b561966-b00b-4c84-a1a0-87042220ba9f")`
   and `DebugProbe.NodeEnter(self, "da9a9c0b-25f8-4a81-9a52-75c715456f18")`.
4. For every authored **exec** node id (the set CF-1 classified as exec — at minimum EventEntry `…0001`,
   SetVariable `…0002`, Sequence `da9a9c0b`, Delay `0b561966`, Return `7b6da53f`), there is exactly one
   `DebugProbe.NodeEnter` with that id. No `NodeEnter` is emitted for a pure data-only node id
   (e.g. GetVariable `…0004` must NOT have a probe).
5. **End-to-end pause** (mirror `BreakpointTests` style with the real `DataBreakpointManager` + `MockTimeController`):
   wiring a session, `SetBreakpoint(assetId, graphId, Guid 0b561966…)` (Delay) then driving the compiled blueprint
   one tick must result in `MockTimeController.PauseRequestCount == 1`. Repeat for the Sequence id.

Build 0 errors. Report the full before/after failing-test set; expect probe-count/step tests to shift (next batch).

---

## Batch CF-3 — Reconcile dependent tests, editor breakpoint gating, cleanup

**Depends on:** CF-2.

**Do:**
1. **Reconcile probe-count / step tests.** Adding correct exec-node probes changes how many `OnNodeEnter` calls
   fire. Find every test that asserts a probe/step/`OnNodeEnter` invocation **count** (STATUS.md lists ~10;
   examples: `StepOver_StepRequestCount_IsExactlyOne`, the `DebugProbeInsertionTests`,
   `Stage6_LoweringTests`). For each, update ONLY the expected count to the new correct value and document old→new.
   Do not change a test's intent. If a test now needs a structurally different graph to express its intent, STOP
   and flag for lead.
2. **Editor: gate breakpoints to probe-eligible nodes (no silent dead breakpoints).** Using the now-correct
   DebugMap (`DebugMapIndex.AllNodes` / `TryResolveNode`), make the canvas only allow/show "Toggle Breakpoint" on
   nodes that have a DebugMap entry (i.e. exec/breakpointable). For non-breakpointable nodes, either hide the menu
   item or show it disabled with tooltip "Not a breakpoint target (data node)". Touch points:
   `CanvasRenderer.cs` HoverKind.Node menu (`:732`) and/or the `editor.toggle-breakpoint` handler in
   `BlueprintDocumentFactory.cs:230-242`. Keep it data-driven from the session/DebugMap; do not hardcode node kinds.
3. **Remove the temporary diagnostics** from `Hrot.Blueprints.Editor/BlueprintDebugSession.cs`: the `DiagLog`
   method, `_diagCount`, `_diagLogPath` fields, and the `DiagLog(...)` calls in `SetBreakpoint` and `OnNodeEnter`
   (added during diagnosis; they write `bp-diag.log`). Delete `bp-diag.log`.

**SUCCESS CONDITION (CF-3):**
- `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors.
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **0 new failures** vs the documented
  pre-existing baseline; every changed test listed by name with old→new expectation and justification.
- No `DiagLog`/`bp-diag.log` references remain (`grep -r DiagLog`, `grep -r bp-diag` return nothing in source).
- Editor: setting a breakpoint is only possible on nodes with a DebugMap entry; CF-2's end-to-end pause tests pass.

**User smoke (after lead commit):** open `Count4`, compile, attach to a ticking entity, set a breakpoint on the
Delay node → sim pauses on it; on Add (if pure) the toggle is disabled with the data-node tooltip; clear → resumes.

---

## CF-2/CF-3 review outcome (2026-06-08) — partial fix; CF-4 required

CF-2/CF-3 shipped and **Sequence + Delay pause correctly** (verified: user smoke + `CF2_EndToEnd_DelayBreakpointPauses`).
But a hard review of the diff found the fix is **block-owner-only** and leaves two real defects:

- **Silent dead breakpoints (HIGH).** `IsNodeBreakpointable` returns true for *any* node with a DebugMap entry,
  but only block-owner nodes get a probe. For `Count4`, SetVariable `…0002` and Add `…0003` are in the map (gating
  allows a breakpoint) yet emit **no probe** → the breakpoint silently never fires. This is the original symptom.
- **Data node breakpointable + fires (MED).** GetVariable `…0004` (pure data) still gets a probe via the tier-3
  `Statements[0]` fallback in `DebugProbeInsertion`, and `IsNodeBreakpointable` returns true for it — despite its
  doc-comment claiming the opposite. Per the engine's whole-tick-rewind pause model, data nodes must not be targets.
- **Masked regression (MED).** `ProbeIntegrationTests.Breakpoint_FiresTwice_AcrossTwoTicks` was edited from
  targeting the Branch node to the EventEntry node — i.e. breakpoints on Branch regressed and the test was changed
  to test the node that still works. `CF2_AllExecNodes_HaveExactlyOneProbe_NoDataNodeProbes` was loosened to
  `<= 1` / "known limitation" rather than enforcing the spec.

Root cause: **one probe per block, keyed only to the block owner.** CF-4 makes the model correct and consistent.

---

## Batch CF-4 — Exec-only, block-granular breakpoints (no dead, no data targets)

**Depends on:** CF-2/CF-3. **Priority:** HIGH (the feature is currently inconsistent).

**Principle (from engine semantics — do not deviate):** the pause is a *soft* whole-tick pause (the tick runs to
completion; the entity repository is rewound to the pre-tick state; the clock pauses at the tick boundary). A
breakpoint is therefore a **coverage trigger** at **basic-block granularity** — "pause when execution reaches this
exec region." Consequences that bound this batch:
- **Do NOT add per-statement probes.** Block granularity is the correct and final level — per-statement probing
  adds zero debugging value (sub-tick state is never observable) and only churns step/probe-count tests.
- **Only exec nodes are breakpoint targets.** Pure/data nodes (GetVariable, LiteralNode, CastNode, pure
  FunctionCall) are never breakpointable and emit **no** probe.
- **A breakpoint on ANY exec node pauses when its containing block runs** — so every exec node the user can click
  is a live target (no dead breakpoints), even if several share one block.

### Task A — Compiler: probes keyed to exec owners only; build a node→block-probe map

1. **Classify exec vs data.** In Stage 5 the exec traversal (`ScheduleBlock`/`EmitNodeStatements`/control-flow
   handlers) visits exec nodes; data nodes are reached only via `ResolveNodeOutput`. Record the set of authored
   **exec** node ids, and for each exec node the **block** its statements landed in.
2. **Every reachable block gets a `SourceNodeId` that is an exec node.** Extend Stage 5 so the default
   `ScheduleBlock` path also sets `BlockBuilder.SourceNodeId` (today only entry/latent/sequence set it). The
   block's probe id = its `SourceNodeId`.
3. **`DebugProbeInsertion`: drop the data fallback.** Remove tier-3 (`Statements[0].Debug?.NodeId`). The probe id
   must come from `SourceNodeId` (or `OriginNodeId`); if a reachable block has neither, that is a compiler bug —
   fail loudly in a debug assert / test, do not silently key it to a data read. Result: **no probe is ever emitted
   for a pure/data node.**
4. **Emit a node→block-probe map into the DebugMap.** Add (e.g.) `IReadOnlyDictionary<Guid,Guid> BreakpointTargets`
   to `DebugMap`/`DebugMapIndex` (and serializer): for **every exec node**, `authoredNodeId → blockProbeNodeId`
   (many-to-one — all exec nodes sharing a block map to that block's probe id). Data nodes are **absent** from this
   map. Keep it `JsonIgnore`-when-empty for byte-stability.

### Task B — Editor session: translate breakpoints to the block probe

In `BlueprintDebugSession`:
1. **`SetBreakpoint(assetId, graphId, nodeId)`** resolves `nodeId → blockProbeId` via the DebugMap's
   `BreakpointTargets`. Store the breakpoint so that **runtime matching uses `blockProbeId`** (the id `OnNodeEnter`
   actually fires) while the **clicked `nodeId` is retained for the marker**. Add a `ProbeNodeId` to the
   `Breakpoint` record (clicked `NodeId` stays for display). `_bpByNodeString` is keyed by `ProbeNodeId`; since
   several exec nodes can map to one probe id, make it tolerate multiple breakpoints per key (e.g.
   `Dictionary<string,List<Breakpoint>>` or check all on hit). If no map is registered yet (pre-compile), fall back
   to keying by the clicked id (tentative breakpoint, as today).
2. **`IsNodeBreakpointable`** returns true **iff** the node is present in `BreakpointTargets` (exec node). Data
   nodes and unknown ids return false. Fix the doc-comment to match reality.
3. **`GetBreakpoints()` / the NodeEdit adapter `Breakpoints` set** must expose the **clicked `NodeId`** so the red
   marker draws on the node the user clicked, not the block owner.

### Task C — Tests (tighten, don't loosen), gating, cleanup

1. **Tighten `CF2_AllExecNodes_HaveExactlyOneProbe_NoDataNodeProbes`:** assert `CountProbesFor(GetVariableGuid) == 0`
   (data node, no probe) and that every exec node in `Count4` resolves through `BreakpointTargets` to a probe id
   that **is** emitted. Remove the `<= 1` / "known limitation" escape hatches.
2. **End-to-end:** add tests — `SetBreakpoint` on SetVariable `…0002` → one tick → `PauseRequestCount >= 1` (proves
   block-share translation works); `IsNodeBreakpointable(GetVariable …0004) == false`; Sequence + Delay still pause;
   marker set from `GetBreakpoints()` contains the clicked id.
3. **`ProbeIntegrationTests.Breakpoint_FiresTwice…`:** it may target any exec node, but add an assertion that a
   breakpoint set on the **Branch** node (Nodes[1]) also fires (via block translation) — i.e. the masked regression
   is closed, not re-hidden.
4. Editor gating already calls `IsNodeBreakpointable`; with Task B it now correctly disables data nodes. Confirm the
   "Toggle Breakpoint" menu item is disabled (with the data-node tooltip) on a pure node and enabled on exec nodes.

### SUCCESS CONDITION (CF-4)

- Build 0 errors. `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **0 net-new failures**
  vs. the true baseline (re-run and list the full failing set by name; resolve the CF-3 "8 vs 7" discrepancy —
  confirm every failure is genuinely pre-existing).
- For `Count4`: **no** `DebugProbe.NodeEnter` literal for GetVariable `…0004`; `BreakpointTargets` contains every
  exec node and **no** data node; a breakpoint on SetVariable, Sequence, or Delay each yields `PauseRequestCount
  >= 1`; `IsNodeBreakpointable` is false for GetVariable and any pure FunctionCall.
- Editor: breakpoint settable only on exec nodes; the red marker appears on the clicked node; the sim pauses.

**User smoke:** open `Count4`, compile, attach, set a breakpoint on SetVariable → sim pauses; the "Toggle
Breakpoint" item is disabled on Get Count (data) with the tooltip; Sequence/Delay still pause.

---

## Batch CF-5 — Step/Resume controls in the Blueprint Tools panel

**Depends on:** C (step backend) — independent of CF-4. **Priority:** MEDIUM (usability; backend already works).

**Context:** the Continue / Step Over / Step Into / Step Out buttons **already exist and work** in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs:34-63` (enabled when
`_session.IsPaused`, wired to `_session.Continue()/StepOver()/StepInto()/StepOut()`). The gap is purely
**placement**: after commit `d06fd144` ("merge four blueprint toolbar panels into single 'Blueprint Tools'
window"), the user wants these pause/step controls reachable from the **Blueprint Tools** panel, not a separate
Debug window.

**Do:**
1. **Locate the merged "Blueprint Tools" window** (introduced by `d06fd144` — `git show d06fd144 --stat` to find
   the class/file; it is NOT `GraphEditorWindow`). Confirm how it composes its sub-sections.
2. **Surface the step/resume controls there.** Add a "Debug" section to the Blueprint Tools window that renders the
   pause banner + Continue / Step Over / Into / Out row. **Reuse, don't duplicate:** extract the control-row from
   `DebugPanelWindow.DrawUI` into a small shared helper (e.g. `DebugStepControls.Draw(IBlueprintDebugSession)`) and
   call it from both the Blueprint Tools section and the existing `DebugPanelWindow`, so there is one source of
   truth. The section shows "Not paused" (disabled) when `!IsPaused`.
3. **Pass the debug session** into the Blueprint Tools window if it doesn't already have it (mirror how
   `DebugPanelWindow` receives `IBlueprintDebugSession`; the session is created in `EditorSubsystem` ~`:887`).
4. **Decide the fate of the standalone Debug window** (flag for lead, don't guess): either keep it as-is (controls
   shared) or retire it if the Blueprint Tools section fully replaces it. Default: keep both wired to the shared
   helper; do not delete in this batch.

**Tests:** mirror `DebugWindowDrawUITests` — headless: when the session reports `IsPaused`, the Blueprint Tools
debug section's buttons invoke the matching `IBlueprintDebugSession` method (use a capturing/mock session and the
`LastStepActionInvoked`-style capture already present in `DebugPanelWindow`); buttons inert/disabled when not paused.

**SUCCESS CONDITION (CF-5):** build 0 errors; tests pass with 0 net-new failures; the Blueprint Tools panel shows
Continue/Step Over/Into/Out when paused, wired to the session; the step-control logic is shared (not copy-pasted)
between the panel section and `DebugPanelWindow`.

**User smoke:** hit a breakpoint (e.g. on Delay) → in the Blueprint Tools panel press Continue → sim resumes; press
Step Over → sim advances one tick and re-pauses.
