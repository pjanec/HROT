# Blueprint Debugger — Onboarding for a fresh DEV-LEAD session (blueprint-dbg-2)

You are the **dev lead** for the Hrot blueprint **debugger** (node-granular stepping) on branch `blueprint-integ-1`,
working copy **`D:\Work\IOS-IG-SimHost-FDP-2`). Read this fully before doing anything. The big feature is
DONE and merged; one P1 follow-up is specced + approved and just needs running.

---

## 0. Operating model — you orchestrate, you don't write feature code
You are dev lead per `.dev/.guides/DEV-LEAD-GUIDE_claude.md`. Loop: **plan a single-objective batch → delegate →
hard-review (believe nothing; read diffs + assertions; run the suite yourself) → curate + commit → repeat.**

**Delegation (token-saving — see memories `feedback_external_copilot_agent_delegation`, `reference_zoo_experimental_coding_agent`):**
- **Zoo worker via `claude-worker-orchestrator` MCP** (`start_worker` `mode:non-blocking` + poll `get_worker_status`; `wait` between polls). Zoo has NO auto-completion notification — you must poll.
  - `model:"flash"` = fastest/cheapest — trivial prescribed fixes (one-file edit + test + run).
  - `model:"pro"` = capable on well-scoped compiler/IR changes with a tight spec (proven on BPC-IMPLICIT-RETURN).
  - Zoo prompt MUST be ≤2KB → point it at the batch file; reference `.dev/.guides/DEV-GUIDE.md` (plain, NOT `_claude`); do NOT mention codebase-memory. **Hard-review every Zoo diff (it can hide problems / scope-creep / touch goldens) — trust diffs not its report.**
- **sonnet sub-agent via the Agent tool** (`subagent_type:general-purpose`, `model:sonnet`, `run_in_background:true`, reads `DEV-GUIDE_claude.md`) for complex/integration work. Auto-notifies on completion.
- Rule of thumb: simple prescribed → Zoo (flash/pro); integration / high-blast-radius / debugger-runtime → sonnet.
- **Prescribe exact test assertions in every batch** (behavioral: drive ticks/steps, assert recorded state / cursor / field values). Do-not-stop-until-`Failed:0`. You commit; agents don't.

---

