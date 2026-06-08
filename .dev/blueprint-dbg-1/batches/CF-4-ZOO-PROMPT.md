# Paste-ready prompt — Batch CF-4 (Zoo)

> Paste everything below the line into Zoo. Self-contained.

---

You are implementing **Batch CF-4** in repo `IOS-IG-SimHost-FDP-2` on branch `blueprint-integ-1`.

**First read your contract:** `.dev/.guides/DEV-GUIDE.md` (build/test gates, reporting, **never weaken or delete a
test to make it pass**, never regenerate snapshots). Then read the full spec: `.dev/blueprint-dbg-1/TASK-DETAIL.md`
→ section **"Batch CF-4 — Exec-only, block-granular breakpoints"**.

## Problem

Blueprint breakpoints were partially fixed in CF-2/CF-3: Sequence and Delay pause, but (1) breakpoints on exec nodes
that share a block (e.g. SetVariable `…0002`, Add `…0003` in `Count4`) are allowed by the editor gating yet emit
**no probe**, so they silently never fire; and (2) the pure data node GetVariable `…0004` still gets a probe and is
breakpointable. Both are wrong.

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

## SUCCESS CONDITION (all must hold)

- `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors (editor closed).
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **0 net-new failures**. Report the full
  failing set by name; reconcile the prior "8 vs 7 baseline" claim — confirm every failure is genuinely
  pre-existing (list them).
- For `Count4`: no `DebugProbe.NodeEnter` literal for GetVariable `…0004`; `BreakpointTargets` contains every exec
  node and no data node; breakpoints on SetVariable, Sequence, and Delay each yield `PauseRequestCount >= 1`;
  `IsNodeBreakpointable` false for GetVariable and any pure FunctionCall.

## Reporting (per DEV-GUIDE)

Write `.dev/blueprint-dbg-1/reports/CF4-REPORT.md`: what changed, exact build/test command lines + results, full
failing-test set by name (before/after). Do not weaken/delete tests, do not regenerate snapshots. If blocked, STOP
and report. The lead reviews the **diff** (not the report) and commits.
