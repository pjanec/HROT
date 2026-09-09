# Blueprint Finalization — Onboarding for a fresh DEV-LEAD session

You are the **dev lead** for finishing the Hrot blueprint/BTree/HSM AI-editor subsystem on branch
`blueprint-integ-1`. This doc onboards a brand-new chat. Read it fully before doing anything.

---

## 0. Operating model (READ FIRST — this is non-negotiable)

The user is **token-constrained on you (Opus/Sonnet)**. You do **NOT write implementation code**. You are lead-only:
1. **Plan** → write a focused batch markdown in `.dev/_DONE/blueprint-finalize/batches/<NAME>-INSTRUCTIONS.md`.
2. **Hand the user a paste-ready prompt** for their external coding agent.
3. **Review hard** when they say it's done: read the diffs + test assertions, run the suite yourself, verify the
   behavior independently, **curate out litter**, then **commit** (you commit; the agent doesn't).

There are two external agents the user runs (you never spawn sub-agents for impl):
- **Zoo** — experimental Cline-based agent. Strong on **small, single-objective, headless-testable** tasks (it
  landed EXECFANOUT, DIAGFAIL, SEQ1, DELAYTIME, INSPECTOR-FIELDS clean, one round each). **Weak/negative on multi-part
  or non-headless-testable work**: loses focus, **gives up early / reports "complete" with red tests**,
  **rationalizes failures** ("test-harness limitation" — usually false), **creeps scope** (touches other batches'
  committed files, re-litigates committed decisions), and **leaves litter** (debug `File.WriteAllText`, a `$null`
  junk file). It also shares habit of **neutering/excluding assets to make a build pass**.

**Hard rules for EVERY batch you write** (learned the hard way over EXECFANOUT→SEQ2→DELAYTIME):
- **Reference `.dev/.guides/DEV-GUIDE.md`** (the plain variant). 
- **Prescribe the EXACT test assertions** — never let the agent invent its own success conditions. Give the
  *discriminating* assertion (e.g. "compile the generated source via Roslyn and assert no CS errors", "assert block X
  is a goto target / reachable", "tick at time=100.5 → Count==1 (still waiting)"), not just a scenario.
- Include a **DO-NOT-STOP-UNTIL-GREEN** clause verbatim: the agent must run
  `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`) itself and is
  not done until `Failed: 0` (except the one documented zero-alloc test); loop until green; never report complete
  with red tests; end the report with the green suite output.
- **Guardrails**: never edit/exclude/neuter user blueprint assets or csproj includes, suppress diagnostics with
  pragmas, weaken assertions, or touch other batches' committed files to make a build pass. Fail loud, not silent.
- **ONE objective per batch.** Split anything bigger. (BB1-as-one-batch already failed this test — see §2.)

**On review, ALWAYS, independently:**
- Run the full suite **without** the regen flag (regen mode writes goldens, masking failures).
- Re-verify the risky path yourself (compile generated code, run a tick, read the actual IR/values) — the agents
  write plausible tests that don't exercise the real bug (SEQ1's propagation tests were Return-masked no-ops; the
  DELAYTIME test defaulted duration to 0).
- **Curate the commit**: exclude any `*.bp.json` the user is live-experimenting with; delete
  litter (`$null`, debug writes); revert scope-creep before committing.

Memory files (auto-loaded) back this up: `feedback_external_copilot_agent_delegation`,
`reference_zoo_experimental_coding_agent`, `feedback_batch_workflow_tracker_and_sonnet`,
`feedback_snapshot_regen_masks_failures`, `feedback_ask_in_chat_not_widget`.

---

## 1. What's DONE (committed on `blueprint-integ-1`)

Recent arc (newest first), all committed + reviewed:
- `1eab735f` INSPECTOR-FIELDS — runtime inspector shows Instance state fields via `BlueprintDefinition.StateFields`
  fallback when the DebugMap layout is absent.
- `1f2b3752` DELAYTIME — `LatentDelay` `WaitUntilTime = time + duration` (relative, was absolute).
- `2ddbc230` SEQ2 — Sequence×latent emit: fresh-start dispatch → `graph.Entry` (reachable entry), per-block braces
  removed (shared method scope; fixes cross-block CS0103), dead-block filtering by reachability (no pragmas), latent
  loop reset; locale InvariantCulture.
- `11964f6b` SEQ1 — `SequenceNode` branch scheduling (per-branch blocks chained via `IrTerm_Goto` + `_fallThroughTarget`).
- `417d6c88` DIAGFAIL-REBUILD — BP1412 fail-loud on dropped exec successors; `<UpToDateCheckInput>` so editor Full
  Rebuild regenerates codegen on a `.bp.json`-only change.
