# Onboarding — Thread 2: Blueprint integration finalization + remaining authoring features

> **New-chat brief.** This document is self-contained: it assumes no prior conversation. Read it top to bottom, then read the files it points at before changing anything.

## Mission

The blueprint **visual editor + full runtime lifecycle** is integrated into ClusterRunner's `EditorSubsystem` and the live loop is closed (author → compile → run → observe → save → hot-reload). This thread:

1. **Hardens** the integration — most importantly fixes **DEBT-MVE-003** (a P1 correctness blocker that corrupts the registry the moment there is more than one editor-compiled blueprint).
2. **Adds the remaining authoring features** the user asked for to reach a true "full editing experience": **FunctionCall configuration**, **function return values / graph-signature editing (Return + Entry value pins)**, **node value pins for all node kinds**, plus the canvas-polish backlog (mini-editors, fonts, comments/reroutes/containers, ChannelCommand enrichment).
3. Makes the live loop **durably observable** via **DEBT-MVE-002** (emit `StateFields` in codegen) and ships a **canvas-authorable demo blueprint that visibly counts up**.

**This thread runs FIRST.** A separate thread (persistence JSON unification + unified save + unified asset browser — see `.dev/persistence-unification/ONBOARDING.md`) runs after and depends on nothing here, but both touch the editor, so land this thread's blueprint-runtime changes first.

**Scope guardrails:** branch `blueprint-integ-1` (anchor commit `42aab24c`). **No `editor_stride`** in this branch — target `EditorSubsystem` only. **GizmoMap.Contracts stays 0.2.2.** Do not touch `Hrot.IG` / DDS / `Stride/`.

## How we work (non-negotiable conventions)

- **Read `.dev/.guides/DEV-GUIDE_claude.md` first** — it is the coder contract (verify-first, cite file:line, never fake a pass, run the full implement→build→test→fix loop to green before reporting).
- **Codebase Memory MCP FIRST** (per `.claude/CLAUDE.md`): `list_projects` → `get_architecture` → `search_graph`/`trace_call_path`/`get_code_snippet`. Project name: `D-Work-IOS-IG-SimHost-FDP-2`. Do **not** use `search_code`.
- **Delegate implementation and test-fix-test-fix to `sonnet` agents** (explicit user cost directive — conserve Opus). The lead plans, writes batch instructions, reviews hard, verifies, commits.
- **Projection-only invariant (inviolable):** loaded `.bp.json` store `"Pins": []`; pins are hydrated at runtime by `NodePinSchema`. Never persist pins. This is protected by a byte-stability test and the compiler golden/snapshot tests — they must stay green (or be deliberately re-baselined with `BLUEPRINT_REGENERATE_SNAPSHOTS=1` only when codegen intentionally changes).
- **Batch workflow:** write `.dev/blueprint-finalize/batches/BATCH-XX-INSTRUCTIONS.md`, delegate to a sonnet coder, review, write a report under `.dev/blueprint-finalize/reports/`, then commit per batch. Commit via a message file: write `.git/BFxx_MSG.txt`, then `git commit -F .git/BFxx_MSG.txt`. End commit messages with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Exclude `Stride/` and `no-tests-HROT.Engine.dumpfilter` from staging.

## What already exists (the closed lifecycle)

Committed MVE batches (all on `blueprint-integ-1`):

