# Architect question #7 — the EQS request/poll slice (`RequestAreaQuery` / `IsAreaQueryResolved`)

**Context.** Next migration target is the commander's EQS pair —
`Action_RequestAreaQuery` (`HillAttackCommanderNodes.cs:188-227`) and
`Condition_IsAreaQueryResolved` (`:237-278`). Q#6-D already ruled the batch area-query is a **distinct
surface** from `SpawnEqsSensor` and blessed a **new curated helper trio** over `AreaQueryBatchHelper`
(`RequestAreaQuery` / `GetAreaQueryResult` / `FreeAreaQuerySlot`). That settled the *surface*. Building the
two nodes surfaces **four node-level decisions Q#6-D did not touch** — and one is a blueprint pattern we've
**never shipped** (`Running`-return polling). Bundling them Q#5/Q#6-style. Leans given; we proceed on the
leans unless you redirect.

**The oracle in one breath.** `RequestAreaQuery`: if a request is already in-flight
(`CachedEqsRequestId != -1`), poll it → `Running` if not ready else `Success`; else validate
`TargetAreaEntity` alive, submit `AreaQueryBatchHelper.RequestAreaQuery(world, self, TargetAreaEntity,
ForceId.Hostile)`, cache `id`+`SimulationTime`, `Running` if the batch is full (`id==-1`) else `Success`.
`IsAreaQueryResolved`: poll `GetAreaQueryResult`; if not ready and `SimulationTime - EqsRequestTime > 5s`
→ free slot + clear cached ids + `Failure`; not-ready-within-timeout → `Running`; ready with
`TargetCount==0` → free + clear + `Failure` (clear area); ready with targets → cache `TargetGroupHandle`,
**leave `CachedEqsRequestId` set** (SC-HA011-5) → `Success`.
`AreaQueryResult` = `{ long RequestId; bool IsReady; int TargetCount; int TargetGroupHandle; int SourceNodeId }`.
Relevant WorkingState (already declared in `CalculateSegments`): `CachedEqsRequestId` (long, `-1`
sentinel), `CachedTargetGroupHandle` (int), `EqsRequestTime` (float).

---

## Q-A — `Running`-return polling: a plain `Return(Running)` node, or a latent primitive? *(the novel one)*

Both oracle nodes are **stateless polls**: they return `NodeStatus.Running` and are re-ticked from the top
next frame, re-reading `CachedEqsRequestId` from WorkingState — there is no per-node suspend/resume state.
Every latent blueprint we've shipped so far (`WaitForChannel`) uses the `__phase` suspend/resume machinery;
we have **never** authored a blueprint that simply returns `Running` and relies on top-of-tick re-entry.
The `ReturnNode` already carries a `NodeStatus Status` (so `Return(Running)` is expressible), and the
AiPrimitive `TickCore(ref p, ref ws, self, world, time)` is invoked afresh each frame by the BTree host.

- **Our lean:** express the poll as a plain **`Return(Running)`** node on the not-ready arms — no `__phase`,
  no latent primitive. The blueprint is re-ticked top-to-bottom each frame; poll state lives entirely in
  WorkingState (`CachedEqsRequestId`), exactly as the oracle does. `__phase` stays 0 (no latent node in the
  graph). Confirm the AiPrimitive host re-ticks a `Running`-returning blueprint from the top (and does **not**
  require `__phase` to resume), so a stateless `Return(Running)` is the sanctioned polling shape — vs. you
  wanting polling wrapped as a latent `WaitFor…` primitive instead.
- **Reuse vs build:** lean = reuse the existing `ReturnNode` with `Status=Running` (zero new machinery), plus
  a first proof that stateless `Running` re-entry works. The alternative (a latent EQS wait primitive) is a
  new node + `__phase` lowering for a poll that carries no local state.

## Q-B — `IsAreaQueryResolved`: curated status-verb, or visual control flow over scalar accessors?

The resolve node has real branching (not-ready / timed-out / clear-area / targets-found) **with side
effects** (free slot; cache/clear ids). Two ways to draw it:
1. **Scalar accessors + visual graph:** curated `AreaQueryBatchOps.{Request→long, IsReady→bool,
   TargetCount→int, TargetGroupHandle→int, Free}` (each downcasting `ISimulationView`→`EntityRepository`
   like `WorldOps.IsAlive` already does), and the node's decisions (`IsReady?`, `TargetCount==0?`, the 5s
   timeout) + WorkingState writes are authored **visually** (`Branch`/`Compare`/`SetVariable`/`Return`).
   Keeps the control flow on-graph (max visual authorability); only the batch-system access is curated.
2. **Curated status-verb:** one curated `AreaQueryBatchOps.Resolve(self, view, …) → <status enum>` that
   does the whole poll+timeout+free internally and returns e.g. `Pending/Cleared/TargetsFound`; the
   blueprint is a thin `Branch`-on-enum → `Return`. Smaller graph, but the interesting logic is off-graph.