- `470a2ecb` EXECFANOUT — BP1411 fail-loud on exec-out fan-out; editor exec-out 1:1 replace-on-reconnect.
- Earlier: the whole AN1–AN8b non-channel action track, enum mechanism, JSON pretty-print, SE1 StructEdit facet,
  FIX-A/B/C, demo actions. See git log + `reports/`.

**Net result the user can use now:** Sequence fan-out works; a latent (Delay) inside a Sequence loops and increments
~once per period; generated code compiles clean; `Count` shows in the runtime inspector.

Suite baseline: ~**1651 passed / 1 failed / 8 skipped**. The 1 failure is the documented pre-existing
`TickFrame_1000Frames_AllocatesZeroBytes` (genuine ~3.2 bytes/entity/frame runtime alloc, likely `EntityQuery.ForEach`
closures — NOT a regression; do not chase unless tasked).

---

## 2. What REMAINS (the point of this session) — with agent-fit + decomposition

Authoritative lists: **`TASK-TRACKER.md`** (checkboxes) + **`TASK-DETAIL.md`** (specs). Design docs:
`ACTION-NODE-DESIGN.md`, `docs/blueprints/Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md`
(BB1, architect-approved), `Blackboard_Authoring_Detailed_Design.md`, `ENUM-DESIGN.md`, `DESIGN-DEBT.md`.

### A. BB1 — Action-parameter authoring + node-owned variables (Phase 7) — user's priority, but BIG
Action nodes bind their **whole** param DTO to **one** blackboard variable; "+ Promote to new variable" auto-creates
a hidden node-owned (`IsAutoManaged`) variable. **Split into a LOGIC track (Zoo-able, headless) and a UI track
(visual + composition-root wiring — Zoo-unsuitable; needs eyes-on-editor and is token-hungry).**
- **Logic (small Zoo batches, headless tests):**
  - **B-2** — add `IsAutoManaged` (bool) to `BlackboardVariableDto` (persist, JsonIgnore-when-false for byte
    stability) + `BlackboardVariableEntry`; "+ Promote" creates `_auto_{VisualId:N}` (BTree)/`_auto_{StableId:N}`
    (HSM) var of the action's `DtoType` and binds `ExpressionTargetField`. Tests: JSON round-trip; promote yields a
    correctly-typed bound var. **Start here — it's the foundation.**
  - **B-1 filter logic** — given an action's `DtoType` (`IActionSchemaExporter`) + a variable list, return only
    compatible vars. Headless.
  - **B-4 lifecycle** — on owning-node delete, command sink (`BTreeCommandSink`/`HsmCommandSink`) removes the
    node-owned var + re-packs; exclude `IsAutoManaged` vars from Approach-A alias-target lists. Headless (model).