## 1. What's DONE (committed on `blueprint-integ-1`)
Node-granular blueprint stepping is complete and user-validated (within-tick Step/StepBack, per-node inspector state,
breakpoints, step-past-end across ticks, implicit Return). Commits:
- `040f6f82` BATCH-00 engine version-clock split (`_globalVersion` memory clock vs `_simulationTick` frame clock; `BumpMemoryVersion`).
- `c839c122` BATCH-01 `SubTickSnapshotRecorder` (keyframe-per-node capture ring + restore).
- `7b1aae5b` BATCH-02 wire recorder into `BlueprintDebugSession` (record during a real debug tick).
- `5007c22f` BATCH-03 virtual-pointer Step/StepBack + inspector redirect to the per-node scratch repo.
- `53d9eb84` BATCH-04 editor UI surfacing (inspector shows pointer state; highlight follows pointer; Step Back button + "node X/N").
- `06ac8987` DD addendum (`docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md`).
- `2e1b4c25` BF-01 scratch repo registers ALL component types (`SyncFrom(includeTransient:true)`) — fixed a pause crash.
- `65a9b7c4` BF-02 Stage5 `FindVariableIndex` strips `var:` prefix.
- `05d1a10b` BATCH-05 + `134eb197` BF-03 + `0ad1157a` BF-04 — step-past-end tick-bridge (latent-safe; lands on the next iteration's first node).
- `8768d4e4` BPC-IMPLICIT-RETURN — `ReturnNode` optional (implicit return at end-of-chain).

**Key design fact (memory `project-node-granular-stepping-design`):** capture is **full keyframe per node** (not delta) because blueprint `SetVar` writes bypass `GetComponentRW` chunk-version stamping. The BATCH-00 version split is retained as the hook for a future delta optimization but is largely inert.

---

## 2. IMMEDIATE NEXT TASK — BPDBG-PERNODE-PROBES (P1, batch written + user-approved, deferred)
**Problem:** debug probes are per-BLOCK not per-NODE (`DebugProbeInsertion` = 1 probe/block; `ScheduleLatentNode`
overwrites the block's `SourceNodeId`). So a synchronous exec node fused with a following latent in the same block
(e.g. `SetVar → Delay`) has NO probe → not breakpointable/steppable/recorded. (Worked before only because `Sequence`
nodes split graphs into one-node blocks.)
**Fix (analyzed, mechanical):** per-exec-node probes + make `BreakpointTargets` one-to-one (each exec node → its own
probe id). Full spec, exact call sites, BF-03/04 compatibility analysis, and the regression set are in
**`.dev/blueprint-dbg-2/batches/BPDBG-PERNODE-PROBES-INSTRUCTIONS.md`** — run it on **sonnet** (high blast radius:
touches `DebugProbeInsertion`, `Stage5_Schedule`, `IrDebugAnnotation`, `DebugMapBuilder`, the `ProbeNodeId` doc).
Hard-review the BF-03/BF-04 regression especially. Tracked as DBG2-PNP in the debt tracker.

---

## 3. Backlog — `.dev/blueprint-dbg-2/DEBT-TRACKER.md` (single source of truth)
After BPDBG-PERNODE-PROBES, remaining are optional polish:
- **DBG2-D4 (P2):** compiler silently emits `s.__var_-1` for a truly-undeclared variable → emit a clean BP-error in Stage2/Stage5 instead.
- **DBG2-D5 / D6 (P3):** strengthen two debugger tests (cross-tick value-distinct; assert exact landing node).
- **DBG2-D1/D2/D3 (P3):** example-code GlobalVersion usage; pre-existing reds tracking; an unused test helper.

---

## 4. Verify / run (NO regen flags)
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (main debugger + compiler suite)
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
- Engine: `dotnet test FDP/Engine/Fdp.Core.Tests`, `FDP/Engine/Fdp.ModuleHost.Tests`; HSM: `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests`.
- **Documented pre-existing reds** in `Hrot.Blueprints.Tests` (NOT regressions — never mask/regen): `AiPrimitive_EmitMatchesGoldenSource`(×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. (A flaky timing benchmark sometimes makes the summary say 8 vs 7 — the 7 unique names above are the real set.) Confirm a "pre-existing" failure by stashing changes and running the clean baseline.

## 5. Gotchas (memories)
- **Stale source-generator cache** (`project-blueprint-generator-stale-cache`): after changing the compiler, the generator can serve STALE `*.g.cs` (a fix "not taking"). Force regen: `dotnet build <consumer>.csproj --no-incremental`, or in VS Clean+Rebuild / restart VS. Verified for the `var:` fix.
- **Never commit user `.bp.json` experiments** (e.g. `Count5.bp.json`) — they appear in the working tree; exclude them from every commit. `.dev/blueprint-dbg-1/reports/CF1-NODE-IDENTITY-REPORT.md` is an unrelated pre-existing edit — leave it out too.
- **Blueprints CAN write other entities/managed components mid-tick** (`project-blueprint-cross-entity-sync-mutation`) — why capture is whole-repo, not single-entity. (The NotebookLM architect was wrong on this; trusted-but-verify.)
- **Hard-review delegated work** (`reference_zoo_experimental_coding_agent`): read diffs + assertions, run the suite yourself, confirm golden changes are intended, exclude litter, verify "pre-existing failures" against a clean baseline.

## 6. Key source (the feature)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — probe sink (`OnNodeEnter`), breakpoints, virtual pointer (`_nodePointer`, Step/StepBack/`StepFromNodeOrNextIteration`), recorder wiring, inspector (`CaptureStateSnapshot`/`GetCurrentStateSnapshot`), `RestorePointerToScratch`, `BreakpointTargets`→`ProbeNodeId` (`:400-418`).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs` — keyframe-per-node ring + restore.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/DebugProbeInsertion.cs` — probe insertion (per-block today; per-node after BPDBG).
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` — block scheduling, `SourceNodeId`, `bpTargets` (`:220-229`), `SealFallThrough` (implicit return), `ScheduleLatentNode`.
- `FDP/Engine/Fdp.Core/EntityRepository.cs` (version split), `FlightRecorder/RecorderSystem.cs` + `PlaybackSystem.cs` (keyframe/restore).
- Editor UI: `Inspector/BlueprintRuntimeInspectorPane.cs`, `Debug/DebugStepControls.cs`, `Debug/BlueprintDebugToNodeEditAdapter.cs`.

## 7. Plan/tracker docs
`.dev/blueprint-dbg-2/`: `PLAN.md`, `TASK-TRACKER.md`, `DEBT-TRACKER.md`, `batches/`, `reports/`, `reviews/`.

## First moves
1. Read this + the DEBT-TRACKER + the BPDBG-PERNODE-PROBES batch file.
2. Launch BPDBG-PERNODE-PROBES on sonnet (it's specced + approved). Hard-review (esp. BF-03/04 regression + data-node exclusion + one-to-one `BreakpointTargets`), run the suite, exclude `.bp.json`, commit.
3. Then offer the optional polish (DBG2-D4 etc.). User smoke-tests visual editor behavior.
