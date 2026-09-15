# Blueprint Debugging — Design Addendum

**Scope:** breakpoints, stepping, instrumentation, breakpoint storage/lifecycle, and session persistence for the
live in-editor blueprint debugger. This addendum is the **authoritative design** for the CF corrective line
(CF-1…CF-9). It supersedes the "probe coverage" theory in the earlier STATUS rev 1 (which rested on a wrong
node-id table). Implementation tasks live in `TASK-DETAIL.md` and reference the sections here by number.

**Relationship to the Debug DD slices:** Slice-1 held breakpoints strictly in memory (cleared on editor close).
Sections 7 (persistence) and parts of 4 (on-demand instrumentation) **bring the Slice-2 persistence model forward**
deliberately, at the user's request. Everything else refines Slice-1 to actually work.

**Status of the line at time of writing:** CF-1…CF-5 shipped (breakpoints set + pause after an in-editor Compile;
data nodes correctly non-breakpointable; step/resume buttons present). CF-6 (stepping), CF-7-rev (on-demand
instrumentation), CF-8 (persistence) are designed below and not yet implemented.

---

## 1. Execution & pause model (the foundation)

A blueprint executes per simulation tick. Each tick, execution runs from the graph's current resume point until it
either reaches the end of the graph or hits a **latent** node (e.g. `Delay`, `WaitFor…`) which suspends until a
later tick. A purely synchronous segment therefore runs **in full within a single tick**; latent nodes split
execution across ticks.

Debug probes (`DebugProbe.NodeEnter(self, nodeId)`) fire **during** execution and are **soft**: a probe never
blocks the calling thread (per the existing soft-pause design). When a breakpoint's probe fires, a pause is
*requested*; the current tick still **runs to completion**, and the simulation clock halts at the **tick
boundary**. On pause the entity repository is presented in its **pre-tick** state (rewound), and on resume it is
restored to the post-tick state.

**Consequences that drive the rest of this design:**
- Pause granularity is the **tick boundary**, not a point mid-tick. You cannot stop "between two synchronous
  nodes" — both already ran.
- The inspected variable state is the **pre-tick snapshot**, identical regardless of *which* node in that tick
  triggered the pause. A breakpoint is therefore a **coverage / execution-cursor trigger** ("execution reached
  this region this tick"), not a mid-execution memory freeze.
- This is why breakpoints are **block-granular and exec-only** (§3) and why stepping is implemented as
  "advance the cursor to the next node" rather than a mid-tick halt (§6).

---

## 2. Node identity & breakpoint targeting

**Problem this solves:** the node id the editor uses (the **authored** id from `.bp.json`, shown on the canvas) is
**not** the id the running code probes with. The compiler normalizes and lowers the graph: latent and control-flow
nodes (e.g. `Delay`, `Sequence`) are remapped/synthesized to new ids, pure-data nodes are inlined, and the original
per-block probe was mis-attributed to a block's first IR statement (often an inlined data read). Setting a
breakpoint by authored id therefore never matched the runtime probe.

**Model (CF-2/CF-4):**
- The compiler preserves provenance through lowering: `IrDebugAnnotation.OriginNodeId` and `IrBlock.SourceNodeId`
  carry the **authored exec-node id** that owns each block.
- Each block emits exactly one `NodeEnter` probe, keyed to the block's **owning exec node** (never to an inlined
  data statement).
