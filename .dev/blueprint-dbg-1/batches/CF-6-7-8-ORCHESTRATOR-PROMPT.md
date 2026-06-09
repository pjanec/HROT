# Orchestrator prompt — Batches CF-6 + CF-7-rev + CF-8 (Zoo)

> For the orchestrator: dispatch **one task block at a time**, each followed by lead review + commit.
> **Order:** TASK 1 (CF-6) → TASK 2 (CF-7-rev) → TASK 3 (CF-8). CF-6 is independent; **CF-8 must come after
> CF-7-rev is committed** (restore relies on on-demand instrumentation). Hand Zoo exactly one block at a time.

## Shared contract (ALL tasks)

- Repo `IOS-IG-SimHost-FDP-2`, branch `blueprint-integ-1`. **Read `.dev/.guides/DEV-GUIDE.md` first** and follow it.
- **DESIGN OF RECORD: `.dev/blueprint-dbg-1/DEBUG-DD-ADDENDUM.md`** — read the cited sections before coding; it is
  authoritative. Full task detail: `.dev/blueprint-dbg-1/TASK-DETAIL.md` (sections CF-6 / CF-7-rev / CF-8). Where a
  task block and the addendum differ, the **addendum wins** — STOP and flag.
- **Never weaken, skip, or delete an existing test to make it pass.** If behavior legitimately changed, change only
  the expected value and list every such test by name (old→new) in the report. **Never** regenerate golden
  snapshots — if a golden changes, STOP and report.
- Gates: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors (editor CLOSED — DLL locks);
  `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -c Debug` → **0 net-new failures** (report the full
  failing set by name, before/after, with exact command lines). Lead reviews the **diff**, not the report, and
  commits. If blocked, STOP and report — do not guess.
- Already shipped (rely on it): CF-4 added `DebugMap.BreakpointTargets` (authored exec node → block-probe id) and
  `IBlueprintDebugSession.IsNodeBreakpointable`; breakpoints translate clicked node → block probe. CF-5 surfaced
  Continue/Step buttons in the Blueprint Tools panel.

---
---

# TASK 1 — CF-6: Real stepping via temporary breakpoint on the next node

**Read first:** DEBUG-DD-ADDENDUM.md **§1 (execution/pause model)** and **§6 (stepping)**; TASK-DETAIL "Batch CF-6".

## Problem
Step Over/Into/Out don't advance to the next executable node — the sim runs one tick and re-pauses at the same/top
node. Root cause: stepping does `RequestStepOneTick` and re-matches the first probed node next tick; the graph
re-executes from entry each tick, so that's the top of the loop, not the paused node's successor.

## Implementation (per §6)
1. **Compute the next exec node(s).** When paused at node X (`_session.PausedAt.NodeId`), follow X's exec-output
   wire(s) in the open blueprint graph model to its immediate exec successor node id(s). Include all immediate
   successors for multi-out nodes (Sequence/Branch).
2. **Session temporary-breakpoint API,** e.g. `SetTemporaryBreakpoints(IEnumerable<(Guid asset,Guid graph,Guid
   node)>)`: register **one-shot** breakpoints (translate via CF-4 `BreakpointTargets`); on first hit → pause and
   **auto-clear all temporaries**. Temporaries must NOT appear in `GetBreakpoints()` or the gutter markers.
3. **Suppress user breakpoints during the step pass.** Because the graph re-executes from entry each tick, honor
   **only** the temporary step targets and suppress user breakpoints (incl. X) until a temp is hit, then restore.
   (Document the choice. Without it, Step immediately re-pauses at X.)
4. **Resume, don't single-tick:** Step calls `RequestResume()` (run until a temp fires), NOT `RequestStepOneTick()`.
5. **Slice-1 scope:** Step Over/Into/Out **converge** to "next exec node" (no cross-peer-call stepping). Implement
   one `Step()`, wire all three buttons to it; keep `_currentCallDepth` hooks for future true Over/Out; document.