| Commit | What |
|---|---|
| `7d2b35da` | MVE-02: blueprint runtime wired into the real `EditorSubsystem` kernel (tier components + `BlueprintTickSystem` + maintenance) + `BlueprintAttachService` |
| `8d69a5d0` | MVE-03: "Run Opened Blueprint on Selected Entity" toolbar button (run-mode-agnostic; uses the currently-selected entity) |
| `a4682ceb` | MVE-04: editor Save of the active blueprint → `.bp.json` (projection-only) |
| `1b34ea1a` | MVE-05: compile-on-demand — `QuickReloadService` compiles the opened in-memory asset and registers it into the SAME `_blueprintRegistry` the kernel ticks ("Compile / Reload Blueprint" toolbar) |
| `cbcb9928` | MVE-06: debug-observe — `CaptureLiveState` API + `BlueprintRuntimeInspectorPane` (reads live state via the compiler's DebugMap, not StateFields) |
| `42aab24c` | MVE-07: hot-reload proof (behavior swap + state-preserved / structural hard-reset / observe-survives) + DEBT-MVE-003 upgraded to P1 |

**Read these for full context (they are dense and accurate):** every file under `.dev/blueprint-mve/reports/` and `.dev/blueprint-mve/reviews/` (MVE-02 … MVE-07). Also `docs/blueprints/Blueprint_Subsystem_Editor_Detailed_Design.md`, `.../Blueprint_Subsystem_Compiler_Detailed_Design.md`, `.../Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`, `.../Blueprint_Subsystem_Runtime_Detailed_Design.md`, and the NodeEdit specs in `docs/blueprints/NodeEdit/`.

### Key files / seams

- **Editor composition root:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — kernel build (~640–730: `simHostCorePack`, sim group, `EditorSimulationModule`), `BlueprintRuntimeWiring.WireBlueprintRuntime` + `.Append(bpTick)`, the `RegenerationScheduler` + `AiAssetEmitService` + `QuickReloadService` wiring (~2040–2130), the runtime-inspector pane registration (~1900–1925, BTree/HSM/Blueprint).
- **Runtime:** `FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs` (re-resolves the definition every tick — line ~85; StructureHash reconciliation lines ~87–99), `.../BlueprintRegistry.cs` (atomic `CommitStaging` **full-replace** — lines 117–138), `.../Components/*` (tier blackboards, slot entries), `BlueprintBlackboardPartitions` (TryAttach / TryGetSlotOffset).
- **Compiler:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/CSharpEmitter.cs` (`EmitRegistrarClass(asset)` — one registrar per compiled asset, line 103; `EmitInstanceRegistration` where `StateFields` is currently NOT written), `FieldLayout`/`StructureHashComputation` (`Compiler/Lowering/StructureHashComputation.cs:9-17` — hash covers Dispatch/Parameters/WorkingState/Variables, NOT the Tick body), `DebugMap`.
- **Editor blueprint host:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` — `Reload/QuickReloadService.cs`, `Host/NodePinSchema.cs` (canonical pins per node kind — **the place to extend for value pins / FunctionCall / Return pins**), `Host/BlueprintGraphModel.cs`, `Host/BlueprintCommandSink.cs`, `Host/BlueprintNodeCatalog.cs`, `NodeDrawers/BlueprintNodePaletteEntries.cs`, `BlueprintDebugSession.cs` (`CaptureLiveState`/`CaptureStateSnapshot`), `Inspector/BlueprintRuntimeInspectorPane.cs`.
- **Coordinator / ALC:** `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs` (single `_currentAlc`, `ApplyQuickReload`, unloads old ALC lines ~188–190).
- **Hand-built test fixtures / harness:** `Hrot.Blueprints.Tests/` — `BlueprintTestFixture`, `BlueprintRunHarness`, `BlueprintCompileOnDemandMveTests`, `BlueprintHotReloadMveTests`; `Hrot.ClusterRunner.Integration.Tests/BlueprintObserveTests.cs`.

## Tasks (suggested order)

### 0. DEBT-MVE-003 — P1 production blocker (do early)
**Problem (confirmed, cited in `.dev/blueprint-integ-1/DEBT-TRACKER.md`):** `BlueprintRegistry.CommitStaging` fully **replaces** the snapshot (`BlueprintRegistry.cs:117-138`); `CSharpEmitter.EmitRegistrarClass(asset)` emits **one** registrar per compile (`CSharpEmitter.cs:103`); `QuickReloadService` stages only that one assembly's registrar (`QuickReloadService.cs:120-157`); `AiHotReloadCoordinator` tracks a single `_currentAlc` it unloads on the next reload (`AiHotReloadCoordinator.cs:188-190`). **Consequence with >1 editor-compiled blueprint:** quick-reloading blueprint A (a) **wipes** B, C… from the registry, and (b) **dangles** B/C's `Tick`/`InitDefault` delegates in an ALC that gets unloaded → access violation on next tick.
**Fix (architectural — design before coding):** Option 1 — before `CommitStaging`, **carry forward** all live definitions except the recompiled id into staging, AND track ALCs **per asset** (`Dictionary<blueprintId, AssemblyLoadContext>`) so only the recompiled asset's old ALC is unloaded. Option 2 — a **merge-commit** mode on `BlueprintRegistry` (upsert, not replace) + multi-ALC retention. Option 1 is simpler but must also carry forward code-defined (non-ALC) defs. **Must be atomic.** Add a multi-blueprint test (compile A, run; compile B; assert A still ticks and is not wiped/crashing) — this is the proof that's missing today.

### 1. FunctionCall configuration
The user asked "how can we configure Function Call?" A FunctionCall node needs: a **library/function picker** (which function/graph to call) + a **Details panel** to bind it, and its **pins must reflect the chosen function's signature** (params in, return out). Extend `NodePinSchema.GetCanonicalPins` for FunctionCall (today it derives FunctionCall pins via reflection — see the BCP-03 work). Reference NodeEdit demo `S18_FunctionAuthoring` and `S30_GoToDefinition`.

### 2. Function return values / graph-signature editing / Return + Entry value pins
The user's question "function return needs a data pin — where does the return value come from?" Implement **graph signature editing** (a graph's `Inputs`/`Outputs`) so the **Entry** node exposes input **value pins** and the **Return** node exposes output **value pins**, and FunctionCall nodes mirror that signature. Reference NodeEdit demo `S15_VariablesGetSet`, `S16_PromoteToVariable`, `S18`, `S19_MultipleReturnNodes`. This is coupled to #1.