- The `DebugMap` carries **`BreakpointTargets`**: a map `authoredExecNodeId → blockProbeNodeId` (many-to-one —
  several exec nodes sharing a block map to that block's probe). **Only exec nodes** appear here; pure/data nodes
  are absent and thus not breakpointable.
- The editor sets/clears breakpoints by **authored node id**. The session translates authored → block-probe id via
  `BreakpointTargets` for runtime matching. The **red marker is drawn on the clicked (authored) node**, while the
  match index keys on the block-probe id.

**Invariant:** the editor and the runtime never need to share a 1:1 node-id space; `BreakpointTargets` is the
single translation layer, and it is the same map used to decide breakpoint eligibility (§3).

---

## 3. Breakpoint granularity & semantics

- **Exec-only.** Breakpoints may be placed only on nodes that participate in the execution (control) flow —
  entry, control-flow (Sequence/Branch), impure calls, SetVariable, latent nodes, etc. **Pure/data nodes**
  (GetVariable, Literal, Cast, pure FunctionCall) are **not** breakpoint targets: they are inlined into their
  consumer and have no distinct execution moment, and under the §1 whole-tick-rewind model they expose no
  separately-observable state. The canvas disables the "Toggle Breakpoint" action on such nodes (with a "data
  node — not a breakpoint target" hint); `IBlueprintDebugSession.IsNodeBreakpointable` returns true **iff** the
  node is in `BreakpointTargets`.
- **Block-granular.** A breakpoint on any exec node pauses when that node's **block** executes. Multiple exec
  nodes that share one straight-line block resolve to the same block probe — breaking on any of them is
  equivalent (and, per §1, exposes the same pre-tick state). Per-statement granularity is intentionally **not**
  provided; it would add no observable value and only churn the probe/step model.

---

## 4. Instrumentation model (on-demand, in-memory)

Blueprint debugging is **purely interactive (editor-only)**. There are two compile paths:
- **In-editor Quick Reload** — compiles a blueprint in `Debug` (or `Trace`) mode, emitting `DebugProbe.NodeEnter`
  calls and a `DebugMap`, and hot-loads it **in memory**. (Reads `asset.EditorMetadata.CompilerMode`.)
- **MSBuild source generator (Full Rebuild / precompiled artifacts)** — compiles in `Release`: **no probes, no
  debug map.** This is correct for production; it is left unchanged.

> **Decided (user + architect aligned, 2026-06-09).** The generator stays `Release`; debug instrumentation is
> **editor-on-demand, in-memory only** (CF-7-rev). Rationale: keeps production/Release artifacts clean and fast,
> and avoids the rejected alternative of "generator reads `asset.EditorMetadata.CompilerMode`" — `EditorMetadata`
> is committed to `.bp.json`, so that would bake one developer's `Debug` mode (and probe overhead) into shared
> source for the whole team. If build-baked probes were ever wanted, the only acceptable trigger is the build
> *configuration* (Debug ⇒ instrument), never per-asset metadata. The architect concurred (see
> `ARCHITECT-RESPONSE-01.md` and the follow-up review).

**Design:** the editor instruments **on demand, in memory**, never via the production build:
- When an asset transitions from "no breakpoints/watches" → "has at least one" (first `SetBreakpoint`/`AddWatch`,
  or a session restore), and its running build is not already instrumented for the needed mode, the editor sets
  `asset.EditorMetadata.CompilerMode` to the needed mode and triggers a Quick Reload (→ probes + `DebugMap`
  registered), then (re-)applies the breakpoints. Debounced to once per 0→active transition.
- **Mode selection:** node breakpoints **and conditional data breakpoints** need only **`Debug`**. A condition's
  `SearchPredicateDto` is **not** evaluated via pin-value probes — it is compiled by the predicate compiler and
  evaluated by `DataBreakpointSystem` directly against ECS component/variable state at the tick boundary
  (`EntityRepository.QueryDelta`). **Only pin-value Watches** (the Watch panel) need **`Trace`**, because Trace is
  what emits `PinValueChanged` (and boxes pin values into objects — a real per-tick cost). **Do NOT force Trace for
  a conditional breakpoint.** Rule: an asset needs `Trace` iff it has an active Watch; otherwise `Debug`.
- **Zero overhead until debugging:** before any breakpoint exists, assets run their normal (Release) build.
- **De-instrument policy:** when the last breakpoint/watch on an asset is removed, **leave it instrumented until
  the asset/editor closes** (chosen default; `Debug` overhead is small).

This is what makes breakpoints hittable **without a manual Compile**, including on a fresh editor with precompiled
(Release) artifacts: restoring a session (§7) triggers on-demand instrumentation for each affected asset.

---

## 5. Breakpoint storage & lifecycle

**Owner:** the **`DataBreakpointManager` is the load-independent, durable owner** of breakpoint records. It retains
each breakpoint's predicate **DTO**, `DisplayName`, `SourceElementId` (the node association), and
`Enabled`/`IsWatch`/`IsBroken` flags — independent of whether the asset is loaded or compiled. `BlueprintDebugSession`
is the **node-breakpoint + canvas + probe-match** layer that forwards node breakpoints to the manager
(`AddBreakpoint(ExternalHitTagPredicateDto{Tag = nodeId}, sourceElementId: nodeId)`) and renders markers; the
per-document `BlueprintDebugToNodeEditAdapter` is a filtered canvas view. Breakpoints are keyed by
`(assetId, graphId, authoredNodeId)`.

**Entity-agnostic.** One breakpoint per node fires for **every** entity running that blueprint (the probe matches
on node id, not entity). The first entity to reach it this tick pauses; same-tick dedup prevents double-pause for
other entities; `_pausedOnEntity` records the triggering entity so the state snapshot inspects that instance.
Per-entity scoping remains available via `SetEntityFilter`. There is **no** per-entity/per-instance breakpoint
storage (see §8).

**Pending / inert (load-order independence).** A breakpoint whose compiled predicate cannot be mounted — because
the asset isn't loaded yet, or its `DebugMap`/compile failed — is retained with its DTO and treated as **"never
fires"** (`IsBroken`/null delegate). It is **not** dropped. The manager's `OnHotReloadCompleted` drops stale
delegates and **re-mounts from the retained DTOs** on every asset (re)load/compile; `RegisterDebugMap` re-resolves
the session's node breakpoints (authored → block-probe id). A breakpoint set before its asset is loaded therefore
**auto-binds** when the asset arrives — mirroring IDE source-line breakpoints in unloaded modules.

**Stale on structural change (BPF-003).** When a `DebugMap` registers with a **different structure hash**, existing
breakpoints for that asset are marked **stale, not cleared**. UX: the breakpoint is **disabled and shown with a
yellow warning marker**; the user explicitly re-binds it to the new structure or discards it. If a node was
deleted, the stale breakpoint is retained for the user to clean up. Note: a **freshly regenerated** build never
emits a probe for a deleted node (the source is regenerated from the current graph), so there are no "phantom"
emissions from a current build. The session nonetheless **defensively ignores any hit/probe callback for a node id
it has no active breakpoint for** (never throws) — this covers (a) the normal case where every non-breakpointed
node calls the probe, and (b) the transient window where a **stale in-memory build** (running code from before a
re-instrument) still emits old node ids.

**Activation points (the two events that bind/rebind breakpoints):** `RegisterDebugMap(asset)` (rebuild the
session's authored→probe match index; mark stale on hash change) and `DataBreakpointManager.OnHotReloadCompleted`
(re-mount predicate delegates from retained DTOs).

---

## 6. Stepping

Stepping uses the conventional **temporary-breakpoint-on-the-next-node** model, adapted to the §1 execution model.

- On Step, compute the **immediate exec successor node(s)** of the currently paused node by following its
  exec-output wires in the open graph (multi-successor nodes like Sequence/Branch contribute all immediate
  successors; the step pauses at whichever runs first).
- Register **invisible one-shot** breakpoints on those successors (translated via `BreakpointTargets`, §2). These
  must **not** appear in the user breakpoint list or the gutter.
- **Suppress the origin (and other user) breakpoints for the step pass.** Because the graph re-executes from entry
  each tick (§1), a naive resume would re-pause at the origin node before reaching the successor. The step pass
  honors **only** the temporary targets, then restores user breakpoints once a target is hit.
- **Resume** (run until a temporary target fires) — not single-tick-step. On hit: pause and **auto-clear** all
  temporaries.
- Effect: the execution **cursor advances node-by-node** (visible via the runtime overlay); across a latent `Delay`
  the cursor advance also advances real time to the resume point.

**Slice-1 scope:** Step Over / Into / Out **converge** to "next exec node" because cross-peer-call stepping is out
of scope. One `Step()` backs all three buttons; the call-depth tracking (`_currentCallDepth`, peer-call enter/exit)
is retained so true Over/Out can be added when peer-call stepping is in scope.

---

## 7. Session persistence

The user's debug session — node breakpoints, **data breakpoints including their conditions**, and watches —
survives editor restarts.

- **File:** per-project, **per-user, gitignored** (e.g. `breakpoints.json` / `watches.json` alongside the
  `.bp.json` assets, added to `.gitignore`). Breakpoints are session-local and **must not** be committed or written
  into asset/`[...Layout]` files.
- **What is saved:** node breakpoints (`assetId`, `graphId`, **authored** `nodeId`, `enabled`); data breakpoints
  (the predicate **`SearchPredicateDto`** + `DisplayName` + `SourceElementId` + entity filter); watches
  (`assetId`, `graphId`, `pinId`, `displayName`, `expectedType`). **Conditions are saved as DTOs**, which are
  polymorphic and serializable; the **JIT-compiled delegate is never serialized** — it is recompiled via
  `PredicateCompiler` on load. (Generalize the existing `WatchPersistence` mechanism beyond watches.)
- **Serialization correctness (must verify, do not assume).** The predicate hierarchy is polymorphic via
  `[JsonPolymorphic(... "$type")]` + `[JsonDerivedType]` **attributes on `SearchPredicateDto`**, so System.Text.Json
  resolves derived types with **default** options — no special `JsonSerializerOptions`/registry is required (this
  is why `WatchPersistence`'s plain options already round-trip conditions; see `SearchPredicateDtoSerializationTests`).
  **Caveat:** every predicate type a blueprint condition can use **must** be in that `[JsonDerivedType]` list — at
  least one value DTO is intentionally *not* registered. CF-8 **must** include a round-trip test of a deeply-nested
  condition (`CompoundPredicateDto` → `BlueprintVariablePredicateDto`/`PropertyMatchDto`) and a guard that an
  unregistered/unresolved derived type **fails loudly** on save/load rather than silently dropping the condition.
- **Save triggers:** on change (debounced) and on editor/asset close, so a crash doesn't lose state.
- **Restore flow:** load the file into the `DataBreakpointManager` (recompile DTOs; mark `IsBroken` on failure);
  the session rebuilds its node-breakpoint records + canvas markers from the manager's breakpoints that carry a
  node `SourceElementId`; on-demand instrumentation (§4) and the activation points (§5) bind them as assets load.
  **Load-order independent** — breakpoints for not-yet-loaded assets remain pending and bind on load.
- **No silent loss:** a saved breakpoint whose node no longer exists after restore is kept **stale/disabled** with
  a hint, not dropped.

---

## 8. Multi-instance behavior

Two (or more) entities running the **same** blueprint share a **single** breakpoint per node (§5, entity-agnostic).
When execution reaches the node:
- the **first** entity to reach it this tick triggers the pause;
- same-tick dedup prevents a second pause for other entities in the same tick (their hit counts still accumulate);
- `_pausedOnEntity` identifies the entity whose instance state is shown in the inspector.

To debug a specific instance only, the user sets an **entity filter** (`SetEntityFilter`); otherwise the breakpoint
applies to all instances. There is no separate per-instance breakpoint object.

---

## 9. Out of scope / open items

- **Cross-peer-call stepping** (Step Into/Out across `CallPeerBlueprint`): deferred; Slice-1 stepping converges to
  next-node within one graph (§6).
- **Debugging precompiled artifacts outside the editor** (e.g. a standalone runner): not supported — instrumentation
  is in-editor/in-memory by design (§4). Production builds stay uninstrumented.
- **Conditional breakpoints / value editing at pause / true break-on-pin-write data breakpoints**: tracked as
  Slice-2 follow-ons; the persistence and manager model here already accommodate their DTOs.