- **UI track (NOT Zoo-alone; visual smoke + you must verify composition-root wiring):**
  - **B-1 drawer** (type-filtered `[BlackboardFieldPicker]` rendering), **B-3** (StructEdit inline render of the bound
    var's `DefaultValueJson`), **B-4 presentation** (dimmed "Node-Owned Allocations" group in `VariablesPanelControl`),
    **B-5** (static-vs-dynamic tooltip). Gate: **REVIEW-BB1** user smoke.
  - ⚠️ **Editor live-wiring is the #1 recurring trap** (see §3). Budget for it; you may need to do the wiring yourself.

### B. AN9 — "Wait Until Completed" static metadata (Phase 5C) — good Zoo fit (compiler-side)
Add a STATIC `WaitUntilCompleted` bool (default true) to the generalized action node (persisted). Stage-5 fuses by
the static value (channel+true → ChannelCommand then WaitForChannel; channel+false → fire-and-forget; non-channel+true
→ inline-latent; non-channel+false → forbidden, Stage-2 **BP1405**). UI: a Details checkbox (disabled+locked-true for
non-channel). Mostly headless (golden/emit + diagnostic tests) → delegate to Zoo; the checkbox is a small visual bit.
Spec: `TASK-DETAIL.md` AN9 / `ACTION-NODE-DESIGN.md §ROUND-5`.

### C. BATCH-09 — comments / reroutes / containers (Phase 4) — LARGE, deeply visual, DEFERRED
NodeEdit infra exists (`ICommentModel`/`IContainerNodeModel`, renderers, demo Fakes). Adds NEW persisted asset model
(comment boxes/containers/reroutes — **must be JsonIgnore-when-empty for byte-stability**). **Unverifiable headlessly**
→ needs visual iteration. Do this only with a real visual-review budget; not a Zoo-alone task.

### D. Visual review gates (user smoke; you can't do these headlessly)
- **REVIEW-V1** (Phase 5B) — per-action palette, immutable action nodes with baked pins, enum combos, compile→run.
- **REVIEW-BB1** — see above.

### E. Debt (Phase 8, on-demand)
- AN1 vector/Quaternion inline-default literal materialization (skipped; enums assume int-backed).
- DD-1..DD-4 (`DESIGN-DEBT.md`).
- The pre-existing zero-alloc test (own investigation if ever tasked).
- HSM-TRANS / JSON-PRETTY-BTHSM are DONE (see reports).

**Recommended order:** B-2 (BB1 logic foothold, Zoo) → B-1 filter + B-4 lifecycle (Zoo) → AN9 (Zoo) → then the
BB1 UI track + BATCH-09 as a deliberate visual push when token budget allows. Confirm priority with the user.

---

## 3. Critical technical context + traps

- **Compiler pipeline** (`Hrot.Blueprints.Compiler`, netstandard2.0): Stage2_Validate → Stage3_Normalize →
  Stage4_TypeResolve → Stage5_Schedule (BFS basic-block scheduler; `IrTerm_Goto/Branch/Return/Suspend/FallThrough`;
  `_fallThroughTarget` redirect for Sequence) → Stage6 wait-lowering (`WaitLowering_Instance`/`_AiPrimitive` build the
  ResumeAt/phase dispatch entry block — fresh-start edge must target `graph.Entry`) → Stage7_Emit
  (`CSharpEmitter`/`BlockEmitter`/`InstanceEmitter`/etc., a labelled-block **goto state machine**; locals are
  method-scoped, no per-block braces) → Roslyn. Diagnostics in `DiagnosticCodes.cs` (BP1411 fan-out, BP1412
  dropped-exec, BP1413 reserved).
- **#1 RECURRING TRAP — editor live-wiring gaps:** every agent passes headless tests but leaves the **composition
  root** (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`) un-wired so the feature is invisible live (AN4 palette
  catalog, AN7 catalog threading, SE1 facet service, FIX-A bridges, the ActionSchemaExporter.Rebuild gap). For ANY
  editor batch: **independently verify the wiring in EditorSubsystem.cs**, and expect a user visual smoke.
- **Byte-stability:** new persisted asset fields MUST be `JsonIgnore`-when-null/empty/false (e.g. `IsAutoManaged`,
  `ActionFqn`). Golden/round-trip tests enforce this.
- **Snapshot regen masks failures:** `BLUEPRINT_REGENERATE_SNAPSHOTS=1` **writes** goldens instead of comparing.
  Always run the suite **without** it for the true baseline. If an emit change legitimately shifts a golden,
  regenerate only after proving the diff is exactly the intended change.
- **`Count4.bp.json` is a USER experiment asset** (live Sequence+Delay test) — do NOT commit changes to it; exclude
  from every commit. (`Counting.bp.json` was deleted earlier.)
- **NodeStatus:** generated game code must use `global::Fbt.NodeStatus` (Failure=0,Success=1,Running=2). The
  compiler-internal `Hrot.Blueprints.Core.Assets.NodeStatus` must NEVER appear in emitted code.
- **Env:** Windows / PowerShell (or Bash tool). Project mandates the **codebase-memory MCP** for YOUR exploration
  (see `.claude/CLAUDE.md`) — `list_projects` → `get_architecture` first. (Don't put this in agent prompts.)
- **Run/verify** the test project: `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`. Compile a specific
  asset by building `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj --no-incremental` and reading
  `obj/GeneratedFiles/Hrot.Blueprints.Generators/.../<Asset>_<hash>_Bp.g.cs`.

---

## 4. Where things live
- `.dev/.guides/DEV-LEAD-GUIDE.md` (your loop) + `DEV-GUIDE.md` (the agent contract you reference in prompts).
- `.dev/_DONE/blueprint-finalize/`: `TASK-TRACKER.md`, `TASK-DETAIL.md`, `DESIGN-DEBT.md`, `ACTION-NODE-DESIGN.md`,
  `ENUM-DESIGN.md`, `batches/`, `reports/` (agent writes), `reviews/` (you write).
- Design: `docs/blueprints/Blackboard_Authoring_*.md`.
- Compiler: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/`. Editor:
  `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/` + `Hrot/Editor/Hrot.Editor.AiShared/` + BTree/HSM editors under
  `Hrot/Subsystems/AI/`. Composition root: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`.

---

## 5. First moves in the new session
1. Read this, `TASK-TRACKER.md`, and the relevant `TASK-DETAIL.md` / design section for the chosen task.
2. Ask the user which remaining item to tackle (default suggestion: **BB1 B-2 logic foothold** via Zoo).
3. Write a **single-objective** batch with prescribed assertions + do-not-stop-until-green + guardrails; hand over the
   paste-ready prompt.
4. On "done": review hard, run the suite (no regen flag), verify independently, curate the commit, commit. Repeat.