### 3. Node value pins for all node kinds
User: **"node value pins for all kinds are more important than mini-editors."** Audit every node kind in `BlueprintNodePaletteEntries` / `NodePinSchema` and ensure each exposes its proper **data/value pins** (not just exec pins). This is the highest-priority authoring feature.

### 4. Canvas polish backlog (lower priority)
Inline mini-editors; fonts (engine multi-size atlas — see NodeEdit `S05`/font handling); comments/reroutes/containers (`S06`, `S26`, `S27`, `S35`); **ChannelCommand param enrichment (DEBT-BCP-006)**.

### 5. DEBT-MVE-002 — emit StateFields in codegen (durable observe; +5 golden)
Today `CSharpEmitter` does **not** write `BlueprintDefinition.StateFields`, so a *compiled* blueprint's working-state can't be read by field name via `BlueprintStateView.TryGetField` (the observe/hot-reload tests use hand-built defs or the DebugMap path as a workaround). Emit `StateFields` from `FieldLayout` (offsets from byte 16, after the 16-byte `BlueprintLatentCursor`). **This regenerates ~5 additive Instance golden fixtures** — regenerate with `BLUEPRINT_REGENERATE_SNAPSHOTS=1` and review the diff carefully (it must be purely additive `StateFields`).

### 6. Canvas-authorable counting demo
The current proofs use code-defined `CounterDemoBlueprint` / hand-built defs because `BlueprintAssetBuilder` can't author an increment (`SetVariable` discards the value expression; no Add/GetVariable node — `BlueprintAssetBuilder.cs:231-237`). Once #2/#3 land, author a real `.bp.json` whose Tick increments a blackboard `Count` and that compiles + runs + shows a climbing value in the inspector — so a **manual editor test is convincing**.

## Verification (reach green before reporting)
- `dotnet build IOS-IG-SimHost.sln` — 0 errors; touched projects 0 new warnings (a full `--no-incremental` rebuild surfaces ~26 **pre-existing** warnings in unrelated test projects — leave them; DEBT-BCP-004).
- `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10 (composition integrity).
- `Hrot.Blueprints.Tests` → only the **10 pre-existing DEBT-006** golden/snapshot failures (0 new) unless a task intentionally re-baselines goldens. The flaky sub-80ns perf test (DEBT-014) passes in isolation.
- `Hrot.Editor.AiShared.Tests`.

## Pre-existing failures (NOT regressions — don't chase)
DEBT-006 (10 Blueprints golden/snapshot), DEBT-008 (BreakpointSubsystemWiring), SpatialHashSystem AV in EditorPreview, ClusterOpE2eScriptTests DDS crash, flaky sub-80ns perf (DEBT-014), ~26 pre-existing warnings (DEBT-BCP-004). Baseline against `git stash` if unsure whether a failure is yours.

## Done-definition for this thread
Multi-blueprint editor use is safe (DEBT-MVE-003 fixed + tested); FunctionCall is configurable; graphs have editable signatures with Entry/Return value pins; every node kind exposes its value pins; compiled blueprints are observable by field name (DEBT-MVE-002); and a hand-authored `.bp.json` demonstrably counts up in the running editor.
