# Orchestrator prompt — Batches CF-4 + CF-5 (Zoo)

> For the orchestrator: dispatch **TASK 1 (CF-4) first**, let Zoo finish + lead-review + commit, **then** dispatch
> **TASK 2 (CF-5)**. They are independent (CF-5 depends only on Batch C), but CF-4 is the higher priority and both
> touch the editor debug session, so run them in order to keep diffs clean. Each task below is self-contained —
> hand Zoo exactly one task block at a time.

## Shared contract (applies to BOTH tasks)

- Repo `IOS-IG-SimHost-FDP-2`, branch `blueprint-integ-1`. **Read `.dev/.guides/DEV-GUIDE.md` first** and follow it
  (build/test gates, reporting).
- **Never weaken, skip, or delete an existing test to make the suite pass.** If behavior legitimately changed,
  change only the expected value and list every such test by name (old→new) in the report.
- **Never** regenerate golden snapshots (`BLUEPRINT_REGENERATE_SNAPSHOTS`). If a golden changes, STOP and report.
- Build gate: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors (the editor must be CLOSED — it locks DLLs).
- Report the **full failing-test set by name** before and after, with exact `dotnet test` command lines. The lead
  reviews the **diff** (not the report) and commits. If blocked, STOP and report rather than guess.
- Full spec for both tasks: `.dev/_DONE/blueprint-dbg-1/TASK-DETAIL.md` (sections "Batch CF-4" and "Batch CF-5").

---
---

# TASK 1 — CF-4: Exec-only, block-granular breakpoints

## Problem

Blueprint breakpoints were partially fixed in CF-2/CF-3: Sequence and Delay pause, but (1) breakpoints on exec
nodes that share a block (e.g. SetVariable `…0002`, Add `…0003` in `Count4`) are allowed by the editor gating yet
emit **no probe**, so they silently never fire; and (2) the pure data node GetVariable `…0004` still gets a probe
and is breakpointable. Both are wrong.

## Principle (do not deviate)

The engine pause is a **soft whole-tick pause**: the blueprint tick runs to completion, the entity repository is
rewound to the pre-tick state, and the clock pauses at the tick boundary. So a breakpoint is a **coverage trigger
at basic-block granularity** ("pause when execution reaches this exec region"). Therefore:
- **Do NOT add per-statement probes** — block granularity is correct and final (sub-tick state is never
  observable; per-statement probing only churns step/probe-count tests for zero value).
- **Only exec nodes are breakpoint targets.** Pure/data nodes (GetVariable, LiteralNode, CastNode, pure
  FunctionCall) are never breakpointable and emit **no** probe.
- **A breakpoint on ANY exec node must pause when its containing block runs** — even when several exec nodes share
  one block.

## Tasks

**A. Compiler (`Hrot.Blueprints.Compiler`):**
1. In Stage 5 (`Stage5_Schedule.cs`), record the set of authored **exec** node ids (visited by the exec traversal:
   `ScheduleBlock`/`EmitNodeStatements`/control-flow handlers) and, for each, the block its statements land in.
   Data nodes (reached only via `ResolveNodeOutput`) are NOT exec.
2. Ensure **every reachable block** sets `BlockBuilder.SourceNodeId` to an exec node (today only entry/latent/
   sequence do — extend the default `ScheduleBlock` path).
3. In `DebugProbeInsertion.cs`, **remove the tier-3 `Statements[0].Debug?.NodeId` fallback.** The probe id must be
   `SourceNodeId` (or `OriginNodeId`). If a reachable block has neither, fail loudly (debug assert / test failure) —
   never key a probe to a data read. Result: **no probe is emitted for any pure/data node.**