6. **Replace** the dead `_stepMode` tick-matching path in `BlueprintDebugSession.OnNodeEnter` with the temp-bp
   mechanism (or re-implement `_stepMode` to set/clear temporaries). Remove now-dead tick-step matching.

## Tests
- Headless: paused at a node with a known successor → `Step()` registers a temp on the successor's probe id,
  suppresses user bps, resumes; simulating the successor's `OnNodeEnter` pauses + clears the temp; user bps
  restored; `GetBreakpoints()` never included the temp.
- 3-node linear chain (Entry→A→B): breakpoint on A, Step → pauses at B (not A); Step again → past B.
- Stepping never re-pauses at the origin node.

## SUCCESS CONDITION (CF-6)
Build 0 errors; 0 net-new failures; in `Count4`, pausing on the Sequence then Step advances the executing-node
cursor to the next exec node (not the top); temporaries never show as user breakpoints. Report →
`.dev/blueprint-dbg-1/reports/CF6-REPORT.md`.

---
---

# TASK 2 — CF-7-rev: Auto in-memory instrumentation on demand

**Read first:** DEBUG-DD-ADDENDUM.md **§4 (instrumentation)** (and §1); TASK-DETAIL "Batch CF-7-rev".

## Goal
Breakpoints become hittable **without the user clicking Compile** — including on a fresh editor with precompiled
(Release) artifacts — by transparently doing an in-memory Debug/Trace Quick Reload of an asset the moment debugging
becomes active for it. **Do NOT change the source generator / production build** (it stays Release).

## Implementation (per §4)
1. **Trigger.** When an asset goes from "no breakpoints/watches" → "has ≥1" (first `SetBreakpoint`/`AddWatch`, or a
   CF-8 restore) and its running build isn't already instrumented for the needed mode: set
   `asset.EditorMetadata.CompilerMode` to the needed mode, invoke Quick Reload (reuses `QuickReloadService.cs:64`
   → emits probes + registers `DebugMap`), then (re-)apply the breakpoints/watches. **Debounce** to once per
   0→active transition.
2. **Mode selection per asset:** node breakpoints **and conditional data breakpoints** → `Debug` only (conditions
   are evaluated by `DataBreakpointSystem` against ECS state via `QueryDelta`, NOT via pin probes — do **not** force
   Trace for them). **Only pin-value Watches** → `Trace` (Trace emits `PinValueChanged` and boxes pin values — real
   cost). Rule: an asset needs `Trace` iff it has an active Watch; else `Debug`. (See addendum §4.)
3. **Zero overhead until debugging:** before any breakpoint exists, the asset keeps its existing build (no
   recompile, no probes).
4. **De-instrument policy:** when the last breakpoint/watch on an asset is removed, **leave it instrumented until
   asset/editor close** (chosen default). Document; do not auto-revert to Release.
5. **Confirm the running entity ticks the instrumented build** after auto-instrument (not a stale Release copy).

## Tests
- Headless: an asset running un-instrumented + `SetBreakpoint` on an exec node triggers a Quick Reload with
  `CompilerMode.Debug`, after which a tick fires the node's probe and pauses (existing fixture + `MockTimeController`).
- Mode selection: only node breakpoints → Debug; a pin watch → Trace.

## SUCCESS CONDITION (CF-7-rev)
Build 0 errors; 0 net-new failures; placing a breakpoint on an exec node of a not-yet-debugged asset causes the sim
to pause on it **without a manual Compile**; the source generator / Release path is unchanged. Report →
`.dev/blueprint-dbg-1/reports/CF7rev-REPORT.md`.

---
---

# TASK 3 — CF-8: Persist & restore the debug session  (dispatch AFTER CF-7-rev is committed)

**Read first:** DEBUG-DD-ADDENDUM.md **§5 (storage & lifecycle)**, **§7 (persistence)**, **§8 (multi-instance)**;
TASK-DETAIL "Batch CF-8" (esp. the "Storage model" invariant).

## Goal
Node breakpoints + data breakpoints (incl. **JIT-compiled conditions**) + watches survive editor restarts via a
**per-user, gitignored** file, and rebind automatically on open — no manual Compile.

