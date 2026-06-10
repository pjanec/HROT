# Blueprint Debugger — Onboarding for a fresh DEV-LEAD session (blueprint-dbg-2)

You are the **dev lead** for the next phase of the Hrot blueprint **debugger** (branch `blueprint-integ-1` /
`blueprint-dbg-*` lineage). This thread continues the breakpoint/stepping work from `.dev/blueprint-dbg-1/`. Read this
fully before doing anything.

---

## 0. Operating model — SONNET SUBAGENTS you orchestrate (NOT Zoo)

**This thread does NOT use the external Zoo/Copilot delegation.** You are the dev lead and you **spawn sonnet
sub-agents via the Agent tool** to do the implementation, exactly per the original loop:

- Workflow contract: `.dev/.guides/DEV-LEAD-GUIDE_claude.md` (your loop) + `.dev/.guides/DEV-GUIDE_claude.md` (the
  coder sub-agent's contract). Use the **codebase-memory MCP first** for exploration (`.claude/CLAUDE.md`).
- Delegate each batch with the **Agent tool**, `subagent_type: general-purpose`, `model: sonnet`. The sub-agent reads
  `DEV-GUIDE_claude.md` + the batch file, implements, writes its report, returns. You review hard and commit.
- You may run several independent sub-agents in parallel (one message, multiple Agent calls) when work is independent.

**Keep the hard-won discipline (it applies to sonnet sub-agents too):**
- **Prescribe the EXACT test assertions** in the batch — never let the agent invent its own success conditions. For a
  debugger feature the gold standard is *behavioral*: drive ticks/steps and assert recorded state/overlay/cursor
  values, not "object exists".
- **Do-not-stop-until-green**: the agent runs the full affected suite itself (no `BLUEPRINT_REGENERATE_SNAPSHOTS`) and
  loops until `Failed: 0` (except the one documented pre-existing zero-alloc test).
- **Review hard, verify independently**: read diffs + assertions, run the suite yourself, and reproduce the actual
  behavior. Curate the commit (exclude user experiment `.bp.json` like `Count4.bp.json`; delete any litter). You
  commit; the sub-agent doesn't.
- One objective per batch; split big work.

Relevant memories (auto-loaded): `feedback_batch_workflow_tracker_and_sonnet`, `project_debugger_node_granular_stepping`,
`project_blueprint_breakpoint_id_drift`, `feedback_snapshot_regen_masks_failures`, `feedback_ask_in_chat_not_widget`,
`reference_notebooklm_architect_usage`.

---

## 1. Mission (this thread)

Primary: **node-granular stepping** — make "Step" move the execution pointer **between nodes** and show the entity
state *as of that node*, instead of today's tick-granular pause. Full design + the limitation analysis is in
**`DEBUGGER-NODE-GRANULAR-STEPPING-IDEA.md`** (copied into this folder — read it first; it's the spec).

Also in scope (debugger backlog): finish/forward-port any open CF tasks (see §3 clone note), and whatever debugger
polish the user prioritizes.

---

## 2. Why today's stepping is tick-granular (the core constraint — verified)

- A compiled blueprint **tick is atomic**: the generated goto-state-machine runs the **entire synchronous node chain**
  in one tick, stopping only at a **latent suspend** (Delay / WaitForChannel) or Return. Probes (`OnNodeEnter`) are
  **non-blocking callbacks** — they can't halt the method mid-tick.
- Pause/inspect uses `DataBreakpointManager`'s **triple-buffer, TICK-granular** rewind: `_preTickSnapshot` (start of
  tick), `_postTickSnapshot` (hit time); on a hit it rewinds the live repo to pre-tick, and while paused the view =
  pre-tick state. Exec/node breakpoints engage it via `HandleBreakpointHit → _dataBreakpointManager.OnExternalHit`,
  plus `_timeController.RequestPause()` (clock pause, **no mid-tick halt, no rewind for exec BPs** beyond the DBM's
  pre-tick restore).
- **Consequence:** any pause *inside* a multi-node tick shows the same start-of-tick state. You cannot see
  "after SetVariable, before Delay" — there's no such snapshot. The "one node per tick" intuition only holds at latent
  boundaries.
- Zoo's CF-6 (`34748364`, gate `_onNodeExecuted` on `!_isPaused`) only fixed which node the **overlay highlights** —
  not the state shown. Orthogonal to node-granular stepping.

## The proposed fix (in the design file): per-probe ECS snapshots + virtual execution pointer
Record an ECS snapshot at **each probe (`OnNodeEnter`)** during debug-active ticks; while paused, "Step" moves a
**virtual pointer** over the recordings and restores the target node's snapshot into a read-only view — **clock stays
paused**, no re-execution. Snapshot-at-entry yields exactly the wanted semantics (pre-node state at each node).
Scope snapshots to the **debugged entity's** components, only during debug-active ticks, read-only first. Full
rationale + open questions for the architect are in `DEBUGGER-NODE-GRANULAR-STEPPING-IDEA.md`.

---

## 3. ⚠️ Clone / branch divergence — RESOLVE FIRST
Debugger work is split across two working copies:
- **`D:\Work\IOS-IG-SimHost-FDP-2`** (this one): CF-3/4/5 committed (`1e319680`, `01bfea3f`, …); the design notes.
- **`D:\Work\IOS-IG-SimHost-FDP`** (no `-2`): where Zoo did **CF-6** (`34748364`, the `_onNodeExecuted` overlay gate)
  and the latent-stepping testing — that commit is **NOT in `-2`**.

**First action:** confirm with the user which clone/branch is the source of truth for the debugger, and **reconcile
CF-6 (and any later FDP-clone debugger commits) into it** before building on top. Don't start the snapshot feature on
a base that's missing CF-6.

---

## 4. Key source (debugger)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — probe sink (`OnNodeEnter`),
  breakpoints, temp-BP stepping (Step Into/Over/Out via temp BPs), `HandleBreakpointHit`, `_isPaused`, Resume/Step.
- `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` — the triple-buffer pre/post-tick snapshot
  rewind (`_preTickSnapshot`/`_postTickSnapshot`, `ActiveView`, `OnExternalHit`). This is where node-granular snapshots
  would layer in.
- `IEngineDebugTimeController` / `MasterSyncTimeControllerAdapter` — `RequestPause/Resume/StepOneTick` (clock control).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/BlueprintRuntimeInspectorPane.cs` — paused-state UI
  (cursor + field table; reads field values).
- Probe instrumentation + node-ID mapping: the generated code calls the probe sink per node; mapping is keyed by node
  id via the DebugMap. Mind the historical **node-ID drift / probe mis-attribution** (`project_blueprint_breakpoint_id_drift`).
- Prior workstream reference: `.dev/blueprint-dbg-1/` (TASK-TRACKER, CF-TASK-DETAIL, DEBT-TRACKER, DEBUG-DD-ADDENDUM,
  ARCHITECT briefs, reports CF1-5). Note its batches were Zoo prompts; this thread uses sonnet sub-agents.

## 5. Verify / run
- Tests: `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (no regen flag) + the debug-session /
  breakpoint test areas. The one documented pre-existing red is `TickFrame_1000Frames_AllocatesZeroBytes` (not a
  regression). Debugger behavior is best verified by **driving ticks/steps in a test** and asserting recorded
  state/cursor/overlay — much of the *visual* editor behavior also needs a user smoke.

## 6. Architect
Consult NotebookLM (relayed by the user) for the node-granular design fork (snapshot ownership, per-entity snapshot
mechanism, the step-past-end→advance-tick handshake) — trusted-but-verify: focused questions, verify every code-level
claim against source, redirect when off-track. (It has been wrong on ABI/property-population details before.)

## 7. First moves
1. Read this + `DEBUGGER-NODE-GRANULAR-STEPPING-IDEA.md` + skim `.dev/blueprint-dbg-1/` for prior CF context.
2. **Resolve the clone/branch divergence (§3) with the user.**
3. Decide scope with the user (default: node-granular stepping, read-only, per §design). Consider an architect round
   on the snapshot-ownership design before coding.
4. Write a single-objective batch (prescribed assertions + do-not-stop-until-green), delegate via the **Agent tool
   (sonnet)**, review hard, run the suite, verify independently, commit. Repeat.