4. Add `IReadOnlyDictionary<Guid,Guid> BreakpointTargets` to `DebugMap` + `DebugMapIndex` (+ the serializer,
   `JsonIgnore`-when-empty for byte-stability): for **every exec node**, `authoredNodeId → blockProbeNodeId`
   (many-to-one — exec nodes sharing a block all map to that block's probe id). Data nodes are **absent**.

**B. Editor session (`Hrot.Blueprints.Editor/BlueprintDebugSession.cs`):**
1. `SetBreakpoint(assetId, graphId, nodeId)`: resolve `nodeId → blockProbeId` via `BreakpointTargets`; key the
   breakpoint for runtime matching by `blockProbeId` while **retaining the clicked `nodeId` for the marker**. Add
   `ProbeNodeId` to the `Breakpoint` record (clicked `NodeId` stays for display). Make `_bpByNodeString` tolerate
   multiple breakpoints per probe id (several exec nodes can map to one). If no DebugMap is registered yet, fall
   back to keying by the clicked id (tentative, as today).
2. `IsNodeBreakpointable(assetId, graphId, nodeId)` returns true **iff** the node is in `BreakpointTargets` (exec).
   Return false for data/unknown. **Fix its doc-comment** to match (it currently falsely claims GetVariable
   returns false — make that true).
3. `GetBreakpoints()` and the NodeEdit adapter `Breakpoints` set must expose the **clicked `NodeId`** so the red
   marker draws on the clicked node, not the block owner.

**C. Tests (tighten — do not loosen):**
1. Tighten `CF2_AllExecNodes_HaveExactlyOneProbe_NoDataNodeProbes`: assert `CountProbesFor(GetVariableGuid) == 0`;
   assert every `Count4` exec node resolves via `BreakpointTargets` to a probe id that **is** emitted. Remove the
   `<= 1` / "known limitation" escape hatches.
2. Add: `SetBreakpoint` on SetVariable `…0002` → one tick → `PauseRequestCount >= 1`; `IsNodeBreakpointable`
   (GetVariable `…0004`) == false; Sequence + Delay still pause; the marker set from `GetBreakpoints()` contains the
   clicked id.
3. In `ProbeIntegrationTests`, add an assertion that a breakpoint on the **Branch** node (Nodes[1]) also fires via
   block translation (closes the masked regression — do not re-hide it).

## SUCCESS CONDITION (CF-4 — all must hold)

- Build 0 errors; `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **0 net-new failures**.
  List the full failing set by name; reconcile the prior "8 vs 7 baseline" claim — confirm every failure is
  genuinely pre-existing.
- For `Count4`: no `DebugProbe.NodeEnter` literal for GetVariable `…0004`; `BreakpointTargets` contains every exec
  node and no data node; breakpoints on SetVariable, Sequence, and Delay each yield `PauseRequestCount >= 1`;
  `IsNodeBreakpointable` false for GetVariable and any pure FunctionCall.
- Report → `.dev/_DONE/blueprint-dbg-1/reports/CF4-REPORT.md`.

Known authored ids (`Count4`, verify against `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count4.bp.json`):
EventEntry `20000006-…0001`, SetVariable `…0002`, FunctionCall/Add `…0003`, GetVariable `…0004`,
Sequence `da9a9c0b-25f8-4a81-9a52-75c715456f18`, Delay `0b561966-b00b-4c84-a1a0-87042220ba9f`,
Return `7b6da53f-4e11-4bc9-9d0c-bad0e22c7f5c`. AssetId `47fe9c55-c6ca-4c69-9c5a-d46de25745de`.

---
---

# TASK 2 — CF-5: Step/Resume controls in the Blueprint Tools panel

## Context

The Continue / Step Over / Step Into / Step Out buttons **already exist and work** in
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/DebugPanelWindow.cs:34-63` (enabled when
`_session.IsPaused`, wired to `_session.Continue()/StepOver()/StepInto()/StepOut()`). The gap is purely
**placement**: after commit `d06fd144` ("merge four blueprint toolbar panels into single 'Blueprint Tools'
window"), the user wants these pause/step controls reachable from the **Blueprint Tools** panel, not a separate
Debug window. This task adds NO new debug functionality — it surfaces existing controls.

## Tasks

1. **Locate the merged "Blueprint Tools" window** (introduced by `d06fd144` — run `git show d06fd144 --stat` to
   find the class/file; it is NOT `GraphEditorWindow`). Confirm how it composes its sub-sections.
2. **Extract the step-control row** from `DebugPanelWindow.DrawUI` into a small shared helper, e.g.
   `DebugStepControls.Draw(IBlueprintDebugSession session)`, that renders the PAUSED banner + Continue / Step Over /
   Into / Out row (and "Not paused" disabled state when `!IsPaused`). **Reuse, do not duplicate** — call the helper
   from BOTH the new Blueprint Tools section and the existing `DebugPanelWindow` so there is one source of truth.
3. **Add a "Debug" section to the Blueprint Tools window** that calls the helper. Pass the
   `IBlueprintDebugSession` into that window if it doesn't already have it (mirror how `DebugPanelWindow` receives
   it; the session is created in `EditorSubsystem` ~`:887`).
4. **Do NOT delete the standalone Debug window** in this batch — keep it wired to the shared helper. Flag for the
   lead whether to retire it later.

## Tests

Mirror `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/DebugWindowDrawUITests.cs` — headless: when the
session reports `IsPaused`, the Blueprint Tools debug section's buttons invoke the matching
`IBlueprintDebugSession` method (use a capturing/mock session and the `LastStepActionInvoked`-style capture already
in `DebugPanelWindow`); buttons inert/disabled when not paused.

## SUCCESS CONDITION (CF-5)

- Build 0 errors; `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → 0 net-new failures.
- The Blueprint Tools panel shows Continue / Step Over / Into / Out when paused, wired to the session; the
  step-control logic is **shared** (not copy-pasted) between the panel section and `DebugPanelWindow`.
- Report → `.dev/_DONE/blueprint-dbg-1/reports/CF5-REPORT.md`.

**User smoke:** hit a breakpoint (e.g. on Delay) → in the Blueprint Tools panel press Continue → sim resumes; press
Step Over → sim advances one tick and re-pauses.
