# Blueprint Debugging (blueprint-dbg-1) — Onboarding for the next thread

**Mission:** make blueprint debugging work end-to-end in the running editor — placing **code breakpoints**
(node-level) and **data breakpoints** (pin/variable watches), **stepping** (Over/Into/Out), and **monitoring**
values in a **watch window**, against blueprints that are now authored→compiled→executing live in the editor.

Read this top-to-bottom, then read the Debug Protocol DD (linked below) before scoping the first batch.

---

## 0. The big context (what just happened, why this is now possible)
This continues a long line of blueprint work. As of branch **`blueprint-integ-1`** (where this work happens):
- **Blueprints compile + execute live in the editor.** The full compile chain was fixed this session (BP-1..4):
  - BP-1: generator stopped swallowing exceptions.
  - BP-3: removed an `Fdp.Toolkits` type from the serialized model so the **netstandard2.0 MSBuild generator**
    can deserialize (`ConditionMetPayload.Condition` is now `JsonNode?`). **Invariant:** the netstandard2.0
    compiler/generator must NEVER hard-depend on `Fdp.Toolkits` (it isn't loaded in the Roslyn analyzer host).
  - BP-2 (keystone): the compiler now **rehydrates pins** on the projection-only (`"Pins": []`) path via
    `Stage0_Rehydrate` + `BuiltInNodeRegistry.GetStaticPins`, with **link-GUID-driven** pin-id assignment
    (mirrors `BlueprintGraphModel.Rebuild`). Connection resolution in Stage4/Stage5 is **100% pin-id-driven**.
  - BP-4: editor `NodePinSchema` static shapes now delegate to `BuiltInNodeRegistry` (single source of truth).
  - Also: BATCH-06 (ChannelCommand real param pins) + BATCH-07 (inline value-pin mini-editors) + their fix
    (`ChannelCommandNodeDrawer`, editors on unset pins). BATCH-08 (fonts) + BATCH-09 (comments/reroutes/
    containers) were DEFERRED (see `.dev/blueprint-finalize/TASK-TRACKER.md`).
- **Execution path:** `EditorSubsystem.Initialize` wires `BlueprintRuntimeWiring.WireBlueprintRuntime` and appends
  the returned **`BlueprintTickSystem`** to the editor's `TogglableSimulationGroup` ("EditorSim"). So a Blueprint
  attached to a selected entity ticks **live in the editor**, against the same `BlueprintRegistry` the Quick
  Reload / hot-reload coordinator compiles into. **You do NOT need an external cluster/sim runner to debug.**
- **The user verified** authoring → compile (Quick Reload + Full Rebuild) → attach → tick (Count climbs).

---

## 1. How we work (the operating contract — follow it)
- **Lead loop (DEV-LEAD-GUIDE):** the lead (you) plans a batch, **delegates implementation to a `sonnet`
  sub-agent** (Agent tool, `subagent_type: general-purpose`, `model: sonnet`), then **reviews HARD** (reads the
  code + test assertions, runs the suites independently), writes a review, and **commits per batch** (the lead
  commits, never the sub-agent). Guide: `.dev/.guides/DEV-LEAD-GUIDE_claude.md`; the coder's contract:
  `.dev/.guides/DEV-GUIDE_claude.md`. Keep `TASK-TRACKER.md` + `DEBT-TRACKER.md` in this folder.
- **Trust-but-verify the sub-agent.** Sub-agents have repeatedly reported "0 regressions / all green" when they
  had introduced regressions or false baselines. ALWAYS re-run the suites yourself and **diff the failure-set
  by name** against the known baseline. Read the actual test assertions (watch for tautological/vacuous tests).
- **Codebase Memory MCP FIRST** (project `D-Work-IOS-IG-SimHost-FDP-2`): `list_projects` → `get_architecture`
  → `search_graph` / `get_code_snippet` / `trace_call_path` before reading files or `search_code`. This is a
  hard rule from `.claude/CLAUDE.md`.
- **The "engine architect" (NotebookLM).** The user relays questions to a NotebookLM architect trained on the
  design docs. It is precise on narrow design-intent questions but sometimes wrong on specifics (it once gave a
  confident-but-wrong bug location). **For design/intent questions, formulate a focused question for the user to
  relay rather than burning tokens spelunking; then VERIFY its answer against code.** For code-defect
  root-causing (bugs it can't know), investigate yourself. See `memory/feedback_notebooklm_architect_usage.md`.
- **Ask clarifying questions in chat prose, never the AskUserQuestion widget** (user preference).
- **Persistent memory** at `C:\Users\petr.janecek\.claude\projects\d--Work-IOS-IG-SimHost-FDP-2\memory\`
  (`MEMORY.md` index). Check it; add `feedback`/`project` memories as you learn.
- **Visual/interactive features need the USER to verify.** Headless gates (build/tests/boot) cannot exercise the
  ImGui canvas or interactive debugging. This session's lesson: BATCH-06/07 passed headless gates but had real
  UX gaps (missing selector drawer; editor gated on a non-null value) that only the user's smoke caught. So:
  build + headless-gate, COMMIT with a clear "VISUAL/INTERACTIVE VERIFICATION PENDING" note, and have the user
  smoke-test. For debugging especially (breakpoints actually pausing, stepping, watch updating), an interactive
  smoke is essential — design batches so the user can verify each increment.

---

## 2. Sources of information
- **Debug Protocol Detailed Design (READ FIRST):**
  `.dev/blueprints-1/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` — the authoritative spec. Sections:
  1 overview/goals, 2 `IBlueprintDebugSession`, 3 `DebugProbe` static dispatcher, 4 debug-map format,
  5 node-id resolution + structure-hash safety, 6 breakpoints, 7 step semantics, 8 watch expressions + pin-value
  snapshotting, 9 multi-entity debugging, 10 **PDB integration for source-line breakpoints**, 11 hot-reload
  interaction, 12 test strategy, 13 open questions. (M12 = debug protocol; **M13 = editor UI** — likely the bulk
  of remaining work.) Companion architecture: `.dev/blueprints-1/Blueprint_Subsystem_Architecture_v1.2.md` and
  the Compiler/Runtime/Hot-Reload/Editor DDs alongside it.
- **This session's write-ups:** `.dev/blueprint-compile-fix/` (BP-1..4 reports, `DEBT-TRACKER.md` with BCF-D01
  the real AllocationFree alloc bug, BCF-D02 golden snapshots, BCF-D03 CLR-FunctionCall MSBuild reflection);
  `.dev/blueprint-finalize/` (TASK-TRACKER/DETAIL + BATCH-06/07/fix reports; 08/09 deferral rationale).
- **NodeEdit** (the canvas framework): `FDP/ExtDeps/NodeEdit/` — `src/NodeEditor.Core` (interfaces),
  `src/NodeEditor.UI/Canvas` (renderers, incl. how inline editors/comments/containers render), and
  `src/NodeEditor.Demo` (working reference scenarios + `FakeBlueprint/` reference models). When wiring an editor
  feature, find the matching NodeEdit contract + demo scenario and model on it (don't invent contracts).
  One of demo cases shows breakpoints as red bullets, currently executed node highlighting etc - this is what  it shoudl look like.

---

## 3. Debug subsystem map (what already exists — verify current state before building)
The debug protocol (M12) is **substantially built**. Inventory:

**Core contracts + runtime (`Hrot.Blueprints.Core/`):**
- `IBlueprintDebugSession.cs` — the full surface: `Attach/Detach`, `SetBreakpoint(asset,graph,node)`/`ClearBreakpoint`,
  watches, `StepMode {None,Over,Into,Out}`, records `BreakpointHit`, `Breakpoint`, `Watch` (64-byte value buffer),
  `CallFrame`. Implements `IBlueprintProbeSink`.
- `DebugProbe.cs` — the static dispatcher the COMPILER emits calls into (zero-overhead in Release; routes to the
  session sink). `IBlueprintProbeSink.cs`, `DebugMapIndex.cs` (node→source mapping), `IBlueprintTimeController.cs`
  (stepping/time control — note: being superseded by `IEngineDebugTimeController`; `IBlueprintTimeController` is
  marked obsolete "removed after one batch").
- `DebugProbe` insertion happens in the compiler: `Compiler/Lowering/DebugProbeInsertion.cs`,
  `Compiler/Ir/IrDebugAnnotation.cs`, `Compiler/Emit/DebugMapBuilder.cs` + `DebugMapSerializer.cs`. The debug map
  ships next to the generated `.cs` (Quick Reload emits PDB + sets generated-source path for step-through —
  see `QuickReloadService` `EmitPdbWithEmbeddedSource`).

**Editor session + UI (`Hrot.Blueprints.Editor/`):**
- `BlueprintDebugSession.cs` — the production `IBlueprintDebugSession` (holds `_debugMaps`, `_activeEntities`,
  `_pdbLocators`, `RegisterDebugMap`, call-frame stack).
- `BlueprintBreakpointMenuPopulator.cs` (breakpoint context menu), `Debug/DebugPanelWindow.cs`,
  `Debug/WatchPanelWindow.cs`, `Debug/CallstackWindow.cs`, `Debug/HotReloadLogWindow.cs`/`HotReloadLogModel.cs`,
  `Debug/MasterSyncTimeControllerAdapter.cs` (time/stepping adapter).

**Tests (`Hrot.Blueprints.Tests/Debug/` + `Compiler/Stage{6,7}…` + `Runtime/BlueprintTickSystem/`):**
- `BreakpointTests`, `StepTests`, `WatchTests`, `StateInspectorTests` (+ `FIX2_009_InstanceStateInspectionTests`),
  `NodeHistoryTests`, `MultiEntityTests`, `ProbeDispatchTests`/`ProbeIntegrationTests`, `DebugMap{,Extension}Tests`,
  `BlueprintDebugSessionLifecycleTests`, `DebugSessionInterfaceTests`, `HotReloadInteractionTests`
  (breakpoints cleared on structure-hash change). Compiler: `DebugProbeInsertionTests`, `BPF015_DebugProbeEmitTests`,
  `FIX2_002_DebugMapEmitTests`. Test doubles: `CapturingDebugSession`, `MockTimeController`, `DebugProbeCollection`.

**Likely remaining work (verify against the DD + a current editor smoke):** the protocol/probe/map/breakpoint/
watch/step *machinery* exists and is tested; the gaps are probably in the **editor UX wiring (M13)** — does the
running editor let you (a) set a breakpoint on a node from the canvas/context menu and actually PAUSE the live
tick, (b) step Over/Into/Out via the toolbar/time-controller, (c) see live values in the Watch window and add
**data breakpoints** (break-on-write to a pin/variable). Start by smoke-testing what works today, then close gaps.

---

## 4. Key invariants & gotchas (don't relearn these the hard way)
- **Projection-only persistence:** blueprints save with `"Pins": []` (byte-stability). The compiler rehydrates
  pins (`Stage0_Rehydrate`). Any new persisted editor data (like debug breakpoints/watches if persisted) must be
  `JsonIgnore`-when-null/empty so existing assets stay byte-stable (see how `Node.PinDefaults` did it). Decide
  early whether breakpoints/watches persist in the asset at all (they may be session-only).
- **netstandard2.0 generator can't load `Fdp.Toolkits`** — never make the compiler/generator deserialize path
  reflect over game types (BP-3). Editor (net8) reflection is fine; the generator degrades gracefully.
- **Structure-hash safety:** breakpoints/debug-maps are keyed by node-id + a structure hash; a recompile/reload
  with a changed hash must reconcile/clear stale breakpoints (`HotReloadInteractionTests`, DD §5/§11).
- **Hot reload:** Quick Reload swaps a collectible ALC; debug maps/PDBs must re-register per reload. The
  per-registrar invocation is now isolation-wrapped (one bad asset can't crash the editor reload).
- **Test baseline:** the Blueprints suite has **7 pre-existing failures** (DEBT-006 golden/snapshot:
  AiPrimitiveEmitGolden MoveToAndFire/HasVisibleTarget, LibraryEmitGolden, LibraryMath/MoveToAndFire
  `*_GeneratedSource_Snapshot`, `ConditionSummaryAttachmentTests.…EqsResult`, `AllocationFreeTests`). Keep new
  failures = 0; list the full failure-set in every review. `AllocationFreeTests` is ALSO **flaky-adjacent** and
  has a real underlying alloc bug (BCF-D01 — `EntityRepository.FlushCommandBuffers` allocates via
  `ThreadLocal.Values`; a fix was reverted because it regressed a recorder test). `AtomicMultiFileWriter` temp-
  file test is flaky (passes isolated).
- **Don't regenerate golden snapshots** unless codegen intentionally changes (`BLUEPRINT_REGENERATE_SNAPSHOTS=1`).
- **The running editor LOCKS dlls** — builds/tests fail to copy while the app is open. If a build/test reports a
  file-lock, ask the user to close the editor; don't work around with stale binaries.
- **Standard gates per batch:** `dotnet build IOS-IG-SimHost.sln -c Debug` (0/0), `Hrot.Blueprints.Tests`
  (7 pre-existing/0 new), `Hrot.Editor.AiShared.Tests`, `EditorSubsystemBoot` 10/10. Plus a **user interactive
  smoke** for the debugging UX.
- Constraints: branch `blueprint-integ-1`; GizmoMap.Contracts 0.2.2; no `Hrot.IG`/DDS/`Stride/`; stay on
  `EditorSubsystem` (no `editor_stride`). There may be **uncommitted user WIP** (e.g. New-from-Recipe / Full
  Compile UI) — check `git status` and never revert the user's files.

---

## 5. Suggested first steps
1. `list_projects` → `get_architecture` (MCP). Read the Debug Protocol DD fully.
2. Ask the user to run the editor and smoke the CURRENT debug state: set a node breakpoint, run a blueprint on an
   entity, see if it pauses; try step Over/Into/Out; open the Watch window; try a data/watch on a pin. Get a
   concrete "what works / what's missing" list (this is the equivalent of the BATCH-06/07 visual smoke that
   surfaced the real gaps).
3. From that + the DD, write `TASK-TRACKER.md` + `TASK-DETAIL.md` here, decompose into small batches (each
   user-verifiable interactively), and run the lead loop (delegate→review→commit).
4. Formulate focused architect questions for any design-intent ambiguity (e.g. data-breakpoint semantics, step
   granularity over a visual graph, watch-on-pin vs watch-on-variable) and have the user relay them.

Good luck, future me. The machinery is mostly there; the work is making it land in the live editor UX, verified
interactively, one small batch at a time.