- **Our lean:** **(1)** — scalar accessors + visual branching. It matches the project's "draw the behavior,
  keep only the unsafe/system-adjacent bit curated" thesis and the `AreAllAtBaseline`/`Dispatch` precedent,
  and it's the version worth proving with headless checks. Bundle into a curated verb (2) only if you judge
  the EQS poll+timeout+free too coupled to split. Which split do you want?
- **Reuse vs build:** (1) = ~4 tiny accessors + existing `Compare`/`Branch`/`SetVariable` (S). (2) = one
  bigger curated method + a new status enum the graph must switch on (S-M, less visual).

## Q-C — simulation-time access for the 5 s timeout

The timeout is `SimulationTime - EqsRequestTime > 5.0`. `SimulationTime` reaches `TickCore` as the trailing
`float time` arg, but **no visual node reads it** today.
- **Our lean:** add a curated **`WorldOps.SimTime(ISimulationView view) → float`** accessor (same downcast
  shape as `IsAlive`), then author the timeout as visual `BinaryOp(Subtract)` + `Compare(GreaterThan, 5f)`.
  Alternative A: a native **`GetTime` node** that reads the `TickCore` `time` param directly (cleaner, but a
  new node + a way to surface the param as a pin value). Alternative B: fold the whole 5s timeout into the
  curated resolve verb (couples to Q-B option 2). We lean curated accessor now; a native `GetTime` node only
  if you expect sim-time to be broadly needed across blueprints.
- **Reuse vs build:** lean = 1 accessor + existing `BinaryOp`/`Compare` (S). `GetTime` node = new node + IR
  surfacing the ABI `time` param (M) — worth it only if sim-time is a recurring need.

## Q-D — failure-path side effects (free slot + clear cached ids)

On the timeout and clear-area failure arms the oracle both **frees the slot** and **clears** three
WorkingState fields (`CachedEqsRequestId=-1`, `CachedTargetGroupHandle=-1`, `EqsRequestTime=0`); on the
targets-found success arm it caches `TargetGroupHandle` and (per SC-HA011-5) **leaves `CachedEqsRequestId`
set**.
- **Our lean:** curated **`AreaQueryBatchOps.Free(id, view)`** does the `FreeAreaQuerySlot` (the batch-system
  touch); the WorkingState clears are authored **visually** as `SetVariable(Literal -1/-1/0f)` on the two
  failure arms (mirrors `CalculateSegments`'s literal-init chain). Success arm = `SetVariable(TargetGroupHandle
  ← GetVariable/accessor)` only, `CachedEqsRequestId` deliberately untouched. Confirm the free-then-clear
  ordering can be split this way (curated free + visual clears) rather than bundled into one helper.
- **Reuse vs build:** lean = 1 curated `Free` + visual `SetVariable`s (reuses `CalculateSegments` pattern).
  Bundling the clears into `Free` is fewer nodes but hides the sentinel-reset semantics off-graph.

---

**Our lean defaults if you're happy with them:** A — plain `Return(Running)` stateless poll, no `__phase`
(confirm the host re-ticks from the top). B — scalar `AreaQueryBatchOps` accessors + **visual** branching
(control flow on-graph). C — curated `WorldOps.SimTime` accessor + visual `BinaryOp`/`Compare` timeout (no
new `GetTime` node yet). D — curated `Free` + **visual** `SetVariable` clears; success arm leaves
`CachedEqsRequestId` set per SC-HA011-5. We proceed on these unless you redirect.

---

## ARCHITECT ANSWERS (2026-07-17) — all four leans APPROVED

All four leans confirmed; continue the "orchestration-first" + "curated-generic" principles applied to
the new `AreaQueryBatchHelper` surface.

- **A — `Return(Running)` polling (APPROVED).** Plain, stateless `Return(Running)` is the sanctioned
  pattern. The BTree host naturally re-ticks `AiPrimitive` actions from the top every frame while they
  return `Running`; because the poll state (`CachedEqsRequestId`) lives entirely in `WorkingState`, there
  is **zero need** for a latent `Wait` primitive or the `__phase` state machine. This slice is the proof
  that stateless `Running` re-entry works for polling.
- **B — `IsAreaQueryResolved` scalar accessors + visual branching (APPROVED).** Go with Option (1). Keep
  the timeout logic, target-count check, and routing **on the visual graph** — burying them inside a
  single curated status-verb (Option 2) would defeat Blueprint orchestration by pushing behavioral logic
  back into C#.
- **C — `WorldOps.SimTime` accessor (APPROVED).** Curated `WorldOps.SimTime(ISimulationView) → float` +
  visual `BinaryOp`/`Compare` for the 5 s timeout. Correct demand-driven choice; do **not** speculatively
  build a native `GetTime` node / new IR before sim-time proves broadly needed.
- **D — curated `Free` + visual `SetVariable` clears (APPROVED).** `AreaQueryBatchOps.Free(id, view)` does
  the batch-system touch; the `WorkingState` resets (`CachedEqsRequestId=-1`, etc.) are **visual**
  `SetVariable`s. Success arm intentionally leaves `CachedEqsRequestId` set — correctly implements
  SC-HA011-5.

**Cleared to proceed on all four leans.**