## Implementation (per §5/§7 — reuse existing machinery, do NOT reinvent)
1. **`DataBreakpointManager` is the durable owner.** It already retains predicate **DTOs** + `DisplayName` +
   `SourceElementId` + flags and re-mounts delegates from the DTOs in `OnHotReloadCompleted` (DataBreakpointManager.
   cs:394), marking `IsBroken` on failure (= pending/inert "never fires", retained). **Reuse this** — do not build a
   parallel pending mechanism in the session. Verify `OnHotReloadCompleted` is invoked on every asset (re)load/
   compile in the editor reload cycle (wire it if missing), and that `RegisterDebugMap` re-resolves the session's
   node breakpoints (authored → block-probe via `BreakpointTargets`) and marks stale on structure-hash change
   (BPF-003: stale-but-retained, disabled + yellow marker — see §5).
2. **Persistence file.** Generalize `WatchPersistence` (it currently filters `IsWatch`) to persist **all**
   breakpoints: node breakpoints (assetId, graphId, **authored** nodeId, enabled), data breakpoints (the
   `SearchPredicateDto` Condition + DisplayName + SourceElementId + entity filter), and watches. Save the **DTO**
   (recompiled via `PredicateCompiler` on load) — **never** the compiled delegate. Per-project, **user-local,
   gitignored** path (add to `.gitignore`); excluded from asset/`[...Layout]` files.
   - **Serialization:** the predicate polymorphism is **attribute-based** (`[JsonPolymorphic]`/`[JsonDerivedType]`
     on `SearchPredicateDto`, `$type` discriminator) — default `JsonSerializerOptions` resolve it, **no special
     registry needed**. BUT every predicate type a condition can use must be in that `[JsonDerivedType]` list (one
     value DTO is intentionally not). **Add a round-trip test** of a deeply-nested condition (`CompoundPredicateDto`
     → `BlueprintVariablePredicateDto`/`PropertyMatchDto`) and make an **unresolved derived type fail loudly** on
     save/load, never silently drop the condition. (See addendum §7.)
3. **Save triggers:** on change (debounced) and on editor/asset close.
4. **Restore on open:** load into the `DataBreakpointManager` (recompile DTOs; `IsBroken` on fail); the session
   rebuilds its node-breakpoint records + canvas markers from manager breakpoints carrying a node `SourceElementId`;
   trigger CF-7-rev instrumentation for affected assets; binding completes via `RegisterDebugMap` /
   `OnHotReloadCompleted` as assets load. **Load-order independent** — breakpoints for not-yet-loaded assets stay
   pending and bind on load. A saved breakpoint whose node no longer exists → kept **stale/disabled**, not dropped.
5. **Entity-agnostic** storage stays as-is (one breakpoint per node, all instances; `SetEntityFilter` optional) —
   do not add per-instance storage (§8).

## Tests
- Round-trip: a session with a node breakpoint + a **conditional data breakpoint** (`CompoundPredicateDto` with a
  `BlueprintVariablePredicateDto`) + a watch → save → load → re-register reproduces them; assert the condition DTO
  round-trips and recompiles via `PredicateCompiler`.
- Restore where one saved node id is missing → that entry is stale, others restore, no throw.
- Integration with CF-7-rev: restoring a session for an un-instrumented asset triggers a Debug Quick Reload and the
  breakpoint then pauses on a tick.
- Pending/load-order: a breakpoint restored before its asset's `DebugMap` registers is inert, then binds and fires
  after `RegisterDebugMap`/`OnHotReloadCompleted`.

## SUCCESS CONDITION (CF-8)
Build 0 errors; 0 net-new failures; a debug session (incl. a JIT-conditional data breakpoint) saved → editor
restarted → restored → **without any manual Compile** the breakpoints are active and pause the sim; missing-node
entries degrade to stale, not lost; the session file is user-local + gitignored. Report →
`.dev/blueprint-dbg-1/reports/CF8-REPORT.md`.
